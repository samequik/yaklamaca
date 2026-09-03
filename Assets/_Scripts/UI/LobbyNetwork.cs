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
        RoomCode = LobbyCode.FromAddress(LobbyCode.LocalAddress());
        StatusMessage = string.Empty;

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

        if (!LobbyCode.TryToAddress(codeOrAddress, out string address))
        {
            StatusMessage = $"Kod okunamadı. {LobbyCode.Length} harf ya da bir IP adresi bekleniyor.";
            return;
        }

        leaving = false;
        RoomCode = LobbyCode.FromAddress(address);
        StatusMessage = "Bağlanılıyor…";
        connectTimer = connectTimeout;

        manager.networkAddress = address;
        manager.StartClient();

        if (menu != null)
            menu.ShowLobby();
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
