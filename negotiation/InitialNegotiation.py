from negmas import SAOMechanism, TimeBasedConcedingNegotiator, make_issue
from negmas.preferences import LinearAdditiveUtilityFunction as LUFun

# どんな交渉かの定義
issues = [make_issue(name="price", values=100)]

# 交渉セッションの作成（Stacked Alternating Offers）
session = SAOMechanism(issues=issues, n_steps=50)

# 買い手（低い料金を好む）と売り手（高い料金を好む）を追加
session.add(
    TimeBasedConcedingNegotiator(name="buyer"),
    ufun=LUFun.random(issues=issues, reserved_value=0.0),
)
session.add(
    TimeBasedConcedingNegotiator(name="seller"),
    ufun=LUFun.random(issues=issues, reserved_value=0.0),
)

result = session.run()
print(f"Agreement: {result.agreement}, Rounds: {result.step}")