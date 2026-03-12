/*
 * Copyright 2023 Samsung Electronics Co., Ltd. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

package com.samsung.health.hrdatatransfer.presentation

import android.util.Log
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.samsung.android.service.health.tracking.HealthTrackerException
import com.samsung.health.data.TrackedData
import com.samsung.health.hrdatatransfer.data.ConnectionMessage
import com.samsung.health.hrdatatransfer.data.TrackerMessage
import com.samsung.health.hrdatatransfer.domain.AreTrackingCapabilitiesAvailableUseCase
import com.samsung.health.hrdatatransfer.domain.MakeConnectionToHealthTrackingServiceUseCase
import com.samsung.health.hrdatatransfer.domain.SendMessageUseCase
import com.samsung.health.hrdatatransfer.domain.StopTrackingUseCase
import com.samsung.health.hrdatatransfer.domain.TrackHeartRateUseCase
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

private const val TAG = "MainViewModel"

@HiltViewModel
class MainViewModel @Inject constructor(
    private val makeConnectionToHealthTrackingServiceUseCase: MakeConnectionToHealthTrackingServiceUseCase,
    private val sendMessageUseCase: SendMessageUseCase,
    private val stopTrackingUseCase: StopTrackingUseCase,
    private val areTrackingCapabilitiesAvailableUseCase: AreTrackingCapabilitiesAvailableUseCase
) : ViewModel() {

    private val _messageSentToast = MutableSharedFlow<Boolean>()
    val messageSentToast = _messageSentToast.asSharedFlow()

    private val _trackingState =
        MutableStateFlow(
            TrackingState(
                trackingRunning = false,
                trackingError = false,
                valueHR = "-",
                valueIBI = arrayListOf(),
                message = ""
            )
        )
    val trackingState: StateFlow<TrackingState> = _trackingState

    private val _connectionState =
        MutableStateFlow(ConnectionState(connected = false, message = "", null))
    val connectionState: StateFlow<ConnectionState> = _connectionState

    @Inject
    lateinit var trackHeartRateUseCase: TrackHeartRateUseCase

    private var currentHR = "-"
    private var currentIBI = ArrayList<Int>(4)

    fun stopTracking() {
        stopTrackingUseCase()
        trackingJob?.cancel()
        _trackingState.value = TrackingState(
            trackingRunning = false,
            trackingError = false,
            valueHR = "-",
            valueIBI = arrayListOf(),
            message = ""
        )
    }

    fun setUpTracking() {
        Log.i(TAG, "setUpTracking()")
        viewModelScope.launch {
            makeConnectionToHealthTrackingServiceUseCase().collect { connectionMessage ->
                Log.i(TAG, "makeConnectionToHealthTrackingServiceUseCase().collect")
                when (connectionMessage) {
                    is ConnectionMessage.ConnectionSuccessMessage -> {
                        Log.i(TAG, "ConnectionMessage.ConnectionSuccessMessage")
                        _connectionState.value = ConnectionState(
                            connected = true,
                            message = "Connected to Health Tracking Service",
                            connectionException = null
                        )
                    }

                    is ConnectionMessage.ConnectionFailedMessage -> {
                        Log.i(TAG, "Connection: Sth went wrong")
                        _connectionState.value = ConnectionState(
                            connected = false,
                            message = "Connection to Health Tracking Service failed",
                            connectionException = connectionMessage.exception
                        )
                    }

                    is ConnectionMessage.ConnectionEndedMessage -> {
                        Log.i(TAG, "Connection ended")
                        _connectionState.value = ConnectionState(
                            connected = false,
                            message = "Connection ended. Try again later",
                            connectionException = null
                        )
                    }
                }
            }
        }
    }

    fun sendMessage() {
        viewModelScope.launch {
            if (sendMessageUseCase()) {
                _messageSentToast.emit(true)
            } else {
                _messageSentToast.emit(false)
            }
        }
    }

    private fun processExerciseUpdate(trackedData: TrackedData) {

        val hr = trackedData.hr
        val ibi = trackedData.ibi
        Log.i(TAG, "last HeartRate: $hr, last IBI: $ibi")
        currentHR = hr.toString()
        currentIBI = ibi

        _trackingState.value = TrackingState(
            trackingRunning = true,
            trackingError = false,
            valueHR = if (hr > 0) hr.toString() else "-",
            valueIBI = ibi,
            message = ""
        )
    }

    private var trackingJob: Job? = null

    fun startTracking() {
        trackingJob?.cancel()
        Log.i(TAG, "trackHeartRate()")
        if (areTrackingCapabilitiesAvailableUseCase()) {
            trackingJob = viewModelScope.launch {
                trackHeartRateUseCase().collect { trackerMessage ->
                    when (trackerMessage) {
                        is TrackerMessage.DataMessage -> {
                            processExerciseUpdate(trackerMessage.trackedData)
                            Log.i(TAG, "TrackerMessage.DataReceivedMessage")
                        }

                        is TrackerMessage.FlushCompletedMessage -> {
                            Log.i(TAG, "TrackerMessage.FlushCompletedMessage")
                            _trackingState.value = TrackingState(
                                trackingRunning = false,
                                trackingError = false,
                                valueHR = "-",
                                valueIBI = arrayListOf(),
                                message = ""
                            )
                        }

                        is TrackerMessage.TrackerErrorMessage -> {
                            Log.i(TAG, "TrackerMessage.TrackerErrorMessage")
                            _trackingState.value = TrackingState(
                                trackingRunning = false,
                                trackingError = true,
                                valueHR = "-",
                                valueIBI = arrayListOf(),
                                message = trackerMessage.trackerError
                            )
                        }

                        is TrackerMessage.TrackerWarningMessage -> {
                            Log.i(TAG, "TrackerMessage.TrackerWarningMessage")
                            _trackingState.value = TrackingState(
                                trackingRunning = true,
                                trackingError = false,
                                valueHR = "-",
                                valueIBI = currentIBI,
                                message = trackerMessage.trackerWarning
                            )
                        }
                    }
                }
            }
        } else {
            _trackingState.value = TrackingState(
                trackingRunning = false,
                trackingError = true,
                valueHR = "-",
                valueIBI = arrayListOf(),
                message = "HR tracking capabilities not available"
            )
        }
    }
}

data class ConnectionState(
    val connected: Boolean,
    val message: String,
    val connectionException: HealthTrackerException?
)

data class TrackingState(
    val trackingRunning: Boolean,
    val trackingError: Boolean,
    val valueHR: String,
    val valueIBI: ArrayList<Int>,
    val message: String
)
