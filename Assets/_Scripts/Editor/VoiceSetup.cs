using UnityEditor;
using UnityEngine;

/// <summary>
/// Sesli sohbeti var olan oyuncu prefabına ekler.
///
/// ### Neden `Ağ Kurulumu`'ndan ayrı bir menü
///
/// O araç oyuncu prefabını **sıfırdan** kuruyor, yani bir kez çalıştırmak
/// canavar/kaçan modellerini, menüyü, sesleri, yankıyı ve katmanları da
/// yeniden kurmayı gerektiriyor (bölüm 7'deki sıra). Oturmuş bir projeye
/// yalnızca üç bileşen eklemek için o zinciri çalıştırmak, her şeyi riske
/// atmak demek. `EOS Kurulumu` da tam bu gerekçeyle ayrı duruyor.
///
/// **Bu menü hiçbir şey silmiyor**: eksik bileşenleri ekliyor ve referansları
/// yazıyor, o kadar. İki kez çalıştırmak zararsız.
///
/// `Ağ Kurulumu` da aynı bileşenleri kuruyor, yani sıfırdan kurulumda bu
/// menüye gerek yok — ikisi aynı sonucu veriyor.
/// </summary>
public static class VoiceSetup
{
    private const string PrefabPath = "Assets/_Prefabs/NetworkPlayer.prefab";

    [MenuItem("Yakalamaca/Sesli Sohbet Kur", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Sesli Sohbet Kur")]
    private static void Run()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"{PrefabPath} yok. Önce Yakalamaca > Ağ Kurulumu (1. adım).");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        try
        {
            AudioSource source = FindOrCreateVoiceSource(root);

            VoicePlayback playback = root.GetComponent<VoicePlayback>()
                ?? root.AddComponent<VoicePlayback>();

            VoiceCapture capture = root.GetComponent<VoiceCapture>()
                ?? root.AddComponent<VoiceCapture>();

            VoiceChat chat = root.GetComponent<VoiceChat>()
                ?? root.AddComponent<VoiceChat>();

            SerializedObject serializedPlayback = new SerializedObject(playback);
            serializedPlayback.FindProperty("source").objectReferenceValue = source;
            serializedPlayback.ApplyModifiedProperties();

            SerializedObject serializedChat = new SerializedObject(chat);
            serializedChat.FindProperty("playback").objectReferenceValue = playback;
            serializedChat.FindProperty("capture").objectReferenceValue = capture;
            serializedChat.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();

        Debug.Log(
            "Sesli sohbet kuruldu.\n\n" +
            "Varsayılan: BAS-KONUŞ, V tuşu. Ayarlar ekranından değiştirilebilir.\n\n" +
            "Duyma kuralları (VoiceChat):\n" +
            "· Lobide herkes herkesi duyuyor, mesafesiz.\n" +
            "· Turda sahadakiler birbirini 18 m'ye kadar duyuyor.\n" +
            "· Elenenler yalnızca birbirini duyuyor; yaşayanlar onları DUYMUYOR.\n\n" +
            "Tek makinede kendi sesini duyamazsın (kendine göndermiyoruz) — " +
            "denemek iki makine gerektiriyor, EOS'taki gibi.");
    }

    /// <summary>
    /// Konuşmanın kendi 3B kaynağını bulur ya da kurar.
    ///
    /// Prefabta birden çok `AudioSource` var (adım, konuşma, saldırı) ve
    /// hangisinin hangisi olduğunu tür bilgisi söylemiyor. Ölçüt
    /// `VoicePlayback`'in halihazırda bağlı kaynağı; yoksa **yeni bir tane**
    /// ekleniyor. Var olanlardan birini seçmeye çalışmak, adım sesinin
    /// kaynağını çalma riski taşırdı.
    /// </summary>
    private static AudioSource FindOrCreateVoiceSource(GameObject root)
    {
        VoicePlayback existing = root.GetComponent<VoicePlayback>();

        if (existing != null)
        {
            SerializedObject serialized = new SerializedObject(existing);
            Object wired = serialized.FindProperty("source").objectReferenceValue;

            if (wired is AudioSource wiredSource)
                return wiredSource;
        }

        AudioSource source = root.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 3f;

        // Menzil ayak sesinden (28 m) kısa: konuşmanın koridor boyu taşınması
        // fısıltıyla plan yapmayı anlamsız kılardı. Sunucu da aynı mesafede
        // süzüyor; buradaki yalnızca sönümlenme eğrisi.
        source.maxDistance = 18f;

        return source;
    }
}
