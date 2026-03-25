# 新しいメカニズム（プロトコル）を開発する
"""
新しいメカニズムの作り方
1. 新しいMechanismクラスを作成し，round()メソッドをオーバーライドしてプロトコルの1ラウンドを実装
    必要に応じて，__init__関数をオーバーライドして，メカニズム初期化のためのコンストラクタを提供する
    コンストラクタ内でadd_requirements()を使って，メカニズムに参加する交渉者が満たさなければならない要件を定義
2. カスタムメカニズムクラスが呼び出すメソッドに対応する抽象メソッドを持つNegotiatorクラスを作成
    ↑は新しいメカニズムと互換性のあるすべての交渉者の基底クラスになる
    新しい交渉者の基底クラスの__init__メソッド内で，add_capabilities()を使って必要な能力を設定
3. 必要に応じて，交渉者に渡す追加の状態情報を含む新しいMechanismStateデータクラスを作成
    メカニズムクラスのextra_state()メソッドをオーバーライドして，追加状態を辞書形式で返すようにする
    Mechanismコンストラクタに渡すstate_factoryの引数も，この新しく作成した状態型を使うように変更する必要あり
    この手順は任意．stateを通じて交渉者に追加状態を渡す必要がある場合にのみ必要
    メカニズムにうち公開可能なすべての変数情報は，state変数に保持しておくことが推奨される
    ↑によってメカニズム実行後にhistoryプロパティを通じてstateの履歴にアクセスできるようになる
"""

# ナッシュ交渉ゲームを作ってみよう！
"""
完全情報二者間交渉の単一ステップ
"""
from negmas.mechanisms import Mechanism, MechanismStepResult

from negmas import (
    make_issue,
)

from negmas.preferences import Preferences
from negmas.preferences import UtilityFunction, LinearUtilityFunction
from negmas.negotiators import Negotiator

import random
import numpy as np
from typing import Optional, Tuple, List
from abc import ABC, abstractmethod # Abstract Base Class モジュール　抽象クラスの実装に使用
from scipy.optimize import minimize

import plotly.graph_objects as go
from plotly.subplots import make_subplots

class NashBargainingGame(Mechanism):
    # ナッシュ交渉ゲーム
    # コンストラクタの定義
    def __init__(self, **kwargs):
        kwargs.update(dict(n_steps=1, max_n_negotiators=2, dynamic_entry=False))
        # サンプルコードでは論点が一個なのに，交渉者の論点とかが2個になっててエラー吐いた．なので論点追加した
        super().__init__(**kwargs, issues=[make_issue((0.0, 1.0), "f0"), make_issue((0.0, 1.0), "f1")])
        self.add_requirements(dict(propose_for_self=True))
        self.ufuns: list[UtilityFunction] = []

    # 交渉者の追加
    def add(
            self,
            negotiator: Negotiator,
            *,
            preferences: Preferences | None = None,
            **kwargs,
    ) -> Optional[bool]: # Optional型　なんか既定の型でなくNoneが帰ってきても許容してくれる型らしい お友達にUnion型がいる
        added = super().add(negotiator, preferences=preferences, role=None, **kwargs)
        if added:
            self.ufuns.append(self.negotiators[-1].ufun) # 番号の-1指定って配列とかリストとかの一番最後のところを意味してるっぽい

    """
    交渉解の実現可能性のテスト
    全交渉者に割り当てられた効用の合計が1以下である，デフォルトの実装のテスト
    """
    def is_feasible(self, outcome: Tuple[float]):
        # 解に対する全体効用が1.003以下か判定．0.003足してるのは丸め誤差とかで1をちょっと超えちゃったときの対策すかね
        return sum(u(outcome) for u in self.ufuns) <= (1.0 + 1e-3) 
    
    # メカニズムの1ラウンドごと
    def __call__(self, state, action = None) -> MechanismStepResult:
        # 2者間交渉じゃなかったらエラー，って感じかな
        if len(self.negotiators) != 2:
            state.has_error = True
            state.error_details = f"Got {len(self.negotiators)} negotiators!!"
            state.broken = True

        # 交渉者の数だけ繰り返し
        outcome = tuple(
            n.propose_for_self(self.ufuns, i) for i, n in enumerate(self.negotiators)
        )

        # 合計1.003以下ならoutcomeを渡す
        if self.is_feasible(outcome):
            state.agreement = outcome

        return MechanismStepResult(state=state)


# ナッシュ交渉ゲームにおいて交渉可能なすべての交渉者の基底クラス
class NashBargainingNegotiator(Negotiator, ABC):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.add_capabilities(dict(propose_for_self=True))

    # 抽象メソッドを定義　実数で交渉解を提出・提案
    @abstractmethod
    def propose_for_self(self, ufuns: List[UtilityFunction], my_indx: int) -> float:
        """"""

# 交渉ゲームにおけるナッシュ解の実装
class NashNegotiator(NashBargainingNegotiator):
    def propose_for_self(self, ufuns: List[UtilityFunction], my_indx: int) -> float:
        # 最初ラムダ式でやってたけど，チャッピーが修正してきたのでそれに従った部分．まあこっちの方が分かりやすい
        # 内容としてはナッシュ積の式　なぜ全体にマイナスつけてるのかはいまいちわかってない
        # あとでminimizeに渡すためにやってるらしい　ナッシュ積の最大化 = 負のナッシュ積の最小化　みたいなやりかたらしい
        def objective(x):
            f0 = float(x[0])
            return -(
                (ufuns[0]((f0, 1.0 -f0)) - ufuns[0].reserved_value)
                 * (ufuns[1]((f0, 1.0 - f0)) - ufuns[1].reserved_value)
            )
        while True:
            # minimizeの返り値は最適化された解の配列x，最適化が正常終了したかのフラグsuccess，終了原因messageらしい
            result = minimize(
                objective,  # 最小化したい関数
                x0=[random.random()],  # 最小化の際の初期値　今回はランダムで渡してる．局所最適化問題が起こり得るからランダムなんすかね
                bounds=[(0.0, 1.0)] # 最小値と最大値の指定
            )
            if result.success:
                break
        
        # ここはwhileの中にあったやつを外に出した
        # 多分，breakされたあとにreturnがあったから，何も返さない状態になってたっぽい
        # result.x[0]には提案によい最適化された解が入るということか
        f0 = float(result.x[0])
        # my_indxは自分の交渉者番号
        return f0 if my_indx == 0 else 1.0 - f0


# 結果出力1 とりあえず一回試してみる
# m = NashBargainingGame()
# u1 = LinearUtilityFunction([0,0], reserved_value=0.0, outcome_space=m.outcome_space)
# u2 = LinearUtilityFunction([0,1], reserved_value=0.0, outcome_space=m.outcome_space)
# m.add(NashNegotiator(ufun=u1, name="a1"))
# m.add(NashNegotiator(ufun=u2, name="a2"))
# result = m.run()
# print(f"Agreement: {result.agreement}")


# # 結果出力2 予約値を片側だけ上げていってみる
# u1values, u2values = np.zeros(101), np.zeros(101)
# a1values, a2values = np.zeros(101), np.zeros(101)

# # 0.00～1.00まで0.01刻みの値を格納
# values = np.linspace(0.0, 1.0, 101, endpoint=True)

# # 0.00~1.00までの101回ループ　enumerateでそれに番号も振ってる
# for i, r in enumerate(values):
#     m = NashBargainingGame()
#     # u1だけresarved_valueがrになってるので，片側だけだんだんと予約値が上がっていく
#     u1 = LinearUtilityFunction([1, 0], reserved_value=r, outcome_space=m.outcome_space)
#     u2 = LinearUtilityFunction([0, 1], reserved_value=0.0, outcome_space=m.outcome_space)

#     m.add(NashNegotiator(ufun=u1, name="a1"))
#     m.add(NashNegotiator(ufun=u2, name="a2"))
#     result = m.run()

#     # 各回の交渉結果の効用を追加
#     u1values[i] = u1(result.agreement)
#     u2values[i] = u2(result.agreement)
#     a1values[i], a2values[i] = result.agreement

# fig = make_subplots(
#     rows=2, cols=1, subplot_titles=("Utility Received", "Agreement Reached")
# )
# fig.add_trace(
#     go.Scatter(x=values, y=u1values, mode="lines", name="First negotiator"),
#     row=1,
#     col=1,
# )
# fig.add_trace(
#     go.Scatter(x=values, y=u2values, mode="lines", name="Second negotiator"),
#     row=1,
#     col=1,
# )
# fig.add_trace(
#     go.Scatter(x=values, y=u1values+u2values, mode="lines", name="Welfare"),
#     row=1,
#     col=1,
# )
# fig.add_trace(
#     go.Scatter(x=values, y=a1values, mode="lines", name="First negotiator"),
#     row=2,
#     col=1,
# )
# fig.add_trace(
#     go.Scatter(x=values, y=a2values, mode="lines", name="Second negotiator"),
#     row=2,
#     col=1,
# )
# fig.update_xaxes(title_text="Reservation value for first negotiator", row=1, col=1)
# fig.update_xaxes(title_text="Reservation value for first negotiator", row=2, col=1)
# fig.update_yaxes(title_text="Utility received", row=1, col=1)
# fig.update_yaxes(title_text="Agreement Reached", row=2, col=1)
# fig.update_layout(height=600)

# fig.write_image("TutorialNewMechanism.png")

# 結果出力3 効用値の傾きを片側だけ上げていってみる
u1values, u2values = np.zeros(101), np.zeros(101)
a1values, a2values = np.zeros(101), np.zeros(101)

# 0.00～1.00まで0.01刻みの値を格納
slopes = np.linspace(0.0, 1.0, 101, endpoint=True)

# 0.00~1.00までの101回ループ　enumerateでそれに番号も振ってる
for i, s in enumerate(slopes):
    m = NashBargainingGame()
    # u1だけ1要素目の効用値がsになってる
    u1 = LinearUtilityFunction([s, 0], reserved_value=0.0, outcome_space=m.outcome_space)
    u2 = LinearUtilityFunction([0, 1], reserved_value=0.0, outcome_space=m.outcome_space)

    m.add(NashNegotiator(ufun=u1, name="a1"))
    m.add(NashNegotiator(ufun=u2, name="a2"))
    result = m.run()

    # 各回の交渉結果の効用を追加
    u1values[i] = u1(result.agreement)
    u2values[i] = u2(result.agreement)
    a1values[i], a2values[i] = result.agreement

fig = make_subplots(
    rows=2, cols=1, subplot_titles=("Utility Received", "Agreement Reached")
)
fig.add_trace(
    go.Scatter(x=slopes, y=u1values, mode="lines", name="First negotiator"),
    row=1,
    col=1,
)
fig.add_trace(
    go.Scatter(x=slopes, y=u2values, mode="lines", name="Second negotiator"),
    row=1,
    col=1,
)
fig.add_trace(
    go.Scatter(x=slopes, y=u1values+u2values, mode="lines", name="Welfare"),
    row=1,
    col=1,
)
fig.add_trace(
    go.Scatter(x=slopes, y=a1values, mode="lines", name="First negotiator"),
    row=2,
    col=1,
)
fig.add_trace(
    go.Scatter(x=slopes, y=a2values, mode="lines", name="Second negotiator"),
    row=2,
    col=1,
)
fig.update_xaxes(title_text="Slope value for first negotiator", row=1, col=1)
fig.update_xaxes(title_text="Slope value for first negotiator", row=2, col=1)
fig.update_yaxes(title_text="Utility received", row=1, col=1)
fig.update_yaxes(title_text="Agreement Reached", row=2, col=1)
fig.update_layout(width = 1500, height=600)

fig.write_image("TutorialNewMechanismSlope.png")


"""
結果出力1
結果が(0.4999981778695653, 0.5000000033414473)となった
どっちも0.5付近で公平な結果といえる
だいたい予想通り
"""

"""
結果出力2
予約値が上がっていったほうだけどんどん効用が上がっていって，もう一方は下がっていく感じになった
想像通りといえば想像通りなのだが, なぜ予約値が0.5より小さいときにもどんどん上がっていくのだ？と疑問に思った
ナッシュ解の定義を見たら納得が得られた
ナッシュ解はその解の効用からそれぞれ予約値を引いた値を掛け合わせるからだった

例えば予約値がともに0だった場合，(0.5, 0.5)あたりが最適解となる
最適 　　　(0.5-0.0) × (0.5-0.0) = 0.25
その他の例 (0.6-0.0) × (0.4-0.0) = 0.24
予約値が片側だけ0.2だった場合，(0.6, 0.4)あたりが最適になる
最適 　　　　　　　　(0.6-0.2) × (0.4-0.0) = 0.4 × 0.4 = 0.16
かつて最適だったもの (0.5-0.2) × (0.5-0.0) = 0.3 × 0.5 = 0.15

こんな感じになるので最初から片側だけ上がっていくのは当たり前の話ではあったみたい
数学って難しいっすね
"""

"""
結果出力3 
基本的には（0.5, 0.5）が最適解という結果を維持しつつ，重みが変化してる方はどんどん効用が上がっていき，それに従って全体効用が上がる
固定されてる方はずっと固定
ただ，重みが変化してる方が[0,0]のときだけ交渉解がバラバラになる
多分ナッシュ積で0かけちゃってるからどんな値でも全体効用が0になるって状況が発生して，結局ランダムのままで落ち着くと思われる
"""