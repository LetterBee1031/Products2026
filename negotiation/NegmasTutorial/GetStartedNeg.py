from negmas import make_issue, SAOMechanism, TimeBasedConcedingNegotiator
from negmas.sao.negotiators import BoulwareTBNegotiator as Boulware
from negmas.sao.negotiators import LinearTBNegotiator as Linear
from negmas.preferences import LinearAdditiveUtilityFunction as UFun
from negmas.preferences.value_fun import IdentityFun, AffineFun

# 交渉課題の作成
# 50個の離散値を持つ課題「価格」を作成
issues = [make_issue(name="price", values=50)]

# mechanismを作成
# SAOメカニズム（スタック交互提案）
# n_stepは交渉回数の制限（20）
mechanism = SAOMechanism(issues=issues, n_steps=20)

# 売り手の定義
# IdentityFun()は価格が高いほど効用関数が高くなること
seller_utility = UFun(values=[IdentityFun()], outcome_space=mechanism.outcome_space)

# 買い手の定義
# AffineFun(slope=-1)は価格が低いほど効用関数が低くなること
buyer_utility = UFun(values=[AffineFun(slope=-1)], outcome_space=mechanism.outcome_space)

# 効用が0~1になるよう正規化
seller_utility = seller_utility.normalize()
buyer_utility = buyer_utility.normalize()

# エージェントの作成と交渉への追加
# bulware戦略を持つエージェント
mechanism.add(Boulware(name="seller"), ufun=seller_utility)
mechanism.add(Linear(name="buyer"), ufun=buyer_utility)

# 交渉の実行と結果出力
print(mechanism.run())
# 交渉過程の出力
print(mechanism.extended_trace)
# グラフにプロット
mechanism.plot(mark_max_welfare_points=False)