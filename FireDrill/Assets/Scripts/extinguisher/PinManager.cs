using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class PinManager : MonoBehaviour
{
    [Header("Pin Settings")]

    [SerializeField] private Transform pinBone;             // ピンのボーン
    [SerializeField] private Transform pinVisualTarget;     // ピンが追従する空オブジェクト
    [SerializeField] private XRSocketInteractor pinSocket;  // ピンの初期位置のソケット

    [Header("Stopper")]
    [SerializeField] private Transform stopperBone;
    [SerializeField] private GameObject sealed_1;
    [SerializeField] private GameObject sealed_2;
    [SerializeField] private Vector3 stopperRotationAxis = Vector3.right;
    [SerializeField] private float stopperFallAngle = -60.0f;
    [SerializeField] private float stopperFallSpeed = 180.0f;

    private bool pinRemoved = false;
    private bool stopperFalling = false;

    private Quaternion stopperInitialRotation;
    private Quaternion stopperTargetRotation;
    private void Awake()
    {
        if (pinSocket == null)
            pinSocket = GetComponent<XRSocketInteractor>();

        if (stopperBone != null)
        {
            // ストッパーの初期姿勢を保存
            stopperInitialRotation = stopperBone.localRotation;

            // ピンを抜いた後の姿勢を計算
            stopperTargetRotation =
                stopperInitialRotation *
                Quaternion.AngleAxis(stopperFallAngle, stopperRotationAxis);
        }
    }

    private void LateUpdate()
    {
        if (pinBone == null || pinVisualTarget == null)
            return;

        // ピンの追従
        pinBone.position = pinVisualTarget.position;
        pinBone.rotation = pinVisualTarget.rotation;

        if (!stopperFalling || stopperBone == null)
            return;

        // ストッパーを目標角度まで徐々に倒す
        stopperBone.localRotation = Quaternion.RotateTowards(
            stopperBone.localRotation,
            stopperTargetRotation,
            stopperFallSpeed * Time.deltaTime
        );

        // 目標角度まで到達したら停止
        if (Quaternion.Angle(
            stopperBone.localRotation,
            stopperTargetRotation) < 0.1f)
        {
            stopperBone.localRotation = stopperTargetRotation;
            stopperFalling = false;
        }
    }

    private void OnEnable()
    {
        pinSocket.selectExited.AddListener(OnPinRemoved);
    }

    private void OnDisable()
    {
        pinSocket.selectExited.RemoveListener(OnPinRemoved);
    }

    private void OnPinRemoved(SelectExitEventArgs args)
    {
        if (pinRemoved)
            return;

        pinRemoved = true;

        // 一度抜いたらSocketを無効化
        pinSocket.enabled = false;

        // ストッパーを倒し始める
        stopperFalling = true;

        sealed_1.SetActive(false);
        sealed_2.SetActive(false);

        Debug.Log("消火器のピンが抜かれました");
    }

    public bool IsPinRemoved()
    {
        return pinRemoved;
    }
}