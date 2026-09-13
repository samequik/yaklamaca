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
/// Kullanıcının kendi isteği: burası yalnızca yeni koridorların iskeletini
/// (duvar/zemin/tavan/birkaç kapı ve eğilme geçidi) kuruyor. Süsleme YOK,
/// aydınlatma YOK — ikisi de kasıtlı, aşağıda "Sonra ne çalıştırılmalı"
/// başlığında anlatılıyor. Asıl ince ayar (hangi kapı nerede duracak, hangi
/// köşe boş kalacak) elle yapılacak, tıpkı bugünkü haritanın geri kalanı gibi.
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
/// Bu araç bilerek şunlara DOKUNMUYOR, çünkü hepsi zaten var olan güvenli
/// araçlarla (bölüm 0'ın listesi) çözülüyor ve aynı mantığı burada ikinci kez
/// yazmak iki ayrı süsleme/ışık kuralı demek olurdu:
///
/// | Sırada | Araç | Neden burada değil |
/// |---|---|---|
/// | 1 | `Katmanları Kur` | Yeni duvarlar/kapılar `Harita`/`Etkilesim` katmanını `MarkStatic`/`BuildDoors` üzerinden zaten alıyor, ama tekrar çalıştırmak zararsız bir doğrulama |
/// | 2 | `Haritayı Giydir` | Bileşene/şekle göre tarıyor, konuma bakmıyor — yeni duvarları da otomatik giydirir |
/// | 3 | `Harita Süsle` | Kullanıcının istediği "süs modelleri" tam olarak burada geliyor |
/// | 4 | `Sesleri Yerleştir` | Yeni kapılara ses bağlar |
/// | 5 | `Işığı Pişir` | Yeni kanat şu an IŞIKSIZ — gerçek lamba/ambient yerleşimi ELLE ya da bu pişirmeyle geliyor |
///
/// Bu araç ışık koymuyor (yalnızca düz bir tavan kutusu var, enkaza
/// düşmesin diye) — `Atmosfer Kur`'u burada taklit etmek o aracın kendi
/// lamba yerleştirme mantığını ikinci kez yazmak, üstelik `Atmosfer Kur`'un
/// KENDİSİ çalıştırılamaz (bölüm 0 — `Lambalar` grubunun tamamını siliyor).
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

        GameObject[] breachWalls = new GameObject[ExistingBreachX.Length];
        for (int i = 0; i < ExistingBreachX.Length; i++)
        {
            string name = $"Duvar_{ExistingBreachX[i]}_0";
            breachWalls[i] = GameObject.Find(name);

            if (breachWalls[i] == null)
            {
                EditorUtility.DisplayDialog("Bağlantı noktası bulunamadı",
                    $"'{name}' sahnede yok — harita bu satırda elle değiştirilmiş olabilir.\n\n" +
                    "Aracı çalıştırmadan önce ExistingBreachX/LocalBreachX dizilerini güncel " +
                    "harita düzenine göre elden geçir (sınıf yorumu nasıl ölçüleceğini anlatıyor: " +
                    "hedef x'te Duvar_X_0 dolu, Duvar_X_1 boş/açık olmalı).",
                    "Tamam");
                return;
            }
        }

        float areaPercent = 100f * WingSize * WingSize / (17f * 17f);
        bool proceed = EditorUtility.DisplayDialog("Haritayı genişlet",
            $"Güneye {WingSize}x{WingSize}'lik yeni bir kanat eklenecek " +
            $"(mevcut alanın ~%{areaPercent:0}'i kadar).\n\n" +
            $"Var olan Harita'ya TEK dokunuş: x={ExistingBreachX[0]} ve x={ExistingBreachX[1]}'deki " +
            "iki güney duvar bloğu silinip yeni kanada bağlanacak. Başka HİÇBİR ŞEY " +
            "silinmiyor, taşınmıyor ya da yeniden üretilmiyor.\n\n" +
            $"{WingDoorCount} kapı, {WingCrouchCount} eğilme geçidi eklenecek. Düz bir tavan " +
            "var (ışıksız) — süsleme ve aydınlatma sonraki adım, bkz. sınıf yorumundaki tablo.",
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

        BuildFloor(root.transform, floorMaterial);
        BuildCeiling(root.transform, wallMaterial);
        BuildWalls(root.transform, wall, crouchCells, wallMaterial);
        BuildCrouchPassages(root.transform, wall, crouchCells, crouchMaterial);
        BuildDoors(root.transform, wall, doorCells, doorMaterial, buttonMaterial);

        // Var olana dokunan TEK adım: bağlantı noktalarındaki iki güney
        // duvarını sil. Undo'ya kaydediliyor — beğenmezsen Ctrl+Z.
        foreach (GameObject wallObject in breachWalls)
            Undo.DestroyObjectImmediate(wallObject);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        string warning = unreachable > 0
            ? $"\nUYARI: yeni kanatta {unreachable} hücreye ulaşılamıyor. Seed'i değiştirip tekrar dene."
            : "\nBağlantı doğrulandı: yeni kanadın tamamına ulaşılabiliyor.";

        Debug.Log(
            $"Güney kanadı kuruldu ({WingSize}x{WingSize}, seed {Seed}). " +
            $"{doorCells.Count} kapı, {crouchCells.Count} eğilme geçidi.\n" +
            $"Mevcut haritadan x={ExistingBreachX[0]} ve x={ExistingBreachX[1]}'de iki nokta " +
            "açıldı, başka hiçbir şey silinmedi.\n" +
            "Sırada: Katmanları Kur → Haritayı Giydir → Harita Süsle → Sesleri Yerleştir → " +
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

    private static void BuildFloor(Transform parent, Material material)
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
    }

    /// <summary>
    /// Düz bir tavan kutusu — lamba YOK, `Atmosfer Kur`'un işini burada
    /// taklit etmiyoruz (sınıf yorumu). Amaç yalnızca yeni kanadı gökyüzüne
    /// açık bırakmamak; gerçek aydınlatma Işığı Pişir'den geliyor.
    /// </summary>
    private static void BuildCeiling(Transform parent, Material material)
    {
        float span = WingSize * MazeMapBuilder.CellSize;
        float centerX = WorldX((WingSize - 1) / 2);
        float centerZ = WorldZ((WingSize - 1) / 2);

        GameObject ceiling = MazeMapBuilder.CreateBox("Tavan", parent,
            new Vector3(centerX, MazeMapBuilder.WallHeight + 0.25f, centerZ),
            new Vector3(span, 0.5f, span), material);

        MazeMapBuilder.MarkStatic(ceiling);
    }

    private static void BuildWalls(Transform parent, bool[,] wall, List<Vector2Int> crouchCells, Material material)
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
    }

    private static void BuildCrouchPassages(Transform parent, bool[,] wall, List<Vector2Int> cells, Material material)
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
    }

    private static void BuildDoors(Transform parent, bool[,] wall, List<Vector2Int> cells,
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
    }
}
