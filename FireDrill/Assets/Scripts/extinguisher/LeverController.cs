using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class LeverController : MonoBehaviour
{
    [SerializeField] private XRInputReader inputReader;             // Input Actionを管理するスクリプト
    [SerializeField] private XRGrabInteractable extinguisherGrab;

    // 消火器のleverボーン
    [SerializeField] private Transform lever;
    [SerializeField] private PinManager pinManager;

    [Header("Lever Settings")]
    // レバーを握ったときの回転角度
    [SerializeField] private float pressedAngle = -30.0f;

    // レバーを動かす軸
    // 例：X軸なら (1, 0, 0)
    [SerializeField] private Vector3 rotationAxis = Vector3.right;

    // レバーの動く速さ
    [SerializeField] private float leverSpeed = 10.0f;

    // レバーの初期角度
    private Quaternion initialRotation;

    private bool isPinRemoved = false;
    private bool isLeverHeld = false;



    private void Start()
    {
        // 元のレバー角度を保存
        initialRotation = lever.localRotation;
    }

    private void Update()
    {
        isPinRemoved = pinManager.IsPinRemoved();
        isLeverHeld = extinguisherGrab.isSelected;

        if (isPinRemoved && isLeverHeld)
        {
            // Questコントローラーのトリガー値を0～1で取得
            float triggerValue =
                inputReader.buttonTriggerRight.ReadValue<float>();

            // トリガー値に応じた回転量を計算
            Quaternion pressedRotation =
                Quaternion.AngleAxis(
                    pressedAngle * triggerValue,
                    rotationAxis
                );

            // 元の角度にレバーの回転を追加
            Quaternion targetRotation = initialRotation * pressedRotation;

            // 急に角度が変わらないよう滑らかに動かす
            lever.localRotation =
                Quaternion.Slerp(
                    lever.localRotation,
                    targetRotation,
                    leverSpeed * Time.deltaTime
                );
        }

    }
}