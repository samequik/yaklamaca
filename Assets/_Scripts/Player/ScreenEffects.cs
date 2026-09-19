using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using TMPro;
using UnityEngine.UI;
#endif

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
///
/// ### Retro PSP görünümü (2026-09-19)
///
/// Kullanıcı oyunun "eski PSP oyunları gibi, biraz pikselli" görünmesini
/// istedi. Görüntü yarı çözünürlüğe iniyor (1080p'de 960×540, yani her oyun
/// pikseli 2×2 — daha kaba kademeler denendi ve fazla pikselli bulundu), bütün
/// efektler orada hesaplanıyor ve sonuç nokta süzgeciyle ekrana büyütülüyor;
/// üstüne eski konsolların rengi gibi basamaklı, titreşimli bir renk azaltma
/// biniyor. Ayrıntılar CLAUDE.md bölüm 25'te.
///
/// **Korku kaydırıcısına BAĞLI DEĞİL.** `Master` 0'a çekilse de retro görünüm
/// kalıyor: o oyunun sanat yönü, "gözümü yoruyor" diye kısılan bir efekt değil.
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

    /// <summary>
    /// Retro görünümün bir kademesi: hedeflenen satır sayısı ve kanal başına
    /// renk seviyesi. `Lines` 0 = pikselleme yok, `ColorLevels` 2'nin altı =
    /// renk azaltma yok.
    /// </summary>
    private readonly struct RetroPreset
    {
        public readonly string Name;
        public readonly int Lines;
        public readonly int ColorLevels;

        public RetroPreset(string name, int lines, int colorLevels)
        {
            Name = name;
            Lines = lines;
            ColorLevels = colorLevels;
        }
    }

    /// <summary>
    /// F10 bu sırayla geziyor; ilki varsayılan.
    ///
    /// | Kademe | 1080p'de | Renk |
    /// |---|---|---|
    /// | İnce | 960×540 — her piksel 2×2 | 64 seviye |
    /// | Kapalı | ekranın kendisi | dokunulmuyor — eski görünüm |
    ///
    /// **Kullanıcı F10'la karşılaştırıp İnce'yi seçti (2026-09-19):** "2×2
    /// İnce en iyisi, diğerleri çok pikselli, güzel olmuyor." Listede ondan
    /// kaba üç kademe vardı ve üçü de bu yüzden çıktı: PSP'nin kendi 480×270'i
    /// (32 seviye, ilk varsayılan — "uzaktaki şeyleri çok pikselliyor"), Hafif
    /// 640×360 (ikinci varsayılan) ve PS1'e yakın Kaba 320×180. Reddedilmiş
    /// bir görünümü F10 turunda tutmak, her karşılaştırmada ondan yeniden
    /// geçmek demekti. Sayıları CLAUDE.md bölüm 25'te; geri gelmeleri tek
    /// satır.
    ///
    /// Kapalı karşılaştırma için kalıyor: F10 artık retro açık/kapalı.
    ///
    /// **Satır sayısı bir HEDEF, kesin değer değil.** Gerçek çözünürlük
    /// ekranın TAM SAYI bölümü seçiliyor (`RetroHeightFor`): İnce 1440p'de 540
    /// değil 480, çünkü 1440/480 = 3 ve her oyun pikseli tam 3×3 ekran pikseli
    /// oluyor. 540 olsaydı büyütme 2.67 olur, satırların kimi 2 kimi 3 piksel
    /// kalın çıkar ve kamera dönerken ızgara kayıyormuş gibi titrerdi.
    /// </summary>
    private static readonly RetroPreset[] RetroPresets =
    {
        new RetroPreset("İnce", 540, 64),
        new RetroPreset("Kapalı", 0, 0),
    };

    /// <summary>
    /// Seçili kademe. Statik, çünkü oyuncu her turda prefabtan yeniden doğuyor
    /// ve bileşene yazılan bir değer orada kaybolurdu (`Master` ile aynı
    /// gerekçe). Menünün arkasındaki karakter sahnesi de buradan okuyor.
    /// </summary>
    private static int retroPresetIndex;

    private static RetroPreset CurrentRetro => RetroPresets[retroPresetIndex];

    /// <summary>
    /// Verilen ekran yüksekliğinde retro görünümün çizileceği yükseklik; 0 =
    /// pikselleme yok. Tam sayı katına oturtuluyor, yani her oyun pikseli
    /// ekranda aynı boyda: İnce'de 1080 → 540 (2×), 1200 → 600 (2×), 1440 →
    /// 480 (3×), 2160 → 540 (4×). Oyun ve menüdeki karakter sahnesi
    /// (`MenuStage`) aynı hesabı kullanıyor — iki yerde ayrı yazılsaydı biri
    /// değişince öbürü unutulurdu.
    ///
    /// **Kat 2'nin altında pikselleme KAPALI.** 720, 768 ve 800 satırlık
    /// ekranlarda (eski dizüstüler, Steam Deck) İnce'nin 540 satırı iki kata
    /// oturmuyor ve iki seçenek de kötü: 2× o ekranda 360-400 satır demek,
    /// yani kullanıcının "çok pikselli" bulduğu seviye; kesirli büyütme ise
    /// satırların kimini 1 kimini 2 piksel yapıyor ve kamera dönerken ızgara
    /// titriyor. O ekranların pikselleri zaten fiziksel olarak iri; renk
    /// azaltma yine açık kalıyor.
    ///
    /// Burada bir süre bir "küçük pencere yolu" vardı: kat 2'nin altında hedef
    /// satır sayısı kesirli büyütmeyle yine de kullanılıyordu ki editörün
    /// küçük Game penceresinde pikselleme kaybolmasın. Varsayılan Hafif
    /// (360 satır) iken o yol yalnızca pencerelerde çalışıyordu — "720 satır
    /// ve üstünde kat hep 2 ya da fazla". İnce varsayılan olunca aynı yol
    /// 720/768/800 satırlık GERÇEK tam ekranlarda çalışacaktı; kaldırıldı.
    /// Küçük pencerenin durumunu artık F10 etiketi söylüyor.
    /// </summary>
    public static int RetroHeightFor(int screenHeight)
    {
        int lines = CurrentRetro.Lines;
        if (lines <= 0 || screenHeight <= 0)
            return 0;

        int factor = Mathf.RoundToInt((float)screenHeight / lines);
        return factor < 2 ? 0 : screenHeight / factor;
    }

    [Header("Retro görünüm")]
    [Tooltip("Retro açıkken grenin çarpanı. Gren artık büyük piksel başına " +
        "hesaplanıyor, yani 1080p'de 2×2'lik bloklar hâlinde kıpırdıyor — aynı " +
        "genlik ince grenden daha çok göze batıyor. Kullanıcı bir kez 'ekranda " +
        "pixelimsi şeyler var, göz bozuyor' demişti (bölüm 25), o yüzden " +
        "görünür boy büyüyünce genlik yarıya iniyor.")]
    [SerializeField] private float retroGrainScale = 0.5f;

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
    [Tooltip("0.78'den 0.60'a indirildi (2026-09-13, oynanış geri bildirimi): " +
        "kovalamaca sırasında ekranı neredeyse tamamen kapatıyordu. Dehşet " +
        "duvarın arkasındaki canavarı da hissettirmek için BİLEREK görüş " +
        "hattı aramıyor (bölüm 25) — şikâyet edilen o tasarım değil, şiddetti.")]
    [SerializeField] private float dreadVignette = 0.60f;
    [SerializeField] private float dreadGrain = 0.075f;
    [SerializeField] private float dreadAberration = 0.009f;
    [SerializeField] private float dreadDesaturate = 0.55f;
    [SerializeField] private float dreadContrast = 1.08f;

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
        "tam çözünürlükte kalıyor. 3'ten 2'ye indirildi (2026-09-13): " +
        "kovalamaca sırasında görüşü fazla bozuyordu.")]
    [SerializeField] private float dreadPixelate = 2f;

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

    /// <summary>
    /// Bu mesafenin ötesinde dehşet YOK (metre). Sis görüşü ~25 m.
    /// 22'den 16'ya indirildi (2026-09-13, oynanış geri bildirimi): dört
    /// tüketicinin (ekran, fener titremesi, kamera sarsıntısı, kalp atışı)
    /// HEPSİ bu sabitten besleniyor, yani tek satır değişince dördü birden
    /// daha dar/geç bir yarıçapta devreye giriyor — canavar labirentin
    /// herhangi bir yerinde "hissedilir" olmak yerine gerçekten yaklaşınca.
    /// </summary>
    public const float DreadFar = 16f;
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

    [Tooltip("Retro görünümü açıp kapatır (İnce ↔ Kapalı) — karşılaştırmak " +
        "için. İlk basıştan sonra sol alt köşede hangisinin açık olduğu " +
        "yazıyor. Listenin başındaki kademe varsayılan. Gönderilen build'de " +
        "bu tuş yok.")]
    [SerializeField] private KeyCode debugRetroKey = KeyCode.F10;

    /// <summary>-1 = zorlama yok.</summary>
    private float forcedDread = -1f;

    /// <summary>
    /// Bu Play oturumunda F10'a basıldı mı. Basıldıysa sol alt köşede hangi
    /// kademenin açık olduğu yazıyor ve oturum boyunca orada kalıyor.
    ///
    /// **Neden gerekti (2026-09-19):** F10 kademeyi yalnızca konsola yazıyordu
    /// ve kullanıcı "sırayla değişiyor ama hangisi hangisi anlamıyorum" dedi —
    /// oyunu oynarken konsola bakılmıyor. Görüntüyü değiştiren bir tuş, neye
    /// değiştirdiğini de görüntüde söylemeli.
    ///
    /// **Hiç basılmadıysa etiket yok:** host çoğu zaman editörden açılıyor ve
    /// gerçek bir oyun sırasında köşede sürekli duran bir test yazısı
    /// istenmez. Statik, çünkü oyuncu objesi değişince (odadan çıkıp yeniden
    /// kurmak, tutorial'dan dönmek) etiket kaybolmasın; domain reload açık
    /// olduğu için her Play'de kendiliğinden sıfırlanıyor.
    /// </summary>
    private static bool retroLabelWanted;

    private CanvasGroup retroLabelGroup;
    private TextMeshProUGUI retroLabel;
    private Camera retroLabelCamera;

    // Yazı yalnızca bunlardan biri değişince yeniden kuruluyor: her karede
    // yeni bir string çöp üretirdi (bölüm 2).
    private int retroLabelPreset = -1;
    private int retroLabelWidth = -1;
    private int retroLabelHeight = -1;
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
    private static readonly int RetroSizeId = Shader.PropertyToID("_RetroSize");
    private static readonly int RetroBlockId = Shader.PropertyToID("_RetroBlock");
    private static readonly int ColorLevelsId = Shader.PropertyToID("_ColorLevels");

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
        Debug.Log($"Ekran efekti AÇIK — kamera '{name}', shader '{shader.name}', " +
            $"retro kademe '{CurrentRetro.Name}'. F9 dehşeti zorluyor (kapalı/yarı/tam), " +
            "F10 retro kademeyi değiştiriyor.", this);
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
        // Dehşet zorlamasının erken çıkışından ÖNCE: F9 açıkken de F10
        // çalışmalı, ikisi birlikte denenebilsin.
        if (Input.GetKeyDown(debugRetroKey))
        {
            retroPresetIndex = (retroPresetIndex + 1) % RetroPresets.Length;
            retroLabelWanted = true;

            int height = RetroHeightFor(Screen.height);
            Debug.Log(height > 0
                ? $"Retro kademe: {CurrentRetro.Name} — ekran {Screen.height} satır, " +
                  $"oyun {height} satır ({(float)Screen.height / height:0.##}× büyütme), " +
                  $"{CurrentRetro.ColorLevels} renk seviyesi."
                : $"Retro kademe: {CurrentRetro.Name} — pikselleme yok " +
                  $"(ekran {Screen.height} satır), renk seviyesi " +
                  $"{(CurrentRetro.ColorLevels > 1 ? CurrentRetro.ColorLevels.ToString() : "dokunulmuyor")}.", this);
        }

        UpdateRetroLabel();

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Sol alt köşedeki kademe etiketi: açık kademenin sırası, adı ve bu
    /// pencerede GERÇEKTE hangi çözünürlükte çizildiği.
    ///
    /// **1080p değerini değil pencerenin kendisini yazıyor.** 810 satırın
    /// altındaki bir pencerede — editörün Game penceresi ~500 satırdı — İnce
    /// hiç pikselleşmiyor (`RetroHeightFor`), yani Kapalı'dan ayırt
    /// edilemiyor. "Hangisi hangisi anlamıyorum" şikâyetinin öbür yarısı
    /// buydu; etiket bunu açıkça söylüyor.
    ///
    /// Kendi Canvas'ını kuruyor (`TutorialHud` ile aynı yöntem) ve kameranın
    /// çocuğu oluyor: oyuncu objesiyle birlikte yok oluyor, ayrıca temizlemek
    /// gerekmiyor. Overlay çizildiği için retro efekt ona dokunmuyor, yazı
    /// keskin kalıyor. Menü açıkken HUD'la aynı kuralla gizleniyor.
    ///
    /// Yazılar `Localization`'a eklenmedi, bilerek: etiket gönderilen build'e
    /// girmiyor, yalnızca geliştiricinin gördüğü bir test göstergesi. Kademe
    /// adları da koddakilerin aynısı — kullanıcı "şunu beğendim" derken
    /// doğrudan `RetroPresets`'teki satırı söylüyor.
    /// </summary>
    private void UpdateRetroLabel()
    {
        if (!retroLabelWanted)
            return;

        if (retroLabel == null)
            BuildRetroLabel();

        GameHud.SetVisible(retroLabelGroup, GameHud.Visible);

        int width = retroLabelCamera.pixelWidth;
        int height = retroLabelCamera.pixelHeight;

        if (retroPresetIndex == retroLabelPreset
            && width == retroLabelWidth && height == retroLabelHeight)
            return;

        retroLabelPreset = retroPresetIndex;
        retroLabelWidth = width;
        retroLabelHeight = height;

        retroLabel.SetText(RetroLabelText(width, height));
    }

    private static string RetroLabelText(int screenWidth, int screenHeight)
    {
        RetroPreset preset = CurrentRetro;
        string title = $"<size=135%>Görünüm {retroPresetIndex + 1}/{RetroPresets.Length}:  " +
            $"<b>{preset.Name}</b></size>";

        string detail;
        bool smallWindow = false;

        if (preset.Lines <= 0)
        {
            detail = "retro yok — oyunun eski görünümü";
        }
        else
        {
            int height = RetroHeightFor(screenHeight);

            if (height <= 0)
            {
                detail = "pencere bu kademe için küçük — burada pikselleme hiç görünmüyor";
                smallWindow = true;
            }
            else
            {
                int width = Mathf.Max(1,
                    Mathf.RoundToInt(screenWidth * (float)height / screenHeight));
                int factor = Mathf.RoundToInt((float)screenHeight / height);
                detail = $"oyun {width}x{height}  ·  her piksel {factor}x{factor}";
            }
        }

        string hint = smallWindow
            ? "gerçek görünüm için oyun ekranını büyüt  ·  F10: sonraki"
            : "F10: sonraki";

        return $"{title}\n{detail}\n<color=#9A9AA3>{hint}</color>";
    }

    private void BuildRetroLabel()
    {
        retroLabelCamera = GetComponent<Camera>();

        GameObject root = new GameObject("RetroKademeEtiketi",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Menünün (100) altında: menü açıkken zaten gizleniyor. Tutorial
        // yazısının (20) ve diriltme ekranının (12) üstünde.
        canvas.sortingOrder = 90;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Sol alt köşe boş: sağ üstte mikrofon göstergesi, üst ortada tur
        // satırları, alt ortada tutorial yazısı var.
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(root.transform, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(24f, 24f);
        rect.sizeDelta = new Vector2(430f, 0f);

        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.02f, 0.02f, 0.03f, 0.82f);
        background.raycastTarget = false;

        // Genişlik sabit, yükseklik yazıya göre: uzun satır alta kayıyor.
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject labelObject = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(panel.transform, false);

        retroLabel = labelObject.GetComponent<TextMeshProUGUI>();
        retroLabel.font = TMP_Settings.defaultFontAsset;
        retroLabel.fontSize = 21f;
        retroLabel.alignment = TextAlignmentOptions.TopLeft;
        retroLabel.color = new Color(0.92f, 0.92f, 0.95f, 1f);
        retroLabel.enableWordWrapping = true;
        retroLabel.raycastTarget = false;

        retroLabelGroup = panel.AddComponent<CanvasGroup>();
    }
#endif

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
        RetroPreset retro = CurrentRetro;
        int retroHeight = RetroHeightFor(source.height);
        bool pixelated = retroHeight > 0 && retroHeight < source.height;

        // Korku efektleri kapalı VE retro görünüm yoksa yapılacak iş yok. Retro
        // görünüm `Master`'a bağlı değil: kaydırıcıyı sıfıra çeken oyuncu
        // yine retro görünümü görüyor, yalnızca vinyet/gren/renk kaybı gidiyor.
        if (material == null
            || (master <= 0.001f && !pixelated && retro.ColorLevels < 2))
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
        material.SetFloat(GrainId, Mathf.Lerp(calmGrain, dreadGrain, level) * master
            * (pixelated ? retroGrainScale : 1f));
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
        //
        // **Retro açıkken dehşet pikselleşmesi KAPALI.** Kullanıcı 1080p'de
        // 3 piksellik blokları (≈640×360) "kovalamacada görüşü fazla bozuyor"
        // diye bulmuş ve 2'ye indirtmişti (2026-09-13). Retro taban 960×540
        // (İnce); üstüne blokları ikiye katlamak kovalamacayı 480×270'te —
        // kullanıcının retro kademelerinde de "çok pikselli" bulduğu PSP
        // seviyesinde — oynatmak olurdu, tam da en çok görmen gereken anda.
        // Dehşeti vinyet, renk kaybı, gren ve parazit taşımaya devam ediyor.
        bool dreadPixels = !pixelated && master > 0.001f && dreadPixelate > 1f;
        material.SetFloat(PixelateId, dreadPixels ? Mathf.Lerp(1f, dreadPixelate, level) : 0f);

        material.SetFloat(ColorLevelsId, retro.ColorLevels);

        if (!pixelated)
        {
            material.SetVector(RetroSizeId, new Vector4(source.width, source.height,
                1f / source.width, 1f / source.height));
            material.SetFloat(RetroBlockId, 0f);

            Graphics.Blit(source, destination, material);
            return;
        }

        // **Düşük çözünürlüklü ara hedef.** Genişlik yükseklikle AYNI oranda
        // küçülüyor, yani pikseller kare kalıyor ve en boy oranı korunuyor
        // (1920×1080 → 960×540).
        //
        // Kaynağın tanımı kopyalanıyor ki biçim (HDR ya da sRGB) aynı kalsın;
        // MSAA ve derinlik bu ara adımda anlamsız.
        int retroWidth = Mathf.Max(1,
            Mathf.RoundToInt(source.width * (float)retroHeight / source.height));

        RenderTextureDescriptor descriptor = source.descriptor;
        descriptor.width = retroWidth;
        descriptor.height = retroHeight;
        descriptor.depthBufferBits = 0;
        descriptor.msaaSamples = 1;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;

        // `GetTemporary` Unity'nin kendi havuzundan veriyor: her karede aynı
        // boy istendiği için aynı doku geri geliyor, çöp üretmiyor (bölüm 2).
        RenderTexture low = RenderTexture.GetTemporary(descriptor);

        // NOKTA süzgeci: büyütürken pikseller kenarları keskin, dolu kareler
        // olarak kalıyor. Çift doğrusal süzgeç onları bulanık bir lekeye
        // çevirirdi ve "pikselli" değil "düşük kaliteli" görünürdü.
        low.filterMode = FilterMode.Point;

        material.SetVector(RetroSizeId, new Vector4(retroWidth, retroHeight,
            1f / retroWidth, 1f / retroHeight));
        material.SetFloat(RetroBlockId, 1f);

        // Bütün efektler küçük hedefte, büyük piksel başına BİR kez
        // hesaplanıyor — gren ve renk deseni de piksellerle aynı boyda. Yan
        // kazanç: pahalı geçiş artık dörtte bir piksel sayısında çalışıyor.
        Graphics.Blit(source, low, material);
        Graphics.Blit(low, destination);

        RenderTexture.ReleaseTemporary(low);
    }
}
