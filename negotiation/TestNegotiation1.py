from __future__ import annotations

from dataclasses import dataclass
from math import exp
from typing import Any, Callable, Dict, List, Optional, Sequence, Tuple

from negmas import ResponseType
from negmas.outcomes import Outcome, make_issue
from negmas.sao import SAOMechanism, SAONegotiator, SAOState

try:
    from Server import shared_state
except ModuleNotFoundError:
    from Server import shared_state


# このファイルでは論点値をすべて 0.0-1.0 の数値として扱う。
# 以前の "slow" / "medium" / "polite" のようなカテゴリ値への写像は使わない。
IssueSetting = Dict[str, float]


def issue_names() -> list[str]:
    """shared_state がCSVから読み込んだ論点名を使う。"""
    return list(shared_state.ISSUE_OPTIONS.keys())


def issue_options(issue: str) -> list[float]:
    """NegMASに渡す選択肢。標準は 0.25 刻み。"""
    return [float(value) for value in shared_state.ISSUE_OPTIONS[issue]]


def nearest_option(issue: str, value: float) -> float:
    """CSVやAPIから来た値を、その論点で許される一番近い選択肢へ丸める。"""
    return min(issue_options(issue), key=lambda option: abs(option - float(value)))


def normalize_setting(setting: Dict[str, Any]) -> IssueSetting:
    """不足論点を補いつつ、すべての論点値を数値選択肢へ正規化する。"""
    normalized: IssueSetting = {}
    for issue in issue_names():
        raw_value = setting.get(issue, 0.5)
        normalized[issue] = nearest_option(issue, float(raw_value))
    return normalized


@dataclass
class Thresholds:
    """観測負荷帯と予測負荷帯を管理する。"""

    L_obs_low: float
    L_obs_high: float
    margin: float = 0.10

    @property
    def L_pred_low(self) -> float:
        return self.L_obs_low + self.margin

    @property
    def L_pred_high(self) -> float:
        return self.L_obs_high - self.margin


@dataclass
class RuleBasedLoadModel:
    """L_pred = L_current + sum(coeff_i * delta_i) の単純な負荷予測モデル。"""

    a_coeffs: Dict[str, float]

    def predict(
        self,
        L_current: float,
        current_setting: IssueSetting,
        offer: IssueSetting,
    ) -> float:
        delta = 0.0
        for issue, coeff in self.a_coeffs.items():
            if issue not in current_setting or issue not in offer:
                continue
            delta += float(coeff) * (float(offer[issue]) - float(current_setting[issue]))
        return max(0.0, min(1.0, float(L_current) + delta))


def d_out(L_pred: float, low: float, high: float) -> float:
    """予測負荷が許容帯からどれだけ外れているか。"""
    return max(0.0, low - L_pred) + max(0.0, L_pred - high)


def d_in(L_pred: float, low: float, high: float) -> float:
    """予測負荷が許容帯の中心からどれだけ離れているか。"""
    return abs(L_pred - ((low + high) / 2.0))


def change_cost(
    current_setting: IssueSetting,
    offer: IssueSetting,
    rho: Dict[str, float],
) -> float:
    """設定変更のしにくさ。rho が大きい論点ほど変更コストが大きい。"""
    cost = 0.0
    for issue, weight in rho.items():
        if issue not in current_setting or issue not in offer:
            continue
        cost += float(weight) * abs(float(offer[issue]) - float(current_setting[issue]))
    return cost


@dataclass
class PAProfile:
    """PA側の好み。p は理想値、w は論点ごとの重み。"""

    p: Dict[str, float]
    w: Dict[str, float]
    tau_accept: float = 0.70
    tau_min: float = 0.30
    lambda_L: float = 0.30
    eta: float = 6.0


def s_ordinal(z: float, p: float) -> float:
    """0-1値同士の近さを効用にする。"""
    return 1.0 - abs(float(z) - float(p))


def U_PA_pref(offer: IssueSetting, profile: PAProfile) -> float:
    """PAの好みによる効用。テイスト項は一旦除外している。"""
    utility = 0.0
    for issue, weight in profile.w.items():
        if issue not in offer or issue not in profile.p:
            continue
        utility += float(weight) * s_ordinal(float(offer[issue]), float(profile.p[issue]))
    return max(0.0, min(1.0, utility))


def U_PA_load(L_pred: float, thresholds: Thresholds, profile: PAProfile) -> float:
    """負荷が予測許容帯から外れるほど指数的に効用を下げる。"""
    return exp(-profile.eta * d_out(L_pred, thresholds.L_pred_low, thresholds.L_pred_high))


def U_PA(
    offer: IssueSetting,
    *,
    L_current: float,
    current_setting: IssueSetting,
    load_model: RuleBasedLoadModel,
    thresholds: Thresholds,
    profile: PAProfile,
) -> Tuple[float, float]:
    """好み効用と負荷効用を混合したPA効用を返す。"""
    L_pred = load_model.predict(L_current, current_setting, offer)
    u_pref = U_PA_pref(offer, profile)
    u_load = U_PA_load(L_pred, thresholds, profile)
    lamL = max(0.0, min(1.0, profile.lambda_L))
    return max(0.0, min(1.0, (1.0 - lamL) * u_pref + lamL * u_load)), L_pred


@dataclass
class AAParams:
    """AA側の効用パラメータ。"""

    alpha: float = 10.0
    beta: float = 2.0
    gamma: float = 1.0
    lam: float = 0.75


def U_AA(
    offer: IssueSetting,
    *,
    L_current: float,
    current_setting: IssueSetting,
    load_model: RuleBasedLoadModel,
    thresholds: Thresholds,
    rho: Dict[str, float],
    params: AAParams,
) -> Tuple[float, float]:
    """AA効用。負荷帯復帰、中心志向、変更コストを掛け合わせる。"""
    L_pred = load_model.predict(L_current, current_setting, offer)
    out = d_out(L_pred, thresholds.L_pred_low, thresholds.L_pred_high)
    inn = d_in(L_pred, thresholds.L_pred_low, thresholds.L_pred_high)
    cost = change_cost(current_setting, offer, rho)
    utility = exp(-params.alpha * out) * exp(-params.beta * inn) * exp(-params.gamma * cost)
    return utility, L_pred


def outcome_to_dict(outcome: Outcome, names: Sequence[str]) -> IssueSetting:
    """NegMASのOutcomeを論点名付きdictへ戻す。"""
    if isinstance(outcome, dict):
        return normalize_setting(outcome)
    return {name: float(outcome[index]) for index, name in enumerate(names)}


def dict_to_outcome(setting: IssueSetting, names: Sequence[str]) -> Tuple[Any, ...]:
    """論点dictをNegMASが扱いやすいtupleへ変換する。"""
    return tuple(float(setting[name]) for name in names)


@dataclass
class PAConstraints:
    """PAが許容する論点値の集合。現在はCSVのISSUE_OPTIONSをそのまま許容する。"""

    allowed_values: Dict[str, set[float]]

    def allows(self, offer: IssueSetting) -> bool:
        return all(float(offer[issue]) in self.allowed_values[issue] for issue in self.allowed_values)


class PlayerAgentPA(SAONegotiator):
    """ユーザ側エージェント。好みと負荷条件を満たす提案を受け入れる。"""

    def __init__(
        self,
        *,
        name: str,
        issue_names: Sequence[str],
        profile: PAProfile,
        thresholds: Thresholds,
        load_model: RuleBasedLoadModel,
        initial_setting: IssueSetting,
        L_current: float,
    ):
        super().__init__(name=name)
        self.issue_names = list(issue_names)
        self.profile = profile
        self.thresholds = thresholds
        self.load_model = load_model
        self.current_setting = dict(initial_setting)
        self.L_current = float(L_current)
        self.aa_ref: Optional["AdjustmentAgentAA"] = None
        self.constraints = PAConstraints(
            allowed_values={issue: set(issue_options(issue)) for issue in self.issue_names}
        )

    def propose(self, state: SAOState) -> Outcome:
        # PAから提案する場合は、各論点で理想値に最も近い値を出す。
        offer = {
            issue: self._closest_allowed(issue, self.profile.p.get(issue, 0.5))
            for issue in self.issue_names
        }
        return dict_to_outcome(offer, self.issue_names)

    def respond(self, state: SAOState, source: str | None = None) -> ResponseType:
        offer = state.current_offer
        if offer is None:
            return ResponseType.REJECT_OFFER

        offer_dict = outcome_to_dict(offer, self.issue_names)
        if not self.constraints.allows(offer_dict):
            return ResponseType.REJECT_OFFER

        u_pa, L_pred = U_PA(
            offer_dict,
            L_current=self.L_current,
            current_setting=self.current_setting,
            load_model=self.load_model,
            thresholds=self.thresholds,
            profile=self.profile,
        )
        load_ok = self.thresholds.L_pred_low <= L_pred <= self.thresholds.L_pred_high
        if load_ok and u_pa >= self._tau_accept_now(state):
            self.current_setting = dict(offer_dict)
            self.L_current = L_pred
            return ResponseType.ACCEPT_OFFER

        return ResponseType.REJECT_OFFER

    def _tau_accept_now(self, state: SAOState) -> float:
        # 交渉が進むほど受諾閾値を tau_accept から tau_min へ線形に下げる。
        tau0 = float(self.profile.tau_accept)
        tau_min = float(self.profile.tau_min)
        step = None
        for attr in ("step", "current_step", "k", "round"):
            if hasattr(state, attr):
                try:
                    step = int(getattr(state, attr))
                    break
                except Exception:
                    pass

        n_steps_calc = None
        for owner in (state, self.nmi):
            for attr in ("n_steps", "max_steps"):
                if hasattr(owner, attr):
                    try:
                        n_steps_calc = int(getattr(owner, attr))
                        break
                    except Exception:
                        pass
            if n_steps_calc is not None:
                break

        if step is None or n_steps_calc is None or n_steps_calc <= 1:
            progress = 0.0
        else:
            progress = max(0.0, min(1.0, step / (n_steps_calc - 1)))
        return max(0.0, min(1.0, (1.0 - progress) * tau0 + progress * tau_min))

    def _closest_allowed(self, issue: str, p: float) -> float:
        candidates = list(self.constraints.allowed_values[issue])
        return min(candidates, key=lambda value: abs(float(value) - float(p)))


class AdjustmentAgentAA(SAONegotiator):
    """調整側エージェント。AA効用 + PA効用の重み付き和が最大の提案を探す。"""

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
        initial_setting: IssueSetting,
        L_current: float,
        get_pa_constraints: Callable[[], PAConstraints],
        max_candidates: int = 3000,
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
        outcome_space = self.nmi.outcome_space
        self._cached_candidates = list(
            outcome_space.enumerate_or_sample(max_cardinality=self.max_candidates)
        )

    def propose(self, state: SAOState) -> Outcome:
        assert self._cached_candidates is not None, "candidates are not initialized"

        constraints = self.get_pa_constraints()
        best_offer: Optional[Outcome] = None
        best_score = float("-inf")

        for outcome in self._cached_candidates:
            offer_dict = outcome_to_dict(outcome, self.issue_names)
            if not constraints.allows(offer_dict):
                continue

            u_aa, L_pred = U_AA(
                offer_dict,
                L_current=self.L_current,
                current_setting=self.current_setting,
                load_model=self.load_model,
                thresholds=self.thresholds,
                rho=self.rho_change,
                params=self.aa_params,
            )
            if not (self.thresholds.L_pred_low <= L_pred <= self.thresholds.L_pred_high):
                continue

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

        if best_offer is None:
            best_offer = dict_to_outcome(self._fallback_offer(), self.issue_names)
        return best_offer

    def respond(self, state: SAOState, source: str | None = None) -> ResponseType:
        offer = state.current_offer
        if offer is None:
            return ResponseType.REJECT_OFFER

        offer_dict = outcome_to_dict(offer, self.issue_names)
        L_offer_pred = self.load_model.predict(self.L_current, self.current_setting, offer_dict)
        if not (self.thresholds.L_pred_low <= L_offer_pred <= self.thresholds.L_pred_high):
            return ResponseType.REJECT_OFFER

        my_next = outcome_to_dict(self.propose(state), self.issue_names)
        if self._combined_score(offer_dict) >= self._combined_score(my_next):
            self.current_setting = dict(offer_dict)
            self.L_current = L_offer_pred
            return ResponseType.ACCEPT_OFFER
        return ResponseType.REJECT_OFFER

    def _combined_score(self, offer: IssueSetting) -> float:
        u_aa, _ = U_AA(
            offer,
            L_current=self.L_current,
            current_setting=self.current_setting,
            load_model=self.load_model,
            thresholds=self.thresholds,
            rho=self.rho_change,
            params=self.aa_params,
        )
        u_pa, _ = U_PA(
            offer,
            L_current=self.L_current,
            current_setting=self.current_setting,
            load_model=self.load_model,
            thresholds=self.thresholds,
            profile=self.pa_profile,
        )
        return u_aa + self.aa_params.lam * u_pa

    def _fallback_offer(self) -> IssueSetting:
        # 帯内候補が見つからない場合は、現在負荷を帯に戻す方向へ各論点を動かす。
        offer = dict(self.current_setting)
        for issue, coeff in self.load_model.a_coeffs.items():
            options = issue_options(issue)
            if self.L_current < self.thresholds.L_pred_low:
                target = max(options) if coeff >= 0 else min(options)
            else:
                target = min(options) if coeff >= 0 else max(options)
            offer[issue] = float(target)
        return normalize_setting(offer)


def run_example(
    L_current: float,
    current_setting: Dict[str, Any],
    pa_preference: Dict[str, float],
    pa_weight: Dict[str, float],
    pa_taste_preference: str | None = None,
    pa_taste_weight: float | None = None,
    n_steps: int = 20,
    max_candidates: int = 15000,
    coeffs: Dict[str, float] | None = None,
    rho: Dict[str, float] | None = None,
) -> IssueSetting:
    """1ユーザ分の交渉を実行する。

    pa_taste_preference / pa_taste_weight は旧API互換のために残しているが、
    現在の効用計算では使わない。
    """
    thresholds = Thresholds(L_obs_low=0.3, L_obs_high=0.7, margin=0.10)
    current = normalize_setting(current_setting)
    names = issue_names()
    a_coeffs = dict(coeffs or shared_state.DEFAULT_COEFFS)
    rho_change = dict(rho or shared_state.DEFAULT_RHO)
    aa_params = AAParams(alpha=10.0, beta=2.0, gamma=1.0, lam=0.75)

    pa_profile = PAProfile(
        p={issue: nearest_option(issue, pa_preference.get(issue, 0.5)) for issue in names},
        w={issue: float(pa_weight.get(issue, 0.0)) for issue in names},
        tau_accept=0.70,
        tau_min=0.1,
        lambda_L=0.30,
        eta=6.0,
    )

    issues = [make_issue(name=issue, values=issue_options(issue)) for issue in names]
    session = SAOMechanism(issues=issues, n_steps=n_steps)
    load_model = RuleBasedLoadModel(a_coeffs=a_coeffs)

    pa = PlayerAgentPA(
        name="PA",
        issue_names=names,
        profile=pa_profile,
        thresholds=thresholds,
        load_model=load_model,
        initial_setting=current,
        L_current=L_current,
    )
    aa = AdjustmentAgentAA(
        name="AA",
        issue_names=names,
        thresholds=thresholds,
        load_model=load_model,
        pa_profile=pa_profile,
        aa_params=aa_params,
        rho_change=rho_change,
        initial_setting=current,
        L_current=L_current,
        get_pa_constraints=lambda: pa.constraints,
        max_candidates=max_candidates,
    )
    pa.aa_ref = aa

    session.add(pa)
    session.add(aa)
    final_state = session.run()
    agreement = final_state.agreement

    print("agreement:", agreement)
    if agreement is None:
        return dict(current)

    result = outcome_to_dict(agreement, names)
    L_after = load_model.predict(L_current, current, result)
    u_pa, _ = U_PA(
        result,
        L_current=L_current,
        current_setting=current,
        load_model=load_model,
        thresholds=thresholds,
        profile=pa_profile,
    )
    u_aa, _ = U_AA(
        result,
        L_current=L_current,
        current_setting=current,
        load_model=load_model,
        thresholds=thresholds,
        rho=rho_change,
        params=aa_params,
    )

    print("agreement_dict:", result)
    print("L_before:", round(L_current, 3))
    print("L_after:", round(L_after, 3))
    print("delta_L:", round(L_after - L_current, 3))
    print("U_PA:", round(u_pa, 3))
    print("  U_PA^pref:", round(U_PA_pref(result, pa_profile), 3))
    print("  U_PA^load:", round(U_PA_load(L_after, thresholds, pa_profile), 3))
    print("U_AA:", round(u_aa, 3))
    print("  U_AA^out:", round(exp(-aa_params.alpha * d_out(L_after, thresholds.L_pred_low, thresholds.L_pred_high)), 3))
    print("  U_AA^in:", round(exp(-aa_params.beta * d_in(L_after, thresholds.L_pred_low, thresholds.L_pred_high)), 3))
    print("  U_AA^change:", round(exp(-aa_params.gamma * change_cost(current, result, rho_change)), 3))

    return result
