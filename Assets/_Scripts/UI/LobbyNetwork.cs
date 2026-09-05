using System.Collections;
using EpicTransport;
using Mirror;
using UnityEngine;

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
        TickConnectTimeout();
        TickPhase();
    }

    // ---------- Menü butonları ----------

    /// <summary>Sunucuyu açıp kendi de oyuncu olarak katılır (Mirror'ın host modu).</summary>
    public void HostLobby()
    {
        NetworkManager manager = NetworkManager.singleton;
        if (manager == null)
        {
            StatusMessage = "NetworkManager yok. Yakalamaca > Ağ Kurulumu (1. adım).";
            Debug.LogError(StatusMessage);
            return;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            StatusMessage = "Zaten bir odadasın.";
            return;
        }

        leaving = false;

        // EOS girişi ASENKRON: Device ID üretiliyor, sonra Connect girişi
        // yapılıyor. Play'e basıp hemen LOBİ KUR diyen biri için henüz hazır
        // olmuyor ve oyun sessizce yerel odaya düşüyordu — her şey doğru
        // kurulmuşken bile. Hazır olana kadar bekliyoruz.
        if (relayTransport != null && !UseRelay)
        {
            StatusMessage = "EOS bağlanıyor…";

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
            StatusMessage = "EOS hazır değil, yerel oda kuruldu. Katılacak kişi " +
                $"şu adreslerden birini yazmalı:\n{LobbyCode.LocalAddresses()}";

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
            StatusMessage = "İnternet odası. Kodu kopyalayıp arkadaşına ver.";

            manager.StartHost();
            return;
        }

        // Sunucu, kısa kod hazır olduktan SONRA açılıyor. Önce açıp kodu
        // sonradan değiştirmek daha hızlı olurdu ama ekrandaki kod bir anda
        // 32 karakterden 6 karaktere dönerdi; oyuncu arkadaşına hangisini
        // vereceğini bilemez ve muhtemelen ilk gördüğünü verirdi.
        RoomCode = LobbyCode.Unknown;
        StatusMessage = "Oda kuruluyor…";

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
                StatusMessage = "İnternet odası. Kodu arkadaşına söyle.";

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
                StatusMessage = "Kısa kod alınamadı. Uzun kodla devam: " +
                    "kodu kopyalayıp arkadaşına ver.";

                manager.StartHost();
            });
    }

    /// <summary>Kod ya da ham IP ile bağlanır.</summary>
    public void JoinLobby(string codeOrAddress)
    {
        NetworkManager manager = NetworkManager.singleton;
        if (manager == null)
        {
            StatusMessage = "NetworkManager yok. Yakalamaca > Ağ Kurulumu (1. adım).";
            Debug.LogError(StatusMessage);
            return;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            StatusMessage = "Zaten bir odadasın.";
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
                ? $"Kod okunamadı. {LobbyCode.RoomCodeLength} harflik oda kodu, " +
                  $"{LobbyCode.Length} harflik yerel kod ya da IP adresi bekleniyor."
                : $"Kod okunamadı. {LobbyCode.Length} harf ya da bir IP adresi bekleniyor. " +
                  "(EOS hazır değil, oda koduyla katılamazsın.)";
            return;
        }

        leaving = false;
        StatusMessage = "Bağlanılıyor…";
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
    private const string SelfConnectMessage =
        "Bu senin kendi odan. Aynı bilgisayardaki iki kopya aynı EOS kimliğini " +
        "paylaşıyor, yani kendine bağlanamazsın — test için ikinci bir makine " +
        "gerekiyor.";

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
        StatusMessage = "Oda aranıyor…";

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

                StatusMessage = "Bağlanılıyor…";
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
    private bool UseRelay =>
        relayTransport != null
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
    }

    /// <summary>Odadan ayrılır. Sunucuysak oda kapanıyor, herkes düşüyor.</summary>
    public void Leave()
    {
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
        StatusMessage = "Kod panoya kopyalandı.";
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

        StatusMessage = hadLocalPlayer
            ? "Bağlantı koptu — oda sahibi çıkmış olabilir."
            : "Bağlanılamadı. Kodu kontrol et; sunucu aynı ağda mı?";

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

        StatusMessage = "Bağlanılamadı (zaman aşımı). Kodu kontrol et; oda hâlâ açık mı?";

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
