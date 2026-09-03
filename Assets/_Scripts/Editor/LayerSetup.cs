using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Katman düzenini kurar ve fizik maskelerini daraltır. CLAUDE.md teknik borç 3.
///
/// **Sorun neydi:** projedeki her nesne `Default` katmanındaydı ve bütün
/// `LayerMask` alanları `~0` ("her şeye bak") duruyordu. Işın atan üç sistem de
/// bu yüzden görmemesi gereken şeylere takılıyordu:
///
/// - Canavarın vuruş ışını yerden 90 cm'den yatay gidiyor. `Harita Süsle`
///   varilleri **duvar diplerine** dağıtıyor ve collider'larını koruyor, yani
///   köşeye sıkışan kaçanın önündeki varil ışını kesiyor ve sunucu vuruşu
///   reddediyor. Canavarın ekranında ıskalama oynuyor, sebebi hiçbir yerde
///   görünmüyor.
/// - İzleyici kamerasının duvar kaçınma ışını diğer oyuncuların kapsüllerine de
///   çarpıyor; biri arkadan geçtiğinde kamera öne zıplıyor.
///
/// **Dört katman:**
///
/// | Katman | Anlamı |
/// |---|---|
/// | `Harita` | Katı dünya: zemin, duvar, tavan, kapı. Işını KESER. |
/// | `Sus` | Varil, kasa, giydirme. Gövdeyi durdurur, ışını kesmez. |
/// | `Oyuncu` | Kaçan ve canavar. |
/// | `Etkilesim` | Düğme ve terminal — nişan alınacak şeyler. |
///
/// **Çarpışma matrisine (Physics ayarları) DOKUNULMUYOR.** Katmanlar burada
/// yalnızca ışın filtresi olarak kullanılıyor; her şey eskisi gibi her şeyle
/// çarpışmaya devam ediyor. Yani hareketin hissi bu araçtan hiç etkilenmiyor —
/// matrisi kurcalamak, Source hareketini sessizce bozabilecek tek adımdı.
///
/// **Kapı `Etkilesim` değil `Harita`.** `SlidingDoor` de IInteractable ama kapalı
/// kapının arkasından vurulmamalı; bileşene değil işleve bakıyoruz. Kapı
/// açılınca panel tavana çekildiği için 90 cm'deki vuruş ışınını zaten kesmiyor.
///
/// **Neden ayrı bir araç:** koddaki `= ~0` varsayılanını değiştirmek sahnede
/// duran bileşenleri değiştirmiyor — MovementProfile'daki tuzağın aynısı
/// (CLAUDE.md bölüm 1). Bu araç maskeleri hem sahnede hem oyuncu prefabında
/// yeniden yazıyor.
///
/// **Haritayı yeniden kurmayı gerektirmiyor:** sahneyi gezip katman atıyor.
/// Elle düzenlemeye başladıktan sonra da güvenle çalıştırılabilir — hiçbir şey
/// silmiyor, yalnızca katman ve maske alanlarına yazıyor.
///
/// Menü: Yakalamaca > Katmanları Kur
/// </summary>
public static class LayerSetup
{
    public const string Harita = "Harita";
    public const string Sus = "Sus";
    public const string Oyuncu = "Oyuncu";
    public const string Etkilesim = "Etkilesim";

    private static readonly string[] AllLayers = { Harita, Sus, Oyuncu, Etkilesim };

    private const string PlayerPrefabPath = "Assets/_Prefabs/NetworkPlayer.prefab";

    // Kurulum araçlarının ürettiği kök objeler. Bunların dışına çıkılmıyor:
    // menü canvas'ı, NetworkManager ve doğum noktaları Default'ta kalmalı,
    // çünkü hiçbiri fizik sorgusuna girmiyor.
    private static readonly string[] MapRoots = { "Harita", "HedefSistemi", "TestEtkilesim" };

    // Harita'nın altında olup Harita SAYILMAYAN gruplar.
    private static readonly string[] DecorGroups = { "Giydirme", "Suslemeler" };

    [MenuItem("Yakalamaca/Katmanları Kur", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Katmanları Kur")]
    private static void Run()
    {
        int created = EnsureLayers();

        if (LayerMask.NameToLayer(Harita) < 0)
        {
            EditorUtility.DisplayDialog("Katmanlar kurulamadı",
                "Boş katman yuvası kalmamış. Project Settings > Tags and Layers " +
                "altından yer açıp tekrar dene.", "Tamam");
            return;
        }

        int scene = AssignScene();
        int prefab = AssignPlayerPrefab();
        List<string> masks = RewriteSceneMasks();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        string maskReport = masks.Count > 0
            ? "\n\nSahnede daraltılan maskeler:\n  " + string.Join("\n  ", masks)
            : "\n\nSahnede daraltılacak maske yoktu (asıl maskeler prefabta).";

        EditorUtility.DisplayDialog("Katmanlar kuruldu",
            $"{created} yeni katman tanımlandı ({string.Join(", ", AllLayers)}).\n" +
            $"{scene} sahne nesnesine, {prefab} prefab nesnesine katman atandı.\n" +
            "Oyuncu prefabındaki maskeler daraltıldı." +
            maskReport +
            "\n\nÇarpışma matrisine dokunulmadı: hareket ve çarpışma aynen eskisi gibi.",
            "Tamam");
    }

    // ---------- 1. Katmanları tanımla ----------

    /// <summary>
    /// TagManager.asset'e eksik katmanları yazar. 0-7 arası Unity'nin kendi
    /// yuvaları — boş görünenler dahil — o yüzden 8'den başlıyoruz.
    /// </summary>
    private static int EnsureLayers()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("TagManager.asset okunamadı, katmanlar tanımlanamadı.");
            return 0;
        }

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        int created = 0;

        foreach (string name in AllLayers)
        {
            if (Exists(layers, name))
                continue;

            int slot = FirstEmptySlot(layers);
            if (slot < 0)
            {
                Debug.LogError($"Boş katman yuvası kalmadı, '{name}' tanımlanamadı.");
                continue;
            }

            layers.GetArrayElementAtIndex(slot).stringValue = name;
            created++;
        }

        if (created > 0)
            tagManager.ApplyModifiedProperties();

        return created;
    }

    private static bool Exists(SerializedProperty layers, string name)
    {
        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == name)
                return true;
        }

        return false;
    }

    private static int FirstEmptySlot(SerializedProperty layers)
    {
        for (int i = 8; i < layers.arraySize; i++)
        {
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                return i;
        }

        return -1;
    }

    // ---------- 2. Sahneye katman ata ----------

    private static int AssignScene()
    {
        int touched = 0;

        foreach (string rootName in MapRoots)
        {
            GameObject root = GameObject.Find(rootName);
            if (root != null)
                touched += Walk(root.transform, Harita);
        }

        // Oyuncular kök listede değil: TestBot sahnenin kökünde duruyor, ağdan
        // doğanlar ise hiç sahnede değil. Bileşenden bulmak tek güvenilir yol.
        foreach (RoundParticipant participant in
            Object.FindObjectsByType<RoundParticipant>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            touched += Walk(participant.transform, Oyuncu);
        }

        return touched;
    }

    /// <summary>
    /// Hiyerarşiyi gezer. Her nesne ya kendi kuralıyla bir katman seçer ya da
    /// ebeveyninden devralır — böylece terminalin ekranı da `Etkilesim`,
    /// giydirmenin altındaki her parça da `Sus` oluyor.
    /// </summary>
    private static int Walk(Transform node, string inherited)
    {
        string layer = Classify(node, inherited);
        int touched = SetLayer(node.gameObject, layer) ? 1 : 0;

        for (int i = 0; i < node.childCount; i++)
            touched += Walk(node.GetChild(i), layer);

        return touched;
    }

    private static string Classify(Transform node, string inherited)
    {
        // Nişan alınacak şeyler. SlidingDoor bilerek DIŞARIDA: o da
        // IInteractable ama kapalı kapının arkasından vurulmamalı.
        if (node.GetComponent<Terminal>() != null || node.GetComponent<UseButton>() != null)
            return Etkilesim;

        if (node.GetComponent<SlidingDoor>() != null)
            return Harita;

        if (node.GetComponent<RoundParticipant>() != null)
            return Oyuncu;

        foreach (string group in DecorGroups)
        {
            if (node.name == group)
                return Sus;
        }

        return inherited;
    }

    private static bool SetLayer(GameObject target, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0 || target.layer == layer)
            return false;

        Undo.RecordObject(target, "Katmanları Kur");
        target.layer = layer;
        EditorUtility.SetDirty(target);
        return true;
    }

    // ---------- 3. Oyuncu prefabı ----------

    /// <summary>
    /// Oyuncu ağdan doğduğu için asıl maskeler burada duruyor; sahnedeki
    /// kopyalara yazmak yetmez.
    /// </summary>
    private static int AssignPlayerPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
        {
            Debug.LogWarning("NetworkPlayer.prefab bulunamadı — önce Yakalamaca > Ağ Kurulumu (1. adım).");
            return 0;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        int touched;

        try
        {
            touched = WalkPrefab(contents.transform);
            ApplyMasks(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        return touched;
    }

    /// <summary>
    /// Prefab içeriği geçici bir sahnede açıldığı için Undo yok, ayrı gezinme.
    /// Tamamı `Oyuncu`: tek collider kökte ama model ve kamera da aynı katmanda
    /// dursun ki ileride eklenecek bir collider şaşırtmasın.
    /// </summary>
    private static int WalkPrefab(Transform node)
    {
        int layer = LayerMask.NameToLayer(Oyuncu);
        if (layer < 0)
            return 0;

        int touched = 0;

        if (node.gameObject.layer != layer)
        {
            node.gameObject.layer = layer;
            touched++;
        }

        for (int i = 0; i < node.childCount; i++)
            touched += WalkPrefab(node.GetChild(i));

        return touched;
    }

    // ---------- 4. Maskeleri daralt ----------

    /// <summary>
    /// Sahnede duran kopyalar için: TestBot ve ağ öncesinden kalan artıklar.
    /// Asıl oyuncununki ApplyMasks ile prefaba yazılıyor.
    /// </summary>
    private static List<string> RewriteSceneMasks()
    {
        List<string> report = new List<string>();

        int obstacle = Mask(Harita);
        int interact = Mask(Harita, Etkilesim);

        foreach (MonsterAttack attack in
            Object.FindObjectsByType<MonsterAttack>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (SetMask(attack, "obstacleMask", obstacle))
                report.Add($"{attack.name} > MonsterAttack.obstacleMask = Harita");
        }

        foreach (SpectatorController spectator in
            Object.FindObjectsByType<SpectatorController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (SetMask(spectator, "obstacleMask", obstacle))
                report.Add($"{spectator.name} > SpectatorController.obstacleMask = Harita");
        }

        foreach (PlayerInteractor interactor in
            Object.FindObjectsByType<PlayerInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (SetMask(interactor, "interactMask", interact))
                report.Add($"{interactor.name} > PlayerInteractor.interactMask = Harita + Etkilesim");
        }

        return report;
    }

    /// <summary>
    /// Bir kök altındaki bütün maskeleri daraltır. Kurulum araçları da
    /// çağırıyor: oyuncu prefabı her Ağ Kurulumu'nda sıfırdan yapıldığı için
    /// orada yazılmazsa bu aracın işi bir sonraki kurulumda geri giderdi.
    /// </summary>
    public static void ApplyMasks(GameObject root)
    {
        int obstacle = Mask(Harita);
        int interact = Mask(Harita, Etkilesim);

        foreach (MonsterAttack attack in root.GetComponentsInChildren<MonsterAttack>(true))
            SetMask(attack, "obstacleMask", obstacle);

        foreach (SpectatorController spectator in root.GetComponentsInChildren<SpectatorController>(true))
            SetMask(spectator, "obstacleMask", obstacle);

        foreach (PlayerInteractor interactor in root.GetComponentsInChildren<PlayerInteractor>(true))
            SetMask(interactor, "interactMask", interact);
    }

    private static bool SetMask(Component component, string property, int mask)
    {
        if (component == null)
            return false;

        SerializedObject serialized = new SerializedObject(component);
        SerializedProperty found = serialized.FindProperty(property);

        if (found == null || found.intValue == mask)
            return false;

        found.intValue = mask;
        serialized.ApplyModifiedProperties();
        return true;
    }

    // ---------- Ortak yardımcılar ----------

    /// <summary>
    /// Verilen katmanlardan maske kurar. Tanımsız katman uyarı yazıyor: eksik
    /// katman sessizce "hiçbir şey ışını kesmiyor"a dönüşür ve canavar duvarın
    /// arkasından vurmaya başlar — sebebi ekranda görünmeyen bir bozulma.
    /// </summary>
    public static int Mask(params string[] names)
    {
        int mask = 0;

        foreach (string name in names)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0)
            {
                Debug.LogWarning($"'{name}' katmanı tanımlı değil — " +
                    "Yakalamaca > Katmanları Kur çalıştırılmalı.");
                continue;
            }

            mask |= 1 << layer;
        }

        return mask;
    }

    /// <summary>
    /// Karışık içerikli bir kökü bileşenlerine bakarak katmanlara böler —
    /// sahne geçişiyle birebir aynı kural. Düğmesi de duvarı da olan bir kök
    /// için tek tek Apply çağırmaktan daha güvenli: yeni bir parça eklendiğinde
    /// kuralı burada bir kez güncellemek yetiyor.
    /// </summary>
    public static void AssignHierarchy(GameObject root)
    {
        if (root != null)
            Walk(root.transform, Harita);
    }

    /// <summary>
    /// Kurulum araçları için: üretilen nesneye ve altındakilere katman atar.
    /// Katman tanımlı değilse hiçbir şey yapmıyor — böylece "Katmanları Kur"
    /// hiç çalıştırılmamış bir projede diğer araçlar hata vermeden çalışıyor.
    /// </summary>
    public static void Apply(GameObject target, string layerName)
    {
        if (target == null)
            return;

        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
            return;

        foreach (Transform child in target.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }
}
