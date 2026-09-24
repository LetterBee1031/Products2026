using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class CeilingFireController : MonoBehaviour
{
    [SerializeField] private ParticleSystem ceilingSpreadFire;
    [SerializeField] private Transform fireOrigin;

    [Header("Spread Settings")]
    [SerializeField] private float spreadSpeed = 2.0f;
    [SerializeField] private float surfaceOffset = 0.02f;
    [SerializeField] private int particlesPerCollision = 1;

    [Header("Size Settings")]
    // この速度以下なら最小サイズ
    [SerializeField] private float minCollisionSpeed = 1.0f;
    // この速度以上なら最大サイズ
    [SerializeField] private float maxCollisionSpeed = 5.0f;
    // 天井炎の最小サイズ
    [SerializeField] private float minCeilingSize = 0.3f;
    // 天井炎の最大サイズ
    [SerializeField] private float maxCeilingSize = 1.2f;

    private ParticleSystem sourceParticleSystem;
    private readonly List<ParticleCollisionEvent> collisionEvents =
        new List<ParticleCollisionEvent>();

    private void Awake()
    {
        sourceParticleSystem = GetComponent<ParticleSystem>();

        // 指定がなければ、このParticleSystemの位置を炎の中心にする
        if (fireOrigin == null)
        {
            fireOrigin = transform;
        }
    }

    // Particleが天井などのColliderに衝突したときに呼ばれる
    private void OnParticleCollision(GameObject other)
    {
        // 今回発生したParticleの衝突情報を取得
        int count = sourceParticleSystem.GetCollisionEvents(other, collisionEvents);

        for (int i = 0; i < count; i++)
        {
            ParticleCollisionEvent collision = collisionEvents[i];

            // 衝突位置と衝突面の法線
            Vector3 hitPoint = collision.intersection;
            Vector3 normal = collision.normal;

            // 炎の中心から衝突位置へ向かう方向
            Vector3 radialDirection = hitPoint - fireOrigin.position;

            // 天井面に沿った方向へ変換
            Vector3 spreadDirection =
                Vector3.ProjectOnPlane(radialDirection, normal);

            // 真上付近に当たって方向がほぼ0なら処理しない
            if (spreadDirection.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            spreadDirection.Normalize();

            // 衝突したParticleの速度の大きさを取得
            float collisionSpeed = collision.velocity.magnitude;

            // 衝突速度から天井炎のサイズを計算
            float ceilingSize = CalculateCeilingSize(collisionSpeed);

            // 天井炎を生成
            EmitCeilingParticle(
                hitPoint,
                normal,
                spreadDirection,
                ceilingSize
            );
        }
    }

    // 衝突速度から天井炎のサイズを決定
    private float CalculateCeilingSize(float collisionSpeed)
    {
        // minCollisionSpeed～maxCollisionSpeedを0～1に変換
        float t = Mathf.InverseLerp(
            minCollisionSpeed,
            maxCollisionSpeed,
            collisionSpeed
        );

        // 0～1をminCeilingSize～maxCeilingSizeに変換
        return Mathf.Lerp(
            minCeilingSize,
            maxCeilingSize,
            t
        );
    }

    // 衝突地点から天井炎を生成
    private void EmitCeilingParticle(
        Vector3 hitPoint,
        Vector3 normal,
        Vector3 direction,
        float ceilingSize
    )
    {
        for (int i = 0; i < particlesPerCollision; i++)
        {
            ParticleSystem.EmitParams emitParams =
                new ParticleSystem.EmitParams();

            // 天井へのめり込み防止
            emitParams.position =
                hitPoint + normal * surfaceOffset;

            // 天井に沿って外側へ移動
            emitParams.velocity =
                direction * spreadSpeed;

            // 衝突速度から求めたサイズを設定
            emitParams.startSize =
                ceilingSize;

            ceilingSpreadFire.Emit(
                emitParams,
                1
            );
        }
    }
}