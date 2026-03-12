package com.samsung.health.hrdatatransfer.PcSend

import android.util.Log
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException
import java.util.concurrent.TimeUnit

private const val TAG = "PcSender"

class PcSender(
    private val baseUrl: String, // 例: "http://192.168.1.50:8080"
) {
    private val normalizedBaseUrl = baseUrl.trim().trim('"').trimEnd('/')
    private val client = OkHttpClient.Builder()
        .connectTimeout(3, TimeUnit.SECONDS)
        .readTimeout(5, TimeUnit.SECONDS)
        .writeTimeout(5, TimeUnit.SECONDS)
        .build()

    private val json = Json {
        encodeDefaults = true
        ignoreUnknownKeys = true
    }

    private val mediaType = "application/json; charset=utf-8".toMediaType()

    fun sendHeartData(payload: HeartPayload): Boolean {
        val bodyString = json.encodeToString(payload)
        return sendRawJson(bodyString)
    }

    fun sendRawJson(jsonBody: String): Boolean {
        val url = "$normalizedBaseUrl/api/hr"
        val requestBody = jsonBody.toRequestBody(mediaType)

        val request = Request.Builder()
            .url(url)
            .post(requestBody)
            .build()

        return try {
            client.newCall(request).execute().use { resp ->
                val body = resp.body?.string()
                Log.i(TAG, "POST $url -> code=${resp.code}, body=$body")
                resp.isSuccessful
            }
        } catch (e: IOException) {
            Log.w(TAG, "Network error: ${e.message}", e)
            false
        } catch (e: Exception) {
            Log.w(TAG, "Send failed: ${e::class.qualifiedName}", e)
            false
        }
    }
}