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
/// ### Yerleşim `AtmosphereSetup.BuildLights` ile AYNI algoritma
///
/// Rastgele nokta örnekle → `Physics.CheckSphere` ile duvarın içine
/// düşmediğini doğrula → önceki lambalardan yeterince uzak mı bak → koy.
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

    // Geri kalan dördü AtmosphereSetup.BuildLights/CreateLight ile BİREBİR
    // AYNI — ayrıntılı gerekçe orada (bölüm 3). Farklı bir sayı, kanadı ana
    // haritadan gözle görülür başka bir parlaklıkta/yoğunlukta bırakırdı.
    private const float LightRange = 8f;
    private const float LightIntensity = 0.75f;
    private const float LightHeight = 2.6f;
    private const float MinLightSpacing = 7f;

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
        float height = Mathf.Min(LightHeight, MazeMapBuilder.WallHeight - 0.3f);

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

            // Duvarın içine lamba koymayalım — AtmosphereSetup.BuildLights ile aynı kontrol.
            if (Physics.CheckSphere(candidate, 0.8f, ~0, QueryTriggerInteraction.Ignore))
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

        string warning = placed < LightCount
            ? $"\nUYARI: yalnızca {placed}/{LightCount} lamba için yer bulunabildi " +
              "(koridorlar dar ya da çok dolu olabilir)."
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
