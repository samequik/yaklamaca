using UnityEngine;

/// <summary>
/// Hareket hızına göre ayak sesi ve iniş sesi çalar.
///
/// Korku/kovalamaca oyununda ayak sesi bir mekanik: kaçan canavarın yerini
/// sesle tespit eder, canavar da kaçanın. Bu yüzden eğilerek gitmek neredeyse
/// sessiz, koşmak yüksek sesli — hız ile gürültü arasındaki takas oyuncunun
/// vereceği bir karar.
///
/// Adımlar zamanla değil **kat edilen mesafeyle** tetikleniyor; böylece koşarken
/// kendiliğinden sıklaşıyor ve hız değişince ayrıca tempo ayarı gerekmiyor.
/// Koşmak hem daha sık hem daha gürültülü: sıklık mesafeden, seviye hızdan.
///
/// Klipler **tek adım** olmalı, döngü değil. Bir ara elde 9-18 saniyelik
/// yürüme/koşma döngüleri vardı ve bu yöntem onlarla çalışmıyordu (üst üste
/// binip gürültüye dönüyordu); o dönemde döngü + perde oynatma kullanıldı.
/// Klipler tek adıma kırpıldıktan sonra buraya, yani doğru yönteme dönüldü.
///
/// ### Bu bileşen HER oyuncuda çalışmalı (2026-09-05 düzeltmesi)
///
/// Uzun süre `localOnlyComponents` listesindeydi, yani uzak oyuncularda
/// **tamamen kapalıydı**: herkes yalnızca kendi adımını duyuyordu ve canavarın
/// adımı hiçbir kaçana ulaşmıyordu. Oysa ayak sesi bu oyunda bir mekanik —
/// bölüm 5'teki hız/gizlilik takasının üç ayağından biri. Ses 3B ve 28 metreden
/// duyuluyor; kapalı bileşen o takası sessizce yok ediyordu.
///
/// Listeden çıkarıldı. Ama **`PlayerController` uzak oyuncuda hâlâ kapalı**,
/// yani ondan okunan hız ve zemin bilgisi orada donmuş kalıyor (aynı tuzak
/// bölüm 14 ve 17'de de var). Bu yüzden ikisi de gerektiğinde
/// **pozisyondan çıkarılıyor** — animatörlerin yaptığının aynısı, ek ağ
/// trafiği sıfır.
///
/// Ölçüt `controller.enabled`: açıksa (yerel oyuncu) hızı ve zemini doğrudan
/// hareket kodundan almak daha kesin, kapalıysa pozisyon farkına düşülüyor.
/// Eğilme ikisinde de doğrudan okunuyor — `duckFraction`'ı `PlayerPoseSync`
/// senkronluyor.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class FootstepAudio : MonoBehaviour
{
    [Tooltip("Tek seferlik sesler (iniş) buradan çalıyor.")]
    [SerializeField] private AudioSource source;

    [Tooltip("Kaçan adım sesi. Tek adım olmalı — döngü değil.")]
    [SerializeField] private AudioClip lightStep;

    [Tooltip("Canavar adım sesi — daha pes ve ağır. Bu da tek adım olmalı.")]
    [SerializeField] private AudioClip heavyStep;

    [Header("İniş")]
    [Tooltip("Yere değme sesi. Zıplama TUŞUNA basılınca değil, ayak yere " +
        "çarpınca çalıyor.")]
    [SerializeField] private AudioClip landClip;

    [Tooltip("Bu mesafeden az düşüşte ses çıkmaz (metre). Düz zıplama 1.14 m düşüyor; " +
        "kutunun üstüne zıplayınca çok daha az. Eşik ikisinin arasında olmalı ki " +
        "kutuya çıkmak sessiz, yere inmek sesli olsun.")]
    [SerializeField] private float minFallDistance = 0.95f;

    [Tooltip("Bu mesafeden düşünce ses tam açık (metre).")]
    [SerializeField] private float hardFallDistance = 4f;

    [SerializeField] private Vector2 landVolumeRange = new Vector2(0.25f, 0.9f);

    [Header("Ritim")]
    [Tooltip("Bu hızın altında ses çıkmaz (u/s).")]
    [SerializeField] private float minSpeed = 40f;

    [Tooltip("Bu hızın üstü koşma sayılır (u/s). Yürüme 200, sprint 400.")]
    [SerializeField] private float sprintThreshold = 300f;

    [Tooltip("Yürürken kaç metrede bir adım sesi.")]
    [SerializeField] private float walkStride = 1.8f;

    [Tooltip("Koşarken kaç metrede bir adım sesi. Yürümeden büyük olmalı: koşarken " +
        "adım aralığı uzar. Eşit bıraksaydık tempo hızla iki katına çıkıp " +
        "makineli tüfek gibi duyulurdu.")]
    [SerializeField] private float sprintStride = 2.6f;

    [SerializeField] private Vector2 stepPitchRange = new Vector2(0.92f, 1.08f);

    [Header("Ses Seviyesi")]
    [SerializeField] private float walkVolume = 0.5f;
    [SerializeField] private float sprintVolume = 0.85f;

    [Tooltip("Eğilerek gitmek gizlenmenin yolu — kasten çok düşük.")]
    [SerializeField] private float crouchVolume = 0.12f;

    [SerializeField] private Vector2 landPitchRange = new Vector2(0.92f, 1.08f);

    [Header("Uzak oyuncu")]
    [Tooltip("PlayerController kapalıyken havada sayılma eşiği (m/s). Dikey " +
        "hız bunun üstündeyse adım sesi çalmıyor — yoksa zıplayarak geçen " +
        "oyuncu havada adım sesi çıkarırdı.")]
    [SerializeField] private float airborneVerticalSpeed = 2.5f;

    private PlayerController controller;
    private RoundParticipant participant;
    private float distanceSinceStep;

    // Uzak oyuncuda hareket bilgisi pozisyon farkından çıkarılıyor.
    private Vector3 lastPosition;
    private float derivedSpeed;     // u/s — eşiklerle aynı birimde
    private float derivedVertical;  // m/s
    private bool derivedAirborne;
    private float peakHeight;

    /// <summary>
    /// Hareket kodundan okunan değerler güvenilir mi.
    ///
    /// Uzak oyuncuda `PlayerController` kapalı: `Update` çalışmıyor, `velocity`
    /// ve `isGrounded` son karede kaldıkları yerde donuyor. Bileşenin açık olup
    /// olmadığı bunun tam ölçütü.
    /// </summary>
    private bool ControllerLive => controller != null && controller.enabled;

    private bool Grounded => ControllerLive ? controller.IsGrounded : !derivedAirborne;

    private float CurrentSpeed => ControllerLive ? controller.HorizontalSpeed : derivedSpeed;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        participant = GetComponent<RoundParticipant>(); // olmayabilir
    }

    // İnişi hareket koduna sormak yerine olaydan dinliyoruz; kare sırasına
    // bağlı bir bayrak okumak güvenilmez olurdu. Uzak oyuncuda bu olay hiç
    // gelmiyor, orada iniş de pozisyondan çıkarılıyor (TrackMotion).
    private void OnEnable()
    {
        controller.Landed += PlayLanding;

        // Doğduğu karede eski konumla fark almak devasa bir hız üretirdi.
        lastPosition = transform.position;
        derivedAirborne = false;
    }

    private void OnDisable()
    {
        controller.Landed -= PlayLanding;
    }

    /// <summary>
    /// Yere değme sesi. Zıplama tuşuna basıldığında **hiçbir ses çalmıyor**,
    /// bilerek: bu klip bir iniş vuruşu, tuş sesi değil.
    /// </summary>
    private void PlayLanding(float fallDistance)
    {
        // Kısa düşüşler sessiz: kutunun üstüne zıplamak, basamaktan inmek,
        // eğimde sekmek. Bunlar sürekli olduğu için ses çıkarsalar sinir bozar.
        if (source == null || landClip == null || fallDistance < minFallDistance)
            return;

        // Yüksekten düşüş gürlüyor — canavara mesafe hakkında ipucu veriyor.
        float hardness = Mathf.InverseLerp(minFallDistance, hardFallDistance, fallDistance);
        float volume = Mathf.Lerp(landVolumeRange.x, landVolumeRange.y, hardness);

        if (controller.IsDucked)
            volume *= 0.4f; // eğilerek inmek sesi kısıyor

        source.pitch = Random.Range(landPitchRange.x, landPitchRange.y);
        source.PlayOneShot(landClip, volume);
    }

    /// <summary>
    /// Adımlar zamanla değil **kat edilen mesafeyle** tetikleniyor. Koşunca
    /// aynı sürede daha çok yol gidildiği için adımlar kendiliğinden sıklaşıyor —
    /// ayrıca tempo ayarı gerekmiyor. Ses seviyesi de hızla artıyor, yani
    /// koşmak hem daha sık hem daha gürültülü.
    /// </summary>
    private void Update()
    {
        TrackMotion();

        // Elenen ya da kurtulan oyuncunun bedeni sahnede bir süre kalıyor ve
        // izleyiciye geçerken taşınabiliyor; o taşımayı adım sesi sanmayalım.
        if (participant != null && !participant.IsAlive)
            return;

        if (source == null || !Grounded)
            return;

        float speed = CurrentSpeed;

        if (speed < minSpeed)
        {
            // Durunca birikimi sıfırlamıyoruz; tekrar yürüyünce ilk adım hemen
            // gelsin diye bir miktar dolu kalması daha doğal duruyor.
            return;
        }

        distanceSinceStep += speed * PlayerController.UnitsToMeters * Time.deltaTime;

        float stride = speed >= sprintThreshold ? sprintStride : walkStride;
        if (distanceSinceStep < stride)
            return;

        distanceSinceStep = 0f;
        PlayStep(speed);
    }

    /// <summary>
    /// Uzak oyuncunun hızını, dikey hızını ve inişini pozisyon farkından
    /// çıkarır — `CharacterAnimatorBase`'in hız için yaptığının aynısı.
    ///
    /// Yerel oyuncuda da hesaplanıyor ama kullanılmıyor: hesabı koşullu yapmak
    /// `lastPosition`'ı bayatlatır ve rol değişince (host kendi karakterini
    /// devraldığında) ilk kare devasa bir hız üretirdi.
    /// </summary>
    private void TrackMotion()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;

        if (Time.deltaTime <= 0f)
            return;

        float inverse = 1f / Time.deltaTime;

        // Eşikler Source biriminde (u/s), pozisyon farkı metrede: çeviriyoruz
        // ki yerel ve uzak yol aynı sayılarla ayarlansın.
        derivedSpeed = new Vector2(delta.x, delta.z).magnitude * inverse
            / PlayerController.UnitsToMeters;

        derivedVertical = delta.y * inverse;

        if (ControllerLive)
            return;

        // Uzak oyuncuda `Landed` olayı hiç gelmiyor, iniş de buradan çıkıyor:
        // en yüksek nokta ile yere değme arasındaki fark düşülen mesafe.
        bool airborne = Mathf.Abs(derivedVertical) > airborneVerticalSpeed;

        if (airborne)
            peakHeight = derivedAirborne ? Mathf.Max(peakHeight, position.y) : position.y;
        else if (derivedAirborne)
            PlayLanding(Mathf.Max(0f, peakHeight - position.y));

        derivedAirborne = airborne;
    }

    private void PlayStep(float speed)
    {
        AudioClip clip = SelectStep();
        if (clip == null)
            return;

        // Tek klip elimizde; perdeyi hafif oynatmak üst üste aynı sesi
        // duymanın makineleşmiş hissini kırıyor.
        source.pitch = Random.Range(stepPitchRange.x, stepPitchRange.y);
        source.PlayOneShot(clip, SelectVolume(speed));
    }

    private AudioClip SelectStep()
    {
        bool isMonster = participant != null && participant.Role == RoundRole.Monster;
        return isMonster && heavyStep != null ? heavyStep : lightStep;
    }

    private float SelectVolume(float speed)
    {
        if (controller.IsDucked)
            return crouchVolume;

        return speed >= sprintThreshold ? sprintVolume : walkVolume;
    }
}
