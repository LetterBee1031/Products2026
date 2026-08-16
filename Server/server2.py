from __future__ import annotations

from datetime import datetime
import json
from pathlib import Path
import re
import sys
from typing import List, Optional
from zoneinfo import ZoneInfo

from fastapi import FastAPI, HTTPException, Request
from pydantic import AliasChoices, BaseModel, Field

# 2段階上のフォルダ（Products2026）のパスをとってる
PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.append(str(PROJECT_ROOT))

# `uvicorn Server.server2:app` と `python server2.py` のどちらでも import できるようにする。
try:
    from Server.bio_data_analysis import predict_latest_cognitive_load
    from Server.bio_data_analysis import save_analysis_with_summary_to_csv
    from Server.bio_data_analysis import train_cognitive_load_regression
    from Server.read_jsonl_from_last import read_last_n_jsonl_as_dataframe
    from Server.shared_state import ISSUE_OPTIONS, user_status, load_user_profiles
except ModuleNotFoundError:
    from Server.bio_data_analysis import predict_latest_cognitive_load
    from Server.bio_data_analysis import save_analysis_with_summary_to_csv
    from Server.bio_data_analysis import train_cognitive_load_regression
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
NEGOTIATION_LOAD_LOW = 0.3
NEGOTIATION_LOAD_HIGH = 0.7

# 記録用ファイルパス
HR_JSONL_PATH = DATA_DIR / "hr_ibi.jsonl"
STATUS_JSONL_PATH = DATA_DIR / "status_events.jsonl"
USER_PROFILE_PATH = DATA_DIR / "user_profile.csv"

# load_user_profiles() 内部で issue_option.csv が先に読み込まれる。
# サーバ起動時に初期化し、どのAPIから交渉を始めてもCSVの設定を利用できるようにする。
load_user_profiles(USER_PROFILE_PATH)

# Galaxy Watchから送られるHR/IBI/EDAをまとめて受け取るためのデータ形式
class BiodataPost(BaseModel):
    user_id: str = Field(
        default="01",
        validation_alias=AliasChoices("user_id", "userId", "userID", "id", "participant_id"),
    )
    hr: int = Field(ge=0)
    ibi: List[int] = Field(default_factory=list)
    eda: Optional[float] = None
    sentAt: str
    timestamp: Optional[int] = None
    deviceIp: Optional[str] = None

# Unit環境から視線データを送るためのやつ
class EyedataPost(BaseModel):
    user_id: str = Field(
        default="01",
        validation_alias=AliasChoices("user_id", "userId", "userID", "id", "participant_id"),
    )
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
    user_id: str = Field(
        default="01",
        validation_alias=AliasChoices("user_id", "userId", "userID", "id", "participant_id"),
    )
    sentAt: Optional[str] = None
    timestamp: Optional[int] = None
    deviceIp: Optional[str] = None
    samples: List[PLRCalibrationSample]

# Unityへ返すPLRモデル推定結果
class PLRFitResponse(BaseModel):
    ok: bool
    user_id: str
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
    user_id: str = Field(
        default="01",
        validation_alias=AliasChoices("user_id", "userId", "userID", "id", "participant_id"),
    )
    status_flag: str
    ex_block_id: str = None
    sent_at: str

# UnityのStroopManagerから送られる1試行分のログ。
class StroopLogPost(BaseModel):
    user_id: str = Field(
        default="01",
        validation_alias=AliasChoices("user_id", "userId", "userID", "id", "participant_id"),
    )
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
    user_id: str = Field(
        default="01",
        validation_alias=AliasChoices("user_id", "userId", "userID", "id", "participant_id"),
    )
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


# NASA-TLX の受信データモデル
class NASATLXPost(BaseModel):
    user_id: str = Field(
        default="01",
        validation_alias=AliasChoices("user_id", "userId", "userID", "id", "participant_id"),
    )
    # block_id: str
    mental_demand: float
    physical_demand: float
    temporal_demand: float 
    performance: float
    effort: float
    frustration: float
    sent_at: Optional[str] = None
    timestamp: Optional[int] = None
    deviceIp: Optional[str] = None

def normalize_user_id(user_id: str) -> str:
    safe_id = re.sub(r"[^0-9A-Za-z_-]", "_", user_id.strip())
    return safe_id or "01"


def user_id_from_record(record: dict, default: str = "01") -> str:
    """新旧いずれのユーザーIDキーからでも保存先IDを取得する。"""
    for key in ("user_id", "userId", "userID", "id", "participant_id"):
        value = record.get(key)
        if value is not None and str(value).strip():
            return normalize_user_id(str(value))
    return normalize_user_id(default)


def resolve_query_user_id(user_id: Optional[str], legacy_id: Optional[str]) -> str:
    """新しいuser_idと移行期間用の旧idクエリをどちらも受け付ける。"""
    return normalize_user_id(user_id if user_id is not None else legacy_id or "01")

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
            user_id = user_id_from_record(record)
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
            user_id = user_id_from_record(record)
            if user_id not in files:
                files[user_id] = eye_jsonl_path_for_user(user_id).open("a", encoding="utf-8")
            files[user_id].write(json.dumps(record, ensure_ascii=False) + "\n")
    finally:
        for f in files.values():
            f.close()

def append_stroop_record_by_user(record: dict) -> None:
    user_id = user_id_from_record(record)
    with stroop_jsonl_path_for_user(user_id).open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")

def append_mental_arithmetic_record_by_user(record: dict) -> None:
    user_id = user_id_from_record(record)
    with mental_arithmetic_jsonl_path_for_user(user_id).open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")

def append_nasa_tlx_record_by_user(record: dict) -> None:
    user_id = user_id_from_record(record)
    path = DATA_DIR / f"NASA-TLX_data_{user_id}.jsonl"
    # ファイルが無ければ open('a') で自動的に作成される
    with path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")


# /api/hr の保存形式を拡張し、EDA・端末時刻・端末IPも同じjsonlに記録する
@app.post("/api/Biodata")
async def receive_biodata(payload: List[BiodataPost], request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()

    records = []
    for item in payload:
        user_id = normalize_user_id(item.user_id)
        # client_hostはHTTP接続元、device_ipは時計側が自己申告したIPとして両方残す
        records.append({
            "user_id": user_id,
            "ex_status": user_status.get(user_id, user_status["01"]).ex_status,
            "block_id": user_status.get(user_id, user_status["01"]).block_id,
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
        user_id = normalize_user_id(item.user_id)
        records.append({
            "user_id": user_id,
            "ex_status": user_status.get(user_id, user_status["01"]).ex_status,
            "block_id": user_status.get(user_id, user_status["01"]).block_id,
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
    user_id = normalize_user_id(payload.user_id)

    record = {
        "user_id": user_id,
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
        "user_id": user_id,
        "block_id": payload.block_id,
        "trial_index": payload.trial_index,
    }

# NASA-TLX解答を受け取り保存
@app.post("/api/nasa_tlx")
async def receive_nasa_tlx(payload: NASATLXPost, request: Request, mode: str = "raw_tlx"):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()
    user_id = normalize_user_id(payload.user_id)

    # Raw TLX の計算モード
    mode_lower = (mode or "").lower()
    if mode_lower not in {"raw_tlx", "mental_only"}:
        raise HTTPException(status_code=400, detail="invalid mode; use 'raw_tlx' or 'mental_only'")

    try:
        mental = float(payload.mental_demand)
        physical = float(payload.physical_demand)
        temporal = float(payload.temporal_demand)
        performance = float(payload.performance)
        effort = float(payload.effort)
        frustration = float(payload.frustration)
    except Exception:
        raise HTTPException(status_code=400, detail="invalid TLX values")

    if mode_lower == "mental_only":
        raw_tlx = mental
    else:
        scales = [mental, physical, temporal, performance, effort, frustration]
        raw_tlx = sum(scales) / len(scales)

    record = {
        "user_id": user_id,
        #"block_id": payload.block_id,
        "ex_status": user_status.get(user_id, user_status["01"]).ex_status,
        "block_id": user_status.get(user_id, user_status["01"]).block_id,
        "mental_demand": payload.mental_demand,
        "physical_demand": payload.physical_demand,
        "temporal_demand": payload.temporal_demand,
        "performance": payload.performance,
        "effort": payload.effort,
        "frustration": payload.frustration,
        "RawTLX": raw_tlx,
        "subjective_mode": mode_lower,
        "sent_at": payload.sent_at,
        "received_at": received_at,
        "timestamp": payload.timestamp,
        "client_host": client_host,
        "device_ip": payload.deviceIp,
    }

    append_nasa_tlx_record_by_user(record)

    return {"ok": True, "user_id": user_id, "ex_status": user_status.get(user_id, user_status["01"]).ex_status, "block_id": user_status.get(user_id, user_status["01"]).block_id, "RawTLX": raw_tlx}

# 輝度補正モデルフィット
@app.post("/api/plr/fit", response_model=PLRFitResponse)
async def fit_plr(payload: PLRFitRequest, request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()
    user_id = normalize_user_id(payload.user_id)

    # Pydanticモデルを通常のdictに変換する。
    samples = [sample.model_dump() for sample in payload.samples]

    # 後から確認できるよう、生データもjsonlに保存しておく。
    raw_record = {
        "user_id": user_id,
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
        "user_id": user_id,
        "received_at": received_at,
        **result,
        "luminanceCorrelation": luminance_correlation["correlation"],
        "luminanceCorrelationSampleCount": luminance_correlation["sampleCount"],
    }
    with (DATA_DIR / f"plr_params_{user_id}.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps(result_record, ensure_ascii=False) + "\n")

    return {
        "ok": True,
        "user_id": user_id,
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

    user_id = normalize_user_id(payload.user_id)
    if user_id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    user_status[user_id].ex_status = payload.status_flag
    user_status[user_id].block_id = payload.ex_block_id

    record = {
        "received_at": received_at,
        "client_host": client_host,
        "user_id": user_id,
        "status_flag": payload.status_flag,
        "block_id": payload.ex_block_id,
        "sent_at": payload.sent_at,
    }
    with STATUS_JSONL_PATH.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")

    return {"ok": True, "user_id": user_id, "status": user_status[user_id].ex_status, "block_id": user_status[user_id].block_id}


# 個人別の認知負荷線形回帰モデルを学習・評価・保存するパス
@app.get("/api/analyze_hr/set_threshold")
async def analyze_hr_save_csv(
    user_id: Optional[str] = None,
    id: Optional[str] = None,
    subjective_measure: str = "raw_tlx",
    w_obj: float = 0.5,
    w_sub: float = 0.5,
    folds: int = 5,
    save_training_plot: bool = False,
):
    resolved_user_id = resolve_query_user_id(user_id, id)
    if resolved_user_id not in user_status:
        raise HTTPException(status_code=404, detail="unknown user_id")

    try:
        model, scaler, training_df, cv_metrics = train_cognitive_load_regression(
            resolved_user_id,
            data_dir=DATA_DIR,
            model_dir=SERVER_DIR / "models",
            subjective_measure=subjective_measure,
            w_obj=w_obj,
            w_sub=w_sub,
            folds=folds,
            save_training_plot=save_training_plot,
        )
        features = [str(feature) for feature in scaler.feature_names_in_]
        output_dir = SERVER_DIR / "models" / resolved_user_id
        return {
            "ok": True,
            "message": "cognitive load linear regression trained",
            "user_id": resolved_user_id,
            "rows": int(len(training_df)),
            "blocks": sorted(
                training_df["block_id"].astype(str).unique().tolist()
            ),
            "features": features,
            "coefficients": {
                feature: float(coefficient)
                for feature, coefficient in zip(features, model.coef_)
            },
            "intercept": float(model.intercept_),
            "cross_validation": cv_metrics,
            "artifacts": {
                "model": str(output_dir / "model.joblib"),
                "scaler": str(output_dir / "x_scaler.joblib"),
                "metadata": str(output_dir / "metadata.json"),
                "cv_metrics": str(output_dir / "cv_metrics.json"),
                "training_plot": (
                    str(output_dir / "regression_training_fit.png")
                    if save_training_plot
                    else None
                ),
            },
        }
    except Exception as e:
        return {"ok": False, "user_id": resolved_user_id, "error": str(e)}

# 体験段階の取得のパス
@app.get("/api/ex_status_get")
async def read_ex_status(user_id: Optional[str] = None, id: Optional[str] = None):
    resolved_user_id = resolve_query_user_id(user_id, id)
    if resolved_user_id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")
    return {
        "ok": True,
        "user_id": resolved_user_id,
        "status": user_status[resolved_user_id].ex_status,
    }

# 体験者の認知負荷推定値の取得と交渉のパス
@app.get("/api/cl_condition_get")
async def read_cl_condition(user_id: Optional[str] = None, id: Optional[str] = None):
    resolved_user_id = resolve_query_user_id(user_id, id)
    if resolved_user_id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    try:
        result = predict_latest_cognitive_load(
            resolved_user_id,
            data_dir=DATA_DIR,
            model_dir=SERVER_DIR / "models",
        )
    except Exception as e:
        return {"ok": False, "user_id": resolved_user_id, "error": str(e)}

    current_load = float(result["L_cur"])
    # cl_conditionは既存クライアントが文字列として受信するため、連続値の文字列表現を保持する。
    # 新規クライアントは数値型のL_curを使用する。
    user_status[resolved_user_id].cl_condition = str(current_load)

    negotiation_result = None
    negotiation_triggered = (
        current_load < NEGOTIATION_LOAD_LOW
        or current_load > NEGOTIATION_LOAD_HIGH
    )
    if negotiation_triggered:
        # 回帰モデルの連続値をそのままAAとPAの交渉へ渡す。
        # 合意した場合のshared_state更新はNegotiationManager側で行う。
        negotiation_result = run_negotiation(
            user_id=resolved_user_id,
            current_load=current_load,
        )

    return {
        "ok": True,
        "user_id": resolved_user_id,
        "cl_condition": user_status[resolved_user_id].cl_condition,
        "L_cur": current_load,
        "L_cur_raw": float(result["L_cur_raw"]),
        "ml_result": result,
        "issue_settings": user_status[resolved_user_id].issue_settings,
        "negotiation_triggered": negotiation_triggered,
        "negotiation": (
            negotiation_result.to_dict() if negotiation_result is not None else None
        ),
    }

# 現在の現在の体験設定を確認するパス
@app.get("/api/issue_settings_get")
async def read_issue_settings(user_id: Optional[str] = None, id: Optional[str] = None):
    resolved_user_id = resolve_query_user_id(user_id, id)
    if resolved_user_id not in user_status:
        raise HTTPException(status_code=404, detail="unknown id")

    return {
        "ok": True,
        "user_id": resolved_user_id,
        #"issue_options": ISSUE_OPTIONS,
        "issue_settings": user_status[resolved_user_id].issue_settings,
    }







# 後で消すであろう部分まとめ
# 心拍データクラス
class TrackedData(BaseModel):
    user_id: str = Field(
        default="01",
        validation_alias=AliasChoices("user_id", "userId", "userID", "id", "participant_id"),
    )
    hr: int = Field(ge=0)
    ibi: List[int] = Field(default_factory=list)
    sentAt: str

    # 心拍・心拍変動を受け取り，保存するパス
@app.post("/api/hr")
async def receive_batch(payload: List[TrackedData], request: Request):
    client_host = request.client.host if request.client else "unknown"
    received_at = datetime.now(ZoneInfo("Asia/Tokyo")).isoformat()

    records = []
    for item in payload:
        user_id = normalize_user_id(item.user_id)
        records.append({
            "user_id": user_id,
            "ex_status": user_status.get(user_id, user_status["01"]).ex_status,
            "sent_at": item.sentAt,
            "received_at": received_at,
            "client_host": client_host,
            "hr": item.hr,
            "ibi": item.ibi[:MAX_IBI_PER_RECORD],
        })
    append_records_by_user(records)

    return {"ok": True, "count": len(payload)}
