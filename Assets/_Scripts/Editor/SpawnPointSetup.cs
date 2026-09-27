using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Doğum noktalarını İKİ haritaya dağıtır: kaçanlar ana haritaya, canavar
/// güney kanadına.
///
/// Menü: `Yakalamaca > Doğum Noktalarını Kur (iki harita)`
///
/// ### Neden yazıldı — kaçanlar İÇ İÇE doğuyordu (2026-09-23)
///
/// Eski düzende sahnede altı nokta vardı, altısı da ana haritadaydı ve
/// `RoundManager` bütün kaçanları **tek** noktaya gönderip etrafında 1.6 m
/// yarıçaplı bir halkaya diziyordu. Halka yarıçapı koridorun yarı
/// genişliğinin (3.2 / 2) TAM KENDİSİ: koridora dik duran yuvalar her
/// seferinde duvarın yüzüne biniyor, kontrol küresi onları reddediyor ve
/// yedek yol hepsini çapanın üstüne yığıyordu.
///
/// Sahnedeki gerçek duvar verisiyle simüle edildi — altı çapanın ALTISINDA
/// da 2-3 oyuncu üst üste doğuyordu:
///
/// ```
/// Dogum_1  X  ok  X  ok   -> 3 kisi capada
/// Dogum_2  ok X  ok  X    -> 3 kisi capada
/// ...
/// ```
///
/// Çözüm halkayı büyütmek DEĞİL — koridor dar olduğu sürece her yarıçap bir
/// duvara denk gelir. Doğrusu herkese KENDİ noktasını vermek.
///
/// ### Noktalar sahneden ÖLÇÜLÜYOR, koda yazılmıyor
///
/// Bölgelerin sınırları `Harita/Zemin` ve
/// `Harita/Harita_Genisleme_Guney/Zemin`'in gerçek dünya sınırlarından
/// okunuyor. Harita ileride büyürse aracı yeniden çalıştırmak yetiyor;
/// burada elle güncellenecek bir koordinat yok. Aynı tercih
/// `MiniMapView`'da da var: "sahnedeki gerçek konum zaten doğru cevabı
/// veriyor".
///
/// ### Arama KAPSANMIŞ, global değil
///
/// Kanat `Harita`'nın çocuğu ve iki bölgenin zemini de `Zemin` adını
/// taşıyor. `GameObject.Find("Zemin")` hangisini bulacağını garanti etmez —
/// bölüm 0.1'in `Duvar_3_0` dersi. Bu yüzden her arama `Transform.Find`
/// zinciriyle kendi köküne kapsanmış.
///
/// ### Var olan hiçbir şeye dokunmuyor
///
/// Yalnızca `DogumNoktalari` grubunu silip yeniden kuruyor. Haritaya,
/// terminallere, kabinlere ve süslere hiç dokunmuyor — bölüm 0'ın "elle
/// düzenleneni silme" kuralı.
/// </summary>
public static class SpawnPointSetup
{
    private const string GroupName = "DogumNoktalari";
    private const string MapRootName = "Harita";
    private const string WingName = "Harita_Genisleme_Guney";
    private const string FloorName = "Zemin";

    /// <summary>Kaçan noktası sayısı. Kadro en fazla 4 kaçan; fazlası tur
    /// başına çeşitlilik demek — aynı turda iki kez aynı diziliş olmuyor.</summary>
    private const int RunnerPointCount = 8;

    /// <summary>Canavar noktası sayısı. Kullanıcının isteği "2-3 tane".</summary>
    private const int MonsterPointCount = 3;

    /// <summary>Kapsül merkezi: ayaklar zeminde (bölüm 0.1'in ölçümü).</summary>
    private const float SpawnHeight = 0.7f;

    /// <summary>
    /// Duvar boşluğu sınaması. Küre doğum noktasının 0.25 m üstünde
    /// soruluyor: aynı yerde ve büyük yarıçapla sorulursa kürenin altı
    /// zemine değiyor ve HER nokta "dolu" çıkıyor.
    /// </summary>
    private const float ClearRadius = 0.6f;

    private const float ClearProbeHeight = 0.25f;

    /// <summary>Bir hücre kenar payı: dış duvar halkası bu kadar yer kaplıyor.</summary>
    private const float EdgeMargin = 3.2f;

    private const float RunnerSpacing = 9f;
    private const float MonsterSpacing = 11f;

    /// <summary>
    /// Canavar noktalarının HER kaçan noktasına en az uzaklığı.
    ///
    /// **Kanat ayrı harita olsa da bu şart gerekiyor.** Güney kanadı ana
    /// haritanın hemen güneyinde bitişik: ana haritanın güney kenarındaki
    /// bir kaçan noktası (z ≈ -24) ile kanadın kuzey kenarındaki bir
    /// canavar noktası (z ≈ -30) arasında 11 metre kalabiliyor. Ölçüldü —
    /// şart konmadan 12 denemenin bazılarında 10.9 m çıktı.
    ///
    /// 25, `RoundManager.minimumSpawnSeparation` ile aynı sayı: orası
    /// altına düşülürse zaten konsola uyarı yazıyor, yani iki taraf aynı
    /// ölçütü kullanıyor. Simülasyonda 12/12 tutuyor (gerçekleşen en kötü
    /// 25.4 m).
    /// </summary>
    private const float MonsterFromRunners = 25f;

    private const int MaxAttempts = 4000;

    [MenuItem("Yakalamaca/Doğum Noktalarını Kur (iki harita)", true)]
    private static bool CanRun() => !EditorApplication.isPlaying;

    [MenuItem("Yakalamaca/Doğum Noktalarını Kur (iki harita)")]
    private static void Run()
    {
        int placed = Build(out string report);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("Doğum noktaları",
            placed > 0 ? report : "Hiç nokta yerleştirilemedi.\n\n" + report, "Tamam");

        Debug.Log("Doğum noktaları kuruldu.\n" + report);
    }

    /// <summary>
    /// Noktaları kurar ve kaç tane yerleştiğini döndürür.
    ///
    /// `internal`, çünkü `Ağ Kurulumu` da aynı noktaları kuruyor. İki ayrı
    /// yerleştirme kodu tutmak, birini düzeltince öbürünü unutmak demekti —
    /// bu proje o hatayı `MonsterSetup`/`RunnerSetup` ikilisinde bir kez
    /// yaşadı ("aynı hata iki yerde varsa biri düzeltilince öbürü aranmalı").
    /// </summary>
    internal static int Build(out string report)
    {
        Transform mapRoot = FindMapRoot();

        if (mapRoot == null)
        {
            report = $"Sahnede '{MapRootName}' kökü yok — harita kurulu mu?";
            return 0;
        }

        Bounds? mainArea = FloorArea(mapRoot, null);
        Bounds? wingArea = FloorArea(mapRoot, WingName);

        if (mainArea == null)
        {
            report = $"'{MapRootName}/{FloorName}' bulunamadı; bölge sınırları ölçülemiyor.";
            return 0;
        }

        GameObject existing = FindGroup();
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        GameObject group = new GameObject(GroupName);
        Undo.RegisterCreatedObjectUndo(group, "Doğum Noktalarını Kur");

        // --- Kaçanlar: ANA harita ---
        List<Vector3> runners = Scatter(mainArea.Value, RunnerPointCount,
            RunnerSpacing, null, 0f);

        for (int i = 0; i < runners.Count; i++)
            CreatePoint(group.transform, $"Dogum_Kacan_{i + 1}", runners[i], RoundRole.Runner);

        // --- Canavar: GÜNEY kanadı ---
        //
        // Kanat yoksa (yalnızca ana haritayla çalışan bir projede) canavar
        // yine ana haritaya konuyor ama kaçanlardan AÇIKÇA uzağa. Sessizce
        // kaçanların dibine koymak, düzeltmeye çalıştığımız "canavar dibimde
        // doğdu" şikâyetini geri getirirdi (bölüm 11.1).
        Bounds monsterArea = wingArea ?? mainArea.Value;
        bool usedWing = wingArea != null;

        List<Vector3> monsters = Scatter(monsterArea, MonsterPointCount,
            MonsterSpacing, runners, MonsterFromRunners);

        for (int i = 0; i < monsters.Count; i++)
            CreatePoint(group.transform, $"Dogum_Canavar_{i + 1}", monsters[i], RoundRole.Monster);

        report = BuildReport(runners, monsters, usedWing);
        return runners.Count + monsters.Count;
    }

    // ---------- Bölge ----------

    private static Transform FindMapRoot()
    {
        GameObject root = GameObject.Find(MapRootName);
        return root != null ? root.transform : null;
    }

    /// <summary>
    /// Bir bölgenin yerleştirilebilir alanı: zeminin dünya sınırları, her
    /// kenardan bir hücre içeri çekilmiş (dış duvar halkası orada).
    ///
    /// `child` null ise ana haritanın kendi zemini, doluysa o alt kökün
    /// zemini aranıyor. Arama kapsanmış — iki bölgenin zemini de `Zemin`
    /// adını taşıyor.
    /// </summary>
    private static Bounds? FloorArea(Transform mapRoot, string child)
    {
        Transform owner = mapRoot;

        if (!string.IsNullOrEmpty(child))
        {
            owner = mapRoot.Find(child);
            if (owner == null)
                return null;
        }

        Transform floor = owner.Find(FloorName);
        if (floor == null)
            return null;

        Renderer renderer = floor.GetComponent<Renderer>();
        if (renderer == null)
            return null;

        Bounds area = renderer.bounds;
        area.Expand(new Vector3(-EdgeMargin * 2f, 0f, -EdgeMargin * 2f));

        return area;
    }

    // ---------- Yerleştirme ----------

    /// <summary>
    /// Bölgeye rastgele ama çakışmayan noktalar serpiştirir.
    ///
    /// İstenen aralık tutmazsa KADEMELİ gevşiyor: dar koridorlu bir
    /// labirentte "9 m arayla 8 nokta" her zaman mümkün değil ve hiç nokta
    /// koymamaktansa biraz yakın koymak yeğ. Aynı kademeli gevşetme
    /// `ObjectiveSetup`'ın terminal yerleştirmesinde de var.
    /// </summary>
    private static List<Vector3> Scatter(Bounds area, int wanted, float spacing,
        List<Vector3> avoid, float avoidDistance)
    {
        List<Vector3> placed = new List<Vector3>();
        float current = spacing;

        for (int relax = 0; relax < 4 && placed.Count < wanted; relax++)
        {
            int attempts = 0;

            while (placed.Count < wanted && attempts < MaxAttempts)
            {
                attempts++;

                Vector3 candidate = new Vector3(
                    Random.Range(area.min.x, area.max.x),
                    SpawnHeight,
                    Random.Range(area.min.z, area.max.z));

                if (Physics.CheckSphere(candidate + Vector3.up * ClearProbeHeight,
                        ClearRadius, ~0, QueryTriggerInteraction.Ignore))
                    continue; // duvarın içi

                if (TooClose(placed, candidate, current))
                    continue;

                if (avoid != null && avoidDistance > 0f
                    && !FarEnoughFromAll(avoid, candidate, avoidDistance))
                    continue;

                placed.Add(candidate);
            }

            // İkisi BİRLİKTE gevşiyor. Yalnızca aralığı gevşetmek yetmezdi:
            // yerleşememenin sebebi çoğu zaman "kaçanlardan uzak dur" şartı
            // oluyor ve o sabit kalırsa döngü boşa dönerdi.
            current *= 0.7f;
            avoidDistance *= 0.85f;
        }

        return placed;
    }

    private static bool TooClose(List<Vector3> points, Vector3 candidate, float minDistance)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if ((points[i] - candidate).sqrMagnitude < minDistance * minDistance)
                return true;
        }

        return false;
    }

    private static bool FarEnoughFromAll(List<Vector3> points, Vector3 candidate,
        float minDistance)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if ((points[i] - candidate).sqrMagnitude < minDistance * minDistance)
                return false;
        }

        return true;
    }

    private static void CreatePoint(Transform parent, string name, Vector3 position,
        RoundRole role)
    {
        GameObject point = new GameObject(name);
        point.transform.SetParent(parent, false);
        point.transform.position = position;

        // Mirror LOBİDE bunu kullanıyor; rol ancak tur başında belli oluyor.
        point.AddComponent<NetworkStartPosition>();

        SpawnPoint marker = point.AddComponent<SpawnPoint>();
        marker.EditorAssign(role);
    }

    // ---------- Rapor ----------

    private static GameObject FindGroup()
    {
        // `GameObject.Find` kapalı objeyi bulmuyor (bölüm 13'ün MenuSahnesi
        // dersi). Grup açık kuruluyor ama biri elle kapatmış olabilir, o
        // yüzden sahne köklerinden aranıyor.
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == GroupName)
                return roots[i];
        }

        return null;
    }

    private static string BuildReport(List<Vector3> runners, List<Vector3> monsters,
        bool usedWing)
    {
        float closest = float.MaxValue;

        for (int i = 0; i < runners.Count; i++)
        {
            for (int j = 0; j < monsters.Count; j++)
            {
                float distance = Vector3.Distance(runners[i], monsters[j]);
                if (distance < closest) closest = distance;
            }
        }

        string where = usedWing
            ? "güney kanadında"
            : $"ANA HARİTADA ('{WingName}' bulunamadı)";

        string text =
            $"{runners.Count} kaçan noktası (ana harita), "
            + $"{monsters.Count} canavar noktası ({where}).\n";

        if (runners.Count > 0 && monsters.Count > 0)
        {
            text += $"En yakın kaçan-canavar çifti: {closest:0.0} m "
                  + $"(istenen en az {MonsterFromRunners:0} m).\n";

            if (closest < MonsterFromRunners * 0.9f)
                text += "UYARI: canavar kaçanlara istenenden yakın doğuyor. "
                      + "Noktalar sığmamış olabilir — harita dar.\n";
        }

        if (runners.Count < RunnerPointCount)
            text += $"UYARI: {RunnerPointCount} kaçan noktası istendi, "
                  + $"{runners.Count} yerleşti.\n";

        if (monsters.Count < MonsterPointCount)
            text += $"UYARI: {MonsterPointCount} canavar noktası istendi, "
                  + $"{monsters.Count} yerleşti.\n";

        return text;
    }
}
