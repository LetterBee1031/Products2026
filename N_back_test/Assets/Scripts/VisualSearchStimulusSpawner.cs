using System.Collections.Generic;
using UnityEngine;

// 設定済みの静的Prefabを探索範囲に配置し、全刺激をまとめて表示・削除する。
public class VisualSearchStimulusSpawner : MonoBehaviour
{
    [Header("探索範囲・配置")]
    [SerializeField] private BoxCollider spawnArea; // 室内に収まる探索領域。Is Triggerを有効にする。
    [SerializeField] private Transform searchOrigin; // XR OriginのMain Camera。壁の向こう側への配置を防ぐ。
    [SerializeField, Min(0)] private float minObjectDistance = 0.3f; // 刺激中心間の最小距離（m）。
    [SerializeField, Min(1)] private int maxPlacementAttempts = 1000; // 1刺激につき許容する配置候補数。
    [SerializeField] private float stimulusHeight; // BoxCollider中心からのローカルY座標（m）。
    [SerializeField] private LayerMask obstacleLayers = ~0; // 壁・床等の物理Colliderを含むLayer。
    [Header("刺激Prefab（同じ大きさの静的Mesh）")]
    [SerializeField] private GameObject redSpherePrefab; // 赤いSphere。
    [SerializeField] private GameObject redCubePrefab; // 赤いCube。
    [SerializeField] private GameObject blueSpherePrefab; // 青いSphere。
    private GameObject stimulusRoot;
    public int GeneratedObjectCount { get; private set; }
    // 全座標を確保してから非表示で生成する。必要数を配置できなければfalseを返す。
    public bool TryPrepare(int setSize, bool targetPresent, System.Random random)
    {
        ClearStimuli();
        // 3種類を包む共通半径で、領域外へのはみ出しと刺激同士の重なりを防ぐ。
        float radius = Mathf.Max(PrefabRadius(redSpherePrefab),
            Mathf.Max(PrefabRadius(redCubePrefab), PrefabRadius(blueSpherePrefab)));
        
        Vector3 scale = spawnArea.transform.lossyScale;
        Vector3 half = spawnArea.size * 0.5f;
        // ワールド半径をローカル座標へ換算して、境界から余白を取る。
        Vector3 margin = new Vector3(radius / scale.x, radius / scale.y, radius / scale.z);
        if (half.x <= margin.x || half.z <= margin.z || Mathf.Abs(stimulusHeight) + margin.y > half.y)
        {
            return false;
        }
        var positions = new List<Vector3>(setSize);
        float separation = Mathf.Max(minObjectDistance, 2 * radius);
        Physics.SyncTransforms();
        for (int i = 0; i < setSize; i++)
        {
            bool found = false;
            // 配置不能でも無限ループにならないよう、1刺激あたりの抽選回数を制限する。
            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                // 探索面は領域のローカルXZ平面。高さは中心からのYオフセットで指定する。
                Vector3 local = spawnArea.center + new Vector3(
                    SignedRandom(random) * (half.x - margin.x), stimulusHeight,
                    SignedRandom(random) * (half.z - margin.z));
                Vector3 candidate = spawnArea.transform.TransformPoint(local);
                bool overlaps = positions.Exists(p => (p - candidate).sqrMagnitude < separation * separation);
                if (overlaps || Blocked(candidate, radius)) continue;
                positions.Add(candidate);
                found = true;
                break;
            }
            if (!found)
            {
                return false;
            }
        }
        // Presentは赤い球1個、Absentは0個。残りは赤いCubeと青いSphereに分ける。
        VisualSearchTrialGenerator.GetCounts(setSize, targetPresent, random,
            out int targets, out int cubes, out int spheres);
        var prefabs = new List<GameObject>(setSize);
        for (int i = 0; i < targets; i++) prefabs.Add(redSpherePrefab);
        for (int i = 0; i < cubes; i++) prefabs.Add(redCubePrefab);
        for (int i = 0; i < spheres; i++) prefabs.Add(blueSpherePrefab);
        // Targetの位置が固定されないよう、座標に割り当てるPrefabの順番をシャッフルする。
        for (int i = prefabs.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            GameObject temp = prefabs[i];
            prefabs[i] = prefabs[j];
            prefabs[j] = temp;
        }
        // 親が非表示なので、生成途中の刺激は見えない。Prefabは動かない設定にしておく。
        stimulusRoot = new GameObject("VisualSearchStimuli");
        stimulusRoot.SetActive(false);
        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject item = Instantiate(prefabs[i], positions[i], Quaternion.identity, stimulusRoot.transform);
            item.SetActive(true);
        }
        GeneratedObjectCount = prefabs.Count;
        return true;
    }
    // 障害物との接触と、視点から候補中心までの遮蔽を検査する。Triggerは除外する。
    private bool Blocked(Vector3 point, float radius)
    {
        foreach (var collider in Physics.OverlapSphere(point, radius, obstacleLayers, QueryTriggerInteraction.Ignore))
            if (collider != spawnArea) return true;
        Vector3 direction = point - searchOrigin.position;
        foreach (var hit in Physics.RaycastAll(searchOrigin.position, direction.normalized,
            direction.magnitude, obstacleLayers, QueryTriggerInteraction.Ignore))
            if (hit.collider != spawnArea) return true;
        return false;
    }
    // Meshの中心ずれとScaleを含め、Prefab原点から全体を包む半径を計算する。
    private static float PrefabRadius(GameObject prefab)
    {
        Bounds bounds = prefab.GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 extent = bounds.extents;
        Vector3 center = bounds.center;
        Vector3 farthest = new Vector3(Mathf.Abs(center.x) + extent.x,
            Mathf.Abs(center.y) + extent.y, Mathf.Abs(center.z) + extent.z);
        return Vector3.Scale(farthest, prefab.transform.localScale).magnitude;
    }
    private static float SignedRandom(System.Random random) => (float)(random.NextDouble() * 2 - 1);
    // 全刺激を親ごと有効化する。反応時間はManagerで計測する。
    public void ShowStimuli()
    {
        if (stimulusRoot != null) stimulusRoot.SetActive(true);
    }
    public void ClearStimuli()
    {
        if (stimulusRoot != null)
        {
            // Destroyは遅延するため、先に表示と衝突を止める。
            stimulusRoot.SetActive(false);
            Destroy(stimulusRoot);
            stimulusRoot = null;
        }
        GeneratedObjectCount = 0;
    }
    private void OnDisable() => ClearStimuli();
}
