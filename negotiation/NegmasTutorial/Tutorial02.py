# 単純な二者間交渉

"""
交渉の状況
買い手と売り手が商取引の交渉をしている
買い手は利益を最大買いしたい，売り手はコストを最小化したい
両者ともできるだけ多くの商品を取引したいと考えている
それぞれ希望する納期もある
"""

from negmas import (
    make_issue,
    SAOMechanism,
    NaiveTitForTatNegotiator,
    TimeBasedConcedingNegotiator,
)
from negmas.preferences import LinearAdditiveUtilityFunction as LUFun
from negmas.preferences.value_fun import LinearFun, IdentityFun, AffineFun

# 交渉問題の作成
issues = [
    make_issue(name="price", values=10),
    make_issue(name="quantity", values=(1,11)),
    make_issue(name="delivery_time", values=10),
]

# メカニズムの作成
# 変更3 ステップの制約を20→5000
session = SAOMechanism(issues=issues, n_steps=5000)

# 売り手と買い手の選好の定義
"""
売り手の定義
price(価格) → 高い方がいい
quantity(数量) → 多い方がいい(傾きは小さめ)
delivery_time(配送時間) → 早い方がいい
"""

seller_utility = LUFun(
    values={
        "price": IdentityFun(),
        "quantity": LinearFun(0.2),
        "delivery_time": AffineFun(-1, bias=9.0),
    },
    # 変更1 重み追加　もし売り手が配送時間を重視していたら？
    weights={"price": 1.0, "quantity": 1.0, "delivery_time": 10.0},
    outcome_space=session.outcome_space,
)

"""
買い手の定義
price(価格) → 安い方がいい
quantity(数量) → 多い方がいい(傾きは小さめ)
delivery_time(配送時間) → 遅い方がいい
"""
buyer_utility = LUFun(
    values={
        "price": AffineFun(-1, bias=9.0),
        "quantity": LinearFun(0.2),
        "delivery_time": IdentityFun(),
    },
    outcome_space=session.outcome_space,
)

# 変更2 正規化
seller_utility = seller_utility.scale_max(1.0)
buyer_utility = buyer_utility.scale_max(1.0)

# 売り手と買い手の交渉者を作成・追加
# 変更4 譲歩に関する設定をboulware(デフォルト) → linearに変更
# 変更5 buyerだけboulwareに戻す　片方だけ譲歩しづらくした場合は？
session.add(
    TimeBasedConcedingNegotiator(name="buyer", offering_curve="boulware"), 
    ufun=buyer_utility,
    )
session.add(
    TimeBasedConcedingNegotiator(name="seller", offering_curve="linear"), 
    ufun=seller_utility,
    )

# 交渉の実行と結果の表示
print(session.run())

# 各ラウンドの結果を見やすくするやつ
# prev_step = None
# for step, negotiator, offer in session.extended_trace:
#     if step != prev_step:
#         print(f"Round {step}")
#         prev_step = step
#     print(f"  {negotiator}: {offer}")

# グラフ出力
session.plot(ylimits=(0.0, 1.01), show_reserved=False)

"""
変更1 重み追加　もし売り手が配送時間を重視していたら？
結果が大きく変化
パレート最適解から離れた解が多数存在，パレート最適解で合意に至らなかった → 取りこぼされた利益がある
sellerはbuyerよりもはるかに高い効用を得ている（seller: 100→67.2 buyer: 20→11.8）d
この差はufunの値を正規化すると解消できる
"""

"""
変更2 効用関数を正規化
sellerとbuyerの不公平感は大分解消された（seller: 1.0→0.48, buyer: 1.0→0.57）
"""

"""
変更3 もっと交渉の制限時間を延ばしてみたら？
前回と比較して，パレート最適に近い解となった ← 近づいただけでパレート最適ではない
なんか例のほうではあまり変わらなかったらしい. 多分ステップ数の変え方の違いかも
　例のほうは（50 → 5000）だった
　自分の方は（20 → 5000）だった
この交渉だと40～50ステップぐらいで十分ってことかね
"""

"""
変更4 譲歩に関する設定をboulware(デフォルト) → linearに変更
譲歩が早い段階で行われるよう変更したため，合意が早まった
しかし，合意した交渉解は変化しなかった ← パレート最適でない解

交渉時間を長くしたり，譲歩を早めたりしても結果は良くならない
"""

"""
変更5 buyerだけboulwareに戻す　片方だけ譲歩しづらくした場合は？
前と比べてパレート最適解から遠ざかった
buyer方の効用が一方的に高い状態で終了（buyer: 0.81, seller: 0.33）
"""

"""
ページ最後の問いについて
1. なぜ今回の交渉は前回の交渉よりも早く終わったのか？
→ 早く終わってないが？
→ 両方boulwareだった時と比較するならば，片側の譲歩が早かったことで，全体の合意が早まったのだろう
2. 最終合意がパレート最適解に近いのはなぜか？
→ 片側(buyer)の効用が大分高くなっているから
→ パレート最適解は「片方の効用を下げなければ，もう片方の効用が上がることはない」点だとするなら，片側だけの効用が最大化した点は一種のパレート最適解であるはず
→ これはあくまでパレート最適であるというだけで，全体効用が最大と言えるかというとそうではない．ナッシュ距離とかも両方が同一戦略だったときより遠くなってるし
→ この理解であってるよね？
3. なぜ買い手（buyer）は両方boulware戦略であったときと比べて高い効用を得ているのか？
→ 相手（seller）だけが早い段階で譲歩したため
→ 自身が大きく譲歩する前に相手が大きく譲歩したことから，効用が高い状態で保たれた
4. なぜ売り手（seller）は両方がlinear戦略を用いていた時よりも低い効用となっているのか？
→ 相手（buyer）が譲歩する前に自身が大きく譲歩してしまったため
→ 相手が強硬な戦略をとってる状態で自分が先に譲歩したため，自身の方だけが効用が大きく下がる結果となった
5. 買い手がこの戦略を用いていることを売り手が知った場合，売り手にとって最善の対応策は？
→ 自身も同様の戦略を取ることなのかなぁ
→ それを逆手に取る戦略とかありそうだけど，
→ 最後には相手が譲歩することが分かっているなら，自分は最後まで譲歩しないとか？
→ それだと合意が得られない危険もあるけど
"""


"""
チャッピーによると
IdentityFun() → 受け取った値をそのまま効用として利用
LinearFun(slope) → 傾きslopeを定義した1次関数を効用として利用．効用の拡大縮小に便利
AffineFun(slope, bias) → 傾きslopeと切片biasを定義した1次関数を効用として利用．傾きを負にしたときに効用が負にならないように切片をいじるって場面で便利

つまり
IdentityFun() = LinearFun(1) = AffineFun(1,0)
と言えるっぽい．
ドキュメント見ても分かりづらいけど，使われ方的に恐らく合ってそう
"""