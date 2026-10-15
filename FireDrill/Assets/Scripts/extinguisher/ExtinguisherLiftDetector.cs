using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ExtinguisherLiftDetector : MonoBehaviour
{
    [SerializeField] private XRGrabInteractable extinguisherGrab;

    // 消火器底部に置いた判定用Transform
    [SerializeField] private Transform groundCheckPoint;
    [SerializeField] private FrontUiManager frontUiManager;
    // 床として判定するLayer
    [SerializeField] private LayerMask floorLayer;

    // この距離以内に床があれば「まだ床上」とする
    [SerializeField] private float groundCheckDistance = 0.1f;

    private bool isLifted = false;
    private bool isHeld = false;
    private bool isOnFloor = true;

    private void Update()
    {
        // 消火器が手でGrabされているか
        isHeld = extinguisherGrab.isSelected;

        if (!isLifted)
        {
            // 消火器の底から下方向にRayを飛ばす
            isOnFloor = Physics.Raycast(
                groundCheckPoint.position,
                Vector3.down,
                groundCheckDistance,
                floorLayer,
                QueryTriggerInteraction.Ignore
            );

            // Grabされていて、床から離れていれば「持ち上げた」
            isLifted = isHeld && !isOnFloor;

            if (isLifted)
            {
                Debug.Log("extinguisher Lifted");
                frontUiManager.changeFrontUiTitle("消火しよう");
                frontUiManager.changeFrontUiMainText("炎の根元を狙って\n消火しよう");
            }
        }
        
    }

    public bool IsLifted()
    {
        return isLifted;
    }
}