using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Kaçanın modelini ve animasyonlarını kurar — `MonsterSetup`'ın kaçan
/// karşılığı. Elle yapılırsa on beş Inspector alanı doldurmak gerekiyor ve
/// `Ağ Kurulumu` prefabı sıfırdan kurduğu için hepsi bir sonraki çalıştırmada
/// uçuyor.
///
/// **Model:** Banana Man (Banana Yellow Games). Rig zaten Humanoid ve hatasız
/// geliyor. Materyalleri Built-in Standard, yani canavardaki "URP shader yok,
/// model pembe" tuzağı burada yok — ama **materyaller modele bağlı değildi**:
/// FBX materyal yuvası boştu ve Unity varsayılan gri materyali kullanıyordu.
/// Araç bağlantıyı açıkça kuruyor (bkz. FixMaterials, ModelMaterialFix).
///
/// **Animasyonlar:** `Assets/_Art/Models/Kacan` altındaki `...@<ad>.fbx`
/// dosyaları. Eşleşme `@` sonrasındaki ada bakıyor, küçük harfe çevrilerek.
///
/// ### Kaynak avatar dosyaya göre seçiliyor
///
/// Animasyon FBX'i kendi iskeletini taşıyor ve `Copy From Other Avatar` o
/// iskelete uyan bir avatar istiyor. Klasördeki klipler Mixamo'dan
/// **KillerDoll** rigiyle indirilmiş (`KillerDollUnity_BaseBody@...`), yani
/// kaynak avatar canavarınki olmalı — Banana Man'inki verilirse kemikler
/// tutmaz. Sonuçta çıkan klip humanoid kas uzayında olduğu için **her** humanoid
/// avatarda oynuyor; Banana Man'de oynamasının sebebi bu.
///
/// Bu yüzden araç dosya adının `@` ÖNCESİNE bakıp avatarı seçiyor: ileride
/// `BananaMan@Idle.fbx` atılırsa o da doğru avatarla import ediliyor.
///
/// ### Eksik klip = boş durum, hata değil
///
/// Elde `Idle` yoksa canavarın `Idle` klibine düşülüyor (humanoid klipler
/// avatardan bağımsız). Kaçan klasörüne bir `...@Idle.fbx` atıldığı anda
/// kendiliğinden ona geçiyor.
///
/// Menü: Yakalamaca > Kaçan Modelini Kur
/// </summary>
public static class RunnerSetup
{
    internal const string AnimationFolder = "Assets/_Art/Models/Kacan";
    internal const string MonsterAnimationFolder = "Assets/_Art/Models/Canavar";
    internal const string ControllerPath = AnimationFolder + "/Kacan.controller";

    /// <summary>
    /// Kostümün denetleyici yolu. Sıfırıncı eski yolu koruyor: `ControllerPath`
    /// başka araçlarda da geçiyor ve adını değiştirmek onları kırardı.
    /// </summary>
    internal static string ControllerPathFor(int costume) =>
        costume == 0 ? ControllerPath : $"{AnimationFolder}/Kacan_{costume}.controller";
    private const string PlayerPrefabPath = "Assets/_Prefabs/NetworkPlayer.prefab";

    /// <summary>
    /// Birincil kaçan modeli — animasyonların içe aktarıldığı ve materyali
    /// onarılan model. Yol `CharacterCatalog`'dan geliyor: kostüm listesi tek
    /// yerde dursun diye (bkz. o dosyadaki "tek kaynak" notu).
    /// </summary>
    internal static string ModelPath => CharacterCatalog.Runners[0].ModelPath;

    internal static string MonsterModelPath => CharacterCatalog.Monsters[0].ModelPath;

    private const string RunnerRootName = "KacanGovde";

    /// <summary>Canavarın yakalama klibinin anahtarı — ölüm klibi buna eşitleniyor.</summary>
    private const string MonsterKillKey = "kill";

    /// <summary>
    /// Hull boyunun üstüne çarpan. Canavarda 1.18 kullanılıyor — kovalayan
    /// şeyin olduğundan büyük görünmesi istenen etki. Kaçanda **1**: canavar
    /// ona nişan alıyor, görünen gövde çarpışma kutusuyla örtüşmeli. Şişirilmiş
    /// bir kaçan, isabet etmesi gerekirken etmeyen vuruşlar üretirdi.
    /// </summary>
    private const float ExtraScale = 1f;

    /// <summary>
    /// Ölüm klibinin oynatma hızı. Ham klipte kurban yere geç düşüyordu —
    /// canavar çoktan yumruklamaya başlamışken kaçan hâlâ havadaydı, arada bir
    /// saniyeye yakın fark vardı.
    ///
    /// **Yalnızca ölüm klibine uygulanıyor.** Locomotion'ın hızı zaten
    /// karakterin gerçek hızından hesaplanıyor (`SpeedScaleParameter`); oraya
    /// sabit bir çarpan koymak ayak kaymasını bozardı.
    ///
    /// Ayarlamak için: klip 2.6 sn, yani düşüş anı bu çarpanın tersiyle
    /// öne geliyor. Daha erken düşsün istiyorsan büyüt, yumuşasın istiyorsan
    /// 1'e yaklaştır. Değiştirdikten sonra `Kaçan Modelini Kur`.
    /// </summary>
    private const float DeathSpeed = 1.7f;

    // Klip anahtarları: dosya adında "@" sonrası kısım, küçük harf.
    /// <summary>Canavardan ödünç alınan boşta klibinin anahtarı (bölüm 17).</summary>
    private const string IdleKey = "idle";

    /// <summary>Havada olma klibi. `falling idle` varsa tercih ediliyor: gerçek
    /// bir döngü. Yoksa `jumping` kullanılıyor.</summary>

    private static readonly string[] AirborneHints = { "jump00", "falling idle", "falling", "jumping" };

    /// <summary>Yakalanma klibi. Ad uzun ve Türkçe, o yüzden birebir değil
    /// "içeriyor mu" diye aranıyor.</summary>
    private static readonly string[] DeathHints = { "takedown", "ölme", "olme", "death", "dying" };

    // Rol ipuçları. Sıra ÖNEMLİ: ilk eşleşen kazanıyor, yani modele özel ad
    // ortak addan önce geliyor. Tek liste iki paketi birden karşılıyor —
    // ayrı bir "bu pakette şu klip şuna denk" tablosu tutmak, her yeni
    // kostümde güncellenmesi gereken üçüncü bir yer olurdu.
    /// <summary>
    /// Ayakta roller için dışlanan adlar: eğilme klipleri ayakta rollerin
    /// adını kapsıyor (`crouching idle` → "idle", `crouched walking` →
    /// "walking") ve dışlanmazsa karakter dururken eğilmiş duruyor.
    /// </summary>
    private static readonly string[] UprightOnly = { "crouch", "egilme", "eğilme" };

    private static readonly string[] IdleHints = { "wait00", "idle" };
    private static readonly string[] WalkHints = { "walk00_f", "walking" };
    private static readonly string[] RunHints = { "run00_f", "running" };
    private static readonly string[] WalkBackHints = { "walk00_b" };
    private static readonly string[] WalkLeftHints = { "walk00_l" };
    private static readonly string[] WalkRightHints = { "walk00_r" };
    private static readonly string[] RunLeftHints = { "run00_l" };
    private static readonly string[] RunRightHints = { "run00_r" };
    private static readonly string[] CrouchIdleHints = { "crouching idle" };
    private static readonly string[] CrouchWalkHints = { "crouched walking" };

    /// <summary>
    /// Boşta kalma kırılımları: uzun süre kıpırdamayınca oynayan esneme,
    /// gerinme gibi klipler. Her biri ayrı bir varyant.
    /// </summary>
    private static readonly string[] IdleBreakHints = { "wait01", "wait02", "wait03", "wait04" };

    /// <summary>
    /// Döngüye alınacak klipler — ADA GÖRE, tam eşleşmeye değil.
    ///
    /// Eskiden tam ad listesiydi ve yalnızca bizim klip adlarımızı tanıyordu:
    /// başka bir paketin yürüyüşü döngüsüz içe aktarılıp tek seferde durup
    /// kalıyordu. İpucu listesi her paketi birden karşılıyor.
    /// </summary>
    private static readonly string[] LoopingHints =
    {
        "idle", "walk", "run", "wait", "falling", "jumping"
    };

    [MenuItem("Yakalamaca/Kaçan Modelini Kur", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Yakalamaca/Kaçan Modelini Kur")]
    private static void Run()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog("Model yok",
                $"{ModelPath} bulunamadı.\n\nBanana Man paketi içe aktarılmış mı?", "Tamam");
            return;
        }

        Avatar target = FindAvatar(ModelPath);
        if (target == null)
        {
            EditorUtility.DisplayDialog("Avatar yok",
                "Banana Man'in Humanoid avatarı bulunamadı. FBX'in Rig sekmesinde " +
                "Animation Type = Humanoid olmalı.", "Tamam");
            return;
        }

        int materials = FixMaterials();

        // Materyal bağlama yeniden import tetikliyor; elimizdeki referanslar
        // bayatlamış olabilir.
        model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        target = FindAvatar(ModelPath);

        // Ölüm klibi canavarın yakalama klibiyle AYNI süreye çekiliyor: uzun
        // kalırsa canavar işini bitirip yürümeye başlarken kurban hâlâ yere
        // düşüyor oluyor.
        AnimationClip monsterKill = BorrowFromMonster(MonsterKillKey);
        float deathSeconds = monsterKill != null ? monsterKill.length : 0f;

        if (monsterKill == null)
            Debug.LogWarning("Canavarın 'kill' klibi bulunamadı; ölüm klibi kırpılmadı.");

        // ORTAK klipler: bizim Mixamo takımımız. Her kostüm bunların üstüne
        // kendi kliplerini yazıyor; yazmadığı yerler ortaktan doluyor.
        Dictionary<string, AnimationClip> shared = ImportAnimations(AnimationFolder, target, deathSeconds);

        List<AnimatorController> controllers = new List<AnimatorController>();
        int ownClipTotal = 0;

        for (int i = 0; i < CharacterCatalog.Runners.Length; i++)
        {
            CharacterCatalog.Costume costume = CharacterCatalog.Runners[i];
            Dictionary<string, AnimationClip> merged = new Dictionary<string, AnimationClip>(shared);

            if (!string.IsNullOrEmpty(costume.AnimationFolder))
            {
                // Kostümün klasöründeki klipler KENDİ iskeletinde yazılmış:
                // kaynak avatar ad tahminine bırakılmıyor, doğrudan veriliyor.
                Avatar own = FindAvatar(costume.ModelPath);

                if (own == null)
                {
                    Debug.LogWarning($"Kostüm '{costume.Name}': avatar bulunamadı, " +
                        "kendi animasyonları atlandı ve ortak klipler kullanılacak.");
                }
                else
                {
                    Dictionary<string, AnimationClip> ownClips =
                        ImportAnimations(costume.AnimationFolder, target, deathSeconds, own);

                    foreach (KeyValuePair<string, AnimationClip> pair in ownClips)
                        merged[pair.Key] = pair.Value;

                    ownClipTotal += ownClips.Count;
                }
            }

            controllers.Add(BuildController(merged, ControllerPathFor(i), costume.Name));
        }

        Dictionary<string, AnimationClip> clips = shared;
        string prefabReport = AttachToPlayerPrefab(controllers, clips, deathSeconds);

        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Kaçan modeli kuruldu",
            $"· {clips.Count} animasyon içe aktarıldı ve Humanoid'e çevrildi\n" +
            (materials > 0 ? $"· {materials} materyal onarıldı (bağlantı + albedo)\n" : "") +
            (deathSeconds > 0f ? $"· Ölüm klibi {deathSeconds:0.00} sn'ye kırpıldı (canavarın yakalama süresi)\n" : "") +
            $"· {controllers.Count} denetleyici kuruldu (kostüm başına bir tane)\n" +
            (ownClipTotal > 0 ? $"· {ownClipTotal} klip kostümlerin kendi paketlerinden geldi\n" : "") +
            $"· {prefabReport}\n\n" +
            "Klipler:\n" + DescribeClips(clips) +
            "\nTEST: Play → Host → [2] kaçan olarak başlat → üçüncü şahıs görmek " +
            "için [3] ile kendini elendir.",
            "Tamam");
    }

    // ---------- Animasyonlar ----------

    /// <summary>
    /// Bir klasördeki klipleri humanoid'e çevirip sözlük hâlinde döndürür.
    ///
    /// `forcedAvatar` doluysa klasördeki HER klip onunla içe aktarılıyor:
    /// kostümün kendi klasöründeki klipler kendi iskeletinde yazılmış oluyor
    /// ve ad tahminine gerek kalmıyor. Ortak klasörde ise ad kuralı işliyor
    /// (`@` öncesi hangi model).
    /// </summary>
    private static Dictionary<string, AnimationClip> ImportAnimations(string folder,
        Avatar runnerAvatar, float deathSeconds, Avatar forcedAvatar = null)
    {
        Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();

        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogWarning($"{folder} yok — animasyon bulunamadı.");
            return result;
        }

        Avatar monsterAvatar = FindAvatar(MonsterModelPath);
        string runnerModelName = Path.GetFileNameWithoutExtension(ModelPath).ToLowerInvariant();

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                continue;

            string key = ClipKey(path);
            bool loop = IsLoopingKey(key);
            bool isDeath = IsDeathKey(key);

            // Kare hızı dosyadan okunuyor, 30 varsayılmıyor.
            float fps = 30f;

            if (isDeath && deathSeconds > 0f)
            {
                AnimationClip existing = FindClip(path);

                if (existing != null && existing.frameRate > 0f)
                    fps = existing.frameRate;
            }

            // İskelet dosyanın kendisinde: "@" öncesi ad hangi modele aitse
            // kaynak avatar da o. Yanlış avatar kemikleri tutturamıyor.
            Avatar source = forcedAvatar
                ?? SourceAvatarFor(path, runnerModelName, runnerAvatar, monsterAvatar);
            if (source == null)
            {
                Debug.LogWarning($"{Path.GetFileName(path)} için kaynak avatar bulunamadı, atlandı.");
                continue;
            }

            bool changed = false;

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther
                || importer.sourceAvatar != source)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = source;
                changed = true;
            }

            // Animasyon dosyalarından materyal üretilmesin: klipler skinsiz,
            // üretilen materyaller boşuna klasör kalabalığı olurdu.
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }

            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            ModelImporterClipAnimation[] takes = importer.clipAnimations;

            if (takes == null || takes.Length != defaults.Length)
                takes = defaults;

            for (int i = 0; i < takes.Length; i++)
            {
                if (takes[i].loopTime != loop)
                {
                    takes[i].loopTime = loop;
                    changed = true;
                }

                // **Loop Pose'a DOKUNULMUYOR.** Bir sürümde açılmıştı; sonra
                // ölçüldü ve iki pakette de zaten açık çıktı (`loopBlend: 1`),
                // yani satır hiçbir şey yapmıyordu. Hiçbir şey yapmayan bir
                // satır, ileride onu okuyanı "demek ki bu gerekliymiş" diye
                // yanıltıyor.

                // Aralık HER ZAMAN dosyanın tam aralığından hesaplanıyor,
                // mevcut ayardan değil: kırpma import ayarına yazılıyor ve
                // kalıcı, yoksa her çalıştırmada üst üste binerdi. Araç böylece
                // kendi kendini onarıyor (aynı desen MonsterSetup'ta da var).
                float first = defaults[i].firstFrame;
                float last = isDeath && deathSeconds > 0f
                    ? Mathf.Min(defaults[i].lastFrame, first + deathSeconds * fps)
                    : defaults[i].lastFrame;

                if (!Mathf.Approximately(takes[i].firstFrame, first))
                {
                    takes[i].firstFrame = first;
                    changed = true;
                }

                if (!Mathf.Approximately(takes[i].lastFrame, last))
                {
                    takes[i].lastFrame = last;
                    changed = true;
                }

                // Ölüm klibi kök hareketiyle yere iniyor; applyRootMotion kapalı
                // olduğu için o hareket atılıyor ve kurban havada yatıyor
                // görünüyordu (bkz. ClipRootMotion).
                if (isDeath && ClipRootMotion.BakeVerticalIntoPose(ref takes[i]))
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
    /// Dosya adının `@` öncesi kısmına bakıp kaynak avatarı seçer. Kaçan
    /// modelinin adıyla başlıyorsa kaçanınki, değilse canavarınki — klasördeki
    /// Mixamo klipleri KillerDoll rigiyle indirilmiş.
    /// </summary>
    private static Avatar SourceAvatarFor(string path, string runnerModelName,
        Avatar runnerAvatar, Avatar monsterAvatar)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int at = name.IndexOf('@');
        string prefix = (at > 0 ? name.Substring(0, at) : name).ToLowerInvariant();

        if (prefix == runnerModelName)
            return runnerAvatar;

        return monsterAvatar != null ? monsterAvatar : runnerAvatar;
    }

    /// <summary>
    /// Kaçan klasöründe yoksa canavarınkinden ödünç alır.
    ///
    /// Humanoid klipler kas uzayında saklanıyor, yani hangi avatarla import
    /// edildiklerinden bağımsız olarak her humanoid iskelette oynuyorlar.
    /// `Idle` bugün eksik ve kaçanın hiç kımıldamadan durduğu hâl bu — boş
    /// bırakmak karakteri T-poza düşürürdü.
    /// </summary>
    private static AnimationClip BorrowFromMonster(string key)
    {
        if (!AssetDatabase.IsValidFolder(MonsterAnimationFolder))
            return null;

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MonsterAnimationFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (ClipKey(path) == key)
                return FindClip(path);
        }

        return null;
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

    private static Avatar FindAvatar(string path)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Avatar avatar)
                return avatar;
        }

        return null;
    }

    // ---------- Animator ----------

    /// <summary>
    /// Durum makinesini sıfırdan kurar.
    ///
    /// <code>
    /// Locomotion       (Speed karışımı: idle → yürüme → koşma)
    ///     ↕ Crouch eşiği
    /// CrouchLocomotion (Speed karışımı: eğik idle → eğik yürüme)
    ///
    /// AnyState --Airborne--> Havada --(Airborne biter)--> Locomotion
    /// AnyState --Death-----> Olum   (çıkışı yok, son pozda kalıyor)
    /// </code>
    ///
    /// **Ölüm durumunun çıkışı bilerek yok.** Yeni tur başlayınca gövde kökü
    /// `PlayerBodyVisual` tarafından kapatılıp açılıyor; Animator kapalı bir
    /// objede yeniden aktifleşince varsayılan duruma dönüyor. Ayrı bir "dirildi"
    /// parametresi eklemek aynı işi ikinci kez yapmak olurdu.
    /// </summary>
    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips,
        string controllerPath, string costumeName)
    {
        AnimatorController controller = LoadOrCreateController(controllerPath);

        controller.AddParameter(CharacterAnimatorBase.SpeedParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(CharacterAnimatorBase.SpeedScaleParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(CharacterAnimatorBase.CrouchParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(RunnerAnimator.AirborneParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(RunnerAnimator.DeathTrigger, AnimatorControllerParameterType.Trigger);

        // Yön klipleri VARSA 2B karışım kuruluyor. Yoksa parametreleri hiç
        // eklemiyoruz: `CharacterAnimatorBase` denetleyicide olmayan bir
        // parametreye yazmıyor ve konsol temiz kalıyor.
        AnimationClip walkBack = ByPriority(clips, WalkBackHints, UprightOnly);
        AnimationClip walkLeft = ByPriority(clips, WalkLeftHints, UprightOnly);
        AnimationClip walkRight = ByPriority(clips, WalkRightHints, UprightOnly);
        AnimationClip runLeft = ByPriority(clips, RunLeftHints, UprightOnly);
        AnimationClip runRight = ByPriority(clips, RunRightHints, UprightOnly);

        bool directional = walkBack != null || walkLeft != null || walkRight != null;

        if (directional)
        {
            controller.AddParameter(CharacterAnimatorBase.ForwardParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimatorBase.StrafeParameter, AnimatorControllerParameterType.Float);
        }

        List<AnimationClip> idleBreaks = AllByHints(clips, IdleBreakHints);

        if (idleBreaks.Count > 0)
        {
            controller.AddParameter(CharacterAnimatorBase.IdleBreakTrigger, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(CharacterAnimatorBase.IdleVariantParameter, AnimatorControllerParameterType.Int);
        }

        // Bakış IK'sı olmadan OnAnimatorIK hiç çağrılmıyor; kafa bakış yönüne
        // dönmezdi (CLAUDE.md bölüm 14).
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimationClip idle = ByPriority(clips, IdleHints, UprightOnly) ?? BorrowFromMonster(IdleKey);

        if (idle == null)
            Debug.LogWarning("Kaçan için 'Idle' klibi yok ve canavardan da ödünç alınamadı.");

        AnimationClip walk = ByPriority(clips, WalkHints, UprightOnly);
        AnimationClip run = ByPriority(clips, RunHints, UprightOnly);

        AnimatorState upright = directional
            ? CreateDirectionalState(controller, machine, "Locomotion",
                idle, walk, run, walkBack, walkLeft, walkRight, runLeft, runRight)
            : CreateBlendState(controller, machine, "Locomotion",
                new[] { idle, walk, run },
                new[] { 0f, 1.8f, 6f });

        AnimatorState crouched = CreateBlendState(controller, machine, "CrouchLocomotion",
            new[] { ByPriority(clips, CrouchIdleHints), ByPriority(clips, CrouchWalkHints) },
            new[] { 0f, 2.5f });

        machine.defaultState = upright;

        // Eğilme geçişi. Eşik ortada değil, iki yönde farklı (0.6 / 0.4):
        // tam eşikte titremeyi önlüyor.
        AddCondition(upright.AddTransition(crouched), AnimatorConditionMode.Greater, 0.6f,
            CharacterAnimatorBase.CrouchParameter);
        AddCondition(crouched.AddTransition(upright), AnimatorConditionMode.Less, 0.4f,
            CharacterAnimatorBase.CrouchParameter);

        // Havada olma: canavarda yok, çünkü canavar zıplayamıyor.
        AnimationClip airborneClip = ByPriority(clips, AirborneHints, UprightOnly);

        if (airborneClip != null)
        {
            AnimatorState airborne = CreateClipState(machine, "Havada", airborneClip);

            AnimatorStateTransition toAir = machine.AddAnyStateTransition(airborne);
            toAir.hasExitTime = false;
            toAir.duration = 0.1f;
            toAir.AddCondition(AnimatorConditionMode.If, 0f, RunnerAnimator.AirborneParameter);

            // AnyState kendine geri dönmesin: her karede baştan başlarsa
            // animasyon donuk kalır.
            toAir.canTransitionToSelf = false;

            AnimatorStateTransition toGround = airborne.AddTransition(upright);
            toGround.hasExitTime = false;
            toGround.duration = 0.1f;
            toGround.AddCondition(AnimatorConditionMode.IfNot, 0f, RunnerAnimator.AirborneParameter);
        }

        // Yakalanma. Çıkışı yok — kurbanın bedeni animasyon bitene kadar yerde
        // duruyor, sonra RoundParticipant gövdeyi tamamen kapatıyor.
        BuildIdleBreaks(machine, upright, idleBreaks);

        AnimationClip deathClip = FirstAvailableByHint(clips, DeathHints);

        if (deathClip != null)
        {
            AnimatorState death = CreateClipState(machine, "Olum", deathClip);
            death.speed = DeathSpeed;

            AnimatorStateTransition toDeath = machine.AddAnyStateTransition(death);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.05f;
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, RunnerAnimator.DeathTrigger);
        }
        else
        {
            Debug.LogWarning("Kaçan için yakalanma klibi bulunamadı; ölüm animasyonu kurulmadı.");
        }

        VerifyStates(controller, costumeName);

        // Hangi klibin hangi role düştüğü KONSOLA yazılıyor. Bir rolün yanlış
        // klibe düşmesi ekranda "animasyon tuhaf" olarak görünüyor ve hangi
        // klip olduğunu tahmin etmek zorunda kalmamak gerekiyor.
        Debug.Log($"Kostüm '{costumeName}' animasyonları:\n" +
            $"  boşta: {Name(idle)}\n" +
            $"  yürüme: {Name(walk)}   koşma: {Name(run)}\n" +
            $"  geri: {Name(walkBack)}   sol: {Name(walkLeft)}   sağ: {Name(walkRight)}\n" +
            $"  koşarak sol: {Name(runLeft)}   sağ: {Name(runRight)}\n" +
            $"  havada: {Name(airborneClip)}   ölüm: {Name(deathClip)}\n" +
            $"  eğilme: {Name(ByPriority(clips, CrouchIdleHints))} / {Name(ByPriority(clips, CrouchWalkHints))}\n" +
            $"  esneme varyantı: {idleBreaks.Count}\n" +
            $"  karışım: {(directional ? "yönlü (2B)" : "tek eksenli")}");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// İpucu SIRASINA göre klip arar: önce tam ad, sonra içinde geçen.
    ///
    /// `FirstAvailableByHint` sözlüğü gezip ipuçlarına bakıyor, yani hangi
    /// ipucunun kazandığı sözlüğün sırasına kalıyor. Rol seçiminde sıra
    /// belirleyici olmalı: modele özel ad ortak addan önce gelmeli.
    /// </summary>
    private static AnimationClip ByPriority(Dictionary<string, AnimationClip> clips, string[] hints,
        string[] exclude = null)
    {
        foreach (string hint in hints)
        {
            if (clips.TryGetValue(hint, out AnimationClip exact) && !Excluded(hint, exclude))
                return exact;

            foreach (KeyValuePair<string, AnimationClip> pair in clips)
            {
                if (pair.Key.Contains(hint) && !Excluded(pair.Key, exclude))
                    return pair.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Bu anahtar rolün dışında mı — "içinde geçiyor" aramasının yanlış klibi
    /// yakalamasını engelliyor.
    ///
    /// > **Tam olarak bu yüzden muz adam eğilmiş pozda donuyordu.** Boşta
    /// > durma klibi `idle` ipucuyla aranıyor ve bizim klasörümüzde `idle`
    /// > diye bir klip YOK (canavarınki ödünç alınıyor, bölüm 17). Ama
    /// > `crouching idle` "idle" içeriyor: karakter dururken eğilme klibini
    /// > oynuyordu.
    /// >
    /// > Aynı tuzak yürümede de vardı: `crouched walking` "walking" içeriyor.
    /// >
    /// > Ders: **"içinde geçiyor" araması tek başına bir eşleştirme kuralı
    /// > değil.** Roller birbirinin adını kapsıyorsa neyin DIŞARIDA kalacağını
    /// > da söylemek gerekiyor.
    /// </summary>
    private static bool Excluded(string key, string[] exclude)
    {
        if (exclude == null)
            return false;

        foreach (string word in exclude)
        {
            if (key.Contains(word))
                return true;
        }

        return false;
    }

    /// <summary>İpuçlarının HEPSİNİ karşılayan klipleri sırayla toplar.</summary>
    private static List<AnimationClip> AllByHints(Dictionary<string, AnimationClip> clips, string[] hints)
    {
        List<AnimationClip> found = new List<AnimationClip>();

        foreach (string hint in hints)
        {
            AnimationClip clip = ByPriority(clips, new[] { hint });

            if (clip != null && !found.Contains(clip))
                found.Add(clip);
        }

        return found;
    }

    /// <summary>Bu klip döngüye alınmalı mı — ada göre, tam eşleşmeye değil.</summary>
    private static bool IsLoopingKey(string key)
    {
        if (IsDeathKey(key))
            return false;

        foreach (string hint in LoopingHints)
        {
            if (key.Contains(hint))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Yönlü yürüme/koşma karışımı: ileri, geri, sağa, sola ayrı klipler.
    ///
    /// Tek eksenli ağaç yalnızca HIZI biliyor, yani 2 m/s ileri gitmekle geri
    /// gitmek onun için aynı şey. Yön klibi olan bir pakette bu, geri giderken
    /// ileri yürüyen bir karakter demek.
    ///
    /// > **Serbest YÖNLÜ ağaç kullanılamıyor ve bu T-poza sebep oluyordu.**
    /// > Yönlü karışım her yönde TEK örnek bekliyor; bizde ileri yönünde iki
    /// > tane var (yürüme 0.45'te, koşma 1'de) ve yanlarda da öyle. Aynı yönde
    /// > iki örnek olunca ağırlıklar toplamı 1 etmiyor ve karakter kısa
    /// > aralıklarla hiçbir klibin sürmediği hâle, yani T-poza düşüyor.
    /// >
    /// > `FreeformCartesian2D` konumları düz koordinat olarak okuyor: aynı yön
    /// > üstünde farklı büyüklükte örnekler tam da onun çözdüğü durum.
    /// >
    /// > Oynanınca "hepsinde bir saniyeliğine T-poza geçiyor" diye bildirildi
    /// > ve önce döngü ayarı sanıldı — klipler zaten döngülüydü, bozuk olan
    /// > ağacın türüydü.
    ///
    /// Eşikler 0.45 ve 1: yürüme hızı koşunun kabaca yarısı, yani
    /// `CharacterAnimatorBase` değerleri koşuya böldüğünde yürüme oraya
    /// düşüyor. Geri ve yan klipleri yalnızca yürüyüş hızında var, o yüzden
    /// koşarken yana giderken yürüme klibi hızlanarak oynuyor — ayak kayması
    /// oluyor ama yanlış yöne bakan bir karakterden iyi.
    /// </summary>
    private static AnimatorState CreateDirectionalState(AnimatorController controller,
        AnimatorStateMachine machine, string name, AnimationClip idle, AnimationClip walk,
        AnimationClip run, AnimationClip walkBack, AnimationClip walkLeft, AnimationClip walkRight,
        AnimationClip runLeft, AnimationClip runRight)
    {
        BlendTree tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = CharacterAnimatorBase.StrafeParameter,
            blendParameterY = CharacterAnimatorBase.ForwardParameter,
        };

        AssetDatabase.AddObjectToAsset(tree, controller);

        // Merkez şart: sıfıra yakın değerlerde oynatılacak bir şey olmalı,
        // yoksa karakter durduğu anda T-poza düşüyor.
        if (idle != null)
            tree.AddChild(idle, new Vector2(0f, 0f));

        // Konumlar METRE/SANİYE. Tek eksenli ağacın eşikleriyle aynı dil:
        // yürüme 1.8, koşma 6. Karakterin gerçek hızları 3.81 ve 7.62 olduğu
        // için ikisi de kendi örneğine oturuyor.
        AddDirectional(tree, walk, new Vector2(0f, 1.8f));
        AddDirectional(tree, run, new Vector2(0f, 6f));
        AddDirectional(tree, walkBack, new Vector2(0f, -1.8f));
        AddDirectional(tree, walkLeft, new Vector2(-1.8f, 0f));
        AddDirectional(tree, walkRight, new Vector2(1.8f, 0f));
        AddDirectional(tree, runLeft, new Vector2(-6f, 0f));
        AddDirectional(tree, runRight, new Vector2(6f, 0f));

        AnimatorState state = machine.AddState(name);
        state.motion = tree;

        // Oynatma hızı BURADA ölçeklenmiyor. Tek eksenli ağaçta hız
        // parametresi ayak kaymasını azaltıyordu; burada karışımın kendisi
        // zaten hıza göre klip seçiyor ve üstüne bir de hızlandırmak yürüyüşü
        // tuhaf hâle getiriyor.
        return state;
    }

    /// <summary>
    /// Hareketi olmayan durum var mı diye bakıyor ve varsa **hata** yazıyor.
    ///
    /// Boş hareketli bir durum ekranda T-poz demek ve hiçbir yerde kendiliğinden
    /// hata vermiyor: animatör sessizce hiçbir şey oynatmıyor. Bir kez
    /// yaşandı ve sebebi bulmak bir tur sürdü; artık araç söylüyor.
    /// </summary>
    /// <summary>
    /// Denetleyiciyi var olan dosyanın ÜSTÜNE kuruyor; yoksa yenisini açıyor.
    ///
    /// > **Eskiden dosya silinip yeniden yaratılıyordu ve bu sahneyi sessizce
    /// > bozuyordu.** Silinen varlığın GUID'i de gidiyor, yani menü
    /// > sahnesindeki figürlerin ve prefabın denetleyici referansları **kopuk**
    /// > kalıyordu. Denetleyicisi olmayan bir `Animator` hiçbir şey oynatmıyor:
    /// > ekranda T-poz. Hiçbir yerde hata yazmıyor, çünkü Unity için "boş
    /// > referans" geçerli bir durum.
    /// >
    /// > Oynanınca "karakter seçim ekranında T-poz ile duruyor" diye
    /// > bildirildi ve önce animasyonlarda sanıldı — klipler de denetleyici de
    /// > doğruydu, kopuk olan aradaki bağdı.
    /// >
    /// > Bu, aracın **iki kez çalıştırılamaz** olması demekti: `Menü Kur`'u
    /// > her seferinde arkasından çalıştırmayı unutmak bozulmaya yetiyordu.
    /// > GUID korununca sıra da önemini yitiriyor.
    ///
    /// Varlığın İÇİ temizleniyor: alt varlıklar (durumlar, ağaçlar, geçişler)
    /// ayrı nesneler ve silinmezlerse dosyada birikip büyürler.
    /// </summary>
    private static AnimatorController LoadOrCreateController(string path)
    {
        AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

        if (existing == null)
            return AnimatorController.CreateAnimatorControllerAtPath(path);

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset != existing && asset != null)
                Object.DestroyImmediate(asset, true);
        }

        existing.parameters = new AnimatorControllerParameter[0];

        AnimatorStateMachine machine = new AnimatorStateMachine
        {
            name = "Base Layer",
            hideFlags = HideFlags.HideInHierarchy,
        };

        AssetDatabase.AddObjectToAsset(machine, existing);

        existing.layers = new[]
        {
            new AnimatorControllerLayer
            {
                name = "Base Layer",
                defaultWeight = 1f,
                stateMachine = machine,
            }
        };

        return existing;
    }

    private static string Name(Motion motion) => motion != null ? motion.name : "YOK";

    private static void VerifyStates(AnimatorController controller, string costumeName)
    {
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            AnimatorState state = child.state;

            if (state.motion == null)
            {
                Debug.LogError($"Kostüm '{costumeName}': '{state.name}' durumunun klibi YOK. " +
                    "O duruma geçen karakter T-poza düşer.");
                continue;
            }

            if (state.motion is BlendTree tree && tree.children.Length == 0)
            {
                Debug.LogError($"Kostüm '{costumeName}': '{state.name}' karışım ağacı BOŞ. " +
                    "O duruma geçen karakter T-poza düşer.");
            }
        }
    }

    private static void AddDirectional(BlendTree tree, AnimationClip clip, Vector2 position)
    {
        if (clip != null)
            tree.AddChild(clip, position);
    }

    /// <summary>
    /// Boşta kalma kırılımları: uzun süre kıpırdamayınca esneme/gerinme.
    ///
    /// Her varyant ayrı bir durum ve girişi `IdleVariant` sayısıyla seçiliyor:
    /// Unity'nin geçişlerinde rastgelelik yok, o yüzden hangisinin oynayacağını
    /// kod söylüyor (bkz. CharacterAnimatorBase.UpdateIdleBreak).
    ///
    /// Her durumun İKİ çıkışı var: klip bitince normal dönüş, ve oyuncu
    /// hareket ederse anında dönüş. İkincisi olmasaydı esneme ortasında
    /// yürümeye başlayan karakter bir saniye boyunca yerinde esnemeye devam
    /// ederdi.
    /// </summary>
    private static void BuildIdleBreaks(AnimatorStateMachine machine, AnimatorState upright,
        List<AnimationClip> breaks)
    {
        for (int i = 0; i < breaks.Count; i++)
        {
            AnimatorState state = CreateClipState(machine, $"Bosta_{i}", breaks[i]);

            AnimatorStateTransition enter = upright.AddTransition(state);
            enter.hasExitTime = false;
            enter.duration = 0.35f;
            enter.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimatorBase.IdleBreakTrigger);

            // SONUNCU durum, aralığın dışında kalan her sayıyı da yakalıyor.
            //
            // > **Tüketilmeyen bir tetik Unity'de ASILI KALIYOR.** Kod
            // > varyantı kendi sayısına göre seçiyordu ve o sayı denetleyicide
            // > kurulandan fazla olabiliyordu (iki ipucu aynı klibe düşerse
            // > durum sayısı azalıyor). Eşleşen geçiş bulunmayınca tetik
            // > sönmüyor, oyuncu yürümeye başlayınca ilk fırsatta ateşliyor ve
            // > karakter yürürken esniyor — "kolları havaya kalkıyor" diye
            // > bildirilen şey buydu.
            // >
            // > Son durumu "bundan büyük veya eşit" yapmak her sayıyı bir
            // > geçişe bağlıyor, yani tetik her zaman tüketiliyor.
            if (i == breaks.Count - 1)
            {
                enter.AddCondition(AnimatorConditionMode.Greater, i - 1,
                    CharacterAnimatorBase.IdleVariantParameter);
            }
            else
            {
                enter.AddCondition(AnimatorConditionMode.Equals, i,
                    CharacterAnimatorBase.IdleVariantParameter);
            }

            AnimatorStateTransition done = state.AddTransition(upright);
            done.hasExitTime = true;
            done.exitTime = 0.92f;
            done.duration = 0.35f;

            AnimatorStateTransition interrupt = state.AddTransition(upright);
            interrupt.hasExitTime = false;
            interrupt.duration = 0.12f;
            interrupt.AddCondition(AnimatorConditionMode.Greater, 0.4f,
                CharacterAnimatorBase.SpeedParameter);
        }
    }

    private static AnimatorState CreateBlendState(AnimatorController controller,
        AnimatorStateMachine machine, string name, AnimationClip[] motions, float[] thresholds)
    {
        BlendTree tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = CharacterAnimatorBase.SpeedParameter,
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

        // Oynatma hızı parametreden: ayaklar yerde kaymasın diye RunnerAnimator
        // gerçek hıza göre ölçekliyor.
        state.speedParameterActive = true;
        state.speedParameter = CharacterAnimatorBase.SpeedScaleParameter;

        return state;
    }

    private static AnimatorState CreateClipState(AnimatorStateMachine machine, string name,
        AnimationClip clip)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;
        return state;
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

        Debug.LogWarning($"Kaçan animasyonu eksik: '{key}'. O durum boş kalacak.");
        return null;
    }

    /// <summary>Sırayla dener, ilk bulduğunu döndürür — uyarı yazmadan.</summary>
    /// <summary>Bu klip yakalanma klibi mi — ad ipuçlarına bakıyor.</summary>
    private static bool IsDeathKey(string key)
    {
        foreach (string hint in DeathHints)
        {
            if (key.Contains(hint))
                return true;
        }

        return false;
    }

    /// <summary>Anahtarın içinde geçen ipuçlarına göre arar (uzun Türkçe adlar için).</summary>
    private static AnimationClip FirstAvailableByHint(Dictionary<string, AnimationClip> clips,
        string[] hints)
    {
        foreach (KeyValuePair<string, AnimationClip> pair in clips)
        {
            foreach (string hint in hints)
            {
                if (pair.Key.Contains(hint))
                    return pair.Value;
            }
        }

        return null;
    }

    private static string DescribeClips(Dictionary<string, AnimationClip> clips)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();

        foreach (KeyValuePair<string, AnimationClip> pair in clips)
            text.AppendLine($"    {pair.Key}: {pair.Value.length:0.00} sn");

        return text.ToString();
    }

    // ---------- Materyal ----------

    /// <summary>
    /// Üç sessiz materyal sorununu kapatıyor:
    ///
    /// 1. **Materyal modele hiç bağlı olmayabiliyor.** Banana Man'de olan buydu:
    ///    `Body.mat` doğru dokuya sahipti ama FBX materyal yuvası boştu
    ///    (`externalObjects: {}`) ve Unity varsayılan **gri** materyali
    ///    kullanıyordu — hiçbir yerde hata yazmadan.
    /// 2. **URP shader'ı** projede yok, model pembe görünür.
    /// 3. **URP albedosu** Standard'a taşınmaz (`_BaseMap` → `_MainTex`),
    ///    model düz gri kalır.
    ///
    /// Bkz. ModelMaterialFix.
    /// </summary>
    private static int FixMaterials()
    {
        int count = 0;

        // HER kostüm modeli geziliyor, yalnızca birincisi değil: yeni bir
        // kostüm de aynı üç tuzağa düşebilir ve "model düz gri" şikâyeti
        // hangi modelden geldiğini söylemiyor.
        foreach (CharacterCatalog.Costume costume in CharacterCatalog.Runners)
            count += FixModelMaterials(costume.ModelPath);

        return count;
    }

    private static int FixModelMaterials(string modelPath)
    {
        int count = ModelMaterialFix.RemapExternalMaterials(modelPath);

        // Remap SaveAndReimport çağırıyor: model referansı bayatlıyor.
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
            return count;

        Shader standard = Shader.Find("Standard");
        List<Material> materials = new List<Material>();

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || materials.Contains(material))
                    continue;

                materials.Add(material);

                if (standard == null || material.shader == null)
                    continue;

                if (ConvertToStandard(material, standard))
                    count++;
            }
        }

        count += ModelMaterialFix.FillBuiltinSlots(materials, "Kaçan Modelini Kur");
        return count;
    }

    // ---------- Prefab ----------

    private static string AttachToPlayerPrefab(List<AnimatorController> controllers,
        Dictionary<string, AnimationClip> clips, float deathSeconds)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            return "UYARI: oyuncu prefabı yok, model bağlanmadı (önce Ağ Kurulumu).";

        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            CharacterController capsule = contents.GetComponent<CharacterController>();
            if (capsule == null)
                return "UYARI: prefabta CharacterController yok, model bağlanmadı.";

            // Eski gövdeler siliniyor: hem tek gövdeli sürümün adı
            // (`KacanGovde`) hem kostüm adları (`KacanGovde_0`). Ad değiştiği
            // için eskisini tek adla aramak yetmiyordu ve eski gövde prefabta
            // asılı kalırdı.
            for (int i = contents.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = contents.transform.GetChild(i);

                if (child.name == RunnerRootName || child.name.StartsWith(RunnerRootName + "_"))
                    Object.DestroyImmediate(child.gameObject);
            }

            List<GameObject> bodies = new List<GameObject>();
            List<Animator> animators = new List<Animator>();

            for (int i = 0; i < CharacterCatalog.Runners.Length; i++)
            {
                CharacterCatalog.Costume costume = CharacterCatalog.Runners[i];
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(costume.ModelPath);

                if (source == null)
                {
                    Debug.LogWarning($"Kostüm '{costume.Name}': {costume.ModelPath} bulunamadı, " +
                        "gövde kurulmadı. Seçim ekranında görünür ama görsel karşılığı olmaz.");
                    continue;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, contents.transform);
                instance.name = $"{RunnerRootName}_{i}";

                // Ölçek tahmin edilmiyor, ÖLÇÜLÜYOR: gerçek renderer sınırları
                // okunup hull yüksekliğine oranlanıyor.
                //
                // HER kostüm aynı hull boyuna çekiliyor. Model kendi içinde
                // kısa ya da uzun olabilir, ekrandaki boyu aynı: bölüm 17'nin
                // kuralı gereği görünen gövde çarpışma kutusuyla örtüşmeli,
                // yoksa isabet etmesi gereken vuruşlar ıskalıyor. Aynı şey
                // kamerayı da yerinde tutuyor — göz hizası hull'dan geliyor,
                // yani kısa bir modelde kamera kafanın üstünde kalmıyor.
                float scale = ResolveScale(instance, capsule.height) * ExtraScale;
                instance.transform.localScale = Vector3.one * scale;

                // Ayaklar hull'un tabanına: kök objenin merkezi controller.center'da,
                // taban ondan height/2 aşağıda.
                instance.transform.localPosition = capsule.center + Vector3.down * (capsule.height / 2f);
                instance.transform.localRotation = Quaternion.identity;

                Animator animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();

                // Her kostümün KENDİ denetleyicisi: biri yön klipleriyle 2B
                // karışım kullanıyor, öbürü tek eksenli. Tek denetleyici
                // paylaşsalardı yön parametreleri olmayan modelde ağaç boş
                // kalırdı.
                animator.runtimeAnimatorController = i < controllers.Count ? controllers[i] : null;
                animator.applyRootMotion = false; // hareketi PlayerController veriyor
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                LayerSetup.Apply(instance, LayerSetup.Oyuncu);

                bodies.Add(instance);
                animators.Add(animator);

                // Sıfırıncı dışındakiler KAPALI kuruluyor. Prefabta hepsi açık
                // kalsaydı üst üste binmiş modeller görünür ve prefabı açan
                // herkes "bozuk" diye okurdu. Çalışma anında PlayerBodyVisual
                // zaten seçiliyi açıyor.
            }

            if (bodies.Count == 0)
                return "UYARI: hiçbir kostüm modeli bulunamadı, gövde kurulmadı.";

            WireComponents(contents, bodies, animators, clips, deathSeconds);

            // Gövdeler ANCAK kablolama bittikten sonra kapanıyor. Kapalı bir
            // `Animator`'da `GetBoneTransform` null dönüyor (bölüm 21.1'deki
            // yedi tuzaktan biri) — önce kapatsaydık ikinci kostümün kafa
            // kemiği boş kalır ve birinci şahısta kafası gizlenmezdi.
            //
            // Sıfırıncı dışındakiler prefabta kapalı duruyor: hepsi açık
            // kalsaydı üst üste binmiş modeller görünür ve prefabı açan herkes
            // "bozuk" diye okurdu. Çalışma anında PlayerBodyVisual seçiliyi
            // zaten açıyor.
            for (int i = 0; i < bodies.Count; i++)
                bodies[i].SetActive(i == 0);

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            return $"{bodies.Count} kostüm gövdesi prefaba bağlandı.";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// Kaçan modelini oyuncu prefabı DIŞINDA, verilen bir sahne objesine bağlar
    /// — `AttachToPlayerPrefab`'ın prefab olmayan hâli. `TestBotSetup` botun
    /// görsel eksikliğini gidermek için kullanıyor (CLAUDE.md bölüm 17'deki
    /// "test botu ölüm animasyonunu gösteremiyor, botta model yok" sınırı).
    ///
    /// **Animasyonları ve Animator Controller'ı yeniden KURMUYOR** — bu araç
    /// yalnızca `Kaçan Modelini Kur`'un önceden ürettiği `Kacan.controller`
    /// varlığını olduğu gibi kullanıyor. O yüzden en az bir kez `Kaçan
    /// Modelini Kur` çalıştırılmış olmalı; değilse anlaşılır bir uyarıyla
    /// çıkıyor, sessizce yarım kalmıyor.
    ///
    /// `WireComponents`'e boş bir klip sözlüğü geçiyor: `deathHoldDuration` bu
    /// yüzden GÜNCELLENMİYOR (metod `hold > 0f` değilse alana dokunmuyor) —
    /// bilerek, çünkü bot ölüm koreografisini zaten göstermiyor
    /// (`PlayerBodyVisual.ApplyDeathPose` çağrılmıyor, `BeginDeathHold` sadece
    /// `runnerAnimator` null değilse çalışıyor ve o artık dolu olacak, ama
    /// koreografi hâlâ canavarla eşleşmeyecek). Botun kazandığı şey yalnızca
    /// LOKOMOSYON: yürüme/koşma/zıplama — asıl istenen de bu, hareket
    /// mekaniklerini gerçek bir modelde izleyebilmek.
    /// </summary>
    internal static string AttachToSceneObject(GameObject root)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            return $"UYARI: {ControllerPath} yok — önce Yakalamaca > Kaçan Modelini Kur çalıştır.";

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            return $"UYARI: {ModelPath} bulunamadı.";

        CharacterController capsule = root.GetComponent<CharacterController>();
        if (capsule == null)
            return "UYARI: objede CharacterController yok, model bağlanmadı.";

        Transform existing = root.transform.Find(RunnerRootName);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        instance.name = RunnerRootName;

        float scale = ResolveScale(instance, capsule.height) * ExtraScale;
        instance.transform.localScale = Vector3.one * scale;
        instance.transform.localPosition = capsule.center + Vector3.down * (capsule.height / 2f);
        instance.transform.localRotation = Quaternion.identity;

        Animator animator = instance.GetComponentInChildren<Animator>();
        if (animator == null)
            animator = instance.AddComponent<Animator>();

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        // Bot yalnızca BİRİNCİ kostümü giyiyor: işi kadroyu doldurmak, kaçanı
        // taklit etmek değil (bölüm 17). Kostüm seçimi bir oyuncu tercihi ve
        // botun tercihi yok.
        WireComponents(root, new List<GameObject> { instance }, new List<Animator> { animator },
            new Dictionary<string, AnimationClip>(), 0f);

        LayerSetup.Apply(instance, LayerSetup.Oyuncu);

        return $"Model bağlandı (ölçek {scale:0.###}).";
    }

    /// <summary>
    /// Kostüm materyalini Built-in `Standard`'a çevirir. Zaten Standard ise
    /// hiçbir şey yapmıyor ve `false` dönüyor.
    ///
    /// ### Neden ÖZEL shader'lar da çevriliyor, yalnızca bozuk olanlar değil
    ///
    /// Built-in ileri işlemede **nokta ve spot ışıkları `ForwardAdd`
    /// geçişinden** geliyor. `ForwardBase`'i olup `ForwardAdd`'i olmayan bir
    /// shader yalnızca ortam ışığını ve ana yönlü ışığı görüyor.
    ///
    /// Bu oyunda ortam 0.006, yönlü ışık 0.05 ve **fener bir spot** (bölüm 5).
    /// Yani öyle bir shader taşıyan karakter fenerin altında bile simsiyah
    /// kalıyor — Unity-chan'ın toon shader'ları tam olarak böyle ve oynanınca
    /// "menüde simsiyah gözüküyor" diye bildirildi. Menüde görünen şey aslında
    /// oyunda da olacak bir sorundu: karakteri aydınlatacak hiçbir ışığımız
    /// onun shader'ına ulaşmıyor.
    ///
    /// Alternatif shader'a `ForwardAdd` geçişi eklemekti; o bir üçüncü parti
    /// yaması olurdu ve paket güncellenince kaybolurdu (bölüm 9). Materyali
    /// çevirmek hem kalıcı hem de karakteri oyunun geri kalanıyla aynı
    /// aydınlatmaya sokuyor.
    ///
    /// **Bedeli toon görünümün ve dış çizginin gitmesi.** Bu oyunda kazanç:
    /// her şey gerçekçi gölgelendirmeyle çiziliyor ve sahne tamamen fenerle
    /// aydınlanıyor.
    /// </summary>
    private static bool ConvertToStandard(Material material, Shader standard)
    {
        string shaderName = material.shader.name;

        bool alreadyLit = shaderName == "Standard"
            || shaderName == "Standard (Specular setup)"
            || shaderName.StartsWith("Legacy Shaders/");

        if (alreadyLit)
            return false;

        // Dokular dönüşümden ÖNCE okunuyor: shader değişince `HasProperty`
        // eski slotları görmez olur ve elimizde kalan tek referans kaybolurdu
        // (bölüm 14'teki `_BaseMap` tuzağının aynısı).
        Texture albedo = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;

        Texture normal = material.HasProperty("_NormalMapSampler")
            ? material.GetTexture("_NormalMapSampler")
            : material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;

        Color tint = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;

        // Saydam olanlar adından ve kuyruğundan anlaşılıyor: kirpik, göz ve
        // yanak allığı yüzün üstüne karışıyor. Opak çevrilirlerse yüze siyah
        // dikdörtgenler olarak biner.
        bool blended = shaderName.ToLowerInvariant().Contains("blend")
            || material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent;

        material.shader = standard;

        if (albedo != null)
            material.SetTexture("_MainTex", albedo);

        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }

        material.SetColor("_Color", tint);

        // Toon bir karakter parlak olmamalı; varsayılan Standard fazla cilalı.
        material.SetFloat("_Glossiness", 0.12f);
        material.SetFloat("_Metallic", 0f);

        if (blended)
            ApplyFadeMode(material);

        EditorUtility.SetDirty(material);
        return true;
    }

    /// <summary>
    /// Standard'ı saydam (Fade) moduna alır. Inspector'daki açılır listenin
    /// yaptığı şeyin kodla karşılığı: mod, harmanlama, derinlik yazımı,
    /// anahtar kelimeler ve kuyruk birlikte değişmek zorunda — biri eksik
    /// kalırsa materyal ya opak kalır ya da yanlış sırada çizilir.
    /// </summary>
    private static void ApplyFadeMode(Material material)
    {
        material.SetFloat("_Mode", 2f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

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
    /// Kurulan gövdeleri `PlayerBodyVisual`'a ve tur sistemine bağlar.
    ///
    /// Gövdeler bir DİZİ ve sırası `CharacterCatalog.Runners` ile aynı: kostüm
    /// indeksi doğrudan gövde indeksi oluyor, yani eşlemeyi tutan üçüncü bir
    /// sayı yok (bkz. o dosyadaki not).
    /// </summary>
    private static void WireComponents(GameObject root, List<GameObject> bodies,
        List<Animator> animators, Dictionary<string, AnimationClip> clips, float deathSeconds)
    {
        PlayerBodyVisual visual = root.GetComponent<PlayerBodyVisual>()
            ?? root.AddComponent<PlayerBodyVisual>();

        RunnerAnimator runnerAnimator = root.GetComponent<RunnerAnimator>()
            ?? root.AddComponent<RunnerAnimator>();

        SerializedObject serializedVisual = new SerializedObject(visual);

        SerializedProperty bodyArray = serializedVisual.FindProperty("runnerBodies");
        bodyArray.arraySize = bodies.Count;

        for (int i = 0; i < bodies.Count; i++)
        {
            SerializedProperty element = bodyArray.GetArrayElementAtIndex(i);
            Animator bodyAnimator = animators[i];

            element.FindPropertyRelative("root").objectReferenceValue = bodies[i];
            element.FindPropertyRelative("animator").objectReferenceValue = bodyAnimator;

            element.FindPropertyRelative("headBone").objectReferenceValue =
                bodyAnimator != null ? bodyAnimator.GetBoneTransform(HumanBodyBones.Head) : null;

            Renderer[] renderers = bodies[i].GetComponentsInChildren<Renderer>(true);
            SerializedProperty rendererArray = element.FindPropertyRelative("renderers");
            rendererArray.arraySize = renderers.Length;

            for (int r = 0; r < renderers.Length; r++)
                rendererArray.GetArrayElementAtIndex(r).objectReferenceValue = renderers[r];

            List<Renderer> headParts = new List<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (IsHeadPart(renderer))
                    headParts.Add(renderer);
            }

            SerializedProperty headArray = element.FindPropertyRelative("headRenderers");
            headArray.arraySize = headParts.Count;

            for (int h = 0; h < headParts.Count; h++)
                headArray.GetArrayElementAtIndex(h).objectReferenceValue = headParts[h];
        }

        // Kostüm değişince animatörü yeniden yönlendirecek olan bileşen.
        serializedVisual.FindProperty("runnerAnimator").objectReferenceValue = runnerAnimator;

        // Ölüm pozunda kurbanı canavarın ölçeğine çıkaran oran. İki modelin
        // ikisi de hull boyuna normalleniyor, üstüne kendi ExtraScale'i
        // geliyor — yani ekrandaki boy oranı sadece bu ikisinin oranı. Kostüm
        // sayısından bağımsız: hepsi aynı hull boyunda.
        SerializedProperty scaleMatch = serializedVisual.FindProperty("deathScaleMatch");
        if (scaleMatch != null)
            scaleMatch.floatValue = MonsterSetup.ExtraScale / ExtraScale;

        serializedVisual.ApplyModifiedProperties();

        SerializedObject serializedAnimator = new SerializedObject(runnerAnimator);
        // Başlangıçta sıfırıncı kostüm sürülüyor; kostüm değişince
        // `PlayerBodyVisual` bunu yeniden yönlendiriyor.
        serializedAnimator.FindProperty("animator").objectReferenceValue =
            animators.Count > 0 ? animators[0] : null;
        serializedAnimator.FindProperty("controller").objectReferenceValue =
            root.GetComponent<PlayerController>();

        Camera playerCamera = root.GetComponentInChildren<Camera>(true);
        serializedAnimator.FindProperty("lookSource").objectReferenceValue =
            playerCamera != null ? playerCamera.transform : null;

        serializedAnimator.ApplyModifiedProperties();

        // Tur sistemi ölüm animasyonunu buradan sürüyor ve bedeni klip bitene
        // kadar sahnede tutuyor. Süre klipten ÖLÇÜLÜYOR: animasyonu
        // değiştirip aracı tekrar çalıştırınca bekleme de güncelleniyor.
        RoundParticipant participant = root.GetComponent<RoundParticipant>();
        if (participant == null)
            return;

        SerializedObject serializedParticipant = new SerializedObject(participant);
        serializedParticipant.FindProperty("bodyVisual").objectReferenceValue = visual;

        SerializedProperty animatorField = serializedParticipant.FindProperty("runnerAnimator");
        if (animatorField != null)
            animatorField.objectReferenceValue = runnerAnimator;

        // Beden canavarın yakalama animasyonu boyunca sahnede kalmalı: kısa
        // olursa canavar havayı yumruklar, uzun olursa ceset öylece bekler.
        //
        // **`DeathSpeed`'e BÖLÜNMÜYOR, bilerek.** Ölüm klibi hızlandırıldığı
        // için kurban yere daha erken iniyor, ama beden yine canavarın klibi
        // bitene kadar durmalı — aradaki farkta kurban yerde yatıyor, canavar
        // yumruklamayı bitiriyor. Süreyi de kısaltmak cesedi canavarın altından
        // çekip alırdı.
        SerializedProperty holdField = serializedParticipant.FindProperty("deathHoldDuration");

        if (holdField != null)
        {
            AnimationClip death = FirstAvailableByHint(clips, DeathHints);

            float hold = deathSeconds > 0f
                ? deathSeconds
                : death != null ? death.length : 0f;

            if (hold > 0f)
                holdField.floatValue = hold;
        }

        // Mixamo eşli animasyonu iki karakteri de aynı noktada varsayıyor.
        SerializedProperty offsetField = serializedParticipant.FindProperty("deathForwardOffset");

        if (offsetField != null)
            offsetField.floatValue = 0f;

        serializedParticipant.ApplyModifiedProperties();
    }

    /// <summary>
    /// Bu renderer kafaya mı ait. Birinci şahısta kafa kemiği sıfıra
    /// ölçekleniyor ama kendi iskeletine bağlı ayrı mesh'ler (gözler) bundan
    /// etkilenmiyor; onlar doğrudan kapatılıyor.
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
            // "hair"/"sac" da listede: bazı modellerde saç ayrı bir
            // SkinnedMeshRenderer ve kendi iskeletine bağlı, yani kafa
            // kemiğini sıfırlamak onu toplamıyor. Birinci şahısta havada duran
            // bir saç kalırdı.
            return lower.Contains("eye") || lower.Contains("head") || lower.Contains("goz")
                || lower.Contains("hair") || lower.Contains("sac");
        }
    }
}
