package com.example.biodata_from_watch.presentation

import android.Manifest
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.wear.compose.material.Button
import androidx.wear.compose.material.ButtonDefaults
import androidx.wear.compose.material.Text
import androidx.wear.tooling.preview.devices.WearDevices
import com.example.biodata_from_watch.sensor.BiodataUploadService
import com.example.biodata_from_watch.presentation.theme.Biodata_from_watchTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        // Wear OS 標準の起動画面を表示したあと、計測開始用の画面をComposeで描画する。
        installSplashScreen()
        super.onCreate(savedInstanceState)
        setTheme(android.R.style.Theme_DeviceDefault)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        setContent {
            BiodataApp()
        }
    }
}

@Composable
fun BiodataApp() {
    val context = LocalContext.current
    val accentColor = Color(0xFF00D4AA)
    val statusColor = Color(0xFFB2DFDB)
    // サーバURLは実験環境ごとに変わるため、画面で編集した値を端末内に保存して再利用する。
    val prefs = remember {
        context.getSharedPreferences("biodata_settings", Context.MODE_PRIVATE)
    }
    var endpoint by remember {
        mutableStateOf(
            prefs.getString("endpoint", BiodataUploadService.DEFAULT_ENDPOINT)
                ?: BiodataUploadService.DEFAULT_ENDPOINT
        )
    }
    var userId by remember {
        mutableStateOf(
            prefs.getString("user_id", BiodataUploadService.DEFAULT_USER_ID)
                ?: BiodataUploadService.DEFAULT_USER_ID
        )
    }
    var heartRateWindow by remember {
        mutableStateOf(
            prefs.getFloat(
                "heart_rate_window_seconds",
                BiodataUploadService.DEFAULT_HEART_RATE_WINDOW_SECONDS,
            ).toString()
        )
    }
    var running by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf("Ready") }
    var sentHr by remember { mutableStateOf<Int?>(null) }
    var permissionDenied by remember { mutableStateOf(false) }
    DisposableEffect(context) {
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(receiverContext: Context?, intent: Intent?) {
                if (intent?.action != BiodataUploadService.ACTION_UPLOAD_STATUS) return
                sentHr = intent.getIntExtra(BiodataUploadService.EXTRA_SENT_HR, 0)
                intent.getStringExtra(BiodataUploadService.EXTRA_UPLOAD_MESSAGE)?.let { message ->
                    status = message
                }
            }
        }
        val filter = IntentFilter(BiodataUploadService.ACTION_UPLOAD_STATUS)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            context.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            context.registerReceiver(receiver, filter)
        }
        onDispose {
            context.unregisterReceiver(receiver)
        }
    }
    // BODY_SENSORS 権限がない場合は、Startボタン押下時にユーザーへ許可を求める。
    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { grants ->
        if (requiredSensorPermissions().all { permission -> grants[permission] == true }) {
            permissionDenied = false
            startUpload(context, endpoint, userId, heartRateWindow)
            running = true
            status = "Sending"
        } else {
            permissionDenied = true
            status = "Allow sensors"
        }
    }

    Biodata_from_watchTheme {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black)
                .padding(horizontal = 14.dp, vertical = 12.dp),
        ) {
            Column(
                modifier = Modifier.align(Alignment.Center),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(5.dp),
            ) {
                Text(
                    text = "Biodata",
                    color = Color.White,
                    fontSize = 14.sp,
                    textAlign = TextAlign.Center,
                )
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Button(
                        modifier = Modifier.size(56.dp),
                        colors = ButtonDefaults.primaryButtonColors(
                            backgroundColor = accentColor,
                            contentColor = Color.Black,
                        ),
                        onClick = {
                            // Start時点の送信先を保存し、foreground serviceの開始/停止を切り替える。
                            prefs.edit()
                                .putString("endpoint", endpoint)
                                .putString("user_id", userId)
                                .putFloat(
                                    "heart_rate_window_seconds",
                                    parseHeartRateWindow(heartRateWindow),
                                )
                                .apply()
                            if (running) {
                                context.startService(BiodataUploadService.stopIntent(context))
                                running = false
                                status = "Stopped"
                                sentHr = null
                            } else if (hasBodySensorPermission(context)) {
                                permissionDenied = false
                                startUpload(context, endpoint, userId, heartRateWindow)
                                running = true
                                status = "Sending"
                            } else {
                                permissionLauncher.launch(requiredSensorPermissions())
                            }
                        },
                    ) {
                        Text(if (running) "Stop" else "Start", fontSize = 11.sp)
                    }
                    Column(
                        modifier = Modifier.width(62.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.Center,
                    ) {
                        Text(
                            text = "HR",
                            color = statusColor,
                            fontSize = 10.sp,
                            textAlign = TextAlign.Center,
                        )
                        Text(
                            text = sentHr?.toString() ?: "--",
                            color = Color.White,
                            fontSize = 18.sp,
                            textAlign = TextAlign.Center,
                        )
                        Text(
                            text = "bpm",
                            color = statusColor,
                            fontSize = 9.sp,
                            textAlign = TextAlign.Center,
                        )
                    }
                }
                Text(
                    text = status,
                    color = statusColor,
                    fontSize = 10.sp,
                    textAlign = TextAlign.Center,
                )
                if (permissionDenied) {
                    Button(
                        modifier = Modifier.size(82.dp, 34.dp),
                        colors = ButtonDefaults.primaryButtonColors(
                            backgroundColor = Color.White,
                            contentColor = Color.Black,
                        ),
                        onClick = { openAppSettings(context) },
                    ) {
                        Text("Settings", fontSize = 10.sp)
                    }
                }
                BasicTextField(
                    value = userId,
                    onValueChange = { userId = it.trim().take(16) },
                    textStyle = TextStyle(color = Color.White, fontSize = 10.sp, textAlign = TextAlign.Center),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(24.dp)
                        .border(1.dp, Color.White)
                        .padding(4.dp),
                    singleLine = true,
                )
                BasicTextField(
                    value = endpoint,
                    onValueChange = { endpoint = it.trim() },
                    textStyle = TextStyle(color = Color.White, fontSize = 8.sp, textAlign = TextAlign.Center),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(26.dp)
                        .border(1.dp, accentColor)
                        .padding(5.dp),
                    singleLine = true,
                )
                Text(
                    text = "HR window (sec)",
                    color = statusColor,
                    fontSize = 8.sp,
                    textAlign = TextAlign.Center,
                )
                BasicTextField(
                    value = heartRateWindow,
                    onValueChange = { value ->
                        heartRateWindow = value.filter { it.isDigit() || it == '.' }.take(6)
                    },
                    textStyle = TextStyle(color = Color.White, fontSize = 9.sp, textAlign = TextAlign.Center),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(24.dp)
                        .border(1.dp, Color.White)
                        .padding(4.dp),
                    singleLine = true,
                    decorationBox = { innerTextField ->
                        if (heartRateWindow.isEmpty()) {
                            Text("HR window (sec)", color = statusColor, fontSize = 8.sp)
                        }
                        innerTextField()
                    },
                )
            }
        }
    }
}

private fun hasBodySensorPermission(context: Context): Boolean {
    // targetSdk 36以降はBODY_SENSORSではなく、health系の細かい権限を確認する。
    return requiredSensorPermissions().all { permission ->
        ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED
    }
}

private fun requiredSensorPermissions(): Array<String> {
    return if (Build.VERSION.SDK_INT >= 36) {
        arrayOf(
            READ_HEART_RATE_PERMISSION,
            READ_HEALTH_DATA_IN_BACKGROUND_PERMISSION,
            READ_ADDITIONAL_HEALTH_DATA_PERMISSION,
        )
    } else {
        arrayOf(Manifest.permission.BODY_SENSORS)
    }
}

private fun openAppSettings(context: Context) {
    // 一度拒否された場合はダイアログが再表示されないことがあるため、アプリ設定から許可してもらう。
    val intent = Intent(
        Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
        Uri.fromParts("package", context.packageName, null),
    ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    context.startActivity(intent)
}

private const val READ_HEART_RATE_PERMISSION = "android.permission.health.READ_HEART_RATE"
private const val READ_HEALTH_DATA_IN_BACKGROUND_PERMISSION =
    "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND"
private const val READ_ADDITIONAL_HEALTH_DATA_PERMISSION =
    "com.samsung.android.hardware.sensormanager.permission.READ_ADDITIONAL_HEALTH_DATA"

private fun parseHeartRateWindow(value: String): Float =
    value.toFloatOrNull()?.coerceAtLeast(0.01f)
        ?: BiodataUploadService.DEFAULT_HEART_RATE_WINDOW_SECONDS

private fun startUpload(context: Context, endpoint: String, userId: String, heartRateWindow: String) {
    // バックグラウンドでも送信を継続できるよう foreground service として起動する。
    val intent = BiodataUploadService.startIntent(
        context,
        endpoint,
        userId,
        parseHeartRateWindow(heartRateWindow),
    )
    ContextCompat.startForegroundService(context, intent)
}

@Preview(device = WearDevices.SMALL_ROUND, showSystemUi = true)
@Composable
fun DefaultPreview() {
    BiodataApp()
}
