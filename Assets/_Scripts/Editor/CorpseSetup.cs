using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ceset prefab'ını kurar: fizik motorlu, itilebilir, sunucu otoriteli.
/// CLAUDE.md — "haritada kalsın, dümdüz kalmasın, biri yürüyerek ittirebilsin".
///
/// **Neden ayrı bir araç.** `Corpse` bileşeni `RoundManager`'ın bir alanı
/// üzerinden çağrılıyor ama kendi prefab'ı `Ağ Kurulumu`'na hiç bağlı değil —
/// oyuncu modeliyle ilgisi yok, sadece kurbanın gövdesinden ÇALIŞMA ANINDA bir
/// kopya alıp fiziğe bağlıyor. `Sesli Sohbet Kur`/`EOS Kurulumu`'yla aynı
/// gerekçeyle bağımsız: bütün model/menü/ses zincirini gerektirmeden tek
/// başına çalıştırılabilir olmalı.
///
/// **Fizik sunucu otoriteli.** `NetworkRigidbodyReliable`
/// (`syncDirection = ServerToClient`) Mirror'ın kendi mekanizması: sunucuda
/// gerçekten simüle ediyor, her istemcide `isKinematic = true` yapıp yalnızca
/// geleni uyguluyor. Aksi hâlde her istemci cesedi kendi başına simüle eder ve
/// birkaç itmeden sonra herkes farklı yerde görür — CLAUDE.md bölüm 4'teki
/// "his istemcide, karar sunucuda" kuralının fizik karşılığı.
///
/// **`Sus` katmanında** (bkz. LayerSetup, CLAUDE.md bölüm 16): gövdeyi durdurur
/// ama canavarın vuruş ışınını kesmez — varil/kasa ile aynı gerekçe, ceset
/// arkasına saklanmak kalkan olmamalı.
///
/// Menü: Yakalamaca > Ceset Sistemini Kur
/// </summary>
public static class CorpseSetup
{
    private const string PrefabFolder = "Assets/_Prefabs";
    private const string PrefabPath = PrefabFolder + "/Corpse.prefab";
    private const string ManagerName = "NetworkManager";

    [MenuItem("Yakalamaca/Ceset Sistemini Kur", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Ceset Sistemini Kur")]
    private static void Run()
    {
        GameObject prefab = BuildCorpsePrefab();
        int registered = RegisterSpawnPrefab(prefab);
        bool wired = WireRoundManager(prefab);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        string managerNote = registered == 1
            ? "NetworkManager'ın doğurabileceği prefab listesine eklendi."
            : "NetworkManager'ın listesinde zaten vardı.";

        string roundNote = wired
            ? "RoundManager.corpsePrefab bağlandı."
            : "RoundManager sahnede bulunamadı — Ağ Kurulumu'nu önce çalıştır.";

        Debug.Log($"Ceset sistemi kuruldu.\n{managerNote}\n{roundNote}\n\n" +
            "Bir kaçan yakalanıp ölüm klibi bitince o noktada fizik motorlu, " +
            "itilebilir bir ceset kalıyor. Bir sonraki tur başında temizleniyor.");
    }

    /// <summary>
    /// Boş bir kapsül gövdesi: Rigidbody + Collider + ağ kimliği + sunucu
    /// otoriteli konum senkronu + `Corpse` bileşeni. Görsel gövde YOK —
    /// `Corpse.OnStartClient` onu kurbanın o anki modelinden ÇALIŞMA ANINDA
    /// klonluyor, prefab'a gömülü bir mesh gerekmiyor.
    /// </summary>
    private static GameObject BuildCorpsePrefab()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "_Prefabs");

        GameObject root = new GameObject("Corpse");

        // Kabaca diz çökmüş/yığılmış bir gövdeyi kapsayan tek parça collider.
        // Tam silüet önemli değil — itilebilir bir "kütle" olması yetiyor.
        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.radius = 0.35f;
        collider.height = 1.2f;
        collider.center = new Vector3(0f, 0.6f, 0f);
        collider.direction = 1; // Y ekseni

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.mass = 70f;
        rb.drag = 0.5f;        // Unity 2022.3'te alan adı bu — linearDamping ancak Unity 6'da geldi
        rb.angularDrag = 0.8f;
        rb.useGravity = true;
        rb.isKinematic = false; // gerçek doğası bu; ağ otoritesi olmayanlarda NetworkRigidbodyReliable çalışma anında kapatıyor
        // Dönüş KASITLI OLARAK kilitlenmedi: "dümdüz kalmasın" isteği tam
        // olarak bu — sert bir itme cesedi yan yatırıp devirebilmeli.

        root.AddComponent<NetworkIdentity>();

        NetworkRigidbodyReliable netRb = root.AddComponent<NetworkRigidbodyReliable>();
        netRb.target = root.transform;
        netRb.syncDirection = SyncDirection.ServerToClient;
        netRb.updateMethod = UpdateMethod.FixedUpdate; // fizik gövdesi, kareyle değil fizik adımıyla örnekleniyor

        root.AddComponent<Corpse>();

        LayerSetup.Apply(root, LayerSetup.Sus);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return prefab;
    }

    /// <summary>Zaten listedeyse tekrar eklemiyor — araç kaç kez çalıştırılırsa çalıştırılsın liste tek satır kalıyor.</summary>
    private static int RegisterSpawnPrefab(GameObject prefab)
    {
        GameObject managerObject = GameObject.Find(ManagerName);
        if (managerObject == null)
        {
            Debug.LogWarning($"'{ManagerName}' sahnede yok — önce Yakalamaca > Ağ Kurulumu (1. adım) çalıştır.");
            return 0;
        }

        NetworkManager manager = managerObject.GetComponent<NetworkManager>();
        if (manager == null)
            return 0;

        if (manager.spawnPrefabs.Contains(prefab))
            return 0;

        Undo.RecordObject(manager, "Ceset Sistemini Kur");
        manager.spawnPrefabs.Add(prefab);
        EditorUtility.SetDirty(manager);
        return 1;
    }

    private static bool WireRoundManager(GameObject prefab)
    {
        RoundManager manager = Object.FindObjectOfType<RoundManager>(true);
        if (manager == null)
            return false;

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("corpsePrefab");

        if (property == null)
        {
            Debug.LogError("RoundManager: 'corpsePrefab' alanı bulunamadı.");
            return false;
        }

        property.objectReferenceValue = prefab;
        serialized.ApplyModifiedProperties();
        return true;
    }
}
