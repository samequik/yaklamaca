using Mirror;
using UnityEngine;

/// <summary>
/// Kaçanların açması gereken duvar terminali. Gereken sayıda terminal bitince
/// çıkış açılır (bkz. CLAUDE.md bölüm 11).
///
/// Mekaniğin bedeli **durmak**: terminal başındaki kaçan hareket edemiyor ve
/// yalnızca dar bir açıda bakabiliyor. Kovalamacada durmak, canavara fırsat
/// vermek demek — oyunun gerilimi buradan geliyor.
///
/// ---
///
/// **İlerleme kalıcı.** Yarıda bırakılan terminal sıfırlanmıyor; başkası
/// devam edebiliyor. Ölen kişinin emeği kaybolmasın diye: aksi halde her ölüm,
/// o ana kadar yapılan işi de silerdi ve kartopu daha da sertleşirdi.
///
/// **Karar sunucuda.** İlerlemeyi sunucu sayıyor, istemci sadece "başlıyorum"
/// ve "bıraktım" diyor. İstemci yüzde bildirseydi, değiştirilmiş bir istemci
/// terminali anında bitirirdi.
/// </summary>
public class Terminal : NetworkBehaviour, IInteractable
{
    [Header("Doldurma")]
    [Tooltip("Kesintisiz çalışıldığında terminalin bitme süresi (saniye).")]
    [SerializeField] private float fillDuration = 18f;

    [Tooltip("Oyuncu bu mesafeden uzaklaşırsa bağlantı kendiliğinden kopar. " +
        "Kaçan zaten hareket edemiyor ama ışınlanma veya sekme ihtimaline karşı.")]
    [SerializeField] private float maxUseDistance = 2.5f;

    [Header("Görsel")]
    [Tooltip("Yüzdeye göre dolan gösterge — ileride ekran materyali de olabilir.")]
    [SerializeField] private Renderer progressLight;

    [SerializeField] private Color idleColor = new Color(0.15f, 0.35f, 0.5f);
    [SerializeField] private Color activeColor = new Color(0.2f, 0.9f, 1f);
    [SerializeField] private Color lockedColor = new Color(1f, 0.25f, 0.1f);
    [SerializeField] private Color doneColor = new Color(0.25f, 1f, 0.35f);

    /// <summary>0-1 arası doluluk. Yalnızca sunucu yazar.</summary>
    [SyncVar] private float progress;

    /// <summary>
    /// Şu an kim çalışıyor. 0 = boş. netId tutuyoruz çünkü istemcinin ihtiyacı
    /// olan tek şey "bu ben miyim" sorusunun cevabı.
    /// </summary>
    [SyncVar] private uint activeUserNetId;

    /// <summary>
    /// Kilitli mi. Yanlış yön tuşu veya canavarın müdahalesi kilitliyor;
    /// açılana kadar ilerleme duruyor (4. ve 6. adımda dolacak).
    /// </summary>
    [SyncVar] private bool locked;

    [Header("Mini Oyun")]
    [Tooltip("İki yön sınavı arasındaki bekleme aralığı (saniye).")]
    [SerializeField] private Vector2 promptInterval = new Vector2(3.5f, 7f);

    [Tooltip("Doğru tuşa basmak için tanınan süre (saniye). Sunucu buna ağ " +
        "gecikmesi payı ekliyor, yoksa yüksek pingli oyuncu haksız yere kaybederdi.")]
    [SerializeField] private float promptWindow = 1.6f;

    [Tooltip("E'ye bastıktan sonra dolmanın başlaması için beklenen süre (saniye). " +
        "E'ye basıp bırakarak terminal tıkırdatmayı anlamsız kılıyor.")]
    [SerializeField] private float startDelay = 1f;

    [Tooltip("Kilit açma örüntüsündeki yön sayısı.")]
    [SerializeField] private int unlockLength = 4;

    [Tooltip("Canavarın terminali kilitlemesi kaç saniye sürer. Bu süre boyunca " +
        "canavar da hareket edemez — kilitlemek bedava olmamalı. 3.5 sn " +
        "oynandığında fazla geldi: canavarı çok uzun savunmasız bırakıyor ve " +
        "kilitlemeyi hiç yapılmayan bir hamleye çeviriyordu.")]
    [SerializeField] private float monsterLockDuration = 1.5f;

    [Header("Odaklanma")]
    [Tooltip("Terminal başındayken sağa-sola bakabilme açısı (derece, tek yöne).")]
    [SerializeField] private float focusYawLimit = 35f;

    [Tooltip("Terminal başındayken yukarı-aşağı bakabilme açısı (derece).")]
    [SerializeField] private float focusPitchLimit = 12f;

    /// <summary>Beklenen yön: 0 yok, 1 W, 2 S, 3 A, 4 D.</summary>
    [SyncVar] private byte prompt;

    /// <summary>Sınavın biteceği ağ zamanı.</summary>
    [SyncVar] private double promptDeadline;

    /// <summary>Kilit örüntüsü, yön başına 2 bit paketli.</summary>
    [SyncVar] private int unlockCode;

    /// <summary>Örüntüde kaç yön doğru girildi.</summary>
    [SyncVar] private int unlockEntered;

    /// <summary>Canavarın kilitleme işleminin biteceği ağ zamanı. 0 = kilitlemiyor.</summary>
    [SyncVar] private double monsterLockEndTime;

    /// <summary>Dolumun başlayacağı ağ zamanı — bağlanma gecikmesi.</summary>
    [SyncVar] private double fillReadyTime;

    // Sınav sayacı **dolum süresiyle** ilerliyor, duvar saatiyle değil.
    private float fillSincePrompt;
    private float nextPromptAfter;
    private bool focusApplied;

    // Etkileşim tuşunu bu terminal mi üstlendi — bırakırken geri vermek için.
    private bool usingLocally;

    private GUIStyle screenStyle;
    private MaterialPropertyBlock propertyBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public float Progress => progress;
    public bool IsCompleted => progress >= 1f;
    public bool IsLocked => locked;
    public bool IsBusy => activeUserNetId != 0;

    /// <summary>Bu terminali kullanan benim mi — odaklanma kısıtı buna bakacak.</summary>
    public bool IsUsedByLocalPlayer =>
        activeUserNetId != 0
        && NetworkClient.localPlayer != null
        && NetworkClient.localPlayer.netId == activeUserNetId;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Ödünç alınan etkileşim tuşunu geri verir. Oyuncu terminaldeyken elenirse
    /// ya da terminal yok edilirse bırakılmazsa etkileşim bir daha çalışmazdı.
    /// </summary>
    private void OnDisable()
    {
        if (!usingLocally)
            return;

        usingLocally = false;
        PlayerInteractor.InputCaptured = false;
    }

    private void Update()
    {
        if (isServer)
            ServerTick();

        UpdateLocalFocus();
        ReadLocalInput();
        UpdateVisual();
    }

    /// <summary>
    /// Yön tuşlarını okur. Sınav sırasında cevap, kilitliyken örüntü girişi
    /// olarak gidiyor. Karar sunucuda: burası yalnızca hangi tuşa basıldığını
    /// bildiriyor.
    /// </summary>
    private void ReadLocalInput()
    {
        // Etkileşim tuşunu terminal üstleniyor: bağlıyken bakış terminalden
        // kayabiliyor ve PlayerInteractor'ın ışını onu bulamıyor. Çıkmak için
        // yeniden nişan almak gerekmemeli.
        if (usingLocally != IsUsedByLocalPlayer)
        {
            usingLocally = IsUsedByLocalPlayer;
            PlayerInteractor.InputCaptured = usingLocally;
        }

        if (!IsUsedByLocalPlayer)
            return;

        if (KeyBindings.Pressed(GameAction.Interact))
        {
            Interact(null);
            return;
        }

        byte direction = ReadDirectionKey();
        if (direction == 0)
            return;

        if (locked)
            CmdUnlockInput(direction);
        else if (prompt != 0)
            CmdAnswerPrompt(direction);
    }

    private static byte ReadDirectionKey()
    {
        // Hareket tuşlarını kullanıyor, sabit WASD'yi değil: tuşlarını
        // değiştiren oyuncu için sınav aksi hâlde oynanamaz hâle gelirdi.
        if (KeyBindings.Pressed(GameAction.Forward)) return 1;
        if (KeyBindings.Pressed(GameAction.Back)) return 2;
        if (KeyBindings.Pressed(GameAction.Left)) return 3;
        if (KeyBindings.Pressed(GameAction.Right)) return 4;
        return 0;
    }

    private static string DirectionLabel(int direction)
    {
        // Tuş adı atamadan okunuyor: sabit "W" yazmak, tuşlarını değiştiren
        // oyuncuya yanlış tuşu gösterirdi.
        switch (direction)
        {
            case 1: return "↑ " + KeyBindings.Describe(KeyBindings.Get(GameAction.Forward));
            case 2: return "↓ " + KeyBindings.Describe(KeyBindings.Get(GameAction.Back));
            case 3: return "← " + KeyBindings.Describe(KeyBindings.Get(GameAction.Left));
            case 4: return "→ " + KeyBindings.Describe(KeyBindings.Get(GameAction.Right));
            default: return "";
        }
    }

    /// <summary>
    /// Terminal başındaki oyuncunun hareketini kilitler, bakışını dar bir
    /// koniye sıkıştırır. Kilidi terminal veriyor çünkü "kim kullanıyor"
    /// bilgisi burada; PlayerController sadece uyguluyor.
    ///
    /// Sunucu bağlantıyı düşürdüğünde (ölüm, uzaklaşma, tamamlanma)
    /// activeUserNetId sıfırlanıyor ve kilit kendiliğinden kalkıyor.
    /// </summary>
    private void UpdateLocalFocus()
    {
        bool usedByMe = IsUsedByLocalPlayer;

        if (usedByMe == focusApplied)
            return;

        focusApplied = usedByMe;

        if (NetworkClient.localPlayer == null)
            return;

        PlayerController controller = NetworkClient.localPlayer.GetComponent<PlayerController>();
        if (controller == null)
            return;

        if (usedByMe)
            controller.BeginFocus(focusYawLimit, focusPitchLimit);
        else
            controller.EndFocus();
    }

    // ---------- Sunucu ----------

    [Server]
    private void ServerTick()
    {
        RoundManager manager = RoundManager.Instance;

        // Lobiye dönülünce terminaller sıfırlanıyor; yeni tur temiz başlasın.
        if (manager == null || manager.Phase == RoundPhase.Waiting)
        {
            if (progress > 0f || locked || activeUserNetId != 0)
                ServerReset();

            return;
        }

        if (activeUserNetId == 0 || IsCompleted)
            return;

        RoundParticipant user = ServerFindActiveUser();

        // Kullanan öldü, kaçtı, koptu ya da uzaklaştıysa bağlantı düşsün.
        if (user == null
            || !user.IsAlive
            || user.IsEscaped
            || Vector3.Distance(user.transform.position, transform.position) > maxUseDistance)
        {
            ServerRelease();
            return;
        }

        // Terminalin başındaki canavarsa iş kilitlemek; kaçansa doldurmak.
        if (user.Role == RoundRole.Monster)
        {
            ServerTickMonsterLock();
            return;
        }

        // Kilitliyken ilerleme durur ama kullanıcı bağlı kalır — örüntüyü
        // girmesi lazım.
        if (locked)
            return;

        // Bağlanma gecikmesi dolmadan ne dolum başlıyor ne de sınav sayacı
        // ilerliyor.
        if (NetworkTime.time < fillReadyTime)
            return;

        ServerTickPrompt();

        // Sınav ekrandayken ilerleme DURUYOR. Doğru yöne basınca devam ediyor.
        // Böylece sınav bir "arada bir çıkan engel" değil, ilerlemenin şartı:
        // ekrana bakmadan terminal doldurulamıyor.
        if (prompt != 0)
            return;

        progress = Mathf.Min(1f, progress + Time.deltaTime / Mathf.Max(fillDuration, 0.1f));

        if (!IsCompleted)
            return;

        prompt = 0;
        ServerRelease();
        manager.ReportTerminalCompleted();

        Debug.Log($"{name} tamamlandı.");
    }

    /// <summary>
    /// Yön sınavını yürütür: aralıklarla bir yön ister, süresi dolarsa
    /// terminali kilitler. Sınavı **sunucu üretiyor** — istemci üretseydi
    /// değiştirilmiş bir istemci hiç sınav çıkarmazdı.
    /// </summary>
    [Server]
    private void ServerTickPrompt()
    {
        if (prompt != 0)
        {
            // Gecikme payı: pakete gidip gelme süresi kadar geç gelen doğru cevap
            // haksız yere kaybettirmesin.
            if (NetworkTime.time > promptDeadline + NetworkTime.rtt + 0.15)
                ServerFailPrompt("süre doldu");

            return;
        }

        if (nextPromptAfter <= 0f)
            ServerScheduleNextPrompt();

        // Sayaç **kat edilen dolum süresiyle** ilerliyor, duvar saatiyle değil.
        // Duvar saati olsaydı E'ye basıp bırakarak sayaç sıfırlanır ve sınav
        // hiç çıkmazdı — terminal bedavaya doldurulurdu. Şimdi bağlantıyı
        // kesmek yalnızca kendi ilerlemeni durduruyor, sınavı ertelemiyor.
        fillSincePrompt += Time.deltaTime;

        if (fillSincePrompt < nextPromptAfter)
            return;

        prompt = (byte)Random.Range(1, 5);
        promptDeadline = NetworkTime.time + promptWindow;
    }

    /// <summary>
    /// Yanlış tuş ya da geç kalma: terminal kilitlenir, kilidi açacak örüntü
    /// üretilir ve **canavara haber gider**. Haber, canavarın devriye
    /// rotasını seçmesini sağlayan asıl bilgi.
    /// </summary>
    [Server]
    private void ServerFailPrompt(string reason) => ServerApplyLock(true, reason);

    /// <summary>
    /// Terminali kilitler ve kilit örüntüsünü üretir.
    ///
    /// alertMonster: kaçanın hatasında canavara haber gidiyor. Kilidi canavarın
    /// kendisi kurduğunda haber göndermenin anlamı yok — zaten orada duruyor.
    /// </summary>
    [Server]
    private void ServerApplyLock(bool alertMonster, string reason)
    {
        prompt = 0;
        unlockEntered = 0;

        unlockCode = 0;
        for (int i = 0; i < Mathf.Max(1, unlockLength); i++)
            unlockCode |= Random.Range(0, 4) << (i * 2);

        locked = true;

        if (alertMonster)
        {
            RoundManager manager = RoundManager.Instance;
            RoundParticipant monster = manager != null ? manager.ServerFindMonster() : null;

            if (monster != null && monster.connectionToClient != null)
                TargetTerminalAlarm(monster.connectionToClient, transform.position);
        }

        Debug.Log($"{name} kilitlendi ({reason}).");
    }

    /// <summary>Yalnızca canavarın bağlantısına gider — kaçanların haberi olmamalı.</summary>
    [TargetRpc]
    private void TargetTerminalAlarm(NetworkConnectionToClient target, Vector3 position)
    {
        AlarmMessage = "TERMİNAL ALARMI";
        AlarmPosition = position;
        AlarmTime = Time.time;
    }

    /// <summary>Canavarın ekranında gösterilecek son alarm — RoundHud okuyor.</summary>
    public static string AlarmMessage { get; private set; }
    public static Vector3 AlarmPosition { get; private set; }
    public static float AlarmTime { get; private set; }

    /// <summary>
    /// Canavarın kilitleme sayacı. Süre dolana kadar canavar terminalin
    /// başında hareketsiz duruyor — kilitlemenin bedeli bu. O sırada
    /// savunmasız: kaçan yanından geçip gidebilir.
    /// </summary>
    [Server]
    private void ServerTickMonsterLock()
    {
        if (NetworkTime.time < monsterLockEndTime)
            return;

        ServerApplyLock(false, "canavar kilitledi");

        monsterLockEndTime = 0;
        ServerRelease();
    }

    /// <summary>Canavar terminali kilitlemeye başlıyor.</summary>
    [Command(requiresAuthority = false)]
    private void CmdBeginMonsterLock(NetworkConnectionToClient sender = null)
    {
        RoundManager manager = RoundManager.Instance;

        if (manager == null || manager.Phase != RoundPhase.Playing)
            return;
        if (locked || IsCompleted || IsBusy)
            return;
        if (sender == null || sender.identity == null)
            return;

        RoundParticipant participant = sender.identity.GetComponent<RoundParticipant>();

        if (participant == null || participant.Role != RoundRole.Monster || !participant.IsAlive)
            return;
        if (Vector3.Distance(sender.identity.transform.position, transform.position) > maxUseDistance)
            return;

        activeUserNetId = sender.identity.netId;
        monsterLockEndTime = NetworkTime.time + Mathf.Max(0.1f, monsterLockDuration);
    }

    [Server]
    private void ServerScheduleNextPrompt()
    {
        fillSincePrompt = 0f;
        nextPromptAfter = Random.Range(promptInterval.x, promptInterval.y);
    }

    [Server]
    private void ServerReset()
    {
        progress = 0f;
        locked = false;
        activeUserNetId = 0;
        prompt = 0;
        unlockEntered = 0;
    }

    /// <summary>
    /// Kullanıcıyı bırakır; ilerleme olduğu yerde kalır ama kilit örüntüsü
    /// baştan başlar — yarım girilmiş bir örüntü ayrılıp dönerek biriktirilemesin.
    /// </summary>
    [Server]
    private void ServerRelease()
    {
        activeUserNetId = 0;
        unlockEntered = 0;
        monsterLockEndTime = 0;
    }

    [Server]
    private RoundParticipant ServerFindActiveUser()
    {
        if (!NetworkServer.spawned.TryGetValue(activeUserNetId, out NetworkIdentity identity)
            || identity == null)
            return null;

        return identity.GetComponent<RoundParticipant>();
    }

    /// <summary>Dışarıdan kilitleme — mini oyun ve canavar bunu kullanacak.</summary>
    [Server]
    public void ServerSetLocked(bool value)
    {
        locked = value;

        if (value)
            ServerRelease();
    }

    // ---------- İstemciden sunucuya ----------

    [Command(requiresAuthority = false)]
    private void CmdBeginUse(NetworkConnectionToClient sender = null)
    {
        RoundManager manager = RoundManager.Instance;

        if (manager == null || manager.Phase != RoundPhase.Playing)
            return;
        // Kilitliyken de bağlanılabiliyor: kilidi açmanın yolu terminalin
        // başına geçip örüntüyü girmek.
        if (IsCompleted || IsBusy)
            return;
        if (sender == null || sender.identity == null)
            return;

        RoundParticipant participant = sender.identity.GetComponent<RoundParticipant>();

        // Canavar terminal dolduramaz; onun terminal üzerindeki yetkisi ayrı
        // (kilitleme — 6. adım).
        if (participant == null
            || participant.Role != RoundRole.Runner
            || !participant.IsAlive
            || participant.IsEscaped)
            return;

        if (Vector3.Distance(sender.identity.transform.position, transform.position) > maxUseDistance)
            return;

        activeUserNetId = sender.identity.netId;

        // Bağlanma gecikmesi. Sınav sayacına DOKUNMUYORUZ: burada sıfırlasaydık
        // E'ye basıp bırakarak sınavdan kaçmak mümkün olurdu.
        fillReadyTime = NetworkTime.time + Mathf.Max(0f, startDelay);
    }

    /// <summary>Yön sınavına cevap. Yanlışsa terminal kilitlenir.</summary>
    [Command(requiresAuthority = false)]
    private void CmdAnswerPrompt(byte direction, NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null || sender.identity.netId != activeUserNetId)
            return;
        if (locked || prompt == 0)
            return;

        if (direction == prompt)
        {
            prompt = 0;
            ServerScheduleNextPrompt();
            return;
        }

        ServerFailPrompt("yanlış yön");
    }

    /// <summary>Kilit örüntüsüne giriş. Yanlış basınca örüntü baştan başlar.</summary>
    [Command(requiresAuthority = false)]
    private void CmdUnlockInput(byte direction, NetworkConnectionToClient sender = null)
    {
        if (!locked || direction == 0)
            return;
        if (sender == null || sender.identity == null || sender.identity.netId != activeUserNetId)
            return;

        int expected = ((unlockCode >> (unlockEntered * 2)) & 3) + 1;

        if (direction != expected)
        {
            unlockEntered = 0;
            return;
        }

        unlockEntered++;

        if (unlockEntered < Mathf.Max(1, unlockLength))
            return;

        // Örüntü tamam: kilit açılıyor, ilerleme kaldığı yerden devam ediyor.
        locked = false;
        unlockEntered = 0;
        prompt = 0;
        ServerScheduleNextPrompt();
    }

    [Command(requiresAuthority = false)]
    private void CmdEndUse(NetworkConnectionToClient sender = null)
    {
        // Yalnızca çalışan kişi bırakabilir; başkası "bıraktı" diyememeli.
        if (sender != null && sender.identity != null && sender.identity.netId == activeUserNetId)
            ServerRelease();
    }

    // ---------- Etkileşim ----------

    public string GetPrompt()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null || manager.Phase != RoundPhase.Playing)
            return null;

        int percent = Mathf.RoundToInt(progress * 100f);

        if (IsCompleted)
            return $"Terminal tamamlandı  (%100)";

        // Canavar terminale bakınca yüzdesini görüyor (bkz. CLAUDE.md 11.4).
        // Hangi terminalin ne kadar dolduğunu bilmek, devriye rotasını
        // seçebilmesi demek.
        if (LocalPlayerIsMonster())
        {
            if (locked)
                return $"Kilitli  (%{percent})";

            return IsUsedByLocalPlayer
                ? $"Kilitleniyor…  (%{percent})"
                : $"Kilitle  (%{percent})";
        }

        // Kilitliyken ne yapılacağını da yazıyoruz: "KİLİTLİ" tek başına
        // "buraya dokunma" gibi okunuyordu.
        if (locked)
            return $"Kilidi aç  (%{percent})";

        if (IsUsedByLocalPlayer)
            return $"Bırak  (%{percent})";

        if (IsBusy)
            return $"Meşgul  (%{percent})";

        return $"Terminali çalıştır  (%{percent})";
    }

    public void Interact(GameObject user)
    {
        if (IsCompleted)
            return;

        // Canavarın terminaldeki işi doldurmak değil kilitlemek.
        if (LocalPlayerIsMonster())
        {
            if (IsUsedByLocalPlayer)
                CmdEndUse();
            else if (!locked && !IsBusy)
                CmdBeginMonsterLock();

            return;
        }

        // Aynı tuş hem başlatıyor hem bırakıyor: terminal başındayken oyuncu
        // hareket edemediği için ayrılmanın başka bir yolu olmalı.
        //
        // Kilitliyken de bağlanılabiliyor — kilidi açmanın yolu terminalin
        // başına geçip örüntüyü girmek. Burada "locked" kontrolü kalmıştı ve
        // kilitlenen terminale bir daha girilemiyordu.
        if (IsUsedByLocalPlayer)
            CmdEndUse();
        else if (!IsBusy)
            CmdBeginUse();
    }

    private static bool LocalPlayerIsMonster()
    {
        if (NetworkClient.localPlayer == null)
            return false;

        RoundParticipant local = NetworkClient.localPlayer.GetComponent<RoundParticipant>();
        return local != null && local.Role == RoundRole.Monster;
    }

    /// <summary>
    /// Terminal ekranı: küçük, yeşil fosfor bir bilgisayar paneli. Yalnızca
    /// terminali kullanan oyuncunun ekranına çiziliyor.
    ///
    /// Tam ekran değil, bilerek. Terminal başındayken oyuncu hareket edemiyor
    /// ve tek savunması etrafını duyup görebilmek; ekranı kaplayan bir arayüz
    /// mekaniğin bedelini haksız hâle getirirdi.
    ///
    /// Prototip arayüz — RoundHud ve PlayerInteractor ile birlikte gerçek UI'a
    /// geçilince silinecek (CLAUDE.md teknik borç 2).
    /// </summary>
    private void OnGUI()
    {
        if (!IsUsedByLocalPlayer)
            return;

        EnsureScreenStyles();

        float scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 1.8f);
        float width = 360f * scale;
        float height = 168f * scale;

        // Tam ortada. Nişangah bu sırada çizilmiyor (PlayerInteractor girdiyi
        // terminale bıraktığında susuyor), o yüzden ortayı kapatan bir şey yok
        // ve göz sınav yönünü aramak zorunda kalmıyor.
        Rect box = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f,
            width, height);

        DrawScreenBody(box, scale);
    }

    private void DrawScreenBody(Rect box, float scale)
    {
        bool monsterLocking = monsterLockEndTime > 0d && LocalPlayerIsMonster();
        Color accent = monsterLocking ? LockingColor : locked ? AlarmColor : ScreenGreen;

        DrawPanel(box, accent);

        float pad = 12f * scale;
        Rect inner = new Rect(box.x + pad, box.y + pad, box.width - pad * 2f, box.height - pad * 2f);

        float headerHeight = 20f * scale;
        DrawLabel(new Rect(inner.x, inner.y, inner.width, headerHeight),
            HeaderText(monsterLocking), 15f * scale, accent, TextAnchor.MiddleLeft);

        DrawLabel(new Rect(inner.x, inner.y, inner.width, headerHeight),
            $"[{KeyBindings.Describe(KeyBindings.Get(GameAction.Interact))}] bırak",
            13f * scale, Fade(accent, 0.55f), TextAnchor.MiddleRight);

        float y = inner.y + headerHeight + 6f * scale;

        if (monsterLocking)
        {
            DrawLockingScreen(inner, y, scale, accent);
            return;
        }

        if (locked)
        {
            DrawLockedScreen(inner, y, scale, accent);
            return;
        }

        if (NetworkTime.time < fillReadyTime)
        {
            DrawLabel(new Rect(inner.x, y, inner.width, 40f * scale), "BAĞLANIYOR…",
                24f * scale, Fade(accent, 0.7f), TextAnchor.MiddleCenter);
            return;
        }

        DrawFillingScreen(inner, y, scale, accent);
    }

    private string HeaderText(bool monsterLocking)
    {
        if (monsterLocking)
            return "KİLİTLEME";

        if (locked)
            return "SİSTEM KİLİTLİ";

        return NetworkTime.time < fillReadyTime ? "BAĞLANTI" : "VERİ AKTARIMI";
    }

    /// <summary>Canavar kilitlerken: geri sayım ve dolan çubuk.</summary>
    private void DrawLockingScreen(Rect inner, float y, float scale, Color accent)
    {
        float left = Mathf.Max(0f, (float)(monsterLockEndTime - NetworkTime.time));
        float done = monsterLockDuration > 0f
            ? Mathf.Clamp01(1f - left / monsterLockDuration)
            : 1f;

        DrawLabel(new Rect(inner.x, y, inner.width, 44f * scale), $"{left:0.0}",
            34f * scale, accent, TextAnchor.MiddleCenter);

        DrawBar(new Rect(inner.x, y + 48f * scale, inner.width, 12f * scale), done, accent);

        DrawLabel(new Rect(inner.x, y + 66f * scale, inner.width, 24f * scale),
            "hareket edemezsin", 14f * scale, Fade(accent, 0.6f), TextAnchor.MiddleCenter);
    }

    /// <summary>Kilitliyken: girilmesi gereken yön örüntüsü.</summary>
    private void DrawLockedScreen(Rect inner, float y, float scale, Color accent)
    {
        int percent = Mathf.RoundToInt(progress * 100f);

        DrawLabel(new Rect(inner.x, y, inner.width, 26f * scale),
            $"ilerleme %{percent} — donduruldu", 14f * scale, Fade(accent, 0.6f),
            TextAnchor.MiddleCenter);

        DrawLabel(new Rect(inner.x, y + 26f * scale, inner.width, 40f * scale),
            BuildUnlockText(), 26f * scale, accent, TextAnchor.MiddleCenter);

        DrawLabel(new Rect(inner.x, y + 68f * scale, inner.width, 24f * scale),
            "sırayla gir", 14f * scale, Fade(accent, 0.6f), TextAnchor.MiddleCenter);
    }

    /// <summary>Normal dolum: yüzde, çubuk ve varsa yön sınavı.</summary>
    private void DrawFillingScreen(Rect inner, float y, float scale, Color accent)
    {
        int percent = Mathf.RoundToInt(progress * 100f);

        DrawLabel(new Rect(inner.x, y, inner.width, 44f * scale), $"%{percent}",
            34f * scale, accent, TextAnchor.MiddleCenter);

        DrawBar(new Rect(inner.x, y + 48f * scale, inner.width, 12f * scale), progress, accent);

        if (prompt == 0)
        {
            DrawLabel(new Rect(inner.x, y + 66f * scale, inner.width, 24f * scale),
                "aktarım sürüyor", 14f * scale, Fade(accent, 0.55f), TextAnchor.MiddleCenter);
            return;
        }

        // Sınav: kalan süre hem yazıyla hem incelen bir çubukla. Süre azalınca
        // kırmızıya dönüyor — ekrana bakmadan terminal doldurulamıyor.
        float remaining = Mathf.Max(0f, (float)(promptDeadline - NetworkTime.time));
        float ratio = promptWindow > 0f ? Mathf.Clamp01(remaining / promptWindow) : 0f;
        Color urgency = ratio < 0.35f ? AlarmColor : PromptColor;

        DrawLabel(new Rect(inner.x, y + 62f * scale, inner.width, 34f * scale),
            DirectionLabel(prompt), 26f * scale, urgency, TextAnchor.MiddleCenter);

        DrawBar(new Rect(inner.x + inner.width * 0.25f, y + 96f * scale,
            inner.width * 0.5f, 6f * scale), ratio, urgency);
    }

    // ---------- Ekran çizim yardımcıları ----------

    private static readonly Color ScreenGreen = new Color(0.35f, 1f, 0.45f);
    private static readonly Color AlarmColor = new Color(1f, 0.35f, 0.2f);
    private static readonly Color LockingColor = new Color(1f, 0.65f, 0.2f);
    private static readonly Color PromptColor = new Color(0.55f, 1f, 0.85f);
    private static readonly Color PanelBack = new Color(0.02f, 0.05f, 0.03f, 0.88f);

    // Tek piksellik beyaz doku: kutu, çerçeve ve çubuklar bununla çiziliyor.
    private static Texture2D solidTexture;

    private void EnsureScreenStyles()
    {
        screenStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, richText = false };

        if (solidTexture != null)
            return;

        solidTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        solidTexture.SetPixel(0, 0, Color.white);
        solidTexture.Apply();
    }

    private static Color Fade(Color color, float alpha) =>
        new Color(color.r, color.g, color.b, alpha);

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, solidTexture);
        GUI.color = previous;
    }

    /// <summary>Koyu gövde + ince çerçeve. Ekranın "monitör" hissi buradan.</summary>
    private static void DrawPanel(Rect box, Color accent)
    {
        Fill(box, PanelBack);

        Color border = Fade(accent, 0.55f);
        Fill(new Rect(box.x, box.y, box.width, 2f), border);
        Fill(new Rect(box.x, box.yMax - 2f, box.width, 2f), border);
        Fill(new Rect(box.x, box.y, 2f, box.height), border);
        Fill(new Rect(box.xMax - 2f, box.y, 2f, box.height), border);
    }

    private static void DrawBar(Rect rect, float fill, Color accent)
    {
        Fill(rect, Fade(accent, 0.18f));
        Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), accent);
    }

    private void DrawLabel(Rect rect, string text, float fontSize, Color color, TextAnchor anchor)
    {
        screenStyle.fontSize = Mathf.RoundToInt(fontSize);
        screenStyle.alignment = anchor;
        screenStyle.normal.textColor = color;
        GUI.Label(rect, text, screenStyle);
    }

    /// <summary>Girilen kısmı işaretli, kalanı gizli örüntü.</summary>
    private string BuildUnlockText()
    {
        int length = Mathf.Max(1, unlockLength);
        string text = "";

        for (int i = 0; i < length; i++)
        {
            int direction = ((unlockCode >> (i * 2)) & 3) + 1;
            text += i < unlockEntered ? $"[{DirectionLabel(direction)}] " : $"{DirectionLabel(direction)} ";
        }

        return text.Trim();
    }

    // ---------- Görsel ----------

    /// <summary>
    /// Gösterge rengi durumu anlatıyor: boşta mavi, çalışırken parlak, kilitli
    /// kırmızı, bitmiş yeşil. Karanlık koridorda uzaktan okunabilmesi önemli.
    /// </summary>
    private void UpdateVisual()
    {
        if (progressLight == null)
            return;

        Color color;

        if (IsCompleted)
            color = doneColor;
        else if (locked)
            color = lockedColor;
        else if (IsBusy)
            color = Color.Lerp(idleColor, activeColor, Mathf.PingPong(Time.time * 2f, 1f));
        else
            color = Color.Lerp(idleColor, activeColor, progress);

        propertyBlock.SetColor(ColorId, color);
        progressLight.SetPropertyBlock(propertyBlock);
    }
}
