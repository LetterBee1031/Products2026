"""
NegMAS を用いた PA/AA 2者・多論点・交互提案（SAO）自動交渉のコード（改訂版）

この版で反映した点（前の最終形に対応）：
- PA効用を「好み + 負荷適正」の混合に変更
    U_PA(x) = (1-λ_L) U_PA^pref(x) + λ_L U_PA^load(x)
    U_PA^load(x) = exp(-η d_out(x))  （帯外れを罰する）
- AAの提案選択は、(U_AA + λ U_PA) を最大化（U_PA は上記の新定義）
- PAの受諾条件：
    (1) 予測負荷が予測帯に入る（ハード）
    (2) U_PA が閾値以上（ソフト）
- 予測負荷はルールベース（デルタ）：
    L_pred(x)=clip(L_obs + Σ a_i (z_i(offer)-z_i(current)), 0,1)
- 観測閾値と予測閾値は分離（安全マージンΔm）：
    L_pred_low = L_obs_low + margin
    L_pred_high= L_obs_high - margin

注意：
- ここでは離散空間（3段階×複数論点）を想定し、AAは候補列挙/サンプルで最良案を提案します。
"""

from __future__ import annotations

from dataclasses import dataclass
from math import exp
from typing import Dict, List, Optional, Sequence, Tuple, Callable, Any

from negmas import ResponseType
from negmas.sao import SAOMechanism, SAONegotiator, SAOState
from negmas.outcomes import make_issue, Outcome


# ----------------------------
# 1) 交渉論点（離散カテゴリ）
# ----------------------------

TEMPO_VALUES = ["slow", "normal", "fast"]
LEVEL3_VALUES = ["low", "medium", "high"]
BREAK_VALUES = ["rare", "on_demand", "frequent"]
FEEDBACK_VALUES = ["summary", "brief_immediate", "detailed_immediate"]

# 順序なしカテゴリ（文章テイスト）
TASTE_VALUES = ["polite", "concise", "encouraging", "neutral"]

# 順序あり論点を 0〜1 に写像（距離や差分計算のため）
ORDINAL_MAPS: Dict[str, Dict[str, float]] = {
    "tempo": {"slow": 0.0, "normal": 0.5, "fast": 1.0},
    "guidance": {"low": 0.0, "medium": 0.5, "high": 1.0},
    "complexity": {"low": 0.0, "medium": 0.5, "high": 1.0},
    "stimulus": {"low": 0.0, "medium": 0.5, "high": 1.0},
    "break_policy": {"rare": 0.0, "on_demand": 0.5, "frequent": 1.0},
    "feedback": {"summary": 0.0, "brief_immediate": 0.5, "detailed_immediate": 1.0},
}

# テイスト類似度行列 K（例。研究設計/予備実験で再設定推奨）
TASTE_SIMILARITY: Dict[str, Dict[str, float]] = {
    "polite": {"polite": 1.0, "concise": 0.4, "encouraging": 0.7, "neutral": 0.6},
    "concise": {"polite": 0.4, "concise": 1.0, "encouraging": 0.3, "neutral": 0.6},
    "encouraging": {"polite": 0.7, "concise": 0.3, "encouraging": 1.0, "neutral": 0.6},
    "neutral": {"polite": 0.6, "concise": 0.6, "encouraging": 0.6, "neutral": 1.0},
}


# ----------------------------------------
# 2) 観測/予測 閾値（安全マージン込み）
# ----------------------------------------

@dataclass
class Thresholds:
    """CLE（観測）閾値と、AAルール予測で使う予測帯（マージン込み）を管理する。"""
    L_obs_low: float
    L_obs_high: float
    margin: float = 0.10 # 仮置きのマージン　調整後の負荷がしっかり最適帯に入るようにということで

    # Low + margin で低負荷帯を高めに設定
    @property
    def L_pred_low(self) -> float:
        return self.L_obs_low + self.margin

    # High - margin で高負荷帯を低めに設定
    @property
    def L_pred_high(self) -> float:
        return self.L_obs_high - self.margin


# ----------------------------------------
# 3) ルールベース予測（Δモデル）
# ----------------------------------------

@dataclass
class RuleBasedLoadModel:
    """
    予測負荷：
      L_pred(x) = clip(L_current + Σ a_i (z_i(offer)-z_i(current)), 0, 1)

    a_coeffs の符号：
      +：値を上げると負荷が上がる（tempo, complexity, stimulus 等）
      -：値を上げると負荷が下がる（guidance, break_policy 等）
    """
    a_coeffs: Dict[str, float] # 各論点の体験への影響係数a_iを格納する辞書

    # 予測負荷帯の計算
    def predict(self, L_current: float, current_setting: Dict[str, str], offer: Dict[str, str]) -> float:
        delta = 0.0
        for issue, a in self.a_coeffs.items():
            z_offer = ORDINAL_MAPS[issue][offer[issue]] # 提案の体験設定
            z_curr = ORDINAL_MAPS[issue][current_setting[issue]] # 現在の体験設定
            delta += a * (z_offer - z_curr) # 影響係数 × 変更後の差分

        # テイストは順序なしなので、デフォルトでは負荷予測に入れない
        return max(0.0, min(1.0, L_current + delta))

# -------------------------
# 4) 便利関数（帯外れ・中心距離・変更コスト）
# -------------------------

def d_out(L_pred: float, low: float, high: float) -> float:
    """帯外れ量（ヒンジ）：帯内なら0"""
    return max(0.0, low - L_pred) + max(0.0, L_pred - high)

def d_in(L_pred: float, low: float, high: float) -> float:
    """帯内中心距離：中心からの距離（任意）"""
    L_star = (low + high) / 2.0
    return abs(L_pred - L_star)

# ローは変更コストらしいで
def change_cost(current_setting: Dict[str, str], offer: Dict[str, str], rho: Dict[str, float]) -> float:
    """急変抑制：順序あり論点の変化量の重み付き和"""
    c = 0.0
    for issue, r in rho.items():
        z_offer = ORDINAL_MAPS[issue][offer[issue]]
        z_curr = ORDINAL_MAPS[issue][current_setting[issue]]
        c += r * abs(z_offer - z_curr)
    return c


# -------------------------
# 5) PA効用（改訂：好み + 負荷適正）
# -------------------------

@dataclass
class PAProfile:
    """
    lambda_Lとetaの違いについて
    lambda_Lは効用内においてどれだけ負荷適正を重視するか（全体への影響）
    etaはU_PA^loadをどれだけ厳しくするか（単体での厳しさ）
    みたいな
    """

    p: Dict[str, float] # 順序あり論点の理想値（0〜1）
    w: Dict[str, float] # 順序あり論点の重み（合計 <= 1 推奨）
    preferred_taste: str # 好みテイスト（カテゴリ）
    w_taste: float = 0.10 # テイスト重み
    tau_accept: float = 0.60 # PA受諾の下限（新しい U_PA に対して適用）

    tau_min: float = 0.30 # 締め切り時のPA受諾下限（tau_acceptからここまで下がる）

    lambda_L: float = 0.30 # PA効用に占める「負荷適正」重み（0〜1）
    eta: float = 6.0 # U_PA^load = exp(-eta * d_out) の強さ

def s_ordinal(z: float, p: float) -> float:
    """s_i(x)=1-|z_i(x)-p_i|"""
    return 1.0 - abs(z - p)

def s_taste(taste: str, preferred: str) -> float:
    """順序なしカテゴリは類似度行列で評価"""
    return TASTE_SIMILARITY[taste][preferred]

def U_PA_pref(offer: Dict[str, str], profile: PAProfile) -> float:
    """好み効用（順序あり+テイスト）"""
    u = 0.0
    for issue, w in profile.w.items():
        z = ORDINAL_MAPS[issue][offer[issue]] # 各論点の提案内容
        u += w * s_ordinal(z, profile.p[issue]) # 重み × |(提案内容-好みの内容)|

    u += profile.w_taste * s_taste(offer["taste"], profile.preferred_taste) # 文章テイストのやつ足してる
    return max(0.0, min(1.0, u))

def U_PA_load(L_pred: float, thresholds: Thresholds, profile: PAProfile) -> float:
    """負荷適正効用：帯外れを指数で罰する"""
    out = d_out(L_pred, thresholds.L_pred_low, thresholds.L_pred_high)
    return exp(-profile.eta * out)

def U_PA(
    offer: Dict[str, str],
    *,
    L_current: float,
    current_setting: Dict[str, str],
    load_model: RuleBasedLoadModel,
    thresholds: Thresholds,
    profile: PAProfile,
) -> Tuple[float, float]:
    """
    新しい PA 効用：
      U_PA = (1-λ_L) U_pref + λ_L U_load
    戻り値： (U_PA, L_pred)
    """
    L_pred = load_model.predict(L_current, current_setting, offer) # 予測負荷
    u_pref = U_PA_pref(offer, profile) # 好み効用計算
    u_load = U_PA_load(L_pred, thresholds, profile) # 負荷効用計算
    lamL = max(0.0, min(1.0, profile.lambda_L)) # 負荷に関する重みを丸めてるだけ
    u = (1.0 - lamL) * u_pref + lamL * u_load # 効用の計算
    return max(0.0, min(1.0, u)), L_pred


# -------------------------
# 6) AA効用
# -------------------------

@dataclass
class AAParams:
    alpha: float = 10.0  # 帯外れ罰
    beta: float = 2.0    # 中心志向
    gamma: float = 1.0   # 変更抑制
    lam: float = 0.25    # AAがPA効用をどれだけ尊重するか（交渉提案選択用）


def U_AA(
    offer: Dict[str, str],
    *,
    L_current: float,
    current_setting: Dict[str, str],
    load_model: RuleBasedLoadModel,
    thresholds: Thresholds,
    rho: Dict[str, float],
    params: AAParams,
) -> Tuple[float, float]:
    """
    AA効用：
      U_AA = exp(-α d_out) * exp(-β d_in) * exp(-γ c)
    戻り値： (U_AA, L_pred)
    """
    L_pred = load_model.predict(L_current, current_setting, offer) # 負荷予測
    out = d_out(L_pred, thresholds.L_pred_low, thresholds.L_pred_high) # 帯外れ値計算
    inn = d_in(L_pred, thresholds.L_pred_low, thresholds.L_pred_high) # 中心志向計算
    c = change_cost(current_setting, offer, rho) # 変更コスト計算
    u = exp(-params.alpha * out) * exp(-params.beta * inn) * exp(-params.gamma * c) # 効用計算
    return u, L_pred


# ------------------------------------
# 7) Outcome ↔ dict 変換
# ------------------------------------

# negmasの交渉結果を扱いやすくしたい
# Outcome（交渉結果）がタプルだったり辞書だったりするから，全部辞書にしちまおうという魂胆
def outcome_to_dict(outcome: Outcome, issue_names: Sequence[str]) -> Dict[str, str]:
    if isinstance(outcome, dict): # dictionaryになってるかどうか
        return outcome
    return {name: outcome[i] for i, name in enumerate(issue_names)}

def dict_to_outcome(d: Dict[str, str], issue_names: Sequence[str]) -> Tuple[Any, ...]:
    return tuple(d[name] for name in issue_names)


# ------------------------------------
# 8) CPA(k)（PAが返す許容集合 Ω_i(k)）
# ------------------------------------

@dataclass
class PAConstraints:
    # PAが許容する提案集合
    # set型は同じ要素が入らず，順番が関係ない集合の変数らしい
    allowed_values: Dict[str, set]

    # 提案がallowedに入ってなかったらFalse
    def allows(self, offer: Dict[str, str]) -> bool:
        for issue, allowed in self.allowed_values.items():
            if offer[issue] not in allowed:
                return False
        return True

# ------------------------------------
# 9) PA（受諾/拒否/制約更新）
# ------------------------------------

# PAの交渉者
class PlayerAgentPA(SAONegotiator):
    """
    PAの受諾条件（最終形）：
      (1) L_pred(x) が予測帯に入る（ハード）
      (2) U_PA(x) >= tau_accept（ソフト、U_PA は「好み+負荷適正」）

    拒否したら：重要論点のみ許容集合 Ω を絞る（CPA(k)更新）
    """

    def __init__(
        self,
        *,
        name: str, # 交渉者ID
        issue_names: Sequence[str], # 論点名
        profile: PAProfile, # ユーザプロファイル
        thresholds: Thresholds, # 閾値情報
        load_model: RuleBasedLoadModel, # 負荷予測モデル(計算)
        initial_setting: Dict[str, str], # 体験設定の初期設定
        L_current: float, # 現在の体験状態
    ):
        super().__init__(name=name)
        self.issue_names = list(issue_names)
        self.profile = profile
        self.thresholds = thresholds
        self.load_model = load_model

        self.current_setting = dict(initial_setting)
        self.L_current = float(L_current)

        # AAインスタンス参照（run_exampleでセットする）
        self.aa_ref: Optional["AdjustmentAgentAA"] = None

        # 緩和レベル（方針1：候補ゼロのときだけ緩める）
        self.relax_level: int = 0
        self.max_relax_level: int = 2

        # 体験制約の設定
        # ※方針1に合わせて「最初は好みを守る」ため、重要論点は厳しめ（理想に近い値）から開始する
        self.constraints = PAConstraints(
            allowed_values={
                "tempo": set(TEMPO_VALUES),
                "guidance": set(LEVEL3_VALUES),
                "complexity": set(LEVEL3_VALUES),
                "stimulus": set(LEVEL3_VALUES),
                "break_policy": set(BREAK_VALUES),
                "feedback": set(FEEDBACK_VALUES),
                "taste": set(TASTE_VALUES),
}
        )

    # 提案
    def propose(self, state: SAOState) -> Outcome:
        """
        SAOでは自分の番で提案が必要になるため、
        制約内で理想点に最も近い案を（簡易に）返す。
        """
        offer = {
            "tempo": self._closest_allowed("tempo", self.profile.p["tempo"]),
            "guidance": self._closest_allowed("guidance", self.profile.p["guidance"]),
            "complexity": self._closest_allowed("complexity", self.profile.p["complexity"]),
            "stimulus": self._closest_allowed("stimulus", self.profile.p["stimulus"]),
            "break_policy": self._closest_allowed("break_policy", self.profile.p["break_policy"]),
            "feedback": self._closest_allowed("feedback", self.profile.p["feedback"]),
            "taste": self.profile.preferred_taste
            if self.profile.preferred_taste in self.constraints.allowed_values["taste"]
            else next(iter(self.constraints.allowed_values["taste"])), # iter: イテレータを作る関数．許容集合内に好みのテイストがなかった時のリスクヘッジで追加
        }
        return dict_to_outcome(offer, self.issue_names)

    # 提案に対する応答
    def respond(self, state: SAOState, source: str | None = None) -> ResponseType:
        # 提案がなかったら却下
        offer = state.current_offer
        if offer is None:
            return ResponseType.REJECT_OFFER
        # ディクショナリに変換
        offer_dict = outcome_to_dict(offer, self.issue_names)

        # ① PA制約（Ω）チェック
        if not self.constraints.allows(offer_dict):
            # ※方針1では「候補ゼロのときだけ緩める」ので、ここでは単に拒否する
            return ResponseType.REJECT_OFFER

        # ② U_PA(x) を計算（ここで L_pred も同時に得る）
        u_pa, L_pred = U_PA(
            offer_dict,
            L_current=self.L_current,
            current_setting=self.current_setting,
            load_model=self.load_model,
            thresholds=self.thresholds,
            profile=self.profile,
        )

        # ③ ハード条件：予測帯に入ること
        load_ok = (self.thresholds.L_pred_low <= L_pred <= self.thresholds.L_pred_high)

        # ④ ソフト条件：U_PA >= tau_accept（締め切りが近づくほど tau_accept を下げる）
        tau_now = self._tau_accept_now(state)
        if load_ok and (u_pa >= tau_now):
            # 合意：内部状態更新（次回の予測基準が変わる）
            self.current_setting = dict(offer_dict)
            self.L_current = L_pred
            return ResponseType.ACCEPT_OFFER

        # 拒否：CPA(k)更新（Ωを絞る）
        # ※方針1では「帯内候補ゼロ」のときにのみ緩めるので、ここでは拒否のみ
        return ResponseType.REJECT_OFFER


    def _tau_accept_now(self, state: SAOState) -> float:
        """交渉ステップのみから進行度を計算し、tau_accept を線形に下げる（TimeBasedConceding系の簡易版）。

        - progress = step / (n_steps-1)
        - progress=0 で tau_accept、progress=1 で tau_min
        """
        tau0 = float(self.profile.tau_accept)
        tau_min = float(getattr(self.profile, "tau_min", tau0))

        # step と n_steps のみを使って progress ∈ [0,1] を計算
        step = None
        for attr in ("step", "current_step", "k", "round"):
            if hasattr(state, attr):
                try:
                    step = int(getattr(state, attr))
                    break
                except Exception:
                    pass

        n_steps_calc = None
        for attr in ("n_steps", "max_steps"):
            if hasattr(state, attr):
                try:
                    n_steps_calc = int(getattr(state, attr))
                    break
                except Exception:
                    pass

        if n_steps_calc is None:
            # mechanism側にあることも多い
            for attr in ("n_steps", "max_steps"):
                if hasattr(self.nmi, attr):
                    try:
                        n_steps_calc = int(getattr(self.nmi, attr))
                        break
                    except Exception:
                        pass

        if step is None or n_steps_calc is None or n_steps_calc <= 0:
            progress = 0.0
        else:
            denom = (n_steps_calc - 1) if n_steps_calc > 1 else 1
            progress = max(0.0, min(1.0, step / denom))

        # 線形に譲歩：progress=0でtau0、progress=1でtau_min
        tau_now = (1.0 - progress) * tau0 + progress * tau_min
        return float(max(0.0, min(1.0, tau_now)))

    # 許容された提案候補の中から，理想に近いものをとる関数．提案生成に使用
    def _closest_allowed(self, issue: str, p: float) -> str:
        candidates = list(self.constraints.allowed_values[issue])
        return min(candidates, key=lambda v: abs(ORDINAL_MAPS[issue][v] - p))

    # 全提案候補の中から，理想に近いものをとる関数．
    def _closest_allowed_from_set(self, issue: str, candidates: set, p: float) -> str:
        cand = list(candidates)
        return min(cand, key=lambda v: abs(ORDINAL_MAPS[issue][v] - p))

    # 論点を絞る関数
    def _tighten_constraints(self) -> None:
        """
        CPA(k) 更新例：
        - 交渉が詰まらないよう、重要論点だけを絞る
        - 各論点は理想値に近い上位2候補を残す
        - テイストは {preferred, neutral} に絞る
        """
        important = ["tempo", "guidance", "complexity", "stimulus", "taste"]

        for issue in important:
            # テイスト部分に関して
            if issue == "taste":
                pref = self.profile.preferred_taste
                allowed = {pref}
                if "neutral" in TASTE_VALUES:
                    allowed.add("neutral")
                self.constraints.allowed_values["taste"] = self.constraints.allowed_values["taste"].intersection(allowed) # intersectionは積集合を返す関数らしい
                continue
            # その他の論点について
            p = self.profile.p[issue]
            vals = sorted(
                list(self.constraints.allowed_values[issue]),
                key=lambda v: abs(ORDINAL_MAPS[issue][v] - p),
            )
            self.constraints.allowed_values[issue] = set(vals[:2]) if len(vals) >= 2 else set(vals)

        # 空集合になったら保険で全許容へ戻す
        for issue, allowed in self.constraints.allowed_values.items():
            if allowed:
                continue
            if issue == "tempo":
                self.constraints.allowed_values[issue] = set(TEMPO_VALUES)
            elif issue == "break_policy":
                self.constraints.allowed_values[issue] = set(BREAK_VALUES)
            elif issue == "feedback":
                self.constraints.allowed_values[issue] = set(FEEDBACK_VALUES)
            elif issue == "taste":
                self.constraints.allowed_values[issue] = set(TASTE_VALUES)
            else:
                self.constraints.allowed_values[issue] = set(LEVEL3_VALUES)

    # 方針1：候補が存在しないときにだけ、許容集合Ωを緩める
    def _relax_constraints_once(self) -> None:
        """候補が存在しないときにだけ、許容集合Ωを1段階だけ広げる。"""
        if self.relax_level >= self.max_relax_level:
            return

        self.relax_level += 1

        # 緩和対象（重要論点だけ）
        important = ["tempo", "guidance", "complexity", "stimulus", "taste"]

        for issue in important:
            # テイスト部分に関して
            if issue == "taste":
                pref = self.profile.preferred_taste
                if self.relax_level == 1:
                    allowed = {pref}
                    if "neutral" in TASTE_VALUES:
                        allowed.add("neutral")
                    self.constraints.allowed_values["taste"] = self.constraints.allowed_values["taste"].union(allowed)
                else:
                    self.constraints.allowed_values["taste"] = set(TASTE_VALUES)
                continue

            # 順序あり：理想値に近い順に全候補を並べ、上位Nを許可
            p = self.profile.p[issue]
            all_vals = sorted(list(ORDINAL_MAPS[issue].keys()), key=lambda v: abs(ORDINAL_MAPS[issue][v] - p))

            if self.relax_level == 1:
                self.constraints.allowed_values[issue] = set(all_vals[:2])  # 2候補に拡張
            else:
                self.constraints.allowed_values[issue] = set(all_vals)      # 全許容


# ------------------------------------
# 10) AA（候補探索→最良提案）
# ------------------------------------

class AdjustmentAgentAA(SAONegotiator):
    """
    AAの提案選択（最終形）：
      x^(k) = argmax_x ( U_AA(x) + λ U_PA(x) )
      s.t. x は PA制約（Ω）を満たす

    実装では安定化のため、予測帯に入る候補のみ評価（ハードゲート）する。
    """

    def __init__(
        self,
        *,
        name: str, # 交渉者AAのID
        issue_names: Sequence[str], # 論点集．シーケンス型にしてるのは対応力上げるため？
        thresholds: Thresholds, # 閾値
        load_model: RuleBasedLoadModel, # 負荷推定モデル
        pa_profile: PAProfile, # PAのプロファイル　提案時にPAの選好も考慮するため
        aa_params: AAParams, # 3つの関数の影響の大きさ（重みではない）
        rho_change: Dict[str, float], # 論点に対する重み（ユーザ定義）
        initial_setting: Dict[str, str], # 初期設定
        L_current: float, # 現在の負荷
        get_pa_constraints: Callable[[], PAConstraints], # PAの制約．引数を取らずに呼び出せて、戻り値として PAConstraints を返す関数（または関数のように呼べるもの） Callableに関しては後で調べたい
        max_candidates: int = 3000, # 評価する最大交渉案数
    ):
        super().__init__(name=name)
        self.issue_names = list(issue_names)
        self.thresholds = thresholds
        self.load_model = load_model
        self.pa_profile = pa_profile
        self.aa_params = aa_params
        self.rho_change = rho_change

        self.current_setting = dict(initial_setting)
        self.L_current = float(L_current)

        self.get_pa_constraints = get_pa_constraints
        self.max_candidates = max_candidates
        self._cached_candidates: Optional[List[Outcome]] = None # 提案候補のリストをキャッシュしておく変数

        # 方針1：帯内候補が存在しなかったかをPAに通知するためのフラグ
        self.no_feasible: bool = False

    def on_negotiation_start(self, state: SAOState) -> None:
        super().on_negotiation_start(state)
        os = self.nmi.outcome_space
        self._cached_candidates = list(os.enumerate_or_sample(max_cardinality=self.max_candidates)) # 提案候補の列挙

    # 提案
    def propose(self, state: SAOState) -> Outcome:
        assert self._cached_candidates is not None, "候補が初期化されていません"

        # proposeのたびにリセット（このラウンドの探索結果を表す）
        self.no_feasible = False

        constraints = self.get_pa_constraints()
        best_offer: Optional[Outcome] = None
        best_score = float("-inf")

        for outcome in self._cached_candidates:
            offer_dict = outcome_to_dict(outcome, self.issue_names)

            # ① PA制約（Ω）
            if not constraints.allows(offer_dict):
                continue

            # ② AA効用と予測負荷
            u_aa, L_pred = U_AA(
                offer_dict,
                L_current=self.L_current,
                current_setting=self.current_setting,
                load_model=self.load_model,
                thresholds=self.thresholds,
                rho=self.rho_change,
                params=self.aa_params,
            )

            # ③ ハードゲート：予測帯に戻る案のみ
            if not (self.thresholds.L_pred_low <= L_pred <= self.thresholds.L_pred_high):
                continue

            # ④ 新しい U_PA（好み+負荷）を計算
            u_pa, _ = U_PA(
                offer_dict,
                L_current=self.L_current,
                current_setting=self.current_setting,
                load_model=self.load_model,
                thresholds=self.thresholds,
                profile=self.pa_profile,
            )

            score = u_aa + self.aa_params.lam * u_pa

            if score > best_score:
                best_score = score
                best_offer = outcome

        # フォールバック：帯内案が見つからない場合（負荷状態に応じて分岐）
        if best_offer is None:
            self.no_feasible = True
            fb = dict(self.current_setting)

            # 現在の負荷が「低負荷」か「高負荷」かで、フォールバック方向を変える
            if self.L_current < self.thresholds.L_pred_low:
                # 低負荷 → 負荷を上げる方向（例：休憩減、刺激/複雑さ↑、ガイダンス↓、テンポ↑）
                fb["break_policy"] = "rare"
                fb["guidance"] = "low"
                fb["stimulus"] = "high"
                fb["complexity"] = "high"
                fb["tempo"] = "fast"
                fb["feedback"] = "detailed_immediate"
            else:
                # 高負荷（または不明）→ 負荷を下げる方向（従来どおり）
                fb["break_policy"] = "frequent"
                fb["guidance"] = "high"
                fb["stimulus"] = "low"
                fb["complexity"] = "low"
                fb["tempo"] = "slow"
                fb["feedback"] = "summary"

            best_offer = dict_to_outcome(fb, self.issue_names)

        return best_offer

    def respond(self, state: SAOState, source: str | None = None) -> ResponseType:
        """
        AAは相手案が「自分が次に出す案以上」なら受諾する簡易方針。
        ただし、予測帯（L_pred_low〜L_pred_high）を満たさない案は絶対に受諾しない（ハード条件）。
        """
        offer = state.current_offer
        if offer is None:
            return ResponseType.REJECT_OFFER

        offer_dict = outcome_to_dict(offer, self.issue_names)

        # --- ハード条件：帯外は即REJECT（ここが重要） ---
        L_offer_pred = self.load_model.predict(self.L_current, self.current_setting, offer_dict)
        if not (self.thresholds.L_pred_low <= L_offer_pred <= self.thresholds.L_pred_high):
            return ResponseType.REJECT_OFFER

        # 自分の次案（proposeは帯内ゲート付き）
        my_next = outcome_to_dict(self.propose(state), self.issue_names)

        # offer のスコア
        u_aa_offer, _ = U_AA(
            offer_dict,
            L_current=self.L_current,
            current_setting=self.current_setting,
            load_model=self.load_model,
            thresholds=self.thresholds,
            rho=self.rho_change,
            params=self.aa_params,
        )
        u_pa_offer, _ = U_PA(
            offer_dict,
            L_current=self.L_current,
            current_setting=self.current_setting,
            load_model=self.load_model,
            thresholds=self.thresholds,
            profile=self.pa_profile,
        )
        score_offer = u_aa_offer + self.aa_params.lam * u_pa_offer

        # next のスコア（proposeで帯内のみ返す前提）
        u_aa_next, _ = U_AA(
            my_next,
            L_current=self.L_current,
            current_setting=self.current_setting,
            load_model=self.load_model,
            thresholds=self.thresholds,
            rho=self.rho_change,
            params=self.aa_params,
        )
        u_pa_next, _ = U_PA(
            my_next,
            L_current=self.L_current,
            current_setting=self.current_setting,
            load_model=self.load_model,
            thresholds=self.thresholds,
            profile=self.pa_profile,
        )
        score_next = u_aa_next + self.aa_params.lam * u_pa_next

        if score_offer >= score_next:
            # 受諾：内部状態更新（L_currentも予測値で更新）
            self.current_setting = dict(offer_dict)
            self.L_current = L_offer_pred
            return ResponseType.ACCEPT_OFFER

        return ResponseType.REJECT_OFFER




# -----------------------------
# 11) 実行例
# -----------------------------

def run_example(
    L_current: float,
    current_setting: Dict[str, str],
    pa_preference: Dict[str, float],
    pa_weight: Dict[str, float],
    pa_taste_preference:str,
    pa_taste_weight:float,
    n_steps: int = 20,
    max_candidates: int = 3000,
)-> Optional[Dict[str, str]]:

    thresholds = Thresholds(L_obs_low=0.3, L_obs_high=0.7, margin=0.10)

    a_coeffs = {
        "tempo": +0.10,
        "guidance": -0.12,
        "complexity": +0.14,
        "stimulus": +0.16,
        "break_policy": -0.20,
        "feedback": +0.05,
    }

    rho_change = {
        "tempo": 0.5,
        "guidance": 0.3,
        "complexity": 0.6,
        "stimulus": 0.6,
        "break_policy": 0.4,
        "feedback": 0.2,
    }

    aa_params = AAParams(alpha=10.0, beta=2.0, gamma=1.0, lam=0.25)


    pa_profile = PAProfile(
        p=pa_preference,
        w=pa_weight,
        preferred_taste=pa_taste_preference,
        w_taste=pa_taste_weight,
        tau_accept=0.70,
        tau_min=0.1,
        lambda_L=0.50,
        eta=6.0,
    )

    issues = [
        make_issue(name="tempo", values=TEMPO_VALUES),
        make_issue(name="guidance", values=LEVEL3_VALUES),
        make_issue(name="complexity", values=LEVEL3_VALUES),
        make_issue(name="stimulus", values=LEVEL3_VALUES),
        make_issue(name="break_policy", values=BREAK_VALUES),
        make_issue(name="feedback", values=FEEDBACK_VALUES),
        make_issue(name="taste", values=TASTE_VALUES),
    ]
    issue_names = [i.name for i in issues]

    # 交渉ステップ数を引数化
    session = SAOMechanism(issues=issues, n_steps=n_steps)

    # ルールベース予測係数 a_i を引数で受け取る
    load_model = RuleBasedLoadModel(a_coeffs=a_coeffs)

    pa = PlayerAgentPA(
        name="PA",
        issue_names=issue_names,
        profile=pa_profile,
        thresholds=thresholds,
        load_model=load_model,
        initial_setting=current_setting,
        L_current=L_current,
    )

    aa = AdjustmentAgentAA(
        name="AA",
        issue_names=issue_names,
        thresholds=thresholds,
        load_model=load_model,
        pa_profile=pa_profile,
        aa_params=aa_params,           # 引数化
        rho_change=rho_change,         # 引数化
        initial_setting=current_setting,
        L_current=L_current,
        get_pa_constraints=lambda: pa.constraints,
        max_candidates=max_candidates, # 引数化
    )

    # 方針1：PAがAAの「候補ゼロ」情報を参照できるようにする
    pa.aa_ref = aa

    session.add(pa)
    session.add(aa)

    final_state = session.run()
    agreement = final_state.agreement

    print("合意:", agreement)
    if agreement is not None:
        ag = outcome_to_dict(agreement, issue_names)

        # 変更前/変更後負荷（予測）と差分
        L_before = L_current
        L_after = load_model.predict(L_current, current_setting, ag)
        delta_L = L_after - L_before

        # 効用類
        u_pa, _ = U_PA(
            ag,
            L_current=L_current,
            current_setting=current_setting,
            load_model=load_model,
            thresholds=thresholds,
            profile=pa_profile,
        )
        u_pref = U_PA_pref(ag, pa_profile)
        u_load = U_PA_load(L_after, thresholds, pa_profile)

        print("合意（dict）:", ag)
        print("観測帯:", (thresholds.L_obs_low, thresholds.L_obs_high), " margin=", thresholds.margin)
        print("予測帯:", (thresholds.L_pred_low, thresholds.L_pred_high))
        print("負荷（変更前）:", round(L_before, 3))
        print("負荷（変更後）:", round(L_after, 3))
        print("負荷差分 ΔL:", round(delta_L, 3))
        print("U_PA（総合）:", round(u_pa, 3))
        print("  U_PA^pref:", round(u_pref, 3))
        print("  U_PA^load:", round(u_load, 3))

        # 合意案（Outcome）→ dict（issue_settings）へ変換して返す
        return outcome_to_dict(agreement, issue_names)
    else:
         return dict(current_setting)

# def run_example(
#     L_current: float,
#     current_setting: Dict[str, str],
#     pa_profile: PAProfile,
#     thresholds: Thresholds,
#     a_coeffs: Dict[str, float],
#     rho_change: Dict[str, float],
#     aa_params: AAParams,
#     n_steps: int = 8,
#     max_candidates: int = 3000,
# )-> Optional[Dict[str, str]]:

#     issues = [
#         make_issue(name="tempo", values=TEMPO_VALUES),
#         make_issue(name="guidance", values=LEVEL3_VALUES),
#         make_issue(name="complexity", values=LEVEL3_VALUES),
#         make_issue(name="stimulus", values=LEVEL3_VALUES),
#         make_issue(name="break_policy", values=BREAK_VALUES),
#         make_issue(name="feedback", values=FEEDBACK_VALUES),
#         make_issue(name="taste", values=TASTE_VALUES),
#     ]
#     issue_names = [i.name for i in issues]

#     # 交渉ステップ数を引数化
#     session = SAOMechanism(issues=issues, n_steps=n_steps)

#     # ルールベース予測係数 a_i を引数で受け取る
#     load_model = RuleBasedLoadModel(a_coeffs=a_coeffs)

#     pa = PlayerAgentPA(
#         name="PA",
#         issue_names=issue_names,
#         profile=pa_profile,
#         thresholds=thresholds,
#         load_model=load_model,
#         initial_setting=current_setting,
#         L_current=L_current,
#     )

#     aa = AdjustmentAgentAA(
#         name="AA",
#         issue_names=issue_names,
#         thresholds=thresholds,
#         load_model=load_model,
#         pa_profile=pa_profile,
#         aa_params=aa_params,           # 引数化
#         rho_change=rho_change,         # 引数化
#         initial_setting=current_setting,
#         L_current=L_current,
#         get_pa_constraints=lambda: pa.constraints,
#         max_candidates=max_candidates, # 引数化
#     )

#     # 方針1：PAがAAの「候補ゼロ」情報を参照できるようにする
#     pa.aa_ref = aa

#     session.add(pa)
#     session.add(aa)

#     final_state = session.run()
#     agreement = final_state.agreement

#     print("合意:", agreement)
#     if agreement is not None:
#         ag = outcome_to_dict(agreement, issue_names)

#         # 変更前/変更後負荷（予測）と差分
#         L_before = L_current
#         L_after = load_model.predict(L_current, current_setting, ag)
#         delta_L = L_after - L_before

#         # 効用類
#         u_pa, _ = U_PA(
#             ag,
#             L_current=L_current,
#             current_setting=current_setting,
#             load_model=load_model,
#             thresholds=thresholds,
#             profile=pa_profile,
#         )
#         u_pref = U_PA_pref(ag, pa_profile)
#         u_load = U_PA_load(L_after, thresholds, pa_profile)

#         print("合意（dict）:", ag)
#         print("観測帯:", (thresholds.L_obs_low, thresholds.L_obs_high), " margin=", thresholds.margin)
#         print("予測帯:", (thresholds.L_pred_low, thresholds.L_pred_high))
#         print("負荷（変更前）:", round(L_before, 3))
#         print("負荷（変更後）:", round(L_after, 3))
#         print("負荷差分 ΔL:", round(delta_L, 3))
#         print("U_PA（総合）:", round(u_pa, 3))
#         print("  U_PA^pref:", round(u_pref, 3))
#         print("  U_PA^load:", round(u_load, 3))

#         # 合意案（Outcome）→ dict（issue_settings）へ変換して返す
#         return outcome_to_dict(agreement, issue_names)
#     else:
#          return None



# if __name__ == "__main__":
#     thresholds = Thresholds(L_obs_low=0.35, L_obs_high=0.65, margin=0.10)

#     a_coeffs = {
#         "tempo": +0.10,
#         "guidance": -0.12,
#         "complexity": +0.14,
#         "stimulus": +0.16,
#         "break_policy": -0.20,
#         "feedback": +0.05,
#     }

#     rho_change = {
#         "tempo": 0.5,
#         "guidance": 0.3,
#         "complexity": 0.6,
#         "stimulus": 0.6,
#         "break_policy": 0.4,
#         "feedback": 0.2,
#     }

#     aa_params = AAParams(alpha=10.0, beta=2.0, gamma=1.0, lam=0.25)

#     L_current = 0.78
#     current_setting = {
#         "tempo": "fast",
#         "guidance": "low",
#         "complexity": "high",
#         "stimulus": "high",
#         "break_policy": "rare",
#         "feedback": "detailed_immediate",
#         "taste": "concise",
#     }

#     pa_profile = PAProfile(
#         p={"tempo": 0.5, "guidance": 1.0, "complexity": 0.5, "stimulus": 0.5, "break_policy": 0.5, "feedback": 0.0},
#         w={"tempo": 0.15, "guidance": 0.20, "complexity": 0.20, "stimulus": 0.20, "break_policy": 0.10, "feedback": 0.15},
#         preferred_taste="polite",
#         w_taste=0.10,
#         tau_accept=0.60,
#         lambda_L=0.30,
#         eta=6.0,
#     )

#     run_example(
#         L_current=L_current,
#         current_setting=current_setting,
#         pa_profile=pa_profile,
#         thresholds=thresholds,
#         a_coeffs=a_coeffs,
#         rho_change=rho_change,
#         aa_params=aa_params,
#         n_steps=8,
#         max_candidates=3000,
#     )