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

from negmas.common import PreferencesChangeType
from negmas.sao import SAONegotiator, SAOState, ResponseType
from negmas.preferences import LinearAdditiveUtilityFunction as LUFun
from negmas.preferences import PresortingInverseUtilityFunction
from negmas.preferences.value_fun import LinearFun, IdentityFun, AffineFun
from negmas.negotiators import PolyAspiration
import plotly.express as px

from random import choice
from collections import defaultdict
import math
class RandomNegotiator(SAONegotiator):
    def propose(self, state: SAOState, dest: str | None = None):
        return self.nmi.random_outcomes(1)[0]
    

# 変更2 （追加）ランダムな交渉者でありつつも，閾値以上のオファーのみを受け入れるよう設定
# 変更3 閾値を変えられるようにデフォルトパラメータに追加してみる
class BetterRandomNegotiator(RandomNegotiator):
    def __init__(self, *args, acceptance_threshold=0.8 ,**kwargs):
        super().__init__(*args, **kwargs)
        self._th = acceptance_threshold

    def respond(self, state, source:str |None = None):
        offer = state.current_offer
        if self.ufun(offer) > self._th:
            return ResponseType.ACCEPT_OFFER
        return ResponseType.REJECT_OFFER
    
# 変更4（追加）スマートな交渉者を追加
class SmartAspirationNegotiator(SAONegotiator):
    _imv = None # 効用関数の変換機
    _partner_first = None # 交渉相手にとっての最高の提案（初期提案を最高のものと仮定）
    _min = None # 自身の効用関数における最低値
    _max = None # 自身の効用関数における最高値
    _best = None # 自身にとって最高の交渉解

    def __init__(self, *args, **kwargs):
        # ベースのSAONegotiatorの初期化（必須）
        super().__init__(*args, **kwargs)
        # Aspirationの初期化，初期に受容する効用値を1.0以上に設定，譲歩もゆっくり
        self._asp = PolyAspiration(1.0, "boulware")

    def on_preferences_changed(self, changes):
        # 効用関数の変換器の作成
        changes = [_ for _ in changes if _.type not in (PreferencesChangeType.Scale,)]
        if not changes:
            return
        self._inv = PresortingInverseUtilityFunction(self.ufun)
        self._inv.init()
        
        # 最悪・最高の効用を持つ解を見つける
        worst, self._best = self.ufun.extreme_outcomes()
        # 最悪・最高の結果の時の，効用値を取得
        self._min, self._max = self.ufun(worst), self.ufun(self._best)
        # 無意味な再呼び出しを防ぐために，親メソッドを必ず呼び出す必要があるらしい
        super().on_preferences_changed(changes)

    def respond(self, state, source: str | None = None):
        offer = state.current_offer
        if offer is None:
            return ResponseType.REJECT_OFFER
        # 交渉相手の初期オファーを受け取り次第セット
        if not self._partner_first:
            self._partner_first = offer
        # 自分のオファーより相手のオファーが悪くなかったら（同等以上だったら）承諾
        return super().respond(state, source)

    def propose(self, state, dest: str | None = None):
        # 現在の目標効用値のレベル（オファーや承諾できる効用値のレベル）
        a = (self._max - self._min) * self._asp.utility_at(state.relative_time) + self._min
        # 現在の目標効用レベルを超える解を見つける（解空間が離散値の場合は全ての解）
        outcomes = self._inv.some((a - 1e-6, self._max + 1e-6), False)
        # もし目標効用値を超える解がなかった場合は最高の解をオファー
        if not outcomes:
            return self._best
        # もし相手からなんの解も受け取らなかったら，目標効用値を超える解をオファー
        if not self._partner_first:
            return choice(outcomes)
        # 上記二つを突破したら，相手の初期オファーに最も近い解をオファー（もちろん，目標効用値を超える範囲で）
        nearest, ndist = None, float("inf")
        for o in outcomes:
            d = sum((a-b) * (a-b) for a, b in zip(o, self._partner_first))
            if d < ndist:
                nearest, ndist = o, d
        return nearest


def try_negotiator(cls, replace_buyer=True, replace_seller=True, plot=True, n_steps=20):
    buyer_cls = cls if replace_buyer else AspirationNegotiator
    seller_cls = cls if replace_seller else AspirationNegotiator

    # 交渉問題の作成
    issues = [
        make_issue(name="price", values=10),
        make_issue(name="quantity", values=(1,11)),
        make_issue(name="delivery_time", values=10),
    ]

    # メカニズムの作成
    session = SAOMechanism(issues=issues, n_steps=n_steps)

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
    session.add(buyer_cls(name="buyer"), ufun=buyer_utility)
    session.add(seller_cls(name="seller"), ufun=seller_utility)
    
    # 交渉の実施
    session.run()
    # print(session.run())
    # print_each_neg_round(session)

    if plot:
        session.plot()
    return session

# 各ラウンドの結果を見やすくするやつ
def print_each_neg_round(session):
    prev_step = None
    for step, negotiator, offer in session.extended_trace:
        if step != prev_step:
            print(f"Round {step}")
            prev_step = step
        print(f"  {negotiator}: {offer}")

# 関数の呼び出し

# 両方ランダム交渉者で
# s = try_negotiator(RandomNegotiator)

# 変更1 片方（buyer）だけランダムにしてみる
# s = try_negotiator(RandomNegotiator, replace_seller=False)

# 変更2 （追加）ランダムな交渉者でありつつも，閾値以上のオファーのみを受け入れるよう設定
# s3 = try_negotiator(BetterRandomNegotiator)
# print(s3.state)
# negotiator_ids = [_.id for _ in s3.negotiators]
# acceptor = [i for i, _ in enumerate(negotiator_ids) if _ != s3.state.current_proposer][0]
# print(s3.negotiators[acceptor].ufun(s3.agreement))

# 変更4（追加）スマートな交渉者を追加
s = try_negotiator(SmartAspirationNegotiator)


# 変更5 複数の交渉の平均をとった場合の比較
# パレートフロントの探索
frontier_utils, frontier_outcomes = s.pareto_frontier()
nash_utils, nash_outcomes = s.nash_points()[0]
nash_welfare = sum(nash_utils)

# 距離の定義（ユークリッド距離）
def ed(a, b):
    return math.sqrt(sum((x-y) ** 2 for x, y in zip(a, b)))

def pareto_dist(a, frontier):
    # パレート解からの距離
    return min(ed(a,b) for b in frontier)

def nash_diff(a, nash_welfare):
    # 全体的に見た時の合意解の効用とナッシュ解との距離
    return nash_welfare - sum(_.ufun(a) for _ in s.negotiators)

# 合意解とパレートフロントとの距離に関するデータの収集
n, pdist, ndiff = 100, defaultdict(float), defaultdict(float)
for _ in range(n):
    for cls in (AspirationNegotiator, SmartAspirationNegotiator, RandomNegotiator):
        a = try_negotiator(cls, plot=False).state.agreement
        pdist[cls.__name__] += pareto_dist(a, frontier_outcomes) / n
        ndiff[cls.__name__] += nash_diff(a, nash_welfare) / n

print(
    f"Distance to Pareto Frontier: {dict(pdist)}\nDistance to the Nash Bargaining Solution: {dict(ndiff)}"
)


"""
両方をランダム交渉者で
なんか1ラウンドで交渉が終わる
"""

"""
変更1 片方（buyer）だけランダムにしてみる
売り手は時間ベースの交渉者として想定通りの行動（最良 → 譲歩）
買い手もランダム生成交渉者として様々な結果を提示（ここまではいい）
買い手が売り手からのオファーを早い段階で受け入れている（なぜ雑に受け入れたのか？ここが，ポイントっぽい）

以下引用
NegMASのデフォルトの受容戦略は，
「その交渉状態において，自分が提案する予定だったオファーと比べ，交渉者にとって同等かそれ以上の効用を持つ結果である場合に限り，結果を受諾する」

つまり，この場合，buyerがランダムでの提案の効用が，sellerの提案の効用を下回ったタイミングで合意した
これじゃあランダムで提案したら負けるだけで，意味ないじゃないか！（当たり前の話）

両方ランダムの時もお互いランダムであったために，早い段階で勝負がついているのだろう，と
"""

"""
変更2 （追加）ランダムな交渉者でありつつも，閾値以上のオファーのみを受け入れるよう設定
少なくとも片側は高い効用で終われるようになったっぽい
いろいろ工夫して結果を見てみたら，最後に承諾したほうの効用が
0.9802371541501976
だったのでどうやらうまく行ってるらしい
"""

"""
変更4（追加）スマートな交渉者を追加
AspirationNegotiator（既定の効用値を満たしたいという願望を持つ交渉者）として，譲歩を行う
与えられた効用レベル内で，相手の最初の提案に最も近い結果を返す

解空間内である効用の閾値を超えるすべての結果を見つける（InverseUtilityFunctionの利用）
交渉者は交渉中に効用関数が変更される可能性を想定する必要がある
効用関数が変更されるたびに，各交渉解に対する効用値を再計算（on_preferences_changed()をコールバックして実現）
提示・承諾する効用レベルを計算する方法の必要性（PolyAspirationにて実現）

オーバライドした4つのメソッドについて
1. __init__()：
交渉者を初期化する．正しく初期化するために常に呼び出す．
aspirationのミックスインを初期化することで，0から徐々に譲歩
2. on_preferences_changed(changes)：
効用関数の変換機，効用関数の範囲を更新し，最良の結果を見つけ出す．
不要な呼び出しを避けるため親メソッドを実装して呼び出す必要がある
3. respond()：
受け入れ戦略の実装．今回はデフォルトの受け入れ戦略でやってる
交渉相手の初期オファーを保存して使用
4. proposal()：
提案戦略の実装．この交渉者の中核と言える部分．詳しく見ていく
4-1 現在の目標効用のレベルを計算．提案する解の効用レベルを決定
    a = (self._max - self._min) * self.utility_at(state.relative_time) + self._min
    最高値から最低値を引いて，それに経過時間での譲歩するための係数をかけてるってのは分かる
    なんで最後に最低値足してるんだ？最低限の効用は守るため？
    どっちにしろ，最低値より低い解がないんだから↑こんな処理いらんくないか？
    いやでも足さなかったら最高値は絶対に提案されなくなるのか？（最高値 - 最低値の効用が最大になる）
    なら最低値は足す必要がありそうだな...
    いやだとしたら別に最初から（最高値-最低値）をしなければいい話では？
    そしたら最大値×時間変化で丸いのでは？そうすると下がり過ぎた時に最低保証が出来なくなるって話？
    あとは効用の変化を最初だけ一気にやってあとは緩やかに...みたいには出来るか．時間たつにつれて最後に足してる最低値の割合が大きくなるから
    うーんでもいまいち納得できない．こういう関数とか理屈があるのかね

4-2 目標効用値を超える交渉解を探索
    outcomes = self._inv.some((a, self._max), False)
    離散値の場合は全部取得する．連続値の場合はいくつかピックアップするっぽい

4-3 3つのケースについて
    閾値を超える交渉解がない場合，とりあえず最高の解を出しとく
    if not outcomes:
        return self._best

    相手の初期提案を知らない場合（自分が先に提案をしてるなど），解空間から閾値を超える解を選定
    if not outcomes:
        return self._best
    
    相手の初期提案を知ってる場合，相手の提案と自分が持つ解（閾値を超える解）の距離を計算，ユークリッド距離を使う
    ※zip関数 リストやタプルなどのイテラブルオブジェクトの要素をまとめるらしい
    d = sum((a - b) * (a - b) for a, b in zip(o, self._partner_first))

結果
ついにパレート解で終了！やったね！
やはり交渉戦略は大事らしい

"""

"""
変更5 複数の交渉の平均をとった場合の比較
Smartのやつが一番いいように見せかけて，自分の環境だと普通のAspirationの方がナッシュ距離が近いという結果に
なぜ...？
想定ではスマートさんがナッシュ距離も一番近いはずなのに...
提案パターンの妙か
パレートフロントからの距離はスマートさんが一番近かった
"""

"""
補足的な奴　スマートさんに隠された前提条件について
1. 結果空間に意味のある距離尺度が定義されているということを暗黙的に想定している
    結果の一部が数値でない場合はあてはめられない
    あと単位が違うものを同じ尺度で扱っていいのか，みたいな問題も発生する
    一致・不一致の0 or 1で近似するとかが有効
    あと，初期提案だけじゃなくて，交渉全体での提案の平均とか取って戦略立ててもよし
2．今回のaspirationミックスインは最小値が0ではなく，予約値としていた
    これは，今までの使用方法と一致していない
    今回のケースでは予約値が0だったから影響がなかった
    一般的な交渉では考慮が必要
"""