// using UnityEngine;

// public class HoseTargetController : MonoBehaviour
// {
//     [Header("Hose")]
//     // ホース根元
//     [SerializeField] private Transform hoseRoot;

//     // 左手など、ホース先端を合わせたい位置
//     [SerializeField] private Transform nozzleTarget;

//     [Header("IK Targets")]
//     [SerializeField] private Transform target1;
//     [SerializeField] private Transform target2;
//     [SerializeField] private Transform target3;

//     [Header("Sag")]
//     // ホース途中の垂れ量
//     [SerializeField] private float sag1 = 0.15f;
//     [SerializeField] private float sag2 = 0.25f;

//     private void LateUpdate()
//     {
//         Vector3 start = hoseRoot.position;
//         Vector3 end = nozzleTarget.position;

//         // 根元～先端の約1/3地点
//         target1.position =
//             Vector3.Lerp(start, end, 0.33f)
//             + Vector3.down * sag1;

//         // 根元～先端の約2/3地点
//         target2.position =
//             Vector3.Lerp(start, end, 0.66f)
//             + Vector3.down * sag2;

//         // 最終Targetはノズル位置に合わせる
//         target3.position = end;

//         // 最終Targetの向きもノズルに合わせる
//         target3.rotation = nozzleTarget.rotation;
//     }
// }


using UnityEngine;

public class HoseTargetController : MonoBehaviour
{
    // 左手やノズルの追従先
    [SerializeField] private Transform nozzleTarget;

    // ホース終端のPhysics用Rigidbody
    [SerializeField] private Rigidbody hoseTargetRigidbody;

    private void FixedUpdate()
    {
        // Physics更新に合わせてTarget3を移動
        hoseTargetRigidbody.MovePosition(nozzleTarget.position);

        // 必要なら向きも追従
        hoseTargetRigidbody.MoveRotation(nozzleTarget.rotation);
    }
}