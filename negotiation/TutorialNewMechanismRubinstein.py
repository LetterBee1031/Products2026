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

from negmas.preferences import Preferences
from negmas.preferences import UtilityFunction, LinearUtilityFunction
from negmas.negotiators import Negotiator

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
    discounts = field(factory=list)

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
            self, negotiator: Negotiator, discount: float = 0.95, 
    )