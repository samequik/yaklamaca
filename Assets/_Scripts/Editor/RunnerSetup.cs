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
    private const string AnimationFolder = "Assets/_Art/Models/Kacan";
    private const string MonsterAnimationFolder = "Assets/_Art/Models/Canavar";
    private const string ControllerPath = AnimationFolder + "/Kacan.controller";
    private const string PlayerPrefabPath = "Assets/_Prefabs/NetworkPlayer.prefab";

    private const string ModelPath =
        "Assets/Plugins/Banana Yellow Games/Characters/Banana Man/Banana Man.fbx";

    private const string MonsterModelPath =
        "Assets/RamsterZ_FreeDoll/Art/Models/KillerDollUnity_BaseBody.fbx";

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

    // Klip anahtarları: dosya adında "@" sonrası kısım, küçük harf.
    private const string IdleKey = "idle";
    private const string WalkKey = "walking";
    private const string RunKey = "running";
    private const string CrouchIdleKey = "crouching idle";
    private const string CrouchWalkKey = "crouched walking";

    /// <summary>Havada olma klibi. `falling idle` varsa tercih ediliyor: gerçek
    /// bir döngü. Yoksa `jumping` kullanılıyor.</summary>
    private static readonly string[] AirborneKeys = { "falling idle", "falling", "jumping" };

    /// <summary>Yakalanma klibi. Ad uzun ve Türkçe, o yüzden birebir değil
    /// "içeriyor mu" diye aranıyor.</summary>
    private static readonly string[] DeathHints = { "takedown", "ölme", "olme", "death", "dying" };

    private static readonly string[] LoopingClips =
    {
        IdleKey, WalkKey, RunKey, CrouchIdleKey, CrouchWalkKey,
        "falling idle", "falling", "jumping"
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

        Dictionary<string, AnimationClip> clips = ImportAnimations(target, deathSeconds);

        AnimatorController controller = BuildController(clips);
        string prefabReport = AttachToPlayerPrefab(model, controller, clips, deathSeconds);

        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Kaçan modeli kuruldu",
            $"· {clips.Count} animasyon içe aktarıldı ve Humanoid'e çevrildi\n" +
            (materials > 0 ? $"· {materials} materyal onarıldı (bağlantı + albedo)\n" : "") +
            (deathSeconds > 0f ? $"· Ölüm klibi {deathSeconds:0.00} sn'ye kırpıldı (canavarın yakalama süresi)\n" : "") +
            $"· Animator: {ControllerPath}\n" +
            $"· {prefabReport}\n\n" +
            "Klipler:\n" + DescribeClips(clips) +
            "\nTEST: Play → Host → [2] kaçan olarak başlat → üçüncü şahıs görmek " +
            "için [3] ile kendini elendir.",
            "Tamam");
    }

    // ---------- Animasyonlar ----------

    private static Dictionary<string, AnimationClip> ImportAnimations(Avatar runnerAvatar,
        float deathSeconds)
    {
        Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();

        if (!AssetDatabase.IsValidFolder(AnimationFolder))
        {
            Debug.LogWarning($"{AnimationFolder} yok — animasyon bulunamadı.");
            return result;
        }

        Avatar monsterAvatar = FindAvatar(MonsterModelPath);
        string runnerModelName = Path.GetFileNameWithoutExtension(ModelPath).ToLowerInvariant();

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                continue;

            string key = ClipKey(path);
            bool loop = System.Array.IndexOf(LoopingClips, key) >= 0;
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
            Avatar source = SourceAvatarFor(path, runnerModelName, runnerAvatar, monsterAvatar);
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
    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        controller.AddParameter(CharacterAnimatorBase.SpeedParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(CharacterAnimatorBase.SpeedScaleParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(CharacterAnimatorBase.CrouchParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(RunnerAnimator.AirborneParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(RunnerAnimator.DeathTrigger, AnimatorControllerParameterType.Trigger);

        // Bakış IK'sı olmadan OnAnimatorIK hiç çağrılmıyor; kafa bakış yönüne
        // dönmezdi (CLAUDE.md bölüm 14).
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimationClip idle = Clip(clips, IdleKey) ?? BorrowFromMonster(IdleKey);

        if (idle == null)
            Debug.LogWarning("Kaçan için 'Idle' klibi yok ve canavardan da ödünç alınamadı.");

        AnimatorState upright = CreateBlendState(controller, machine, "Locomotion",
            new[] { idle, Clip(clips, WalkKey), Clip(clips, RunKey) },
            new[] { 0f, 1.8f, 6f });

        AnimatorState crouched = CreateBlendState(controller, machine, "CrouchLocomotion",
            new[] { Clip(clips, CrouchIdleKey), Clip(clips, CrouchWalkKey) },
            new[] { 0f, 2.5f });

        machine.defaultState = upright;

        // Eğilme geçişi. Eşik ortada değil, iki yönde farklı (0.6 / 0.4):
        // tam eşikte titremeyi önlüyor.
        AddCondition(upright.AddTransition(crouched), AnimatorConditionMode.Greater, 0.6f,
            CharacterAnimatorBase.CrouchParameter);
        AddCondition(crouched.AddTransition(upright), AnimatorConditionMode.Less, 0.4f,
            CharacterAnimatorBase.CrouchParameter);

        // Havada olma: canavarda yok, çünkü canavar zıplayamıyor.
        AnimationClip airborneClip = FirstAvailable(clips, AirborneKeys);

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
        AnimationClip deathClip = FirstAvailableByHint(clips, DeathHints);

        if (deathClip != null)
        {
            AnimatorState death = CreateClipState(machine, "Olum", deathClip);

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

        EditorUtility.SetDirty(controller);
        return controller;
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
    private static AnimationClip FirstAvailable(Dictionary<string, AnimationClip> clips,
        string[] keys)
    {
        foreach (string key in keys)
        {
            if (clips.TryGetValue(key, out AnimationClip clip))
                return clip;
        }

        return null;
    }

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
        int count = ModelMaterialFix.RemapExternalMaterials(ModelPath);

        // Remap SaveAndReimport çağırıyor: model referansı bayatlıyor.
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
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

                bool broken = material.shader.name == "Hidden/InternalErrorShader"
                    || material.shader.name.Contains("Universal Render Pipeline");

                if (!broken)
                    continue;

                material.shader = standard;
                EditorUtility.SetDirty(material);
                count++;
            }
        }

        count += ModelMaterialFix.FillBuiltinSlots(materials, "Kaçan Modelini Kur");
        return count;
    }

    // ---------- Prefab ----------

    private static string AttachToPlayerPrefab(GameObject model, AnimatorController controller,
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

            Transform existing = contents.transform.Find(RunnerRootName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, contents.transform);
            instance.name = RunnerRootName;

            // Ölçek tahmin edilmiyor, ÖLÇÜLÜYOR: gerçek renderer sınırları
            // okunup hull yüksekliğine oranlanıyor. Pivotun nerede olduğunu
            // bilmek zorunda kalmıyoruz.
            float scale = ResolveScale(instance, capsule.height) * ExtraScale;
            instance.transform.localScale = Vector3.one * scale;

            // Ayaklar hull'un tabanına: kök objenin merkezi controller.center'da,
            // taban ondan height/2 aşağıda.
            instance.transform.localPosition = capsule.center + Vector3.down * (capsule.height / 2f);
            instance.transform.localRotation = Quaternion.identity;

            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = instance.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // hareketi PlayerController veriyor
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            WireComponents(contents, instance, animator, clips, deathSeconds);

            LayerSetup.Apply(instance, LayerSetup.Oyuncu);

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            return $"Model prefaba bağlandı (ölçek {scale:0.###}).";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
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

    private static void WireComponents(GameObject root, GameObject runnerRoot, Animator animator,
        Dictionary<string, AnimationClip> clips, float deathSeconds)
    {
        PlayerBodyVisual visual = root.GetComponent<PlayerBodyVisual>()
            ?? root.AddComponent<PlayerBodyVisual>();

        RunnerAnimator runnerAnimator = root.GetComponent<RunnerAnimator>()
            ?? root.AddComponent<RunnerAnimator>();

        SerializedObject serializedVisual = new SerializedObject(visual);
        serializedVisual.FindProperty("runnerRoot").objectReferenceValue = runnerRoot;

        serializedVisual.FindProperty("runnerHeadBone").objectReferenceValue =
            animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

        Renderer[] runnerRenderers = runnerRoot.GetComponentsInChildren<Renderer>(true);
        SerializedProperty array = serializedVisual.FindProperty("runnerRenderers");
        array.arraySize = runnerRenderers.Length;
        for (int i = 0; i < runnerRenderers.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = runnerRenderers[i];

        List<Renderer> headParts = new List<Renderer>();
        foreach (Renderer renderer in runnerRenderers)
        {
            if (IsHeadPart(renderer))
                headParts.Add(renderer);
        }

        SerializedProperty headArray = serializedVisual.FindProperty("runnerHeadRenderers");
        headArray.arraySize = headParts.Count;
        for (int i = 0; i < headParts.Count; i++)
            headArray.GetArrayElementAtIndex(i).objectReferenceValue = headParts[i];

        serializedVisual.ApplyModifiedProperties();

        SerializedObject serializedAnimator = new SerializedObject(runnerAnimator);
        serializedAnimator.FindProperty("animator").objectReferenceValue = animator;
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
            return lower.Contains("eye") || lower.Contains("head") || lower.Contains("goz");
        }
    }
}
