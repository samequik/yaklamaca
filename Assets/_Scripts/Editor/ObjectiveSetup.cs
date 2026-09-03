using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Terminalleri duvarlara, çıkış kapılarını da haritanın kenarına yerleştirir
/// (bkz. CLAUDE.md bölüm 11). Sayılar TerminalCount ve ExitCount sabitlerinde.
///
/// Terminaller **duvara monte**: koridorun ortasına konsalar kovalamacada
/// engel olurlardı. Duvar bulmak için PropScatterWindow'daki yöntem
/// kullanılıyor — rastgele nokta seç, dört yöne ışın at, en yakın dikey yüzeye
/// yasla.
///
/// Her çıkış, dış duvar halkasında açılan bir gedik. Oradaki duvar bloğunun
/// görüntüsü ve çarpışması kapatılıyor, yerine kapı + tetikleyici + canavar
/// engeli konuyor. Labirentin kendisi hiç yeniden üretilmiyor, yani giydirme
/// ve süsler bozulmuyor.
///
/// **İki çıkış birbirinden en uzak gediklere kuruluyor.** Aynı kenara düşen
/// ikinci çıkış canavarın ikisini birden görmesi demek olurdu.
///
/// Menü: Yakalamaca > Terminal ve Çıkış Kur
/// </summary>
public static class ObjectiveSetup
{
    private const string MapName = "Harita";
    private const string GroupName = "HedefSistemi";
    private const string KitRoot = "Assets/SciFi Warehouse Kit/Prefabs/Structures";
    private const string KitProps = KitRoot + "/Structure Props";

    private const int TerminalCount = 5;

    // Haritada kaç çıkış olacak. Tek çıkışta canavar kapıyı bekleyerek turu
    // bitirebiliyordu (CLAUDE.md teknik borç 6).
    private const int ExitCount = 2;
    private const float TerminalHeight = 1.35f;
    private const float MinTerminalSpacing = 12f;

    [MenuItem("Yakalamaca/Terminal ve Çıkış Kur", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Terminal ve Çıkış Kur")]
    private static void Run()
    {
        GameObject map = GameObject.Find(MapName);
        Transform walls = map != null ? map.transform.Find("Duvarlar") : null;

        if (walls == null)
        {
            EditorUtility.DisplayDialog("Harita yok",
                "Önce Yakalamaca > Labirent Harita Kur çalıştır.", "Tamam");
            return;
        }

        Dictionary<Vector2Int, Transform> wallCells = ReadWallCells(walls, out int gridSize);
        if (wallCells.Count == 0)
        {
            EditorUtility.DisplayDialog("Duvar yok", "'Duvar_x_z' bloğu bulunamadı.", "Tamam");
            return;
        }

        Transform sample = First(wallCells);
        float cellSize = sample.localScale.x;
        float wallHeight = sample.localScale.y;

        // Önceki kurulumu geri al: gedik açılan duvar tekrar görünür olsun.
        Transform existing = map.transform.Find(GroupName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        RestoreAllWalls(map.transform, wallCells);

        GameObject group = new GameObject(GroupName);
        group.transform.SetParent(map.transform, false);
        Undo.RegisterCreatedObjectUndo(group, "Terminal ve Çıkış Kur");

        int placed = BuildTerminals(group.transform, cellSize);
        int exits = BuildExits(group.transform, wallCells, gridSize, cellSize, wallHeight);

        SyncTerminalGoal(placed);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            $"{placed}/{TerminalCount} terminal duvara yerleştirildi. " +
            $"{exits}/{ExitCount} çıkış kuruldu." +
            (placed < TerminalCount
                ? "\nUYARI: terminal eksik kaldı; terminalGoal yerleşen sayıya çekildi."
                : "") +
            (exits < ExitCount
                ? "\nUYARI: çıkış için yeterli dış duvar bulunamadı."
                : "") +
            "\nSahne kaydedildi.\n\n" +
            "TEST: Play → Host → [1] tur başlat → bir terminale bak, E'ye bas.\n" +
            "Yüzde dolarken hareket edemezsin, sadece sağa sola biraz bakabilirsin.\n" +
            "Gereken sayı bitince çıkış açılır; içinden geçen kaçan kurtulur, canavar geçemez.");
    }

    // ---------- Terminaller ----------

    private static int BuildTerminals(Transform parent, float cellSize)
    {
        GameObject body = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitProps}/Fusebox 01.prefab");

        List<Vector3> placedPositions = new List<Vector3>();
        float halfSpan = cellSize * 7f;
        int placed = 0;

        // Aralık kademeli gevşiyor. Sabit eşikte beşinci terminal yerleşemeyip
        // sessizce eksik kalabiliyordu ve eksik terminal turu bozuyor: çıkış
        // açılmadan tur kilitleniyor. Tavanı SyncTerminalGoal ayrıca yerleşen
        // sayıya çekiyor, yani iki tedbir birden var.
        float[] spacings = { MinTerminalSpacing, MinTerminalSpacing * 0.7f, MinTerminalSpacing * 0.45f };

        foreach (float spacing in spacings)
        {
            int attempts = 0;

            while (placed < TerminalCount && attempts < 800)
            {
                attempts++;

                Vector3 sample = new Vector3(
                    Random.Range(-halfSpan, halfSpan), TerminalHeight, Random.Range(-halfSpan, halfSpan));

                if (!TryFindWall(sample, out Vector3 wallPoint, out Vector3 normal))
                    continue;

                // Terminaller birbirinden uzak olsun: hepsi aynı köşede
                // toplanırsa canavar tek noktayı bekleyerek turu bitirir.
                if (IsTooClose(placedPositions, wallPoint, spacing))
                    continue;

                CreateTerminal(parent, body, wallPoint, normal, placed + 1);
                placedPositions.Add(wallPoint);
                placed++;
            }

            if (placed >= TerminalCount)
                break;
        }

        return placed;
    }

    private static void CreateTerminal(Transform parent, GameObject body,
        Vector3 wallPoint, Vector3 normal, int index)
    {
        GameObject terminal = new GameObject($"Terminal_{index}");
        terminal.transform.SetParent(parent, false);
        terminal.transform.SetPositionAndRotation(
            wallPoint + normal * 0.06f, Quaternion.LookRotation(normal, Vector3.up));

        // Etkileşim ışını buna çarpıyor. PlayerInteractor kökten arıyor, o
        // yüzden collider'ın burada olması yeterli.
        BoxCollider box = terminal.AddComponent<BoxCollider>();
        box.size = new Vector3(0.6f, 0.8f, 0.16f);

        terminal.AddComponent<NetworkIdentity>();
        Terminal component = terminal.AddComponent<Terminal>();

        if (body != null)
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(body, terminal.transform);
            visual.name = "Govde";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>())
                collider.enabled = false;
        }

        // Durum göstergesi: karanlık koridorda terminali uzaktan tanıyabilmek
        // ve yüzdeyi renkten okuyabilmek için.
        GameObject light = GameObject.CreatePrimitive(PrimitiveType.Cube);
        light.name = "Gosterge";
        Object.DestroyImmediate(light.GetComponent<Collider>());
        light.transform.SetParent(terminal.transform, false);
        light.transform.localPosition = new Vector3(0f, 0.28f, -0.09f);
        light.transform.localScale = new Vector3(0.34f, 0.08f, 0.03f);
        light.GetComponent<Renderer>().sharedMaterial =
            GetOrCreateUnlitMaterial("Terminal_Gosterge", Color.white);

        SerializedObject serialized = new SerializedObject(component);
        serialized.FindProperty("progressLight").objectReferenceValue = light.GetComponent<Renderer>();

        // Oynanarak ayarlanan değerler burada da açıkça yazılıyor. Yalnızca
        // koddaki varsayılanı değiştirmek yetmiyor: sahnede duran terminaller
        // eski değeri serileştirilmiş hâlde taşıyor ve öyle kalıyorlar.
        serialized.FindProperty("monsterLockDuration").floatValue = 1.5f;

        serialized.ApplyModifiedProperties();

        // Nişan alınacak yüzey: PlayerInteractor ışını buna çarpıyor, ama
        // canavarın vuruş ışınına engel sayılmıyor.
        LayerSetup.Apply(terminal, LayerSetup.Etkilesim);

        Undo.RegisterCreatedObjectUndo(terminal, "Terminal ve Çıkış Kur");
    }

    /// <summary>Örnek noktadan dört yöne ışın atıp en yakın dikey yüzeyi bulur.</summary>
    private static bool TryFindWall(Vector3 sample, out Vector3 wallPoint, out Vector3 normal)
    {
        wallPoint = default;
        normal = Vector3.forward;

        if (Physics.CheckSphere(sample, 0.5f, ~0, QueryTriggerInteraction.Ignore))
            return false;

        Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        float best = float.MaxValue;
        bool found = false;

        foreach (Vector3 direction in directions)
        {
            if (!Physics.Raycast(sample, direction, out RaycastHit hit, 2.2f, ~0,
                    QueryTriggerInteraction.Ignore))
                continue;

            if (Mathf.Abs(hit.normal.y) > 0.3f || hit.distance >= best)
                continue;

            best = hit.distance;
            wallPoint = hit.point;
            normal = hit.normal;
            found = true;
        }

        return found;
    }

    // ---------- Çıkış ----------

    /// <summary>
    /// Çıkışları kurar. Birden fazlaysa birbirinden **en uzak** gedikler
    /// seçiliyor: tek çıkışta canavar kapıyı bekleyerek turu bitirebiliyordu ve
    /// bu 5 kişilik turda çok daha ezici (CLAUDE.md teknik borç 6).
    /// </summary>
    private static int BuildExits(Transform parent, Dictionary<Vector2Int, Transform> wallCells,
        int gridSize, float cellSize, float wallHeight)
    {
        List<BorderOpening> openings = FindBorderOpenings(wallCells, gridSize);
        List<BorderOpening> chosen = PickSpreadOpenings(openings, ExitCount);

        int built = 0;

        foreach (BorderOpening opening in chosen)
        {
            if (BuildExit(parent, wallCells, cellSize, wallHeight, opening, built + 1))
                built++;
        }

        return built;
    }

    /// <summary>
    /// Dış duvar halkasında bir gedik açıp kapıyı oraya kuruyor. Gedik açılan
    /// blok silinmiyor, sadece görüntüsü ve çarpışması kapatılıyor — böylece
    /// kurulum geri alınabiliyor.
    /// </summary>
    private static bool BuildExit(Transform parent, Dictionary<Vector2Int, Transform> wallCells,
        float cellSize, float wallHeight, BorderOpening opening, int index)
    {
        Vector2Int cell = opening.Cell;

        if (!wallCells.TryGetValue(cell, out Transform block))
            return false;

        Vector3 center = block.position;
        Vector3 outDirection = new Vector3(opening.Outward.x, 0f, opening.Outward.y);

        // Gediği aç.
        Renderer blockRenderer = block.GetComponent<Renderer>();
        if (blockRenderer != null)
        {
            Undo.RecordObject(blockRenderer, "Terminal ve Çıkış Kur");
            blockRenderer.enabled = false;
        }

        Collider blockCollider = block.GetComponent<Collider>();
        if (blockCollider != null)
        {
            Undo.RecordObject(blockCollider, "Terminal ve Çıkış Kur");
            blockCollider.enabled = false;
        }

        // Kapı: gediğin tam ortasında, tavana çekilerek açılıyor — diğer
        // kapılarla aynı davranış.
        GameObject door = CreateBox(parent, $"Cikis_Kapisi_{index}",
            new Vector3(center.x, wallHeight / 2f, center.z),
            AxisSize(outDirection, 0.25f, wallHeight, cellSize));

        // Karanlıkta seçilebilsin diye ışıktan etkilenmeyen parlak bir yüzey:
        // çıkış, kaçanın uzaktan tanıması gereken nokta. Materyal adında indeks
        // YOK: iki kapı aynı materyali paylaşsın (batching).
        door.GetComponent<Renderer>().sharedMaterial =
            GetOrCreateUnlitMaterial("Cikis_Kapisi", new Color(0.85f, 0.75f, 0.2f));

        door.AddComponent<NetworkIdentity>();
        SlidingDoor sliding = door.AddComponent<SlidingDoor>();

        SerializedObject serializedDoor = new SerializedObject(sliding);
        serializedDoor.FindProperty("slideDirection").vector3Value = Vector3.up;
        serializedDoor.FindProperty("slideDistance").floatValue = wallHeight + 0.05f;
        serializedDoor.FindProperty("allowDirectUse").boolValue = false;
        serializedDoor.FindProperty("autoCloseDelay").floatValue = 0f; // çıkış bir daha kapanmaz
        serializedDoor.ApplyModifiedProperties();

        // Kit gövdesi: çıplak sarı kutu yerine labirent kapılarıyla aynı
        // görünüm. Katmandan ÖNCE, çünkü giydirme kapının çocuğu oluyor ve
        // LayerSetup.Apply altındakileri de dolaşıyor.
        DressExitDoor(door, outDirection, cellSize);

        // Çıkış kapısı da katı dünya: diğer kapılarla aynı gerekçe, kapalıyken
        // arkasından vurulmamalı.
        LayerSetup.Apply(door, LayerSetup.Harita);

        // Geçit: tetikleyici, canavar engeli ve dışarıdaki zemin burada.
        GameObject gate = new GameObject($"Cikis_Gecidi_{index}");
        gate.transform.SetParent(parent, false);
        gate.transform.position = center + outDirection * cellSize;
        gate.AddComponent<NetworkIdentity>();
        ExitGate exitGate = gate.AddComponent<ExitGate>();

        // Zemin gediğin içini de kapsıyor: giydirme yalnızca açık hücrelere karo
        // döşediği için, duvar hücresinden açılan gedikte zemin görünmüyordu.
        GameObject floor = CreateBox(gate.transform, "Zemin",
            center + outDirection * (cellSize * 0.75f) + Vector3.down * 0.25f,
            AxisSize(outDirection, cellSize * 2.2f, 0.5f, cellSize * 1.4f));

        floor.GetComponent<Renderer>().sharedMaterial =
            GetOrCreateUnlitMaterial("Cikis_Zemin", new Color(0.30f, 0.32f, 0.34f));

        GameObject trigger = CreateInvisible(gate.transform, "Tetik",
            gate.transform.position + Vector3.up * (wallHeight / 2f),
            AxisSize(outDirection, cellSize * 0.6f, wallHeight, cellSize));
        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;

        // Canavar engeli gediğin tam ağzında: kaçan geçerken canavar burada
        // duruyor. Sadece canavarın istemcisinde açık olacak.
        GameObject blocker = CreateInvisible(gate.transform, "Engel",
            center + outDirection * (cellSize * 0.45f) + Vector3.up * (wallHeight / 2f),
            AxisSize(outDirection, 0.3f, wallHeight, cellSize));
        BoxCollider blockerCollider = blocker.AddComponent<BoxCollider>();

        SerializedObject serializedGate = new SerializedObject(exitGate);
        serializedGate.FindProperty("escapeTrigger").objectReferenceValue = triggerCollider;
        serializedGate.FindProperty("monsterBlocker").objectReferenceValue = blockerCollider;
        serializedGate.FindProperty("door").objectReferenceValue = sliding;
        serializedGate.ApplyModifiedProperties();

        // Gediğin dışını kapat: yoksa kapıdan gökyüzü görünüyor.
        BuildVestibule(gate.transform, center, outDirection, cellSize, wallHeight);

        HideDressingPanels(parent.parent, cell);

        // Geçidin tamamı Harita. Engel bilerek burada: bugün de vuruş ışınını
        // kesiyor, katman düzeni davranışı değiştirmemeli.
        LayerSetup.Apply(gate, LayerSetup.Harita);

        MarkStatic(floor);
        Undo.RegisterCreatedObjectUndo(gate, "Terminal ve Çıkış Kur");

        return true;
    }

    /// <summary>Dış halkada açılabilecek bir gedik: hangi hücre, hangi yöne.</summary>
    private struct BorderOpening
    {
        public Vector2Int Cell;
        public Vector2Int Outward;
    }

    /// <summary>
    /// Dış halkada, içeri tarafı koridor olan BÜTÜN duvar hücrelerini toplar.
    /// Eskiden ilk bulunan döndürülüyordu; iki çıkış için adayların hepsi
    /// gerekiyor, yoksa ikisi yan yana düşebilir.
    /// </summary>
    private static List<BorderOpening> FindBorderOpenings(
        Dictionary<Vector2Int, Transform> wallCells, int gridSize)
    {
        Vector2Int[] directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        List<BorderOpening> found = new List<BorderOpening>();

        foreach (KeyValuePair<Vector2Int, Transform> pair in wallCells)
        {
            Vector2Int candidate = pair.Key;

            bool onBorder = candidate.x == 0 || candidate.y == 0
                || candidate.x == gridSize - 1 || candidate.y == gridSize - 1;

            if (!onBorder)
                continue;

            foreach (Vector2Int step in directions)
            {
                Vector2Int inward = candidate + step;

                // İçeri tarafı koridor olmalı, dışarı tarafı harita dışı.
                if (!InsideGrid(inward, gridSize) || wallCells.ContainsKey(inward))
                    continue;

                found.Add(new BorderOpening { Cell = candidate, Outward = -step });
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// Birbirinden olabildiğince uzak `count` gedik seçer. İlk ikisi haritanın
    /// en uzak çifti, sonrakiler seçilenlere en uzak aday.
    ///
    /// Rastgele seçmek yetmezdi: iki çıkış aynı kenara düşerse canavar ikisini
    /// birden görebiliyor ve ikinci çıkış hiçbir şey değiştirmiyor.
    /// </summary>
    private static List<BorderOpening> PickSpreadOpenings(List<BorderOpening> all, int count)
    {
        List<BorderOpening> chosen = new List<BorderOpening>();

        if (all.Count == 0 || count <= 0)
            return chosen;

        int bestA = 0;
        int bestB = 0;
        int bestDistance = -1;

        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                int distance = (all[i].Cell - all[j].Cell).sqrMagnitude;
                if (distance <= bestDistance)
                    continue;

                bestDistance = distance;
                bestA = i;
                bestB = j;
            }
        }

        chosen.Add(all[bestA]);

        if (count > 1 && all.Count > 1)
            chosen.Add(all[bestB]);

        // Üç ve fazlası: her turda seçilenlere en uzak adayı ekle.
        while (chosen.Count < count && chosen.Count < all.Count)
        {
            int bestIndex = -1;
            int bestNearest = -1;

            for (int i = 0; i < all.Count; i++)
            {
                if (Contains(chosen, all[i].Cell))
                    continue;

                int nearest = int.MaxValue;

                foreach (BorderOpening picked in chosen)
                    nearest = Mathf.Min(nearest, (picked.Cell - all[i].Cell).sqrMagnitude);

                if (nearest <= bestNearest)
                    continue;

                bestNearest = nearest;
                bestIndex = i;
            }

            if (bestIndex < 0)
                break;

            chosen.Add(all[bestIndex]);
        }

        return chosen;
    }

    private static bool Contains(List<BorderOpening> list, Vector2Int cell)
    {
        foreach (BorderOpening opening in list)
        {
            if (opening.Cell == cell)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gereken terminal sayısının TAVANI sahnedeki RoundManager'da duruyor
    /// (`terminalGoal`). Gerçekte yerleşen sayıdan büyük kalırsa çıkış **hiç
    /// açılmaz**: RoundManager hayattaki kaçan + 1 isteyip var olmayan bir
    /// terminali bekler. Bu yüzden tavan her kurulumda yerleşen sayıya çekiliyor.
    ///
    /// Koddaki varsayılanı değiştirmek yetmiyor: sahnedeki RoundManager eski
    /// değeri serileştirilmiş hâlde taşıyor (CLAUDE.md bölüm 1 ve 16).
    /// </summary>
    private static void SyncTerminalGoal(int placed)
    {
        RoundManager[] managers = Object.FindObjectsByType<RoundManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (managers.Length == 0)
        {
            Debug.LogWarning("Sahnede RoundManager yok, terminalGoal güncellenemedi.");
            return;
        }

        foreach (RoundManager manager in managers)
        {
            SerializedObject serialized = new SerializedObject(manager);
            SerializedProperty goal = serialized.FindProperty("terminalGoal");

            if (goal == null || goal.intValue == placed)
                continue;

            goal.intValue = placed;
            serialized.ApplyModifiedProperties();
            Debug.Log($"RoundManager.terminalGoal {goal.intValue} yapıldı (yerleşen terminal sayısı).");
        }
    }

    /// <summary>Önceki kurulumun açtığı gediği kapatır: çarpışma ve paneller geri gelir.</summary>
    private static void RestoreAllWalls(Transform map, Dictionary<Vector2Int, Transform> wallCells)
    {
        foreach (KeyValuePair<Vector2Int, Transform> pair in wallCells)
        {
            Collider collider = pair.Value.GetComponent<Collider>();
            if (collider != null && !collider.enabled)
            {
                Undo.RecordObject(collider, "Terminal ve Çıkış Kur");
                collider.enabled = true;
            }
        }

        Transform dressing = map.Find("Giydirme");
        Transform walls = dressing != null ? dressing.Find("Duvarlar") : null;

        if (walls == null)
            return;

        foreach (Transform panel in walls)
        {
            if (panel.gameObject.activeSelf)
                continue;

            Undo.RecordObject(panel.gameObject, "Terminal ve Çıkış Kur");
            panel.gameObject.SetActive(true);
        }
    }

    // ---------- Çıkışın görünümü ----------

    /// <summary>
    /// Çıkış kapısına kit gövdesi giydirir — labirent kapılarıyla aynı görünüm.
    ///
    /// **`Haritayı Giydir` bunu yapamıyor.** O araç `Harita/Kapilar` altını
    /// tarıyor; çıkış kapısı ise `HedefSistemi` altında ve giydirmeden SONRA
    /// kuruluyor, yani araç çalışırken ortada yok. Çıkışın çıplak sarı bir kutu
    /// olarak kalmasının sebebi buydu.
    ///
    /// Ölçek collider'a taşınıyor (`MapDressWindow.UnscalePanel` ile aynı
    /// numara): kutu 0.25 x 3 x 3.2 ölçekli, o ölçek altındaki kit gövdesini
    /// eziyor. Çarpışma hacmi değişmiyor.
    /// </summary>
    private static void DressExitDoor(GameObject door, Vector3 outDirection, float cellSize)
    {
        GameObject prefab = LoadKit("Walls/Wall BayDoor");
        if (prefab == null)
            return;

        Vector3 size = door.transform.localScale;
        BoxCollider box = door.GetComponent<BoxCollider>();

        if (box != null)
            box.size = size;

        door.transform.localScale = Vector3.one;

        Bounds bounds = MeasurePrefab(prefab);
        bool thinAlongZ = bounds.size.z <= bounds.size.x;
        float modelWidth = thinAlongZ ? bounds.size.x : bounds.size.z;

        if (modelWidth <= 0.001f)
            return;

        Quaternion rotation = Quaternion.LookRotation(outDirection, Vector3.up);

        if (!thinAlongZ)
            rotation *= Quaternion.Euler(0f, -90f, 0f);

        // Gövde kapının ÇOCUĞU: kapı yukarı kayınca onunla birlikte gidiyor.
        PlaceKit(prefab, door.transform, "Giydirme_Kapi", door.transform.position,
            rotation, cellSize / modelWidth, door.transform.position.y - size.y / 2f);

        // Sarı kutu artık yalnızca çarpışma; görüntüyü kit veriyor.
        Renderer renderer = door.GetComponent<Renderer>();

        if (renderer != null)
            renderer.enabled = false;
    }

    /// <summary>
    /// Gediğin dışına kapalı bir sahanlık kurar: yan duvarlar, arka duvar, tavan.
    ///
    /// Çıkışın arkasında hiçbir şey yoktu — kapıdan gökyüzü ve boş zemin
    /// görünüyordu, çıkış "haritada açılmış bir delik" gibi duruyordu. Zemin
    /// zaten vardı; eksik olan çevresiydi.
    ///
    /// Ölçüler zeminle aynı (2.2 x 1.4 hücre), yani sahanlık tam onun üstüne
    /// oturuyor. Yüzler `Wall Plain` ile giydiriliyor ki koridorlardan farklı
    /// durmasın.
    /// </summary>
    private static void BuildVestibule(Transform gate, Vector3 center, Vector3 outDirection,
        float cellSize, float wallHeight)
    {
        Vector3 cross = Vector3.Cross(Vector3.up, outDirection).normalized;

        // Sahanlık duvar halkasının DIŞ yüzünden başlayıp zeminin bittiği yerde
        // bitiyor. Halkanın içine taşsaydı yan duvarlar komşu duvar bloklarının
        // içinde kalır ve yüzeyler çakışırdı.
        const float thickness = 0.3f;

        float innerFace = cellSize * 0.5f;   // duvar halkasının dış yüzü
        float outerEdge = cellSize * 1.85f;  // Cikis_Gecidi/Zemin burada bitiyor

        float depth = outerEdge - innerFace;
        float width = cellSize * 1.4f;

        Vector3 floorCenter = center + outDirection * ((innerFace + outerEdge) * 0.5f);

        Material wallMaterial = LoadMaterial("Harita_Duvar");
        Material ceilingMaterial = LoadMaterial("Harita_Tavan");

        // Yan duvarlar.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 position = floorCenter
                + cross * (side * (width / 2f + thickness / 2f))
                + Vector3.up * (wallHeight / 2f);

            GameObject wall = CreateBox(gate, $"Sahanlik_Yan_{side}", position,
                AxisSize(outDirection, depth, wallHeight, thickness));

            Paint(wall, wallMaterial);
            MarkStatic(wall);

            // İç yüz giydiriliyor: dışarıya bakan yüzü kimse görmüyor.
            DressFace(gate, prefabName: "Walls/Wall Plain",
                target: position - cross * (side * (thickness / 2f)),
                normal: -cross * side, opening: depth, height: wallHeight,
                name: $"Sahanlik_Giydirme_Yan_{side}");
        }

        // Arka duvar — çıkışın dip noktası.
        Vector3 backPosition = floorCenter
            + outDirection * (depth / 2f + thickness / 2f)
            + Vector3.up * (wallHeight / 2f);

        GameObject back = CreateBox(gate, "Sahanlik_Arka", backPosition,
            AxisSize(outDirection, thickness, wallHeight, width));

        Paint(back, wallMaterial);
        MarkStatic(back);

        DressFace(gate, prefabName: "Walls/Wall Plain",
            target: backPosition - outDirection * (thickness / 2f),
            normal: -outDirection, opening: width, height: wallHeight,
            name: "Sahanlik_Giydirme_Arka");

        // Tavan: gökyüzünü kapatan asıl parça.
        GameObject ceiling = CreateBox(gate, "Sahanlik_Tavan",
            floorCenter + Vector3.up * (wallHeight + 0.25f),
            AxisSize(outDirection, depth, 0.5f, width + thickness * 2f));

        Paint(ceiling, ceilingMaterial);
        MarkStatic(ceiling);
    }

    /// <summary>Bir yüzeye kit paneli yaslar. Panel yoksa sessizce atlanıyor.</summary>
    private static void DressFace(Transform parent, string prefabName, Vector3 target,
        Vector3 normal, float opening, float height, string name)
    {
        GameObject prefab = LoadKit(prefabName);
        if (prefab == null)
            return;

        Bounds bounds = MeasurePrefab(prefab);
        bool thinAlongZ = bounds.size.z <= bounds.size.x;
        float modelWidth = thinAlongZ ? bounds.size.x : bounds.size.z;

        if (modelWidth <= 0.001f)
            return;

        Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);

        if (!thinAlongZ)
            rotation *= Quaternion.Euler(0f, -90f, 0f);

        PlaceKit(prefab, parent, name, target, rotation, opening / modelWidth,
            target.y - height / 2f);
    }

    private static GameObject LoadKit(string relativePath) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{KitRoot}/{relativePath}.prefab");

    private static Material LoadMaterial(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Art/Materials/{name}.mat");

    private static void Paint(GameObject target, Material material)
    {
        Renderer renderer = target.GetComponent<Renderer>();

        if (renderer != null && material != null)
            renderer.sharedMaterial = material;
    }

    /// <summary>
    /// Kit parçasını ölçekleyip hedefe oturtur — `MapDressWindow.Place` ile aynı
    /// yöntem. Collider'ları kapatılıyor: çarpışma kutulardan geliyor ve kitin
    /// kendi kutuları vuruş ışınına fazladan engel olurdu.
    /// </summary>
    private static void PlaceKit(GameObject prefab, Transform parent, string name,
        Vector3 target, Quaternion rotation, float scale, float baseY)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = name;
        instance.transform.SetPositionAndRotation(Vector3.zero, rotation);
        instance.transform.localScale = Vector3.one * scale;

        Bounds bounds = WorldBounds(instance);

        instance.transform.position += new Vector3(
            target.x - bounds.center.x,
            baseY - bounds.min.y,
            target.z - bounds.center.z);

        foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
            collider.enabled = false;

        LayerSetup.Apply(instance, LayerSetup.Sus);
    }

    /// <summary>
    /// Prefabın ölçek 1, dönüş sıfırken kapladığı yer. Geçici bir örnek
    /// üzerinden ölçülüyor; prefab asset'inin sınırlarını doğrudan okumak
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

    // ---------- Yardımcılar ----------

    private static GameObject CreateBox(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.position = position;
        box.transform.localScale = scale;
        return box;
    }

    private static GameObject CreateInvisible(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        GameObject item = new GameObject(name);
        item.transform.SetParent(parent, false);
        item.transform.position = position;
        item.transform.localScale = scale;
        return item;
    }

    /// <summary>Dışa bakan eksende ince, koridor ekseninde geniş bir kutu boyutu.</summary>
    private static Vector3 AxisSize(Vector3 outDirection, float thickness, float height, float width)
    {
        bool alongX = Mathf.Abs(outDirection.x) > 0.5f;
        return alongX
            ? new Vector3(thickness, height, width)
            : new Vector3(width, height, thickness);
    }

    /// <summary>
    /// Işıktan etkilenmeyen materyal. Sahne bilerek zifiri (ortam ışığı 0.018);
    /// Standard shader kullanan bir gösterge, rengi ne olursa olsun siyah
    /// görünüyor. TrailMarkSystem de aynı sebeple Sprites/Default kullanıyor.
    /// </summary>
    private static Material GetOrCreateUnlitMaterial(string name, Color color)
    {
        const string folder = "Assets/_Art/Materials";
        string path = $"{folder}/{name}.mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.color = color;
            return existing;
        }

        if (!AssetDatabase.IsValidFolder("Assets/_Art"))
            AssetDatabase.CreateFolder("Assets", "_Art");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/_Art", "Materials");

        Material material = new Material(Shader.Find("Sprites/Default")) { color = color };
        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    /// <summary>
    /// Giydirme, duvar küplerinin üstüne panel döşüyor. Gediği açarken küpün
    /// görüntüsünü kapatmak yetmiyor — o yüze denk gelen paneller de
    /// kaldırılmalı, yoksa kapının önünde sağlam bir duvar duruyor gibi görünür.
    /// </summary>
    private static void HideDressingPanels(Transform map, Vector2Int cell)
    {
        Transform dressing = map.Find("Giydirme");
        Transform walls = dressing != null ? dressing.Find("Duvarlar") : null;

        if (walls == null)
            return;

        string prefix = $"Panel_{cell.x}_{cell.y}_";

        foreach (Transform panel in walls)
        {
            if (!panel.name.StartsWith(prefix))
                continue;

            Undo.RecordObject(panel.gameObject, "Terminal ve Çıkış Kur");
            panel.gameObject.SetActive(false);
        }
    }

    private static void MarkStatic(GameObject target)
    {
        GameObjectUtility.SetStaticEditorFlags(target,
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.ContributeGI);

        LayerSetup.Apply(target, LayerSetup.Harita);
    }

    private static bool IsTooClose(List<Vector3> existing, Vector3 candidate, float minDistance)
    {
        float sqrMin = minDistance * minDistance;

        for (int i = 0; i < existing.Count; i++)
        {
            if ((existing[i] - candidate).sqrMagnitude < sqrMin)
                return true;
        }

        return false;
    }

    private static Dictionary<Vector2Int, Transform> ReadWallCells(Transform walls, out int gridSize)
    {
        Dictionary<Vector2Int, Transform> cells = new Dictionary<Vector2Int, Transform>();
        int maxIndex = 0;

        foreach (Transform child in walls)
        {
            string[] parts = child.name.Split('_');

            if (parts.Length < 3
                || !int.TryParse(parts[parts.Length - 2], out int x)
                || !int.TryParse(parts[parts.Length - 1], out int z))
                continue;

            cells[new Vector2Int(x, z)] = child;
            maxIndex = Mathf.Max(maxIndex, Mathf.Max(x, z));
        }

        gridSize = maxIndex + 1;
        return cells;
    }

    private static bool InsideGrid(Vector2Int cell, int gridSize)
        => cell.x >= 0 && cell.y >= 0 && cell.x < gridSize && cell.y < gridSize;

    private static Transform First(Dictionary<Vector2Int, Transform> cells)
    {
        foreach (KeyValuePair<Vector2Int, Transform> pair in cells)
            return pair.Value;

        return null;
    }
}
