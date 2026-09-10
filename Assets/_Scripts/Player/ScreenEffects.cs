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
    /// Ayarlar ekranına bir kaydırıcı eklendiğinde bağlanacak yer burası.
    /// Statik, çünkü oyuncu her turda prefabtan yeniden doğuyor ve bileşene
    /// yazılan bir değer orada kaybolurdu (bölüm 13'teki ayar kuralı).
    /// </summary>
    public static float Master = 1f;

    [Header("Atmosfer — canavar uzaktayken")]
    [SerializeField] private float calmVignette = 0.38f;
    [SerializeField] private float calmGrain = 0.028f;
    [SerializeField] private float calmAberration = 0.0016f;
    [SerializeField] private float calmDesaturate = 0.10f;

    [Header("Dehşet — canavar dibindeyken")]
    [SerializeField] private float dreadVignette = 0.80f;
    [SerializeField] private float dreadGrain = 0.085f;
    [SerializeField] private float dreadAberration = 0.0065f;
    [SerializeField] private float dreadDesaturate = 0.50f;

    [Tooltip("Dehşet tavanındaki piksel blok boyutu. 0 veya 1 = pikselleme " +
        "KAPALI (varsayılan). Denemek için 4-8 arası bir değer yaz.")]
    [SerializeField] private float dreadPixelate = 0f;

    [Header("Vinyet geometrisi")]
    [Tooltip("Merkeze uzaklık (köşe = 1): kararmanın başladığı yer.")]
    [SerializeField] private float vignetteStart = 0.45f;
    [Tooltip("Tam karardığı yer. 1'in üstü köşeleri tamamen siyah yapmıyor.")]
    [SerializeField] private float vignetteEnd = 1.15f;

    [Header("Dehşetin mesafeyle ilişkisi")]
    [Tooltip("Bu mesafede dehşet TAM (metre).")]
    [SerializeField] private float dreadNear = 5f;
    [Tooltip("Bu mesafenin ötesinde dehşet YOK (metre). Sis görüşü ~25 m.")]
    [SerializeField] private float dreadFar = 22f;
    [Tooltip("Saniyede artış hızı — yaklaşma çabuk hissedilmeli.")]
    [SerializeField] private float dreadRise = 0.9f;
    [Tooltip("Saniyede düşüş hızı. Artıştan YAVAŞ: canavar gittikten sonra " +
        "gerilim bir süre üstünde kalsın.")]
    [SerializeField] private float dreadFall = 0.30f;

    [Header("Nabız")]
    [SerializeField] private float pulseHz = 1.15f;
    [SerializeField] private float pulseDepth = 0.10f;

    private static readonly int VignetteId = Shader.PropertyToID("_Vignette");
    private static readonly int VignetteStartId = Shader.PropertyToID("_VignetteStart");
    private static readonly int VignetteEndId = Shader.PropertyToID("_VignetteEnd");
    private static readonly int GrainId = Shader.PropertyToID("_Grain");
    private static readonly int GrainSeedId = Shader.PropertyToID("_GrainSeed");
    private static readonly int AberrationId = Shader.PropertyToID("_Aberration");
    private static readonly int PixelateId = Shader.PropertyToID("_Pixelate");
    private static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");

    private Material material;
    private RoundParticipant owner;
    private float dread;

    /// <summary>Yerel oyuncunun kamerasına takar; ikinci kez takmıyor.</summary>
    public static void Attach(Camera target)
    {
        if (target == null || target.GetComponent<ScreenEffects>() != null)
            return;

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
        float target = TargetDread();
        float rate = target > dread ? dreadRise : dreadFall;
        dread = Mathf.MoveTowards(dread, target, Time.deltaTime * rate);
    }

    /// <summary>
    /// Canavara olan mesafeden dehşet oranı. Görüş hattı ARANMIYOR: duvarın
    /// arkasındaki canavarın da hissedilmesi gerekiyor, mekaniğin tamamı o.
    /// </summary>
    private float TargetDread()
    {
        if (RoundManager.Instance == null || RoundManager.Instance.Phase != RoundPhase.Playing)
            return 0f;

        RoundParticipant local = Owner();

        // Canavar dehşet görmüyor: "yakında kaçan var" uyarısı doğrudan hile
        // olurdu. Elenen ve kurtulan da görmüyor.
        if (local == null || local.Role != RoundRole.Runner || !local.IsAlive
            || local.IsSpectating || local.IsEscaped)
            return 0f;

        Vector3 here = local.transform.position;
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

        if (nearest == float.MaxValue)
            return 0f;

        return Mathf.Clamp01((dreadFar - nearest) / Mathf.Max(0.01f, dreadFar - dreadNear));
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
        material.SetFloat(GrainSeedId, Random.value * 1000f);
        material.SetFloat(AberrationId, Mathf.Lerp(calmAberration, dreadAberration, level) * master);
        material.SetFloat(DesaturateId, Mathf.Lerp(calmDesaturate, dreadDesaturate, level) * master);

        // 1 ve altı shader'da "kapalı" demek, yani dehşet 0'ken pikselleme yok.
        material.SetFloat(PixelateId, dreadPixelate > 1f ? Mathf.Lerp(1f, dreadPixelate, level) : 0f);

        Graphics.Blit(source, destination, material);
    }
}
