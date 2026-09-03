using System;
using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Açık sahnedeki Mirror kaynaklı konsol hatalarını kapatır, lobi kamerasını
/// kurar ve **sahneyi kaydeder**. Unity derlemeyi bitirdiğinde bir kez
/// kendiliğinden çalışır; sonrasında menüden elle tetiklenebilir.
///
/// Konsolu dolduran hatalar iki kaynaktan geliyor:
///
/// 1) RoundManager artık NetworkBehaviour ama sahnedeki objesinde
///    NetworkIdentity yok. Mirror bunu OnValidate'te hata yazıyor; dahası
///    Update içindeki `isServer` her karede NullReference atıyor — kimlik
///    yoksa netIdentity null. Doğru çözüm kimliği eklemek: sahne nesneleri
///    sunucu açılınca kendiliğinden spawn edilir.
///
/// 2) Tek oyunculu prototipten kalan Player ve TestKacanlar objelerinin
///    üstünde hâlâ RoundParticipant duruyor. Bunlara NetworkIdentity
///    **eklenmemeli**: NetworkServer.SpawnObjects sunucu açılınca sahnedeki
///    bütün NetworkIdentity'leri — kapalı olanlar dahil — SetActive(true)
///    yapıp spawn ediyor. Yani ağ testi için kapattığımız eski Player geri
///    açılır, kendi kamerası ve AudioListener'ı prefabtan doğan oyuncuyla
///    çakışırdı. Katılımcı artık NetworkPlayer prefabından geldiği için
///    doğru olan, ağ öncesinden kalan bileşeni sökmek.
///
///    Sökmek bedava değil: RoundParticipant'a [RequireComponent] ile bağlı
///    MonsterAttack ve TrailLeaver de gitmek zorunda, üstlerindeki ayarlı
///    değerlerle birlikte (bıçak menzili, ses klipleri, iz aralığı). Bu yüzden
///    sökmeden önce ilgili kök obje _Prefabs/_Eski altına prefab olarak
///    yedekleniyor — ağ prefabına taşınacakları gün referans orada duruyor.
///
/// Ayrıca sahnede hiç kamera kalmamıştı: eski Player kapatılınca tek kamera
/// ağ prefabının içinde kaldı, o da ancak bağlanınca doğuyor. Bağlanmadan önce
/// ekran "No cameras rendering" diyordu. Onarım, koridora bakan bir LobiKamera
/// bırakıyor; NetworkPlayerSetup.DisableOtherCameras zaten oyuncu doğunca onu
/// kapatıyor, yani devir teslim hazır.
///
/// Menü: Yakalamaca > Hataları Temizle (Sahne Onarımı)
/// </summary>
public static class SceneRepair
{
    private const string BackupFolder = "Assets/_Prefabs/_Eski";
    private const string LobbyCameraName = "LobiKamera";

    /// <summary>Kendiliğinden onarım bir kez çalışsın diye; sonrası elle.</summary>
    private static string AutoRunKey => "Yakalamaca.SceneRepair.AutoRan:" + Application.dataPath;

    // ---------- Tetikleyiciler ----------

    [MenuItem("Yakalamaca/Hataları Temizle (Sahne Onarımı)")]
    private static void RepairFromMenu() => RepairOpenScene(automatic: false);

    /// <summary>
    /// Play modunda menüyü kapatıyoruz: o sırada yapılan sahne değişiklikleri
    /// Play bitince geri alınır, kullanıcı da düzeldi sanır.
    /// </summary>
    [MenuItem("Yakalamaca/Hataları Temizle (Sahne Onarımı)", true)]
    private static bool CanRepairFromMenu() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [InitializeOnLoadMethod]
    private static void ScheduleAutoRepair()
    {
        EditorApplication.update += AutoRepairTick;
    }

    /// <summary>
    /// Play modunda, derleme sırasında veya sahne yüklenmeden çalışmıyor;
    /// koşullar oluşana kadar her karede yeniden bakıyor. Kullanıcı Play'den
    /// çıkınca onarım kendiliğinden devreye giriyor.
    /// </summary>
    private static void AutoRepairTick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling
            || EditorApplication.isUpdating)
            return;

        if (!SceneManager.GetActiveScene().isLoaded)
            return;

        EditorApplication.update -= AutoRepairTick;

        if (EditorPrefs.GetBool(AutoRunKey, false))
            return;

        EditorPrefs.SetBool(AutoRunKey, true);
        RepairOpenScene(automatic: true);
    }

    // ---------- Onarım ----------

    private static void RepairOpenScene(bool automatic)
    {
        List<string> addedIdentities = new List<string>();
        List<string> removedComponents = new List<string>();
        List<string> backups = new List<string>();
        HashSet<GameObject> backedUpRoots = new HashSet<GameObject>();

        // Kapalı objelerdeki bileşenler de hata yazıyor — OnValidate aktiflik
        // gözetmiyor — o yüzden includeInactive.
        NetworkBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<NetworkBehaviour>(true);

        foreach (NetworkBehaviour behaviour in behaviours)
        {
            // Önceki turda bağımlı bileşen olarak silinmiş olabilir.
            if (behaviour == null || HasIdentity(behaviour))
                continue;

            GameObject owner = behaviour.gameObject;

            // Ağa taşınmış sahne nesneleri: tur yöneticisi ve tetiklenebilirler
            // (kapılar). Bunlara kimlik ekleniyor — durumları herkeste aynı
            // olmak zorunda. Geri kalanı ağ öncesinden kalma artık.
            if (behaviour is RoundManager || behaviour is Triggerable)
            {
                Undo.AddComponent<NetworkIdentity>(owner);
                addedIdentities.Add(owner.name);
                continue;
            }

            GameObject root = owner.transform.root.gameObject;
            if (backedUpRoots.Add(root))
            {
                string path = Backup(root);
                if (path != null)
                    backups.Add(path);
            }

            RemoveWithDependents(behaviour, removedComponents, new HashSet<Component>());
        }

        string lobbyCamera = EnsureLobbyCamera();

        if (addedIdentities.Count == 0 && removedComponents.Count == 0 && lobbyCamera == null)
        {
            if (!automatic)
                Debug.Log("Sahne temiz: eksik NetworkIdentity, artık ağ bileşeni ve kamerasız ekran yok.");

            ReportNetworkManager();
            return;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        string report = automatic
            ? "Sahne kendiliğinden onarıldı ve kaydedildi."
            : "Sahne onarıldı ve kaydedildi.";

        if (addedIdentities.Count > 0)
            report += "\n\nNetworkIdentity eklendi: " + string.Join(", ", addedIdentities);

        if (lobbyCamera != null)
            report += "\n\n" + lobbyCamera;

        if (backups.Count > 0)
            report += "\n\nSökülmeden önce yedeklendi:\n  " + string.Join("\n  ", backups);

        if (removedComponents.Count > 0)
            report += "\n\nAğ öncesinden kalan bileşenler söküldü (katılımcı artık " +
                "NetworkPlayer prefabından geliyor):\n  " + string.Join("\n  ", removedComponents);

        Debug.Log(report);
        ReportNetworkManager();
    }

    /// <summary>
    /// Mirror'ın OnValidate'teki kuralının aynısı: kimlik kendinde ya da bir
    /// üst objede olabilir.
    /// </summary>
    private static bool HasIdentity(Component behaviour)
        => behaviour.GetComponent<NetworkIdentity>() != null
        || behaviour.GetComponentInParent<NetworkIdentity>(true) != null;

    // ---------- Lobi kamerası ----------

    /// <summary>
    /// Bağlanmadan önce ekranın siyah kalmaması için sahneye kamera koyar.
    /// AudioListener bilerek eklenmiyor: oyuncu doğduğu an kendi dinleyicisini
    /// açıyor, ikisi üst üste binerse Unity "2 audio listener" uyarısı yazar.
    /// Lobide çalan ses de yok.
    /// </summary>
    private static string EnsureLobbyCamera()
    {
        // Parametresiz FindObjectsOfType yalnızca açık ve etkin olanları döndürür;
        // yani "Play'e basınca gerçekten çizecek olanlar" tam olarak bu küme.
        if (UnityEngine.Object.FindObjectsOfType<Camera>().Length > 0)
            return null;

        Vector3 position = FindLobbyViewpoint();

        GameObject cameraObject = new GameObject(LobbyCameraName);
        Undo.RegisterCreatedObjectUndo(cameraObject, "Sahne Onarımı");

        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetPositionAndRotation(position, LookIntoOpenSpace(position));
        cameraObject.AddComponent<Camera>();

        return $"{LobbyCameraName} eklendi — bağlanmadan önce ekran artık siyah değil. " +
            "Oyuncu doğunca NetworkPlayerSetup bunu kapatıp devri teslim alıyor.";
    }

    /// <summary>
    /// Kameranın duracağı nokta. Karanlık bu oyunda bilerek agresif: ortam ışığı
    /// neredeyse sıfır, sis yoğun, lambalar loş ve aralarındaki mesafe zifiri
    /// olacak şekilde ayarlı (bkz. AtmosphereSetup). Rastgele bir noktada duran
    /// kamera teknik olarak çalışır ama ekran yine siyah görünür — o yüzden
    /// lambanın altını seçiyoruz: hem aydınlık, hem de yerleştirilirken fizikle
    /// boş olduğu zaten doğrulanmış bir nokta.
    /// </summary>
    private static Vector3 FindLobbyViewpoint()
    {
        // Göz hizası: doğum noktasının kapsül merkezi + PlayerController'ın
        // kullandığı göz farkı.
        float eyeHeight = 0.7f + (64f - 36f) * PlayerController.UnitsToMeters;

        foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
        {
            if (light.type != LightType.Point)
                continue;

            Vector3 underLamp = light.transform.position;
            underLamp.y = eyeHeight;
            return underLamp;
        }

        // Lamba yoksa doğum noktaları da fizikle sınanmış boş hücrelerde.
        NetworkStartPosition[] spawns = UnityEngine.Object.FindObjectsOfType<NetworkStartPosition>();
        if (spawns.Length > 0)
        {
            Vector3 spawn = spawns[0].transform.position;
            spawn.y = eyeHeight;
            return spawn;
        }

        return new Vector3(0f, eyeHeight, 0f);
    }

    /// <summary>Duvara bakmasın diye en açık yönü arıyoruz.</summary>
    private static Quaternion LookIntoOpenSpace(Vector3 from)
    {
        const float maxDistance = 40f;

        float bestDistance = -1f;
        Vector3 bestDirection = Vector3.forward;

        for (int i = 0; i < 12; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;

            float distance = Physics.Raycast(from, direction, out RaycastHit hit, maxDistance,
                ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : maxDistance;

            if (distance <= bestDistance)
                continue;

            bestDistance = distance;
            bestDirection = direction;
        }

        return Quaternion.LookRotation(bestDirection, Vector3.up);
    }

    // ---------- Yedekleme ve sökme ----------

    /// <summary>
    /// Kök objeyi prefab olarak yedekler. Yedek sahnedeki objeden değil bir
    /// kopyasından alınıyor; sahneye dokunmuyoruz. Kopyadaki kimliksiz
    /// NetworkBehaviour'lara NetworkIdentity ekleniyor, yoksa yedeğin kendisi
    /// aynı hatayı yazmaya başlardı. Prefab hiçbir yerde kayıtlı olmadığı için
    /// oyunda spawn edilmez, sadece durur.
    /// </summary>
    private static string Backup(GameObject root)
    {
        if (!AssetDatabase.IsValidFolder(BackupFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Prefabs"))
                AssetDatabase.CreateFolder("Assets", "_Prefabs");

            AssetDatabase.CreateFolder("Assets/_Prefabs", "_Eski");
        }

        string path = BackupFolder + "/Eski_" + root.name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            return null; // Daha önce yedeklenmiş; üstüne yazmıyoruz.

        // Kapalı obje kopyalanınca Awake çalışmaz, sahneye yan etkisi yok.
        GameObject clone = UnityEngine.Object.Instantiate(root);
        clone.name = root.name;

        foreach (NetworkBehaviour behaviour in clone.GetComponentsInChildren<NetworkBehaviour>(true))
        {
            if (!HasIdentity(behaviour))
                behaviour.gameObject.AddComponent<NetworkIdentity>();
        }

        PrefabUtility.SaveAsPrefabAsset(clone, path);
        UnityEngine.Object.DestroyImmediate(clone);

        return path;
    }

    /// <summary>
    /// Bileşeni söker. Ona [RequireComponent] ile bağlı olanlar önce gidiyor;
    /// yoksa Unity silmeyi reddedip hata yazar.
    /// </summary>
    private static void RemoveWithDependents(Component target, List<string> log, HashSet<Component> visited)
    {
        if (target == null || !visited.Add(target))
            return;

        GameObject owner = target.gameObject;
        Type targetType = target.GetType();

        // GetComponents kopya döndürüyor; içinde silmek güvenli.
        foreach (Component other in owner.GetComponents<Component>())
        {
            if (other == null || other == target)
                continue;

            if (DependsOn(other.GetType(), targetType))
                RemoveWithDependents(other, log, visited);
        }

        log.Add(owner.name + " -> " + targetType.Name);
        Undo.DestroyObjectImmediate(target);
    }

    private static bool DependsOn(Type candidate, Type required)
    {
        object[] attributes = candidate.GetCustomAttributes(typeof(RequireComponent), true);

        foreach (object attribute in attributes)
        {
            RequireComponent requirement = (RequireComponent)attribute;

            if (Matches(requirement.m_Type0, required)
                || Matches(requirement.m_Type1, required)
                || Matches(requirement.m_Type2, required))
                return true;
        }

        return false;
    }

    private static bool Matches(Type declared, Type required)
        => declared != null && declared.IsAssignableFrom(required);

    /// <summary>
    /// Onarım bittikten sonra ağ kurulumunun ayakta olduğunu doğrular. Hata
    /// değil ama eksikse oyun sessizce çalışmaz, o yüzden uyarıyoruz.
    /// </summary>
    private static void ReportNetworkManager()
    {
        NetworkManager manager = UnityEngine.Object.FindObjectOfType<NetworkManager>(true);

        if (manager == null)
        {
            Debug.LogWarning("Sahnede NetworkManager yok. Yakalamaca > Ağ Kurulumu (1. adım) çalıştır.");
            return;
        }

        if (manager.playerPrefab == null)
            Debug.LogWarning("NetworkManager.playerPrefab boş. Yakalamaca > Ağ Kurulumu (1. adım) çalıştır.");

        if (manager.transport == null)
            Debug.LogWarning("NetworkManager.transport boş. Yakalamaca > Ağ Kurulumu (1. adım) çalıştır.");
    }
}
