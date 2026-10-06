using UnityEngine;
using UnityEngine.InputSystem;

public class FirePlacementController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private XRInputReader inputReader;     // 既存のXRInputReader

    [Header("Ray")]
    [SerializeField] private Transform pointerOrigin;       // Rayを発射する位置と向き，基本的には左コントローラー、またはポインタ用Transformを指定
    [SerializeField] private LayerMask floorLayer;          // 炎を配置できる床のLayer
    [SerializeField] private float maxDistance = 20.0f;     // Rayを飛ばす最大距離

    [Header("Extinguisher")]
    [SerializeField] private ParticleSystem extinguishingParticle;  // 消火剤のパーティクル

    [Header("Fire")]
    [SerializeField] private GameObject firePrefab;         // 配置する炎のPrefab
    [SerializeField] private float surfaceOffset = 0.01f;   // 床と完全に同じ位置に生成するとめり込む場合があるため、床の法線方向に少しだけ浮かせる

    [Header("Preview")]
    [SerializeField] private GameObject placementMarker;    // 炎を配置できる位置に表示するマーカー



    private Vector3 currentHitPoint;    // 現在Rayが当たっている床の位置
    private Vector3 currentHitNormal;   // 現在Rayが当たっている床面の法線．通常の水平な床ならほぼVector3.upになる
    private bool canPlaceFire = false;  // 現在炎を配置可能な状態かどうか
    private bool initialized = false;   // InputActionのイベント登録が完了したかを記録する

    private void Start()
    {
        // Pointer Originが設定されていない場合は、このスクリプトが付いているGameObject自身を使用する
        if (pointerOrigin == null)
            pointerOrigin = transform;

        // 開始時は配置位置マーカーを非表示にしておく
        if (placementMarker != null)
            placementMarker.SetActive(false);

        // Aボタンが押されたときにOnPlaceFireを呼び出す
        inputReader.buttonX.performed += OnPlaceFire;

        initialized = true;
    }

    private void Update()
    {
        // 毎フレームRayを飛ばして、現在コントローラーが指している床位置を更新する
        UpdatePointer();
    }

    private void OnDestroy()
    {
        // 登録したInputActionイベントを解除する
        // 解除しないと、オブジェクト破棄後にイベントが残る可能性がある
        if (!initialized || inputReader == null)
            return;

        inputReader.buttonA.performed -= OnPlaceFire;
    }

    private void UpdatePointer()
    {
        // Pointer Originの位置からforward方向へRayを作成
        Ray ray = new Ray(
            pointerOrigin.position,
            pointerOrigin.forward
        );

        // Floor Layerに属するColliderだけを対象にRaycastする
        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            maxDistance,
            floorLayer,
            QueryTriggerInteraction.Ignore))
        {
            // 床にRayが当たったので炎を配置可能
            canPlaceFire = true;

            // Rayが当たった位置を保存
            currentHitPoint = hit.point;

            // 当たった面の法線を保存
            currentHitNormal = hit.normal;

            // 配置位置確認用のマーカーが設定されている場合
            if (placementMarker != null)
            {
                placementMarker.SetActive(true);

                // 床面より少しだけ浮かせて表示する
                placementMarker.transform.position = currentHitPoint + currentHitNormal * surfaceOffset;

                // マーカーの上方向を床面の法線方向に合わせる
                // 傾いた床でも床面に沿うようになる
                placementMarker.transform.up = currentHitNormal;
            }
        }
        else
        {
            // 床にRayが当たっていない場合は配置不可
            canPlaceFire = false;

            // 配置マーカーも非表示にする
            if (placementMarker != null)
                placementMarker.SetActive(false);
        }
    }

    private void OnPlaceFire(InputAction.CallbackContext context)
    {
        // 床を指していない場合は何もしない
        if (!canPlaceFire)
            return;

        // 現在指している床位置に炎を生成する
        SpawnFire();
    }

    private void SpawnFire()
    {
        // 炎Prefabが設定されていなければ処理しない
        if (firePrefab == null)
            return;

        // 床に少しめり込まないよう、
        // 法線方向へsurfaceOffset分だけ移動した位置を生成位置にする
        Vector3 spawnPosition = currentHitPoint + currentHitNormal * surfaceOffset;

        // 炎Prefabの上方向(Vector3.up)を、床面の法線方向へ合わせる
        // 水平な床ならほぼ回転なし，傾斜した床なら床に合わせて炎も傾く
        Quaternion spawnRotation = Quaternion.FromToRotation(
                Vector3.up,
                currentHitNormal
            );

        // 炎Prefabを生成
        GameObject newFire = Instantiate(firePrefab, spawnPosition, spawnRotation);
        Collider fireHitBox = newFire.GetComponentInChildren<Collider>();

        if (fireHitBox != null && extinguishingParticle != null)
        {
            // 消火剤ParticleのTrigger Moduleへ追加
            ParticleSystem.TriggerModule trigger =extinguishingParticle.trigger;

            trigger.AddCollider(fireHitBox);

            Debug.Log($"FireHitBoxを登録しました: {fireHitBox.name}");
        }

        Debug.Log("炎を配置しました");
    }
}