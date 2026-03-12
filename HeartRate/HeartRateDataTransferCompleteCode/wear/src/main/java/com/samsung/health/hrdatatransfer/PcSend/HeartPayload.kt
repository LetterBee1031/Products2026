package com.samsung.health.hrdatatransfer.PcSend

import kotlinx.serialization.Serializable

@Serializable
data class HeartPayload(
    //val deviceId: String,
    //val timestampMs: Long,
    val sentAt: String,
    val heartRate: Int,
    val ibiMsList: List<Int>
)