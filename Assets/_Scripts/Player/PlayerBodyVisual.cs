using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Oyuncunun görünen gövdesi. Hangi gövdenin görüneceğine tek yerden karar
/// veriyor:
///
/// - **Canavar** → canavar modeli
/// - **Kaçan** → seçilen KOSTÜMÜN gövdesi (bkz. CharacterCatalog)
/// - **Kostüm gövdesi yoksa** → kapsül (eski yer tutucu)
///
/// Kapsül bilerek duruyor: model bağlanmadan da oyun oynanabilsin, ve
/// `Kaçan Modelini Kur` hiç çalıştırılmamış bir projede kimse görünmez olmasın.
///
/// **Görünürlük üç şeyden hesaplanıyor** ve hepsi tek yerde toplanıyor:
///
/// - **Rol** — hangi gövde
/// - **Sahada mı** — elenen, kurtulan ve tura geç katılan görünmüyor
/// - **Birinci şahıs mı** — kendi gövdeni görmemelisin, ama gölgen düşmeli
///
/// Üçünü ayrı ayrı açıp kapatmak sıraya bağımlı hatalar üretiyordu (bkz.
/// RoundParticipant.RefreshBodyState); burada da aynı desen kullanılıyor.
///
/// ### Kaçan bir DİZİ, canavar tek — bilerek
///
/// Kaçanın birden çok kostümü var ve hepsi prefabta hazır duruyor; aynı anda
/// yalnızca biri açık. Canavarınki hâlâ tek alan: orada seçilecek ikinci bir
/// gövde yok ve alan adlarını değiştirmek prefabtaki mevcut bağlantıları
/// koparıp `MonsterSetup`'ı da yeniden yazdırırdı. Kazancı olmayan bir risk.
/// </summary>
public class PlayerBodyVisual : MonoBehaviour
{
    /// <summary>
    /// Tek bir kaçan kostümünün gövdesi. `Kaçan Modelini Kur` her kostüm için
    /// bir tane kuruyor ve sıraları `CharacterCatalog.Runners` ile aynı.
    /// </summary>
    [System.Serializable]
    public class CostumeBody
    {
        [Tooltip("Gövdenin kökü. Kapatınca Animator da durur.")]
        public GameObject root;

        public Renderer[] renderers;

        [Tooltip("Kafa kemiği. Birinci şahısta sıfıra ölçekleniyor.")]
        public Transform headBone;

        [Tooltip("Kafaya ait AYRI renderer'lar (göz, saç gibi). Kemik ölçeği " +
            "bunları toplamıyor — kendi iskeletleri var.")]
        public Renderer[] headRenderers;

        [Tooltip("Bu gövdenin animatörü. Kostüm değişince RunnerAnimator buna " +
            "yönlendiriliyor.")]
        public Animator animator;
    }

    [Header("Kaçan — yer tutucu")]
    [Tooltip("Kapsül mesh — CapsuleBodyVisual boyunu hull'a eşitliyor. " +
        "Yalnızca hiçbir kostüm gövdesi takılı DEĞİLSE kullanılıyor.")]
    [SerializeField] private Renderer capsuleRenderer;

    [Header("Canavar")]
    [Tooltip("Canavar modelinin kökü. Kapatınca Animator da durur.")]
    [SerializeField] private GameObject monsterRoot;

    [SerializeField] private Renderer[] monsterRenderers;

    [Tooltip("Kafa kemiği. Birinci şahısta sıfıra ölçekleniyor: gövdeni " +
        "görebilesin ama kafan görüşü kapatmasın.")]
    [SerializeField] private Transform headBone;

    [Tooltip("Kafaya ait AYRI renderer'lar (gözler gibi). Kemik ölçeği bunları " +
        "toplamıyor — kendi iskeletleri var — o yüzden birinci şahısta " +
        "doğrudan kapatılıyorlar.")]
    [SerializeField] private Renderer[] headRenderers;

    [Header("Kaçan — kostümler")]
    [Tooltip("Kostüm gövdeleri. Sıra CharacterCatalog.Runners ile AYNI olmalı; " +
        "`Kaçan Modelini Kur` ikisini birlikte kuruyor.")]
    [SerializeField] private CostumeBody[] runnerBodies;

    [Tooltip("Kostüm değişince hangi animatörü süreceğini bilmesi gereken " +
        "bileşen. Oyuncunun kökünde duruyor, gövdenin içinde değil.")]
    [SerializeField] private RunnerAnimator runnerAnimator;

    [Header("Canavar — kostümler")]
    [Tooltip("Canavar kostüm gövdeleri. Sıra CharacterCatalog.Monsters ile " +
        "AYNI olmalı; `Canavar Modelini Kur` ikisini birlikte kuruyor. " +
        "BOŞSA yukarıdaki tekil monsterRoot'a düşülüyor — eski prefablar " +
        "çalışmaya devam etsin diye.")]
    [SerializeField] private CostumeBody[] monsterBodies;

    [Tooltip("Canavar kostümü değişince yönlendirilecek animatör. Kaçandaki " +
        "runnerAnimator'ın karşılığı.")]
    [SerializeField] private MonsterAnimator monsterAnimator;

    [Header("Ölüm pozu")]
    [Tooltip("Kurbanın gövdesi ölüm klibi boyunca bu çarpanla ölçekleniyor. " +
        "Canavar hull boyunun 1.30 katı çiziliyor (MonsterSetup.ExtraScale), " +
        "kaçan 1 katı; yani canavarın yakalama koreografisi daha büyük " +
        "oynuyor ve elleri kurbanın gövdesinin olmadığı yere iniyor. Çarpan " +
        "ikisini ölüm süresince aynı ölçeğe getiriyor. 1 = kapalı. " +
        "`Kaçan Modelini Kur` bunu iki aracın ExtraScale oranından yazıyor — " +
        "elle değiştirirsen bir sonraki kurulum geri alır.")]
    [SerializeField] private float deathScaleMatch = 1f;

    private RoundRole role = RoundRole.None;
    private bool onField = true;
    private bool firstPerson;

    private int runnerCostume;
    private int monsterCostume;

    private Vector3 monsterHeadRestScale = Vector3.one;
    private bool monsterHeadCached;

    private Vector3 runnerHeadRestScale = Vector3.one;
    private bool runnerHeadCached;

    // Boyun kemikleri. Prefabta alan olarak DURMUYORLAR, çalışma anında
    // Animator'dan çözülüyorlar: alan eklemek prefabı yeniden kurmayı
    // gerektirirdi ve o zincir canavar/kaçan modellerini de yeniden kurmak
    // demek (bölüm 7'deki sıra). Aynı desen MonsterAura ve Terminal'de de var.
    private Transform monsterNeckBone;
    private Vector3 monsterNeckRestScale = Vector3.one;
    private bool monsterNeckCached;

    // Canavarın boynu da artık KOSTÜM BAŞINA: ikinci canavar (domuz katil)
    // gelince tek bir önbellek yanlış modelin kemiğini tutmaya başladı.
    private int monsterNeckCachedFor = -1;

    // Kaçanın boynu KOSTÜM BAŞINA çözülüyor: her modelin kendi iskeleti var.
    private Transform runnerNeckBone;
    private Vector3 runnerNeckRestScale = Vector3.one;
    private bool runnerNeckCached;
    private int neckCachedFor = -1;

    private Transform posedBody;
    private Vector3 posedLocalPosition;
    private Quaternion posedLocalRotation;
    private Vector3 posedLocalScale;

    private void Start() => Refresh();

    /// <summary>Rol değişti — RoundParticipant çağırıyor.</summary>
    public void SetRole(RoundRole value)
    {
        role = value;
        Refresh();
    }

    /// <summary>Sahada mı: elendi / kurtuldu / geç katıldı ise değil.</summary>
    public void SetOnField(bool value)
    {
        onField = value;
        Refresh();
    }

    /// <summary>
    /// Bu gövde yerel oyuncunun mu. Birinci şahısta kendi gövdeni görmemelisin
    /// ama gölgen düşmeli — yoksa fener tutarken gölgesiz bir hayalet oluyorsun.
    /// </summary>
    public void SetFirstPerson(bool value)
    {
        firstPerson = value;
        Refresh();
    }

    /// <summary>
    /// Kostümleri uygular (bkz. CharacterCatalog). İkisi birlikte veriliyor:
    /// rol tur ortasında değişebiliyor ve o anda öbür kostümün de doğru olması
    /// gerekiyor.
    ///
    /// `RoundParticipant`'ın SyncVar hook'undan çağrılıyor, yani **her
    /// istemcide** çalışıyor — karşındakini de kendi seçtiği kostümde
    /// görüyorsun.
    /// </summary>
    public void SetCostume(int runner, int monster)
    {
        if (runnerCostume != runner)
        {
            // Boyun kemiği önbelleği kostüme bağlı: her modelin kendi iskeleti
            // var ve eski kemiği yeni gövdede kullanmak yanlış kemiği
            // sıfırlamak olurdu.
            ReleaseRunnerBones();
            runnerCostume = runner;
        }

        if (monsterCostume != monster)
        {
            ReleaseMonsterBones();
            monsterCostume = monster;
        }

        Refresh();
    }

    /// <summary>
    /// Kostüm değişirken eski gövdenin gizlenen kemiklerini ESKİ HÂLİNE
    /// döndürüyor. Yapılmasaydı kapatılan gövde sıfır ölçekli bir kafayla
    /// kalır ve oyuncu o kostüme geri döndüğünde kafasız görünürdü.
    /// </summary>
    private void ReleaseRunnerBones()
    {
        CostumeBody body = ActiveRunner();

        if (body != null)
        {
            HideBone(body.headBone, false, ref runnerHeadRestScale, ref runnerHeadCached);
            HideBone(runnerNeckBone, false, ref runnerNeckRestScale, ref runnerNeckCached);
        }

        runnerHeadCached = false;
        runnerNeckCached = false;
        runnerNeckBone = null;
        neckCachedFor = -1;
    }

    /// <summary>
    /// `ReleaseRunnerBones`'un canavar karşılığı. Kostüm değişirken eski
    /// gövdenin sıfırlanmış kafa/boyun kemikleri eski hâline dönüyor.
    /// </summary>
    private void ReleaseMonsterBones()
    {
        CostumeBody body = ActiveMonster();

        if (body != null)
        {
            HideBone(body.headBone, false, ref monsterHeadRestScale, ref monsterHeadCached);
            HideBone(monsterNeckBone, false, ref monsterNeckRestScale, ref monsterNeckCached);
        }

        monsterHeadCached = false;
        monsterNeckCached = false;
        monsterNeckBone = null;
        monsterNeckCachedFor = -1;
    }

    /// <summary>Seçili kostümün gövdesi; dizi boşsa ya da indeks dışarıdaysa null.</summary>
    private CostumeBody ActiveRunner() => Pick(runnerBodies, runnerCostume);

    /// <summary>
    /// Seçili CANAVAR kostümünün gövdesi. Dizi boşsa null döner ve çağıranlar
    /// tekil `monsterRoot`'a düşer — `Canavar Modelini Kur` hiç
    /// çalıştırılmamış bir prefabta canavar görünmez olmasın diye.
    /// </summary>
    private CostumeBody ActiveMonster() => Pick(monsterBodies, monsterCostume);

    /// <summary>
    /// Kostüm indeksini gövdeye çeviren ortak seçici. Geçersiz indeks SIFIRA
    /// düşüyor, kırpılmıyor — `CharacterCatalog.Sanitize` ile aynı kural.
    /// </summary>
    private static CostumeBody Pick(CostumeBody[] list, int index)
    {
        if (list == null || list.Length == 0)
            return null;

        CostumeBody body = list[index >= 0 && index < list.Length ? index : 0];

        return body != null && body.root != null ? body : null;
    }

    /// <summary>
    /// Ölüm için gövdeyi canavarın önüne oturtur.
    ///
    /// **Taşınan gövde kökü, oyuncunun kökü DEĞİL.** Kök NetworkTransform ile
    /// taşınıyor ve istemci otoriteli; başka bir istemciden yazmak tam da Source
    /// hissini bozacak prediction kavgasını başlatırdı (CLAUDE.md bölüm 4).
    /// Gövde kökü ise ağda hiç yok, yani her istemci canavarın zaten bildiği
    /// pozisyonundan aynı sonucu kendi hesaplıyor — ek trafik tek bir netId.
    ///
    /// **Yalnızca yatay düzlemde taşınıyor.** Y olduğu gibi bırakılıyor:
    /// kurbanın ayak hizası zaten doğru. Y'yi de canavardan almak, eğimli bir
    /// yerde bedeni zemine gömer ya da havada bırakırdı.
    ///
    /// **Aynı noktada ama ZIT yöne bakıyorlar.** Klip çifti böyle yapılmış:
    /// canavar atlayıp adamı düşürüyor, üstüne çıkıp yumrukluyor. İkisi iç içe
    /// geçmeli, o yüzden varsayılan offset 0 — araya mesafe koymak koreografiyi
    /// bozuyor.
    /// </summary>
    /// <param name="matchScale">
    /// Kurbanın gövdesi canavarın ölçeğine çıkarılsın mı. Eşli yakalama
    /// koreografisinde ŞART (iki klip iç içe geçiyor ve ölçek farkı onları
    /// ayırıyor — bölüm 10), ama FIRLATAN canavarda koreografi yok: orada
    /// kurbanı %18 büyütmek yalnızca görünür bir sıçrama üretir.
    /// </param>
    public void ApplyDeathPose(Transform killer, float forwardOffset, bool matchScale = true)
    {
        Transform body = ActiveBody();
        if (body == null || killer == null)
            return;

        ClearDeathPose();

        posedBody = body;
        posedLocalPosition = body.localPosition;
        posedLocalRotation = body.localRotation;
        posedLocalScale = body.localScale;

        // Ölçek eşitleme, konumdan ÖNCE: ölçek gövdenin kendi kökünde
        // uygulanıyor ve dünya konumunu kaydırmıyor, ama sırayı tersine
        // çevirmek okuyanı "acaba kaydırıyor mu" diye düşündürüyor.
        if (matchScale && deathScaleMatch > 0f && !Mathf.Approximately(deathScaleMatch, 1f))
            body.localScale = posedLocalScale * deathScaleMatch;

        Vector3 forward = Vector3.ProjectOnPlane(killer.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();

        Vector3 target = killer.position + forward * forwardOffset;
        body.position = new Vector3(target.x, body.position.y, target.z);
        body.rotation = Quaternion.LookRotation(-forward, Vector3.up);
    }

    /// <summary>Ölüm pozunu geri alır. Yeni turda gövde kendi yerine dönmeli.</summary>
    public void ClearDeathPose()
    {
        if (posedBody == null)
            return;

        posedBody.localPosition = posedLocalPosition;
        posedBody.localRotation = posedLocalRotation;
        posedBody.localScale = posedLocalScale;
        posedBody = null;
    }

    private Transform ActiveBody()
    {
        if (role == RoundRole.Monster)
        {
            CostumeBody monster = ActiveMonster();

            return monster != null
                ? monster.root.transform
                : (monsterRoot != null ? monsterRoot.transform : null);
        }

        CostumeBody body = ActiveRunner();
        return body != null ? body.root.transform : null;
    }

    /// <summary>
    /// Ceset sistemi için: kaçanın şu an gösterilen gövdesi (seçili kostüm,
    /// hiçbiri yoksa kapsül). Yalnızca kaçanlar ölüyor, canavar hiç sorulmuyor
    /// — bu yüzden <see cref="ActiveBody"/>'nin rol dallanmasını tekrarlamıyor.
    ///
    /// `Corpse` bunu ölüm klibinin TAM BİTTİĞİ anda (deathHoldDuration
    /// sonunda, `ClearDeathPose` çağrılmadan hemen ÖNCE) okuyor: o anda
    /// `ApplyDeathPose`'un konumlandırdığı, canavarın önünde diz çökmüş hâl
    /// hâlâ duruyor. `Instantiate` bu transform'un o anki DÜNYA
    /// pozisyonunu/rotasyonunu kopyalıyor — ayrı bir kemik/poz taşıma kodu
    /// gerekmiyor.
    /// </summary>
    public Transform RunnerBodyForCorpse
    {
        get
        {
            CostumeBody body = ActiveRunner();

            if (body != null)
                return body.root.transform;

            return capsuleRenderer != null ? capsuleRenderer.transform : null;
        }
    }

    /// <summary>
    /// Ceset için: gövdenin ölüm pozundan ÖNCEKİ yerel ölçeği.
    ///
    /// `ApplyDeathPose` ölüm klibi boyunca gövdeyi `deathScaleMatch` ile
    /// şişiriyor (canavarın koreografisiyle örtüşsün diye, bölüm 10). Ceset
    /// klonu tam o pencerede alınıyor, yani şişmiş hâli kopyalanır. Kalıcı
    /// ceset normal boyunda olmalı: `1` yazmak da yanlış olurdu, çünkü modelin
    /// kendi ölçeği hull'a oranlanarak hesaplanıyor (`RunnerSetup.ResolveScale`).
    /// Doğru cevap, poz uygulanmadan önce saklanan değer.
    /// </summary>
    public Vector3 RunnerBodyRestScale
    {
        get
        {
            Transform body = RunnerBodyForCorpse;

            if (body == null)
                return Vector3.one;

            return posedBody == body ? posedLocalScale : body.localScale;
        }
    }

    /// <summary>
    /// Canavarın gövdesini yürürken/koşarken çeviriyor — **yalnızca
    /// başkalarının ekranında.**
    ///
    /// Domuz katilin yürüme ve koşma klipleri "sopayı tutarak" yazılmış ve
    /// gövde gidiş yönüne göre yan duruyor. Açı bir süre klibin import
    /// ayarına gömülüydü; o zaman canavarı OYNAYAN da kendi gövdesini yamuk
    /// görüyordu. Burada `firstPerson` sorulabildiği için düzeltme yalnızca
    /// dışarıdan bakana gidiyor.
    ///
    /// **Ağa hiçbir şey gitmiyor.** Gövde kökü zaten senkronlanmıyor
    /// (bölüm 17) ve her istemci "bu gövde benim mi" sorusunu kendi
    /// cevaplıyor — izlerin yalnızca canavara gönderilmesiyle aynı desen.
    ///
    /// `LateUpdate`, çünkü animatör pozu `Update`'ten sonra yazıyor; gövde
    /// KÖKÜNÜN dönüşünü animatör yazmıyor ama sıralamayı garanti altına
    /// almak bedava.
    /// </summary>
    private void LateUpdate()
    {
        if (role != RoundRole.Monster)
            return;

        CostumeBody body = ActiveMonster();

        if (body == null || body.root == null)
            return;

        Transform root = body.root.transform;

        // Ölüm pozu gövdeyi dünya uzayında konumlandırıyor; o sırada
        // karışmıyoruz.
        if (posedBody == root)
            return;

        float yaw = 0f;

        if (!firstPerson)
        {
            float blend = monsterAnimator != null ? monsterAnimator.LocomotionBlend : 0f;
            yaw = CharacterCatalog.Monster(monsterCostume).LocomotionYawOffset * blend;
        }

        root.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void Refresh()
    {
        bool monster = role == RoundRole.Monster;
        CostumeBody runner = monster ? null : ActiveRunner();
        CostumeBody monsterBody = monster ? ActiveMonster() : null;

        // Kostüm dizisi boşsa (araç hiç çalıştırılmamış) tekil `monsterRoot`
        // devreye giriyor. İkisi asla birlikte açılmıyor — `legacyMonster`
        // yalnızca dizi bir gövde veremediğinde doğru.
        bool legacyMonster = monster && monsterBody == null;
        bool capsule = !monster && runner == null;

        // Kullanılmayan model kökü tamamen kapalı: Animator ve SkinnedMesh
        // boşuna çalışmasın. Kostüm dizisinin TAMAMI geziliyor, yalnızca
        // seçili olan değil — yoksa önceki kostüm açık kalır ve iki gövde üst
        // üste görünürdü.
        if (monsterRoot != null)
            monsterRoot.SetActive(legacyMonster && onField);

        SetActiveOnly(monsterBodies, monsterBody, onField);
        SetActiveOnly(runnerBodies, runner, onField);

        // Animatör seçili gövdeyi sürüyor. Kapalı bir animatöre yazmak sessizce
        // hiçbir şey yapmaz ve karakter T-pozunda donardı.
        if (runnerAnimator != null && runner != null && runner.animator != null)
            runnerAnimator.UseAnimator(runner.animator);

        if (monsterAnimator != null && monsterBody != null && monsterBody.animator != null)
            monsterAnimator.UseAnimator(monsterBody.animator);

        // Kapsül kendine gösterilmiyor: suratının önünde duran bir kapsül
        // kimseye bir şey anlatmıyor, sadece görüşü kapatıyor.
        ApplyMode(capsuleRenderer, capsule && onField, showToSelf: false);

        // Modeller birinci şahısta da çiziliyor: aşağı bakınca kendi ellerini ve
        // bacaklarını görmek, hareketi hissettiren şey.
        ApplyRenderers(monsterRenderers, legacyMonster && onField, showToSelf: true);

        if (monsterBody != null)
            ApplyRenderers(monsterBody.renderers, onField, showToSelf: true);

        if (runner != null)
            ApplyRenderers(runner.renderers, onField, showToSelf: true);

        // EN SONDA: kafa parçaları renderer listelerinde de var ve yukarıdaki
        // döngüler onları açıyor. Önce çalıştırırsak yaptığımız iş aynı karede
        // geri alınıyor — gözlerin kaybolmamasının sebebi buydu.
        //
        // Tekil ve dizi yol AYNI önbelleği paylaşıyor; birbirini ezmiyorlar
        // çünkü `legacyMonster` ile `monsterBody != null` birbirini dışlıyor.
        ApplyHead(headBone, headRenderers, legacyMonster && onField,
            ref monsterHeadRestScale, ref monsterHeadCached);

        if (monsterBody != null)
        {
            ApplyHead(monsterBody.headBone, monsterBody.headRenderers, onField,
                ref monsterHeadRestScale, ref monsterHeadCached);
        }

        if (runner != null)
        {
            ApplyHead(runner.headBone, runner.headRenderers, onField,
                ref runnerHeadRestScale, ref runnerHeadCached);
        }

        // Boyun da gizleniyor — kafayı sıfırlamak tek başına yetmiyordu.
        Transform monsterNeck = monsterBody != null
            ? ResolveMonsterNeck(monsterBody)
            : ResolveNeck(monsterRoot, ref monsterNeckBone);

        HideBone(monsterNeck, monster && onField && firstPerson,
            ref monsterNeckRestScale, ref monsterNeckCached);

        if (runner != null)
        {
            HideBone(ResolveRunnerNeck(runner), onField && firstPerson,
                ref runnerNeckRestScale, ref runnerNeckCached);
        }
    }

    /// <summary>
    /// Dizideki YALNIZCA seçili gövdeyi açar, kalanını kapatır.
    ///
    /// Dizinin TAMAMI geziliyor, sadece seçili olan değil: yoksa kostüm
    /// değiştirildiğinde önceki gövde açık kalır ve iki model üst üste
    /// görünürdü.
    /// </summary>
    private static void SetActiveOnly(CostumeBody[] list, CostumeBody active, bool onField)
    {
        if (list == null)
            return;

        for (int i = 0; i < list.Length; i++)
        {
            CostumeBody body = list[i];

            if (body != null && body.root != null)
                body.root.SetActive(body == active && onField);
        }
    }

    /// <summary>
    /// Seçili kostümün boyun kemiği. Önbellek KOSTÜME bağlı: her modelin kendi
    /// iskeleti var ve indeks değiştiğinde yeniden çözülmesi gerekiyor.
    /// </summary>
    private Transform ResolveRunnerNeck(CostumeBody body)
    {
        if (neckCachedFor != runnerCostume)
        {
            runnerNeckBone = null;
            neckCachedFor = runnerCostume;
        }

        return ResolveNeck(body.root, ref runnerNeckBone);
    }

    /// <summary>`ResolveRunnerNeck`'in canavar karşılığı.</summary>
    private Transform ResolveMonsterNeck(CostumeBody body)
    {
        if (monsterNeckCachedFor != monsterCostume)
        {
            monsterNeckBone = null;
            monsterNeckCachedFor = monsterCostume;
        }

        return ResolveNeck(body.root, ref monsterNeckBone);
    }

    /// <summary>
    /// Modelin boyun kemiğini Animator'dan bulur ve saklar.
    ///
    /// Humanoid rig'te kemiğe **adıyla değil rolüyle** ulaşılıyor: model
    /// değişirse kemik adı değişir ama `HumanBodyBones.Neck` değişmez. Yeni bir
    /// kostüm eklemek bu yüzden hiçbir ad listesi güncellemiyor.
    /// </summary>
    private static Transform ResolveNeck(GameObject modelRoot, ref Transform cache)
    {
        if (cache != null || modelRoot == null)
            return cache;

        Animator animator = modelRoot.GetComponentInChildren<Animator>(true);

        if (animator != null && animator.isHuman)
            cache = animator.GetBoneTransform(HumanBodyBones.Neck);

        return cache;
    }

    /// <summary>
    /// Kemiği sıfıra ölçekler, ilk seferde özgün ölçeğini saklayarak.
    ///
    /// Ölçek **animasyonun yazmadığı** tek kanal (humanoid klipler konum ve
    /// dönüş yazıyor), o yüzden bir kez yazmak yetiyor ve her karede
    /// tekrarlamaya gerek kalmıyor.
    /// </summary>
    private static void HideBone(Transform bone, bool hide, ref Vector3 restScale, ref bool cached)
    {
        if (bone == null)
            return;

        if (!cached)
        {
            restScale = bone.localScale;
            cached = true;
        }

        bone.localScale = hide ? Vector3.zero : restScale;
    }

    /// <summary>
    /// Birinci şahısta kafayı gizler.
    ///
    /// Kemik sıfıra ölçekleniyor, renderer kapatılmıyor: gövde tek bir skinned
    /// mesh, kafayı ayrı kapatmanın yolu yok. Kemik ölçeği **senkronlanmıyor**
    /// (NetworkTransform yalnızca kökü taşıyor), yani bu yalnızca kendi
    /// ekranını etkiliyor — karşıdakiler seni kafanla görüyor.
    ///
    /// Kafaya ait ayrı renderer'lar (gözler, saç) kemik ölçeğini toplamıyor:
    /// kendi iskeletlerine bağlı ayrı mesh'ler. Ekranda havada duran parçalar
    /// olarak kalıyorlardı, o yüzden ayrıca kapatılıyorlar.
    ///
    /// > **Kafayı sıfırlamak tek başına YETMİYOR** (2026-09-05). Canavar
    /// > koşarken kendi kafasının içini görüyordu. Sebep, sıfırlanan kemiğin
    /// > kendisi değil **komşusu**: boyun ile kafa arasında ağırlığı paylaşan
    /// > vertex'ler var ve kafa bir noktaya çökünce o vertex'ler boyundan o
    /// > noktaya doğru uzun ince üçgenler hâline geliyor. Koşu animasyonu
    /// > gövdeyi öne eğdiğinde bu huni kameranın önünden geçiyor ve arka
    /// > yüzleri görünüyor — oyuncunun "kafamın içi" dediği şey o.
    /// >
    /// > Boyun da sıfırlanınca huninin ucu omuz hizasına, yani kameradan
    /// > belirgin şekilde uzağa iniyor. Bedeli: birinci şahısta omuz/yaka
    /// > hafif deforme oluyor — **yalnızca kendi ekranında**, kemik ölçeği
    /// > senkronlanmıyor.
    /// </summary>
    private void ApplyHead(Transform bone, Renderer[] renderers, bool bodyVisible,
        ref Vector3 restScale, ref bool cached)
    {
        bool hide = bodyVisible && firstPerson;

        HideBone(bone, hide, ref restScale, ref cached);

        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = bodyVisible && !hide;
        }
    }

    private void ApplyRenderers(Renderer[] renderers, bool active, bool showToSelf)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
            ApplyMode(renderers[i], active, showToSelf);
    }

    /// <summary>
    /// Görünürlüğü `enabled` ile değil gölge moduyla veriyoruz: kendi gövdeni
    /// görmemen gerektiği durumda bile gölgenin düşmesi gerekiyor. Renderer'ı
    /// kapatmak gölgeyi de götürürdü.
    /// </summary>
    private void ApplyMode(Renderer target, bool active, bool showToSelf)
    {
        if (target == null)
            return;

        if (!active)
        {
            target.enabled = false;
            return;
        }

        target.enabled = true;

        target.shadowCastingMode = firstPerson && !showToSelf
            ? ShadowCastingMode.ShadowsOnly
            : ShadowCastingMode.On;
    }
}
