using UnityEngine;

/// <summary>
/// Source engine (Half-Life 2 / Garry's Mod) hareket mantığının Unity uyarlaması.
/// Modern platformer kontrolcülerinden farkı: hedef hıza yumuşatarak (Lerp)
/// yaklaşmaz. Bunun yerine her kare hıza sürtünme uygular, sonra bakış yönüne
/// ivme ekler. Airstrafe, momentum koruma ve bunny hop bu iki adımın doğal
/// sonucudur — ayrıca kodlanmış özellikler değildir.
///
/// Tüm hız/ivme değerleri Source biriminde (unit/saniye) tutulur, böylece
/// bilinen cvar değerlerini (sv_friction, sv_accelerate...) doğrudan
/// yazabilirsin. Unity'ye taşınırken metreye çevrilir.
///
/// Kurulum: Yakalamaca > Sahneye Oyuncu Kur
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    /// <summary>1 Source unit = 0.75 inch.</summary>
    public const float UnitsToMeters = 0.01905f;

    /// <summary>Source oyuncu hull'u: ayakta 72u, eğilmiş 36u. Göz hizaları 64u ve 28u.</summary>
    public const float StandingHeightUnits = 72f;
    public const float DuckedHeightUnits = 36f;
    private const float StandingEyeUnits = 64f;
    private const float DuckedEyeUnits = 28f;

    /// <summary>
    /// Zemindeyken uygulanan küçük aşağı hız. İnişli yüzeylerde ve basamaklarda
    /// karakterin zemine yapışık kalmasını sağlar; olmazsa her tümsekte havalanır.
    /// </summary>
    private const float GroundStickSpeed = 50f;

    // Hızlar GMod sandbox varsayılanında. Sürtünme ve ivme ise Source
    // varsayılanlarının üstünde: oyun dar koridorlarda geçtiği için köşe dönmek
    // açık alanda hız yapmaktan daha önemli. İkisi de hızla orantılı çalıştığından
    // sprintSpeed'i değiştirmek dönüş çevikliğini bozmaz.
    [Header("Hız (unit/saniye)")]
    [SerializeField] private float walkSpeed = 200f;    // GMod sandbox varsayılanı (HL2'de 190)
    [SerializeField] private float sprintSpeed = 400f;  // GMod sandbox varsayılanı (HL2 sprint'i 320)
    [SerializeField] private float maxVelocity = 3500f; // sv_maxvelocity, güvenlik tavanı

    [Header("Sürtünme ve İvme (Source cvar karşılıkları)")]
    [SerializeField] private float friction = 5.5f;       // sv_friction 4'tü; yüksek sürtünme = eski yöndeki hız çabuk sönüyor
    [SerializeField] private float stopSpeed = 100f;      // sv_stopspeed
    [SerializeField] private float accelerate = 14f;      // sv_accelerate 10'du; yüksek ivme = yeni yöne daha çabuk geçiş
    [SerializeField] private float airAccelerate = 100f;  // sv_airaccelerate
    [SerializeField] private float airSpeedCap = 30f;     // havada eklenebilen hız tavanı — airstrafe'in sebebi

    // 2026-09-07: yerçekimi 600 -> 900 (havada asılı kalma şikayeti üzerine).
    // jumpPower da BİRLİKTE büyütüldü ki zıplama YÜKSEKLİĞİ aynı kalsın (hâlâ
    // ~60 unit / 1.14 m — iniş sesi eşiği ve iz sistemi bu sayıya bağlı,
    // bkz. CLAUDE.md bölüm 1). Sabit yükseklikte havada geçen süre
    // t = 2*sqrt(2h/g) — g büyüdükçe KISALIYOR, yani aynı zıplama aynı
    // yükseğe çıkıyor ama tepeye daha çabuk varıp daha çabuk iniyor
    // (0.894 sn -> 0.730 sn, yaklaşık %18 daha kısa havada kalma).
    [Header("Zıplama")]
    [SerializeField] private float gravity = 900f;      // sv_gravity
    [SerializeField] private float jumpPower = 328.6f;  // ~60 unit zıplama yüksekliği (aynı, bkz. yukarı)

    [Tooltip("Minecraft usulü sprint sıçraması: koşarken zıplarsan gidiş yönüne " +
        "bir seferlik bu kadar ek hız ekleniyor (u/s). Sprint hızının üstüne " +
        "biniyor, yani 400 + 60 = 460'a fırlıyorsun. Yalnızca ZIPLAMA ANINDA " +
        "bir kez uygulanıyor — havada airstrafe ile kazanılan hızdan (bkz. " +
        "AccelerateInAir/airSpeedCap) ayrı, ona hiç dokunmuyor. 0 = kapalı.")]
    [SerializeField] private float sprintJumpLunge = 60f;

    // Koridorda kapalı: bhop yaparken zemin sürtünmesi hiç uygulanmaz, dönebilmek
    // için tek şansın 30 u/s'lik airstrafe olur — dar alanda kontrolü tamamen kaybedersin.
    [SerializeField] private bool autoBunnyHop = false; // space basılı tutmak zıplamayı tekrarlar

    [Tooltip("Kapalıysa zıplanamaz. Canavarda kapalı — bkz. MovementProfile.canJump.")]
    [SerializeField] private bool canJump = true;

    [Header("Hız birikimi (canavar)")]
    [Tooltip("Kesintisiz koşunca en yüksek hıza eklenen pay (u/s). 0 = kapalı.")]
    [SerializeField] private float boostSpeed;

    [SerializeField] private float boostBuildTime = 3.5f;
    [SerializeField] private float boostDecayTime = 1.2f;

    [Tooltip("Pay ancak bu hızın üstünde koşarken doluyor (u/s).")]
    [SerializeField] private float boostMinSpeed = 380f;

    [Header("Direksiyon (canavar)")]
    [Tooltip("Bu açıya kadar dönmek bedava (derece).")]
    [SerializeField] private float steerFreeAngle = 25f;

    [Tooltip("Bu açıda ceza tam (derece).")]
    [SerializeField] private float steerFullAngle = 80f;

    [Tooltip("Tam cezada dolu payın boşalma süresi (saniye). 0 = kapalı. " +
        "Bkz. MovementProfile.steerScrubTime.")]
    [SerializeField] private float steerScrubTime = 0.35f;

    [Tooltip("Bu hızın altında dönmek bedava (u/s).")]
    [SerializeField] private float steerMinSpeed = 340f;

    [Header("Çarpışma (araba modeli)")]
    [Tooltip("Duvara kafa kafaya girilen hız bu eşiği aşarsa hız kesilir (u/s). " +
        "0 = kapalı. Bkz. MovementProfile.crashSpeed.")]
    [SerializeField] private float crashSpeed;

    [Tooltip("Çarpmadan sonra korunan yatay hız oranı. 0 = tam duruş.")]
    [Range(0f, 1f)]
    [SerializeField] private float crashSpeedRetained;

    [Header("Eğilme (Crouch)")]
    [SerializeField] private float crouchSpeedMultiplier = 0.35f; // GMod'un SetCrouchedWalkSpeed'i gibi, walkSpeed'in oranı

    // Kayma, Source modeline uygun şekilde ayrı bir itme kuvvetiyle değil
    // sürtünmeyi düşürerek yapılıyor: mevcut momentum korunuyor, kendiliğinden
    // sönüyor. Böylece hızlıyken kayma uzun, yavaşken hiç başlamıyor.
    [Header("Kayma (Slide)")]
    [Tooltip("Kayma başlarken eklenen hız (u/s). Profilden ezilir.")]
    [SerializeField] private float slideBoost = 60f;

    [Tooltip("Kayarken sürtünme. Profilden ezilir.")]
    [SerializeField] private float slideFriction = 1.2f;

    [Tooltip("Bu hızın altında kayma başlamaz — yürürken eğilmek kayma sayılmasın.")]
    [SerializeField] private float slideMinStartSpeed = 300f;

    [Tooltip("Bu hızın altına düşünce kayma biter.")]
    [SerializeField] private float slideEndSpeed = 150f;

    [SerializeField] private float maxSlideDuration = 1.1f;
    [SerializeField] private float slideCooldown = 0.7f;

    [Tooltip("Kayarken yön verme gücü. 0 = hiç dönemezsin, 1 = normal. Dar koridorda biraz kontrol şart.")]
    [Range(0f, 1f)]
    [SerializeField] private float slideControl = 0.25f;
    [SerializeField] private float duckTime = 0.4f;               // TIME_TO_DUCK
    [SerializeField] private float unduckTime = 0.2f;             // TIME_TO_UNDUCK

    [Header("Bakış")]
    [SerializeField] private Transform cameraTransform;

    [Tooltip("Eğilirken kameranın ne kadar ÖNE kayacağı (metre). Eğilme " +
        "animasyonunda kafa öne çıkıyor; kamera sadece aşağı inince göz " +
        "gövdenin içinde kalıyor ve kafa ileride duruyordu.")]
    [SerializeField] private float duckedCameraForward = 0.25f;

    [Tooltip("Kameranın yüzeylere koruyacağı en küçük mesafe (metre). Yakın " +
        "kırpma düzleminin KÖŞESİNDEN büyük olmalı — köşe 0.08 kırpma, 60° " +
        "görüş ve 21:9'da 0.142 m. Küçültürsen duvarın içi görünmeye başlar.")]
    [SerializeField] private float cameraProbeRadius = 0.18f;
    [SerializeField] private float maxLookAngle = 89f;

    [Tooltip("Aşağı bakış sınırı (derece). İki rolde de daraltılıyor: tam dibe " +
        "bakınca kendi gövdenin içi görünüyor. Canavarda daha dar, çünkü " +
        "modelin kafası bakış yönüne dönüyor.")]
    [SerializeField] private float maxLookDownAngle = 89f;

    [Tooltip("Ayaktaki göz hizasına eklenen pay (unit). Bkz. " +
        "MovementProfile.eyeHeightOffset — sınırı tavan belirliyor.")]
    [SerializeField] private float eyeHeightOffset;

    [Tooltip("Kamerayı ayaktayken ileri alan pay (metre). Bkz. " +
        "MovementProfile.eyeForwardOffset.")]
    [SerializeField] private float eyeForwardOffset;

    [Header("Zemin Kontrolü")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckDistance = 0.2f;

    private CharacterController controller;
    private IMovementInputSource inputSource;
    private Vector3 velocity; // Source biriminde (u/s), yatay + dikey
    private Vector3 wishDirection;
    private float wishSpeed;
    private float verticalLookRotation;
    private bool isGrounded;
    private float peakHeight; // havadayken ulaşılan en yüksek nokta (metre)

    // 0-1 arası hız payı. Koşarken doluyor, durunca boşalıyor, çarpınca sıfır.
    private float boost;

    // Terminal odağı: hareket kilitli, bakış dar bir koniye sıkışmış.
    private bool isFocused;
    private float focusYaw;       // odağa girerken bakılan yön
    private float focusYawLimit;
    private float focusPitchLimit;

    private float standingHeight;
    private float duckedHeight;
    private float duckFraction; // 0 = tamamen ayakta, 1 = tamamen eğilmiş
    private float cameraRestZ;
    private bool cameraRestCached;
    private float cameraPull;
    private float cameraPullDistance;
    private float cameraPullDuration;
    private float cameraPullTimer;
    private float cameraPullRampIn;
    private float cameraPullRampOut;

    private bool isSliding;
    private float slideTimer;
    private float nextSlideTime;

    private readonly Collider[] overlapBuffer = new Collider[8];
    private readonly RaycastHit[] cameraHits = new RaycastHit[8];

    // Kameranın engel yüzünden gidebileceği en uzak pay. Sonsuz = engel yok.
    private float cameraForwardLimit = float.PositiveInfinity;

    /// <summary>Yatay hız — HUD, ses veya animasyon için (u/s).</summary>
    public float HorizontalSpeed => new Vector2(velocity.x, velocity.z).magnitude;

    /// <summary>
    /// Gerçek gidiş yönü (yatay, birim). Baktığın yönden farklı olabilir —
    /// airstrafe yaparken kafa bir yana, gidiş başka yana.
    /// </summary>
    public Vector3 HorizontalVelocityDirection
    {
        get
        {
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            return horizontal.sqrMagnitude < 0.0001f ? Vector3.zero : horizontal.normalized;
        }
    }

    /// <summary>Dikey hız (u/s). Negatif = düşüyor. İniş sarsıntısı için.</summary>
    public float VerticalSpeed => velocity.y;

    /// <summary>Zıplama anında tetiklenir — ses ve animasyon için.</summary>
    public event System.Action Jumped;

    /// <summary>
    /// Duvara kafa kafaya çarpıp hızın kesildiği anda tetiklenir; parametre
    /// çarpma hızı (u/s). Ses, sarsıntı ve animasyon buraya bağlanacak —
    /// çarpmanın duyulması gerekiyor, yoksa canavar neden durduğunu anlamıyor.
    /// </summary>
    public event System.Action<float> Crashed;

    /// <summary>
    /// Yere değildiği anda tetiklenir; parametre **düşülen mesafe** (metre).
    ///
    /// İniş hızı yerine mesafe veriyoruz çünkü ayarlanabilir olan bu: "1
    /// metreden alçak düşüşte ses çıkmasın" diyebilmek, "200 u/s altında
    /// çıkmasın" demekten çok daha anlaşılır. Kutunun üstüne zıplamak ile
    /// zıplayıp yere inmek arasındaki farkı da doğru ayırıyor — ikisinde de
    /// aynı yükseğe çıkarsın ama kutuya inerken daha az düşersin.
    /// </summary>
    public event System.Action<float> Landed;

    public bool IsGrounded => isGrounded;

    /// <summary>Tamamen eğilmiş mi — havalandırma tetikleyicileri, animasyon vb. için.</summary>
    public bool IsDucked => duckFraction >= 1f;

    /// <summary>Terminal başında mı — hareket kilitli, bakış dar.</summary>
    public bool IsFocused => isFocused;

    /// <summary>
    /// Oyuncuyu bir noktaya odaklar: hareket girdisi yok sayılır, bakış içinde
    /// bulunduğu yönün etrafında dar bir koniye sıkışır.
    ///
    /// Kilidi hareket koduna gömmek yerine girdiyi kesip açıyı kırpıyoruz —
    /// böylece Source hareketinin sürtünme/ivme akışına hiç dokunmuyoruz ve
    /// odak bitince oyuncu tam bıraktığı yerden devam ediyor.
    /// </summary>
    /// <param name="stopMomentum">
    /// Mevcut yatay hız kesilsin mi. Terminalde evet — oyuncu terminalin
    /// önünde kaymaya devam etmemeli. Canavarın saldırısında **hayır**:
    /// atılmanın kendisi bir hız itmesi, kesersek atılma hiç olmuyor.
    /// </param>
    public void BeginFocus(float yawLimit = 35f, float pitchLimit = 12f, bool stopMomentum = true)
    {
        isFocused = true;
        focusYaw = transform.eulerAngles.y;
        focusYawLimit = Mathf.Max(0f, yawLimit);
        focusPitchLimit = Mathf.Max(0f, pitchLimit);

        if (stopMomentum)
            velocity = new Vector3(0f, velocity.y, 0f);
    }

    public void EndFocus() => isFocused = false;

    /// <summary>Eğilme oranı (0-1). Ağda karşı tarafa taşınıyor.</summary>
    public float DuckFraction => duckFraction;

    /// <summary>
    /// Kameranın yerel z'si — **tek kaynak.** İki şey topluyor:
    ///
    /// - **Eğilme payı:** eğilme animasyonunda gövde öne eğilip kafa ileri
    ///   çıkıyor; kamerayı yalnızca aşağı indirmek gözü gövdenin içinde
    ///   bırakıyordu.
    /// - **Geri çekilme:** yakalama animasyonunda kamera biraz geriye gidiyor ki
    ///   canavar kendi öldürüşünü görebilsin.
    ///
    /// `CameraBob` z'yi MUTLAK yazdığı için değeri buradan okumak zorunda;
    /// eskiden sıfıra sabitliyordu ve payı her karede siliyordu.
    /// </summary>
    public float CameraForwardOffset
    {
        get
        {
            float wanted = RawCameraForward;
            return Mathf.Sign(wanted) * Mathf.Min(Mathf.Abs(wanted), cameraForwardLimit);
        }
    }

    /// <summary>
    /// Engel dikkate alınmadan istenen pay.
    ///
    /// Rol payı (`eyeForwardOffset`) eğilme payıyla **çarpılmıyor, toplanıyor**:
    /// ikisi ayrı gerekçeden geliyor ve eğilirken de ikisi birden geçerli.
    /// </summary>
    private float RawCameraForward =>
        cameraRestZ + eyeForwardOffset + duckedCameraForward * duckFraction - cameraPull;

    /// <summary>
    /// Kamerayı geçici olarak geriye çeker. `MonsterAttack` yakalamada
    /// çağırıyor: birinci şahısta kendi öldürme animasyonunu göremiyordun.
    ///
    /// Süreyi ve mesafeyi çağıran veriyor; temizlik gerekmiyor, sayaç kendi
    /// bitiyor. Kilit yarıda kesilse bile kamera geri geliyor.
    /// </summary>
    /// <param name="rampIn">Geri çekilmenin oturma süresi (saniye). 0 = anında.</param>
    /// <param name="rampOut">Sonunda geri gelme süresi (saniye). 0 = anında.</param>
    public void PushCameraBack(float distance, float duration, float rampIn, float rampOut)
    {
        cameraPullDistance = distance;
        cameraPullDuration = Mathf.Max(duration, 0.01f);
        cameraPullTimer = cameraPullDuration;
        cameraPullRampIn = Mathf.Max(rampIn, 0f);
        cameraPullRampOut = Mathf.Max(rampOut, 0f);
    }

    /// <summary>
    /// Geri çekilme. Rampalar 0 ise kamera anında yerine oturup **kilit
    /// boyunca kıpırdamıyor**.
    ///
    /// Başlangıçta yumuşak giriş/çıkış vardı ("hızlı gir, tut, yavaş çık").
    /// Yakalama animasyonunda istenmiyor: animasyon zaten hareketli, kameranın
    /// da kayması görüntüyü okunmaz yapıyordu. Kayma silinince ortaya sabit bir
    /// "omuz üstü" çekim çıkıyor ve öldürme animasyonu izlenebiliyor.
    ///
    /// Rampalar alan olarak duruyor: yumuşak geçiş istenirse
    /// `MonsterAttack`'teki iki sayıyı büyütmek yetiyor.
    /// </summary>
    private void UpdateCameraPull()
    {
        if (cameraPullTimer <= 0f)
        {
            cameraPull = 0f;
            return;
        }

        cameraPullTimer -= Time.deltaTime;

        float elapsed = cameraPullDuration - cameraPullTimer;

        // Sıfır rampa bölmeye girmemeli; 1 yazmak "zaten tamam" demek.
        float rampIn = cameraPullRampIn > 0f ? Mathf.Clamp01(elapsed / cameraPullRampIn) : 1f;
        float rampOut = cameraPullRampOut > 0f
            ? Mathf.Clamp01(cameraPullTimer / cameraPullRampOut)
            : 1f;

        cameraPull = cameraPullDistance * Mathf.Min(rampIn, rampOut);
    }

    /// <summary>Dikey bakış açısı (derece). Ağda karşı tarafa taşınıyor.</summary>
    public float LookPitch => verticalLookRotation;

    /// <summary>
    /// Geçici hız çarpanı. Canavarın bıçak salladıktan sonraki yavaşlaması gibi
    /// dış etkiler bunu kısar. 1 = normal.
    /// </summary>
    public float SpeedMultiplier { get; set; } = 1f;

    /// <summary>
    /// Dışarıdan ani hız ekler (Source birimi). Bıçak atılması gibi tek seferlik
    /// itmeler için — sürtünme bunu doğal biçimde söndürür, ayrı bir sönme
    /// mantığı yazmaya gerek yok.
    /// </summary>
    public void AddVelocity(Vector3 unitsPerSecond)
    {
        velocity += unitsPerSecond;
    }

    /// <summary>
    /// Rol değişince (canavar/kaçan) hareket profilini uygular.
    ///
    /// Sürtünme ve ivme de artık profilde. Eskiden dışındaydı ve gerekçesi
    /// "ikisi de hızla orantılı, role göre değişmesi gerekmiyor"du; canavara
    /// araba benzeri hareket verilince o gerekçe düştü. İki rolün ivmelenme ve
    /// durma karakteri artık kasten farklı (bkz. CLAUDE.md bölüm 1).
    /// </summary>
    public void ApplyMovementProfile(MovementProfile profile)
    {
        if (profile == null)
            return;

        walkSpeed = profile.walkSpeed;
        sprintSpeed = profile.sprintSpeed;
        crouchSpeedMultiplier = profile.crouchSpeedMultiplier;
        slideBoost = profile.slideBoost;
        slideFriction = profile.slideFriction;

        accelerate = profile.accelerate;
        friction = profile.friction;
        airAccelerate = profile.airAccelerate;

        canJump = profile.canJump;
        maxLookDownAngle = profile.maxLookDownAngle;
        eyeHeightOffset = profile.eyeHeightOffset;
        eyeForwardOffset = profile.eyeForwardOffset;

        // Göz hizası değişti: kamerayı hemen yerine oturt, yoksa rol
        // değiştikten sonra bir kare eski hizada kalırdı. Null kontrolü şart —
        // profil, rol SyncVar'ı üzerinden Awake'ten önce de gelebilir.
        if (controller != null)
            ApplyDuckGeometry(duckFraction);

        steerFreeAngle = profile.steerFreeAngle;
        steerFullAngle = profile.steerFullAngle;
        steerScrubTime = profile.steerScrubTime;
        steerMinSpeed = profile.steerMinSpeed;

        boostSpeed = profile.boostSpeed;
        boostBuildTime = profile.boostBuildTime;
        boostDecayTime = profile.boostDecayTime;
        boostMinSpeed = profile.boostMinSpeed;

        crashSpeed = profile.crashSpeed;
        crashSpeedRetained = profile.crashSpeedRetained;
    }

    /// <summary>
    /// Karakteri süren kaynağı değiştirir. Canavar oyuncusu koptuğunda AI'ya,
    /// geri döndüğünde oyuncuya geçmek için.
    /// </summary>
    public void SetInputSource(IMovementInputSource source)
    {
        inputSource = source;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        inputSource = GetComponent<IMovementInputSource>();
        isGrounded = true;

        // İmleç kilidi bilerek burada değil: menü açıkken imleç serbest olmalı
        // ve bu kararı MenuController veriyor (tek sahip kuralı).

        // Ayakta boyu sahnedeki CharacterController'dan alıyoruz ki Inspector'da
        // değiştirilirse eğilme oranları da onunla uyumlu kalsın.
        standingHeight = controller.height;
        duckedHeight = standingHeight * (DuckedHeightUnits / StandingHeightUnits);
    }

    private void Update()
    {
        // Girdi kaynağı oyuncunun klavyesi de olabilir, canavar AI'ı da.
        MovementIntent intent = inputSource != null ? inputSource.Read() : default;

        // Terminal başındayken hareket girdisi yok sayılıyor; bakış ise
        // aşağıda dar bir açıya sıkıştırılıyor. Girdiyi burada kesmek,
        // hareket kodunun geri kalanına hiç dokunmadan kilitlemeyi sağlıyor.
        if (isFocused)
        {
            intent.moveRight = 0f;
            intent.moveForward = 0f;
            intent.sprint = false;
            intent.jumpPressed = false;
            intent.jumpHeld = false;
            intent.crouchPressed = false;
        }

        HandleLook(intent);

        // Eğilmeden ÖNCE: HandleCrouch içindeki ApplyDuckGeometry kamerayı
        // yerine koyuyor ve sınırı orada hazır bulmalı.
        UpdateCameraClearance();

        UpdateSlide(intent);   // eğilmeden önce: kayma zorunlu eğilme getiriyor
        HandleCrouch(intent);
        ReadMovementInput(intent);

        // Source'un FullWalkMove sırası: yerçekiminin yarısı, zıplama,
        // sürtünme + ivme, yerçekiminin diğer yarısı, sonra hareket.
        // Yerçekimini ikiye bölmek zıplama yüksekliğini kare hızından bağımsız kılar.
        ApplyHalfGravity();
        HandleJump(intent);

        if (isGrounded)
        {
            ApplyFriction();
            // Kayarken ivme kısılıyor; yoksa hemen sprint hızına dönüp
            // kayma diye bir şey kalmıyor. Sıfır da değil, çünkü dar koridorda
            // hiç yön veremeden kaymak duvara çarpmak demek.
            Accelerate(wishSpeed, isSliding ? accelerate * slideControl : accelerate);
        }
        else
        {
            AccelerateInAir();
        }

        ApplyHalfGravity();
        ClampVelocity();

        controller.Move(velocity * UnitsToMeters * Time.deltaTime);

        UpdateGroundState();
    }

    private void HandleLook(MovementIntent intent)
    {
        transform.Rotate(Vector3.up * intent.lookYaw);

        // Odaklanmışken gövde dönüşü dar bir koniye sıkışıyor: terminale
        // bakarken omzunun üstünden şöyle bir bakabiliyorsun ama arkanı
        // dönemiyorsun. Canavarın yaklaşmasını görememek, mekaniğin bedeli.
        if (isFocused)
        {
            float delta = Mathf.DeltaAngle(focusYaw, transform.eulerAngles.y);
            float clamped = Mathf.Clamp(delta, -focusYawLimit, focusYawLimit);

            if (!Mathf.Approximately(delta, clamped))
                transform.rotation = Quaternion.Euler(0f, focusYaw + clamped, 0f);
        }

        // Yukarı ve aşağı sınırlar ayrı: pozitif değer aşağı bakmak demek ve
        // canavarda kafa dönüşü yüzünden aşağısı daha dar tutuluyor.
        float upLimit = isFocused ? focusPitchLimit : maxLookAngle;
        float downLimit = isFocused ? focusPitchLimit : maxLookDownAngle;

        verticalLookRotation = Mathf.Clamp(verticalLookRotation - intent.lookPitch,
            -upLimit, downLimit);

        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(verticalLookRotation, 0f, 0f);
    }

    /// <summary>
    /// Kayma durumunu günceller.
    ///
    /// Başlama koşulu: zemindeyken Ctrl'e basmak ve yeterince hızlı olmak.
    /// "Koşarak zıplayıp inince Ctrl" durumu ayrıca kodlanmadı — iniş anında
    /// zaten zeminde ve hızlı olduğun için aynı koşul kendiliğinden sağlanıyor.
    ///
    /// Ctrl'i bırakmak kaymayı bitirmez; hız düşene ya da süre dolana kadar
    /// sürer. Tuşu basılı tutmayı zorunlu kılmak kaymayı zahmetli bir hareket
    /// haline getiriyordu.
    /// </summary>
    private void UpdateSlide(MovementIntent intent)
    {
        float horizontalSpeed = HorizontalSpeed;

        if (isSliding)
        {
            slideTimer -= Time.deltaTime;

            bool finished = !isGrounded || slideTimer <= 0f || horizontalSpeed < slideEndSpeed;
            if (!finished)
                return;

            isSliding = false;
            nextSlideTime = Time.time + slideCooldown;
            return;
        }

        if (!intent.crouchPressed || !isGrounded)
            return;
        if (Time.time < nextSlideTime)
            return;
        if (horizontalSpeed < slideMinStartSpeed)
            return;

        isSliding = true;
        slideTimer = maxSlideDuration;

        // Mevcut gidiş yönüne küçük bir itme — kaymanın başladığı hissedilsin.
        Vector3 direction = new Vector3(velocity.x, 0f, velocity.z) / horizontalSpeed;
        velocity += direction * slideBoost;
    }

    /// <summary>
    /// Source'un duck mekaniği: hull yukarıdan kısalır (ayaklar sabit kalır),
    /// göz hizası 64u'dan 28u'ya iner. Tavan alçaksa doğrulamaya izin verilmez —
    /// havalandırma boşluğunun ortasında kalkıp duvara sıkışmayı bu engelliyor.
    /// </summary>
    private void HandleCrouch(MovementIntent intent)
    {
        UpdateCameraPull();

        // Kayarken zorla eğik kalınır; Ctrl bırakılsa bile gövde yerde.
        if (intent.crouch || isSliding)
            duckFraction = Mathf.MoveTowards(duckFraction, 1f, Time.deltaTime / Mathf.Max(duckTime, 0.01f));
        else if (CanStandUp())
            duckFraction = Mathf.MoveTowards(duckFraction, 0f, Time.deltaTime / Mathf.Max(unduckTime, 0.01f));

        ApplyDuckGeometry(duckFraction);
    }

    /// <summary>
    /// Eğilme oranını hull'a ve kameraya uygular.
    ///
    /// Kararla (yukarıdaki MoveTowards) geometri bilerek ayrıldı: kararı sadece
    /// oyuncunun kendi istemcisi verebilir — girdi orada — ama geometriyi karşı
    /// tarafın da uygulaması gerekiyor, yoksa eğilen oyuncu diğer ekranlarda
    /// dimdik durur. Karşı tarafta bu metodu PlayerPoseSync çağırıyor.
    /// </summary>
    /// <summary>
    /// Kameranın öne/geriye kayarken duvarın içine girmesini engeller.
    ///
    /// ### Neden gerekiyor
    ///
    /// Yakın kırpma düzlemi bir nokta değil **dikdörtgen**; köşesi kameradan
    /// kırpma mesafesinden uzakta. Kamera gövde ekseninde durduğu sürece bu
    /// yönetilebilir: duvar en fazla `yarıçap − skinWidth ≈ 0.274 m` yaklaşıyor
    /// ve 0.08'lik kırpmanın köşesi 0.14 m'de kalıyor.
    ///
    /// Ama kamera **eksende durmuyor**: eğilirken 0.25 m öne kayıyor (bölüm 1).
    /// Duvara yaslanıp çömelen oyuncunun kamerası duvara 0.024 m kalıyor ve
    /// hiçbir kırpma değeri onu kurtaramıyor. `MonsterAttack`'in yakalamada
    /// uyguladığı 0.7 m'lik geri çekme de arkadaki duvarı deliyor.
    ///
    /// ### Nasıl
    ///
    /// Kameranın **paysız** konumundan istenen yöne bir küre atılıyor; bir şeye
    /// çarparsa pay oraya kadar kısalıyor. Kürenin yarıçapı kırpma düzleminin
    /// köşesinden büyük seçildiği için kamera duvara hep o köşe kadar uzakta
    /// kalıyor.
    ///
    /// Kendi çarpışma kutumuz eleniyor: küre eksenden başlıyor ve gövdenin
    /// içinde. `groundMask` daraltılmadı (bölüm 16'daki bilinçli tercih), o
    /// yüzden filtre maskeyle değil objeyle yapılıyor.
    ///
    /// Yalnızca yerel oyuncuda anlamlı — uzakta bu bileşen kapalı ve kamera
    /// zaten çizmiyor.
    /// </summary>
    private void UpdateCameraClearance()
    {
        cameraForwardLimit = float.PositiveInfinity;

        if (cameraTransform == null || cameraProbeRadius <= 0f)
            return;

        float wanted = Mathf.Abs(RawCameraForward);
        if (wanted <= 0.001f)
            return;

        Vector3 local = cameraTransform.localPosition;
        Vector3 origin = transform.TransformPoint(new Vector3(local.x, local.y, 0f));
        Vector3 direction = RawCameraForward >= 0f ? transform.forward : -transform.forward;

        int count = Physics.SphereCastNonAlloc(origin, cameraProbeRadius, direction,
            cameraHits, wanted, groundMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = cameraHits[i].collider;

            if (hit == null || hit.transform.IsChildOf(transform))
                continue;

            cameraForwardLimit = Mathf.Min(cameraForwardLimit, cameraHits[i].distance);
        }
    }

    public void ApplyDuckGeometry(float fraction)
    {
        // Alanı da yazıyoruz. UZAK oyuncuda bu bileşen kapalı, yani Update hiç
        // çalışmıyor ve duckFraction sonsuza kadar 0 kalıyordu; animatör
        // DuckFraction'a baktığı için karşı taraf eğilen oyuncuyu dimdik
        // görüyordu. Kapsül doğru çalışıyordu çünkü o height'a bakıyor.
        //
        // Yerel oyuncuda çağrı zaten ApplyDuckGeometry(duckFraction) şeklinde,
        // yani bu satır kendi kendine atama.
        duckFraction = fraction;

        float height = Mathf.Lerp(standingHeight, duckedHeight, fraction);
        controller.height = height;

        // Merkez aşağı kayar, böylece kapsül tepeden kısalır ve ayaklar yerinde kalır.
        controller.center = new Vector3(0f, (height - standingHeight) / 2f, 0f);

        if (cameraTransform != null)
        {
            // Ayaktaki z bir kez saklanıyor: her karede okunsaydı eğilme payı
            // kendi üstüne birikirdi.
            if (!cameraRestCached)
            {
                cameraRestZ = cameraTransform.localPosition.z;
                cameraRestCached = true;
            }

            // Pay yalnızca AYAKTAKİ hizaya biniyor: eğilmiş göz hizası olduğu
            // gibi kalıyor. Eğilme geçidi 1.1 m ve orada kamerayı yukarı almak
            // tavanın içini gösterirdi.
            float eyeFromFeet =
                Mathf.Lerp(StandingEyeUnits + eyeHeightOffset, DuckedEyeUnits, fraction)
                * UnitsToMeters;
            Vector3 localPosition = cameraTransform.localPosition;
            localPosition.y = eyeFromFeet - standingHeight / 2f;

            localPosition.z = CameraForwardOffset;

            cameraTransform.localPosition = localPosition;
        }
    }

    /// <summary>
    /// Oyuncuyu bir noktaya taşır ve momentumunu sıfırlar.
    ///
    /// `CharacterController` açıkken transform'a doğrudan yazmak güvenilir
    /// değil — kapatıp açıyoruz (`TestRunnerBot.ServerTeleportTo` ile aynı
    /// gerekçe).
    ///
    /// **Hız ve pay sıfırlanıyor.** Lobide koşarken biriktirilen momentumla
    /// tura başlamak yanlış olurdu; canavar için ayrıca hız payının dolu
    /// başlaması, tasarımın "payı koşarak kazan" kuralını (bölüm 1) tur
    /// başında delerdi.
    ///
    /// **Dikey bakış da sıfırlanıyor:** lobide yere bakan oyuncu tura yere
    /// bakarak başlardı. Yatay dönüş verilen rotasyondan geliyor — kök yalnızca
    /// yaw taşıyor, pitch kamerada.
    /// </summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        if (controller != null)
            controller.enabled = false;

        transform.SetPositionAndRotation(position,
            Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));

        if (controller != null)
            controller.enabled = true;

        velocity = Vector3.zero;
        boost = 0f;
        verticalLookRotation = 0f;

        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Karşı oyuncunun dikey bakışını uygular. Kamera kök objenin child'ı ve
    /// NetworkTransform yalnızca kökü senkronluyor; yatay dönüş (yaw) köke
    /// yazıldığı için karşıya geçiyor ama dikey bakış (pitch) burada kalıyordu.
    /// Bıçak kameranın child'ı olduğu için bu, canavarın bıçağının nereye
    /// baktığıyla ilgisiz durmasına yol açıyordu.
    /// </summary>
    public void ApplyRemoteLook(float pitch)
    {
        verticalLookRotation = pitch;

        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    /// <summary>
    /// Ayağa kalkınca kaplayacağımız hacmin üst kısmında engel var mı?
    /// Sadece eğilmiş boyun üstündeki bölgeye bakıyoruz — alt kısımda zaten
    /// duruyoruz, oraya bakmak zemini engel sanmaya yol açardı.
    /// </summary>
    private bool CanStandUp()
    {
        if (duckFraction <= 0f)
            return true;

        float radius = controller.radius;
        Vector3 feet = transform.position + Vector3.down * (standingHeight / 2f);
        Vector3 checkBottom = feet + Vector3.up * (duckedHeight + radius);
        Vector3 checkTop = feet + Vector3.up * (standingHeight - radius);

        if (checkTop.y <= checkBottom.y)
            return true; // eğilmiş boy ayakta boya çok yakın, kontrol edilecek boşluk yok

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            checkBottom, checkTop, radius * 0.95f, overlapBuffer, groundMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Transform hit = overlapBuffer[i].transform;
            if (hit != transform && !hit.IsChildOf(transform))
                return false;
        }

        return true;
    }

    private void ReadMovementInput(MovementIntent intent)
    {
        // Bakış yönünün yatay izdüşümü — yukarı bakarken ileri gitmek yavaşlamamalı.
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        Vector3 wishVelocity = right * intent.moveRight + forward * intent.moveForward;

        // Pay hesabı istenen yönü bilmek zorunda: köşe dönmenin cezası oradan
        // çıkıyor. `wishDirection` aşağıda yazılıyor, o yüzden değer önden
        // veriliyor — sırayı değiştirmek cezayı bir kare geciktirirdi.
        TickBoost(intent.sprint, wishVelocity.normalized);

        // Eğilirken sprint devre dışı; hız duck oranına göre kademeli düşer.
        // Hız payı yalnızca koşarken ekleniyor: yürürken birikmesi de
        // kullanılması da yanlış olurdu.
        float uprightSpeed = intent.sprint ? sprintSpeed + boostSpeed * boost : walkSpeed;
        float targetSpeed = Mathf.Lerp(uprightSpeed, walkSpeed * crouchSpeedMultiplier, duckFraction)
            * SpeedMultiplier;

        wishDirection = wishVelocity.normalized;
        wishSpeed = Mathf.Min(wishVelocity.magnitude, 1f) * targetSpeed;
    }

    /// <summary>
    /// Canavarın hız payı: **normal hızda başlıyor, kesintisiz koştukça
    /// açılıyor.**
    ///
    /// İlk deneme ivmeyi düşürerek yapılmıştı ve yanlıştı: canavar duruştan
    /// kalkarken de ağır oluyordu, yani her yavaşlama bir ceza gibi
    /// hissettiriyordu. Doğrusu tabanı normal bırakıp **üstüne** eklemek —
    /// araba da duruştan seyir hızına çabuk çıkar, asıl son hıza yavaş varır.
    ///
    /// Pay yalnızca gerçekten koşarken doluyor (`boostMinSpeed`), yoksa
    /// canavar sinsice dolaşıp hız depolayabilirdi. Boşalması dolmasından
    /// hızlı: kazanması emek, kaybetmesi kolay.
    /// </summary>
    private void TickBoost(bool sprinting, Vector3 wish)
    {
        if (boostSpeed <= 0f)
        {
            // Payı olmayan rol (kaçan) buradan çıkıyor — direksiyon cezası da
            // yalnızca payı olanı ilgilendirdiği için kaçana hiç dokunmuyor.
            boost = 0f;
            return;
        }

        float steer = SteerPenalty(wish);

        // Dönerken pay DOLMUYOR: dolmaya devam etseydi ceza ile kazanç aynı
        // karede birbirini yer, köşe bedava kalırdı.
        bool building = sprinting && isGrounded
            && HorizontalSpeed >= boostMinSpeed
            && steer <= 0f;

        float rate;

        if (steer > 0f)
            rate = -steer / Mathf.Max(steerScrubTime, 0.01f);
        else if (building)
            rate = 1f / Mathf.Max(boostBuildTime, 0.01f);
        else
            rate = -1f / Mathf.Max(boostDecayTime, 0.01f);

        boost = Mathf.Clamp01(boost + rate * Time.deltaTime);
    }

    /// <summary>
    /// Direksiyon cezası: keskin dönmek payı siliyor. 0 = ceza yok, 1 = tam.
    ///
    /// ### Neden gerekti
    ///
    /// Canavar koridorda tam hızla koşarken 90°'lik dönüşü **hiçbir bedel
    /// ödemeden** yapabiliyordu. Pay `sprint && grounded && hız ≥ eşik` iken
    /// dolmaya devam ediyor ve bu üç şart dönerken de sağlanıyordu; yani köşe
    /// bedavaydı ve canavar labirenti düz koridor gibi kullanıyordu.
    ///
    /// Bölüm 1 zaten "her köşe onu başa döndürüyor" ve "labirent canavarın
    /// rakibi" diyordu — **niyet yazılıydı, karşılığı kodda yoktu.** Var olan
    /// tek ceza duvara toslamaktı (`TryCrash`) ve iyi oynayan hiç toslamıyor.
    ///
    /// ### Ölçüt bakış değil, GİDİŞ ile İSTEK arasındaki açı
    ///
    /// Bakış hızını ölçmek yanlış olurdu: düz koşarken etrafa bakınmak
    /// cezalandırılırdı ve canavar kör hâle gelirdi. Burada ölçülen şey
    /// direksiyon açısı — hâlihazırda gidilen yön ile gidilmek istenen yön
    /// arasındaki fark. Tuşa basılmıyorsa istek yok, ceza da yok.
    ///
    /// Açı dönüşten sonra kendiliğinden kapanıyor (hız yeni yöne oturuyor),
    /// yani ceza dönüşün **süresince** birikiyor: 90°'lik bir köşe payın kabaca
    /// üçte birini, geri dönüş çok daha fazlasını götürüyor. Ayrıca ayar
    /// gerektirmeyen bir ölçekleme.
    ///
    /// Yavaşken bedava (`steerMinSpeed`): duruştan dönmek zaten doğal ve
    /// eşiğin altında pay da dolmuyor.
    /// </summary>
    private float SteerPenalty(Vector3 wish)
    {
        if (steerScrubTime <= 0f || wish.sqrMagnitude < 0.01f)
            return 0f;

        Vector3 heading = new Vector3(velocity.x, 0f, velocity.z);

        if (heading.magnitude < steerMinSpeed)
            return 0f;

        float angle = Vector3.Angle(heading.normalized, wish);

        return Mathf.InverseLerp(steerFreeAngle, steerFullAngle, angle);
    }

    /// <summary>
    /// Source'ta sürtünme, zıplayan karede uygulanmaz — bunny hop tam olarak budur.
    /// Yere değdiğin anda tekrar zıplarsan hız kesilmez, koştukça hızlanırsın.
    /// </summary>
    private void HandleJump(MovementIntent intent)
    {
        // Canavar zıplayamıyor. Zıplayamamak aynı zamanda bhop yapamamak demek:
        // hızını momentum hilesinden değil, düz koridorda ivmelenerek kazanmalı.
        if (!canJump || !isGrounded)
            return;

        bool wantsToJump = autoBunnyHop ? intent.jumpHeld : intent.jumpPressed;
        if (!wantsToJump)
            return;

        velocity.y = jumpPower;
        isGrounded = false; // bu kare sürtünme uygulanmasın

        // MC usulü sprint sıçraması: koşarken zıplarsan gidiş yönüne bir
        // seferlik ek itki. `intent.sprint` yalnızca tuşun basılı olduğunu
        // söylüyor, fiilen bir yöne basılmıyorsa (`wishDirection` sıfır)
        // "ileri atılmak" diye bir şey yok — olduğu yerde zıplamak bundan
        // muaf. `AccelerateInAir`'in az aşağıda ekleyeceği airstrafe hızından
        // (bkz. airSpeedCap) TAMAMEN AYRI: o zaten mevcut hızın ÜSTÜNE
        // çıkmıyor (bkz. Accelerate'in addSpeed<=0 çıkışı), yani bu itkiyi
        // aynı karede geri almıyor.
        if (intent.sprint && wishDirection.sqrMagnitude > 0.01f)
            velocity += wishDirection * sprintJumpLunge;

        Jumped?.Invoke();
    }

    /// <summary>
    /// sv_friction: hızı yönünü değiştirmeden azaltır. stopSpeed altındaki
    /// hızlarda sabit bir taban kullanılır, böylece yavaşken de net durursun.
    /// </summary>
    private void ApplyFriction()
    {
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        float speed = horizontal.magnitude;

        if (speed < 0.1f)
        {
            velocity.x = 0f;
            velocity.z = 0f;
            return;
        }

        float control = Mathf.Max(speed, stopSpeed);
        float activeFriction = isSliding ? slideFriction : friction;
        float newSpeed = Mathf.Max(speed - control * activeFriction * Time.deltaTime, 0f);

        velocity.x *= newSpeed / speed;
        velocity.z *= newSpeed / speed;
    }

    /// <summary>
    /// Source'un Accelerate'i: hedef hıza yaklaştırmaz, mevcut hızın istenen
    /// yöndeki bileşenine bakar ve aradaki farkı kapatacak kadar ivme ekler.
    /// Yana doğru ivmelenirken toplam hız korunduğu için momentum kaybolmaz.
    /// </summary>
    private void Accelerate(float targetSpeed, float accelerationRate)
    {
        if (wishSpeed <= 0f)
            return;

        float currentSpeed = Vector3.Dot(velocity, wishDirection);
        float addSpeed = targetSpeed - currentSpeed;
        if (addSpeed <= 0f)
            return;

        float accelSpeed = Mathf.Min(accelerationRate * wishSpeed * Time.deltaTime, addSpeed);
        velocity += wishDirection * accelSpeed;
    }

    /// <summary>
    /// Havada hedef hız airSpeedCap'e (30 u/s) kırpılır. Airstrafe'in tamamı bu
    /// satırdan çıkar: ileri baktığında zaten 30'un üstünde olduğun için hız
    /// eklenmez, ama yana bakıp yana basınca o yöndeki bileşenin küçük olduğundan
    /// ivme eklenir ve toplam hız büyür. Mouse'u çevirerek hızlanmak bu demek.
    /// </summary>
    private void AccelerateInAir()
    {
        Accelerate(Mathf.Min(wishSpeed, airSpeedCap), airAccelerate);
    }

    private void ApplyHalfGravity()
    {
        velocity.y -= gravity * 0.5f * Time.deltaTime;
    }

    private void ClampVelocity()
    {
        velocity = Vector3.ClampMagnitude(velocity, maxVelocity);
    }

    private void UpdateGroundState()
    {
        bool wasGrounded = isGrounded;
        float fallSpeed = velocity.y;

        isGrounded = controller.isGrounded || IsTouchingGround();

        // Havadayken ulaşılan en yüksek nokta; inişte buradan ne kadar
        // düştüğümüzü çıkarıyoruz.
        if (!isGrounded)
            peakHeight = Mathf.Max(peakHeight, transform.position.y);

        // Yere değme anı: havadan zemine geçiş. Zıplama tuşuna basıldığı an
        // değil burası tetiklenmeli — iniş sesi ayağın yere çarptığı anda
        // gelmeli, tuşa basıldığında değil.
        if (isGrounded && !wasGrounded && fallSpeed < 0f)
            Landed?.Invoke(Mathf.Max(0f, peakHeight - transform.position.y));

        if (isGrounded)
            peakHeight = transform.position.y;

        if (isGrounded && velocity.y < 0f)
            velocity.y = -GroundStickSpeed;
    }

    /// <summary>
    /// Ayak hizasında küre sorgusu. CharacterController'ın kendi kapsülü de fizik
    /// sorgularına takıldığı için kendi collider'larımızı eleriz.
    /// </summary>
    private bool IsTouchingGround()
    {
        Vector3 footPosition = transform.position + controller.center
            + Vector3.down * (controller.height / 2f - controller.skinWidth);

        int hitCount = Physics.OverlapSphereNonAlloc(
            footPosition, groundCheckDistance, overlapBuffer, groundMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Transform hit = overlapBuffer[i].transform;
            if (hit != transform && !hit.IsChildOf(transform))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Source'un ClipVelocity'si. CharacterController duvara çarpınca kendi
    /// hareketini keser ama bizim velocity değişkenimiz bunu bilmez; her kare
    /// duvara bastırmaya devam eder ve momentum ölür. Burada hızı yüzey düzlemine
    /// yansıtıyoruz — duvara yalayarak koşarken hız korunur.
    /// </summary>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        float intoSurface = Vector3.Dot(velocity, hit.normal);
        if (intoSurface >= 0f)
            return; // yüzeyden uzaklaşıyoruz, dokunma

        if (TryCrash(hit.normal, -intoSurface))
            return;

        velocity -= hit.normal * intoSurface;
    }

    /// <summary>
    /// Araba modeli: duvara **kafa kafaya** girilirse hız kesilir.
    ///
    /// Ölçüt yüzeye giren hız bileşeni, temas etmiş olmak değil. Koridorda
    /// duvarı sıyırarak koşmak neredeyse sıfır bileşen üretiyor ve
    /// cezalandırılmıyor; dosdoğru toslamak ise tam hızı üretiyor. Aradaki fark,
    /// labirenti canavarın rakibi yapan şey: köşeyi kesemezsen duruyorsun.
    ///
    /// **Zemin hariç.** Yerçekimi her karede zemine bastırıyor; zemini de
    /// çarpışma sayarsak yürürken sürekli duruyorduk.
    /// </summary>
    private bool TryCrash(Vector3 normal, float impactSpeed)
    {
        if (crashSpeed <= 0f || impactSpeed < crashSpeed)
            return false;

        // Dik yüzey mi — zemin ve rampa değil.
        if (Mathf.Abs(normal.y) >= 0.5f)
            return false;

        velocity.x *= crashSpeedRetained;
        velocity.z *= crashSpeedRetained;

        // Biriken pay da gidiyor: asıl ceza bu. Anlık hızı kaybetmek birkaç
        // saniyelik iş, payı yeniden doldurmak koridoru baştan koşmak demek.
        boost = 0f;

        Crashed?.Invoke(impactSpeed);
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        if (cc == null)
            return;

        Vector3 footPosition = transform.position + cc.center
            + Vector3.down * (cc.height / 2f - cc.skinWidth);

        Gizmos.color = Application.isPlaying && isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(footPosition, groundCheckDistance);
    }
}
