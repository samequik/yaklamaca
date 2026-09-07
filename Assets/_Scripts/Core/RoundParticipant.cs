using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Tura katılan oyuncu. Rolü, canlılığı ve adı sunucudan gelir.
///
/// Önemli: bu bileşen kendi rolüne veya ölümüne **karar vermez**. Değerler
/// [SyncVar] olduğu için yalnızca sunucu yazabilir; istemci sadece sonucu
/// uygular. Tek oyunculu prototipte de kural buydu, ağ katmanı bunu artık
/// derleyici seviyesinde zorluyor.
/// </summary>
[DisallowMultipleComponent]
public class RoundParticipant : NetworkBehaviour
{
    [Header("Rol Profilleri")]
    [Tooltip("Rol değişince ilgili profil PlayerController'a uygulanır.")]
    [SerializeField] private MovementProfile monsterProfile;
    [SerializeField] private MovementProfile runnerProfile;

    [Header("Eleme")]
    [Tooltip("Elendiğinde kapatılacak bileşenler — hareket, etkileşim vb.")]
    [SerializeField] private Behaviour[] disableWhenEliminated;

    [Tooltip("Elendiğinde gizlenecek gövde. Rol bazlı gövde değişimi varsa " +
        "bodyVisual kullanılıyor, burası yedek kalıyor.")]
    [SerializeField] private Renderer bodyRenderer;

    [Tooltip("Rol değişince kapsül/canavar gövdesini değiştiren bileşen.")]
    [SerializeField] private PlayerBodyVisual bodyVisual;

    [Header("Bot")]
    [Tooltip("Sahnedeki test botu mu. Rastgele dağıtımda canavar adayı DEĞİL " +
        "(kovalayamaz, tur sürüncemede kalır) ama lobide elle seçilebilir — " +
        "kaçan olarak tek başına test etmenin yolu bu. Kaçan olarak sayılıyor: " +
        "turun başlaması için gereken ikinci katılımcı odur.")]
    [SerializeField] private bool isBot;

    [Tooltip("Botun arayüzde görünecek adı. Gerçek oyuncular adını kendi istemcisinden bildiriyor.")]
    [SerializeField] private string botName = "Test Botu";

    [SyncVar(hook = nameof(OnRoleChanged))]
    private RoundRole role = RoundRole.None;

    /// <summary>
    /// Bu kaçanı kim yakaladı. **`alive`den ÖNCE tanımlı, bilerek:** Mirror aynı
    /// güncellemedeki SyncVar'ları tanım sırasına göre çözüyor, yani ölüm hook'u
    /// çalıştığında öldüren zaten belli oluyor.
    ///
    /// 0 = öldüren yok (test tuşuyla kendini elendirme gibi); o zaman beden
    /// olduğu yerde yatıyor.
    /// </summary>
    [SyncVar]
    private uint killerNetId;

    [SyncVar(hook = nameof(OnAliveChanged))]
    private bool alive = true;

    [SyncVar]
    private string displayName = PlayerProfile.DefaultName;

    /// <summary>
    /// Çıkıştan geçip kurtuldu mu. Ölümden ayrı bir durum: kaçan ne elendi ne
    /// de sahada — tur sonucu bu ikisinin farkına bakıyor.
    /// </summary>
    [SyncVar(hook = nameof(OnEscapedChanged))]
    private bool escaped;

    /// <summary>
    /// Tur ortasında katıldığı için sahada oynamıyor.
    ///
    /// Ölümden ve kurtulmadan **ayrı** bir durum: tur sonucuna hiç girmiyor
    /// (ne elendi sayılıyor ne kaçtı), ölüm sesi çalmıyor ve bir sonraki tura
    /// normal katılıyor. Bunu `alive = false` ile yapmak, hiç oynamamış birine
    /// ölüm sesi çaldırırdı.
    /// </summary>
    [SyncVar(hook = nameof(OnSpectatingChanged))]
    private bool spectating;

    /// <summary>
    /// Lobide hazır işaretledi mi. Tur başlatma şartı — sunucu hepsini hazır
    /// görmeden başlatmıyor.
    /// </summary>
    [SyncVar] private bool isReady;

    /// <summary>
    /// Odayı kuran kişi mi. Canavarı seçme ve turu başlatma yetkisi bunda.
    /// Sunucu yazıyor; istemci yalnızca arayüzü buna göre çiziyor, yetkiyi
    /// yine sunucu doğruluyor (bkz. RoundManager.ServerIsHost).
    /// </summary>
    [SyncVar] private bool isRoomOwner;

    public RoundRole Role => role;
    public bool IsAlive => alive;
    public string DisplayName => displayName;

    /// <summary>Test botu mu — rol dağıtımı buna bakıyor.</summary>
    public bool IsBot => isBot;

    /// <summary>Kaçıp kurtuldu mu.</summary>
    public bool IsEscaped => escaped;

    /// <summary>Tur ortasında katıldığı için bu turda sahada değil.</summary>
    public bool IsSpectating => spectating;

    /// <summary>Lobide hazır mı. Bot her zaman hazır sayılıyor.</summary>
    public bool IsReady => isBot || isReady;

    /// <summary>Odanın sahibi mi.</summary>
    public bool IsRoomOwner => isRoomOwner;

    /// <summary>
    /// İstemcide görünen tüm katılımcılar. Lobi listesi bunu okuyor.
    ///
    /// Sunucunun ayrıca bir liste yollamasına gerek yok: katılımcılar zaten
    /// spawn edilmiş NetworkIdentity'ler, herkes hepsini görüyor ve adı,
    /// hazır durumu, rolü SyncVar olarak geliyor. Ayrı bir liste mesajı aynı
    /// veriyi ikinci kez göndermek olurdu.
    /// </summary>
    public static IReadOnlyList<RoundParticipant> All => all;

    private static readonly List<RoundParticipant> all = new List<RoundParticipant>();

    /// <summary>Bu istemcinin kendi katılımcısı; menü komutları bunun üzerinden gidiyor.</summary>
    public static RoundParticipant Local => NetworkClient.localPlayer != null
        ? NetworkClient.localPlayer.GetComponent<RoundParticipant>()
        : null;

    [Header("Ses")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip deathClip;
    [SerializeField] private float deathVolume = 0.9f;

    [Header("Işık")]
    [Tooltip("Canavarın kırmızı hâlesi. Rol değişince açılıp kapanıyor.")]
    [SerializeField] private MonsterAura monsterAura;

    [Tooltip("El feneri. Canavarda kapatılıyor: onun ışığı kırmızı hâle.")]
    [SerializeField] private Flashlight flashlight;

    [Header("Yakalanma Animasyonu")]
    [Tooltip("Kaçanın animatörü. Yakalanınca ölüm klibi buradan tetikleniyor.")]
    [SerializeField] private RunnerAnimator runnerAnimator;

    [Tooltip("Beden kaç saniye sahnede kalacak. Kaçan Modelini Kur bunu ölüm " +
        "klibinin uzunluğundan ÖLÇÜP yazıyor — klip değişirse süre de değişir.")]
    [SerializeField] private float deathHoldDuration = 2.6f;

    [Tooltip("Kurbanın canavarın kaç metre önüne kaydırılacağı. VARSAYILAN 0: " +
        "Mixamo eşli animasyonları iki karakteri de aynı noktada varsayıyor, " +
        "yani ikisi iç içe geçmeli. Ayrılırlarsa buradan ince ayar yapılır.")]
    [SerializeField] private float deathForwardOffset;

    private bool dying;
    private Coroutine deathRoutine;

    private PlayerController playerController;

    /// <summary>
    /// Kaçanın çarpışma kapsülü. `disableWhenEliminated` bunu KAPSAMIYOR —
    /// `CharacterController` bir `Collider`, `Behaviour` değil, yani o diziye
    /// eklenemiyor. Ayrı tutulup `SetControlActive`'te elle kapatılıyor.
    ///
    /// **Düzeltme (2026-09-07).** Eskiden bu hiç kapanmıyordu: `PlayerController`
    /// (girdi/hareket) elenince kapanıyordu ama altındaki gerçek çarpışma
    /// kutusu açık kalıyordu. Sonuç, "beden yok oluyor ama öldüğü noktada
    /// bişiler görünmez oluyor ama dokunuluyor" diye bildirilen hayalet
    /// duvardı — görsel gövde gizlenmişti, çarpışma kutusu koridoru tıkamaya
    /// devam ediyordu.
    /// </summary>
    private CharacterController characterController;

    // Ölüm sesi yalnızca canlı→ölü geçişinde çalmalı. Host'ta ApplyAlive hem
    // sunucu tarafından hem SyncVar hook'undan çağrılabiliyor; bayrak olmasa
    // ses üst üste binerdi.
    private bool wasAlive = true;

    private TestRunnerBot bot;
    private NetworkTransformBase netTransform;

    /// <summary>
    /// Bu oyuncunun sunucuya gidiş-dönüş süresi (ms). Skor tablosu gösteriyor.
    ///
    /// **Sahibi bildiriyor, sunucu yazıyor.** Mirror'ın `NetworkTime.rtt`'si
    /// yalnızca yerel istemcide anlamlı; sunucunun her bağlantı için aynı
    /// ölçümü kendi yapması da mümkün ama Mirror bunu her sürümde aynı yerde
    /// vermiyor. Bildirmek hem taşınabilir hem tek satır.
    ///
    /// Bu bir OYUN kararı değil, bir gösterge — yanlış bildiren bir istemci
    /// yalnızca kendi pingini yanlış gösterir.
    /// </summary>
    [SyncVar] private int pingMs;

    public int PingMs => pingMs;

    private float nextPingReport;

    private void Update()
    {
        if (!isOwned || !NetworkClient.active || Time.unscaledTime < nextPingReport)
            return;

        // Saniyede bir yetiyor: ping göstergesi anlık değil eğilim bilgisi ve
        // her karede komut yollamak boşuna trafik.
        nextPingReport = Time.unscaledTime + 1f;

        CmdReportPing(Mathf.RoundToInt((float)NetworkTime.rtt * 1000f));
    }

    [Command]
    private void CmdReportPing(int value) => pingMs = Mathf.Clamp(value, 0, 9999);

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();

        // Doğum yerleştirmesi için: bot mu, ağ transformu var mı. İkisi de
        // olmayabilir (bot oyuncu değil, oyuncu bot değil), o yüzden null
        // kontrolü çağrı yerlerinde.
        bot = GetComponent<TestRunnerBot>();
        netTransform = GetComponent<NetworkTransformBase>();

        // Işık bileşenleri eksikse burada tamamlanıyor. `Ağ Kurulumu` ikisini
        // de bağlıyor, ama o araç oyuncu prefabını sıfırdan kuruyor: var olan
        // bir projede yalnızca hâleyi eklemek için canavar/kaçan modellerini,
        // menüyü, sesleri ve yankıyı da yeniden kurmak gerekirdi (bölüm 7).
        //
        // Alanlar serileştirilmiş, yani bağlıysa hiçbir şey yapılmıyor.
        if (flashlight == null)
            flashlight = GetComponent<Flashlight>();

        if (monsterAura == null)
            monsterAura = GetComponent<MonsterAura>();

        if (monsterAura == null)
            monsterAura = gameObject.AddComponent<MonsterAura>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!all.Contains(this))
            all.Add(this);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        all.Remove(this);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // Adı istemci bilir, sunucu bilmez; doğar doğmaz bildiriyoruz.
        CmdSetName(PlayerProfile.Name);
    }

    /// <summary>Adı sunucuya bildirir. Sunucu temizleyip SyncVar'a yazar.</summary>
    [Command]
    private void CmdSetName(string requested)
    {
        displayName = PlayerProfile.Sanitize(requested);
    }

    // ---------- Lobi komutları ----------
    //
    // Menü kendi başına Command gönderemez: sahnedeki menü objesine
    // NetworkIdentity eklenemiyor (Mirror sunucu açılışında sahnedeki bütün
    // kimlikleri aktifleştirip spawn ediyor — CLAUDE.md bölüm 4). Bu yüzden
    // menü, komutları oyuncunun kendi objesi üzerinden yolluyor.

    /// <summary>Menüden çağrılır: hazır işaretini değiştirir.</summary>
    public void SetReady(bool value)
    {
        if (isLocalPlayer)
            CmdSetReady(value);
    }

    [Command]
    private void CmdSetReady(bool value)
    {
        // Tur başladıktan sonra hazır işareti anlamsız.
        if (RoundManager.Instance != null && RoundManager.Instance.Phase != RoundPhase.Waiting)
            return;

        isReady = value;
    }

    /// <summary>Menüden çağrılır: adı tur sırasında da güncelleyebilmek için.</summary>
    public void PushName()
    {
        if (isLocalPlayer)
            CmdSetName(PlayerProfile.Name);
    }

    /// <summary>Menüden çağrılır: canavarı seç. 0 = rastgele. Yetkiyi sunucu doğruluyor.</summary>
    public void RequestMonsterChoice(uint chosenNetId)
    {
        if (isLocalPlayer)
            CmdSetMonsterChoice(chosenNetId);
    }

    [Command]
    private void CmdSetMonsterChoice(uint chosenNetId)
    {
        if (RoundManager.Instance != null)
            RoundManager.Instance.ServerSetMonsterChoice(this, chosenNetId);
    }

    /// <summary>Menüden çağrılır: turu başlat. Yetkiyi ve şartları sunucu doğruluyor.</summary>
    public void RequestStartRound()
    {
        if (isLocalPlayer)
            CmdRequestStartRound();
    }

    [Command]
    private void CmdRequestStartRound()
    {
        if (RoundManager.Instance != null)
            RoundManager.Instance.ServerRequestStart(this);
    }

    // ---------- Sunucunun yazdığı lobi durumu ----------

    [Server]
    public void ServerSetRoomOwner(bool value) => isRoomOwner = value;

    [Server]
    public void ServerSetReady(bool value) => isReady = value;

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Botun adını istemci bildiremez — bağlantısı yok, sunucu kendisi yazıyor.
        if (isBot)
            displayName = botName;

        RoundManager manager = RoundManager.Instance;
        if (manager != null)
            manager.ServerRegister(this);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        // Bağlantı kopunca oyuncu objesi sunucuda yok edilir; tur yöneticisi
        // ayrılmayı bu yoldan öğreniyor (canavar ayrılırsa tur iptal).
        RoundManager manager = RoundManager.Instance;
        if (manager != null)
            manager.ServerUnregister(this);
    }

    // ---------- Yalnızca sunucu çağırır ----------

    [Server]
    public void ServerSetRole(RoundRole newRole)
    {
        role = newRole;

        // Host'ta hook her zaman tetiklenmeyebilir; sunucu tarafında da
        // uyguluyoruz. İşlemler tekrarlanabilir, iki kez çalışması zararsız.
        ApplyRole(newRole);
    }

    /// <summary>
    /// Kaçan çıkıştan geçti. Bedeni sahneden çekiliyor.
    ///
    /// Sıfırlanabilir olması şart: lobiye dönerken temizlenmezse geçen turda
    /// kurtulan oyuncu bir daha asla kurtulamıyordu — `ReportEscaped`
    /// `IsEscaped` olanı reddediyor.
    /// </summary>
    [Server]
    public void ServerSetEscaped(bool value = true)
    {
        escaped = value;
        ApplyEscaped(value);
    }

    /// <summary>
    /// Tur ortasında katılanı izleyiciye alır (ya da lobiye dönerken çıkarır).
    /// </summary>
    [Server]
    public void ServerSetSpectating(bool value)
    {
        spectating = value;
        ApplySpectating(value);
    }

    /// <summary>
    /// Öldüreni kaydeder. `ServerSetAlive(false)`den ÖNCE çağrılmalı ki ölüm
    /// hook'u bedeni doğru yere oturtabilsin.
    /// </summary>
    [Server]
    public void ServerSetKiller(RoundParticipant killer)
    {
        killerNetId = killer != null && killer.netIdentity != null ? killer.netId : 0u;
    }

    [Server]
    public void ServerSetAlive(bool value)
    {
        alive = value;
        ApplyAlive(value);
    }

    /// <summary>
    /// Katılımcıyı bir noktaya yerleştirir — tur başındaki rol bazlı doğum
    /// (`RoundManager.ServerPlaceParticipants`) buradan geçiyor.
    ///
    /// ### Neden sunucu tek başına taşıyamıyor
    ///
    /// Hareket **istemci otoriteli** (`NetworkTransform` → ClientToServer,
    /// bölüm 4): sunucudaki konumu yazmak sahibinin bir sonraki
    /// güncellemesinde eziliyor. Asıl taşımayı, sahibine giden `TargetRpc`
    /// yapıyor.
    ///
    /// Sunucuda da uygulanıyor: isabet ve tur kararlarını sunucu kendi gördüğü
    /// pozisyonlarla veriyor, bir ağ turu boyunca eski konumda görünmek yanlış
    /// kararlara kapı bırakırdı.
    ///
    /// `NetworkTransform.ServerTeleport` ayrıca çağrılıyor ki **diğer**
    /// istemciler sıçramayı ara değerlemesin — yoksa oyuncular haritanın bir
    /// ucundan öbürüne duvarların içinden süzülerek gidiyor görünür.
    /// </summary>
    [Server]
    public void ServerPlaceAt(Vector3 position, Quaternion rotation)
    {
        // Botun kendi ışınlama yolu var: CharacterController'ı kapatıp açıyor
        // ve NetworkTransform'u kendisi haberdar ediyor. Bağlantısı olmadığı
        // için TargetRpc de gönderilemez.
        if (bot != null)
        {
            bot.ServerTeleportTo(position);
            return;
        }

        ApplyPlacement(position, rotation);

        if (netTransform != null)
            netTransform.ServerTeleport(position, rotation);

        if (connectionToClient != null)
            TargetPlaceAt(connectionToClient, position, rotation);
    }

    [TargetRpc]
    private void TargetPlaceAt(NetworkConnectionToClient target, Vector3 position,
        Quaternion rotation)
    {
        ApplyPlacement(position, rotation);
    }

    private void ApplyPlacement(Vector3 position, Quaternion rotation)
    {
        if (playerController != null)
            playerController.Teleport(position, rotation);
        else
            transform.SetPositionAndRotation(position, rotation);
    }

    // ---------- Yerel uygulama ----------

    private void OnRoleChanged(RoundRole oldRole, RoundRole newRole) => ApplyRole(newRole);

    private void OnAliveChanged(bool oldValue, bool newValue) => ApplyAlive(newValue);

    private void OnEscapedChanged(bool oldValue, bool newValue) => ApplyEscaped(newValue);

    private void OnSpectatingChanged(bool oldValue, bool newValue) => ApplySpectating(newValue);

    /// <summary>
    /// Kurtulan oyuncu haritadan çekiliyor: hareket ve gövde kapanıyor.
    /// Elenmekle aynı görsel sonuç ama sebebi farklı — izleyici modu da
    /// buna bakıp devreye giriyor.
    /// </summary>
    /// <summary>Ölüm sesi ÇALMIYOR: kurtulmak elenmek değil.</summary>
    private void ApplyEscaped(bool value) => RefreshBodyState();

    /// <summary>
    /// İzleyici de ses çıkarmıyor: tura geç katılan kişi ölmedi, sadece bu
    /// turda oynamıyor. Ölüm sesi duyulsaydı hem kendisi hem çevresindekiler
    /// olmayan bir eleme duyardı.
    /// </summary>
    private void ApplySpectating(bool value) => RefreshBodyState();

    private void ApplyRole(RoundRole newRole)
    {
        // Gövde rolden önce geliyor: kontrolcü null olsa bile görünüm doğru
        // olmalı (bot gibi hareket bileşeni olmayan katılımcılar var).
        if (bodyVisual != null)
            bodyVisual.SetRole(newRole);

        // Işık da kontrolcüden önce, aynı gerekçeyle. Canavarda fener yok,
        // yerine kırmızı hâle var: fener kaçanın "görürsün ama görünürsün"
        // takası (bölüm 5) ve canavarda o takasın karşılığı yok.
        //
        // Burası rolün SyncVar hook'undan çağrıldığı için her istemcide
        // çalışıyor — hâleyi ve fenerin kapanmasını herkes aynı anda görüyor,
        // ayrıca bir mesaj göndermek gerekmiyor (bölüm 4).
        if (flashlight != null)
            flashlight.SetAvailable(newRole != RoundRole.Monster);

        RefreshAura();

        if (playerController == null)
            return;

        MovementProfile profile = newRole == RoundRole.Monster ? monsterProfile : runnerProfile;
        playerController.ApplyMovementProfile(profile);
    }

    private void ApplyAlive(bool value)
    {
        bool justDied = !value && wasAlive;

        if (justDied && audioSource != null && deathClip != null)
            audioSource.PlayOneShot(deathClip, deathVolume);

        wasAlive = value;

        if (justDied)
            BeginDeathHold();
        else if (value)
            CancelDeathHold();

        RefreshBodyState();
    }

    /// <summary>
    /// Yakalanan kaçanın bedeni, ölüm animasyonu bitene kadar sahnede kalır.
    ///
    /// Eskiden beden aynı karede gizleniyordu ve canavar havayı yumrukluyordu
    /// (CLAUDE.md bölüm 14, bilinen eksikler). Artık hareket ve etkileşim
    /// anında kapanıyor — ölen ölmüştür — ama **görüntü** klip boyunca duruyor.
    ///
    /// Sayaç ağdan gelmiyor: `alive` zaten SyncVar ve hook her istemcide
    /// çalışıyor, herkes kendi sayacını başlatıyor. Ayrıca bir "şimdi öl"
    /// mesajı yollamak aynı bilgiyi ikinci kez göndermek olurdu
    /// (CLAUDE.md bölüm 4).
    ///
    /// **Bilinen sınır:** kurban öldüğü yerde yatıyor, canavarın tam önüne
    /// taşınmıyor. Yandan yakalanınca iki animasyon birebir örtüşmüyor.
    /// Düzeltmek için öldüren canavarın netId'sini kurbana taşıyıp gövde kökünü
    /// ona göre oturtmak gerekiyor; gövde kökü ağda olmadığı için her istemci
    /// aynı sonucu kendi hesaplayabilir.
    /// </summary>
    private void BeginDeathHold()
    {
        if (role != RoundRole.Runner || escaped
            || runnerAnimator == null || deathHoldDuration <= 0f)
            return;

        CancelDeathHold();
        deathRoutine = StartCoroutine(DeathHold());
    }

    private IEnumerator DeathHold()
    {
        dying = true;

        // Yerel oyuncu ölünce izleyici kamerasına, yani ÜÇÜNCÜ şahsa geçiyor.
        // Birinci şahıs için gizlenen kafa orada kafasız bir ceset olarak
        // görünürdü — kendi ölümünü izleyen tek kişi de o.
        if (bodyVisual != null && isLocalPlayer)
            bodyVisual.SetFirstPerson(false);

        RefreshBodyState();
        runnerAnimator.PlayDeath();

        // Canavar bir kare sonra çözülebiliyorsa bir kez daha deniyoruz: nesne
        // henüz spawn sırasındaysa ilk denemede bulunamayabilir.
        if (!TryApplyDeathPose())
        {
            yield return null;
            TryApplyDeathPose();
        }

        yield return new WaitForSeconds(deathHoldDuration);

        // Gövde kalıcı bir cesede dönüşüyor — TAM BURADA, ClearDeathPose'dan
        // ÖNCE: `ApplyDeathPose`'un konumlandırdığı poz (canavarın önünde diz
        // çökmüş) hâlâ duruyor, `Corpse` onu bu anda kopyalıyor. Sıra
        // değişirse ceset, gövde kendi orijinal yerine dönmüş hâlde doğar.
        //
        // Yalnızca SUNUCU yaratıyor (`NetworkServer.Spawn` sunucu dışında
        // çağrılamaz) ama bu korutin her istemcide çalışıyor (SyncVar hook'u
        // tetikliyor, bölüm 4) — o yüzden `isServer` ile süzülüyor.
        if (isServer && RoundManager.Instance != null)
            RoundManager.Instance.ServerSpawnCorpse(this);

        dying = false;
        deathRoutine = null;

        if (bodyVisual != null)
            bodyVisual.ClearDeathPose();

        RefreshBodyState();
    }

    /// <summary>
    /// Ceset sistemi için: kaçanın şu an gösterilen gövdesi. `Corpse` bunu
    /// yalnızca `victimNetId` üzerinden, kendi `OnStartClient`'ında okuyor —
    /// her istemci kendi yerel kopyasından bağımsız olarak aynı sonucu üretiyor
    /// (bölüm 4: aynı bilgiyi ikinci kez ağdan göndermeye gerek yok).
    /// </summary>
    public Transform CorpseSourceBody => bodyVisual != null ? bodyVisual.RunnerBodyForCorpse : null;

    /// <summary>
    /// Bedeni canavarın önüne oturtur. Öldüren bilinmiyorsa (test tuşuyla
    /// eleme) hiçbir şey yapmıyor: beden olduğu yerde yatıyor.
    /// </summary>
    private bool TryApplyDeathPose()
    {
        if (bodyVisual == null || killerNetId == 0)
            return true;

        Transform killer = ResolveKiller();
        if (killer == null)
            return false;

        bodyVisual.ApplyDeathPose(killer, deathForwardOffset);
        return true;
    }

    /// <summary>
    /// netId'den canavarın transform'u. İstemcide ve sunucuda ayrı sözlükler
    /// var; host ikisine de sahip, adanmış sunucuda yalnızca ikincisi dolu.
    /// </summary>
    private Transform ResolveKiller()
    {
        if (NetworkClient.spawned.TryGetValue(killerNetId, out NetworkIdentity identity)
            && identity != null)
            return identity.transform;

        if (NetworkServer.active
            && NetworkServer.spawned.TryGetValue(killerNetId, out identity)
            && identity != null)
            return identity.transform;

        return null;
    }

    /// <summary>Tur yeniden başladıysa bekleme yarıda kesiliyor.</summary>
    private void CancelDeathHold()
    {
        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }

        if (!dying)
            return;

        dying = false;

        if (bodyVisual != null)
        {
            bodyVisual.ClearDeathPose();

            // Ölürken üçüncü şahsa geçmiştik; dirilince birinci şahıs geri geliyor.
            bodyVisual.SetFirstPerson(isLocalPlayer);
        }
    }

    /// <summary>
    /// Gövde durumunu **üç bayrağın hepsinden** yeniden hesaplar.
    ///
    /// Eskiden her bayrak gövdeyi kendi başına açıp kapatıyordu ve sıraya
    /// bağımlıydı: lobiye dönerken "canlandır" ile "kurtulmayı temizle"
    /// çağrılarının sırası yanlış olsa beden kapalı kalıyordu. Tek kaynaktan
    /// hesaplamak o tuzağı tamamen kaldırıyor.
    ///
    /// **Hareket ile görüntü burada ayrılıyor.** Yakalanan oyuncunun hareketi
    /// anında kesiliyor ama bedeni ölüm animasyonu boyunca sahnede kalıyor.
    /// </summary>
    private void RefreshBodyState()
    {
        bool onField = alive && !escaped && !spectating;

        SetControlActive(onField);
        SetVisualActive(onField || dying);
        RefreshAura();
    }

    /// <summary>
    /// Kırmızı hâle yalnızca SAHADAKİ canavarda yanıyor.
    ///
    /// İki şarta birden bağlı olması gerekiyor: rol tek başına yetmez (elenen
    /// canavarın hâlesi boş koridorda yanmaya devam ederdi), sahada olmak da
    /// tek başına yetmez (her kaçan kırmızı yanardı). O yüzden iki çağıran var
    /// ve ikisi de buraya düşüyor.
    /// </summary>
    private void RefreshAura()
    {
        if (monsterAura == null)
            return;

        bool onField = alive && !escaped && !spectating;
        monsterAura.SetMonster(role == RoundRole.Monster && onField);
    }

    /// <summary>Hareket ve etkileşim. Ölende anında kapanıyor.</summary>
    private void SetControlActive(bool value)
    {
        // Çarpışma kutusu ayrı: Behaviour[] dizisine giremiyor (yukarıdaki
        // alan yorumu). Elenince kapanmazsa görünmez ama katı bir engel
        // olarak koridorda kalır.
        if (characterController != null)
            characterController.enabled = value;

        if (disableWhenEliminated == null)
            return;

        for (int i = 0; i < disableWhenEliminated.Length; i++)
        {
            if (disableWhenEliminated[i] != null)
                disableWhenEliminated[i].enabled = value;
        }
    }

    /// <summary>Görünen gövde. Ölüm animasyonu boyunca açık kalıyor.</summary>
    private void SetVisualActive(bool value)
    {
        if (bodyVisual != null)
            bodyVisual.SetOnField(value);
        else if (bodyRenderer != null)
            bodyRenderer.enabled = value;
    }
}
