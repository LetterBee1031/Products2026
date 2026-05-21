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
    public Image calibrationPanel;
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
    // 平滑化窓サイズ
    // 1秒窓 moving average
    private const float SmoothWindowSeconds = 1.0f;
    // サーバ送信周期
    private const float EyeDataSendIntervalSeconds = 1.0f;

    // ============================================================
    // Timers
    // ============================================================

    // 10Hz瞳孔取得タイマー
    private float pupilTimer = 0.0f;
    // 2Hz視線取得タイマー
    private float gazeTimer = 0.0f;
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
        Screen.width,
        Screen.height,
        24,
        RenderTextureFormat.ARGB32
    );

    // キャリブレーションパネルを非表示にする
    if (calibrationPanel != null)
    {
        calibrationPanel.gameObject.SetActive(false);
    }
}

    // ============================================================
    // Update
    // ============================================================
    void Update()
    {
        // タイマー更新
        pupilTimer += Time.deltaTime;
        gazeTimer += Time.deltaTime;

        // ========================================================
        // 10Hz瞳孔径取得
        // ========================================================

        if (pupilTimer >= 1.0f / pupilSampleHz)
        {
            pupilTimer = 0.0f;
            SamplePupil10Hz();
        }

        // ========================================================
        // 2Hz視線・輝度取得
        // ========================================================
        if (gazeTimer >= 1.0f / gazeSampleHz)
        {
            gazeTimer = 0.0f;
            SampleGazeAndLuminance2Hz();
        }
    }

    // ============================================================
    // Calibration
    // ============================================================

    public void StartPupilCalibration()
    {
        calibrationPanel.gameObject.SetActive(true);
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
            if (calibrationPanel != null)
            {
                calibrationPanel.gameObject.SetActive(true);

                calibrationPanel.color = new Color(gray, gray, gray, 1.0f);
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

                    calibrationSamples.Add(new RequestSender.PLRCalibrationSample
                        {
                            luminanceY = y,
                            pupilMm = pupil.Value
                        }
                    );
                }

                yield return new WaitForSeconds(1.0f / pupilSampleHz);
            }
        }

        // パネル非表示
        if (calibrationPanel != null)
        {
            calibrationPanel.gameObject.SetActive(false);
        }

        // RequestSender未設定なら終了
        if (requestSender == null)
        {
            Debug.LogError(
                "RequestSender is not assigned."
            );
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
        // 生瞳孔径
        float pupilRaw = pupil.Value;

        // ========================================================
        // 生瞳孔径履歴へ追加
        // ========================================================
        AddValueSample(pupilMeanSamples, pupilRaw);

        // キャリブレーション中は終了
        if (isCalibrationRunning || !isModelReady)
        {
            return;
        }

        // ========================================================
        // 平滑化瞳孔径
        // ========================================================
        float pupilSmoothed = GetSmoothedValue(pupilMeanSamples);

        // ========================================================
        // TEPR計算
        // ========================================================
        // 0.5秒前の輝度
        float delayedY = GetDelayedLuminance(Time.time - TEPRDelay);

        // PLR予測瞳孔径
        float predictedPupil = PredictPupilDiameter(delayedY);

        // TEPR
        float teprRaw = pupilSmoothed - predictedPupil;

        // ========================================================
        // TEPR履歴へ追加
        // ========================================================
        AddValueSample(teprSamples, teprRaw);
        // 平滑化TEPR
        float teprSmoothed = GetSmoothedValue(teprSamples);

        // ========================================================
        // server2.pyへ送信
        // ========================================================
        if (Time.time - lastEyeDataSendTime >= EyeDataSendIntervalSeconds)
        {
            lastEyeDataSendTime = Time.time;

            if (requestSender != null)
            {
                requestSender.PostEyeData(
                    pupilRaw,
                    pupilSmoothed,
                    predictedPupil,
                    teprSmoothed,
                    delayedY
                );
            }
        }

        // ========================================================
        // UI表示
        // ========================================================
        if (statusText != null)
        {
            statusText.text =
                $"Pupil raw: " + $"{pupilRaw:F3} mm\n" +
                $"Pupil smoothed: " + $"{pupilSmoothed:F3} mm\n" +
                $"Predicted pupil: " + $"{predictedPupil:F3} mm\n" +
                $"TEPR raw: " + $"{teprRaw:F3} mm\n" +
                $"TEPR smoothed: " + $"{teprSmoothed:F3} mm\n" +
                $"Luminance: " + $"{delayedY:F3}";
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
        if (!pupilData[left].isDiameterValid || !pupilData[right].isDiameterValid)
        {
            return null;
        }

        // 左右平均
        float meanPupil = (pupilData[left].pupilDiameter + pupilData[right].pupilDiameter) / 2.0f;

        // 前回との差が大きすぎる場合除外
        if (previousValidPupil.HasValue && Mathf.Abs(meanPupil - previousValidPupil.Value) > BlinkThresholdMm)
        {
            return null;
        }
        // 今回値保存
        previousValidPupil = meanPupil;
        return meanPupil;
    }

    // ============================================================
    // 2Hz 視線・輝度取得
    // ============================================================
    void SampleGazeAndLuminance2Hz()
    {
        XR_HTC_eye_tracker.Interop.GetEyeGazeData(out XrSingleEyeGazeDataHTC[] gazeData);

        if (gazeData == null)
        {
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
        Vector3 direction = ((leftDir + rightDir ) / 2.0f).normalized;
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
        CaptureLuminanceFromScreen(latestScreenGazePoint);
    }

    // ============================================================
    // GPU Readback
    // ============================================================
    void CaptureLuminanceFromScreen(Vector2 fixationScreenPoint)
    {
        // 多重実行防止
        if (isReadbackRunning)
        {
            return;
        }
        isReadbackRunning = true;

        // 現在画面をRenderTextureへコピー
        ScreenCapture.CaptureScreenshotIntoRenderTexture(screenRT);

        // GPU→CPU 非同期コピー
        AsyncGPUReadback.Request(
            screenRT,
            0,
            TextureFormat.RGBA32,
            request =>
            {
                isReadbackRunning = false;

                if (request.hasError)
                {
                    return;
                }

                NativeArray<Color32> pixels = request.GetData<Color32>();
                CalculateFixationAndBackgroundY(
                    pixels,
                    Screen.width,
                    Screen.height,
                    fixationScreenPoint
                );
            }
        );
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

        // 4px間引き
        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
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

        // 注視領域平均
        float yFixation = fixationCount > 0 ? fixationSum /fixationCount : 0.0f;
        // 背景平均
        float yBackground = backgroundCount > 0 ? backgroundSum / backgroundCount : 0.0f;
        // 重み付き平均
        latestCombinedY = wFixation * yFixation + wBackground * yBackground;
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

        // 1秒より古いもの削除
        while (samples.Count > 0 && now - samples.Peek().time > SmoothWindowSeconds)
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