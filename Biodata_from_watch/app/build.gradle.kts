plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "com.example.biodata_from_watch"
    // ローカルSDKに入っているBuild Toolsを明示して、オフラインでもビルドできるようにする。
    buildToolsVersion = "36.1.0"
    compileSdk {
        version = release(36)
    }

    defaultConfig {
        applicationId = "com.example.biodata_from_watch"
        minSdk = 33
        targetSdk = 36
        versionCode = 1
        versionName = "1.0"

    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    useLibrary("wear-sdk")
    buildFeatures {
        // 送信開始/停止画面をWear Composeで作る。
        compose = true
    }
}

dependencies {
    // センサー監視と1秒ごとのHTTP送信ループをCoroutineで扱う。
    implementation(libs.coroutines.android)
    implementation(libs.core.ktx)
    // Samsung Health Sensor SDK。EDA_CONTINUOUSから皮膚コンダクタンスを取得する。
    implementation(files("../samsung-health-sensor-sdk/1.4.1/libs/samsung-health-sensor-api-1.4.1.aar"))
    implementation(libs.play.services.wearable)
    implementation(platform(libs.compose.bom))
    implementation(libs.ui)
    implementation(libs.ui.graphics)
    implementation(libs.ui.tooling.preview)
    implementation(libs.compose.material)
    implementation(libs.compose.foundation)
    implementation(libs.wear.tooling.preview)
    implementation(libs.activity.compose)
    implementation(libs.core.splashscreen)
    implementation(libs.tiles)
    implementation(libs.tiles.material)
    implementation(libs.tiles.tooling.preview)
    implementation(libs.horologist.compose.tools)
    implementation(libs.horologist.tiles)
    implementation(libs.watchface.complications.data.source.ktx)
    androidTestImplementation(platform(libs.compose.bom))
    androidTestImplementation(libs.ui.test.junit4)
    debugImplementation(libs.ui.tooling)
    debugImplementation(libs.ui.test.manifest)
    debugImplementation(libs.tiles.tooling)
}
