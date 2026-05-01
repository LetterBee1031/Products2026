package com.example.biodata_from_watch.sensor

// サーバへ約2秒ごとに送る生体データ。IBIは仕様に合わせて0〜4個のリストにする。
data class BiodataSample(
    val userId: String,
    val hr: Int,
    val ibi: List<Int>,
    val eda: Double?,
    val sentAt: String,
    val timestamp: Long,
    val deviceIp: String,
)

// センサーイベントから得た最新値。送信ループがこの値を読み取り、送信後にIBIだけクリアする。
data class SensorSnapshot(
    val hr: Int = 0,
    val ibi: List<Int> = emptyList(),
    val eda: Double? = null,
)
