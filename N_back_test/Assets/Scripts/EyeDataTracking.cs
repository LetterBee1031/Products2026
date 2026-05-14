using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

public class EyeDataTracking : MonoBehaviour
{
    public RequestSender requestSender;
    // 左右の視線情報を表示するText
    // 0: Left, 1: Right
    public TextMeshProUGUI[] text_gazeData = new TextMeshProUGUI[2];

    // 左右の瞳孔情報を表示するText
    // 0: Left, 1: Right
    public TextMeshProUGUI[] text_pupilData = new TextMeshProUGUI[2];

    // 平滑化に使う時間幅
    // 直近1秒分の瞳孔径を平均する
    private const float SmoothWindowSeconds = 1.0f;

    // UI表示を更新する間隔
    // ここでは1秒に1回だけ表示を更新する
    private const float PrintIntervalSeconds = 1.0f;

    // 最後にUI表示を更新した時刻
    private float lastPrintTime = 0.0f;

    // 左目の瞳孔径データ履歴
    private readonly Queue<PupilSample> leftPupilSamples = new Queue<PupilSample>();

    // 右目の瞳孔径データ履歴
    private readonly Queue<PupilSample> rightPupilSamples = new Queue<PupilSample>();

    // 最新の左目視線データ
    private XrSingleEyeGazeDataHTC latestLeftGaze;

    // 最新の右目視線データ
    private XrSingleEyeGazeDataHTC latestRightGaze;

    // 最新の左目瞳孔データ
    private XrSingleEyePupilDataHTC latestLeftPupil;

    // 最新の右目瞳孔データ
    private XrSingleEyePupilDataHTC latestRightPupil;

    // 最新データを取得できているかどうか
    private bool hasLatestData = false;

    // 瞳孔径1サンプル分のデータ構造
    private struct PupilSample
    {
        // データを取得した時刻
        public float time;

        // 瞳孔径
        public float diameter;

        public PupilSample(float time, float diameter)
        {
            this.time = time;
            this.diameter = diameter;
        }
    }

    // 瞳孔径の送信
    public void SendPupilDiameterData(float leftPupilDiameter, float rightPupilDiameter)
    {
        if (requestSender == null)
        {
            requestSender = GetComponent<RequestSender>();
        }

        if (requestSender == null)
        {
            Debug.LogWarning("EyeDataTracking: RequestSender is not assigned.");
            return;
        }

        if (!hasLatestData ||
            !latestLeftPupil.isDiameterValid ||
            !latestRightPupil.isDiameterValid)
        {
            Debug.LogWarning("EyeDataTracking: pupil diameter data is not valid.");
            return;
        }

        requestSender.PostEyeData(
            leftPupilDiameter,
            rightPupilDiameter
        );
    }

    void Update()
    {
        // 毎フレーム、VIVE OpenXRから左右の視線情報を取得する
        XR_HTC_eye_tracker.Interop.GetEyeGazeData(
            out XrSingleEyeGazeDataHTC[] gazeData
        );

        // 毎フレーム、VIVE OpenXRから左右の瞳孔情報を取得する
        XR_HTC_eye_tracker.Interop.GetEyePupilData(
            out XrSingleEyePupilDataHTC[] pupilData
        );

        // データ取得に失敗した場合は、このフレームの処理を中断する
        if (gazeData == null || pupilData == null)
        {
            return;
        }

        // 左目・右目の配列番号を取得する
        int left = (int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC;
        int right = (int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC;

        // 最新の視線・瞳孔データを保存する
        latestLeftGaze = gazeData[left];
        latestRightGaze = gazeData[right];
        latestLeftPupil = pupilData[left];
        latestRightPupil = pupilData[right];

        // 少なくとも1回はデータ取得できたことを記録する
        hasLatestData = true;

        // 左目の瞳孔径が有効な場合だけ、Queueに追加する
        if (latestLeftPupil.isDiameterValid)
        {
            AddPupilSample(leftPupilSamples, latestLeftPupil.pupilDiameter);
        }

        // 右目の瞳孔径が有効な場合だけ、Queueに追加する
        if (latestRightPupil.isDiameterValid)
        {
            AddPupilSample(rightPupilSamples, latestRightPupil.pupilDiameter);
        }

        // 前回のUI更新から1秒経っていない場合は、ここで処理を終了する
        // ただし、ここまでの瞳孔径データ保存は毎フレーム行われている
        if (Time.time - lastPrintTime < PrintIntervalSeconds)
        {
            return;
        }

        // UI更新時刻を現在時刻に更新する
        lastPrintTime = Time.time;

        // 最新データがまだない場合は表示しない
        if (!hasLatestData)
        {
            return;
        }

        // // 左目の情報をUIに表示する
        // PrintEyeData(0, "Left", latestLeftGaze, latestLeftPupil, leftPupilSamples);

        // // 右目の情報をUIに表示する
        // PrintEyeData(1, "Right", latestRightGaze, latestRightPupil, rightPupilSamples);

        SendPupilDiameterData(
            PrintEyeData(0, "Left", latestLeftGaze, latestLeftPupil, leftPupilSamples),
            PrintEyeData(1, "Right", latestRightGaze, latestRightPupil, rightPupilSamples)
        );
    }

    float PrintEyeData(
        int eyeIndex,                         // UI配列の番号。0: 左目, 1: 右目
        string eyeName,                       // 表示用の目の名前
        XrSingleEyeGazeDataHTC gaze,          // 最新の視線データ
        XrSingleEyePupilDataHTC pupil,        // 最新の瞳孔データ
        Queue<PupilSample> pupilSamples       // 直近1秒分の瞳孔径履歴
    )
    {
        string gazeText;
        string pupilDiameterText;
        string pupilPositionText;
        float smoothedPupilDiameter = -1.0f;

        // 視線情報が有効な場合
        if (gaze.isValid)
        {
            // OpenXR形式の位置をUnityのVector3に変換する
            Vector3 gazePosition = gaze.gazePose.position.ToUnityVector();

            // OpenXR形式の回転をUnityのQuaternionに変換する
            Quaternion gazeRotation = gaze.gazePose.orientation.ToUnityQuaternion();

            // 回転情報から視線方向ベクトルを計算する
            Vector3 gazeDirection = gazeRotation * Vector3.forward;

            gazeText =
                $"{eyeName} Eye Gaze" +
                $"Position: {gazePosition}" +
                $"Direction: {gazeDirection}";
        }
        else
        {
            // 視線情報が取得できない場合
            gazeText = $"{eyeName} Eye Gaze: invalid";
        }

        // 瞳孔径が有効な場合
        if (pupil.isDiameterValid)
        {
            // 今回取得した生の瞳孔径
            float rawPupilDiameter = pupil.pupilDiameter;

            // Queueに保存されている直近1秒分の瞳孔径平均
            smoothedPupilDiameter = GetSmoothedPupilDiameter(pupilSamples);

            pupilDiameterText =
                $"{eyeName} Eye Pupil Diameter" +
                $"Raw: {rawPupilDiameter:F3}" +
                $"Smoothed 1s: {smoothedPupilDiameter:F3}";
        }
        else
        {
            // 瞳孔径が取得できない場合
            pupilDiameterText =
                $"{eyeName} Eye Pupil Diameter\n" +
                "invalid";
        }

        // 瞳孔位置が有効な場合
        if (pupil.isPositionValid)
        {
            var pupilPosition = pupil.pupilPosition;

            pupilPositionText =
                $"Pupil Position" +
                $"x: {pupilPosition.x:F3}, y: {pupilPosition.y:F3}";
        }
        else
        {
            // 瞳孔位置が取得できない場合
            pupilPositionText = "Pupil Position: invalid";
        }

        // 瞳孔径と瞳孔位置をまとめる
        string pupilText =
            pupilDiameterText + " " +
            pupilPositionText;

        // 視線情報をUIに表示する
        if (text_gazeData != null &&
            text_gazeData.Length > eyeIndex &&
            text_gazeData[eyeIndex] != null)
        {
            text_gazeData[eyeIndex].text = gazeText;
        }

        // 瞳孔情報をUIに表示する
        if (text_pupilData != null &&
            text_pupilData.Length > eyeIndex &&
            text_pupilData[eyeIndex] != null)
        {
            text_pupilData[eyeIndex].text = pupilText;
        }
        return smoothedPupilDiameter;
    }

    // 毎フレーム取得した瞳孔径をQueueに追加する関数
    void AddPupilSample(
        Queue<PupilSample> samples,   // 瞳孔径の履歴
        float currentDiameter         // 今回取得した瞳孔径
    )
    {
        // 現在時刻を取得する
        float currentTime = Time.time;

        // 今回取得した瞳孔径を履歴に追加する
        samples.Enqueue(new PupilSample(currentTime, currentDiameter));

        // 直近1秒より古いデータをQueueから削除する
        // Queueは古い順にデータが並ぶため、先頭から削除すればよい
        while (samples.Count > 0 &&
               currentTime - samples.Peek().time > SmoothWindowSeconds)
        {
            samples.Dequeue();
        }
    }

    // Queueに残っている直近1秒分の瞳孔径平均を返す関数
    float GetSmoothedPupilDiameter(
        Queue<PupilSample> samples    // 直近1秒分の瞳孔径履歴
    )
    {
        // データがない場合は0を返す
        if (samples.Count == 0)
        {
            return 0.0f;
        }

        // 瞳孔径の合計値
        float sum = 0.0f;

        // Queue内の全データを合計する
        foreach (PupilSample sample in samples)
        {
            sum += sample.diameter;
        }

        // 平均値を返す
        return sum / samples.Count;
    }
}