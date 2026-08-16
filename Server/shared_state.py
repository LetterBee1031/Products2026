from __future__ import annotations

import csv
from pathlib import Path
from typing import Dict, List

from pydantic import BaseModel, Field


ISSUE_STEP = 0.25
DEFAULT_NUMERIC_OPTIONS: List[float] = [0.0, 0.25, 0.5, 0.75, 1.0]
LEGACY_NON_ISSUE_SUFFIXES = {"taste"}

# このモジュールはサーバ側APIと交渉側コードの共有メモリとして使う。
# user_profile.csv: ユーザごとの好み p_* と重み w_* を持つ。
# issue_option.csv: 論点一覧、選択肢、影響係数 coeff、変えにくさ rho を持つ。

# issue_option.csv が見つからない場合だけ使う後方互換デフォルト。
# 通常は issue_option.csv の issue/options/coeff/rho 列から読み込む。
BUILTIN_DEFAULT_COEFFS: Dict[str, float] = {
    "tempo": +0.10,
    "complexity": +0.14,
    "stimulus": +0.16,
    "feedback": +0.05,
    "guidance": -0.12,
    "break_policy": -0.20,
}
BUILTIN_DEFAULT_RHO: Dict[str, float] = {
    "tempo": 0.5,
    "complexity": 0.6,
    "stimulus": 0.6,
    "feedback": 0.2,
    "guidance": 0.3,
    "break_policy": 0.4,
}


def _default_issue_names() -> list[str]:
    # issue_option.csv がまだ読まれていない起動直後のための初期論点。
    # 実運用では load_issue_settings() 後にCSVの内容へ置き換わる。
    return [
        "tempo",
        "guidance",
        "complexity",
        "stimulus",
        "break_policy",
        "feedback",
    ]


def _default_issue_options() -> Dict[str, List[float]]:
    # すべての初期論点に共通の 0.25 刻み選択肢を割り当てる。
    return {issue: list(DEFAULT_NUMERIC_OPTIONS) for issue in _default_issue_names()}


def _default_values(value: float) -> Dict[str, float]:
    # DEFAULT_ISSUE_SETTINGS や DEFAULT_P を現在の論点セットから作る。
    return {issue: value for issue in ISSUE_OPTIONS}


def _default_weights() -> Dict[str, float]:
    # ユーザCSVに w_* が無い場合は、論点数で均等割りした重みを使う。
    if not ISSUE_OPTIONS:
        return {}
    weight = 1.0 / len(ISSUE_OPTIONS)
    return {issue: weight for issue in ISSUE_OPTIONS}


ISSUE_OPTIONS: Dict[str, List[float]] = _default_issue_options()
DEFAULT_ISSUE_SETTINGS: Dict[str, float] = _default_values(0.5)
DEFAULT_P: Dict[str, float] = _default_values(0.5)
DEFAULT_W: Dict[str, float] = _default_weights()
DEFAULT_COEFFS: Dict[str, float] = dict(BUILTIN_DEFAULT_COEFFS)
DEFAULT_RHO: Dict[str, float] = dict(BUILTIN_DEFAULT_RHO)


def _is_issue_suffix(name: str) -> bool:
    # 旧CSVに残っている p_taste / w_taste を論点として扱わないためのフィルタ。
    return bool(name) and name not in LEGACY_NON_ISSUE_SUFFIXES


def _parse_options(raw: str) -> list[float]:
    # options_tempo のような列では "0,0.25,0.5,0.75,1" の形式を想定する。
    # 区切りは CSV 内で扱いやすいように | や ; も許可している。
    values = [
        part.strip()
        for part in raw.replace("|", ",").replace(";", ",").split(",")
        if part.strip()
    ]
    if not values:
        return list(DEFAULT_NUMERIC_OPTIONS)
    return sorted({float(value) for value in values})


def _nearest_option(issue: str, value: float) -> float:
    # APIやCSVで 0.51 のような値が来ても、交渉空間に存在する値へ丸める。
    options = ISSUE_OPTIONS.get(issue, DEFAULT_NUMERIC_OPTIONS)
    return min(options, key=lambda option: abs(float(option) - float(value)))


def _refresh_defaults(issue_names: list[str]) -> None:
    # CSVから推定された論点セットに合わせて、共有状態のデフォルト値を作り直す。
    # dict自体を再代入せず clear/update することで、import済み参照をなるべく保つ。
    ISSUE_OPTIONS.clear()
    for issue in issue_names:
        ISSUE_OPTIONS[issue] = list(DEFAULT_NUMERIC_OPTIONS)

    DEFAULT_ISSUE_SETTINGS.clear()
    DEFAULT_ISSUE_SETTINGS.update({issue: _nearest_option(issue, 0.5) for issue in ISSUE_OPTIONS})

    DEFAULT_P.clear()
    DEFAULT_P.update({issue: _nearest_option(issue, 0.5) for issue in ISSUE_OPTIONS})

    DEFAULT_W.clear()
    DEFAULT_W.update(_default_weights())

    DEFAULT_COEFFS.clear()
    DEFAULT_COEFFS.update(
        {issue: float(BUILTIN_DEFAULT_COEFFS.get(issue, 0.0)) for issue in ISSUE_OPTIONS}
    )

    DEFAULT_RHO.clear()
    DEFAULT_RHO.update(
        {issue: float(BUILTIN_DEFAULT_RHO.get(issue, 0.0)) for issue in ISSUE_OPTIONS}
    )


def _first_value(row: dict[str, str], *keys: str) -> str:
    # 列名の揺れを吸収する小さなヘルパー。
    # 例: coeff / coeffs / a / a_coeff のどれでも同じ意味として読める。
    for key in keys:
        raw = (row.get(key) or "").strip()
        if raw:
            return raw
    return ""


def load_issue_settings(csv_path: str | Path) -> None:
    """Load ISSUE_OPTIONS, DEFAULT_COEFFS, and DEFAULT_RHO from issue_option.csv.

    Expected columns:
      - issue: issue name
      - options: numeric choices such as "0,0.25,0.5,0.75,1.0"
      - coeff: load effect coefficient
      - rho: change difficulty
    """
    csv_path = Path(csv_path)
    if not csv_path.exists():
        raise FileNotFoundError(f"issue_option.csv not found: {csv_path}")

    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        rows = list(reader)

    issue_names: list[str] = []
    for row in rows:
        issue = _first_value(row, "issue", "issue_name", "name")
        if _is_issue_suffix(issue):
            issue_names.append(issue)

    if not issue_names:
        raise ValueError("issue_option.csv must contain at least one issue row")

    _refresh_defaults(issue_names)

    for row in rows:
        # 各行が1論点を表す。issue名をキーに、選択肢・係数・rhoを反映する。
        issue = _first_value(row, "issue", "issue_name", "name")
        if issue not in ISSUE_OPTIONS:
            continue

        options = _first_value(row, "options", "issue_options", "values")
        if options:
            ISSUE_OPTIONS[issue] = _parse_options(options)

        coeff = _first_value(row, "coeff", "coeffs", "a", "a_coeff")
        if coeff:
            DEFAULT_COEFFS[issue] = float(coeff)

        rho = _first_value(row, "rho", "rho_change")
        if rho:
            DEFAULT_RHO[issue] = float(rho)

    DEFAULT_ISSUE_SETTINGS.update(
        {issue: _nearest_option(issue, 0.5) for issue in ISSUE_OPTIONS}
    )
    # ユーザCSV側で p_* が欠けている場合の理想値も、更新済み選択肢に合わせる。
    DEFAULT_P.update({issue: _nearest_option(issue, 0.5) for issue in ISSUE_OPTIONS})


class userData(BaseModel):
    # 各ユーザが持つ状態。論点セットはCSV読込時に動的に差し替わる。
    name: str = "None"
    ex_status: str = "None"
    block_id: str = "None"
    cl_condition: str = "None"
    p: Dict[str, float] = Field(default_factory=lambda: dict(DEFAULT_P))
    w: Dict[str, float] = Field(default_factory=lambda: dict(DEFAULT_W))
    issue_settings: Dict[str, float] = Field(
        default_factory=lambda: dict(DEFAULT_ISSUE_SETTINGS)
    )

    # Legacy attributes kept so older callers can still access them. They are
    # no longer used by negotiation logic.
    p_taste: str = ""
    w_taste: float = 0.0

    def reset_dynamic_defaults(self) -> None:
        # issue_option.csv を読み直した後、既存ユーザにも新しい論点セットを反映する。
        self.p = dict(DEFAULT_P)
        self.w = dict(DEFAULT_W)
        self.issue_settings = dict(DEFAULT_ISSUE_SETTINGS)

    def load_profile_row(self, row: Dict[str, str]) -> None:
        # user_profile.csv の p_論点 / w_論点 だけを読む。
        # coeff と rho は issue_option.csv 側の責務なので、ここでは扱わない。
        for issue in ISSUE_OPTIONS:
            p_key = f"p_{issue}"
            w_key = f"w_{issue}"
            if row.get(p_key, "") != "":
                self.p[issue] = _nearest_option(issue, float(row[p_key]))
            if row.get(w_key, "") != "":
                self.w[issue] = float(row[w_key])


user_status: Dict[str, userData] = {
    # 初期ユーザ。CSVに別IDがあれば load_user_profiles() で追加される。
    "01": userData(),
    "02": userData(),
    "03": userData(),
}


def load_user_profiles(csv_path: str | Path, *, create_missing_users: bool = True) -> None:
    # 外部からは基本的にこの関数だけ呼べばよい。
    # 内部で issue_option.csv -> user_profile.csv の順に読み込み、共有状態を更新する。
    csv_path = Path(csv_path)
    if not csv_path.exists():
        raise FileNotFoundError(f"user_profile.csv not found: {csv_path}")

    # ユーザプロファイルを読む前に、論点定義・係数・rho を別CSVから読む。
    # ファイル名は指定どおり "issue_option.csv"。
    issue_csv_path = csv_path.with_name("issue_option.csv")
    load_issue_settings(issue_csv_path)

    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if reader.fieldnames is None or "user_id" not in reader.fieldnames:
            raise ValueError("CSV must contain a 'user_id' column")
        rows = list(reader)

    for user in user_status.values():
        # 既存ユーザも、論点定義が変わった場合に古いキーを残さないよう初期化する。
        user.reset_dynamic_defaults()

    for row in rows:
        user_id = (row.get("user_id") or "").strip()
        if not user_id:
            continue

        if user_id not in user_status:
            if not create_missing_users:
                continue
            user_status[user_id] = userData()
        else:
            user_status[user_id].reset_dynamic_defaults()

        user_status[user_id].load_profile_row(row)


def get_user_issue_settings(user_id: str = "01") -> Dict[str, float]:
    # API応答用にコピーを返し、呼び出し側が共有状態を直接壊さないようにする。
    user = user_status.get(user_id)
    if user is None:
        raise KeyError(f"unknown user id: {user_id}")
    return dict(user.issue_settings)


def update_user_issue_settings(user_id: str, issue_settings: Dict[str, float]) -> Dict[str, float]:
    # 交渉後の設定更新入口。値は必ず現在の ISSUE_OPTIONS に丸めて保存する。
    user = user_status.get(user_id)
    if user is None:
        raise KeyError(f"unknown user id: {user_id}")
    user.issue_settings = {
        issue: _nearest_option(issue, float(value))
        for issue, value in issue_settings.items()
        if issue in ISSUE_OPTIONS
    }
    return dict(user.issue_settings)


def get_user_profile(user_id: str = "01") -> Dict[str, object]:
    # デバッグや別モジュールからの参照用に、ユーザ設定とグローバル係数をまとめて返す。
    user = user_status.get(user_id)
    if user is None:
        raise KeyError(f"unknown user id: {user_id}")
    return {
        "user_id": user_id,
        "p": dict(user.p),
        "w": dict(user.w),
        "coeffs": dict(DEFAULT_COEFFS),
        "rho": dict(DEFAULT_RHO),
    }
