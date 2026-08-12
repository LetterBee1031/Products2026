package com.example.biodata_from_watch.sensor

import java.io.BufferedReader
import java.io.DataOutputStream
import java.io.InputStream
import java.io.InputStreamReader
import java.net.HttpURLConnection
import java.net.URL
import java.util.Locale

class BiodataPoster(private val endpointUrl: String) {
    fun post(samples: List<BiodataSample>): Result<Unit> = runCatching {
        val jsonBody = samples.toJson().toByteArray(Charsets.UTF_8)

        // server2.py の /api/Biodata が受け取るJSON配列をHTTP POSTで送信する。
        val connection = (URL(normalizeEndpointUrl(endpointUrl)).openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 5_000
            readTimeout = 5_000
            doOutput = true
            doInput = true
            useCaches = false
            setFixedLengthStreamingMode(jsonBody.size)
            setRequestProperty("Content-Type", "application/json; charset=utf-8")
            setRequestProperty("Accept", "application/json")
        }

        DataOutputStream(connection.outputStream).use { output ->
            output.write(jsonBody)
            output.flush()
        }

        val status = connection.responseCode
        val responseText = connection.responseStreamOrError()?.readTextUtf8().orEmpty()
        connection.disconnect()
        // 2xx以外は送信失敗として呼び出し元に通知し、時計側の通知表示に反映する。
        if (status !in 200..299) {
            error("POST /api/Biodata failed: HTTP $status $responseText")
        }
    }

    private fun List<BiodataSample>.toJson(): String {
        // Android標準だけで動くよう、外部JSONライブラリを使わず必要な形だけ生成する。
        return joinToString(prefix = "[", postfix = "]") { sample ->
            val edaJson = sample.eda
                ?.takeIf { java.lang.Double.isFinite(it) }
                ?.let { String.format(Locale.US, "%.6f", it) }
                ?: "null"
            """
            {
              "user_id": "${sample.userId.escapeJson()}",
              "hr": ${sample.hr},
              "ibi": ${sample.ibi.joinToString(prefix = "[", postfix = "]")},
              "eda": $edaJson,
              "sentAt": "${sample.sentAt.escapeJson()}",
              "timestamp": ${sample.timestamp},
              "deviceIp": "${sample.deviceIp.escapeJson()}"
            }
            """.trimIndent()
        }
    }

    private fun normalizeEndpointUrl(rawUrl: String): String {
        val trimmed = rawUrl.trim().trimEnd('/')
        return if (trimmed.endsWith("/api/Biodata")) {
            trimmed
        } else {
            "$trimmed/api/Biodata"
        }
    }

    private fun HttpURLConnection.responseStreamOrError(): InputStream? {
        return if (responseCode in 200..299) inputStream else errorStream
    }

    private fun InputStream.readTextUtf8(): String {
        return BufferedReader(InputStreamReader(this, Charsets.UTF_8)).use { reader ->
            reader.readText()
        }
    }

    private fun String.escapeJson(): String = buildString {
        // URLや時刻文字列に特殊文字が含まれてもJSONが壊れないようにエスケープする。
        for (char in this@escapeJson) {
            when (char) {
                '\\' -> append("\\\\")
                '"' -> append("\\\"")
                '\n' -> append("\\n")
                '\r' -> append("\\r")
                '\t' -> append("\\t")
                else -> append(char)
            }
        }
    }
}
