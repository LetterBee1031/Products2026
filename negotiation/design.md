# 自動交渉（PA/AA）＋効用設計＋NegMAS実装仕様まとめ（現時点）
---

## 1. 目的と前提

- 主タスク：災害体験などの**体験学習**（XR訓練）
- 認知負荷の推定：HMDアイトラッカー（瞳孔径・瞬き率）＋心拍（HR/HRV）＋脳波等
- N-back課題：**ベースライン設定のみに使用**（主タスクの一部ではない）
- 調整は「負荷を望ましい帯へ戻す」ことを優先しつつ、体験者の**好み・属性**も反映する
- 調整後負荷の予測は現段階では **ルールベース（デルタ型）**

---

## 2. フレームワーク全体（サーバ上の3機構）

### 2.1 認知負荷推定機構（CLE: Cognitive Load Estimator）
- 入力：生体情報（視線/心拍/脳波等）
- 出力：観測負荷 `L_obs ∈ [0, 1]`
- トリガ：`L_obs > L_obs_high` または `L_obs < L_obs_low` のとき **PAへ通知**（交渉開始）

### 2.2 自動交渉機構（NEG: Negotiation）
- 交渉参加者：PA（Player Agent）と AA（Adjustment Agent）の **2者間**
- 交渉方式：**交互提案（SAO: Alternating Offers）**
- 出力：合意した調整案 `x*` を最適化機構へ送る

### 2.3 最適化機構（OPT: Optimizer）
- 入力：交渉結果（調整案）
- 処理：要素リスト（利用可能オブジェクト/シナリオ/UI等）に基づき空間・内容を再構築
- 出力：最適化後の体験をHMDへ反映

---

## 3. 交渉論点（変数）と表現

### 3.1 提案（調整案）`x`
- `x` は複数論点の組（離散カテゴリ）
- 実装上は NegMAS の `Outcome` としてやり取り（内部処理は dict に統一）

### 3.2 好み論点 `I_e`（交渉に入れる6指標）
各論点は 3段階カテゴリ（順序あり）で管理し、計算用に `ORDINAL_MAPS` で `[0,1]` に写像する。

1. `tempo`：進行テンポ（slow/normal/fast）
2. `guidance`：ガイダンス量（low/medium/high）
3. `complexity`：判断の複雑さ（low/medium/high）
4. `stimulus`：刺激強度（low/medium/high）
5. `break_policy`：休憩方針（rare/on_demand/frequent）
6. `feedback`：フィードバック（summary/brief_immediate/detailed_immediate）

### 3.3 文章テイスト（順序なしカテゴリ）
- `taste`：順序なしカテゴリ（例：polite/concise/encouraging/neutral）
- 一致度は「距離」ではなく、**類似度行列 `K`（TASTE_SIMILARITY）**で評価

---

## 4. 観測閾値と予測閾値（スケール分離）

### 4.1 観測閾値（CLE）
- `L_obs_low`, `L_obs_high`：交渉開始判定に使用

### 4.2 予測閾値（安全マージン込み）
ルール予測は誤差が見込まれるため、**観測帯より内側に寄せた予測帯**を用いる。

- `margin = 0.10`（仮置き：安全余裕）
- `L_pred_low = L_obs_low + margin`
- `L_pred_high = L_obs_high - margin`

> 注意：`2*margin < (L_obs_high - L_obs_low)` を満たさないと帯が成立しないため、帯幅に応じて調整が必要。

---

## 5. ルールベース予測負荷（デルタ型）

調整案 `x` を適用した後の予測負荷 `L_pred(x)` を以下で計算する：

- `z_i(offer)`：提案の論点 `i` を `[0,1]` に写像した値
- `z_i(current)`：現在設定の写像値
- `a_i`：影響係数（`coeffs` 辞書）

**予測式：**

L_pred(x) = clip( L_current + Σ a_i * ( z_i(offer) - z_i(current) ), 0, 1 )


- `clip(·,0,1)` は `max(0, min(1, ·))`
- `taste` は順序なしカテゴリのため、デフォルトで負荷予測には入れない
- `coeffs` の符号：
  - 正：値を上げると負荷↑（例：tempo, complexity, stimulus）
  - 負：値を上げると負荷↓（例：guidance, break_policy）

---

## 6. 効用関数設計

### 6.1 便利関数
**帯外れ量（ヒンジ）**

d_out(L, low, high) = max(0, low - L) + max(0, L - high)


**帯内中心距離（任意）**

d_in(L, low, high) = | L - (low+high)/2 |


**変更コスト（急変抑制）**

c(x) = Σ ρ_i * | z_i(offer) - z_i(current) |

- `rho` は論点別の「変えにくさ」重み辞書

---

### 6.2 PA効用（改訂：好み + 負荷適正）

#### (A) 好み効用（順序あり + テイスト）
順序あり論点の一致度：

s_i(x) = 1 - | z_i(x) - p_i |

- `p_i`：その人の理想値（[0,1]）

テイスト一致度（類似度行列）：

s_taste(x) = K[taste(x), preferred_taste]


好み効用：

U_PA^pref(x) = Σ w_i * s_i(x) + w_taste * s_taste(x)


#### (B) 負荷適正効用

U_PA^load(x) = exp( -η * d_out(L_pred(x), L_pred_low, L_pred_high) )


#### (C) 総合PA効用（混合）

U_PA(x) = (1 - λ_L) * U_PA^pref(x) + λ_L * U_PA^load(x)


- `λ_L`：PAが負荷適正をどれだけ重視するか（全体への影響）
- `η`：負荷項単体での「厳しさ」（帯外れにどれだけ敏感か）
- `tau_accept`：PA受諾の下限（`U_PA` に対して適用）

---

### 6.3 AA効用（負荷帯復帰 + 中心志向 + 急変抑制）

AA効用（指数の積）：

U_AA(x) = exp(-α d_out) * exp(-β d_in) * exp(-γ c)

同値：

U_AA(x) = exp( - ( α d_out + β d_in + γ c ) )


- `α`：帯外れ回避を強く（通常最大）
- `β`：帯内で中心に寄せる（安定化）
- `γ`：急変抑制（変更コスト）

---

## 7. 交渉プロトコル（SAO：交互提案）

### 7.1 AAの提案選択（最終形）
AAは候補集合 `X`（要素リスト/離散論点空間から生成可能）から、


x^(k) = argmax_x [ U_AA(x) + λ * U_PA(x) ]
s.t. x は PA制約（Ω）を満たす


実装上は安定化のため、**帯内ゲート**を適用：
- `L_pred_low <= L_pred(x) <= L_pred_high` を満たす候補だけ評価

### 7.2 PAの受諾条件
- ハード：`L_pred(x)` が予測帯に入る
- ソフト：`U_PA(x) >= tau_accept`


ACCEPT ⇔ (L_pred_low <= L_pred <= L_pred_high) AND (U_PA >= tau_accept)


---

## 8. CPA(k)（PAの制約）と「方針1：候補ゼロのときだけ緩める」

### 8.1 PA制約表現（Ω）
- `PAConstraints.allowed_values: Dict[str, set]`
- `allows(offer)` は「全論点で offer[issue] ∈ Ω_issue」を満たすか判定

### 8.2 方針1（採用）
- PAは初期に好みを強く守る（重要論点を狭く許容）
- AAが「帯内候補が見つからない（no_feasible）」場合にのみ、PAが段階的にΩを緩める

#### 緩和レベル例
- レベル0：理想に最も近い1値（テイストは preferred のみ）
- レベル1：上位2値（テイストは {preferred, neutral}）
- レベル2：全許容

---

## 9. NegMAS（Python）実装仕様

### 9.1 使用コンポーネント
- `SAOMechanism`：交互提案メカニズム
- `SAONegotiator`：PA/AAの基底クラス
- `OutcomeSpace.enumerate_or_sample(max_cardinality=...)`：
  - 提案候補を列挙/サンプルしてキャッシュ

### 9.2 実装クラス
- `Thresholds`：観測/予測閾値（margin込）
- `RuleBasedLoadModel`：`coeffs` に基づき `L_pred` を計算
- `PAProfile`：PAの好み/負荷重み/受諾閾値
- `AAParams`：AA効用パラメータ
- `PAConstraints`：Ω（許容集合）管理
- `PlayerAgentPA`：
  - `propose()`：制約内で理想に近い案を提案（簡易）
  - `respond()`：no_feasible検出時に緩和、通常はハード+ソフトで受諾判定
- `AdjustmentAgentAA`：
  - `on_negotiation_start()`：候補を `_cached_candidates` にキャッシュ
  - `propose()`：`U_AA + λ U_PA` 最大の候補を返す（帯内ゲート）
  - 候補ゼロなら `no_feasible=True` を立てフォールバック案を返す
  - `respond()`：相手案が自分の次案以上なら受諾（簡易）

### 9.3 Outcome ↔ dict 変換
- NegMASの `Outcome` は tuple/dict の場合があるため、内部処理は dict に統一：
  - `outcome_to_dict()`
  - `dict_to_outcome()`

### 9.4 主なパラメータ
- `margin`：予測帯の安全余裕（例：0.10）
- `coeffs`：負荷影響係数 `a_i`
- `rho_change`：変更コスト重み `ρ_i`
- `lambda_L`, `eta`：PA効用における負荷項
- `tau_accept`：PAの受諾閾値
- `alpha, beta, gamma`：AA効用パラメータ
- `AAParams.lam`：AAが `U_PA` をどれだけ尊重するか
- `max_candidates`：AAが評価する候補数上限（例：3000）

---

## 10. 既知の注意点・今後の改善余地

- ルールベース予測（coeffs）は初期は設計値。ログから校正すると安定する
- `margin` は帯幅に依存。固定0.10が常に適切とは限らない（帯幅比例や誤差分位点で更新推奨）
- `taste` の類似度行列 `K` は仮置き。予備実験等で妥当化可能
- set の順序は不定のため、tie-break（同率時の選択）で再現性が揺れる可能性あり
- 実運用では「要素リスト」からの候補生成に置換（現実は全列挙が困難な場合がある）
- 交渉が長引く場合は、最大ラウンド/タイムアウト/フォールバック戦略の明確化が必要

---

## 11. 参考：最小の実行例（run_example）
- 3段階×6論点 + テイスト4種 → 全候補は `3^6*4 = 2916`
- `max_candidates=3000` でほぼ全列挙に相当
- `PA.aa_ref = aa` をセットし、no_feasible検出→緩和の連携を行う
