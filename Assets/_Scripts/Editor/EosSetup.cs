using EpicTransport;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// EOS (Epic Online Services) relay'ini sahneye kurar ve lobiye bağlar.
///
/// ### Neden `Ağ Kurulumu`'ndan ayrı bir menü
///
/// O araç oyuncu prefabını **sıfırdan** kuruyor, yani bir kez çalıştırmak
/// canavar/kaçan modellerini, menüyü, sesleri ve yankıyı da yeniden kurmayı
/// gerektiriyor (bölüm 7'deki sıra). Var olan bir projeye sonradan EOS eklemek
/// için o zinciri çalıştırmak, oturmuş her şeyi riske atmak demek.
///
/// Bu menü **hiçbir şey silmiyor**: bileşenleri ekliyor ve referansları
/// yazıyor, o kadar.
///
/// ### Aynı objede durabiliyor
///
/// `EosTransport` doğrudan `Transport`'tan türüyor, `KcpTransport`'tan değil.
/// Bu önemli: `KcpTransport` sınıfında `[DisallowMultipleComponent]` var ve
/// ondan türeyen bir transport aynı objeye eklenemiyordu — Edgegap denemesinde
/// tam bu duvara çarpılmıştı ve `AddComponent` hiçbir hata yazmadan null
/// döndürüyordu. EOS'ta o sorun yok, alt obje numarasına gerek kalmıyor.
///
/// ### Kimlik bilgileri koda yazılmıyor
///
/// `EosApiKey` bir ScriptableObject ve değerleri oyuncu Epic portalından
/// kendisi alıyor. Araç yalnızca varlığı bulup bağlıyor; anahtarı hiçbir yere
/// kopyalamıyor.
/// </summary>
public static class EosSetup
{
    private const string ManagerName = "NetworkManager";

    [MenuItem("Yakalamaca/EOS Kurulumu (relay)", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/EOS Kurulumu (relay)")]
    private static void Run()
    {
        GameObject managerObject = GameObject.Find(ManagerName);
        if (managerObject == null)
        {
            Debug.LogError("NetworkManager yok. Önce Yakalamaca > Ağ Kurulumu (1. adım).");
            return;
        }

        EosApiKey key = FindApiKey();
        if (key == null)
        {
            Debug.LogError(
                "EosApiKey varlığı bulunamadı. Project penceresinde sağ tık > " +
                "Create > EOS > API Key ile oluştur ve Epic portalından aldığın " +
                "değerleri gir.");

            return;
        }

        EOSSDKComponent sdk = managerObject.GetComponent<EOSSDKComponent>()
            ?? Undo.AddComponent<EOSSDKComponent>(managerObject);

        SerializedObject serializedSdk = new SerializedObject(sdk);
        serializedSdk.FindProperty("apiKeys").objectReferenceValue = key;

        // Oyuncudan Epic hesabı İSTENMİYOR. `authInterfaceLogin` açılsaydı
        // herkesin Epic hesabıyla giriş yapması gerekirdi; Device ID kimliği
        // sessizce üretiyor ve oyuncu hiçbir şey fark etmiyor.
        serializedSdk.FindProperty("authInterfaceLogin").boolValue = false;
        serializedSdk.ApplyModifiedProperties();

        EosTransport relay = managerObject.GetComponent<EosTransport>()
            ?? Undo.AddComponent<EosTransport>(managerObject);

        // Kısa oda kodunu üreten lobi servisi. Transport'la aynı objede
        // duruyor: ikisi de aynı `EOSSDKComponent`'e bakıyor ve ayrı bir obje
        // yalnızca sahnede gezinecek bir isim daha olurdu.
        RelayLobby lobbyService = managerObject.GetComponent<RelayLobby>()
            ?? Undo.AddComponent<RelayLobby>(managerObject);

        // Paketin kendi öznitelik listesi. Anahtarları koda yazıyoruz, yani bu
        // alan çalışmayı etkilemiyor — ama varsayılanı "lobby_name" ve
        // Inspector'da onu görmek bileşeni okuyanı yanıltırdı.
        SerializedObject serializedLobby = new SerializedObject(lobbyService);
        SerializedProperty keys = serializedLobby.FindProperty("AttributeKeys");
        keys.arraySize = 2;
        keys.GetArrayElementAtIndex(0).stringValue = RelayLobby.CodeKey;
        keys.GetArrayElementAtIndex(1).stringValue = RelayLobby.NameKey;
        serializedLobby.ApplyModifiedProperties();

        Transport local = managerObject.GetComponent<kcp2k.KcpTransport>();

        WireLobby(relay, local, lobbyService);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Selection.activeGameObject = managerObject;

        Debug.Log(
            "EOS kuruldu ve lobiye bağlandı. Sahne kaydedildi.\n\n" +
            "Denemek için Play'e bas ve Console'a bak:\n" +
            "· 'EOS SDK' ile ilgili bir hata YOKSA kimlik bilgileri doğru.\n" +
            "· LOBİ KUR deyince ekrandaki kodun UZUNLUĞU nerede olduğunu " +
            "söylüyor:\n" +
            "    6 harf  → her şey çalışıyor (relay + lobi servisi).\n" +
            "    32 harf → relay çalışıyor ama lobi servisi cevap vermedi; " +
            "oda yine oynanabilir, kod uzun.\n" +
            "    7 harf  → EOS hiç açılmadı, oyun yerel odaya düştü.\n\n" +
            "EOS açılmazsa ilk bakılacak yer Epic portalındaki istemci " +
            "politikası: P2P izni yoksa SDK başlamıyor.");
    }

    /// <summary>
    /// Projedeki `EosApiKey` varlığını bulur.
    ///
    /// Yol sabitlenmiyor, tür aranıyor: varlık `_ScriptableObjects` altında da
    /// olabilir başka yerde de, ve oyuncu onu kendi oluşturuyor. Birden fazla
    /// varsa ilki alınıyor ve konsola yazılıyor — sessizce yanlışını seçmek
    /// en kötüsü olurdu.
    /// </summary>
    private static EosApiKey FindApiKey()
    {
        string[] guids = AssetDatabase.FindAssets("t:EosApiKey");

        if (guids.Length == 0)
            return null;

        if (guids.Length > 1)
        {
            Debug.LogWarning(
                $"{guids.Length} adet EosApiKey bulundu; ilki kullanılıyor. " +
                "Fazlalıkları silmek karışıklığı önler.");
        }

        return AssetDatabase.LoadAssetAtPath<EosApiKey>(
            AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    /// <summary>
    /// Menüdeki `LobbyNetwork`'e iki transport'u da bağlar.
    ///
    /// Menü `Menü Kur` ile ayrı kuruluyor; bu araç ondan önce çalıştırılırsa
    /// lobi henüz yok. Bulunamaması hata değil — sonra tekrar çalıştırılır.
    /// </summary>
    private static void WireLobby(EosTransport relay, Transport local, RelayLobby lobbyService)
    {
        LobbyNetwork lobby = Object.FindObjectOfType<LobbyNetwork>(true);

        if (lobby == null)
        {
            Debug.LogWarning(
                "LobbyNetwork bulunamadı (Menü Kur çalıştırılmamış olabilir). " +
                "Menüyü kurduktan sonra bu aracı tekrar çalıştır.");

            return;
        }

        SerializedObject serialized = new SerializedObject(lobby);
        serialized.FindProperty("relayTransport").objectReferenceValue = relay;
        serialized.FindProperty("localTransport").objectReferenceValue = local;
        serialized.FindProperty("relayLobby").objectReferenceValue = lobbyService;
        serialized.ApplyModifiedProperties();
    }
}
