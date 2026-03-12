import pandas as pd
from pathlib import Path

HR_FILE = "data/hr_ibi.jsonl"
STATUS_FILE = "data/status_events.jsonl"
RESULT_FILE = Path("data/analysis_result.csv")
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
    one_back_events = status_df[
        status_df["status_flag"].isin([f"{n_back_num}_back_start", f"{n_back_num}_back_end"])
    ].reset_index(drop=True)

    sessions = []
    current_start = None

    # start/end を対応付けてセッションを作る
    for _, row in one_back_events.iterrows():
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