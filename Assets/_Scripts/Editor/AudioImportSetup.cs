using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Elle bırakılan ses dosyalarını yerine taşır, import ayarlarını yapar ve
/// sahnedeki kapılara bağlar.
///
/// İki ayar kritik:
///
/// **Force To Mono** — Unity'de stereo klipler 3B konumlandırılmıyor. Bu oyunda
/// ayak sesinin hangi yönden geldiği doğrudan oynanış; stereo bırakılırsa
/// canavarın nereden geldiğini duyamazsın. Elindeki dosyaların neredeyse hepsi
/// stereo geldi.
///
/// **Load Type** — kısa efektler bellekte açık dursun (anında çalsın), uzun
/// döngüler diskten akıtılsın (bellek şişmesin).
///
/// Menü: Yakalamaca > Sesleri Yerleştir
/// </summary>
public static class AudioImportSetup
{
    private const string AudioFolder = "Assets/_Audio";

    /// <summary>
    /// Kaynak dosya adının **başlangıcı** → hedef ad. Tam eşleşme değil önek
    /// arıyoruz, çünkü kırpma araçları dosya adının sonuna kendi etiketini
    /// ekliyor ("yürüme koşma_[cut_0sec].mp3").
    ///
    /// Sıra önemli: uzun önekler önce denenmeli, yoksa "zıplama" öneki
    /// "zıplama yere düşünce" dosyasını da yakalar.
    /// </summary>
    private static readonly (string Prefix, string Target)[] Renames =
    {
        ("zıplama yere düşünce",  "Inis"),
        ("canavarkoşma yürüme",   "Adim_Canavar"),
        ("yürüme koşma",          "Adim_Kacan"),
        ("kapısesi",              "Kapi"),
        ("ölmesesi",              "Olum"),
        ("zıplama",               "Ziplama")
    };

    /// <summary>Bu eşiğin üstündeki klipler döngü sayılıp diskten akıtılıyor.</summary>
    private const float StreamingThresholdSeconds = 5f;

    [MenuItem("Yakalamaca/Sesleri Yerleştir", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Sesleri Yerleştir")]
    private static void Run()
    {
        if (!AssetDatabase.IsValidFolder(AudioFolder))
            AssetDatabase.CreateFolder("Assets", "_Audio");

        List<string> moved = new List<string>();

        foreach ((string prefix, string targetName) in Renames)
        {
            string source = FindLooseFile(prefix);
            if (source == null)
                continue;

            string extension = System.IO.Path.GetExtension(source);
            string target = $"{AudioFolder}/{targetName}{extension}";

            if (AssetDatabase.LoadAssetAtPath<AudioClip>(target) != null)
                AssetDatabase.DeleteAsset(target);

            string error = AssetDatabase.MoveAsset(source, target);

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"{source} taşınamadı: {error}");
                continue;
            }

            moved.Add($"{System.IO.Path.GetFileName(source)} → {targetName}{extension}");
        }

        int configured = ConfigureAll();
        int doors = WireDoors();
        int terminals = WireTerminals();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string report = moved.Count > 0
            ? "Taşındı ve yeniden adlandırıldı:\n  " + string.Join("\n  ", moved)
            : "Taşınacak yeni ses bulunamadı (muhtemelen zaten yerindeler).";

        Debug.Log($"{report}\n\n" +
            $"{configured} klibin import ayarı yapıldı (mono + yükleme tipi).\n" +
            $"{doors} kapıya ses bağlandı (labirent + çıkış).\n" +
            $"{terminals} terminal/çıkış kilidine çalışma sesi bağlandı.\n\n" +
            "Oyuncu sesleri için Yakalamaca > Ağ Kurulumu (1. adım) çalıştır — " +
            "ayak sesi artık döngü olduğu için prefaba ayrı bir AudioSource gerekiyor.");
    }

    /// <summary>
    /// Adı verilen önekle başlayan, henüz düzgün adlandırılmamış klibi bulur.
    /// _Audio klasörünün içine de bakıyor: kırpılmış dosyalar oraya doğrudan
    /// bırakılmış olabiliyor, sadece adları bozuk oluyor.
    /// </summary>
    private static string FindLooseFile(string prefix)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains("/Mirror/") || path.Contains("/SciFi Warehouse Kit/"))
                continue;

            if (System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(prefix))
                return path;
        }

        return null;
    }

    /// <summary>
    /// _Audio altındaki her klibi 3B sese uygun hale getirir. Uzun olanlar
    /// döngü kabul edilip akıtılıyor, kısalar belleğe açılıyor.
    /// </summary>
    private static int ConfigureAll()
    {
        int count = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

            if (importer == null || clip == null)
                continue;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;

            settings.loadType = clip.length >= StreamingThresholdSeconds
                ? AudioClipLoadType.Streaming
                : AudioClipLoadType.DecompressOnLoad;

            bool changed = importer.forceToMono != true
                || importer.defaultSampleSettings.loadType != settings.loadType;

            if (!changed)
                continue;

            importer.forceToMono = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();

            count++;
        }

        return count;
    }

    /// <summary>
    /// Sahnedeki kapı panellerine AudioSource ekleyip klibi bağlar. Kapılar
    /// MazeMapBuilder tarafından üretiliyor ama haritayı yeniden kurmak
    /// giydirmeyi ve süsleri silerdi — o yüzden mevcut kapılar yerinde
    /// donatılıyor.
    /// </summary>
    /// <summary>
    /// Terminallere çalışma ve uyarı seslerini bağlar.
    ///
    /// **Neden burada, `Terminal ve Çıkış Kur`'da değil.** O araç var olan
    /// terminallere bilerek dokunmuyor (bölüm 0) — elle yerleştirilmiş beş
    /// terminale hiç ulaşamazdı. Bu araç ise zaten "sesleri bul ve bağla"
    /// işini yapıyor ve kurulum sırasında terminallerden SONRA çalışıyor
    /// (bölüm 7), yani yeni kurulumda da doğru sırada yakalıyor.
    ///
    /// Hiçbir şey silmiyor, taşımıyor: yalnızca iki klip alanına yazıyor.
    ///
    /// Hoparlörü `Terminal` kendi kuruyor (`GetOrCreateStateSource`), burada
    /// yalnızca klipler bağlanıyor — biri sahne nesnesi, öbürü varlık.
    /// </summary>
    private static int WireTerminals()
    {
        AudioClip working = AssetDatabase.LoadAssetAtPath<AudioClip>(
            $"{AudioFolder}/Terminal_Calisma.mp3");
        AudioClip warning = AssetDatabase.LoadAssetAtPath<AudioClip>(
            $"{AudioFolder}/Terminal_Uyari.mp3");

        if (working == null && warning == null)
            return 0;

        int count = 0;

        foreach (Terminal terminal in Object.FindObjectsOfType<Terminal>(true))
        {
            SerializedObject serialized = new SerializedObject(terminal);
            serialized.FindProperty("workingClip").objectReferenceValue = working;
            serialized.FindProperty("warningClip").objectReferenceValue = warning;
            serialized.ApplyModifiedProperties();
            count++;
        }

        // Çıkış kilidi de bir terminal gibi çalışıyor: başında duruyorsun,
        // makine çalışıyor. Aynı klip, aynı his — ayrı bir ses ikisini
        // birbirinden farklı iki mekanikmiş gibi gösterirdi.
        foreach (ExitLock exitLock in Object.FindObjectsOfType<ExitLock>(true))
        {
            SerializedObject serialized = new SerializedObject(exitLock);
            serialized.FindProperty("workingClip").objectReferenceValue = working;
            serialized.ApplyModifiedProperties();
            count++;
        }

        if (count > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }

        return count;
    }

    private static int WireDoors()
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/Kapi.mp3");
        if (clip == null)
            return 0;

        int count = 0;

        // Sahnedeki BÜTÜN sürgülü kapılar. Eskiden yalnızca `Harita/Kapilar`
        // altı taranıyordu ve **çıkış kapıları sessiz kalıyordu**: onlar
        // `HedefSistemi` altında duruyor. Bileşene göre aramak yeri sormaktan
        // daha sağlam — sonradan başka bir yere kapı konursa da yakalanıyor.
        foreach (SlidingDoor sliding in Object.FindObjectsOfType<SlidingDoor>(true))
        {
            AudioSource source = sliding.GetComponent<AudioSource>();
            if (source == null)
                source = Undo.AddComponent<AudioSource>(sliding.gameObject);

            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = 30f;

            SerializedObject serialized = new SerializedObject(sliding);
            serialized.FindProperty("audioSource").objectReferenceValue = source;
            serialized.FindProperty("moveClip").objectReferenceValue = clip;
            serialized.ApplyModifiedProperties();

            count++;
        }

        if (count > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }

        return count;
    }
}
