using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sahneye tek bir sahte kaçan koyar. Amacı oynanış değil test:
///
/// - Tek başına oynarken turun başlaması için ikinci katılımcı gerekiyor
///   (minimumPlayers 2).
/// - Elendikten sonra izleyici modunun çalıştığını görmek için izlenecek
///   birinin hayatta olması gerekiyor.
///
/// Bot sahne nesnesi olarak duruyor; Mirror sunucu açılınca sahnedeki
/// NetworkIdentity'leri kendiliğinden spawn ediyor, ayrıca kayıt gerekmiyor.
/// Hareketini sunucu sürdüğü için NetworkTransform yönü ServerToClient —
/// oyuncu prefabının tam tersi, orada hareketi sahibi bildiriyor.
///
/// Menü: Yakalamaca > Test Botu Ekle (kaçan) / Test Botu Kaldır
/// </summary>
public static class TestBotSetup
{
    private const string BotName = "TestBot";
    private const float AreaHalfSize = 24f;

    [MenuItem("Yakalamaca/Test Botu Ekle (kaçan)", true)]
    private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Test Botu Ekle (kaçan)")]
    private static void Build()
    {
        GameObject existing = GameObject.Find(BotName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        GameObject bot = new GameObject(BotName);
        Undo.RegisterCreatedObjectUndo(bot, "Test Botu");

        // Ölçüler oyuncunun hull'uyla aynı: bıçağın isabet kontrolü ve izleyici
        // kamerasının takip mesafesi bu boya göre ayarlı.
        CharacterController controller = bot.AddComponent<CharacterController>();
        controller.height = 72f * PlayerController.UnitsToMeters;
        controller.radius = 16f * PlayerController.UnitsToMeters;
        controller.center = Vector3.zero;
        controller.slopeLimit = 45f;
        controller.stepOffset = 18f * PlayerController.UnitsToMeters;
        controller.skinWidth = controller.radius * 0.1f;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Govde";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(bot.transform, false);
        body.transform.localScale = new Vector3(
            controller.radius * 2f, controller.height / 2f, controller.radius * 2f);

        // Harita bilerek zifiri (ortam ışığı ~0, yoğun sis). Düz bir kapsülü
        // koridorda bulmak şansa kalıyordu; bot bir test aracı olduğu için
        // üstüne işaret ışığı koyuyoruz. Renk soğuk mavi: ortam lambaları soluk
        // sarı, uzaktan karışmasın.
        GameObject markerObject = new GameObject("IsaretIsigi");
        markerObject.transform.SetParent(bot.transform, false);
        markerObject.transform.localPosition = Vector3.up * 0.9f;

        Light marker = markerObject.AddComponent<Light>();
        marker.type = LightType.Point;
        marker.range = 7f;
        marker.intensity = 1.8f;
        marker.color = new Color(0.4f, 0.9f, 1f);
        marker.shadows = LightShadows.None;

        bot.transform.position = FindClearSpot();

        bot.AddComponent<NetworkIdentity>();

        NetworkTransformReliable netTransform = bot.AddComponent<NetworkTransformReliable>();

        // Mirror'ın Reset()'i her NetworkTransform'u ClientToServer'a çekiyor
        // ("works immediately for users"). Bot için yanlış: bağlantısı yok,
        // hareketi sunucu sürüyor. Açıkça geri çeviriyoruz.
        netTransform.syncDirection = SyncDirection.ServerToClient;
        netTransform.syncInterval = 0.05f;

        RoundParticipant participant = bot.AddComponent<RoundParticipant>();
        bot.AddComponent<TestRunnerBot>();

        SerializedObject serialized = new SerializedObject(participant);
        serialized.FindProperty("isBot").boolValue = true;
        serialized.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
        serialized.ApplyModifiedProperties();

        // Kaçan modeli (Banana Man) — CLAUDE.md bölüm 17'deki "botta model
        // yok, kapsül yer tutucu" sınırının kapatılması: hareket testleri artık
        // gerçek bir animasyonlu karakterde izlenebiliyor. `RunnerSetup`'ın
        // `Kaçan Modelini Kur`'la önceden ürettiği Animator Controller'ı
        // olduğu gibi kullanıyor, yeniden kurmuyor.
        string modelReport = RunnerSetup.AttachToSceneObject(bot);

        // Model bağlandıysa kapsülü PlayerBodyVisual'a fallback olarak veriyoruz
        // — runnerRoot doluyken zaten gösterilmiyor, ama model bir gün
        // kaldırılırsa (ya da bu araç Kaçan Modelini Kur'dan önce çalıştırılırsa)
        // kimse görünmez olmasın diye.
        PlayerBodyVisual visual = bot.GetComponent<PlayerBodyVisual>();
        if (visual != null)
        {
            SerializedObject serializedVisual = new SerializedObject(visual);
            serializedVisual.FindProperty("capsuleRenderer").objectReferenceValue = body.GetComponent<Renderer>();
            serializedVisual.ApplyModifiedProperties();
        }

        Selection.activeGameObject = bot;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            "Test botu eklendi ve sahne kaydedildi.\n" +
            $"Model: {modelReport}\n\n" +
            "TEK BAŞINA İZLEYİCİ TESTİ (host penceresinde):\n" +
            "  [2] Turu KAÇAN olarak başlat — canavar botlardan seçilmediği için, " +
            "[1] ile başlatsaydın canavar sen olurdun ve elenemezdin.\n" +
            "  [3] Kendini elendir → kamera kendiliğinden bota geçer.\n" +
            "  Sol tık ile hayattaki kaçanlar arasında gezinirsin. Canavar listede yok, bilerek.\n\n" +
            "BIÇAK TESTİ: [1] ile turu başlat (canavar sen olursun), sonra [4] ile botu " +
            "önüne ışınla. Botu labirentte aramana gerek yok; mavi işaret ışığından da tanırsın.\n\n" +
            "NOT: Bot yalnızca YÜRÜME/KOŞMA/ZIPLAMA animasyonlarını gösteriyor — " +
            "ölüm/yakalanma koreografisi hâlâ yok (bilinen sınır, CLAUDE.md bölüm 17), " +
            "TestRunnerBot AI'ı zaten kendini elendirmiyor.\n\n" +
            "Botu kaldırmak: Yakalamaca > Test Botu Kaldır (ya da objeyi sil).");
    }

    [MenuItem("Yakalamaca/Test Botu Kaldır", true)]
    private static bool CanRemove()
        => !EditorApplication.isPlayingOrWillChangePlaymode && GameObject.Find(BotName) != null;

    [MenuItem("Yakalamaca/Test Botu Kaldır")]
    private static void Remove()
    {
        GameObject existing = GameObject.Find(BotName);
        if (existing == null)
            return;

        Undo.DestroyObjectImmediate(existing);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("Test botu kaldırıldı ve sahne kaydedildi.");
    }

    /// <summary>
    /// Duvarın içinde doğmasın diye fizikle sınanmış boş bir hücre arıyor —
    /// doğum noktalarıyla aynı yöntem.
    /// </summary>
    private static Vector3 FindClearSpot()
    {
        for (int attempt = 0; attempt < 400; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(-AreaHalfSize, AreaHalfSize),
                0.7f,
                Random.Range(-AreaHalfSize, AreaHalfSize));

            // Kontrol küresi biraz yukarıdan: doğum noktasının kendi hizasında
            // sorarsak kürenin altı zemine değiyor ve her nokta dolu çıkıyor.
            if (!Physics.CheckSphere(candidate + Vector3.up * 0.25f, 0.55f, ~0,
                    QueryTriggerInteraction.Ignore))
                return candidate;
        }

        Debug.LogWarning("Bot için boş nokta bulunamadı, merkeze konuldu. Harita kurulu mu?");
        return new Vector3(0f, 0.7f, 0f);
    }
}
