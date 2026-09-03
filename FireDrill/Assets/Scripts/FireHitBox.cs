using System.Collections.Generic;
using UnityEngine;

public class FireHitBox : MonoBehaviour
{
    [SerializeField] private float fireHealth = 1.0f;
    [SerializeField] private float extinguishPower = 0.005f;

    private readonly List<ParticleCollisionEvent> collisionEvents
        = new List<ParticleCollisionEvent>();

    private void OnParticleCollision(GameObject other)
    {
        if (!other.CompareTag("ExtinguishingAgent"))
        {
            return;
        }

        // 衝突してきたParticleSystemを取得
        ParticleSystem particleSystem = other.GetComponent<ParticleSystem>();

        if (particleSystem == null)
        {
            return;
        }

        // このHitBoxに当たったParticleの衝突情報を取得
        int collisionCount = particleSystem.GetCollisionEvents(
            gameObject,
            collisionEvents
        );

        // 当たったParticle数に応じて炎を弱くする
        fireHealth -= extinguishPower * collisionCount;

        Debug.Log(
            "Hit : " + collisionCount +
            " / Fire Health : " + fireHealth
        );

        if (fireHealth <= 0.0f)
        {
            Extinguish();
        }
    }

    private void Extinguish()
    {
        Debug.Log("消火完了");
        transform.parent.gameObject.SetActive(false);
    }
}