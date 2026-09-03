using System.Collections.Generic;
using UnityEngine;

// このGameObjectにParticleSystemが必要
[RequireComponent(typeof(ParticleSystem))]
public class CeilingFireController : MonoBehaviour
{
    [Header("Ceiling spread particle")]
    // 天井に沿って広がる炎用ParticleSystem
    [SerializeField] private ParticleSystem ceilingSpreadFire;

    [Header("Fire origin")]
    // 炎が広がる中心位置
    [SerializeField] private Transform fireOrigin;

    [Header("Spread settings")]
    // 天井に沿って移動するParticleの速度
    [SerializeField] private float spreadSpeed = 2.0f;
    // 天井面から少し離して生成するための値
    [SerializeField] private float surfaceOffset = 0.02f;
    // 1回の衝突で生成するParticle数
    [SerializeField] private int particlesPerCollision = 1;

    // 上昇する炎側のParticleSystem
    private ParticleSystem sourceParticleSystem;
    // Particleの衝突情報を保存
    private readonly List<ParticleCollisionEvent> collisionEvents = new List<ParticleCollisionEvent>();

    private void Awake()
    {
        // このGameObjectについているParticleSystemを取得
        sourceParticleSystem = GetComponent<ParticleSystem>();

        // 指定がなければ、このParticleSystemの位置を炎の中心にする
        if (fireOrigin == null)
        {
            fireOrigin = transform;
        }
    }

    // ParticleがColliderに衝突したときに呼ばれる
    // Collision ModuleのSend Collision MessagesをONにする
    private void OnParticleCollision(GameObject other)
    {
        // 今回発生したParticleの衝突情報を取得
        int count = sourceParticleSystem.GetCollisionEvents(other, collisionEvents);

        for (int i = 0; i < count; i++)
        {
            ParticleCollisionEvent collision = collisionEvents[i];

            // 衝突位置と天井面の法線
            Vector3 hitPoint = collision.intersection;
            Vector3 normal = collision.normal;

            // 炎の中心から衝突位置へ向かう方向
            Vector3 radialDirection = hitPoint - fireOrigin.position;

            // 上下方向の成分を除去して、天井面に沿った方向へ変換
            Vector3 spreadDirection = Vector3.ProjectOnPlane(radialDirection, normal);

            // 真上付近に衝突して方向がほぼ0なら処理しない
            if (spreadDirection.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            // 方向ベクトルとして正規化
            spreadDirection.Normalize();

            // 衝突地点から天井用Particleを生成
            EmitCeilingParticle(hitPoint, normal, spreadDirection);
        }
    }

    // 衝突地点に天井用Particleを生成
    private void EmitCeilingParticle(Vector3 hitPoint, Vector3 normal, Vector3 direction)
    {
        for (int i = 0; i < particlesPerCollision; i++)
        {
            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();

            // Colliderへのめり込みを防ぐため少しずらす
            emitParams.position = hitPoint + normal * surfaceOffset;

            // 天井に沿って外側へ移動する速度を設定
            emitParams.velocity = direction * spreadSpeed;

            // Particleを1個生成
            ceilingSpreadFire.Emit(emitParams, 1);
        }
    }
}