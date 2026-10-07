# 視覚探索課題の組み込み

対象は Unity 6000.3.8f1 / Input System / OpenXR の既存プロジェクトです。
最小構成版では詳細な設定検証を省いています。必須参照、正のScale、3以上の刺激数、正の制限時間をInspectorで設定してください。
シーン、Prefab、Input Actionアセットは自動変更していません。以下をUnity Editorで設定してください。

## 1. GameObjectとComponent

| GameObject | Component | 設定・参照先 |
|---|---|---|
| VisualSearch（新規の空オブジェクト） | VisualSearchExperimentManager | Request Sender = 既存のRequestSender、Stimulus Spawner = 下記Component、UI Controller = 任意の表示Component |
| VisualSearch | VisualSearchStimulusSpawner | Spawn Area = 下記BoxCollider、Search Origin = XR Origin配下のMain Camera、3つのPrefab参照 |
| SearchArea（新規） | BoxCollider | Is Trigger = ON、TransformのScale = (1,1,1)。室内に収まる探索範囲をSizeで設定 |
| World Space Canvas配下の説明テキスト | TextMeshProUGUI、VisualSearchUIController | Message Text = 同じTextMeshProUGUI。日本語を表示できるFont Assetを使用 |
| World Space Canvas配下の時間入力欄 | TMP_InputField | VisualSearchExperimentManagerのBlock Duration Input Fieldへ設定 |
| 本番終了後の遷移ボタン | Button | VisualSearchExperimentManagerのButton Move For Questionへ設定し、On ClickにMoveToNasaTlxQuestionnaireを登録 |

`VisualSearchTrialGenerator`は通常のC#クラスのためComponentとして追加しません。
RequestSenderは実験終了通知まで有効に保ってください。Managerと別の常時有効なGameObjectへの配置を推奨します。
ほかの課題Managerの自動開始は無効にし、回答Actionや状態通知を同時に使用しないようにしてください。

## 2. 刺激Prefabと探索範囲

- UnityのSphereから`RedSphere`と`BlueSphere`、Cubeから`RedCube`のPrefabを用意します。
- 各PrefabのルートにMeshFilterと有効なMeshRendererを1つずつ配置します。子Renderer、Animator、位置・サイズ・色を変更するスクリプトは使用しません。
- 3種類とも同じ正の均一Scale（例：0.12）にします。RedSphereとRedCubeは同一の赤いMaterial、BlueSphereは青いMaterialを設定します。
- 色・サイズ・領域は全難易度で共通です。生成時の回転はワールド座標の無回転です。Rigidbodyは付けないか、Prefab側でIs KinematicをONにしてください。
- 探索面はSearchAreaのローカルXZ平面です。`stimulusHeight`はBoxCollider中心からのローカルYオフセットです。眼前の垂直平面にする場合はSearchAreaをX軸に90度回転させます。
- 例：SearchAreaを眼前約2 m、Sizeを(2, 0.4, 2)、Stimulus Heightを0、Min Object Distanceを0.3に設定します。実環境の部屋・視野に合わせて調整してください。
- 刺激の外接球全体が領域内に入るよう境界を縮め、刺激同士の距離も外接球の直径以上に保ちます。Cubeを含む共通の安全距離なので保守的な配置です。
- Obstacle Layersには壁・床・家具のColliderのLayerを指定し、XRリグ・コントローラ・UIを除外してください。Trigger Colliderは障害物として扱いません。
- SearchArea全体を室内に置いてください。障害物との接触と、Search Originから刺激までの遮蔽を検査します。壁にColliderがなければ検出できません。
- Max Placement Attemptsは1刺激につき1000回が既定です。配置できない場合は刺激を部分表示せず、Consoleに警告を出して対応する`visual_{試行レベル}_end`を送信し停止します。刺激数を自動で減らす処理はありません。

## 3. 入力・開始・停止

Input Actionsに`AnswerPresent`（赤い球あり）と`AnswerAbsent`（なし）を作り、どちらもAction TypeをButtonにします。
異なるコントローラボタンをBindingsに設定し、Managerの対応するInputActionReferenceへ割り当てます。
Interactionsは未設定（標準の押下時判定）にしてください。Hold / Release Onlyにすると反応時間の意味が変わります。
コードに機種固有ボタンはありません。Editor検証用のキーボードBindingもAction側で追加できます。

Practice、Low、Medium、High用のUI Buttonを4個用意し、各ButtonのOn Clickへ同じ`VisualSearchExperimentManager`を登録します。

- Practiceボタン：`StartPractice()`
- Lowボタン：`StartLow()`
- Mediumボタン：`StartMedium()`
- Highボタン：`StartHigh()`

ボタンを押すと、選択したブロックだけを実施します。実行中に別の開始ボタンを押しても新しいブロックは始まりません。
中断は`StopExperiment()`または`Stop Visual Search`です。未回答試行は送信しません。
回答は刺激提示中の最初の1回だけ採用し、押しっぱなしの入力は両ボタンを離してから受け付けます。

時間入力欄を選択すると、`XRNumericKeyboardInputBinder`を介して`XRNumericKeyboard`が表示されます。
キーボードのOK、またはInputFieldの編集終了時に`blockDurationSeconds`へ反映されます。小数入力も可能です。
既存の`XRNumericKeyboardInputBinder`をVisualSearchExperimentManagerの`Numeric Keyboard Input Binder`へ設定してください。
未設定の場合は同じGameObjectから取得し、存在しなければ実行時に追加します。

## 4. NASA-TLXへの遷移

VisualSearchExperimentManagerの`Nasa Tlx Manager`へ、シーン内の既存`NasaTlxManager`を設定します。
未設定の場合は、StroopManagerやMentalArithmeticManagerと同様にEventSystem上のComponentを探し、その後シーン全体から検索します。

Low・Medium・Highを正常終了すると`Button Move For Question`が表示されます。
そのボタンのOn Clickから`MoveToNasaTlxQuestionnaire()`を呼ぶと、完了した本番ブロックの`block_id`を渡してNASA-TLXを開始します。
Practice終了時と中断時にはNASA-TLX遷移ボタンを表示しません。

## 5. ブロック・乱数・時間

ブロックの固定実施順はありません。UI Buttonを押した順に、Practice / Low / Medium / Highを個別に実施します。
Practiceの`block_id`は`visual_0`です。Low・Medium・Highは難易度に固定せず、UIボタンから開始した順に`visual_1`、`visual_2`、`visual_3`が割り当てられます。
例えばHigh → Low → Mediumの順に開始した場合、High=`visual_1`、Low=`visual_2`、Medium=`visual_3`になります。
同じ条件を再度開始した場合は、その条件へ最初に割り当てた`block_id`を再利用します。
本番のSet SizeはLow=5、Medium=15、High=25です。3以上で変更できます。
1ブロック120秒、試行前待機1秒が既定です。ブロック内では刺激数・難易度・制限時間を固定します。
実験中の設定変更、RequestSenderのUser Id変更、SearchArea移動は避けてください。

練習では次の6条件を、`Practice Repetitions Per Condition`に指定した回数ずつ実施します。

- Set Size 5：Target Present / Target Absent
- Set Size 15：Target Present / Target Absent
- Set Size 25：Target Present / Target Absent

既定値1の場合、練習は合計6試行です。値が2なら各条件2回、合計12試行です。
条件の実施回数を保ったまま、練習内の提示順をランダム化します。練習の終了は時間制限ではなく、全条件の完了で決まります。

制限時間には試行前待機、回答待ち、通信待ち、練習フィードバックを含みます。
ブロック前後の説明表示は含みません。時間はTime.timeScaleに依存しません。
開始通知直前から計時し、時間判定は試行間だけです。開始済み試行は回答まで待つため、制限時間を超えることがあります。
無回答で自動終了するタイムアウトはありません。必要なら実験者が中断してください。
非常に短い制限時間や長い回答時間では、両条件を実施しきれない場合があります。

Present/Absentは2試行ごとに1回ずつ、順序をランダム化します。途中終了で数の差は最大1です。
Presentは赤い球が1個、Absentは0個。残りをRedCube/BlueSphereにできるだけ等分します。
Distractor数が奇数の場合はどちらを1個多くするかをランダムに決めます。
`Use Random Seed=true`で指定Seed、falseで実行時Seedを生成します（設計書の意味に合わせています）。
実際のSeedは全試行に保存します。同一Seed、設定、選択したブロック、実施試行数、環境Colliderで条件・配置を再現できます。
時刻計測はUnityで全刺激を有効化した時点から入力コールバックまでです。HMDの実表示時刻をハードウェア測定するものではありません。

## 6. 通信・保存

RequestSenderの既存Base URLとUser Idを使います。別の参加者ID設定やローカル結果ファイルは作りません。
視覚探索の5種類の状態通知だけを使う場合、既存RequestSenderの`Send Start Flag On Start`をOFFにしてください。

1. 開始：既存`PostStatusFlag`で`visual_practice_start` / `visual_low_start` / `visual_medium_start` / `visual_high_start`と`visual_{id}`形式のブロックIDを送信。
2. 回答：`SendVisualSearchResult`で`POST /api/visual_search_log`へ送信。
3. 終了：最後の結果送信が完了してから`visual_practice_end` / `visual_low_end` / `visual_medium_end` / `visual_high_end`と同じブロックIDを送信。

Managerでの通信成否判定とエラー表示は省いています。送信処理の完了後は、成功・失敗にかかわらず課題を進めます。
通信エラーは既存RequestSenderのConsoleログで確認してください。結果送信はRequestSenderの15秒タイムアウト、状態通知は既定のタイムアウトなしを使います。
再送による二重記録を避けるため自動再送はありません。応答を受信できなくてもサーバに保存済みの場合があるため、再実施前にサーバログを確認してください。
Managerを無効化した場合も、RequestSenderが有効なら進行中の結果送信完了後に終了通知を試みます。
アプリ終了・RequestSenderのGameObject破棄・ネットワーク断時の到達保証はありません。

サーバは既存`Server/server2.py`を起動します。保存先は`Server/data/visual_search_log_<user_id>.jsonl`です。
フィールドは設計書13章に合わせて`sent_at` / `received_at`へ統一しました。
`sent_at`は既存RequestSenderと同じ`DateTime.Now.ToString()`、`received_at`はサーバ生成の日本時間ISO 8601です。
クライアントが`received_at`を送ると422で拒否します。状態イベントは既存`status_events.jsonl`へ別に保存されます。

## 7. 確認

- サーバ自動テスト：リポジトリ直下から`venv\Scripts\python.exe -m unittest Server.test_visual_search -v`。
- Unityでは3つのPrefab、探索領域、Action参照を設定してPlayし、各刺激数と赤い球の有無、正誤・反応時間、練習表示を確認してください。
- 短いブロック時間を設定しても提示中の刺激が消えず、回答後に結果→`visual_{試行レベル}_end`の順で保存されることを確認してください。
- 高密度の領域や壁付近で配置エラーになること、押しっぱなし・連打・中断で余計な試行結果が保存されないことを確認してください。
