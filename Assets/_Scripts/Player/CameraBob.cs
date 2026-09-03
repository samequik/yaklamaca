using UnityEngine;

/// <summary>
/// Kamerayı yürüyüş ritmine göre sallar.
///
/// Salınımın fazı adım sayısından hesaplanıyor: her `strideLength` metrede bir
/// tam döngü. Dikey salınım adım başına bir kez, yanal salınım ve yalpalama iki
/// adımda bir — insan yürüyüşünün ritmi bu. Frekansı doğrudan sayı olarak
/// vermek (eski hâli) 10-20 Hz'lik titremeye yol açıyordu.
///
/// strideLength'i FootstepAudio ile aynı tutmak önemli; ikisi ayrışırsa kamera
/// ve ayak sesi farklı ritimde gider, kulağa ve göze yanlış gelir.
///
/// LateUpdate'te çalışıyor: PlayerController göz yüksekliğini Update'te mutlak
/// değer olarak yazıyor, biz üstüne sapmayı ekliyoruz. Böylece eğilme ile
/// sallanma birbirini bozmuyor ve sapma birikmiyor.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class CameraBob : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;

    [Header("Ritim")]
    [Tooltip("Kaç metrede bir tam salınım döngüsü. FootstepAudio'daki değerle aynı olmalı.")]
    [SerializeField] private float strideLength = 2f;

    [Tooltip("Bu hızın altında salınım durur (u/s).")]
    [SerializeField] private float minSpeed = 40f;

    [Tooltip("Bu hızın üstü koşma sayılır (u/s).")]
    [SerializeField] private float sprintThreshold = 300f;

    [Header("Salınım Genlikleri (tam şiddette)")]
    [Tooltip("Aşağı-yukarı sapma, metre. Adım başına bir kez.")]
    [SerializeField] private float verticalAmplitude = 0.042f;

    [Tooltip("Sağa-sola sapma, metre. İki adımda bir.")]
    [SerializeField] private float horizontalAmplitude = 0.032f;

    [Tooltip("Yana yatma, derece. Yürüyüşü insani kılan asıl şey bu — küçük tut.")]
    [SerializeField] private float rollAmplitude = 0.7f;

    [Header("Şiddet")]
    [Range(0f, 1f)] [SerializeField] private float walkIntensity = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float sprintIntensity = 1f;
    [Range(0f, 1f)] [SerializeField] private float crouchIntensity = 0.35f;

    [Tooltip("Şiddet değişiminin yumuşaması. Düşük değer = yavaş açılıp kapanan salınım.")]
    [SerializeField] private float intensitySmoothing = 6f;

    [Header("İniş / Zıplama Tepkisi")]
    [Tooltip("Yayın sertliği. Yüksek = hızlı toparlanma.")]
    [SerializeField] private float landStiffness = 110f;

    [Tooltip("Yayın sönümü. Düşük değer kamerayı zıplatır.")]
    [SerializeField] private float landDamping = 15f;

    [Tooltip("Düşme hızının kaçta kaçı sarsıntıya çevrilecek (m/s → m/s). " +
        "0.11 ≈ normal zıplama inişinde 5 cm çökme.")]
    [SerializeField] private float landImpactRatio = 0.11f;

    [Tooltip("Zıplama anındaki yukarı tepki hızı (m/s). Bhop'ta sürekli tekrarlandığı için küçük.")]
    [SerializeField] private float jumpKickSpeed = 0.12f;

    [Tooltip("Sarsıntının üst sınırı (metre) — güvenlik kelepçesi.")]
    [SerializeField] private float maxSpringOffset = 0.12f;

    private PlayerController controller;

    private float bobPhase;
    private float intensity;

    private float springOffset;
    private float springVelocity;

    private bool wasGrounded = true;
    private float lastAirborneVerticalSpeed;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
    }

    private void OnEnable() => controller.Jumped += OnJumped;

    private void OnDisable() => controller.Jumped -= OnJumped;

    private void OnJumped()
    {
        springVelocity += jumpKickSpeed;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null)
            return;

        // Elenince PlayerController kapatılıyor ve kamerayı SpectatorController
        // sürüyor; o sırada karışmıyoruz.
        if (!controller.enabled)
            return;

        UpdateSpring();
        UpdateRhythm();
        ApplyOffset();
    }

    private void UpdateSpring()
    {
        bool grounded = controller.IsGrounded;

        if (!grounded)
            lastAirborneVerticalSpeed = controller.VerticalSpeed;

        if (grounded && !wasGrounded)
        {
            // Düşme hızı Source biriminde; metreye çevirmeden çarpmak
            // sarsıntıyı ~50 kat büyütüyordu.
            float fallSpeed = Mathf.Abs(Mathf.Min(lastAirborneVerticalSpeed, 0f))
                * PlayerController.UnitsToMeters;

            springVelocity -= fallSpeed * landImpactRatio;
        }

        wasGrounded = grounded;

        // Yay-sönüm: hedefi sıfır olan sönümlü salınım.
        springVelocity += (-landStiffness * springOffset - landDamping * springVelocity) * Time.deltaTime;
        springOffset += springVelocity * Time.deltaTime;

        if (Mathf.Abs(springOffset) > maxSpringOffset)
        {
            springOffset = Mathf.Sign(springOffset) * maxSpringOffset;
            springVelocity = 0f;
        }
    }

    private void UpdateRhythm()
    {
        float speed = controller.HorizontalSpeed;
        bool moving = controller.IsGrounded && speed >= minSpeed;

        float target = 0f;
        if (moving)
        {
            if (controller.IsDucked)
                target = crouchIntensity;
            else
                target = speed >= sprintThreshold ? sprintIntensity : walkIntensity;
        }

        intensity = Mathf.Lerp(intensity, target, intensitySmoothing * Time.deltaTime);

        if (!moving)
            return;

        // Her strideLength metrede bir tam döngü. Yürürken ~1.9, koşarken
        // ~3.8 adım/saniye çıkıyor — insan ritmi bu.
        float distance = speed * PlayerController.UnitsToMeters * Time.deltaTime;
        bobPhase += distance / Mathf.Max(strideLength, 0.01f) * Mathf.PI * 2f;

        if (bobPhase > Mathf.PI * 4f)
            bobPhase -= Mathf.PI * 4f; // iki döngüde bir sarmala, float hassasiyeti bozulmasın
    }

    private void ApplyOffset()
    {
        // Dikey adım başına bir kez, yanal ve yalpalama iki adımda bir.
        float vertical = Mathf.Sin(bobPhase) * verticalAmplitude * intensity;
        float sway = Mathf.Cos(bobPhase * 0.5f);

        float horizontal = sway * horizontalAmplitude * intensity;
        float roll = -sway * rollAmplitude * intensity;

        Vector3 local = cameraTransform.localPosition;

        // x/z mutlak yazılıyor: artımlı yazsaydık sapma her karede birikirdi.
        //
        // z ESKİDEN sıfıra sabitleniyordu ("PlayerController sadece y'ye
        // dokunuyor" varsayımıyla). Eğilirken kamerayı öne alan pay gelince o
        // varsayım bozuldu ve pay her karede siliniyordu — Inspector'dan değeri
        // ne yaparsan yap hiçbir şey değişmiyordu. Artık kontrolcüden okunuyor.
        local.x = horizontal;
        local.z = controller != null ? controller.CameraForwardOffset : 0f;
        local.y += vertical + springOffset;

        cameraTransform.localPosition = local;

        // Yalpalama bakış açısının üstüne biniyor; PlayerController her Update'te
        // localRotation'ı mutlak yazdığı için burada çarpmak birikme yapmıyor.
        cameraTransform.localRotation *= Quaternion.Euler(0f, 0f, roll);
    }
}
