using System.Collections;
using EpicTransport;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menü ile Mirror arasındaki köprü: sunucu açma, kodla bağlanma, ayrılma ve
/// tur başlayınca menüyü kapatma.
///
/// **Neden ayrı bir bileşen ve neden NetworkBehaviour değil?** Menü sahnede
/// duran bir obje ve ona `NetworkIdentity` eklenemiyor: Mirror sunucu açılışında
/// sahnedeki bütün kimlikleri `SetActive(true)` yapıp spawn ediyor
/// (CLAUDE.md bölüm 4). Bu yüzden menü ağ durumunu yalnızca **statiklerden
/// okuyor** (`NetworkServer.active`, `NetworkClient.isConnected`,
/// `RoundManager.Instance`), komut göndermesi gerektiğinde ise oyuncunun kendi
/// objesini kullanıyor (`RoundParticipant.Local`).
///
/// Bağlanma hatası sessiz kalmamalı: Mirror başarısız bağlantıyı da
/// `OnDisconnectedEvent` ile bildiriyor, "hiç bağlanamadan koptu" ile
/// "bağlandı sonra koptu" arasındaki farkı burada ayırıyoruz.
/// </summary>
public class LobbyNetwork : MonoBehaviour
{
    public enum State
    {
        Offline,     // ana menü, ağ kapalı
        Connecting,  // istemci bağlanmayı bekliyor
        InLobby,     // bağlı, tur başlamadı
        InRound      // tur oynanıyor
    }

    [SerializeField] private MenuController menu;

    [Tooltip("Bağlanma denemesinin vazgeçme süresi (saniye). Ulaşılamayan bir " +
        "adreste Mirror kendi zaman aşımını beklerken oyuncu ne olduğunu " +
        "anlamıyor; bu sayaç ona bir cevap veriyor.")]
    [SerializeField] private float connectTimeout = 10f;

    [Tooltip("İnternet üzerinden oynatan EOS transport'u. Boşsa yalnızca aynı " +
        "ağda (ya da sanal ağda) oynanabiliyor. EOS Kurulumu bağlıyor.")]
    [SerializeField] private EosTransport relayTransport;

    [Tooltip("EOS'un lobi servisi — 6 harflik kısa oda kodunu o üretiyor. " +
        "Boşsa oda yine kuruluyor ama kod host'un 32 karakterlik ürün kimliği " +
        "oluyor. EOS Kurulumu bağlıyor.")]
    [SerializeField] private RelayLobby relayLobby;

    [Tooltip("Aynı ağ için doğrudan bağlantı. EOS hazır değilken ve IP ile " +
        "katılırken kullanılıyor: internet gerektirmiyor, anında açılıyor.")]
    [SerializeField] private Transport localTransport;

    [Tooltip("EOS'un bağlanması için beklenecek en uzun süre (saniye). Giriş " +
        "asenkron; süre dolarsa yerel odaya düşülüyor ve sebebi ekranda " +
        "yazıyor. Sonsuza kadar beklemek oyuncuyu asılı bırakırdı.")]
    [SerializeField] private float relayWaitTimeout = 12f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// **TEST ANAHTARI (F8): EOS hazır olsa bile yerel odayı zorlar.**
    ///
    /// ### Neden gerekti
    ///
    /// EOS kimliği **cihaz** başına üretiliyor (bölüm 13): editördeki oyun da
    /// build de aynı `ProductUserId`'yi alıyor, yani biri öbürünün koduna
    /// bağlanmaya çalıştığında KENDİNE bağlanmış oluyor ve EOS reddediyor.
    /// Aynı bilgisayarda iki kopyayla test etmek bu yüzden imkânsızdı.
    ///
    /// KCP'de böyle bir sorun yok — orada kimlik ADRES, iki kopya
    /// `127.0.0.1` üzerinden birbirini görüyor. Yani yerel test hep
    /// mümkündü; eksik olan tek şey **host'un yerel yolu SEÇEBİLMESİ**ydi:
    /// `LOBİ KUR` EOS hazırsa her zaman onu tercih ediyor.
    ///
    /// Katılma tarafı zaten hazırdı: `JoinLobby` bir IP görünce
    /// `localTransport`'a geçiyor (aşağıdaki `TryToAddress` dalı). Yani bu
    /// bayrak ağ mimarisine hiç dokunmuyor, yalnızca var olan iki yoldan
    /// hangisinin seçileceğini söylüyor.
    ///
    /// **STATİK, bilerek.** Tutorial'dan dönünce sahne yeniden yükleniyor ve
    /// bu obje ölüyor (bölüm 16'nın `DontDestroyOnLoad` dersi); alana yazılan
    /// bir değer orada kaybolurdu. `ScreenEffects.Master` ve
    /// `retroPresetIndex` ile aynı gerekçe.
    ///
    /// **Play yeniden başlayınca sıfırlanıyor** (domain reload) — bir test
    /// bayrağının gerçek bir oturuma sessizce sızmaması gerekiyor.
    /// </summary>
    public static bool ForceLocalRoom;
#else
    // Yayınlanan build'de SABİT false: derleyici aşağıdaki dalları tamamen
    // atıyor, yani F8 de zorlama mesajı da gönderilen oyuna hiç girmiyor.
    private const bool ForceLocalRoom = false;
#endif

    /// <summary>Ekranda gösterilecek son durum/hata metni.</summary>
    public string StatusMessage { get; private set; } = string.Empty;

    /// <summary>Odanın kodu — sunucuysak kendi adresimizden, istemciysek girilen.</summary>
    public string RoomCode { get; private set; } = LobbyCode.Unknown;

    public static LobbyNetwork Instance { get; private set; }

    private float connectTimer;
    private bool leaving;

    // Faz geçişini yakalamak için: menüyü her karede zorlamak, tur ortasında
    // Esc'ye basan oyuncunun duraklatma ekranını anında kapatırdı.
    private RoundPhase lastPhase = RoundPhase.Waiting;
    private bool hadLocalPlayer;

    public State CurrentState
    {
        get
        {
            if (!NetworkClient.active && !NetworkServer.active)
                return State.Offline;

            if (!NetworkClient.isConnected)
                return State.Connecting;

            RoundManager manager = RoundManager.Instance;
            return manager != null && manager.Phase == RoundPhase.Playing
                ? State.InRound
                : State.InLobby;
        }
    }

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        NetworkClient.OnConnectedEvent += HandleConnected;
        NetworkClient.OnDisconnectedEvent += HandleDisconnected;
    }

    private void OnDisable()
    {
        NetworkClient.OnConnectedEvent -= HandleConnected;
        NetworkClient.OnDisconnectedEvent -= HandleDisconnected;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        RebindToLiveManager();
        TickConnectTimeout();
        TickPhase();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        TickForceLocalKey();
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ---------- F8: yerel odayı zorla (yalnızca editör/dev build) ----------

    [Tooltip("EOS'u atlayıp yerel odayı zorlayan test tuşu. Aynı " +
        "bilgisayarda iki kopyayla oynamanın tek yolu bu.")]
    [SerializeField] private KeyCode forceLocalKey = KeyCode.F8;

    private TMP_Text forceLocalLabel;
    private bool forceLocalLabelWanted;
    private bool forceLocalLabelState;

    private void TickForceLocalKey()
    {
        if (Input.GetKeyDown(forceLocalKey))
        {
            // Ağ açıkken değiştirmek hiçbir şey yapmıyor (transport yalnızca
            // oda kurulurken seçiliyor) ama oyuncuya "değişti" gibi görünürdü.
            if (NetworkServer.active || NetworkClient.active)
            {
                Debug.Log("[YerelOda] Oda açıkken değiştirilemez — önce AYRIL.");
            }
            else
            {
                ForceLocalRoom = !ForceLocalRoom;
                Debug.Log(ForceLocalRoom
                    ? "[YerelOda] ZORLANDI: LOBİ KUR artık KCP ile açılacak."
                    : "[YerelOda] Kapatıldı: EOS hazırsa internet odası açılacak.");
            }

            forceLocalLabelWanted = true;
        }

        if (!forceLocalLabelWanted)
            return;

        if (forceLocalLabel == null)
            BuildForceLocalLabel();

        if (forceLocalLabelState == ForceLocalRoom && forceLocalLabel.text.Length > 0)
            return;

        forceLocalLabelState = ForceLocalRoom;
        forceLocalLabel.SetText(ForceLocalRoom
            ? "<b>YEREL ODA: AÇIK</b>  (F8)\nLOBİ KUR → KCP. İkinci kopya <b>127.0.0.1</b> yazsın."
            : "<b>YEREL ODA: KAPALI</b>  (F8)\nEOS hazırsa internet odası açılıyor.");
    }

    /// <summary>
    /// Durumu EKRANDA yazan küçük etiket.
    ///
    /// Konsola yazmak yetmiyor: bu dosyanın kendi dersi (`ScreenEffects`'in
    /// F10 kademe etiketi) **oynarken konsola bakılmadığını** söylüyor —
    /// görüntüyü ya da davranışı değiştiren bir test tuşu, neye değiştirdiğini
    /// de ekranda söylemeli.
    ///
    /// **Kendi Canvas'ı ve menünün ÜSTÜNDE** (sıra 110; menü 100). F10
    /// etiketi menünün ALTINDA duruyor çünkü o oyun içi bir ayar; bu ise
    /// tam da ana menüde, `LOBİ KUR`'a basmadan önce okunmak zorunda.
    ///
    /// **Araç çalıştırmak gerekmiyor:** çalışma anında kuruluyor
    /// (`Terminal.GetOrCreateStateLight` ve `MonsterAura` ile aynı desen) —
    /// `Menü Kur`'a bağlanmış olsaydı test etmek için önce bir araç
    /// çalıştırmak gerekirdi.
    ///
    /// **İlk F8'e kadar hiç kurulmuyor:** gerçek bir oturumda köşede duran
    /// bir test yazısı istenmez.
    /// </summary>
    private void BuildForceLocalLabel()
    {
        GameObject root = new GameObject("YerelOdaEtiketi",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Sağ ALT köşe: sağ üstte mikrofon göstergesi, sol altta F10 kademe
        // etiketi, ortada menü sütunu var.
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(root.transform, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 24f);
        rect.sizeDelta = new Vector2(470f, 0f);

        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.02f, 0.02f, 0.03f, 0.82f);
        background.raycastTarget = false;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject labelObject = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(panel.transform, false);

        forceLocalLabel = labelObject.GetComponent<TextMeshProUGUI>();
        forceLocalLabel.fontSize = 17f;
        forceLocalLabel.color = new Color(1f, 0.82f, 0.35f, 1f);
        forceLocalLabel.alignment = TextAlignmentOptions.TopLeft;
        forceLocalLabel.raycastTarget = false;
        forceLocalLabel.richText = true;
    }
#endif

    // ---------- Menü butonları ----------

    /// <summary>Sunucuyu açıp kendi de oyuncu olarak katılır (Mirror'ın host modu).</summary>
    public void HostLobby()
    {
        RebindToLiveManager();

        NetworkManager manager = NetworkManager.singleton;
        if (manager == null)
        {
            StatusMessage = Localization.Get("NetworkManager yok. Yakalamaca > Ağ Kurulumu (1. adım).");
            Debug.LogError(StatusMessage);
            return;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            StatusMessage = Localization.Get("Zaten bir odadasın.");
            return;
        }

        leaving = false;

        // EOS girişi ASENKRON: Device ID üretiliyor, sonra Connect girişi
        // yapılıyor. Play'e basıp hemen LOBİ KUR diyen biri için henüz hazır
        // olmuyor ve oyun sessizce yerel odaya düşüyordu — her şey doğru
        // kurulmuşken bile. Hazır olana kadar bekliyoruz.
        // `ForceLocalRoom` da `UseRelay`'i false yapıyor, ama o durumda
        // BEKLEMEK anlamsız: EOS gerçekten hazır olsa bile kullanmayacağız,
        // yani oyuncu boşuna 12 saniye bekleme ekranında kalırdı.
        if (relayTransport != null && !ForceLocalRoom && !UseRelay)
        {
            StatusMessage = Localization.Get("EOS bağlanıyor…");

            if (menu != null)
                menu.ShowLobby();

            StartCoroutine(HostWhenRelayReady(manager));
            return;
        }

        StartHosting(manager);

        if (menu != null)
            menu.ShowLobby();
    }

    /// <summary>
    /// EOS bağlanana kadar bekleyip odayı öyle kuruyor.
    ///
    /// Süre sınırı var: EOS hiç bağlanamazsa (kimlik yanlış, P2P izni yok)
    /// `IsConnecting` sonsuza kadar açık kalabilir ve oyuncu bekleme ekranında
    /// asılı kalırdı. Süre dolunca yerel odaya düşülüyor ve sebebi yazılıyor.
    /// </summary>
    private IEnumerator HostWhenRelayReady(NetworkManager manager)
    {
        float deadline = Time.unscaledTime + relayWaitTimeout;

        // `IsConnecting`'e BAKILMIYOR, bilerek: giriş daha başlamadan önceki ilk
        // karelerde o da false oluyor ve o anı yakalayan bir kontrol EOS'a hiç
        // şans vermeden yerel odaya düşerdi. Ölçüt tek: hazır mı, değil mi.
        while (!UseRelay && Time.unscaledTime < deadline)
        {
            if (leaving)
                yield break;

            yield return null;
        }

        if (leaving)
            yield break;

        StartHosting(manager);
    }

    private void StartHosting(NetworkManager manager)
    {
        if (!UseRelay)
        {
            UseTransport(manager, localTransport);

            // Kod, internete çıkan adaptörün adresinden üretiliyor. Sanal ağda
            // (Radmin, Hamachi) gereken adres başka bir adaptörde olduğu için
            // kod yanlış çıkıyor; bütün adresleri yazmak oyuncunun doğrusunu
            // tanıyıp arkadaşına vermesini sağlıyor.
            RoomCode = LobbyCode.FromAddress(LobbyCode.LocalAddress());
            StatusMessage = LocalRoomMessage();

            manager.StartHost();
            return;
        }

        UseTransport(manager, relayTransport);

        // Lobi servisi bağlı değilse eski davranış: adres host'un ürün kimliği
        // (ProductUserId), 32 karakterlik bir metin. Kopyalanabiliyor ama
        // söylenemiyor — yine de oda açılıyor, ki EOS Kurulumu'nun yarım
        // kaldığı bir projede oyun oynanabilir kalsın.
        if (relayLobby == null)
        {
            RoomCode = EOSSDKComponent.LocalUserProductIdString;
            StatusMessage = Localization.Get("İnternet odası. Kodu kopyalayıp arkadaşına ver.");

            manager.StartHost();
            return;
        }

        // Sunucu, kısa kod hazır olduktan SONRA açılıyor. Önce açıp kodu
        // sonradan değiştirmek daha hızlı olurdu ama ekrandaki kod bir anda
        // 32 karakterden 6 karaktere dönerdi; oyuncu arkadaşına hangisini
        // vereceğini bilemez ve muhtemelen ilk gördüğünü verirdi.
        RoomCode = LobbyCode.Unknown;
        StatusMessage = Localization.Get("Oda kuruluyor…");

        relayLobby.HostRoom(
            PlayerProfile.Name,
            code =>
            {
                // Oda kurulurken AYRIL'a basılmış olabilir. EOS'taki kayıt yine
                // de oluştu: kapatmazsak kimsenin giremeyeceği bir oda listede
                // ve aramada asılı kalır.
                if (leaving)
                {
                    CloseRelayRoom();
                    return;
                }

                RoomCode = code;
                StatusMessage = Localization.Get("İnternet odası. Kodu arkadaşına söyle.");

                manager.StartHost();
            },
            error =>
            {
                if (leaving)
                    return;

                // Lobi servisi takıldı ama RELAY çalışıyor: oda yine kurulabilir,
                // yalnızca kod uzun olur. Kısa kodu alamadık diye oyuncuyu
                // odasız bırakmak, çalışan bir yolu çalışmayan bir yol yüzünden
                // kapatmak olurdu.
                Debug.LogWarning($"EOS lobi servisi: {error}");

                RoomCode = EOSSDKComponent.LocalUserProductIdString;
                StatusMessage = Localization.Get(
                    "Kısa kod alınamadı. Uzun kodla devam: kodu kopyalayıp arkadaşına ver.");

                manager.StartHost();
            });
    }

    /// <summary>Kod ya da ham IP ile bağlanır.</summary>
    public void JoinLobby(string codeOrAddress)
    {
        RebindToLiveManager();

        NetworkManager manager = NetworkManager.singleton;
        if (manager == null)
        {
            StatusMessage = Localization.Get("NetworkManager yok. Yakalamaca > Ağ Kurulumu (1. adım).");
            Debug.LogError(StatusMessage);
            return;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            StatusMessage = Localization.Get("Zaten bir odadasın.");
            return;
        }

        string trimmed = codeOrAddress != null ? codeOrAddress.Trim() : string.Empty;
        string address;

        // Dört giriş biçimi var ve ayırt etmek kolay:
        //   "192.168.1.42"  → nokta içeriyor, IP
        //   "K7M2QXB"       → 7 harf, IP'den üretilmiş yerel kod
        //   "K7M2QX"        → 6 harf, EOS oda kodu
        //   32 karakter hex → EOS ürün kimliği (kısa kod alınamadıysa yedek)
        // Kod alfabesinde nokta yok; iki kod biçimini de uzunluk ayırıyor
        // (bkz. LobbyCode.RoomCodeLength).
        if (LobbyCode.TryToAddress(trimmed, out address))
        {
            UseTransport(manager, localTransport);
            RoomCode = LobbyCode.FromAddress(address);
        }
        else if (UseRelay && relayLobby != null && LobbyCode.IsRoomCode(trimmed))
        {
            JoinByRoomCode(manager, trimmed);
            return;
        }
        else if (UseRelay && trimmed == EOSSDKComponent.LocalUserProductIdString)
        {
            StatusMessage = SelfConnectMessage;
            return;
        }
        else if (UseRelay && trimmed.Length > LobbyCode.Length)
        {
            UseTransport(manager, relayTransport);
            address = trimmed;
            RoomCode = trimmed;
        }
        else
        {
            StatusMessage = UseRelay
                ? Localization.Format("Kod okunamadı. {0} harflik oda kodu, {1} harflik yerel kod ya da IP adresi bekleniyor.",
                    LobbyCode.RoomCodeLength, LobbyCode.Length)
                : Localization.Format("Kod okunamadı. {0} harf ya da bir IP adresi bekleniyor. (EOS hazır değil, oda koduyla katılamazsın.)",
                    LobbyCode.Length);
            return;
        }

        leaving = false;
        StatusMessage = Localization.Get("Bağlanılıyor…");
        connectTimer = connectTimeout;

        manager.networkAddress = address;
        manager.StartClient();

        if (menu != null)
            menu.ShowLobby();
    }

    /// <summary>
    /// EOS kimliği CİHAZ başına üretiliyor (Device ID), oyuncu başına değil:
    /// aynı bilgisayardaki iki kopya aynı ProductUserId'yi alıyor ve biri
    /// öbürüne bağlanmaya çalıştığında kendine bağlanmış oluyor. EOS bunu
    /// reddediyor ve geriye yalnızca zaman aşımı kalıyordu — sebebi hiçbir
    /// yerde görünmüyordu.
    /// </summary>
    private static string SelfConnectMessage => Localization.Get(
        "Bu senin kendi odan. Aynı bilgisayardaki iki kopya aynı EOS kimliğini " +
        "paylaşıyor, yani kendine bağlanamazsın — test için ikinci bir makine " +
        "gerekiyor.");

    /// <summary>
    /// Kısa kodla katılır: önce EOS'a odayı sorup host'un adresini alıyor,
    /// sonra Mirror'ı bağlıyor.
    ///
    /// **Katılma ekranından çıkılmıyor.** Arama birkaç yüz milisaniye sürüyor
    /// ve sonucu belirsiz; lobiye geçip hata çıkınca geri dönmek ekranı iki kez
    /// zıplatırdı. Ekran ancak Mirror bağlanmaya başlayınca değişiyor.
    ///
    /// **Bağlanma sayacı da orada başlıyor**, aramanın başında değil: arama
    /// süresi bağlanma süresinden düşülseydi yavaş bir aramadan sonra bağlantı
    /// daha doğmadan zaman aşımına uğrardı.
    /// </summary>
    private void JoinByRoomCode(NetworkManager manager, string code)
    {
        leaving = false;
        RoomCode = code.ToUpperInvariant();
        StatusMessage = Localization.Get("Oda aranıyor…");

        relayLobby.JoinRoom(
            code,
            address =>
            {
                // Arama sürerken vazgeçilmiş olabilir; o ana kadar EOS odasına
                // çoktan girmiş oluyoruz, çıkmazsak kadroda hayalet bir üye
                // kalır.
                if (leaving)
                {
                    CloseRelayRoom();
                    return;
                }

                // Kendi odamızı bulduk. Kod doğru olduğu için buraya kadar
                // geliyor; sebebi söylemezsek geriye yalnızca zaman aşımı
                // kalırdı (bkz. SelfConnectMessage).
                if (address == EOSSDKComponent.LocalUserProductIdString)
                {
                    CloseRelayRoom();
                    RoomCode = LobbyCode.Unknown;
                    StatusMessage = SelfConnectMessage;
                    return;
                }

                StatusMessage = Localization.Get("Bağlanılıyor…");
                connectTimer = connectTimeout;

                UseTransport(manager, relayTransport);
                manager.networkAddress = address;
                manager.StartClient();

                if (menu != null)
                    menu.ShowLobby();
            },
            error =>
            {
                if (leaving)
                    return;

                RoomCode = LobbyCode.Unknown;
                StatusMessage = error;
            });
    }

    /// <summary>
    /// EOS kullanılabilir mi.
    ///
    /// Üç şart birden: transport bağlı, SDK açılmış ve kimlik alınmış. Üçü de
    /// çalışma anında belli oluyor — kimlik bilgileri yanlışsa ya da istemci
    /// politikasında P2P izni yoksa SDK açılmıyor ve `Initialized` false
    /// kalıyor.
    ///
    /// **Hazır değilse sessizce yerel odaya düşüyoruz.** Alternatifi, oyuncuya
    /// hiç açılmayan bir oda vermekti; EOS kurulumu tamamlanmamış bir projede
    /// oyunun büsbütün oynanamaz olması doğru değil.
    /// </summary>
    /// <summary>
    /// Yerel oda kurulduğunda ekrana yazılacak metin.
    ///
    /// İki ayrı sebep, iki ayrı cümle: EOS gerçekten hazır olmadığı için mi
    /// düşüldü, yoksa test için mi zorlandı. Tek bir metin kullanmak
    /// "EOS hazır değil" diye YALAN söylerdi ve oyuncu olmayan bir arıza
    /// ararken zaman kaybederdi.
    /// </summary>
    private static string LocalRoomMessage()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (ForceLocalRoom)
        {
            // `Localization`'a GİRMİYOR, bilerek: yalnızca editör/dev
            // build'de görünüyor, yani tabloya eklenen satır gönderilen
            // oyunda ÖLÜ kalırdı — ve hiçbir şey yapmayan bir satır sonraki
            // okuyana "demek ki bu çevriliyor" diye yanlış bilgi verir
            // (bölüm 13'ün `MUZ ADAM` dersi). F10 kademe etiketi de aynı
            // gerekçeyle tablonun dışında.
            return "YEREL ODA ZORLANDI (F8) — ikinci kopya 127.0.0.1 yazarak katılsın.";
        }
#endif

        return Localization.Format(
            "EOS hazır değil, yerel oda kuruldu. Katılacak kişi şu adreslerden birini yazmalı:\n{0}",
            LobbyCode.LocalAddresses());
    }

    private bool UseRelay =>
        !ForceLocalRoom
        && relayTransport != null
        && EOSSDKComponent.Initialized
        && !string.IsNullOrEmpty(EOSSDKComponent.LocalUserProductIdString);

    /// <summary>
    /// İstenen transport'u NetworkManager'a takar.
    ///
    /// Mirror'da aktif transport tek (`Transport.active`); ikisini sahnede
    /// tutup başlamadan önce seçmek serbest. Böylece EOS kurulu olsa bile
    /// aynı ağdaki bir odaya IP'yle girmek mümkün kalıyor.
    ///
    /// Ağ açıkken değiştirmek bağlantıyı koparırdı; çağıranların hepsi önce
    /// "zaten bir odadasın" kontrolünden geçiyor.
    /// </summary>
    private void UseTransport(NetworkManager manager, Transport wanted)
    {
        if (wanted == null)
            return;

        manager.transport = wanted;
        Transport.active = wanted;

        ApplySnapshotBuffer(manager);
    }

    /// <summary>
    /// Anlık görüntü tamponunu, oyuncunun `NetworkTransform`'unun GERÇEK
    /// gönderim aralığına göre büyütür.
    ///
    /// ### Neden gerekti — tampon bir gönderim aralığından KÜÇÜKTÜ (2026-09-23)
    ///
    /// Mirror'ın formülü:
    ///
    /// ```
    /// bufferTime = NetworkServer.sendInterval * bufferTimeMultiplier
    /// ```
    ///
    /// Sahnedeki değerler `sendRate = 60` ve `bufferTimeMultiplier = 2`,
    /// yani tampon **33 ms**. Oyuncunun `NetworkTransform`'u ise
    /// `syncInterval = 0.05`, yani konum **50 ms**'de bir geliyor.
    ///
    /// Tampon bir gönderim aralığından küçük olunca zaman çizgisi en yeni
    /// anlık görüntüye yetişiyor ve ara değerleyecek bir şey bulamıyor:
    /// uzak oyuncunun konumu DONUYOR, sonraki paket gelince sıçrıyor.
    /// Kusursuz bir LAN'de bile oluyor, internette jitter'le birlikte çok
    /// daha uzun sürüyor.
    ///
    /// **Mirror'ın dinamik ayarı bunu göremiyor.** `DynamicAdjustment`
    /// jitter'in standart sapmasını ölçüyor:
    ///
    /// ```
    /// multiples = (sendInterval + jitterStd) / sendInterval
    /// safezone  = multiples + tolerance
    /// ```
    ///
    /// Paketler düzenli aralıklarla geliyorsa (LAN) jitterStd ≈ 0 ve sonuç
    /// yine 2 çıkıyor. Ölçtüğü şey paketlerin DÜZENSİZLİĞİ, gönderim
    /// hızlarının uyuşmazlığı değil.
    ///
    /// ### Neyi düzeltiyor
    ///
    /// Donan konum üç şeyi birden bozuyordu ve üçü de "bazen gelmiyor"
    /// diye bildirildi:
    ///
    /// | Belirti | Neden |
    /// |---|---|
    /// | Canavarın ayak sesi | Hız pozisyon farkından çıkıyor; donan karede sıfır görünüyor (bkz. `FootstepAudio.ReportTooSlow`) |
    /// | Kalp atışı | `ScreenEffects.DreadAt` canavara olan MESAFEYİ ölçüyor; donmuş konum mesafeyi olduğundan uzak gösteriyor |
    /// | Ekran sarsıntısı | Aynı dehşet sayısından besleniyor |
    ///
    /// Dehşet 16 m'de başlıyor: kovalarken 10.67 m/s giden bir canavarın
    /// konumu 300 ms donarsa 3.2 m'lik bir hata demek, yani canavar
    /// gerçekte menzildeyken ekranda hâlâ dışarıda görünüyor.
    ///
    /// ### Sayı ÖLÇÜLÜYOR, yazılmıyor
    ///
    /// Gereken çarpan prefabın kendi `syncInterval`'inden hesaplanıyor.
    /// Sabit bir sayı yazmak, biri `syncInterval`'i değiştirdiğinde aynı
    /// hatayı sessizce geri getirirdi — bölüm 16'nın "iki sayı birbirine
    /// bağlıysa birini değiştirirken öbürünü de kontrol et" dersi.
    ///
    /// Yalnızca BÜYÜTÜYOR: elle daha yüksek bir değer verilmişse ona
    /// dokunulmuyor.
    /// </summary>
    private static void ApplySnapshotBuffer(NetworkManager manager)
    {
        if (manager == null || manager.sendRate <= 0)
            return;

        float sendInterval = 1f / manager.sendRate;
        float transformInterval = LongestTransformInterval(manager.playerPrefab);

        if (transformInterval <= 0f)
            return;

        // Tampon en az BİR tam gönderim aralığını örtmeli, üstüne bir
        // gönderim aralığı kadar da jitter payı. Yukarı yuvarlanıyor:
        // eksik bir tampon donma demek, fazlası yalnızca birkaç ms gecikme.
        int needed = Mathf.CeilToInt((transformInterval + sendInterval) / sendInterval);

        if (manager.snapshotSettings.bufferTimeMultiplier >= needed)
            return;

        Debug.Log($"Anlık görüntü tamponu büyütüldü: " +
            $"{manager.snapshotSettings.bufferTimeMultiplier} → {needed} " +
            $"({sendInterval * needed * 1000f:0} ms). Oyuncunun konumu " +
            $"{transformInterval * 1000f:0} ms'de bir geliyor; küçük tampon " +
            "uzak oyuncuları donduruyordu.");

        manager.snapshotSettings.bufferTimeMultiplier = needed;
    }

    /// <summary>
    /// Oyuncu prefabındaki `NetworkTransform`'ların en UZUN gönderim
    /// aralığı. En yavaş olan belirleyici: tampon onu örtmüyorsa o bileşen
    /// donuyor.
    /// </summary>
    private static float LongestTransformInterval(GameObject playerPrefab)
    {
        if (playerPrefab == null)
            return 0f;

        float longest = 0f;
        NetworkTransformBase[] transforms =
            playerPrefab.GetComponentsInChildren<NetworkTransformBase>(true);

        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].syncInterval > longest)
                longest = transforms[i].syncInterval;
        }

        return longest;
    }

    /// <summary>
    /// Transport ve lobi servisi alanlarını, o an YAŞAYAN ağ yöneticisinin
    /// bileşenlerine yeniden bağlar.
    ///
    /// **Neden gerekiyor: NASIL OYNANIR'dan dönünce menü ölü kopyaya
    /// bakıyordu.** `NetworkManager` DontDestroyOnLoad ile sahne değişiminden
    /// sağ çıkıyor. Tutorial'dan ana menüye dönülürken SampleScene baştan
    /// yükleniyor ve sahnedeki İKİNCİ NetworkManager'ı Mirror kendisi yok
    /// ediyor ("Multiple NetworkManagers detected") — o objenin üstündeki
    /// KcpTransport, EosTransport, EOSSDKComponent ve RelayLobby de onunla
    /// gidiyor. Bu bileşenin Inspector'dan bağlı alanları tam olarak o yok
    /// edilen kopyaları gösteriyordu: EOS hazır olduğu hâlde
    /// `relayTransport == null` çıkıyor, LOBİ KUR sessizce "EOS hazır değil,
    /// yerel oda kuruldu"ya (EOS öncesinin 7 harflik IP koduna) düşüyor ve
    /// katılma ekranındaki oda listesi kayboluyordu. Tutorial'a bir kez giren
    /// oyuncu o oturumda internetten oynayamıyordu.
    ///
    /// Yaşayan yönetici aynı bileşenleri taşıyor ve EOS girişi onda zaten
    /// yapılmış durumda. Yeniden BAĞLAMAK yetiyor; yöneticiyi silip EOS'u
    /// baştan başlatmak ise aynı süreçte SDK'yı ikinci kez açmak demekti
    /// (bkz. EOSSDKComponent.OnApplicationQuit — kapatma yalnızca çıkışta).
    ///
    /// Her karede çağrılıyor: yok edilme kare SONUNDA oluyor, yani sahnenin
    /// yüklendiği karede alan hâlâ sağlam görünüyor. Maliyeti üç null kontrolü.
    /// </summary>
    private void RebindToLiveManager()
    {
        relayTransport = FromLiveManager(relayTransport);
        relayLobby = FromLiveManager(relayLobby);
        localTransport = FromLiveManager(localTransport);
    }

    /// <summary>
    /// Referans yok edilmiş bir kopyayı gösteriyorsa yaşayan yöneticideki
    /// aynı türden bileşeni döndürür; sağlamsa ya da hiç atanmamışsa olduğu
    /// gibi bırakır. `JoinLobbyPanel` de aynı sebeple kullanıyor.
    /// </summary>
    public static T FromLiveManager<T>(T reference) where T : Component
    {
        // Unity'nin `==` işleci yok edilmiş objeyi null sayıyor, C#'ın
        // ReferenceEquals'ı saymıyor. İkisi ayrışıyorsa elimizde yok edilmiş
        // bir kopya var. Hiç atanmamış alan (gerçek null) olduğu gibi dönüyor:
        // EOS kurulmamış bir projede aranacak bir şey yok.
        if (reference != null || ReferenceEquals(reference, null))
            return reference;

        NetworkManager manager = NetworkManager.singleton;
        if (manager == null)
            return reference;

        // Tür, yok edilmiş kopyanın KENDİ türünden okunuyor (GetType yok
        // edilmiş objede de çalışır). Alan tipiyle aramak yanlış olurdu:
        // `localTransport` soyut `Transport` tipinde ve yöneticide iki
        // transport birden duruyor — ilk bulunan EOS olabilirdi.
        Component live = manager.GetComponent(reference.GetType());
        return live != null ? (T)live : reference;
    }

    /// <summary>Odadan ayrılır. Sunucuysak oda kapanıyor, herkes düşüyor.</summary>
    public void Leave()
    {
        RebindToLiveManager();

        NetworkManager manager = NetworkManager.singleton;
        leaving = true;
        connectTimer = 0f;

        if (manager != null)
        {
            if (NetworkServer.active)
                manager.StopHost();
            else if (NetworkClient.active)
                manager.StopClient();
        }

        CloseRelayRoom();

        RoomCode = LobbyCode.Unknown;
        StatusMessage = string.Empty;
        hadLocalPlayer = false;

        if (menu != null)
            menu.ShowMain();
    }

    /// <summary>
    /// EOS'taki oda kaydını kapatır.
    ///
    /// Oda, Mirror bağlantısından **ayrı** duruyor: bağlantı kapansa bile lobi
    /// kaydı serviste kalır, kod hâlâ bulunur ve arkadaşın artık var olmayan
    /// bir odaya bağlanmaya çalışırdı. Host'ta oda yok ediliyor, katılanda
    /// yalnızca çıkılıyor — ayrımı `RelayLobby` yapıyor.
    /// </summary>
    private void CloseRelayRoom()
    {
        if (relayLobby != null)
            relayLobby.CloseRoom();
    }

    /// <summary>Kodu panoya kopyalar — lobi ekranındaki düğme.</summary>
    public void CopyCode()
    {
        if (RoomCode == LobbyCode.Unknown)
            return;

        GUIUtility.systemCopyBuffer = RoomCode;
        StatusMessage = Localization.Get("Kod panoya kopyalandı.");
    }

    // ---------- Ağ olayları ----------

    private void HandleConnected()
    {
        connectTimer = 0f;
        StatusMessage = string.Empty;
    }

    private void HandleDisconnected()
    {
        connectTimer = 0f;

        // Kendi bastığımız "Ayrıl" da bu olayı tetikliyor; onu hata gibi
        // göstermek yanlış olurdu.
        if (leaving)
        {
            leaving = false;
            return;
        }

        // Atılma/yasaklanma normal bir koptan ÖNCE kontrol ediliyor: ikisi de
        // aynı Mirror olayından (OnDisconnectedEvent) geçiyor ve ayırt eden
        // tek şey RoundManager.ServerKick'in bağlantı kapanmadan HEMEN ÖNCE
        // yolladığı bildirim (bkz. RoundParticipant.TargetNotifyKicked).
        // Kontrol edilmezse kovulan oyuncu "oda sahibi çıkmış olabilir" gibi
        // YANLIŞ bir mesaj görürdü.
        if (RoundParticipant.WasKicked)
        {
            StatusMessage = Localization.Get(RoundParticipant.WasBanned
                ? "Oda sahibi seni bu oturum için yasakladı."
                : "Oda sahibi seni odadan çıkardı.");

            RoundParticipant.WasKicked = false;
            RoundParticipant.WasBanned = false;
        }
        else
        {
            StatusMessage = Localization.Get(hadLocalPlayer
                ? "Bağlantı koptu — oda sahibi çıkmış olabilir."
                : "Bağlanılamadı. Kodu kontrol et; sunucu aynı ağda mı?");
        }

        hadLocalPlayer = false;

        if (menu != null)
            menu.ShowMain();
    }

    /// <summary>
    /// Ulaşılamayan adreste Mirror'ın kendi zaman aşımını beklemek oyuncuyu
    /// donmuş bir ekranla baş başa bırakıyor. Burası daha erken vazgeçiyor.
    /// </summary>
    private void TickConnectTimeout()
    {
        if (connectTimer <= 0f)
            return;

        connectTimer -= Time.unscaledDeltaTime;
        if (connectTimer > 0f)
            return;

        if (NetworkClient.isConnected)
            return;

        StatusMessage = Localization.Get("Bağlanılamadı (zaman aşımı). Kodu kontrol et; oda hâlâ açık mı?");

        NetworkManager manager = NetworkManager.singleton;
        leaving = true;

        if (manager != null && NetworkClient.active)
            manager.StopClient();

        // Kısa kodla girildiyse EOS odasına çoktan katılmış olabiliriz; Mirror
        // bağlantısı kurulamadığına göre orada da işimiz kalmadı.
        CloseRelayRoom();

        if (menu != null)
            menu.ShowMain();
    }

    /// <summary>
    /// Tur başlayınca menüyü kapatır, bitince lobiye döndürür.
    ///
    /// Yalnızca **geçişte** müdahale ediyor: her karede zorlasaydı tur ortasında
    /// Esc'ye basan oyuncunun duraklatma ekranı anında kapanırdı.
    /// </summary>
    private void TickPhase()
    {
        if (RoundParticipant.Local != null)
            hadLocalPlayer = true;

        RoundManager manager = RoundManager.Instance;
        if (manager == null || !NetworkClient.isConnected)
            return;

        RoundPhase phase = manager.Phase;
        if (phase == lastPhase)
            return;

        lastPhase = phase;

        if (menu == null)
            return;

        // Fazın kendisine bakılıyor, geçişin nereden geldiğine değil. Tur
        // Playing → Ended → Waiting diye ilerliyor; "Playing'den Waiting'e
        // geçince lobiye dön" kuralı aradaki Ended yüzünden hiç tutmuyordu ve
        // tur bitince oyuncu boş sahnede kalıyordu.
        switch (phase)
        {
            case RoundPhase.Playing:
                menu.CloseMenu();
                break;

            case RoundPhase.Waiting:
                menu.ShowLobby();
                break;

            case RoundPhase.Ended:
                // Dokunmuyoruz: RoundHud sonucu birkaç saniye gösteriyor,
                // menüyü hemen açmak onu okunmadan kapatırdı.
                break;
        }
    }
}
