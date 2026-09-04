using kcp2k; // KcpTransport Mirror namespace'inde değil, kendi namespace'inde
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ağ oyuncusunu ve ağ altyapısını sıfırdan kurar: bağlanma, hareket, rol,
/// süre, bıçak ve iz. Eksik kalan tek parça lobi arayüzü — tur şimdilik
/// sunucuda [1] tuşuyla başlıyor.
///
/// Mirror oyuncuyu bir **prefab'tan** doğurur, sahnedeki hazır objeden değil.
/// Bu yüzden ayrı bir ağ oyuncusu prefab'ı üretiliyor; sahnedeki Player ve menü
/// test sırasında kapatılıyor (silinmiyor, geri açılabilir).
///
/// Hareket **istemci otoriteli**: NetworkTransform'un yönü ClientToServer.
/// Sunucu otoriteli hareket + prediction, Source hissini kaybettirirdi
/// (bkz. CLAUDE.md).
///
/// Menü: Yakalamaca > Ağ Kurulumu (1. adım)
/// </summary>
public static class NetworkSetup
{
    private const string PrefabFolder = "Assets/_Prefabs";
    private const string PrefabPath = PrefabFolder + "/NetworkPlayer.prefab";
    private const string ManagerName = "NetworkManager";

    /// <summary>
    /// Play modunda kapalı: o sırada yapılan sahne değişiklikleri Play bitince
    /// geri alınır, kurulum çalışmış gibi görünüp hiçbir iz bırakmazdı.
    /// </summary>
    [MenuItem("Yakalamaca/Ağ Kurulumu (1. adım)", true)]
    private static bool CanSetup() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Ağ Kurulumu (1. adım)")]
    private static void Setup()
    {
        GameObject prefab = BuildPlayerPrefab();
        NetworkManager manager = BuildNetworkManager(prefab);
        int spawnCount = BuildSpawnPoints();
        PrepareRoundManager();

        // Menü artık kapatılmıyor: ağın kendisi oradan yönetiliyor (LOBİ KUR /
        // LOBİYE KATIL). Ağ öncesinden kalan sahne oyuncusu ve test kaçanları
        // ise hâlâ karışıyor — Mirror oyuncuyu prefabtan doğuruyor.
        DisableForNetworkTest("Player");
        DisableForNetworkTest("TestKacanlar");

        Selection.activeGameObject = manager.gameObject;
        EditorUtility.SetDirty(manager);

        // Sahneyi burada kaydediyoruz, kullanıcıya bırakmıyoruz. Prefab her
        // çalıştırmada sıfırdan üretildiği için kök objesinin fileID'si
        // değişiyor; sahnedeki playerPrefab referansı ancak bu kurulum
        // kaydedilirse doğru kalıyor. Kaydetmeyi unutmak, kurulum çalışmış ama
        // Unity kapanınca her şey geri gitmiş gibi görünmesine yol açıyordu.
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            $"Ağ kurulumu tamam. {spawnCount} doğum noktası yerleştirildi.\n" +
            "Sahnedeki Player, Menu ve TestKacanlar KAPATILDI — Mirror oyuncuyu prefab'tan doğuruyor. " +
            "Silinmediler, sonraki adımlarda geri bağlanacaklar.\n\n" +
            "TEST: Önce File > Build Settings > Build ile bir build al. Sonra:\n" +
            "  1) Editor'de Play → ekrandaki HUD'dan 'Host (Server + Client)'\n" +
            "  2) Build'i çalıştır → 'Client' (adres zaten localhost)\n" +
            "  3) İkisi de bağlandıktan sonra HOST penceresinde [1] tuşuna bas — tur başlar.\n" +
            "Rol dağıtımı sunucuda yapılır; iki ekranda da aynı süre ve kendi rolün yazmalı.\n" +
            "Canavar olan sol tıkı basılı tutup bırakarak bıçağı savurur; isabeti sunucu onaylar.");
    }

    /// <summary>
    /// Ağ oyuncusu prefab'ı. Sahnedeki Player'dan bağımsız olarak sıfırdan
    /// kuruluyor: sahne objesinden çevirmek, sahneye özel referansları
    /// prefab'ın içine taşırdı.
    /// </summary>
    private static GameObject BuildPlayerPrefab()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "_Prefabs");

        GameObject root = new GameObject("NetworkPlayer");

        // --- Gövde ölçüleri: Source hull'u (bkz. CLAUDE.md) ---
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 72f * PlayerController.UnitsToMeters;
        controller.radius = 16f * PlayerController.UnitsToMeters;
        controller.center = Vector3.zero;
        controller.slopeLimit = 45f;
        controller.stepOffset = 18f * PlayerController.UnitsToMeters;
        controller.skinWidth = controller.radius * 0.1f;

        // --- Görünür gövde ---
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Govde";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = controller.center;
        body.transform.localScale = new Vector3(
            controller.radius * 2f, controller.height / 2f, controller.radius * 2f);

        // --- Kamera: prefab'ın kendi kamerası, sahnedeki değil ---
        GameObject cameraObject = new GameObject("Kamera");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition =
            new Vector3(0f, (64f - 36f) * PlayerController.UnitsToMeters, 0f);

        Camera camera = cameraObject.AddComponent<Camera>();
        AudioListener listener = cameraObject.AddComponent<AudioListener>();

        // --- Bıçak: kameranın child'ı, sadece canavarda görünür ---
        // Konum ve ölçü tek oyunculu sürümden birebir alındı (_Eski/Eski_Player
        // yedeği). z=0.42 önemli: kameranın near clip düzlemi 0.3, daha yakına
        // koyarsak birinci şahısta bıçağın yarısı kesilir.
        GameObject knifeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        knifeObject.name = "Bicak";
        Object.DestroyImmediate(knifeObject.GetComponent<Collider>());
        knifeObject.transform.SetParent(cameraObject.transform, false);
        knifeObject.transform.localPosition = new Vector3(0.26f, -0.2f, 0.42f);
        knifeObject.transform.localScale = new Vector3(0.035f, 0.3f, 0.07f);

        // Canavarın kırmızı hâlesi. KAMERANIN değil KÖKÜN çocuğu, bilerek:
        // fener bakışı takip etmeli ama hâle canavarın etrafında durmalı —
        // kameraya bağlansaydı canavar başını çevirince ışık da savrulurdu.
        GameObject auraObject = new GameObject("CanavarHalesi");
        auraObject.transform.SetParent(root.transform, false);
        auraObject.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        Light aura = auraObject.AddComponent<Light>();
        aura.type = LightType.Point;
        aura.range = 10f;
        aura.intensity = 2.2f;
        aura.color = new Color(1f, 0.13f, 0.08f);

        // Gölgesiz nokta ışık duvar tanımaz; hâle yan koridora sızsaydı
        // canavarın yeri duvarın arkasından belli olurdu (bölüm 4).
        // Hard: nokta ışığın gölgesi altı yüzlü ve pahalı, oyunda böyle tek
        // ışık var ve yumuşaklık burada bir şey kazandırmıyor.
        aura.shadows = LightShadows.Hard;

        // Rol gelene kadar kapalı. RoundParticipant.ApplyRole açıyor.
        aura.enabled = false;

        GameObject flashlightObject = new GameObject("Fener");
        flashlightObject.transform.SetParent(cameraObject.transform, false);
        Light spot = flashlightObject.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 26f;
        spot.spotAngle = 55f;
        spot.intensity = 2.6f;
        spot.color = new Color(0.95f, 0.95f, 0.85f);
        spot.shadows = LightShadows.Hard;

        // --- Oyun bileşenleri ---
        PlayerInputSource input = root.AddComponent<PlayerInputSource>();
        PlayerController playerController = root.AddComponent<PlayerController>();
        PlayerInteractor interactor = root.AddComponent<PlayerInteractor>();
        CapsuleBodyVisual bodyVisual = root.AddComponent<CapsuleBodyVisual>();
        CameraBob bob = root.AddComponent<CameraBob>();
        Flashlight flashlight = root.AddComponent<Flashlight>();
        MonsterAura monsterAura = root.AddComponent<MonsterAura>();

        AudioSource footstepSource = root.AddComponent<AudioSource>();
        footstepSource.playOnAwake = false;
        footstepSource.spatialBlend = 1f;
        footstepSource.rolloffMode = AudioRolloffMode.Linear;
        footstepSource.minDistance = 2f;
        footstepSource.maxDistance = 28f;

        FootstepAudio footsteps = root.AddComponent<FootstepAudio>();

        // Bıçak sesi ayrı kaynakta: ayak sesiyle aynı AudioSource'u paylaşsalar
        // savurma, koşarken çalan adımı keserdi.
        AudioSource attackSource = root.AddComponent<AudioSource>();
        attackSource.playOnAwake = false;
        attackSource.spatialBlend = 1f;
        attackSource.rolloffMode = AudioRolloffMode.Linear;
        attackSource.minDistance = 2f;
        attackSource.maxDistance = 34f;

        // --- Ağ bileşenleri ---
        root.AddComponent<NetworkIdentity>();

        NetworkTransformReliable netTransform = root.AddComponent<NetworkTransformReliable>();
        // İstemci otoriteli: pozisyonu sahibi bildirir, sunucu dağıtır.
        netTransform.syncDirection = SyncDirection.ClientToServer;

        // 20 Hz. Mirror'ın kendi Reset()'i de bunu yapıyor ama ona güvenmiyoruz:
        // orada bir sıralama hatası vardı ve bu satır sessizce çalışmayınca
        // syncInterval 0'da kalıp transform her ağ karesinde gidiyordu. Değeri
        // burada açıkça yazmak, prefabın hızını üçüncü parti bir varsayılana
        // bırakmamak demek.
        netTransform.syncInterval = 0.05f;

        // Duruş (dikey bakış + eğilme) da sahibinden gidiyor. NetworkTransform
        // sadece kök objeyi taşıyor; bunlar kökün altında kaldığı için ayrı
        // bileşen gerekiyor.
        PlayerPoseSync poseSync = root.AddComponent<PlayerPoseSync>();
        poseSync.syncDirection = SyncDirection.ClientToServer;
        poseSync.syncInterval = 0.05f;

        NetworkPlayerSetup networkSetup = root.AddComponent<NetworkPlayerSetup>();
        RoundParticipant participant = root.AddComponent<RoundParticipant>();

        // Bıçak ve iz: RoundParticipant'tan sonra eklenmeli, ikisi de onu
        // [RequireComponent] ile istiyor.
        MonsterAttack attack = root.AddComponent<MonsterAttack>();
        root.AddComponent<TrailLeaver>();
        SpectatorController spectator = root.AddComponent<SpectatorController>();

        // --- Bağlantılar ---
        Wire(playerController, "cameraTransform", cameraObject.transform);
        Wire(interactor, "viewTransform", cameraObject.transform);
        Wire(bodyVisual, "body", body.transform);
        Wire(bob, "cameraTransform", cameraObject.transform);
        Wire(flashlight, "spotLight", spot);
        Wire(monsterAura, "auraLight", aura);

        SerializedObject serializedFootsteps = new SerializedObject(footsteps);
        serializedFootsteps.FindProperty("source").objectReferenceValue = footstepSource;
        AudioSetupUtility.AssignClip(serializedFootsteps.FindProperty("lightStep"), "Adim_Kacan");
        AudioSetupUtility.AssignClip(serializedFootsteps.FindProperty("heavyStep"), "Adim_Canavar");

        // Zıplama TUŞUNA ses bağlanmıyor, bilerek: elde olan klip bir iniş
        // vuruşu ve yere değme anına ait. Bkz. FootstepAudio.
        AudioSetupUtility.AssignClip(serializedFootsteps.FindProperty("landClip"), "Inis");
        serializedFootsteps.ApplyModifiedProperties();

        SerializedObject serializedNetwork = new SerializedObject(networkSetup);
        serializedNetwork.FindProperty("playerCamera").objectReferenceValue = camera;
        serializedNetwork.FindProperty("audioListener").objectReferenceValue = listener;
        serializedNetwork.FindProperty("bodyVisual").objectReferenceValue = bodyVisual;

        // Karşıdaki oyuncuda kapalı kalacaklar: girdi, hareket, nişangah,
        // fener kontrolü, kamera sallanması, kendi ayak sesi.
        //
        // MonsterAttack ve TrailLeaver bu listede DEĞİL, bilerek. MonsterAttack
        // karşıdaki oyuncuda da çalışmalı — bıçağın görünmesi ve savrulması
        // oradan sürülüyor — ve host modunda bu liste sunucuda da uygulandığı
        // için kapatsaydık sunucunun isabet kontrolü hiç çalışmazdı. TrailLeaver
        // ise zaten kendi içinde isLocalPlayer kontrolü yapıyor.
        Behaviour[] localOnly = { input, playerController, interactor, flashlight, bob, footsteps };
        SerializedProperty array = serializedNetwork.FindProperty("localOnlyComponents");
        array.arraySize = localOnly.Length;
        for (int i = 0; i < localOnly.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = localOnly[i];

        serializedNetwork.ApplyModifiedProperties();

        // Rol profilleri katılımcının üstünde: rol SyncVar'ı değişince ilgili
        // profil kendiliğinden uygulanıyor, RoundManager'ın profil bilmesine
        // gerek kalmıyor.
        MovementProfile monsterProfile = GetOrCreateProfile("CanavarProfili",
            walkSpeed: 210f, sprintSpeed: 420f, crouchMultiplier: 0.42f,
            slideBoost: 70f, slideFriction: 1.15f);

        MovementProfile runnerProfile = GetOrCreateProfile("KacanProfili",
            walkSpeed: 200f, sprintSpeed: 400f, crouchMultiplier: 0.35f,
            slideBoost: 60f, slideFriction: 1.2f);

        SerializedObject serializedParticipant = new SerializedObject(participant);
        serializedParticipant.FindProperty("monsterProfile").objectReferenceValue = monsterProfile;
        serializedParticipant.FindProperty("runnerProfile").objectReferenceValue = runnerProfile;
        serializedParticipant.FindProperty("bodyRenderer").objectReferenceValue =
            body.GetComponent<Renderer>();

        // Rol değişince ışığı ApplyRole sürüyor: canavarda hâle yanıyor, fener
        // kapanıyor; kaçanda tersi.
        serializedParticipant.FindProperty("monsterAura").objectReferenceValue = monsterAura;
        serializedParticipant.FindProperty("flashlight").objectReferenceValue = flashlight;

        // Elenince hareket ve etkileşim kapansın. CameraBob de listede: elenince
        // kamerayı SpectatorController dünya koordinatıyla sürüyor, bob ise her
        // LateUpdate'te localPosition yazıyor — ikisi aynı transform için
        // yarışıp titremeye yol açıyordu.
        Behaviour[] disableWhenDead = { playerController, interactor, flashlight, bob };
        SerializedProperty deadArray = serializedParticipant.FindProperty("disableWhenEliminated");
        deadArray.arraySize = disableWhenDead.Length;
        for (int i = 0; i < disableWhenDead.Length; i++)
            deadArray.GetArrayElementAtIndex(i).objectReferenceValue = disableWhenDead[i];

        // Ölüm sesi bıçak kaynağından çalıyor: ikisi de tek seferlik ve
        // yakalanma anında zaten aynı yerde oluyorlar.
        serializedParticipant.FindProperty("audioSource").objectReferenceValue = attackSource;
        AudioSetupUtility.AssignClip(serializedParticipant.FindProperty("deathClip"), "Olum");

        serializedParticipant.ApplyModifiedProperties();

        // Bıçağın bağlantıları. Ayarlı sayılar (menzil, koni, atılma, ceza)
        // script'teki varsayılanlarla aynı; burada sadece sahneye özel olmayan
        // referansları bağlıyoruz.
        SerializedObject serializedAttack = new SerializedObject(attack);
        serializedAttack.FindProperty("knife").objectReferenceValue = knifeObject.transform;
        serializedAttack.FindProperty("audioSource").objectReferenceValue = attackSource;
        AudioSetupUtility.AssignClip(serializedAttack.FindProperty("swingClip"), "Bicak_Savurma");
        AudioSetupUtility.AssignClip(serializedAttack.FindProperty("hitClip"), "Bicak_Isabet");
        serializedAttack.ApplyModifiedProperties();

        // İzleyici kamerayı doğrudan sürüyor; feneri de elenince söndürüyor.
        Wire(spectator, "cameraTransform", cameraObject.transform);
        Wire(spectator, "flashlight", flashlight);

        // Oyuncu katmanı ve daraltılmış maskeler. Prefab her kurulumda sıfırdan
        // yapıldığı için burada yazılmazsa Katmanları Kur aracının işi bir
        // sonraki Ağ Kurulumu ile geri giderdi.
        LayerSetup.Apply(root, LayerSetup.Oyuncu);
        LayerSetup.ApplyMasks(root);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return prefab;
    }

    /// <summary>
    /// RoundManager sahnede duruyor ama artık ağ üzerinden durum yayınlıyor;
    /// bunun için NetworkIdentity şart. Sahne nesneleri sunucu başlayınca
    /// otomatik spawn edilir.
    /// </summary>
    private static void PrepareRoundManager()
    {
        RoundManager manager = Object.FindObjectOfType<RoundManager>(true);
        if (manager == null)
        {
            Debug.LogWarning("Sahnede RoundManager yok; tur sistemi çalışmaz.");
            return;
        }

        // Ağ testinde kapatılmış olabilir.
        if (!manager.gameObject.activeSelf)
        {
            Undo.RecordObject(manager.gameObject, "Ağ Kurulumu");
            manager.gameObject.SetActive(true);
        }

        if (manager.GetComponent<NetworkIdentity>() == null)
            Undo.AddComponent<NetworkIdentity>(manager.gameObject);
    }

    private static MovementProfile GetOrCreateProfile(string name, float walkSpeed, float sprintSpeed,
        float crouchMultiplier, float slideBoost, float slideFriction)
    {
        const string folder = "Assets/_ScriptableObjects";
        string path = $"{folder}/{name}.asset";

        MovementProfile existing = AssetDatabase.LoadAssetAtPath<MovementProfile>(path);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "_ScriptableObjects");

        MovementProfile profile = ScriptableObject.CreateInstance<MovementProfile>();
        profile.walkSpeed = walkSpeed;
        profile.sprintSpeed = sprintSpeed;
        profile.crouchSpeedMultiplier = crouchMultiplier;
        profile.slideBoost = slideBoost;
        profile.slideFriction = slideFriction;
        AssetDatabase.CreateAsset(profile, path);

        return profile;
    }

    /// <summary>
    /// Doğum noktaları. Mirror bunlar yoksa herkesi (0,0,0)'da doğurur —
    /// labirentin merkez hücresi duvar olduğu için oyuncular duvarın içinde
    /// belirirdi. Noktalar fizikle sınanıyor, harita elle düzenlenmiş olsa bile
    /// çalışıyor.
    /// </summary>
    private static int BuildSpawnPoints()
    {
        const string groupName = "DogumNoktalari";
        const int wanted = 6;
        const float areaHalfSize = 24f;
        const float minSpacing = 8f;

        GameObject existing = GameObject.Find(groupName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        GameObject group = new GameObject(groupName);
        Undo.RegisterCreatedObjectUndo(group, "Ağ Kurulumu");

        int placed = 0;
        int attempts = 0;

        while (placed < wanted && attempts < 600)
        {
            attempts++;

            Vector3 candidate = new Vector3(
                Random.Range(-areaHalfSize, areaHalfSize),
                0.7f, // kapsül merkezi: ayaklar zeminde
                Random.Range(-areaHalfSize, areaHalfSize));

            // Kontrol küresi doğum noktasından ayrı: aynı yerde ve 0.7 yarıçapla
            // sorarsak kürenin altı zemine değiyor ve her nokta "dolu" çıkıyor.
            Vector3 probe = candidate + Vector3.up * 0.25f;

            if (Physics.CheckSphere(probe, 0.55f, ~0, QueryTriggerInteraction.Ignore))
                continue; // duvarın içi
            if (IsTooCloseToExisting(group.transform, candidate, minSpacing))
                continue;

            GameObject point = new GameObject($"Dogum_{placed + 1}");
            point.transform.SetParent(group.transform, false);
            point.transform.position = candidate;
            point.AddComponent<NetworkStartPosition>();

            placed++;
        }

        if (placed == 0)
            Debug.LogWarning("Hiç doğum noktası yerleştirilemedi — oyuncular (0,0,0)'da, " +
                "yani labirentin duvarında doğacak. Harita kurulu mu?");

        return placed;
    }

    private static bool IsTooCloseToExisting(Transform group, Vector3 candidate, float minDistance)
    {
        foreach (Transform child in group)
        {
            if ((child.position - candidate).sqrMagnitude < minDistance * minDistance)
                return true;
        }

        return false;
    }

    private static NetworkManager BuildNetworkManager(GameObject playerPrefab)
    {
        GameObject managerObject = GameObject.Find(ManagerName);
        if (managerObject == null)
        {
            managerObject = new GameObject(ManagerName);
            Undo.RegisterCreatedObjectUndo(managerObject, "Ağ Kurulumu");
        }

        // Taşıma katmanı önce eklenmeli; NetworkManager Awake'te onu arıyor.
        KcpTransport transport = managerObject.GetComponent<KcpTransport>();
        if (transport == null)
            transport = managerObject.AddComponent<KcpTransport>();

        NetworkManager manager = managerObject.GetComponent<NetworkManager>();
        if (manager == null)
            manager = managerObject.AddComponent<NetworkManager>();

        // Mirror'ın ekran üstü Host/Client butonları kaldırıldı: kendi lobimiz
        // devreye girdi ve HUD menünün üstüne biniyor. Menüsüz hızlı bir test
        // gerekirse NetworkManager objesine elle geri eklenebilir.
        NetworkManagerHUD hud = managerObject.GetComponent<NetworkManagerHUD>();
        if (hud != null)
            Undo.DestroyObjectImmediate(hud);

        manager.transport = transport;
        manager.playerPrefab = playerPrefab;
        manager.autoCreatePlayer = true;

        // Kadro 5 kişi (1 canavar + 4 kaçan). Mirror'ın 100'lük varsayılanı
        // altıncı kişinin bağlanıp lobide yer bulamamasına yol açardı.
        manager.maxConnections = LobbyRoster.MaxPlayers;

        return manager;
    }

    /// <summary>
    /// Ağ testi sırasında karışmaması için sahnedeki objeyi kapatır. Silmiyoruz;
    /// sonraki adımlarda tur sistemi ve menü geri bağlanacak.
    /// </summary>
    private static void DisableForNetworkTest(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        if (target == null || !target.activeSelf)
            return;

        Undo.RecordObject(target, "Ağ Kurulumu");
        target.SetActive(false);
    }

    private static void Wire(Object target, string propertyName, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);

        if (property == null)
        {
            Debug.LogError($"{target.GetType().Name}: '{propertyName}' alanı bulunamadı.");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }
}
