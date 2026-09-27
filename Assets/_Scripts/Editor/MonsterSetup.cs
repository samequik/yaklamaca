using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Canavar modelini ve animasyonlarını baştan sona kurar: import ayarları,
/// materyal düzeltmesi, animator controller ve oyuncu prefabına bağlama.
///
/// **Neden araç?** Elle yapılırsa on beş ayrı Inspector alanı doldurulması
/// gerekiyor ve `Ağ Kurulumu` prefabı sıfırdan kurduğu için hepsi bir sonraki
/// çalıştırmada uçuyor. Araç, kurulumu her an tekrar edilebilir kılıyor —
/// projedeki bütün kurulum işleri gibi (CLAUDE.md bölüm 7).
///
/// **Animasyon durumu ağdan gönderilmiyor.** Animator hızı gözlemlenen
/// pozisyon farkından sürüyor (bkz. MonsterAnimator), yani ekstra trafik yok.
/// Yalnızca saldırı tetikleyicileri var ve onlar da zaten var olan RPC'lere
/// biniyor.
///
/// Menü: Yakalamaca > Canavar Modelini Kur
/// </summary>
public static class MonsterSetup
{
    private const string ModelFolder = "Assets/_Art/Models/Canavar";
    private const string DollFolder = "Assets/RamsterZ_FreeDoll";
    private const string DollModel = DollFolder + "/Art/Models/KillerDollUnity_BaseBody.fbx";
    private const string DollMaterials = DollFolder + "/Art/Materials";
    internal const string ControllerPath = ModelFolder + "/Canavar.controller";

    /// <summary>
    /// Kostüm başına denetleyici yolu. İlk kostüm eski adı koruyor
    /// (`Canavar.controller`): adı değiştirmek var olan prefab ve menü
    /// referanslarını koparırdı.
    ///
    /// `MenuStageSetup` de bunu çağırıyor — yol iki yerde ayrı ayrı
    /// kurulsaydı biri değişince öbürü sessizce yanlış dosyayı arardı.
    /// </summary>
    internal static string ControllerPathFor(int index) =>
        index == 0 ? ControllerPath : $"{ModelFolder}/Canavar_{index}.controller";
    private const string PlayerPrefabPath = "Assets/_Prefabs/NetworkPlayer.prefab";

    private const string MonsterRootName = "CanavarGovde";

    /// <summary>Karanlık koridorda 4K doku israf; 1024 gözle fark edilmiyor.</summary>
    private const int MaxTextureSize = 1024;

    /// <summary>
    /// Hull boyunun üstüne çarpan. Hull 1.37 m (Source ölçüsü, gerçek insandan
    /// kısa) ve modeli tam ona oturtmak canavarı ufak gösteriyordu. Model
    /// çarpışma kutusundan biraz taşıyor — kovalayan bir şeyin olduğundan büyük
    /// görünmesi zaten istenen etki.
    /// </summary>
    /// <summary>
    /// Canavar hull boyunun bu katı çiziliyor — kovalayan şeyin olduğundan
    /// büyük görünmesi istenen etki (CLAUDE.md bölüm 17).
    ///
    /// `RunnerSetup` bunu okuyor: ölüm pozunda kurbanı aynı ölçeğe çıkarmak
    /// için oran gerekiyor ve sayı iki yere elle yazılmamalı.
    ///
    /// **1.18 → 1.30 (2026-09-06),** haritada daha heybetli dursun diye.
    /// Ekrandaki boy 1.619 → 1.784 m. Değiştirince **iki aracı da** yeniden
    /// çalıştır (önce canavar, sonra kaçan): ölüm pozunun ölçek eşitlemesi bu
    /// sayının oranından geliyor.
    ///
    /// **Çarpışma kutusuna dokunmuyor** — hull 1.372 m'de kalıyor. Canavar
    /// nişan ALAN taraf, nişan alınan değil; görünen gövdenin kutudan büyük
    /// olması isabeti bozmuyor (kaçanda çarpan 1, tam da bu yüzden).
    ///
    /// Büyütmenin iki sessiz bedeli var: ölüm pozunda kurban da aynı oranda
    /// şişiyor (2.6 saniye, yerde yatarken) ve kamera modelin daha da aşağısına
    /// düşüyor — gerekirse `MovementProfile.eyeHeightOffset` yeniden ayarlanır.
    /// </summary>
    public const float ExtraScale = 1.30f;

    // Klip adları: dosya adında "@" sonrası kısım.
    private const string LungeClipKey = "attack";
    private const string KillClipKey = "kill";
    private const string RecoverClipKey = "ıskalama";

    /// <summary>Eksik rollerin ödünç alınacağı klasör (kaçanın klipleri).</summary>
    private const string BorrowFolder = "Assets/_Art/Models/Kacan";

    // ---- Fırlatan canavarın ölüm koreografisi ----
    //
    // Prefaba serileşmiş alanlar olduğu için araç bunları HER ÇALIŞTIRMADA
    // yazıyor; koddaki varsayılanı değiştirmek tek başına hiçbir şey yapmaz
    // (bölüm 16). Ayar yeri burası.

    /// <summary>Kurban canavarın kaç metre önüne oturtuluyor.</summary>
    private const float LaunchForwardOffset = 1.1f;

    /// <summary>Kurban orada kaç saniye durup sonra uçuyor.</summary>
    private const float LaunchDeathHold = 0.25f;

    // ---- Canavarın ayak sesi ----
    //
    // Bunlar da prefaba serileşmiş alanlar, yani koddaki varsayılanı
    // değiştirmek yetmiyor (bölüm 16). Ayar yeri burası.
    //
    // Adımlar MESAFEYLE tetikleniyor: `MonsterWalkStride` doğrudan tempoyu
    // belirliyor. 1.5'te yürüme 0.39 sn/adım çıkıyordu ve koşmanın
    // 0.36'sıyla neredeyse aynıydı — 2.4'te 0.63 sn/adım.

    /// <summary>Canavar yürürken/eğilirken kaç metrede bir adım sesi.</summary>
    private const float MonsterWalkStride = 2.4f;

    /// <summary>Canavarın yürüme/eğilme adımının sesi (koşu 0.85).</summary>
    private const float MonsterWalkVolume = 0.32f;

    /// <summary>
    /// Rol → kabul edilen klip adları. Her kostüm kliplerini kendi adıyla
    /// indiriyor ve o adlar rollerle uyuşmuyor: domuz katilde boşta durma
    /// `Unarmed Idle`, yürüme `Standing Walk Forward`.
    ///
    /// **Dışlama listesi ŞART.** Roller birbirinin adını kapsıyor:
    /// `crouching idle` "idle" içeriyor, `crouched walking` "walking"
    /// içeriyor. Bu tuzağa 2026-09-12'de bir kez düşüldü — muz adam dururken
    /// eğilme klibini oynuyordu (bölüm 13). Sıra da önemli: daha uzun ve
    /// daha spesifik ad önce aranıyor.
    /// </summary>
    private static readonly (string Role, string[] Hints, string[] Exclude)[] ClipRoles =
    {
        ("crouching idle", new[] { "crouch idle", "crouching idle" },
                           new string[0]),
        ("running crawl",  new[] { "running crawl", "crouched walking", "crouch walk" },
                           new string[0]),
        ("standard run",   new[] { "standing run forward", "standard run", "running", "run" },
                           new[] { "crouch", "crawl" }),
        ("walking",        new[] { "standing walk forward", "walking", "walk" },
                           new[] { "crouch" }),
        ("idle",           new[] { "unarmed idle", "idle" },
                           new[] { "crouch" }),
        (LungeClipKey,     new[] { "standing melee attack", "melee attack", "attack" },
                           new string[0]),
        (RecoverClipKey,   new[] { "ıskalama", "iskalama", "recover" },
                           new string[0]),
        (KillClipKey,      new[] { "kill", "takedown" },
                           new string[0]),
    };

    /// <summary>
    /// Kırpılacak klipler: ad → (ilk kare, son kare).
    ///
    /// Sayılar Inspector'daki `Start`/`End` kutularıyla birebir aynı; klipler
    /// 30 FPS. Kırpma burada duruyor çünkü Inspector'da elle yapılan ayar
    /// aracın bir sonraki çalıştırmasında sıfırlanıyor — tek doğru kaynak bu
    /// tablo (CLAUDE.md bölüm 7).
    ///
    /// **`attack` 15-45:** atılış bu aralıkta; öncesi hazırlık, sonrası
    /// yakalama ve yumruklama. Aralık gözle bulundu, hesaplanmadı.
    ///
    /// **`kill` 0-78:** ham klip 6 saniye ve sonu tekrar tekrar yumruklama.
    /// Kovalamacada çok uzun — canavar tek kişiyle meşgulken diğer kaçanlar
    /// bedavaya terminal dolduruyor. 78 kare ≈ 2.6 saniye.
    /// </summary>
    /// <summary>
    /// FBX materyal yuvası → kullanılacak `.mat` dosyası.
    ///
    /// Tablo ŞART: adlar birbirini tutmuyor (yuva `Unity_KillerDoll_Body`,
    /// dosya `KillerDollBodyPaintedWood`), yani ada göre otomatik eşleşme
    /// çalışmıyor. Yuva adları FBX'in içinden okundu.
    ///
    /// Gözler kırmızı seçildi; `KillerDollEyesGrey` de klasörde duruyor.
    /// </summary>
    private static readonly Dictionary<string, string> MaterialSlots =
        new Dictionary<string, string>
    {
        { "Unity_KillerDoll_Body", "KillerDollBodyPaintedWood" },
        { "Unity_KillerDoll_Head", "KillerDollHeadPaintedWood" },
        { "Unity_KillerDoll_Eyes", "KillerDollEyesRed" }
    };

    private static readonly Dictionary<string, Vector2> TrimFrames =
        new Dictionary<string, Vector2>
    {
        { LungeClipKey, new Vector2(15f, 45f) },
        { KillClipKey, new Vector2(0f, 78f) }
    };

    /// <summary>
    /// Döngü olması GEREKEN roller. Yürüyüş, koşu ve boşta durma sürekli
    /// tekrarlıyor; saldırı, ıskalama ve yakalama tek atımlık.
    ///
    /// **Karar ROLE göre veriliyor, klip ADINA göre değil.** Burada bir süre
    /// tam ad listesi vardı (`"idle", "walking", …`) ve yalnızca KUKLA'nın
    /// klip adlarını tanıyordu: başka bir paketin yürüyüşü (`Standing Walk
    /// Forward`) listede bulunamayıp DÖNGÜSÜZ import ediliyor, karakter tek
    /// adım atıp son karede donuyordu. Oynayan bunu "yürüme animasyonu yok"
    /// diye görüyor — hiçbir yerde hata yazmıyor.
    ///
    /// Aynı hata `RunnerSetup`'ta bulunup düzeltilmişti (bölüm 13); burada
    /// kalmıştı ve 2026-09-20'de domuz katille ortaya çıktı.
    /// </summary>
    private static readonly string[] LoopingRoles =
    {
        "idle", "walking", "standard run", "crouching idle", "running crawl"
    };

    [MenuItem("Yakalamaca/Canavar Modelini Kur", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Canavar Modelini Kur")]
    private static void Run()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(DollModel);
        if (model == null)
        {
            EditorUtility.DisplayDialog("Model yok",
                $"{DollModel} bulunamadı.\n\nAsset Store'dan 'Free Doll Character' " +
                "içe aktarılmış mı?", "Tamam");
            return;
        }

        int materials = FixDollMaterials();
        int textures = ShrinkDollTextures();

        // Materyal bağlama yeniden import tetikliyor; model referansı bayatlıyor.
        model = AssetDatabase.LoadAssetAtPath<GameObject>(DollModel);

        Avatar avatar = FindAvatar(DollModel);
        if (avatar == null)
        {
            EditorUtility.DisplayDialog("Avatar yok",
                "Modelin Humanoid avatarı bulunamadı. FBX'in Rig sekmesinde " +
                "Animation Type = Humanoid olmalı.", "Tamam");
            return;
        }

        // ORTAK klipler: ilk kostümün (KUKLA) klasörü aynı zamanda paylaşılan
        // set. Kendi klasörü olmayan kostümler tamamen bunu kullanıyor.
        Dictionary<string, AnimationClip> shared = ImportAnimations(avatar, ModelFolder);
        if (shared.Count == 0)
        {
            EditorUtility.DisplayDialog("Animasyon yok",
                $"{ModelFolder} altında animasyon FBX'i bulunamadı.", "Tamam");
            return;
        }

        List<MonsterBuild> builds = new List<MonsterBuild>();
        System.Text.StringBuilder report = new System.Text.StringBuilder();

        for (int i = 0; i < CharacterCatalog.Monsters.Length; i++)
        {
            CharacterCatalog.Costume costume = CharacterCatalog.Monsters[i];

            GameObject costumeModel = i == 0
                ? model
                : AssetDatabase.LoadAssetAtPath<GameObject>(costume.ModelPath);

            if (costumeModel == null)
            {
                report.AppendLine($"· UYARI: {costume.Name} modeli yok ({costume.ModelPath})");
                continue;
            }

            // Her kostümün kendi avatarı: klipleri o iskelete bağlamak için
            // gerekiyor. Humanoid'e ÇEVİRME işi burada yapılıyor — domuz
            // katilin FBX'i Generic iniyordu.
            Avatar costumeAvatar = i == 0 ? avatar : EnsureHumanoid(costume.ModelPath);

            if (costumeAvatar == null)
            {
                report.AppendLine($"· UYARI: {costume.Name} Humanoid'e çevrilemedi, atlandı");
                continue;
            }

            Dictionary<string, AnimationClip> clips = ResolveClips(costume, costumeAvatar, shared);

            string controllerPath = ControllerPathFor(i);

            builds.Add(new MonsterBuild
            {
                Costume = costume,
                Model = AssetDatabase.LoadAssetAtPath<GameObject>(costume.ModelPath),
                Controller = BuildController(clips, controllerPath),
                Clips = clips,
            });

            report.AppendLine($"· {costume.Name}: {clips.Count} klip");
            report.Append(DescribeClips(clips));
        }

        // Denetleyiciler prefab bağlamadan ÖNCE diske yazılıyor.
        //
        // 2026-09-20'de prefab adımı bir istisnayla patladı ve `Run` buraya
        // hiç ulaşamadı: denetleyiciler yalnızca BELLEKTE doluydu, diskte boş
        // kaldı ve canavar T-poza düştü. Kaydı öne almak, sonraki bir adımın
        // patlaması hâlinde bile çalışan bir animatör bırakıyor.
        AssetDatabase.SaveAssets();

        string attached = AttachToPlayerPrefab(builds);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"Canavar kuruldu — {builds.Count} kostüm.\n\n" +
            $"· {materials} materyal onarıldı (Standard shader + eksik albedo)\n" +
            $"· {textures} doku {MaxTextureSize}px'e indirildi\n" +
            report +
            $"· {attached}\n\n" +
            "Test: lobide canavarı kendine seç, KARAKTER ekranından kostüm seç, " +
            "turu başlat.\n\n" +
            "Ağ Kurulumu'nu tekrar çalıştırırsan prefab sıfırdan kurulur — " +
            "bu menüyü de tekrar çalıştır.");
    }

    /// <summary>Tek bir canavar kostümünün kurulum çıktısı.</summary>
    private struct MonsterBuild
    {
        public CharacterCatalog.Costume Costume;
        public GameObject Model;
        public AnimatorController Controller;
        public Dictionary<string, AnimationClip> Clips;
    }

    /// <summary>
    /// Modeli Humanoid'e çevirip avatarını döndürür. Zaten Humanoid'se
    /// dokunmuyor.
    ///
    /// Domuz katil paketi Generic (`animationType: 2`) iniyordu ve Humanoid
    /// olmadan üç şey birden çalışmaz: ortak kliplerin kas uzayından
    /// oynatılması (bölüm 17), sopa için `GetBoneTransform(RightHand)` ve
    /// birinci şahısta kafa/boyun gizleme.
    ///
    /// **Otomatik eşleme şaşabilir.** Unity kemikleri ada ve hiyerarşiye göre
    /// tahmin ediyor; stilize bir karakterde (domuz kafası, kısa uzuvlar)
    /// tutmayabilir. Tutmazsa avatar geçersiz kalıyor ve burası uyarı yazıyor
    /// — sessizce T-poza düşmesindense.
    /// </summary>
    private static Avatar EnsureHumanoid(string modelPath)
    {
        if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
            return null;

        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }

        Avatar avatar = FindAvatar(modelPath);

        if (avatar == null)
            Debug.LogWarning($"{modelPath}: Humanoid avatar üretilemedi. " +
                "Rig > Configure ekranında eksik kemik olabilir.");
        else if (!avatar.isValid)
            Debug.LogWarning($"{modelPath}: avatar GEÇERSİZ — Unity kemikleri " +
                "otomatik eşleyemedi. Rig > Configure'dan elle eşlemek gerekiyor.");

        return avatar;
    }

    /// <summary>Bu klip anahtarı döngü olması gereken bir role mi düşüyor?</summary>
    private static bool ShouldLoop(string key)
    {
        foreach ((string role, string[] hints, string[] exclude) in ClipRoles)
        {
            if (System.Array.IndexOf(LoopingRoles, role) < 0)
                continue;

            if (IsExcluded(key, exclude))
                continue;

            foreach (string hint in hints)
            {
                if (key == hint || key.Contains(hint))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bir kostümün rol → klip tablosunu kurar.
    ///
    /// ### Kendi klasörü olan kostüm hiçbir şeyi SESSİZCE miras almaz
    ///
    /// İlk sürüm "eksik rolü nerede bulursan doldur" zinciri kuruyordu
    /// (kendi → ortak → ödünç) ve oynanınca üç klip birden sızdı: domuz katil
    /// KUKLA'nın `ıskalama`, `kill` ve `Running Crawl` kliplerini devraldı.
    /// Oysa ilk ikisinin HİÇ olmaması, üçüncüsünün de kaçandan gelmesi
    /// gerekiyordu.
    ///
    /// Artık iki ayrı yol var:
    ///
    /// - **Kendi klasörü OLAN kostüm** (domuz katil): yalnızca kendi klipleri
    ///   + `Costume.BorrowedRoles`'ta AÇIKÇA yazılmış ödünçler. Başka hiçbir
    ///   şey. Bulunmayan rol tabloya girmiyor ve `BuildController` o durumu
    ///   hiç kurmuyor.
    /// - **Kendi klasörü OLMAYAN kostüm** (KUKLA): ortak seti kullanıyor,
    ///   davranışı hiç değişmedi.
    /// </summary>
    private static Dictionary<string, AnimationClip> ResolveClips(
        CharacterCatalog.Costume costume, Avatar avatar,
        Dictionary<string, AnimationClip> shared)
    {
        Dictionary<string, AnimationClip> result =
            new Dictionary<string, AnimationClip>();

        bool hasOwn = !string.IsNullOrEmpty(costume.AnimationFolder);

        Dictionary<string, AnimationClip> own = hasOwn
            ? ImportAnimations(avatar, costume.AnimationFolder)
            : shared;

        // Ödünç klasörü SALT OKUNUR açılıyor — `ImportAnimations` DEĞİL.
        //
        // O metot klibi verilen avatara `CopyFromOther` ile bağlayıp
        // `SaveAndReimport` çağırıyor. Ödünç klasörü KAÇANIN klasörü ve
        // klipleri KillerDoll rig'inde yazılmış: oraya domuz katilin avatarını
        // yazmak kaçanın kliplerini BOZUYOR ("Transform 'mixamorig:Hips' for
        // human bone 'Hips' not found"). 2026-09-20'de tam bu oldu.
        //
        // Yeniden import etmeye zaten gerek yok: humanoid klipler kas uzayında
        // saklanıyor, hangi avatarla import edildiklerinden bağımsız olarak
        // her humanoid iskelette oynuyorlar (bölüm 17). Okumak yeterli.
        Dictionary<string, AnimationClip> borrowed =
            costume.BorrowedRoles.Length > 0
                ? LoadClips(BorrowFolder)
                : new Dictionary<string, AnimationClip>();

        foreach ((string role, string[] hints, string[] exclude) in ClipRoles)
        {
            AnimationClip clip = Match(own, hints, exclude);

            if (clip == null && System.Array.IndexOf(costume.BorrowedRoles, role) >= 0)
                clip = Match(borrowed, hints, exclude);

            if (clip != null)
                result[role] = clip;
        }

        return result;
    }

    /// <summary>
    /// Bir klasördeki klipleri **hiçbir import ayarına dokunmadan** okur.
    ///
    /// `ImportAnimations`'ın aksine avatar yazmıyor, `SaveAndReimport`
    /// çağırmıyor — başka bir karakterin klasöründen ödünç alırken tek
    /// güvenli yol bu.
    /// </summary>
    private static Dictionary<string, AnimationClip> LoadClips(string folder)
    {
        Dictionary<string, AnimationClip> result =
            new Dictionary<string, AnimationClip>();

        if (!AssetDatabase.IsValidFolder(folder))
            return result;

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip clip = FindClip(path);

            if (clip != null)
                result[ClipKey(path)] = clip;
        }

        return result;
    }

    /// <summary>
    /// İpuçlarını sırayla deneyip ilk eşleşen klibi döndürür.
    ///
    /// Tam ad eşleşmesi önce, sonra "içinde geçiyor". Dışlama listesindeki bir
    /// kelime anahtarda geçiyorsa aday elenir — `crouching idle`'ın "idle"
    /// rolüne düşmesini engelleyen şey bu.
    /// </summary>
    private static AnimationClip Match(Dictionary<string, AnimationClip> clips,
        string[] hints, string[] exclude)
    {
        if (clips == null || clips.Count == 0)
            return null;

        foreach (string hint in hints)
        {
            foreach (KeyValuePair<string, AnimationClip> pair in clips)
            {
                if (pair.Key != hint)
                    continue;
                if (!IsExcluded(pair.Key, exclude))
                    return pair.Value;
            }
        }

        foreach (string hint in hints)
        {
            foreach (KeyValuePair<string, AnimationClip> pair in clips)
            {
                if (!pair.Key.Contains(hint))
                    continue;
                if (!IsExcluded(pair.Key, exclude))
                    return pair.Value;
            }
        }

        return null;
    }

    private static bool IsExcluded(string key, string[] exclude)
    {
        foreach (string word in exclude)
        {
            if (key.Contains(word))
                return true;
        }

        return false;
    }

    // ---------- Materyaller ----------

    /// <summary>
    /// Bebek materyalleri URP için yazılmış ama projede URP yok; shader
    /// referansları çözülmüyor ve model pembe görünüyor.
    ///
    /// Şanslıyız: Built-in slotları (`_MainTex`, `_BumpMap`, `_MetallicGlossMap`,
    /// `_OcclusionMap`) zaten dolu, yani shader'ı değiştirmek yetiyor — dokuları
    /// yeniden bağlamaya gerek yok.
    /// </summary>
    private static int FixDollMaterials()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null)
            return 0;

        int count = 0;
        List<Material> characterMaterials = new List<Material>();

        // Yalnızca karakterin materyalleri. Klasörde demo sahnesinin Floor,
        // Walls ve Skybox materyalleri de duruyor; onlar oyunda kullanılmıyor,
        // dokunmak gereksiz risk.
        foreach (string guid in AssetDatabase.FindAssets("KillerDoll t:Material", new[] { DollMaterials }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
                continue;

            // Materyal varyantının shader'ı ebeveyninden geliyor ve doğrudan
            // yazılamıyor — denemek Unity'ye hata bastırıyor.
            if (material.isVariant)
            {
                Debug.LogWarning($"{path} bir materyal varyantı; shader'ı ebeveyninden " +
                    "geliyor, atlandı. Pembe görünüyorsa ebeveynini elle Standard yap.");
                continue;
            }

            characterMaterials.Add(material);

            if (material.shader == standard)
                continue;

            // Shader'ı çözülüyorsa dokunmuyoruz: kullanıcı elle ayarlamış olabilir.
            if (material.shader != null && material.shader.name != "Hidden/InternalErrorShader")
                continue;

            Undo.RecordObject(material, "Canavar Modelini Kur");
            material.shader = standard;
            EditorUtility.SetDirty(material);
            count++;
        }

        // Shader Standard OLSA BİLE albedo eksik olabiliyor: URP'de `_BaseMap`,
        // Standard'da `_MainTex` ve shader dönüşümü dokuyu taşımıyor — model düz
        // gri kalıyor. Bu kontrol eskiden hiç yapılmıyordu, çünkü döngü shader
        // zaten Standard olduğunda en başta çıkıyordu (bkz. ModelMaterialFix).
        count += ModelMaterialFix.FillBuiltinSlots(characterMaterials, "Canavar Modelini Kur");

        // Materyalleri düzeltmek YETMİYORDU: model bu `.mat` dosyalarını hiç
        // kullanmıyordu. FBX materyal yuvası boştu (externalObjects: {}) ve model
        // kendi ürettiği gri materyali çiziyordu — dokular düzeldiği hâlde
        // canavarın gri kalmasının sebebi buydu.
        count += ModelMaterialFix.RemapExternalMaterials(DollModel, MaterialSlots);

        return count;
    }

    private static int ShrinkDollTextures()
    {
        int count = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { DollFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                continue;

            if (importer.maxTextureSize <= MaxTextureSize)
                continue;

            importer.maxTextureSize = MaxTextureSize;
            importer.SaveAndReimport();
            count++;
        }

        return count;
    }

    // ---------- Animasyonlar ----------

    private static Avatar FindAvatar(string modelPath)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is Avatar avatar)
                return avatar;
        }

        return null;
    }

    /// <summary>
    /// Animasyon FBX'lerini Humanoid'e ve bebeğin avatarına bağlar.
    ///
    /// `CopyFromOther` şart: her animasyon kendi avatarını üretirse Unity onları
    /// ayrı iskeletler sayar ve retargeting kalitesi düşer. Hepsi bebeğin
    /// avatarını kullanınca Mixamo iskeletinden bebeğe çeviri tek yerden geçiyor.
    ///
    /// Döngü işareti isimden: yürüme/koşma/idle döngü, saldırı ve ölüm değil.
    /// Döngüsüz bir yürüyüş her tekrarda takılıyor; döngülü bir saldırı ise hiç
    /// bitmiyor.
    /// </summary>
    private static Dictionary<string, AnimationClip> ImportAnimations(
        Avatar avatar, string folder)
    {
        Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();

        if (!AssetDatabase.IsValidFolder(folder))
            return result;

        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                continue;

            string key = ClipKey(path);
            bool loop = ShouldLoop(key);

            bool changed = false;

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther
                || importer.sourceAvatar != avatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                changed = true;
            }

            // Animasyon dosyalarından materyal üretilmesin: klipler "Without
            // Skin" indirildi, içlerinde mesh yok — üretilen materyaller boşuna
            // klasör kalabalığı olurdu.
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }

            // Kare aralığı HER ZAMAN dosyanın kendi tam aralığından
            // başlatılıyor, mevcut ayardan değil.
            //
            // Aksi hâlde kırpma kalıcı oluyordu: bir klibi kısaltıp sonra
            // listeden çıkarmak eski hâlini geri getirmiyordu, çünkü kısaltma
            // import ayarına yazılmış kalıyordu. Böylece araç kendi kendini
            // onarıyor — kırpma listesi neyse dosyalar ona eşitleniyor.
            // Alım listesi `ClipTakes` üzerinden: boş dönerse önce bekleyen
            // rig ayarlarıyla yeniden import ediliyor, hâlâ boşsa konsola
            // yazılıyor. Doğrudan okumak rig bozukken sessizce hiçbir ayar
            // yazmamaya yol açıyordu (bkz. ClipTakes).
            ModelImporterClipAnimation[] defaults = ClipTakes.Resolve(importer, path);
            ModelImporterClipAnimation[] takes = importer.clipAnimations;

            if (takes == null || takes.Length != defaults.Length)
                takes = defaults;

            // Kök dönüşü HER ZAMAN sıfırlanıyor ve kilit açılıyor.
            //
            // Bir süre domuz katilin yürüme/koşma kliplerine -30 derecelik bir
            // pay GÖMÜLÜYORDU. Oynanışta istenmedi: gömülü açıyı herkes
            // görüyor, yani canavarı oynayan kendi gövdesini de yamuk
            // görüyordu. Açı artık çalışma anında uygulanıyor
            // (`PlayerBodyVisual`), çünkü orada "kim bakıyor" sorusu
            // sorulabiliyor.
            //
            // Sıfır yazmak GEREKLİ, atlamak değil: ayar klibin import
            // dosyasında KALICI, yani eskiden gömülen -30 kendiliğinden
            // gitmiyor. Kırpmanın "her çalıştırmada tam aralıktan başlat"
            // kuralıyla aynı gerekçe — araç kendi kendini onarmalı.
            const float yaw = 0f;

            for (int i = 0; i < takes.Length; i++)
            {
                if (takes[i].loopTime != loop)
                {
                    takes[i].loopTime = loop;
                    changed = true;
                }

                if (!Mathf.Approximately(takes[i].rotationOffset, yaw))
                {
                    takes[i].rotationOffset = yaw;
                    changed = true;
                }

                // Pay sıfırsa kilidi de açıyoruz: sayı geri alındığında klip
                // eski hâline dönmeli. Kırpmanın "her çalıştırmada tam
                // aralıktan başlat" kuralıyla aynı gerekçe — ayar kalıcı
                // olduğu için araç kendi kendini onarmalı.
                bool lockRotation = yaw != 0f;

                if (takes[i].lockRootRotation != lockRotation)
                {
                    takes[i].lockRootRotation = lockRotation;
                    changed = true;
                }

                float fullFirst = defaults[i].firstFrame;
                float fullLast = defaults[i].lastFrame;

                if (!Mathf.Approximately(takes[i].firstFrame, fullFirst))
                {
                    takes[i].firstFrame = fullFirst;
                    changed = true;
                }

                if (!Mathf.Approximately(takes[i].lastFrame, fullLast))
                {
                    takes[i].lastFrame = fullLast;
                    changed = true;
                }

                if (TrimClip(key, importer, ref takes[i]))
                    changed = true;

                // Yakalama klibi kök hareketiyle yere iniyor; applyRootMotion
                // kapalı olduğu için o hareket atılıyor ve canavar havada
                // yatıyor görünüyordu (bkz. ClipRootMotion).
                if (key == KillClipKey && ClipRootMotion.BakeVerticalIntoPose(ref takes[i]))
                    changed = true;
            }

            if (changed)
            {
                importer.clipAnimations = takes;
                importer.SaveAndReimport();
            }

            AnimationClip clip = FindClip(path);
            if (clip != null)
                result[key] = clip;
        }

        return result;
    }

    /// <summary>
    /// Yakalama klibini kısaltır. Kırpma kare aralığıyla yapılıyor; klibin
    /// kare hızı Mixamo'dan 30 geliyor ama sabit varsaymıyoruz, dosyadan
    /// okunuyor.
    /// </summary>
    private static bool TrimClip(string key, ModelImporter importer,
        ref ModelImporterClipAnimation take)
    {
        if (!TrimFrames.TryGetValue(key, out Vector2 range))
            return false;

        // Aralık klibin dışına taşarsa kırpıyoruz: yanlış bir sayı sessizce
        // boş bir klip üretmesin.
        float first = Mathf.Clamp(range.x, 0f, take.lastFrame);
        float last = Mathf.Clamp(range.y, first + 1f, take.lastFrame);

        if (Mathf.Approximately(take.firstFrame, first) && Mathf.Approximately(take.lastFrame, last))
            return false;

        take.firstFrame = first;
        take.lastFrame = last;
        return true;
    }

    /// <summary>"Karakter@Standard Run.fbx" → "standard run"</summary>
    private static string ClipKey(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int at = name.LastIndexOf('@');

        if (at >= 0 && at < name.Length - 1)
            name = name.Substring(at + 1);

        return name.ToLowerInvariant().Trim();
    }

    private static AnimationClip FindClip(string path)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            // __preview__ ile başlayanlar Unity'nin kendi önizleme klipleri.
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        }

        return null;
    }

    // ---------- Animator ----------

    /// <summary>
    /// Durum makinesini sıfırdan kurar. Elle kurulmuyor çünkü animasyonlar
    /// değiştikçe yeniden kurulması gerekecek.
    ///
    /// Yapı:
    /// <code>
    /// Locomotion  (Speed karışımı: idle → yürüme → koşma)
    ///     ↕ Crouch eşiği
    /// CrouchLocomotion (Speed karışımı: eğik idle → emekleme)
    ///
    /// AnyState --Attack--> Atilma --(bitince)--> Iskalama --> Locomotion
    /// AnyState --Kill----> Yakalama --(bitince)--> Locomotion
    /// </code>
    ///
    /// Işkalama ayrı bir tetikleyici istemiyor: atılma klibi bittiğinde
    /// kendiliğinden geliyor. Isabet gelirse `Kill` araya giriyor ve ıskalama
    /// hiç oynamıyor — CLAUDE.md bölüm 4, kararı sunucu veriyor.
    /// </summary>
    private static AnimatorController BuildController(
        Dictionary<string, AnimationClip> clips, string controllerPath)
    {
        AnimatorController controller = GetOrClearController(controllerPath);

        controller.AddParameter(MonsterAnimator.SpeedParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(MonsterAnimator.SpeedScaleParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(MonsterAnimator.CrouchParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(MonsterAnimator.AttackTrigger, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(MonsterAnimator.KillTrigger, AnimatorControllerParameterType.Trigger);

        // Bakış IK'sı olmadan MonsterAnimator.OnAnimatorIK hiç çağrılmıyor;
        // kafa bakış yönüne dönmezdi.
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState upright = CreateBlendState(controller, machine, "Locomotion",
            new[] { Clip(clips, "idle"), Clip(clips, "walking"), Clip(clips, "standard run") },
            new[] { 0f, 1.8f, 6f });

        AnimatorState crouched = CreateBlendState(controller, machine, "CrouchLocomotion",
            new[] { Clip(clips, "crouching idle"), Clip(clips, "running crawl") },
            new[] { 0f, 2.5f });

        machine.defaultState = upright;

        // Eğilme geçişi. Eşik ortada değil, iki yönde farklı (0.6 / 0.4):
        // tam eşikte titremeyi önlüyor.
        AddCondition(upright.AddTransition(crouched), AnimatorConditionMode.Greater, 0.6f,
            MonsterAnimator.CrouchParameter);
        AddCondition(crouched.AddTransition(upright), AnimatorConditionMode.Less, 0.4f,
            MonsterAnimator.CrouchParameter);

        // Saldırı üç aşamalı:
        //
        //   ATILMA (attack 15-45) → isabet yoksa → KALKMA (ıskalama) → yürüyüş
        //                         → isabet varsa → YAKALAMA (kill 0-78)
        //
        // İlk denemede atılış olarak `attack`in ilk 0.55 saniyesi alınmıştı ve
        // bozuktu: o dilim atılış değil, hazırlıktı. Doğru aralık gözle
        // bulundu (15-45).
        AnimatorState lunge = CreateClipState(machine, "Atilma", Clip(clips, LungeClipKey));

        AddCondition(machine.AddAnyStateTransition(lunge), AnimatorConditionMode.If, 0f,
            MonsterAnimator.AttackTrigger);

        // ---- Kalkma ve Yakalama KOŞULLU ----
        //
        // Klibi olmayan bir durum kurulmuyor. Domuz katilde ikisi de yok:
        // ıskalayınca doğrudan yürüyüşe dönüyor, yakalayınca kurban olduğu
        // yerde ragdoll olup uçuyor (`Costume.LaunchesVictim`).
        //
        // Boş bir durum kurmak ekranda T-POZ demek ve Unity bunu sessizce
        // yapıyor (bölüm 13'ün "boş hareket artık HATA yazıyor" dersi) — o
        // yüzden durum hiç açılmıyor, atılma doğrudan yürüyüşe bağlanıyor.
        AnimationClip recoverClip = Clip(clips, RecoverClipKey);

        if (recoverClip != null)
        {
            AnimatorState recover = CreateClipState(machine, "Kalkma", recoverClip);

            // Geçiş normalden uzun (0.25 sn): iki klip ayrı dosyalardan geliyor
            // ve kesim noktasındaki pozlar birebir tutmayabilir; uzun karışım
            // o farkı yutuyor.
            Chain(lunge, recover, 0.25f);
            Chain(recover, upright);
        }
        else
        {
            Chain(lunge, upright, 0.2f);
        }

        AnimationClip killClip = Clip(clips, KillClipKey);

        if (killClip != null)
        {
            AnimatorState kill = CreateClipState(machine, "Yakalama", killClip);

            // İsabet onaylanınca araya giriyor; atılma bitmeden de kesebiliyor.
            AddCondition(machine.AddAnyStateTransition(kill), AnimatorConditionMode.If, 0f,
                MonsterAnimator.KillTrigger);

            Chain(kill, upright);
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// Var olan denetleyiciyi İÇİNİ BOŞALTARAK yeniden kullanır; yoksa kurar.
    ///
    /// **Dosya SİLİNMİYOR, GUID korunuyor.** Silinen bir varlığın GUID'i de
    /// gidiyor ve ona bakan her referans (oyuncu prefabı, menü sahnesindeki
    /// figürler) KOPUK kalıyor. Denetleyicisi olmayan bir `Animator` hiçbir
    /// şey oynatmıyor: ekranda T-POZ, hiçbir yerde hata yok.
    ///
    /// Aynı hata `RunnerSetup`'ta bulunup düzeltilmişti (bölüm 13) ama burada
    /// kalmıştı ve 2026-09-20'de tam olarak bu belirtiyi verdi: araç yarıda
    /// patlayınca KUKLA'nın denetleyicisi silinmiş, yenisi ise diske
    /// yazılmamış hâlde kaldı.
    ///
    /// İçerik temizleniyor çünkü durumlar, karışım ağaçları ve geçişler ayrı
    /// ALT VARLIKLAR: silinmezlerse dosyada birikirler.
    /// </summary>
    private static AnimatorController GetOrClearController(string path)
    {
        AnimatorController existing =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

        if (existing == null)
            return AnimatorController.CreateAnimatorControllerAtPath(path);

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset != existing && asset != null)
                Object.DestroyImmediate(asset, true);
        }

        existing.parameters = new AnimatorControllerParameter[0];
        existing.layers = new AnimatorControllerLayer[0];
        existing.AddLayer("Base Layer");

        return existing;
    }

    private static AnimatorState CreateBlendState(AnimatorController controller,
        AnimatorStateMachine machine, string name, AnimationClip[] motions, float[] thresholds)
    {
        BlendTree tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = MonsterAnimator.SpeedParameter,
            useAutomaticThresholds = false
        };

        AssetDatabase.AddObjectToAsset(tree, controller);

        for (int i = 0; i < motions.Length; i++)
        {
            if (motions[i] != null)
                tree.AddChild(motions[i], thresholds[i]);
        }

        AnimatorState state = machine.AddState(name);
        state.motion = tree;

        // Oynatma hızı parametreden: ayaklar yerde kaymasın diye MonsterAnimator
        // gerçek hıza göre ölçekliyor.
        state.speedParameterActive = true;
        state.speedParameter = MonsterAnimator.SpeedScaleParameter;

        return state;
    }

    private static AnimatorState CreateClipState(AnimatorStateMachine machine, string name,
        AnimationClip clip)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;
        return state;
    }

    /// <summary>Klip bitince sıradaki duruma geçiş.</summary>
    private static void Chain(AnimatorState from, AnimatorState to, float duration = 0.1f)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 0.9f;
        transition.duration = duration;
    }

    private static void AddCondition(AnimatorStateTransition transition,
        AnimatorConditionMode mode, float threshold, string parameter)
    {
        transition.hasExitTime = false;
        transition.duration = 0.12f;
        transition.AddCondition(mode, threshold, parameter);
    }

    private static AnimationClip Clip(Dictionary<string, AnimationClip> clips, string key)
    {
        if (clips.TryGetValue(key, out AnimationClip clip))
            return clip;

        Debug.LogWarning($"Canavar animasyonu eksik: '{key}'. O durum boş kalacak.");
        return null;
    }

    /// <summary>Klip adlarını ve sürelerini rapora yazar — eşleşmeyi gözle doğrulamak için.</summary>
    private static string DescribeClips(Dictionary<string, AnimationClip> clips)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();

        foreach (KeyValuePair<string, AnimationClip> pair in clips)
            text.AppendLine($"    {pair.Key}: {pair.Value.length:0.00} sn");

        return text.ToString();
    }

    // ---------- Prefab ----------

    /// <summary>
    /// Modeli oyuncu prefabına ekler, hull boyuna ölçekler ve bileşenleri bağlar.
    ///
    /// Ölçek tahmin edilmiyor, **ölçülüyor**: model sahneye alınıp gerçek
    /// renderer sınırları okunuyor ve hull yüksekliğine oranlanıyor. Aynı yöntem
    /// harita giydirmede de kullanılıyor (MapDressWindow) — pivotun nerede
    /// olduğunu bilmek zorunda kalmıyoruz.
    /// </summary>
    private static string AttachToPlayerPrefab(List<MonsterBuild> builds)
    {
        if (builds.Count == 0)
            return "UYARI: hiçbir kostüm kurulamadı.";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            return "UYARI: oyuncu prefabı yok, model bağlanmadı (önce Ağ Kurulumu).";

        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            CharacterController capsule = contents.GetComponent<CharacterController>();
            if (capsule == null)
                return "UYARI: prefabta CharacterController yok, model bağlanmadı.";

            // Hem eski TEKİL adı hem numaralı olanları temizliyoruz: araç
            // daha önce tek gövde kurmuş olabilir ve o kök yerinde kalırsa
            // iki canavar üst üste görünürdü.
            for (int i = contents.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = contents.transform.GetChild(i);

                if (child.name == MonsterRootName || child.name.StartsWith(MonsterRootName + "_"))
                    Object.DestroyImmediate(child.gameObject);
            }

            RemoveKnife(contents);

            List<GameObject> roots = new List<GameObject>();
            List<Animator> animators = new List<Animator>();
            System.Text.StringBuilder notes = new System.Text.StringBuilder();

            for (int i = 0; i < builds.Count; i++)
            {
                MonsterBuild build = builds[i];

                GameObject instance =
                    (GameObject)PrefabUtility.InstantiatePrefab(build.Model, contents.transform);
                instance.name = $"{MonsterRootName}_{i}";

                float scale = ResolveScale(instance, capsule.height) * ExtraScale;
                instance.transform.localScale = Vector3.one * scale;

                // Ayaklar hull'un tabanına: kök objenin merkezi
                // controller.center'da, taban ondan height/2 aşağıda.
                instance.transform.localPosition =
                    capsule.center + Vector3.down * (capsule.height / 2f);
                instance.transform.localRotation = Quaternion.identity;

                Animator animator = instance.GetComponentInChildren<Animator>();

                if (animator != null)
                {
                    animator.runtimeAnimatorController = build.Controller;
                    animator.applyRootMotion = false; // hareketi PlayerController veriyor
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }

                // Sopa kostüme özel. Kemik çözümü Animator'dan geldiği için
                // gövde AÇIKKEN yapılmak zorunda — kapalı bir Animator'da
                // `GetBoneTransform` null döner (bölüm 21.1). Gövdeler
                // aşağıda, kablolama bittikten SONRA kapatılıyor.
                if (build.Costume.CarriesBat)
                {
                    GameObject bat = MonsterBatBuilder.Attach(instance);
                    notes.Append(bat != null
                        ? $" · {build.Costume.Name}: sopa takıldı"
                        : $" · {build.Costume.Name}: SOPA TAKILAMADI");
                }

                // Model Ağ Kurulumu'ndan SONRA ekleniyor, yani prefabın katmanı
                // o sırada atanmış oluyor ama modelinki Default kalıyordu.
                LayerSetup.Apply(instance, LayerSetup.Oyuncu);

                ReportEyeHeight(build.Costume, instance, animator, capsule);

                roots.Add(instance);
                animators.Add(animator);
            }

            WireComponents(contents, roots, animators, builds);

            // Kablolama BİTTİKTEN sonra kapatılıyor: kapalı bir Animator'da
            // `GetBoneTransform` null döner ve ikinci kostümün kafa/boyun
            // kemiği boş kalırdı (bölüm 13'ün aynı tuzağı).
            for (int i = 1; i < roots.Count; i++)
                roots[i].SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            return $"{roots.Count} canavar gövdesi prefaba bağlandı.{notes}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// Kostümün KAFA KEMİĞİNİN gerçek yüksekliğini ölçüp konsola yazar.
    ///
    /// Kamera hizası (`Costume.EyeHeightOffset`) iki tur tahminle ayarlanmaya
    /// çalışıldı ve ikisi de alçak kaldı. Ölçüm tahminden ucuz: araç artık
    /// hedefi kendisi söylüyor, geriye sayıyı yazmak kalıyor.
    ///
    /// **Ölçüm BAĞLAMA POZUNDA (T-poz).** Eğilmiş hiza buradan çıkmıyor —
    /// onu eğilme klibi belirliyor ve ancak oynanırken görülüyor.
    ///
    /// Göz hizası kafa kemiğinin biraz ÜSTÜNDE değil ALTINDA olmalı: kemik
    /// çenenin üstünde, gözler ondan yukarıda ama kafanın tepesinden aşağıda.
    /// Pratik hedef kafa kemiği ile tepe arasının ortası.
    /// </summary>
    private static void ReportEyeHeight(CharacterCatalog.Costume costume,
        GameObject instance, Animator animator, CharacterController capsule)
    {
        if (animator == null || !animator.isHuman)
            return;

        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);

        if (head == null)
            return;

        // Gövde kökü hull'un TABANINDA duruyor (AttachToPlayerPrefab onu
        // oraya koyuyor), yani kemiğin ona göre yüksekliği doğrudan
        // "ayaktan itibaren kaç metre" demek.
        float meters = head.position.y - instance.transform.position.y;
        float units = meters / PlayerController.UnitsToMeters;

        // Bugünkü kamera hizası: hull'un 64 birimi + profilin payı (canavarda
        // 6) + kostümün payı.
        float camera = 64f + 6f + costume.EyeHeightOffset;

        Debug.Log(
            $"[Kamera ölçümü] {costume.Name}\n" +
            $"· Kafa kemiği: {units:0.0} unit ({meters:0.000} m) — ayaktan itibaren\n" +
            $"· Kamera şu an: {camera:0.0} unit ({camera * PlayerController.UnitsToMeters:0.000} m)\n" +
            $"· Fark: {units - camera:0.0} unit — kamerayı kafaya getirmek için " +
            $"`CharacterCatalog` içinde eyeHeightOffset'e bu kadar ekle.");
    }

    /// <summary>
    /// Renderer dizisini serileştirilmiş bir diziye yazar.
    ///
    /// <paramref name="headOnly"/> doğruyken yalnızca kafa parçaları giriyor:
    /// kafa kemiğini sıfırlamak gözleri/saçı toplamıyor (kendi iskeletleri
    /// var), o yüzden birinci şahısta ayrıca kapatılıyorlar.
    /// </summary>
    private static void FillRenderers(SerializedProperty array, Renderer[] source, bool headOnly)
    {
        List<Renderer> picked = new List<Renderer>();

        foreach (Renderer renderer in source)
        {
            if (!headOnly || IsHeadPart(renderer))
                picked.Add(renderer);
        }

        array.arraySize = picked.Count;

        for (int i = 0; i < picked.Count; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = picked[i];
    }

    /// <summary>Modelin gerçek boyunu ölçüp hull yüksekliğine oranlar.</summary>
    private static float ResolveScale(GameObject instance, float targetHeight)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return 1f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds.size.y > 0.01f ? targetHeight / bounds.size.y : 1f;
    }

    /// <summary>
    /// Elindeki bıçağı siler. Animasyonlar elle saldırıyor; bıçak hem gereksiz
    /// hem de saldırı animasyonunun içinden geçiyor.
    ///
    /// `MonsterAttack` bıçağa her yerde null kontrolüyle dokunuyor, o yüzden
    /// silmek güvenli — vuruş mantığı zaten bıçağa değil menzile bakıyor.
    /// </summary>
    private static void RemoveKnife(GameObject root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == "Bicak")
            {
                Object.DestroyImmediate(child.gameObject);
                return;
            }
        }
    }

    /// <summary>
    /// Bu renderer kafaya mı ait. Ad ya da materyal adında "eye"/"head"
    /// geçmesine bakıyor — modelin parçaları öyle adlandırılmış.
    /// </summary>
    private static bool IsHeadPart(Renderer renderer)
    {
        if (renderer == null)
            return false;

        if (Matches(renderer.name))
            return true;

        foreach (Material material in renderer.sharedMaterials)
        {
            if (material != null && Matches(material.name))
                return true;
        }

        return false;

        bool Matches(string value)
        {
            string lower = value.ToLowerInvariant();
            return lower.Contains("eye") || lower.Contains("head") || lower.Contains("goz");
        }
    }

    private static void WireComponents(GameObject root, List<GameObject> monsterRoots,
        List<Animator> animators, List<MonsterBuild> builds)
    {
        PlayerBodyVisual visual = root.GetComponent<PlayerBodyVisual>()
            ?? root.AddComponent<PlayerBodyVisual>();

        MonsterAnimator monsterAnimator = root.GetComponent<MonsterAnimator>()
            ?? root.AddComponent<MonsterAnimator>();

        GameObject monsterRoot = monsterRoots[0];
        Animator animator = animators[0];

        Transform capsule = root.transform.Find("Govde");

        SerializedObject serializedVisual = new SerializedObject(visual);
        serializedVisual.FindProperty("capsuleRenderer").objectReferenceValue =
            capsule != null ? capsule.GetComponent<Renderer>() : null;

        // Tekil alanlar artık YEDEK yol. Dizi dolduğu için çalışma anında
        // kullanılmıyorlar (`PlayerBodyVisual.Refresh`'teki `legacyMonster`),
        // ama boş bırakmak eski bir prefabı kurtarma yolunu kapatırdı.
        serializedVisual.FindProperty("monsterRoot").objectReferenceValue = monsterRoot;
        serializedVisual.FindProperty("headBone").objectReferenceValue =
            animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

        FillRenderers(serializedVisual.FindProperty("monsterRenderers"),
            monsterRoot.GetComponentsInChildren<Renderer>(true), false);
        FillRenderers(serializedVisual.FindProperty("headRenderers"),
            monsterRoot.GetComponentsInChildren<Renderer>(true), true);

        // ---- Asıl yol: kostüm dizisi ----
        SerializedProperty bodies = serializedVisual.FindProperty("monsterBodies");
        bodies.arraySize = monsterRoots.Count;

        for (int i = 0; i < monsterRoots.Count; i++)
        {
            SerializedProperty entry = bodies.GetArrayElementAtIndex(i);
            Renderer[] renderers = monsterRoots[i].GetComponentsInChildren<Renderer>(true);

            entry.FindPropertyRelative("root").objectReferenceValue = monsterRoots[i];
            entry.FindPropertyRelative("animator").objectReferenceValue = animators[i];
            entry.FindPropertyRelative("headBone").objectReferenceValue =
                animators[i] != null && animators[i].isHuman
                    ? animators[i].GetBoneTransform(HumanBodyBones.Head)
                    : null;

            FillRenderers(entry.FindPropertyRelative("renderers"), renderers, false);
            FillRenderers(entry.FindPropertyRelative("headRenderers"), renderers, true);
        }

        serializedVisual.FindProperty("monsterAnimator").objectReferenceValue = monsterAnimator;
        serializedVisual.ApplyModifiedProperties();

        Dictionary<string, AnimationClip> clips = builds[0].Clips;

        SerializedObject serializedAnimator = new SerializedObject(monsterAnimator);
        serializedAnimator.FindProperty("animator").objectReferenceValue = animator;
        serializedAnimator.FindProperty("controller").objectReferenceValue =
            root.GetComponent<PlayerController>();

        // Bakış yönü kameradan okunuyor. Uzak oyuncuda da doğru: PlayerPoseSync
        // dikey bakışı senkronlayıp kameranın localRotation'ına yazıyor.
        Camera playerCamera = root.GetComponentInChildren<Camera>(true);
        serializedAnimator.FindProperty("lookSource").objectReferenceValue =
            playerCamera != null ? playerCamera.transform : null;

        serializedAnimator.ApplyModifiedProperties();

        // Yerel oyuncu bilgisi buradan geliyor: kendi kafanı gizlemek için
        // "bu gövde benim mi" sorusunun cevabı lazım ve onu yalnızca ağ
        // katmanı biliyor.
        NetworkPlayerSetup networkSetup = root.GetComponent<NetworkPlayerSetup>();
        if (networkSetup != null)
        {
            SerializedObject serializedNetwork = new SerializedObject(networkSetup);
            serializedNetwork.FindProperty("playerBody").objectReferenceValue = visual;
            serializedNetwork.ApplyModifiedProperties();
        }

        // Tur sistemi gövdeyi bu bileşen üzerinden değiştiriyor.
        RoundParticipant participant = root.GetComponent<RoundParticipant>();
        if (participant != null)
        {
            SerializedObject serializedParticipant = new SerializedObject(participant);
            serializedParticipant.FindProperty("bodyVisual").objectReferenceValue = visual;

            // Fırlatan canavarın ölüm koreografisi. **Koddaki varsayılanı
            // değiştirmek YETMİYOR:** bu alanlar prefaba bir kez serileşti ve
            // orada donmuş duruyor (bölüm 16'nın tuzağı — `lockYawLimit` ve
            // `attackLockDuration` de aynı sebeple burada yazılıyor).
            //
            // Kurban canavarın `LaunchForwardOffset` metre önüne oturuyor,
            // `LaunchDeathHold` saniye orada duruyor (sopayı yediği an), sonra
            // cesede dönüşüp ileri uçuyor.
            serializedParticipant.FindProperty("launchForwardOffset").floatValue =
                LaunchForwardOffset;
            serializedParticipant.FindProperty("launchDeathHold").floatValue =
                LaunchDeathHold;

            serializedParticipant.ApplyModifiedProperties();
        }

        // Canavarın ayak sesi. Alanlar prefaba serileşmiş olduğu için
        // koddaki varsayılanları onlara ulaşmıyor (bölüm 16) — araç açıkça
        // yazıyor. Kaçanın sayılarına (sprintStride/sprintVolume) hiç
        // dokunulmuyor: sessizlik onun aracı.
        FootstepAudio footsteps = root.GetComponent<FootstepAudio>();

        if (footsteps != null)
        {
            SerializedObject serializedSteps = new SerializedObject(footsteps);
            serializedSteps.FindProperty("monsterWalkStride").floatValue = MonsterWalkStride;
            serializedSteps.FindProperty("monsterWalkVolume").floatValue = MonsterWalkVolume;
            serializedSteps.ApplyModifiedProperties();
        }

        MonsterAttack attack = root.GetComponent<MonsterAttack>();
        if (attack != null)
        {
            SerializedObject serializedAttack = new SerializedObject(attack);
            serializedAttack.FindProperty("monsterAnimator").objectReferenceValue = monsterAnimator;

            // Kilit süreleri kliplerden ÖLÇÜLÜYOR, tahmin edilmiyor. Animasyonu
            // değiştirdiğinde araç tekrar çalışınca süreler de kendiliğinden
            // güncelleniyor.
            // Kilit atılma + kalkma boyunca: ikisi tek bir hareket.
            float attackLock = 0f;

            if (clips.TryGetValue(LungeClipKey, out AnimationClip lunge) && lunge != null)
                attackLock += lunge.length;

            if (clips.TryGetValue(RecoverClipKey, out AnimationClip recover) && recover != null)
                attackLock += recover.length;

            if (attackLock > 0f)
                serializedAttack.FindProperty("attackLockDuration").floatValue = attackLock;

            // Kostüm başına kilit: her canavar KENDİ kliplerinin uzunluğu
            // kadar kilitleniyor. Tek bir sayı, kalkma klibi olmayan domuz
            // katili savurduktan sonra animasyonsuz dondururdu.
            SerializedProperty locks = serializedAttack.FindProperty("costumeAttackLocks");
            locks.arraySize = builds.Count;

            for (int i = 0; i < builds.Count; i++)
            {
                Dictionary<string, AnimationClip> own = builds[i].Clips;
                float total = 0f;

                if (own.TryGetValue(LungeClipKey, out AnimationClip ownLunge) && ownLunge != null)
                    total += ownLunge.length;

                if (own.TryGetValue(RecoverClipKey, out AnimationClip ownRecover) && ownRecover != null)
                    total += ownRecover.length;

                locks.GetArrayElementAtIndex(i).floatValue = total;
            }

            if (clips.TryGetValue(KillClipKey, out AnimationClip kill) && kill != null)
                serializedAttack.FindProperty("killLockDuration").floatValue = kill.length;

            // Kilitliyken bakış tamamen sabit. Prefabta 45 derecelik bir koni
            // duruyordu ve saldırı animasyonu boyunca kamera dönebiliyordu:
            // hem görüntüyü sallıyor hem de savurduktan SONRA nişan
            // düzeltmeye izin veriyordu. Kapatınca savurmak bir taahhüt
            // oluyor ve kaçanın keskin dönüşü gerçek bir savunmaya dönüşüyor.
            //
            // Koddaki varsayılanı değiştirmek yetmiyor: alan prefabta
            // serileştirilmiş duruyor (CLAUDE.md bölüm 16'daki tuzak), o
            // yüzden araç açıkça yazıyor.
            serializedAttack.FindProperty("lockYawLimit").floatValue = 0f;

            // Yakalama mesafesi, kurbanın ölüm pozundaki mesafesiyle AYNI
            // sabitten yazılıyor. İkisi ayrışırsa kurban önce bir noktaya
            // çekilir, ölürken başka bir noktaya sıçrar — düzeltmeye
            // çalıştığımız "vuruş anında ışınlanma" sorununun ta kendisi.
            serializedAttack.FindProperty("grabDistance").floatValue =
                LaunchForwardOffset;

            serializedAttack.ApplyModifiedProperties();
        }
    }
}
