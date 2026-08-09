package com.example.biodata_from_watch.sensor

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.IBinder
import android.os.PowerManager
import android.os.SystemClock
import androidx.core.app.NotificationCompat
import com.example.biodata_from_watch.R
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.ZoneId
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.util.ArrayDeque
import kotlin.math.roundToInt

class BiodataUploadService : Service(), SensorEventListener {
    // センサー取得とHTTP送信はUIを止めないようIO用のCoroutineScopeで動かす。
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var uploadJob: Job? = null
    private lateinit var sensorManager: SensorManager
    private var wakeLock: PowerManager.WakeLock? = null
    private var edaReader: SamsungEdaReader? = null
    private val snapshotLock = Any()
    // 最新のセンサー値を保持し、送信タイミングでスナップショットとして読む。
    private var snapshot = SensorSnapshot()
    private var lastBeatElapsed: Long? = null
    private var endpointUrl: String = DEFAULT_ENDPOINT
    private var userId: String = DEFAULT_USER_ID
    private var heartRateWindowMs: Long = DEFAULT_HEART_RATE_WINDOW_MS
    private val heartRateSamples = ArrayDeque<HeartRateSample>()

    override fun onCreate() {
        super.onCreate()
        sensorManager = getSystemService(SENSOR_SERVICE) as SensorManager
        wakeLock = (getSystemService(POWER_SERVICE) as PowerManager).newWakeLock(
            PowerManager.PARTIAL_WAKE_LOCK,
            "$packageName:BiodataUpload",
        )
        edaReader = SamsungEdaReader(
            context = this,
            onEdaChanged = { eda ->
                updateSnapshot { current -> current.copy(eda = eda) }
            },
            onStatusChanged = { message ->
                (getSystemService(NOTIFICATION_SERVICE) as NotificationManager)
                    .notify(NOTIFICATION_ID, notification(message))
            },
        )
        // Androidの制約に合わせ、常駐計測は通知付きforeground serviceとして開始する。
        createNotificationChannel()
        startForeground(NOTIFICATION_ID, notification("Starting"))
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // UIのStopボタンから明示的に停止できるよう、停止専用アクションを受け取る。
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }

        endpointUrl = intent?.getStringExtra(EXTRA_ENDPOINT_URL) ?: DEFAULT_ENDPOINT
        userId = intent?.getStringExtra(EXTRA_USER_ID)?.ifBlank { DEFAULT_USER_ID } ?: DEFAULT_USER_ID
        heartRateWindowMs = intent?.getLongExtra(
            EXTRA_HEART_RATE_WINDOW_MS,
            DEFAULT_HEART_RATE_WINDOW_MS,
        )?.coerceAtLeast(MIN_HEART_RATE_WINDOW_MS) ?: DEFAULT_HEART_RATE_WINDOW_MS
        acquireWakeLock()
        registerSensors()
        edaReader?.start()
        startUploading()
        return START_STICKY
    }

    override fun onDestroy() {
        // Service終了時はセンサー監視と送信ループを止め、端末側のリソースを解放する。
        sensorManager.unregisterListener(this)
        edaReader?.stop()
        releaseWakeLock()
        uploadJob?.cancel()
        scope.cancel()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onSensorChanged(event: SensorEvent) {
        when (event.sensor.type) {
            Sensor.TYPE_HEART_RATE -> {
                // 心拍数は小数で来ることがあるため、サーバ形式に合わせて整数BPMに丸める。
                val hr = event.values.firstOrNull()?.roundToInt()?.coerceAtLeast(0) ?: 0
                if (hr > 0) {
                    val now = SystemClock.elapsedRealtime()
                    synchronized(snapshotLock) {
                        heartRateSamples.addLast(HeartRateSample(now, hr))
                        removeExpiredHeartRates(now)
                        snapshot = snapshot.copy(hr = hr)
                    }
                }
            }
            Sensor.TYPE_HEART_BEAT -> {
                // HEART_BEATイベント間の経過時間から簡易的にIBI(ms)を計算する。
                val now = SystemClock.elapsedRealtime()
                val previous = lastBeatElapsed
                lastBeatElapsed = now
                if (previous != null) {
                    val ibi = (now - previous).toInt()
                    updateSnapshot { current ->
                        current.copy(ibi = (current.ibi + ibi).takeLast(MAX_IBI_PER_SAMPLE))
                    }
                }
            }
        }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

    private fun registerSensors() {
        // Wear OS標準センサーから取得できるHR/心拍イベントを購読する。
        sensorManager.getDefaultSensor(Sensor.TYPE_HEART_RATE)?.also { sensor ->
            sensorManager.registerListener(this, sensor, SensorManager.SENSOR_DELAY_NORMAL)
        }
        sensorManager.getDefaultSensor(Sensor.TYPE_HEART_BEAT)?.also { sensor ->
            sensorManager.registerListener(this, sensor, SensorManager.SENSOR_DELAY_NORMAL)
        }
        // EDAはSamsung Health Sensor SDKのSamsungEdaReaderで別途購読する。
    }

    private fun startUploading() {
        // 二重起動で同じデータを重複送信しないよう、既存ループが動作中なら何もしない。
        if (uploadJob?.isActive == true) return

        val poster = BiodataPoster(endpointUrl)
        uploadJob = scope.launch {
            var nextUploadElapsed = SystemClock.elapsedRealtime()
            while (true) {
                val waitMillis = nextUploadElapsed - SystemClock.elapsedRealtime()
                if (waitMillis > 0) {
                    delay(waitMillis)
                }

                runCatching {
                    // 1秒程度の窓で、IBIは0〜4個までまとめて送信する。
                    val current = consumeSnapshot()
                    val sample = BiodataSample(
                        userId = userId,
                        hr = current.hr,
                        ibi = current.ibi.take(MAX_IBI_PER_SAMPLE),
                        eda = current.eda,
                        sentAt = ZonedDateTime.now(JST_ZONE)
                            .format(DateTimeFormatter.ISO_OFFSET_DATE_TIME),
                        timestamp = System.currentTimeMillis(),
                        deviceIp = DeviceNetwork.localIpAddress(),
                    )
                    val result = poster.post(listOf(sample))
                    // 送信結果を通知に出して、時計単体でも状態を確認できるようにする。
                    val message = result.fold(
                        onSuccess = { "Sent HR ${sample.hr}" },
                        onFailure = { "Upload failed: ${it.message}" },
                    )
                    sendBroadcast(
                        Intent(ACTION_UPLOAD_STATUS)
                            .setPackage(packageName)
                            .putExtra(EXTRA_SENT_HR, sample.hr)
                            .putExtra(EXTRA_UPLOAD_MESSAGE, message),
                    )
                    (getSystemService(NOTIFICATION_SERVICE) as NotificationManager)
                        .notify(NOTIFICATION_ID, notification(message))
                }.onFailure { error ->
                    (getSystemService(NOTIFICATION_SERVICE) as NotificationManager)
                        .notify(NOTIFICATION_ID, notification("Upload loop error: ${error.message}"))
                }
                nextUploadElapsed += UPLOAD_INTERVAL_MS
                val now = SystemClock.elapsedRealtime()
                while (nextUploadElapsed <= now) {
                    nextUploadElapsed += UPLOAD_INTERVAL_MS
                }
            }
        }
    }

    private fun acquireWakeLock() {
        // 画面消灯後も2秒ごとの送信ループが止まらないよう、計測中だけCPUを維持する。
        val lock = wakeLock ?: return
        if (!lock.isHeld) {
            lock.acquire()
        }
    }

    private fun releaseWakeLock() {
        val lock = wakeLock ?: return
        if (lock.isHeld) {
            lock.release()
        }
    }

    private fun updateSnapshot(update: (SensorSnapshot) -> SensorSnapshot) {
        synchronized(snapshotLock) {
            snapshot = update(snapshot)
        }
    }

    private fun consumeSnapshot(): SensorSnapshot {
        return synchronized(snapshotLock) {
            val now = SystemClock.elapsedRealtime()
            removeExpiredHeartRates(now)
            val smoothedHr = if (heartRateSamples.isEmpty()) {
                snapshot.hr
            } else {
                (heartRateSamples.sumOf { it.value }.toDouble() / heartRateSamples.size).roundToInt()
            }
            val current = snapshot.copy(hr = smoothedHr)
            snapshot = snapshot.copy(ibi = emptyList())
            current
        }
    }

    private fun removeExpiredHeartRates(now: Long) {
        while (heartRateSamples.isNotEmpty() &&
            now - heartRateSamples.first().elapsedRealtimeMs > heartRateWindowMs
        ) {
            heartRateSamples.removeFirst()
        }
    }

    private data class HeartRateSample(val elapsedRealtimeMs: Long, val value: Int)

    private fun notification(text: String): Notification {
        // foreground service用の常駐通知。計測が動いていることをユーザーに示す。
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.splash_icon)
            .setContentTitle(getString(R.string.app_name))
            .setContentText(text)
            .setOngoing(true)
            .build()
    }

    private fun createNotificationChannel() {
        // Android 8以降では通知チャンネルが必須なので、低優先度の計測用チャンネルを作る。
        val channel = NotificationChannel(
            CHANNEL_ID,
            "Biodata upload",
            NotificationManager.IMPORTANCE_LOW,
        )
        (getSystemService(NOTIFICATION_SERVICE) as NotificationManager)
            .createNotificationChannel(channel)
    }

    companion object {
        // ActivityとServiceの間で使うIntentキーと、初期状態の送信先URL。
        const val ACTION_STOP = "com.example.biodata_from_watch.sensor.STOP"
        const val ACTION_UPLOAD_STATUS = "com.example.biodata_from_watch.sensor.UPLOAD_STATUS"
        const val EXTRA_ENDPOINT_URL = "endpoint_url"
        const val EXTRA_USER_ID = "user_id"
        const val EXTRA_HEART_RATE_WINDOW_MS = "heart_rate_window_ms"
        const val EXTRA_SENT_HR = "sent_hr"
        const val EXTRA_UPLOAD_MESSAGE = "upload_message"
        const val DEFAULT_ENDPOINT = "http://192.168.150.127:8080/api/Biodata"
        const val DEFAULT_USER_ID = "01"
        const val DEFAULT_HEART_RATE_WINDOW_SECONDS = 2f
        private const val CHANNEL_ID = "biodata_upload"
        private const val NOTIFICATION_ID = 1001
        private const val MAX_IBI_PER_SAMPLE = 4
        private const val UPLOAD_INTERVAL_MS = 1_000L
        private const val MIN_HEART_RATE_WINDOW_MS = 10L
        private const val DEFAULT_HEART_RATE_WINDOW_MS = 1_000L
        private val JST_ZONE: ZoneId = ZoneId.of("Asia/Tokyo")

        fun startIntent(
            context: Context,
            endpointUrl: String,
            userId: String,
            heartRateWindowSeconds: Float,
        ): Intent {
            return Intent(context, BiodataUploadService::class.java)
                .putExtra(EXTRA_ENDPOINT_URL, endpointUrl)
                .putExtra(EXTRA_USER_ID, userId)
                .putExtra(
                    EXTRA_HEART_RATE_WINDOW_MS,
                    (heartRateWindowSeconds * 1_000f).toLong().coerceAtLeast(MIN_HEART_RATE_WINDOW_MS),
                )
        }

        fun stopIntent(context: Context): Intent {
            return Intent(context, BiodataUploadService::class.java).setAction(ACTION_STOP)
        }
    }
}
