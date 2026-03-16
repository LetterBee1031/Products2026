# 新しい交渉担当者を育成
# SAOP用の新しいエージェントを開発するプロセスについて

# 議題の作成
# 単純な二者間交渉

from negmas import (
    make_issue,
    SAOMechanism,
    NaiveTitForTatNegotiator,
    TimeBasedConcedingNegotiator,
    AspirationNegotiator,    
)
from negmas.sao import SAONegotiator
from negmas.preferences import LinearAdditiveUtilityFunction as LUFun
from negmas.preferences.value_fun import LinearFun, IdentityFun, AffineFun

class RandomNegotiator(SAONegotiator):
    def propose(self, state, dest: str | None = None):
        return self.nmi.random_outcomes(1)[0] 



# 交渉問題の作成
issues = [
    make_issue(name="price", values=10),
    make_issue(name="quantity", values=(1,11)),
    make_issue(name="delivery_time", values=10),
]

# メカニズムの作成
session = SAOMechanism(issues=issues, n_steps=20)

# 効用関数の定義
seller_utility = LUFun(
    values={
        "price": IdentityFun(),
        "quantity": LinearFun(0.2),
        "delivery_time": AffineFun(-1, 9),
    },
    weights={"price":1.0, "quantity":1.0, "delivery_time":10.0},
    outcome_space=session.outcome_space,
    reserved_value=15,
).scale_max(1.0)

buyer_utility = LUFun(
    values={
        "price": AffineFun(-1, 9.0),
        "quantity": LinearFun(0.2),
        "delivery_time": IdentityFun(),
    },
    outcome_space=session.outcome_space,
    reserved_value=10,
).scale_max(1.0)

# 交渉者の追加
session.add(AspirationNegotiator(name="buyer"), ufun=buyer_utility)
session.add(AspirationNegotiator(name="seller"), ufun=seller_utility)
session.run()
session.plot()