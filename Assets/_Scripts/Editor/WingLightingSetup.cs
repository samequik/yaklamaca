using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Güney kanadına (`MazeExpansionSetup`'ın kurduğu `Harita_Genisleme_Guney`)
/// rastgele loş lamba serpiştirir — kanat şu an tamamen ışıksız.
///
/// ### Neden `Atmosfer Kur` DEĞİL
///
/// O araç `Harita`'nın KENDİ `Lambalar` grubunu ve `Tavan`'ını komple silip
/// yeniden kuruyor (bölüm 0'ın en katı kuralı) — üstelik `GameObject.Find`
/// ile ARANDIĞI için "Lambalar" gibi ortak bir isim ana haritanınkiyle
/// çakışabilirdi (`MazeExpansionSetup`'ın "Duvar_3_0" hikâyesinin aynısı).
/// Bu araç ana haritaya HİÇ dokunmuyor: yalnızca kanadın kendi altına yeni
/// bir `Lambalar` grubu ekliyor, kanadın duvarlarına/kapılarına/geçitlerine/
/// süslerine (kullanıcının artık ELLE düzenlediği hiçbir şeye) dokunmuyor.
///
/// ### Yerleşim `AtmosphereSetup.BuildLights` ile AYNI algoritma — ama GERÇEKTEN test edildiğinde bir gizli hata çıktı
///
/// Rastgele nokta örnekle → `Physics.CheckSphere` ile duvarın içine
/// düşmediğini doğrula → önceki lambalardan yeterince uzak mı bak → koy.
///
/// **İlk sürüm `AtmosphereSetup`'ın SAYILARINI da (yükseklik 2.6, yarıçap
/// 0.8) birebir kopyalamıştı ve 0/8 lamba yerleştirdi — sahne dosyasından
/// doğrulandı (`Lambalar` grubu kuruldu ama içi boştu).** Sebep geometrik:
/// tavan `wallHeight+0.25=3.25` konumunda, 0.5 kalınlığında, yani ALT yüzü
/// dünya Y=3.0'da. Örnekleme yüksekliği 2.6 + yarıçap 0.8 = **3.4** —
/// tavanın 0.4 m İÇİNE giriyor. Tavan kanadın TÜM alanını kapladığı için bu
/// çakışma HER (x,z) noktasında, KOŞULSUZ gerçekleşiyor: 800 denemenin
/// hepsi `Physics.CheckSphere`'de tavana çarpıp reddediliyordu.
///
/// **Ana haritada aynı sayılar neden şimdiye kadar hiç sorun çıkarmadı?**
/// `AtmosphereSetup.Setup()` `BuildCeiling`'i `BuildLights`'tan HEMEN ÖNCE,
/// AYNI çağrıda çalıştırıyor — yani tavanın collider'ı `Physics.CheckSphere`
/// çağrıldığı anda AYNI KAREDE yeni oluşturulmuş oluyor. Unity'nin fizik
/// broadphase'i aynı karede yaratılan bir collider'ı senkronize ETMEDEN
/// önce sorgulanırsa onu GÖRMEYEBİLİYOR — yani ana haritanın 14 lambası bu
/// GERÇEK çakışma hatasını hiç görmedi, çünkü tavan o an fiziksel olarak
/// henüz "sorgulanabilir" değildi. Bu araç ise ÇOKTAN KAYDEDİLİP YENİDEN
/// AÇILMIŞ bir sahnede (tavan uzun süredir var, tamamen senkron) çalışıyor,
/// yani aynı hatayı GERÇEKTEN yakalıyor.
///
/// **Ders: "eski araçta hiç sorun çıkmadı" bir algoritmanın doğru olduğunu
/// kanıtlamıyor — yalnızca çalıştırma zamanlamasının o hatayı gizlediğini
/// gösterebilir.** `AtmosphereSetup.cs`'e dokunulmadı (bölüm 0 — çalıştırılamaz
/// zaten, ve orada hâlâ "şans eseri" çalışıyor); yükseklik/yarıçap yalnızca
/// BURADA, tavana asla değmeyecek şekilde küçültüldü (`LightHeight`+
/// `WallClearanceRadius` = 2.9 < 3.0 tavan alt yüzü — 0.1 m güvenli pay).
///
/// Kod ikinci kez yazılmadı, kopyalandı: `AtmosphereSetup`'ın kendisi
/// `internal`/paylaşılabilir değil (bütün metotları private static) ve tek
/// kullanımlık bir algoritma için üçüncü bir sınıfa taşımak (`MazeMapBuilder`/
/// `MapDressWindow`'a yaptığımız gibi) gereksiz bir soyutlama olurdu — burada
/// tekrar eden şey yalnızca otuz satırlık bir örnekleme döngüsü.
///
/// Alan sabit `SpawnAreaHalfSize` DEĞİL, kanadın KENDİ `Zemin`inin dünya
/// konumundan ve ölçeğinden hesaplanıyor (`PropScatterWindow.FindMapCenter`
/// ile aynı gerekçe) — kanat dünya orijininde durmuyor, sabit bir alan
/// kullansaydık örnekleme kanadın dışına düşerdi.
///
/// ### Görsel armatür `Haritayı Giydir`'den DEĞİL, doğrudan buradan
///
/// `MapDressWindow.DressLamps` `Lambalar` grubunu `GameObject.Find("Harita")`
/// ile ANA haritada arıyor, kanadın kendi grubuna hiç ulaşmıyor —
/// `MazeExpansionSetup`'ın "Haritayı Giydir yeni kanadı görmüyor" dersinin
/// aynısı. Bu yüzden her lambanın "Hanging Light" armatürü de burada,
/// `MapDressWindow`'un paylaşılan `Place`/`MeasurePrefab` yardımcılarıyla
/// (ikisi de `internal`) doğrudan takılıyor — ayrı bir giydirme adımı
/// gerekmiyor, ışıklar Işığı Pişir'e kadar zaten görünür bekliyor.
///
/// ### Işık AYARLARI ana haritayla AYNI
///
/// `LightIntensity` = 0.75, `AtmosphereSetup`'ınkiyle birebir aynı sayı —
/// ikisi de aynı Mixed (Shadowmask) aydınlatma modunda pişecek (bölüm 3),
/// farklı bir sayı kullanmak kanadı ana haritadan gözle görülür başka bir
/// parlaklıkta bırakırdı. `RenderSettings` (ortam/sis/yansıma) ve yönlü ışık
/// şiddeti GLOBAL — ana haritanın `Atmosfer Kur`'u zaten ayarladı, sahnede
/// tek bir kopyası var, buradan tekrar dokunmaya gerek yok.
///
/// Menü: Yakalamaca > Güney Kanadına Işık Ekle (rastgele lamba)
/// </summary>
public static class WingLightingSetup
{
    // MazeExpansionSetup.WingName ile aynı — o sınıfın kendi sabiti private
    // olduğu için burada ayrıca tanımlandı.
    private const string WingName = "Harita_Genisleme_Guney";
    private const string LightGroupName = "Lambalar";

    // MapDressWindow.OnEnable'daki varsayılanla AYNI yol — armatür oradan.
    private const string LampPrefabPath =
        "Assets/SciFi Warehouse Kit/Prefabs/Props/Misc Props/Hanging Light.prefab";

    // Alan oranına göre: ana harita 54.4x54.4 için 14 lamba (bölüm 3). Bu
    // kanat 41.6x41.6 — alan oranı ~%58, 14 * 0.58 ≈ 8. Yoğunluk ana
    // haritayla aynı hissetsin diye ölçüldü, rastgele seçilmedi.
    private const int LightCount = 8;

    // Işık RENGİ/MENZİLİ/ŞİDDETİ AtmosphereSetup.CreateLight ile BİREBİR
    // AYNI (bölüm 3) — farklı bir sayı kanadı ana haritadan gözle görülür
    // başka bir parlaklıkta bırakırdı.
    private const float LightRange = 8f;
    private const float LightIntensity = 0.75f;
    private const float MinLightSpacing = 7f;

    // YÜKSEKLİK ve YARIÇAP artık AtmosphereSetup'takiyle AYNI DEĞİL, bilerek
    // — sınıf yorumundaki "gizli hata" kutusuna bak. İkisinin toplamı tavanın
    // alt yüzüne (dünya Y=3.0) ASLA değmemeli: 2.5 + 0.4 = 2.9, 0.1 m pay.
    private const float LightHeight = 2.5f;
    private const float WallClearanceRadius = 0.4f;

    // MapDressWindow.lampWidth varsayılanıyla AYNI — armatür ana haritadakiyle
    // aynı görünsün diye.
    private const float LampVisualWidth = 0.8f;

    [MenuItem("Yakalamaca/Güney Kanadına Işık Ekle (rastgele lamba)", true)]
    private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Güney Kanadına Işık Ekle (rastgele lamba)")]
    private static void Build()
    {
        GameObject wing = GameObject.Find(WingName);
        if (wing == null)
        {
            EditorUtility.DisplayDialog("Kanat yok",
                $"Sahnede '{WingName}' bulunamadı. Önce Yakalamaca > " +
                "Haritayı Genişlet (güney kanat) çalıştırılmış olmalı.",
                "Tamam");
            return;
        }

        Transform floor = wing.transform.Find("Zemin");
        if (floor == null)
        {
            EditorUtility.DisplayDialog("Kanat bozuk",
                $"'{WingName}/Zemin' bulunamadı — kanat beklenenden farklı kurulmuş.",
                "Tamam");
            return;
        }

        GameObject lampPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefabPath);
        if (lampPrefab == null)
        {
            EditorUtility.DisplayDialog("Kit bulunamadı",
                $"'{LampPrefabPath}' bulunamadı. SciFi Warehouse Kit projede kurulu mu?",
                "Tamam");
            return;
        }

        Transform existingGroup = wing.transform.Find(LightGroupName);
        if (existingGroup != null)
        {
            bool confirmClear = EditorUtility.DisplayDialog("Lambalar yeniden kurulacak",
                $"'{WingName}/{LightGroupName}' zaten var ({existingGroup.childCount} lamba). " +
                "Devam edersen SADECE bu grup silinip yeniden dağıtılacak.\n\n" +
                "Kanadın duvarlarına, kapılarına, eğilme geçitlerine, zeminine, tavanına " +
                "ya da süslerine (Harita Süsle ile eklediklerin dahil) HİÇ dokunulmuyor.",
                "Sil ve yeniden dağıt", "Vazgeç");

            if (!confirmClear)
                return;

            Undo.DestroyObjectImmediate(existingGroup.gameObject);
        }

        GameObject group = new GameObject(LightGroupName);
        group.transform.SetParent(wing.transform, false);
        Undo.RegisterCreatedObjectUndo(group, "Güney Kanadına Işık Ekle");

        Vector3 center = floor.position;
        float span = floor.localScale.x;

        // Kenardan bir hücre içeride kalsın — AtmosphereSetup'ın SpawnAreaHalfSize'ı
        // da aynı mantıkla (54.4/2 - 3.2 ≈ 24) hesaba oturuyor, burada aynı
        // formül kanadın kendi ölçüsüne uygulanıyor.
        float halfSize = span / 2f - MazeMapBuilder.CellSize;

        // height + WallClearanceRadius tavanın alt yüzüne (WallHeight) ASLA
        // değmemeli — sınıf yorumundaki "gizli hata" kutusuna bak. Duvar
        // yüksekliği elle küçültülmüş olabilir diye Min ile de sınırlanıyor.
        float height = Mathf.Min(LightHeight, MazeMapBuilder.WallHeight - WallClearanceRadius - 0.1f);

        Bounds lampBounds = MapDressWindow.MeasurePrefab(lampPrefab);
        float lampWidth = Mathf.Max(lampBounds.size.x, lampBounds.size.z);
        float visualScale = lampWidth > 0.001f ? LampVisualWidth / lampWidth : 1f;

        int placed = 0;
        int attempts = 0;

        while (placed < LightCount && attempts < 800)
        {
            attempts++;

            Vector3 candidate = new Vector3(
                center.x + Random.Range(-halfSize, halfSize),
                height,
                center.z + Random.Range(-halfSize, halfSize));

            // Duvarın içine lamba koymayalım. Yarıçap AtmosphereSetup'takinden
            // (0.8) KÜÇÜK — sınıf yorumundaki "gizli hata" kutusuna bak.
            if (Physics.CheckSphere(candidate, WallClearanceRadius, ~0, QueryTriggerInteraction.Ignore))
                continue;
            if (IsTooCloseToExisting(group.transform, candidate))
                continue;

            CreateLamp(group.transform, candidate, placed + 1, lampPrefab, visualScale);
            placed++;
        }

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = group;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        if (placed == 0)
        {
            Debug.LogWarning(
                $"'{WingName}' altına HİÇ lamba yerleştirilemedi ({attempts} deneme). " +
                $"Yükseklik ({height:0.00}) + yarıçap ({WallClearanceRadius}) tavanın alt " +
                $"yüzüne ({MazeMapBuilder.WallHeight}) değiyor olabilir, ya da koridorlar " +
                "artık çok dar/dolu. Grup boş kaldı, elle silip tekrar deneyebilirsin.");
            return;
        }

        string warning = placed < LightCount
            ? $"\nUYARI: yalnızca {placed}/{LightCount} lamba için yer bulunabildi " +
              $"({attempts} deneme — koridorlar dar ya da çok dolu olabilir)."
            : "";

        Debug.Log(
            $"'{WingName}' altına {placed} lamba eklendi (gerçek zamanlı, henüz pişmemiş).\n" +
            "Kanadın duvarlarına, kapılarına, geçitlerine, zeminine, tavanına ya da " +
            "süslerine hiç dokunulmadı; ana Harita da hiç etkilenmedi.\n" +
            "Sırada: Işığı Pişir — yeni lambalar da dahil, sahnedeki bütün ışıklar " +
            "birlikte pişecek." + warning);
    }

    private static bool IsTooCloseToExisting(Transform group, Vector3 candidate)
    {
        foreach (Transform existing in group)
        {
            if ((existing.position - candidate).sqrMagnitude < MinLightSpacing * MinLightSpacing)
                return true;
        }

        return false;
    }

    /// <summary>
    /// `AtmosphereSetup.CreateLight` + `MapDressWindow.DressLamps` tek adımda:
    /// önce ışığı, sonra armatürü ONUN çocuğu olarak takıyor — kanadın kendi
    /// giydirmesi zaten kendi kendini giydirdiği için (`MazeExpansionSetup.
    /// DressWing`) burada da aynı desen sürdürülüyor.
    /// </summary>
    private static void CreateLamp(Transform parent, Vector3 position, int index,
        GameObject lampPrefab, float visualScale)
    {
        GameObject lampObject = new GameObject($"Lamba_{index}");
        lampObject.transform.SetParent(parent, false);
        lampObject.transform.position = position;

        Light light = lampObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = LightRange;
        light.intensity = LightIntensity;
        light.color = new Color(1f, 0.85f, 0.65f); // AtmosphereSetup ile aynı soluk sarı
        light.shadows = LightShadows.None; // gölgeyi fener veriyor, bölüm 3

        // Armatür tavana yaslı sarkıyor — MapDressWindow.DressLamps'taki
        // alignTop:true ile birebir aynı çağrı.
        MapDressWindow.Place(lampPrefab, lampObject.transform, "Giydirme_Lamba",
            lampObject.transform.position, Quaternion.identity, visualScale,
            MazeMapBuilder.WallHeight, alignTop: true, markStatic: true);
    }
}
