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
from negmas.mechanisms import Mechanism

from negmas import (
    make_issue,
    SAOMechanism,
    NaiveTitForTatNegotiator,
    TimeBasedConcedingNegotiator,
    AspirationNegotiator,
)

from negmas.common import PreferencesChangeType
from negmas.sao import SAONegotiator, SAOState, ResponseType
from negmas.preferences import Preferences
from negmas.preferences import UtilityFunction
from negmas.preferences import LinearAdditiveUtilityFunction as LUFun
from negmas.preferences import PresortingInverseUtilityFunction
from negmas.preferences.value_fun import LinearFun, IdentityFun, AffineFun
from negmas.negotiators import PolyAspiration
from negmas.negotiators import Negotiator
import plotly.express as px

from random import choice
from collections import defaultdict
import math
from typing import Optional
from typing import Tuple


class NashBargainingGame(Mechanism):
    # ナッシュ交渉ゲーム

    def __init__(self, **kwargs):
        kwargs.update(dict(n_steps=1, max_n_negotiators=2, dynamic_entry=False))
        super().__init__(**kwargs, issues=[make_issue((0.0, 0.1))])
        self.add_requirements(dict(propose_for_self=True))
        self.ufuns: list[UtilityFunction] = []

    def add(
            self,
            negotiator: Negotiator,
            *,
            preferences: Preferences | None = None,
            **kwargs,
    ) -> Optional[bool]:
        added = super().add(negotiator, preferences=preferences, role=None, **kwargs)
        if added:
            self.ufuns.append(self.negotiators[-1].ufun)
    
    def is_feasible(self, outcome: Tuple[float]):
        """
        Tests feasibility of outcomes.
        The default implementation tests that the of all utilities assigned to all negotiators is less than 1.0.
        """
        return sum(u(outcome) for u in self.ufuns) <= (1.0 + 1e-3)
    
    def __call__(self, state, action = None):
        return super().__call__(state, action)