from __future__ import annotations

import argparse
import csv
from datetime import datetime
from pathlib import Path
from statistics import fmean, pstdev
import sys

import matplotlib

# 画面を持たないサーバ環境でもグラフを画像として保存できるようにする。
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import MultipleLocator


# このファイルを直接実行した場合でも、Server/ と negotiation/ をimportできるよう
# プロジェクトルートをPythonのモジュール検索パスへ追加する。
PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.append(str(PROJECT_ROOT))

from Server import shared_state
from negotiation import run_negotiation


DEFAULT_PROFILE_CSV = PROJECT_ROOT / "Server" / "data" / "user_profile.csv"
DEFAULT_OUTPUT_CSV = (
    PROJECT_ROOT / "Server" / "data" / "negmas_negotiation_test_results.csv"
)
DEFAULT_AVERAGE_CSV = (
    PROJECT_ROOT / "Server" / "data" / "negmas_utility_averages_100.csv"
)
DEFAULT_AVERAGE_PLOT = (
    PROJECT_ROOT / "Server" / "data" / "negmas_utility_averages_100.png"
)
DEFAULT_AGREEMENT_PLOT = (
    PROJECT_ROOT / "Server" / "data" / "negmas_agreement_issue_means_100.png"
)
PLOT_TITLE_FONT_SIZE = 22
PLOT_LABEL_FONT_SIZE = 20
PLOT_TICK_FONT_SIZE = 18
PLOT_LEGEND_FONT_SIZE = 15
PLOT_BAR_LABEL_FONT_SIZE = 13


def load_user_ids(profile_csv: Path) -> list[str]:
    """user_profile.csvの行順でテスト対象ユーザIDを取得する。"""
    # utf-8-sigを使い、Excelなどが付加したBOMを含むCSVにも対応する。
    with profile_csv.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if reader.fieldnames is None or "user_id" not in reader.fieldnames:
            raise ValueError("user_profile.csv must contain a 'user_id' column")

        # 空のuser_idはテスト対象から除外する。
        return [
            (row.get("user_id") or "").strip()
            for row in reader
            if (row.get("user_id") or "").strip()
        ]


def run_all_users(
    *,
    profile_csv: Path,
    output_csv: Path,
    current_load: float,
    max_steps: int,
    comfort_weight: float,
    random_seed: int | None,
    use_critiques: bool,
) -> list[dict]:
    """全ユーザと交渉し、shared_stateを変更せず結果行を返す。"""
    # 内部でissue_option.csvを先に読み込み、その後user_profile.csvを反映する。
    shared_state.load_user_profiles(profile_csv)
    user_ids = load_user_ids(profile_csv)
    if not user_ids:
        raise ValueError(f"no users found in {profile_csv}")

    # CSVへ各論点の初期値・結果値・選好値などを列として出力するため、
    # shared_stateが管理している論点順をここで固定する。
    issue_names = list(shared_state.ISSUE_OPTIONS)
    rows: list[dict] = []

    for user_id in user_ids:
        # 交渉前の設定を退避し、テスト終了後に書き換わっていないことを検証する。
        user = shared_state.user_status[user_id]
        settings_before = shared_state.get_user_issue_settings(user_id)

        # persist_agreement=Falseにより、合意してもshared_stateへ反映しない。
        # 本番状態を壊さず、交渉結果だけを評価するためのテスト実行である。
        result = run_negotiation(
            user_id=user_id,
            current_load=current_load,
            max_steps=max_steps,
            comfort_weight=comfort_weight,
            random_seed=random_seed,
            persist_agreement=False,
            use_critiques=use_critiques,
        )

        settings_after = shared_state.get_user_issue_settings(user_id)
        if settings_after != settings_before:
            raise RuntimeError(
                f"shared_state was unexpectedly changed for user {user_id}"
            )

        # 合意なしの場合は適用予定案がないため、開始時設定を結果列へ記録する。
        result_settings = (
            result.agreement if result.agreement is not None else settings_before
        )

        # 最終ステップが存在しない場合は、効用・閾値を空欄として出力する。
        final_step = result.steps[-1] if result.steps else None

        # 各ステップでPAが返したCritiqueの総数を集計する。
        critique_count = sum(len(step.critiques) for step in result.steps)

        # 交渉全体の結果と、最後の提案における効用・閾値を1行にまとめる。
        row = {
            "executed_at": datetime.now().isoformat(timespec="seconds"),
            "engine": result.engine,
            "user_id": user_id,
            "accepted": result.accepted,
            "reason": result.reason,
            "use_critiques": use_critiques,
            "step_count": len(result.steps),
            "critique_count": critique_count,
            "state_unchanged": settings_after == settings_before,
            "L_before": result.initial_load,
            "L_after": result.predicted_load,
            "delta_L": result.predicted_load - result.initial_load,
            "final_U_AA": final_step.aa_utility if final_step else "",
            "final_U_PA": final_step.pa_utility if final_step else "",
            "final_tau_AA": final_step.aa_threshold if final_step else "",
            "final_tau_PA": final_step.pa_threshold if final_step else "",
        }

        # 論点ごとの値を動的な列名へ展開する。
        row.update({f"initial_{issue}": settings_before[issue] for issue in issue_names})
        row.update({f"result_{issue}": result_settings[issue] for issue in issue_names})
        row.update({f"p_{issue}": user.p[issue] for issue in issue_names})
        row.update({f"w_{issue}": user.w[issue] for issue in issue_names})
        row.update(
            {f"coeff_{issue}": shared_state.DEFAULT_COEFFS[issue] for issue in issue_names}
        )
        row.update(
            {f"rho_{issue}": shared_state.DEFAULT_RHO[issue] for issue in issue_names}
        )
        rows.append(row)

    # 全ユーザ分を1つのCSVへ上書き保存する。
    output_csv.parent.mkdir(parents=True, exist_ok=True)
    with output_csv.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    return rows


def run_repeated_negotiations_and_plot(
    *,
    profile_csv: Path,
    output_csv: Path,
    output_plot: Path,
    output_agreement_plot: Path,
    trials_per_user: int = 100,
    current_load: float = 0.75,
    max_steps: int = 30,
    comfort_weight: float = 0.0,
    base_random_seed: int = 7,
    use_critiques: bool = True,
) -> list[dict]:
    """各ユーザと複数回交渉し、平均効用をCSVと棒グラフへ出力する。"""
    if trials_per_user < 1:
        raise ValueError("trials_per_user must be at least 1")

    shared_state.load_user_profiles(profile_csv)
    user_ids = load_user_ids(profile_csv)
    if not user_ids:
        raise ValueError(f"no users found in {profile_csv}")

    issue_names = list(shared_state.ISSUE_OPTIONS)
    summary_rows: list[dict] = []
    for user_index, user_id in enumerate(user_ids):
        # 反復試行中に共有設定が変化していないか確認するため、開始値を保持する。
        settings_before = shared_state.get_user_issue_settings(user_id)
        aa_utilities: list[float] = []
        pa_utilities: list[float] = []
        predicted_loads: list[float] = []
        agreement_issue_values: dict[str, list[float]] = {
            issue: [] for issue in issue_names
        }
        accepted_count = 0
        step_counts: list[int] = []

        for trial_index in range(trials_per_user):
            # ユーザと試行番号から一意なseedを作る。
            # 同じ引数なら結果を再現でき、試行ごとには異なる候補系列になる。
            random_seed = (
                base_random_seed
                + user_index * trials_per_user
                + trial_index
            )
            result = run_negotiation(
                user_id=user_id,
                current_load=current_load,
                max_steps=max_steps,
                comfort_weight=comfort_weight,
                random_seed=random_seed,
                persist_agreement=False,
                use_critiques=use_critiques,
            )
            predicted_loads.append(result.predicted_load)
            if result.agreement is not None:
                for issue in issue_names:
                    agreement_issue_values[issue].append(float(result.agreement[issue]))

            # 長時間の反復テストでも進行状況を確認できるよう、
            # 交渉が1回終わるたびにユーザIDと完了回数を表示する。
            print(
                f"user_id={user_id}: "
                f"negotiation {trial_index + 1}/{trials_per_user} completed",
                flush=True,
            )

            # 提案履歴がある場合だけ、最終ステップのAA・PA効用を集計対象にする。
            if result.steps:
                final_step = result.steps[-1]
                aa_utilities.append(final_step.aa_utility)
                pa_utilities.append(final_step.pa_utility)
            accepted_count += int(result.accepted)
            step_counts.append(len(result.steps))

            if shared_state.get_user_issue_settings(user_id) != settings_before:
                raise RuntimeError(
                    f"shared_state was unexpectedly changed for user {user_id}"
                )

        # すべての試行で提案が生成されなかった場合、平均効用を計算できない。
        if not aa_utilities or not pa_utilities:
            raise RuntimeError(f"no utility values were produced for user {user_id}")

        # ユーザごとに受諾率、効用の平均・標準偏差、平均ステップ数をまとめる。
        summary_row = {
            "executed_at": datetime.now().isoformat(timespec="seconds"),
            "engine": "negmas",
            "user_id": user_id,
            "trials": trials_per_user,
            "use_critiques": use_critiques,
            "accepted_count": accepted_count,
            "acceptance_rate": accepted_count / trials_per_user,
            "mean_U_AA": fmean(aa_utilities),
            "std_U_AA": pstdev(aa_utilities),
            "mean_U_PA": fmean(pa_utilities),
            "std_U_PA": pstdev(pa_utilities),
            "mean_predicted_load": fmean(predicted_loads),
            "std_predicted_load": pstdev(predicted_loads),
            "mean_steps": fmean(step_counts),
            "state_unchanged": True,
            "L_current": current_load,
        }
        # 合意案が存在した試行だけを対象に、各論点の平均値を保存する。
        summary_row.update(
            {
                f"mean_agreement_{issue}": (
                    fmean(values) if values else ""
                )
                for issue, values in agreement_issue_values.items()
            }
        )
        summary_rows.append(summary_row)

    # ユーザ別の集計結果をCSVへ保存する。
    output_csv.parent.mkdir(parents=True, exist_ok=True)
    with output_csv.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(summary_rows[0]))
        writer.writeheader()
        writer.writerows(summary_rows)

    # ユーザごとにAAとPAの平均効用を横並びで比較する。
    labels = [row["user_id"] for row in summary_rows]
    aa_means = [row["mean_U_AA"] for row in summary_rows]
    pa_means = [row["mean_U_PA"] for row in summary_rows]
    x_positions = list(range(len(labels)))
    bar_width = 0.38

    # AAを左、PAを右にずらしたグループ化棒グラフを作る。
    figure, axis = plt.subplots(figsize=(10, 6))
    aa_bars = axis.bar(
        [x - bar_width / 2 for x in x_positions],
        aa_means,
        bar_width,
        label="AA utility",
    )
    pa_bars = axis.bar(
        [x + bar_width / 2 for x in x_positions],
        pa_means,
        bar_width,
        label="PA utility",
    )
    axis.set_title(
        f"Mean Utility by User ({trials_per_user} negotiations)",
        fontsize=PLOT_TITLE_FONT_SIZE,
    )
    # axis.set_xlabel("PA ID", fontsize=PLOT_LABEL_FONT_SIZE)
    axis.set_ylabel("Mean utility", fontsize=PLOT_LABEL_FONT_SIZE)
    axis.set_xticks(x_positions, labels)
    axis.tick_params(axis="both", labelsize=PLOT_TICK_FONT_SIZE)
    axis.set_ylim(0.0, 1.05)
    axis.grid(axis="y", alpha=0.25)
    # axis.legend(fontsize=PLOT_LEGEND_FONT_SIZE)
    axis.legend(
            # title="PA ID",
            loc="upper center",
            bbox_to_anchor=(0.5, -0.1),
            ncol=2,
            fontsize=PLOT_LEGEND_FONT_SIZE,
            title_fontsize=PLOT_LEGEND_FONT_SIZE,
        )
    axis.bar_label(
        aa_bars,
        fmt="%.3f",
        padding=3,
        fontsize=PLOT_BAR_LABEL_FONT_SIZE,
    )
    axis.bar_label(
        pa_bars,
        fmt="%.3f",
        padding=3,
        fontsize=PLOT_BAR_LABEL_FONT_SIZE,
    )
    figure.tight_layout()

    # GUI表示は行わず、PNGファイルへ直接保存してFigureを解放する。
    output_plot.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(output_plot, dpi=200)
    plt.close(figure)

    # 合意案に含まれる各論点の平均値を、ユーザごとに比較する。
    agreement_figure, agreement_axis = plt.subplots(figsize=(12, 9))
    issue_count = len(issue_names)
    user_count = len(summary_rows)
    issue_positions = list(range(issue_count))
    agreement_bar_width = 0.8 / max(user_count, 1)

    for user_index, row in enumerate(summary_rows):
        offset = (user_index - (user_count - 1) / 2) * agreement_bar_width
        means = [
            row[f"mean_agreement_{issue}"]
            if row[f"mean_agreement_{issue}"] != ""
            else 0.0
            for issue in issue_names
        ]
        agreement_axis.bar(
            [position + offset for position in issue_positions],
            means,
            agreement_bar_width,
            label=str(row["user_id"]),
        )

    agreement_axis.set_title(
        f"Mean Agreed Issue Values by User ({trials_per_user} negotiations)",
        fontsize=PLOT_TITLE_FONT_SIZE,
    )
    # agreement_axis.set_xlabel("Issue", fontsize=PLOT_LABEL_FONT_SIZE)
    agreement_axis.set_ylabel("Mean agreed value", fontsize=PLOT_LABEL_FONT_SIZE)
    agreement_axis.set_xticks(issue_positions, issue_names, rotation=30, ha="right")
    #agreement_axis.set_xticklabels("進行スピード", "難易度", "演出・刺激強度", "文章量", "補助量", "休憩頻度")
    agreement_axis.set_ylim(0.0, 1.05)
    agreement_axis.yaxis.set_major_locator(MultipleLocator(0.1))
    agreement_axis.tick_params(axis="both", labelsize=PLOT_TICK_FONT_SIZE)
    agreement_axis.grid(axis="y", alpha=0.25)
    agreement_axis.legend(
        # title="PA ID",
        loc="upper center",
        bbox_to_anchor=(0.5, -0.22),
        ncol=max(1, min(user_count, 6)),
        fontsize=PLOT_LEGEND_FONT_SIZE,
        title_fontsize=PLOT_LEGEND_FONT_SIZE,
    )
    agreement_figure.tight_layout(rect=(0.0, 0.16, 1.0, 1.0))

    output_agreement_plot.parent.mkdir(parents=True, exist_ok=True)
    agreement_figure.savefig(output_agreement_plot, dpi=200, bbox_inches="tight")
    plt.close(agreement_figure)
    return summary_rows


def parse_args() -> argparse.Namespace:
    # コマンドラインから入力CSV、出力先、交渉条件を変更できるようにする。
    parser = argparse.ArgumentParser(
        description=(
            "Run the NegMAS negotiation for every user without updating "
            "shared_state, then write one result row per user."
        )
    )
    parser.add_argument("--profile-csv", type=Path, default=DEFAULT_PROFILE_CSV)
    parser.add_argument("--output-csv", type=Path, default=DEFAULT_OUTPUT_CSV)
    parser.add_argument("--l-current", type=float, default=0.75)
    parser.add_argument("--max-steps", type=int, default=100)
    parser.add_argument("--comfort-weight", type=float, default=0.0)
    parser.add_argument("--random-seed", type=int, default=7)
    parser.add_argument(
        "--disable-critiques",
        action="store_true",
        help="Run negotiations without generating or applying Critique feedback.",
    )
    parser.add_argument(
        "--trials-per-user",
        type=int,
        default=1,
        help="Run repeated negotiations and plot mean utilities when greater than 1.",
    )
    parser.add_argument("--average-output-csv", type=Path, default=DEFAULT_AVERAGE_CSV)
    parser.add_argument("--average-output-plot", type=Path, default=DEFAULT_AVERAGE_PLOT)
    parser.add_argument(
        "--agreement-output-plot",
        type=Path,
        default=DEFAULT_AGREEMENT_PLOT,
    )
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()

    # 2回以上なら統計集計とグラフ出力、1回ならユーザごとの詳細CSVを作成する。
    if args.trials_per_user > 1:
        summary = run_repeated_negotiations_and_plot(
            profile_csv=args.profile_csv,
            output_csv=args.average_output_csv,
            output_plot=args.average_output_plot,
            output_agreement_plot=args.agreement_output_plot,
            trials_per_user=args.trials_per_user,
            current_load=args.l_current,
            max_steps=args.max_steps,
            comfort_weight=args.comfort_weight,
            base_random_seed=args.random_seed,
            use_critiques=not args.disable_critiques,
        )
        print(
            f"wrote {len(summary)} user averages to {args.average_output_csv} "
            f"and plots to {args.average_output_plot}, "
            f"{args.agreement_output_plot} (state unchanged)"
        )
    else:
        result_rows = run_all_users(
            profile_csv=args.profile_csv,
            output_csv=args.output_csv,
            current_load=args.l_current,
            max_steps=args.max_steps,
            comfort_weight=args.comfort_weight,
            random_seed=args.random_seed,
            use_critiques=not args.disable_critiques,
        )
        accepted_count = sum(bool(row["accepted"]) for row in result_rows)
        print(
            f"wrote {len(result_rows)} users to {args.output_csv} "
            f"(accepted={accepted_count}, state unchanged)"
        )
