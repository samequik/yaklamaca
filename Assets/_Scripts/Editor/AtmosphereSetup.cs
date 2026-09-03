using UnityEditor;
using UnityEngine;

/// <summary>
/// Labirente tavan atar ve sahneyi korku oyununa uygun şekilde karartır.
///
/// Tavan olmadan karanlık işe yaramaz: gökyüzü ışığı içeri sızar ve koridorlar
/// aydınlık kalır. Tavan + düşük ortam ışığı + sis üçlüsü birlikte çalışır,
/// biri eksik olunca diğer ikisi anlamsızlaşır.
///
/// Sis burada süs değil oynanış: 3.2 m'lik koridorların 48 metreye kadar
/// uzayabildiği bir labirentte, görüş mesafesi kısıtlanmazsa köşeyi dönmeden
/// koridorun sonunu görürsün ve labirent labirent olmaktan çıkar.
///
/// Menü: Yakalamaca > Atmosfer Kur (tavan + ışık)
/// </summary>
public static class AtmosphereSetup
{
    private const string MapName = "Harita";
    private const string CeilingName = "Tavan";
    private const string LightGroupName = "Lambalar";
    private const string MaterialFolder = "Assets/_Art/Materials";

    private const float CeilingThickness = 0.5f;

    // Koridorlara serpiştirilen loş lambalar. Az sayıda ve kısa menzilli:
    // aradaki karanlık bölgeler oyunun kendisi.
    private const int LightCount = 14;
    private const float LightRange = 8f;
    /// <summary>
    /// Lamba şiddeti. **Gerçek zamanlı için fazla, pişirilmiş için doğru.**
    ///
    /// 0.75'ti ve gerçek zamanlıda güzel duruyordu, ama pişirildiğinde harita
    /// kapkara oluyordu. Sebep iki modun düşüş eğrisinin farklı olması:
    /// Built-in'in gerçek zamanlı nokta ışığı menzile göre normalize edilmiş,
    /// affedici bir eğri kullanıyor; **pişirici ise fiziksel ters-kare.**
    ///
    /// Ölçümle doğrulandı. 0.75'te pişmiş atlasın en parlak değeri 1.803'tü ve
    /// bu, lambanın 0.4 m üstündeki tavana denk geliyor
    /// (`0.75 / 0.4² × albedo 0.38 ≈ 1.78`). Oyuncunun bastığı zemin ise
    /// 2.6 m aşağıda: `0.75 / 2.6² × 0.38 ≈ 0.042` — ortam ışığı zaten 0.018.
    /// Yani ışığın neredeyse tamamı kimsenin bakmadığı tavanda toplanıyordu.
    ///
    /// 5.0, zemini ~0.28'e çıkarıyor: lambanın altı belli, arası hâlâ zifiri.
    /// Lambanın dibindeki tavan yanıyor gibi görünüyor — armatür için doğru olan
    /// da bu.
    ///
    /// **Gerçek zamanlıya dönersen fazla parlak gelecek**, bilerek: oyunun
    /// gönderilecek hâli pişirilmiş, gerçek zamanlı bir hata ayıklama yedeği.
    ///
    /// Aynı fark yalnızca **nokta ve spot** ışıkları etkiliyor; `Directional`ın
    /// mesafeye bağlı düşüşü yok, o yüzden 0.05'e dokunulmadı.
    /// </summary>
    private const float LightIntensity = 5f;
    private const float LightHeight = 2.6f;
    private const float MinLightSpacing = 7f;
    private const float SpawnAreaHalfSize = 24f;

    [MenuItem("Yakalamaca/Atmosfer Kur (tavan + ışık)")]
    private static void Setup()
    {
        GameObject map = GameObject.Find(MapName);
        if (map == null)
        {
            EditorUtility.DisplayDialog("Harita yok",
                "Önce Yakalamaca > Labirent Harita Kur çalıştır.", "Tamam");
            return;
        }

        float wallHeight = FindWallHeight(map);
        float span = FindMapSpan(map);

        BuildCeiling(map.transform, span, wallHeight);
        BuildLights(map.transform, wallHeight);
        ConfigureRenderSettings();
        DimDirectionalLight();

        Selection.activeGameObject = map;

        Debug.Log(
            $"Atmosfer kuruldu. Tavan {wallHeight} m yükseklikte, {span:0.#} m kare.\n" +
            $"{LightCount} loş lamba serpiştirildi, ortam ışığı ve sis karartıldı.\n" +
            "Play'e bas ve F ile feneri aç/kapa. Fener kapalıyken lambaların arası zifiri olmalı.\n" +
            "Sahne penceresinde çalışırken tavanı görmek istemezsen Harita > Tavan objesini kapat.\n" +
            "NOT: Lambalar şimdilik gerçek zamanlı. Harita kesinleşince Baked'e çevirip " +
            "lightmap pişirmek gerekiyor (CLAUDE.md bölüm 3).");
    }

    /// <summary>Duvar yüksekliğini sahnedeki bir duvar bloğundan okur.</summary>
    private static float FindWallHeight(GameObject map)
    {
        Transform walls = map.transform.Find("Duvarlar");
        if (walls != null && walls.childCount > 0)
            return walls.GetChild(0).localScale.y;

        return 3f; // MazeMapBuilder varsayılanı
    }

    /// <summary>Harita genişliğini zemin bloğundan okur.</summary>
    private static float FindMapSpan(GameObject map)
    {
        Transform floor = map.transform.Find("Zemin");
        return floor != null ? floor.localScale.x : 54.4f;
    }

    private static void BuildCeiling(Transform mapRoot, float span, float wallHeight)
    {
        Transform existing = mapRoot.Find(CeilingName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        Material material = GetOrCreateMaterial("Harita_Tavan", new Color(0.22f, 0.22f, 0.25f));

        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.name = CeilingName;
        ceiling.transform.SetParent(mapRoot, false);
        ceiling.transform.localPosition = new Vector3(0f, wallHeight + CeilingThickness / 2f, 0f);
        ceiling.transform.localScale = new Vector3(span, CeilingThickness, span);
        ceiling.GetComponent<Renderer>().sharedMaterial = material;

        Undo.RegisterCreatedObjectUndo(ceiling, "Atmosfer Kur");

        GameObjectUtility.SetStaticEditorFlags(ceiling,
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);

        // Tavan da katı dünya: fener ışığını ve görüş hattını kesmeli.
        LayerSetup.Apply(ceiling, LayerSetup.Harita);
    }

    private static void BuildLights(Transform mapRoot, float wallHeight)
    {
        Transform existing = mapRoot.Find(LightGroupName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject group = new GameObject(LightGroupName);
        group.transform.SetParent(mapRoot, false);
        Undo.RegisterCreatedObjectUndo(group, "Atmosfer Kur");

        float height = Mathf.Min(LightHeight, wallHeight - 0.3f);
        int placed = 0;
        int attempts = 0;

        while (placed < LightCount && attempts < 800)
        {
            attempts++;

            Vector3 candidate = new Vector3(
                Random.Range(-SpawnAreaHalfSize, SpawnAreaHalfSize),
                height,
                Random.Range(-SpawnAreaHalfSize, SpawnAreaHalfSize));

            // Duvarın içine lamba koymayalım.
            if (Physics.CheckSphere(candidate, 0.8f, ~0, QueryTriggerInteraction.Ignore))
                continue;
            if (IsTooCloseToExisting(group.transform, candidate))
                continue;

            CreateLight(group.transform, candidate, placed + 1);
            placed++;
        }

        if (placed < LightCount)
            Debug.LogWarning($"Sadece {placed} lamba yerleştirilebildi.");
    }

    private static bool IsTooCloseToExisting(Transform group, Vector3 candidate)
    {
        foreach (Transform existing in group)
        {
            if ((existing.position - candidate).sqrMagnitude < MinLightSpacing * MinLightSpacing)
                return true;
        }

        return false;
    }

    private static void CreateLight(Transform parent, Vector3 position, int index)
    {
        GameObject lightObject = new GameObject($"Lamba_{index}");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.position = position;

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = LightRange;
        light.intensity = LightIntensity;
        light.color = new Color(1f, 0.85f, 0.65f); // soluk sarı, floresan hissi

        // Gölge en pahalı kısım. Ortam lambalarında kapalı, gölgeyi fener veriyor.
        light.shadows = LightShadows.None;
    }

    private static void ConfigureRenderSettings()
    {
        // Ortam ışığı neredeyse sıfır: aydınlatma lambalardan ve fenerden gelsin.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.018f, 0.018f, 0.028f);

        // Sis, görüş mesafesini ~25 metreye indiriyor. 48 metrelik koridorun
        // sonunu göremiyorsun; labirent yeniden labirent oluyor.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.035f);
        RenderSettings.fogDensity = 0.045f;
    }

    private static void DimDirectionalLight()
    {
        foreach (Light light in Object.FindObjectsOfType<Light>())
        {
            if (light.type != LightType.Directional)
                continue;

            Undo.RecordObject(light, "Atmosfer Kur");
            light.intensity = 0.05f;
            light.shadows = LightShadows.None;
        }
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder("Assets/_Art"))
            AssetDatabase.CreateFolder("Assets", "_Art");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/_Art", "Materials");

        Material material = new Material(Shader.Find("Standard")) { color = color };
        material.enableInstancing = true;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
