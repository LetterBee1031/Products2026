import pandas as pd
from pathlib import Path
import re
from typing import Dict, List

import matplotlib.pyplot as plt
import joblib
from sklearn.tree import plot_tree
from sklearn.ensemble import RandomForestClassifier
from sklearn.preprocessing import StandardScaler
from sklearn.pipeline import Pipeline

from sklearn.model_selection import train_test_split
from sklearn.metrics import accuracy_score, precision_score, recall_score, f1_score, classification_report
from sklearn.metrics import confusion_matrix

HR_FILE = "data/hr_ibi.jsonl"
STATUS_FILE = "data/status_events.jsonl"
RESULT_FILE = Path("data/analysis_result.csv")
MODEL_DIR = Path("models")

# N-backの体験状態を、機械学習で扱う認知負荷ラベルに変換する対応表。
ML_STATUS_LABELS: Dict[str, str] = {
    # "0_back_start": "Low",
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
RANDOM_FOREST_MODEL_CACHE: Dict[str, dict] = {}


def normalize_user_id(user_id: str) -> str:
    # ユーザIDはファイル名に使うため、安全な文字以外を "_" に置き換える。
    safe_id = re.sub(r"[^0-9A-Za-z_-]", "_", str(user_id).strip())
    return safe_id or "01"


def hr_ibi_path_for_user(user_id: str, data_dir: str | Path = "data") -> Path:
    # ユーザごとの生体データ保存先を組み立てる。
    return Path(data_dir) / f"hr_ibi_{normalize_user_id(user_id)}.jsonl"


def eye_data_path_for_user(user_id: str, data_dir: str | Path = "data") -> Path:
    # 視線データは eye_data{user_id}.jsonl という名前で保存されている。
    return Path(data_dir) / f"eye_data{normalize_user_id(user_id)}.jsonl"


def normalize_sent_at_to_jst_second(value) -> pd.Timestamp:
    # eye_dataは "YYYY/MM/DD HH:MM:SS"、hr_ibiはISO形式のことがあるため、
    # どちらもJSTの秒単位に丸めて同期用キーとして使う。
    timestamp = pd.to_datetime(value, errors="coerce")
    if pd.isna(timestamp):
        return pd.NaT
    if timestamp.tzinfo is None:
        timestamp = timestamp.tz_localize("Asia/Tokyo")
    else:
        timestamp = timestamp.tz_convert("Asia/Tokyo")
    return timestamp.floor("s")


def merge_eye_data_by_sent_at(
    hr_df: pd.DataFrame,
    user_id: str,
    data_dir: str | Path = "data",
) -> pd.DataFrame:
    # HR/EDAの各行に、同じsent_atを持つ左右瞳孔径を結合する。
    # 一致しない行は瞳孔径がNaNになり、後段のdropnaで学習/推論から除外される。
    eye_path = eye_data_path_for_user(user_id, data_dir)
    if not eye_path.exists():
        raise FileNotFoundError(f"視線データファイルが見つかりません: {eye_path}")

    if "sent_at" not in hr_df.columns:
        raise ValueError("生体データに sent_at 列がありません。")

    eye_df = pd.read_json(eye_path, lines=True)
    #required_eye_columns = {"sent_at", "pupilDiaLeft", "pupilDiaRight"}
    required_eye_columns = {"sent_at", "tepr"}
    missing_eye_columns = required_eye_columns - set(eye_df.columns)
    if missing_eye_columns:
        raise ValueError(f"視線データに必要な列がありません: {sorted(missing_eye_columns)}")

    # 元のsent_at文字列は残したまま、比較専用の正規化キーを一時列として作る。
    hr_with_key = hr_df.copy()
    #eye_with_key = eye_df[["sent_at", "pupilDiaLeft", "pupilDiaRight"]].copy()
    eye_with_key = eye_df[["sent_at", "tepr"]].copy()
    hr_with_key["_sent_at_key"] = hr_with_key["sent_at"].map(normalize_sent_at_to_jst_second)
    eye_with_key["_sent_at_key"] = eye_with_key["sent_at"].map(normalize_sent_at_to_jst_second)

    # 同じ秒に複数のeye_dataがある場合は、最後に記録された値を代表値として使う。
    eye_with_key = eye_with_key.dropna(subset=["_sent_at_key"])
    eye_with_key = eye_with_key.drop_duplicates(subset=["_sent_at_key"], keep="last")

    merged = hr_with_key.merge(
        #eye_with_key[["_sent_at_key", "pupilDiaLeft", "pupilDiaRight"]],
        eye_with_key[["_sent_at_key", "tepr"]],
        on="_sent_at_key",
        how="left",
    )
    return merged.drop(columns=["_sent_at_key"])


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


def load_ml_training_dataframe(user_id: str, data_dir: str | Path = "data") -> pd.DataFrame:
    # ユーザIDに対応するjsonlから、学習に使える行だけをDataFrameとして読み込む。
    file_path = hr_ibi_path_for_user(user_id, data_dir)
    if not file_path.exists():
        raise FileNotFoundError(f"生体データファイルが見つかりません: {file_path}")

    df = pd.read_json(file_path, lines=True)
    df = merge_eye_data_by_sent_at(df, user_id, data_dir)
    # HR/EDA/ex_statusと、同じsent_atを持つ瞳孔径が揃っていないと教師あり学習ができない。
    #required_columns = {"hr", "eda", "ex_status", "sent_at", "pupilDiaLeft", "pupilDiaRight"}
    required_columns = {"hr", "eda", "ex_status", "sent_at", "tepr"}
    missing_columns = required_columns - set(df.columns)
    if missing_columns:
        raise ValueError(f"必要な列がありません: {sorted(missing_columns)}")

    # design.mdで指定されたN-back開始状態だけを教師データとして使う。
    df = df[df["ex_status"].isin(ML_STATUS_LABELS.keys())].copy()
    df["cl_label"] = df["ex_status"].map(ML_STATUS_LABELS)

    # センサー値に文字列やnullが混ざっても扱えるよう、数値化できないものは欠損にする。
    for column in ML_FEATURE_COLUMNS:
        df[column] = pd.to_numeric(df[column], errors="coerce")

    # HR/EDA/ラベルが欠けている行と、心拍0のような無効値は学習から除外する。
    df = df.dropna(subset=ML_FEATURE_COLUMNS + ["cl_label"])
    df = df[df["hr"] > 0]
    return df

def load_latest_prediction_dataframe(user_id: str, data_dir: str | Path = "data") -> pd.DataFrame:
    # 現在のhr_ibi_{user_id}.jsonl全体から、推論に使える最新のHR/EDA行だけを取り出す。
    file_path = hr_ibi_path_for_user(user_id, data_dir)
    if not file_path.exists():
        raise FileNotFoundError(f"生体データファイルが見つかりません: {file_path}")

    df = pd.read_json(file_path, lines=True)
    df = merge_eye_data_by_sent_at(df, user_id, data_dir)
    missing_columns = set(ML_FEATURE_COLUMNS) - set(df.columns)
    if missing_columns:
        raise ValueError(f"推論に必要な列がありません: {sorted(missing_columns)}")

    for column in ML_FEATURE_COLUMNS:
        df[column] = pd.to_numeric(df[column], errors="coerce")

    # 推論ではex_statusで絞り込まず、現在ファイルにある最新の有効な生体データを使う。
    df = df.dropna(subset=ML_FEATURE_COLUMNS)
    df = df[df["hr"] > 0]
    if df.empty:
        raise ValueError("推論に使える最新HR/EDAデータがありません。")

    if "received_at" in df:
        return df.sort_values("received_at").tail(1)
    return df.tail(1)


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
