using UnityEngine;

/// <summary>
/// Hareket hızına göre ayak sesi ve iniş sesi çalar.
///
/// Korku/kovalamaca oyununda ayak sesi bir mekanik: kaçan canavarın yerini
/// sesle tespit eder, canavar da kaçanın. Hız/gizlilik takası **ikili**
/// (2026-09-13, oynanış geri bildirimi): yürümek ve eğilerek gitmek TAMAMEN
/// sessiz, yalnızca gerçek koşu (`sprintThreshold` üstü) ses çıkarıyor.
/// `TrailLeaver`'daki izin eşiğiyle aynı sayı — ikisi hep aynı anda
/// açılıp kapanmalı, yoksa "iz var ama ses yok" gibi tutarsız bir mekanik
/// çıkar.
///
/// Adımlar zamanla değil **kat edilen mesafeyle** tetikleniyor; böylece koşarken
/// kendiliğinden sıklaşıyor ve hız değişince ayrıca tempo ayarı gerekmiyor.
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
    [Tooltip("Bu hızın altında (yürüme, eğilme) hiç ses çıkmaz — yalnızca " +
        "gerçek koşu ses çıkarır (u/s). Yürüme 200, sprint 400. " +
        "`TrailLeaver.minSpeed` ile AYNI sayı olmalı, ikisi hep birlikte " +
        "açılıp kapanıyor (2026-09-13, oynanış geri bildirimi).")]
    [SerializeField] private float sprintThreshold = 300f;

    [Tooltip("Koşarken kaç metrede bir adım sesi.")]
    [SerializeField] private float sprintStride = 2.6f;

    [Header("Canavar — her hızda duyulur")]
    [Tooltip("Canavar YÜRÜRKEN ve EĞİLİRKEN de ses çıkarıyor; yalnızca kaçan " +
        "sessiz kalıyor.\n\nGerekçe: sessizlik KAÇANIN aracı (bölüm 5'teki " +
        "hız/gizlilik takası) — gizlenmesi gereken o. Canavarın zaten sönmeyen " +
        "kırmızı bir hâlesi var, yani yeri baştan belli; onu da sessiz yapmak " +
        "kaçanın erken uyarısını götürüyordu.\n\nBu hızın altında (durma, " +
        "ufak kayma) yine ses yok.")]
    [SerializeField] private float monsterMinSpeed = 60f;

    [Tooltip("Canavar koşmuyorken (yürüme/eğilme) kaç metrede bir adım.\n\n" +
        "Adımlar MESAFEYLE tetikleniyor, yani bu sayı doğrudan tempoyu " +
        "belirliyor. 1.5'te yürüme 0.39 sn/adım çıkıyordu ve koşmanın " +
        "0.36'sıyla neredeyse aynıydı — oynanışta 'yürürken koşma sesi gibi " +
        "geliyor' diye bildirildi. 2.4'te yürüme 0.63 sn/adım: açıkça daha " +
        "ağır bir tempo.")]
    [SerializeField] private float monsterWalkStride = 2.4f;

    [Tooltip("Canavarın yürüme/eğilme adımının sesi. Koşudan belirgin kısık " +
        "(koşu 0.85): sinsice yaklaşmak mümkün olmalı ama duyulmadan değil.")]
    [SerializeField] private float monsterWalkVolume = 0.32f;

    [SerializeField] private Vector2 stepPitchRange = new Vector2(0.92f, 1.08f);

    [Header("Ses Seviyesi")]
    [SerializeField] private float sprintVolume = 0.85f;

    [Tooltip("Canavarın kendi ağır adımı kendi kulağında SIFIR mesafeden " +
        "çalıyor — 3B ses mesafeyle düşmediği için tam seviye rahatsız edici " +
        "geliyor. Yalnızca CANAVARI OYNAYAN kişide bu çarpanla kısılıyor " +
        "(2026-09-13); kaçanlar canavarın adımını hâlâ normal, mesafeyle " +
        "düşen hâliyle duyuyor — ses zaten ağdan gitmiyor, burada değişen " +
        "yalnızca yerel çalma ölçeği.")]
    [SerializeField] private float ownHeavyStepVolumeScale = 0.35f;

    [SerializeField] private Vector2 landPitchRange = new Vector2(0.92f, 1.08f);

    [Header("Uzak oyuncu")]
    [Tooltip("PlayerController kapalıyken havada sayılma eşiği (m/s). Dikey " +
        "hız bunun üstündeyse adım sesi çalmıyor — yoksa zıplayarak geçen " +
        "oyuncu havada adım sesi çıkarırdı.")]
    [SerializeField] private float airborneVerticalSpeed = 2.5f;

    [Tooltip("Hız eşiğin altına düştükten kaç saniye sonra adım birikimi " +
        "siliniyor. SIFIR YAPMA: uzak oyuncunun hızı ağdan gelen konumdan " +
        "çıkıyor ve paket gecikince bir anlığına sıfır görünüyor — hemen " +
        "silmek o oyuncunun adım sesini tamamen susturur.")]
    [SerializeField] private float stopResetDelay = 0.35f;

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

    /// <summary>
    /// Uzak oyuncunun hızındaki yumuşatma zaman sabiti (saniye). Ağdan gelen
    /// konumun donduğu kareleri dolduracak kadar uzun, gerçek duruşu geç
    /// fark ettirmeyecek kadar kısa.
    /// </summary>
    private const float RemoteSpeedSmoothing = 0.12f;

    /// <summary>Hızın eşiğin altında kaldığı süre — `stopResetDelay` için.</summary>
    private float slowTimer;

    private float CurrentSpeed => ControllerLive ? controller.HorizontalSpeed : derivedSpeed;

    /// <summary>
    /// Bu oyuncu canavar mı? `FootstepAudio` düz bir `MonoBehaviour`, rolü
    /// kendisi bilmiyor — `RoundParticipant`'tan okuyor (o bir
    /// `NetworkBehaviour` ve rol zaten SyncVar).
    /// </summary>
    private bool IsMonster =>
        participant != null && participant.Role == RoundRole.Monster;

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
        derivedSpeed = 0f;
        slowTimer = 0f;
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
    /// ayrıca tempo ayarı gerekmiyor.
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

        // ---- CANAVAR: her hızda duyuluyor (2026-09-21) ----
        //
        // 2026-09-13'te "yalnızca koşarken ses" kuralı konulmuştu ve canavarı
        // da kapsıyordu; oynanışta "canavarların ayak sesi yürürken ve
        // eğilirken hiç gelmiyor" diye bildirildi.
        //
        // Sessizlik KAÇANIN aracı (bölüm 5). Canavarın zaten sönmeyen kırmızı
        // bir hâlesi var, yani konumu baştan belli — onu da sessiz yapmak
        // kaçanın erken uyarısını götürüyor, takasın karşılığı ise yok.
        //
        // **Eğilme bayrağına BAKILMIYOR, hıza bakılıyor.** `controller.IsDucked`
        // uzak oyuncuda donmuş (bileşen orada kapalı, bölüm 12) — eğilirken hız
        // zaten `sprintThreshold`'un altına düşüyor, yani sorulacak tek soru
        // hız.
        if (IsMonster)
        {
            if (speed < monsterMinSpeed)
            {
                ReportTooSlow();
                return;
            }

            slowTimer = 0f;

            bool sprinting = speed >= sprintThreshold;

            distanceSinceStep += speed * PlayerController.UnitsToMeters * Time.deltaTime;

            if (distanceSinceStep < (sprinting ? sprintStride : monsterWalkStride))
                return;

            distanceSinceStep = 0f;
            PlayStep(sprinting ? sprintVolume : monsterWalkVolume);
            return;
        }

        // ---- KAÇAN: yalnızca gerçek koşu ----
        //
        // Eğilerek gitmek TAMAMEN sessiz. Normalde eğilirken hız zaten
        // sprintThreshold'un altına düşüyor, ama niyeti örtük bir hız eşiğine
        // bırakmak yerine burada açıkça yazmak daha güvenli.
        if (controller.IsDucked)
        {
            distanceSinceStep = 0f;
            return;
        }

        // Yürümek de TAMAMEN sessiz (2026-09-13: "yürüyünce ne ses ne iz").
        // `TrailLeaver.minSpeed` ile birebir aynı eşik ve aynı davranış:
        // eşiğin altına her düşüşte birikim sıfırlanıyor — ikisi hep aynı
        // anda açılıp kapanmalı.
        if (speed < sprintThreshold)
        {
            ReportTooSlow();
            return;
        }

        slowTimer = 0f;

        distanceSinceStep += speed * PlayerController.UnitsToMeters * Time.deltaTime;
        if (distanceSinceStep < sprintStride)
            return;

        distanceSinceStep = 0f;
        PlayStep(sprintVolume);
    }

    /// <summary>
    /// Hız eşiğin altına düştü: birikimi SİLMEDEN önce biraz bekle.
    ///
    /// ### Neden gecikme var (2026-09-23)
    ///
    /// Burada eskiden doğrudan `distanceSinceStep = 0f` vardı ve uzak
    /// oyuncunun adım sesini ağ koşullarına bağımlı kılıyordu: hız pozisyon
    /// farkından çıktığı için, ağdan yeni konum gelmeyen HER kare "durdu"
    /// gibi okunuyor ve o ana kadar biriken mesafe siliniyordu.
    ///
    /// Sayıyla: canavarın yürüme adımı 2.4 m'de bir çalıyor, yani 3.81 m/s
    /// hızda 0.63 saniyede bir — 60 FPS'te ~38 kare. O 38 karenin BİR
    /// tanesinde bile konum yenilenmezse birikim sıfırlanıyor ve adım hiç
    /// çalmıyordu. "Bazen ayak sesi gelmiyor" şikâyeti tam olarak buydu ve
    /// bağlantı kötüleştikçe artıyordu.
    ///
    /// Gecikme kuralın NİYETİNİ bozmuyor: duran bir oyuncu 0.35 saniye
    /// sonra birikimini yine kaybediyor, yani "durup kalkınca hemen adım
    /// sesi" diye bir kazanç oluşmuyor.
    /// </summary>
    private void ReportTooSlow()
    {
        slowTimer += Time.deltaTime;

        if (slowTimer >= stopResetDelay)
            distanceSinceStep = 0f;
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
        // Eşikler Source biriminde (u/s), pozisyon farkı metrede.
        float instant = new Vector2(delta.x, delta.z).magnitude * inverse
            / PlayerController.UnitsToMeters;

        // **Uzak oyuncuda anlık hız güvenilir DEĞİL, yumuşatılıyor.**
        //
        // Konum ağdan geliyor ve her karede yenilenmiyor: `NetworkTransform`
        // 20 Hz gönderiyor (syncInterval 0.05), ekran 60+ FPS çiziyor. Mirror
        // ara değerleme yapıyor ama anlık görüntü tamponu boşalınca konum
        // DONUYOR — o karelerde fark sıfır ve anlık hız sıfır görünüyor,
        // oyuncu tam hızla koşarken bile.
        //
        // Yumuşatma o boşlukları dolduruyor: birkaç karelik donma hızı
        // sıfıra düşürmüyor, gerçek duruş ise 0.1 sn içinde okunuyor.
        // Yerel oyuncuda bu yola hiç girilmiyor (`CurrentSpeed` orada
        // doğrudan hareket kodundan okuyor), yani yumuşatma kimsenin kendi
        // adımını geciktirmiyor.
        derivedSpeed = ControllerLive
            ? instant
            : Mathf.Lerp(derivedSpeed, instant,
                1f - Mathf.Exp(-Time.deltaTime / RemoteSpeedSmoothing));

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

    private void PlayStep(float baseVolume)
    {
        bool isMonster = IsMonster;
        AudioClip clip = isMonster && heavyStep != null ? heavyStep : lightStep;
        if (clip == null)
            return;

        // Tek klip elimizde; perdeyi hafif oynatmak üst üste aynı sesi
        // duymanın makineleşmiş hissini kırıyor.
        source.pitch = Random.Range(stepPitchRange.x, stepPitchRange.y);

        float volume = baseVolume;

        // Canavarın kendi ekranında dinleyici (kamerası) kaynağın üstünde —
        // yalnızca o durumda kısılıyor, kaçanlar aynı adımı mesafesine göre
        // normal duyuyor (yukarıdaki tooltip). `FootstepAudio` düz bir
        // MonoBehaviour, `isLocalPlayer` yok — `RoundParticipant` üzerinden
        // okunuyor (o bir NetworkBehaviour).
        if (isMonster && participant.isLocalPlayer)
            volume *= ownHeavyStepVolumeScale;

        // 2026-09-14'ten beri burada geçici bir teşhis logu duruyordu:
        // "izlerken canavarın adımı BAZEN duyulmuyor, bazen duyuluyor."
        // Sebep 2026-09-21'de bulundu ve log kaldırıldı.
        //
        // Şikâyet 2026-09-14'te geldi; "yalnızca koşarken ses" kuralı
        // 2026-09-13'te konmuştu — yani bir gün önce. Canavar kovalarken
        // eşiğin (300 u/s) üstünde ve duyuluyor, ama köşe dönünce hız payı
        // siliniyor (bölüm 1'in direksiyon cezası) ve hız eşiğin altına
        // düşüyor: tam o anlarda sessizleşiyordu. "Bazen geliyor bazen
        // gelmiyor" tarifi birebir bu.
        //
        // Mesafe teşhisi de bu yüzden çürümüştü: canavar DİBİNDEYKEN bile
        // duyulmuyordu, çünkü yaklaşırken yavaşlamıştı.
        source.PlayOneShot(clip, volume);
    }
}
