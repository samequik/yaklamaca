using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tek kişilik tutorial koridoru. Menü: Yakalamaca > Tutorial Sahnesi Kur.
///
/// ### Oyunun KENDİ parçalarıyla kuruluyor
///
/// İlk sürüm düz gri kutular ve aydınlık bir yönlü ışıkla kurulmuştu; kullanıcı
/// onu "oyunun çok eski sürümü" diye okudu (CLAUDE.md, 2026-09-16 oturumu).
/// Artık her parça gerçek haritayı kuran kodun AYNISINDAN geçiyor:
///
/// | Parça | Nereden |
/// |---|---|
/// | Duvar/zemin/tavan blokları, eğilme geçidi, kapı + iki düğme | `MazeMapBuilder` ölçüleri ve yardımcıları |
/// | Duvar paneli, zemin/tavan karosu, kapı gövdesi | `MazeExpansionSetup.DressWing` ile aynı kural (`MapDressWindow.Place`) |
/// | Terminal | `ObjectiveSetup.CreateTerminal` |
/// | Çıkış kapısı gövdesi, sahanlık | `ObjectiveSetup.DressExitDoor` / `BuildVestibule` |
/// | Diriltme kabini | `RevivalSetup.BuildStation` |
/// | Lambalar | `WingLightingSetup` ile aynı ışık + "Hanging Light" armatürü |
/// | Sesler | `AudioImportSetup` ile aynı klipler ve kaynak ayarları |
///
/// Koridor bir IZGARA olarak tarif ediliyor (x=2 sütunu koridor), böylece
/// giydirme gerçek haritanın kuralıyla çalışıyor: duvar hücresinin açık bir
/// komşuya bakan her yüzüne bir panel.
///
/// ### Neden ayrı bir SAHNE
///
/// Ana harita elle düzenlendi ve CLAUDE.md bölüm 0 ona dokunmayı yasaklıyor.
///
/// ### Oyuncu haritadan düşüyordu (2026-09-17)
///
/// Üç sebep üst üste biniyordu:
/// - Doğum noktası zeminin 5 cm üstündeydi. Oyuncunun KAPSÜL MERKEZİ oraya
///   konuyor (`CharacterController.center` = 0), yani alt yarısı zeminin içine
///   gömülüyordu. Gerçek haritadaki doğum noktaları 0.7 m'de; burada da öyle.
/// - Zemin 10 cm'lik bir levhaydı; gerçek haritanınki 0.5 m.
/// - Tur başında ölü eğitim botu da oyuncuyla aynı noktaya diziliyordu.
///   `RoundManager.placeBotsAtRoundStart` bu sahnede kapalı.
///
/// Doğum noktasında ayrıca bir `NetworkStartPosition` var: Mirror oyuncuyu tur
/// başlamadan önce de oraya koyuyor. Yoksa dünya merkezinde, yani koridorun
/// 1000 m üstünde doğup tur başlayana kadar boşluğa düşüyordu.
///
/// ### Arayüz: sahnenin KENDİ menüsü var (2026-09-17)
///
/// İlk sürümde tutorial sahnesinde hiç menü canvas'ı yoktu ve bu iki şeyi
/// birden bozuyordu: terminal ile çıkış kilidi ekranları `OyunHud`'ın çocuğu
/// olarak kurulduğu için (bölüm 20) **hiç çizilmiyordu**, ve Esc'ye basınca
/// duraklatma menüsü diye bir şey olmadığı için koridordan ANINDA çıkılıyordu.
///
/// `MenuSetup.BuildTutorialMenu` ana menünün kendi yapıcılarını çağırıp bu
/// sahneye küçük bir sürüm kuruyor: HUD (mini harita hariç), duraklatma,
/// seçenekler, ses ve tuş atamaları. Lobi/katılma/karakter ekranları ve
/// `LobbyNetwork` YOK — tutorial'da oda kurulmuyor.
///
/// ### Ağ
///
/// `NetworkManager` burada KURULMUYOR; menüden DontDestroyOnLoad ile geliyor.
/// Sahne doğrudan açılıp Play'e basılırsa çalışmaz — yalnızca ana menüdeki
/// NASIL OYNANIR ile.
///
/// ### Canavar vitrini
///
/// Gerçek bir AI yok, o yüzden canavarın gerçek modeli camın ardında sabit
/// duruyor (NetworkPlayer prefabından `PlayerBodyVisual.monsterRoot`
/// sökülerek). Amaç "işte böyle görünüyor, şu kurallar geçerli".
/// </summary>
public static class TutorialSetup
{
    private const string ScenePath = "Assets/_Scenes/TutorialScene.unity";
    private const string RootName = "Tutorial";

    /// <summary>İlk sürümün sahne köküne koyduğu yönlü ışık; yeniden kurarken siliniyor.</summary>
    private const string LegacySunName = "YonluIsik";

    /// <summary>
    /// `MenuSetup.BuildTutorialMenu`'nun kurduğu iki kök. `Tutorial` kökünün
    /// ALTINDA değiller (ana sahnede de kök objeler), o yüzden yeniden kurarken
    /// ayrıca siliniyorlar — yoksa her çalıştırmada bir menü canvas'ı daha
    /// birikir ve hangi `MenuController`'ın `Instance` olacağı tanımsız kalırdı.
    /// </summary>
    private const string MenuRootName = "Menu";

    private const string EventSystemName = "EventSystem";

    private const string PlayerPrefabPath = "Assets/_Prefabs/NetworkPlayer.prefab";
    private const string CorpsePrefabPath = "Assets/_Prefabs/Corpse.prefab";
    private const string LampPrefabPath =
        "Assets/SciFi Warehouse Kit/Prefabs/Props/Misc Props/Hanging Light.prefab";
    private const string PropFolder = "Assets/SciFi Warehouse Kit/Prefabs/Props";
    private const string StructurePropFolder = MapDressWindow.KitRoot + "/Structure Props";

    /// <summary>
    /// `Tutorial` kökünün dünya konumu. Araç çalışırken iki sahne kısa süre üst
    /// üste açık kalıyor; ana haritayla aynı yerde kurulsaydı koridor haritanın
    /// içinden geçiyormuş gibi görünürdü. `MenuStageSetup`'ın menü sahnesini
    /// 200 m aşağı koymasıyla aynı çözüm.
    /// </summary>
    private static readonly Vector3 WorldOffset = new Vector3(0f, -1000f, 0f);

    private const float Cell = MazeMapBuilder.CellSize;
    private const float WallHeight = MazeMapBuilder.WallHeight;

    // ---------- Izgara ----------
    //
    // x: 0..4, koridor x=2 (yerel x=0). z: 0 başlangıç duvarı, 16 çıkış gediği.
    // Her istasyonun yazısı bir önceki satırda tetikleniyor.

    private const int GridWidth = 5;
    private const int CorridorX = 2;

    private const int SpawnRow = 1;
    private const int FlashlightRow = 2;
    private const int CrouchCaptionRow = 3;
    private const int CrouchRow = 4;
    private const int DoorCaptionRow = 5;
    private const int DoorRow = 6;
    private const int TerminalCaptionRow = 7;
    private const int TerminalRow = 8;
    private const int MonsterCaptionRow = 9;
    private const int MonsterRow = 10;
    private const int BodyCaptionRow = 11;
    private const int BodyRow = 12;
    private const int RevivalRow = 13;
    private const int ExitCaptionRow = 14;
    private const int ExitLockRow = 15;
    private const int ExitRow = 16;
    private const int GridLength = ExitRow + 1;

    /// <summary>
    /// Lamba satırları. Kapı ve geçit satırlarında lamba YOK: kapı paneli
    /// tavana kayıyor, geçidin üst bloğu da 1.1 m'den tavana kadar dolu.
    /// </summary>
    private static readonly int[] LampRows =
    {
        SpawnRow, CrouchCaptionRow, DoorCaptionRow, TerminalRow, BodyCaptionRow, RevivalRow, ExitLockRow
    };

    /// <summary>Oyuncu kapsülünün merkezi; gerçek haritadaki `Dogum_1..6` ile aynı yükseklik.</summary>
    private const float CapsuleCenterHeight = 0.7f;

    /// <summary>Duvara monte panellerin yüksekliği — `ObjectiveSetup.TerminalHeight` ile aynı.</summary>
    private const float PanelHeight = 1.35f;

    // Işık: WingLightingSetup'ın renk/menzil değerleri. Şiddet biraz yüksek,
    // çünkü bu sahne pişmiyor ve öğretici okunabilir kalmalı.
    private const float LampIntensity = 1.1f;
    private const float LampRange = 8f;
    private const float LampHeight = 2.5f;
    private const float LampVisualWidth = 0.8f;

    [MenuItem("Yakalamaca/Tutorial Sahnesi Kur", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Tutorial Sahnesi Kur")]
    private static void Run()
    {
        Scene previousActive = SceneManager.GetActiveScene();

        // Unity, hiç kaydedilmemiş ("Untitled") bir sahne AÇIKKEN yanına başka
        // bir sahneyi additive açamıyor — istisna atıyor. Kaydedilmemiş bir
        // sahneyi kendiliğinden kaydetmek riskli olurdu; açıkça söylüyoruz.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene open = SceneManager.GetSceneAt(i);

            if (!string.IsNullOrEmpty(open.path))
                continue;

            EditorUtility.DisplayDialog("Önce açık sahneyi kaydet",
                "Açık sahnelerden biri hiç kaydedilmemiş (\"Untitled\"). Unity, " +
                "kaydedilmemiş bir sahne açıkken yanına başka bir sahne ekleyemiyor.\n\n" +
                "O sahneyi kaydet (Ctrl+S) ya da kapat — ya da yalnızca " +
                "Assets/_Scenes/SampleScene.unity açıkken bu aracı tekrar çalıştır.", "Tamam");
            return;
        }

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            EditorUtility.DisplayDialog("Oyuncu prefabı yok",
                $"{PlayerPrefabPath} bulunamadı. Önce Yakalamaca > Ağ Kurulumu (1. adım).", "Tamam");
            return;
        }

        GameObject corpsePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CorpsePrefabPath);
        if (corpsePrefab == null)
        {
            EditorUtility.DisplayDialog("Ceset prefabı yok",
                $"{CorpsePrefabPath} bulunamadı. Önce Yakalamaca > Ceset Sistemini Kur.", "Tamam");
            return;
        }

        Scene tutorialScene = OpenOrCreateScene();

        // Aktif sahne değiştirilemezse DUR. Aşağıdaki her `new GameObject` ve
        // `RenderSettings` yazısı AKTİF sahneye gidiyor: aktif sahne hâlâ
        // SampleScene kalsaydı koridor ve ortam ayarları doğrudan ana oyunun
        // sahnesine yazılır, haritanın karanlığı (CLAUDE.md bölüm 5) bozulurdu.
        if (!SceneManager.SetActiveScene(tutorialScene))
        {
            EditorSceneManager.CloseScene(tutorialScene, true);
            EditorUtility.DisplayDialog("Tutorial sahnesi kurulamadı",
                "Tutorial sahnesi etkin sahne yapılamadı. Ana sahneye hiçbir şey " +
                "yazılmadı; Unity'yi yeniden başlatıp tekrar dene.", "Tamam");
            return;
        }

        // Eski kurulum YALNIZCA tutorial sahnesinin kök nesnelerinde aranıyor,
        // `GameObject.Find` ile değil: o AÇIK OLAN BÜTÜN sahnelerde arıyor ve
        // SampleScene'de aynı adı taşıyan bir obje olsaydı onu silerdi — bölüm
        // 0.1'deki "Duvar_3_0" isim çakışması dersinin aynısı.
        foreach (GameObject existing in tutorialScene.GetRootGameObjects())
        {
            if (existing.name == RootName || existing.name == LegacySunName
                || existing.name == MenuRootName || existing.name == EventSystemName)
                Object.DestroyImmediate(existing);
        }

        GameObject root = new GameObject(RootName);
        root.transform.position = WorldOffset;

        // Ne olursa olsun aşağıdaki kaydet+kapat adımına düşüyoruz: 2026-09-16'da
        // bir istasyonda çıkan istisna sahneyi YARIM, kaydedilmemiş ve AÇIK
        // bırakmıştı. Artık kurulan her şey diske yazılıyor, önceki sahneye
        // dönülüyor ve sonda "yarım kaldı" diye açıkça söyleniyor.
        string failure = null;

        try
        {
            ConfigureAtmosphere();

            Layout layout = CreateLayout();
            Geometry geometry = BuildGeometry(root.transform, layout);
            Dress(root.transform, layout, geometry);

            BuildLamps(root.transform);
            Transform spawn = BuildSpawn(root.transform);

            BuildTerminal(root.transform);
            BuildMonsterShowcase(root.transform, playerPrefab);
            BuildDeadBot(root.transform);
            BuildRevivalStation(root.transform);
            BuildExit(root.transform);
            BuildProps(root.transform);
            BuildCaptions(root.transform);

            BuildRoundManager(root.transform, spawn, corpsePrefab);
            TutorialBootstrap bootstrap = BuildBootstrap(root.transform);

            // Menü EN SONDA: duraklatma ekranındaki çıkış düğmesi bootstrap'e
            // bağlanıyor, yani o obje çoktan var olmalı. Canvas `Tutorial`
            // kökünün altına GİRMİYOR — ana sahnedeki gibi kendi kökü, çünkü
            // koridorun 1000 m aşağıdaki konumu bir ekran arayüzünü
            // ilgilendirmiyor.
            //
            // Bu adım terminal/çıkış kilidi ekranlarını, nişangahı, tur
            // satırlarını ve Esc duraklatma menüsünü getiriyor: üçü de
            // sahnede menü canvas'ı olmadığı için yoktu.
            MenuSetup.BuildTutorialMenu(bootstrap);
        }
        catch (System.Exception exception)
        {
            failure = exception.Message;
            Debug.LogException(exception);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(tutorialScene);
        EditorSceneManager.SaveScene(tutorialScene, ScenePath);

        RegisterInBuildSettings();

        if (previousActive.IsValid())
        {
            SceneManager.SetActiveScene(previousActive);
            EditorSceneManager.CloseScene(tutorialScene, true);
        }

        if (failure != null)
        {
            EditorUtility.DisplayDialog("Tutorial sahnesi YARIM kaldı",
                "Kurulum bir hatayla yarıda kesildi:\n\n" + failure + "\n\n" +
                "Şimdiye kadar kurulanlar yine de " + ScenePath + " içine kaydedildi, " +
                "ama sahne eksik. Bu hatayı bildir; düzeltilince aracı tekrar " +
                "çalıştırmak güvenli ('Tutorial' kökü baştan siliniyor).", "Tamam");
            return;
        }

        Debug.Log(
            "Tutorial sahnesi kuruldu: " + ScenePath + "\n\n" +
            "Koridor gerçek haritanın parçalarıyla kuruldu: SciFi Kit duvar/zemin/tavan, " +
            "gerçek eğilme geçidi, iki düğmeli kapı, terminal, canavar vitrini, " +
            "ölü eğitim botu + diriltme kabini, kilitli çıkış ve asılı lambalar.\n\n" +
            "Arayüz de kuruldu (MenuSetup.BuildTutorialMenu): nişangah, tur satırları, " +
            "TERMİNAL ve ÇIKIŞ KİLİDİ ekranları, Esc duraklatma menüsü (DEVAM ET / " +
            "SEÇENEKLER / TUTORIAL'DAN ÇIK) ve seçenekler + ses + tuş atamaları. " +
            "Mini harita bilerek YOK: duvarları sahneden okuyor ve bu araç " +
            "çalışırken ana harita da açık.\n\n" +
            "Denemek için: Play → ana menü → NASIL OYNANIR. Yalnızca oradan çalışır " +
            "(NetworkManager menüden geliyor).");
    }

    // ---------- Sahne ----------

    private static Scene OpenOrCreateScene()
    {
        if (System.IO.File.Exists(ScenePath))
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        if (!AssetDatabase.IsValidFolder("Assets/_Scenes"))
            AssetDatabase.CreateFolder("Assets", "_Scenes");

        return scene;
    }

    /// <summary>
    /// Oyunun karanlık dili, öğretici olduğu için biraz yumuşatılmış hâliyle.
    /// Gerçek harita: ortam 0.006, sis 0.045 (`AtmosphereSetup`). Burada ortam
    /// ~5 kat, sis daha seyrek — lambasız köşeler yine karanlık, ama oyuncu
    /// yazıları ve istasyonları fenersiz de seçebiliyor.
    /// </summary>
    private static void ConfigureAtmosphere()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.03f, 0.03f, 0.04f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.01f, 0.01f, 0.016f);
        RenderSettings.fogDensity = 0.03f;

        RenderSettings.reflectionIntensity = 0.2f;
    }

    // ---------- Izgara ----------

    private sealed class Layout
    {
        public readonly bool[,] Wall = new bool[GridWidth, GridLength];
        public readonly List<Vector2Int> CrouchCells = new List<Vector2Int>();

        public Vector2Int Door => new Vector2Int(CorridorX, DoorRow);
        public Vector2Int Niche => new Vector2Int(CorridorX + 1, MonsterRow);
        public Vector2Int Alcove => new Vector2Int(CorridorX - 1, RevivalRow);

        public bool Inside(int x, int z) => x >= 0 && z >= 0 && x < GridWidth && z < GridLength;

        /// <summary>Küp olan hücre. Geçit hücresi ızgarada dolu ama küpü yok (`MazeMapBuilder` ile aynı temsil).</summary>
        public bool IsSolid(int x, int z) => Wall[x, z] && !CrouchCells.Contains(new Vector2Int(x, z));

        public bool HasOpenNeighbour(int x, int z)
        {
            return (Inside(x + 1, z) && !IsSolid(x + 1, z))
                || (Inside(x - 1, z) && !IsSolid(x - 1, z))
                || (Inside(x, z + 1) && !IsSolid(x, z + 1))
                || (Inside(x, z - 1) && !IsSolid(x, z - 1));
        }
    }

    private static Layout CreateLayout()
    {
        Layout layout = new Layout();

        for (int x = 0; x < GridWidth; x++)
        {
            for (int z = 0; z < GridLength; z++)
                layout.Wall[x, z] = true;
        }

        for (int z = SpawnRow; z <= ExitRow; z++)
            layout.Wall[CorridorX, z] = false;

        layout.Wall[CorridorX, CrouchRow] = true;
        layout.CrouchCells.Add(new Vector2Int(CorridorX, CrouchRow));

        // Canavar odası sağda, diriltme girintisi solda: ikisi de koridora açık.
        layout.Wall[layout.Niche.x, layout.Niche.y] = false;
        layout.Wall[layout.Alcove.x, layout.Alcove.y] = false;

        return layout;
    }

    /// <summary>Hücre merkezi, `Tutorial` köküne göre (y = zemin üstü).</summary>
    private static Vector3 CellLocal(int x, int z) => new Vector3((x - CorridorX) * Cell, 0f, z * Cell);

    /// <summary>Hücre merkezi, dünya konumu. Kök yalnızca ötelenmiş (dönüş ve ölçek yok).</summary>
    private static Vector3 CellWorld(int x, int z) => WorldOffset + CellLocal(x, z);

    // ---------- Geometri: gerçek haritanın blokları ----------

    private sealed class Geometry
    {
        public Transform Walls;
        public Transform CrouchPassages;
        public Transform Doors;
        public GameObject Floor;
        public GameObject Ceiling;
    }

    private static Geometry BuildGeometry(Transform root, Layout layout)
    {
        // AYNI materyaller: MazeMapBuilder'ın klasöründe zaten varlar, bulunup
        // paylaşılıyorlar. Küplerin görüntüsü giydirmeden sonra zaten kapanıyor.
        Material wallMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Duvar", new Color(0.52f, 0.52f, 0.56f));
        Material floorMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Zemin", new Color(0.34f, 0.34f, 0.38f));
        Material doorMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Kapi", new Color(0.30f, 0.55f, 0.75f));
        Material buttonMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Dugme", new Color(0.90f, 0.35f, 0.25f));
        Material crouchMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Gecit", new Color(0.62f, 0.55f, 0.32f));

        Geometry geometry = new Geometry();

        Vector3 center = new Vector3(
            ((GridWidth - 1) / 2f - CorridorX) * Cell, 0f, (GridLength - 1) * Cell / 2f);
        Vector3 span = new Vector3(GridWidth * Cell, 0.5f, GridLength * Cell);

        // Gerçek haritayla aynı: 0.5 m kalınlık, üst yüzü y=0'da.
        geometry.Floor = MazeMapBuilder.CreateBox("Zemin", root, center + Vector3.down * 0.25f, span, floorMaterial);
        MazeMapBuilder.MarkStatic(geometry.Floor);

        geometry.Ceiling = MazeMapBuilder.CreateBox("Tavan", root,
            center + Vector3.up * (WallHeight + 0.25f), span, wallMaterial);
        MazeMapBuilder.MarkStatic(geometry.Ceiling);

        geometry.Walls = MazeMapBuilder.CreateGroup("Duvarlar", root);

        for (int x = 0; x < GridWidth; x++)
        {
            for (int z = 0; z < GridLength; z++)
            {
                // Her yanı duvarla çevrili hücreye küp gerekmiyor: kimse onu
                // görmüyor, oyuncu oraya ulaşamıyor.
                if (!layout.IsSolid(x, z) || !layout.HasOpenNeighbour(x, z))
                    continue;

                GameObject block = MazeMapBuilder.CreateBox($"Duvar_{x}_{z}", geometry.Walls,
                    CellLocal(x, z) + Vector3.up * (WallHeight / 2f),
                    new Vector3(Cell, WallHeight, Cell), wallMaterial);

                MazeMapBuilder.MarkStatic(block);
            }
        }

        geometry.CrouchPassages = BuildCrouchPassages(root, layout, crouchMaterial);
        geometry.Doors = MazeMapBuilder.CreateGroup("Kapilar", root);
        BuildDoor(geometry.Doors, layout, layout.Door, doorMaterial, buttonMaterial);

        return geometry;
    }

    /// <summary>`MazeMapBuilder.BuildCrouchPassages` ile birebir aynı ölçüler: 1.4 m genişlik, 1.1 m yükseklik.</summary>
    private static Transform BuildCrouchPassages(Transform root, Layout layout, Material material)
    {
        Transform group = MazeMapBuilder.CreateGroup("EgilmeGecitleri", root);

        foreach (Vector2Int cell in layout.CrouchCells)
        {
            bool alongX = !layout.Wall[cell.x - 1, cell.y] && !layout.Wall[cell.x + 1, cell.y];
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 cross = alongX ? Vector3.forward : Vector3.right;

            Transform passage = MazeMapBuilder.CreateGroup($"Gecit_{cell.x}_{cell.y}", group);
            passage.localPosition = CellLocal(cell.x, cell.y);

            float sideWidth = (Cell - MazeMapBuilder.CrouchWidth) / 2f;
            float sideOffset = MazeMapBuilder.CrouchWidth / 2f + sideWidth / 2f;

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject block = MazeMapBuilder.CreateBox($"Yan_{side}", passage,
                    cross * (sideOffset * side) + Vector3.up * (WallHeight / 2f),
                    MazeMapBuilder.AxisSize(axis, Cell, WallHeight, cross, sideWidth), material);

                MazeMapBuilder.MarkStatic(block);
            }

            GameObject top = MazeMapBuilder.CreateBox("Tavan", passage,
                Vector3.up * ((MazeMapBuilder.CrouchHeight + WallHeight) / 2f),
                MazeMapBuilder.AxisSize(axis, Cell, WallHeight - MazeMapBuilder.CrouchHeight,
                    cross, MazeMapBuilder.CrouchWidth),
                material);

            MazeMapBuilder.MarkStatic(top);
        }

        return group;
    }

    /// <summary>
    /// Labirent kapısının aynısı (`MazeMapBuilder.BuildDoors`): koridoru kaplayan
    /// tek panel, tavana kayarak açılıyor, iki yanında birer düğme.
    /// </summary>
    private static void BuildDoor(Transform group, Layout layout, Vector2Int cell,
        Material doorMaterial, Material buttonMaterial)
    {
        bool alongX = !layout.Wall[cell.x - 1, cell.y] && !layout.Wall[cell.x + 1, cell.y];
        Vector3 axis = alongX ? Vector3.right : Vector3.forward;
        Vector3 cross = alongX ? Vector3.forward : Vector3.right;

        Transform doorRoot = MazeMapBuilder.CreateGroup($"Kapi_{cell.x}_{cell.y}", group);
        doorRoot.localPosition = CellLocal(cell.x, cell.y);

        GameObject panel = MazeMapBuilder.CreateBox("Panel", doorRoot,
            Vector3.up * (WallHeight / 2f),
            MazeMapBuilder.AxisSize(axis, MazeMapBuilder.DoorThickness, WallHeight, cross, Cell),
            doorMaterial);

        panel.AddComponent<NetworkIdentity>();

        SlidingDoor door = panel.AddComponent<SlidingDoor>();
        SerializedObject serializedDoor = new SerializedObject(door);
        serializedDoor.FindProperty("slideDirection").vector3Value = Vector3.up;
        serializedDoor.FindProperty("slideDistance").floatValue = WallHeight + 0.05f;
        serializedDoor.FindProperty("allowDirectUse").boolValue = false;
        serializedDoor.ApplyModifiedPropertiesWithoutUndo();

        LayerSetup.Apply(panel, LayerSetup.Harita);
        WireDoorAudio(door);

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject buttonBox = MazeMapBuilder.CreateBox($"Dugme_{side}", doorRoot,
                axis * (MazeMapBuilder.ButtonAxisOffset * side)
                    + cross * MazeMapBuilder.ButtonWallOffset
                    + Vector3.up * MazeMapBuilder.ButtonHeight,
                MazeMapBuilder.AxisSize(axis, 0.3f, 0.3f, cross, 0.12f),
                buttonMaterial);

            UseButton button = buttonBox.AddComponent<UseButton>();
            SerializedObject serializedButton = new SerializedObject(button);
            serializedButton.FindProperty("prompt").stringValue = "Kapıyı çalıştır";
            serializedButton.FindProperty("pressVisual").objectReferenceValue = buttonBox.transform;
            serializedButton.FindProperty("pressDirection").vector3Value = cross;

            SerializedProperty targets = serializedButton.FindProperty("targets");
            targets.arraySize = 1;
            targets.GetArrayElementAtIndex(0).objectReferenceValue = door;
            serializedButton.ApplyModifiedPropertiesWithoutUndo();

            LayerSetup.Apply(buttonBox, LayerSetup.Etkilesim);
            WireButtonAudio(button);
        }
    }

    // ---------- Giydirme: gerçek haritanın SciFi Kit parçaları ----------

    private static GameObject LoadKit(string relativePath) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{MapDressWindow.KitRoot}/{relativePath}.prefab");

    private static void Dress(Transform root, Layout layout, Geometry geometry)
    {
        GameObject wallPrefab = LoadKit("Walls/Wall Plain");
        GameObject floorPrefab = LoadKit("Floor/Floor Tile 01");
        GameObject ceilingPrefab = LoadKit("Ceiling/Ceiling Closed");
        GameObject doorPrefab = LoadKit("Walls/Wall BayDoor");
        Material crouchDressMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            $"{MapDressWindow.KitMaterials}/Ducts Pillars Mat.mat");

        // Kit yoksa çıplak küpler görünür kalıyor — koridor yine oynanabilir.
        if (wallPrefab == null || floorPrefab == null || ceilingPrefab == null || doorPrefab == null)
        {
            Debug.LogWarning("Tutorial: SciFi Kit parçalarından biri bulunamadı, koridor " +
                "çıplak küplerle kaldı. Kit'in kurulu olduğundan emin ol.");
            return;
        }

        Transform dressing = MazeMapBuilder.CreateGroup("Giydirme", root);

        DressWalls(dressing, layout, wallPrefab);
        DressTiles(dressing, layout, floorPrefab, "Zemin", WorldOffset.y, alignTop: true);
        DressTiles(dressing, layout, ceilingPrefab, "Tavan", WorldOffset.y + WallHeight, alignTop: false);
        DressDoors(geometry.Doors, doorPrefab);

        // Geçit panelle kaplanmıyor, materyal değiştiriyor: panel deliği
        // kapatırdı (MapDressWindow.MarkCrouchPassages ile aynı gerekçe).
        if (crouchDressMaterial != null)
        {
            foreach (Renderer renderer in geometry.CrouchPassages.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = crouchDressMaterial;
        }

        // Çarpışma küplerden geliyor, görüntü kitten.
        foreach (Transform block in geometry.Walls)
            SetRendererEnabled(block.gameObject, false);

        SetRendererEnabled(geometry.Floor, false);
        SetRendererEnabled(geometry.Ceiling, false);
    }

    /// <summary>
    /// `MazeExpansionSetup.DressWingWalls` ile aynı kural: duvar hücresinin açık
    /// komşuya bakan her yüzüne bir `Wall Plain` paneli.
    ///
    /// Tek ek: çıkış satırındaki iki yan hücrenin DIŞA (+z) bakan yüzü de
    /// giydiriliyor. Izgara orada bitiyor ama sahanlık koridordan geniş
    /// (1.4 hücre), yani o iki yüzün bir şeridi sahanlığın içinden görünüyor.
    /// </summary>
    private static void DressWalls(Transform parent, Layout layout, GameObject wallPrefab)
    {
        Bounds bounds = MapDressWindow.MeasurePrefab(wallPrefab);

        bool thinAlongZ = bounds.size.z <= bounds.size.x;
        float width = thinAlongZ ? bounds.size.x : bounds.size.z;
        float thickness = thinAlongZ ? bounds.size.z : bounds.size.x;
        if (width <= 0.001f)
            return;

        float scale = Cell / width;
        float scaledThickness = thickness * scale;

        Transform group = MazeMapBuilder.CreateGroup("Duvarlar", parent);
        Vector2Int[] directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        for (int x = 0; x < GridWidth; x++)
        {
            for (int z = 0; z < GridLength; z++)
            {
                if (!layout.IsSolid(x, z))
                    continue;

                Vector3 center = CellWorld(x, z);

                foreach (Vector2Int step in directions)
                {
                    int nx = x + step.x;
                    int nz = z + step.y;

                    bool visible = layout.Inside(nx, nz)
                        ? !layout.IsSolid(nx, nz)
                        : z == ExitRow && step.y == 1 && Mathf.Abs(x - CorridorX) == 1;

                    if (!visible)
                        continue;

                    Vector3 normal = new Vector3(step.x, 0f, step.y);
                    Vector3 facePoint = center + normal * (Cell / 2f);
                    Vector3 target = facePoint - normal * (scaledThickness / 2f);

                    Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
                    if (!thinAlongZ)
                        rotation *= Quaternion.Euler(0f, -90f, 0f);

                    MapDressWindow.Place(wallPrefab, group, $"Panel_{x}_{z}_{step.x}_{step.y}",
                        target, rotation, scale, WorldOffset.y, alignTop: false);
                }
            }
        }
    }

    /// <summary>`MazeExpansionSetup.DressWingTiles` ile aynı: küp olmayan her hücreye bir karo.</summary>
    private static void DressTiles(Transform parent, Layout layout, GameObject prefab, string label,
        float baseY, bool alignTop)
    {
        Bounds bounds = MapDressWindow.MeasurePrefab(prefab);
        float width = Mathf.Max(bounds.size.x, bounds.size.z);
        if (width <= 0.001f)
            return;

        float scale = Cell / width;
        Transform group = MazeMapBuilder.CreateGroup(label, parent);

        for (int x = 0; x < GridWidth; x++)
        {
            for (int z = 0; z < GridLength; z++)
            {
                if (layout.IsSolid(x, z))
                    continue;

                MapDressWindow.Place(prefab, group, $"{label}_{x}_{z}",
                    CellWorld(x, z), Quaternion.identity, scale, baseY, alignTop);
            }
        }
    }

    /// <summary>`MazeExpansionSetup.DressWingDoors` ile aynı: panelin ölçeği collider'a taşınıp `Wall BayDoor` giydiriliyor.</summary>
    private static void DressDoors(Transform doorsGroup, GameObject doorPrefab)
    {
        Bounds bounds = MapDressWindow.MeasurePrefab(doorPrefab);
        bool thinAlongZ = bounds.size.z <= bounds.size.x;
        float modelWidth = thinAlongZ ? bounds.size.x : bounds.size.z;
        if (modelWidth <= 0.001f)
            return;

        foreach (Transform door in doorsGroup)
        {
            Transform panel = door.Find("Panel");
            if (panel == null)
                continue;

            BoxCollider box = panel.GetComponent<BoxCollider>();
            Vector3 size = panel.localScale;
            if (box != null)
                box.size = size;
            panel.localScale = Vector3.one;

            bool facesX = size.x <= size.z;
            Vector3 normal = facesX ? Vector3.right : Vector3.forward;
            float opening = facesX ? size.z : size.x;

            Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
            if (!thinAlongZ)
                rotation *= Quaternion.Euler(0f, -90f, 0f);

            MapDressWindow.Place(doorPrefab, panel, "Giydirme_Kapi", panel.position, rotation,
                opening / modelWidth, panel.position.y - size.y / 2f, alignTop: false, markStatic: false);

            SetRendererEnabled(panel.gameObject, false);
        }
    }

    private static void SetRendererEnabled(GameObject target, bool visible)
    {
        if (target == null)
            return;

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
            renderer.enabled = visible;
    }

    // ---------- Işık ----------

    /// <summary>
    /// `WingLightingSetup.CreateLamp` ile aynı: nokta ışığı + onun çocuğu olarak
    /// tavana yaslı "Hanging Light" armatürü. Bu sahne pişmiyor, ışıklar gerçek
    /// zamanlı kalıyor.
    /// </summary>
    private static void BuildLamps(Transform root)
    {
        GameObject lampPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefabPath);
        Transform group = MazeMapBuilder.CreateGroup("Lambalar", root);

        float visualScale = 1f;
        if (lampPrefab != null)
        {
            Bounds lampBounds = MapDressWindow.MeasurePrefab(lampPrefab);
            float lampWidth = Mathf.Max(lampBounds.size.x, lampBounds.size.z);
            visualScale = lampWidth > 0.001f ? LampVisualWidth / lampWidth : 1f;
        }

        for (int i = 0; i < LampRows.Length; i++)
        {
            GameObject lamp = new GameObject($"Lamba_{i + 1}");
            lamp.transform.SetParent(group, false);
            lamp.transform.position = CellWorld(CorridorX, LampRows[i]) + Vector3.up * LampHeight;

            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = LampRange;
            light.intensity = LampIntensity;
            light.color = new Color(1f, 0.85f, 0.65f); // AtmosphereSetup ile aynı soluk sarı
            light.shadows = LightShadows.None;

            if (lampPrefab != null)
            {
                MapDressWindow.Place(lampPrefab, lamp.transform, "Giydirme_Lamba",
                    lamp.transform.position, Quaternion.identity, visualScale,
                    WorldOffset.y + WallHeight, alignTop: true, markStatic: true);
            }
        }
    }

    // ---------- Doğum ----------

    private static Transform BuildSpawn(Transform root)
    {
        GameObject anchor = new GameObject("DogumNoktasi");
        anchor.transform.SetParent(root, false);
        anchor.transform.localPosition = CellLocal(CorridorX, SpawnRow) + Vector3.up * CapsuleCenterHeight;
        anchor.transform.localRotation = Quaternion.LookRotation(Vector3.forward);

        // Mirror oyuncuyu tur başlamadan ÖNCE de buraya koysun (sınıf yorumu).
        anchor.AddComponent<NetworkStartPosition>();

        return anchor.transform;
    }

    // ---------- İstasyon: terminal ----------

    /// <summary>Gerçek terminal: `ObjectiveSetup.CreateTerminal`, Fusebox gövdesiyle, sağ duvara monte.</summary>
    private static void BuildTerminal(Transform root)
    {
        GameObject body = AssetDatabase.LoadAssetAtPath<GameObject>($"{StructurePropFolder}/Fusebox 01.prefab");

        Vector3 wallPoint = CellWorld(CorridorX, TerminalRow) + new Vector3(Cell / 2f, PanelHeight, 0f);
        ObjectiveSetup.CreateTerminal(root, body, wallPoint, Vector3.left, 1);

        Transform created = root.Find("Terminal_1");
        Terminal terminal = created != null ? created.GetComponent<Terminal>() : null;
        if (terminal == null)
            return;

        SerializedObject serialized = new SerializedObject(terminal);
        AudioSetupUtility.AssignClip(serialized.FindProperty("workingClip"), "Terminal_Calisma");
        AudioSetupUtility.AssignClip(serialized.FindProperty("warningClip"), "Terminal_Uyari");
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------- İstasyon: canavar vitrini ----------

    private static void BuildMonsterShowcase(Transform root, GameObject playerPrefab)
    {
        Transform showcase = MazeMapBuilder.CreateGroup("CanavarVitrini", root);
        showcase.localPosition = CellLocal(CorridorX + 1, MonsterRow);

        // Cam: odanın koridora açılan yüzünde. Katı (yürünemez) ama görünüyor.
        Material glass = GetOrCreateMaterial("Tutorial_Cam", new Color(0.55f, 0.85f, 0.95f, 0.28f));
        MakeTransparent(glass);

        GameObject pane = MazeMapBuilder.CreateBox("Cam", showcase,
            new Vector3(-Cell / 2f, WallHeight / 2f, 0f), new Vector3(0.08f, WallHeight, Cell), glass);
        LayerSetup.Apply(pane, LayerSetup.Harita);

        BuildMonsterModel(showcase, playerPrefab, new Vector3(0.3f, 0f, 0f));
    }

    /// <summary>
    /// `NetworkPlayer.prefab`'ı geçici olarak örnekleyip `PlayerBodyVisual.monsterRoot`'u
    /// söküyor, geri kalanını siliyor. Kök ayakların hizasında (prefabta
    /// hull'un tabanında), yani y=0 zemine oturtuyor.
    /// </summary>
    private static void BuildMonsterModel(Transform parent, GameObject playerPrefab, Vector3 localPosition)
    {
        GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);

        // Unity, bir prefab ÖRNEĞİNİN içindeki çocuğu başka bir hiyerarşiye
        // taşımaya izin vermiyor ("Setting the parent of a transform which
        // resides in a Prefab instance is not possible"). `temp` birazdan
        // tamamen siliniyor, prefab bağını korumanın anlamı yok.
        PrefabUtility.UnpackPrefabInstance(temp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        PlayerBodyVisual visual = temp.GetComponentInChildren<PlayerBodyVisual>(true);
        GameObject monsterRoot = null;

        if (visual != null)
        {
            SerializedObject serialized = new SerializedObject(visual);
            SerializedProperty property = serialized.FindProperty("monsterRoot");
            monsterRoot = property != null ? property.objectReferenceValue as GameObject : null;
        }

        if (monsterRoot == null)
        {
            Debug.LogWarning("Tutorial: PlayerBodyVisual.monsterRoot bulunamadı, " +
                "canavar vitrini boş kaldı. NetworkPlayer.prefab'ı elle kontrol et.");
            Object.DestroyImmediate(temp);
            return;
        }

        monsterRoot.transform.SetParent(parent, false);
        monsterRoot.transform.localPosition = localPosition;
        monsterRoot.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        monsterRoot.SetActive(true);
        monsterRoot.name = "CanavarModeli";

        Object.DestroyImmediate(temp);

        // Vitrindeki model ağda bir oyuncu değil: sunucu onu spawn etmeye kalkmasın.
        foreach (NetworkIdentity leftover in monsterRoot.GetComponentsInChildren<NetworkIdentity>(true))
            Object.DestroyImmediate(leftover);

        // `MonsterAura` kullanılmıyor: ışığını yalnızca rol hook'u açıyor ve
        // vitrindeki modelde rol yok. Aynı kırmızı hâle doğrudan kuruluyor.
        GameObject halo = new GameObject("CanavarHalesi", typeof(Light));
        halo.transform.SetParent(monsterRoot.transform, false);
        halo.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        Light light = halo.GetComponent<Light>();
        light.type = LightType.Point;
        light.range = 10f;
        light.intensity = 2.2f;
        light.color = new Color(1f, 0.13f, 0.08f);
        light.shadows = LightShadows.Hard;
    }

    // ---------- İstasyon: ceset ve diriltme ----------

    /// <summary>
    /// Tur başlar başlamaz elenen bot — `TestBotSetup`'ın ölü botuyla aynı
    /// desen: eleme normal yoldan geçtiği için gerçek bir ceset doğuyor.
    ///
    /// Kapsül MERKEZİ 0.7 m'de, oyuncunun doğum noktasıyla aynı kural. Hızı
    /// sıfır: diriltilince kabinde duruyor, koridorda koşturup oyuncunun önüne
    /// çıkmıyor.
    /// </summary>
    private static void BuildDeadBot(Transform root)
    {
        GameObject bot = new GameObject("TutorialOluBot");
        bot.transform.SetParent(root, false);
        bot.transform.localPosition = CellLocal(CorridorX, BodyRow) + Vector3.up * CapsuleCenterHeight;

        CharacterController controller = bot.AddComponent<CharacterController>();
        controller.height = 72f * PlayerController.UnitsToMeters;
        controller.radius = 16f * PlayerController.UnitsToMeters;
        controller.center = Vector3.zero;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Govde";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(bot.transform, false);
        body.transform.localScale = new Vector3(
            controller.radius * 2f, controller.height / 2f, controller.radius * 2f);

        bot.AddComponent<NetworkIdentity>();

        NetworkTransformReliable netTransform = bot.AddComponent<NetworkTransformReliable>();
        netTransform.syncDirection = SyncDirection.ServerToClient;

        RoundParticipant participant = bot.AddComponent<RoundParticipant>();
        TestRunnerBot runnerBot = bot.AddComponent<TestRunnerBot>();

        SerializedObject serialized = new SerializedObject(participant);
        serialized.FindProperty("isBot").boolValue = true;
        serialized.FindProperty("startEliminated").boolValue = true;
        serialized.FindProperty("botName").stringValue = "Eğitim Botu";
        serialized.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedBot = new SerializedObject(runnerBot);
        serializedBot.FindProperty("speed").floatValue = 0f;
        serializedBot.ApplyModifiedPropertiesWithoutUndo();

        RunnerSetup.AttachToSceneObject(bot);

        // **Kapsül `PlayerBodyVisual`'a da verilmeli.** Model bağlandığı anda
        // gövdeyi gizleme/gösterme işi `RoundParticipant`'tan `PlayerBodyVisual`'a
        // geçiyor (`SetVisualActive`: bodyVisual varsa ona, yoksa bodyRenderer'a).
        // `Refresh` kapsülü `ApplyMode(capsuleRenderer, ...)` ile kapatıyor —
        // ama alan boşsa kapatacak bir şey bulamıyor ve yer tutucu kapsül
        // modelin İÇİNDE, elenmiş botta bile açık kalıyor. Koridorda görülen
        // "parlayan beyaz kapsül" tam olarak buydu.
        //
        // `TestBotSetup` aynı satırı zaten yazıyor; burada eksikti.
        PlayerBodyVisual visual = bot.GetComponent<PlayerBodyVisual>();
        if (visual != null)
        {
            SerializedObject serializedVisual = new SerializedObject(visual);
            serializedVisual.FindProperty("capsuleRenderer").objectReferenceValue =
                body.GetComponent<Renderer>();
            serializedVisual.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Gerçek diriltme kabini (`RevivalSetup.BuildStation`), koridorun solundaki
    /// girintide. Kabinin açık ön yüzü yerel +z; 90° çevrilince koridora bakıyor.
    /// </summary>
    private static void BuildRevivalStation(Transform root)
    {
        GameObject station = RevivalSetup.BuildStation(root,
            CellWorld(CorridorX - 1, RevivalRow), "Diriltme_Egitim");

        station.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        RevivalStation component = station.GetComponentInChildren<RevivalStation>(true);
        if (component == null)
            return;

        SerializedObject serialized = new SerializedObject(component);
        AudioSetupUtility.AssignClip(serialized.FindProperty("successClip"), "Diriltme_Basari");
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------- İstasyon: çıkış ----------

    /// <summary>
    /// `ObjectiveSetup.BuildExit` ile aynı çıkış: sarı kilitli kapı (BayDoor
    /// gövdesiyle), kaçış tetiği, canavar engeli, dışarıda kapalı bir sahanlık
    /// ve kapının yanındaki kilit paneli.
    ///
    /// **Tek fark yükseklikte, bilerek.** `BuildExit` merkezi duvar bloğunun
    /// konumundan (y = 1.5) okuyor ve üstüne bir de yarım duvar yüksekliği
    /// ekliyor; ana haritada sahanlık duvarları ve canavar engeli bu yüzden
    /// 1.5 m havada duruyor. Burada merkez ZEMİN hizasında veriliyor.
    /// </summary>
    private static void BuildExit(Transform root)
    {
        Vector3 outDirection = Vector3.forward;
        Vector3 cross = Vector3.right;
        Vector3 center = CellWorld(CorridorX, ExitRow);

        Material doorMaterial = ObjectiveSetup.GetOrCreateUnlitMaterial("Cikis_Kapisi", new Color(0.85f, 0.75f, 0.2f));
        GameObject door = MazeMapBuilder.CreateBox("Cikis_Kapisi", root, Vector3.zero,
            MazeMapBuilder.AxisSize(outDirection, 0.25f, WallHeight, cross, Cell), doorMaterial);
        door.transform.position = center + Vector3.up * (WallHeight / 2f);

        door.AddComponent<NetworkIdentity>();
        SlidingDoor sliding = door.AddComponent<SlidingDoor>();

        SerializedObject serializedDoor = new SerializedObject(sliding);
        serializedDoor.FindProperty("slideDirection").vector3Value = Vector3.up;
        serializedDoor.FindProperty("slideDistance").floatValue = WallHeight + 0.05f;
        serializedDoor.FindProperty("allowDirectUse").boolValue = false;
        serializedDoor.FindProperty("autoCloseDelay").floatValue = 0f;
        serializedDoor.ApplyModifiedPropertiesWithoutUndo();

        ObjectiveSetup.DressExitDoor(door, outDirection, Cell);
        LayerSetup.Apply(door, LayerSetup.Harita);
        WireDoorAudio(sliding);

        GameObject gate = new GameObject("Cikis_Gecidi");
        gate.transform.SetParent(root, false);
        gate.transform.position = center + outDirection * Cell;
        gate.AddComponent<NetworkIdentity>();
        ExitGate exitGate = gate.AddComponent<ExitGate>();

        // Sahanlığın zemini: gediğin dış yüzünden sahanlığın bittiği yere kadar.
        // Gediğin kendisi zaten koridor zemini ve karosu — üst üste binen iki
        // yüzey titreşirdi.
        float innerFace = Cell * 0.5f;
        float outerEdge = Cell * 1.85f;

        GameObject floor = MazeMapBuilder.CreateBox("Zemin", gate.transform, Vector3.zero,
            MazeMapBuilder.AxisSize(outDirection, outerEdge - innerFace, 0.5f, cross, Cell * 1.4f + 0.6f),
            ObjectiveSetup.GetOrCreateUnlitMaterial("Cikis_Zemin", new Color(0.30f, 0.32f, 0.34f)));
        floor.transform.position = center + outDirection * ((innerFace + outerEdge) / 2f) + Vector3.down * 0.25f;
        MazeMapBuilder.MarkStatic(floor);

        GameObject trigger = new GameObject("Tetik");
        trigger.transform.SetParent(gate.transform, false);
        trigger.transform.position = gate.transform.position + Vector3.up * (WallHeight / 2f);
        trigger.transform.localScale = MazeMapBuilder.AxisSize(outDirection, Cell * 0.6f, WallHeight, cross, Cell);
        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        trigger.AddComponent<ExitTriggerRelay>();

        GameObject blocker = new GameObject("Engel");
        blocker.transform.SetParent(gate.transform, false);
        blocker.transform.position = center + outDirection * (Cell * 0.45f) + Vector3.up * (WallHeight / 2f);
        blocker.transform.localScale = MazeMapBuilder.AxisSize(outDirection, 0.3f, WallHeight, cross, Cell);
        BoxCollider blockerCollider = blocker.AddComponent<BoxCollider>();

        ExitLock exitLock = BuildExitLock(root, sliding);

        SerializedObject serializedGate = new SerializedObject(exitGate);
        serializedGate.FindProperty("escapeTrigger").objectReferenceValue = triggerCollider;
        serializedGate.FindProperty("monsterBlocker").objectReferenceValue = blockerCollider;
        serializedGate.FindProperty("door").objectReferenceValue = sliding;
        serializedGate.FindProperty("exitLock").objectReferenceValue = exitLock;
        serializedGate.ApplyModifiedPropertiesWithoutUndo();

        ObjectiveSetup.BuildVestibule(gate.transform, center, outDirection, Cell, WallHeight);

        LayerSetup.Apply(gate, LayerSetup.Harita);
    }

    /// <summary>
    /// `ObjectiveSetup.BuildExitLock` ile aynı panel (Fusebox 02 gövdesi, durum
    /// göstergesi); tek fark yeri: kapının bir hücre önünde, sağ duvarda.
    /// </summary>
    private static ExitLock BuildExitLock(Transform root, SlidingDoor door)
    {
        Vector3 normal = Vector3.left;
        Vector3 wallPoint = CellWorld(CorridorX, ExitLockRow) + new Vector3(Cell / 2f, PanelHeight, 0f);

        GameObject panel = new GameObject("Cikis_Kilidi");
        panel.transform.SetParent(root, false);
        panel.transform.SetPositionAndRotation(wallPoint + normal * 0.08f,
            Quaternion.LookRotation(normal, Vector3.up));

        BoxCollider box = panel.AddComponent<BoxCollider>();
        box.size = new Vector3(0.6f, 0.8f, 0.16f);

        panel.AddComponent<NetworkIdentity>();
        ExitLock exitLock = panel.AddComponent<ExitLock>();

        GameObject body = AssetDatabase.LoadAssetAtPath<GameObject>($"{StructurePropFolder}/Fusebox 02.prefab")
            ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{StructurePropFolder}/Fusebox 01.prefab");

        if (body != null)
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(body, panel.transform);
            visual.name = "Govde";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>())
                collider.enabled = false;
        }

        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        indicator.name = "Gosterge";
        Object.DestroyImmediate(indicator.GetComponent<Collider>());
        indicator.transform.SetParent(panel.transform, false);
        indicator.transform.localPosition = new Vector3(0f, 0.28f, -0.09f);
        indicator.transform.localScale = new Vector3(0.34f, 0.08f, 0.03f);
        indicator.GetComponent<Renderer>().sharedMaterial =
            ObjectiveSetup.GetOrCreateUnlitMaterial("Cikis_Kilit_Gosterge", Color.white);

        SerializedObject serialized = new SerializedObject(exitLock);
        serialized.FindProperty("door").objectReferenceValue = door;
        serialized.FindProperty("statusLight").objectReferenceValue = indicator.GetComponent<Renderer>();
        AudioSetupUtility.AssignClip(serialized.FindProperty("workingClip"), "Terminal_Calisma");
        serialized.ApplyModifiedPropertiesWithoutUndo();

        LayerSetup.Apply(panel, LayerSetup.Etkilesim);

        return exitLock;
    }

    // ---------- Süsler ----------

    /// <summary>
    /// Birkaç varil ve kasa — `Harita Süsle`'nin kullandığı kit parçaları, ama
    /// rastgele değil sabit yerlerde: tek şeritli koridorda rastgele bir kasa
    /// bir istasyonun önünü kapatabilirdi.
    /// </summary>
    private static void BuildProps(Transform root)
    {
        Transform group = MazeMapBuilder.CreateGroup("Suslemeler", root);

        PlaceProp(group, "Crates Barrels Pallets/Barrel", SpawnRow, wallSide: -1, alongOffset: -1f);
        PlaceProp(group, "Crates Barrels Pallets/Crate Short", TerminalCaptionRow, wallSide: -1, alongOffset: 0.4f);
        PlaceProp(group, "Crates Barrels Pallets/Crate Long", BodyCaptionRow, wallSide: 1, alongOffset: 0f);
        PlaceProp(group, "Misc Props/Garbage Bin", ExitCaptionRow, wallSide: -1, alongOffset: 0f);
    }

    /// <summary>
    /// Parçayı duvara yaslar, tabanını zemine oturtur — `PropScatterWindow.PlaceProp`
    /// ile aynı ölçüp hizalama. Collider'lar kalıyor: süsün içinden yürünmesin.
    /// </summary>
    private static void PlaceProp(Transform parent, string relativePath, int row, int wallSide, float alongOffset)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropFolder}/{relativePath}.prefab");
        if (prefab == null)
            return;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

        // Duvardan koridora bakan yön; parçanın sırtı duvarda.
        Vector3 normal = wallSide < 0 ? Vector3.right : Vector3.left;
        instance.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);

        Bounds bounds = MapDressWindow.WorldBounds(instance);
        float depth = bounds.size.x;

        // Uzun bir kasa koridora dik uzanırsa 3.2 m'lik şeridi tıkar: duvar boyunca yatır.
        if (depth > 1f)
        {
            instance.transform.rotation *= Quaternion.Euler(0f, 90f, 0f);
            bounds = MapDressWindow.WorldBounds(instance);
            depth = bounds.size.x;
        }

        Vector3 wallPoint = CellWorld(CorridorX, row) + new Vector3(wallSide * Cell / 2f, 0f, alongOffset);
        Vector3 target = wallPoint + normal * (depth / 2f + 0.05f);

        instance.transform.position += new Vector3(
            target.x - bounds.center.x,
            WorldOffset.y - bounds.min.y,
            target.z - bounds.center.z);

        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;

        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);

        LayerSetup.Apply(instance, LayerSetup.Sus);
    }

    // ---------- Alt yazılar ----------

    private static void BuildCaptions(Transform root)
    {
        Transform group = MazeMapBuilder.CreateGroup("Ipuclari", root);

        // Karşılama doğum hücresinin tamamını kaplıyor: oyuncu tetiğin içinde
        // doğuyor, yani yazı yürümeye başlamadan görünüyor.
        AddCaption(group, "Karsilama", SpawnRow, Cell * 0.9f,
            "Terminal Five'a hoş geldin! Bu koridor sana oyunun temellerini gösterecek. " +
                "İlerlemek için {0}. Esc ile duraklatıp ayarlara girebilir ya da çıkabilirsin.",
            true, GameAction.Forward, 8f);

        AddCaption(group, "Fener", FlashlightRow, 1f,
            "Burası karanlık. {0} ile fenerini açıp kapatabilirsin — fener görmeni " +
                "sağlar ama canavara da yerini gösterir.",
            true, GameAction.Flashlight, 8f);

        AddCaption(group, "Egilme", CrouchCaptionRow, 1f,
            "Alçak bir geçit. Geçmek için {0} ile eğil.",
            true, GameAction.Crouch, 6f);

        AddCaption(group, "Kapi", DoorCaptionRow, 1f,
            "Bir kapı. Yanındaki düğmeye {0} ile bas.",
            true, GameAction.Interact, 6f);

        AddCaption(group, "Terminal", TerminalCaptionRow, 1f,
            "Bu bir terminal. Bağlanmak için {0}. Dolarken ekranda çıkan yöne " +
                "hareket tuşlarınla hızlıca bas — yoksa kilitlenir.",
            true, GameAction.Interact, 9f);

        AddCaption(group, "Canavar", MonsterCaptionRow, 1f,
            "Bu canavar. Fenerin onu görmeni sağlar ama seni de gösterir. " +
                "Koşarsan duyulursun, yerde iz bırakırsın. Yakalarsa anında elenirsin. " +
                "Gerçek oyunda ondan kaçıp saklanacaksın.",
            false, GameAction.Forward, 10f);

        AddCaption(group, "CesetTasima", BodyCaptionRow, 1f,
            "Elenen bir arkadaşının cesedi böyle kalıyor. Taşımak için {0} ile al.",
            true, GameAction.Interact, 7f);

        AddCaption(group, "Diriltme", RevivalRow, 1f,
            "Cesedi diriltme kabinine bırak, sonra kabinin yanındaki terminalden " +
                "diriltmeyi başlat. Orada da terminaldeki gibi yön sınavı var.",
            false, GameAction.Interact, 9f);

        AddCaption(group, "Cikis", ExitCaptionRow, 1f,
            "Terminal bitince çıkış kilidi çalışır. Kapının yanındaki panele {0} ile " +
                "bağlan, on adımlık yön dizilimini gir ve açılan kapıdan geçip kaç.",
            true, GameAction.Interact, 10f);
    }

    /// <summary>
    /// Koridoru enine kesen bir tetik kutusu. Metin `Localization` sözlüğünün
    /// anahtarı — yeni bir yazı eklenirse İngilizcesi oraya da eklenmeli.
    /// </summary>
    private static void AddCaption(Transform parent, string name, int row, float depth,
        string templateKey, bool hasKeyArg, GameAction keyArg, float seconds)
    {
        GameObject trigger = new GameObject($"Ipucu_{name}");
        trigger.transform.SetParent(parent, false);
        trigger.transform.localPosition = CellLocal(CorridorX, row) + Vector3.up * 1.25f;

        BoxCollider box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(Cell, 2.5f, depth);

        TutorialCaptionTrigger caption = trigger.AddComponent<TutorialCaptionTrigger>();
        caption.Configure(templateKey, hasKeyArg, keyArg, seconds);
    }

    // ---------- Tur ----------

    private static void BuildRoundManager(Transform root, Transform spawnAnchor, GameObject corpsePrefab)
    {
        GameObject managerObject = new GameObject("RoundManager");
        managerObject.transform.SetParent(root, false);

        managerObject.AddComponent<NetworkIdentity>();
        RoundManager manager = managerObject.AddComponent<RoundManager>();

        SerializedObject serialized = new SerializedObject(manager);

        // Tek oyuncu yeter: tutorial'da ikinci bir insan hiç olmayacak.
        serialized.FindProperty("minimumPlayers").intValue = 1;

        // Koridorda tek gerçek terminal var; ExitOpen onunla açılmalı.
        serialized.FindProperty("terminalGoal").intValue = 1;

        // Canavar yok, iki çapa da aynı noktayı gösteriyor — ResolveSpawnAnchors
        // ikisinin de dolu olmasını istiyor.
        serialized.FindProperty("runnerSpawn").objectReferenceValue = spawnAnchor;
        serialized.FindProperty("monsterSpawn").objectReferenceValue = spawnAnchor;

        // Ölü eğitim botu kendi yerinde kalsın (sınıf yorumu, "düşme").
        serialized.FindProperty("placeBotsAtRoundStart").boolValue = false;

        serialized.FindProperty("corpsePrefab").objectReferenceValue = corpsePrefab;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Bileşeni GERİ DÖNDÜRÜYOR: duraklatma menüsünün `TUTORIAL'DAN ÇIK`
    /// düğmesi ona kalıcı dinleyiciyle bağlanıyor (`MenuSetup.BuildTutorialMenu`).
    /// </summary>
    private static TutorialBootstrap BuildBootstrap(Transform root)
    {
        GameObject bootstrap = new GameObject("TutorialBootstrap");
        bootstrap.transform.SetParent(root, false);
        return bootstrap.AddComponent<TutorialBootstrap>();
    }

    private static void RegisterInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        foreach (EditorBuildSettingsScene existing in scenes)
        {
            if (existing.path == ScenePath)
                return;
        }

        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ---------- Ses: AudioImportSetup ile aynı klipler ve kaynak ayarları ----------

    private static void WireDoorAudio(SlidingDoor door)
    {
        AudioSource source = door.GetComponent<AudioSource>();
        if (source == null)
            source = door.gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 3f;
        source.maxDistance = 30f;

        SerializedObject serialized = new SerializedObject(door);
        serialized.FindProperty("audioSource").objectReferenceValue = source;
        AudioSetupUtility.AssignClip(serialized.FindProperty("moveClip"), "Kapi");
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireButtonAudio(UseButton button)
    {
        AudioSource source = button.GetComponent<AudioSource>();
        if (source == null)
            source = button.gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 20f;

        SerializedObject serialized = new SerializedObject(button);
        serialized.FindProperty("audioSource").objectReferenceValue = source;
        AudioSetupUtility.AssignClip(serialized.FindProperty("pressClip"), "Dugme");
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------- Yardımcılar ----------

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        const string folder = "Assets/_Art/Materials";
        string path = $"{folder}/{name}.mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder("Assets/_Art"))
            AssetDatabase.CreateFolder("Assets", "_Art");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/_Art", "Materials");

        Material material = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    /// <summary>Standard shader'ı saydam moda çeviriyor — Unity'nin bilinen tarifi.</summary>
    private static void MakeTransparent(Material material)
    {
        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
    }
}
