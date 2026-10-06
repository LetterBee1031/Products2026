# 4.1 生体情報データ
- 参加者識別子は"userID"
- 瞳孔径の列名は"tepr"とし，すでに左右平均を取られているものとする
- 計測時刻は"sent_at"として時系列同期に利用
- 
# 4.2 NASA-TLXデータ
- 参加者識別子は"userID"
- 回答形式は0～20
- 簡単化のためraw-tlxまたはMental Demandのみを利用する
  - 重み付きNASA-TLXは使用しない

# 6. サンプル単位
- 時間窓方式を使用する
- 1ブロックを2秒窓に分割し，モデル学習・推論に利用する
- 同ブロック内の各時間窓には同じNASA-TLX得点を付与

# 7. 前処理要件
- 説明変数は平均値ではなく2秒窓のデータを使用する

# 8. 特徴量要件
- NASA-TLX得点も標準化する

# 9. モデル学習要件
- 個人別モデルのみ実装

# 10. データ分割要件
- 基本的に交差検証を使用する設計にしたい

- 目的変数について以下のような式で定義したい
  - $L_{label} = w_{obj} \cdot L_{obj}+w_{sub} \cdot L_{sub}$
  - $L_{obj}$
    - n-backのn数に関するパラメータ
    - 以下のような形で定義
      - 0-back: 0.25
      - 1-back: 0.50
      - 2-back: 0.75
      - 3-back: 1.00
  - $L_{sub}$
    - NASA-TLX回答結果の標準化済みデータ
  - $w_{obj}$，$w_{sub}$
    - $L_{obj}$，$L_{sub}$それぞれの重み

- 出力結果$L_{cur}$は以下の式の処理を加えて，中心0.5の分布に変換
  - $L_{cur} = 回帰モデルの出力結果/4 + 0.5$

- その後$L_{cur}$を0~1の範囲にクリッピング


# 視覚探索課題について
- 論文概要等の実装に関係のない情報は削除してよし
- 試行回数ではなく，時間制限にて終了する方式に変更
- 試行は同じ難易度条件ごとにまとめて実施するよう変更
- 計測データは以下のみにとする．各データの注釈も付与するよう変更
  - user_id
  - block_id
  - difficulty
  - trial_index
  - is_practice  
  - target_present
  - is_correct
  - reaction_time_ms
  - randomSeed
  - timestamp
- 保存形式について 
  - 計測データをRequestSender.csにてサーバに送信
  - サーバ側はServer2.pyにて送信データを受け取り，保存
- user_idはRequestSenderで設定されている値を参照

- .unityファイルに許可なく変更は加えない
- requestSender.cs側で送信時刻をデータに付与
- server2.py側で受信時刻をデータに付与
- イベントについて
  - イベントはrequestSender.csのPostStatusFlag関数を利用し，サーバに送信
  - イベントは以下のように定義．
    - Practice
    - Low
    - Medium
    - High
    - block_end
  - 各ブロック開始時にPractice，Low，Medium，Highの中から開始されたブロックに対応するイベントを送信
  - 各ブロック終了時にblock_endを送信
