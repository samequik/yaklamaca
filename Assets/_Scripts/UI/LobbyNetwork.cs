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

    [Tooltip("Aynı ağ için doğrudan bağlantı. EOS hazır değilken ve IP ile " +
        "katılırken kullanılıyor: internet gerektirmiyor, anında açılıyor.")]
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

        if (UseRelay)
        {
            UseTransport(manager, relayTransport);

            // EOS'ta adres, host'un ürün kimliği (ProductUserId). 32 karakterlik
            // bir metin: sesli sohbette söylenemez ama kopyalanabilir.
            // Kısa koda geçmek EOS'un lobi servisini kullanmayı gerektiriyor,
            // o ayrı bir adım (bölüm 13).
            RoomCode = EOSSDKComponent.LocalUserProductIdString;
            StatusMessage = "İnternet odası. Kodu kopyalayıp arkadaşına ver.";
        }
        else
        {
            UseTransport(manager, localTransport);

            // Kod, internete çıkan adaptörün adresinden üretiliyor. Sanal ağda
            // (Radmin, Hamachi) gereken adres başka bir adaptörde olduğu için
            // kod yanlış çıkıyor; bütün adresleri yazmak oyuncunun doğrusunu
            // tanıyıp arkadaşına vermesini sağlıyor.
            RoomCode = LobbyCode.FromAddress(LobbyCode.LocalAddress());
            StatusMessage = "EOS hazır değil, yerel oda kuruldu. Katılacak kişi " +
                $"şu adreslerden birini yazmalı:\n{LobbyCode.LocalAddresses()}";
        }

        manager.StartHost();

        if (menu != null)
            menu.ShowLobby();
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

        // Üç giriş biçimi var ve ayırt etmek kolay:
        //   "192.168.1.42"  → nokta içeriyor, IP
        //   "K7M2QXB"       → 7 harf, yerel kod
        //   32 karakter hex → EOS ürün kimliği
        // Kod alfabesinde nokta yok, EOS kimliği de 7 karakterden uzun.
        if (LobbyCode.TryToAddress(trimmed, out address))
        {
            UseTransport(manager, localTransport);
            RoomCode = LobbyCode.FromAddress(address);
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
                ? $"Kod okunamadı. {LobbyCode.Length} harflik kod, IP adresi ya da " +
                  "arkadaşının verdiği uzun kod bekleniyor."
                : $"Kod okunamadı. {LobbyCode.Length} harf ya da bir IP adresi bekleniyor. " +
                  "(EOS hazır değil, uzun kodla katılamazsın.)";
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
