# NASA-TLX回答機能 実装設計

## Unity / C# / Codex向け

# 1. 目的

Unity上のXR空間内でNASA-TLXの6項目に回答するUIを実装する。

回答結果はUnity側ではファイル保存せず、既存の`RequestSender.cs`を利用してサーバへ送信する。

NASA-TLXのRaw値算出やデータ保存はサーバ側で実施するため、Unity側では各項目の**生の回答値のみを取得・保持・送信する**。

---

# 2. 実装方針

Unity側の責務は以下とする。

1. NASA-TLX回答UIを表示する
2. 6項目を1項目ずつ提示する
3. Sliderから回答値を取得する
4. 回答を一時的にメモリ上へ保持する
5. 6項目回答完了後に`RequestSender.cs`を用いて回答をサーバへ送信する
6. 回答完了後に`RequestSender.cs`の`PostStatusFlag`を呼び出す
7. 回答UIを終了する

Unity側では以下を実施しない。

* CSVファイル生成
* ローカルファイル保存
* Raw NASA-TLX算出
* NASA-TLXの標準化
* 認知負荷ラベル算出
* 回帰モデル処理

これらはすべてサーバ側の責務とする。

---

# 3. 前提となる既存実装

以下の既存ファイルを利用する。

```text
RequestSender.cs
```

`RequestSender.cs`には既にサーバ送信用の関数が実装されている。

Codexによる実装時には、必ず既存の`RequestSender.cs`を確認し、

* メソッド名
* 引数
* Request Body
* JSON形式
* userIDの取得方法
* block情報の渡し方
* 非同期処理方式

を既存実装に合わせること。

NASA-TLX側で独自のHTTP通信処理を新規実装しない。

また、回答完了後の状態通知には、

```csharp
PostStatusFlag(...)
```

を使用する。

---

# 4. NASA-TLX項目

以下の6項目を使用する。

| 順番 | 項目              | 内部識別名             |
| -: | --------------- | ----------------- |
|  1 | Mental Demand   | `mental_demand`   |
|  2 | Physical Demand | `physical_demand` |
|  3 | Temporal Demand | `temporal_demand` |
|  4 | Performance     | `performance`     |
|  5 | Effort          | `effort`          |
|  6 | Frustration     | `frustration`     |

質問は1項目ずつ表示する。

---

# 5. 回答尺度

Sliderを用いた整数尺度とする。

デフォルト値は、

```text
下限 = 0
上限 = 20
```

とする。

ただし、下限値・上限値はInspectorから変更可能にする。

例：

```csharp
[SerializeField] private int minValue = 0;
[SerializeField] private int maxValue = 20;
```

Sliderには実行時に、

```csharp
slider.minValue = minValue;
slider.maxValue = maxValue;
slider.wholeNumbers = true;
```

を設定する。

これにより、デフォルトでは、

```text
0, 1, 2, ... , 20
```

の21段階で回答できる。

---

# 6. UI構成

World Space Canvas上に以下のUIを配置する。

```text
NASA_TLX_Panel
│
├─ ProgressText
│
├─ QuestionTitle
│
├─ QuestionDescription
│
├─ LowLabel
│
├─ Slider
├─ HighLabel
│
├─ MinusButton
├─ PlusButton
│
└─ ConfirmButton
```

現在値を数値として表示するUIは設けない。

---

# 6.1 ProgressText

現在の質問番号を表示する。

例：

```text
1 / 6
```

```text
4 / 6
```

---

# 6.2 QuestionTitle

現在のNASA-TLX項目名を表示する。

例：

```text
精神的要求
```

---

# 6.3 QuestionDescription

質問内容を表示する。

例：

```text
この課題では、どの程度の精神的・知覚的活動が必要でしたか？
```

---

# 6.4 LowLabel / HighLabel

各尺度の両端に説明文を表示する。

例：

```text
低い                               高い
```

項目によって表現を変更できる設計とする。

---

# 6.5 Slider

NASA-TLX回答値を入力する。

回答値は整数のみとする。

```csharp
slider.wholeNumbers = true;
```

Sliderの現在値そのものは画面上に表示しない。

---

# 6.6 MinusButton / PlusButton

XR環境でSliderの細かい操作を行いやすくするために配置する。

MinusButton：

```text
Slider値を1減少
```

PlusButton：

```text
Slider値を1増加
```

下限値・上限値を超えないようにする。

例：

```csharp
slider.value = Mathf.Clamp(
    slider.value + 1,
    minValue,
    maxValue
);
```

---

# 6.7 ConfirmButton

現在の回答を確定する。

回答確定後、

```text
回答値を一時保存
↓
次の質問を表示
```

する。

6問目の場合は送信処理へ進む。

---

# 7. Slider初期状態

質問表示時にはSliderを中央付近へ設定する。

例えば、

```csharp
slider.value = (minValue + maxValue) / 2f;
```

とする。

ただし、初期値をそのまま回答として確定してしまうことを防ぐため、

```csharp
hasAnswered = false;
confirmButton.interactable = false;
```

とする。

以下のいずれかを操作した時点で、

```text
Slider
MinusButton
PlusButton
```

`hasAnswered = true`とし、ConfirmButtonを有効化する。

---

# 8. 質問データ管理

質問情報は、質問ごとに以下の情報を保持する。

```csharp
[System.Serializable]
public class NasaTlxQuestion
{
    public string id;
    public string title;
    public string description;
    public string lowLabel;
    public string highLabel;
}
```

NASA-TLXの質問内容を`NasaTlxManager.cs`内にハードコードしてもよいが、Inspectorから変更可能な配列として保持することを推奨する。

例：

```csharp
[SerializeField]
private NasaTlxQuestion[] questions;
```

質問順序は原則として固定とする。

---

# 9. 回答データの保持

回答は別クラスを作らず、`NasaTlxManager.cs`内部で保持してよい。

例：

```csharp
private int mentalDemand;
private int physicalDemand;
private int temporalDemand;
private int performance;
private int effort;
private int frustration;
```

または、

```csharp
private Dictionary<string, int> answers;
```

を利用してもよい。

ただし、`RequestSender.cs`へ渡す際に既存の送信形式へ容易に変換できる実装とする。

---

# 10. クラス構成

初期実装では以下の2ファイルを基本とする。

```text
NASA-TLX/
│
├─ NasaTlxManager.cs
│
└─ NasaTlxQuestion.cs
```

既存ファイル：

```text
RequestSender.cs
```

を利用する。

以下のクラスは新規作成しない。

```text
NasaTlxResult.cs
NasaTlxCsvLogger.cs
```

理由：

* データ送信は`RequestSender.cs`が担当する
* データ保存はサーバ側が担当する
* NASA-TLX回答は`NasaTlxManager.cs`内部で一時保持できる

---

# 11. NasaTlxManager.csの責務

`NasaTlxManager.cs`は以下を担当する。

* NASA-TLX回答開始
* UI表示・非表示
* 質問切り替え
* Slider制御
* ±ボタン制御
* ConfirmButton制御
* 回答一時保持
* 回答完了判定
* `RequestSender.cs`への送信依頼
* `PostStatusFlag`の呼び出し

---

# 12. 主なSerializeField

以下をInspectorから設定可能とする。

```csharp
[SerializeField] private GameObject nasaTlxPanel;

[SerializeField] private TMP_Text progressText;
[SerializeField] private TMP_Text questionTitle;
[SerializeField] private TMP_Text questionDescription;

[SerializeField] private TMP_Text lowLabel;
[SerializeField] private TMP_Text highLabel;

[SerializeField] private Slider answerSlider;

[SerializeField] private Button minusButton;
[SerializeField] private Button plusButton;
[SerializeField] private Button confirmButton;

[SerializeField] private int minValue = 0;
[SerializeField] private int maxValue = 20;

[SerializeField] private RequestSender requestSender;
```

必要に応じて質問配列も追加する。

```csharp
[SerializeField]
private NasaTlxQuestion[] questions;
```

---

# 13. 内部状態

最低限以下を保持する。

```csharp
private int currentQuestionIndex = 0;
private bool hasAnswered = false;
private bool isSending = false;
```

回答値：

```csharp
private int mentalDemand;
private int physicalDemand;
private int temporalDemand;
private int performance;
private int effort;
private int frustration;
```

必要に応じて、

```csharp
private string currentBlockId;
```

等を保持する。

`userID`については既存システムの管理方法に合わせる。

---

# 14. NASA-TLX開始処理

外部からNASA-TLXを開始できる公開メソッドを用意する。

例：

```csharp
public void StartQuestionnaire(string blockId)
```

または、既存システムの設計に合わせて、

```csharp
public void StartQuestionnaire()
```

とする。

Codexは既存コードを確認し、`userID`や`block_id`をどこで管理しているかに合わせること。

開始処理：

```text
回答状態初期化
↓
currentQuestionIndex = 0
↓
回答値初期化
↓
NASA-TLX Panel表示
↓
1問目表示
```

---

# 15. 質問表示処理

以下のようなメソッドを用意する。

```csharp
private void ShowQuestion(int index)
```

処理：

```text
QuestionTitle更新
↓
QuestionDescription更新
↓
LowLabel更新
↓
HighLabel更新
↓
ProgressText更新
↓
Slider初期値設定
↓
hasAnswered = false
↓
ConfirmButton無効化
```

---

# 16. Slider操作

Sliderの`OnValueChanged`イベントから、

```csharp
public void OnSliderValueChanged(float value)
```

を呼ぶ。

このメソッドでは回答値の画面表示は行わない。

実施する処理は、

```text
hasAnswered = true
↓
ConfirmButton有効化
```

のみでよい。

---

# 17. ±ボタン

以下のメソッドを用意する。

```csharp
public void IncreaseValue()
```

```csharp
public void DecreaseValue()
```

処理例：

```csharp
answerSlider.value = Mathf.Clamp(
    answerSlider.value + 1,
    minValue,
    maxValue
);
```

減少時も同様とする。

操作時には、

```csharp
hasAnswered = true;
confirmButton.interactable = true;
```

とする。

---

# 18. 回答確定

ConfirmButton押下時、

```csharp
public void ConfirmAnswer()
```

を呼び出す。

以下の条件を満たさない場合は処理しない。

```text
hasAnswered == true
isSending == false
```

現在値は、

```csharp
int answer = Mathf.RoundToInt(answerSlider.value);
```

で取得する。

回答値は必ず、

```csharp
Mathf.Clamp(answer, minValue, maxValue)
```

によって範囲内にする。

---

# 19. 回答保存

`currentQuestionIndex`に応じて回答を変数へ保存する。

例：

```text
0 → mentalDemand
1 → physicalDemand
2 → temporalDemand
3 → performance
4 → effort
5 → frustration
```

回答保存後、

```csharp
currentQuestionIndex++;
```

する。

---

# 20. 次質問への遷移

以下の場合、

```text
currentQuestionIndex < 6
```

次の質問を表示する。

```csharp
ShowQuestion(currentQuestionIndex);
```

6項目すべて回答済みの場合、

```csharp
CompleteQuestionnaire();
```

を実行する。

---

# 21. 回答完了処理

```csharp
private void CompleteQuestionnaire()
```

を実装する。

処理順序は以下とする。

```text
6項目回答済み確認
↓
isSending = true
↓
回答UI操作無効化
↓
RequestSender.csでNASA-TLX回答を送信
↓
送信成功確認
↓
PostStatusFlag実行
↓
NASA-TLX Panel非表示
↓
状態初期化
```

---

# 22. NASA-TLX回答の送信

CSV保存処理はUnity側では実施しない。

回答送信には既存の、

```text
RequestSender.cs
```

を使用する。

Codexによる実装時には必ず`RequestSender.cs`の既存コードを確認する。

特に、

* 既存HTTP POSTメソッド
* JSON Request Body
* DTO
* userID
* block_id
* timestamp
* エンドポイント
* Coroutine / async方式

を参照すること。

NASA-TLX用に送信関数を追加する必要がある場合でも、通信処理そのものを`NasaTlxManager.cs`へ記述しない。

---

# 23. サーバへ送信する値

最低限以下を送信する。

```text
userID
block_id
mental_demand
physical_demand
temporal_demand
performance
effort
frustration
```

必要に応じて、

```text
answered_at
```

も送信する。

ただし、最終的な送信形式は必ず`RequestSender.cs`内の既存フォーマットに従う。

NASA-TLX側で独自のRequest Body形式を定義しない。

---

# 24. Raw NASA-TLX

Unity側ではRaw NASA-TLXを算出しない。

Unityからは、

```text
mental_demand
physical_demand
temporal_demand
performance
effort
frustration
```

の生値のみ送信する。

以下はサーバ側で実施する。

```text
Raw NASA-TLX算出
↓
NASA-TLX標準化
↓
L_sub算出
↓
L_objとの統合
↓
L_label生成
```

---

# 25. PostStatusFlag

NASA-TLX回答送信完了後、

```csharp
RequestSender.PostStatusFlag(...)
```

を利用して状態を通知する。

NASA-TLX側で独自のUnityEventによる回答完了通知機構は初期実装では作成しない。

処理イメージ：

```text
NASA-TLX回答完了
↓
回答POST
↓
回答POST成功
↓
PostStatusFlag(...)
↓
次の処理へ
```

`PostStatusFlag`へ渡す具体的な値は、既存の`RequestSender.cs`および現在の実験状態管理仕様を参照すること。

Codexが独自のStatus値を新規定義しない。

---

# 26. 送信中のUI

回答送信開始後は、

```csharp
isSending = true;
```

とする。

送信中は、

* Slider
* MinusButton
* PlusButton
* ConfirmButton

を操作不可にする。

多重送信を防止する。

送信完了後にNASA-TLX Panelを閉じる。

---

# 27. 送信失敗時

サーバ送信に失敗した場合、

```text
NASA-TLX Panelを閉じない
```

ことを基本とする。

また、

```csharp
isSending = false;
```

へ戻す。

必要に応じてConfirmButtonを再度有効化し、再送可能な状態にする。

ただし、RequestSender側ですでにリトライ機構が実装されている場合は、そちらを優先して利用する。

同一回答の二重保存が発生しないよう既存通信仕様を確認すること。

---

# 28. Slider設定エラー

Sliderの下限・上限はInspectorから変更可能とする。

デフォルト：

```text
minValue = 0
maxValue = 20
```

実行開始時に以下を検証する。

```text
minValue < maxValue
```

満たさない場合は、

```csharp
Debug.LogError(...)
```

を出力する。

必要に応じてデフォルト値、

```text
0 ～ 20
```

へフォールバックする。

---

# 29. その他のエラー処理

開始時に以下を確認する。

* `nasaTlxPanel != null`
* `answerSlider != null`
* `confirmButton != null`
* `requestSender != null`
* NASA-TLX質問が6件存在する
* `minValue < maxValue`

回答完了時に以下を確認する。

* 6項目すべて回答済み
* 回答値が設定範囲内
* `RequestSender`が利用可能
* 送信中ではない

---

# 30. Unity Hierarchy例

```text
NASA_TLX_System
│
├─ NasaTlxManager
│
└─ NASA_TLX_Canvas
    │
    └─ NASA_TLX_Panel
        │
        ├─ ProgressText
        ├─ QuestionTitle
        ├─ QuestionDescription
        │
        ├─ ScaleArea
        │   ├─ LowLabel
        │   ├─ Slider
        │   └─ HighLabel
        │
        ├─ MinusButton
        ├─ PlusButton
        │
        └─ ConfirmButton
```

Canvasは、

```text
Render Mode = World Space
```

を想定する。

---

# 31. XR操作

既存XR UI操作方式をそのまま利用する。

XR Interaction Toolkitを利用している場合は、

```text
XR Controller
↓
XR Ray Interactor
↓
Tracked Device Graphic Raycaster
↓
NASA-TLX UI
```

という構成とする。

NASA-TLX専用の独自入力システムは作成しない。

---

# 32. 想定処理フロー

```text
N-back終了
↓
NASA-TLX開始
↓
Question 1
Mental Demand
↓
回答
↓
Question 2
Physical Demand
↓
回答
↓
Question 3
Temporal Demand
↓
回答
↓
Question 4
Performance
↓
回答
↓
Question 5
Effort
↓
回答
↓
Question 6
Frustration
↓
回答
↓
全6回答をRequestSenderへ渡す
↓
サーバへPOST
↓
サーバ側で保存
↓
PostStatusFlag(...)
↓
NASA-TLX UI終了
```

---

# 33. Codex実装時の重要事項

Codexは実装開始前に必ず以下を確認すること。

## 33.1 RequestSender.cs

最優先で確認する。

特に、

```text
既存POST処理
送信形式
userID取得方法
block_id取得方法
PostStatusFlag
非同期処理方式
エラー処理方式
```

を確認する。

既存設計を無視して類似通信機能を重複実装しない。

---

## 33.2 既存Experiment Manager等

`userID`や現在のN-back条件、`block_id`を管理している既存クラスがある場合は、それを利用する。

NASA-TLX側で別の識別子管理機構を作らない。

---

## 33.3 UI参照

UIオブジェクトは可能な限り、

```csharp
[SerializeField]
```

でInspectorから設定する。

`GameObject.Find()`等による実行時検索は原則使用しない。

---

## 33.4 通信とUIの責務分離

`NasaTlxManager.cs`

```text
UI制御
回答管理
質問遷移
```

`RequestSender.cs`

```text
HTTP通信
Request Body生成
サーバ送信
Status送信
```

として責務を分離する。

---

# 34. 受け入れ基準

以下をすべて満たした場合、初期実装完了とする。

1. NASA-TLX Panelを外部から表示できる
2. 6項目を1問ずつ表示できる
3. 進捗`1 / 6`～`6 / 6`を表示できる
4. Sliderで整数値を選択できる
5. Slider下限・上限をInspectorから設定できる
6. デフォルト範囲が0～20である
7. 現在のSlider数値を画面表示しない
8. ±ボタンで1段階ずつ調整できる
9. Slider未操作状態では回答確定できない
10. 各回答を一時的に保持できる
11. 6項目終了後に回答データをまとめられる
12. Unity側でCSV保存を行わない
13. Unity側でRaw NASA-TLXを算出しない
14. `RequestSender.cs`を利用して回答を送信できる
15. Request形式が既存`RequestSender.cs`の仕様に従っている
16. 回答送信完了後に`PostStatusFlag`を呼び出せる
17. 送信中の多重操作・多重送信を防止できる
18. 送信失敗時に回答内容を失わない
19. NASA-TLX専用のHTTP処理を`NasaTlxManager.cs`に実装していない
20. HMD上のWorld Space UIから操作できる

---

# 35. 実装対象ファイル

新規作成：

```text
NasaTlxManager.cs
NasaTlxQuestion.cs
```

既存ファイルを必要に応じて変更：

```text
RequestSender.cs
```

新規作成しない：

```text
NasaTlxResult.cs
NasaTlxCsvLogger.cs
```

---

# 36. 最終方針

Unity側は、

```text
NASA-TLXを提示
↓
0～20で回答
↓
6回答を一時保持
↓
RequestSenderへ渡す
```

までを担当する。

`RequestSender.cs`は、

```text
既存フォーマットへ変換
↓
サーバへ送信
↓
PostStatusFlag
```

を担当する。

サーバ側は、

```text
回答保存
↓
Raw NASA-TLX算出
↓
認知負荷モデル用のラベル生成
```

を担当する。

これにより、

```text
UI
通信
保存・分析
```

の責務を明確に分離する。
