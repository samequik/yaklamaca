using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kaçan ve canavar animatörlerinin ortak tabanı: hız, eğilme ve bakış IK.
///
/// **Animasyon durumu ağdan gönderilmiyor, gözlemleniyor.** Herkes zaten karşı
/// oyuncunun pozisyonunu görüyor (NetworkTransform); hız da iki kare arasındaki
/// farktan çıkıyor. "Şu an koşuyorum" mesajı yollamak aynı bilgiyi ikinci kez
/// göndermek olurdu. Ek ağ trafiği sıfır ve aynı kod hem yerel hem uzak
/// oyuncuda çalışıyor — uzakta `PlayerController` kapalı olduğu için ondan hız
/// zaten okunamazdı (CLAUDE.md bölüm 4 ve 14).
///
/// Eğilme pozisyondan çıkarılamıyor, o yüzden `PlayerController`'dan okunuyor:
/// `PlayerPoseSync` `duckFraction`'ı senkronlayıp uzak oyuncuda uyguluyor,
/// yani değer iki tarafta da doğru.
///
/// **Bu sınıf doğrudan takılmıyor**, `MonsterAnimator` ve `RunnerAnimator`
/// ondan türüyor. Soyutlama spekülatif değil: iki somut kullanım var
/// (CLAUDE.md bölüm 5).
/// </summary>
public abstract class CharacterAnimatorBase : MonoBehaviour
{
    [SerializeField] protected Animator animator;

    [Tooltip("Eğilme oranı buradan okunuyor. PlayerPoseSync senkronladığı için " +
        "uzak oyuncuda da doğru.")]
    [SerializeField] protected PlayerController controller;

    [Header("Hız")]
    [Tooltip("Koşu animasyonunun kendi ilerleme hızı (m/s). Oynatma hızı buna " +
        "bölünerek ayarlanıyor ki ayaklar yerde kaymasın.")]
    [SerializeField] private float clipRunSpeed = 4f;

    [Tooltip("Oynatma hızı çarpanının sınırları. Tam orantı bacakları gülünç " +
        "şekilde çırpıyor; biraz kayma, çok hızlı animasyondan iyi.")]
    [SerializeField] private Vector2 playbackScaleRange = new Vector2(0.7f, 1.6f);

    [Tooltip("Hız yumuşatma. NetworkTransform ara değerleri kare kare " +
        "zıplayabiliyor; ham fark doğrudan kullanılırsa animasyon titriyor.")]
    [SerializeField] private float speedSmoothing = 12f;

    [Header("Bekleme kırılımı")]
    [Tooltip("Kaç saniye kıpırdamayınca esneme/gerinme klibi oynasın. " +
        "Kısa olursa oyuncu her durduğunda oynar ve bıktırır.")]
    [SerializeField] private float idleBreakDelay = 14f;

    [Tooltip("Bu hızın altındaki her şey 'duruyor' sayılıyor (m/s).")]
    [SerializeField] private float idleSpeedThreshold = 0.15f;

    [Tooltip("Kaç farklı kırılım klibi var. Denetleyici bu kadarını kuruyor.")]
    [SerializeField] private int idleBreakVariants = 4;

    [Header("Bakış")]
    [Tooltip("Bakış yönünü veren transform — oyuncunun kamerası. Uzak " +
        "oyuncuda da doğru: PlayerPoseSync dikey bakışı senkronluyor.")]
    [SerializeField] private Transform lookSource;

    [Tooltip("Kafanın bakış yönüne dönme gücü. 1 = tamamen, 0 = hiç.")]
    [Range(0f, 1f)]
    [SerializeField] private float lookWeight = 0.8f;

    [Tooltip("Gövdenin bakışa katılma payı. Yüksek değer animasyonu bozuyor; " +
        "dönmesi gereken kafa, gövde değil.")]
    [Range(0f, 1f)]
    [SerializeField] private float lookBodyWeight = 0.15f;

    [Tooltip("Bakış noktasının kaç metre ileriye konacağı. Yakın nokta kafayı " +
        "aşırı çeviriyor.")]
    [SerializeField] private float lookDistance = 12f;

    // Animator parametre adları. Kurulum araçları (MonsterSetup, RunnerSetup)
    // aynı adları kullanıyor — birini değiştirirsen diğerini de değiştir.
    public const string SpeedParameter = "Speed";
    public const string SpeedScaleParameter = "SpeedScale";
    public const string CrouchParameter = "Crouch";

    /// <summary>
    /// Gidiş yönü, karakterin KENDİ eksenlerinde: ileri/geri ve sağa/sola.
    ///
    /// Yalnızca hız yetmiyor: aynı 2 m/s ileri yürümek de olabilir geri geri
    /// gitmek de. Yön animasyonu olan bir pakette (Unity-chan) ikisi bambaşka
    /// klipler, yani ekranda geri giderken ileri yürüyen bir karakter oluyordu.
    ///
    /// Değerler koşu hızına göre normalleniyor: 1 = tam hızda ileri, -1 = tam
    /// hızda geri. Böylece karışım ağacının eşikleri modelin hızından bağımsız.
    /// </summary>
    public const string ForwardParameter = "Forward";

    public const string StrafeParameter = "Strafe";

    /// <summary>
    /// Uzun süre kıpırdamayınca oynatılan bekleme kırılımı (esneme, gerinme).
    ///
    /// Tetik + bir de "hangisi" sayısı: Unity'nin geçişlerinde rastgele seçim
    /// yok, o yüzden varyantı kod seçip sayıyla söylüyor. Tek klip olsaydı
    /// üçüncü tekrarda ezberlenirdi.
    /// </summary>
    public const string IdleBreakTrigger = "IdleBreak";

    public const string IdleVariantParameter = "IdleVariant";

    private Vector3 lastPosition;
    private float smoothedSpeed;
    private Vector3 smoothedLocalVelocity;

    /// <summary>
    /// Konumun en son GERÇEKTEN değiştiği andan beri geçen süre.
    ///
    /// Uzak oyuncunun konumu ağdan geliyor ve her karede yenilenmiyor:
    /// `NetworkTransform` 20 Hz gönderiyor, ekran 60+ FPS çiziyor ve anlık
    /// görüntü tamponu boşalınca konum tamamen DONUYOR. O karelerde fark
    /// sıfır — ama bu "durdu" demek değil, "yeni bilgi gelmedi" demek.
    /// </summary>
    private float staleTime;

    /// <summary>Son GEÇERLİ ölçümden kalan yatay hız (m/s).</summary>
    private float rawSpeed;

    /// <summary>Son GEÇERLİ ölçümden kalan yatay hız vektörü (dünya).</summary>
    private Vector3 rawVelocity;

    // Hangi parametrelerin VAR olduğu. Olmayan bir parametreye yazmak Unity'de
    // her karede konsola uyarı bastırıyor: canavarın ve yön klibi olmayan
    // kostümlerin denetleyicisinde `Forward`/`Strafe` yok.
    private HashSet<string> available;

    private float idleSeconds;

    /// <summary>Yatay hız (m/s), pozisyon farkından ve yumuşatılmış.</summary>
    protected float SmoothedSpeed => smoothedSpeed;

    /// <summary>
    /// Karakterin YÜRÜME/KOŞMA animasyonunda olma oranı: 0 = duruyor ya da
    /// eğilmiş, 1 = tam ayakta hareket.
    ///
    /// `PlayerBodyVisual` gövde açısını bununla ölçekliyor (bkz. domuz
    /// katilin sopalı yürüyüşü): açı yalnızca o klipler oynarken devreye
    /// girmeli, boşta durmada ve saldırıda girmemeli. Sert bir açma/kapama
    /// gövdeyi zıplatırdı — karışım oranıyla çarpınca animasyonla birlikte
    /// yumuşakça geliyor.
    ///
    /// Eşikler canavarın karışım ağacıyla aynı dili konuşuyor (`Speed`
    /// m/s: yürüme 1.8, koşma 6).
    /// </summary>
    public float LocomotionBlend
    {
        get
        {
            float crouch = controller != null
                ? Mathf.Clamp01(controller.DuckFraction)
                : 0f;

            return Mathf.InverseLerp(0.4f, 1.8f, smoothedSpeed) * (1f - crouch);
        }
    }

    /// <summary>Dikey hız (m/s), pozisyon farkından. Havada olma bundan çıkıyor.</summary>
    protected float VerticalSpeed { get; private set; }

    /// <summary>Bu animatörün kullandığı bütün tetikleyiciler — bkz. SetTriggerExclusive.</summary>
    protected abstract string[] Triggers { get; }

    protected virtual void Awake()
    {
        if (controller == null)
            controller = GetComponent<PlayerController>();

        lastPosition = transform.position;
    }

    protected virtual void OnEnable()
    {
        lastPosition = transform.position;
        staleTime = 0f;
        rawSpeed = 0f;
        rawVelocity = Vector3.zero;

        if (available == null)
            CacheParameters();
    }

    /// <summary>
    /// Sürülecek `Animator`'ı değiştirir — kostüm değişince çağrılıyor
    /// (bkz. PlayerBodyVisual).
    ///
    /// Her kostümün kendi modeli, kendi iskeleti ve kendi `Animator`'ı var;
    /// aynı anda yalnızca biri açık. Bileşen oyuncunun KÖKÜNDE duruyor ve
    /// gövdenin içindeki animatöre bir alanla bakıyor, yani kostüm
    /// değiştiğinde o alanın da yeni gövdeyi göstermesi gerekiyor. Alan
    /// güncellenmezse kapalı bir animatöre yazılır ve karakter T-pozunda
    /// donar — hiçbir yerde hata yazmadan.
    /// </summary>
    public void UseAnimator(Animator value)
    {
        if (animator == value)
            return;

        animator = value;
        CacheParameters();

        // Hız yumuşatması sıfırlanıyor: yeni animatör eskisinin biriktirdiği
        // hızla başlarsa kostüm değiştiren oyuncu bir an koşuyormuş gibi
        // görünür.
        smoothedSpeed = 0f;
        smoothedLocalVelocity = Vector3.zero;
        idleSeconds = 0f;
        lastPosition = transform.position;

        rawSpeed = 0f;
        rawVelocity = Vector3.zero;
        staleTime = 0f;
    }

    /// <summary>
    /// Denetleyicide hangi parametrelerin olduğunu bir kez okuyor.
    ///
    /// Olmayan bir parametreye yazmak Unity'de **her karede** konsola uyarı
    /// bastırıyor. Canavarın denetleyicisinde yön parametreleri yok, yön
    /// klibi olmayan kostümlerde de yok — kontrolsüz yazmak konsolu
    /// kullanılmaz hâle getirirdi.
    /// </summary>
    private void CacheParameters()
    {
        available = new HashSet<string>();

        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
            available.Add(parameter.name);
    }

    private bool Has(string parameter) => available != null && available.Contains(parameter);

    protected virtual void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        UpdateSpeed();
        UpdateCrouch();
    }

    /// <summary>
    /// Konumun hiç kıpırdamadığı sayılacağı eşik (metre). Tampon boşalınca
    /// `NetworkTransform` son değeri OLDUĞU GİBİ tutuyor, yani fark tam
    /// sıfır çıkıyor; eşik yalnızca kayan nokta gürültüsüne karşı.
    /// </summary>
    private const float PositionEpsilon = 1e-5f;

    /// <summary>
    /// Konum bu kadar süre hiç değişmediyse oyuncu GERÇEKTEN durmuştur.
    ///
    /// Olağan ağ boşluğundan (50 ms gönderim + jitter) belirgin şekilde uzun,
    /// ama duruşu geç fark ettirmeyecek kadar kısa. `onlySyncOnChange` açık
    /// olduğu için duran bir oyuncu hiç paket göndermiyor — bu süre olmasaydı
    /// son hızında sonsuza kadar koşar görünürdü.
    /// </summary>
    private const float StaleTimeout = 0.25f;

    /// <summary>
    /// Hızı ve dikey hızı pozisyon farkından çıkarır.
    ///
    /// ### Fark, BİR KAREYE değil GEÇEN SÜREYE bölünüyor (2026-09-23)
    ///
    /// Eski kod `delta / Time.deltaTime` diyordu ve bu, konumun ağdan geldiği
    /// bir oyuncuda yanlış: tampon boşalınca konum 2-3 kare sabit kalıyor,
    /// sonra tek karede yetişiyor ve o karenin farkı gerçekte 50 ms'lik bir
    /// hareketi taşıyor. Bir kareye bölmek sonucu **üçe katlıyordu**:
    ///
    /// | Gerçek Y hareketi (50 ms'de) | Eski | Doğru |
    /// |---|---|---|
    /// | 2 cm | 1.20 m/s | 0.40 m/s |
    /// | 3.3 cm | **1.98 m/s** | 0.66 m/s |
    /// | 5 cm | 3.00 m/s | 1.00 m/s |
    ///
    /// **Sonucu `RunnerAnimator`'da görülüyordu.** `riseThreshold` 2.0: yani
    /// 3.3 cm'lik sıradan bir Y sıçraması (zıplama, eğilme, basamağa basma)
    /// `airborne` bayrağını kuruyordu. Bayrak sonra İNMİYORDU, çünkü iki
    /// çıkış şartı da kapanıyor: biri `falling` istiyor (yalnızca hızlı
    /// düşüşte kuruluyor), öbürü dikey hızın `settleTime` (0.12 sn) boyunca
    /// `settleSpeed`in (0.35) altında kalmasını — ama her ~50 ms'de gelen
    /// yetişme karesi 1.2 m/s üretip sayacı sıfırlıyordu.
    ///
    /// Oyunda görünen: kaçan koşarken havada-kalma pozunda donuyor, yani
    /// "takılıyor, animasyonsuz koşuyor". Yatay hız da aynı sebepten
    /// zıplıyordu — donan karelerde sıfıra düşüp yetişme karesinde fırlıyor,
    /// yürüme/koşma karışımı titriyordu.
    ///
    /// ### Yerel oyuncuda hiçbir şey değişmiyor
    ///
    /// Orada konum her karede gerçekten değişiyor, yani `staleTime` hep bir
    /// karelik ve bölen eskisiyle aynı.
    /// </summary>
    private void UpdateSpeed()
    {
        staleTime += Time.deltaTime;

        Vector3 delta = transform.position - lastPosition;

        if (delta.sqrMagnitude > PositionEpsilon * PositionEpsilon)
        {
            lastPosition = transform.position;

            // Bölen GEÇEN SÜRE, bir kare değil.
            float inverse = staleTime > 0f ? 1f / staleTime : 0f;

            // Yalnızca yatay hız: düşerken bacakların koşması yanlış olurdu.
            rawVelocity = new Vector3(delta.x, 0f, delta.z) * inverse;
            rawSpeed = rawVelocity.magnitude;
            VerticalSpeed = delta.y * inverse;

            staleTime = 0f;
        }
        else if (staleTime >= StaleTimeout)
        {
            // Uzun süredir hiç kıpırdamadı: gerçekten duruyor.
            rawVelocity = Vector3.zero;
            rawSpeed = 0f;
            VerticalSpeed = 0f;
        }

        smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed,
            1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));

        animator.SetFloat(SpeedParameter, smoothedSpeed);

        UpdateDirection(rawVelocity);
        UpdateIdleBreak(rawSpeed);

        // Ayak kayması: karakter animasyonun kendi hızından farklı gidiyorsa
        // ayaklar yerde süzülüyor. Oynatma hızını orantılayarak azaltıyoruz,
        // ama sınırlıyoruz.
        float scale = clipRunSpeed > 0f ? smoothedSpeed / clipRunSpeed : 1f;
        animator.SetFloat(SpeedScaleParameter,
            Mathf.Clamp(scale, playbackScaleRange.x, playbackScaleRange.y));
    }

    /// <summary>
    /// Gidiş yönünü karakterin kendi eksenlerine çevirip yazıyor.
    ///
    /// Dünya hızı tek başına yetmiyor: aynı 2 m/s ileri de olabilir geri de.
    /// `InverseTransformDirection` onu "ne kadar ileri, ne kadar yana"ya
    /// çeviriyor ve karışım ağacı hangi klibin oynayacağını buradan seçiyor.
    ///
    /// Koşu hızına bölünüyor, yani 1 tam hızda ileri demek. Ağacın eşikleri
    /// böylece modelin gerçek hızından bağımsız kalıyor.
    /// </summary>
    private void UpdateDirection(Vector3 velocity)
    {
        if (!Has(ForwardParameter) && !Has(StrafeParameter))
            return;

        // Değerler METRE/SANİYE olarak yazılıyor, normalleştirilmeden.
        //
        // İlk sürüm `clipRunSpeed`'e bölüyordu ve sayılar tutmuyordu: o alan
        // KLİBİN hızı (4 m/s), karakterinki ise 7.62. Yürüme hızı (3.81 m/s)
        // bölündüğünde 0.95 çıkıyor, yani ağaçtaki KOŞMA örneğinin tam üstüne
        // düşüyordu — yürürken koşma animasyonu oynuyordu. Koşarken de 1.9
        // çıkıp ağacın tamamen dışına taşıyordu.
        //
        // Ham hız kullanmak eşikleri tek eksenli ağaçtakiyle aynı dile
        // sokuyor (`Speed` de m/s): iki ağaç aynı sayıları konuşuyor.
        Vector3 target = transform.InverseTransformDirection(velocity);

        smoothedLocalVelocity = Vector3.Lerp(smoothedLocalVelocity, target,
            1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));

        if (Has(ForwardParameter))
            animator.SetFloat(ForwardParameter, smoothedLocalVelocity.z);

        if (Has(StrafeParameter))
            animator.SetFloat(StrafeParameter, smoothedLocalVelocity.x);
    }

    /// <summary>
    /// Uzun süre kıpırdamayan karakterde esneme/gerinme klibini tetikliyor.
    ///
    /// Sayaç HAM hızla sıfırlanıyor, yumuşatılmışla değil: yumuşatma duruşun
    /// ilk saniyesini hâlâ hareketli gösteriyor ve sayaç geç başlardı.
    ///
    /// Varyantı kod seçiyor, çünkü Unity'nin geçişlerinde rastgelelik yok.
    /// </summary>
    private void UpdateIdleBreak(float rawSpeed)
    {
        if (!Has(IdleBreakTrigger))
            return;

        if (rawSpeed > idleSpeedThreshold)
        {
            idleSeconds = 0f;
            return;
        }

        idleSeconds += Time.deltaTime;

        if (idleSeconds < idleBreakDelay)
            return;

        idleSeconds = 0f;

        if (Has(IdleVariantParameter) && idleBreakVariants > 0)
            animator.SetInteger(IdleVariantParameter, Random.Range(0, idleBreakVariants));

        animator.SetTrigger(IdleBreakTrigger);
    }

    private void UpdateCrouch()
    {
        if (controller != null)
            animator.SetFloat(CrouchParameter, controller.DuckFraction);
    }

    /// <summary>
    /// Kafayı bakış yönüne çevirir.
    ///
    /// Animator'ın kendi bakış IK'sı kullanılıyor: animasyonun üstüne yazmıyor,
    /// **karıştırıyor.** Elle kemik döndürmek animasyonu ezerdi ve koşarken
    /// kafa gövdeden kopuk dururdu.
    ///
    /// Çalışması için katmanın IK Pass'i açık olmalı — kurulum aracı açıyor.
    /// Kapalıyken bu metot hiç çağrılmıyor ve sessizce hiçbir şey olmuyor.
    /// </summary>
    protected virtual void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || lookSource == null)
            return;

        animator.SetLookAtWeight(lookWeight, lookBodyWeight, 1f, 0f, 0.6f);
        animator.SetLookAtPosition(lookSource.position + lookSource.forward * lookDistance);
    }

    /// <summary>
    /// Bir tetikleyiciyi kurarken diğerlerini temizler.
    ///
    /// Unity'de tetikleyici, bir geçiş onu tüketene kadar kurulu kalıyor. Yeni
    /// bir tetikleyici kurulduğunda eskisi bir geçiş bulamadıysa bayrak asılı
    /// kalıyor ve **bir sonraki sefer** yanlış animasyon kendiliğinden
    /// oynuyordu — canavarın kimseyi tutmadan yumruklaması buradan geliyordu.
    /// </summary>
    protected void SetTriggerExclusive(string trigger)
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        string[] all = Triggers;

        for (int i = 0; i < all.Length; i++)
            animator.ResetTrigger(all[i]);

        animator.SetTrigger(trigger);
    }
}
