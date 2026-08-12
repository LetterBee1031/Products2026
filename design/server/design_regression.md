# 心拍数・瞳孔径を用いた認知負荷線形回帰モデル 要件定義

## 1. 目的

心拍数および瞳孔径から、個人ごとの認知負荷を連続値として推定する線形回帰モデルを構築する。

モデル作成時の正解ラベルには、以下の2種類の情報を組み合わせる。

* N-back課題のN数から定義する客観的負荷 `L_obj`
* NASA-TLX回答から定義する主観的負荷 `L_sub`

これらを重み付きで統合した `L_label` を線形回帰モデルの目的変数として使用する。

最終的な認知負荷推定値 `L_cur` は0〜1の連続値として出力する。

---

# 2. モデル概要

## 2.1 説明変数

以下の2変数を使用する。

* 心拍数 `heart_rate`
* 瞳孔径 `tepr`

入力される値は、事前に2秒窓で平滑化・サンプリング済みであることを前提とする。

したがって、

```text
CSVの1行 = 1つの学習・推論サンプル
```

とする。

---

## 2.2 回帰モデル

個人ごとに線形回帰モデルを構築する。

```text
L_cur_raw
= β0
+ β1 × heart_rate_z
+ β2 × tepr_z
```

ここで、

* `L_cur_raw`：線形回帰モデルの出力
* `heart_rate_z`：標準化済み心拍数
* `tepr_z`：標準化済みTEPR
* `β0`：切片
* `β1`：心拍数の回帰係数
* `β2`：TEPRの回帰係数

とする。

---

# 3. 入力データ

## 3.1 生体情報データ

必須列は以下とする。

| 列名           | 型        | 内容              |
| ------------ | -------- | --------------- |
| `userID`     | string   | 参加者識別子          |
| `block_id`   | string   | N-back課題ブロック識別子 |
| `sent_at`    | datetime | サンプル時刻          |
| `heart_rate` | float    | 2秒窓で処理済みの心拍数    |
| `tepr`       | float    | 2秒窓で処理済みの瞳孔径    |
| `n_back`     | int      | N-back課題のN数     |

`tepr`については、以下の処理が事前に完了していることを前提とする。

* 左右眼の統合
* 瞬き等の異常値除去
* 必要な瞳孔径補正
* 2秒窓による平滑化

`heart_rate`についても、2秒窓単位で使用可能な値として前処理済みであることを前提とする。

---

## 3.2 NASA-TLXデータ

NASA-TLXは各N-backブロック終了後に取得する。

必須列は以下とする。

| 列名                | 内容              |
| ----------------- | --------------- |
| `userID`          | 参加者識別子          |
| `block_id`        | 対応するN-backブロック  |
| `mental_demand`   | Mental Demand   |
| `physical_demand` | Physical Demand |
| `temporal_demand` | Temporal Demand |
| `performance`     | Performance     |
| `effort`          | Effort          |
| `frustration`     | Frustration     |

各尺度の回答範囲は0〜20とする。

NASA-TLXの利用方法は以下から選択可能とする。

* Raw NASA-TLX
* Mental Demandのみ

重み付きNASA-TLXは使用しない。

---

# 4. NASA-TLX得点の算出

## 4.1 Raw NASA-TLXを使用する場合

6尺度の単純平均を算出する。

```text
RawTLX
=
(
  mental_demand
  + physical_demand
  + temporal_demand
  + performance
  + effort
  + frustration
) / 6
```

RawTLXの範囲は0〜20とする。

Performance尺度について回答方向が他尺度と逆の場合は、事前に方向を統一する。

---

## 4.2 Mental Demandのみ使用する場合

```text
RawTLX = mental_demand
```

として扱う。

---

# 5. 主観的負荷 L_sub

NASA-TLX得点を参加者内でZ標準化する。

```text
Z_TLX
=
(RawTLX - μ_TLX) / σ_TLX
```

ここで、

* `μ_TLX`：当該参加者のNASA-TLX平均
* `σ_TLX`：当該参加者のNASA-TLX標準偏差

とする。

その後、標準化値を中心0.5の0〜1尺度へ変換する。

```text
L_sub
=
Z_TLX / 4 + 0.5
```

さらに0〜1の範囲へクリッピングする。

```text
L_sub
=
clip(
  Z_TLX / 4 + 0.5,
  0.0,
  1.0
)
```

代表的な対応は以下となる。

| Z_TLX | L_sub |
| ----: | ----: |
|  -2.0 |  0.00 |
|  -1.0 |  0.25 |
|   0.0 |  0.50 |
|  +1.0 |  0.75 |
|  +2.0 |  1.00 |

したがって、

* `L_sub = 0.5`：その参加者の平均的な主観負荷
* `L_sub < 0.5`：平均より低い負荷
* `L_sub > 0.5`：平均より高い負荷

と解釈する。

---

# 6. 客観的負荷 L_obj

N-back課題のN数に応じて、以下の値を設定する。

| N-back | L_obj |
| ------ | ----: |
| 0-back |  0.25 |
| 1-back |  0.50 |
| 2-back |  0.75 |
| 3-back |  1.00 |

実装上は以下の対応表を使用する。

```text
0 → 0.25
1 → 0.50
2 → 0.75
3 → 1.00
```

`L_obj`は課題そのものから与えられる客観的な負荷レベルを表す。

---

# 7. 学習用認知負荷ラベル L_label

最終的な線形回帰モデルの目的変数を以下で定義する。

```text
L_label
=
w_obj × L_obj
+
w_sub × L_sub
```

ここで、

* `L_label`：モデル学習用認知負荷ラベル
* `L_obj`：N-backによる客観的負荷
* `L_sub`：NASA-TLXによる主観的負荷
* `w_obj`：客観的負荷の重み
* `w_sub`：主観的負荷の重み

とする。

---

## 7.1 重み

以下の条件を基本とする。

```text
w_obj >= 0
w_sub >= 0
w_obj + w_sub = 1
```

初期値の例として、

```text
w_obj = 0.5
w_sub = 0.5
```

を設定可能とする。

ただし、最終的な重みは予備実験やモデル性能を踏まえて決定する。

---

## 7.2 ラベル算出例

2-backの場合、

```text
L_obj = 0.75
```

NASA-TLXが参加者平均より1標準偏差高ければ、

```text
Z_TLX = 1.0

L_sub
= 1.0 / 4 + 0.5
= 0.75
```

`w_obj = 0.5`、`w_sub = 0.5`の場合、

```text
L_label
= 0.5 × 0.75
+ 0.5 × 0.75
= 0.75
```

となる。

---

# 8. ラベル付与単位

NASA-TLXはブロック終了後に1回取得する。

同一N-backブロック内では、

* `L_obj`
* `L_sub`
* `L_label`

はすべて同一となる。

例えば120秒のN-back課題を2秒周期で取得した場合、

```text
120秒 / 2秒
= 60サンプル
```

が生成される。

この60サンプルすべてに、同一ブロックの`L_label`を付与する。

---

# 9. 説明変数の標準化

心拍数とTEPRは参加者内でZ標準化する。

```text
heart_rate_z
=
(heart_rate - μ_HR) / σ_HR
```

```text
tepr_z
=
(tepr - μ_TEPR) / σ_TEPR
```

モデルへの入力は、

```text
X =
[
  heart_rate_z,
  tepr_z
]
```

とする。

---

# 10. モデル学習

## 10.1 モデル単位

モデルは`userID`ごとに個別作成する。

```text
user01 → model_user01
user02 → model_user02
...
```

全参加者共通モデルは初期実装の対象外とする。

---

## 10.2 モデル

scikit-learnの、

```text
LinearRegression
```

を使用する。

入力：

```text
heart_rate_z
tepr_z
```

目的変数：

```text
L_label
```

とする。

---

# 11. 交差検証

モデル性能の評価には、基本的にGroup K-Fold交差検証を使用する。

```text
Group = block_id
```

とする。

同一ブロック内の2秒サンプルがTrainingとValidationに分割されることを禁止する。

---

## 11.1 交差検証時の処理

各foldについて以下の順序で処理する。

```text
Training blockとValidation blockを分離
↓
Training側のHR・TEPRから
X用標準化パラメータ算出
↓
Training側のNASA-TLXから
μ_TLX・σ_TLX算出
↓
TrainingのL_sub算出
↓
ValidationのL_subも
Training側μ_TLX・σ_TLXで算出
↓
L_obj算出
↓
L_label算出
↓
LinearRegression学習
↓
Validationデータを推論
↓
モデル性能評価
```

ValidationデータをNASA-TLX標準化や説明変数標準化のパラメータ算出に使用してはならない。

---

## 11.2 Fold数

基本設定は、

```text
5-fold Group K-Fold
```

とする。

ただし、利用可能なブロック数が5未満の場合はfold数を自動調整する。

ブロック数が少ない場合はLeave-One-Block-Out方式も利用可能とする。

---

# 12. 回帰モデルの出力

線形回帰モデルから直接得られる値を、

```text
L_cur_raw
```

とする。

学習ラベル`L_label`自体が0〜1を基本とした尺度になっているため、以前想定していた、

```text
L_cur_raw / 4 + 0.5
```

という再変換は行わない。

---

# 13. 最終認知負荷 L_cur

線形回帰では、0〜1の目的変数で学習しても予測値が範囲外になる可能性がある。

そのため、最終的な認知負荷推定値を、

```text
L_cur
=
clip(
  L_cur_raw,
  0.0,
  1.0
)
```

として定義する。

具体的には、

```text
L_cur_raw < 0
→ L_cur = 0
```

```text
0 <= L_cur_raw <= 1
→ L_cur = L_cur_raw
```

```text
L_cur_raw > 1
→ L_cur = 1
```

とする。

最終的な出力範囲は、

```text
0.0 <= L_cur <= 1.0
```

とする。

---

# 14. 推論処理

推論時には、2秒窓で処理済みの、

```text
heart_rate
tepr
```

を入力する。

処理フローは以下とする。

```text
heart_rate・tepr取得
↓
学習済みX ScalerでZ標準化
↓
LinearRegression
↓
L_cur_raw
↓
0～1へclip
↓
L_cur
```

推論時にNASA-TLXは使用しない。

---

# 15. 推論結果

出力例：

```json
{
  "userID": "user01",
  "sent_at": "2026-08-07T15:00:02.000",
  "heart_rate": 74.2,
  "tepr": 3.41,
  "L_cur_raw": 0.72,
  "L_cur": 0.72
}
```

範囲外となった場合：

```json
{
  "userID": "user01",
  "sent_at": "2026-08-07T15:00:04.000",
  "heart_rate": 91.4,
  "tepr": 4.15,
  "L_cur_raw": 1.08,
  "L_cur": 1.0
}
```

`L_cur_raw`は解析・デバッグ用に保存し、実際のXR体験調整には`L_cur`を使用する。

---

# 16. モデル評価

以下の評価指標を使用する。

* MAE
* RMSE
* R²
* Pearson相関係数

基本的なモデル性能評価では、

```text
正解値：L_label
予測値：L_cur_raw
```

を比較する。

`L_cur`はクリッピング後の運用値であるため、モデルそのものの回帰性能評価には原則として使用しない。

必要に応じて、

* クリッピング発生率
* `L_cur_raw < 0`となった割合
* `L_cur_raw > 1`となった割合

も記録する。

---

# 17. 最終モデル作成

交差検証によるモデル性能評価終了後、参加者の全有効データを利用して最終モデルを作成する。

```text
全block
↓
HR・TEPR用Scaler fit
↓
NASA-TLXのμ・σ算出
↓
L_sub算出
↓
L_obj算出
↓
L_label算出
↓
LinearRegression fit
↓
保存
```

---

# 18. モデル保存

参加者ごとに以下を保存する。

```text
models/
  user01/
    model.joblib
    x_scaler.joblib
    metadata.json
    cv_metrics.json
```

`metadata.json`には以下を記録する。

* `userID`
* NASA-TLX利用方式
* NASA-TLX平均値
* NASA-TLX標準偏差
* `w_obj`
* `w_sub`
* `L_obj`のマッピング
* 使用特徴量
* HR・TEPRの標準化条件
* 使用block
* 学習サンプル数
* 回帰係数
* 切片
* モデル作成日時

---

# 19. 設定ファイル例

```yaml
target:
  subjective_measure: raw_tlx

  subjective_load:
    normalization: z_score
    scale_divisor: 4.0
    center: 0.5
    clip_min: 0.0
    clip_max: 1.0

  objective_load:
    0: 0.25
    1: 0.50
    2: 0.75
    3: 1.00

  weights:
    objective: 0.5
    subjective: 0.5

features:
  heart_rate_column: heart_rate
  pupil_column: tepr
  standardize: true

input:
  user_column: userID
  group_column: block_id
  time_column: sent_at
  n_back_column: n_back
  sampling_interval_seconds: 2

model:
  type: linear_regression
  scope: individual

cross_validation:
  enabled: true
  method: group_k_fold
  folds: 5

prediction:
  clip_output: true
  clip_min: 0.0
  clip_max: 1.0

output:
  save_model: true
  save_scaler: true
  save_cv_results: true
  save_raw_prediction: true
```

---

# 20. 全体処理フロー

```text
             N-back N数
                 ↓
               L_obj
                 ↓
                 ├──────────────┐
                 │              │
NASA-TLX         │              │
    ↓            │              │
Z標準化          │              │
    ↓            │              │
/4 + 0.5         │              │
    ↓            │              │
clip(0,1)        │              │
    ↓            │              │
  L_sub          │              │
    ↓            ↓              │
    └────→ 重み付き統合 ←──────┘
              ↓
           L_label
              ↑
              │ 学習
              │
Z標準化HR ────┐
              ├─ LinearRegression
Z標準化TEPR ──┘
              ↓
            L_cur_raw
              ↓
          clip(0,1)
              ↓
            L_cur
              ↓
      XR体験調整等で利用
```

---

# 21. 研究上の留意事項

## 21.1 L_objは厳密な認知負荷測定値ではない

`L_obj`はN-backのN数から人工的に設定した課題負荷パラメータであり、参加者が実際に感じた認知負荷そのものではない。

そのため、

```text
0-back = 0.25
1-back = 0.50
2-back = 0.75
3-back = 1.00
```

という割り当てについては、本研究における設計上の定義として明記する。

---

## 21.2 L_subは個人内相対値

NASA-TLXをZ標準化しているため、`L_sub`は絶対的なNASA-TLX得点ではなく、

```text
その参加者自身の平均と比較して
どの程度負荷が高かったか
```

を表す指標となる。

これにより個人差をある程度吸収できる。

---

## 21.3 2秒サンプルの独立性

1つのN-backブロックから多数の2秒サンプルを生成しても、各サンプルに付与される`L_label`は同一である。

したがって、

```text
2秒サンプル数 = 独立した正解ラベル数
```

ではない。

モデル性能評価では、サンプル数だけでなくN-backブロック数を重要なデータ量として扱う。

---

## 21.4 交差検証

同一ブロック内の生体情報は非常に類似しており、同じ`L_label`が付与される。

したがって、同一blockのデータをTrainingとValidationに分割するとデータリークが発生する。

交差検証では必ず、

```text
Group = block_id
```

としてブロック単位で分割する。
