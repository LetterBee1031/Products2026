from __future__ import annotations

from datetime import datetime
import json
from pathlib import Path
import sys
from typing import List
from zoneinfo import ZoneInfo

from fastapi import FastAPI, HTTPException, Request
from pydantic import BaseModel, Field

# 2段階上のフォルダ（Products2026）のパスをとってる
PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.append(str(PROJECT_ROOT))

# `uvicorn Server.server2:app` と `python server2.py` のどちらでも import できるようにする。
try:
    from Server.hr_data_analysis import save_analysis_with_summary_to_csv
    from Server.read_jsonl_from_last import read_last_n_jsonl_as_dataframe
    from Server.shared_state import ISSUE_OPTIONS, user_status
except ModuleNotFoundError:
    from hr_data_analysis import save_analysis_with_summary_to_csv
    from read_jsonl_from_last import read_last_n_jsonl_as_dataframe
    from shared_state import ISSUE_OPTIONS, user_status

from negotiation.TestNegotiation1 import run_example

app = FastAPI()

DATA_DIR = Path("data")
DATA_DIR.mkdir(exist_ok=True)

# 記録用ファイルパス
HR_JSONL_PATH = DATA_DIR / "hr_ibi.jsonl"
STATUS_JSONL_PATH = DATA_DIR / "status_events.jsonl"

# 心拍データクラス
class TrackedData(BaseModel):
    hr: int = Field(ge=0)
    ibi: List[int] = []
    sentAt: str

# 体験段階のポストのためのクラス
class StatusPost(BaseModel):
    id: str = "01"
    status_flag: str
    sent_at: str

# 心拍・心拍変動を受け取り，保存するパス
@app.post("/api/hr")
async def receive_batch(payload: List[TrackedData], request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()

    with HR_JSONL_PATH.open("a", encoding="utf-8") as f:
        for item in payload:
            record = {
                "ex_status": user_status["01"].ex_status,
                "sent_at": item.sentAt,
                "received_at": received_at,
                "client_host": client_host,
                "hr": item.hr,
                "ibi": item.ibi,
            }
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

    return {"ok": True, "count": len(payload)}

# 体験段階の取得・変更に関するパス
@app.post("/api/status_post")
async def change_status(payload: StatusPost, request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()

    if payload.id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    user_status[payload.id].ex_status = payload.status_flag

    record = {
        "received_at": received_at,
        "client_host": client_host,
        "id": payload.id,
        "status_flag": payload.status_flag,
        "sent_at": payload.sent_at,
    }
    with STATUS_JSONL_PATH.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")

    return {"ok": True, "id": payload.id, "status": user_status[payload.id].ex_status}

# 心拍情報の解析・閾値設定のパス
@app.get("/api/analyze_hr/set_threshold")
async def analyze_hr_save_csv(id: str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    try:
        # hr_data_analysis.pyから呼び出し
        result_one_back_df, mean_one_back = save_analysis_with_summary_to_csv(1)
        result_three_back_df, mean_three_back = save_analysis_with_summary_to_csv(3)

        user_status[id].low_threshold = mean_one_back
        user_status[id].high_threshold = mean_three_back

        rows = len(result_one_back_df) + len(result_three_back_df)
        return {
            "ok": True,
            "message": "thresholds updated",
            "rows": rows,
        }
    except Exception as e:
        return {"ok": False, "error": str(e)}

# 体験段階の取得のパス
@app.get("/api/ex_status_get")
async def read_ex_status(id: str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")
    return {"ok": True, "id": id, "status": user_status[id].ex_status}

# 体験者の認知負荷状態の取得のパス
@app.get("/api/cl_condition_get")
async def read_cl_condition(id: str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    latest_hr_df = read_last_n_jsonl_as_dataframe(HR_JSONL_PATH, 3)
    latest_hr = latest_hr_df["hr"].mean()

    if latest_hr > user_status[id].high_threshold:
        user_status[id].cl_condition = "High"
        run_example(L_current=80, user_id=id)
    elif latest_hr < user_status[id].low_threshold:
        user_status[id].cl_condition = "Low"
        run_example(L_current=20, user_id=id)
    else:
        user_status[id].cl_condition = "Optimal"

    return {
        "ok": True,
        "id": id,
        "cl_condition": user_status[id].cl_condition,
        "issue_settings": user_status[id].issue_settings,
    }

# 現在の現在の体験設定を確認するパス
@app.get("/api/issue_settings_get")
async def read_issue_settings(id: str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    return {
        "ok": True,
        "id": id,
        #"issue_options": ISSUE_OPTIONS,
        "issue_settings": user_status[id].issue_settings,
    }
