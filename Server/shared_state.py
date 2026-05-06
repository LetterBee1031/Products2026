from __future__ import annotations

import csv
from pathlib import Path
from typing import Dict, List, Optional

from pydantic import BaseModel, Field, field_validator

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

# ユーザプロファイル（順序あり論点の理想値p/重みw + テイストp_taste/w_taste）
DEFAULT_P: Dict[str, float] = {
    "tempo": 0.5,
    "guidance": 0.5,
    "complexity": 0.5,
    "stimulus": 0.5,
    "break_policy": 0.5,
    "feedback": 0.5,
}
DEFAULT_W: Dict[str, float] = {
    "tempo": 1 / 6,
    "guidance": 1 / 6,
    "complexity": 1 / 6,
    "stimulus": 1 / 6,
    "break_policy": 1 / 6,
    "feedback": 1 / 6,
}


# ユーザデータクラス
class userData(BaseModel):
    """サーバと交渉で共有するユーザ状態。

    user_profile.csv を読み込んで下記を埋めることができる：
    - p: 順序あり論点の理想値（0〜1）
    - w: 順序あり論点の重み
    - p_taste: テイスト（カテゴリ）
    - w_taste: テイスト重み
    """

    name: str = "None"
    ex_status: str = "None"
    cl_condition: str = "None"

    # 順序あり論点の理想値 / 重み
    p: Dict[str, float] = Field(default_factory=lambda: dict(DEFAULT_P))
    w: Dict[str, float] = Field(default_factory=lambda: dict(DEFAULT_W))

    # テイスト（順序なしカテゴリ）
    p_taste: str = "polite"
    w_taste: float = 0.10

    # 現在の体験設定（交渉結果で更新される）
    issue_settings: Dict[str, str] = Field(
        default_factory=lambda: DEFAULT_ISSUE_SETTINGS.copy()
    )

    @field_validator("p_taste")
    @classmethod
    def _validate_taste(cls, v: str) -> str:
        if v not in ISSUE_OPTIONS["taste"]:
            raise ValueError(f"invalid taste: {v!r}. allowed={ISSUE_OPTIONS['taste']}")
        return v

    def load_profile_row(self, row: Dict[str, str]) -> None:
        """user_profile.csv の1行（DictReaderのrow）からプロファイルをロードする。"""

        # p_*, w_* を更新（存在する列だけ反映）
        for key in DEFAULT_P.keys():
            pk = f"p_{key}"
            wk = f"w_{key}"
            if pk in row and row[pk] != "":
                self.p[key] = float(row[pk])
            if wk in row and row[wk] != "":
                self.w[key] = float(row[wk])

        # taste
        if "p_taste" in row and row["p_taste"] != "":
            self.p_taste = row["p_taste"]
        if "w_taste" in row and row["w_taste"] != "":
            self.w_taste = float(row["w_taste"])


# `server2.py` と `TestNegotiation1.py` が同じ状態を参照するための共有オブジェクト。
# 既存IDは残しつつ、CSVロードで追加・更新できる。
user_status: Dict[str, userData] = {
    "01": userData(),
    "02": userData(),
    "03": userData(),
}


def load_user_profiles(csv_path: str | Path, *, create_missing_users: bool = True) -> None:
    """user_profile.csv を読み込み user_status を更新する。

    CSV必須列:
      - user_id
    CSV任意列:
      - p_tempo, ..., p_feedback
      - w_tempo, ..., w_feedback
      - p_taste, w_taste

    例:
      user_id,p_tempo,...,p_taste,w_tempo,...,w_taste
      U001,0.5,...,polite,0.15,...,0.10
    """
    csv_path = Path(csv_path)
    if not csv_path.exists():
        raise FileNotFoundError(f"user_profile.csv not found: {csv_path}")

    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if reader.fieldnames is None or "user_id" not in reader.fieldnames:
            raise ValueError("CSV must contain a 'user_id' column")
        for row in reader:
            user_id = (row.get("user_id") or "").strip()
            if not user_id:
                continue

            if user_id not in user_status:
                if not create_missing_users:
                    continue
                user_status[user_id] = userData()

            user_status[user_id].load_profile_row(row)


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


def get_user_profile(user_id: str = "01") -> Dict[str, object]:
    """交渉側などで使いやすいように、プロファイルをdictで返す。"""
    user = user_status.get(user_id)
    if user is None:
        raise KeyError(f"unknown user id: {user_id}")
    return {
        "user_id": user_id,
        "p": dict(user.p),
        "w": dict(user.w),
        "p_taste": user.p_taste,
        "w_taste": float(user.w_taste),
    }
