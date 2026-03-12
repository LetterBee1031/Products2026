package com.samsung.health.hrdatatransfer.PcSend

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.LocalDateTime

class HeartUploader(
    private val sender: PcSender,
    private val deviceId: String
) {
    suspend fun uploadOnce(heartRate: Int, ibiMsList: List<Int>): Boolean {
        val payload = HeartPayload(
            // deviceId = deviceId,
            // timestampMs = System.currentTimeMillis(),
            sentAt = LocalDateTime.now().toString(),
            heartRate = heartRate,
            ibiMsList = ibiMsList
        )

        return withContext(Dispatchers.IO) {
            sender.sendHeartData(payload)
        }
    }
}