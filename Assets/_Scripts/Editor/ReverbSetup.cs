using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Haritaya mağara yankısı kurar: bir `AudioReverbZone` ve tüm 3B ses
/// kaynaklarına mesafeye bağlı yankı eğrisi.
///
/// **Yankı burada da süs değil, ama dozu oynanışa bağlı.** CLAUDE.md bölüm
/// 12'nin kuralı: "sesin yönü doğrudan oynanış" — kaçan, canavarın nerede
/// olduğunu ayak sesinden anlıyor. Unity'de yankı sinyali yönsüzdür (dry sinyal
/// panlanır, wet sinyal panlanmaz), yani yankıyı sonuna kadar açmak canavarın
/// yönünü silmek demek. Bu yüzden iki tedbir var:
///
/// **1. Yankı mesafeyle açılıyor.** Yakındaki ses kuru kalıyor (yön okunuyor),
/// uzaktaki ses ıslanıyor (mekân büyük hissediliyor). Tam da ihtiyacın olduğu
/// yerde keskinlik: "arkamda — hangi tarafta?" sorusu her zaman yakın mesafede
/// soruluyor.
///
/// **2. Yankının tizi kısılıyor** (`roomHF` ve `decayHFRatio`). Kulak yönü
/// büyük ölçüde yüksek frekanslardan çıkarıyor; kuyruğu pesleştirince yankı
/// hâlâ derin bir mağara gibi duyuluyor ama yön ipuçlarının önüne geçmiyor.
/// Unity'nin hazır `Cave` ayarı bunu yapmıyor (tizi hiç kısmıyor), o yüzden
/// hazır ayar yerine özel değerler yazılıyor.
///
/// **2B sesler yankılanmıyor.** Kalp atışı, arayüz sesi gibi kafanın içinde
/// çalan şeyler mekânda değil; onlara yankı vermek kafa karıştırıcı olurdu.
/// Araç `spatialBlend == 0` olan kaynakların yankısını sıfırlıyor.
///
/// Sesli sohbet eklendiğinde ekstra iş çıkmıyor: konuşma oyuncunun üstündeki
/// 3B bir AudioSource'tan çalarsa bu yankıya kendiliğinden girer.
///
/// Menü: Yakalamaca > Mağara Yankısı Kur (reverb)
/// </summary>
public static class ReverbSetup
{
    private const string ZoneName = "MagaraYankisi";
    private const string MapName = "Harita";
    private const string PlayerPrefabPath = "Assets/_Prefabs/NetworkPlayer.prefab";

    private const float FallbackSpan = 54.4f;

    /// <summary>
    /// Kaynağın dibindeki yankı oranı. Sıfır değil — kendi ayak sesin de
    /// mağarada yankılanır — ama tam da değil: yakındaki sesin yönü okunabilir
    /// kalmalı.
    /// </summary>
    private const float NearReverbMix = 0.5f;

    /// <summary>Duyulabilir menzilin ucundaki yankı oranı.</summary>
    private const float FarReverbMix = 1f;

    [MenuItem("Yakalamaca/Mağara Yankısı Kur (reverb)", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Mağara Yankısı Kur (reverb)")]
    private static void Run()
    {
        AudioReverbZone zone = BuildZone();
        int sceneSources = ApplyCurveToScene();
        int prefabSources = ApplyCurveToPlayerPrefab();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Selection.activeGameObject = zone.gameObject;

        Debug.Log(
            $"Mağara yankısı kuruldu. Bölge yarıçapı {zone.maxDistance:0.#} m — " +
            "labirentin tamamını kaplıyor.\n" +
            $"{sceneSources} sahne kaynağı ve {prefabSources} oyuncu prefabı kaynağı " +
            $"mesafe eğrisine bağlandı ({NearReverbMix} → {FarReverbMix}).\n\n" +
            "Ayarlamak için: sahnedeki '" + ZoneName + "' objesini seç. " +
            "Inspector'daki değerler Play modunda canlı çalışıyor — oyunu " +
            "başlatıp koşarken `Decay Time`'ı oynatarak beğendiğin yeri bul.\n" +
            "Hazır bir şey denemek istersen `Reverb Preset`'i Cave / StoneCorridor / " +
            "Hangar yapabilirsin; User'a geri alırsan buradaki özel değerler döner.\n\n" +
            "NOT: Ağ Kurulumu'nu tekrar çalıştırırsan oyuncu prefabı sıfırdan " +
            "kurulur ve eğri gider — o zaman bu menüyü de tekrar çalıştır.");
    }

    // ---------- Bölge ----------

    /// <summary>
    /// Yankı bölgesini kurar. Haritanın çocuğu DEĞİL, sahnenin kökünde:
    /// `Labirent Harita Kur` haritayı komple silip yeniden ürettiği için,
    /// çocuğu olsaydı her harita düzenlemesinde kaybolurdu.
    /// </summary>
    private static AudioReverbZone BuildZone()
    {
        GameObject zoneObject = GameObject.Find(ZoneName);
        if (zoneObject == null)
        {
            zoneObject = new GameObject(ZoneName);
            Undo.RegisterCreatedObjectUndo(zoneObject, "Mağara Yankısı");
        }

        zoneObject.transform.position = Vector3.zero;

        AudioReverbZone zone = zoneObject.GetComponent<AudioReverbZone>();
        if (zone == null)
            zone = Undo.AddComponent<AudioReverbZone>(zoneObject);

        Undo.RecordObject(zone, "Mağara Yankısı");

        float span = FindMapSpan();

        // Dinleyici labirentin her yerinde bölgenin tam içinde olmalı: minDistance
        // "yankının tam güçte olduğu" yarıçap. Haritanın köşesi merkeze span/2 · √2
        // uzaklıkta, ona da pay bırakıyoruz.
        zone.minDistance = span * 0.75f;
        zone.maxDistance = span;

        // Özel değerleri yazabilmek için önce User'a geçmek şart; başka bir
        // hazır ayar seçiliyken Unity aşağıdaki atamaları görmezden gelir.
        zone.reverbPreset = AudioReverbPreset.User;

        // Odanın genel yankı seviyesi (mB). Cave hazır ayarıyla aynı.
        zone.room = -1000;

        // Yankının tizi kısılıyor. Asıl tasarım kararı bu: yön ipuçları büyük
        // ölçüde yüksek frekanslardan çıkıyor, kuyruğu pesleştirince mağara
        // hissi kalıyor ama canavarın yönü silinmiyor.
        zone.roomHF = -1500;
        zone.roomLF = 0;

        // Cave 2.91 sn. Koridorlar 3.2 m — dar bir taş koridor o kadar uzun
        // çınlamaz, ayrıca uzun kuyruk arka arkaya gelen adım seslerini birbirine
        // karıştırıp tempoyu okunmaz hâle getiriyor.
        zone.decayTime = 2.2f;

        // 1'in altı: tiz, pesten hızlı sönüyor. Kuyruk ilerledikçe pes bir
        // uğultuya dönüşüyor.
        zone.decayHFRatio = 0.7f;

        // Erken yansımalar — duvarın yakınlığını veren kısım.
        zone.reflections = -600;
        zone.reflectionsDelay = 0.02f;

        zone.reverb = -400;
        zone.reverbDelay = 0.03f;

        zone.HFReference = 5000f;
        zone.LFReference = 250f;

        // Dar ve düzensiz taş koridor: yankı yoğun ve dağınık.
        zone.diffusion = 100f;
        zone.density = 100f;

        EditorUtility.SetDirty(zone);
        return zone;
    }

    private static float FindMapSpan()
    {
        GameObject map = GameObject.Find(MapName);
        if (map == null)
            return FallbackSpan;

        Transform floor = map.transform.Find("Zemin");
        return floor != null ? floor.localScale.x : FallbackSpan;
    }

    // ---------- Eğri ----------

    private static int ApplyCurveToScene()
    {
        int count = 0;

        foreach (AudioSource source in Object.FindObjectsOfType<AudioSource>())
        {
            Undo.RecordObject(source, "Mağara Yankısı");
            if (ApplyCurve(source))
                count++;

            EditorUtility.SetDirty(source);
        }

        return count;
    }

    /// <summary>
    /// Oyuncu prefabındaki kaynaklara da uygular. Oyuncu sahnede değil,
    /// çalışma anında prefabtan doğuyor — sahneyi taramak yetmiyor.
    /// </summary>
    private static int ApplyCurveToPlayerPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
        {
            Debug.LogWarning($"{PlayerPrefabPath} yok — önce Yakalamaca > Ağ Kurulumu " +
                "(1. adım) çalıştır. Sahne kaynakları yine de ayarlandı.");
            return 0;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        int count = 0;

        try
        {
            foreach (AudioSource source in contents.GetComponentsInChildren<AudioSource>(true))
            {
                if (ApplyCurve(source))
                    count++;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        return count;
    }

    /// <summary>
    /// Tek kaynağa kuralı uygular. 3B ise mesafeyle açılan eğri, 2B ise yankı
    /// tamamen kapalı.
    /// </summary>
    /// <returns>Kaynak 3B olduğu için eğri aldıysa true.</returns>
    private static bool ApplyCurve(AudioSource source)
    {
        if (source.spatialBlend <= 0f)
        {
            // Kafanın içinde çalan ses (kalp atışı, arayüz). Mekânda olmadığı
            // için yankısı da olmamalı.
            source.reverbZoneMix = 0f;
            return false;
        }

        // Eğrinin x ekseni normalize mesafe: 0 = kaynağın dibi, 1 = maxDistance.
        source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, new AnimationCurve(
            new Keyframe(0f, NearReverbMix),
            new Keyframe(1f, FarReverbMix)));

        return true;
    }
}
