from __future__ import annotations

from dataclasses import asdict, dataclass, field
from itertools import product
import math
import random
from typing import Dict, Iterable, List, Mapping, Sequence

from Server import shared_state


IssueSetting = Dict[str, float]


def clip(value: float, low: float = 0.0, high: float = 1.0) -> float:
    """認知負荷や効用を指定範囲内に収める。"""
    return max(low, min(high, float(value)))


def concession_threshold(
    step: int,
    max_steps: int,
    initial: float,
    minimum: float,
    beta: float,
) -> float:
    """設計書のBoulware型譲歩式から、現在ステップの受諾閾値を求める。"""
    if max_steps <= 1:
        progress = 1.0
    else:
        progress = clip(step / (max_steps - 1))
    # beta < 1 のとき、序盤の譲歩を小さくして終盤に大きく譲歩する。
    exponent = 1.0 / max(float(beta), 1e-9)
    return float(initial) - (float(initial) - float(minimum)) * progress**exponent


@dataclass(frozen=True)
class Critique:
    """PAが拒否した論点と、次回提案で望む変更方向を表す。"""

    issue: str
    direction: str
    dissatisfaction: float

    def is_satisfied(self, previous: Mapping[str, float], offer: Mapping[str, float]) -> bool:
        """前回提案からCritiqueの方向へ値が動いたか判定する。"""
        if self.direction == "increase":
            return offer[self.issue] > previous[self.issue]
        return offer[self.issue] < previous[self.issue]


@dataclass
class NegotiationStep:
    """1回の提案、効用、閾値、PAの応答を記録する。"""

    step: int
    offer: IssueSetting
    predicted_load: float
    aa_utility: float
    pa_utility: float
    aa_threshold: float
    pa_threshold: float
    accepted: bool
    critiques: List[Critique] = field(default_factory=list)

    def to_dict(self) -> dict:
        data = asdict(self)
        data["offer"] = dict(self.offer)
        return data


@dataclass
class NegotiationResult:
    """交渉全体の結果と、APIへ返すための履歴を保持する。"""

    user_id: str
    agreement: IssueSetting | None
    initial_settings: IssueSetting
    final_settings: IssueSetting
    initial_load: float
    predicted_load: float
    accepted: bool
    reason: str
    steps: List[NegotiationStep]
    engine: str = "direct"

    def to_dict(self) -> dict:
        return {
            "user_id": self.user_id,
            "agreement": None if self.agreement is None else dict(self.agreement),
            "initial_settings": dict(self.initial_settings),
            "final_settings": dict(self.final_settings),
            "initial_load": self.initial_load,
            "predicted_load": self.predicted_load,
            "accepted": self.accepted,
            "reason": self.reason,
            "engine": self.engine,
            "steps": [step.to_dict() for step in self.steps],
        }


class AdjustmentAgent:
    """認知負荷を安全帯へ収める候補を生成・提案するAA。"""

    def __init__(
        self,
        *,
        current_load: float,
        current_settings: Mapping[str, float],
        issue_options: Mapping[str, Sequence[float]],
        coeffs: Mapping[str, float],
        rho: Mapping[str, float],
        load_low: float,
        load_high: float,
        threshold_initial: float = 0.95,
        threshold_minimum: float = 0.0,
        beta: float = 1.0,
        rng: random.Random | None = None,
    ) -> None:
        if load_low >= load_high:
            raise ValueError("load_low must be smaller than load_high")
        self.current_load = clip(current_load)
        self.current_settings = dict(current_settings)
        self.issue_options = {
            issue: [float(value) for value in values]
            for issue, values in issue_options.items()
        }
        self.coeffs = {issue: float(coeffs.get(issue, 0.0)) for issue in self.issue_options}
        self.rho = {issue: float(rho.get(issue, 0.0)) for issue in self.issue_options}
        self.load_low = float(load_low)
        self.load_high = float(load_high)
        self.threshold_initial = float(threshold_initial)
        self.threshold_minimum = float(threshold_minimum)
        self.beta = float(beta)
        self.rng = rng or random.Random()
        # issue_option.csv の選択肢から直積を作り、交渉中に再利用する。
        self._candidates = list(self._generate_candidates())
        self._offered: set[tuple[float, ...]] = set()

    @property
    def issues(self) -> list[str]:
        return list(self.issue_options)

    def _generate_candidates(self) -> Iterable[IssueSetting]:
        """全論点の選択肢を組み合わせ、設定候補を列挙する。"""
        option_sets = [self.issue_options[issue] for issue in self.issues]
        for values in product(*option_sets):
            yield dict(zip(self.issues, values))

    def predict_load(self, offer: Mapping[str, float]) -> float:
        """現在設定との差分とcoeffから、提案適用後の認知負荷を予測する。"""
        delta = sum(
            self.coeffs[issue] * (float(offer[issue]) - self.current_settings[issue])
            for issue in self.issues
        )
        return clip(self.current_load + delta)

    def utility(self, offer: Mapping[str, float]) -> float:
        """安全帯の中心に近い提案ほど高くなるAA効用を計算する。"""
        target = (self.load_low + self.load_high) / 2.0
        half_width = (self.load_high - self.load_low) / 2.0
        distance = abs(self.predict_load(offer) - target) / half_width
        return clip(1.0 - distance)

    def change_cost(self, offer: Mapping[str, float]) -> float:
        """rhoを使い、現在設定から提案へ変更する際の抵抗を計算する。"""
        return sum(
            self.rho[issue] * abs(float(offer[issue]) - self.current_settings[issue])
            for issue in self.issues
        )

    def threshold(self, step: int, max_steps: int) -> float:
        # 閾値τを返す関数
        return concession_threshold(
            step, max_steps, self.threshold_initial, self.threshold_minimum, self.beta
        )

    def propose(
        self,
        *,
        step: int,
        max_steps: int,
        critiques: Sequence[Critique],
        previous_offer: Mapping[str, float] | None,
    ) -> tuple[IssueSetting, float, float] | None:
        """安全性、AA閾値、Critiqueの順に候補を絞って1件提案する。"""
        threshold = self.threshold(step, max_steps)
        eligible = []
        for candidate in self._candidates:
            key = tuple(candidate[issue] for issue in self.issues)
            predicted_load = self.predict_load(candidate)
            utility = self.utility(candidate)
            if key in self._offered:
                continue
            if not self.load_low <= predicted_load <= self.load_high:
                continue
            if utility < threshold:
                continue
            eligible.append((candidate, predicted_load, utility))

        # koko
        # 安全条件と現在のAA閾値を両立する候補がない場合は交渉を終了する。
        if not eligible:
            return None

        if critiques and previous_offer is not None:
            # 全Critiqueを満たせない場合も、満たす項目数が最大の候補を優先する。
            match_counts = [
                sum(c.is_satisfied(previous_offer, candidate) for c in critiques)
                for candidate, _, _ in eligible
            ]
            best_match = max(match_counts)
            if best_match > 0:
                eligible = [
                    item for item, matches in zip(eligible, match_counts) if matches == best_match
                ]

        # 設計書どおり候補はランダム選択する。ただしrhoが大きい変更は
        # 選ばれにくくし、安全条件やCritiqueの優先順位は上書きしない。
        
        # 2026/07/22 変更コストの考慮はなし
        # weights = [math.exp(-self.change_cost(candidate)) for candidate, _, _ in eligible]
        
        candidate, predicted_load, utility = self.rng.choices(eligible, k=1)[0]
        # 同一提案の繰り返しを防ぎ、別候補を探索できるようにする。
        self._offered.add(tuple(candidate[issue] for issue in self.issues))
        return dict(candidate), predicted_load, utility


class PlayerAgent:
    """利用者の選好を代理し、提案の受諾またはCritique生成を行うPA。"""

    def __init__(
        self,
        *,
        preference: Mapping[str, float],
        weights: Mapping[str, float],
        load_low: float,
        load_high: float,
        comfort_weight: float = 0.0,
        threshold_initial: float = 0.95,
        threshold_minimum: float = 0.0,
        beta: float = 0.5,
        critique_count: int = 2,
    ) -> None:
        self.preference = {key: float(value) for key, value in preference.items()}
        raw_weights = {key: max(0.0, float(value)) for key, value in weights.items()}
        total = sum(raw_weights.values())
        # CSVの重み合計が1でなくても効用を0-1として扱えるよう正規化する。
        self.weights = (
            {key: value / total for key, value in raw_weights.items()}
            if total > 0.0
            else {key: 1.0 / len(raw_weights) for key in raw_weights}
        )
        self.load_low = float(load_low)
        self.load_high = float(load_high)
        self.comfort_weight = clip(comfort_weight)
        self.threshold_initial = float(threshold_initial)
        self.threshold_minimum = float(threshold_minimum)
        self.beta = float(beta)
        self.critique_count = max(1, int(critique_count))

    def preference_utility(self, offer: Mapping[str, float]) -> float:
        """利用者の理想値との重み付き距離から嗜好効用を計算する。"""
        distance = sum(
            self.weights[issue] * abs(float(offer[issue]) - self.preference[issue])
            for issue in self.weights
        )
        return clip(1.0 - distance)

    def comfort_utility(self, predicted_load: float) -> float:
        """予測負荷が安全帯の中心に近いほど高い快適性効用を返す。"""
        target = (self.load_low + self.load_high) / 2.0
        half_width = (self.load_high - self.load_low) / 2.0
        return clip(1.0 - abs(float(predicted_load) - target) / half_width)

    def utility(self, offer: Mapping[str, float], predicted_load: float) -> float:
        """嗜好効用と快適性効用をlambda相当の重みで混合する。"""
        preference = self.preference_utility(offer)
        comfort = self.comfort_utility(predicted_load)
        return clip(
            (1.0 - self.comfort_weight) * preference
            + self.comfort_weight * comfort
        )

    def threshold(self, step: int, max_steps: int) -> float:
        return concession_threshold(
            step, max_steps, self.threshold_initial, self.threshold_minimum, self.beta
        )

    def critiques(self, offer: Mapping[str, float]) -> List[Critique]:
        """理想値との差が大きい上位論点について、望む変更方向を返す。"""
        ranked = sorted(
            (
                Critique(
                    issue=issue,
                    direction=(
                        "increase" # 増加方向
                        if float(offer[issue]) < self.preference[issue]
                        else "decrease" # 減少方向
                    ), 
                    dissatisfaction=abs(float(offer[issue]) - self.preference[issue]),
                )
                for issue in self.preference
                if float(offer[issue]) != self.preference[issue]
            ),
            # 不満度でソート
            key=lambda critique: critique.dissatisfaction,
            reverse=True,
        )
        return ranked[: self.critique_count]


class NegotiationManager:
    """AAとPAを呼び出し、合意または終了条件まで交渉を進める。"""

    def __init__(
        self,
        *,
        load_low: float = 0.3,
        load_high: float = 0.7,
        max_steps: int = 30,
        comfort_weight: float = 0.0,
        random_seed: int | None = None,
    ) -> None:
        if max_steps < 1:
            raise ValueError("max_steps must be at least 1")
        self.load_low = float(load_low)
        self.load_high = float(load_high)
        self.max_steps = int(max_steps)
        self.comfort_weight = float(comfort_weight)
        self.random_seed = random_seed

    def negotiate(self, user_id: str, current_load: float) -> NegotiationResult:
        """shared_stateからユーザ情報を取得し、1回の交渉を実行する。"""
        # 現在設定とCSV由来の選好・係数・rhoを交渉開始時点で固定する。
        current = shared_state.get_user_issue_settings(user_id)
        profile = shared_state.get_user_profile(user_id)
        aa = AdjustmentAgent(
            current_load=current_load,
            current_settings=current,
            issue_options=shared_state.ISSUE_OPTIONS,
            coeffs=profile["coeffs"],
            rho=profile["rho"],
            load_low=self.load_low,
            load_high=self.load_high,
            rng=random.Random(self.random_seed),
        )
        pa = PlayerAgent(
            preference=profile["p"],
            weights=profile["w"],
            load_low=self.load_low,
            load_high=self.load_high,
            comfort_weight=self.comfort_weight,
        )

        history: List[NegotiationStep] = []
        critiques: List[Critique] = []
        previous_offer: IssueSetting | None = None
        last_predicted_load = clip(current_load)

        for step in range(self.max_steps):
            # PAが前回拒否した場合、Critiqueを次のAA提案へ渡す。
            proposal = aa.propose(
                step=step,
                max_steps=self.max_steps,
                critiques=critiques,
                previous_offer=previous_offer,
            )
            if proposal is None:
                # 合意がないためshared_stateは変更せず、開始時設定を返す。
                return NegotiationResult(
                    user_id=user_id,
                    agreement=None,
                    initial_settings=current,
                    final_settings=current,
                    initial_load=clip(current_load),
                    predicted_load=last_predicted_load,
                    accepted=False,
                    reason="no_feasible_offer",
                    steps=history,
                )

            offer, predicted_load, aa_utility = proposal
            pa_utility = pa.utility(offer, predicted_load)
            pa_threshold = pa.threshold(step, self.max_steps)
            accepted = pa_utility >= pa_threshold
            critiques = [] if accepted else pa.critiques(offer)
            # API応答や実験ログで判断過程を確認できるよう各ステップを保存する。
            history.append(
                NegotiationStep(
                    step=step,
                    offer=offer,
                    predicted_load=predicted_load,
                    aa_utility=aa_utility,
                    pa_utility=pa_utility,
                    aa_threshold=aa.threshold(step, self.max_steps),
                    pa_threshold=pa_threshold,
                    accepted=accepted,
                    critiques=critiques,
                )
            )
            last_predicted_load = predicted_load

            if accepted:
                # 合意時だけ共有状態を書き換え、XR側が取得する設定へ反映する。
                updated = shared_state.update_user_issue_settings(user_id, offer)
                return NegotiationResult(
                    user_id=user_id,
                    agreement=updated,
                    initial_settings=current,
                    final_settings=updated,
                    initial_load=clip(current_load),
                    predicted_load=predicted_load,
                    accepted=True,
                    reason="accepted",
                    steps=history,
                )
            previous_offer = offer

        # 最大ステップまで合意しなかった場合も、現在設定は維持する。
        return NegotiationResult(
            user_id=user_id,
            agreement=None,
            initial_settings=current,
            final_settings=current,
            initial_load=clip(current_load),
            predicted_load=last_predicted_load,
            accepted=False,
            reason="max_steps_reached",
            steps=history,
        )


def run_negotiation(
    user_id: str,
    current_load: float,
    *,
    load_low: float = 0.3,
    load_high: float = 0.7,
    max_steps: int = 30,
    comfort_weight: float = 0.0,
    random_seed: int | None = None,
) -> NegotiationResult:
    """FastAPIなど外部呼び出し用の交渉開始関数。"""
    manager = NegotiationManager(
        load_low=load_low,
        load_high=load_high,
        max_steps=max_steps,
        comfort_weight=comfort_weight,
        random_seed=random_seed,
    )
    return manager.negotiate(user_id, current_load)
