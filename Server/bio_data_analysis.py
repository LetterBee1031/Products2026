import pandas as pd
from pathlib import Path
import re
from typing import Dict, List

import matplotlib.pyplot as plt
from sklearn.tree import plot_tree
from sklearn.ensemble import RandomForestClassifier

HR_FILE = "data/hr_ibi.jsonl"
STATUS_FILE = "data/status_events.jsonl"
RESULT_FILE = Path("data/analysis_result.csv")

# N-backの体験状態を、機械学習で扱う認知負荷ラベルに変換する対応表。
ML_STATUS_LABELS: Dict[str, str] = {
    "0_back_start": "Low",
    "1_back_start": "Optimal",
    "2_back_start": "Optimal",
    "3_back_start": "High",
}

# ランダムフォレストに入力する生体特徴量。現状は心拍と皮膚電位だけを使う。
ML_FEATURE_COLUMNS: List[str] = ["hr", "eda"]

def normalize_user_id(user_id: str) -> str:
    # ユーザIDはファイル名に使うため、安全な文字以外を "_" に置き換える。
    safe_id = re.sub(r"[^0-9A-Za-z_-]", "_", str(user_id).strip())
    return safe_id or "01"


def hr_ibi_path_for_user(user_id: str, data_dir: str | Path = "data") -> Path:
    # ユーザごとの生体データ保存先を組み立てる。
    return Path(data_dir) / f"hr_ibi_{normalize_user_id(user_id)}.jsonl"


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


def load_ml_training_dataframe(user_id: str, data_dir: str | Path = "data") -> pd.DataFrame:
    # ユーザIDに対応するjsonlから、学習に使える行だけをDataFrameとして読み込む。
    file_path = hr_ibi_path_for_user(user_id, data_dir)
    if not file_path.exists():
        raise FileNotFoundError(f"生体データファイルが見つかりません: {file_path}")

    df = pd.read_json(file_path, lines=True)
    # HR/EDA/ex_statusが揃っていないと教師あり学習ができないため、先に列を検査する。
    required_columns = {"hr", "eda", "ex_status"}
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


def train_random_forest_cl_classifier(user_id: str, data_dir: str | Path = "data"):
    # ユーザごとのデータでランダムフォレストを学習する。
    df = load_ml_training_dataframe(user_id, data_dir)
    if df.empty:
        raise ValueError("学習に使えるHR/EDA/ex_statusデータがありません。")

    # 入力Xは生体特徴量、教師yはLow/Optimal/Highの認知負荷ラベル。
    x = df[ML_FEATURE_COLUMNS]
    y = df["cl_label"]

    # class_weight="balanced"で、状態ごとのデータ数の偏りを少し補正する。
    model = RandomForestClassifier(
        n_estimators=100,
        random_state=42,
        class_weight="balanced",
    )
    model.fit(x, y)



    # 画像出力
    
    tree_index = 0
    tree = model.estimators_[tree_index]

    importance_df = pd.DataFrame({
        "feature": ML_FEATURE_COLUMNS,
        "importance": model.feature_importances_
    }).sort_values("importance", ascending=True)

    fig, axes = plt.subplots(
        1, 2,
        figsize=(28, 12),
        gridspec_kw={"width_ratios": [2.2, 1]}
    )

    # 左：代表的な決定木
    plot_tree(
        tree,
        feature_names=ML_FEATURE_COLUMNS,
        class_names = [str(c) for c in model.classes_],
        filled=True,
        rounded=True,
        max_depth=5,
        fontsize=8,
        ax=axes[0]
    )
    axes[0].set_title(f"Representative Decision Tree in Random Forest\nTree index: {tree_index}")

    # 右：特徴量重要度
    axes[1].barh(
        importance_df["feature"],
        importance_df["importance"]
    )
    axes[1].set_title("Feature Importances")
    axes[1].set_xlabel("Importance")

    plt.tight_layout()
    plt.savefig("random_forest_summary.png", dpi=300, bbox_inches="tight")
    plt.show()

    return model, df


def classify_latest_cl_condition_with_random_forest(
    user_id: str,
    data_dir: str | Path = "data",
) -> dict:
    # 最新の有効データを、同じユーザの過去データで学習したモデルに入力して分類する。
    model, training_df = train_random_forest_cl_classifier(user_id, data_dir)

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
        "training_rows": int(len(training_df)),
        "classes": [str(label) for label in model.classes_],
        "features": {
            "hr": float(latest_record["hr"]),
            "eda": float(latest_record["eda"]),
        },
        "probabilities": probabilities,
    }
