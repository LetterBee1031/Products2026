# サーバ(FastAPI)-体験環境(Unity)間，サーバ-GalaxyWatch間のデータ授受の設計
## 概要
- 体験者の認知負荷状態の基準設定時のデータ授受について
- Unity, FastAPI, Galaxy Watchの間でデータの送受信を行う
- それぞれの役割
  - Unity 
    - 言語：C#
    - 体験空間の管理
    - N-backテストの開始・終了のフラグをFastAPIに送信
  - FastAPI
    - 言語：Python
    - PC上でサーバとして運用
    - 心拍情報を受け取りjsonファイルに記述
    - Unityの体験状態に関する情報の保持
  - Galaxy Watch
    - 言語：kotlin
    - sumsung Health SDKを使用
    - 体験者の心拍情報をFastAPIサーバに一定間隔で常時送信

## 処理の流れ
0. Galaxy Watch: Galaxy Watchは常時心拍情報をFastAPIサーバに送信
1. Unity: Unity側でN-back課題が開始された際，N-back課題開始フラグをFastAPIサーバに送信（POSTリクエスト）
2. FastAPI: 体験状態（status）をn-back課題中（n-back_running）に変更
3. Unity: Unity側でN-back課題が終了した際，N-back課題開始フラグをFastAPIサーバに送信（POSTリクエスト）
4. FastAPI: 体験状態（status）をn-back課題終了（n-back_end）に変更

## それぞれの送信情報・関数等
### Unity
#### 送信情報
- 文字列型 status_flag: 
  - 体験の進行状況や状態のフラグ情報
  - フラグの例
    - 1_back_start
    - 1_back_end
    - 3_back_start
    - 3_back_end
    - experience_start
    - experience_end
- 整数型 send_time: フラグを送信した時刻

#### 関数
- postJsonArray: 
  - 送信情報をJsonデータに変換してstatusをサーバに送信
  - 引数：
    - 文字列型 status_flag: 体験の状態
    - 文字列型 path: 送信先のpath

- getExpStatus: 
  - FastAPIサーバ上の体験状態（status）を確認
  - 引数: 
    - 文字列型 path: 送信先のpath

### Galaxy Watch（WearOS + sumsung Health SDK）
#### 送信情報
- 整数型 hr: 心拍数
- 整数型リスト ibi: 体験者の心拍間隔
- 整数型 send_time: 送信時刻

### FastAPI
#### パスオペレーション post("/api/hr")
##### 関数
- recive_batch:
  - 心拍データの受け取り
  - 受け取ったデータのjsonファイルへの書き出し
  - 引数: 
    - List[TrackedData]型 payload: 受信データ
      - 整数型 hr: 心拍数
      - 整数型リスト ibi: 体験者の心拍間隔
      - 整数型 send_time: 送信時刻
    - Request型 request: 送信元情報

#### パスオペレーション post("/api/status_post")
##### 関数
- change_status:
  - status変更
  - 受け取ったペイロードを基に，idが一致するユーザの状態（ex_status）を変更する
  - 引数:
    - User型 payload
      - id: ユーザのid
      - status: 変更後の状態

#### パスオペレーション post("/api/status_get")
##### 関数
- read_status:
  - 現在の状態(status)を返す


# 心拍情報の解析について
- jsonデータから心拍データを取り出して解析する
- 心拍の平均値を算出
- 心拍データはファイル「hr_ibi.jsonl」から取得
- 使用する心拍データの指定
  - status_flagが1_back_startのデータのみ使用
  - status_events.jsonlにおける「status_flag」が「1_back_start」のpost要求が送られた時刻以降のデータのみを使用