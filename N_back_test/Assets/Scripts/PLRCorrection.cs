using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

public class PLRCorrectionSample : MonoBehaviour
{
    // ============================================================
    // Inspector References
    // ============================================================

    [Header("References")]

    // FastAPIサーバとの通信を行うクラス
    public RequestSender requestSender;
    // キャリブレーション時に表示するグレーパネル
    // public Image calibrationPanel;

    // キャリブレーション時に表示するグレーパネル
    public Renderer[] quads_eye_calibration = new Renderer[7];
    public GameObject canvas_N_back;
    public GameObject directionalLight;
    
    // 状態表示用UI
    public TextMeshProUGUI statusText;
    // XR用カメラ
    public Camera xrCamera;

    // ============================================================
    // Sampling Settings
    // ============================================================

    [Header("Sampling")]
    // 瞳孔径取得周波数
    // 10Hz = 0.1秒ごと
    public float pupilSampleHz = 10.0f;

    // 視線位置・輝度取得周波数
    // 2Hz = 0.5秒ごと
    public float gazeSampleHz = 2.0f;

    // 平滑化窓サイズ
    // Inspectorで変更可能な時間窓の moving average
    [Header("Smoothing")]
    [Min(0.01f)]
    [Tooltip("Moving-average window used for pupil diameter and TEPR, in seconds.")]
    public float pupilSmoothingWindowSeconds = 2.0f;

    // ============================================================
    // PLR Parameters
    // ============================================================

    [Header("PLR Parameters")]
    // PLRモデル:
    // d(Y) = a * exp(-bY) + c
    // キャリブレーション後にFastAPIから更新される
    public float a = 2.0f;
    public float b = 6.0f;
    public float c = 3.0f;

    // ============================================================
    // Constants
    // ============================================================

    // 前サンプルとの差が0.4mmを超えた場合は除外
    // 瞬き・トラッキング乱れ対策
    private const float BlinkThresholdMm = 0.4f;
    // キャリブレーション時の各輝度表示時間
    private const float CalibrationDuration = 6.0f;
    // 瞳孔反応遅延
    // TEPR = d_m - d(Y(t - 0.5))
    private const float TEPRDelay = 0.5f;
    // 注視領域と背景領域の重み
    private const float wFixation = 0.26f;
    private const float wBackground = 0.74f;
    
    // サーバ送信周期
    private const float EyeDataSendIntervalSeconds = 2.0f;

    // ============================================================
    // Timers
    // ============================================================

    // 10Hz瞳孔取得タイマー
    private float pupilTimer = 0.0f;
    // 2Hz視線取得タイマー
    private float gazeTimer = 0.0f;
    // 1Hz送信用タイマー
    private float sendTimer = 0.0f;
    // 最後にserver2.pyへ送信した時刻
    private float lastEyeDataSendTime = 0.0f;

    // ============================================================
    // Runtime States
    // ============================================================

    // 前回の有効な瞳孔径
    // 外れ値除外に使用
    private float? previousValidPupil = null;
    // キャリブレーション中か
    private bool isCalibrationRunning = false;
    // PLRモデル取得済みか
    private bool isModelReady = false;
    // GPU Readback中か
    private bool isReadbackRunning = false;

    // ============================================================
    // 最新値保持
    // ============================================================
    // 生瞳孔径
    private float latestPupilRaw = 0.0f;
    // 平滑化瞳孔径
    private float latestPupilSmoothed = 0.0f;
    // PLR予測瞳孔径
    private float latestPredictedPupil = 0.0f;
    // 平滑化TEPR
    private float latestTEPRSmoothed = 0.0f;
    // 使用輝度
    private float latestDelayedY = 0.0f;

    // ============================================================
    // GPU Resources
    // ============================================================
    // 画面キャプチャ用RenderTexture
    private RenderTexture screenRT;

    // ============================================================
    // Runtime Data
    // ============================================================
    // 最新の重み付き輝度
    private float latestCombinedY = 0.0f;
    // 最新のワールド空間視線点
    private Vector3 latestWorldGazePoint;
    // 最新のスクリーン空間視線点
    private Vector2 latestScreenGazePoint;

    [Header("Luminance Camera")]
    public Camera luminanceCamera;
    public int luminanceTextureWidth = 1024;
    public int luminanceTextureHeight = 1024;

    // ============================================================
    // Queues
    // ============================================================
    // 過去の輝度履歴
    // 0.5秒前の輝度取得に使用
    private readonly Queue<LuminanceSample> luminanceHistory = new Queue<LuminanceSample>();
    // 生瞳孔径履歴
    // 1秒窓平滑化用
    private readonly Queue<ValueSample> pupilMeanSamples = new Queue<ValueSample>();
    // TEPR履歴
    // TEPR平滑化用
    private readonly Queue<ValueSample> teprSamples = new Queue<ValueSample>();

    // ============================================================
    // Calibration Data
    // ============================================================
    // キャリブレーション用データ
    // FastAPIへ送信する
    private List<RequestSender.PLRCalibrationSample> calibrationSamples = new List<RequestSender.PLRCalibrationSample>();

    // ============================================================
    // Calibration Brightness Levels
    // ============================================================
    // 8段階の輝度
    private readonly float[] calibrationLevels =
    {
        0.05f, 0.18f, 0.32f, 0.46f,
        0.60f, 0.74f, 0.88f, 1.00f
    };

    // 疑似ランダム順
    private readonly int[] pseudoRandomOrder =
    {
        2, 6, 1, 7, 3, 0, 5, 4
    };

    // ============================================================
    // Structs
    // ============================================================
    // 輝度履歴1件分
    private struct LuminanceSample
    {
        public float time;
        public float y;

        public LuminanceSample(float time, float y)
        {
            this.time = time;
            this.y = y;
        }
    }

    // 平滑化用データ
    private struct ValueSample
    {
        public float time;
        public float value;

        public ValueSample(float time, float value)
        {
            this.time = time;
            this.value = value;
        }
    }

    // ============================================================
    // Start
    // ============================================================
    void Start()
    {
        // Inspectorで未設定なら、シーン内からRequestSenderを探す
        if (requestSender == null)
        {
            requestSender = FindFirstObjectByType<RequestSender>();
            calibrationSamples = new List<RequestSender.PLRCalibrationSample>();
        }

        // それでも見つからない場合はエラーを出して終了する
        if (requestSender == null)
        {
            Debug.LogError("RequestSender が見つかりません。Inspectorで割り当てるか、シーン上に配置してください。");
            return;
        }

        // PostStatusFlagはIEnumeratorなのでStartCoroutineで実行する
        StartCoroutine(requestSender.PostStatusFlag("PLCorrection.csより愛をこめて"));

        // Camera未設定ならMainCameraを使う
        if (xrCamera == null)
        {
            xrCamera = Camera.main;
        }

        // GPU Readback用RenderTexture作成
        screenRT = new RenderTexture(
            luminanceTextureWidth,
            luminanceTextureHeight,
            24,
            RenderTextureFormat.ARGB32
        );

        if (luminanceCamera != null)
        {
            luminanceCamera.targetTexture = screenRT;
        }

        // キャリブレーションパネルを非表示にする
        if (quads_eye_calibration != null)
        {
            quads_eye_calibration[0].gameObject.SetActive(false);
        }
    }

    // ============================================================
    // Update
    // ============================================================
    void Update()
    {
        pupilTimer += Time.deltaTime;
        gazeTimer += Time.deltaTime;
        sendTimer += Time.deltaTime;

        // 10Hz瞳孔処理
        if (pupilTimer >= 1.0f / pupilSampleHz)
        {
            pupilTimer = 0.0f;
            SamplePupil10Hz();
        }

        // 2Hz視線・輝度処理
        if (gazeTimer >= 1.0f / gazeSampleHz)
        {
            gazeTimer = 0.0f;
            SampleGazeAndLuminance2Hz();
        }

        // 1Hz server送信
        if (sendTimer >= EyeDataSendIntervalSeconds)
        {
            sendTimer = 0.0f;
            SendEyeTrackingData();
        }
    }

    // ============================================================
    // Calibration
    // ============================================================

    public void StartPupilCalibration()
    {
        canvas_N_back.SetActive(false);
        directionalLight.SetActive(false);
        quads_eye_calibration[0].gameObject.SetActive(true);
        StartCoroutine(CalibrationRoutine());
    }

    IEnumerator CalibrationRoutine()
    {
        isCalibrationRunning = true;
        isModelReady = false;

        // 古いデータ削除
        calibrationSamples.Clear();

        if (statusText != null)
        {
            statusText.text = "PLR Calibration Start";
            StartCoroutine(requestSender.PostStatusFlag(statusText.text));
        }

        // 疑似ランダム順で8段階表示
        foreach (int index in pseudoRandomOrder)
        {
            float gray = calibrationLevels[index];

            // 画面全体グレー表示
            if (quads_eye_calibration[0] != null)
            {
                quads_eye_calibration[0].gameObject.SetActive(true);

                for(int i=1; i<7; i++)
                {
                    quads_eye_calibration[i].material.color = new Color(gray, gray, gray, 1.0f);
                }

                
            }

            float startTime = Time.time;

            // 6秒間記録
            while (Time.time - startTime < CalibrationDuration)
            {
                // 左右平均瞳孔径取得
                float? pupil = GetFilteredMeanPupilDiameter();

                // 有効データのみ記録
                if (pupil.HasValue)
                {
                    // グレーなのでR=G=B
                    float y = CalculateLuminance(gray, gray, gray);
                    float y_cam = GetDelayedLuminance(Time.time - TEPRDelay);

                    calibrationSamples.Add(new RequestSender.PLRCalibrationSample
                    {
                        luminanceY_panel = y,
                        luminanceY_cam = y_cam,
                        luminanceGap = y_cam-y,
                        pupilMm = pupil.Value
                    }
                    );

                    Debug.Log("calibrationSamples Added: Y=" + y + "pupilMm=" + pupil.Value);
                }

                yield return new WaitForSeconds(1.0f / pupilSampleHz);
            }
        }

        // パネル非表示
        if (quads_eye_calibration[0] != null)
        {
            quads_eye_calibration[0].gameObject.SetActive(false);
        }

        canvas_N_back.SetActive(true);
        directionalLight.SetActive(true);

        // RequestSender未設定なら終了
        if (requestSender == null)
        {
            Debug.LogError("RequestSender is not assigned.");
            yield break;
        }
        bool received = false;

        // ========================================================
        // FastAPIへPLR学習要求
        // ========================================================
        requestSender.PostPLRFitRequest(
            calibrationSamples,
            result =>
            {
                // 返却されたPLRモデル保存
                a = Mathf.Clamp(result.a, 1.0f, 4.0f);
                b = Mathf.Clamp(result.b, 4.0f, 8.0f);
                c = Mathf.Clamp(result.c, 0.0f, 8.0f);

                isModelReady = true;
                received = true;

                Debug.Log($"PLR model received: " + $"a={a}, b={b}, c={c}");
            }
        );

        // 応答待ち
        while (!received)
        {
            yield return null;
        }

        isCalibrationRunning = false;

        if (statusText != null)
        {
            statusText.text = $"Calibration Done\n" + $"a={a:F3}, " + $"b={b:F3}, " + $"c={c:F3}";
        }
    }

    // ============================================================
    // 10Hz Pupil Sampling
    // ============================================================
    void SamplePupil10Hz()
    {
        // 左右平均瞳孔径取得
        float? pupil = GetFilteredMeanPupilDiameter();

        // 無効データなら終了
        if (!pupil.HasValue)
        {
            return;
        }

        // ========================================================
        // 生瞳孔径
        // ========================================================

        float pupilRaw = pupil.Value;

        // 生瞳孔径履歴へ追加
        AddValueSample(pupilMeanSamples, pupilRaw);

        // キャリブレーション中は終了
        if (isCalibrationRunning || !isModelReady)
        {
            return;
        }

        // ========================================================
        // 平滑化瞳孔径
        // ========================================================

        // float pupilSmoothed = GetSmoothedValue(pupilMeanSamples);

        // ========================================================
        // TEPR計算
        // ========================================================

        // 0.5秒前輝度
        float delayedY = GetDelayedLuminance(Time.time - TEPRDelay);

        // PLR予測瞳孔径
        float predictedPupil = PredictPupilDiameter(delayedY);

        // TEPR
        float teprRaw = pupilRaw - predictedPupil;

        // ========================================================
        // TEPR平滑化
        // ========================================================

        AddValueSample(teprSamples, teprRaw);

        // float teprSmoothed = GetSmoothedValue(teprSamples);

        // ========================================================
        // 最新値保存
        // ========================================================

        latestPupilRaw = pupilRaw;
        // latestPupilSmoothed = pupilSmoothed;
        latestPredictedPupil = predictedPupil;
        // latestTEPRSmoothed = teprSmoothed;
        latestDelayedY = delayedY;

        // ========================================================
        // UI表示
        // ========================================================

        if (statusText != null)
        {
            statusText.text =
                $"Pupil raw: {latestPupilRaw:F3} mm\n" +
                $"Pupil smoothed: {latestPupilSmoothed:F3} mm\n" +
                $"Predicted pupil: {latestPredictedPupil:F3} mm\n" +
                $"TEPR smoothed: {latestTEPRSmoothed:F3} mm\n" +
                $"Luminance: {latestDelayedY:F3}";
        }
    }

    // ============================================================
    // 左右平均瞳孔径取得
    // ============================================================
    float? GetFilteredMeanPupilDiameter()
    {
        // OpenXRから瞳孔情報取得
        XR_HTC_eye_tracker.Interop.GetEyePupilData(out XrSingleEyePupilDataHTC[] pupilData);

        if (pupilData == null)
        {
            return null;
        }

        int left = (int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC;

        int right = (int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC;

        // 左右どちらか無効なら除外
        // if (!pupilData[left].isDiameterValid || !pupilData[right].isDiameterValid)
        // {
        //     return null;
        // }

        if (!pupilData[left].isDiameterValid)
        {
            return null;
        }
        else if (!pupilData[right].isDiameterValid)
        {
            return null;
        }

        // 左右平均
        float meanPupil = (pupilData[left].pupilDiameter + pupilData[right].pupilDiameter) / 2.0f;

        // 前回との差が大きすぎる場合除外
        if (previousValidPupil.HasValue && Mathf.Abs(meanPupil - previousValidPupil.Value) > BlinkThresholdMm)
        {
            // Debug.Log("GetFilteredMeanPupilDiameter: Pupil Change is too large");
            // Debug.Log("previous:" + previousValidPupil + " now:" + meanPupil);
            previousValidPupil = meanPupil;
            return null;
        }

        // 今回値保存
        previousValidPupil = meanPupil;
        // Debug.Log("GetFilteredMeanPupilDiameter: Pupil Data is saved correctly");
        return meanPupil;
    }

    // ============================================================
    // 2Hz 視線・輝度取得
    // ============================================================
    void SampleGazeAndLuminance2Hz()
    {
        XR_HTC_eye_tracker.Interop.GetEyeGazeData(out XrSingleEyeGazeDataHTC[] gazeData);
        // Debug.Log("SampleGazeAndLuminance2Hz: Start");

        if (gazeData == null)
        {
            // Debug.Log("SampleGazeAndLuminance2Hz: GazeData is empty");
            return;
        }

        int left = (int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC;
        int right = (int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC;

        // 左右どちらか無効なら終了
        if (!gazeData[left].isValid || !gazeData[right].isValid)
        {
            return;
        }

        // ========================================================
        // 左目視線
        // ========================================================
        Vector3 leftOrigin = gazeData[left].gazePose.position.ToUnityVector();
        Quaternion leftRot = gazeData[left].gazePose.orientation.ToUnityQuaternion();
        Vector3 leftDir = leftRot * Vector3.forward;

        // ========================================================
        // 右目視線
        // ========================================================
        Vector3 rightOrigin = gazeData[right].gazePose.position.ToUnityVector();
        Quaternion rightRot = gazeData[right].gazePose.orientation.ToUnityQuaternion();
        Vector3 rightDir = rightRot * Vector3.forward;

        // ========================================================
        // 両眼平均視線
        // ========================================================
        Vector3 origin = (leftOrigin + rightOrigin) / 2.0f;
        Vector3 direction = ((leftDir + rightDir) / 2.0f).normalized;
        Ray gazeRay = new Ray(origin, direction);

        // ========================================================
        // Raycast
        // ========================================================
        if (Physics.Raycast(gazeRay, out RaycastHit hit, 100.0f))
        {
            latestWorldGazePoint = hit.point;
        }
        else
        {
            // 当たらなければ10m先
            latestWorldGazePoint = origin + direction * 10.0f;
        }

        // ========================================================
        // スクリーン座標変換
        // ========================================================
        latestScreenGazePoint = xrCamera.WorldToScreenPoint(latestWorldGazePoint);

        // ========================================================
        // 輝度取得
        // ========================================================
        // Debug.Log("SampleGazeAndLuminance2Hz: CaptureLuminanceFromScreen");
        CaptureLuminanceFromScreen(latestScreenGazePoint);
    }

    void SendEyeTrackingData()
    {
        // RequestSender未設定なら終了
        if (requestSender == null)
        {
            return;
        }

        // キャリブレーション中・モデル未準備なら送信しない
        if (isCalibrationRunning || !isModelReady)
        {
            return;
        }

        latestPupilSmoothed = GetSmoothedValue(pupilMeanSamples);
        latestTEPRSmoothed = GetSmoothedValue(teprSamples);
        // server2.pyへ送信
        requestSender.PostEyeData(
            latestPupilRaw,
            latestPupilSmoothed,
            latestPredictedPupil,
            latestTEPRSmoothed,
            latestDelayedY
        );
    }

    // ============================================================
    // GPU Readback
    // ============================================================
    // void CaptureLuminanceFromScreen(Vector2 fixationScreenPoint)
    // {
    //     Debug.Log("CaptureLuminanceFromScreen:fixationScreenPoint x=" + fixationScreenPoint.x + " y=" + fixationScreenPoint.y);
    //     // Debug.Log("CaptureScreenshotIntoRenderTexture: start");
    //     // 多重実行防止
    //     if (isReadbackRunning)
    //     {
    //         return;
    //     }
    //     isReadbackRunning = true;
    //     // Debug.Log("CaptureScreenshotIntoRenderTexture: Try copying current display to RenderTexture.");
    //     // 現在画面をRenderTextureへコピー
    //     ScreenCapture.CaptureScreenshotIntoRenderTexture(screenRT);

    //     // GPU→CPU 非同期コピー
    //     AsyncGPUReadback.Request(
    //         screenRT,
    //         0,
    //         TextureFormat.RGBA32,
    //         request =>
    //         {
    //             isReadbackRunning = false;

    //             if (request.hasError)
    //             {
    //                 Debug.Log("CaptureScreenshotIntoRenderTexture: GPU to CPU request error");
    //                 return;
    //             }

    //             NativeArray<Color32> pixels = request.GetData<Color32>();
    //             CalculateFixationAndBackgroundY(
    //                 pixels,
    //                 Screen.width,
    //                 Screen.height,
    //                 fixationScreenPoint
    //             );
    //         }
    //     );
    // }

    void CaptureLuminanceFromScreen(Vector2 fixationScreenPoint)
    {
        // GPU Readback多重実行防止
        if (isReadbackRunning)
        {
            return;
        }

        // 輝度計算用Cameraが未設定なら終了
        if (luminanceCamera == null)
        {
            Debug.LogError("luminanceCamera is not assigned.");
            return;
        }

        // RenderTextureが未生成なら終了
        if (screenRT == null)
        {
            Debug.LogError("screenRT is null.");
            return;
        }

        isReadbackRunning = true;

        // 輝度計算用CameraでRenderTextureへ描画
        luminanceCamera.Render();

        // GPU上のRenderTextureをCPUへ非同期読み出し
        AsyncGPUReadback.Request(screenRT, 0, TextureFormat.RGBA32, request =>
        {
            isReadbackRunning = false;

            if (request.hasError)
            {
                Debug.LogError("AsyncGPUReadback failed.");
                return;
            }

            NativeArray<Color32> pixels = request.GetData<Color32>();

            // 元のScreen座標を低解像度RenderTexture座標へ変換
            Vector2 rtFixationPoint = new Vector2(
                fixationScreenPoint.x / Screen.width * luminanceTextureWidth,
                fixationScreenPoint.y / Screen.height * luminanceTextureHeight
            );

            CalculateFixationAndBackgroundY(
                pixels,
                luminanceTextureWidth,
                luminanceTextureHeight,
                rtFixationPoint
            );

            //Debug.Log($"LuminanceCamera Y={latestCombinedY:F3}");
        });
    }

    // ============================================================
    // 輝度計算
    // ============================================================

    void CalculateFixationAndBackgroundY(
        NativeArray<Color32> pixels,
        int width,
        int height,
        Vector2 fixationPoint
    )
    {
        // 注視円半径
        int radius = width / 5;

        float fixationSum = 0.0f;
        float backgroundSum = 0.0f;

        int fixationCount = 0;
        int backgroundCount = 0;

        int fx = Mathf.RoundToInt(fixationPoint.x);
        int fy = Mathf.RoundToInt(fixationPoint.y);
        int r2 = radius * radius;


        // float out_r = 0f;
        // float out_g = 0f;
        // float out_b = 0f;


        // 4px間引き
        for (int y = 0; y < height; y += 1)
        {
            for (int x = 0; x < width; x += 1)
            {
                int index = y * width + x;

                if (index < 0 || index >= pixels.Length)
                {
                    continue;
                }

                Color32 color = pixels[index];

                // RGB正規化
                float r = color.r / 255.0f;
                float g = color.g / 255.0f;
                float b = color.b / 255.0f;

                // RGB→輝度Y
                float luminance = CalculateLuminance(r, g, b);
                // Debug.Log("CalculateFixationAndBackgroundY: luminance=" + luminance);
                int dx = x - fx;
                int dy = y - fy;
                // 注視領域 or 背景
                if (dx * dx + dy * dy <= r2)
                {
                    fixationSum += luminance;
                    fixationCount++;
                }
                else
                {
                    backgroundSum += luminance;
                    backgroundCount++;
                }
            }
        }
        //Debug.Log("CalculateFixationAndBackgroundY: r=" + out_r + " g=" + out_g + " b=" + out_b);

        // 注視領域平均
        float yFixation = fixationCount > 0 ? fixationSum / fixationCount : 0.0f;
        // 背景平均
        float yBackground = backgroundCount > 0 ? backgroundSum / backgroundCount : 0.0f;
        // 重み付き平均
        latestCombinedY = wFixation * yFixation + wBackground * yBackground;

        Debug.Log(
            $"YFix={yFixation:F3}, YBack={yBackground:F3}, YCombined={latestCombinedY:F3}, " +
            $"FixCount={fixationCount}, BackCount={backgroundCount}, " +
            $"FixPoint=({fx},{fy}), Radius={radius}"
        );

        // 履歴保存
        luminanceHistory.Enqueue(new LuminanceSample(
                Time.time,
                latestCombinedY
            )
        );

        // 古い履歴削除
        while (luminanceHistory.Count > 0 && Time.time - luminanceHistory.Peek().time > 5.0f)
        {
            luminanceHistory.Dequeue();
        }
    }

    // ============================================================
    // Queue追加
    // ============================================================
    void AddValueSample(Queue<ValueSample> samples, float value)
    {
        float now = Time.time;

        // 現在値追加
        samples.Enqueue(new ValueSample(now, value));

        // 設定した時間窓より古いものを削除
        float windowSeconds = Mathf.Max(0.01f, pupilSmoothingWindowSeconds);
        while (samples.Count > 0 && now - samples.Peek().time > windowSeconds)
        {
            samples.Dequeue();
        }
    }

    // ============================================================
    // Queue平均取得
    // ============================================================
    float GetSmoothedValue(Queue<ValueSample> samples)
    {
        // 空なら0
        if (samples.Count == 0)
        {
            return 0.0f;
        }

        float sum = 0.0f;

        // Queue平均
        foreach (ValueSample sample in samples)
        {
            sum += sample.value;
        }
        return sum / samples.Count;
    }

    // ============================================================
    // RGB→輝度Y
    // ============================================================
    float CalculateLuminance(float r, float g, float b)
    {
        return
            0.2125f * r +
            0.7154f * g +
            0.0720f * b;
    }

    // ============================================================
    // PLR予測
    // ============================================================
    float PredictPupilDiameter(float y)
    {
        // d(Y)=a*exp(-bY)+c
        return a * Mathf.Exp(-b * y) + c;
    }

    // ============================================================
    // 遅延輝度取得
    // ============================================================
    float GetDelayedLuminance(float targetTime)
    {
        if (luminanceHistory.Count == 0)
        {
            return latestCombinedY;
        }

        LuminanceSample closest = default;

        float closestDiff = float.MaxValue;

        foreach (LuminanceSample sample in luminanceHistory)
        {
            float diff = Mathf.Abs(sample.time - targetTime);

            if (diff < closestDiff)
            {
                closest = sample;
                closestDiff = diff;
            }
        }
        return closest.y;
    }
}
