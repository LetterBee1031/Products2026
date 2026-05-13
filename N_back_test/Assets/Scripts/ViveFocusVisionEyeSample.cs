using TMPro;
using UnityEngine;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

public class ViveFocusVisionEyeSample : MonoBehaviour
{
    public TextMeshProUGUI[] text_gazeData = new TextMeshProUGUI[2];
    public TextMeshProUGUI[] text_pupilData = new TextMeshProUGUI[2];

    void Update()
    {
        XR_HTC_eye_tracker.Interop.GetEyeGazeData(
            out XrSingleEyeGazeDataHTC[] gazeData
        );

        XR_HTC_eye_tracker.Interop.GetEyePupilData(
            out XrSingleEyePupilDataHTC[] pupilData
        );

        if (gazeData == null || pupilData == null)
        {
            return;
        }

        int left = (int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC;
        int right = (int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC;

        PrintEyeData(0, "Left", gazeData[left], pupilData[left]);
        PrintEyeData(1, "Right", gazeData[right], pupilData[right]);
    }

    void PrintEyeData(
        int eyeIndex,
        string eyeName,
        XrSingleEyeGazeDataHTC gaze,
        XrSingleEyePupilDataHTC pupil
    )
    {
        string gazeText;
        string pupilDiameterText;
        string pupilPositionText;

        // 視線情報
        if (gaze.isValid)
        {
            Vector3 gazePosition = gaze.gazePose.position.ToUnityVector();
            Quaternion gazeRotation = gaze.gazePose.orientation.ToUnityQuaternion();
            Vector3 gazeDirection = gazeRotation * Vector3.forward;

            gazeText =
                $"{eyeName} Eye Gaze" +
                $"Position: {gazePosition}" +
                $"Direction: {gazeDirection}";
        }
        else
        {
            gazeText = $"{eyeName} Eye Gaze: invalid";
        }

        // 瞳孔径
        if (pupil.isDiameterValid)
        {
            float pupilDiameter = pupil.pupilDiameter;

            pupilDiameterText =
                $"{eyeName} Eye Pupil Diameter" +
                $"{pupilDiameter:F3}";
        }
        else
        {
            pupilDiameterText =
                $"{eyeName} Eye Pupil Diameter" +
                "invalid";
        }

        // 瞳孔位置
        if (pupil.isPositionValid)
        {
            var pupilPosition = pupil.pupilPosition;

            pupilPositionText =
                $"Pupil Position" +
                $"x: {pupilPosition.x:F3}, y: {pupilPosition.y:F3}";
        }
        else
        {
            pupilPositionText = "Pupil Position: invalid";
        }

        string pupilText =
            pupilDiameterText + "\n" +
            pupilPositionText;

        Debug.Log(gazeText);
        Debug.Log(pupilText);

        if (text_gazeData != null && text_gazeData.Length > eyeIndex && text_gazeData[eyeIndex] != null)
        {
            text_gazeData[eyeIndex].text = gazeText;
        }

        if (text_pupilData != null && text_pupilData.Length > eyeIndex && text_pupilData[eyeIndex] != null)
        {
            text_pupilData[eyeIndex].text = pupilText;
        }
    }
}