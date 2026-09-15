using UnityEngine;

public class FireHitBox : MonoBehaviour
{
    [SerializeField] private float fireHealth = 0.1f;          // 炎体力: 炎の残り体力のイメージ．炎の大きさ変更・消火判定等に使用
    [SerializeField] private float extinguishPower = 0.010f;   // 消化力: 消火剤パーティクルが1個炎にあたるたびに，どの程度，炎体力を削るかのパラメータ
    [SerializeField] private float fireGrowPower = 0.005f;     // 炎成長力: 単位時間当たりに炎が成長する速度を決定するパラメータ
    [SerializeField] private float gravityCoeff = -0.2f;       // 重力係数: 炎のパーティクルにかかる重力の係数．炎の上昇速度を決定している
    public GameObject[] fireParticlesObjects = new GameObject[4];
    public ParticleSystem.MainModule[] fireParticlesMain = new ParticleSystem.MainModule[4]; // 

    // 一度消火したら保持し、再有効化されても自然成長を再開させない。
    private bool extinguished;
    // 最後に消火剤が接触したフレーム。接触前はフレーム番号と一致しない値にする。
    private int lastContactFrame = -1;

    private void Start()
    {
        // Inspectorに登録された炎の数に合わせて、操作用の配列を確保する。
        fireParticlesMain = new ParticleSystem.MainModule[fireParticlesObjects.Length];
        for (int i = 0; i < fireParticlesObjects.Length; i++)
        {
            fireParticlesMain[i] = fireParticlesObjects[i].GetComponent<ParticleSystem>().main;
        }

        // 初期体力を見た目に反映し、開始時から0以下なら消火済みにする。
        fireHealth = Mathf.Max(0f, fireHealth);
        UpdateFireAppearance();
        if (fireHealth <= 0f)
        {
            Extinguish();
        }
    }

    private void LateUpdate()
    {
        // 粒子の接触通知を受けてから、そのフレームの成長を判定する。
        if (extinguished || lastContactFrame == Time.frameCount)
        {
            return;
        }

        // fireGrowPowerを毎秒の成長量として加算し、フレームレートによる差を抑える。
        // 負の係数によって自然に体力が減ることは防ぐ。
        fireHealth += Mathf.Max(0f, fireGrowPower) * Time.deltaTime;
        UpdateFireAppearance();
    }

    // 進入時・内部滞在中の両方から呼び、このフレームの自然成長を止める。
    // 接触通知だけでは体力を減らさず、消火量の計算はHitExtinguishingAgentで行う。
    public void NotifyExtinguishingAgentContact()
    {
        lastContactFrame = Time.frameCount;
    }
    // 消火剤が当たった数に応じて炎を弱くする
    public void HitExtinguishingAgent(int hitCount)
    {
        // 消火済みの炎への追加処理と、無効な命中数による更新を防ぐ。
        if (extinguished || hitCount <= 0)
        {
            return;
        }

        // 命中したフレームは成長を止め、消火で体力が負にならないよう0で制限する。
        NotifyExtinguishingAgentContact();
        fireHealth = Mathf.Max(0f, fireHealth - hitCount * extinguishPower);
        UpdateFireAppearance();

        Debug.Log(gameObject.name + " Hit : " + hitCount);
        Debug.Log("Fire Health : " + fireHealth);

        if (fireHealth <= 0.0f)
        {
            Extinguish();
        }
        
    }

    // 初期化・成長・消火で共通の見た目更新を使い、体力に大きさと重力係数を連動させる。
    private void UpdateFireAppearance()
    {
        for (int i = 0; i < fireParticlesObjects.Length; i++)
        {
            fireParticlesObjects[i].transform.localScale = Vector3.one * fireHealth;
            fireParticlesMain[i].gravityModifier = gravityCoeff * fireHealth;
        }
    }

    private void Extinguish()
    {
        // オブジェクトを無効化する前に、以後の成長・消火処理を禁止する。
        extinguished = true;
        Debug.Log(gameObject.name + " 消火完了");

        // 仮処理として炎全体を非表示
        transform.parent.gameObject.SetActive(false);
    }
}
