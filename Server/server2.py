from __future__ import annotations

from datetime import datetime
import json
from pathlib import Path
import re
import sys
from typing import List, Optional
from zoneinfo import ZoneInfo

from fastapi import FastAPI, HTTPException, Request
from pydantic import BaseModel, Field

# 2段階上のフォルダ（Products2026）のパスをとってる
PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.append(str(PROJECT_ROOT))

# `uvicorn Server.server2:app` と `python server2.py` のどちらでも import できるようにする。
try:
    from Server.bio_data_analysis import classify_latest_cl_condition_with_random_forest
    from Server.bio_data_analysis import save_analysis_with_summary_to_csv
    from Server.bio_data_analysis import train_random_forest_cl_classifier
    from Server.read_jsonl_from_last import read_last_n_jsonl_as_dataframe
    from Server.shared_state import ISSUE_OPTIONS, user_status, load_user_profiles
except ModuleNotFoundError:
    from Server.bio_data_analysis import classify_latest_cl_condition_with_random_forest
    from Server.bio_data_analysis import save_analysis_with_summary_to_csv
    from Server.bio_data_analysis import train_random_forest_cl_classifier
    from read_jsonl_from_last import read_last_n_jsonl_as_dataframe
    from shared_state import ISSUE_OPTIONS, user_status, load_user_profiles

from negotiation import run_negotiation

# PLR補正モデルの学習・推論処理
try:
    from Server.plr_model import calculate_luminance_correlation, fit_plr_model, predict_pupil_diameter
except ModuleNotFoundError:
    from plr_model import calculate_luminance_correlation, fit_plr_model, predict_pupil_diameter

app = FastAPI()

SERVER_DIR = Path(__file__).resolve().parent
DATA_DIR = SERVER_DIR / "data"
DATA_DIR.mkdir(exist_ok=True)
MAX_IBI_PER_RECORD = 4

# 記録用ファイルパス
HR_JSONL_PATH = DATA_DIR / "hr_ibi.jsonl"
STATUS_JSONL_PATH = DATA_DIR / "status_events.jsonl"
USER_PROFILE_PATH = DATA_DIR / "user_profile.csv"

# load_user_profiles() 内部で issue_option.csv が先に読み込まれる。
# サーバ起動時に初期化し、どのAPIから交渉を始めてもCSVの設定を利用できるようにする。
load_user_profiles(USER_PROFILE_PATH)

# 心拍データクラス
class TrackedData(BaseModel):
    userId: str = "01"
    hr: int = Field(ge=0)
    ibi: List[int] = Field(default_factory=list)
    sentAt: str

# Galaxy Watchから送られるHR/IBI/EDAをまとめて受け取るためのデータ形式
class BiodataPost(BaseModel):
    userId: str = "01"
    hr: int = Field(ge=0)
    ibi: List[int] = Field(default_factory=list)
    eda: Optional[float] = None
    sentAt: str
    timestamp: Optional[int] = None
    deviceIp: Optional[str] = None

# Unit環境から視線データを送るためのやつ
class EyedataPost(BaseModel):
    userID: str = "01"
    pupilDiaMeanRaw: float
    pupilDiaMeanSmoothed: float

    predictedPupilMm: float
    tepr: float
    luminanceY: float

    sentAt: str
    timestamp: int
    deviceIp: str


# PLRキャリブレーションデータ1サンプル
class PLRCalibrationSample(BaseModel):
    luminanceY_panel: float
    luminanceY_cam: float
    luminanceGap: float
    pupilMm: float

# Unityから送られるPLRキャリブレーションデータ
class PLRFitRequest(BaseModel):
    userID: str = "01"
    sentAt: Optional[str] = None
    timestamp: Optional[int] = None
    deviceIp: Optional[str] = None
    samples: List[PLRCalibrationSample]

# Unityへ返すPLRモデル推定結果
class PLRFitResponse(BaseModel):
    ok: bool
    userID: str
    a: float
    b: float
    c: float
    mse: float
    sampleCount: int
    error: Optional[str] = None

# 任意の輝度値から予測瞳孔径を返す確認用API
class PLRPredictRequest(BaseModel):
    luminanceY: List[float]
    a: float
    b: float
    c: float

# 体験段階のポストのためのクラス
class StatusPost(BaseModel):
    id: str = "01"
    status_flag: str
    sent_at: str

# UnityのStroopManagerから送られる1試行分のログ。
class StroopLogPost(BaseModel):
    user_id: str = "01"
    condition: str
    trial_index: int
    is_practice: bool
    is_correct: bool
    reaction_time_ms: float
    stimulus_onset_time: str
    response_time: Optional[str] = None
    result: str
    sent_at: Optional[str] = None
    timestamp: Optional[int] = None
    deviceIp: Optional[str] = None

# UnityのMentalArithmeticManagerから送られる1試行分のログ。
class MentalArithmeticLogPost(BaseModel):
    participant_id: str = "01"
    block_id: int
    difficulty: str
    block_duration_sec: int
    trial_index: int
    a: int
    b: int
    correct_answer: int
    user_answer: str = ""
    is_correct: bool
    is_skipped: bool
    reaction_time_ms: float
    block_elapsed_time_ms: float
    trial_timestamp: str
    sent_at: Optional[str] = None
    timestamp: Optional[int] = None
    deviceIp: Optional[str] = None

def normalize_user_id(user_id: str) -> str:
    safe_id = re.sub(r"[^0-9A-Za-z_-]", "_", user_id.strip())
    return safe_id or "01"

def hr_jsonl_path_for_user(user_id: str) -> Path:
    return DATA_DIR / f"hr_ibi_{normalize_user_id(user_id)}.jsonl"

def eye_jsonl_path_for_user(user_id: str) -> Path:
    return DATA_DIR / f"eye_data{normalize_user_id(user_id)}.jsonl"

def stroop_jsonl_path_for_user(user_id: str) -> Path:
    return DATA_DIR / f"stroop_log_{normalize_user_id(user_id)}.jsonl"

def mental_arithmetic_jsonl_path_for_user(user_id: str) -> Path:
    return DATA_DIR / f"mental_arithmetic_log_{normalize_user_id(user_id)}.jsonl"

def append_records_by_user(records: List[dict]) -> None:
    files = {}
    try:
        for record in records:
            user_id = normalize_user_id(str(record.get("id", "01")))
            if user_id not in files:
                files[user_id] = hr_jsonl_path_for_user(user_id).open("a", encoding="utf-8")
            files[user_id].write(json.dumps(record, ensure_ascii=False) + "\n")
    finally:
        for f in files.values():
            f.close()

def append_eye_records_by_user(records: List[dict]) -> None:
    files = {}
    try:
        for record in records:
            user_id = normalize_user_id(str(record.get("userID", "01")))
            if user_id not in files:
                files[user_id] = eye_jsonl_path_for_user(user_id).open("a", encoding="utf-8")
            files[user_id].write(json.dumps(record, ensure_ascii=False) + "\n")
    finally:
        for f in files.values():
            f.close()

def append_stroop_record_by_user(record: dict) -> None:
    user_id = normalize_user_id(str(record.get("user_id", "01")))
    with stroop_jsonl_path_for_user(user_id).open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")

def append_mental_arithmetic_record_by_user(record: dict) -> None:
    user_id = normalize_user_id(str(record.get("participant_id", "01")))
    with mental_arithmetic_jsonl_path_for_user(user_id).open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")

# 心拍・心拍変動を受け取り，保存するパス
@app.post("/api/hr")
async def receive_batch(payload: List[TrackedData], request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()

    records = []
    for item in payload:
        user_id = normalize_user_id(item.userId)
        records.append({
            "id": user_id,
            "ex_status": user_status.get(user_id, user_status["01"]).ex_status,
            "sent_at": item.sentAt,
            "received_at": received_at,
            "client_host": client_host,
            "hr": item.hr,
            "ibi": item.ibi[:MAX_IBI_PER_RECORD],
        })
    append_records_by_user(records)

    return {"ok": True, "count": len(payload)}

# /api/hr の保存形式を拡張し、EDA・端末時刻・端末IPも同じjsonlに記録する
@app.post("/api/Biodata")
async def receive_biodata(payload: List[BiodataPost], request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()

    records = []
    for item in payload:
        user_id = normalize_user_id(item.userId)
        # client_hostはHTTP接続元、device_ipは時計側が自己申告したIPとして両方残す
        records.append({
            "id": user_id,
            "ex_status": user_status.get(user_id, user_status["01"]).ex_status,
            "sent_at": item.sentAt,
            "received_at": received_at,
            "client_host": client_host,
            "device_ip": item.deviceIp,
            "timestamp": item.timestamp,
            "hr": item.hr,
            # IBIは仕様上0〜4個なので、過剰な値が来ても保存時に丸める
            "ibi": item.ibi[:MAX_IBI_PER_RECORD],
            "eda": item.eda,
        })
    append_records_by_user(records)

    return {"ok": True, "count": len(payload)}


@app.post("/api/EyeData")
async def receive_eyedata(payload: List[EyedataPost], request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()

    records = []
    for item in payload:
        user_id = normalize_user_id(item.userID)
        records.append({
            "userID": user_id,
            "ex_status": user_status.get(user_id, user_status["01"]).ex_status,
            "sent_at": item.sentAt,
            "received_at": received_at,
            "client_host": client_host,
            "device_ip": item.deviceIp,
            "timestamp": item.timestamp,
            # "pupilDiaLeft": item.pupilDiaLeft,
            # "pupilDiaRight": item.pupilDiaRight,
            "pupilDiaMeanRaw": item.pupilDiaMeanRaw,
            "pupilDiaMeanSmoothed": item.pupilDiaMeanSmoothed,
            "predictedPupilMm": item.predictedPupilMm,
            "tepr": item.tepr,
            "luminanceY_cam": item.luminanceY
        })
    append_eye_records_by_user(records)

    return {"ok": True, "count": len(payload)}


# PLR補正用モデルの学習API
# UnityのRequestSender.csからキャリブレーションデータを受け取り、a,b,cを返す。
# Stroop課題のログを1試行ごとに受信し、被験者ID別のJSONLへ保存する。
@app.post("/api/stroop_log")
async def receive_stroop_log(payload: StroopLogPost, request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()
    user_id = normalize_user_id(payload.user_id)

    record = {
        "user_id": user_id,
        "condition": payload.condition,
        "trial_index": payload.trial_index,
        "is_practice": payload.is_practice,
        "is_correct": payload.is_correct,
        "reaction_time_ms": payload.reaction_time_ms,
        "stimulus_onset_time": payload.stimulus_onset_time,
        "response_time": payload.response_time,
        "result": payload.result,
        # Unity側の送信時刻とサーバ側の受信時刻を両方保存する。
        "sent_at": payload.sent_at,
        "received_at": received_at,
        "timestamp": payload.timestamp,
        "client_host": client_host,
        "device_ip": payload.deviceIp,
    }
    append_stroop_record_by_user(record)

    return {
        "ok": True,
        "user_id": user_id,
        "trial_index": payload.trial_index,
    }

# 暗算課題のログを1試行ごとに受信し、被験者ID別のJSONLへ保存する。
@app.post("/api/mental_arithmetic_log")
async def receive_mental_arithmetic_log(
    payload: MentalArithmeticLogPost,
    request: Request,
):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()
    participant_id = normalize_user_id(payload.participant_id)

    record = {
        "participant_id": participant_id,
        "block_id": payload.block_id,
        "difficulty": payload.difficulty,
        "block_duration_sec": payload.block_duration_sec,
        "trial_index": payload.trial_index,
        "a": payload.a,
        "b": payload.b,
        "correct_answer": payload.correct_answer,
        "user_answer": payload.user_answer,
        "is_correct": payload.is_correct,
        "is_skipped": payload.is_skipped,
        "reaction_time_ms": payload.reaction_time_ms,
        "block_elapsed_time_ms": payload.block_elapsed_time_ms,
        "trial_timestamp": payload.trial_timestamp,
        "sent_at": payload.sent_at,
        "received_at": received_at,
        "timestamp": payload.timestamp,
        "client_host": client_host,
        "device_ip": payload.deviceIp,
    }
    append_mental_arithmetic_record_by_user(record)

    return {
        "ok": True,
        "participant_id": participant_id,
        "block_id": payload.block_id,
        "trial_index": payload.trial_index,
    }

@app.post("/api/plr/fit", response_model=PLRFitResponse)
async def fit_plr(payload: PLRFitRequest, request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()
    user_id = normalize_user_id(payload.userID)

    # Pydanticモデルを通常のdictに変換する。
    samples = [sample.model_dump() for sample in payload.samples]

    # 後から確認できるよう、生データもjsonlに保存しておく。
    raw_record = {
        "userID": user_id,
        "sent_at": payload.sentAt,
        "received_at": received_at,
        "client_host": client_host,
        "device_ip": payload.deviceIp,
        "timestamp": payload.timestamp,
        "samples": samples,
    }
    with (DATA_DIR / f"plr_calibration_{user_id}.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps(raw_record, ensure_ascii=False) + "\n")

    try:
        result = fit_plr_model(samples)
        luminance_correlation = calculate_luminance_correlation(samples)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

    # 推定結果も別ファイルに保存する。
    result_record = {
        "userID": user_id,
        "received_at": received_at,
        **result,
        "luminanceCorrelation": luminance_correlation["correlation"],
        "luminanceCorrelationSampleCount": luminance_correlation["sampleCount"],
    }
    with (DATA_DIR / f"plr_params_{user_id}.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps(result_record, ensure_ascii=False) + "\n")

    return {
        "ok": True,
        "userID": user_id,
        "a": result["a"],
        "b": result["b"],
        "c": result["c"],
        "mse": result["mse"],
        "sampleCount": result["sampleCount"],
        "error": None,
    }

# PLRモデルの推論確認用API
# Unity側では基本的に a,b,c を受け取った後ローカル計算すればよい。
@app.post("/api/plr/predict")
async def predict_plr(payload: PLRPredictRequest):
    try:
        predicted = predict_pupil_diameter(
            payload.luminanceY,
            payload.a,
            payload.b,
            payload.c,
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

    return {"ok": True, "predictedPupilMm": predicted}

# ユーザデータの読み込み
@app.get("/api/set_profiles")
async def set_user_profiles_from_CSV():
    load_user_profiles(USER_PROFILE_PATH)
    try:
        return {
            "ok": True,
            "message": "user data are read correctly",
        }
    except Exception as e:
        return {"ok": False, "error": str(e)}

# 体験段階の変更に関するパス
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
        # 機械学習版では、このAPIでユーザIDごとのランダムフォレストを学習できるか確認する。
        model, training_df = train_random_forest_cl_classifier(id, DATA_DIR)
        return {
            "ok": True,
            "message": "random forest trained",
            "id": id,
            "rows": int(len(training_df)),
            "classes": [str(label) for label in model.classes_],
            "features": ["hr", "tepr"],
        }
    except Exception as e:
        return {"ok": False, "error": str(e)}

# 体験段階の取得のパス
@app.get("/api/ex_status_get")
async def read_ex_status(id: str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")
    return {"ok": True, "id": id, "status": user_status[id].ex_status}

# 体験者の認知負荷状態の取得と交渉のパス
@app.get("/api/cl_condition_get")
async def read_cl_condition(id: str = "01"):
    if id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    try:
        result = classify_latest_cl_condition_with_random_forest(id, DATA_DIR)
    except Exception as e:
        return {"ok": False, "id": id, "error": str(e)}

    user_status[id].cl_condition = result["label"]
    
    negotiation_result = None
    if user_status[id].cl_condition in {"High", "Low"}:
        # 分類ラベルを負荷代表値へ変換し、AAとPAの交渉を開始する。
        # 合意した場合のshared_state更新はNegotiationManager側で行う。
        current_load = 0.75 if user_status[id].cl_condition == "High" else 0.25
        negotiation_result = run_negotiation(
            user_id=id,
            current_load=current_load,
        )

    return {
        "ok": True,
        "id": id,
        "cl_condition": user_status[id].cl_condition,
        "ml_result": result,
        "issue_settings": user_status[id].issue_settings,
        "negotiation": (
            negotiation_result.to_dict() if negotiation_result is not None else None
        ),
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
