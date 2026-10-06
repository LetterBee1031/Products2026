# VR視覚探索課題 実装設計

## 1. 実装概要

Unity上のVR環境に、結合探索（Conjunction Search）による視覚探索課題を実装する。

課題条件は以下とする。

- Low：Set Size = 5
- Medium：Set Size = 15
- High：Set Size = 25
- 各難易度にTarget Present（標的あり）とTarget Absent（標的なし）の両方を含める
- 試行数ではなく、各ブロックの制限時間によって終了する
- 同一難易度の試行は同じブロック内でまとめて実施する
- 実験状態は既存の`RequestSender.cs`の`PostStatusFlag`関数を用いてサーバへ送信する

---

# 2. Unityシーンファイルに関する制約

`.unity`ファイルには、ユーザの許可なく変更を加えないこと。

Codexが自動的に以下を行ってはならない。

- Scene内へのGameObject追加
- Scene内のGameObject削除
- Transform変更
- Component追加・削除
- Inspector値の変更
- Prefab参照の設定
- Input Action参照の設定
- Scene保存
- `.unity`ファイルの直接編集

実装は基本的にC#スクリプト側で行う。

Scene上で追加設定が必要な場合は、

1. 必要なGameObject
2. 追加するComponent
3. Inspectorで設定する項目
4. 設定する参照先

をユーザに説明する。

`.unity`ファイルそのものは変更しない。

---

# 3. 探索刺激

## 3.1 Target

Targetは以下とする。

- 色：赤
- 形状：Sphere
- 名称：RedSphere

参加者は、

「赤い球体が存在するか」

を判断する。

---

## 3.2 Distractor

以下の2種類を使用する。

### RedCube

- 色：赤
- 形状：Cube

### BlueSphere

- 色：青
- 形状：Sphere

TargetであるRedSphereは、

- 色特徴「赤」をRedCubeと共有する
- 形状特徴「Sphere」をBlueSphereと共有する

ため、色または形状の片方だけではTargetを特定できない。

使用する刺激は以下の3種類のみとする。

```text
RedSphere
RedCube
BlueSphere
```

---

# 4. 難易度

難易度はSet Sizeのみで定義する。

```text
Low    : Set Size = 5
Medium : Set Size = 15
High   : Set Size = 25
```

Set Sizeは、

「1試行中に提示されるTargetとDistractorの総数」

とする。

Inspectorから変更できるようにする。

```text
lowSetSize = 5
mediumSetSize = 15
highSetSize = 25
```

難易度間では以下を変更しない。

- Target種類
- Distractor種類
- 刺激色
- 刺激サイズ
- 探索範囲
- 配置方法

---

# 5. Target Present / Absent

各ブロックには、

- Target Present
- Target Absent

の両方を含める。

## 5.1 Target Present

Target PresentではRedSphereを1個だけ生成する。

```text
RedSphere = 1
Distractor = Set Size - 1
```

### Low

```text
RedSphere  : 1
RedCube    : 2
BlueSphere : 2
Total      : 5
```

### Medium

```text
RedSphere  : 1
RedCube    : 7
BlueSphere : 7
Total      : 15
```

### High

```text
RedSphere  : 1
RedCube    : 12
BlueSphere : 12
Total      : 25
```

---

## 5.2 Target Absent

RedSphereは生成しない。

すべての刺激を、

```text
RedCube
BlueSphere
```

で構成する。

Set Sizeが奇数なので、2種類を完全に同数にはできない。

例えばSet Size = 15では、

```text
RedCube    : 7
BlueSphere : 8
```

または、

```text
RedCube    : 8
BlueSphere : 7
```

とする。

どちらを1個多くするかはランダムに決定する。

---

# 6. ブロック構成

以下の4種類のブロックを扱う。

```text
Practice
Low
Medium
High
```

本番では同一難易度の試行を同じブロック内で連続して実施する。

例：

```text
Low Block
  Set Size = 5
  Present
  Absent
  Present
  Absent
  ...

Medium Block
  Set Size = 15
  Present
  Absent
  ...

High Block
  Set Size = 25
  Present
  Absent
  ...
```

1ブロック中にdifficultyを変更しない。

---

# 7. block_id

各ブロックに`block_id`を割り当てる。

`block_id`はその実験内でブロックを識別するために使用する。

例：

```text
Practice : block_id = 0
Low      : block_id = 1
Medium   : block_id = 2
High     : block_id = 3
```

ただし、ブロック順序の変更に対応できるよう、

```text
block_id == difficulty
```

という固定的な実装にはしないこと。

---

# 8. 時間制限

各ブロックは試行数ではなく実施時間によって終了する。

Inspectorから設定する。

```text
blockDurationSeconds
```

ブロック開始時に計時を開始し、

```text
blockElapsedTime >= blockDurationSeconds
```

となった場合、そのブロックを終了する。

---

## 8.1 制限時間到達時

制限時間の判定は試行間で行う。

進行中の試行がある場合は強制終了しない。

```text
試行開始
↓
刺激提示
↓
回答
↓
データ送信
↓
制限時間確認
↓
超過していればブロック終了
↓
時間内なら次試行
```

回答が完了した試行のみ計測データとして扱う。

---

# 9. Present / Absentの生成

各ブロック内で、

```text
Present : 約50%
Absent  : 約50%
```

となるようにする。

完全な独立ランダムではなく、極端な偏りを防ぐ。

ただし時間制限方式なので、最終的に実施された試行数によっては完全な50:50にならなくてもよい。

---

# 10. 1試行の流れ

## Step 1：前試行の刺激削除

前試行で生成した刺激をすべて削除する。

---

## Step 2：試行条件決定

現在のブロックから以下を取得する。

```text
block_id
difficulty
setSize
is_practice
```

Target Present / Absentを決定する。

---

## Step 3：開始待機

指定時間待機する。

```text
trialStartDelay = 1.0f
```

Inspectorから変更可能にする。

---

## Step 4：刺激生成

条件に従って、

```text
RedSphere
RedCube
BlueSphere
```

を生成する。

総刺激数が必ずSet Sizeと一致することを確認する。

---

## Step 5：刺激提示

全刺激を同時に提示する。

提示開始時点からReaction Timeの計測を開始する。

---

## Step 6：回答

参加者は、

```text
Present
Absent
```

のどちらかを回答する。

Unity Input Systemを使用する。

例：

```text
AnswerPresent
AnswerAbsent
```

具体的なコントローラボタンをコードへハードコードしない。

---

## Step 7：Reaction Time計測

回答入力時に計測を終了する。

```text
reaction_time_ms
```

としてミリ秒単位で保存する。

```text
reaction_time_ms =
(responseTime - stimulusOnsetTime) * 1000
```

---

## Step 8：正誤判定

実際のTarget条件と回答を比較する。

正答：

```text
is_correct = true
```

誤答：

```text
is_correct = false
```

とする。

---

## Step 9：計測データ送信

計測結果を`RequestSender.cs`へ渡す。

`RequestSender.cs`側で送信時刻を付与した後、
Server2.pyへ送信する。

---

## Step 10：刺激削除

現在の試行の刺激を削除する。

---

## Step 11：時間確認

現在のブロック制限時間を確認する。

時間内であれば、

```text
trial_index++
```

として次試行へ進む。

時間を超えていればブロックを終了する。

---

# 11. 刺激配置

刺激は指定された探索範囲内にランダム配置する。

TargetとDistractorに同じ配置アルゴリズムを使用する。

以下を満たすこと。

1. 探索範囲内に配置する
2. オブジェクト同士を重ねない
3. 壁などの外側に配置しない
4. Target位置に規則性を持たせない
5. Present / Absentで配置アルゴリズムを変更しない

刺激同士の最小距離として、

```text
minObjectDistance
```

をInspectorから設定できるようにする。

配置再試行回数として、

```text
maxPlacementAttempts
```

も設定可能にする。

---

# 12. ランダムSeed

実験の再現性確保のため乱数Seedを使用する。

Inspectorから、

```text
randomSeed
useRandomSeed
```

を設定可能にする。

`useRandomSeed = true`の場合は指定Seedを使用する。

`false`の場合は実行開始時にSeedを生成する。

実際に使用したSeedを計測データにも保存する。

---

# 13. 計測データ

各完了試行について、以下のデータをUnityからサーバへ送信する。

| フィールド | 型 | 内容 |
|---|---|---|
| `user_id` | string | 実験参加者ID。`RequestSender.cs`が保持している値を利用する |
| `block_id` | int | 現在実施しているブロックを識別するID |
| `difficulty` | string | 試行条件。`Practice` / `Low` / `Medium` / `High` |
| `trial_index` | int | 現在のブロック内での試行番号。1から開始 |
| `is_practice` | bool | 練習試行の場合true、本番試行の場合false |
| `target_present` | bool | RedSphereが存在していた場合true、存在していない場合false |
| `is_correct` | bool | Present / Absent回答が正しかった場合true |
| `reaction_time_ms` | float | 刺激提示から回答までの時間。単位はms |
| `randomSeed` | int | 実験で使用している乱数Seed |
| `sent_at` | string | `RequestSender.cs`がサーバ送信時に付与する送信日時 |
| `received_at` | string | `Server2.py`がリクエスト受信時に付与する受信日時 |

---

# 14. タイムスタンプ

## 14.1 sent_at

Unityの実験管理スクリプト側では設定しない。

`RequestSender.cs`がHTTP送信を行う直前に、

```text
sent_at
```

を付与する。

つまり、

```text
VisualSearchExperimentManager
↓
計測結果生成
↓
RequestSender.cs
↓
sent_at付与
↓
HTTP POST
```

とする。

時刻形式は既存のRequestSender.cs / Server2.pyで使用している形式があれば、それに合わせる。

特に既存形式がなければISO 8601形式を使用する。

例：

```text
2026-10-06T17:42:31.123+09:00
```

---

## 14.2 received_at

Unityから送信しない。

`Server2.py`がVisual Searchデータを受信した時点で、

```text
received_at
```

を生成して保存データへ追加する。

処理は、

```text
HTTP Request受信
↓
received_at生成
↓
受信データと結合
↓
保存
```

とする。

これにより、

- Unity側送信時刻
- サーバ側受信時刻

を別々に記録する。

---

# 15. user_id

Visual Search側にuser_id設定機能を新規実装しない。

既存の`RequestSender.cs`で設定されているuser_idを参照する。

VisualSearchExperimentManager等で、

```text
[SerializeField]
private string userId;
```

のような重複管理を作らない。

データ送信時に`RequestSender.cs`が保持しているuser_idを使用する。

---

# 16. データ通信

計測データはローカルCSVやJSONへ保存しない。

以下の経路で保存する。

```text
VisualSearchExperimentManager
↓
RequestSender.cs
↓
HTTP POST
↓
Server2.py
↓
サーバ側保存
```

---

# 17. RequestSender.cs

既存の`RequestSender.cs`を拡張する。

Visual Search専用の新しいHTTP通信クラスを作成しない。

既存の通信方式を再利用する。

Visual Search結果送信用メソッドを追加する。

例：

```text
SendVisualSearchResult(...)
```

実際のメソッド名は既存コード構成に合わせてよい。

---

## 17.1 RequestSenderで行う処理

Visual Searchデータ送信時に以下を行う。

1. 既存のuser_idを取得
2. Visual Search試行結果を受け取る
3. `sent_at`を生成
4. 送信用JSONを生成
5. Server2.pyへPOST
6. 成功・失敗をDebug.Log等へ出力

---

# 18. Server2.py

既存の`Server2.py`を拡張する。

Visual Search結果を受信するFastAPIエンドポイントを追加する。

受信時には、

```text
received_at
```

をServer2.py側で生成する。

Unityから`received_at`を受信してはならない。

---

## 18.1 Server2.pyで行う処理

```text
RequestSender.csからPOST受信
↓
受信データ検証
↓
received_at生成
↓
保存データ生成
↓
サーバ側保存
```

既存の保存形式やディレクトリ構成がある場合はそれを再利用する。

---

# 19. 状態イベント

実験状態の通知用に独自のイベント送信機構を作成しない。

既存の`RequestSender.cs`に存在する、

```text
PostStatusFlag(...)
```

を使用する。

状態イベントとして使用する文字列は以下の5種類のみとする。

```text
Practice
Low
Medium
High
block_end
```

表記は上記をそのまま使用する。

---

# 20. ブロック開始イベント

各ブロック開始時に、そのブロックに対応するstatus flagを`PostStatusFlag`で送信する。

## Practiceブロック開始時

```text
PostStatusFlag("Practice")
```

## Lowブロック開始時

```text
PostStatusFlag("Low")
```

## Mediumブロック開始時

```text
PostStatusFlag("Medium")
```

## Highブロック開始時

```text
PostStatusFlag("High")
```

---

# 21. ブロック終了イベント

Practice / Low / Medium / Highのどのブロックでも、
終了時には必ず、

```text
PostStatusFlag("block_end")
```

を送信する。

処理例：

```text
Low Block開始
↓
PostStatusFlag("Low")
↓
Low課題実施
↓
制限時間到達
↓
進行中の試行を完了
↓
試行データ送信
↓
PostStatusFlag("block_end")
↓
Low Block終了
```

---

# 22. Status Flag送信タイミング

## ブロック開始

ブロックの計時開始直前または同時に送信する。

```text
Block開始処理
↓
PostStatusFlag("Low" 等)
↓
block timer開始
↓
試行開始
```

## ブロック終了

最後の試行結果を確定・送信した後に送信する。

```text
最後の試行回答
↓
試行データ送信
↓
ブロック終了判定
↓
PostStatusFlag("block_end")
↓
Block終了
```

---

# 23. Status Flagと試行データ

Status FlagとVisual Search試行結果は別の通信として扱う。

### Status Flag

既存の、

```text
PostStatusFlag(...)
```

を使用する。

### Visual Search試行結果

Visual Search結果用の送信処理を使用する。

この2つを1つのJSONに統合しない。

---

# 24. 練習ブロック

Practiceも1つのブロックとして扱う。

Practice開始時：

```text
PostStatusFlag("Practice")
```

Practice終了時：

```text
PostStatusFlag("block_end")
```

Practice内の試行データは、

```text
is_practice = true
difficulty = "Practice"
```

として送信する。

Low / Medium / Highでは、

```text
is_practice = false
```

とする。

---

# 25. Unityスクリプト構成

## VisualSearchExperimentManager.cs

役割：

- 実験進行
- Practice / Low / Medium / Highブロック管理
- block_id管理
- difficulty管理
- trial_index管理
- blockDurationSeconds管理
- ブロック開始時の`PostStatusFlag`呼び出し
- ブロック終了時の`PostStatusFlag("block_end")`
- 回答受付
- Reaction Time計測
- 正誤判定
- RequestSenderへの試行結果送信要求

---

## VisualSearchStimulusSpawner.cs

役割：

- RedSphere生成
- RedCube生成
- BlueSphere生成
- Set Size管理
- Present / Absentに応じた刺激数計算
- ランダム配置
- 重複防止
- 刺激削除

---

## VisualSearchTrialGenerator.cs

役割：

- Present / Absent生成
- Present / Absent比率管理
- randomSeed管理

difficultyはブロック側で固定する。

---

## VisualSearchUIController.cs

役割：

- ブロック開始表示
- 練習時フィードバック
- ブロック終了表示
- 実験終了表示

SceneへのUIオブジェクト追加が必要な場合は、
`.unity`ファイルを変更せず、必要な設定方法をユーザへ説明する。

---

## RequestSender.cs

既存ファイルを拡張する。

役割：

- user_id管理
- Visual Search結果受け取り
- `sent_at`付与
- JSON生成
- Server2.pyへのPOST
- `PostStatusFlag`によるブロック状態送信

---

## Server2.py

既存ファイルを拡張する。

役割：

- Visual Searchデータ受信
- `received_at`付与
- 試行結果保存
- Status Flag受信・保存

---

# 26. Inspector設定項目

最低限以下をInspectorから変更可能にする。

## Block

```text
blockDurationSeconds
```

## Difficulty

```text
lowSetSize = 5
mediumSetSize = 15
highSetSize = 25
```

## Timing

```text
trialStartDelay = 1.0f
```

## Placement

```text
spawnArea
minObjectDistance
maxPlacementAttempts
stimulusHeight
```

## Prefab

```text
redSpherePrefab
redCubePrefab
blueSpherePrefab
```

## Random

```text
randomSeed
useRandomSeed
```

## Input

```text
answerPresentAction
answerAbsentAction
```

## Communication

```text
RequestSender
```

user_id入力欄はVisual Search側には追加しない。

---

# 27. 重要な実装ルール

## Scene

`.unity`ファイルはユーザの明示的な許可なしに変更しない。

---

## Set Size

```text
generatedObjectCount == setSize
```

を必ず保証する。

---

## Target数

Present：

```text
RedSphere = 1
```

Absent：

```text
RedSphere = 0
```

とする。

---

## 難易度

1ブロック内ではSet Sizeを変更しない。

```text
Low    -> 5
Medium -> 15
High   -> 25
```

---

## 時間制限

ブロックは試行数ではなく、

```text
blockDurationSeconds
```

で終了する。

時間到達時に進行中の試行がある場合は、
その試行を完了してから終了する。

---

## user_id

RequestSender.csの既存値のみを使用する。

---

## 送信時刻

`RequestSender.cs`がHTTP送信直前に、

```text
sent_at
```

を付与する。

---

## 受信時刻

`Server2.py`がHTTPリクエスト受信時に、

```text
received_at
```

を付与する。

---

## ブロック開始通知

以下のいずれかを`PostStatusFlag`で送信する。

```text
Practice
Low
Medium
High
```

---

## ブロック終了通知

必ず、

```text
block_end
```

を`PostStatusFlag`で送信する。

---

# 28. 今回実装しないもの

以下はVisual Search側では直接実装しない。

- Paas尺度
- NASA-TLX
- 心拍取得
- HRV取得
- 瞳孔径取得
- 認知負荷推定モデル
- ローカルCSV保存
- ローカルJSON保存
- 独自Status Flag通信機構
- 新規user_id管理機構
- `.unity`ファイルの自動編集

---

# 29. コーディング方針

- Unity Input Systemを使用する
- OpenXR環境で動作可能にする
- XR Originを前提とする
- HMD固有APIへの依存をできるだけ避ける
- 既存のRequestSender.csを最大限再利用する
- 既存のServer2.pyを拡張する
- Status Flagには既存のPostStatusFlag関数を使用する
- 主要パラメータはInspectorから変更可能にする
- 1つの巨大なスクリプトにまとめず責務を分離する
- SerializeField等には用途が分かる日本語コメントを付ける
- 主要処理にも日本語コメントを付ける
- データフィールドには意味が分かるコメントを付ける
- XML summaryタグは使用しない
- 不要な空行を増やさない
- `.unity`ファイルを無断変更しない
- Scene変更が必要な場合は操作手順のみ提示する
- ソースコードを出力する場合は省略せず全文を提示する
- RequestSender.csを変更する場合は変更後全文を提示する
- Server2.pyを変更する場合も変更後全文を提示する