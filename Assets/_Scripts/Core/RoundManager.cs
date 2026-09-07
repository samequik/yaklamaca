using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Turun tek sahibi ve **sunucu otoritesi**: faz, rol dağıtımı, terminal
/// sayacı, eleme ve kazanma kararı burada alınır. İstemciler yalnızca sonucu
/// görür.
///
/// **Tur süresi yok.** Eskiden 5 dakikalık sayaç vardı; kaldırıldı. Tur ancak
/// sahada oynayan kaçan kalmayınca biter — ya hepsi elenir, ya hepsi kaçar.
/// Bkz. CLAUDE.md bölüm 11.
/// </summary>
public class RoundManager : NetworkBehaviour
{
    [Header("Tur")]
    [Tooltip("Gereken terminal sayısının TAVANI: haritadaki terminal sayısı. " +
        "Tur başında hayattaki kaçan + 1 isteniyor ve bu değerle sınırlanıyor; " +
        "bir kaçan ölünce 1 azalıyor. Terminal ve Çıkış Kur bunu her kurulumda " +
        "gerçekte yerleşen sayıya çekiyor — büyük kalırsa olmayan bir terminal " +
        "beklenir ve çıkış HİÇ açılmaz.")]
    [SerializeField] private int terminalGoal = 5;

    [Tooltip("Tur bittikten sonra lobiye dönüş için beklenen süre (saniye).")]
    [SerializeField] private float lobbyReturnDelay = 4f;

    [Tooltip("Turun başlaması için gereken en az oyuncu sayısı.")]
    [SerializeField] private int minimumPlayers = 2;

    [Header("Doğum")]
    [Tooltip("Kaçanların toplandığı nokta. BOŞ BIRAKILABİLİR: o zaman " +
        "sahnedeki doğum noktalarından biri her turda rastgele seçiliyor. " +
        "Beğenilen bir yer bulunursa buraya bir boş obje sürüklenip sabitlenir.")]
    [SerializeField] private Transform runnerSpawn;

    [Tooltip("Canavarın doğduğu nokta. Boşsa kaçanlarınkine EN UZAK doğum " +
        "noktası seçiliyor. İkisinden biri boşsa ikisi de hesaplanıyor — " +
        "yarısı elle, yarısı otomatik bir çift mesafeyi garanti etmez.")]
    [SerializeField] private Transform monsterSpawn;

    [Tooltip("Kaçanlar bu yarıçapta bir halkaya diziliyor (metre). Hepsini " +
        "aynı noktaya koymak karakterleri birbirini itmeye zorluyor.")]
    [SerializeField] private float runnerSpawnSpread = 1.6f;

    [Tooltip("Canavar ile kaçanlar arasında beklenen en az mesafe (metre). " +
        "Altına düşülürse konsola uyarı yazılıyor — sessiz kalırsa sorun " +
        "yine 'canavar dibimde doğdu' olarak geri döner.")]
    [SerializeField] private float minimumSpawnSeparation = 25f;

    [Header("Ceset")]
    [Tooltip("RoundParticipant.DeathHold bitince doğurulan fizik gövdesi. " +
        "Yakalamaca > Ceset Sistemini Kur kurup buraya bağlıyor.")]
    [SerializeField] private GameObject corpsePrefab;

    // Sunucuda tutulan, bir sonraki turda temizlenecek cesetler. İstemcide
    // boş kalır — spawn/destroy kararı yalnızca sunucudan gidiyor.
    private readonly List<GameObject> spawnedCorpses = new List<GameObject>();

    [Header("Test (lobi arayüzü bağlanınca kaldırılacak)")]
    [Tooltip("Sunucuda tur başlatır.")]
    [SerializeField] private KeyCode startRoundKey = KeyCode.Alpha1;

    [Tooltip("Turu, sen KAÇAN olacak şekilde başlatır. Tek gerçek oyuncuyla oynarken " +
        "canavar hep sen seçilirsin ve elenmen mümkün olmaz; izleyici modunu " +
        "denemenin başka yolu yok.")]
    [SerializeField] private KeyCode startAsRunnerKey = KeyCode.Alpha2;

    [Tooltip("Kendini elendirir — izleyici moduna geçmeyi denemek için.")]
    [SerializeField] private KeyCode selfEliminateKey = KeyCode.Alpha3;

    [Tooltip("Test botlarını önüne ışınlar. 54 metrelik zifiri labirentte botu " +
        "aramak testi şansa bırakıyordu.")]
    [SerializeField] private KeyCode summonBotsKey = KeyCode.Alpha4;

    [Tooltip("AÇIKSA tur, kaçan kalmayınca BİTMEZ — yalnızca test için. Tek bot " +
        "kaçan olduğu bir turda onu öldürmek/kaçırmak normalde turu anında " +
        "kapatıyor; bu, bot üstünde hareket/animasyon/ceset denerken sürekli " +
        "tur yeniden başlatmak zorunda kalmamak içindir. Varsayılan KAPALI: " +
        "gerçek oynanışta 'kimse kaçamadan hepsi elendi/kaçtı' kuralı hep " +
        "geçerli kalmalı (bölüm 11.1). İşin bitince kapatmayı unutma.")]
    [SerializeField] private bool disableRoundEndForTesting;

    // StartRoundAsRunner sırasında dolu: rol dağıtımı bu katılımcıyı canavar
    // adaylarından çıkarıyor.
    private RoundParticipant forcedRunner;

    // Sunucuda tutulan gerçek liste. İstemcilerde boş kalır — istemcinin
    // katılımcıları taraması gerekmiyor, kendi durumunu SyncVar'dan öğreniyor.
    private readonly List<RoundParticipant> participants = new List<RoundParticipant>();

    [SyncVar] private RoundPhase phase = RoundPhase.Waiting;
    [SyncVar] private RoundResult result = RoundResult.None;
    [SyncVar] private int aliveRunnerCount;

    /// <summary>Kaçmayı başaranlar. Tur sonucunu bu belirliyor.</summary>
    [SyncVar] private int escapedRunnerCount;

    /// <summary>Çıkışın açılması için gereken terminal sayısı.</summary>
    [SyncVar] private int requiredTerminals;

    /// <summary>Tamamlanmış terminal sayısı.</summary>
    [SyncVar] private int completedTerminals;

    /// <summary>
    /// Odanın sahibinin seçtiği canavarın netId'si. **0 = rastgele.**
    ///
    /// netId tutuluyor, referans değil: SyncVar bir NetworkBehaviour
    /// referansını taşıyamaz, ayrıca seçilen kişi ayrılırsa netId artık hiçbir
    /// şeye çözülmüyor ve seçim kendiliğinden rastgeleye dönüyor.
    /// </summary>
    [SyncVar] private uint monsterChoice;

    private float lobbyTimer;

    public RoundPhase Phase => phase;
    public RoundResult Result => result;
    public int AliveRunnerCount => aliveRunnerCount;
    public int EscapedRunnerCount => escapedRunnerCount;
    public int RequiredTerminals => requiredTerminals;
    public int CompletedTerminals => completedTerminals;

    /// <summary>
    /// Çıkış açıldı mı — kaçanlar artık kurtulabilir.
    ///
    /// requiredTerminals sıfır kontrolü şart: lobide her iki sayaç da 0 olduğu
    /// için "0 >= 0" doğru çıkıyor ve tur başlamadan çıkış açık görünüyordu.
    /// </summary>
    public bool ExitOpen => requiredTerminals > 0 && completedTerminals >= requiredTerminals;

    /// <summary>Sadece sunucuda dolu; yakalama ve izleyici kontrolleri için.</summary>
    public IReadOnlyList<RoundParticipant> Participants => participants;

    /// <summary>Seçilen canavarın netId'si; 0 ise rastgele.</summary>
    public uint MonsterChoice => monsterChoice;

    /// <summary>Turun başlaması için gereken en az oyuncu. Lobi arayüzü gösteriyor.</summary>
    public int MinimumPlayers => minimumPlayers;

    /// <summary>
    /// Lobi arayüzünün "BAŞLAT" düğmesini açıp kapatması için. İstemcide
    /// çalışıyor ve **tahminden ibaret** — sunucu aynı kontrolü
    /// <see cref="ServerRequestStart"/> içinde baştan yapıyor. İstemci kodu
    /// değiştirilse en fazla gri düğmeye basılır, tur yine başlamaz.
    /// </summary>
    public static bool CanStartFromClient(out string reason)
    {
        RoundManager manager = Instance;

        if (manager == null || manager.phase != RoundPhase.Waiting)
        {
            reason = "Tur zaten sürüyor.";
            return false;
        }

        int count = 0;
        int notReady = 0;

        foreach (RoundParticipant participant in RoundParticipant.All)
        {
            if (participant == null)
                continue;

            count++;
            if (!participant.IsReady)
                notReady++;
        }

        if (count < manager.minimumPlayers)
        {
            reason = $"En az {manager.minimumPlayers} oyuncu gerekiyor ({count} var).";
            return false;
        }

        if (notReady > 0)
        {
            reason = $"{notReady} oyuncu hazır değil.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Sahnedeki tek tur yöneticisi. Oyuncunun üzerindeki bileşenler (bıçak, iz)
    /// artık prefabtan doğduğu için sahnedeki yöneticiye referansla bağlanamıyor;
    /// her karede FindObjectOfType çağırmak da israf. Sahne nesnesi olduğundan
    /// Mirror onu spawn edip aktifleştirene kadar burası null kalıyor — çağıranlar
    /// null kontrolü yapmak zorunda.
    /// </summary>
    public static RoundManager Instance { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Turun canavarı. İzler yalnızca canavara gönderildiği için sunucunun
    /// hedefi bilmesi gerekiyor (bkz. TrailMarkSystem).
    /// </summary>
    [Server]
    public RoundParticipant ServerFindMonster()
    {
        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i] != null && participants[i].Role == RoundRole.Monster)
                return participants[i];
        }

        return null;
    }

    private void Update()
    {
        // Tüm karar mantığı sunucuda. İstemci sadece SyncVar'ları okur.
        if (!isServer)
            return;

        HandleTestKeys();

        if (phase == RoundPhase.Ended)
        {
            lobbyTimer -= Time.deltaTime;
            if (lobbyTimer <= 0f)
                EnterLobby();
            return;
        }

    }

    private void HandleTestKeys()
    {
        // Faz gözetmiyor: botu lobide de tur ortasında da çağırabilmek lazım.
        if (Input.GetKeyDown(summonBotsKey))
            SummonBots();

        if (phase == RoundPhase.Waiting)
        {
            if (Input.GetKeyDown(startRoundKey))
                StartRound();
            else if (Input.GetKeyDown(startAsRunnerKey))
                StartRoundAsRunner();

            return;
        }

        if (phase == RoundPhase.Playing && Input.GetKeyDown(selfEliminateKey))
            SimulateLocalElimination();
    }

    /// <summary>
    /// Test: turu, sunucudaki yerel oyuncu kaçan olacak şekilde başlatır.
    /// Tek gerçek oyuncuyla oynarken canavar hep o seçilir (bot canavar
    /// olamıyor) ve elenemez; izleyici modunu tek başına denemenin yolu bu.
    /// </summary>
    [Server]
    private void StartRoundAsRunner()
    {
        forcedRunner = LocalParticipant();
        StartRound();
        forcedRunner = null;
    }

    /// <summary>
    /// Test: botları önüne ışınlar. Bot kendi başına dolaşıyor ve harita zifiri;
    /// bıçağı denemek için onu aramak testi şansa bırakıyordu.
    /// </summary>
    [Server]
    private void SummonBots()
    {
        RoundParticipant local = LocalParticipant();
        if (local == null)
            return;

        Vector3 destination = local.transform.position + local.transform.forward * 3f;

        // Önün duvarsa üstüne çağırıyoruz; duvarın içine ışınlanan bot işe yaramaz.
        if (Physics.CheckSphere(destination + Vector3.up * 0.25f, 0.55f, ~0,
                QueryTriggerInteraction.Ignore))
            destination = local.transform.position;

        TestRunnerBot[] bots = FindObjectsOfType<TestRunnerBot>();

        for (int i = 0; i < bots.Length; i++)
            bots[i].ServerTeleportTo(destination);

        Debug.Log(bots.Length > 0
            ? $"{bots.Length} bot yanına çağrıldı."
            : "Sahnede test botu yok. Yakalamaca > Test Botu Ekle (kaçan).");
    }

    /// <summary>Test: kendini yakalanmış say.</summary>
    [Server]
    private void SimulateLocalElimination()
    {
        RoundParticipant local = LocalParticipant();
        if (local == null)
            return;

        if (local.Role == RoundRole.Monster)
        {
            Debug.LogWarning($"Canavarsın, elenemezsin. İzleyici modunu denemek için " +
                $"turu [{startAsRunnerKey}] ile başlat.");
            return;
        }

        ReportCaught(local);
    }

    /// <summary>
    /// Sunucuyu da oynayan kişinin katılımcısı. Adanmış sunucuda yerel oyuncu
    /// yok — test tuşları orada sessizce hiçbir şey yapmıyor.
    /// </summary>
    private RoundParticipant LocalParticipant()
        => NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<RoundParticipant>()
            : null;

    // ---------- Katılımcı kaydı ----------

    [Server]
    public void ServerRegister(RoundParticipant participant)
    {
        if (participant == null || participants.Contains(participant))
            return;

        participants.Add(participant);
        ServerRefreshHost();

        // Tur ortasında katılan sahaya çıkmıyor.
        //
        // Mirror bağlantıyı her an kabul ediyor; roller ise tur başında bir
        // kez dağıtılıyor. Müdahale edilmezse geç gelen `RoundRole.None` ile
        // labirentte dolaşıyor: canavar onu vuramıyor (yakalama yalnızca
        // kaçanı kabul ediyor), o da terminal doldurabiliyordu.
        //
        // Bağlantıyı reddetmek yerine izleyici yapıyoruz: arkadaşının odasına
        // girip kapıda kalmaktansa turu izleyip bir sonrakine katılmak
        // beklenen davranış.
        if (phase != RoundPhase.Waiting && !participant.IsBot)
        {
            participant.ServerSetSpectating(true);
            Debug.Log($"{participant.DisplayName} tur ortasında katıldı — izleyici oldu.");
        }
    }

    [Server]
    public void ServerUnregister(RoundParticipant participant)
    {
        if (participant == null || !participants.Remove(participant))
            return;

        // Seçilen canavar ayrıldıysa seçim boşa düşüyor; rastgeleye çeviriyoruz
        // ki lobide "artık burada olmayan biri" seçili görünmesin.
        if (monsterChoice != 0 && participant.netId == monsterChoice)
            monsterChoice = 0;

        ServerRefreshHost();

        if (phase != RoundPhase.Playing)
            return;

        // Canavar ayrılırsa tur oynanamaz hale gelir. Kaçanlar otomatik
        // kazanmış sayılmaz; kovalayan olmayan bir turda süreyi doldurmak
        // başarı değil.
        if (participant.Role == RoundRole.Monster)
        {
            Debug.Log($"{participant.DisplayName} (canavar) ayrıldı. Tur iptal.");
            EndRound(RoundResult.Aborted);
            return;
        }

        if (participant.Role != RoundRole.Runner || !participant.IsAlive)
            return;

        aliveRunnerCount--;
        if (aliveRunnerCount <= 0)
            EndRound(RoundResult.MonsterWins);
    }

    // ---------- Lobi yetkisi ----------

    /// <summary>
    /// Oda sahibini belirler ve işaretler.
    ///
    /// Host modunda sunucuyu çalıştıran kişi (Mirror'ın `localConnection`'ı)
    /// oda sahibi. Adanmış sunucuda öyle biri yok, o zaman ilk bağlanan gerçek
    /// oyuncu sahip oluyor. Sahip ayrılırsa kalanlardan biri devralıyor —
    /// yoksa lobi kimsenin turu başlatamadığı bir ölü odaya dönerdi.
    ///
    /// Bot asla sahip olamaz: bağlantısı yok, düğmeye basamaz.
    /// </summary>
    [Server]
    private void ServerRefreshHost()
    {
        RoundParticipant owner = null;

        for (int i = 0; i < participants.Count; i++)
        {
            RoundParticipant candidate = participants[i];
            if (candidate == null || candidate.IsBot)
                continue;

            if (candidate.connectionToClient != null
                && candidate.connectionToClient == NetworkServer.localConnection)
            {
                owner = candidate;
                break;
            }

            if (owner == null)
                owner = candidate;
        }

        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i] != null)
                participants[i].ServerSetRoomOwner(participants[i] == owner);
        }
    }

    /// <summary>
    /// Çağıran oda sahibi mi. **İstemcinin `isHost` SyncVar'ına güvenilmiyor**;
    /// yetki her komutta sunucunun kendi listesinden doğrulanıyor. SyncVar
    /// yalnızca arayüzün doğru düğmeleri göstermesi için var.
    /// </summary>
    [Server]
    private bool ServerIsHost(RoundParticipant caller)
    {
        if (caller == null || caller.IsBot || !participants.Contains(caller))
            return false;

        for (int i = 0; i < participants.Count; i++)
        {
            RoundParticipant candidate = participants[i];
            if (candidate == null || candidate.IsBot)
                continue;

            if (candidate.connectionToClient != null
                && candidate.connectionToClient == NetworkServer.localConnection)
                return candidate == caller;
        }

        // Adanmış sunucu: ilk gerçek oyuncu sahip.
        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i] != null && !participants[i].IsBot)
                return participants[i] == caller;
        }

        return false;
    }

    /// <summary>Oda sahibi canavarı seçti. 0 = rastgele.</summary>
    [Server]
    public void ServerSetMonsterChoice(RoundParticipant caller, uint chosenNetId)
    {
        if (phase != RoundPhase.Waiting || !ServerIsHost(caller))
            return;

        if (chosenNetId == 0)
        {
            monsterChoice = 0;
            return;
        }

        // Seçilen kişi gerçekten bu turda mı.
        //
        // **Bot da seçilebiliyor**, bilerek: tek başına test ederken kaçan
        // olarak oynamanın tek yolu bu — canavarlığı kovalamayan birine
        // vermek. Rastgele seçimde bot hâlâ aday değil (bkz. PickMonster);
        // fark, bunun açık bir tercih olması.
        for (int i = 0; i < participants.Count; i++)
        {
            RoundParticipant candidate = participants[i];
            if (candidate != null && candidate.netId == chosenNetId)
            {
                monsterChoice = chosenNetId;
                return;
            }
        }
    }

    /// <summary>
    /// Oda sahibi turu başlatmak istiyor. Yetki ve şartlar burada — istemcinin
    /// gri düğmeyi zorlaması işe yaramıyor.
    /// </summary>
    [Server]
    public void ServerRequestStart(RoundParticipant caller)
    {
        if (phase != RoundPhase.Waiting)
            return;

        if (!ServerIsHost(caller))
        {
            Debug.LogWarning($"{caller?.DisplayName}: turu başlatma yetkisi yok.");
            return;
        }

        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i] != null && !participants[i].IsReady)
            {
                Debug.LogWarning($"Tur başlatılamadı: {participants[i].DisplayName} hazır değil.");
                return;
            }
        }

        StartRound();
    }

    // ---------- Tur akışı ----------

    [Server]
    public void StartRound()
    {
        if (participants.Count < minimumPlayers)
        {
            Debug.LogWarning($"Tur başlatılamadı: {participants.Count} oyuncu var, " +
                $"en az {minimumPlayers} gerekiyor.");
            return;
        }

        // Önceki turdan kalan cesetler temizleniyor. Bilerek TUR BAŞINDA,
        // ölümde değil: kullanıcı "haritada kalsın" istedi, yani bir turun
        // SÜRESİ boyunca ceset kalıcı olmalı — yalnızca bir sonraki tur
        // başlarken, harita ve roller sıfırlanırken onunla birlikte gidiyor.
        // Aksi hâlde her oyun oturumu boyunca ceset sayısı sınırsız birikir.
        ServerClearCorpses();

        AssignRoles();

        // Roller belli olduktan SONRA yerleştiriliyor: kim canavar, tur
        // başlayana kadar bilinmiyor ve Mirror'ın doğum noktaları oyuncu
        // objesi spawn olurken, yani rol dağıtılmadan önce seçiliyor.
        ServerPlaceParticipants();

        // Gereken sayı kadro genişliğine göre belirleniyor: haritada hep
        // terminalGoal kadar terminal var ama iki kişilik bir turda tek kaçanın
        // dördünü de doldurması imkânsıza yakın. "Hayattaki kaçan + 1" hem
        // ölçekleniyor hem de hangi terminali yapacağın seçimini bırakıyor.
        requiredTerminals = Mathf.Clamp(aliveRunnerCount + 1, 1, Mathf.Max(1, terminalGoal));
        completedTerminals = 0;
        escapedRunnerCount = 0;

        result = RoundResult.None;
        phase = RoundPhase.Playing;

        Debug.Log($"Tur başladı. {aliveRunnerCount} kaçan, {requiredTerminals} terminal gerekiyor. " +
            "Süre sınırı yok: tur herkes ölene ya da kaçana kadar sürüyor.");
    }

    // ---------- Ceset ----------

    /// <summary>
    /// `RoundParticipant.DeathHold` bitince (ölüm klibi bittiğinde) çağrılır.
    /// Kaçanın o anki gövde pozundan bağımsız, fizik motorlu kalıcı bir ceset
    /// doğurur — CLAUDE.md'deki "haritada kalsın, dümdüz kalmasın, biri
    /// yürüyerek ittirebilsin" isteği.
    ///
    /// `corpsePrefab` boşsa (kurulum aracı hiç çalıştırılmamışsa) sessizce
    /// hiçbir şey yapmıyor: eski davranış (beden gizlenir) korunuyor, oyun
    /// kırılmıyor.
    /// </summary>
    [Server]
    public void ServerSpawnCorpse(RoundParticipant victim)
    {
        if (corpsePrefab == null || victim == null)
            return;

        Transform body = victim.CorpseSourceBody;
        if (body == null)
            return;

        GameObject instance = Instantiate(corpsePrefab, body.position, body.rotation);
        Corpse corpse = instance.GetComponent<Corpse>();

        if (corpse == null)
        {
            Debug.LogWarning("corpsePrefab üstünde Corpse bileşeni yok, yakılıyor.");
            Destroy(instance);
            return;
        }

        // Ceset TAM canavarın üstünde doğuyor — `deathForwardOffset` bilerek 0
        // (bölüm 17: kill animasyonu ikisinin iç içe geçmesini varsayıyor).
        // Görsel poz için sorun değil ama fizik motoru bunu "derin çakışma"
        // sayıp cesedi doğduğu anda fırlatıyordu — haritanın dışına uçan
        // "canavar" ve arkasından hiç görünmeyen ceset şikâyeti buradan
        // geliyordu (aslında fırlayan canavar değil, cesetti). Doğan anda
        // üstüne binen oyuncu collider'larıyla çarpışmayı geçici olarak
        // görmezden geliyoruz; ikisi ayrışınca (ya da en fazla 5 sn sonra)
        // kendiliğinden geri açılıyor.
        Collider corpseCollider = instance.GetComponentInChildren<Collider>();
        if (corpseCollider != null)
            IgnoreOverlappingPlayersTemporarily(corpseCollider, body.position);

        // ÖNCE victimNetId, SONRA Spawn: Mirror ilk durumu spawn mesajına
        // gömüyor, yani her istemci OnStartClient'ta bunu zaten dolu buluyor.
        corpse.ServerInit(victim.netId);
        NetworkServer.Spawn(instance);

        spawnedCorpses.Add(instance);
    }

    /// <summary>
    /// Doğduğu noktada üstüne binen oyuncu collider'larıyla (`Oyuncu` katmanı
    /// — canavar, kaçanlar, botlar) çarpışmayı geçici olarak kapatır.
    ///
    /// **Yalnızca `Oyuncu` katmanı, `~0` DEĞİL.** Zemin de doğum noktasının
    /// hemen altında ve `OverlapSphere` onu da yakalardı — zeminle çarpışmayı
    /// kapatsaydık ceset o birkaç saniye boyunca serbestçe düşüp yerin altına
    /// gömülürdü. Katman filtresi bu riski baştan eliyor.
    ///
    /// **Katman adı burada elle yazılı, `LayerSetup.Oyuncu`'ya atıfla değil**:
    /// `LayerSetup` `Assets/_Scripts/Editor/` altında, yani yalnızca editör
    /// derlemesinde var — çalışma anındaki bu sınıf ona hiç ulaşamıyor
    /// (build'de derlemesi bile projeye girmiyor). Bölüm 16'daki dört katman
    /// adı sabit ve `Katmanları Kur` tarafından tanımlanıyor.
    /// </summary>
    [Server]
    private void IgnoreOverlappingPlayersTemporarily(Collider corpseCollider, Vector3 spawnPosition)
    {
        int playerLayer = LayerMask.NameToLayer("Oyuncu");

        // Katman hiç tanımlanmamışsa (Katmanları Kur çalıştırılmamış) maskeyi
        // ~0'a düşürmüyoruz — o zaman zemini de yakalayıp yukarıdaki gömülme
        // riskini geri getirirdi. Bu durumda cesedin doğduğu anda bir kez
        // sarsılması, üstüne düşülen bir riskten iyi.
        if (playerLayer < 0)
            return;

        int playerMask = 1 << playerLayer;

        // 1 m: hull yarıçapının (~0.3 m) birkaç katı — aynı noktada duran
        // canavarı ve yakındaki kaçanları rahatça kapsıyor.
        Collider[] nearby = Physics.OverlapSphere(spawnPosition, 1f, playerMask, QueryTriggerInteraction.Ignore);

        List<Collider> ignored = new List<Collider>();
        for (int i = 0; i < nearby.Length; i++)
        {
            if (nearby[i] == corpseCollider)
                continue;

            Physics.IgnoreCollision(corpseCollider, nearby[i], true);
            ignored.Add(nearby[i]);
        }

        if (ignored.Count > 0)
            StartCoroutine(RestoreCollisionsWhenClear(corpseCollider, ignored));
    }

    /// <summary>
    /// Yok sayılan çiftler gerçekten ayrışınca (ya da en fazla 5 sn sonra,
    /// güvenlik payı olarak) çarpışmayı geri açar. Sabit bir süre yerine
    /// mesafeye bakmak, biri kasıtlı olarak orada beklerse (test sırasında
    /// olduğu gibi) aynı patlamanın gecikmeli tekrarlanmasını önlüyor.
    /// </summary>
    private IEnumerator RestoreCollisionsWhenClear(Collider corpseCollider, List<Collider> others)
    {
        const float clearDistance = 1.2f;
        const float maxWait = 5f;
        float elapsed = 0f;

        while (elapsed < maxWait && corpseCollider != null)
        {
            bool allClear = true;

            for (int i = 0; i < others.Count; i++)
            {
                if (others[i] == null)
                    continue;

                if (Vector3.Distance(others[i].transform.position, corpseCollider.transform.position) < clearDistance)
                {
                    allClear = false;
                    break;
                }
            }

            if (allClear)
                break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (corpseCollider == null)
            yield break;

        for (int i = 0; i < others.Count; i++)
        {
            if (others[i] != null)
                Physics.IgnoreCollision(corpseCollider, others[i], false);
        }
    }

    /// <summary>
    /// Önceki turdan kalan cesetleri siler. `StartRound`'un başında çağrılır
    /// (yukarıda) — bir turun SÜRESİ boyunca ceset kalıcı kalsın, ama oyun
    /// oturumu boyunca sınırsız birikmesin.
    /// </summary>
    [Server]
    private void ServerClearCorpses()
    {
        for (int i = 0; i < spawnedCorpses.Count; i++)
        {
            if (spawnedCorpses[i] != null)
                NetworkServer.Destroy(spawnedCorpses[i]);
        }

        spawnedCorpses.Clear();
    }

    // ---------- Doğum yerleşimi ----------

    /// <summary>
    /// Tur başında herkesi role göre yerleştirir: **kaçanlar bir arada,
    /// canavar onlardan en uzakta.**
    ///
    /// ### Neden gerekti
    ///
    /// Mirror doğum noktasını oyuncu objesi spawn olurken seçiyor — yani
    /// **lobide, rol dağıtılmadan önce.** Herkes rastgele bir noktaya
    /// düşüyordu ve canavarın bir kaçanın dibinde doğması işten değildi:
    /// kovalamaca daha başlamadan bitiyordu.
    ///
    /// Rol ancak `AssignRoles`'den sonra belli olduğu için yerleştirme de
    /// oraya taşındı. Mirror'ın kendi doğum noktaları duruyor ve hâlâ işe
    /// yarıyor: lobide nerede duracağını onlar belirliyor, tur başında burası
    /// üstüne yazıyor.
    ///
    /// ### Nokta seçimi her turda değişiyor, mesafe değişmiyor
    ///
    /// Kaçanların noktası rastgele seçiliyor, canavarınki **ona en uzak**
    /// olan. Sabit bir çift, birkaç turda ezberlenir ve harita ölürdü; sabit
    /// olan şey mesafenin kendisi, yeri değil.
    /// </summary>
    [Server]
    private void ServerPlaceParticipants()
    {
        if (!ResolveSpawnAnchors(out Transform runnerAnchor, out Transform monsterAnchor))
            return;

        int runnerIndex = 0;

        for (int i = 0; i < participants.Count; i++)
        {
            RoundParticipant participant = participants[i];
            if (participant == null)
                continue;

            if (participant.Role == RoundRole.Monster)
            {
                participant.ServerPlaceAt(monsterAnchor.position, monsterAnchor.rotation);
                continue;
            }

            participant.ServerPlaceAt(
                RunnerSlot(runnerAnchor.position, runnerIndex), runnerAnchor.rotation);

            runnerIndex++;
        }
    }

    /// <summary>
    /// Kaçanların dizileceği noktalar: ilki merkezde, kalanlar çevresinde bir
    /// halkada.
    ///
    /// Hepsini aynı noktaya koymak `CharacterController`'ları birbirini itmeye
    /// zorluyor ve oyuncular tur başlar başlamaz fırlıyordu.
    /// </summary>
    private Vector3 RunnerSlot(Vector3 anchor, int index)
    {
        if (index <= 0 || runnerSpawnSpread <= 0f)
            return anchor;

        float step = Mathf.PI * 2f / Mathf.Max(1, LobbyRoster.MaxPlayers - 1);
        float angle = (index - 1) * step;

        Vector3 candidate = anchor
            + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * runnerSpawnSpread;

        // Duvarın içine denk gelirse merkeze düşülüyor: iki kaçanın aynı
        // noktada doğması, birinin duvara gömülmesinden iyi. Sınama
        // `NetworkSetup.BuildSpawnPoints` ile aynı ölçülerde.
        return Physics.CheckSphere(candidate + Vector3.up * 0.25f, 0.55f, ~0,
            QueryTriggerInteraction.Ignore)
            ? anchor
            : candidate;
    }

    /// <summary>
    /// İki doğum çapasını seçer. Elle konmuş noktalar varsa onlar geçerli,
    /// yoksa sahnedeki doğum noktalarından en uzak çift bulunuyor.
    /// </summary>
    [Server]
    private bool ResolveSpawnAnchors(out Transform runnerAnchor, out Transform monsterAnchor)
    {
        runnerAnchor = runnerSpawn;
        monsterAnchor = monsterSpawn;

        if (runnerAnchor != null && monsterAnchor != null)
            return true;

        NetworkStartPosition[] points = FindObjectsOfType<NetworkStartPosition>();

        if (points.Length < 2)
        {
            Debug.LogWarning("En az iki doğum noktası gerekiyor; oyuncular oldukları " +
                "yerde başlıyor. Yakalamaca > Ağ Kurulumu (1. adım) onları kuruyor.");
            return false;
        }

        runnerAnchor = points[Random.Range(0, points.Length)].transform;

        float best = -1f;

        for (int i = 0; i < points.Length; i++)
        {
            Transform candidate = points[i].transform;
            if (candidate == runnerAnchor)
                continue;

            float distance = (candidate.position - runnerAnchor.position).sqrMagnitude;
            if (distance <= best)
                continue;

            best = distance;
            monsterAnchor = candidate;
        }

        if (monsterAnchor == null)
            return false;

        // Sessiz kalmıyoruz: noktalar birbirine yakın kurulmuşsa mesafe
        // garantisi diye bir şey kalmıyor ve sorun yine "canavar dibimde
        // doğdu" olarak geri döner.
        float separation = Mathf.Sqrt(best);

        if (separation < minimumSpawnSeparation)
        {
            Debug.LogWarning($"Canavar kaçanlardan yalnızca {separation:F1} m uzakta " +
                $"doğuyor (istenen en az {minimumSpawnSeparation} m). Doğum noktaları " +
                "birbirine çok yakın — RoundManager'daki çapaları elle koymak çözer.");
        }

        return true;
    }

    /// <summary>
    /// Yakalama bildirimi. Kararı burası verir — vuran taraf sadece bildirir.
    /// Çift eleme ve tur dışı bildirimler burada eleniyor.
    /// </summary>
    [Server]
    public void ReportCaught(RoundParticipant victim, RoundParticipant killer = null)
    {
        if (phase != RoundPhase.Playing)
            return;
        if (victim == null || !victim.IsAlive || victim.Role != RoundRole.Runner)
            return;

        // Öldüren ÖNCE: kurbanın bedeni canavarın önüne oturtuluyor ve ölüm
        // hook'u çalışırken bu değerin gelmiş olması gerekiyor.
        victim.ServerSetKiller(killer);
        victim.ServerSetAlive(false);
        aliveRunnerCount--;

        // Ölüm, kalanlara iş yükü bindirmesin: gereken sayı da bir azalıyor.
        // Kaçmakta bu indirim yok — sadece ölümde (bkz. CLAUDE.md 11.1).
        requiredTerminals = Mathf.Max(1, requiredTerminals - 1);

        Debug.Log($"{victim.DisplayName} yakalandı. Sahada {aliveRunnerCount} kaçan kaldı, " +
            $"gereken terminal {requiredTerminals}.");

        ServerCheckRoundEnd();
    }

    /// <summary>Kaçan çıkıştan geçti — kurtuldu.</summary>
    [Server]
    public void ReportEscaped(RoundParticipant runner)
    {
        if (phase != RoundPhase.Playing)
            return;
        if (runner == null || !runner.IsAlive || runner.IsEscaped || runner.Role != RoundRole.Runner)
            return;

        runner.ServerSetEscaped();
        aliveRunnerCount--;
        escapedRunnerCount++;

        Debug.Log($"{runner.DisplayName} kaçmayı başardı. Sahada {aliveRunnerCount} kaçan kaldı.");

        ServerCheckRoundEnd();
    }

    /// <summary>Bir terminal tamamlandı; yeterse çıkış açılıyor.</summary>
    [Server]
    public void ReportTerminalCompleted()
    {
        if (phase != RoundPhase.Playing)
            return;

        completedTerminals++;

        if (ExitOpen)
            Debug.Log($"Çıkış açıldı ({completedTerminals}/{requiredTerminals}).");
    }

    /// <summary>
    /// Sahada oynayan kaçan kalmadıysa tur biter. Süre sınırı olmadığı için
    /// turu bitiren tek şey bu: ya hepsi elendi, ya hepsi çıktı.
    /// </summary>
    [Server]
    private void ServerCheckRoundEnd()
    {
        if (phase != RoundPhase.Playing || aliveRunnerCount > 0)
            return;

        if (disableRoundEndForTesting)
        {
            Debug.Log("Tur normalde burada biterdi ama 'disableRoundEndForTesting' " +
                "açık — RoundManager'da kapatmayı unutma.");
            return;
        }

        EndRound(escapedRunnerCount > 0 ? RoundResult.RunnersWin : RoundResult.MonsterWins);
    }

    /// <summary>
    /// Bir kişi canavar, kalan herkes kaçan. Seçim sunucuda.
    ///
    /// Canavar yalnızca gerçek oyunculardan seçilir: test botu kovalayamaz,
    /// canavar olsaydı tur kimse elenmeden süre dolana kadar sürerdi. Aday
    /// kalmazsa canavarsız bir tur başlıyor — yalnızca test senaryolarında
    /// olacak bir durum, o yüzden uyarıyla geçiliyor.
    /// </summary>
    [Server]
    private void AssignRoles()
    {
        RoundParticipant monster = PickMonster();

        if (monster == null)
            Debug.LogWarning("Canavar seçilemedi (aday yok). Tur canavarsız başlıyor — " +
                "yalnızca test için anlamlı.");

        aliveRunnerCount = 0;

        for (int i = 0; i < participants.Count; i++)
        {
            bool isMonster = participants[i] == monster;

            participants[i].ServerSetRole(isMonster ? RoundRole.Monster : RoundRole.Runner);
            participants[i].ServerSetAlive(true);

            if (!isMonster)
                aliveRunnerCount++;
        }
    }

    [Server]
    private RoundParticipant PickMonster()
    {
        // Açık seçim önce geliyor ve **bot da seçilebiliyor**: tek başına test
        // ederken kaçan olarak oynamanın yolu, canavarlığı kovalamayan birine
        // vermek. Seçim `forcedRunner`'ı geçemiyor — [2] tuşuyla "beni kaçan
        // yap" testi lobi seçiminin önünde kalmalı.
        if (monsterChoice != 0)
        {
            for (int i = 0; i < participants.Count; i++)
            {
                RoundParticipant candidate = participants[i];

                if (candidate != null && candidate != forcedRunner && candidate.netId == monsterChoice)
                    return candidate;
            }
        }

        // Rastgele seçimde bot aday değil: kimse istemeden canavarlık
        // kovalayamayan birine düşerse tur kimse elenmeden sürüncemede kalır.
        List<RoundParticipant> candidates = new List<RoundParticipant>();

        for (int i = 0; i < participants.Count; i++)
        {
            RoundParticipant candidate = participants[i];

            if (candidate == null || candidate.IsBot || candidate == forcedRunner)
                continue;

            candidates.Add(candidate);
        }

        return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
    }

    [Server]
    private void EndRound(RoundResult outcome)
    {
        result = outcome;
        phase = RoundPhase.Ended;
        lobbyTimer = lobbyReturnDelay;

        switch (outcome)
        {
            case RoundResult.MonsterWins:
                Debug.Log("Tur bitti: canavar kazandı, kimse kurtulamadı.");
                break;
            case RoundResult.RunnersWin:
                Debug.Log("Tur bitti: süre doldu, kaçanlar kazandı.");
                break;
            default:
                Debug.Log("Tur iptal edildi: canavar ayrıldı.");
                break;
        }
    }

    /// <summary>Roller sıfırlanır, elenenler tekrar hareket edebilir hale gelir.</summary>
    [Server]
    private void EnterLobby()
    {
        phase = RoundPhase.Waiting;
        aliveRunnerCount = 0;
        escapedRunnerCount = 0;
        completedTerminals = 0;

        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i] == null)
                continue;

            participants[i].ServerSetRole(RoundRole.None);
            participants[i].ServerSetEscaped(false);

            // Geç katılan bir sonraki tura normal giriyor.
            participants[i].ServerSetSpectating(false);
            participants[i].ServerSetAlive(true);

            // Hazır işareti her turda sıfırlanıyor: bir öncekinden kalan
            // "hazır", masadan kalkmış birini oyuna sokardı.
            participants[i].ServerSetReady(false);
        }

        // Canavar seçimi de sıfırlanıyor. Aynı kişiyi üst üste canavar yapmak
        // isteyen oda sahibi tekrar seçer; varsayılan rastgele olmalı.
        monsterChoice = 0;

        ServerRefreshHost();

        Debug.Log("Lobiye dönüldü.");
    }
}
