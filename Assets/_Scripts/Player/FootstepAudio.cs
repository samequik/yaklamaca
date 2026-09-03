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

    private PlayerController controller;
    private RoundParticipant participant;
    private float distanceSinceStep;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        participant = GetComponent<RoundParticipant>(); // olmayabilir

    }

    // İnişi hareket koduna sormak yerine olaydan dinliyoruz; kare sırasına
    // bağlı bir bayrak okumak güvenilmez olurdu.
    private void OnEnable() => controller.Landed += PlayLanding;

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
        if (source == null || !controller.IsGrounded)
            return;

        float speed = controller.HorizontalSpeed;

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
