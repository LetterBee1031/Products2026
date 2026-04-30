package com.example.biodata_from_watch.sensor

import android.content.Context
import com.samsung.android.service.health.tracking.ConnectionListener
import com.samsung.android.service.health.tracking.HealthTracker
import com.samsung.android.service.health.tracking.HealthTrackerException
import com.samsung.android.service.health.tracking.HealthTrackingService
import com.samsung.android.service.health.tracking.data.DataPoint
import com.samsung.android.service.health.tracking.data.HealthTrackerType
import com.samsung.android.service.health.tracking.data.ValueKey

class SamsungEdaReader(
    context: Context,
    private val onEdaChanged: (Double?) -> Unit,
    private val onStatusChanged: (String) -> Unit,
) {
    private var edaTracker: HealthTracker? = null
    private lateinit var healthTrackingService: HealthTrackingService

    private val trackerListener = object : HealthTracker.TrackerEventListener {
        override fun onDataReceived(dataPoints: List<DataPoint>) {
            // EDA_CONTINUOUSの最新データ点から皮膚コンダクタンスを取り出し、送信用snapshotへ反映する。
            val latestConductance = dataPoints
                .asSequence()
                .mapNotNull { dataPoint ->
                    runCatching {
                        dataPoint.getValue(ValueKey.EdaSet.SKIN_CONDUCTANCE)?.toDouble()
                    }.getOrNull()
                }
                .lastOrNull()

            onEdaChanged(latestConductance)
        }

        override fun onFlushCompleted() = Unit

        override fun onError(error: HealthTracker.TrackerError) {
            onStatusChanged("EDA error: $error")
        }
    }

    private val connectionListener = object : ConnectionListener {
        override fun onConnectionSuccess() {
            // 端末がEDA_CONTINUOUSをサポートしている場合だけtrackerを作成する。
            val supportedTypes = healthTrackingService
                .getTrackingCapability()
                .supportHealthTrackerTypes

            if (HealthTrackerType.EDA_CONTINUOUS !in supportedTypes) {
                onStatusChanged("EDA unsupported")
                return
            }

            edaTracker = healthTrackingService.getHealthTracker(HealthTrackerType.EDA_CONTINUOUS)
            edaTracker?.setEventListener(trackerListener)
            onStatusChanged("EDA connected")
        }

        override fun onConnectionEnded() {
            onStatusChanged("EDA disconnected")
        }

        override fun onConnectionFailed(exception: HealthTrackerException) {
            onStatusChanged("EDA connection failed: ${exception.errorCode}")
        }
    }

    init {
        healthTrackingService = HealthTrackingService(connectionListener, context.applicationContext)
    }

    fun start() {
        // Samsung Health Sensor Serviceへ接続し、接続成功後にEDA trackerを購読する。
        healthTrackingService.connectService()
    }

    fun stop() {
        // Service終了時にtracker購読とSDK接続を切り、センサー利用を止める。
        edaTracker?.unsetEventListener()
        edaTracker = null
        healthTrackingService.disconnectService()
    }
}
