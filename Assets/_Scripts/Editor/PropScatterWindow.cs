using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verilen prefab'ları labirentin duvar diplerine dağıtır.
///
/// Neden duvar dibi? Koridorlar 3.2 m ve oyun bir kovalamaca — koridorun
/// ortasına konan her nesne kaçış yolunu daraltır ve takılmaya yol açar.
/// Duvara yaslanmış süs, koridoru dar göstermeden dolu gösteriyor.
///
/// Aynı seed aynı dağılımı verir; beğenmezsen seed'i değiştirip tekrar bas.
///
/// Menü: Yakalamaca > Harita Süsle (prop dağıt)
/// </summary>
public class PropScatterWindow : EditorWindow
{
    private const string GroupName = "Suslemeler";
    private const string MapName = "Harita";

    [SerializeField]
    [Tooltip("Dağıtılacak prefab'lar. Birden fazla verirsen rastgele seçilir.")]
    private GameObject[] prefabs = new GameObject[0];

    [SerializeField] private int count = 70;
    [SerializeField] private int seed = 1;

    [SerializeField]
    [Tooltip("İki süs arası en az mesafe (metre).")]
    private float minSpacing = 2.5f;

    [SerializeField]
    [Tooltip("Duvarla nesnenin sırtı arasındaki boşluk. Nesnenin kendi kalınlığı " +
        "otomatik ölçülüyor, bu sadece üstüne eklenen pay.")]
    private float wallOffset = 0.05f;

    [SerializeField]
    [Tooltip("Süs konduktan sonra koridorda kalması gereken en az geçiş genişliği (metre). " +
        "Oyuncu kapsülü 0.61 m ama kovalamacada saniyede 7.6 m'ye çıkıyorsun; " +
        "2 m'nin altı takılmaya davetiye.")]
    private float minCorridorWidth = 2.2f;

    [SerializeField]
    [Tooltip("Kapı ve eğilme geçitlerine bu kadar yaklaşmasın — geçişi tıkamasın.")]
    private float passageClearance = 3.5f;

    [SerializeField]
    [Tooltip("Duvara dik duruştan sapma (derece). Tam serbest dönüş yapılmıyor: " +
        "nesnenin sırtı duvara dönük kalmalı, yoksa raf gibi derin şeyler " +
        "koridora dik uzanıp yolu keser.")]
    [Range(0f, 45f)]
    private float yawJitter = 12f;

    [SerializeField]
    [Tooltip("Kitin kendi ölçeği. Prop'lar depo ölçeğinde çizildiyse buradan küçült. " +
        "Dağıtım sonrası konsolda her nesnenin ölçülen boyu yazıyor, ona bakarak ayarla.")]
    private float baseScale = 1f;

    [SerializeField] private Vector2 scaleRange = new Vector2(0.9f, 1.15f);

    [SerializeField]
    [Tooltip("Kapalıysa süslerin collider'ları silinir — hiçbir şekilde harekete engel olmazlar.")]
    private bool keepColliders = true;

    private SerializedObject serialized;

    [MenuItem("Yakalamaca/Harita Süsle (prop dağıt)")]
    private static void Open()
    {
        GetWindow<PropScatterWindow>("Harita Süsle").minSize = new Vector2(340f, 380f);
    }

    private void OnEnable()
    {
        serialized = new SerializedObject(this);
    }

    private void OnGUI()
    {
        serialized.Update();

        EditorGUILayout.LabelField("Dağıtılacak nesneler", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serialized.FindProperty("prefabs"), true);

        if (GUILayout.Button("SciFi Kit prop'larını yükle"))
            LoadKitProps();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Dağılım", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serialized.FindProperty("count"));
        EditorGUILayout.PropertyField(serialized.FindProperty("seed"));
        EditorGUILayout.PropertyField(serialized.FindProperty("minSpacing"));
        EditorGUILayout.PropertyField(serialized.FindProperty("wallOffset"));
        EditorGUILayout.PropertyField(serialized.FindProperty("passageClearance"));
        EditorGUILayout.PropertyField(serialized.FindProperty("minCorridorWidth"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Çeşitlilik", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serialized.FindProperty("yawJitter"));
        EditorGUILayout.PropertyField(serialized.FindProperty("baseScale"));
        EditorGUILayout.PropertyField(serialized.FindProperty("scaleRange"));
        EditorGUILayout.PropertyField(serialized.FindProperty("keepColliders"));

        serialized.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Süsler duvar diplerine yerleştirilir; koridorun ortası boş kalır. " +
            "Aynı seed aynı sonucu verir.", MessageType.Info);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!HasPrefabs()))
        {
            if (GUILayout.Button("Dağıt", GUILayout.Height(30f)))
                Scatter();
        }

        if (!HasPrefabs())
            EditorGUILayout.HelpBox("Önce en az bir prefab ekle.", MessageType.Warning);

        if (GUILayout.Button("Süsleri Temizle"))
            ClearGroup();
    }

    private bool HasPrefabs()
    {
        if (prefabs == null)
            return false;

        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] != null)
                return true;
        }

        return false;
    }

    private void Scatter()
    {
        GameObject map = GameObject.Find(MapName);
        if (map == null)
        {
            EditorUtility.DisplayDialog("Harita yok",
                "Önce Yakalamaca > Labirent Harita Kur çalıştır.", "Tamam");
            return;
        }

        ClearGroup();

        Transform group = new GameObject(GroupName).transform;
        group.SetParent(map.transform, false);
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Harita Süsle");

        float halfSpan = FindMapSpan(map) / 2f - 2f;
        List<Vector3> placed = new List<Vector3>();
        List<Vector3> passages = CollectPassagePositions(map);

        System.Random random = new System.Random(seed);
        Dictionary<string, Vector3> measured = new Dictionary<string, Vector3>();
        int attempts = 0;

        while (placed.Count < count && attempts < count * 60)
        {
            attempts++;

            Vector3 sample = new Vector3(
                (float)(random.NextDouble() * 2f - 1f) * halfSpan,
                0.6f,
                (float)(random.NextDouble() * 2f - 1f) * halfSpan);

            if (!TryFindWallSpot(sample, out Vector3 wallPoint, out Vector3 normal))
                continue;
            if (IsTooClose(placed, wallPoint, minSpacing))
                continue;
            if (IsTooClose(passages, wallPoint, passageClearance))
                continue;
            if (!PlaceProp(group, wallPoint, normal, random, measured))
                continue;

            placed.Add(wallPoint);
        }

        Selection.activeGameObject = group.gameObject;

        // Ölçüleri yazdırıyoruz: kit prop'ları depo ölçeğinde çizilmiş olabilir
        // ve bunu gözle kestirmek zor. Varil 2 metre çıkıyorsa baseScale'i
        // buradan görüp düşürürsün.
        string report = "";
        foreach (KeyValuePair<string, Vector3> pair in measured)
            report += $"\n  {pair.Key}: {pair.Value.x:0.00} × {pair.Value.y:0.00} × {pair.Value.z:0.00} m";

        Debug.Log($"{placed.Count} süs yerleştirildi ({attempts} deneme).\n" +
            $"Koridorda en az {minCorridorWidth:0.0} m geçiş bırakıldı; sığmayan yerler atlandı.\n" +
            $"Ölçülen boyutlar:{report}\n" +
            "Beğenmezsen seed'i değiştirip tekrar bas.");
    }

    /// <summary>Kitin zemine konabilen prop'larını listeye doldurur.</summary>
    private void LoadKitProps()
    {
        string[] names =
        {
            "Crates Barrels Pallets/Barrel",
            "Crates Barrels Pallets/Crate Long",
            "Crates Barrels Pallets/Crate Short",
            "Crates Barrels Pallets/Pallet",
            "Crates Barrels Pallets/Pallet Variations/Pallet Variation 02",
            "Crates Barrels Pallets/Pallet Variations/Pallet Variation 05",
            "Crates Barrels Pallets/Pallet Variations/Pallet Variation 08",
            "Misc Props/Garbage Bin",
            "Misc Props/Cart"
        };

        List<GameObject> found = new List<GameObject>();

        foreach (string name in names)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/SciFi Warehouse Kit/Prefabs/Props/{name}.prefab");

            if (prefab != null)
                found.Add(prefab);
        }

        if (found.Count == 0)
        {
            EditorUtility.DisplayDialog("Kit bulunamadı",
                "Assets/SciFi Warehouse Kit altında prop prefabları yok.", "Tamam");
            return;
        }

        prefabs = found.ToArray();
        serialized.Update();

        // Listede olmayanlar ve nedenleri:
        // - Raflar: koridorda çok yer kaplıyor, 3.2 m'lik geçitte fazla iri duruyor.
        // - Duvara monte olanlar (yangın söndürücü, kamera, lamba, boru, sigorta
        //   kutusu): bu araç zemine koyuyor, onların yeri duvarda ve elle konmalı.
        Debug.Log($"{found.Count} kit prop'u yüklendi (varil, kasa, palet, çöp kutusu, el arabası). " +
            "Raflar ve duvara monte olanlar bilerek dışarıda.");
    }

    /// <summary>
    /// Örnek noktadan dört yöne ışın atıp en yakın duvarı bulur, nesneyi
    /// sırtı duvara dönük olacak şekilde oraya yaslar.
    /// </summary>
    private bool TryFindWallSpot(Vector3 sample, out Vector3 wallPoint, out Vector3 normal)
    {
        wallPoint = default;
        normal = Vector3.forward;

        // Örnek nokta boşlukta mı? Duvarın içindeyse ışın atmanın anlamı yok.
        if (Physics.CheckSphere(sample, 0.5f, ~0, QueryTriggerInteraction.Ignore))
            return false;

        Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        float bestDistance = float.MaxValue;
        RaycastHit bestHit = default;
        bool found = false;

        for (int i = 0; i < directions.Length; i++)
        {
            if (!Physics.Raycast(sample, directions[i], out RaycastHit hit, 2.2f, ~0, QueryTriggerInteraction.Ignore))
                continue;

            // Sadece dikey yüzeyler; zemin veya tavan olmaz.
            if (Mathf.Abs(hit.normal.y) > 0.3f)
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            bestHit = hit;
            found = true;
        }

        if (!found)
            return false;

        wallPoint = new Vector3(bestHit.point.x, 0f, bestHit.point.z);
        normal = bestHit.normal;
        return true;
    }

    /// <summary>
    /// Nesneyi duvara yaslar. Konum önceden hesaplanmıyor: parça sahneye
    /// konup **gerçek sınırları ölçülüyor**, sırtı duvara ve tabanı zemine
    /// göre hizalanıyor. Pivotu nerede olursa olsun doğru oturuyor.
    ///
    /// Koridorda yeterli geçiş kalmıyorsa nesne siliniyor ve o nokta atlanıyor —
    /// kovalamacada takılmak, süsün güzelliğinden çok daha pahalı.
    /// </summary>
    private bool PlaceProp(Transform parent, Vector3 wallPoint, Vector3 normal,
        System.Random random, Dictionary<string, Vector3> measured)
    {
        GameObject prefab = PickPrefab(random);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

        if (instance == null)
            instance = Object.Instantiate(prefab, parent);

        // Sırtı duvara dönük; sapma dar tutuluyor ki derin nesneler koridora
        // dik uzanmasın. Tam serbest dönüş rafları yolun ortasına çeviriyordu.
        float jitter = (float)(random.NextDouble() * 2f - 1f) * yawJitter;
        instance.transform.rotation = Quaternion.LookRotation(normal, Vector3.up)
            * Quaternion.Euler(0f, jitter, 0f);

        float variation = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)random.NextDouble());
        instance.transform.localScale *= baseScale * variation;
        instance.transform.position = wallPoint;

        Bounds bounds = WorldBounds(instance);
        bool alongX = Mathf.Abs(normal.x) > 0.5f;
        float depth = alongX ? bounds.size.x : bounds.size.z;

        if (!measured.ContainsKey(prefab.name))
            measured[prefab.name] = bounds.size;

        // Karşı duvara kadar olan boşluk; nesne konunca geriye ne kalıyor?
        float corridor = Physics.Raycast(wallPoint + normal * 0.05f + Vector3.up * 0.6f,
            normal, out RaycastHit far, 12f, ~0, QueryTriggerInteraction.Ignore)
            ? far.distance
            : 12f;

        if (corridor - depth - wallOffset < minCorridorWidth)
        {
            Object.DestroyImmediate(instance);
            return false;
        }

        Vector3 target = wallPoint + normal * (depth * 0.5f + wallOffset);

        instance.transform.position += new Vector3(
            target.x - bounds.center.x,
            -bounds.min.y,
            target.z - bounds.center.z);

        if (!keepColliders)
        {
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(collider);
        }

        // Süsler hareket etmiyor: batching, occlusion ve lightmap'e dahil.
        // Prefab'ın alt objelerine de uygulanmalı, yoksa asıl mesh'ler
        // batching'e girmez (CLAUDE.md bölüm 3).
        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;

        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);

        // Süs katmanı: gövdeyi durdurur ama ışını kesmez. Variller duvar
        // diplerine dağılıyor ve tam da köşeye sıkışan kaçanın önünde
        // duruyorlardı; canavarın vuruşunu engellemeleri kalkan etkisi
        // yaratıyordu (CLAUDE.md teknik borç 3).
        LayerSetup.Apply(instance, LayerSetup.Sus);

        Undo.RegisterCreatedObjectUndo(instance, "Harita Süsle");
        return true;
    }

    /// <summary>Nesnenin sahnedeki gerçek sınırları — pivot nerede olursa olsun.</summary>
    private static Bounds WorldBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return new Bounds(instance.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private GameObject PickPrefab(System.Random random)
    {
        // Boş kayıtları atlayarak seç.
        List<GameObject> valid = new List<GameObject>();
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] != null)
                valid.Add(prefabs[i]);
        }

        return valid[random.Next(valid.Count)];
    }

    /// <summary>Kapı ve eğilme geçidi konumları — çevrelerine süs konmasın.</summary>
    private static List<Vector3> CollectPassagePositions(GameObject map)
    {
        List<Vector3> positions = new List<Vector3>();

        AddChildPositions(map.transform.Find("Kapilar"), positions);
        AddChildPositions(map.transform.Find("EgilmeGecitleri"), positions);

        return positions;
    }

    private static void AddChildPositions(Transform parent, List<Vector3> target)
    {
        if (parent == null)
            return;

        foreach (Transform child in parent)
            target.Add(child.position);
    }

    private static bool IsTooClose(List<Vector3> existing, Vector3 candidate, float minDistance)
    {
        float sqrMin = minDistance * minDistance;

        for (int i = 0; i < existing.Count; i++)
        {
            Vector3 delta = existing[i] - candidate;
            delta.y = 0f;

            if (delta.sqrMagnitude < sqrMin)
                return true;
        }

        return false;
    }

    private static float FindMapSpan(GameObject map)
    {
        Transform floor = map.transform.Find("Zemin");
        return floor != null ? floor.localScale.x : 54.4f;
    }

    private static void ClearGroup()
    {
        GameObject map = GameObject.Find(MapName);
        if (map == null)
            return;

        Transform existing = map.transform.Find(GroupName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);
    }
}
