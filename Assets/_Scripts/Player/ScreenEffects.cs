using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oyuncunun kamerasına binen korku kaplaması: vinyet, gren, renk ayrışması,
/// renk kaybı ve (isteğe bağlı) pikselleme.
///
/// ### İki katman
///
/// **Atmosfer** her zaman açık ve sabit — oyunun "ucuz kamera" görünümünü
/// veren şey bu. **Dehşet** ise canavar yaklaştıkça atmosferin üstüne biniyor:
/// kenarlar kapanıyor, renk çekiliyor, gren artıyor ve vinyet kalp gibi
/// atmaya başlıyor.
///
/// ### Yön BİLDİRMİYOR, bilerek
///
/// Bütün efektler ekranın merkezine göre simetrik. Bölüm 12'nin kalp atışı
/// kuralının aynısı: canavarın hangi tarafta olduğunu söyleyen bir efekt
/// "geliyor ama nereden" gerilimini radara çevirir. Sağ kenarı karartmak
/// cazip ama oyunu bozar.
///
/// ### Mesafe AĞDAN GELMİYOR
///
/// Canavarın konumu zaten NetworkTransform ile her istemcide var; ayrı bir
/// "yakınlık" mesajı yollamak aynı bilgiyi ikinci kez göndermek olurdu.
/// Animatörlerin hızı pozisyon farkından çıkarması (bölüm 14, 17) ve
/// FootstepAudio'nun aynı şeyi yapması (bölüm 12) ile birebir aynı desen:
/// **zaten gönderilenden türet, yeniden gönderme.**
///
/// Bir sonuç: bu bir karar değil, yalnızca yerel bir görüntü. Değiştirilmiş
/// bir istemci zaten aynı mesafeyi hesaplayabilirdi, yani tavan yükselmiyor.
///
/// ### Kurulum gerekmiyor
///
/// NetworkPlayerSetup yerel oyuncunun kamerasına çalışma anında takıyor ve
/// shader Resources'tan yükleniyor. Yani ne prefab değişikliği ne editör
/// aracı gerekiyor — Terminal.GetOrCreateStateLight ve MonsterAura ile aynı
/// gerekçe (bölüm 11.2).
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class ScreenEffects : MonoBehaviour
{
    private const string ShaderName = "EkranEfekti";

    /// <summary>
    /// Bütün efektlerin ortak çarpanı; 0 = tamamen kapalı.
    ///
    /// Ayarlar ekranındaki kaydırıcı buraya yazıyor (`SettingsPanel`). Statik,
    /// çünkü oyuncu her turda prefabtan yeniden doğuyor ve bileşene yazılan bir
    /// değer orada kaybolurdu (bölüm 13'teki ayar kuralı).
    ///
    /// Kalıcı yeri `PlayerProfile.HorrorEffects`; buraya her bileşen doğarken
    /// oradan okunuyor. Yalnızca kaydırıcı yazsaydı, ayarlar ekranını hiç
    /// açmayan bir oyuncuda kayıt okunmaz ve efekt her açılışta tam güçte
    /// gelirdi.
    /// </summary>
    public static float Master = 1f;

    [Header("Atmosfer — canavar uzaktayken")]
    [SerializeField] private float calmVignette = 0.45f;
    [SerializeField] private float calmGrain = 0.040f;
    [SerializeField] private float calmAberration = 0.005f;
    [SerializeField] private float calmDesaturate = 0.30f;

    [Tooltip("1 = DOKUNMA (sakin durumun varsayılanı). Üstü aydınlığı " +
        "parlatıp karanlığı çökertiyor. Sakinde 1 tutuluyor çünkü bloom'la " +
        "birlikte kullanınca ikisi aynı yöne ittiriyor: aydınlık patlıyor, " +
        "karanlık büsbütün çöküyor. Lambaları parlatma işi artık tek bir " +
        "kaldıraçta — bloom'da.")]
    [SerializeField] private float calmContrast = 1.0f;

    [Header("Dehşet — canavar dibindeyken")]
    [SerializeField] private float dreadVignette = 0.78f;
    [SerializeField] private float dreadGrain = 0.095f;
    [SerializeField] private float dreadAberration = 0.012f;
    [SerializeField] private float dreadDesaturate = 0.70f;
    [SerializeField] private float dreadContrast = 1.15f;

    [Tooltip("Kontrastın döndüğü eksen: bunun ÜSTÜ parlıyor, altı çöküyor. " +
        "Sahnenin gerçek orta parlaklığı olmak zorunda. 0.5 (matematiksel " +
        "orta) bu haritada TAVANIN ÜSTÜNDE kalıyor — lamba altı 0.28, geri " +
        "kalan 0.05 civarı — yani her şeyi karartıyordu.")]
    [SerializeField] private float contrastPivot = 0.15f;

    [Header("Bloom — lambaların halesi")]
    [Tooltip("Taşmanın gücü. 0 = kapalı. Karanlık bir oyunda ışıkları " +
        "'patlatmanın' doğru yolu bu: toplamsal ve eşikli olduğu için " +
        "yalnızca zaten parlak yerleri etkiliyor, karanlığa dokunmuyor.")]
    [SerializeField] private float bloom = 0.20f;

    [Tooltip("Bu parlaklığın ÜSTÜ taşıyor. Lamba altı ~0.28, fener konisi " +
        "daha yüksek, ambient 0.006. 0.32'ye çekildi (2026-09-13): 0.26'da " +
        "lamba altındaki ZEMİN de eşiği geçip bloom veriyordu, 'çok parlak' " +
        "şikâyetinin bir kısmı bundandı. 0.32 zemini (0.28) dışarıda " +
        "bırakıyor, hale artık neredeyse yalnızca ışık kaynağının kendisinden " +
        "taşıyor.")]
    [SerializeField] private float bloomThreshold = 0.32f;

    [Tooltip("Halenin yarıçapı (ekran genişliğinin oranı). 0.018'den 0.012'ye " +
        "indirildi (2026-09-13): 'blurlu görünüyor' şikâyeti buradandı — geniş " +
        "yarıçap örnekleri ekranda daha uzağa yayıp haleyi sisli bir bulanıklık " +
        "gibi gösteriyordu. Dar yarıçap aynı 12 örneği daha yakın topluyor, " +
        "hale sisten çok bir parlama gibi duruyor.")]
    [SerializeField] private float bloomRadius = 0.012f;

    [Tooltip("Dehşet tavanındaki piksel blok boyutu. 0 veya 1 = pikselleme " +
        "kapalı. Dehşetle birlikte artıyor, yani canavar uzaktayken görüntü " +
        "tam çözünürlükte kalıyor.")]
    [SerializeField] private float dreadPixelate = 3f;

    [Tooltip("Grenin TAM güce ulaştığı parlaklık. Altında kademeli olarak " +
        "sönüyor, simsiyahta hiç yok. Sabit genlikli gren karanlık bir " +
        "ekranda statik gibi görünüyordu; asıl sorun genlik değil, karanlık " +
        "zeminde göreli kontrastın devasa olmasıydı. 0.18 çok yüksekti ve " +
        "greni tamamen görünmez yaptı: bu haritada yüzeylerin çoğu 0.05 " +
        "civarında, yalnızca lamba altları 0.28'e çıkıyor.")]
    [SerializeField] private float grainFloor = 0.05f;

    [Header("Vinyet geometrisi")]
    [Tooltip("Merkeze uzaklık (köşe = 1): kararmanın başladığı yer.")]
    [SerializeField] private float vignetteStart = 0.36f;
    [Tooltip("Tam karardığı yer. 1'in üstü köşeleri tamamen siyah yapmıyor.")]
    [SerializeField] private float vignetteEnd = 1.05f;

    [Header("Dehşetin mesafeyle ilişkisi")]
    /// <summary>Bu mesafede dehşet TAM (metre).</summary>
    public const float DreadNear = 5f;

    /// <summary>Bu mesafenin ötesinde dehşet YOK (metre). Sis görüşü ~25 m.</summary>
    public const float DreadFar = 22f;
    [Tooltip("Saniyede artış hızı — yaklaşma çabuk hissedilmeli.")]
    [SerializeField] private float dreadRise = 0.9f;
    [Tooltip("Saniyede düşüş hızı. Artıştan YAVAŞ: canavar gittikten sonra " +
        "gerilim bir süre üstünde kalsın.")]
    [SerializeField] private float dreadFall = 0.30f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Test — yalnızca editörde ve geliştirme build'inde")]
    [Tooltip("Dehşeti elle zorlar: kapalı → yarı → tam → kapalı. Tek başına " +
        "test ederken sahada canavar olmadığı için dehşet hiç tetiklenmiyor " +
        "([2] ile başlayınca bütün botlar kaçan) ve efektin o katmanı hiç " +
        "görülemiyordu.")]
    [SerializeField] private KeyCode debugDreadKey = KeyCode.F9;

    /// <summary>-1 = zorlama yok.</summary>
    private float forcedDread = -1f;
#endif

    [Tooltip("Dehşet tavanındaki yatay bant kayması (UV birimi). Parazit " +
        "yalnızca dehşetin üst yarısında görünüyor.")]
    [SerializeField] private float glitchStrength = 0.045f;

    [Header("Nabız")]
    [SerializeField] private float pulseHz = 1.15f;
    [SerializeField] private float pulseDepth = 0.10f;

    private static readonly int VignetteId = Shader.PropertyToID("_Vignette");
    private static readonly int VignetteStartId = Shader.PropertyToID("_VignetteStart");
    private static readonly int VignetteEndId = Shader.PropertyToID("_VignetteEnd");
    private static readonly int GrainId = Shader.PropertyToID("_Grain");
    private static readonly int GrainFloorId = Shader.PropertyToID("_GrainFloor");
    private static readonly int GrainSeedId = Shader.PropertyToID("_GrainSeed");
    private static readonly int AberrationId = Shader.PropertyToID("_Aberration");
    private static readonly int PixelateId = Shader.PropertyToID("_Pixelate");
    private static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");
    private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
    private static readonly int ContrastPivotId = Shader.PropertyToID("_ContrastPivot");
    private static readonly int GlitchId = Shader.PropertyToID("_Glitch");
    private static readonly int BloomId = Shader.PropertyToID("_Bloom");
    private static readonly int BloomThresholdId = Shader.PropertyToID("_BloomThreshold");
    private static readonly int BloomRadiusId = Shader.PropertyToID("_BloomRadius");

    private Material material;
    private RoundParticipant owner;
    private float dread;

    /// <summary>Yerel oyuncunun kamerasına takar; ikinci kez takmıyor.</summary>
    public static void Attach(Camera target)
    {
        if (target == null || target.GetComponent<ScreenEffects>() != null)
            return;

        // Kayıtlı tercih burada okunuyor: bileşen çalışma anında takılıyor ve
        // Inspector'da serileştirilmiş bir alanı yok.
        Master = PlayerProfile.HorrorEffects;

        target.gameObject.AddComponent<ScreenEffects>();
    }

    private void OnEnable()
    {
        if (material != null)
            return;

        // Shader Resources altında duruyor, çünkü Shader.Find yalnızca
        // editörde güvenilir: build'e girmeyen bir shader'ı bulamıyor ve hata
        // ancak build alınınca çıkıyor (bölüm 7'deki tuzağın aynısı).
        Shader shader = Resources.Load<Shader>(ShaderName);

        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("Ekran efekti shader'ı yüklenemedi; efekt kapalı.", this);
            enabled = false;
            return;
        }

        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        // Teşhis logu. "Efekti hiç göremiyorum" şikâyeti geldiğinde ilk soru
        // "çalışıyor mu" oluyor ve tahminle aranması bir tur kaybettirdi.
        // Bir satır, oyun başında bir kez.
        Debug.Log($"Ekran efekti AÇIK — kamera '{name}', shader '{shader.name}'. " +
            "F9 dehşeti zorluyor (kapalı/yarı/tam).", this);
    }

    private void OnDisable()
    {
        if (material == null)
            return;

        Destroy(material);
        material = null;
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(debugDreadKey))
            forcedDread = forcedDread < 0f ? 0.5f : forcedDread < 0.9f ? 1f : -1f;

        if (forcedDread >= 0f)
        {
            dread = forcedDread;
            return;
        }
#endif

        float target = TargetDread();
        float rate = target > dread ? dreadRise : dreadFall;
        dread = Mathf.MoveTowards(dread, target, Time.deltaTime * rate);
    }

    /// <summary>
    /// Yerel oyuncunun dehşeti. Hesap `DreadAt`'te duruyor, çünkü fener
    /// titremesi ve kamera sarsıntısı da aynı sayıyı kullanıyor — üçü
    /// ayrışırsa ekran, ışık ve kamera farklı şeyler söylerdi.
    /// </summary>
    private float TargetDread() => DreadAt(Owner());

    /// <summary>
    /// Bir kaçanın o anki dehşeti — fener titremesi ve kamera sarsıntısı da
    /// buradan besleniyor, böylece üçü aynı sayıyı kullanıyor.
    ///
    /// Statik ve herkeste hesaplanabilir: canavarın konumu zaten senkron,
    /// yani her istemci AYNI sonucu buluyor ve fener titremesi karşı tarafta
    /// da aynı görünüyor. Ağ trafiği sıfır.
    /// </summary>
    public static float DreadAt(RoundParticipant player)
    {
        if (player == null || RoundManager.Instance == null
            || RoundManager.Instance.Phase != RoundPhase.Playing
            || player.Role != RoundRole.Runner || !player.IsAlive
            || player.IsSpectating || player.IsEscaped)
            return 0f;

        Vector3 here = player.transform.position;
        float nearest = float.MaxValue;

        IReadOnlyList<RoundParticipant> all = RoundParticipant.All;
        for (int i = 0; i < all.Count; i++)
        {
            RoundParticipant other = all[i];
            if (other == null || other.Role != RoundRole.Monster
                || !other.IsAlive || other.IsSpectating)
                continue;

            float distance = Vector3.Distance(other.transform.position, here);
            if (distance < nearest) nearest = distance;
        }

        if (nearest == float.MaxValue) return 0f;

        return Mathf.Clamp01((DreadFar - nearest) / (DreadFar - DreadNear));
    }

    private RoundParticipant Owner()
    {
        if (owner == null) owner = GetComponentInParent<RoundParticipant>();
        return owner;
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        float master = Mathf.Clamp01(Master);

        if (material == null || master <= 0.001f)
        {
            Graphics.Blit(source, destination);
            return;
        }

        float level = Mathf.Clamp01(dread);

        // Nabız yalnızca dehşetle geliyor: sakinken ekran hiç kıpırdamıyor.
        // Sürekli atan bir vinyet birkaç dakikada göze batmaktan çıkıp
        // yorucu olurdu.
        float pulse = (Mathf.Sin(Time.time * pulseHz * Mathf.PI * 2f) * 0.5f + 0.5f)
            * pulseDepth * level;

        material.SetFloat(VignetteId, (Mathf.Lerp(calmVignette, dreadVignette, level) + pulse) * master);
        material.SetFloat(VignetteStartId, vignetteStart);
        material.SetFloat(VignetteEndId, vignetteEnd);
        material.SetFloat(GrainId, Mathf.Lerp(calmGrain, dreadGrain, level) * master);
        material.SetFloat(GrainFloorId, grainFloor);
        material.SetFloat(GrainSeedId, Random.value * 1000f);
        material.SetFloat(AberrationId, Mathf.Lerp(calmAberration, dreadAberration, level) * master);
        material.SetFloat(DesaturateId, Mathf.Lerp(calmDesaturate, dreadDesaturate, level) * master);
        // Kontrastta "kapalı" 0 değil 1: master 0'a giderken 1'e dönmeli.
        material.SetFloat(ContrastId, Mathf.Lerp(1f, Mathf.Lerp(calmContrast, dreadContrast, level), master));
        material.SetFloat(ContrastPivotId, contrastPivot);
        material.SetFloat(BloomId, bloom * master);
        material.SetFloat(BloomThresholdId, bloomThreshold);
        material.SetFloat(BloomRadiusId, bloomRadius);

        // Parazit yalnızca dehşetin üst yarısında ve karesel artıyor: alt
        // yarıda hiç yok, tavana yaklaşınca hızla açılıyor. Doğrusal olsaydı
        // canavar 15 m ötedeyken bile ekran titrerdi.
        float glitchLevel = Mathf.Clamp01((level - 0.5f) * 2f);
        material.SetFloat(GlitchId, glitchLevel * glitchLevel * glitchStrength * master);

        // 1 ve altı shader'da "kapalı" demek, yani dehşet 0'ken pikselleme yok.
        material.SetFloat(PixelateId, dreadPixelate > 1f ? Mathf.Lerp(1f, dreadPixelate, level) : 0f);

        Graphics.Blit(source, destination, material);
    }
}
