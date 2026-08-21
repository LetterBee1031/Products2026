"""HR・TEPRの探索的分析結果をCSVとグラフへ出力する。"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

try:
    from Server.bio_data_analysis import (
        _extract_n_back,
        load_ml_training_dataframe,
        normalize_sent_at_to_jst,
        normalize_user_id,
    )
except ModuleNotFoundError:
    from bio_data_analysis import (
        _extract_n_back,
        load_ml_training_dataframe,
        normalize_sent_at_to_jst,
        normalize_user_id,
    )


BIOMETRIC_COLUMNS = ["heart_rate", "tepr"]
SOURCE_COLUMNS = {"heart_rate": "hr", "tepr": "tepr"}


def load_analysis_dataframe(
    user_id: str,
    data_dir: str | Path,
    block_warmup_seconds: float = 0.0,
) -> pd.DataFrame:
    """HRとTEPRを同期した分析用DataFrameを作成する。"""
    if not np.isfinite(block_warmup_seconds) or block_warmup_seconds < 0:
        raise ValueError("block_warmup_secondsは0以上の有限値を指定してください。")

    df = load_ml_training_dataframe(
        user_id,
        data_dir,
        feature_columns=[SOURCE_COLUMNS[column] for column in BIOMETRIC_COLUMNS],
    ).copy()
    if df.empty:
        raise ValueError("分析に使用できるHR・TEPRデータがありません。")

    if "block_id" not in df:
        df["block_id"] = df["ex_status"]
    else:
        df["block_id"] = df["block_id"].fillna(df["ex_status"])
    df["block_id"] = df["block_id"].astype(str).str.strip()

    df["n_back"] = df["ex_status"].map(_extract_n_back)
    df["n_back"] = df["n_back"].fillna(df["block_id"].map(_extract_n_back))
    df["heart_rate"] = pd.to_numeric(df["hr"], errors="coerce")
    df["tepr"] = pd.to_numeric(df["tepr"], errors="coerce")
    df = df.dropna(subset=["block_id", "n_back", *BIOMETRIC_COLUMNS]).copy()
    df["n_back"] = df["n_back"].astype(int)

    if block_warmup_seconds > 0:
        sample_times = df["sent_at"].map(normalize_sent_at_to_jst)
        if "_block_start_sent_at" in df:
            block_start_times = df["_block_start_sent_at"].map(
                normalize_sent_at_to_jst
            )
        else:
            block_start_times = sample_times.groupby(df["block_id"]).transform("min")
        elapsed_seconds = (sample_times - block_start_times).dt.total_seconds()
        df = df[elapsed_seconds >= block_warmup_seconds].copy()

    df = df.drop(columns=["_block_start_sent_at"], errors="ignore")
    if df.empty:
        raise ValueError("先頭時間の除外後に分析対象データが残りませんでした。")
    return df.reset_index(drop=True)


def summarize_biometrics(df: pd.DataFrame, group_columns: list[str]) -> pd.DataFrame:
    """指定単位でHR・TEPRの件数、平均値、中央値を計算する。"""
    summary = (
        df.groupby(group_columns, as_index=False, dropna=False)
        .agg(
            sample_count=("heart_rate", "size"),
            heart_rate_mean=("heart_rate", "mean"),
            heart_rate_median=("heart_rate", "median"),
            tepr_mean=("tepr", "mean"),
            tepr_median=("tepr", "median"),
        )
        .sort_values(group_columns)
        .reset_index(drop=True)
    )
    return summary


def _safe_correlation(first: pd.Series, second: pd.Series) -> float | None:
    first_values = pd.to_numeric(first, errors="coerce").to_numpy(dtype=float)
    second_values = pd.to_numeric(second, errors="coerce").to_numpy(dtype=float)
    valid = np.isfinite(first_values) & np.isfinite(second_values)
    first_values = first_values[valid]
    second_values = second_values[valid]
    if (
        len(first_values) < 2
        or np.isclose(np.std(first_values), 0.0)
        or np.isclose(np.std(second_values), 0.0)
    ):
        return None
    return float(np.corrcoef(first_values, second_values)[0, 1])


def calculate_n_back_correlations(df: pd.DataFrame) -> pd.DataFrame:
    """N-backレベルと各生体情報の全体・レベル別相関を計算する。"""
    rows = []
    n_back = df["n_back"].astype(float)
    ranked_n_back = n_back.rank(method="average")

    for feature in BIOMETRIC_COLUMNS:
        feature_values = df[feature].astype(float)
        rows.append(
            {
                "scope": "overall",
                "n_back_level": pd.NA,
                "method": "pearson_ordinal",
                "feature": feature,
                "correlation": _safe_correlation(n_back, feature_values),
                "sample_count": int(len(df)),
            }
        )
        rows.append(
            {
                "scope": "overall",
                "n_back_level": pd.NA,
                "method": "spearman_ordinal",
                "feature": feature,
                "correlation": _safe_correlation(
                    ranked_n_back, feature_values.rank(method="average")
                ),
                "sample_count": int(len(df)),
            }
        )

        for level in sorted(df["n_back"].unique().tolist()):
            level_indicator = (df["n_back"] == level).astype(float)
            rows.append(
                {
                    "scope": "level_vs_other",
                    "n_back_level": int(level),
                    "method": "point_biserial",
                    "feature": feature,
                    "correlation": _safe_correlation(level_indicator, feature_values),
                    "sample_count": int((df["n_back"] == level).sum()),
                }
            )

    return pd.DataFrame(rows)


def save_n_back_biometric_relationship_plot(
    df: pd.DataFrame,
    correlations: pd.DataFrame,
    output_path: str | Path,
    user_id: str,
    standardized: bool = False,
) -> Path:
    """N-backレベル別のHR・TEPR分布と全体相関を保存する。"""
    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    levels = sorted(df["n_back"].unique().tolist())

    fig, axes = plt.subplots(1, len(BIOMETRIC_COLUMNS), figsize=(13, 6))
    if len(BIOMETRIC_COLUMNS) == 1:
        axes = [axes]
    try:
        for axis, feature in zip(axes, BIOMETRIC_COLUMNS):
            distributions = [
                df.loc[df["n_back"] == level, feature].to_numpy(dtype=float)
                for level in levels
            ]
            axis.boxplot(distributions, tick_labels=[str(level) for level in levels])
            axis.set_xlabel("N-back level")
            if standardized:
                axis.set_ylabel(
                    "Heart rate (Z-score)"
                    if feature == "heart_rate"
                    else "TEPR (Z-score)"
                )
            else:
                axis.set_ylabel("Heart rate (bpm)" if feature == "heart_rate" else "TEPR")
            axis.grid(axis="y", alpha=0.25)

            feature_correlations = correlations[
                (correlations["scope"] == "overall")
                & (correlations["feature"] == feature)
            ].set_index("method")["correlation"]
            pearson = feature_correlations.get("pearson_ordinal")
            spearman = feature_correlations.get("spearman_ordinal")
            pearson_text = "N/A" if pd.isna(pearson) else f"{pearson:.3f}"
            spearman_text = "N/A" if pd.isna(spearman) else f"{spearman:.3f}"
            axis.set_title(
                f"{feature}\nPearson r={pearson_text}, Spearman ρ={spearman_text}"
            )

        scale_text = "Z-score" if standardized else "raw"
        fig.suptitle(
            f"N-back level and biometric distributions ({scale_text}) — user {user_id}"
        )
        fig.tight_layout(rect=(0, 0, 1, 0.94))
        fig.savefig(output_path, dpi=200, bbox_inches="tight")
    finally:
        plt.close(fig)
    return output_path


def save_hr_tepr_relationship_plot(
    df: pd.DataFrame,
    output_path: str | Path,
    user_id: str,
    standardized: bool = False,
) -> Path:
    """HRとTEPRの散布図、全体の回帰直線、Pearson相関を保存する。"""
    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    fig, axis = plt.subplots(figsize=(9, 6.5))
    try:
        color_map = plt.get_cmap("viridis")
        levels = sorted(df["n_back"].unique().tolist())
        for index, level in enumerate(levels):
            level_df = df[df["n_back"] == level]
            color = color_map(index / max(len(levels) - 1, 1))
            axis.scatter(
                level_df["heart_rate"],
                level_df["tepr"],
                s=28,
                alpha=0.55,
                color=color,
                label=f"{level}-back (n={len(level_df)})",
            )

        heart_rate = df["heart_rate"].to_numpy(dtype=float)
        tepr = df["tepr"].to_numpy(dtype=float)
        if len(df) >= 2 and not np.isclose(np.std(heart_rate), 0.0):
            slope, intercept = np.polyfit(heart_rate, tepr, 1)
            x_line = np.linspace(float(heart_rate.min()), float(heart_rate.max()), 200)
            axis.plot(
                x_line,
                slope * x_line + intercept,
                color="tab:red",
                linewidth=2.2,
                label="Overall linear trend",
            )

        correlation = None
        if (
            len(df) >= 2
            and not np.isclose(np.std(heart_rate), 0.0)
            and not np.isclose(np.std(tepr), 0.0)
        ):
            correlation = float(np.corrcoef(heart_rate, tepr)[0, 1])

        correlation_text = "N/A" if correlation is None else f"{correlation:.3f}"
        scale_text = "Z-score" if standardized else "raw"
        axis.set_title(
            f"HR and TEPR relationship ({scale_text}) — user {user_id}"
            f"\nPearson r = {correlation_text}"
        )
        axis.set_xlabel("Heart rate (Z-score)" if standardized else "Heart rate (bpm)")
        axis.set_ylabel("TEPR (Z-score)" if standardized else "TEPR")
        axis.grid(alpha=0.25)
        axis.legend(loc="best")
        fig.tight_layout()
        fig.savefig(output_path, dpi=200, bbox_inches="tight")
    finally:
        plt.close(fig)
    return output_path


def standardize_biometrics(df: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    """分析対象全体を基準にHR・TEPRをZスコア変換する。"""
    standardized_df = df.copy()
    parameter_rows = []
    for feature in BIOMETRIC_COLUMNS:
        mean = float(df[feature].mean())
        std = float(df[feature].std(ddof=0))
        if np.isclose(std, 0.0):
            standardized_df[feature] = 0.0
        else:
            standardized_df[feature] = (df[feature] - mean) / std
        parameter_rows.append(
            {
                "feature": feature,
                "mean": mean,
                "std_ddof0": std,
                "sample_count": int(df[feature].notna().sum()),
            }
        )
    return standardized_df, pd.DataFrame(parameter_rows)


def run_analysis(
    user_id: str,
    data_dir: str | Path,
    output_dir: str | Path,
    block_warmup_seconds: float = 0.0,
) -> dict:
    """全分析を実行し、生成物の情報を返す。"""
    safe_user_id = normalize_user_id(user_id)
    output_dir = Path(output_dir) / safe_user_id
    output_dir.mkdir(parents=True, exist_ok=True)

    analysis_df = load_analysis_dataframe(
        safe_user_id,
        data_dir,
        block_warmup_seconds=block_warmup_seconds,
    )
    block_summary = summarize_biometrics(analysis_df, ["block_id", "n_back"])
    n_back_summary = summarize_biometrics(analysis_df, ["n_back"])
    n_back_correlations = calculate_n_back_correlations(analysis_df)
    standardized_df, zscore_parameters = standardize_biometrics(analysis_df)
    standardized_correlations = calculate_n_back_correlations(standardized_df)

    block_csv = output_dir / "block_biometric_summary.csv"
    n_back_csv = output_dir / "n_back_biometric_summary.csv"
    correlation_csv = output_dir / "n_back_biometric_correlations.csv"
    zscore_parameters_csv = output_dir / "biometric_zscore_parameters.csv"
    relationship_plot = output_dir / "hr_tepr_relationship.png"
    n_back_plot = output_dir / "n_back_biometric_relationship.png"
    zscore_relationship_plot = output_dir / "hr_tepr_relationship_zscore.png"
    zscore_n_back_plot = output_dir / "n_back_biometric_relationship_zscore.png"
    block_summary.to_csv(block_csv, index=False, encoding="utf-8-sig")
    n_back_summary.to_csv(n_back_csv, index=False, encoding="utf-8-sig")
    n_back_correlations.to_csv(correlation_csv, index=False, encoding="utf-8-sig")
    zscore_parameters.to_csv(zscore_parameters_csv, index=False, encoding="utf-8-sig")
    save_hr_tepr_relationship_plot(analysis_df, relationship_plot, safe_user_id)
    save_n_back_biometric_relationship_plot(
        analysis_df, n_back_correlations, n_back_plot, safe_user_id
    )
    save_hr_tepr_relationship_plot(
        standardized_df,
        zscore_relationship_plot,
        safe_user_id,
        standardized=True,
    )
    save_n_back_biometric_relationship_plot(
        standardized_df,
        standardized_correlations,
        zscore_n_back_plot,
        safe_user_id,
        standardized=True,
    )

    return {
        "user_id": safe_user_id,
        "sample_count": int(len(analysis_df)),
        "block_count": int(analysis_df["block_id"].nunique()),
        "n_back_levels": sorted(analysis_df["n_back"].unique().astype(int).tolist()),
        "block_warmup_seconds": float(block_warmup_seconds),
        "outputs": {
            "block_summary": str(block_csv.resolve()),
            "n_back_summary": str(n_back_csv.resolve()),
            "n_back_correlations": str(correlation_csv.resolve()),
            "zscore_parameters": str(zscore_parameters_csv.resolve()),
            "hr_tepr_plot": str(relationship_plot.resolve()),
            "n_back_biometric_plot": str(n_back_plot.resolve()),
            "hr_tepr_zscore_plot": str(zscore_relationship_plot.resolve()),
            "n_back_biometric_zscore_plot": str(zscore_n_back_plot.resolve()),
        },
    }


def parse_args() -> argparse.Namespace:
    server_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="HR・TEPRの探索的データ分析")
    parser.add_argument("--user-id", default="01", help="分析対象のユーザID")
    parser.add_argument(
        "--data-dir",
        type=Path,
        default=server_dir / "data",
        help="hr_ibi/eye_dataが保存されたディレクトリ",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=server_dir / "analysis_outputs",
        help="分析結果の出力先",
    )
    parser.add_argument(
        "--block-warmup-seconds",
        type=float,
        default=0.0,
        help="各ブロック先頭から除外する秒数",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    result = run_analysis(
        user_id=args.user_id,
        data_dir=args.data_dir,
        output_dir=args.output_dir,
        block_warmup_seconds=args.block_warmup_seconds,
    )
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
