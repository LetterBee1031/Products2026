// using System.Collections.Generic;
// using UnityEngine;

// public class FireHitBox : MonoBehaviour
// {
//     [SerializeField] private float fireHealth = 1.0f;
//     [SerializeField] private float extinguishPower = 0.005f;
//     [SerializeField] private float gravityCoeff = -0.2f;
//     public GameObject[] fireParticlesObjects = new GameObject[4];
//     public ParticleSystem.MainModule[] fireParticlesMain = new ParticleSystem.MainModule[4];


//     private readonly List<ParticleCollisionEvent> collisionEvents
//         = new List<ParticleCollisionEvent>();

//     private void Start()
//     {
//         for (int i = 0; i < fireParticlesObjects.Length; i++)
//         {
//             fireParticlesMain[i] = fireParticlesObjects[i].GetComponent<ParticleSystem>().main;
//         }
//     }

//     private void OnParticleCollision(GameObject other)
//     {
//         if (!other.CompareTag("ExtinguishingAgent"))
//         {
//             return;
//         }

//         // 衝突してきたParticleSystemを取得
//         ParticleSystem particleSystem = other.GetComponent<ParticleSystem>();

//         if (particleSystem == null)
//         {
//             return;
//         }

//         // このHitBoxに当たったParticleの衝突情報を取得
//         int collisionCount = particleSystem.GetCollisionEvents(
//             gameObject,
//             collisionEvents
//         );

//         // 当たったParticle数に応じて炎を弱くする
//         fireHealth -= extinguishPower * collisionCount;
//         for(int i = 0; i < fireParticlesObjects.Length; i++)
//         {
//             fireParticlesObjects[i].transform.localScale = Vector3.one * fireHealth;
//             fireParticlesMain[i].gravityModifier = gravityCoeff * fireHealth;
//         }

//         Debug.Log(
//             "Hit : " + collisionCount +
//             " / Fire Health : " + fireHealth
//         );

//         if (fireHealth <= 0.0f)
//         {
//             Extinguish();
//         }
//     }

//     private void Extinguish()
//     {
//         Debug.Log("消火完了");
//         transform.parent.gameObject.SetActive(false);
//     }
// }



using UnityEngine;

public class FireHitBox : MonoBehaviour
{
    [SerializeField] private float fireHealth = 1.0f;
    [SerializeField] private float extinguishPower = 0.005f;
    [SerializeField] private float gravityCoeff = -0.2f;
    public GameObject[] fireParticlesObjects = new GameObject[4];
    public ParticleSystem.MainModule[] fireParticlesMain = new ParticleSystem.MainModule[4];

    private void Start()
    {
        for (int i = 0; i < fireParticlesObjects.Length; i++)
        {
            fireParticlesMain[i] = fireParticlesObjects[i].GetComponent<ParticleSystem>().main;
        }
    }
    // 消火剤が当たった数に応じて炎を弱くする
    public void HitExtinguishingAgent(int hitCount)
    {
        fireHealth -= hitCount * extinguishPower;
        for(int i = 0; i < fireParticlesObjects.Length; i++)
        {
            fireParticlesObjects[i].transform.localScale = Vector3.one * fireHealth;
            fireParticlesMain[i].gravityModifier = gravityCoeff * fireHealth;
        }

        Debug.Log(gameObject.name + " Hit : " + hitCount);
        Debug.Log("Fire Health : " + fireHealth);

        if (fireHealth <= 0.0f)
        {
            Extinguish();
        }
        
    }

    private void Extinguish()
    {
        Debug.Log(gameObject.name + " 消火完了");

        // 仮処理として炎全体を非表示
        transform.parent.gameObject.SetActive(false);
    }
}