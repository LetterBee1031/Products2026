# ルビンシュタイン交渉プロトコルを作ってみよう！
"""
2人の交渉者がそれぞれ自身の効用を最大化する合意を見つけようとする
両エージェントの効用関数が共通知識である完全情報ゲーム
潜在的な合意の効用を時間と共に減少させる割引メカニズムが存在　←　これはそれぞれで違うが互いに既知
今回のチュートリアルでは指数割引の場合に着目
    tは交渉ラウンドの番号
    δ_iは交渉者iにおける割引係数
合意の初期効用が，その合意においてエージェントに割り当てられた値である場合に焦点を当てる　←　ここピンと来てない
"""

from negmas.mechanisms import Mechanism, MechanismStepResult, MechanismState

from negmas import (
    make_issue,
)

from negmas.outcomes import Outcome

from negmas.preferences import Preferences
from negmas.preferences import UtilityFunction, LinearUtilityFunction, ExpDiscountedUFun
from negmas.negotiators import Negotiator, PolyAspiration

import random
import numpy as np
from typing import Optional, Tuple, List
from abc import ABC, abstractmethod # Abstract Base Class モジュール　抽象クラスの実装に使用
from scipy.optimize import minimize
from collections import namedtuple

import plotly.graph_objects as go
from plotly.subplots import make_subplots
from attr import define
from dataclasses import field

@define
class RubinsteinMechanismState(MechanismState):
    discounts: list[float] = field(default_factory=list)

# 指数割引に着目して，単純化されたルビンシュタインメカニズム
class RubinsteinMechanism(Mechanism):
    def __init__(self, extended=False, **kwargs):
        kwargs.update(
            dict(
                issues=[
                    make_issue(values=(0.0, 1.0), name="first"),
                    make_issue(values=(0.0, 1.0), name="second"),
                ],
                max_n_negotiators=2,
                dynamic_entry=False,
                initial_state=RubinsteinMechanismState(),
            )
        )
        super().__init__(**kwargs)
        self.add_requirements(dict(propose=True, set_index=True))
        self.state.discounts = []
        self.proposals = []
        self.extended = extended

    def add(
            self, negotiator: Negotiator, discount: float = 0.95, **kwargs
    ) -> Optional[bool]:
        weights = [1,0] if len(self.negotiators) == 0 else [0, 1]
        ufun = ExpDiscountedUFun(
            LinearUtilityFunction(weights, outcome_space=self.outcome_space),
            outcome_space=self.outcome_space,
            discount=discount,
        )
        added = super().add(negotiator, ufun=ufun, role=None, **kwargs)
        if added:
            self.state.discounts.append(discount)

    # メカニズムの1ラウンド
    def __call__(self, state, action=None) -> MechanismStepResult:
        if state.step == 0:
            if len(self.negotiators) != 2:
                state.error = (True,)
                state.error_details = (f"Got {len(self.negotiators)} negotiators!!",)
                state.broken = (True,)
                return MechanismStepResult(state=state)
            for i, n in enumerate(self.negotiators):
                n.set_index(i)
        outcomes = list(n.propose(self.state) for n in self.negotiators)
        self.proposals.append(outcomes)

        # outcomesの中にNoneがあるか　一つでもNoneがあればifのなかに入る
        if any(o is None for o in outcomes):
            state.broken = True
            return MechanismStepResult(state=state)
        
        if sum(outcomes[0]) <= 1+1e-3:

            """
            なんか本来のルビンシュタインモデルに追加してる拡張機能らしい
            本来の交渉モデルだと交渉時間が無限長になり得るから，少し緩めの交渉終了条件にしてるらしい
            具体的には
            交互提案 → 同時提案に変更
            同時提案されたものをお互いに比較する
            相手の提案が自分の提案より良いものだった場合，その時点で承諾
            お互いが承諾したらそこで合意
            """
            if self.extended:
                if (
                    outcomes[0][0] <= outcomes[1][0] + 1e-5
                    and outcomes[1][1] <= outcomes[0][1] + 1e-5
                ):
                    # 相手の提案と自分の提案で小さいほうを合意案とする
                    # 基本は相手の提案を下回る自分の提案（outcomes[0][0]，outcomes[1][1]）を採用することになる
                    # 小さいほうの採用だから，資源の余りが発生しうる　なので　パレート最適解になるかは運次第かなぁ
                    state.agreement = (
                        min(outcomes[0][0], outcomes[1][0]),
                        min(outcomes[0][1], outcomes[1][1]),
                    )
                    return MechanismStepResult(state=state)
            
            # 拡張機能を使わないバージョン
            # 2つの交渉者がほぼ同じ提案をした場合に合意がなされる
            # 元のルビンシュタインモデルを再現した形
            # 合意形成までめちゃくちゃ時間かかるっぽい
            elif max(abs(outcomes[0][i] - outcomes[1][i]) for i in range(2)) < 1e-3:
                state.agreement = tuple(
                    0.5 * (outcomes[0][i] + outcomes[1][i]) for i in range(2)
                )
                return MechanismStepResult(state=state)
            return MechanismStepResult(state=state)

class RubinsteinNegotiator(Negotiator):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.add_capabilities(dict(propose=True, set_index=True))
        self.my_index = -1

    def set_index(self, indx: int) -> None:
        self.my_index = indx

    # 0~1の範囲内の2つの値をタプルとした解を提案とする
    @abstractmethod
    def propose(
        self, state: RubinsteinMechanismState, dest: str | None = None
    ) -> Outcome:
        """ あああああ """

class RandomRubinsteinNegotiator(RubinsteinNegotiator):
    def propose(
            self, state: RubinsteinMechanismState, dest: str | None = None
    ) -> Outcome:
        if self.ufun((1.0, 1.0)) < 0.0:
            return None
        r = random.random()
        return r, 1-r

# 追加1 ルビンシュタインモデルにおける最適な交渉者の実装 交渉の実施
class OptimalRubinsteinNegotiator(RubinsteinNegotiator):
    def propose(
            self, state: RubinsteinMechanismState, dest: str | None = None
    ) -> Outcome:
        first = (1 - state.discounts[1])/ (1 - state.discounts[1] * state.discounts[0])
        return first, 1 - first

# 追加2 他のエージェントの情報を使用しない交渉者　締め切り時間が近づくと譲歩
class AspirationRubinsteinNegotiator(RubinsteinNegotiator):
    def __init__(self, *args,  aspiration_type="linear", max_aspiration=1.0,**kwargs):
        super().__init__(*args, **kwargs)
        self._asp = PolyAspiration(max_aspiration, aspiration_type)
    
    def propose(
            self, state: RubinsteinMechanismState, dest: str | None = None
    ) -> Outcome:
        if self.ufun((1.0, 1.0)) < 0.0:
            return None
        r = self._asp.utility_at(state.relative_time)
        return (r, 1.0 - r) if self.my_index == 0 else (1.0 - r, r)

def plot_a_run(mechanism: RubinsteinMechanism) -> None:
    result = mechanism.state
    x = np.linspace(0.0, 1.0, 101, endpoint=True)
    first = np.array([_[0] for _ in mechanism.proposals])
    second = np.array([_[1] for _ in mechanism.proposals])

    fig = go.Figure()
    fig.add_trace(
        go.Scatter(
            x=x, y=1-x, mode="lines", line=dict(color="gray"), name="Pareto-front"
        )
    )

    fig.add_trace(
        go.Scatter(
            x=first[:, 0],
            y=first[:, 1],
            mode="markers",
            marker=dict(symbol="x", color="green", size=5),
            name="Proposals from 1",
        )
    )
    fig.add_trace(
        go.Scatter(
            x=second[:, 0],
            y=second[:, 1],
            mode="markers",
            marker=dict(symbol="cross", color="blue", size=5),
            name="Proposals from 2",
        )
    )
    if result.agreement is not None:
        fig.add_trace(
            go.Scatter(
                x=[result.agreement[0]],
                y=[result.agreement[1]],
                mode="markers",
                marker=dict(symbol="circle", color="red", size=6),
                name="Agreement",
            )
        )
    fig.update_layout(xaxis_title="Agent 1's utility", yaxis_title="Agent 2's utility")

    fig.write_image("TutorialNewMechanismRubinstein_8.png")

# mechanism = RubinsteinMechanism()
# mechanism.add(RandomRubinsteinNegotiator(), discount=0.75)
# mechanism.add(RandomRubinsteinNegotiator(), discount=0.75)
# print(f"Agree to: {mechanism.run().agreement} after {mechanism.current_step} steps")
# plot_a_run(mechanism)

# 追加1 ルビンシュタインモデルにおける最適な交渉者の実装 交渉の実施
# mechanism = RubinsteinMechanism()
# mechanism.add(OptimalRubinsteinNegotiator())
# mechanism.add(OptimalRubinsteinNegotiator())
# print(f"Agree to: {mechanism.run().agreement} after {mechanism.current_step} steps")
# plot_a_run(mechanism)

# 追加2 他のエージェントの情報を使用しない交渉者　締め切り時間が近づくと譲歩
# mechanism = RubinsteinMechanism(n_steps=100)
# mechanism.add(AspirationRubinsteinNegotiator())
# mechanism.add(AspirationRubinsteinNegotiator())
# result = mechanism.run()
# print(f"Agree to: {result.agreement} after {mechanism.current_step} steps")
# plot_a_run(mechanism)

# 変更3 拡張機能をオンにしてみる 　そして最初の交渉者がconceder（譲歩者）であった場合を見てみよう
# mechanism = RubinsteinMechanism(n_steps=100, extended=True)
# mechanism.add(AspirationRubinsteinNegotiator(aspiration_type="conceder"))
# mechanism.add(AspirationRubinsteinNegotiator())
# result = mechanism.run()
# print(f"Agree to: {result.agreement} after {mechanism.current_step} steps")
# plot_a_run(mechanism)

# 変更4 最初の交渉者がboulware戦略（期限に向けてゆっくり譲歩する戦略）の場合も見てみよう
# mechanism = RubinsteinMechanism(n_steps=100, extended=True)
# mechanism.add(AspirationRubinsteinNegotiator(aspiration_type="boulware"))
# mechanism.add(AspirationRubinsteinNegotiator())
# result = mechanism.run()
# print(f"Agree to: {result.agreement} after {mechanism.current_step} steps")
# plot_a_run(mechanism)

# 変更5 両方の交渉者をboulwareにしてみると？
# mechanism = RubinsteinMechanism(n_steps=100, extended=True)
# mechanism.add(AspirationRubinsteinNegotiator(aspiration_type="boulware"))
# mechanism.add(AspirationRubinsteinNegotiator(aspiration_type="boulware"))
# print(f"Agreed to: {mechanism.run().agreement} in {mechanism.current_step} steps")
# plot_a_run(mechanism)

# 変更6 2者の割引率が異なっていたら？
# mechanism = RubinsteinMechanism()
# mechanism.add(OptimalRubinsteinNegotiator(), discount=0.95)
# mechanism.add(OptimalRubinsteinNegotiator(), discount=0.9)
# print(f"Agreed to: {mechanism.run().agreement} in {mechanism.current_step} steps")
# plot_a_run(mechanism)

# 変更7 boulware戦略に割引率を試してみたら？
mechanism = RubinsteinMechanism(n_steps=100, extended=True)
mechanism.add(AspirationRubinsteinNegotiator(aspiration_type="boulware"), discount=0.95)
mechanism.add(AspirationRubinsteinNegotiator(aspiration_type="boulware"), discount=0.9)
print(f"Agreed to: {mechanism.run().agreement} in {mechanism.current_step} steps")
plot_a_run(mechanism)
"""
形だけ見るとナッシュ交渉ゲームに似てる．違いは以下の通り
1. add()関数で，指数関数的に割引された効用関数を交渉者は受け取る
2. discount(割引率)を与えられるよう，stateを拡張
3. 交渉者におけるpropose()はRubinsteinMechanismStateタイプのstateを受け取れるようにする
4. 各ラウンドで全ての交渉担当者が結果を提案，両方の提案が実現可能(合計が1.0以下)かつほぼ等しい場合にのみ交渉は成功裏に終了
"""

"""
追加1 ルビンシュタインモデルにおける最適な交渉者の実装 交渉の実施
ルービンシュタインが提唱した，1ラウンドでの均衡解に関する式をもとに提案する交渉者を追加
同じ割引率でも先に交渉を始めたほうが有利になる
大体想像通りの結果に
Agree to: (0.5128205128205131, 0.4871794871794869) after 1 steps
"""

"""
追加2 他のエージェントの情報を使用しない交渉者　締め切り時間が近づくと譲歩
解が合意に至りませんでした
Agree to: None after 100 steps
全く同じ提案にはなかなか至りませんよねそれは
"""

"""
変更3 拡張機能をオンにしてみる そして最初の交渉者がconceder（譲歩者）であった場合を見てみよう
合意は交渉者2に有利に働いた
Agree to: (0.27438013387778515, 0.7227722772277227) after 28 steps
交渉者1が譲歩しやすい人らしいから予想通りの結果と言える
"""

"""
変更4 最初の交渉者がboulware戦略（期限に向けてゆっくり譲歩する戦略）の場合も見てみよう
合意は交渉者1に有利に働いた
Agree to: (0.7118348986565985, 0.26732673267326734) after 74 steps
交渉者1があんまり譲歩しないから予想通りの結果と言える
"""

"""
変更5 両方の交渉者をboulwareにしてみると？
お互いが全く同じ均衡状態に
Agreed to: (0.498362254052817, 0.498362254052817) in 85 steps
"""

"""
変更6 2者の割引率が異なっていたら？
割引率が低いほうが不利 元の式的に当たり前の話ではある
Agreed to: (0.6896551724137928, 0.3103448275862072) in 1 steps
"""

"""
変更7 boulware戦略に割引率を試してみたら？
変更5で両方をboulwareにしたときと変わらずである　まあ，AspirationRubinsteinNegotiator()内でdiscountの処理を何もしてないから当たり前だよね！！！
Agreed to: (0.498362254052817, 0.498362254052817) in 85 steps

ということで本日は寝る
"""