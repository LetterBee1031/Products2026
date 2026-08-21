import pandas as pd
from pathlib import Path
import re
import json
from datetime import datetime, timezone
from typing import Dict, List

import matplotlib.pyplot as plt
import joblib
import numpy as np
from sklearn.tree import plot_tree
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import Ridge
from sklearn.preprocessing import StandardScaler
from sklearn.pipeline import Pipeline

from sklearn.model_selection import GroupKFold, train_test_split
from sklearn.metrics import (
    accuracy_score,
    classification_report,
    f1_score,
    mean_absolute_error,
    mean_squared_error,
    precision_score,
    r2_score,
    recall_score,
)
from sklearn.metrics import confusion_matrix

HR_FILE = "data/hr_ibi.jsonl"
STATUS_FILE = "data/status_events.jsonl"
RESULT_FILE = Path("data/analysis_result.csv")
MODEL_DIR = Path("models")

# N-backの体験状態を、機械学習で扱う認知負荷ラベルに変換する対応表。
ML_STATUS_LABELS: Dict[str, str] = {
    "0_back_start": "Low",
    "1_back_start": "Low",
    "2_back_start": "Optimal",
    "3_back_start": "High",
    "4_back_start": "High",
}

# ランダムフォレストに入力する特徴量。hr_ibi側のHR/EDAと、eye_data側の左右瞳孔径を使う。
#ML_FEATURE_COLUMNS: List[str] = ["hr", "eda", "pupilDiaLeft", "pupilDiaRight"]
#ML_FEATURE_COLUMNS: List[str] = ["hr", "pupilDiaLeft", "pupilDiaRight"]
ML_FEATURE_COLUMNS: List[str] = ["hr", "tepr"]
ML_PREPROCESSING_NAME = "standard_scaler_zscore"
HR_TEPR_SYNC_TOLERANCE_SECONDS = 2
RIDGE_ALPHA = 1.0
RANDOM_FOREST_MODEL_CACHE: Dict[str, dict] = {}

# design_regression.md に基づく個人別の認知負荷回帰モデル設定。
# REGRESSION_FEATURE_COLUMNS: List[str] = ["heart_rate", "tepr"]
REGRESSION_FEATURE_COLUMNS: List[str] = ["heart_rate"]
# REGRESSION_FEATURE_COLUMNS: List[str] = ["tepr"]
# REGRESSION_FEATURE_COLUMNS: List[str] = ["heart_rate", "pupil_diameter_smoothed"]
# REGRESSION_FEATURE_COLUMNS: List[str] = ["pupil_diameter_smoothed"]
REGRESSION_FEATURE_SOURCE_COLUMNS: Dict[str, str] = {
    "heart_rate": "hr",
    "tepr": "tepr",
    "pupil_diameter_smoothed": "pupilDiaMeanSmoothed",
}
# OBJECTIVE_LOAD_MAPPING: Dict[int, float] = {
#     0: 0.25,
#     1: 0.50,
#     2: 0.75,
#     3: 1.00,
# }

# OBJECTIVE_LOAD_MAPPING: Dict[int, float] = {
#     0: 0.2,
#     1: 0.4,
#     2: 0.6,
#     3: 0.8,
# }

OBJECTIVE_LOAD_MAPPING: Dict[int, float] = {
    0: 0.25,
    2: 0.50,
    3: 0.75,
}

USER_ID_COLUMN_ALIASES: List[str] = [
    "user_id",
    "userId",
    "userID",
    "id",
    "participant_id",
]

def normalize_user_id(user_id: str) -> str:
    # ユーザIDはファイル名に使うため、安全な文字以外を "_" に置き換える。
    safe_id = re.sub(r"[^0-9A-Za-z_-]", "_", str(user_id).strip())
    return safe_id or "01"


def _canonical_user_id_for_comparison(user_id) -> str:
    # pandas.read_json が "01" を数値1や1.0へ型推論しても同じ参加者として比較する。
    text = str(user_id).strip()
    if re.fullmatch(r"[+]?[0-9]+(?:\.0+)?", text):
        return str(int(float(text)))
    return text


def _coalesce_user_id_column(df: pd.DataFrame) -> pd.Series:
    """新旧JSONLが混在していても、各行のユーザーIDを1列へ統合する。"""
    existing_columns = [column for column in USER_ID_COLUMN_ALIASES if column in df]
    if not existing_columns:
        raise ValueError(
            f"ユーザーID列がありません。対応列: {USER_ID_COLUMN_ALIASES}"
        )

    user_ids = df[existing_columns[0]].replace(r"^\s*$", np.nan, regex=True)
    for column in existing_columns[1:]:
        candidate = df[column].replace(r"^\s*$", np.nan, regex=True)
        user_ids = user_ids.combine_first(candidate)
    return user_ids


def _configured_regression_features(features: List[str] | None = None) -> List[str]:
    """設定された回帰特徴量を検証し、後続処理用のコピーを返す。"""
    features = list(REGRESSION_FEATURE_COLUMNS if features is None else features)
    if not features:
        raise ValueError("REGRESSION_FEATURE_COLUMNSには1つ以上の特徴量が必要です。")
    if len(features) != len(set(features)):
        raise ValueError("REGRESSION_FEATURE_COLUMNSに重複した特徴量があります。")
    unsupported = [
        feature for feature in features
        if feature not in REGRESSION_FEATURE_SOURCE_COLUMNS
    ]
    if unsupported:
        raise ValueError(
            f"未対応の回帰特徴量です: {unsupported}。"
            f" 対応特徴量: {sorted(REGRESSION_FEATURE_SOURCE_COLUMNS)}"
        )
    return features


def hr_ibi_path_for_user(user_id: str, data_dir: str | Path = "data") -> Path:
    # ユーザごとの生体データ保存先を組み立てる。
    return Path(data_dir) / f"hr_ibi_{normalize_user_id(user_id)}.jsonl"


def eye_data_path_for_user(user_id: str, data_dir: str | Path = "data") -> Path:
    # 視線データは eye_data{user_id}.jsonl という名前で保存されている。
    return Path(data_dir) / f"eye_data{normalize_user_id(user_id)}.jsonl"


def nasa_tlx_path_for_user(user_id: str, data_dir: str | Path = "data") -> Path:
    # NASA-TLX は Server/data/NASA-TLX_data_{user_id}.jsonl から読み込む。
    return Path(data_dir) / f"NASA-TLX_data_{normalize_user_id(user_id)}.jsonl"


def normalize_sent_at_to_jst(value) -> pd.Timestamp:
    # eye_dataは "YYYY/MM/DD HH:MM:SS"、hr_ibiはISO形式のことがあるため、
    # どちらもJSTへ揃える。最近傍判定の精度を保つため小数秒は丸めない。
    timestamp = pd.to_datetime(value, errors="coerce")
    if pd.isna(timestamp):
        return pd.NaT
    if timestamp.tzinfo is None:
        timestamp = timestamp.tz_localize("Asia/Tokyo")
    else:
        timestamp = timestamp.tz_convert("Asia/Tokyo")
    return timestamp


def merge_eye_data_by_sent_at(
    hr_df: pd.DataFrame,
    user_id: str,
    data_dir: str | Path = "data",
    eye_feature_columns: List[str] | None = None,
) -> pd.DataFrame:
    # HR/EDAの各行に、許容時間内で最も近いsent_atを持つTEPRを結合する。
    # 許容時間内に候補がない行はTEPRがNaNになり、後段のdropnaで除外される。
    eye_path = eye_data_path_for_user(user_id, data_dir)
    if not eye_path.exists():
        raise FileNotFoundError(f"視線データファイルが見つかりません: {eye_path}")

    if "sent_at" not in hr_df.columns:
        raise ValueError("生体データに sent_at 列がありません。")

    eye_df = pd.read_json(eye_path, lines=True)
    #required_eye_columns = {"sent_at", "pupilDiaLeft", "pupilDiaRight"}
    selected_eye_columns = list(eye_feature_columns or ["tepr"])
    required_eye_columns = {"sent_at", *selected_eye_columns}
    missing_eye_columns = required_eye_columns - set(eye_df.columns)
    if missing_eye_columns:
        raise ValueError(f"視線データに必要な列がありません: {sorted(missing_eye_columns)}")

    # 元のsent_at文字列は残したまま、比較専用の正規化キーを一時列として作る。
    hr_with_key = hr_df.copy()
    #eye_with_key = eye_df[["sent_at", "pupilDiaLeft", "pupilDiaRight"]].copy()
    eye_with_key = eye_df[["sent_at", *selected_eye_columns]].copy()
    hr_with_key["_sent_at_key"] = hr_with_key["sent_at"].map(normalize_sent_at_to_jst)
    eye_with_key["_sent_at_key"] = eye_with_key["sent_at"].map(normalize_sent_at_to_jst)

    # 同じ秒に複数のeye_dataがある場合は、最後に記録された値を代表値として使う。
    eye_with_key = eye_with_key.dropna(subset=["_sent_at_key"])
    eye_with_key = eye_with_key.drop_duplicates(subset=["_sent_at_key"], keep="last")

    # merge_asof用に時刻順へ並べ、結合後に元のHR行順へ戻す。
    hr_with_key["_hr_row_order"] = np.arange(len(hr_with_key))
    valid_hr = hr_with_key.dropna(subset=["_sent_at_key"]).sort_values("_sent_at_key")
    invalid_hr = hr_with_key[hr_with_key["_sent_at_key"].isna()].copy()
    for column in selected_eye_columns:
        invalid_hr[column] = np.nan
    eye_with_key = eye_with_key.sort_values("_sent_at_key")

    matched_hr = pd.merge_asof(
        valid_hr,
        eye_with_key[["_sent_at_key", *selected_eye_columns]],
        on="_sent_at_key",
        direction="nearest",
        tolerance=pd.Timedelta(seconds=HR_TEPR_SYNC_TOLERANCE_SECONDS),
    )
    merged = pd.concat([matched_hr, invalid_hr], ignore_index=True)
    merged = merged.sort_values("_hr_row_order")
    return merged.drop(columns=["_sent_at_key", "_hr_row_order"]).reset_index(drop=True)


def random_forest_model_path_for_user(
    user_id: str,
    model_dir: str | Path = MODEL_DIR,
) -> Path:
    return Path(model_dir) / f"random_forest_{normalize_user_id(user_id)}.joblib"


def cache_random_forest_model(user_id: str, model, training_rows: int) -> None:
    RANDOM_FOREST_MODEL_CACHE[normalize_user_id(user_id)] = {
        "model": model,
        "training_rows": int(training_rows),
        "preprocessing": ML_PREPROCESSING_NAME,
    }


def load_cached_or_saved_random_forest_model(
    user_id: str,
    model_dir: str | Path = MODEL_DIR,
):
    safe_user_id = normalize_user_id(user_id)
    cached = RANDOM_FOREST_MODEL_CACHE.get(safe_user_id)
    if cached is not None:
        if cached.get("preprocessing") != ML_PREPROCESSING_NAME:
            return None, 0
        return cached["model"], int(cached.get("training_rows", 0))

    model_path = random_forest_model_path_for_user(safe_user_id, model_dir)
    if not model_path.exists():
        return None, 0

    saved = joblib.load(model_path)
    if isinstance(saved, dict) and "model" in saved:
        saved_features = saved.get("features")
        # 特徴量構成が変わった保存済みモデルは使わず、現在の特徴量で再学習する。
        if saved_features is not None and list(saved_features) != ML_FEATURE_COLUMNS:
            return None, 0
        if saved.get("preprocessing") != ML_PREPROCESSING_NAME:
            return None, 0
        model = saved["model"]
        training_rows = int(saved.get("training_rows", 0))
    else:
        return None, 0

    cache_random_forest_model(safe_user_id, model, training_rows)
    return model, training_rows


def load_ml_training_dataframe(
    user_id: str,
    data_dir: str | Path = "data",
    feature_columns: List[str] | None = None,
) -> pd.DataFrame:
    # ユーザIDに対応するjsonlから、学習に使える行だけをDataFrameとして読み込む。
    selected_columns = list(ML_FEATURE_COLUMNS if feature_columns is None else feature_columns)
    file_path = hr_ibi_path_for_user(user_id, data_dir)
    if not file_path.exists():
        raise FileNotFoundError(f"生体データファイルが見つかりません: {file_path}")

    df = pd.read_json(file_path, lines=True)
    eye_feature_columns = [
        column for column in selected_columns
        if column in {"tepr", "pupilDiaMeanSmoothed"}
    ]
    if eye_feature_columns:
        df = merge_eye_data_by_sent_at(
            df, user_id, data_dir, eye_feature_columns=eye_feature_columns
        )
    required_columns = {"ex_status", "sent_at", *selected_columns}
    missing_columns = required_columns - set(df.columns)
    if missing_columns:
        raise ValueError(f"必要な列がありません: {sorted(missing_columns)}")

    # design.mdで指定されたN-back開始状態だけを教師データとして使う。
    df = df[df["ex_status"].isin(ML_STATUS_LABELS.keys())].copy()
    df["cl_label"] = df["ex_status"].map(ML_STATUS_LABELS)

    # 特徴量の欠損行を落とす前の時刻から、各ブロックの実質的な開始時刻を保持する。
    if "block_id" in df:
        training_block_ids = df["block_id"].fillna(df["ex_status"])
    else:
        training_block_ids = df["ex_status"]
    training_times = df["sent_at"].map(normalize_sent_at_to_jst)
    df["_block_start_sent_at"] = training_times.groupby(training_block_ids).transform("min")

    # センサー値に文字列やnullが混ざっても扱えるよう、数値化できないものは欠損にする。
    for column in selected_columns:
        df[column] = pd.to_numeric(df[column], errors="coerce")

    # 選択された特徴量・ラベルが欠けている行だけを除外する。
    df = df.dropna(subset=selected_columns + ["cl_label"])
    if "hr" in selected_columns:
        df = df[df["hr"] > 0]
    return df

def load_latest_prediction_dataframe(
    user_id: str,
    data_dir: str | Path = "data",
    feature_columns: List[str] | None = None,
) -> pd.DataFrame:
    # 現在のhr_ibi_{user_id}.jsonl全体から、推論に使える最新のHR/EDA行だけを取り出す。
    selected_columns = list(ML_FEATURE_COLUMNS if feature_columns is None else feature_columns)
    file_path = hr_ibi_path_for_user(user_id, data_dir)
    if not file_path.exists():
        raise FileNotFoundError(f"生体データファイルが見つかりません: {file_path}")

    df = pd.read_json(file_path, lines=True)
    eye_feature_columns = [
        column for column in selected_columns
        if column in {"tepr", "pupilDiaMeanSmoothed"}
    ]
    if eye_feature_columns:
        df = merge_eye_data_by_sent_at(
            df, user_id, data_dir, eye_feature_columns=eye_feature_columns
        )
    missing_columns = set(selected_columns) - set(df.columns)
    if missing_columns:
        raise ValueError(f"推論に必要な列がありません: {sorted(missing_columns)}")

    for column in selected_columns:
        df[column] = pd.to_numeric(df[column], errors="coerce")

    # 推論ではex_statusで絞り込まず、現在ファイルにある最新の有効な生体データを使う。
    df = df.dropna(subset=selected_columns)
    if "hr" in selected_columns:
        df = df[df["hr"] > 0]
    if df.empty:
        raise ValueError(
            f"推論に使える最新の特徴量データがありません: {selected_columns}"
        )

    if "received_at" in df:
        return df.sort_values("received_at").tail(1)
    return df.tail(1)


# モデルの保存先を返す　models/user○○/...
def regression_model_dir_for_user(
    user_id: str,
    model_dir: str | Path = MODEL_DIR,
) -> Path:
    """design_regression.md で規定された参加者別モデル保存先を返す。"""
    return Path(model_dir) / normalize_user_id(user_id)


# N-backの"N"を取り出すための関数．3_back_start → 3　てな感じで
def _extract_n_back(value) -> float:
    if pd.isna(value):
        return np.nan
    if isinstance(value, (int, np.integer)):
        return int(value)
    if isinstance(value, (float, np.floating)) and float(value).is_integer():
        return int(value)
    match = re.search(r"(?:^|[^0-9])([0-9]+)_?back", str(value), flags=re.IGNORECASE)
    return int(match.group(1)) if match else np.nan

# NASA-TLX データの読み出し
def load_nasa_tlx_dataframe(
    user_id: str,
    data_dir: str | Path = "data",
    subjective_measure: str = "raw_tlx",
) -> pd.DataFrame:
    """参加者の NASA-TLX を読み込み、ブロックごとの得点に整形する。"""

    # raw_tlxかmental_demandのどちらのオプションでもないとき
    if subjective_measure not in {"raw_tlx", "mental_demand"}:
        raise ValueError(
            "subjective_measure は 'raw_tlx' または 'mental_demand' を指定してください。"
        )


    file_path = nasa_tlx_path_for_user(user_id, data_dir)
    if not file_path.exists():
        raise FileNotFoundError(f"NASA-TLXデータファイルが見つかりません: {file_path}")

    nasa_df = pd.read_json(file_path, lines=True)
    # 負荷値として読み出すのは保存済みRawTLXとmental_demandのみ。
    # user_idとblock_idは参加者・生体データとの対応付けに使用する。
    required_columns = {"block_id", "RawTLX", "mental_demand"}
    missing_columns = required_columns - set(nasa_df.columns)
    if missing_columns:
        raise ValueError(
            f"NASA-TLXデータに必要な列がありません: {sorted(missing_columns)}"
        )

    # 新形式user_idを優先し、同一JSONL内にある旧形式の行も補完して読み込む。
    nasa_df["user_id"] = _coalesce_user_id_column(nasa_df)
    nasa_df = nasa_df[["user_id", "block_id", "RawTLX", "mental_demand"]].copy()
    comparable_user_id = _canonical_user_id_for_comparison(user_id)
    # IDが同じ行だけ抽出
    nasa_df = nasa_df[
        nasa_df["user_id"].map(_canonical_user_id_for_comparison) == comparable_user_id
    ].copy()
    if nasa_df.empty:
        raise ValueError(f"ユーザ {user_id} のNASA-TLXデータがありません。")

    # 分析のオプションに合わせて抽出するデータを選択
    score_columns = ["RawTLX"] if subjective_measure == "raw_tlx" else ["mental_demand"]
    # 抽出データを数値に変換．欠損値はNaNとなる
    for column in score_columns:
        nasa_df[column] = pd.to_numeric(nasa_df[column], errors="coerce")
    nasa_df = nasa_df.dropna(subset=["block_id"] + score_columns)     # 欠損がある行を削除
    nasa_df["block_id"] = nasa_df["block_id"].astype(str).str.strip() # block_idを文字列に統一
    nasa_df = nasa_df[nasa_df["block_id"] != ""]                      # 空文字の文字列を削除

    if subjective_measure == "raw_tlx":
        # NASA-TLX受信時に算出・保存されたRawTLXを正解ラベル作成に使用する。
        nasa_df["raw_tlx"] = nasa_df["RawTLX"]
    else:
        nasa_df["raw_tlx"] = nasa_df["mental_demand"]

    # 同じblock_idが再送された場合は、JSONL内で最後の回答だけを採用する。
    nasa_df = nasa_df.drop_duplicates(subset=["block_id"], keep="last")
    return nasa_df[["user_id", "block_id", "raw_tlx"]].reset_index(drop=True)


# 学習用データの読み込み
def load_regression_training_dataframe(
    user_id: str,
    data_dir: str | Path = "data",
    subjective_measure: str = "raw_tlx",
    aggregate_by_block: bool = False,
    regression_features: List[str] | None = None,
    block_warmup_seconds: float = 0.0,
) -> pd.DataFrame:
    """既存の心拍・瞳孔径読み込み処理にNASA-TLXをブロック単位で結合する。"""
    regression_features = _configured_regression_features(regression_features)
    if not np.isfinite(block_warmup_seconds) or block_warmup_seconds < 0:
        raise ValueError("block_warmup_secondsは0以上の有限値を指定してください。")
    source_columns = [
        REGRESSION_FEATURE_SOURCE_COLUMNS[feature]
        for feature in regression_features
    ]
    bio_df = load_ml_training_dataframe(
        user_id, data_dir, feature_columns=source_columns
    ).copy()
    if bio_df.empty:
        raise ValueError(
            f"回帰モデルの学習に使える特徴量データがありません: {regression_features}"
        )

    # 保存データ上の列名を、回帰モデル設定上の特徴量名に揃える。
    for feature in regression_features:
        source_column = REGRESSION_FEATURE_SOURCE_COLUMNS[feature]
        bio_df[feature] = pd.to_numeric(bio_df[source_column], errors="coerce")

    # 新形式にblock_id/n_backがあればそれを優先し、現行形式ではex_statusから補う。
    if "block_id" not in bio_df:
        bio_df["block_id"] = bio_df["ex_status"]
    else:
        bio_df["block_id"] = bio_df["block_id"].fillna(bio_df["ex_status"])
    bio_df["block_id"] = bio_df["block_id"].astype(str).str.strip()

    # N-backの"N"数を取得する．fillnaはNaNの行を置き換える関数らしい．
    if "n_back" in bio_df:
        parsed_n_back = bio_df["n_back"].map(_extract_n_back)
    else:
        parsed_n_back = pd.Series(np.nan, index=bio_df.index, dtype=float)
    parsed_n_back = parsed_n_back.fillna(bio_df["ex_status"].map(_extract_n_back))
    parsed_n_back = parsed_n_back.fillna(bio_df["block_id"].map(_extract_n_back))
    bio_df["n_back"] = parsed_n_back

    # NASA-TLX得点と生体情報の対応付け　block_idをキーに内部結合を実施
    nasa_df = load_nasa_tlx_dataframe(user_id, data_dir, subjective_measure)
    merged = bio_df.merge(nasa_df[["block_id", "raw_tlx"]], on="block_id", how="inner")
    merged = merged.dropna(
        subset=regression_features + ["block_id", "n_back", "raw_tlx"]
    )
    merged["n_back"] = merged["n_back"].astype(int) # "N"数を数値に変換
    merged["L_obj"] = merged["n_back"].map(OBJECTIVE_LOAD_MAPPING) # 変換表から"N"数を客観的負荷に変換
    merged = merged.dropna(subset=["L_obj"]).reset_index(drop=True) # 客観的負荷を与えられなかった行を削除．行番号を振り直し

    # mergedの中身がなければ（id不一致等）
    if merged.empty:
        bio_blocks = sorted(bio_df["block_id"].dropna().astype(str).unique())
        nasa_blocks = sorted(nasa_df["block_id"].dropna().astype(str).unique())
        raise ValueError(
            "生体データとNASA-TLXデータで一致するblock_idがありません。"
            f" 生体データ: {bio_blocks}, NASA-TLX: {nasa_blocks}"
        )
    if block_warmup_seconds > 0:
        # 特徴量の欠損除外前に求めた各block_idの開始時刻から、指定秒数未満を除外する。
        sample_times = merged["sent_at"].map(normalize_sent_at_to_jst)
        if "_block_start_sent_at" in merged:
            block_start_times = merged["_block_start_sent_at"].map(normalize_sent_at_to_jst)
        else:
            block_start_times = sample_times.groupby(merged["block_id"]).transform("min")
        elapsed_seconds = (sample_times - block_start_times).dt.total_seconds()
        merged = merged[elapsed_seconds >= block_warmup_seconds].reset_index(drop=True)
        if merged.empty:
            raise ValueError(
                "block_warmup_secondsの適用後に学習データが残りませんでした。"
                f" 指定値: {block_warmup_seconds}秒"
            )
    merged = merged.drop(columns=["_block_start_sent_at"], errors="ignore")
    if aggregate_by_block:
        # 1 block_idを1学習サンプルとし、生体特徴量はブロック内の平均値を使う。
        # n_back、L_obj、raw_tlxは同一ブロック内で共通のラベル情報として扱う。
        merged = (
            merged.groupby(
                ["block_id", "n_back", "L_obj", "raw_tlx"],
                as_index=False,
                dropna=False,
            )
            .agg(
                **{feature: (feature, "mean") for feature in regression_features},
                block_sample_count=("block_id", "size"),
            )
            .reset_index(drop=True)
        )
    return merged

# w_objとw_subについての判定
def _validate_regression_weights(w_obj: float, w_sub: float) -> None:
    if w_obj < 0 or w_sub < 0 or not np.isclose(w_obj + w_sub, 1.0):
        raise ValueError("w_objとw_subは0以上、かつ合計が1になるよう指定してください。")


def _tlx_normalization_parameters(df: pd.DataFrame) -> tuple[float, float]:
    # 1ブロックを1回答として扱い、サンプル数の多いブロックに偏らせない。
    block_scores = df[["block_id", "raw_tlx"]].drop_duplicates("block_id")
    mean = float(block_scores["raw_tlx"].mean())
    std = float(block_scores["raw_tlx"].std(ddof=0))
    return mean, std


# 負荷ラベル付与
def _add_regression_labels(
    df: pd.DataFrame,
    tlx_mean: float,
    tlx_std: float,
    w_obj: float,
    w_sub: float,
) -> pd.DataFrame:
    labeled = df.copy()
    if np.isclose(tlx_std, 0.0):
        labeled["Z_TLX"] = 0.0
    else:
        labeled["Z_TLX"] = (labeled["raw_tlx"] - tlx_mean) / tlx_std          # 標準化の計算
    labeled["L_sub"] = np.clip(labeled["Z_TLX"] / 4.0 + 0.5, 0.0, 1.0)        # 標準化したものを大体0～1にスケーリング
    labeled["L_label"] = w_obj * labeled["L_obj"] + w_sub * labeled["L_sub"]  # 重みづけした負荷ラベル計算
    return labeled


# ピアソン相関の補助用関数
def _safe_pearson_correlation(y_true, y_pred) -> float | None:
    y_true = np.asarray(y_true, dtype=float)
    y_pred = np.asarray(y_pred, dtype=float)
    # 相関計算不可と判断する条件　
    #   1.データ数が2未満で値の変化が比較できない
    #   2.真値が全て同じ（y_trueの標準偏差0）
    #   3.予測値が全て同じ（y_predの標準偏差0）
    if len(y_true) < 2 or np.isclose(np.std(y_true), 0.0) or np.isclose(np.std(y_pred), 0.0):
        return None
    return float(np.corrcoef(y_true, y_pred)[0, 1]) # 相関の計算


# 回帰の評価指標等の出力
def _regression_metrics(y_true, y_pred) -> dict:
    y_true = np.asarray(y_true, dtype=float)
    y_pred = np.asarray(y_pred, dtype=float)
    r2 = float(r2_score(y_true, y_pred)) if len(y_true) >= 2 else None
    if r2 is not None and not np.isfinite(r2):
        r2 = None
    return {
        "mae": float(mean_absolute_error(y_true, y_pred)),
        "rmse": float(np.sqrt(mean_squared_error(y_true, y_pred))),
        "r2": r2,
        "pearson_correlation": _safe_pearson_correlation(y_true, y_pred),
        "clipping_rate": float(np.mean((y_pred < 0.0) | (y_pred > 1.0))),
        "below_zero_rate": float(np.mean(y_pred < 0.0)),
        "above_one_rate": float(np.mean(y_pred > 1.0)),
    }


# 認知負荷推定モデルの評価に関する関数．交差検証する
def evaluate_cognitive_load_regression(
    training_df: pd.DataFrame,
    w_obj: float = 0.5,
    w_sub: float = 0.5,
    folds: int = 5,
    regression_features: List[str] | None = None,
) -> dict:
    """block_idをGroupとし、各fold内で標準化をfitして回帰性能を評価する。"""
    _validate_regression_weights(w_obj, w_sub)
    regression_features = _configured_regression_features(regression_features)
    groups = training_df["block_id"].astype(str)
    block_count = int(groups.nunique()) # 重複を除去してでブロック数を検証
    if block_count < 2: # ブロックが1個では交差検証が出来ない
        return {
            "enabled": False,
            "method": "group_k_fold",
            "estimator": {"name": "ridge", "alpha": float(RIDGE_ALPHA)},
            "block_count": block_count,
            "reason": "交差検証には少なくとも2つのblock_idが必要です。",
            "folds": [],
            "overall": None,
        }

    n_splits = min(max(int(folds), 2), block_count) # 交差検証に使う分割数
    splitter = GroupKFold(n_splits=n_splits) # GroupKFold使って分割
    fold_results = []
    all_true = []
    all_pred = []

    # GroupKFoldで作られた各Foldを順次処理
    # splitter.split(分割するデータ，groups=blockidで紐づけ)
    # enumerateで各foldに番号振り
    # train_index: 学習用データ
    # validation_index: 検証用データ
    for fold_number, (train_index, validation_index) in enumerate(
        splitter.split(training_df, groups=groups), start=1
    ):
        # iloc関数．先頭から何番目かという位置から行を取得
        train_df = training_df.iloc[train_index]
        validation_df = training_df.iloc[validation_index]
        tlx_mean, tlx_std = _tlx_normalization_parameters(train_df)
        train_labeled = _add_regression_labels(train_df, tlx_mean, tlx_std, w_obj, w_sub) # 負荷ラベル計算
        validation_labeled = _add_regression_labels(
            validation_df, tlx_mean, tlx_std, w_obj, w_sub
        ) # 負荷ラベル計算

        scaler = StandardScaler() # z標準化器
        x_train = scaler.fit_transform(train_labeled[regression_features])
        x_validation = scaler.transform(validation_labeled[regression_features])
        model = Ridge(alpha=RIDGE_ALPHA) # L2正則化付き線形回帰モデル
        model.fit(x_train, train_labeled["L_label"]) # foldでの学習
        prediction = model.predict(x_validation) # 予測
        truth = validation_labeled["L_label"].to_numpy() # 検証用負荷ラベル（真値として利用）

        metrics = _regression_metrics(truth, prediction) # 各種評価計算 MAEとか
        # foldでの結果
        fold_results.append(
            {
                "fold": fold_number, # fold番号
                "train_blocks": sorted(train_df["block_id"].astype(str).unique().tolist()), # 学習に使ったblock_id
                "validation_blocks": sorted(
                    validation_df["block_id"].astype(str).unique().tolist()
                ), # 検証に使ったblock_id
                "train_samples": int(len(train_df)), # 学習に使ったサンプル数
                "validation_samples": int(len(validation_df)), # 検証に使ったサンプル数
                "training_tlx_mean": tlx_mean,
                "training_tlx_std": tlx_std,
                **metrics, # MAEとか
            }
        )
        # appendとextendの違いについて．extendだと一次元リストとして保存されるらしい．appendだと入れ子
        all_true.extend(truth.tolist())
        all_pred.extend(prediction.tolist())

    return {
        "enabled": True,
        "method": "group_k_fold",
        "estimator": {"name": "ridge", "alpha": float(RIDGE_ALPHA)},
        "block_count": block_count,
        "n_splits": n_splits,
        "folds": fold_results,
        "overall": _regression_metrics(all_true, all_pred), # 全fold含んだ全体評価
    }


def save_cognitive_load_regression_plot(
    labeled_df: pd.DataFrame,
    standardized_features,
    model: Ridge,
    output_path: str | Path,
    user_id: str,
    regression_features: List[str] | None = None,
) -> Path:
    """学習サンプル、回帰平面、特徴量ごとの部分回帰直線を画像へ保存する。"""
    regression_features = _configured_regression_features(regression_features)
    x = np.asarray(standardized_features, dtype=float)
    if x.ndim != 2 or x.shape[1] != len(regression_features):
        raise ValueError(
            "standardized_featuresの列数がREGRESSION_FEATURE_COLUMNSと一致しません。"
        )
    if len(x) != len(labeled_df):
        raise ValueError("特徴量と学習ラベルのサンプル数が一致しません。")

    y = labeled_df["L_label"].to_numpy(dtype=float)
    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    def plot_range(values: np.ndarray) -> tuple[float, float]:
        minimum = float(np.min(values))
        maximum = float(np.max(values))
        if np.isclose(minimum, maximum):
            return minimum - 1.0, maximum + 1.0
        margin = (maximum - minimum) * 0.05
        return minimum - margin, maximum + margin

    feature_grids = [
        np.linspace(*plot_range(x[:, index]), 40)
        for index in range(len(regression_features))
    ]
    include_regression_plane = len(regression_features) == 2
    plot_count = len(regression_features) + int(include_regression_plane)
    fig = plt.figure(figsize=(6.3 * plot_count, 6.5))
    try:
        subplot_number = 1
        if include_regression_plane:
            first_surface, second_surface = np.meshgrid(
                feature_grids[0], feature_grids[1]
            )
            surface_features = np.column_stack(
                [first_surface.ravel(), second_surface.ravel()]
            )
            load_surface = model.predict(surface_features).reshape(first_surface.shape)
            plane_axis = fig.add_subplot(1, plot_count, subplot_number, projection="3d")
            samples = plane_axis.scatter(
                x[:, 0],
                x[:, 1],
                y,
                c=y,
                cmap="viridis",
                edgecolors="black",
                linewidths=0.3,
                alpha=0.8,
                label="Training samples",
            )
            plane_axis.plot_surface(
                first_surface,
                second_surface,
                load_surface,
                color="tab:orange",
                alpha=0.35,
                linewidth=0,
            )
            plane_axis.set_xlabel(f"{regression_features[0]} (Z-score)")
            plane_axis.set_ylabel(f"{regression_features[1]} (Z-score)")
            plane_axis.set_zlabel("Cognitive load label")
            plane_axis.set_title("Training data and regression plane")
            plane_axis.legend(loc="best")
            fig.colorbar(
                samples, ax=plane_axis, shrink=0.65, pad=0.12, label="L_label"
            )
            subplot_number += 1

        for feature_index, (feature_name, feature_grid) in enumerate(
            zip(regression_features, feature_grids)
        ):
            axis = fig.add_subplot(1, plot_count, subplot_number)
            partial_features = np.zeros((len(feature_grid), len(regression_features)))
            partial_features[:, feature_index] = feature_grid
            partial_prediction = model.predict(partial_features)

            axis.scatter(
                x[:, feature_index],
                y,
                c=y,
                cmap="viridis",
                edgecolors="black",
                linewidths=0.3,
                alpha=0.65,
                label="Training samples",
            )
            axis.plot(
                feature_grid,
                partial_prediction,
                color="tab:red",
                linewidth=2.5,
                label="Regression line",
            )
            axis.set_xlabel(f"{feature_name} (Z-score)")
            axis.set_ylabel("Cognitive load label")
            if len(regression_features) == 1:
                axis.set_title(f"Training data and regression line: {feature_name}")
            else:
                axis.set_title(
                    f"Partial regression: {feature_name}\n"
                    "(other features fixed at Z=0)"
                )
            axis.grid(alpha=0.25)
            axis.legend(loc="best")
            subplot_number += 1

        fig.suptitle(f"Cognitive load linear regression — user {user_id}", fontsize=15)
        fig.tight_layout(rect=(0, 0, 1, 0.94))
        fig.savefig(output_path, dpi=200, bbox_inches="tight")
    finally:
        plt.close(fig)
    return output_path


# モデルの学習・保存の関数
def train_cognitive_load_regression(
    user_id: str,
    data_dir: str | Path = "data",
    model_dir: str | Path = MODEL_DIR,
    subjective_measure: str = "raw_tlx",
    w_obj: float = 0.5,
    w_sub: float = 0.5,
    folds: int = 5,
    save_training_plot: bool = False,
    training_plot_filename: str = "regression_training_fit.png",
    aggregate_by_block: bool = False,
    block_warmup_seconds: float = 0.0,
):
    """参加者別モデルを学習し、必要に応じて学習データとの関係図も保存する。"""
    _validate_regression_weights(w_obj, w_sub) # 負荷ラベルの重みの確認
    regression_features = _configured_regression_features()
    plot_filename_path = Path(training_plot_filename)
    if (
        not plot_filename_path.name
        or plot_filename_path.is_absolute()
        or plot_filename_path.name != training_plot_filename
    ):
        raise ValueError("training_plot_filenameにはファイル名だけを指定してください。")
    training_df = load_regression_training_dataframe( #読み込み
        user_id,
        data_dir,
        subjective_measure,
        aggregate_by_block=aggregate_by_block,
        regression_features=regression_features,
        block_warmup_seconds=block_warmup_seconds,
    )
    cv_metrics = evaluate_cognitive_load_regression( # 交差検証
        training_df,
        w_obj=w_obj,
        w_sub=w_sub,
        folds=folds,
        regression_features=regression_features,
    )

    tlx_mean, tlx_std = _tlx_normalization_parameters(training_df)
    labeled_df = _add_regression_labels( # 負荷ラベルの付与
        training_df, tlx_mean, tlx_std, w_obj, w_sub
    )
    scaler = StandardScaler()
    x = scaler.fit_transform(labeled_df[regression_features]) # 標準化
    model = Ridge(alpha=RIDGE_ALPHA)
    model.fit(x, labeled_df["L_label"]) #学習

    # モデルの保存
    output_dir = regression_model_dir_for_user(user_id, model_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    joblib.dump(model, output_dir / "model.joblib")
    joblib.dump(scaler, output_dir / "x_scaler.joblib")
    training_plot_path = None
    if save_training_plot:
        training_plot_path = save_cognitive_load_regression_plot(
            labeled_df=labeled_df,
            standardized_features=x,
            model=model,
            output_path=output_dir / plot_filename_path,
            user_id=user_id,
            regression_features=regression_features,
        )

    metadata = {
        "user_id": str(user_id),                   # ユーザID
        "subjective_measure": subjective_measure,  # NASA-TLX全体か，mental_demandのみか
        "training_data_granularity": "block_mean" if aggregate_by_block else "sample",
        "block_warmup_seconds": float(block_warmup_seconds),
        "estimator": {"name": "ridge", "alpha": float(RIDGE_ALPHA)},
        "nasa_tlx_mean": tlx_mean,                 # NASA-TLXの平均値
        "nasa_tlx_std": tlx_std,                   # NASA-TLXの標準偏差
        "w_obj": float(w_obj),                     # 客観的負荷（N数）の重み
        "w_sub": float(w_sub),
        "objective_load_mapping": {                # 客観的負荷のマップ N数に応じた数値
            str(key): value for key, value in OBJECTIVE_LOAD_MAPPING.items()
        },
        "features": regression_features,          # 特徴量
        "feature_source_columns": {
            feature: REGRESSION_FEATURE_SOURCE_COLUMNS[feature]
            for feature in regression_features
        },
        # それぞれの特徴量の標準化時に用いた平均値とか
        "standardization": {
            "method": ML_PREPROCESSING_NAME,
            "mean": {
                name: float(value)
                for name, value in zip(regression_features, scaler.mean_)
            },
            "scale": {
                name: float(value)
                for name, value in zip(regression_features, scaler.scale_)
            },
        },
        "blocks": sorted(labeled_df["block_id"].astype(str).unique().tolist()), # 使われたblock_id
        "training_samples": int(len(labeled_df)), # 使われた特徴量サンプル数
        # 回帰モデルにおける特徴量の係数
        "coefficients": {
            name: float(value)
            for name, value in zip(regression_features, model.coef_)
        },
        "intercept": float(model.intercept_), # 回帰モデルの切片
        "training_plot": (
            training_plot_path.name if training_plot_path is not None else None
        ),
        "created_at": datetime.now(timezone.utc).isoformat(), # モデル作成時刻
    }
    (output_dir / "metadata.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    (output_dir / "cv_metrics.json").write_text(
        json.dumps(cv_metrics, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return model, scaler, labeled_df, cv_metrics


# 学習済みモデルを使った予測
def predict_cognitive_load(
    user_id: str,
    heart_rate: float | None = None,
    tepr: float | None = None,
    pupil_diameter_smoothed: float | None = None,
    sent_at=None,
    model_dir: str | Path = MODEL_DIR,
    feature_values: Dict[str, float] | None = None,
) -> dict:
    """学習済みモデルに1サンプルを入力し、L_cur_rawとL_curを返す。"""
    regression_features = _configured_regression_features()
    # モデルパスの取得
    output_dir = regression_model_dir_for_user(user_id, model_dir)
    model_path = output_dir / "model.joblib"
    scaler_path = output_dir / "x_scaler.joblib"
    if not model_path.exists() or not scaler_path.exists():
        raise FileNotFoundError(
            f"学習済み回帰モデルが見つかりません: {output_dir}"
        )

    # モデル・データの読み込み
    model = joblib.load(model_path)
    scaler = joblib.load(scaler_path)
    saved_feature_names = getattr(scaler, "feature_names_in_", None)
    if saved_feature_names is None:
        metadata_path = output_dir / "metadata.json"
        if metadata_path.exists():
            saved_metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
            saved_feature_names = saved_metadata.get("features")
    if saved_feature_names is None:
        saved_feature_count = int(getattr(scaler, "n_features_in_", 0))
        if saved_feature_count != len(regression_features):
            raise ValueError(
                "保存済みモデルの特徴量数が現在の設定と一致しません。"
                " 現在の設定でモデルを再学習してください。"
            )
        saved_feature_names = regression_features
    regression_features = _configured_regression_features(list(saved_feature_names))

    provided_values = {
        "heart_rate": heart_rate,
        "tepr": tepr,
        "pupil_diameter_smoothed": pupil_diameter_smoothed,
    }
    if feature_values is not None:
        provided_values.update(feature_values)
    missing_features = [
        feature for feature in regression_features
        if provided_values.get(feature) is None
    ]
    if missing_features:
        raise ValueError(f"推論に必要な特徴量がありません: {missing_features}")
    input_features = {
        feature: float(provided_values[feature])
        for feature in regression_features
    }
    features = pd.DataFrame(
        [input_features],
        columns=regression_features,
    )
    # 予測値の出力
    l_cur_raw = float(model.predict(scaler.transform(features))[0])
    l_cur = float(np.clip(l_cur_raw, 0.0, 1.0))
    # 時刻の記録
    if sent_at is None:
        sent_at_value = None
    else:
        timestamp = pd.to_datetime(sent_at, errors="coerce")
        sent_at_value = str(sent_at) if pd.isna(timestamp) else timestamp.isoformat()
    result = {
        "user_id": str(user_id),
        "sent_at": sent_at_value,
        "L_cur_raw": l_cur_raw,
        "L_cur": l_cur,
    }
    result.update(input_features)
    return result


def predict_latest_cognitive_load(
    user_id: str,
    data_dir: str | Path = "data",
    model_dir: str | Path = MODEL_DIR,
    auto_train: bool = True,
    subjective_measure: str = "raw_tlx",
    w_obj: float = 0.5,
    w_sub: float = 0.5,
    folds: int = 5,
    save_training_plot: bool = False,
    training_plot_filename: str = "regression_training_fit.png",
) -> dict:
    """既存の最新生体データ読み込み処理を使って認知負荷を推論する。"""
    regression_features = _configured_regression_features()
    output_dir = regression_model_dir_for_user(user_id, model_dir)
    # 自動で学習するオプションがTrueでかつ，学習済みモデルが存在していなかったら
    model_files_exist = (
        (output_dir / "model.joblib").exists()
        and (output_dir / "x_scaler.joblib").exists()
    )
    saved_features = None
    metadata_path = output_dir / "metadata.json"
    if model_files_exist and metadata_path.exists():
        saved_metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        saved_features = saved_metadata.get("features")
    elif model_files_exist:
        saved_scaler = joblib.load(output_dir / "x_scaler.joblib")
        saved_features = getattr(saved_scaler, "feature_names_in_", None)
    if auto_train and not model_files_exist:
        train_cognitive_load_regression(
            user_id,
            data_dir=data_dir,
            model_dir=model_dir,
            subjective_measure=subjective_measure,
            w_obj=w_obj,
            w_sub=w_sub,
            folds=folds,
            save_training_plot=save_training_plot,
            training_plot_filename=training_plot_filename,
        )
        saved_features = regression_features

    if saved_features is not None:
        regression_features = _configured_regression_features(list(saved_features))
    source_columns = [
        REGRESSION_FEATURE_SOURCE_COLUMNS[feature]
        for feature in regression_features
    ]

    # 最新値での予測実行
    latest_df = load_latest_prediction_dataframe(
        user_id, data_dir, feature_columns=source_columns
    )
    latest = latest_df.iloc[0]
    latest_feature_values = {
        feature: float(latest[REGRESSION_FEATURE_SOURCE_COLUMNS[feature]])
        for feature in regression_features
    }
    return predict_cognitive_load(
        user_id=user_id,
        sent_at=latest.get("sent_at", latest.get("received_at")),
        model_dir=model_dir,
        feature_values=latest_feature_values,
    )


def make_label_feature_stats_text(df: pd.DataFrame) -> str:
    labels = ["Low", "Optimal", "High"]
    stats_df = (
        df.groupby("cl_label")[ML_FEATURE_COLUMNS]
        .agg(["count", "mean", "median", lambda values: values.var(ddof=0)])
        .reindex(labels)
    )
    stats_df.columns = [
        f"{feature}_{stat_name if stat_name != '<lambda_0>' else 'variance'}"
        for feature, stat_name in stats_df.columns
    ]

    text = []
    text.append("Label Feature Statistics:")
    text.append("(variance: population variance, ddof=0)")
    text.append(stats_df.to_string(float_format=lambda value: f"{value:.4f}"))
    text.append("")
    return "\n".join(text)


def train_random_forest_cl_classifier(user_id: str, data_dir: str | Path = "data"):
    # ユーザごとのデータでランダムフォレストを学習する。
    df = load_ml_training_dataframe(user_id, data_dir)
    if df.empty:
        raise ValueError("学習に使えるHR/EDA/ex_statusデータがありません。")

    # 入力Xは生体特徴量、教師yはLow/Optimal/Highの認知負荷ラベル。
    x = df[ML_FEATURE_COLUMNS]
    y = df["cl_label"]

    # 変更点: データを訓練80%、一時データ20%に分割
    x_train, x_test, y_train, y_test = train_test_split(
        x,
        y,
        test_size=0.20,
        random_state=42,
        stratify=y
    )

    # 変更点: 一時データ30%を検証15%、テスト15%に分割
    # x_valid, x_test, y_valid, y_test = train_test_split(
    #     x_temp,
    #     y_temp,
    #     test_size=0.50,
    #     random_state=42,
    #     stratify=y_temp
    # )

    # zスコア化は訓練データだけで平均・標準偏差をfitし、検証/テスト/推論にも同じ変換を使う。
    model = Pipeline(
        steps=[
            ("scaler", StandardScaler()),
            (
                "classifier",
                RandomForestClassifier(
                    n_estimators=100,
                    random_state=42,
                    class_weight="balanced",
                ),
            ),
        ]
    )

    # 変更点: 全データではなく訓練データだけで学習
    model.fit(x_train, y_train)

    # 変更点: テストデータで予測
    # y_valid_pred = model.predict(x_valid)
    y_test_pred = model.predict(x_test)

    # 変更点: 評価指標を計算する内部関数を追加
    def make_metrics_text(name, y_true, y_pred):
        accuracy = accuracy_score(y_true, y_pred)
        precision = precision_score(y_true, y_pred, average="weighted", zero_division=0)
        recall = recall_score(y_true, y_pred, average="weighted", zero_division=0)
        f_value = f1_score(y_true, y_pred, average="weighted", zero_division=0)

        # 変更点: ラベル順を固定
        labels = ["Low", "Optimal", "High"]

        # 変更点: 混同行列を作成
        cm = confusion_matrix(y_true, y_pred, labels=labels)
        cm_df = pd.DataFrame(
            cm,
            index=[f"True_{label}" for label in labels],
            columns=[f"Pred_{label}" for label in labels]
        )

        text = []
        text.append(f"===== {name} =====")
        text.append(f"Accuracy : {accuracy:.4f}")
        text.append(f"Precision: {precision:.4f}")
        text.append(f"Recall   : {recall:.4f}")
        text.append(f"F-value  : {f_value:.4f}")
        text.append("")
        text.append("Confusion Matrix:")
        text.append(cm_df.to_string())
        text.append("")
        text.append("Classification Report:")
        text.append(classification_report(y_true, y_pred, labels=labels, zero_division=0))
        text.append("")

        return "\n".join(text)

    # 変更点: 結果をテキストとして作成
    result_text = []
    result_text.append("Random Forest Cognitive Load Classification Result")
    result_text.append("")
    result_text.append(f"Feature columns: {ML_FEATURE_COLUMNS}")
    result_text.append(f"Preprocessing  : {ML_PREPROCESSING_NAME}")
    result_text.append(f"Total rows     : {len(df)}")
    result_text.append(f"Train rows     : {len(x_train)}")
    # result_text.append(f"Validation rows: {len(x_valid)}")
    result_text.append(f"Test rows      : {len(x_test)}")
    result_text.append("")
    result_text.append(make_label_feature_stats_text(df))
    # result_text.append(make_metrics_text("Validation Result", y_valid, y_valid_pred))
    result_text.append(make_metrics_text("Test Result", y_test, y_test_pred))

    result_text = "\n".join(result_text)

    # 変更点: 結果をコンソールにテキスト出力
    print(result_text)

    # 変更点: 結果をテキストファイルにも保存
    RESULT_FILE.parent.mkdir(parents=True, exist_ok=True)
    result_txt_path = RESULT_FILE.with_suffix(".txt")
    result_txt_path.write_text(result_text, encoding="utf-8")

    training_rows = int(len(x_train))
    cache_random_forest_model(user_id, model, training_rows)

    model_path = random_forest_model_path_for_user(user_id)
    model_path.parent.mkdir(parents=True, exist_ok=True)
    joblib.dump(
        {
            "model": model,
            "training_rows": training_rows,
            "features": ML_FEATURE_COLUMNS,
            "classes": [str(label) for label in model.classes_],
            "preprocessing": ML_PREPROCESSING_NAME,
            # 変更点: 評価結果もモデル保存情報に含める
            # "validation_rows": int(len(x_valid)),
            "test_rows": int(len(x_test)),
            "evaluation_text": result_text,
        },
        model_path,
    )

    # 画像出力
    classifier = model.named_steps["classifier"]
    tree_index = 0
    tree = classifier.estimators_[tree_index]

    importance_df = pd.DataFrame({
        "feature": ML_FEATURE_COLUMNS,
        "importance": classifier.feature_importances_
    }).sort_values("importance", ascending=True)

    fig, axes = plt.subplots(
        1, 2,
        figsize=(28, 12),
        gridspec_kw={"width_ratios": [2.2, 1]}
    )

    plot_tree(
        tree,
        feature_names=ML_FEATURE_COLUMNS,
        class_names=[str(c) for c in model.classes_],
        filled=True,
        rounded=True,
        max_depth=5,
        fontsize=8,
        ax=axes[0]
    )
    axes[0].set_title(
        f"Representative Decision Tree in Random Forest\nTree index: {tree_index}"
    )

    axes[1].barh(
        importance_df["feature"],
        importance_df["importance"]
    )
    axes[1].set_title("Feature Importances")
    axes[1].set_xlabel("Importance")

    plt.tight_layout()
    plt.savefig("random_forest_summary.png", dpi=300, bbox_inches="tight")
    plt.close(fig)

    # 変更点: 評価済みモデルと学習元dfを返す
    return model, df

def classify_latest_cl_condition_with_random_forest(
    user_id: str,
    data_dir: str | Path = "data",
) -> dict:
    # 最新の有効データを、同じユーザの過去データで学習したモデルに入力して分類する。
    model, training_rows = load_cached_or_saved_random_forest_model(user_id)
    if model is None:
        model, training_df = train_random_forest_cl_classifier(user_id, data_dir)
        training_rows = int(len(training_df))

    # 学習用に絞った行ではなく、現在のhr_ibi_{user_id}.jsonlから最新の有効行を読む。
    latest_df = load_latest_prediction_dataframe(user_id, data_dir)
    latest_features = latest_df[ML_FEATURE_COLUMNS]
    predicted_label = str(model.predict(latest_features)[0])

    # 可能なら各クラスの予測確率も返し、サーバレスポンスから判定の強さを見られるようにする。
    probabilities = {}
    if hasattr(model, "predict_proba"):
        proba = model.predict_proba(latest_features)[0]
        probabilities = {
            str(label): float(prob)
            for label, prob in zip(model.classes_, proba)
        }

    latest_record = latest_df.iloc[0]
    # server2.pyからそのままJSONとして返せるよう、基本型だけのdictに整形する。
    return {
        "label": predicted_label,
        "training_rows": int(training_rows),
        "classes": [str(label) for label in model.classes_],
        "features": {
            column: float(latest_record[column])
            for column in ML_FEATURE_COLUMNS
        },
        "probabilities": probabilities,
    }















# 使ってないけど一応残してるだけのもの　後で消す
def analyze_Nback_hr(n_back_num: int):
    # JSONLを読み込む
    hr_df = pd.read_json(HR_FILE, lines=True)
    status_df = pd.read_json(STATUS_FILE, lines=True)

    # 時刻文字列を datetime 型に変換
    hr_df["received_at"] = pd.to_datetime(hr_df["received_at"])
    status_df["received_at"] = pd.to_datetime(status_df["received_at"])

    # 時刻順に並べる
    hr_df = hr_df.sort_values("received_at").reset_index(drop=True)
    status_df = status_df.sort_values("received_at").reset_index(drop=True)

    # N_back_start / N_back_end だけ抽出
    n_back_events = status_df[
        status_df["status_flag"].isin([f"{n_back_num}_back_start", f"{n_back_num}_back_end"])
    ].reset_index(drop=True)

    sessions = []
    current_start = None

    # start/end を対応付けてセッションを作る
    for _, row in n_back_events.iterrows():
        flag = row["status_flag"]
        time = row["received_at"]

        if flag == f"{n_back_num}_back_start":
            current_start = time

        elif flag == f"{n_back_num}_back_end" and current_start is not None:
            sessions.append((current_start, time))
            current_start = None

    # 各セッションごとに心拍を集計
    results = []

    # 上のセッションで
    for i, (start_time, end_time) in enumerate(sessions, start=1):
        target_hr_df = hr_df[
            (hr_df["received_at"] >= start_time) &
            (hr_df["received_at"] <= end_time)
        ]

        if target_hr_df.empty:
            results.append({
                "N": n_back_num,
                "session": i,
                "start_time": start_time,
                "end_time": end_time,
                "count": 0,
                "mean_hr": None,
                "max_hr": None,
                "min_hr": None,
                "row_type": "session",
            })
        else:
            results.append({
                "N": n_back_num,
                "session": i,
                "start_time": start_time,
                "end_time": end_time,
                "count": int(target_hr_df["hr"].count()),
                "mean_hr": float(target_hr_df["hr"].mean()),
                "max_hr": int(target_hr_df["hr"].max()),
                "min_hr": int(target_hr_df["hr"].min()),
                "row_type": "session",
            })

    return results


# 解析したデータをCSVファイルに書き出し
def save_analysis_with_summary_to_csv(n_back_num: int, file_path="data/analysis_result.csv"):
    results = analyze_Nback_hr(n_back_num)

    if not results:
        raise ValueError("セッションが見つかりません。")

    result_df = pd.DataFrame(results)

    # mean_hr があるセッションだけ使う
    valid_mean_df = result_df[result_df["mean_hr"].notna()]

    if valid_mean_df.empty:
        raise ValueError("平均心拍数を計算できるセッションがありません。")

    # セッション平均の単純平均
    overall_mean_hr = valid_mean_df["mean_hr"].mean()

    summary_row = pd.DataFrame([{
        "N": n_back_num,
        "session": "summary",
        "start_time": pd.NA,
        "end_time": pd.NA,
        "count": int(valid_mean_df["count"].sum()),
        "mean_hr": float(overall_mean_hr),
        "max_hr": pd.NA,
        "min_hr": pd.NA,
        "row_type": "summary",
    }])

    # summary_rowの形をresult_dfの形に合わせる
    summary_row = summary_row.astype(result_df.dtypes.to_dict(), errors="ignore")

    # セッション行 + summary行 を結合
    output_df = pd.concat([result_df, summary_row], ignore_index=True)

    file_path = Path(file_path)
    output_df.to_csv(
        file_path,
        mode="a",
        index=False,
        header=not file_path.exists(),
        encoding="utf-8-sig"
    )

    return output_df, overall_mean_hr



# def train_random_forest_cl_classifier(user_id: str, data_dir: str | Path = "data"):
#     # ユーザごとのデータでランダムフォレストを学習する。
#     df = load_ml_training_dataframe(user_id, data_dir)
#     if df.empty:
#         raise ValueError("学習に使えるHR/EDA/ex_statusデータがありません。")

#     # 入力Xは生体特徴量、教師yはLow/Optimal/Highの認知負荷ラベル。
#     x = df[ML_FEATURE_COLUMNS]
#     y = df["cl_label"]

#     # class_weight="balanced"で、状態ごとのデータ数の偏りを少し補正する。
#     model = RandomForestClassifier(
#         n_estimators=100,
#         random_state=42,
#         class_weight="balanced",
#     )
#     model.fit(x, y)

#     training_rows = int(len(df))
#     cache_random_forest_model(user_id, model, training_rows)

#     model_path = random_forest_model_path_for_user(user_id)
#     model_path.parent.mkdir(parents=True, exist_ok=True)
#     joblib.dump(
#         {
#             "model": model,
#             "training_rows": training_rows,
#             "features": ML_FEATURE_COLUMNS,
#             "classes": [str(label) for label in model.classes_],
#         },
#         model_path,
#     )



#     # 画像出力

#     tree_index = 0
#     tree = model.estimators_[tree_index]

#     importance_df = pd.DataFrame({
#         "feature": ML_FEATURE_COLUMNS,
#         "importance": model.feature_importances_
#     }).sort_values("importance", ascending=True)

#     fig, axes = plt.subplots(
#         1, 2,
#         figsize=(28, 12),
#         gridspec_kw={"width_ratios": [2.2, 1]}
#     )

#     # 左：代表的な決定木
#     plot_tree(
#         tree,
#         feature_names=ML_FEATURE_COLUMNS,
#         class_names = [str(c) for c in model.classes_],
#         filled=True,
#         rounded=True,
#         max_depth=5,
#         fontsize=8,
#         ax=axes[0]
#     )
#     axes[0].set_title(f"Representative Decision Tree in Random Forest\nTree index: {tree_index}")

#     # 右：特徴量重要度
#     axes[1].barh(
#         importance_df["feature"],
#         importance_df["importance"]
#     )
#     axes[1].set_title("Feature Importances")
#     axes[1].set_xlabel("Importance")

#     plt.tight_layout()
#     plt.savefig("random_forest_summary.png", dpi=300, bbox_inches="tight")
#     plt.close(fig)

#     return model, df
