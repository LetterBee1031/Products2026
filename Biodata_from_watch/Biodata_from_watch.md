# biodata_from_watchについて

## 概要
- galaxy watch 8から，生体データをサーバに送信
- 送信する生体データは心拍数（HR）, 心拍間隔（IBI），皮膚電位（EDA）
- データは1秒窓に平滑化して送信
- Samsung Health Sensor SDKを使用

## 送信データについて
- 生体データ：心拍数（HR）, 心拍間隔（IBI），皮膚電位（EDA）
- 送信時刻
- タイムスタンプ
- 自端末のIPアドレス

## 送信先について
- server2.pyのapp.post("/api/hr")に送信する
  - app.post("/api/hr")の形式に合わせて送信
- server2.pyの修正も実施
  - app.post("/api/hr")をベースとしてapp.post("/api/Biodata")を作成
  - 皮膚電位（EDA）の送信も受け付けるようにして