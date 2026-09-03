using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 14 lambayı gerçek zamanlıdan pişirilmiş ışığa çevirir ve lightmap'i pişirir.
/// CLAUDE.md bölüm 3'ün açık kalan maddesi.
///
/// **Neden Baked, Mixed değil?** Mixed'in tek faydası dinamik nesnelere gerçek
/// zamanlı direkt ışık ve gölge vermesi. Bu haritada lambaların gölgesi zaten
/// kapalıydı (AtmosphereSetup: "gölgeyi fener veriyor"), yani Mixed hiçbir şey
/// kazandırmaz — sadece 14 ışığı çalışır durumda tutar. Tam Baked'de ışıklar
/// çalışma anında motordan tamamen düşüyor.
///
/// **Neden gölgeleri şimdi AÇIYORUZ?** Ters gibi görünüyor ama tam tersi.
/// Gerçek zamanlı gölgesiz nokta ışık duvarı tanımaz: bugün 8 m menzilli bir
/// lamba yan koridora sızıyor. Pişirmede gölge bir çalışma anı maliyeti değil,
/// sadece "ışık geometriyi görsün mü" anahtarı. Kapalı bırakırsak lightmap de
/// duvarların içinden geçen ışıkla pişer. Açtığımızda bedava doğru gölge
/// alıyoruz — pişmiş ışığın gölgesi çalışma anında sıfır.
///
/// **En büyük tuzak: oyuncular static değil.** Bütün ışık lightmap'e girerse
/// hareket eden hiçbir şey ondan pay almaz; ortam ışığı 0.018 olduğu için kaçan
/// da canavar da simsiyah kesilir. Işık probe'ları tam bu yüzden var ve bu araç
/// onları da kuruyor. Fener gerçek zamanlı kalıyor, bilerek: oyuncuyu aydınlatan
/// ve dinamik gölge düşüren tek kaynak o.
///
/// Menü: Yakalamaca > Işığı Pişir (lightmap)
/// </summary>
public class LightBakeWindow : EditorWindow
{
    private const string MapName = "Harita";
    private const string ProbeGroupName = "IsikProbelari";
    private const string PropGroupName = "Suslemeler";
    private const string CeilingName = "Tavan";
    private const string FloorName = "Zemin";
    private const string MeshFolder = "Assets/_Art/Meshes";
    private const string SettingsFolder = "Assets/_ScriptableObjects";
    private const string SettingsPath = SettingsFolder + "/Yakalamaca_Lightmap.lighting";

    [SerializeField]
    [Tooltip("Metre başına texel. Unity varsayılanı 40; 54.4 m'lik bir harita için bu " +
        "atlası patlatır. Bu oyunda ışık zaten loş ve yumuşak, keskin detay yok — " +
        "6 texel/m gölge geçişlerini taşımaya yetiyor. Pişirme süresi bu sayının " +
        "karesiyle artar; önce 4 ile deneyip beğenirsen yükselt.")]
    private float lightmapResolution = 6f;

    [SerializeField]
    [Tooltip("Tek lightmap atlasının en büyük boyu. 1024 bırakılırsa 600-800 parçalık " +
        "harita düzinelerce atlasa bölünür, her atlas ayrı doku bağlama demek.")]
    private int lightmapMaxSize = 2048;

    [SerializeField]
    [Tooltip("Ekran kartıyla pişir. Kapatırsan CPU kullanılır — çok daha yavaş, ama " +
        "VRAM yetmediğinde Unity zaten kendiliğinden buna düşüyor.")]
    private bool useGpuLightmapper = true;

    [SerializeField]
    [Tooltip("Ortam örtüşmesi. Karanlık koridorda köşelerin kararması derinlik hissinin " +
        "yarısı; pişirmede bedava geliyor.")]
    private bool ambientOcclusion = true;

    [SerializeField]
    [Tooltip("Pişmiş gölgenin kenar yumuşaklığı (metre). 0 keskin, büyük değer dağınık. " +
        "Lambalar tavana yakın küçük kaynaklar, 0.25 civarı doğal duruyor.")]
    private float bakedShadowRadius = 0.25f;

    [Header("Işık probe'ları")]
    [SerializeField]
    [Tooltip("Probe ızgarasının aralığı (metre). Hücre 3.2 m; yarısı olan 1.6, bir " +
        "lambanın altından geçerken ışığın oyuncunun üstünde yumuşak değişmesini " +
        "sağlıyor. Büyütürsen aydınlık basamak basamak geçer.")]
    private float probeSpacing = 1.6f;

    [SerializeField]
    [Tooltip("Probe'ların yerleştirileceği yükseklikler (metre). Lambalar 2.6 m'de, " +
        "oyuncunun gözü 1.22 m'de — dikey değişimi yakalamak için en az üç kat lazım.")]
    private float[] probeHeights = { 0.4f, 1.5f, 2.6f };

    [SerializeField]
    [Tooltip("Probe konulurken bu yarıçapta boşluk aranıyor. Duvarın içinde kalan probe " +
        "simsiyah olur ve karanlığı çevresindeki oyunculara taşır.")]
    private float probeClearance = 0.35f;

    [Header("Atlas payı")]
    [SerializeField]
    [Tooltip("Süslemelerin (varil, kasa) lightmap payı. Kimse varilin üstündeki ışık " +
        "geçişine bakmıyor; yarıya indirmek atlası duvarlara bırakıyor.")]
    private float propLightmapScale = 0.5f;

    [SerializeField]
    [Tooltip("Tavanın lightmap payı. 54.4 m kare tek parça ve karanlıkta yalnızca loş " +
        "bir düzlem olarak görünüyor — tam çözünürlük israf.")]
    private float ceilingLightmapScale = 0.4f;

    private SerializedObject serialized;
    private Vector2 scroll;
    private string report = "Henüz denetlenmedi.";

    [MenuItem("Yakalamaca/Işığı Pişir (lightmap)")]
    private static void Open()
    {
        GetWindow<LightBakeWindow>("Işığı Pişir").minSize = new Vector2(420f, 520f);
    }

    [MenuItem("Yakalamaca/Işığı Pişir (lightmap)", true)]
    private static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

    private void OnEnable()
    {
        serialized = new SerializedObject(this);
    }

    private void OnGUI()
    {
        serialized.Update();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "Sırayla 1-2-3-4 çalıştır, sonra pişir. Her adım tekrar çalıştırılabilir; " +
            "hiçbiri iki kez uygulanınca bozulmuyor.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ayarlar", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serialized.FindProperty("lightmapResolution"));
        EditorGUILayout.PropertyField(serialized.FindProperty("lightmapMaxSize"));
        EditorGUILayout.PropertyField(serialized.FindProperty("useGpuLightmapper"));
        EditorGUILayout.PropertyField(serialized.FindProperty("ambientOcclusion"));
        EditorGUILayout.PropertyField(serialized.FindProperty("bakedShadowRadius"));
        EditorGUILayout.PropertyField(serialized.FindProperty("probeSpacing"));
        EditorGUILayout.PropertyField(serialized.FindProperty("probeHeights"), true);
        EditorGUILayout.PropertyField(serialized.FindProperty("probeClearance"));
        EditorGUILayout.PropertyField(serialized.FindProperty("propLightmapScale"));
        EditorGUILayout.PropertyField(serialized.FindProperty("ceilingLightmapScale"));

        serialized.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Denetle (hiçbir şeyi değiştirmez)"))
            report = Audit();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hazırlık", EditorStyles.boldLabel);

        if (GUILayout.Button("1 · Lightmap UV'lerini üret"))
            report = GenerateLightmapUvs();

        if (GUILayout.Button("2 · Işıkları Baked'e çevir"))
            report = ConvertLights();

        if (GUILayout.Button("3 · Işık probe'larını yerleştir"))
            report = BuildProbeGrid();

        if (GUILayout.Button("4 · Pişirme ayarlarını uygula"))
            report = ApplyBakeSettings();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pişirme", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(Lightmapping.isRunning))
        {
            if (GUILayout.Button("1-4'ü yap ve PİŞİR", GUILayout.Height(32f)))
                RunAllAndBake();

            if (GUILayout.Button("Sadece pişir"))
                StartBake();
        }

        if (Lightmapping.isRunning)
        {
            EditorGUILayout.LabelField($"Pişiyor… %{Lightmapping.buildProgress * 100f:0}");
            if (GUILayout.Button("İptal"))
                Lightmapping.Cancel();

            Repaint();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ekstra", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Occlusion culling ayrı bir pişirme. Occluder/Occludee bayrakları harita " +
            "kurulurken zaten atanıyor ama pişirilmediği sürece hiçbir işe yaramıyor: " +
            "şu an duvarın arkasındaki her şey de çiziliyor.", MessageType.None);

        using (new EditorGUI.DisabledScope(StaticOcclusionCulling.isRunning))
        {
            if (GUILayout.Button("Occlusion culling'i pişir"))
                StaticOcclusionCulling.GenerateInBackground();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Geri alma", EditorStyles.boldLabel);
        if (GUILayout.Button("Pişirmeyi sil, ışıkları gerçek zamanlıya döndür"))
            report = RevertToRealtime();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rapor", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(report, GUILayout.MinHeight(160f));

        EditorGUILayout.EndScrollView();
    }

    // ---------- Denetim ----------

    /// <summary>
    /// Sahnenin pişirmeye hazır olup olmadığını yazar. Hiçbir şeye dokunmaz;
    /// amacı "neden simsiyah pişti" sorusunu pişirmeden ÖNCE cevaplamak.
    /// </summary>
    private string Audit()
    {
        StringBuilder text = new StringBuilder();
        List<MeshRenderer> giRenderers = CollectGiRenderers();

        text.AppendLine($"Lightmap'e girecek renderer: {giRenderers.Count}");

        int missingUv = CollectModelsMissingUv2(giRenderers).Count;
        text.AppendLine(missingUv == 0
            ? "Model lightmap UV'si: tamam"
            : $"Model lightmap UV'si EKSİK: {missingUv} dosya — adım 1 gerekli");

        int primitives = CollectPrimitiveFilters(giRenderers).Count;
        text.AppendLine(primitives == 0
            ? "Primitif küp: yok"
            : $"Primitif küp (UV üretilecek): {primitives} adet — adım 1 gerekli");

        int unlit = giRenderers.Count(IsUnlit);
        if (unlit > 0)
        {
            text.AppendLine($"Işıktan etkilenmeyen materyalli renderer: {unlit} — " +
                "atlas israfı, adım 4 bunları listeden çıkarır");
        }

        Light[] lights = CollectSceneLights();
        int notBaked = lights.Count(light => light.lightmapBakeType != LightmapBakeType.Baked);
        int shadowless = lights.Count(light => light.shadows == LightShadows.None);

        string lightState = notBaked > 0 ? $"{notBaked} tanesi hâlâ pişirilmiyor" : "hepsi Baked";
        text.AppendLine($"Sahne ışığı (fener hariç): {lights.Length} — {lightState}");

        if (shadowless > 0)
        {
            text.AppendLine($"  UYARI: {shadowless} ışığın gölgesi kapalı — pişirirsen " +
                "ışık duvarların içinden geçer. Adım 2 düzeltiyor.");
        }

        LightProbeGroup probes = Object.FindObjectOfType<LightProbeGroup>();
        text.AppendLine(probes == null
            ? "Işık probe'ı: YOK — pişirirsen oyuncular simsiyah olur. Adım 3 gerekli."
            : $"Işık probe'ı: {probes.probePositions.Length} adet");

        text.AppendLine(Lightmapping.lightingDataAsset == null
            ? "Pişmiş ışık verisi: yok"
            : "Pişmiş ışık verisi: var");

        return text.ToString();
    }

    // ---------- 1. Lightmap UV'leri ----------

    /// <summary>
    /// Lightmap'in ilk şartı, her mesh'in üst üste binmeyen ikinci bir UV setine
    /// sahip olması. İki ayrı kaynak var ve ikisi farklı çözüm istiyor:
    ///
    /// **SciFi Kit FBX'leri**: import ayarında `generateSecondaryUV` kapalı
    /// geliyor (83 dosyanın hepsinde). Unity'nin kendi unwrap'ını açıp yeniden
    /// import ediyoruz.
    ///
    /// **Primitif küpler** (zemin, tavan, eğilme geçitleri): mesh yerleşik
    /// kaynak, import ayarı yok — üstelik yerleşik küpün UV'sinde altı yüz aynı
    /// kareye biniyor, yani tavanın karanlık dışı ile aydınlık içi aynı texel'i
    /// paylaşırdı. Kopyasını çıkarıp `Unwrapping` ile ayrık ada üretiyor,
    /// kopyayı varlık olarak kaydediyoruz. Çarpışma BoxCollider'dan geldiği için
    /// mesh'i değiştirmek hiçbir şeyi bozmuyor.
    /// </summary>
    private string GenerateLightmapUvs()
    {
        List<MeshRenderer> giRenderers = CollectGiRenderers();
        StringBuilder text = new StringBuilder();

        List<string> models = CollectModelsNeedingImport(giRenderers);

        try
        {
            for (int i = 0; i < models.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Lightmap UV", models[i],
                    (float)i / Mathf.Max(1, models.Count));

                if (!(AssetImporter.GetAtPath(models[i]) is ModelImporter importer))
                    continue;

                importer.generateSecondaryUV = true;

                // Manuel pay yerine hesaplanan pay: Unity hedef lightmap
                // çözünürlüğünü bilince adalar arası boşluğu kendi ayarlıyor.
                // Düşük çözünürlükte elle verilen sabit pay ya sızdırıyor ya da
                // atlası israf ediyor.
                importer.secondaryUVMarginMethod = ModelImporterSecondaryUVMarginMethod.Calculate;
                importer.secondaryUVMinLightmapResolution = lightmapResolution;
                importer.secondaryUVMinObjectScale = 1f;
                importer.SaveAndReimport();
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        text.AppendLine($"{models.Count} model dosyasına lightmap UV'si üretildi.");

        List<MeshFilter> primitives = CollectPrimitiveFilters(giRenderers);
        Dictionary<Mesh, Mesh> unwrapped = new Dictionary<Mesh, Mesh>();
        int replaced = 0;

        foreach (MeshFilter filter in primitives)
        {
            Mesh source = filter.sharedMesh;
            if (source == null)
                continue;

            if (!unwrapped.TryGetValue(source, out Mesh copy))
            {
                copy = CreateUnwrappedCopy(source);
                unwrapped[source] = copy;
            }

            if (copy == null)
                continue;

            Undo.RecordObject(filter, "Lightmap UV");
            filter.sharedMesh = copy;
            replaced++;
        }

        int created = unwrapped.Values.Count(mesh => mesh != null);
        text.AppendLine($"{replaced} primitif nesnenin mesh'i UV'li kopyayla değiştirildi " +
            $"({created} yeni mesh varlığı).");

        AssetDatabase.SaveAssets();
        SaveScene();

        Debug.Log(text.ToString());
        return text.ToString();
    }

    /// <summary>
    /// Yerleşik mesh'in ayrık lightmap adalarına sahip bir kopyasını üretip
    /// <c>Assets/_Art/Meshes</c> altına kaydeder. Aynı mesh için ikinci kez
    /// çağrılırsa var olanı döndürür.
    /// </summary>
    private static Mesh CreateUnwrappedCopy(Mesh source)
    {
        EnsureFolder(MeshFolder);
        string path = $"{MeshFolder}/{source.name}_LightmapUV.asset";

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
            return existing;

        try
        {
            Mesh copy = Object.Instantiate(source);
            copy.name = $"{source.name}_LightmapUV";

            UnwrapParam.SetDefaults(out UnwrapParam param);

            // Küpün yüzleri 90°; hardAngle 88 her yüzü ayrı adaya bölüyor.
            param.hardAngle = 88f;

            // Çözünürlük düşük olduğu için adalar arası pay varsayılandan geniş,
            // yoksa tavanın karanlık dış yüzü aydınlık iç yüzüne sızıyor.
            param.packMargin = 0.01f;

            Unwrapping.GenerateSecondaryUVSet(copy, param);

            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }
        catch (System.Exception error)
        {
            Debug.LogWarning($"'{source.name}' için lightmap UV'si üretilemedi: {error.Message}");
            return null;
        }
    }

    // ---------- 2. Işıklar ----------

    private string ConvertLights()
    {
        Light[] lights = CollectSceneLights();

        foreach (Light light in lights)
        {
            Undo.RecordObject(light, "Işığı Pişir");
            light.lightmapBakeType = LightmapBakeType.Baked;

            // Gölge artık çalışma anı maliyeti değil, pişirmede "ışık duvarı
            // görsün mü" anahtarı. Kapalı bırakmak lambayı yan koridora sızdırır.
            light.shadows = LightShadows.Soft;

            if (light.type == LightType.Point || light.type == LightType.Spot)
                light.shadowRadius = bakedShadowRadius;

            EditorUtility.SetDirty(light);
        }

        SaveScene();

        string text =
            $"{lights.Length} ışık Baked'e çevrildi, gölgeleri açıldı " +
            $"(yumuşaklık {bakedShadowRadius} m).\n" +
            "Fener bilerek atlandı: oyuncuyu aydınlatan ve dinamik gölge düşüren tek " +
            "kaynak o, gerçek zamanlı kalmalı.";

        Debug.Log(text);
        return text;
    }

    // ---------- 3. Işık probe'ları ----------

    /// <summary>
    /// Labirentin yürünebilir hücrelerine probe ızgarası kurar.
    ///
    /// Yürünebilirlik flood fill'den değil fizikten okunuyor: ızgara noktasına
    /// küre sığıyorsa orası koridor. AtmosphereSetup lambaları da böyle
    /// yerleştiriyor — labirent nasıl üretilmiş olursa olsun (giydirme, prop,
    /// elle eklenmiş duvar) doğru sonuç veriyor.
    /// </summary>
    private string BuildProbeGrid()
    {
        GameObject map = GameObject.Find(MapName);
        if (map == null)
            return "Harita yok. Önce Yakalamaca > Labirent Harita Kur çalıştır.";

        if (probeHeights == null || probeHeights.Length == 0)
            return "En az bir probe yüksekliği gerekli.";

        Transform existing = map.transform.Find(ProbeGroupName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject probeObject = new GameObject(ProbeGroupName);
        probeObject.transform.SetParent(map.transform, false);
        Undo.RegisterCreatedObjectUndo(probeObject, "Işık Probe'ları");

        LightProbeGroup group = probeObject.AddComponent<LightProbeGroup>();

        // Ringing: az sayıda parlak kaynağın küresel harmonikte yarattığı negatif
        // salınım. Tek tük parlak lambanın olduğu karanlık sahne tam olarak bunun
        // çıktığı durum.
        group.dering = true;

        float span = FindMapSpan(map);
        float half = span / 2f - probeSpacing / 2f;
        List<Vector3> positions = new List<Vector3>();

        for (float x = -half; x <= half; x += probeSpacing)
        {
            for (float z = -half; z <= half; z += probeSpacing)
            {
                foreach (float y in probeHeights)
                {
                    Vector3 world = new Vector3(x, y, z);
                    if (Physics.CheckSphere(world, probeClearance, ~0, QueryTriggerInteraction.Ignore))
                        continue;

                    positions.Add(probeObject.transform.InverseTransformPoint(world));
                }
            }
        }

        group.probePositions = positions.ToArray();
        EditorUtility.SetDirty(group);
        SaveScene();

        string text =
            $"{positions.Count} ışık probe'ı yerleştirildi ({probeSpacing} m aralık, " +
            $"{probeHeights.Length} kat).\n" +
            "Duvar içinde kalanlar fizikle elendi. Oyuncular pişirmeden sonra ışığı " +
            "buradan alacak.";

        Debug.Log(text);
        return text;
    }

    // ---------- 4. Pişirme ayarları ----------

    private string ApplyBakeSettings()
    {
        LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
        if (settings == null)
        {
            EnsureFolder(SettingsFolder);
            settings = new LightingSettings();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        // autoGenerate kapalı olmazsa Unity her sahne değişikliğinde yeniden
        // pişirmeye kalkıyor ve editör kullanılamaz hâle geliyor.
        settings.autoGenerate = false;
        settings.bakedGI = true;

        // Realtime GI (Enlighten) kapalı: burada hiçbir şey hareket etmiyor,
        // ışıklar sabit — çalışma anı GI'ın vereceği bir şey yok.
        settings.realtimeGI = false;

        settings.lightmapper = useGpuLightmapper
            ? LightingSettings.Lightmapper.ProgressiveGPU
            : LightingSettings.Lightmapper.ProgressiveCPU;

        settings.lightmapResolution = lightmapResolution;
        settings.lightmapMaxSize = lightmapMaxSize;
        settings.lightmapPadding = 4;
        settings.lightmapCompression = LightmapCompression.NormalQuality;

        settings.ao = ambientOcclusion;
        settings.aoMaxDistance = 1f;
        settings.aoExponentDirect = 0f;    // direkt ışığı karartmak sahneyi çamurlaştırıyor
        settings.aoExponentIndirect = 1f;

        settings.directSampleCount = 32;
        settings.indirectSampleCount = 256;
        settings.environmentSampleCount = 256;
        settings.minBounces = 1;
        settings.maxBounces = 2;

        settings.filteringMode = LightingSettings.FilterMode.Auto;

        // OpenImage her ekran kartında çalışıyor; Optix yalnızca NVIDIA'da.
        settings.denoiserTypeDirect = LightingSettings.DenoiserType.OpenImage;
        settings.denoiserTypeIndirect = LightingSettings.DenoiserType.OpenImage;
        settings.denoiserTypeAO = LightingSettings.DenoiserType.OpenImage;

        // Probe'lar oyuncunun tek ışık kaynağı; onlarda cimrilik yapmıyoruz.
        settings.lightProbeSampleCountMultiplier = 4f;

        EditorUtility.SetDirty(settings);
        Lightmapping.lightingSettings = settings;

        int trimmed = TrimUnlitFromGi();
        int scaled = ApplyLightmapScales();

        AssetDatabase.SaveAssets();
        SaveScene();

        string text =
            $"Pişirme ayarları uygulandı ({SettingsPath}).\n" +
            $"Çözünürlük {lightmapResolution} texel/m, atlas {lightmapMaxSize}, " +
            $"{(useGpuLightmapper ? "GPU" : "CPU")} lightmapper, " +
            $"AO {(ambientOcclusion ? "açık" : "kapalı")}.\n" +
            $"{trimmed} ışıktan etkilenmeyen renderer lightmap'ten çıkarıldı.\n" +
            $"{scaled} renderer'ın atlas payı ayarlandı.";

        Debug.Log(text);
        return text;
    }

    /// <summary>
    /// Sprites/Default ve Unlit shader'lı yüzeyler ışıktan etkilenmiyor (terminal
    /// göstergesi, çıkış kapısı — CLAUDE.md bölüm 11.2'deki karanlık uyarısı).
    /// Bunlara lightmap ayırmak boşa atlas harcamak.
    /// </summary>
    private static int TrimUnlitFromGi()
    {
        int count = 0;

        foreach (MeshRenderer renderer in CollectGiRenderers())
        {
            if (!IsUnlit(renderer))
                continue;

            GameObject target = renderer.gameObject;
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(target);

            Undo.RecordObject(target, "Pişirme Ayarları");
            GameObjectUtility.SetStaticEditorFlags(target, flags & ~StaticEditorFlags.ContributeGI);

            Undo.RecordObject(renderer, "Pişirme Ayarları");
            renderer.receiveGI = ReceiveGI.LightProbes;
            EditorUtility.SetDirty(renderer);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Atlas sonlu bir kaynak: bir yere verdiğin texel'i başka yerden alıyorsun.
    /// Süslemeler ve tavan, oyuncunun ışık kalitesine baktığı yüzeyler değil.
    /// </summary>
    private int ApplyLightmapScales()
    {
        GameObject map = GameObject.Find(MapName);
        if (map == null)
            return 0;

        int count = 0;

        Transform props = map.transform.Find(PropGroupName);
        if (props != null)
            count += SetScaleInLightmap(props, propLightmapScale);

        Transform ceiling = map.transform.Find(CeilingName);
        if (ceiling != null)
            count += SetScaleInLightmap(ceiling, ceilingLightmapScale);

        return count;
    }

    private static int SetScaleInLightmap(Transform root, float scale)
    {
        int count = 0;

        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            Undo.RecordObject(renderer, "Pişirme Ayarları");
            renderer.scaleInLightmap = scale;
            EditorUtility.SetDirty(renderer);
            count++;
        }

        return count;
    }

    // ---------- Pişirme ----------

    private void RunAllAndBake()
    {
        report = GenerateLightmapUvs() + "\n\n"
            + ConvertLights() + "\n\n"
            + BuildProbeGrid() + "\n\n"
            + ApplyBakeSettings();

        StartBake();
    }

    private static void StartBake()
    {
        if (Lightmapping.isRunning)
            return;

        Lightmapping.bakeCompleted -= OnBakeCompleted;
        Lightmapping.bakeCompleted += OnBakeCompleted;
        Lightmapping.BakeAsync();
    }

    private static void OnBakeCompleted()
    {
        Lightmapping.bakeCompleted -= OnBakeCompleted;

        long bytes = 0;
        foreach (LightmapData data in LightmapSettings.lightmaps)
        {
            if (data.lightmapColor != null)
                bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(data.lightmapColor);
        }

        SaveScene();

        Debug.Log(
            $"Lightmap pişti: {LightmapSettings.lightmaps.Length} atlas, " +
            $"{bytes / (1024f * 1024f):0.#} MB. Sahne kaydedildi.\n\n" +
            "Kontrol listesi:\n" +
            "· Stats penceresinde Lights sayısı düşmüş olmalı (fener kaldı).\n" +
            "· Fener kapalıyken lambaların altı hâlâ aydınlık olmalı.\n" +
            "· Lambanın yanındaki duvarın ARKASI artık karanlık olmalı — gölge " +
            "pişirmeden önce sızıyordu.\n" +
            "· Oyuncu simsiyahsa probe'lar eksik demektir (adım 3).");
    }

    // ---------- Geri alma ----------

    private string RevertToRealtime()
    {
        Lightmapping.Clear();
        Lightmapping.ClearLightingDataAsset();

        foreach (Light light in CollectSceneLights())
        {
            Undo.RecordObject(light, "Pişirmeyi Geri Al");
            light.lightmapBakeType = LightmapBakeType.Realtime;

            // Gerçek zamanlıya dönerken gölge yeniden pahalı; kapatıyoruz.
            light.shadows = LightShadows.None;
            EditorUtility.SetDirty(light);
        }

        SaveScene();

        string text =
            "Pişmiş ışık verisi silindi, ışıklar gerçek zamanlıya döndü " +
            "(gölgeler yeniden kapatıldı).\n" +
            "Lightmap UV'leri ve probe'lar duruyor — tekrar pişirmek istersen " +
            "adım 1 ve 3'e gerek yok.";

        Debug.Log(text);
        return text;
    }

    // ---------- Toplama yardımcıları ----------

    /// <summary>Lightmap'e gerçekten girecek renderer'lar: ContributeGI + açık.</summary>
    private static List<MeshRenderer> CollectGiRenderers()
    {
        List<MeshRenderer> result = new List<MeshRenderer>();

        foreach (MeshRenderer renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            // Giydirilmiş küplerin renderer'ı kapalı: MapDressWindow üstünü
            // kaplıyor, çarpışmayı bırakıyor. Kapalı renderer pişmiyor.
            if (!renderer.enabled)
                continue;

            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            if ((flags & StaticEditorFlags.ContributeGI) == 0)
                continue;

            result.Add(renderer);
        }

        return result;
    }

    /// <summary>Fener hariç sahnedeki ışıklar.</summary>
    private static Light[] CollectSceneLights()
    {
        return Object.FindObjectsOfType<Light>()
            .Where(light => light.GetComponentInParent<Flashlight>() == null)
            .Where(light => !light.gameObject.name.Contains("Fener"))
            .ToArray();
    }

    /// <summary>Lightmap UV'si hiç üretilmemiş model dosyalarının yolları.</summary>
    private static List<string> CollectModelsMissingUv2(List<MeshRenderer> renderers)
    {
        return CollectModels(renderers, importer => !importer.generateSecondaryUV);
    }

    /// <summary>
    /// Yeniden import edilmesi gereken modeller: UV'si hiç yok, ya da UV'si
    /// başka bir lightmap çözünürlüğüne göre üretilmiş.
    ///
    /// İkinci şart önemli: adalar arası payı Unity hedef çözünürlüğe bakarak
    /// hesaplıyor. Çözünürlüğü sonradan düşürüp yeniden pişirirsen eski dar pay
    /// sızdırmaya başlar, bu da modelleri tekrar import etmeden fark edilmesi
    /// zor bir hata olurdu.
    /// </summary>
    private List<string> CollectModelsNeedingImport(List<MeshRenderer> renderers)
    {
        return CollectModels(renderers, importer =>
            !importer.generateSecondaryUV ||
            !Mathf.Approximately(importer.secondaryUVMinLightmapResolution, lightmapResolution));
    }

    private static List<string> CollectModels(List<MeshRenderer> renderers,
        System.Func<ModelImporter, bool> predicate)
    {
        HashSet<string> paths = new HashSet<string>();

        foreach (MeshRenderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;

            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                continue;

            if (AssetImporter.GetAtPath(path) is ModelImporter importer && predicate(importer))
                paths.Add(path);
        }

        return paths.ToList();
    }

    /// <summary>
    /// Yerleşik primitif mesh kullanan filtreler. Bunlarda import ayarı yok,
    /// mesh'in kendisini değiştirmemiz gerekiyor.
    ///
    /// Bir kez değiştirilenler artık `Assets/` altında bir varlığı gösterdiği
    /// için ikinci çalıştırmada eleniyor — araç tekrar tekrar çalıştırılabiliyor.
    /// </summary>
    private static List<MeshFilter> CollectPrimitiveFilters(List<MeshRenderer> renderers)
    {
        List<MeshFilter> result = new List<MeshFilter>();

        foreach (MeshRenderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;

            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/"))
                continue;

            result.Add(filter);
        }

        return result;
    }

    /// <summary>
    /// Materyal ışıktan etkilenmiyor mu. Sprites/Default ve Unlit ailesi
    /// karanlıkta okunabilsin diye bilerek seçilmişti (CLAUDE.md bölüm 11.2).
    /// </summary>
    private static bool IsUnlit(Renderer renderer)
    {
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null || material.shader == null)
                continue;

            string name = material.shader.name;
            if (name.StartsWith("Sprites/") || name.StartsWith("Unlit/") || name.StartsWith("UI/"))
                return true;
        }

        return false;
    }

    private static float FindMapSpan(GameObject map)
    {
        Transform floor = map.transform.Find(FloorName);
        return floor != null ? floor.localScale.x : 54.4f;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    /// <summary>
    /// Kurulum araçları sahneyi kendileri kaydeder — etmezlerse Unity kapanınca
    /// kurulum geri gider. CLAUDE.md bölüm 7.
    /// </summary>
    private static void SaveScene()
    {
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }
}
