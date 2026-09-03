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
    private const string ControllerPath = ModelFolder + "/Canavar.controller";
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
    /// </summary>
    public const float ExtraScale = 1.18f;

    // Klip adları: dosya adında "@" sonrası kısım.
    private const string LungeClipKey = "attack";
    private const string KillClipKey = "kill";
    private const string RecoverClipKey = "ıskalama";

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

    // Mixamo dosyaları "Karakter@Animasyon.fbx" diye iniyor; eşleştirme
    // "@" sonrasındaki ada bakıyor. Küçük harfe çevrilip karşılaştırılıyor.
    private static readonly string[] LoopingClips =
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

        Dictionary<string, AnimationClip> clips = ImportAnimations(avatar);
        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog("Animasyon yok",
                $"{ModelFolder} altında animasyon FBX'i bulunamadı.", "Tamam");
            return;
        }

        AnimatorController controller = BuildController(clips);
        string attached = AttachToPlayerPrefab(model, controller, clips);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "Canavar kuruldu.\n\n" +
            $"· {materials} materyal onarıldı (Standard shader + eksik albedo)\n" +
            $"· {textures} doku {MaxTextureSize}px'e indirildi\n" +
            $"· {clips.Count} animasyon Humanoid'e bağlandı (Copy From Other Avatar)\n" +
            DescribeClips(clips) +
            $"· Animator: {ControllerPath}\n" +
            $"· {attached}\n\n" +
            "Test: lobide canavarı kendine seç, turu başlat. Kaçan hâlâ kapsül.\n\n" +
            "Ağ Kurulumu'nu tekrar çalıştırırsan prefab sıfırdan kurulur — " +
            "bu menüyü de tekrar çalıştır.");
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
    private static Dictionary<string, AnimationClip> ImportAnimations(Avatar avatar)
    {
        Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();

        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                continue;

            string key = ClipKey(path);
            bool loop = System.Array.IndexOf(LoopingClips, key) >= 0;

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
    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

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
        AnimatorState recover = CreateClipState(machine, "Kalkma", Clip(clips, RecoverClipKey));
        AnimatorState kill = CreateClipState(machine, "Yakalama", Clip(clips, KillClipKey));

        AddCondition(machine.AddAnyStateTransition(lunge), AnimatorConditionMode.If, 0f,
            MonsterAnimator.AttackTrigger);

        // İsabet onaylanınca araya giriyor; atılma bitmeden de kesebiliyor.
        AddCondition(machine.AddAnyStateTransition(kill), AnimatorConditionMode.If, 0f,
            MonsterAnimator.KillTrigger);

        // Atılma bitip isabet gelmediyse kalkma. Geçiş normalden uzun (0.25 sn):
        // iki klip ayrı dosyalardan geliyor ve kesim noktasındaki pozlar birebir
        // tutmayabilir; uzun karışım o farkı yutuyor.
        Chain(lunge, recover, 0.25f);
        Chain(recover, upright);
        Chain(kill, upright);

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
    private static string AttachToPlayerPrefab(GameObject model, AnimatorController controller,
        Dictionary<string, AnimationClip> clips)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            return "UYARI: oyuncu prefabı yok, model bağlanmadı (önce Ağ Kurulumu).";

        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            CharacterController capsule = contents.GetComponent<CharacterController>();
            if (capsule == null)
                return "UYARI: prefabta CharacterController yok, model bağlanmadı.";

            Transform existing = contents.transform.Find(MonsterRootName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, contents.transform);
            instance.name = MonsterRootName;

            float scale = ResolveScale(instance, capsule.height) * ExtraScale;
            instance.transform.localScale = Vector3.one * scale;

            RemoveKnife(contents);

            // Ayaklar hull'un tabanına: kök objenin merkezi controller.center'da,
            // taban ondan height/2 aşağıda.
            instance.transform.localPosition = capsule.center + Vector3.down * (capsule.height / 2f);
            instance.transform.localRotation = Quaternion.identity;

            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; // hareketi PlayerController veriyor
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }

            WireComponents(contents, instance, animator, clips);

            // Model Ağ Kurulumu'ndan SONRA ekleniyor, yani prefabın katmanı o
            // sırada atanmış oluyor ama modelinki Default kalıyordu.
            LayerSetup.Apply(instance, LayerSetup.Oyuncu);

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            return $"Model prefaba bağlandı (ölçek {scale:0.###}).";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
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

    private static void WireComponents(GameObject root, GameObject monsterRoot, Animator animator,
        Dictionary<string, AnimationClip> clips)
    {
        PlayerBodyVisual visual = root.GetComponent<PlayerBodyVisual>()
            ?? root.AddComponent<PlayerBodyVisual>();

        MonsterAnimator monsterAnimator = root.GetComponent<MonsterAnimator>()
            ?? root.AddComponent<MonsterAnimator>();

        Transform capsule = root.transform.Find("Govde");

        SerializedObject serializedVisual = new SerializedObject(visual);
        serializedVisual.FindProperty("capsuleRenderer").objectReferenceValue =
            capsule != null ? capsule.GetComponent<Renderer>() : null;
        serializedVisual.FindProperty("monsterRoot").objectReferenceValue = monsterRoot;

        // Kafa kemiği: birinci şahısta sıfıra ölçeklenip görüşü açıyor.
        serializedVisual.FindProperty("headBone").objectReferenceValue =
            animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

        Renderer[] monsterRenderers = monsterRoot.GetComponentsInChildren<Renderer>(true);
        SerializedProperty array = serializedVisual.FindProperty("monsterRenderers");
        array.arraySize = monsterRenderers.Length;
        for (int i = 0; i < monsterRenderers.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = monsterRenderers[i];

        // Kafaya ait ayrı renderer'lar. Kafa kemiğini sıfırlamak gözleri
        // toplamıyor (kendi iskeletleri var), o yüzden ayrıca kapatılıyorlar.
        // Ayırma ada ve materyal adına bakıyor: modelin parçaları
        // "KillerDollEyes...", "...Head..." diye adlandırılmış.
        List<Renderer> headParts = new List<Renderer>();
        foreach (Renderer renderer in monsterRenderers)
        {
            if (IsHeadPart(renderer))
                headParts.Add(renderer);
        }

        SerializedProperty headArray = serializedVisual.FindProperty("headRenderers");
        headArray.arraySize = headParts.Count;
        for (int i = 0; i < headParts.Count; i++)
            headArray.GetArrayElementAtIndex(i).objectReferenceValue = headParts[i];

        serializedVisual.ApplyModifiedProperties();

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
            serializedParticipant.ApplyModifiedProperties();
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

            if (clips.TryGetValue(KillClipKey, out AnimationClip kill) && kill != null)
                serializedAttack.FindProperty("killLockDuration").floatValue = kill.length;

            serializedAttack.ApplyModifiedProperties();
        }
    }
}
