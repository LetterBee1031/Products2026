package com.example.biodata_from_watch.sensor

// サーバへ1秒ごとに送る生体データ。server2.py の BiodataPost と同じ項目名に合わせる。
data class BiodataSample(
    val hr: Int,
    val ibi: List<Int>,
    val eda: Double?,
    val sentAt: String,
    val timestamp: Long,
    val deviceIp: String,
)

// センサーイベントから得た最新値。送信ループがこの値を読み取り、1秒窓の代表値として扱う。
data class SensorSnapshot(
    val hr: Int = 0,
    val ibi: List<Int> = emptyList(),
    val eda: Double? = null,
)
