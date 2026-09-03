using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ExtinguishingAgent : MonoBehaviour
{
    private ParticleSystem particleSystem;

    // Triggerに入ったParticleを保存
    private readonly List<ParticleSystem.Particle> enterParticles
        = new List<ParticleSystem.Particle>();

    private void Awake()
    {
        particleSystem = GetComponent<ParticleSystem>();
    }

    private void OnParticleTrigger()
    {
        // Triggerに入ったParticleと、そのParticleが触れたCollider情報を取得
        int count = particleSystem.GetTriggerParticles(
            ParticleSystemTriggerEventType.Enter,
            enterParticles,
            out ParticleSystem.ColliderData colliderData
        );

        for (int i = 0; i < count; i++)
        {
            // このParticleが接触したCollider数を取得
            int colliderCount = colliderData.GetColliderCount(i);

            for (int j = 0; j < colliderCount; j++)
            {
                // Particleが接触したColliderを取得
                Component hitCollider = colliderData.GetCollider(i, j);

                if (hitCollider == null)
                {
                    continue;
                }

                // Colliderが属している炎のHitBoxを取得
                FireHitBox fireHitBox =
                    hitCollider.GetComponentInParent<FireHitBox>();

                if (fireHitBox == null)
                {
                    continue;
                }

                // この炎に消火剤が1粒当たったことを通知
                fireHitBox.HitExtinguishingAgent(1);
            }
        }
    }
}