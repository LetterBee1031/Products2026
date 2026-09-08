# biodata_from_watchについて

## 概要
- galaxy watch 8から，生体データをサーバに送信
- 送信する生体データは心拍数（HR）, 心拍間隔（IBI），皮膚電位（EDA）
- 心拍数は直近2秒のスライディングウィンドウで平均化
- 平滑化した心拍数を1秒ごとに送信
- Samsung Health Sensor SDKを使用

## 送信データについて
- 生体データ：心拍数（HR）, 心拍間隔（IBI），皮膚電位（EDA）
- 送信時刻
- タイムスタンプ
- 自端末のIPアドレス

## 送信先について
- server2.pyのapp.post("/api/Biodata")に送信する
- server2.pyの修正も実施
  - app.post("/api/hr")をベースとしてapp.post("/api/Biodata")を作成
  - 皮膚電位（EDA）の送信も受け付けるようにして
