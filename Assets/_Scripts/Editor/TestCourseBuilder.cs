using UnityEditor;
using UnityEngine;

/// <summary>
/// Hareket ayarlarını ölçmek için bir test parkuru kurar.
/// Her istasyon PlayerController'daki belirli bir değeri sınar:
/// rampalar slopeLimit'i, basamaklar stepOffset'i, boşluklar zıplama
/// mesafesini (hız + jumpPower + gravity), sütunlar zıplama yüksekliğini,
/// köprü ise airstrafe'i. Menü: Yakalamaca > Test Parkuru Kur.
/// </summary>
public static class TestCourseBuilder
{
    private const string CourseName = "TestParkuru";
    private const string GroundName = "Zemin";
    private const string MaterialRoot = "Assets/_Art";
    private const string MaterialFolder = MaterialRoot + "/Materials";

    [MenuItem("Yakalamaca/Test Parkuru Kur")]
    private static void BuildCourse()
    {
        GameObject existing = GameObject.Find(CourseName);
        if (existing != null)
        {
            bool rebuild = EditorUtility.DisplayDialog("Parkur zaten var",
                "Mevcut test parkuru silinip yeniden kurulsun mu?", "Yeniden kur", "Vazgeç");
            if (!rebuild)
                return;

            Undo.DestroyObjectImmediate(existing);
        }

        EnlargeGround();

        GameObject root = new GameObject(CourseName);
        Undo.RegisterCreatedObjectUndo(root, "Test Parkuru Kur");

        Material rampMaterial = GetOrCreateMaterial("Parkur_Rampa", new Color(0.25f, 0.5f, 0.9f));
        Material stepMaterial = GetOrCreateMaterial("Parkur_Basamak", new Color(0.3f, 0.75f, 0.4f));
        Material gapMaterial = GetOrCreateMaterial("Parkur_Bosluk", new Color(0.95f, 0.55f, 0.2f));
        Material pillarMaterial = GetOrCreateMaterial("Parkur_Sutun", new Color(0.65f, 0.35f, 0.8f));
        Material bridgeMaterial = GetOrCreateMaterial("Parkur_Kopru", new Color(0.9f, 0.8f, 0.25f));
        Material wallMaterial = GetOrCreateMaterial("Parkur_Duvar", new Color(0.45f, 0.45f, 0.5f));
        Material mazeMaterial = GetOrCreateMaterial("Parkur_Kose", new Color(0.85f, 0.3f, 0.3f));

        BuildRamps(root.transform, rampMaterial);
        BuildStairs(root.transform, stepMaterial);
        BuildGapJumps(root.transform, gapMaterial);
        BuildHeightPillars(root.transform, pillarMaterial);
        BuildCoyoteBridge(root.transform, bridgeMaterial);
        BuildSprintCorridor(root.transform, wallMaterial);
        BuildCornerMaze(root.transform, mazeMaterial);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;

        Debug.Log(
            "Test parkuru kuruldu. Oyuncu başlangıçta +Z yönüne bakar.\n" +
            "MAVİ rampalar (sol): 15/30/45/60 derece — slopeLimit 45 olduğu için 60'lık rampaya tırmanamamalısın.\n" +
            "YEŞİL merdiven: basamak yükseklikleri 0.15 / 0.30 / 0.45 / 0.60 — stepOffset 0.3 olduğundan 3. basamakta takılıp zıplaman gerekmeli.\n" +
            "TURUNCU platformlar: aralar 2 / 3 / 4 / 5 / 6 birim — nereye kadar yürüyerek, nereden sonra sprintle atlayabildiğine bak.\n" +
            "MOR sütunlar: 0.5 / 1.0 / 1.5 / 2.0 metre yükseklik — Source zıplaması 60 unit (1.14 m) olduğundan son ikisi çıkılamamalı.\n" +
            "SARI köprü (sağ): kenardan boşluğa atla ve havadayken mouse'u yavaşça çevirip A/D tut — airstrafe ile menzilin uzamalı.\n" +
            "GRİ koridor (arkanda): sprint + düz koşu alanı, durma mesafesini ölçmek için.\n" +
            "KIRMIZI zikzak (sağ arka): asıl önemli istasyon. 2.4 m genişlikte koridor, art arda 180 derece dönüş. " +
            "Sprintle gir ve duvara sürtmeden çıkmayı dene — friction/accelerate ayarını burada karara bağla.");
    }

    /// <summary>
    /// PlayerSetup'ın kurduğu 50x50'lik zemin parkur için dar kalıyor; 70x70'e çıkarır.
    /// </summary>
    private static void EnlargeGround()
    {
        GameObject ground = GameObject.Find(GroundName);
        if (ground == null)
            return;

        if (ground.transform.localScale.x < 7f)
        {
            Undo.RecordObject(ground.transform, "Test Parkuru Kur");
            ground.transform.localScale = new Vector3(7f, 1f, 7f);
        }
    }

    // MAVİ — slopeLimit testi. Rampalar +Z yönünde yükselir.
    private static void BuildRamps(Transform parent, Material material)
    {
        Transform group = CreateGroup("Rampalar", parent);
        float[] angles = { 15f, 30f, 45f, 60f };
        float[] positionsX = { -20f, -16f, -12f, -8f };

        const float length = 6f;
        const float startZ = 4f;

        for (int i = 0; i < angles.Length; i++)
        {
            float radians = angles[i] * Mathf.Deg2Rad;
            Vector3 center = new Vector3(
                positionsX[i],
                length * Mathf.Sin(radians) / 2f,
                startZ + length * Mathf.Cos(radians) / 2f);

            GameObject ramp = CreateBox($"Rampa_{angles[i]:0}derece", group, center,
                new Vector3(3f, 0.4f, length), material);
            ramp.transform.localRotation = Quaternion.Euler(-angles[i], 0f, 0f);
        }
    }

    // YEŞİL — stepOffset testi. Her basamağın yüksekliği bir öncekinden fazla.
    private static void BuildStairs(Transform parent, Material material)
    {
        Transform group = CreateGroup("Merdiven", parent);
        float[] riserHeights = { 0.15f, 0.30f, 0.45f, 0.60f };

        const float depth = 1.5f;
        float currentTop = 0f;
        float currentZ = 4f;

        for (int i = 0; i < riserHeights.Length; i++)
        {
            currentTop += riserHeights[i];

            // Basamağı zemine kadar uzatıyoruz ki altı boşlukta kalmasın.
            CreateBox($"Basamak_{riserHeights[i]:0.00}", group,
                new Vector3(-3f, currentTop / 2f, currentZ + depth / 2f),
                new Vector3(3f, currentTop, depth), material);

            currentZ += depth;
        }
    }

    // TURUNCU — zıplama mesafesi testi (hız + jumpPower + gravity birlikte).
    private static void BuildGapJumps(Transform parent, Material material)
    {
        Transform group = CreateGroup("BoslukAtlama", parent);
        float[] gaps = { 2f, 3f, 4f, 5f, 6f };

        const float depth = 3f;
        const float height = 1f;
        float currentZ = 4f;

        CreateBox("Platform_Baslangic", group,
            new Vector3(3f, height / 2f, currentZ + depth / 2f),
            new Vector3(3f, height, depth), material);

        for (int i = 0; i < gaps.Length; i++)
        {
            currentZ += depth + gaps[i];

            CreateBox($"Platform_Bosluk{gaps[i]:0}", group,
                new Vector3(3f, height / 2f, currentZ + depth / 2f),
                new Vector3(3f, height, depth), material);
        }
    }

    // MOR — zıplama yüksekliği testi. Her sütuna zeminden çıkılır.
    private static void BuildHeightPillars(Transform parent, Material material)
    {
        Transform group = CreateGroup("YukseklikSutunlari", parent);
        float[] heights = { 0.5f, 1.0f, 1.5f, 2.0f };

        const float depth = 3f;
        const float spacing = 3f; // aralarda zemine inip tekrar hız alabilmek için
        float currentZ = 4f;

        for (int i = 0; i < heights.Length; i++)
        {
            CreateBox($"Sutun_{heights[i]:0.0}", group,
                new Vector3(10f, heights[i] / 2f, currentZ + depth / 2f),
                new Vector3(3f, heights[i], depth), material);

            currentZ += depth + spacing;
        }
    }

    // SARI — airstrafe testi: rampayla çıkılan yüksek köprü, kenarında boşluk.
    private static void BuildCoyoteBridge(Transform parent, Material material)
    {
        Transform group = CreateGroup("CoyoteKoprusu", parent);

        const float bridgeHeight = 2f;
        const float rampLength = 5f;
        const float angle = 30f;

        // Köprüye çıkış rampası
        float radians = angle * Mathf.Deg2Rad;
        GameObject ramp = CreateBox("Kopru_Rampa", group,
            new Vector3(18f, bridgeHeight / 2f, 4f + rampLength * Mathf.Cos(radians) / 2f),
            new Vector3(3f, 0.4f, rampLength), material);
        ramp.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);

        // Köprü tabanı: rampanın bittiği yerden başlar
        float bridgeStartZ = 4f + rampLength * Mathf.Cos(radians);
        const float bridgeLength = 8f;

        CreateBox("Kopru", group,
            new Vector3(18f, bridgeHeight - 0.25f, bridgeStartZ + bridgeLength / 2f),
            new Vector3(3f, 0.5f, bridgeLength), material);

        // Kenardan sonra boşluk, sonra iniş platformu
        const float gap = 3.5f;
        float landingZ = bridgeStartZ + bridgeLength + gap;

        CreateBox("Kopru_Inis", group,
            new Vector3(18f, bridgeHeight - 0.25f, landingZ + 2f),
            new Vector3(3f, 0.5f, 4f), material);
    }

    // GRİ — sprint hızını ve ivmelenmeyi hissetmek için düz koridor (-Z yönünde).
    private static void BuildSprintCorridor(Transform parent, Material material)
    {
        Transform group = CreateGroup("SprintKoridoru", parent);

        const float length = 26f;
        const float halfWidth = 2.25f;
        const float wallHeight = 3f;
        float centerZ = -4f - length / 2f;

        CreateBox("Duvar_Sol", group,
            new Vector3(-halfWidth, wallHeight / 2f, centerZ),
            new Vector3(0.5f, wallHeight, length), material);

        CreateBox("Duvar_Sag", group,
            new Vector3(halfWidth, wallHeight / 2f, centerZ),
            new Vector3(0.5f, wallHeight, length), material);

        // Koridorun sonundaki kapanış duvarı — durma mesafesini ölçmek için
        CreateBox("Duvar_Son", group,
            new Vector3(0f, wallHeight / 2f, centerZ - length / 2f),
            new Vector3(5f, wallHeight, 0.5f), material);
    }

    /// <summary>
    /// KIRMIZI — köşe dönme testi: 90 derecelik dönüşlerden oluşan zikzak koridor.
    /// Koridor genişliği Source'taki tipik 128 unit'e denk (2.4 m). Asıl oyun dar
    /// alanda kovalamaca olduğu için friction/accelerate ayarını burada denemelisin:
    /// sprintle girip duvara sürtmeden çıkabiliyor musun?
    /// </summary>
    private static void BuildCornerMaze(Transform parent, Material material)
    {
        Transform group = CreateGroup("KoseKoridoru", parent);

        const float laneWidth = 2.4f;      // Source'ta tipik koridor genişliği (128 unit)
        const float blockDepth = 2.6f;     // şeritler arasındaki dolu duvar bloğu
        const float wallHeight = 3f;
        const float wallThickness = 0.5f;
        const float minX = 8f;
        const float maxX = 30f;
        const float firstLaneZ = -8f;
        const int laneCount = 4;

        float laneSpacing = laneWidth + blockDepth;
        float topZ = firstLaneZ + laneWidth / 2f;
        float bottomZ = firstLaneZ - (laneCount - 1) * laneSpacing - laneWidth / 2f;
        float centerZ = (topZ + bottomZ) / 2f;
        float depth = topZ - bottomZ;

        // Şeritleri ayıran bloklar. Geçiş boşluğu her seferinde karşı uçta
        // kaldığı için oyuncu her şeritte 180 derece dönmek zorunda kalır.
        for (int i = 0; i < laneCount - 1; i++)
        {
            bool passageOnRight = i % 2 == 0;
            float blockMinX = passageOnRight ? minX : minX + laneWidth;
            float blockMaxX = passageOnRight ? maxX - laneWidth : maxX;

            CreateBox($"Blok_{i + 1}", group,
                new Vector3((blockMinX + blockMaxX) / 2f, wallHeight / 2f, firstLaneZ - i * laneSpacing - laneSpacing / 2f),
                new Vector3(blockMaxX - blockMinX, wallHeight, blockDepth), material);
        }

        // Dış duvarlar. Giriş, üst duvarın sol ucunda bırakılan boşluk.
        CreateBox("Dis_Sol", group,
            new Vector3(minX - wallThickness / 2f, wallHeight / 2f, centerZ),
            new Vector3(wallThickness, wallHeight, depth), material);

        CreateBox("Dis_Sag", group,
            new Vector3(maxX + wallThickness / 2f, wallHeight / 2f, centerZ),
            new Vector3(wallThickness, wallHeight, depth), material);

        float entranceEndX = minX + laneWidth;
        CreateBox("Dis_Ust", group,
            new Vector3((entranceEndX + maxX + wallThickness) / 2f, wallHeight / 2f, topZ + wallThickness / 2f),
            new Vector3(maxX + wallThickness - entranceEndX, wallHeight, wallThickness), material);

        CreateBox("Dis_Alt", group,
            new Vector3((minX + maxX) / 2f, wallHeight / 2f, bottomZ - wallThickness / 2f),
            new Vector3(maxX - minX + wallThickness * 2f, wallHeight, wallThickness), material);
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
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
