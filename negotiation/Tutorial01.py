# 交渉の実行

from negmas import SAOMechanism, TimeBasedConcedingNegotiator, MappingUtilityFunction
import random 

random.seed(1)

"""
mechanismをインスタンス化
全ての交渉者がオファーを受け入れるか，交渉者がテーブルを離れて終了するか，タイムアウト条件が満たされるまでオファー交換を実施
今回はラウンド数で制限をかけている
時間で制限をかけることも可能　例）time_limit=10
交渉の議題は以下の二つの方法で指定可能
1. outcome=x
単一論点xの問題を含む交渉題を作成するため作成
今回は10個の結果を伴う問題を作成
(0,)～(9,) の範囲の一つの項目からなるタプル
2. issue=x
複数の課題に関する交渉のための引数を渡すことが出来る（方法は後述）
outcomes=10 を issues=[make_issue(10)]に変えれば同じ結果が得られる
"""
session = SAOMechanism(outcomes=100, n_steps=100)

"""
単純な時間ベース戦略の交渉戦略を実行する5タイプの交渉者を生み出す（TimeBasedConcedingNegotiator）
交渉者はまず，自身にとって最大の効用となる結果を提示し，その後，交渉の相対的な時間に基づいて譲歩する
"""
negotiators = [TimeBasedConcedingNegotiator(name=f"a{_}") for _ in range(5)]

"""
上で作成した交渉者を交渉セッションに追加
交渉者は効用関数にアクセスする必要があるから，ここでアクセス（MappingUtilityFunction）
今回は最初に呼び出される値（0~9）と乱数をかけてる
"""
for negotiator in negotiators:
    session.add(
        negotiator,
        preferences=MappingUtilityFunction(
            lambda x: random.random() * x[0], 
            outcome_space=session.outcome_space,
        ),
    )

# 実行と出力
print(session.run())
# print(session.extended_trace)
session.plot(mark_max_welfare_points=False)

"""
疑問点
なぜ，最初に呼び出される値が0～9になってるのか → どうやら2行目の交渉者の生成の時点では全員同じ効用として出てきてるっぽい？random消したら，a0の出した(9,)で一発で合意した
なんでrandomかけたら0～9でばらけてる？ → random.random()は0.0以上1.0未満の値の生成らしいのでこうなるのは当たり前の話でした．すみませんでした
なんで最初はみんな(9,)を選好として持ってるんだ？最大値が入るようになってる？ → outcomes変えてもそうなったから，基本そうなのかも，特にしていなかったら最初は最大値持つ的な
"""