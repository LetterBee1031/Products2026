from __future__ import annotations

from dataclasses import dataclass, field
import random
from typing import Mapping, Sequence

from negmas import ResponseType, make_issue
from negmas.outcomes import Outcome
from negmas.sao import SAOMechanism, SAONegotiator, SAOState
from negmas.sao.common import ExtendedResponseType

from Server import shared_state
from negotiation.protocol import (
    AdjustmentAgent,
    Critique,
    IssueSetting,
    NegotiationResult,
    NegotiationStep,
    PlayerAgent,
    clip,
)


def outcome_to_setting(outcome: Outcome, issues: Sequence[str]) -> IssueSetting:
    """NegMASのOutcomeを、shared_stateで使う論点名付きdictへ変換する。"""
    # NegMASのOutcomeは設定によってdictまたはtupleとして返るため、
    # 以降の効用計算では常に {論点名: 数値} の形へ統一する。
    if isinstance(outcome, Mapping):
        return {issue: float(outcome[issue]) for issue in issues}
    return {issue: float(outcome[index]) for index, issue in enumerate(issues)}


def setting_to_outcome(setting: Mapping[str, float], issues: Sequence[str]) -> tuple[float, ...]:
    """論点名付きdictをNegMASの離散Outcomeへ変換する。"""
    # tuple内の値順は、SAOMechanismへ渡したissue順と一致させる必要がある。
    return tuple(float(setting[issue]) for issue in issues)


@dataclass
class NegotiationChannel:
    """NegMASエージェント間でCritiqueと監査用履歴を共有する。"""

    # AAとPAは別々のSAONegotiatorなので、設計書独自のCritiqueや
    # 各ステップの効用をこの共有オブジェクト経由で受け渡す。
    issues: list[str]
    max_steps: int
    # PAが直前の提案を拒否した理由。次のAA提案で優先条件として使う。
    critiques: list[Critique] = field(default_factory=list)
    # Critiqueが「前回より上げる・下げる」を評価する基準となる提案。
    previous_offer: IssueSetting | None = None
    # 以下のpending値は、AAが生成した最新提案の監査情報を保持する。
    pending_offer: IssueSetting | None = None
    pending_load: float = 0.0
    pending_aa_utility: float = 0.0
    # 安全条件とAA閾値を満たす候補がなくなったことを終了理由へ反映する。
    no_feasible_offer: bool = False
    # API応答や実験結果で交渉過程を確認するための独自履歴。
    steps: list[NegotiationStep] = field(default_factory=list)


class NegmasAdjustmentAgent(SAONegotiator):
    """NegMASのSAONegotiatorを拡張した、提案担当のAA。"""

    def __init__(
        self,
        *,
        strategy: AdjustmentAgent,
        channel: NegotiationChannel,
        name: str = "AA",
    ) -> None:
        # can_propose=Trueにより、NegMASのSAOから提案者として呼び出される。
        super().__init__(name=name, can_propose=True)
        self.strategy = strategy
        self.channel = channel

    def propose(self, state: SAOState, dest: str | None = None) -> Outcome | None:
        # PAの直前のCritiqueを使い、安全集合から次の提案を探索する。
        # state.stepはNegMASが管理する現在ラウンド番号であり、
        # AAのBoulware型閾値計算にも同じ値を利用する。
        proposal = self.strategy.propose(
            step=state.step,
            max_steps=self.channel.max_steps,
            critiques=self.channel.critiques,
            previous_offer=self.channel.previous_offer,
        )
        if proposal is None:
            # Noneを返すとSAOは交渉終了へ進む。原因を結果へ残すため
            # channelにも候補枯渇フラグを保存する。
            self.channel.no_feasible_offer = True
            return None

        offer, predicted_load, aa_utility = proposal
        # NegMASへ渡す前に、提案時点の予測値を共有チャネルへ記録する。
        self.channel.pending_offer = offer
        self.channel.pending_load = predicted_load
        self.channel.pending_aa_utility = aa_utility
        return setting_to_outcome(offer, self.channel.issues)

    def respond(
        self,
        state: SAOState,
        source: str | None = None,
    ) -> ResponseType:
        # PAは提案能力を持たないため、通常AAが相手提案へ応答することはない。
        # SAONegotiatorの抽象的な応答契約を満たすため、保守的にRejectを返す。
        return ResponseType.REJECT_OFFER


class NegmasPlayerAgent(SAONegotiator):
    """NegMASのSAONegotiatorを拡張した、応答専用のPA。"""

    def __init__(
        self,
        *,
        strategy: PlayerAgent,
        channel: NegotiationChannel,
        load_predictor: AdjustmentAgent,
        name: str = "PA",
    ) -> None:
        # PAの提案能力を無効化し、AAだけが提案する設計をSAO上で表現する。
        # can_propose=Falseでも、現在提案へのrespond()はNegMASから呼ばれる。
        super().__init__(name=name, can_propose=False)
        self.strategy = strategy
        self.channel = channel
        self.load_predictor = load_predictor

    def respond(
        self,
        state: SAOState,
        source: str | None = None,
    ) -> ResponseType | ExtendedResponseType:
        # 初回など、まだAA案が存在しない状態では評価対象がない。
        if state.current_offer is None:
            return ResponseType.REJECT_OFFER

        # NegMASのOutcomeを設計書の効用関数が扱えるdictへ戻す。
        offer = outcome_to_setting(state.current_offer, self.channel.issues)
        # PAも提案の快適性を評価するため、AAと同じ負荷予測結果を利用する。
        predicted_load = self.load_predictor.predict_load(offer)
        aa_utility = self.load_predictor.utility(offer)
        pa_utility = self.strategy.utility(offer, predicted_load)
        # AAとPAは異なるbetaを持つため、同じラウンドでも閾値が異なる。
        aa_threshold = self.load_predictor.threshold(
            state.step, self.channel.max_steps
        )
        pa_threshold = self.strategy.threshold(state.step, self.channel.max_steps)
        # 設計書どおり、PA効用が現在のPA閾値以上なら合意とする。
        accepted = pa_utility >= pa_threshold
        critiques = [] if accepted else self.strategy.critiques(offer)

        # NegMAS自身も交渉履歴を保持するが、効用やCritiqueを含む
        # 実験向け履歴はNegotiationStepとして別途保存する。
        self.channel.steps.append(
            NegotiationStep(
                step=state.step,
                offer=offer,
                predicted_load=predicted_load,
                aa_utility=aa_utility,
                pa_utility=pa_utility,
                aa_threshold=aa_threshold,
                pa_threshold=pa_threshold,
                accepted=accepted,
                critiques=critiques,
            )
        )

        if accepted:
            self.channel.critiques = []
            # ExtendedResponseTypeを使うとAcceptと同時に評価値を
            # NegMAS側のresponse dataへ添付できる。
            return ExtendedResponseType(
                ResponseType.ACCEPT_OFFER,
                data={
                    "decision": "accept",
                    "pa_utility": pa_utility,
                    "pa_threshold": pa_threshold,
                },
            )

        # NegMASのdata付き拒否を使い、具体的な再提案は行わずCritiqueだけ返す。
        # PAはcan_propose=Falseなので、Reject後の次提案者は再びAAになる。
        self.channel.previous_offer = offer
        self.channel.critiques = critiques
        return ExtendedResponseType(
            ResponseType.REJECT_OFFER,
            data={
                "decision": "reject",
                "critiques": [
                    {
                        "issue": critique.issue,
                        "direction": critique.direction,
                        "dissatisfaction": critique.dissatisfaction,
                    }
                    for critique in critiques
                ],
            },
        )


class NegmasNegotiationManager:
    """NegMASのSAOMechanismを構築し、shared_stateとの境界を管理する。"""

    def __init__(
        self,
        *,
        load_low: float = 0.3,
        load_high: float = 0.7,
        max_steps: int = 20,
        comfort_weight: float = 0.1,
        random_seed: int | None = None,
        persist_agreement: bool = True,
    ) -> None:
        if max_steps < 1:
            raise ValueError("max_steps must be at least 1")
        # Managerは交渉条件だけを保持し、ユーザ固有情報はnegotiate時に読む。
        self.load_low = float(load_low)
        self.load_high = float(load_high)
        self.max_steps = int(max_steps)
        self.comfort_weight = float(comfort_weight)
        self.random_seed = random_seed
        # FastAPIではTrue、結果確認だけのテスト実行ではFalseを指定する。
        self.persist_agreement = bool(persist_agreement)

    def negotiate(self, user_id: str, current_load: float) -> NegotiationResult:
        # shared_stateから現在設定、PA選好、AA係数を交渉開始時に取得する。
        # 交渉途中で外部状態が変わっても、このセッションの計算条件は変えない。
        current = shared_state.get_user_issue_settings(user_id)
        profile = shared_state.get_user_profile(user_id)
        issues = list(shared_state.ISSUE_OPTIONS)
        # channelはNegMAS標準状態に含まれないCritiqueと効用履歴を共有する。
        channel = NegotiationChannel(
            issues=issues,
            max_steps=self.max_steps,
            pending_load=clip(current_load),
        )

        # 数式や候補探索は直接実装したstrategyへ委譲し、
        # NegMAS拡張クラスはSAOとの接続と役割制御を担当する。
        aa_strategy = AdjustmentAgent(
            current_load=current_load,
            current_settings=current,
            issue_options=shared_state.ISSUE_OPTIONS,
            coeffs=profile["coeffs"],
            rho=profile["rho"],
            load_low=self.load_low,
            load_high=self.load_high,
            rng=random.Random(self.random_seed),
        )
        pa_strategy = PlayerAgent(
            preference=profile["p"],
            weights=profile["w"],
            load_low=self.load_low,
            load_high=self.load_high,
            comfort_weight=self.comfort_weight,
        )

        # issue_option.csvの各行をNegMASの離散issueへ変換する。
        # これによりOutcome SpaceはCSVの論点・選択肢と一致する。
        mechanism = SAOMechanism(
            issues=[
                make_issue(
                    name=issue,
                    values=[float(value) for value in shared_state.ISSUE_OPTIONS[issue]],
                )
                for issue in issues
            ],
            n_steps=self.max_steps,
            # PAのdata付き拒否を許可し、提案なしでも次ラウンドへ進める。
            allow_none_with_data=True,
            # AAが候補を返せない場合は交渉を終了する。
            end_on_no_response=True,
            # AAが提案した時点でAA自身はその案を受諾済みとして扱う。
            # PAもAcceptすれば全参加者の受諾が揃い、agreementが確定する。
            offering_is_accepting=True,
            # partner proposal/responseなどのNegMASコールバックを有効にする。
            extra_callbacks=True,
        )
        aa = NegmasAdjustmentAgent(strategy=aa_strategy, channel=channel)
        pa = NegmasPlayerAgent(
            strategy=pa_strategy,
            channel=channel,
            load_predictor=aa_strategy,
        )
        # roleはNegMAS上で参加者の役割を識別しやすくするために設定する。
        mechanism.add(aa, role="adjustment")
        mechanism.add(pa, role="player")
        # ラウンド進行、応答順、合意成立、最大ステップ判定はNegMASへ委譲する。
        final_state = mechanism.run()

        if final_state.agreement is not None:
            # NegMASが全参加者の受諾を確認したOutcomeを設定dictへ戻す。
            agreement = outcome_to_setting(final_state.agreement, issues)
            if self.persist_agreement:
                # FastAPIからの本番実行では、合意案をXR向け共有状態へ反映する。
                final_settings = shared_state.update_user_issue_settings(
                    user_id, agreement
                )
            else:
                # テスト実行では合意案を結果へ載せるだけで、共有状態は変更しない。
                final_settings = dict(agreement)
            # 返却する負荷値は、合意案に対して同じモデルで再計算する。
            predicted_load = aa_strategy.predict_load(final_settings)
            return NegotiationResult(
                user_id=user_id,
                agreement=final_settings,
                initial_settings=current,
                final_settings=final_settings,
                initial_load=clip(current_load),
                predicted_load=predicted_load,
                accepted=True,
                reason="accepted",
                steps=channel.steps,
                engine="negmas",
            )

        # NegMASの終了状態とAA側フラグから、人が確認しやすい終了理由へ変換する。
        reason = (
            "no_feasible_offer"
            if channel.no_feasible_offer
            else "max_steps_reached"
            if final_state.timedout
            else "negotiation_ended"
        )
        # 合意なしでも最終提案時点の予測負荷を結果に残す。
        predicted_load = (
            channel.steps[-1].predicted_load
            if channel.steps
            else clip(current_load)
        )
        return NegotiationResult(
            user_id=user_id,
            agreement=None,
            initial_settings=current,
            final_settings=current,
            initial_load=clip(current_load),
            predicted_load=predicted_load,
            accepted=False,
            reason=reason,
            steps=channel.steps,
            engine="negmas",
        )


def run_negotiation(
    user_id: str,
    current_load: float,
    *,
    load_low: float = 0.3,
    load_high: float = 0.7,
    max_steps: int = 20,
    comfort_weight: float = 0.1,
    random_seed: int | None = None,
    persist_agreement: bool = True,
) -> NegotiationResult:
    """FastAPIから呼び出すNegMAS版交渉エントリーポイント。"""
    # 呼び出し側がNegMASのクラス構成を意識せず交渉を開始できるよう、
    # Manager生成とnegotiate呼び出しをこの関数にまとめる。
    manager = NegmasNegotiationManager(
        load_low=load_low,
        load_high=load_high,
        max_steps=max_steps,
        comfort_weight=comfort_weight,
        random_seed=random_seed,
        persist_agreement=persist_agreement,
    )
    return manager.negotiate(user_id, current_load)
