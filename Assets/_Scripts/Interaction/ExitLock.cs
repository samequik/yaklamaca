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

    [SyncVar] private int code;
    [SyncVar] private int entered;
    [SyncVar] private uint activeUserNetId;
    [SyncVar] private bool solved;

    private bool focusApplied;
    private bool usingLocally;
    private GUIStyle screenStyle;

    public bool IsSolved => solved;
    public bool IsBusy => activeUserNetId != 0;

    public bool IsUsedByLocalPlayer =>
        activeUserNetId != 0 && NetworkClient.localPlayer != null
        && NetworkClient.localPlayer.netId == activeUserNetId;

    private void OnDisable()
    {
        // Tur biterken panel kapanırsa kilit oyuncunun üstünde kalmasın.
        if (focusApplied)
            ReleaseLocalFocus();
    }

    private void Update()
    {
        if (isServer)
            ServerTick();

        UpdateLocalFocus();
        ReadLocalInput();
        UpdateStatusLight();
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
    private void OnGUI()
    {
        if (!IsUsedByLocalPlayer)
            return;

        screenStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            richText = false
        };

        float scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 1.8f);
        float width = 468f * scale;
        float height = 176f * scale;

        Rect box = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f,
            width, height);

        // Tek renk ailesi: her şey sarının bir tonu. Tamamlananlar da yeşil
        // değil sönük sarı — panelin kimliği renkten geliyor.
        Color accent = new Color(0.95f, 0.8f, 0.15f);
        Color accentDim = new Color(0.52f, 0.43f, 0.10f);
        Color ink = new Color(0.05f, 0.045f, 0.02f);

        Fill(box, new Color(0.045f, 0.042f, 0.028f, 0.94f));

        // İnce tek çizgi yerine tam çerçeve + köşe ayraçları: sci-fi kitin
        // paneleriyle aynı dil, ve panel havada duran bir dikdörtgen gibi
        // durmuyor.
        Frame(box, 2f * scale, accentDim);
        Brackets(box, 22f * scale, 3f * scale, accent);

        float pad = 16f * scale;
        Rect inner = new Rect(box.x + pad, box.y + pad, box.width - pad * 2f, box.height - pad * 2f);

        // Başlık şeridi.
        Rect title = new Rect(inner.x, inner.y, inner.width, 20f * scale);
        Fill(new Rect(title.x, title.y, title.width, title.height), new Color(0.11f, 0.10f, 0.04f));
        Fill(new Rect(title.x, title.yMax - 1f * scale, title.width, 1f * scale), accentDim);
        DrawLabel(title, "Ç I K I Ş   K İ L İ D İ", 13f * scale, accent, TextAnchor.MiddleCenter);

        // Dizilim: her adım kendi hücresinde. Sıradaki hücre DOLU sarı, yazısı
        // koyu — göz onu aramak zorunda kalmıyor.
        float gap = 3f * scale;
        float cellWidth = (inner.width - gap * (SequenceLength - 1)) / SequenceLength;
        float cellHeight = 42f * scale;
        float cellY = inner.y + 30f * scale;

        for (int i = 0; i < SequenceLength; i++)
        {
            Rect cell = new Rect(inner.x + (cellWidth + gap) * i, cellY, cellWidth, cellHeight);

            bool done = i < entered;
            bool current = i == entered;

            Fill(cell, current ? accent
                : done ? new Color(0.16f, 0.14f, 0.05f)
                : new Color(0.085f, 0.082f, 0.06f));

            if (!current)
                Frame(cell, 1f * scale, done ? accentDim : new Color(0.17f, 0.17f, 0.14f));

            DrawLabel(cell, DirectionArrow(DirectionAt(i)), 24f * scale,
                current ? ink : done ? accentDim : new Color(0.42f, 0.41f, 0.35f),
                TextAnchor.MiddleCenter);
        }

        // İlerleme: hücrelerin altında ince bir şerit, aynı genişlikte.
        Rect bar = new Rect(inner.x, cellY + cellHeight + 8f * scale, inner.width, 4f * scale);
        Fill(bar, new Color(0.12f, 0.11f, 0.06f));
        Fill(new Rect(bar.x, bar.y, bar.width * entered / SequenceLength, bar.height), accent);

        DrawLabel(new Rect(inner.x, inner.yMax - 16f * scale, inner.width, 16f * scale),
            $"{entered} / {SequenceLength}      yanlış tuş başa sarar",
            10f * scale, new Color(0.55f, 0.52f, 0.38f), TextAnchor.MiddleCenter);
    }

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    /// <summary>Dört kenara çerçeve çizer.</summary>
    private static void Frame(Rect rect, float thickness, Color color)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
        Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    /// <summary>
    /// Köşe ayraçları: her köşede kısa ve kalın iki çizgi. Çerçevenin tamamını
    /// kalınlaştırmak paneli ağırlaştırıyordu; vurgu köşelerde toplanınca hem
    /// oturaklı hem hafif duruyor.
    /// </summary>
    private static void Brackets(Rect rect, float length, float thickness, Color color)
    {
        // Sol üst
        Fill(new Rect(rect.x, rect.y, length, thickness), color);
        Fill(new Rect(rect.x, rect.y, thickness, length), color);

        // Sağ üst
        Fill(new Rect(rect.xMax - length, rect.y, length, thickness), color);
        Fill(new Rect(rect.xMax - thickness, rect.y, thickness, length), color);

        // Sol alt
        Fill(new Rect(rect.x, rect.yMax - thickness, length, thickness), color);
        Fill(new Rect(rect.x, rect.yMax - length, thickness, length), color);

        // Sağ alt
        Fill(new Rect(rect.xMax - length, rect.yMax - thickness, length, thickness), color);
        Fill(new Rect(rect.xMax - thickness, rect.yMax - length, thickness, length), color);
    }

    private void DrawLabel(Rect rect, string text, float fontSize, Color color, TextAnchor anchor)
    {
        screenStyle.fontSize = Mathf.RoundToInt(fontSize);
        screenStyle.alignment = anchor;
        screenStyle.normal.textColor = color;
        GUI.Label(rect, text, screenStyle);
    }
}
