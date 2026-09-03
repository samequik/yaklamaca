using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Üretilen labirenti hazır modüllerle giydirir: duvar panelleri, zemin ve
/// tavan karoları.
///
/// **Labirenti yeniden kurmuyor, üstünü kaplıyor.** Küpler yerinde kalıyor;
/// sadece MeshRenderer'ları kapanıyor, çarpışma kutuları duruyor. Böylece
/// flood fill ile doğrulanmış bağlantılılık, doğum noktaları, kapılar, eğilme
/// geçitleri ve bıçağın görüş hattı kontrolü aynen çalışmaya devam ediyor.
/// Giydirmeyi kaldırınca harita eski haline dönüyor.
///
/// Ölçek: kit 6 m'lik ızgaraya, 9 m duvar yüksekliğine göre çizilmiş; labirent
/// ise 3.2 m hücre ve 3 m duvar kullanıyor. Paneller **eşit oranda** (0.53)
/// küçültülüyor — eksenleri ayrı ayrı sıkıştırmak dokuları ezerdi. Sonuçta
/// duvar 4.8 m oluyor ama tavan 3 m'de olduğu için üstünü zaten görmüyorsun.
///
/// Modüllerin pivotu ve yönü tahmin edilmiyor: her parça sahneye konup gerçek
/// renderer sınırları ölçülüyor, hizalama ona göre yapılıyor. Kit değişse bile
/// çalışır.
///
/// Menü: Yakalamaca > Haritayı Giydir (SciFi Kit)
/// </summary>
public class MapDressWindow : EditorWindow
{
    private const string MapName = "Harita";
    private const string GroupName = "Giydirme";
    private const string KitRoot = "Assets/SciFi Warehouse Kit/Prefabs/Structures";

    private const string KitMaterials = "Assets/SciFi Warehouse Kit/Art/Materials";

    /// <summary>MazeMapBuilder'ın geçitlere verdiği özgün materyal — geri alırken lazım.</summary>
    private const string CrouchOriginalMaterial = "Assets/_Art/Materials/Harita_Gecit.mat";

    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject ceilingPrefab;
    [SerializeField] private GameObject doorPrefab;
    [SerializeField] private GameObject lampPrefab;
    [SerializeField] private Material crouchMaterial;

    [SerializeField] private bool dressWalls = true;
    [SerializeField] private bool dressFloor = true;
    [SerializeField] private bool dressCeiling = true;
    [SerializeField] private bool dressDoors = true;
    [SerializeField] private bool dressLamps = true;
    [SerializeField] private bool dressCrouch = true;

    [Tooltip("Lamba gövdesinin eni (metre). Kit deposu için çizildi, koridora göre küçültülüyor.")]
    [SerializeField] private float lampWidth = 0.8f;

    [SerializeField] private bool flipWallPanels;

    private Vector2 scroll;

    [MenuItem("Yakalamaca/Haritayı Giydir (SciFi Kit)")]
    private static void Open()
    {
        MapDressWindow window = GetWindow<MapDressWindow>(true, "Haritayı Giydir");
        window.minSize = new Vector2(430f, 330f);
    }

    private void OnEnable()
    {
        wallPrefab = wallPrefab != null ? wallPrefab : Load("Walls/Wall Plain");
        floorPrefab = floorPrefab != null ? floorPrefab : Load("Floor/Floor Tile 01");
        ceilingPrefab = ceilingPrefab != null ? ceilingPrefab : Load("Ceiling/Ceiling Closed");

        // Kapılar garaj kapısı gibi tavandan iniyor; kitin bay door'u birebir o iş.
        doorPrefab = doorPrefab != null ? doorPrefab : Load("Walls/Wall BayDoor");

        lampPrefab = lampPrefab != null
            ? lampPrefab
            : AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/SciFi Warehouse Kit/Prefabs/Props/Misc Props/Hanging Light.prefab");

        // Geçitler kamufle olmamalı: koridor duvarından farklı, bakım kanalı
        // hissi veren bir yüzey.
        crouchMaterial = crouchMaterial != null
            ? crouchMaterial
            : AssetDatabase.LoadAssetAtPath<Material>($"{KitMaterials}/Ducts Pillars Mat.mat");
    }

    private static GameObject Load(string relativePath)
        => AssetDatabase.LoadAssetAtPath<GameObject>($"{KitRoot}/{relativePath}.prefab");

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "Küpleri silmez, üstünü kaplar. Çarpışma ve labirent mantığı olduğu gibi kalır.\n" +
            "Beğenmezsen 'Giydirmeyi Kaldır' ile geri alırsın.",
            MessageType.Info);

        EditorGUILayout.Space();

        wallPrefab = (GameObject)EditorGUILayout.ObjectField("Duvar paneli", wallPrefab, typeof(GameObject), false);
        floorPrefab = (GameObject)EditorGUILayout.ObjectField("Zemin karosu", floorPrefab, typeof(GameObject), false);
        ceilingPrefab = (GameObject)EditorGUILayout.ObjectField("Tavan karosu", ceilingPrefab, typeof(GameObject), false);
        doorPrefab = (GameObject)EditorGUILayout.ObjectField("Kapı gövdesi", doorPrefab, typeof(GameObject), false);
        lampPrefab = (GameObject)EditorGUILayout.ObjectField("Lamba gövdesi", lampPrefab, typeof(GameObject), false);
        crouchMaterial = (Material)EditorGUILayout.ObjectField(
            new GUIContent("Geçit yüzeyi", "Eğilme geçitleri koridor duvarından ayrışmalı — " +
                "kamufle olurlarsa kestirme yol olmaktan çıkarlar."),
            crouchMaterial, typeof(Material), false);

        EditorGUILayout.Space();

        dressWalls = EditorGUILayout.Toggle("Duvarları giydir", dressWalls);
        dressFloor = EditorGUILayout.Toggle("Zemini giydir", dressFloor);
        dressCeiling = EditorGUILayout.Toggle("Tavanı giydir", dressCeiling);
        dressDoors = EditorGUILayout.Toggle("Kapıları giydir", dressDoors);
        dressLamps = EditorGUILayout.Toggle("Lambaları giydir", dressLamps);
        dressCrouch = EditorGUILayout.Toggle("Geçitleri işaretle", dressCrouch);

        if (dressLamps)
            lampWidth = EditorGUILayout.Slider("Lamba eni (m)", lampWidth, 0.3f, 2.5f);

        EditorGUILayout.Space();

        flipWallPanels = EditorGUILayout.Toggle(
            new GUIContent("Panelleri ters çevir",
                "Duvarlar içi boş / ters görünüyorsa işaretle. Modelin hangi yüzünün " +
                "kaplı olduğu kite göre değişiyor."),
            flipWallPanels);

        EditorGUILayout.Space();

        if (GUILayout.Button("Giydir", GUILayout.Height(30f)))
            Dress();

        if (GUILayout.Button("Giydirmeyi Kaldır"))
            Strip();

        EditorGUILayout.EndScrollView();
    }

    // ---------- Giydirme ----------

    private void Dress()
    {
        GameObject map = GameObject.Find(MapName);
        if (map == null)
        {
            EditorUtility.DisplayDialog("Harita yok",
                "Önce Yakalamaca > Labirent Harita Kur çalıştır.", "Tamam");
            return;
        }

        Transform walls = map.transform.Find("Duvarlar");
        if (walls == null)
        {
            EditorUtility.DisplayDialog("Duvarlar yok",
                "Haritada 'Duvarlar' grubu bulunamadı.", "Tamam");
            return;
        }

        // Hücre indeksleri obje adlarında zaten duruyor (Duvar_x_z). Labirenti
        // yeniden üretmektense oradan okumak, sahnede elle yapılmış
        // düzenlemelerin de korunması demek.
        Dictionary<Vector2Int, Transform> wallCells = ReadWallCells(walls, out int gridSize);
        if (wallCells.Count == 0)
        {
            EditorUtility.DisplayDialog("Duvar bulunamadı",
                "'Duvar_x_z' adında blok yok. Harita elle mi değiştirildi?", "Tamam");
            return;
        }

        // Hücre boyu ve duvar yüksekliği sabit olarak yazılmıyor: bloklardan
        // okunuyor, böylece MazeMapBuilder'daki değerler değişirse burası da
        // kendiliğinden uyuyor.
        Transform sample = First(wallCells);
        float cellSize = sample.localScale.x;
        float wallHeight = sample.localScale.y;

        ReplaceGroup(map.transform, out Transform group);

        int placed = 0;

        if (dressWalls && wallPrefab != null)
            placed += DressWalls(group, wallCells, gridSize, cellSize);

        if (dressFloor && floorPrefab != null)
            placed += DressTiles(group, wallCells, gridSize, cellSize, floorPrefab,
                "Zemin", 0f, alignTop: true);

        if (dressCeiling && ceilingPrefab != null)
            placed += DressTiles(group, wallCells, gridSize, cellSize, ceilingPrefab,
                "Tavan", wallHeight, alignTop: false);

        if (dressDoors && doorPrefab != null)
            placed += DressDoors(map.transform);

        if (dressLamps && lampPrefab != null)
            placed += DressLamps(map.transform, wallHeight);

        if (dressCrouch && crouchMaterial != null)
            placed += MarkCrouchPassages(map.transform, crouchMaterial);

        SetOriginalRenderers(map, false);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"Harita giydirildi: {placed} parça yerleştirildi ve sahne kaydedildi.\n" +
            "Küplerin çarpışma kutuları duruyor, sadece görüntüleri kapatıldı.\n" +
            "Duvarlar ters/boş görünüyorsa penceredeki 'Panelleri ters çevir' kutusunu işaretleyip tekrar bas.");
    }

    private void Strip()
    {
        GameObject map = GameObject.Find(MapName);
        if (map == null)
            return;

        Transform existing = map.transform.Find(GroupName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        // Kapı ve lamba giydirmeleri hareketli/mevcut objelerin child'ı olduğu
        // için Giydirme grubunun içinde değiller; adlarından bulunuyorlar.
        foreach (GameObject dressing in FindDressingChildren(map.transform))
            Undo.DestroyObjectImmediate(dressing);

        RestoreDoorPanels(map.transform);
        RestoreCrouchPassages(map.transform);
        SetOriginalRenderers(map, true);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("Giydirme kaldırıldı, harita küp haline döndü.");
    }

    // ---------- Parçalar ----------

    /// <summary>
    /// Duvar bloklarının **açık koridora bakan** yüzlerine panel koyar. İki
    /// duvarın birbirine bakan yüzü hiç görünmediği için atlanıyor; 17x17'lik
    /// ızgarada bu, yerleştirilen parça sayısını kabaca yarıya indiriyor.
    /// </summary>
    private int DressWalls(Transform group, Dictionary<Vector2Int, Transform> wallCells,
        int gridSize, float cellSize)
    {
        Bounds bounds = MeasurePrefab(wallPrefab);

        // Geniş yatay eksen panelin eni, dar olan kalınlığı. Hangisinin hangisi
        // olduğunu modele bakmadan böyle anlıyoruz.
        bool thinAlongZ = bounds.size.z <= bounds.size.x;
        float width = thinAlongZ ? bounds.size.x : bounds.size.z;
        float thickness = thinAlongZ ? bounds.size.z : bounds.size.x;

        if (width <= 0.001f)
            return 0;

        float scale = cellSize / width;
        float scaledThickness = thickness * scale;

        // Panelin ön yüzü küpün yüzeyiyle tam çakışsın diye parça küpün içine
        // doğru kalınlığının yarısı kadar itiliyor. Ortalasaydık duvar,
        // çarpışma kutusundan yarım kalınlık daha yakın görünürdü.
        Transform wallGroup = CreateGroup(group, "Duvarlar");
        Vector2Int[] directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        int placed = 0;

        foreach (KeyValuePair<Vector2Int, Transform> pair in wallCells)
        {
            Vector3 center = pair.Value.position;

            foreach (Vector2Int step in directions)
            {
                Vector2Int neighbour = pair.Key + step;

                if (wallCells.ContainsKey(neighbour))
                    continue; // komşu da duvar, bu yüz hiç görünmüyor
                if (!InsideGrid(neighbour, gridSize))
                    continue; // haritanın dışı

                Vector3 normal = new Vector3(step.x, 0f, step.y);
                Vector3 facePoint = new Vector3(center.x, 0f, center.z) + normal * (cellSize / 2f);
                Vector3 target = facePoint - normal * (scaledThickness / 2f);

                Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
                if (!thinAlongZ)
                    rotation *= Quaternion.Euler(0f, -90f, 0f);
                if (flipWallPanels)
                    rotation *= Quaternion.Euler(0f, 180f, 0f);

                Place(wallPrefab, wallGroup, $"Panel_{pair.Key.x}_{pair.Key.y}_{step.x}_{step.y}",
                    target, rotation, scale, baseY: 0f, alignTop: false);

                placed++;
            }
        }

        return placed;
    }

    /// <summary>
    /// Kapı gövdesini **hareket eden panele** takar, böylece kapı tavandan
    /// inerken model de onunla iniyor.
    ///
    /// Bir tuzak var: panel ölçeklenmiş bir küp (0.2 × 3 × 3.2) ve child'ı bu
    /// ölçeği miras alıp ezilir. Bu yüzden panelin transform ölçeği 1'e
    /// çekiliyor, aynı hacim BoxCollider.size'a taşınıyor. Çarpışma birebir
    /// aynı kalıyor; SlidingDoor da localPosition ile çalıştığı için hiç
    /// etkilenmiyor. Giydirme kaldırılınca tersi yapılıyor.
    ///
    /// Model static işaretlenmiyor — hareket eden bir objeyi batch'lemek onu
    /// yerinde dondururdu.
    /// </summary>
    private int DressDoors(Transform map)
    {
        Transform doors = map.Find("Kapilar");
        if (doors == null)
            return 0;

        Bounds bounds = MeasurePrefab(doorPrefab);

        bool thinAlongZ = bounds.size.z <= bounds.size.x;
        float modelWidth = thinAlongZ ? bounds.size.x : bounds.size.z;

        if (modelWidth <= 0.001f)
            return 0;

        int placed = 0;

        foreach (Transform door in doors)
        {
            Transform panel = door.Find("Panel");
            if (panel == null)
                continue;

            Vector3 size = UnscalePanel(panel);

            // Panelin ince yatay ekseni, kapının baktığı yön.
            bool facesX = size.x <= size.z;
            Vector3 normal = facesX ? Vector3.right : Vector3.forward;
            float opening = facesX ? size.z : size.x;

            Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
            if (!thinAlongZ)
                rotation *= Quaternion.Euler(0f, -90f, 0f);
            if (flipWallPanels)
                rotation *= Quaternion.Euler(0f, 180f, 0f);

            Place(doorPrefab, panel, "Giydirme_Kapi", panel.position, rotation,
                opening / modelWidth, panel.position.y - size.y / 2f,
                alignTop: false, markStatic: false);

            SetRenderer(panel, false);
            placed++;
        }

        return placed;
    }

    /// <summary>
    /// Lambalara görünür bir armatür takar. Işığın kendisi (Light bileşeni)
    /// yerinde kalıyor — şu an ışık kaynağı görünmüyor, havada asılı duruyor.
    /// Armatürün üstü tavana yaslanıp gövdesi aşağı sarkıyor.
    /// </summary>
    private int DressLamps(Transform map, float wallHeight)
    {
        Transform lamps = map.Find("Lambalar");
        if (lamps == null)
            return 0;

        Bounds bounds = MeasurePrefab(lampPrefab);

        float width = Mathf.Max(bounds.size.x, bounds.size.z);
        if (width <= 0.001f)
            return 0;

        float scale = lampWidth / width;
        int placed = 0;

        foreach (Transform lamp in lamps)
        {
            Place(lampPrefab, lamp, "Giydirme_Lamba", lamp.position, Quaternion.identity,
                scale, wallHeight, alignTop: true, markStatic: true);

            placed++;
        }

        return placed;
    }

    /// <summary>
    /// Eğilme geçitlerine ayrı bir yüzey verir.
    ///
    /// Panelle kaplamıyoruz, bilerek: geçit aslında 3.2 × 3'lük bir duvar
    /// yüzünde açılmış 1.4 × 1.1'lik bir delik. Oraya duvar paneli döşemek
    /// deliği kapatırdı. Onun yerine bloklar yerinde kalıp materyal
    /// değiştiriyor — koridor duvarından bakışta ayrışıyorlar. Kamufle
    /// olurlarsa kestirme yol olmaktan çıkarlar, oyunun hız/gizlilik takasının
    /// bir ayağı gider.
    /// </summary>
    private static int MarkCrouchPassages(Transform map, Material material)
    {
        Transform passages = map.Find("EgilmeGecitleri");
        if (passages == null)
            return 0;

        int placed = 0;

        foreach (Transform passage in passages)
        {
            foreach (Transform part in passage)
            {
                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer == null)
                    continue;

                Undo.RecordObject(renderer, "Haritayı Giydir");
                renderer.sharedMaterial = material;
                placed++;
            }
        }

        return placed;
    }

    /// <summary>Açık hücrelere zemin/tavan karosu döşer.</summary>
    private int DressTiles(Transform group, Dictionary<Vector2Int, Transform> wallCells,
        int gridSize, float cellSize, GameObject prefab, string label, float baseY, bool alignTop)
    {
        Bounds bounds = MeasurePrefab(prefab);

        float width = Mathf.Max(bounds.size.x, bounds.size.z);
        if (width <= 0.001f)
            return 0;

        float scale = cellSize / width;

        Transform tileGroup = CreateGroup(group, label);
        Transform anyWall = First(wallCells);

        // Dünya konumunu duvar bloklarının kendi konumundan türetiyoruz;
        // haritanın merkezi kaymış olsa bile karolar duvarlarla hizalı kalıyor.
        Vector3 origin = anyWall.position;
        Vector2Int originCell = ParseCell(anyWall.name);

        int placed = 0;

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (wallCells.ContainsKey(cell))
                    continue; // duvarın altına zemin döşemeye gerek yok

                Vector3 target = new Vector3(
                    origin.x + (x - originCell.x) * cellSize,
                    0f,
                    origin.z + (z - originCell.y) * cellSize);

                Place(prefab, tileGroup, $"{label}_{x}_{z}", target, Quaternion.identity,
                    scale, baseY, alignTop);

                placed++;
            }
        }

        return placed;
    }

    /// <summary>
    /// Parçayı yerleştirir ve **ölçtükten sonra** hizalar. Pivotun nerede
    /// olduğunu bilmemize gerek kalmıyor: örnek sahneye konuyor, gerçek sınırları
    /// okunuyor, fark kadar kaydırılıyor.
    /// </summary>
    private static void Place(GameObject prefab, Transform parent, string name,
        Vector3 target, Quaternion rotation, float scale, float baseY, bool alignTop,
        bool markStatic = true)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = name;
        instance.transform.SetPositionAndRotation(Vector3.zero, rotation);
        instance.transform.localScale = Vector3.one * scale;

        Bounds bounds = WorldBounds(instance);

        instance.transform.position += new Vector3(
            target.x - bounds.center.x,
            alignTop ? baseY - bounds.max.y : baseY - bounds.min.y,
            target.z - bounds.center.z);

        // Çarpışma küpten geliyor; kitin kendi kutuları hem gereksiz fizik yükü
        // hem de bıçağın görüş hattı ışınına takılacak fazladan engel olurdu.
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
            collider.enabled = false;

        if (markStatic)
            GameObjectUtility.SetStaticEditorFlags(instance,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ContributeGI);

        // Giydirmenin collider'ları zaten kapalı, yani fizik onu hiç görmüyor.
        // Katmanı yine de Süs: ileride bir parçanın collider'lı kalması hâlinde
        // görüş hattını kesmesin.
        LayerSetup.Apply(instance, LayerSetup.Sus);
    }

    /// <summary>
    /// Kapı panelinin transform ölçeğini 1'e çeker ve hacmi BoxCollider.size'a
    /// taşır; eski hacmi döndürür. Çarpışma değişmiyor, amaç child modelin
    /// ezilmemesi. Zaten dönüştürülmüşse hacmi collider'dan okuyor, yani
    /// tekrar tekrar giydirmek güvenli.
    /// </summary>
    private static Vector3 UnscalePanel(Transform panel)
    {
        BoxCollider box = panel.GetComponent<BoxCollider>();

        if (panel.localScale == Vector3.one)
            return box != null ? box.size : Vector3.one;

        Vector3 size = panel.localScale;

        if (box != null)
        {
            Undo.RecordObject(box, "Haritayı Giydir");
            box.size = size;
        }

        Undo.RecordObject(panel, "Haritayı Giydir");
        panel.localScale = Vector3.one;

        return size;
    }

    // ---------- Yardımcılar ----------

    private static Dictionary<Vector2Int, Transform> ReadWallCells(Transform walls, out int gridSize)
    {
        Dictionary<Vector2Int, Transform> cells = new Dictionary<Vector2Int, Transform>();
        int maxIndex = 0;

        foreach (Transform child in walls)
        {
            Vector2Int cell = ParseCell(child.name);
            if (cell.x < 0)
                continue;

            cells[cell] = child;
            maxIndex = Mathf.Max(maxIndex, Mathf.Max(cell.x, cell.y));
        }

        gridSize = maxIndex + 1;
        return cells;
    }

    /// <summary>Kapı ve lambalara takılmış giydirme child'ları.</summary>
    private static List<GameObject> FindDressingChildren(Transform map)
    {
        List<GameObject> found = new List<GameObject>();

        foreach (Transform child in map.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("Giydirme_"))
                found.Add(child.gameObject);
        }

        return found;
    }

    /// <summary>Panellerin hacmini collider'dan transform ölçeğine geri taşır.</summary>
    private static void RestoreDoorPanels(Transform map)
    {
        Transform doors = map.Find("Kapilar");
        if (doors == null)
            return;

        foreach (Transform door in doors)
        {
            Transform panel = door.Find("Panel");
            if (panel == null || panel.localScale != Vector3.one)
                continue;

            BoxCollider box = panel.GetComponent<BoxCollider>();
            if (box == null || box.size == Vector3.one)
                continue;

            Undo.RecordObject(panel, "Giydirmeyi Kaldır");
            panel.localScale = box.size;

            Undo.RecordObject(box, "Giydirmeyi Kaldır");
            box.size = Vector3.one;

            SetRenderer(panel, true);
        }
    }

    private static void RestoreCrouchPassages(Transform map)
    {
        Transform passages = map.Find("EgilmeGecitleri");
        if (passages == null)
            return;

        Material original = AssetDatabase.LoadAssetAtPath<Material>(CrouchOriginalMaterial);
        if (original == null)
            return;

        foreach (Transform passage in passages)
        {
            foreach (Transform part in passage)
            {
                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer == null)
                    continue;

                Undo.RecordObject(renderer, "Giydirmeyi Kaldır");
                renderer.sharedMaterial = original;
            }
        }
    }

    /// <summary>Sözlükteki herhangi bir blok — ölçü ve hizalama referansı için.</summary>
    private static Transform First(Dictionary<Vector2Int, Transform> cells)
    {
        foreach (KeyValuePair<Vector2Int, Transform> pair in cells)
            return pair.Value;

        return null;
    }

    /// <summary>"Duvar_12_5" → (12, 5). Ad uymuyorsa (-1, -1).</summary>
    private static Vector2Int ParseCell(string name)
    {
        string[] parts = name.Split('_');

        if (parts.Length >= 3
            && int.TryParse(parts[parts.Length - 2], out int x)
            && int.TryParse(parts[parts.Length - 1], out int z))
            return new Vector2Int(x, z);

        return new Vector2Int(-1, -1);
    }

    private static bool InsideGrid(Vector2Int cell, int gridSize)
        => cell.x >= 0 && cell.y >= 0 && cell.x < gridSize && cell.y < gridSize;

    private static void ReplaceGroup(Transform map, out Transform group)
    {
        Transform existing = map.Find(GroupName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject created = new GameObject(GroupName);
        created.transform.SetParent(map, false);
        Undo.RegisterCreatedObjectUndo(created, "Haritayı Giydir");

        group = created.transform;
    }

    private static Transform CreateGroup(Transform parent, string name)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    /// <summary>
    /// Prefabın ölçek 1, dönüş sıfırken kapladığı yer. Geçici bir örnek üzerinden
    /// ölçülüyor; prefab asset'inin renderer sınırlarını doğrudan okumak
    /// güvenilir değil.
    /// </summary>
    private static Bounds MeasurePrefab(GameObject prefab)
    {
        GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        temp.transform.localScale = Vector3.one;

        Bounds bounds = WorldBounds(temp);
        Object.DestroyImmediate(temp);

        return bounds;
    }

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

    /// <summary>
    /// Küplerin görüntüsünü açar/kapatır. Collider'a dokunmuyoruz — labirentin
    /// çarpışması, doğum noktaları ve bıçağın duvar kontrolü ona bağlı.
    /// </summary>
    private static void SetOriginalRenderers(GameObject map, bool visible)
    {
        Transform walls = map.transform.Find("Duvarlar");
        if (walls != null)
        {
            foreach (Transform child in walls)
                SetRenderer(child, visible);
        }

        SetRenderer(map.transform.Find("Zemin"), visible);
        SetRenderer(map.transform.Find("Tavan"), visible);
    }

    private static void SetRenderer(Transform target, bool visible)
    {
        if (target == null)
            return;

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer == null)
            return;

        Undo.RecordObject(renderer, "Haritayı Giydir");
        renderer.enabled = visible;
    }
}
