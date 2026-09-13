using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mevcut, ELLE DÜZENLENMİŞ haritaya dokunmadan güneyine yeni bir kanat ekler.
///
/// Oynanış geri bildirimi (2026-09-13, beş kişilik test): harita küçük geldi,
/// canavar baştan sona rahatça devriye gezip haritanın tamamını kolayca
/// tarayabiliyordu.
///
/// ### Neden ayrı bir araç, `Labirent Harita Kur` DEĞİL
///
/// O araç `Harita`'nın TAMAMINI siler ve seed'den yeniden üretir (bölüm 0'ın
/// en katı kuralı) — elle düzenlenmiş bir haritada kullanılamaz, Ctrl+Z de
/// kurtarmaz. Bu araç var olan HİÇBİR ŞEYİ silmiyor, taşımıyor ya da yeniden
/// üretmiyor; yalnızca YENİ nesneler ekliyor. Dokunduğu tek var olan şey,
/// bağlantı için sildiği iki duvar bloğu (aşağıda, "Bağlantı" başlığı).
///
/// ### Kaba temel, ince işçilik SENDE
///
/// Kullanıcının kendi isteği: burası yeni koridorların iskeletini
/// (duvar/zemin/tavan/birkaç kapı ve eğilme geçidi) kurup **SciFi Kit'le
/// kendi kendini giydiriyor** (bkz. `DressWing`) — `Haritayı Giydir`
/// penceresi buraya hiç ulaşmıyor, o yüzden bu araç kendi giydirmesini
/// taşıyor. Aydınlatma hâlâ YOK (kasıtlı, aşağıda "Sonra ne çalıştırılmalı").
/// Asıl ince ayar (hangi kapı nerede duracak, hangi köşe boş kalacak) elle
/// yapılacak, tıpkı bugünkü haritanın geri kalanı gibi.
///
/// **İkinci tur (2026-09-13):** ilk sürüm giydirmeyi `Haritayı Giydir`
/// penceresine bırakıyordu ve kullanıcı "basınca giydirmiyor" diye bildirdi.
/// Sebep: o pencere TEK `Harita/Duvarlar` grubunu (`Transform.Find` ile,
/// doğrudan çocuk) okuyor ve `gridSize`'ı en büyük hücre indeksinden
/// çıkarıyor — bu kanadın AYRI grubunu hiç görmüyordu (ana harita hiç
/// etkilenmemişti, yalnızca yeni kanat çıplak kalmıştı). O pencereyi
/// çoklu-bölge bilecek şekilde genişletmek yerine (uzun süredir çalışan ana
/// harita giydirmesini riske atardı), üç jenerik yardımcısı (`Place`,
/// `MeasurePrefab`, `WorldBounds`) `internal` yapılıp burada doğrudan
/// kullanıldı.
///
/// ### Bağlantı: hangi iki noktadan, nereden biliniyor
///
/// Sahne dosyasından ölçüldü (2026-09-13): güney duvarı (z=0 satırı, 17
/// hücrenin hepsi) tamamen dolu ve İKİ ÇIKIŞ DA bu duvarda DEĞİL (ikisi de
/// doğu/batı duvarlarında, birbirinden en uzak köşelerde — bölüm 11.5).
/// Hemen arkasındaki satırda (z=1) yalnızca x=8 dolu, geri kalan 14 hücre tek
/// bir uzun koridor. `BreachX` buradan iki nokta seçiyor (x=3 ve x=11),
/// ikisi de bu koridorda, x=8'deki dolu hücreden ve iki çıkışın olduğu
/// köşelerden uzakta.
///
/// Yeni kanat KENDİ 13x13 ızgarasında `MazeMapBuilder` ile AYNI algoritmayla
/// (aynı sınıf, aynı yardımcı metotlar — internal yapıldı, ikinci bir
/// labirent üretici yazmak yüzlerce satırı ikinci kez yazmak olurdu) bağımsız
/// üretiliyor. Sonra kendi kuzey sınırında (ki algoritma onu normalde hep
/// dolu bırakır) TAM bu iki noktayı açıyoruz ve mevcut haritanın karşılık
/// gelen iki `Duvar_X_0`'ını siliyoruz. Geometri elle hesaplandı, ölçüldü:
/// yeni kanadın kuzey yüzü mevcut haritanın güney yüzüne TAM oturuyor
/// (boşluk da çakışma da yok) — bkz. `WorldZ`.
///
/// ### Sonra ne çalıştırılmalı
///
/// Duvar/zemin/tavan/kapı giydirmesi bu aracın kendi işi (yukarıda). Geri
/// kalanlar hâlâ var olan güvenli araçlarla (bölüm 0'ın listesi) çözülüyor,
/// aynı mantığı ikinci kez yazmaya gerek yok:
///
/// | Sırada | Araç | Neden burada değil |
/// |---|---|---|
/// | 1 | `Katmanları Kur` | Yeni duvarlar/kapılar katmanı zaten `MarkStatic`/`BuildDoors` üzerinden alıyor, ama tekrar çalıştırmak zararsız bir doğrulama |
/// | 2 | `Harita Süsle` | Kullanıcının istediği "süs modelleri" (varil/kasa) tam olarak burada geliyor — bileşene/şekle göre tarıyor, konuma bakmıyor |
/// | 3 | `Sesleri Yerleştir` | Yeni kapılara ses bağlar |
/// | 4 | `Işığı Pişir` | Yeni kanat şu an IŞIKSIZ — gerçek lamba/ambient yerleşimi ELLE ya da bu pişirmeyle geliyor |
///
/// Bu araç ışık koymuyor (yalnızca düz bir tavan var, enkaza düşmesin diye)
/// — `Atmosfer Kur`'u burada taklit etmek o aracın kendi lamba yerleştirme
/// mantığını ikinci kez yazmak olurdu, üstelik `Atmosfer Kur`'un KENDİSİ
/// çalıştırılamaz (bölüm 0 — `Lambalar` grubunun tamamını siliyor).
///
/// ### Üçüncü tur: "Duvar_3_0 sahnede yok" — isim çakışması, `GameObject.Find` yanlış objeyi buluyordu
///
/// Kullanıcı ilk (çıplak) denemeden sonra `Harita_Genisleme_Guney`'i silmeye
/// çalıştı ve aracı tekrar çalıştırınca "'Duvar_3_0' sahnede yok" hatası aldı.
/// Kod okunmadan önce "harita elle değiştirilmiş" sanıldı — **yanlıştı.**
///
/// Gerçek sebep: **yeni kanadın KENDİ duvarları da `Duvar_{x}_{z}` diye
/// adlandırılıyor** (bkz. `BuildWalls`) ve kanat 13×13 olduğu için x=3 ve
/// x=11 kanadın İÇİNDE de geçerli koordinatlar — `Seed=4242` sabit olduğu
/// için kanadın kendi z=0 satırında bu iki nokta HER ÇALIŞTIRMADA solid
/// çıkıyor, yani kanat kurulduğu anda sahnede **iki tane** "Duvar_3_0" oluyor:
/// biri ana haritanın (`Harita/Duvarlar/Duvar_3_0`), biri kanadın kendisinin
/// (`Harita/Harita_Genisleme_Guney/Duvarlar/Duvar_3_0`). `GameObject.Find`
/// isim ÇAKIŞMASINDA hangisini döndüreceğini garanti etmiyor — sahne
/// dosyasından doğrulandı (`m_Father` zinciri), bulunan obje ana haritanın
/// DEĞİL kanadın kendi duvarıydı.
///
/// Kullanıcı kanadı silince (ana haritanın gerçek `Duvar_3_0`'ı zaten ilk
/// çalıştırmada silinmiş ve hiç geri gelmemişti) sahnede o isimde HİÇBİR
/// obje kalmadı — hata da tam bunu söylüyordu, ama teşhis yanlış yöne
/// gidiyordu.
///
/// **Çözüm iki parçalı:**
/// 1. Arama artık GLOBAL değil, **`Harita/Duvarlar` grubuna sıkı sıkıya
///    kapsanmış** (`existingMap.transform.Find("Duvarlar").Find(name)`) —
///    hiçbir kanadın kendi aynı isimli bloğuyla asla karışmıyor, kaç kanat
///    art arda denenirse denensin.
/// 2. O kapsamda bulunamazsa artık HATA VERMİYOR — "zaten açık" sayılıp
///    siliniyor. Bu, tam da kullanıcının düştüğü duruma karşılık geliyor:
///    ilk çalıştırma ana haritanın gerçek bloğunu zaten silmişti; kanadı
///    silip yeniden denemek bu bloğu GERİ GETİRMEZ (Undo grubu ayrı), yani
///    ikinci çalıştırmada o nokta zaten doğru şekilde açık — hata değil.
///
/// Ders bu projenin kendisinin defalarca yazdığı ders: **`GameObject.Find`
/// isim çakışmasında hangisini döndüreceğini garanti etmiyor** — arama her
/// zaman bilinen bir alt ağaca (`Transform.Find` zinciriyle) kapsanmalı.
/// </summary>
public static class MazeExpansionSetup
{
    private const string ExistingMapName = "Harita";
    private const string WingName = "Harita_Genisleme_Guney";

    // Kare — MazeMapBuilder'ın algoritması kare ızgara varsayıyor
    // (GetLength(0)'ı iki eksende de kullanıyor). 13x13 ≈ mevcut 17x17
    // alanın %58'i — kullanıcının istediği "%50 büyüt"e en yakın tek sayı.
    private const int WingSize = 13;

    private const int WingDoorCount = 2;
    private const int WingCrouchCount = 2;

    // Farklı bir sonuç istersen değiştir; MazeMapBuilder'ın Seed'iyle
    // ÇAKIŞMASIN diye bilerek farklı bir sayı.
    private const int Seed = 4242;

    // Yeni kanadın KENDİ ızgarasında, kuzey sınırındaki (localZ=0, normalde
    // hep dolu) iki nokta — ikisi de TEK sayı olmalı (algoritmanın hücre
    // paritesi). Aralarındaki fark (8) mevcut haritadaki iki bağlantı
    // noktasının farkına (11-3=8) BİLE BİLE eşit: WorldX'in ikisini birden
    // doğru noktaya oturtabilmesinin tek yolu bu.
    private static readonly int[] LocalBreachX = { 1, 9 };

    // Mevcut haritada bu iki noktadan bağlanılacak — sahne dosyasından
    // ölçüldü (bkz. sınıf yorumu). Haritayı bir daha elden geçirirsen bu
    // ikisinin hâlâ "Duvar_X_0 dolu, Duvar_X_1 yok (açık)" durumunda
    // olduğunu ölçmeden çalıştırma; aksi hâlde yeni kanat kör bir odaya
    // bağlanır ya da hiç bağlanmaz.
    private static readonly int[] ExistingBreachX = { 3, 11 };

    [MenuItem("Yakalamaca/Haritayı Genişlet (güney kanat)", true)]
    private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Haritayı Genişlet (güney kanat)")]
    private static void Build()
    {
        GameObject existingMap = GameObject.Find(ExistingMapName);
        if (existingMap == null)
        {
            EditorUtility.DisplayDialog("Harita yok",
                $"Sahnede '{ExistingMapName}' bulunamadı. Önce haritanın kurulu olması gerekiyor.",
                "Tamam");
            return;
        }

        if (GameObject.Find(WingName) != null)
        {
            EditorUtility.DisplayDialog("Zaten var",
                $"'{WingName}' sahnede zaten var. İkinci bir kanat daha eklemek için önce " +
                "bunu yeniden adlandır ya da sil — mevcut Harita'ya dokunmuyor, silmesi güvenli.",
                "Tamam");
            return;
        }

        // Ana haritanın KENDİ Duvarlar grubuna kapsanmış arama — bkz. sınıf
        // yorumundaki "üçüncü tur" kutusu. GLOBAL GameObject.Find KULLANMA:
        // her kanat kendi Duvar_{x}_{z}'sini de üretiyor ve x=3/x=11 kanadın
        // 13x13 ızgarasında da geçerli koordinatlar, yani isim çakışması
        // garanti — hangi kanadın bloğunun bulunacağı tanımsız olurdu.
        Transform existingWalls = existingMap.transform.Find("Duvarlar");
        if (existingWalls == null)
        {
            EditorUtility.DisplayDialog("Harita bozuk",
                "'Harita/Duvarlar' grubu bulunamadı — harita beklenenden farklı kurulmuş.",
                "Tamam");
            return;
        }

        GameObject[] breachWalls = new GameObject[ExistingBreachX.Length];
        int alreadyOpenCount = 0;

        for (int i = 0; i < ExistingBreachX.Length; i++)
        {
            string name = $"Duvar_{ExistingBreachX[i]}_0";
            Transform found = existingWalls.Find(name);
            breachWalls[i] = found != null ? found.gameObject : null;

            // Bulunamaması artık HATA DEĞİL: muhtemelen bu aracın önceki bir
            // çalıştırmasından zaten açık kalmış (o çalıştırma sildi, o
            // Undo grubu ayrı olduğu için kanadı silmek bu bloğu geri
            // getirmiyor) — bkz. sınıf yorumu. Silinecek bir şey yok,
            // olduğu gibi devam ediliyor.
            if (breachWalls[i] == null)
                alreadyOpenCount++;
        }

        float areaPercent = 100f * WingSize * WingSize / (17f * 17f);
        string breachNote = alreadyOpenCount > 0
            ? $"\n\nNot: {alreadyOpenCount} bağlantı noktası zaten açık (muhtemelen bu aracın " +
              "önceki bir çalıştırmasından) — o(nlar) olduğu gibi kullanılacak, silinecek bir şey yok."
            : "";
        bool proceed = EditorUtility.DisplayDialog("Haritayı genişlet",
            $"Güneye {WingSize}x{WingSize}'lik yeni bir kanat eklenecek " +
            $"(mevcut alanın ~%{areaPercent:0}'i kadar).\n\n" +
            $"Var olan Harita'ya TEK dokunuş: x={ExistingBreachX[0]} ve x={ExistingBreachX[1]}'deki " +
            "iki güney duvar bloğu silinip yeni kanada bağlanacak. Başka HİÇBİR ŞEY " +
            "silinmiyor, taşınmıyor ya da yeniden üretilmiyor.\n\n" +
            $"{WingDoorCount} kapı, {WingCrouchCount} eğilme geçidi eklenecek, hepsi SciFi Kit'le " +
            "kendiliğinden giydirilecek. Aydınlatma yok (ışıksız) — sonraki adım, bkz. sınıf " +
            "yorumundaki tablo." + breachNote,
            "Kur", "Vazgeç");

        if (!proceed)
            return;

        System.Random random = new System.Random(Seed);

        bool[,] wall = MazeMapBuilder.GenerateMaze(WingSize, random);
        MazeMapBuilder.BraidDeadEnds(wall, random);

        // Kuzey sınırı normalde hep dolu kalır (algoritma yalnızca 1..size-2
        // arasını oyuyor) — burada bilerek İKİ noktasını zorla açıyoruz, o
        // ikisi mevcut haritayla bağlantı. Hemen güneyindeki hücre (localZ=1)
        // tek sayı x'te GARANTİ açık: algoritmanın ürettiği yayılma ağacı
        // 1..size-2 arasındaki HER tek-tek hücreyi kapsıyor.
        foreach (int x in LocalBreachX)
            wall[x, 0] = false;

        int unreachable = MazeMapBuilder.CountUnreachable(wall, new Vector2Int(1, 1));

        List<Vector2Int> doorCells = MazeMapBuilder.PickSpread(
            MazeMapBuilder.FindDoorSpots(wall), WingDoorCount, 4, random);
        List<Vector2Int> crouchCells = MazeMapBuilder.PickSpread(
            MazeMapBuilder.FindCrouchSpots(wall, doorCells), WingCrouchCount, 4, random);

        GameObject root = new GameObject(WingName);
        root.transform.SetParent(existingMap.transform, false);
        Undo.RegisterCreatedObjectUndo(root, "Haritayı Genişlet (güney kanat)");

        // AYNI materyal adları: MazeMapBuilder ile aynı MaterialFolder'a
        // bakıyor, yani "Harita_Duvar" zaten varsa onu bulup PAYLAŞIYOR —
        // yeni kanat mevcut haritayla aynı renkte çıkıyor, ayrı bir materyal
        // seti gerekmiyor.
        Material wallMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Duvar", new Color(0.52f, 0.52f, 0.56f));
        Material floorMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Zemin", new Color(0.34f, 0.34f, 0.38f));
        Material doorMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Kapi", new Color(0.30f, 0.55f, 0.75f));
        Material buttonMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Dugme", new Color(0.90f, 0.35f, 0.25f));
        Material crouchMaterial = MazeMapBuilder.GetOrCreateMaterial("Harita_Gecit", new Color(0.62f, 0.55f, 0.32f));

        GameObject floor = BuildFloor(root.transform, floorMaterial);
        GameObject ceiling = BuildCeiling(root.transform, wallMaterial);
        Transform wallsGroup = BuildWalls(root.transform, wall, crouchCells, wallMaterial);
        Transform crouchGroup = BuildCrouchPassages(root.transform, wall, crouchCells, crouchMaterial);
        Transform doorsGroup = BuildDoors(root.transform, wall, doorCells, doorMaterial, buttonMaterial);

        // Var olana dokunan TEK adım: bağlantı noktalarındaki iki güney
        // duvarını sil. Undo'ya kaydediliyor — beğenmezsen Ctrl+Z. Null
        // olanlar zaten açık (yukarıdaki not) — silinecek bir şey yok.
        foreach (GameObject wallObject in breachWalls)
        {
            if (wallObject != null)
                Undo.DestroyObjectImmediate(wallObject);
        }

        // Giydirme BURADA, kendi kod yoluyla — `Haritayı Giydir` penceresine
        // dokunmuyoruz (bkz. sınıf yorumu, "neden ayrı bir giydirme").
        DressWing(root.transform, wall, crouchCells, wallsGroup, doorsGroup, crouchGroup, floor, ceiling);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        string warning = unreachable > 0
            ? $"\nUYARI: yeni kanatta {unreachable} hücreye ulaşılamıyor. Seed'i değiştirip tekrar dene."
            : "\nBağlantı doğrulandı: yeni kanadın tamamına ulaşılabiliyor.";

        Debug.Log(
            $"Güney kanadı kuruldu ve giydirildi ({WingSize}x{WingSize}, seed {Seed}). " +
            $"{doorCells.Count} kapı, {crouchCells.Count} eğilme geçidi.\n" +
            $"Mevcut haritadan x={ExistingBreachX[0]} ve x={ExistingBreachX[1]}'de iki nokta " +
            "açıldı, başka hiçbir şey silinmedi." + breachNote + "\n" +
            "Sırada: Katmanları Kur → Harita Süsle → Sesleri Yerleştir → " +
            "Işığı Pişir (yeni kanat şu an ışıksız)." + warning);
    }

    // ---------- Geometri: mevcut haritayla hizalanan dünya dönüşümü ----------

    /// <summary>
    /// Yeni kanadın x'i mevcut haritanınkiyle AYNI eksende: (localX-6)*3.2
    /// seçildi çünkü LocalBreachX={1,9} tam bu formülle mevcut haritadaki
    /// x=3 (-16 m) ve x=11 (+9.6 m) dünya konumlarına oturuyor — elle
    /// hesaplandı, iki ayrı doğrusal eksen kaymasının TEK ortak çözümü bu.
    /// </summary>
    private static float WorldX(int localX) => (localX - 6) * MazeMapBuilder.CellSize;

    /// <summary>
    /// Mevcut haritanın güney yüzü (z=0 satırının dış yüzü) dünyada z=-27.2'de
    /// duruyor (satır merkezi -25.6, yarım kalınlık 1.6). localZ=0'ın kuzey
    /// yüzü de BURAYA tam otursun diye +1 kaydırılıp aynı hücre boyutuyla
    /// ilerletiliyor — aradaki boşluk da çakışma da yok, elle ölçülüp
    /// doğrulandı.
    /// </summary>
    private static float WorldZ(int localZ) => -25.6f - (localZ + 1) * MazeMapBuilder.CellSize;

    private static Vector3 WingCellToWorld(int x, int z) => new Vector3(WorldX(x), 0f, WorldZ(z));

    private static GameObject BuildFloor(Transform parent, Material material)
    {
        float span = WingSize * MazeMapBuilder.CellSize;

        // Merkez: x ortalaması (localX=6 → WorldX=0), z ortalaması localZ
        // merkezinin (WingSize-1)/2 karşılığı.
        float centerX = WorldX((WingSize - 1) / 2);
        float centerZ = WorldZ((WingSize - 1) / 2);

        GameObject floor = MazeMapBuilder.CreateBox("Zemin", parent,
            new Vector3(centerX, -0.25f, centerZ),
            new Vector3(span, 0.5f, span), material);

        MazeMapBuilder.MarkStatic(floor);
        return floor;
    }

    /// <summary>
    /// Düz bir tavan kutusu — lamba YOK, `Atmosfer Kur`'un işini burada
    /// taklit etmiyoruz (sınıf yorumu). Amaç yalnızca yeni kanadı gökyüzüne
    /// açık bırakmamak; gerçek aydınlatma Işığı Pişir'den geliyor.
    /// </summary>
    private static GameObject BuildCeiling(Transform parent, Material material)
    {
        float span = WingSize * MazeMapBuilder.CellSize;
        float centerX = WorldX((WingSize - 1) / 2);
        float centerZ = WorldZ((WingSize - 1) / 2);

        GameObject ceiling = MazeMapBuilder.CreateBox("Tavan", parent,
            new Vector3(centerX, MazeMapBuilder.WallHeight + 0.25f, centerZ),
            new Vector3(span, 0.5f, span), material);

        MazeMapBuilder.MarkStatic(ceiling);
        return ceiling;
    }

    private static Transform BuildWalls(Transform parent, bool[,] wall, List<Vector2Int> crouchCells, Material material)
    {
        Transform group = MazeMapBuilder.CreateGroup("Duvarlar", parent);

        for (int x = 0; x < WingSize; x++)
        {
            for (int z = 0; z < WingSize; z++)
            {
                if (!wall[x, z] || crouchCells.Contains(new Vector2Int(x, z)))
                    continue;

                GameObject block = MazeMapBuilder.CreateBox($"Duvar_{x}_{z}", group,
                    WingCellToWorld(x, z) + Vector3.up * (MazeMapBuilder.WallHeight / 2f),
                    new Vector3(MazeMapBuilder.CellSize, MazeMapBuilder.WallHeight, MazeMapBuilder.CellSize),
                    material);

                MazeMapBuilder.MarkStatic(block);
            }
        }

        return group;
    }

    private static Transform BuildCrouchPassages(Transform parent, bool[,] wall, List<Vector2Int> cells, Material material)
    {
        Transform group = MazeMapBuilder.CreateGroup("EgilmeGecitleri", parent);

        foreach (Vector2Int cell in cells)
        {
            bool alongX = !wall[cell.x - 1, cell.y] && !wall[cell.x + 1, cell.y];
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 cross = alongX ? Vector3.forward : Vector3.right;

            Transform passage = MazeMapBuilder.CreateGroup($"Gecit_{cell.x}_{cell.y}", group);
            passage.position = WingCellToWorld(cell.x, cell.y);

            float sideWidth = (MazeMapBuilder.CellSize - MazeMapBuilder.CrouchWidth) / 2f;
            float sideOffset = MazeMapBuilder.CrouchWidth / 2f + sideWidth / 2f;

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject block = MazeMapBuilder.CreateBox($"Yan_{side}", passage,
                    cross * (sideOffset * side) + Vector3.up * (MazeMapBuilder.WallHeight / 2f),
                    MazeMapBuilder.AxisSize(axis, MazeMapBuilder.CellSize, MazeMapBuilder.WallHeight, cross, sideWidth),
                    material);

                MazeMapBuilder.MarkStatic(block);
            }

            GameObject ceiling = MazeMapBuilder.CreateBox("Tavan", passage,
                Vector3.up * ((MazeMapBuilder.CrouchHeight + MazeMapBuilder.WallHeight) / 2f),
                MazeMapBuilder.AxisSize(axis, MazeMapBuilder.CellSize,
                    MazeMapBuilder.WallHeight - MazeMapBuilder.CrouchHeight, cross, MazeMapBuilder.CrouchWidth),
                material);

            MazeMapBuilder.MarkStatic(ceiling);
        }

        return group;
    }

    private static Transform BuildDoors(Transform parent, bool[,] wall, List<Vector2Int> cells,
        Material doorMaterial, Material buttonMaterial)
    {
        Transform group = MazeMapBuilder.CreateGroup("Kapilar", parent);

        foreach (Vector2Int cell in cells)
        {
            bool alongX = !wall[cell.x - 1, cell.y] && !wall[cell.x + 1, cell.y];
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 cross = alongX ? Vector3.forward : Vector3.right;

            Transform doorRoot = MazeMapBuilder.CreateGroup($"Kapi_{cell.x}_{cell.y}", group);
            doorRoot.position = WingCellToWorld(cell.x, cell.y);

            GameObject panel = MazeMapBuilder.CreateBox("Panel", doorRoot,
                Vector3.up * (MazeMapBuilder.WallHeight / 2f),
                MazeMapBuilder.AxisSize(axis, MazeMapBuilder.DoorThickness, MazeMapBuilder.WallHeight,
                    cross, MazeMapBuilder.CellSize),
                doorMaterial);

            panel.AddComponent<Mirror.NetworkIdentity>();

            SlidingDoor door = panel.AddComponent<SlidingDoor>();
            SerializedObject serializedDoor = new SerializedObject(door);
            serializedDoor.FindProperty("slideDirection").vector3Value = Vector3.up;
            serializedDoor.FindProperty("slideDistance").floatValue = MazeMapBuilder.WallHeight + 0.05f;
            serializedDoor.FindProperty("allowDirectUse").boolValue = false;
            serializedDoor.ApplyModifiedProperties();

            LayerSetup.Apply(panel, LayerSetup.Harita);

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject buttonBox = MazeMapBuilder.CreateBox($"Dugme_{side}", doorRoot,
                    axis * (MazeMapBuilder.ButtonAxisOffset * side)
                        + cross * MazeMapBuilder.ButtonWallOffset
                        + Vector3.up * MazeMapBuilder.ButtonHeight,
                    MazeMapBuilder.AxisSize(axis, 0.3f, 0.3f, cross, 0.12f),
                    buttonMaterial);

                UseButton button = buttonBox.AddComponent<UseButton>();
                SerializedObject serializedButton = new SerializedObject(button);
                serializedButton.FindProperty("prompt").stringValue = "Kapıyı çalıştır";
                serializedButton.FindProperty("pressVisual").objectReferenceValue = buttonBox.transform;
                serializedButton.FindProperty("pressDirection").vector3Value = cross;

                SerializedProperty targets = serializedButton.FindProperty("targets");
                targets.arraySize = 1;
                targets.GetArrayElementAtIndex(0).objectReferenceValue = door;
                serializedButton.ApplyModifiedProperties();

                LayerSetup.Apply(buttonBox, LayerSetup.Etkilesim);
            }
        }

        return group;
    }

    // ---------- Giydirme: `MapDressWindow` ile AYNI kit, kendi kod yolu ----------

    /// <summary>
    /// Yeni kanadı SciFi Kit'le giydirir. `Haritayı Giydir` penceresini
    /// ÇAĞIRMIYORUZ, bilerek: o pencere tek bir `Harita/Duvarlar` grubunu
    /// (ana haritanınkini) okuyor ve `gridSize`'ı en büyük hücre indeksinden
    /// çıkarıyor — bu kanadın kendi ayrı grubunu hiç görmez, görse bile iki
    /// bölgenin farklı dünya-konumu formüllerini (WorldX/WorldZ burada, ana
    /// haritanınki CellToWorld'de) TEK bir `gridSize`/`origin` ile karıştırıp
    /// yanlış yerlere döşerdi.
    ///
    /// (2026-09-13, ikinci tur: kullanıcı "Haritayı Giydir'e basınca yeni
    /// kanat giydirilmiyor" diye bildirdi — sebep tam bu buydu. Ana haritanın
    /// KENDİSİ hiç etkilenmemişti, yalnızca yeni kanat çıplak kalmıştı.)
    ///
    /// Çözüm: pencerenin jenerik, "tek harita" varsaymayan üç yardımcısını
    /// (`Place`, `MeasurePrefab`, `WorldBounds`, artık internal) burada
    /// doğrudan kullanmak — aynı prefab'lar, aynı görünüm, ama kendi
    /// `wall[,]`/`WingCellToWorld` verimizle, ikinci bir "sahneden isim
    /// okuyup grid'i yeniden keşfet" adımına hiç gerek kalmadan.
    /// </summary>
    private static void DressWing(Transform root, bool[,] wall, List<Vector2Int> crouchCells,
        Transform wallsGroup, Transform doorsGroup, Transform crouchGroup, GameObject floor, GameObject ceiling)
    {
        GameObject wallPrefab = LoadKitPrefab("Walls/Wall Plain");
        GameObject floorPrefab = LoadKitPrefab("Floor/Floor Tile 01");
        GameObject ceilingPrefab = LoadKitPrefab("Ceiling/Ceiling Closed");
        GameObject doorPrefab = LoadKitPrefab("Walls/Wall BayDoor");
        Material crouchDressMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            $"{MapDressWindow.KitMaterials}/Ducts Pillars Mat.mat");

        if (wallPrefab == null || floorPrefab == null || ceilingPrefab == null || doorPrefab == null)
        {
            Debug.LogWarning("Haritayı Genişlet: SciFi Kit parçalarından biri bulunamadı, " +
                "yeni kanat çıplak (küp) kaldı. Kit'in kurulu olduğundan emin ol.");
            return;
        }

        GameObject dressing = new GameObject("Giydirme");
        dressing.transform.SetParent(root, false);

        DressWingWalls(dressing.transform, wall, crouchCells, wallPrefab);
        DressWingTiles(dressing.transform, wall, crouchCells, floorPrefab, "Zemin", 0f, alignTop: true);
        DressWingTiles(dressing.transform, wall, crouchCells, ceilingPrefab, "Tavan",
            MazeMapBuilder.WallHeight, alignTop: false);
        DressWingDoors(doorsGroup, doorPrefab);

        // Geçitler panelle kaplanmıyor (MapDressWindow.MarkCrouchPassages ile
        // aynı gerekçe): panel deliği kapatırdı. Bloklar yerinde kalıp
        // yalnızca materyal değiştiriyor — koridor duvarından ayrışsınlar diye.
        if (crouchDressMaterial != null)
            ApplyCrouchMaterial(crouchGroup, crouchDressMaterial);

        // Çıplak küpler artık görünmesin — çarpışma duruyor, yalnızca görüntü
        // kapanıyor (MapDressWindow'un ana haritada yaptığının aynısı).
        HideCubeRenderers(wallsGroup);
        SetRendererEnabled(floor, false);
        SetRendererEnabled(ceiling, false);
    }

    private static void ApplyCrouchMaterial(Transform crouchGroup, Material material)
    {
        if (crouchGroup == null)
            return;

        foreach (Transform passage in crouchGroup)
        {
            foreach (Transform part in passage)
            {
                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.sharedMaterial = material;
            }
        }
    }

    private static GameObject LoadKitPrefab(string relativePath)
        => AssetDatabase.LoadAssetAtPath<GameObject>($"{MapDressWindow.KitRoot}/{relativePath}.prefab");

    /// <summary>`MapDressWindow.DressWalls` ile birebir aynı mantık — yalnızca
    /// hücre merkezini `WingCellToWorld`'den okuyor.
    ///
    /// `crouchCells` iki yerde işe yarıyor: (1) o hücrenin KENDİSİ hiç
    /// duvar panosu almıyor — orada zaten dolu bir küp yok, geçit yapısı
    /// var (`BuildWalls`'ın kendi hariç tutmasıyla aynı). (2) bir komşu
    /// hücre geçitse, o yöne bakan yüz "dolu" değil "açık" sayılıyor —
    /// `MapDressWindow`'un asıl davranışıyla aynı: o pencere sahnede
    /// `Duvar_X_Z` adında bir nesne ARAR, geçit hücrelerinde böyle bir nesne
    /// hiç yok, yani sözlüğünde hiç görünmüyorlar. `wall[,]` dizisinde geçit
    /// hücreleri hâlâ "dolu" (true) — yalnızca ADLARI/nesneleri yok — o
    /// yüzden burada ayrıca kontrol ediliyor.</summary>
    private static void DressWingWalls(Transform parent, bool[,] wall, List<Vector2Int> crouchCells,
        GameObject wallPrefab)
    {
        Bounds bounds = MapDressWindow.MeasurePrefab(wallPrefab);

        bool thinAlongZ = bounds.size.z <= bounds.size.x;
        float width = thinAlongZ ? bounds.size.x : bounds.size.z;
        float thickness = thinAlongZ ? bounds.size.z : bounds.size.x;
        if (width <= 0.001f)
            return;

        float scale = MazeMapBuilder.CellSize / width;
        float scaledThickness = thickness * scale;

        Transform group = MazeMapBuilder.CreateGroup("Duvarlar", parent);
        Vector2Int[] directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        for (int x = 0; x < WingSize; x++)
        {
            for (int z = 0; z < WingSize; z++)
            {
                if (!wall[x, z] || crouchCells.Contains(new Vector2Int(x, z)))
                    continue;

                Vector3 center = WingCellToWorld(x, z);

                foreach (Vector2Int step in directions)
                {
                    int nx = x + step.x;
                    int nz = z + step.y;

                    if (nx < 0 || nz < 0 || nx >= WingSize || nz >= WingSize)
                        continue; // kanadın dışı

                    bool neighbourSolid = wall[nx, nz] && !crouchCells.Contains(new Vector2Int(nx, nz));
                    if (neighbourSolid)
                        continue; // komşu da duvar, bu yüz hiç görünmüyor

                    Vector3 normal = new Vector3(step.x, 0f, step.y);
                    Vector3 facePoint = new Vector3(center.x, 0f, center.z) + normal * (MazeMapBuilder.CellSize / 2f);
                    Vector3 target = facePoint - normal * (scaledThickness / 2f);

                    Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
                    if (!thinAlongZ)
                        rotation *= Quaternion.Euler(0f, -90f, 0f);

                    MapDressWindow.Place(wallPrefab, group, $"Panel_{x}_{z}_{step.x}_{step.y}",
                        target, rotation, scale, baseY: 0f, alignTop: false);
                }
            }
        }
    }

    /// <summary>`MapDressWindow.DressTiles` ile aynı mantık — duvar OLMAYAN
    /// (geçit hücreleri dahil — bkz. `DressWingWalls`'ın yorumu) her hücreye
    /// bir karo.</summary>
    private static void DressWingTiles(Transform parent, bool[,] wall, List<Vector2Int> crouchCells,
        GameObject prefab, string label, float baseY, bool alignTop)
    {
        Bounds bounds = MapDressWindow.MeasurePrefab(prefab);
        float width = Mathf.Max(bounds.size.x, bounds.size.z);
        if (width <= 0.001f)
            return;

        float scale = MazeMapBuilder.CellSize / width;
        Transform group = MazeMapBuilder.CreateGroup(label, parent);

        for (int x = 0; x < WingSize; x++)
        {
            for (int z = 0; z < WingSize; z++)
            {
                bool solid = wall[x, z] && !crouchCells.Contains(new Vector2Int(x, z));
                if (solid)
                    continue;

                MapDressWindow.Place(prefab, group, $"{label}_{x}_{z}",
                    WingCellToWorld(x, z), Quaternion.identity, scale, baseY, alignTop);
            }
        }
    }

    /// <summary>`MapDressWindow.DressDoors` ile aynı mantık, tek fark: kapı
    /// grubu zaten elimizde (sahneden yeniden aranmıyor).</summary>
    private static void DressWingDoors(Transform doorsGroup, GameObject doorPrefab)
    {
        if (doorsGroup == null)
            return;

        Bounds bounds = MapDressWindow.MeasurePrefab(doorPrefab);
        bool thinAlongZ = bounds.size.z <= bounds.size.x;
        float modelWidth = thinAlongZ ? bounds.size.x : bounds.size.z;
        if (modelWidth <= 0.001f)
            return;

        foreach (Transform door in doorsGroup)
        {
            Transform panel = door.Find("Panel");
            if (panel == null)
                continue;

            // Panel ölçeklenmiş bir küp; child'ın ezilmemesi için hacim
            // BoxCollider'a taşınıp panel ölçeği 1'e çekiliyor — MapDressWindow.
            // UnscalePanel ile birebir aynı numara (o metot private, burada
            // tek kullanımlık olduğu için kopyalamak internal yapmaktan ucuz).
            BoxCollider box = panel.GetComponent<BoxCollider>();
            Vector3 size = panel.localScale;
            if (box != null)
                box.size = size;
            panel.localScale = Vector3.one;

            bool facesX = size.x <= size.z;
            Vector3 normal = facesX ? Vector3.right : Vector3.forward;
            float opening = facesX ? size.z : size.x;

            Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
            if (!thinAlongZ)
                rotation *= Quaternion.Euler(0f, -90f, 0f);

            MapDressWindow.Place(doorPrefab, panel, "Giydirme_Kapi", panel.position, rotation,
                opening / modelWidth, panel.position.y - size.y / 2f, alignTop: false, markStatic: false);

            SetRendererEnabled(panel.gameObject, false);
        }
    }

    private static void HideCubeRenderers(Transform group)
    {
        if (group == null)
            return;

        foreach (Transform child in group)
            SetRendererEnabled(child.gameObject, false);
    }

    private static void SetRendererEnabled(GameObject target, bool visible)
    {
        if (target == null)
            return;

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
            renderer.enabled = visible;
    }
}
