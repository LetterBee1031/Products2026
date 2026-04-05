from __future__ import annotations

from typing import Dict, List

from pydantic import BaseModel, Field

# 交渉とサーバの両方で共有する論点の選択肢。
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


# `server2.py` と `TestNegotiation1.py` が同じ状態を参照するための共有オブジェクト。
user_status: Dict[str, userData] = {
    "01": userData(),
    "02": userData(),
    "03": userData(),
}


def get_user_issue_settings(user_id: str = "01") -> Dict[str, str]:
    user = user_status.get(user_id)
    if user is None:
        raise KeyError(f"unknown user id: {user_id}")
    return dict(user.issue_settings)


def update_user_issue_settings(user_id: str, issue_settings: Dict[str, str]) -> Dict[str, str]:
    user = user_status.get(user_id)
    if user is None:
        raise KeyError(f"unknown user id: {user_id}")
    user.issue_settings = dict(issue_settings)
    return dict(user.issue_settings)
