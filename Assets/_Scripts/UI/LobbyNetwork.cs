using Edgegap;
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

    [Tooltip("İnternet üzerinden oynatan relay transport'u (Edgegap). Boşsa " +
        "yalnızca yerel oda kurulabiliyor. Ağ Kurulumu bağlıyor.")]
    [SerializeField] private EdgegapLobbyKcpTransport relayTransport;

    [Tooltip("Aynı ağ için doğrudan bağlantı transport'u. Tek başına test " +
        "ederken kullanılıyor: internet gerektirmiyor ve anında açılıyor.")]
    [SerializeField] private Transport localTransport;

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
        TickRelayStatus();
        TickConnectTimeout();
        TickPhase();
    }

    /// <summary>
    /// Relay'in kurulum aşamasını ekrana yazar.
    ///
    /// Doğrudan bağlantıda "oda kur" anlıktı; relay'de değil. Sırayla lobi
    /// oluşturuluyor, başlatılıyor, bir relay atanması bekleniyor, sonra
    /// bağlanılıyor — hepsi birkaç saniye. Ekranda hiçbir şey yazmasa oyuncu
    /// oyunun donduğunu sanardı.
    ///
    /// **Asıl sebep hata durumu.** Host'ta bağlanma zaman aşımı yok
    /// (`connectTimer` yalnızca katılmada kuruluyor), yani lobi oluşturma
    /// başarısız olursa oyuncu sonsuza kadar "Oda kuruluyor…" görürdü. Burası
    /// hatayı yakalayıp ana menüye döndürüyor.
    ///
    /// Yalnızca relay etkinken çalışıyor: yerel odada transport farklı ve bu
    /// aşamaların hiçbiri yok.
    /// </summary>
    private void TickRelayStatus()
    {
        if (relayTransport == null || leaving)
            return;

        if (Transport.active != (Transport)relayTransport)
            return;

        if (!NetworkServer.active && !NetworkClient.active)
            return;

        switch (relayTransport.Status)
        {
            case EdgegapLobbyKcpTransport.TransportStatus.CreatingLobby:
                StatusMessage = "Oda oluşturuluyor…";
                break;

            case EdgegapLobbyKcpTransport.TransportStatus.StartingLobby:
                StatusMessage = "Oda başlatılıyor…";
                break;

            case EdgegapLobbyKcpTransport.TransportStatus.WaitingRelay:
                StatusMessage = "Sunucu atanıyor…";
                break;

            case EdgegapLobbyKcpTransport.TransportStatus.Error:
                FailRelay();
                break;
        }
    }

    /// <summary>
    /// Relay kurulamadı: oyuncuyu asılı bırakmadan ana menüye döndürüyor.
    ///
    /// En sık sebep `lobbyUrl`'ün boş olması — proje ilk kez açıldığında
    /// Edgegap servisi henüz kurulmamış oluyor. Mesaj bunu açıkça söylüyor,
    /// yoksa "bir şeyler ters gitti" deyip oyuncuyu arama yapmaya bırakırdı.
    /// </summary>
    private void FailRelay()
    {
        NetworkManager manager = NetworkManager.singleton;
        leaving = true;
        connectTimer = 0f;

        StatusMessage = string.IsNullOrWhiteSpace(relayTransport.lobbyUrl)
            ? "Relay kurulu değil: NetworkManager > EdgegapLobbyKcpTransport içindeki " +
              "lobbyUrl boş. Şimdilik YEREL ODA kullanabilirsin."
            : "Odaya bağlanılamadı. İnternet bağlantını kontrol et.";

        Debug.LogError(StatusMessage);

        if (manager != null)
        {
            if (NetworkServer.active)
                manager.StopHost();
            else if (NetworkClient.active)
                manager.StopClient();
        }

        RoomCode = LobbyCode.Unknown;

        if (menu != null)
            menu.ShowMain();
    }

    // ---------- Menü butonları ----------

    /// <summary>
    /// Odayı relay üzerinden açar: internetteki herkes kodu yazıp girebilir.
    ///
    /// Akış şu: rastgele bir kod üretiliyor, oda o **adla** açılıyor, Edgegap
    /// bir relay atıyor ve host oraya bağlanıyor. Katılan kişi kodu yazınca
    /// lobi listesinde o ad aranıyor (bkz. JoinByCode).
    ///
    /// **Kararları yine bu bilgisayar veriyor.** Relay yalnızca paketleri
    /// taşıyor; rol dağıtımı, isabet, terminal sayaçları hepsi burada
    /// (bölüm 4). Değişen tek şey baytların hangi yoldan gittiği.
    /// </summary>
    public void HostLobby() => StartHosting(relay: true);

    /// <summary>
    /// Aynı ağ için doğrudan oda açar, internet gerektirmiyor.
    ///
    /// Tek başına test ederken lazım: relay ile oda kurmak Edgegap servisine
    /// gidip gelmek demek, birkaç saniye sürüyor ve internet istiyor. Günde
    /// onlarca kez Play'e basan biri için bu gerçek bir sürtünme; bu yol
    /// bugünkü anındalığı koruyor.
    ///
    /// Kod yine üretiliyor ama işe yaramıyor: doğrudan bağlantıda karşı taraf
    /// IP ile giriyor. Yine de bir şey göstermek, ekranın boş kalmasından iyi.
    /// </summary>
    public void HostLocalLobby() => StartHosting(relay: false);

    private void StartHosting(bool relay)
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

        if (!TryUseTransport(manager, relay))
            return;

        leaving = false;
        RoomCode = LobbyCode.Generate();
        StatusMessage = relay ? "Oda kuruluyor…" : string.Empty;

        // Oda adı = kod. Katılan kişi listeden bu adı arıyor.
        if (relay)
            relayTransport.SetServerLobbyParams(RoomCode, manager.maxConnections);

        manager.StartHost();

        if (menu != null)
            menu.ShowLobby();
    }

    /// <summary>
    /// Kodla ya da ham IP ile bağlanır.
    ///
    /// Ham IP hâlâ kabul ediliyor: yerel test odasına girmenin tek yolu o, ve
    /// sanal ağ (Radmin) kullanan biri de bundan faydalanıyor. Kod ise relay
    /// üzerinden aranıyor.
    /// </summary>
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

        // Nokta içeriyorsa IP kabul ediliyor: kod alfabesinde nokta yok, yani
        // ikisi birbirine karışamıyor.
        if (trimmed.Contains("."))
        {
            JoinDirect(manager, trimmed);
            return;
        }

        if (!LobbyCode.TryNormalize(trimmed, out string code))
        {
            StatusMessage = $"Kod okunamadı. {LobbyCode.Length} harf ya da bir IP adresi bekleniyor.";
            return;
        }

        JoinByCode(manager, code);
    }

    /// <summary>Yerel ya da sanal ağda doğrudan adrese bağlanır.</summary>
    private void JoinDirect(NetworkManager manager, string address)
    {
        if (!TryUseTransport(manager, relay: false))
            return;

        leaving = false;
        RoomCode = LobbyCode.Unknown;
        StatusMessage = "Bağlanılıyor…";
        connectTimer = connectTimeout;

        manager.networkAddress = address;
        manager.StartClient();

        if (menu != null)
            menu.ShowLobby();
    }

    /// <summary>
    /// Kodu lobi listesinde arayıp bulduğu odaya bağlanır.
    ///
    /// **Liste çekmek asenkron**, o yüzden bağlanma iki adımda oluyor: önce
    /// odayı bul, sonra bağlan. Arada oyuncu vazgeçip ayrılabilir; `leaving`
    /// bayrağı o durumu yakalıyor, yoksa iptal edilmiş bir katılma birkaç
    /// saniye sonra kendiliğinden bağlanırdı.
    /// </summary>
    private void JoinByCode(NetworkManager manager, string code)
    {
        if (relayTransport == null)
        {
            StatusMessage = "Relay kurulu değil. Kodla katılmak için Edgegap gerekiyor; " +
                "aynı ağdaysanız ham IP yazabilirsiniz.";
            return;
        }

        if (!TryUseTransport(manager, relay: true))
            return;

        leaving = false;
        RoomCode = code;
        StatusMessage = "Oda aranıyor…";
        connectTimer = connectTimeout;

        if (menu != null)
            menu.ShowLobby();

        relayTransport.Api.RefreshLobbies(
            lobbies =>
            {
                if (leaving)
                    return;

                foreach (LobbyBrief lobby in lobbies)
                {
                    if (lobby.name != code)
                        continue;

                    if (!lobby.is_joinable)
                    {
                        StatusMessage = "Oda dolu ya da tur başlamış.";
                        connectTimer = 0f;
                        return;
                    }

                    StatusMessage = "Bağlanılıyor…";
                    connectTimer = connectTimeout;

                    manager.networkAddress = lobby.lobby_id;
                    manager.StartClient();
                    return;
                }

                StatusMessage = "Böyle bir oda yok. Kodu kontrol et.";
                connectTimer = 0f;
            },
            error =>
            {
                if (leaving)
                    return;

                StatusMessage = $"Oda listesi alınamadı: {error}";
                connectTimer = 0f;
            });
    }

    /// <summary>
    /// İstenen transport'u NetworkManager'a takar.
    ///
    /// Mirror'da aktif transport tek: `Transport.active`. İkisini birden tutup
    /// başlamadan önce seçmek, yerel testi relay'e bağımlı olmaktan kurtarıyor.
    /// Ağ açıkken değiştirmek bağlantıyı koparırdı, o yüzden yalnızca kapalıyken
    /// çağrılıyor; çağıranların hepsi önce bunu kontrol ediyor.
    /// </summary>
    private bool TryUseTransport(NetworkManager manager, bool relay)
    {
        Transport wanted = relay ? (Transport)relayTransport : localTransport;

        if (wanted == null)
        {
            StatusMessage = relay
                ? "Relay transport'u bağlı değil. Yakalamaca > Ağ Kurulumu (1. adım)."
                : "Yerel transport bağlı değil. Yakalamaca > Ağ Kurulumu (1. adım).";

            Debug.LogError(StatusMessage);
            return false;
        }

        manager.transport = wanted;
        Transport.active = wanted;
        return true;
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

        RoomCode = LobbyCode.Unknown;
        StatusMessage = string.Empty;
        hadLocalPlayer = false;

        if (menu != null)
            menu.ShowMain();
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

        StatusMessage = "Bağlanılamadı (zaman aşımı). Kodu kontrol et; sunucu aynı ağda mı?";

        NetworkManager manager = NetworkManager.singleton;
        leaving = true;

        if (manager != null && NetworkClient.active)
            manager.StopClient();

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
