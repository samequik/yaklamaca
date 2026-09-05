using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Oyuncunun görünen gövdesi. Üç gövde tutuyor ve hangisinin görüneceğine tek
/// yerden karar veriyor:
///
/// - **Canavar** → canavar modeli
/// - **Kaçan** → kaçan modeli (Banana Man)
/// - **Kaçan modeli takılı değilse** → kapsül (eski yer tutucu)
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
/// **Kaçan alanları canavarınkilerin kopyası gibi duruyor, bilerek.** Ortak bir
/// "gövde takımı" sınıfına çekmek alan adlarını değiştirirdi ve prefabtaki
/// mevcut canavar bağlantıları kopardı; MonsterSetup da yeniden yazılmak
/// zorunda kalırdı. Kazancı olmayan bir risk.
/// </summary>
public class PlayerBodyVisual : MonoBehaviour
{
    [Header("Kaçan — yer tutucu")]
    [Tooltip("Kapsül mesh — CapsuleBodyVisual boyunu hull'a eşitliyor. " +
        "Yalnızca kaçan modeli takılı DEĞİLSE kullanılıyor.")]
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

    [Header("Kaçan — model")]
    [Tooltip("Kaçan modelinin kökü. Boş bırakılırsa kapsüle düşülüyor.")]
    [SerializeField] private GameObject runnerRoot;

    [SerializeField] private Renderer[] runnerRenderers;

    [Tooltip("Kaçan modelinin kafa kemiği — canavarınkiyle aynı gerekçe.")]
    [SerializeField] private Transform runnerHeadBone;

    [SerializeField] private Renderer[] runnerHeadRenderers;

    [Header("Ölüm pozu")]
    [Tooltip("Kurbanın gövdesi ölüm klibi boyunca bu çarpanla ölçekleniyor. " +
        "Canavar hull boyunun 1.18 katı çiziliyor (MonsterSetup.ExtraScale), " +
        "kaçan 1 katı; yani canavarın yakalama koreografisi %18 daha büyük " +
        "oynuyor ve elleri kurbanın gövdesinin olmadığı yere iniyor. Çarpan " +
        "ikisini ölüm süresince aynı ölçeğe getiriyor. 1 = kapalı. " +
        "`Kaçan Modelini Kur` bunu iki aracın ExtraScale oranından yazıyor — " +
        "elle değiştirirsen bir sonraki kurulum geri alır.")]
    [SerializeField] private float deathScaleMatch = 1f;

    private RoundRole role = RoundRole.None;
    private bool onField = true;
    private bool firstPerson;

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

    private Transform runnerNeckBone;
    private Vector3 runnerNeckRestScale = Vector3.one;
    private bool runnerNeckCached;

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
    public void ApplyDeathPose(Transform killer, float forwardOffset)
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
        if (deathScaleMatch > 0f && !Mathf.Approximately(deathScaleMatch, 1f))
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
            return monsterRoot != null ? monsterRoot.transform : null;

        return runnerRoot != null ? runnerRoot.transform : null;
    }

    private void Refresh()
    {
        bool monster = role == RoundRole.Monster;
        bool runnerModel = !monster && runnerRoot != null;
        bool capsule = !monster && runnerRoot == null;

        // Kullanılmayan model kökü tamamen kapalı: Animator ve SkinnedMesh
        // boşuna çalışmasın.
        if (monsterRoot != null)
            monsterRoot.SetActive(monster && onField);

        if (runnerRoot != null)
            runnerRoot.SetActive(runnerModel && onField);

        // Kapsül kendine gösterilmiyor: suratının önünde duran bir kapsül
        // kimseye bir şey anlatmıyor, sadece görüşü kapatıyor.
        ApplyMode(capsuleRenderer, capsule && onField, showToSelf: false);

        // Modeller birinci şahısta da çiziliyor: aşağı bakınca kendi ellerini ve
        // bacaklarını görmek, hareketi hissettiren şey.
        ApplyRenderers(monsterRenderers, monster && onField, showToSelf: true);
        ApplyRenderers(runnerRenderers, runnerModel && onField, showToSelf: true);

        // EN SONDA: kafa parçaları renderer listelerinde de var ve yukarıdaki
        // döngüler onları açıyor. Önce çalıştırırsak yaptığımız iş aynı karede
        // geri alınıyor — gözlerin kaybolmamasının sebebi buydu.
        ApplyHead(headBone, headRenderers, monster && onField,
            ref monsterHeadRestScale, ref monsterHeadCached);

        ApplyHead(runnerHeadBone, runnerHeadRenderers, runnerModel && onField,
            ref runnerHeadRestScale, ref runnerHeadCached);

        // Boyun da gizleniyor — kafayı sıfırlamak tek başına yetmiyordu.
        HideBone(ResolveNeck(monsterRoot, ref monsterNeckBone), monster && onField && firstPerson,
            ref monsterNeckRestScale, ref monsterNeckCached);

        HideBone(ResolveNeck(runnerRoot, ref runnerNeckBone), runnerModel && onField && firstPerson,
            ref runnerNeckRestScale, ref runnerNeckCached);
    }

    /// <summary>
    /// Modelin boyun kemiğini Animator'dan bulur ve saklar.
    ///
    /// Humanoid rig'te kemiğe **adıyla değil rolüyle** ulaşılıyor: model
    /// değişirse kemik adı değişir ama `HumanBodyBones.Neck` değişmez.
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
    /// Kafaya ait ayrı renderer'lar (gözler) kemik ölçeğini toplamıyor: kendi
    /// iskeletlerine bağlı ayrı mesh'ler. Ekranda havada duran iki küre olarak
    /// kalıyorlardı, o yüzden ayrıca kapatılıyorlar.
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
