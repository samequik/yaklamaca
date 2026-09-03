using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tek katlı labirent harita üretir: uzun koridorlar, ortalarında iki taraftan
/// düğmeyle açılan kapılar, duvarlar arasında eğilerek geçilen geçitler.
///
/// Neden elle çizilmiş bir harita değil de üretici? Elle ASCII harita yazınca
/// bir koridoru yanlışlıkla kapatıp ulaşılamaz bölge bırakmak çok kolay. Burada
/// labirent bağlantılı olacak şekilde üretiliyor ve sonunda flood fill ile
/// doğrulanıyor. Sonuç normal GameObject'ler — Unity'de blokları taşıyıp
/// silerek istediğin gibi düzenleyebilirsin.
///
/// Menü: Yakalamaca > Labirent Harita Kur
/// </summary>
public static class MazeMapBuilder
{
    private const string MapName = "Harita";
    private const string MaterialRoot = "Assets/_Art";
    private const string MaterialFolder = MaterialRoot + "/Materials";

    // Aynı sayı hep aynı haritayı verir. Farklı bir labirent istersen değiştir.
    private const int Seed = 1337;

    // Hücre boyutu = koridor genişliği. CLAUDE.md: hızlı hareket için 3-3.6 m.
    private const float CellSize = 3.2f;
    private const float WallHeight = 3f;

    // Hücre sayısı; ızgara (CellsPerSide * 2 + 1) olur. 8 → 17x17 → 54.4 m kare.
    private const int CellsPerSide = 8;

    // Aynı yönde devam etme eğilimi (%). Yükseldikçe koridorlar uzar.
    private const int StraightBias = 70;

    // Çıkmaz sokakların yüzde kaçı açılıp döngüye çevrilecek. Kovalamacada
    // çıkmaz sokak = anında yakalanmak; döngü olmadan kaçacak yer kalmıyor.
    private const int BraidPercent = 90;

    private const int DoorCount = 5;
    private const int CrouchCount = 5;

    // Kapı garaj kapısı gibi: koridorun tamamını kaplar, tavandan aşağı iner.
    // Kasa yok — labirentin kendi duvarları zaten kapının iki yanını oluşturuyor.
    private const float DoorThickness = 0.2f;

    // Geçit ölçüleri — CLAUDE.md'deki hull hesaplarından.
    private const float CrouchWidth = 1.4f;
    private const float CrouchHeight = 1.1f;

    // Düğmeler labirent duvarına monte, kapının iki yanına.
    private const float ButtonAxisOffset = 1.15f;               // kapıdan koridor boyunca uzaklık
    private const float ButtonWallOffset = CellSize / 2f - 0.07f; // yan duvara yaslı
    private const float ButtonHeight = 1.1f;

    [MenuItem("Yakalamaca/Labirent Harita Kur")]
    private static void BuildMap()
    {
        bool proceed = EditorUtility.DisplayDialog("Labirent harita",
            $"{CellsPerSide * 2 + 1}x{CellsPerSide * 2 + 1} ızgarada tek katlı labirent kurulacak " +
            $"({(CellsPerSide * 2 + 1) * CellSize:0.#} m kare).\n\n" +
            $"{DoorCount} kapı (her iki tarafında düğme), {CrouchCount} eğilme geçidi.\n\n" +
            "Varsa eski Harita, TestParkuru ve Zemin silinecek.",
            "Kur", "Vazgeç");

        if (!proceed)
            return;

        DestroyIfExists(MapName);
        DestroyIfExists("TestParkuru");
        DestroyIfExists("Zemin"); // labirent kendi zeminini getiriyor

        int size = CellsPerSide * 2 + 1;
        System.Random random = new System.Random(Seed);

        bool[,] wall = GenerateMaze(size, random);
        BraidDeadEnds(wall, random);

        List<Vector2Int> doorCells = PickSpread(FindDoorSpots(wall), DoorCount, 5, random);
        List<Vector2Int> crouchCells = PickSpread(FindCrouchSpots(wall, doorCells), CrouchCount, 5, random);

        GameObject root = new GameObject(MapName);
        Undo.RegisterCreatedObjectUndo(root, "Labirent Harita Kur");

        Material wallMaterial = GetOrCreateMaterial("Harita_Duvar", new Color(0.52f, 0.52f, 0.56f));
        Material floorMaterial = GetOrCreateMaterial("Harita_Zemin", new Color(0.34f, 0.34f, 0.38f));
        Material doorMaterial = GetOrCreateMaterial("Harita_Kapi", new Color(0.30f, 0.55f, 0.75f));
        Material buttonMaterial = GetOrCreateMaterial("Harita_Dugme", new Color(0.90f, 0.35f, 0.25f));
        Material crouchMaterial = GetOrCreateMaterial("Harita_Gecit", new Color(0.62f, 0.55f, 0.32f));

        BuildFloor(root.transform, size, floorMaterial);
        BuildWalls(root.transform, wall, crouchCells, wallMaterial);
        BuildCrouchPassages(root.transform, wall, crouchCells, crouchMaterial);
        BuildDoors(root.transform, wall, doorCells, doorMaterial, buttonMaterial);

        Vector2Int spawnCell = MovePlayerToSpawn(wall, doorCells);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;

        int unreachable = CountUnreachable(wall, spawnCell);
        string warning = unreachable > 0
            ? $"\nUYARI: {unreachable} hücreye ulaşılamıyor. Seed değerini değiştirip tekrar kur."
            : "\nBağlantı doğrulandı: tüm koridorlara ulaşılabiliyor.";

        Debug.Log(
            $"Labirent kuruldu (seed {Seed}). {doorCells.Count} kapı, {crouchCells.Count} eğilme geçidi.\n" +
            "MAVİ kapılar: garaj kapısı gibi, koridorun tamamını kaplar ve tavandan aşağı inerek kapanır. " +
            "Kapanırken son anda Ctrl ile kayarak altından geçebilirsin. Yan duvarlarda KIRMIZI düğmeler var, " +
            "kapıya değil onlara E basacaksın.\n" +
            "SARI geçitler: duvarın içinden Ctrl ile eğilerek geçilir, kestirme yol.\n" +
            "Duvarlar Static işaretlendi (batching), hepsi tek materyal paylaşıyor." + warning);
    }

    // ---------- Labirent üretimi ----------

    /// <summary>
    /// Derinlik öncelikli oyma (recursive backtracker). Tek koordinatlar hücre,
    /// çift koordinatlar hücreler arası duvar. Aynı yönde devam etmeye eğilimli,
    /// çünkü düz DFS kısa ve kıvrık koridorlar üretiyor — bize uzun koridor lazım.
    /// </summary>
    private static bool[,] GenerateMaze(int size, System.Random random)
    {
        bool[,] wall = new bool[size, size];
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                wall[x, z] = true;

        Vector2Int[] directions =
        {
            new Vector2Int(2, 0), new Vector2Int(-2, 0),
            new Vector2Int(0, 2), new Vector2Int(0, -2)
        };

        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        List<Vector2Int> candidates = new List<Vector2Int>();

        wall[1, 1] = false;
        stack.Push(new Vector2Int(1, 1));
        Vector2Int lastDirection = Vector2Int.zero;

        while (stack.Count > 0)
        {
            Vector2Int current = stack.Peek();
            candidates.Clear();

            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;
                if (next.x <= 0 || next.y <= 0 || next.x >= size - 1 || next.y >= size - 1)
                    continue;
                if (!wall[next.x, next.y])
                    continue; // zaten oyulmuş

                candidates.Add(direction);
            }

            if (candidates.Count == 0)
            {
                stack.Pop();
                continue;
            }

            Vector2Int chosen = candidates.Contains(lastDirection) && random.Next(100) < StraightBias
                ? lastDirection
                : candidates[random.Next(candidates.Count)];

            Vector2Int between = current + chosen / 2;
            Vector2Int target = current + chosen;

            wall[between.x, between.y] = false;
            wall[target.x, target.y] = false;

            lastDirection = chosen;
            stack.Push(target);
        }

        return wall;
    }

    /// <summary>
    /// Çıkmaz sokakları açıp döngü haline getirir. Kovalamacada çıkmaza girmek
    /// yakalanmak demek; döngü olmadan harita oynanmaz.
    /// </summary>
    private static void BraidDeadEnds(bool[,] wall, System.Random random)
    {
        int size = wall.GetLength(0);
        Vector2Int[] directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };
        List<Vector2Int> breakable = new List<Vector2Int>();

        for (int x = 1; x < size - 1; x++)
        {
            for (int z = 1; z < size - 1; z++)
            {
                if (wall[x, z] || CountOpenNeighbours(wall, x, z) != 1)
                    continue;
                if (random.Next(100) >= BraidPercent)
                    continue;

                breakable.Clear();
                foreach (Vector2Int direction in directions)
                {
                    Vector2Int wallCell = new Vector2Int(x + direction.x, z + direction.y);
                    Vector2Int beyond = new Vector2Int(x + direction.x * 2, z + direction.y * 2);

                    if (beyond.x <= 0 || beyond.y <= 0 || beyond.x >= size - 1 || beyond.y >= size - 1)
                        continue;
                    if (wall[wallCell.x, wallCell.y] && !wall[beyond.x, beyond.y])
                        breakable.Add(wallCell);
                }

                if (breakable.Count > 0)
                {
                    Vector2Int opened = breakable[random.Next(breakable.Count)];
                    wall[opened.x, opened.y] = false;
                }
            }
        }
    }

    private static int CountOpenNeighbours(bool[,] wall, int x, int z)
    {
        int open = 0;
        if (!wall[x + 1, z]) open++;
        if (!wall[x - 1, z]) open++;
        if (!wall[x, z + 1]) open++;
        if (!wall[x, z - 1]) open++;
        return open;
    }

    /// <summary>Kapı için: koridorun ortasındaki açık hücreler (kavşak değil).</summary>
    private static List<Vector2Int> FindDoorSpots(bool[,] wall)
    {
        int size = wall.GetLength(0);
        List<Vector2Int> spots = new List<Vector2Int>();

        for (int x = 2; x < size - 2; x++)
        {
            for (int z = 2; z < size - 2; z++)
            {
                if (wall[x, z])
                    continue;

                bool horizontal = !wall[x - 1, z] && !wall[x + 1, z] && wall[x, z - 1] && wall[x, z + 1]
                    && !wall[x - 2, z] && !wall[x + 2, z];
                bool vertical = !wall[x, z - 1] && !wall[x, z + 1] && wall[x - 1, z] && wall[x + 1, z]
                    && !wall[x, z - 2] && !wall[x, z + 2];

                if (horizontal || vertical)
                    spots.Add(new Vector2Int(x, z));
            }
        }

        return spots;
    }

    /// <summary>
    /// Eğilme geçidi için: iki koridoru ayıran duvar hücreleri. Buraya delik
    /// açınca kestirme yol oluyor.
    /// </summary>
    private static List<Vector2Int> FindCrouchSpots(bool[,] wall, List<Vector2Int> doorCells)
    {
        int size = wall.GetLength(0);
        List<Vector2Int> spots = new List<Vector2Int>();

        for (int x = 1; x < size - 1; x++)
        {
            for (int z = 1; z < size - 1; z++)
            {
                if (!wall[x, z])
                    continue;

                bool horizontal = !wall[x - 1, z] && !wall[x + 1, z] && wall[x, z - 1] && wall[x, z + 1];
                bool vertical = !wall[x, z - 1] && !wall[x, z + 1] && wall[x - 1, z] && wall[x + 1, z];
                if (!horizontal && !vertical)
                    continue;

                // Kapının hemen yanına geçit koyma; kapıyı anlamsızlaştırır.
                bool nearDoor = false;
                foreach (Vector2Int door in doorCells)
                {
                    if (Mathf.Abs(door.x - x) + Mathf.Abs(door.y - z) <= 2)
                    {
                        nearDoor = true;
                        break;
                    }
                }

                if (!nearDoor)
                    spots.Add(new Vector2Int(x, z));
            }
        }

        return spots;
    }

    /// <summary>Adayları karıştırıp birbirinden uzak olacak şekilde seçer.</summary>
    private static List<Vector2Int> PickSpread(List<Vector2Int> candidates, int count, int minDistance, System.Random random)
    {
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        List<Vector2Int> picked = new List<Vector2Int>();
        foreach (Vector2Int candidate in candidates)
        {
            if (picked.Count >= count)
                break;

            bool tooClose = false;
            foreach (Vector2Int chosen in picked)
            {
                if (Mathf.Abs(chosen.x - candidate.x) + Mathf.Abs(chosen.y - candidate.y) < minDistance)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
                picked.Add(candidate);
        }

        return picked;
    }

    /// <summary>Başlangıçtan ulaşılamayan açık hücre sayısı. 0 olmalı.</summary>
    private static int CountUnreachable(bool[,] wall, Vector2Int start)
    {
        int size = wall.GetLength(0);
        bool[,] seen = new bool[size, size];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        queue.Enqueue(start);
        seen[start.x, start.y] = true;

        Vector2Int[] directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int direction in directions)
            {
                int nx = current.x + direction.x;
                int nz = current.y + direction.y;

                if (nx < 0 || nz < 0 || nx >= size || nz >= size)
                    continue;
                if (wall[nx, nz] || seen[nx, nz])
                    continue;

                seen[nx, nz] = true;
                queue.Enqueue(new Vector2Int(nx, nz));
            }
        }

        int unreachable = 0;
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                if (!wall[x, z] && !seen[x, z])
                    unreachable++;

        return unreachable;
    }

    // ---------- Geometri ----------

    private static Vector3 CellToWorld(int x, int z, int size)
    {
        float offset = (size - 1) / 2f;
        return new Vector3((x - offset) * CellSize, 0f, (z - offset) * CellSize);
    }

    private static void BuildFloor(Transform parent, int size, Material material)
    {
        float span = size * CellSize;
        GameObject floor = CreateBox("Zemin", parent,
            new Vector3(0f, -0.25f, 0f),
            new Vector3(span, 0.5f, span), material);

        MarkStatic(floor);
    }

    private static void BuildWalls(Transform parent, bool[,] wall, List<Vector2Int> crouchCells, Material material)
    {
        int size = wall.GetLength(0);
        Transform group = CreateGroup("Duvarlar", parent);

        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                if (!wall[x, z] || crouchCells.Contains(new Vector2Int(x, z)))
                    continue;

                GameObject block = CreateBox($"Duvar_{x}_{z}", group,
                    CellToWorld(x, z, size) + Vector3.up * (WallHeight / 2f),
                    new Vector3(CellSize, WallHeight, CellSize), material);

                MarkStatic(block);
            }
        }
    }

    private static void BuildCrouchPassages(Transform parent, bool[,] wall, List<Vector2Int> cells, Material material)
    {
        int size = wall.GetLength(0);
        Transform group = CreateGroup("EgilmeGecitleri", parent);

        foreach (Vector2Int cell in cells)
        {
            bool alongX = !wall[cell.x - 1, cell.y] && !wall[cell.x + 1, cell.y];
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 cross = alongX ? Vector3.forward : Vector3.right;

            Transform passage = CreateGroup($"Gecit_{cell.x}_{cell.y}", group);
            passage.localPosition = CellToWorld(cell.x, cell.y, size);

            float sideWidth = (CellSize - CrouchWidth) / 2f;
            float sideOffset = CrouchWidth / 2f + sideWidth / 2f;

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject block = CreateBox($"Yan_{side}", passage,
                    cross * (sideOffset * side) + Vector3.up * (WallHeight / 2f),
                    AxisSize(axis, CellSize, WallHeight, cross, sideWidth), material);

                MarkStatic(block);
            }

            GameObject ceiling = CreateBox("Tavan", passage,
                Vector3.up * ((CrouchHeight + WallHeight) / 2f),
                AxisSize(axis, CellSize, WallHeight - CrouchHeight, cross, CrouchWidth), material);

            MarkStatic(ceiling);
        }
    }

    private static void BuildDoors(Transform parent, bool[,] wall, List<Vector2Int> cells,
        Material doorMaterial, Material buttonMaterial)
    {
        int size = wall.GetLength(0);
        Transform group = CreateGroup("Kapilar", parent);

        foreach (Vector2Int cell in cells)
        {
            // Koridor hangi eksende uzanıyorsa kapı ona dik durur.
            bool alongX = !wall[cell.x - 1, cell.y] && !wall[cell.x + 1, cell.y];
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;   // koridor yönü
            Vector3 cross = alongX ? Vector3.forward : Vector3.right;  // koridorun genişliği

            Transform doorRoot = CreateGroup($"Kapi_{cell.x}_{cell.y}", group);
            doorRoot.localPosition = CellToWorld(cell.x, cell.y, size);

            // Tek panel, koridorun tamamını kaplıyor: 3.2 m genişlik, tavana kadar.
            // Kapalıyken geçit tamamen kapalı, kasa veya lento yok.
            GameObject panel = CreateBox("Panel", doorRoot,
                Vector3.up * (WallHeight / 2f),
                AxisSize(axis, DoorThickness, WallHeight, cross, CellSize), doorMaterial);

            // Kapı durumu ağ üzerinden dağıtılıyor; kimlik SlidingDoor'dan önce
            // eklenmeli, yoksa Mirror bileşeni kimliksiz görüp hata yazıyor.
            panel.AddComponent<Mirror.NetworkIdentity>();

            SlidingDoor door = panel.AddComponent<SlidingDoor>();
            SerializedObject serializedDoor = new SerializedObject(door);
            // Açılırken panel tavana çekilir, kapanırken tavandan aşağı iner.
            // Böylece kapanırken boşluk ALTTA kalıyor ve son anda eğilip
            // altından kayarak geçmek mümkün oluyor — kovalamacanın can alıcı anı.
            serializedDoor.FindProperty("slideDirection").vector3Value = Vector3.up;
            serializedDoor.FindProperty("slideDistance").floatValue = WallHeight + 0.05f;
            serializedDoor.FindProperty("allowDirectUse").boolValue = false; // sadece düğmeyle
            serializedDoor.ApplyModifiedProperties();

            // Kapı katı dünyanın parçası: kapalıyken arkasından vurulmamalı.
            // SlidingDoor da IInteractable ama burada bileşene değil işleve bakıyoruz.
            LayerSetup.Apply(panel, LayerSetup.Harita);

            // Düğmeler labirent duvarına yaslı, kapının iki yanında.
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject buttonBox = CreateBox($"Dugme_{side}", doorRoot,
                    axis * (ButtonAxisOffset * side)
                        + cross * ButtonWallOffset
                        + Vector3.up * ButtonHeight,
                    AxisSize(axis, 0.3f, 0.3f, cross, 0.12f), buttonMaterial);

                UseButton button = buttonBox.AddComponent<UseButton>();
                SerializedObject serializedButton = new SerializedObject(button);
                serializedButton.FindProperty("prompt").stringValue = "Kapıyı çalıştır";
                serializedButton.FindProperty("pressVisual").objectReferenceValue = buttonBox.transform;
                // Düğme yan duvara monte; basınca o duvara doğru içeri girer.
                serializedButton.FindProperty("pressDirection").vector3Value = cross;

                SerializedProperty targets = serializedButton.FindProperty("targets");
                targets.arraySize = 1;
                targets.GetArrayElementAtIndex(0).objectReferenceValue = door;
                serializedButton.ApplyModifiedProperties();

                // Nişan alınacak yüzey. Görüşü kesen bir engel DEĞİL: 1.1 m
                // yükseklikte küçük bir kutu, vuruş ışınına takılmamalı.
                LayerSetup.Apply(buttonBox, LayerSetup.Etkilesim);
            }
        }
    }

    /// <summary>Eksen yönlerine göre Vector3 boyut kurar (kapı/geçit iki yönde de çalışsın diye).</summary>
    private static Vector3 AxisSize(Vector3 axis, float alongAxis, float height, Vector3 cross, float alongCross)
    {
        return new Vector3(
            Mathf.Abs(axis.x) * alongAxis + Mathf.Abs(cross.x) * alongCross,
            height,
            Mathf.Abs(axis.z) * alongAxis + Mathf.Abs(cross.z) * alongCross);
    }

    private static Vector2Int MovePlayerToSpawn(bool[,] wall, List<Vector2Int> doorCells)
    {
        int size = wall.GetLength(0);
        Vector2Int spawn = new Vector2Int(1, 1);

        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogWarning("Player bulunamadı — önce Yakalamaca > Sahneye Oyuncu Kur çalıştır.");
            return spawn;
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        float halfHeight = controller != null ? controller.height / 2f : 0.6858f;

        Undo.RecordObject(player.transform, "Labirent Harita Kur");
        player.transform.position = CellToWorld(spawn.x, spawn.y, size) + Vector3.up * halfHeight;

        return spawn;
    }

    // ---------- Yardımcılar ----------

    private static void DestroyIfExists(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);
    }

    private static void MarkStatic(GameObject target)
    {
        // Static batching + occlusion culling + lightmap. CLAUDE.md bölüm 3.
        GameObjectUtility.SetStaticEditorFlags(target,
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);

        // Işını kesen katı dünya. Katman tanımlı değilse sessizce atlanıyor,
        // yani Katmanları Kur hiç çalıştırılmamış projede de sorun çıkmıyor.
        LayerSetup.Apply(target, LayerSetup.Harita);
    }

    private static Transform CreateGroup(string name, Transform parent)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    private static GameObject CreateBox(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = scale;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder(MaterialRoot))
            AssetDatabase.CreateFolder("Assets", "_Art");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder(MaterialRoot, "Materials");

        Material material = new Material(Shader.Find("Standard")) { color = color };
        material.enableInstancing = true; // CLAUDE.md bölüm 3
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
