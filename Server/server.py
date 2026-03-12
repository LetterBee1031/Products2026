from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from typing import List

from fastapi import FastAPI, Request
from pydantic import BaseModel, Field

app = FastAPI()

DATA_DIR = Path("data")
DATA_DIR.mkdir(exist_ok=True)
JSONL_PATH = DATA_DIR / "hr_ibi.jsonl"



class TrackedData(BaseModel):
    hr: int = Field(ge=0)
    ibi: List[int] = []  # ms
    timestamp_ms: int # 送信側のタイムスタンプ

class User(BaseModel):
    id: str
    ex_status: str = "None"

user_data = [
    {"user_id":"01", "status": "1_back"},
    {"user_id":"02", "status": "3_back"},
    {"user_id":"03", "status": "None"}
    ]

@app.post("/api/hr")
async def receive_batch(payload: List[TrackedData], request: Request):
    # 送信元（参考）
    client_host = request.client.host if request.client else "unknown"
    # 受信時刻（UTC）
    received_at = datetime.now(timezone.utc).isoformat()

    # 1件ずつJSON Linesとして追記保存
    lines = []
    for item in payload:
        record = {
            "timestamp_ms": item.timestamp_ms, # 送信時刻
            "received_at": received_at, # 受信時刻
            "client_host": client_host, # 送信元のアドレス
            "hr": item.hr, # 心拍
            "ibi": item.ibi, # 心拍間隔
        }
        lines.append(record)

    # まとめて追記（1リクエスト=複数行）
    with JSONL_PATH.open("a", encoding="utf-8") as f:
        for rec in lines:
            f.write(f"{rec}\n")  # まずは簡単に辞書reprで保存（下でJSON化版も紹介）

    return {"ok": True, "count": len(payload)}

@app.get("/ListTest")
async def read_List():
    return user_data

@app.post("/ListChangeTest")
async def change_List(payload: User):
    for items in user_data:
        if items["user_id"] == payload.id:
            items["status"] = payload.ex_status
    return 