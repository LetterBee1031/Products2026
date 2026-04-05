"""
NegMAS を用いた PA/AA 2者・多論点・交互提案（SAO）による自動交渉の最小実装ひな形。

実装している設計要素（このチャットで決めた内容）：
- 交互提案（Alternating Offers）：SAOMechanism + SAONegotiator
- 多論点（I_cl + I_e）：I_e は 6論点 +（順序なし）文章テイスト
- 認知負荷はルールベース予測：
    L_hat = clip(L_current + Δ_rule(offer), 0, 1)
- 観測閾値（CLE）と予測閾値（AA ルール予測）は分離し、安全マージンを導入：
    L_pred_high = L_obs_high - margin
    L_pred_low  = L_obs_low  + margin
- PA 効用：順序あり論点は s_i(x)=1-|z_i(x)-p_i|、テイストは順序なしカテゴリとして類似度で評価
- PA 受諾：負荷が予測帯に戻ること（ハード）＋ 好み効用が閾値以上（ソフト）
- PA 反対提案：論点ごとの許容集合 Ω_i(k)（CPA(k)）として制約を返す（実装では allowed_values を更新）
- AA 案生成：候補を列挙/サンプルし、argmax (U_AA + λ U_PA) を選ぶ（帯内ゲート付き）

注意：
- これは研究用プロトタイプです。要素リストが巨大な場合、候補生成は列挙ではなくヒューリスティック/サンプリングに置換してください。
"""

from __future__ import annotations

from dataclasses import dataclass
from math import exp
from pathlib import Path
import sys
from typing import Dict, List, Optional, Sequence, Tuple, Callable, Any

from negmas import ResponseType
from negmas.sao import SAOMechanism, SAONegotiator, SAOState
from negmas.outcomes import make_issue, Outcome

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.append(str(PROJECT_ROOT))

# サーバ本体ではなく共有状態だけを読むことで、重い依存や循環 import を避ける。
from Server.shared_state import get_user_issue_settings, update_user_issue_settings  # noqa: E402

# ----------------------------
# 1) 交渉論点（I_e）を離散値で定義
# ----------------------------

# ここ　論点を作るために単なる文字列として選択肢を用意している
# それぞれの論点によって選択肢と負荷の関係性とかが変わってくるから，一様に数値で定義するとよくないよなと
TEMPO_VALUES = ["slow", "normal", "fast"]
LEVEL3_VALUES = ["low", "medium", "high"]
BREAK_VALUES = ["rare", "on_demand", "frequent"]  # 値が大きいほど休憩を入れやすい
FEEDBACK_VALUES = ["summary", "brief_immediate", "detailed_immediate"]

# 文章テイスト（順序なしカテゴリ）
TASTE_VALUES = ["polite", "concise", "encouraging", "neutral"]

# 順序あり論点は 0〜1 に写像して距離で扱う
# 体験者の効用関数的には，高すぎるとダメとかではなく，選択した値から離れてるとダメ　みたいな感じで行く
# システム側の効用関数的には，適した範囲内においては負荷が高いほうがいい　という計算をしたい
# ただ，休憩頻度の部分に関しては多いほうが負荷下がりそうなのでそのあたりは反転させたい
ORDINAL_MAPS: Dict[str, Dict[str, float]] = {
    "tempo": {"slow": 0.0, "normal": 0.5, "fast": 1.0}, # 体験のテンポ
    "guidance": {"low": 0.0, "medium": 0.5, "high": 1.0}, # ガイダンスの量
    "complexity": {"low": 0.0, "medium": 0.5, "high": 1.0}, # 体験中に必要な判断の複雑さ
    "stimulus": {"low": 0.0, "medium": 0.5, "high": 1.0}, # 体験刺激の強度（演出など）
    "break_policy": {"rare": 0.0, "on_demand": 0.5, "frequent": 1.0}, # 休憩・クールダウンの方針
    "feedback": {"summary": 0.0, "brief_immediate": 0.5, "detailed_immediate": 1.0}, # 体験のフィードバック量とか
}

# 文章・体験テイストの類似度行列 K（必要ならオフ対角を 0 にして「完全一致のみ」にしても良い）
TASTE_SIMILARITY: Dict[str, Dict[str, float]] = {
    "polite": {"polite": 1.0, "concise": 0.4, "encouraging": 0.7, "neutral": 0.6}, # polite (丁寧・安心感重視)
    "concise": {"polite": 0.4, "concise": 1.0, "encouraging": 0.3, "neutral": 0.6}, # concise (簡潔・短文・要点のみ)
    "encouraging": {"polite": 0.7, "concise": 0.3, "encouraging": 1.0, "neutral": 0.6}, # encouraging (励まし・不安低減)
    "neutral": {"polite": 0.6, "concise": 0.6, "encouraging": 0.6, "neutral": 1.0}, # neutral (ニュートラル・事実提示)
}


def get_current_setting(user_id: str = "01") -> Dict[str, str]:
    return get_user_issue_settings(user_id)


def update_current_setting(user_id: str, issue_settings: Dict[str, str]) -> None:
    update_user_issue_settings(user_id, issue_settings)

# ----------------------------------------
# 2) 観測/予測 閾値の分離（安全マージン込み）
# ----------------------------------------

@dataclass
class Thresholds:
    """CLE（観測）閾値と、AA ルール予測で使う安全側の予測閾値を管理する。"""
    # Load observe 観測した認知負荷の条件
    L_obs_low: float
    L_obs_high: float
    margin: float = 0.10  # 予測誤差を見込んだ安全マージン

    # Load predict 体験調整後に予想される認知負荷に関する条件
    # ここの式はこのままでは使えない，というか設計しなおす必要がある
    @property
    def L_pred_low(self) -> float:
        return self.L_obs_low + self.margin

    @property
    def L_pred_high(self) -> float:
        return self.L_obs_high - self.margin


# ----------------------------------------
# 3) ルールベース負荷予測（Δモデル）
# ----------------------------------------

@dataclass
class RuleBasedLoadModel:
    """
    介入（調整案）による負荷の増減を線形和で見積もる簡易モデル。

    L_hat = clip( L_current + Σ a_i * (z_i(offer) - z_i(current)), 0, 1)

    coeffs の符号：
      +：値を上げると負荷が上がる（例：tempo, complexity, stimulus）
      -：値を上げると負荷が下がる（例：guidance, break_policy）
    """
    coeffs: Dict[str, float]  # a_i

    def predict(self, L_current: float, current_setting: Dict[str, str], offer: Dict[str, str]) -> float:
        delta = 0.0
        for issue, a in self.coeffs.items():
            z_offer = ORDINAL_MAPS[issue][offer[issue]]
            z_curr = ORDINAL_MAPS[issue][current_setting[issue]]
            delta += a * (z_offer - z_curr)

        # テイストは順序なしカテゴリなので、デフォルトでは負荷予測に入れない
        L_hat = max(0.0, min(1.0, L_current + delta))
        return L_hat


# -------------------------
# 4) PA/AA の効用関数
# -------------------------

@dataclass
class PAProfile:
    """PA（体験者側）の好みプロファイル。"""
    p: Dict[str, float]               # 順序あり論点の理想値 p_i（0〜1）
    w: Dict[str, float]               # 順序あり論点の重み w_i（合計<=1）
    preferred_taste: str              # 好みのテイスト（カテゴリ）
    w_taste: float = 0.15             # テイスト重み
    tau_accept: float = 0.60          # 好み効用がこれ以上なら受諾（負荷が帯内であることが前提）


def s_ordinal(z: float, p: float) -> float:
    """順序あり論点の一致度：s_i(x)=1-|z_i(x)-p_i|"""
    return 1.0 - abs(z - p)


def s_taste(taste: str, preferred: str, similarity: Dict[str, Dict[str, float]]) -> float:
    """順序なしカテゴリ（テイスト）の一致度：類似度行列 K による評価"""
    return similarity[taste][preferred]


def U_PA(offer: Dict[str, str], profile: PAProfile) -> float:
    """PA 効用：順序ありは距離、テイストは類似度で重み付き合成（0〜1にクリップ）。"""
    u = 0.0
    for issue, w in profile.w.items():
        z = ORDINAL_MAPS[issue][offer[issue]]
        u += w * s_ordinal(z, profile.p[issue])

    u += profile.w_taste * s_taste(offer["taste"], profile.preferred_taste, TASTE_SIMILARITY)
    return max(0.0, min(1.0, u))


@dataclass
class AAParams:
    """AA（調整側）の効用パラメータ。"""
    alpha: float = 8.0   # 帯外れの罰（大きいほど帯外を強く避ける）
    beta: float = 2.0    # 帯内中心への寄せ
    gamma: float = 1.0   # 変更コスト（急変抑制）
    lam: float = 0.25    # λ：AAがPA効用をどれだけ尊重するか


def d_out(L_hat: float, low: float, high: float) -> float:
    """帯外れ量（ヒンジ）：帯内なら0"""
    return max(0.0, low - L_hat) + max(0.0, L_hat - high)


def d_in(L_hat: float, low: float, high: float) -> float:
    """帯内中心への距離（任意）：中心からの絶対距離"""
    L_star = (low + high) / 2.0
    return abs(L_hat - L_star)


def change_cost(current_setting: Dict[str, str], offer: Dict[str, str], rho: Dict[str, float]) -> float:
    """変更コスト：順序あり論点の変化量の重み付き和"""
    # current_settingは現在の設定，offerは提案内容，rhoは各論点の重みに関する辞書 ρってことらしい
    c = 0.0
    for issue, r in rho.items():
        z_offer = ORDINAL_MAPS[issue][offer[issue]]
        z_curr = ORDINAL_MAPS[issue][current_setting[issue]]
        c += r * abs(z_offer - z_curr)
    return c


def U_AA(
    offer: Dict[str, str], # 提案
    L_current: float, # 現在の認知負荷
    current_setting: Dict[str, str], # 現在の設定
    load_model: RuleBasedLoadModel, # 調整によってどのように負荷が変化するか
    thresholds: Thresholds, # 閾値
    rho: Dict[str, float], # 各論点に対する重み
    params: AAParams, # 調整エージェントの効用の重みパラメータ　アルファ，ベータ，ガンマのやつ
) -> Tuple[float, float]:
    """
    AA効用：
      U_AA = exp(-α d_out) * exp(-β d_in) * exp(-γ c)
    戻り値： (U_AA, L_hat)
    """
    L_hat = load_model.predict(L_current, current_setting, offer) # 提案内容で調整した際に予測される負荷

    out = d_out(L_hat, thresholds.L_pred_low, thresholds.L_pred_high)
    inn = d_in(L_hat, thresholds.L_pred_low, thresholds.L_pred_high)
    c = change_cost(current_setting, offer, rho)

    u = exp(-params.alpha * out) * exp(-params.beta * inn) * exp(-params.gamma * c)
    return u, L_hat

# ------------------------------------
# 5) NegMAS 用のユーティリティ（Outcome ↔ dict 変換）
# ------------------------------------

def outcome_to_dict(outcome: Outcome, issue_names: Sequence[str]) -> Dict[str, str]:
    """NegMASのOutcome（tupleなど）を、論点名→値のdictへ変換。"""
    if isinstance(outcome, dict):
        return outcome
    return {name: outcome[i] for i, name in enumerate(issue_names)}

def dict_to_outcome(d: Dict[str, str], issue_names: Sequence[str]) -> Tuple[Any, ...]:
    """論点名→値のdictを、NegMASのOutcome（tuple）へ変換。"""
    return tuple(d[name] for name in issue_names)


# ------------------------------------
# 6) CPA(k) の実装：PAが返す「許容集合 Ω_i(k)」
# ------------------------------------

@dataclass
class PAConstraints:
    """
    CPA(k) = { z_i(x) ∈ Ω_i(k) } の簡易実装。
    実装上は「各論点で許容するカテゴリ値の集合」を持つ。
    許容集合に入ってるかを確認してるだけやなここ
    """
    allowed_values: Dict[str, set]

    def allows(self, offer: Dict[str, str]) -> bool:
        for issue, allowed in self.allowed_values.items():
            if offer[issue] not in allowed:
                return False
        return True


# ------------------------------------
# 7) PA（Player Agent）: 受諾/拒否/制約更新
# ------------------------------------

class PlayerAgentPA(SAONegotiator):
    """
    PAの方針：
    - 受諾条件（ハード）：予測負荷 L_hat が予測帯 [L_pred_low, L_pred_high] に入る
    - 受諾条件（ソフト）：好み効用 U_PA が tau_accept 以上
    - 拒否したら：重要論点だけ制約（Ω）を好みに寄せて絞る（交渉を詰ませないため）
    """

    def __init__(
        self,
        *,
        name: str,
        issue_names: Sequence[str],
        profile: PAProfile,
        thresholds: Thresholds,
        load_model: RuleBasedLoadModel,
        initial_setting: Dict[str, str],
        L_current: float,
    ):
        super().__init__(name=name)
        self.issue_names = list(issue_names)
        self.profile = profile
        self.thresholds = thresholds
        self.load_model = load_model

        # 現在の設定・負荷（交渉が成立すると更新）
        self.current_setting = dict(initial_setting)
        self.L_current = float(L_current)

        # 初期は制約なし（全許容）
        # 提案に対して制約をつけるかどうかという話なので別に効用を無視するという話ではなく
        # 交渉のなかでどんどん狭めていくイメージかなぁ
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

    def propose(self, state: SAOState) -> Outcome:
        """
        SAOでは自分の番で提案が必要になるため、PAも提案関数を持つ。
        本設計ではPAは「具体案生成」より「要求（Ω）」が主なので、
        ここでは現在の制約内で理想点に最も近い案を返す。
        """
        offer_dict = {
            "tempo": self._closest_allowed("tempo", self.profile.p["tempo"]),
            "guidance": self._closest_allowed("guidance", self.profile.p["guidance"]),
            "complexity": self._closest_allowed("complexity", self.profile.p["complexity"]),
            "stimulus": self._closest_allowed("stimulus", self.profile.p["stimulus"]),
            "break_policy": self._closest_allowed("break_policy", self.profile.p["break_policy"]),
            "feedback": self._closest_allowed("feedback", self.profile.p["feedback"]),
            "taste": self.profile.preferred_taste
            if self.profile.preferred_taste in self.constraints.allowed_values["taste"]
            else next(iter(self.constraints.allowed_values["taste"])),
        }
        return dict_to_outcome(offer_dict, self.issue_names)

    def respond(self, state: SAOState, source: str | None = None) -> ResponseType:
        offer = state.current_offer
        if offer is None:
            return ResponseType.REJECT_OFFER

        offer_dict = outcome_to_dict(offer, self.issue_names)

        # ① CPA(k) 制約チェック
        if not self.constraints.allows(offer_dict):
            self._tighten_constraints()
            return ResponseType.REJECT_OFFER

        # ② 負荷（ルール予測）チェック：帯内（ハード条件）
        L_hat = self.load_model.predict(self.L_current, self.current_setting, offer_dict)
        load_ok = (self.thresholds.L_pred_low <= L_hat <= self.thresholds.L_pred_high)

        # ③ 好み効用（ソフト条件）
        u_pa = U_PA(offer_dict, self.profile)

        if load_ok and (u_pa >= self.profile.tau_accept):
            # 合意：内部状態を更新
            self.current_setting = dict(offer_dict)
            self.L_current = L_hat
            return ResponseType.ACCEPT_OFFER

        # 拒否：制約（Ω）を少し絞る（重要論点のみ）
        self._tighten_constraints()
        return ResponseType.REJECT_OFFER

    def _closest_allowed(self, issue: str, p: float) -> str:
        """順序あり論点：許容値の中から理想値に最も近いものを選ぶ。"""
        candidates = list(self.constraints.allowed_values[issue])
        best = min(candidates, key=lambda v: abs(ORDINAL_MAPS[issue][v] - p))
        return best

    def _tighten_constraints(self) -> None:
        """
        CPA(k) の更新例：
        - 交渉が詰まらないよう、全論点ではなく「重要論点」だけを絞る
        - 各論点は理想値に近い上位2候補を残す（3段階→2段階へ）
        - テイストは {preferred, neutral} のように許容集合を作る
        """
        important = ["tempo", "guidance", "complexity", "stimulus", "taste"]

        for issue in important:
            if issue == "taste":
                pref = self.profile.preferred_taste
                allowed = {pref}
                if "neutral" in TASTE_VALUES:
                    allowed.add("neutral")
                # intersection()は積集合を返すらしい（両方の要素に入ってるもの）
                self.constraints.allowed_values["taste"] = self.constraints.allowed_values["taste"].intersection(allowed)
                continue

            p = self.profile.p[issue]
            vals = sorted(list(self.constraints.allowed_values[issue]), key=lambda v: (abs(ORDINAL_MAPS[issue][v] - p), v))
            self.constraints.allowed_values[issue] = set(vals[:2]) if len(vals) >= 2 else set(vals)

        # 万一空集合になった論点は全許容へ戻す（保険）
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


# ------------------------------------
# 8) AA（Adjustment Agent）: 候補探索→最良提案
# ------------------------------------

class AdjustmentAgentAA(SAONegotiator):
    """
    AAの方針：
    - 候補（Outcome）を列挙/サンプルし、PA制約（Ω）を満たすものだけ評価
    - 帯内ゲート（負荷が予測帯に戻る案のみ）を通した上で
        score = U_AA + λ U_PA
      が最大の案を提案する
    """

    def __init__(
        self,
        *,
        name: str,
        issue_names: Sequence[str],
        thresholds: Thresholds,
        load_model: RuleBasedLoadModel,
        pa_profile: PAProfile,
        aa_params: AAParams,
        rho_change: Dict[str, float],
        initial_setting: Dict[str, str],
        L_current: float,
        get_pa_constraints: Callable[[], PAConstraints],
        max_candidates: int = 2000, # candidatesは提案候補っぽい
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
        self._cached_candidates: Optional[List[Outcome]] = None

    def on_negotiation_start(self, state: SAOState) -> None:
        super().on_negotiation_start(state)
        # 離散空間が小さい場合は列挙が簡単（大きい場合はサンプルに落ちる）
        # 提案候補をキャッシュしておく処理
        os = self.nmi.outcome_space
        # 候補を出来るだけ列挙．無理ならサンプリングになる
        self._cached_candidates = list(os.enumerate_or_sample(max_cardinality=self.max_candidates))

    def propose(self, state: SAOState) -> Outcome:
        assert self._cached_candidates is not None, "候補が初期化されていません"

        constraints = self.get_pa_constraints()
        best_offer: Optional[Outcome] = None
        best_score = float("-inf")

        for outcome in self._cached_candidates:
            offer_dict = outcome_to_dict(outcome, self.issue_names)

            # ① PA制約（Ω）を満たすか
            if not constraints.allows(offer_dict):
                continue

            # ② AA効用と予測負荷
            u_aa, L_hat = U_AA(
                offer_dict,
                L_current=self.L_current,
                current_setting=self.current_setting,
                load_model=self.load_model,
                thresholds=self.thresholds,
                rho=self.rho_change,
                params=self.aa_params,
            )

            # ③ ハードゲート：予測帯に戻る案だけ残す
            if not (self.thresholds.L_pred_low <= L_hat <= self.thresholds.L_pred_high):
                continue

            # ④ 好みも少し尊重（λ）
            u_pa = U_PA(offer_dict, self.pa_profile)
            score = u_aa + self.aa_params.lam * u_pa

            if score > best_score:
                best_score = score
                best_offer = outcome

        # フォールバック：帯内案が見つからない場合（保守的調整を提案）
        if best_offer is None:
            fb = dict(self.current_setting)
            fb["break_policy"] = "frequent"
            fb["guidance"] = "high"
            fb["stimulus"] = "low"
            fb["complexity"] = "low"
            best_offer = dict_to_outcome(fb, self.issue_names)

        return best_offer

    def respond(self, state: SAOState, source: str | None = None) -> ResponseType:
        """
        AAは基本的に提案者だが、相手提案が「自分の次提案より良い」なら受諾する。
        """
        offer = state.current_offer
        if offer is None:
            return ResponseType.REJECT_OFFER

        offer_dict = outcome_to_dict(offer, self.issue_names)
        my_next_dict = outcome_to_dict(self.propose(state), self.issue_names)

        u_aa_offer, _ = U_AA(
            offer_dict, self.L_current, self.current_setting, self.load_model,
            self.thresholds, self.rho_change, self.aa_params
        )
        u_pa_offer = U_PA(offer_dict, self.pa_profile)
        score_offer = u_aa_offer + self.aa_params.lam * u_pa_offer

        u_aa_next, _ = U_AA(
            my_next_dict, self.L_current, self.current_setting, self.load_model,
            self.thresholds, self.rho_change, self.aa_params
        )
        u_pa_next = U_PA(my_next_dict, self.pa_profile)
        score_next = u_aa_next + self.aa_params.lam * u_pa_next

        if score_offer >= score_next:
            # 受諾：内部状態更新
            self.current_setting = dict(offer_dict)
            self.L_current = self.load_model.predict(self.L_current, self.current_setting, offer_dict)
            return ResponseType.ACCEPT_OFFER

        return ResponseType.REJECT_OFFER


# -----------------------------
# 9) 実行例（交渉を1回回す）
# -----------------------------

def run_example(L_current: float, user_id: str = "01"):
    # 交渉論点（issues）を作成
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

    # 交渉機構（SAO：交互提案）
    session = SAOMechanism(issues=issues, n_steps=8)

    # CLE（観測）閾値の例
    thresholds = Thresholds(L_obs_low=0.35, L_obs_high=0.65, margin=0.10)

    # 現在負荷（高負荷で交渉開始した想定）
    # L_current = 0.78

    # 現在の体験設定
    current_setting = get_current_setting(user_id)

    # ルールベース負荷予測の係数（例）
    load_model = RuleBasedLoadModel(
        coeffs={
            "tempo": +0.10,
            "guidance": -0.12,
            "complexity": +0.14,
            "stimulus": +0.16,
            "break_policy": -0.20,
            "feedback": +0.05,
        }
    )

    # PA 好み（例）
    pa_profile = PAProfile(
        p={
            "tempo": 0.5,        # normal
            "guidance": 1.0,     # high
            "complexity": 0.5,   # medium
            "stimulus": 0.5,     # medium
            "break_policy": 0.5, # on_demand
            "feedback": 0.0,     # summary
        },
        w={
            "tempo": 0.15,
            "guidance": 0.20,
            "complexity": 0.20,
            "stimulus": 0.20,
            "break_policy": 0.10,
            "feedback": 0.15,
        },
        preferred_taste="polite",
        w_taste=0.10,
        tau_accept=0.60,
    )

    # AA パラメータと変更コスト重み（例）
    aa_params = AAParams(alpha=10.0, beta=2.0, gamma=1.0, lam=0.25)
    rho_change = {
        "tempo": 0.5,
        "guidance": 0.3,
        "complexity": 0.6,
        "stimulus": 0.6,
        "break_policy": 0.4,
        "feedback": 0.2,
    }

    # PA を先に作り、AA が制約（Ω）を参照できるようにする
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
        aa_params=aa_params,
        rho_change=rho_change,
        initial_setting=current_setting,
        L_current=L_current,
        get_pa_constraints=lambda: pa.constraints,
        max_candidates=2500,  # 離散空間が小さいのでほぼ全列挙できる
    )

    # 交渉へ参加
    session.add(pa)
    session.add(aa)

    # 実行
    final_state = session.run()
    agreement = final_state.agreement

    print("合意:", agreement)
    if agreement is not None:
        ag = outcome_to_dict(agreement, issue_names)
        update_current_setting(user_id, ag)
        L_hat = load_model.predict(L_current, current_setting, ag)
        print("合意（dict）:", ag)
        print("予測負荷:", round(L_hat, 3))
        print("予測帯:", (thresholds.L_pred_low, thresholds.L_pred_high))
        print("PA効用:", round(U_PA(ag, pa_profile), 3))
        return {
            "ok": True,
            "user_id": user_id,
            "agreement": ag,
            "updated_issue_settings": get_current_setting(user_id),
            "predicted_load": L_hat,
            "predicted_band": (thresholds.L_pred_low, thresholds.L_pred_high),
            "pa_utility": U_PA(ag, pa_profile),
        }

    return {
        "ok": False,
        "user_id": user_id,
        "agreement": None,
        "updated_issue_settings": get_current_setting(user_id),
    }

if __name__ == "__main__":
    run_example()
