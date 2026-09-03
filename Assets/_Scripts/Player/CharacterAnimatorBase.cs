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

    private Vector3 lastPosition;
    private float smoothedSpeed;

    /// <summary>Yatay hız (m/s), pozisyon farkından ve yumuşatılmış.</summary>
    protected float SmoothedSpeed => smoothedSpeed;

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

    protected virtual void OnEnable() => lastPosition = transform.position;

    protected virtual void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        UpdateSpeed();
        UpdateCrouch();
    }

    private void UpdateSpeed()
    {
        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;

        float inverseDelta = Time.deltaTime > 0f ? 1f / Time.deltaTime : 0f;

        // Yalnızca yatay hız: düşerken bacakların koşması yanlış olurdu.
        float rawSpeed = new Vector2(delta.x, delta.z).magnitude * inverseDelta;
        VerticalSpeed = delta.y * inverseDelta;

        smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed,
            1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));

        animator.SetFloat(SpeedParameter, smoothedSpeed);

        // Ayak kayması: karakter animasyonun kendi hızından farklı gidiyorsa
        // ayaklar yerde süzülüyor. Oynatma hızını orantılayarak azaltıyoruz,
        // ama sınırlıyoruz.
        float scale = clipRunSpeed > 0f ? smoothedSpeed / clipRunSpeed : 1f;
        animator.SetFloat(SpeedScaleParameter,
            Mathf.Clamp(scale, playbackScaleRange.x, playbackScaleRange.y));
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
