from __future__ import annotations

from datetime import datetime
from zoneinfo import ZoneInfo
from pathlib import Path
from typing import Dict, List
import json
import pandas as pd

from fastapi import FastAPI, Request, HTTPException
from pydantic import BaseModel, Field

from hr_data_analysis import analyze_Nback_hr, save_analysis_with_summary_to_csv
from read_jsonl_from_last import read_last_n_jsonl_as_dataframe
from Server.shared_state import ISSUE_OPTIONS, userData, user_status

from negotiation.TestNegotiation1 import run_example
app = FastAPI()

DATA_DIR = Path("data")
DATA_DIR.mkdir(exist_ok=True)

# 記録用ファイルパス
HR_JSONL_PATH = DATA_DIR / "hr_ibi.jsonl"
STATUS_JSONL_PATH = DATA_DIR / "status_events.jsonl"

# 調整可能なパラメータ　交渉の論点
ISSUE_OPTIONS: Dict[str, List[str]] = {
    "tempo": ["slow", "normal", "fast"],
    "guidance": ["low", "medium", "high"],
    "complexity": ["low", "medium", "high"],
    "stimulus": ["low", "medium", "high"],
    "break_policy": ["rare", "on_demand", "frequent"],
    "feedback": ["summary", "brief_immediate", "detailed_immediate"],
    "taste": ["polite", "concise", "encouraging", "neutral"],
}

# 調整可能パラメータのデフォルト設定
DEFAULT_ISSUE_SETTINGS: Dict[str, str] = {
    "tempo": "normal",
    "guidance": "medium",
    "complexity": "medium",
    "stimulus": "medium",
    "break_policy": "on_demand",
    "feedback": "brief_immediate",
    "taste": "polite",
}

# 計測データクラス
class TrackedData(BaseModel):
    hr: int = Field(ge=0)
    ibi: List[int] = []
    # timestamp_ms: int  # 送信側タイムスタンプ(ms)
    sentAt:str

# ユーザデータクラス
class userData(BaseModel):
    name: str = "None"
    ex_status: str = "None"
    cl_condition: str = "None"
    low_threshold: float = 0
    high_threshold: float = 1000
    issue_settings: Dict[str, str] = Field(
        default_factory=lambda: DEFAULT_ISSUE_SETTINGS.copy()
    )

# 体験段階のポストのためのクラス
class StatusPost(BaseModel):
    id: str = "01"          # 1人固定
    status_flag: str
    # timestamp_ms: int       # ★Unity送信時刻(ms)
    sent_at: str

class StatusGetRequest(BaseModel):
    id: str = "01"

# user_status: Dict[str, str] = {"01": "None"}
user_status: Dict[str, userData] = {
    "01": userData(),
    "02": userData(),
    "03": userData(),
    }

# 心拍・心拍変動を受け取り，保存のパス
from Server.shared_state import ISSUE_OPTIONS as SHARED_ISSUE_OPTIONS, user_status as SHARED_USER_STATUS, userData as SharedUserData

ISSUE_OPTIONS = SHARED_ISSUE_OPTIONS
user_status = SHARED_USER_STATUS
userData = SharedUserData

@app.post("/api/hr")
async def receive_batch(payload: List[TrackedData], request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()

    with HR_JSONL_PATH.open("a", encoding="utf-8") as f:
        for item in payload:
            record = {
                "ex_status":user_status["01"].ex_status,
                "sent_at":item.sentAt,
                # "timestamp_ms": item.timestamp_ms,
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

    user_status[payload.id].ex_status = payload.status_flag

    record = {
        "received_at": received_at,
        "client_host": client_host,
        "id": payload.id,
        "status_flag": payload.status_flag,
        # "timestamp_ms": payload.timestamp_ms,   # ★統一
        "sent_at": payload.sent_at
    }
    with STATUS_JSONL_PATH.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")

    return {"ok": True, "id": payload.id, "status": user_status[payload.id].ex_status}

# 心拍情報の解析・閾値設定のパス
@app.get("/api/analyze_hr/set_threshold")
async def analyze_hr_save_csv(id:str = "01"):
    try:
        # hr_data_analysis.pyから呼び出し
        result_one_back_df, mean_one_back = save_analysis_with_summary_to_csv(1)
        result_three_back_df, mean_three_back = save_analysis_with_summary_to_csv(3)

        user_status[id].low_threshold = mean_one_back
        user_status[id].high_threshold = mean_three_back
        
        rows = len(result_one_back_df) + len(result_three_back_df)

        return {
            "ok": True,
            "message": "解析結果をCSVに保存しました",
            "rows": rows,
            # "file": "data/analysis_result.csv"
        }
    except Exception as e:
        return {
            "ok": False,
            "error": str(e)
        }

# 体験段階の取得のパス
@app.get("/api/ex_status_get")
async def read_status_post(id:str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")
    return {"ok": True, "id": id, "status": user_status[id].ex_status}

# 体験者の認知負荷状態の取得のパス
@app.get("/api/cl_condition_get")
async def read_status_post(id:str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    latest_hr_df = read_last_n_jsonl_as_dataframe(HR_JSONL_PATH,3)
    latest_hr = latest_hr_df["hr"].mean()

    if latest_hr > user_status[id].high_threshold:
        user_status[id].cl_condition = "High"
        run_example(L_current=0.75)

    elif latest_hr < user_status[id].low_threshold:
        user_status[id].cl_condition = "Low"
        run_example(L_current=0.25)

    else:
        user_status[id].cl_condition = "Optimal"
    
    #return {"ok": True, "id": id, "cl_condition": user_status[id].cl_condition}
    return {"ok": True, "id": id, "issue_setting": user_status[id].issue_settings}

# 現在の現在の体験設定を確認するやつ
@app.get("/api/issue_settings_get")
async def read_issue_settings(id: str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    return {
        "ok": True,
        "id": id,
        # "issue_options": ISSUE_OPTIONS,
        "issue_settings": user_status[id].issue_settings,
    }
