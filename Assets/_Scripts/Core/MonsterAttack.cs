using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Canavarın bıçak saldırısı. Dead by Daylight'ın katil mantığı:
/// dokunmak öldürmez, isabet eden bir vuruş öldürür.
///
/// Sol tık basılı tutuldukça vuruş yüklenir; bırakınca canavar öne atılır ve
/// bıçak kısa süre aktif olur. Vuruştan sonra — ıskalasa bile — canavar
/// yavaşlar. Bu yavaşlama mekaniğin dengesi: bıçak spamlanamıyor, ıskalamanın
/// bedeli var, kaçanın son anda yön değiştirmesi işe yarıyor.
///
/// Vuruş sonrası hız cezası kasten ıskalamaya da uygulanıyor; sadece
/// ıskalayınca cezalandırmak "her ihtimale karşı salla" davranışını ödüllendirir.
///
/// ---
///
/// Ağ bölünmesi: **his istemcide, karar sunucuda.**
///
/// Girdi, atılma, bıçak animasyonu ve savurma sesi yerel istemcide anında
/// çalışıyor — bunları sunucuya sorup cevabı beklesek gecikme kadar geç
/// tepki verirdi ve Source hissi giderdi. Hareket zaten istemci otoriteli
/// (NetworkTransform ClientToServer), atılma da hareketin parçası.
///
/// Kimin elendiğine ise **yalnızca sunucu** karar veriyor. İstemci "vurdum"
/// demiyor, sadece "savurdum" diyor (CmdSwing); menzil, koni ve görüş hattı
/// kontrolünü sunucu kendi gördüğü pozisyonlarla baştan yapıyor. Böylece
/// değiştirilmiş bir istemci menzili büyütüp duvar arkasından vuramıyor.
/// Bedeli, sunucunun bir tık geriden gördüğü pozisyonla karar vermesi —
/// birkaç on milisaniyelik gecikmede fark edilmiyor.
/// </summary>
[RequireComponent(typeof(RoundParticipant))]
[RequireComponent(typeof(PlayerController))]
public class MonsterAttack : NetworkBehaviour
{
    private enum AttackState
    {
        Ready,
        Charging, // sol tık basılı, atılma yükleniyor
        Swinging, // bıçak havada, isabet kontrolü açık
        Cooldown  // vuruş sonrası yavaşlama
    }

    [Header("Bıçak")]
    [Tooltip("Bıçağın erişim mesafesi (metre).")]
    [SerializeField] private float attackRange = 2.3f;

    [Tooltip("Önündeki isabet konisi, derece. Dar tutmak nişan almayı anlamlı kılıyor.")]
    [SerializeField] private float attackAngle = 75f;

    [Tooltip("Bıçağın aktif olduğu süre.")]
    [SerializeField] private float swingDuration = 0.22f;

    [Header("Atılma (Lunge)")]
    [Tooltip("Sol tıkın tam yüklenmesi için gereken süre.")]
    [SerializeField] private float maxChargeTime = 0.8f;

    [Tooltip("Hiç yüklemeden bırakınca eklenen hız (u/s).")]
    [SerializeField] private float minLungeSpeed = 90f;

    [Tooltip("Tam yüklüyken eklenen hız (u/s). Sürtünme bunu hızla söndürür.")]
    [SerializeField] private float maxLungeSpeed = 520f;

    [Tooltip("Yüklerken hız çarpanı. Yüklemek seni yavaşlatır, bedava değil.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float chargeSpeedMultiplier = 0.7f;

    [Tooltip("Bu kadar basılı tutulursa bıçak kendiliğinden savrulur. Sol tıkı basılı tutarak dolaşmayı engelliyor.")]
    [SerializeField] private float maxHoldTime = 2f;

    [Header("Vuruş Sonrası Ceza")]
    [SerializeField] private float cooldownDuration = 1.4f;

    [Tooltip("Vuruş sonrası hız çarpanı. 0.7 = %30 yavaşlama.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float cooldownSpeedMultiplier = 0.7f;

    [Header("Görsel")]
    [Tooltip("Kameranın child'ı olan bıçak. Sadece canavardayken görünür.")]
    [SerializeField] private Transform knife;

    // Ses doğrudan burada çalınıyor: savurma ve isabet bu bileşenin kendi
    // olayları, araya olay sistemi koymak KISS'e aykırı olurdu.
    [Tooltip("Canavar modelinin animatörü. Atılma girdiden, yakalama sunucunun " +
        "isabet onayından tetikleniyor — bkz. CLAUDE.md bölüm 4.")]
    [SerializeField] private MonsterAnimator monsterAnimator;

    [Header("Animasyon kilidi")]
    [Tooltip("Saldırı animasyonu boyunca hareket kilitli kalır (saniye). " +
        "Animasyonda canavar yerden kalkıyor; o sırada yürüyebilmek görüntüyü " +
        "bozuyordu. Süreyi Canavar Modelini Kur aracı klipten ölçüp yazıyor.")]
    [SerializeField] private float attackLockDuration = 1.6f;

    [Tooltip("Yakalama animasyonu boyunca kilit (saniye). Bu süre kaçanlar için " +
        "bedava bir kaçış penceresi — bilinçli, mori mantığı.")]
    [SerializeField] private float killLockDuration = 2.6f;

    [Tooltip("Yakalama animasyonunda kameranın geriye çekileceği mesafe (metre). " +
        "Birinci şahısta kendi öldürme animasyonunu göremiyordun. 0 = kapalı.")]
    [SerializeField] private float killCameraPullBack = 0.7f;

    [Tooltip("Kilitliyken sağa-sola bakabilme açısı (derece).")]
    [SerializeField] private float lockYawLimit = 45f;

    [Tooltip("Kilitliyken yukarı-aşağı bakabilme açısı (derece).")]
    [SerializeField] private float lockPitchLimit = 25f;

    // Kilidin biteceği an. 0 = kilit yok.
    private float lockUntil;

    [Header("Ses")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip swingClip;
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private float swingVolume = 0.7f;
    [SerializeField] private float hitVolume = 0.9f;

    [Header("İsabet Kontrolü")]
    [Tooltip("Görüşü kesen katmanlar. Yakalamaca > Katmanları Kur bunu Harita " +
        "yapıyor; ~0 bırakılırsa yerdeki varil de ışını keser ve kaçana " +
        "kalkan olur (CLAUDE.md teknik borç 3).")]
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField] private float hitHeight = 0.9f;

    [Tooltip("Hedefin gövde yarıçapı. Işın buraya varmadan kesilir, yoksa hedefin kendi kapsülü engel sayılır.")]
    [SerializeField] private float bodyRadius = 0.4f;

    [Header("Ağ Toleransı")]
    [Tooltip("Sunucu, gecikme yüzünden oyuncuları biraz geriden görür. Menzil ve koni " +
        "kontrolü bu çarpanla gevşetiliyor; 1 yaparsan meşru vuruşlar da ıskalar.")]
    [Range(1f, 2f)]
    [SerializeField] private float serverHitTolerance = 1.35f;

    private RoundParticipant self;
    private PlayerController controller;

    private AttackState state = AttackState.Ready;
    private float stateTimer;

    // Tuşun toplam basılı kalma süresi — maxChargeTime'da kırpılmıyor ki
    // otomatik savurma sınırı takip edilebilsin.
    private float holdTime;

    // Sunucunun kendi vuruş penceresi. İstemcinin state makinesinden bilerek
    // ayrı: istemci görselini sürüyor, sunucu kararını veriyor, ikisi
    // birbirine karışmıyor (host modunda ikisi de aynı objede çalışıyor).
    private float serverSwingTimer;
    private bool serverHasHitThisSwing;
    private double serverNextSwingTime;

    /// <summary>Yüklenme oranı (0-1). holdTime maxChargeTime'ı aşsa da 1'de kalır.</summary>
    private float ChargeFraction =>
        maxChargeTime > 0f ? Mathf.Clamp01(holdTime / maxChargeTime) : 1f;

    private Quaternion knifeRestRotation;
    private float knifeAngle;

    public bool IsAttacking => state == AttackState.Swinging;

    private void Awake()
    {
        self = GetComponent<RoundParticipant>();
        controller = GetComponent<PlayerController>();

        if (knife != null)
            knifeRestRotation = knife.localRotation;
    }

    private void Update()
    {
        // Kilit her koşulda sayılmalı: tur biterse ya da canavar elenirse
        // aşağıdaki erken çıkışlar devreye giriyor ve kilit sonsuza kadar
        // kalırdı.
        TickLock();

        RoundManager manager = RoundManager.Instance;

        // Rol ve canlılık SyncVar; yani "canavar mı" sorusunun cevabı her
        // istemcide aynı. Bıçağın görünürlüğü de bu yüzden herkeste doğru.
        bool active = manager != null
            && manager.Phase == RoundPhase.Playing
            && self.Role == RoundRole.Monster
            && self.IsAlive;

        if (knife != null)
            knife.gameObject.SetActive(active);

        if (isServer)
            ServerTickSwing(manager, active);

        if (!active)
        {
            if (isLocalPlayer)
                CancelToReady();
            else
                state = AttackState.Ready;

            return;
        }

        if (isLocalPlayer)
            TickState();
        else
            TickRemoteVisual();

        UpdateKnifeVisual();
    }

    // ---------- Yerel istemci: girdi, atılma, görsel ----------

    private void TickState()
    {
        switch (state)
        {
            case AttackState.Ready:
                if (KeyBindings.Pressed(GameAction.Attack))
                    BeginCharge();
                break;

            case AttackState.Charging:
                holdTime += Time.deltaTime;

                // Bırakınca ya da çok uzun tutunca savurur. İkincisi olmazsa
                // canavar sol tıkı basılı tutup hazır bekleyerek dolaşabilirdi.
                if (!KeyBindings.Held(GameAction.Attack) || holdTime >= maxHoldTime)
                    ReleaseSwing();
                break;

            case AttackState.Swinging:
                // İsabet kontrolü artık burada değil, sunucuda. Burası sadece
                // bıçağın havada kaldığı süreyi sayıyor.
                stateTimer -= Time.deltaTime;

                if (stateTimer <= 0f)
                    BeginCooldown();
                break;

            case AttackState.Cooldown:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                    CancelToReady();
                break;
        }
    }

    private void BeginCharge()
    {
        state = AttackState.Charging;
        holdTime = 0f;
        controller.SpeedMultiplier = chargeSpeedMultiplier;
    }

    private void ReleaseSwing()
    {
        float lungeSpeed = Mathf.Lerp(minLungeSpeed, maxLungeSpeed, ChargeFraction);

        // Atılma yatay düzlemde; yukarı bakarken havalanmasın.
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        controller.AddVelocity(forward * lungeSpeed);

        state = AttackState.Swinging;
        stateTimer = swingDuration;

        Play(swingClip, swingVolume);

        // Atılma animasyonu girdiden, anında: ağ turunu beklemek kendi
        // saldırının gecikmeli hissetmesi demekti. Karşı taraf aynı animasyonu
        // RpcSwing ile görüyor.
        if (monsterAnimator != null)
            monsterAnimator.PlayAttack();

        // Animasyon boyunca hareket kilitli. Momentum KESİLMİYOR: atılmanın
        // kendisi bir hız itmesi, kesersek canavar olduğu yerde çırpınır.
        BeginLock(attackLockDuration, stopMomentum: false);

        // Atılma sırasında yavaşlatma yok — ceza vuruş bitince geliyor.
        controller.SpeedMultiplier = 1f;

        // Sunucu kendi vuruş penceresini açsın. Nereye vurduğumuzu söylemiyoruz;
        // pozisyonu ve bakış yönünü zaten senkronladık, kararı o versin.
        CmdSwing();
    }

    /// <summary>
    /// Animasyon süresince hareketi kilitler. Terminalin kullandığı odak
    /// mekanizmasının aynısı: girdi kesiliyor, bakış dar bir koniye sıkışıyor.
    /// Hareket kodunun sürtünme/ivme akışına hiç dokunulmuyor.
    ///
    /// Yalnızca kendi oyuncumuzda: uzaktaki canavarın kontrolcüsü zaten kapalı.
    /// </summary>
    private void BeginLock(float duration, bool stopMomentum, float cameraPullBack = 0f)
    {
        if (!isLocalPlayer || controller == null || duration <= 0f)
            return;

        controller.BeginFocus(lockYawLimit, lockPitchLimit, stopMomentum);
        lockUntil = Time.time + duration;

        if (cameraPullBack > 0f)
            controller.PushCameraBack(cameraPullBack, duration);
    }

    private void TickLock()
    {
        if (lockUntil <= 0f || Time.time < lockUntil)
            return;

        lockUntil = 0f;

        if (controller != null)
            controller.EndFocus();
    }

    private void BeginCooldown()
    {
        state = AttackState.Cooldown;
        stateTimer = cooldownDuration;
        controller.SpeedMultiplier = cooldownSpeedMultiplier;
    }

    private void CancelToReady()
    {
        state = AttackState.Ready;
        holdTime = 0f;

        if (controller != null)
            controller.SpeedMultiplier = 1f;
    }

    /// <summary>
    /// Karşıdaki oyuncunun bıçağı. Girdi okumuyoruz — savurmayı RpcSwing
    /// başlatıyor, burası sadece süreyi sayıp bıçağı yerine döndürüyor.
    /// </summary>
    private void TickRemoteVisual()
    {
        if (state != AttackState.Swinging)
            return;

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            state = AttackState.Ready;
    }

    // ---------- Sunucu: isabet kararı ----------

    [Command]
    private void CmdSwing()
    {
        RoundManager manager = RoundManager.Instance;

        if (manager == null || manager.Phase != RoundPhase.Playing)
            return;
        if (self.Role != RoundRole.Monster || !self.IsAlive)
            return;

        // Hız sınırı. İstemcinin kendi bekleme süresi var ama ona güvenemeyiz;
        // değiştirilmiş bir istemci her karede CmdSwing çağırabilir. Toleransı
        // biraz gevşek tutuyoruz ki ağdaki dalgalanma meşru vuruşu yemesin.
        if (NetworkTime.time < serverNextSwingTime)
            return;

        serverNextSwingTime = NetworkTime.time + (swingDuration + cooldownDuration) * 0.9f;

        serverSwingTimer = swingDuration;
        serverHasHitThisSwing = false;

        RpcSwing();
    }

    [Server]
    private void ServerTickSwing(RoundManager manager, bool active)
    {
        if (serverSwingTimer <= 0f)
            return;

        // Tur bitti ya da canavar elendiyse havadaki bıçak da düşer.
        if (!active)
        {
            serverSwingTimer = 0f;
            return;
        }

        serverSwingTimer -= Time.deltaTime;

        if (!serverHasHitThisSwing)
            ServerTryHit(manager);
    }

    /// <summary>
    /// Bıçağın önündeki koni içinde, menzilde ve görüş hattı açık bir kaçan var mı.
    /// Bir vuruş en fazla bir kişiyi indirir.
    /// </summary>
    [Server]
    private void ServerTryHit(RoundManager manager)
    {
        IReadOnlyList<RoundParticipant> participants = manager.Participants;

        Vector3 origin = transform.position + Vector3.up * hitHeight;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        float range = attackRange * serverHitTolerance;
        float sqrRange = range * range;
        float halfAngle = Mathf.Min(180f, attackAngle * serverHitTolerance) * 0.5f;

        for (int i = 0; i < participants.Count; i++)
        {
            RoundParticipant other = participants[i];

            if (other == null || other == self)
                continue;
            // Kurtulmus olan hala IsAlive dondurur ama sahada degil.
            if (other.Role != RoundRole.Runner || !other.IsAlive || other.IsEscaped)
                continue;

            Vector3 delta = other.transform.position + Vector3.up * hitHeight - origin;
            if (delta.sqrMagnitude > sqrRange)
                continue;

            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (flat.sqrMagnitude > 0.0001f &&
                Vector3.Angle(forward, flat.normalized) > halfAngle)
                continue;

            // Duvarın ya da kapalı kapının arkasından vurulmasın. obstacleMask artık
            // yalnızca Harita: varil ve diğer oyuncular ışını kesmiyor
            // (LayerSetup, CLAUDE.md bölüm 16).
            //
            // Işın hedefin kapsülüne girmeden kesiliyor. Oyuncular maskeden
            // çıkınca bu şart olmaktan çıktı ama duruyor: kaldırmak isabet
            // geometrisini değiştirir, o da oynanarak ayarlanacak bir sayı.
            float distance = delta.magnitude;
            float rayDistance = distance - bodyRadius;

            if (rayDistance > 0.01f &&
                Physics.Raycast(origin, delta / distance, rayDistance, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            serverHasHitThisSwing = true;
            manager.ReportCaught(other, self);
            RpcHit();
            return;
        }
    }

    // ---------- Sunucudan istemcilere ----------

    /// <summary>
    /// Savurma sesi ve atılma animasyonu. Sahibine gönderilmiyor: o zaten
    /// tıkladığı anda yerel olarak oynattı, ikinci kez duymamalı.
    ///
    /// **Animasyon burada da oynatılıyor.** Eskiden yalnızca ses ve durum
    /// kuruluyordu, yani atılmayı sadece saldıran görüyordu — karşı taraf
    /// canavarı hiç saldırmadan koşarken görüyordu. Yakalama (RpcHit) doğruydu,
    /// eksik olan atılmaydı.
    /// </summary>
    [ClientRpc(includeOwner = false)]
    private void RpcSwing()
    {
        Play(swingClip, swingVolume);

        state = AttackState.Swinging;
        stateTimer = swingDuration;

        if (monsterAnimator != null)
            monsterAnimator.PlayAttack();
    }

    /// <summary>
    /// İsabet sesi. Sahibi de duymalı — vuruşun tuttuğunu tek anlama yolu bu,
    /// o yüzden burada includeOwner kapatılmıyor.
    /// </summary>
    [ClientRpc]
    private void RpcHit()
    {
        Play(hitClip, hitVolume);

        // Yakalama animasyonu YALNIZCA buradan: bu Rpc sunucu isabeti
        // doğruladığında çağrılıyor. Iskalarsan hiç gelmiyor, dolayısıyla
        // canavar havayı yumruklamıyor.
        if (monsterAnimator != null)
            monsterAnimator.PlayKill();

        // Yakalarken duruyor: kurbanın üstünde. Bu süre diğer kaçanlar için
        // bedava kaçış penceresi — bilinçli bir takas.
        BeginLock(killLockDuration, stopMomentum: true, killCameraPullBack);
    }

    // ---------- Ortak ----------

    private void Play(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip, volume);
    }

    private void UpdateKnifeVisual()
    {
        if (knife == null)
            return;

        float targetAngle;
        float turnSpeed;

        switch (state)
        {
            case AttackState.Charging:
                // Yüklendikçe geri çekilir — ne kadar yüklendiği gözle görülsün.
                targetAngle = Mathf.Lerp(0f, -60f, ChargeFraction);
                turnSpeed = 260f;
                break;

            case AttackState.Swinging:
                targetAngle = 75f;
                turnSpeed = 900f;
                break;

            default:
                targetAngle = 0f;
                turnSpeed = 220f;
                break;
        }

        knifeAngle = Mathf.MoveTowards(knifeAngle, targetAngle, turnSpeed * Time.deltaTime);
        knife.localRotation = knifeRestRotation * Quaternion.Euler(knifeAngle, 0f, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * hitHeight;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        Gizmos.color = Color.red;
        Gizmos.DrawRay(origin, Quaternion.Euler(0f, -attackAngle * 0.5f, 0f) * forward * attackRange);
        Gizmos.DrawRay(origin, Quaternion.Euler(0f, attackAngle * 0.5f, 0f) * forward * attackRange);
    }
}
