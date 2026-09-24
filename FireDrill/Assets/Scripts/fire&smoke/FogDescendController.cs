using UnityEngine;

public class FogDescendController : MonoBehaviour
{
    [SerializeField] private GameObject fogParticle;
    private ParticleSystem fogParticleSystem;

    [Header("Fog Area")]
    // 部屋の横幅・奥行き
    [SerializeField] private float areaSizeX = 8.0f;
    [SerializeField] private float areaSizeZ = 8.0f;

    // 天井の高さ
    [SerializeField] private float ceilingHeight = 3.0f;

    // 最初の煙層の厚さ
    [SerializeField] private float startThickness = 0.3f;

    // 最終的な煙の下端の高さ
    [SerializeField] private float endBottomHeight = 2.0f;

    // 何秒かけて煙を下降させるか
    [SerializeField] private float descendTime = 60.0f;

    private float elapsedTime;

    private void Start()
    {
        fogParticle.SetActive(true);
        fogParticleSystem = fogParticle.GetComponent<ParticleSystem>();
        // 最初の煙範囲を設定
        UpdateFogArea(0.0f);
    }

    private void Update()
    {
        elapsedTime += Time.deltaTime;

        // 0～1に正規化
        float t = Mathf.Clamp01(elapsedTime / descendTime);

        // 煙の範囲を更新
        UpdateFogArea(t);
    }

    private void UpdateFogArea(float t)
    {
        var shape = fogParticleSystem.shape;

        // 最初の煙下端
        float startBottomHeight = ceilingHeight - startThickness;

        // 煙の下端を徐々に下げる
        float currentBottomHeight =Mathf.Lerp(startBottomHeight, endBottomHeight, t);

        // 天井から現在の煙下端までの厚さ
        float currentThickness = ceilingHeight - currentBottomHeight;

        // Boxの中心位置
        float centerY = ceilingHeight - currentThickness * 0.5f;

        // 煙の発生範囲を設定
        shape.position = new Vector3(0.0f,centerY,0.0f);

        shape.scale = new Vector3(areaSizeX, currentThickness,areaSizeZ);
    }
}