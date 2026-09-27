using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menünün arkasındaki karakter sahnesini kurar. `Menü Kur` çağırıyor.
///
/// Sahne haritadan **200 metre aşağıda** duruyor. Ayrı bir katman açmak yerine
/// uzaklık kullanmanın sebebi: projede dört katman var (bölüm 16) ve beşinci
/// bir katman `LayerSetup`'ı, maskeleri ve çarpışma matrisini ilgilendirirdi.
/// Uzaklık aynı işi hiçbir şeye dokunmadan yapıyor — kameranın görüş alanına
/// haritadan hiçbir şey girmiyor.
///
/// **Kök KAPALI kuruluyor.** Işıkları ve kamerası menü açılana kadar motorda
/// hiç görünmesin diye; `MenuStage` gerektiğinde açıyor.
/// </summary>
internal static class MenuStageSetup
{
    // ---------------------------------------------------------------------
    // AYARLANACAK SAYILAR BURADA
    //
    // Işık şiddetleri. Oynanınca "çok parlak, kaçanın sarısı beyaz görünüyor"
    // diye geldi ve haklıydı: ilk değerler (9 / 7 / 2.2) karakterin üstünde
    // toplam ~3.4 birim aydınlık üretiyordu. Built-in'de HDR kapalı, yani 1'in
    // üstündeki her şey doğrudan beyaza kırpılıyor — muz sarısı (1, 0.85, 0.2)
    // üçle çarpılınca kırmızı ve yeşil kanalı taşıp beyaz kalıyor.
    //
    // Hedef toplam **1'in biraz altı**: renk doygun kalıyor ama karakter
    // karanlıkta kaybolmuyor.
    //
    // Değiştirmek için: bu sayıları düzelt, sonra `Yakalamaca > Menü Kur`.
    // Denemek için daha hızlı yol: Play'e bas, Hierarchy'den
    // `MenuSahnesi > Isik_Anahtar` seç ve Inspector'dan Intensity'yi oynat —
    // canlı görürsün, ama Play bitince kaybolur, beğendiğin sayıyı buraya yaz.
    // ---------------------------------------------------------------------
    private const float KeyIntensity = 2.2f;     // anahtar ışık (yüzü aydınlatan)
    private const float RimIntensity = 1.8f;     // kenar ışığı (kırmızı, siluet)
    private const float FillIntensity = 0.6f;    // dolgu (gölgeleri açan)
    private const float RoomIntensity = 0.8f;    // odayı gösteren geniş ışık

    private static readonly Vector3 StagePosition = new Vector3(0f, -200f, 0f);

    /// <summary>Labirent hücre ölçüsü — oda karoları aynı boyda olsun diye.</summary>
    private const float Tile = 3.2f;

    private const string KitRoot = "Assets/SciFi Warehouse Kit/Prefabs/Structures";

    internal static void Build()
    {
        // Kendi grubunu yeniden kuran güvenli araç deseni (bölüm 0): yalnızca
        // kendi kökünü siliyor, haritaya ve menüye dokunmuyor.
        // `GameObject.Find` KULLANILAMIYOR: kök bilerek kapalı kuruluyor ve o
        // metot yalnızca açık objeleri buluyor. Kullanılsaydı ikinci
        // çalıştırmada eskisi bulunamaz ve sahnede İKİ `MenuSahnesi` kalırdı —
        // ikisi de aynı adı taşıdığı için `MenuStage` hangisini bulacağını
        // bilemezdi. Aynı tuzağa `MenuStage.OnEnable` da düşmüştü.
        foreach (GameObject sceneRoot in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (sceneRoot.name == MenuStage.StageName)
                Object.DestroyImmediate(sceneRoot);
        }

        GameObject root = new GameObject(MenuStage.StageName);
        root.transform.position = StagePosition;
        Undo.RegisterCreatedObjectUndo(root, "Menü Kur");

        BuildCamera(root.transform);
        BuildLights(root.transform);
        BuildRoom(root.transform);

        GameObject turntable = new GameObject("Doner");
        turntable.transform.SetParent(root.transform, false);

        // Kaçan solda, canavar sağda. DURUŞ burada verilmiyor: `MenuStage`
        // her karede yazıyor (kameraya dönük duruş + salınım + seçim
        // ekranındaki fare dönüşü). İki yerde tutulan bir açı, biri
        // değişince öbürünün unutulması demekti.
        // Kaçanın HER kostümü aynı noktaya kuruluyor; `MenuStage` yalnızca
        // seçili olanı açıyor. Tek figür koyup modelini çalışma anında
        // değiştirmek mümkün değil: her modelin kendi iskeleti ve kendi
        // animatörü var, prefab örneğini yerinde dönüştürmenin yolu yok.
        for (int i = 0; i < CharacterCatalog.Runners.Length; i++)
        {
            // Her kostüm KENDİ denetleyicisini kullanıyor: önizlemede de
            // kendi boşta durma klibini oynasın. Ortak denetleyici verilseydi
            // menüde herkes aynı duruşta beklerdi.
            AddCharacter(turntable.transform, $"Kacan_{i}", CharacterCatalog.Runners[i].ModelPath,
                RunnerSetup.ControllerPathFor(i), new Vector3(-0.62f, 0f, 0f), 1.40f);
        }

        // Canavarın da HER kostümü kuruluyor — kaçandaki desenin aynısı.
        // Tek figür koyup modelini çalışma anında değiştirmek mümkün değil:
        // her modelin kendi iskeleti ve kendi animatörü var.
        for (int i = 0; i < CharacterCatalog.Monsters.Length; i++)
        {
            AddCharacter(turntable.transform, $"Canavar_{i}",
                CharacterCatalog.Monsters[i].ModelPath,
                MonsterSetup.ControllerPathFor(i), new Vector3(0.68f, 0f, 0.25f), 1.80f);
        }

        root.SetActive(false);
    }

    private static void BuildCamera(Transform parent)
    {
        GameObject cameraObject = new GameObject("Kamera", typeof(Camera), typeof(MenuStageCamera));
        cameraObject.transform.SetParent(parent, false);

        // Göğüs hizasından, hafif yukarıdan ve **ayaklar kadrajın içinde**:
        // arkada artık zemin var, ayakları kesmek karakteri zeminden koparıp
        // havada duruyormuş gibi gösterirdi. Önceki kadraj (y 1.35, z -3.1)
        // tabanı 0.19 m'de kesiyordu; buradaki 0'ın biraz altında bitiyor.
        cameraObject.transform.localPosition = new Vector3(0f, 1.25f, -3.7f);
        cameraObject.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);

        Camera camera = cameraObject.GetComponent<Camera>();
        camera.fieldOfView = 34f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 25f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.018f, 0.013f, 0.016f, 1f);
        camera.allowHDR = false;
        camera.allowMSAA = true;

        // Kapalı: `MenuStage` hedef dokusunu verip açıyor. Açık bırakılsaydı
        // hedefi olmayan bir kamera doğrudan ekrana çizerdi.
        camera.enabled = false;
    }

    /// <summary>
    /// Üç ışık: anahtar, kenar ve dolgu.
    ///
    /// **Hiçbiri Directional DEĞİL, bilerek.** Yönlü ışığın konumu yok, yani
    /// 200 m öteden bile bütün haritayı aydınlatırdı ve bölüm 5'in "fenersiz
    /// görülmemeli" kuralını tek başına delerdi. Menzilli ışıklar yalnızca
    /// sahneyi görüyor.
    /// </summary>
    private static void BuildLights(Transform parent)
    {
        Light key = CreateLight(parent, "Isik_Anahtar", LightType.Spot,
            new Vector3(-1.9f, 2.9f, -2.3f), new Color(1f, 0.94f, 0.88f), KeyIntensity, 9f);
        key.spotAngle = 62f;
        key.transform.localRotation = Quaternion.LookRotation(
            (new Vector3(-0.25f, 1.1f, 0f) - key.transform.localPosition).normalized);

        // Gölge YALNIZCA anahtar ışıkta açık. Zemin geldiği için karakterin
        // ayağının dibinde bir gölge olması şart: gölgesiz bir figür zeminin
        // üstünde durmuyor, önünde duruyor gibi görünüyor. Üç ışığın üçünde
        // birden açmak aynı sahneyi üç kez çizdirirdi ve kazancı yok —
        // öbür ikisi zaten dolgu.
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.75f;

        // Kenar isigi KIRMIZI: oyunun kimligi (bolum 5'teki canavar halesi) ve
        // iki figuru koyu arka plandan ayiran sey. Arka duvara yakin durdugu
        // icin duvara da kirmizi bir leke dusuruyor - istenen bu.
        CreateLight(parent, "Isik_Kenar", LightType.Point,
            new Vector3(1.7f, 2.0f, 1.9f), new Color(0.95f, 0.26f, 0.2f), RimIntensity, 7f);

        CreateLight(parent, "Isik_Dolgu", LightType.Point,
            new Vector3(1.5f, 1.2f, -2.6f), new Color(0.55f, 0.62f, 0.82f), FillIntensity, 8f);

        // Odayi gosteren genis isik. Figurlerden UZAGA, arka duvara yakin
        // konuyor: oda gorunsun ama karakterin aydinligina fazla eklemesin.
        // Menzili buyuk, siddeti kucuk - yakindaki duvari yikamaya yetiyor.
        CreateLight(parent, "Isik_Oda", LightType.Point,
            new Vector3(0f, 2.8f, 2.0f), new Color(0.62f, 0.66f, 0.78f), RoomIntensity, 10f);
    }

    /// <summary>
    /// Figurlerin arkasina kucuk bir oda kurar: oyunun kendi zemini ve duvari.
    ///
    /// Menunun arkasi duz siyahti ve karakter bosllukta duruyor gibi
    /// gorunuyordu. Parcalar **haritanin kullandigi kit prefablarinin aynisi**
    /// (`Wall Plain`, `Floor Tile 01`), yani menu oyunla ayni yerde geciyormus
    /// gibi duruyor.
    ///
    /// > **Kitin materyali duz bir kupe verilemezdi.** `walls_a.png` bir
    /// > ATLAS: dort ayri panel cesidi tek dokuda, ikisinde pencere boslugu
    /// > var. Bir kupun UV'si 0-1 oldugu icin atlasin tamami tek yuze sikisir
    /// > ve ortaya pencereli, bolunmus bir yuzey cikardi. Prefabin kendi
    /// > UV'leri atlasin dogru kosesini gosteriyor.
    ///
    /// Duvar paneli **kameraya donuk** olmali. `MapDressWindow` panelleri
    /// `LookRotation(normal)` ile koyuyor ve oradaki `normal` duvar hucresinden
    /// oyuncuya dogru bakiyor - yani panelin +Z'si goreni goruyor. Kamera
    /// -Z'de durdugu icin buradaki panel `LookRotation(back)` ile donuyor.
    ///
    /// **Yan duvar ve tavan yok, bilerek.** Kadrajin disinda kaliyorlar:
    /// kameranin en genis acisinda bile gorunen alan zeminin ve duvarin
    /// icinde. Olmayani kurmak bos yere alti parca daha demekti.
    /// </summary>
    private static void BuildRoom(Transform parent)
    {
        GameObject room = new GameObject("Oda");
        room.transform.SetParent(parent, false);

        GameObject floorPrefab = LoadKit("Floor/Floor Tile 01");
        GameObject wallPrefab = LoadKit("Walls/Wall Plain");

        if (floorPrefab == null || wallPrefab == null)
        {
            Debug.LogWarning("Menu sahnesi: SciFi kit parcalari bulunamadi, oda kurulmadi.");
            return;
        }

        // Zemin: 3 x 2 karo. Ust yuzu y = 0'da, yani karakterler ustunde
        // duruyor. Kalinlik olcumden geliyor, tahmin edilmiyor.
        Bounds floorProbe = MeasurePrefab(floorPrefab);
        float floorWidth = Mathf.Max(floorProbe.size.x, floorProbe.size.z);

        if (floorWidth > 0.001f)
        {
            float scale = Tile / floorWidth;
            float thickness = floorProbe.size.y * scale;

            for (int col = -1; col <= 1; col++)
            {
                for (int row = -1; row <= 0; row++)
                {
                    PlaceKitPiece(floorPrefab, room.transform, "Zemin_" + col + "_" + row,
                        new Vector3(col * Tile, -thickness * 0.5f, row * Tile + Tile * 0.5f),
                        Quaternion.identity, scale);
                }
            }
        }

        // Arka duvar: 3 x 2 panel, zeminden yukari yigiliyor.
        Bounds wallProbe = MeasurePrefab(wallPrefab);
        bool thinAlongZ = wallProbe.size.z <= wallProbe.size.x;
        float panelWidth = thinAlongZ ? wallProbe.size.x : wallProbe.size.z;

        if (panelWidth <= 0.001f)
            return;

        float wallScale = Tile / panelWidth;
        float panelHeight = wallProbe.size.y * wallScale;

        Quaternion rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        if (!thinAlongZ)
            rotation *= Quaternion.Euler(0f, -90f, 0f);

        for (int col = -1; col <= 1; col++)
        {
            for (int row = 0; row < 2; row++)
            {
                PlaceKitPiece(wallPrefab, room.transform, "Duvar_" + col + "_" + row,
                    new Vector3(col * Tile, panelHeight * (row + 0.5f), Tile),
                    rotation, wallScale);
            }
        }
    }

    private static GameObject LoadKit(string relativePath)
        => AssetDatabase.LoadAssetAtPath<GameObject>(KitRoot + "/" + relativePath + ".prefab");

    /// <summary>
    /// Kit parcasini yerlestirir: once sahneye koyup GERCEK sinirlarini
    /// olcuyor, sonra merkezini istenen noktaya kaydiriyor. Boylece prefabin
    /// pivotunun nerede oldugunu bilmeye gerek kalmiyor -
    /// `MapDressWindow.Place` ile ayni yontem.
    ///
    /// Collider'lar siliniyor: bu bir arka plan, fizige girmesinin karsiligi
    /// yok ve haritadan 200 m asagida duran kati govdeler kimseye yaramaz.
    /// </summary>
    private static void PlaceKitPiece(GameObject prefab, Transform parent, string name,
        Vector3 localCenter, Quaternion rotation, float scale)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = name;
        instance.transform.SetPositionAndRotation(Vector3.zero, rotation);
        instance.transform.localScale = Vector3.one * scale;

        Bounds bounds = WorldBounds(instance);
        instance.transform.position += parent.TransformPoint(localCenter) - bounds.center;

        foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(collider);
    }

    /// <summary>Prefabin olcek 1, donus sifirken kapladigi yer.</summary>
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

    private static Light CreateLight(Transform parent, string name, LightType type,
        Vector3 position, Color color, float intensity, float range)
    {
        GameObject lightObject = new GameObject(name, typeof(Light));
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localPosition = position;

        Light light = lightObject.GetComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;

#if UNITY_EDITOR
        // Pişirmeye girmemeli: sahne static değil ve zaten menüde açılıyor.
        // `lightmapBakeType` yalnızca editörde var (bölüm 7).
        light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
        return light;
    }

    private static void AddCharacter(Transform parent, string name, string modelPath,
        string controllerPath, Vector3 position, float targetHeight)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogWarning($"Menü sahnesi: {modelPath} bulunamadı, {name} eklenmedi.");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        instance.name = name;
        instance.transform.localPosition = position;

        ScaleToHeight(instance, targetHeight);

        Animator animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
        animator.runtimeAnimatorController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);

        // Kök hareketi KAPALI: oyunda da kapalı (bölüm 17) ve açık olsaydı
        // boştaki klip karakteri yavaşça platformdan kaydırırdı.
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    /// <summary>
    /// Modeli görünen boyuna göre ölçekliyor.
    ///
    /// İki model bambaşka ölçülerde geliyor ve oyundaki ölçekleri oyuncu
    /// kapsülünden hesaplanıyor (bölüm 17) — burada kapsül yok. Renderer
    /// sınırlarından ölçeklemek aynı oranı bağımsızca üretiyor: canavar
    /// kaçandan görünür şekilde iri kalıyor, ki kovalayanın büyük görünmesi
    /// bilinçli bir tasarım kararı.
    /// </summary>
    private static void ScaleToHeight(GameObject instance, float targetHeight)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        if (bounds.size.y <= 0.001f)
            return;

        float scale = targetHeight / bounds.size.y;
        instance.transform.localScale *= scale;
    }
}
