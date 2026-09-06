using Mirror;
using UnityEngine;

/// <summary>
/// Çıkış kapısının yanındaki kilit paneli. Terminaller bitince kapı **tak diye
/// açılmıyor**; kaçanın buraya gelip on adımlık bir yön dizilimini doğru
/// girmesi gerekiyor.
///
/// ### Neden
///
/// Terminaller bitince kapının kendiliğinden açılması turun son perdesini
/// bedavaya veriyordu: kaçan sadece koşup çıkıyordu, canavarın yapabileceği
/// bir şey yoktu. Panel başında **hareket kilitli** ve dizilim on adım — yani
/// çıkış artık canavara son bir pencere açıyor.
///
/// Dead by Daylight'ın kapı açma süreciyle aynı fikir: hedefler bitse bile
/// kaçış bir eylem gerektiriyor ve o eylem seni bir yere çiviliyor.
///
/// ### Kurallar
///
/// - Panel yalnızca **terminaller bitince** çalışıyor (`RoundManager.ExitOpen`).
/// - Yalnızca **kaçan** kullanabiliyor; canavarın çıkışta işi yok.
/// - Yanlış yön dizilimi **başa sarıyor** — dizilim değişmiyor, baştan
///   giriliyor. Yeni bir dizilim üretmek ezberi imkânsız kılardı ve ceza
///   orantısız olurdu.
/// - Panelden ayrılmak (E) ilerlemeyi sıfırlıyor: kilidin yarısını bırakıp
///   sonra dönmek bedava olmamalı.
///
/// ### Ağ
///
/// Karar sunucuda: dizilimi sunucu üretiyor, doğrulamayı sunucu yapıyor, kapıyı
/// sunucu açıyor. İstemci yalnızca "şu yöne bastım" diyor. Değiştirilmiş bir
/// istemci en fazla kendi ekranında yanlış bir panel çizer (CLAUDE.md bölüm 4).
///
/// Dizilim `code` içinde **yön başına 2 bit** paketli — `Terminal.unlockCode`
/// ile aynı numara. On adım 20 bit, int'e sığıyor.
/// </summary>
public class ExitLock : NetworkBehaviour, IInteractable
{
    /// <summary>Dizilimdeki yön sayısı. 2 bit/yön: 10 adım = 20 bit.</summary>
    public const int SequenceLength = 10;

    [Tooltip("Açılacak çıkış kapısı. Boşsa panel hiçbir şey yapmaz.")]
    [SerializeField] private SlidingDoor door;

    [Header("Odak")]
    [Tooltip("Panel başında sağa-sola bakış sınırı (derece).")]
    [SerializeField] private float focusYawLimit = 35f;

    [Tooltip("Panel başında yukarı-aşağı bakış sınırı (derece).")]
    [SerializeField] private float focusPitchLimit = 12f;

    [Header("Gösterge")]
    [Tooltip("Işıktan ETKİLENMEYEN materyal kullanmalı, yoksa karanlıkta " +
        "siyah görünür (CLAUDE.md 11.2).")]
    [SerializeField] private Renderer statusLight;

    [Header("Ses")]
    [Tooltip("Panelin hoparlörü. Boş bırakılırsa Awake kendi kuruyor.")]
    [SerializeField] private AudioSource stateSource;

    [Tooltip("Panel başındayken dönen çalışma sesi. Terminallerle AYNI klip: " +
        "ikisi de 'makinenin başında duruyorsun' mekaniği, ayrı ses ikisini " +
        "farklı şeylermiş gibi gösterirdi. `Sesleri Yerleştir` bağlıyor.")]
    [SerializeField] private AudioClip workingClip;

    [SerializeField] private float workingVolume = 0.45f;

    [Tooltip("Sesin duyulduğu mesafe (metre). Terminaldekiyle aynı: panelde " +
        "uğraşmak da kendini ele vermek demek.")]
    [SerializeField] private float soundRange = 18f;

    [SyncVar] private int code;
    [SyncVar] private int entered;
    [SyncVar] private uint activeUserNetId;
    [SyncVar] private bool solved;

    private bool focusApplied;
    private bool usingLocally;

    public bool IsSolved => solved;
    public bool IsBusy => activeUserNetId != 0;

    public bool IsUsedByLocalPlayer =>
        activeUserNetId != 0 && NetworkClient.localPlayer != null
        && NetworkClient.localPlayer.netId == activeUserNetId;

    private void OnDisable()
    {
        // Ekran kaydı da bırakılıyor: panel yok olan bir kilide bakmaya devam
        // ederse ekranda asılı kalır.
        if (ActiveLocal == this)
            ActiveLocal = null;

        // Tur biterken panel kapanırsa kilit oyuncunun üstünde kalmasın.
        if (focusApplied)
            ReleaseLocalFocus();
    }

    private void Awake()
    {
        stateSource = GetOrCreateStateSource();
    }

    /// <summary>
    /// Panelin hoparlörünü kurar. `Terminal.GetOrCreateStateSource` ile aynı
    /// gerekçe: paneli kuran araç var olan sahneye dokunmuyor, hoparlörün
    /// çalışma anında kurulması tek çıkar yol.
    /// </summary>
    private AudioSource GetOrCreateStateSource()
    {
        AudioSource source = stateSource != null ? stateSource : GetComponent<AudioSource>();

        if (source == null)
            source = gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = soundRange;

        return source;
    }

    private void Update()
    {
        if (isServer)
            ServerTick();

        UpdateLocalFocus();
        ReadLocalInput();
        UpdateStatusLight();
        UpdateAudio();

        UpdateScreenRegistration();
    }

    /// <summary>
    /// Panel başında biri varken çalışma sesi dönüyor.
    ///
    /// Terminaldekinin aynısı ve aynı sebeple ağdan hiçbir şey gelmiyor:
    /// `activeUserNetId` ve `solved` zaten SyncVar, her istemci aynı sonucu
    /// kendi hesaplıyor (bölüm 4).
    ///
    /// Dizilim çözülünce susuyor — kapı açıldıktan sonra ötmeye devam eden
    /// bir panel, işi bitmiş bir makine gibi durmazdı.
    /// </summary>
    private void UpdateAudio()
    {
        if (stateSource == null)
            return;

        bool working = IsBusy && !solved && workingClip != null;

        if (!working)
        {
            if (stateSource.isPlaying)
                stateSource.Stop();

            return;
        }

        stateSource.volume = workingVolume;

        if (stateSource.clip != workingClip)
        {
            stateSource.clip = workingClip;
            stateSource.Play();
            return;
        }

        if (!stateSource.isPlaying)
            stateSource.Play();
    }

    // ---------- İstemci ----------

    private void ReadLocalInput()
    {
        // Etkileşim tuşunu panel üstleniyor: bağlıyken bakış panelden kayabilir
        // ve PlayerInteractor'ın ışını onu bulamaz. Çıkmak için yeniden nişan
        // almak gerekmemeli (Terminal'de de aynı sorun yaşandı).
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

        if (direction != 0)
            CmdInput(direction);
    }

    /// <summary>
    /// Hareket tuşlarını kullanıyor, sabit WASD'yi değil: tuşlarını değiştiren
    /// oyuncu için dizilim aksi hâlde oynanamaz hâle gelirdi (CLAUDE.md 13).
    /// </summary>
    private static byte ReadDirectionKey()
    {
        if (KeyBindings.Pressed(GameAction.Forward)) return 1;
        if (KeyBindings.Pressed(GameAction.Back)) return 2;
        if (KeyBindings.Pressed(GameAction.Left)) return 3;
        if (KeyBindings.Pressed(GameAction.Right)) return 4;
        return 0;
    }

    private static string DirectionArrow(int direction)
    {
        switch (direction)
        {
            case 1: return "↑";
            case 2: return "↓";
            case 3: return "←";
            case 4: return "→";
            default: return "·";
        }
    }

    private int DirectionAt(int index) => ((code >> (index * 2)) & 3) + 1;

    private void UpdateLocalFocus()
    {
        bool usedByMe = IsUsedByLocalPlayer;

        if (usedByMe == focusApplied)
            return;

        focusApplied = usedByMe;

        if (usedByMe)
            ApplyLocalFocus();
        else
            ReleaseLocalFocus();
    }

    private void ApplyLocalFocus()
    {
        PlayerController controller = LocalController();

        if (controller != null)
            controller.BeginFocus(focusYawLimit, focusPitchLimit);
    }

    private void ReleaseLocalFocus()
    {
        focusApplied = false;

        PlayerController controller = LocalController();

        if (controller != null)
            controller.EndFocus();
    }

    private static PlayerController LocalController() =>
        NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<PlayerController>()
            : null;

    private static bool LocalPlayerIsMonster()
    {
        if (NetworkClient.localPlayer == null)
            return false;

        RoundParticipant local = NetworkClient.localPlayer.GetComponent<RoundParticipant>();
        return local != null && local.Role == RoundRole.Monster;
    }

    /// <summary>Gösterge: kapalıyken kırmızı, hazırken sarı, açılınca yeşil.</summary>
    private void UpdateStatusLight()
    {
        if (statusLight == null || statusLight.sharedMaterial == null)
            return;

        Color color;

        if (solved)
            color = new Color(0.2f, 0.9f, 0.3f);
        else if (Available())
            color = new Color(0.95f, 0.8f, 0.15f);
        else
            color = new Color(0.7f, 0.15f, 0.15f);

        if (statusLight.sharedMaterial.color != color)
            statusLight.sharedMaterial.color = color;
    }

    // ---------- Etkileşim ----------

    /// <summary>Terminaller bitti mi ve tur sürüyor mu.</summary>
    private bool Available()
    {
        RoundManager manager = RoundManager.Instance;
        return manager != null && manager.Phase == RoundPhase.Playing && manager.ExitOpen;
    }

    public string GetPrompt()
    {
        RoundManager manager = RoundManager.Instance;

        if (manager == null || manager.Phase != RoundPhase.Playing)
            return null;

        // Canavarın çıkışta işi yok; panel ona hiçbir şey söylemiyor.
        if (LocalPlayerIsMonster())
            return null;

        if (solved)
            return "Çıkış açık";

        if (!manager.ExitOpen)
            return $"Kilitli — önce terminaller ({manager.CompletedTerminals}/{manager.RequiredTerminals})";

        if (IsUsedByLocalPlayer)
            return $"Bırak  ({entered}/{SequenceLength})";

        if (IsBusy)
            return "Meşgul";

        return "Çıkışı aç";
    }

    public void Interact(GameObject user)
    {
        if (solved || LocalPlayerIsMonster())
            return;

        // Aynı tuş hem başlatıyor hem bırakıyor: panel başındayken oyuncu
        // hareket edemediği için ayrılmanın başka yolu yok.
        if (IsUsedByLocalPlayer)
            CmdEndUse();
        else if (!IsBusy && Available())
            CmdBeginUse();
    }

    // ---------- Sunucu ----------

    /// <summary>
    /// Paneldeki oyuncu hâlâ geçerli mi. Ölen, kurtulan ya da bağlantısı kopan
    /// biri paneli sonsuza kadar meşgul bırakmamalı.
    /// </summary>
    [Server]
    private void ServerTick()
    {
        if (activeUserNetId == 0)
            return;

        if (!NetworkServer.spawned.TryGetValue(activeUserNetId, out NetworkIdentity identity)
            || identity == null)
        {
            ServerRelease();
            return;
        }

        RoundParticipant participant = identity.GetComponent<RoundParticipant>();

        if (participant == null || !participant.IsAlive || participant.IsEscaped || !Available())
            ServerRelease();
    }

    [Server]
    private void ServerRelease()
    {
        activeUserNetId = 0;
        entered = 0;
    }

    [Command(requiresAuthority = false)]
    private void CmdBeginUse(NetworkConnectionToClient sender = null)
    {
        if (solved || IsBusy || !Available())
            return;
        if (sender == null || sender.identity == null)
            return;

        RoundParticipant participant = sender.identity.GetComponent<RoundParticipant>();

        if (participant == null || participant.Role != RoundRole.Runner
            || !participant.IsAlive || participant.IsEscaped)
            return;

        activeUserNetId = sender.identity.netId;
        entered = 0;

        // Dizilim her bağlanışta yeniden üretiliyor: aynı kapıyı ikinci kez
        // açan biri ezberden geçmesin.
        code = 0;

        for (int i = 0; i < SequenceLength; i++)
            code |= Random.Range(0, 4) << (i * 2);
    }

    [Command(requiresAuthority = false)]
    private void CmdEndUse(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;
        if (sender.identity.netId != activeUserNetId)
            return;

        ServerRelease();
    }

    [Command(requiresAuthority = false)]
    private void CmdInput(byte direction, NetworkConnectionToClient sender = null)
    {
        if (solved || direction == 0)
            return;
        if (sender == null || sender.identity == null)
            return;
        if (sender.identity.netId != activeUserNetId)
            return;

        // Yanlış yön: BAŞA SARIYOR. Dizilim aynı kalıyor — yenisini üretmek
        // ekrandaki diziyi okumayı anlamsız kılar ve cezayı orantısız yapardı.
        if (direction != DirectionAt(entered))
        {
            entered = 0;
            return;
        }

        entered++;

        if (entered < SequenceLength)
            return;

        solved = true;
        entered = 0;
        activeUserNetId = 0;

        if (door != null)
            door.SetOpen(true);
    }

    /// <summary>Yeni tur: kapı yeniden kapanıyor, dizilim sıfırlanıyor.</summary>
    [Server]
    public void ServerReset()
    {
        solved = false;
        entered = 0;
        code = 0;
        activeUserNetId = 0;
    }

    // ---------- Ekran ----------

    /// <summary>
    /// Panel ekranı. Terminaldekiyle aynı yerde ve boyutta: nişangahın olduğu
    /// nokta, ekranın tam ortası. Nişangah bu sırada çizilmiyor
    /// (`PlayerInteractor.InputCaptured`), yani ortayı kapatan bir şey yok.
    ///
    /// Tam ekran değil, bilerek: panel başındaki oyuncunun tek savunması
    /// etrafını duyup görebilmek.
    /// </summary>
    // ---------- Ekran verisi ----------

    /// <summary>
    /// Şu anda YEREL oyuncunun başında olduğu kilit paneli; yoksa null.
    ///
    /// Ekran her panelin kendi `OnGUI`'si yerine Canvas'taki tek bir panelde
    /// çiziliyor (teknik borç 2). Statik olması dürüst: panel başında hareket
    /// kilitli, yani aynı anda yalnızca birine bağlanılabiliyor.
    /// </summary>
    public static ExitLock ActiveLocal { get; private set; }

    /// <summary>Kaç adım doğru girildi.</summary>
    public int Entered => entered;

    /// <summary>Dizilimin `index`. adımının ok karakteri.</summary>
    public string ArrowAt(int index) => DirectionArrow(DirectionAt(index));

    /// <summary>
    /// Dizilimin `index`. adımına karşılık gelen TUŞUN adı.
    ///
    /// Ok karakteri fontta bulunamazsa ekran buna düşüyor. Varsayılan TMP
    /// atlası statik ve yalnızca temel Latin kapsıyor; oklar ancak dinamik
    /// fallback'ten geliyor ve garantisi yok — lobi etiketlerinde bir kez
    /// "★" boş kutuya dönüşmüştü (bölüm 13).
    /// </summary>
    public string KeyAt(int index)
    {
        switch (DirectionAt(index))
        {
            case 1: return KeyBindings.Describe(KeyBindings.Get(GameAction.Forward));
            case 2: return KeyBindings.Describe(KeyBindings.Get(GameAction.Back));
            case 3: return KeyBindings.Describe(KeyBindings.Get(GameAction.Left));
            case 4: return KeyBindings.Describe(KeyBindings.Get(GameAction.Right));
            default: return "?";
        }
    }

    private void UpdateScreenRegistration()
    {
        if (IsUsedByLocalPlayer)
            ActiveLocal = this;
        else if (ActiveLocal == this)
            ActiveLocal = null;
    }
}
