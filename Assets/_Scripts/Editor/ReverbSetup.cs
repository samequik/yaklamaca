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

    private const string FloorName = "Zemin";

    /// <summary>
    /// Haritanın hiç bulunamadığı durumda kullanılan yarıçap. Ana haritanın
    /// ölçüsü — kanat hesaba katılmıyor, çünkü bu yola ancak sahne bozuksa
    /// düşülüyor.
    /// </summary>
    private const float FallbackSpan = 54.4f;

    /// <summary>
    /// Bölge yarıçapına eklenen pay (m). İki işi var: zemin sınırları
    /// DİKEYDE tavanı (3 m) kapsamıyor, ve haritanın kenarındaki bir oyuncu
    /// tam sınırda kalmamalı.
    /// </summary>
    private const float ZoneMargin = 3f;

    /// <summary>
    /// `minDistance / maxDistance` oranı. Yankı `minDistance`'a kadar TAM
    /// güçte, oradan `maxDistance`'a doğru sönüyor — yani haritanın tamamı
    /// `minDistance`'ın içinde kalmalı ve sönme bölgesi dışarıda kalan pay
    /// olmalı. Oran eski sürümden aynen korundu.
    /// </summary>
    private const float MinMaxRatio = 0.75f;

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
            $"Mağara yankısı kuruldu. Bölge merkezi {zone.transform.position} — " +
            $"yankı {zone.minDistance:0.#} m'ye kadar TAM güçte, {zone.maxDistance:0.#} m'de " +
            "susuyor. Merkez ve yarıçap haritanın zeminlerinden ölçülüyor, yani " +
            "güney kanadı da kapsam içinde.\n" +
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

        AudioReverbZone zone = zoneObject.GetComponent<AudioReverbZone>();
        if (zone == null)
            zone = Undo.AddComponent<AudioReverbZone>(zoneObject);

        Undo.RecordObject(zone, "Mağara Yankısı");
        Undo.RecordObject(zoneObject.transform, "Mağara Yankısı");

        // Bölge KÜRE: merkezi ve yarıçapı haritanın gerçek sınırlarından
        // çıkıyor. Dinleyici labirentin her yerinde `minDistance`'ın içinde
        // olmalı — dışında kalan yerde yankı tamamen susuyor.
        if (TryFindMapBounds(out Bounds bounds))
        {
            zoneObject.transform.position = bounds.center;

            // `extents.magnitude` = merkezden köşeye uzaklık, yani sınır
            // kutusunun tamamını kapsayan en küçük küre.
            float radius = bounds.extents.magnitude + ZoneMargin;
            zone.minDistance = radius;
            zone.maxDistance = radius / MinMaxRatio;
        }
        else
        {
            Debug.LogWarning(
                $"Mağara Yankısı: '{MapName}' altında '{FloorName}' bulunamadı, " +
                $"yedek yarıçap ({FallbackSpan} m) kullanılıyor. Güney kanadı " +
                "kapsam dışı kalabilir.");

            zoneObject.transform.position = Vector3.zero;
            zone.minDistance = FallbackSpan * MinMaxRatio;
            zone.maxDistance = FallbackSpan;
        }

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

    /// <summary>
    /// Haritanın gerçek sınırları: kökün ALTINDAKİ bütün `Zemin` objelerinin
    /// dünya sınırlarının birleşimi.
    ///
    /// ### Neden böyle: bölge 2026-09-13'ten beri kanadı kapsamıyordu
    ///
    /// Eski sürüm tek satırdı — `map.transform.Find("Zemin").localScale.x`,
    /// yani YALNIZCA ana haritanın zemini (54.4 m) — ve bölgeyi dünya
    /// orijinine koyuyordu. Güney kanadı o tarihte eklendi ve bu araç onu hiç
    /// görmedi. Ölçüldü: kanadın güney köşeleri orijine **71.9 m**, yani
    /// `maxDistance`'ın (54.4) dışında — oyuncu oraya yürüyünce yankı
    /// TAMAMEN kesiliyordu ("güney kısmında sesler normal geliyor").
    ///
    /// Artık `GetComponentsInChildren` ile bütün bölgelerin zeminleri
    /// toplanıyor: ana harita, güney kanadı ve iki çıkış sahanlığı. Harita
    /// büyürse aracı yeniden çalıştırmak yetiyor — koda hiçbir koordinat
    /// yazılmadı (`SpawnPointSetup` ve `MenuSetup.BuildMinimap` ile aynı
    /// alışkanlık).
    ///
    /// **Ölçü `Renderer.bounds`'tan, `localScale`'den DEĞİL.** İki sebep:
    /// `bounds` dünya uzayında (kanat `Harita`'nın ÇOCUĞU, yani yerel ölçek
    /// tek başına yanlış cevap verir) ve renderer şartı `Giydirme/Zemin` gibi
    /// aynı adı taşıyan boş GRUP objelerini kendiliğinden eliyor.
    /// </summary>
    private static bool TryFindMapBounds(out Bounds bounds)
    {
        bounds = new Bounds();

        GameObject map = FindMapRoot();
        if (map == null)
            return false;

        bool any = false;

        foreach (Transform child in map.GetComponentsInChildren<Transform>(true))
        {
            if (child.name != FloorName)
                continue;

            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer == null)
                continue;

            if (any)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                any = true;
            }
        }

        return any;
    }

    /// <summary>
    /// Harita kökünü SAHNE KÖKLERİ arasında arar, `GameObject.Find` ile
    /// değil.
    ///
    /// Sebebi bölüm 0.1'in `Duvar_3_0` dersi: sahnede `Harita` adında İKİ
    /// obje olabiliyor — gerçek harita ve mini haritanın karo kabı
    /// (`MenuSetup.BuildMinimap`). `GameObject.Find` hangisini döndüreceğini
    /// garanti etmiyor ve UI kabını yakalasaydı bu araç sessizce yedek
    /// yarıçapa düşerdi. Kap 2026-09-28'de `HaritaKabi` diye yeniden
    /// adlandırıldı, ama kök taraması bu aracı çakışmadan tamamen bağımsız
    /// kılıyor.
    ///
    /// `GetRootGameObjects` kapalı kökleri de veriyor (bölüm 13'ün
    /// "`GameObject.Find` kapalı objeyi bulmuyor" dersi).
    /// </summary>
    private static GameObject FindMapRoot()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == MapName)
                return root;
        }

        return null;
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
