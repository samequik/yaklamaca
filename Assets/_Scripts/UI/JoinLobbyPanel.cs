using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Kodla odaya katılma ekranı ve **açık odaların listesi**.
///
/// Kod alanı üç biçimi birden alıyor: 6 harflik EOS oda kodu, 7 harflik yerel
/// kod ve ham IP. Hangisi olduğunu çözmek ve bağlanmak bu ekranın işi değil —
/// metni olduğu gibi <see cref="LobbyNetwork"/>'e veriyor (bkz.
/// `LobbyNetwork.JoinLobby`).
///
/// Ham IP'nin kabul edilmesi bilinçli: yerel test odasına girmenin tek yolu o
/// ve sanal ağ (Radmin) üzerinden oynayan birini kod üretmeye zorlamak
/// gereksiz bir engel olurdu.
///
/// ### Liste kodun yerini almıyor, yanında duruyor
///
/// EOS'un lobi araması bu ürünün bütün açık odalarını döndürüyor, yani listede
/// tanımadığın odalar da görünebilir. Kod hâlâ "arkadaşımın odası hangisi"
/// sorusunun kesin cevabı; liste ise kodu yazmadan girmenin kısa yolu ve
/// odanın hâlâ açık olduğunu görmenin yolu.
///
/// **Listeden seçmek de koddan geçiyor.** Satıra basınca kod alanına yazılıyor
/// ve normal katılma yolu işletiliyor — ayrı bir "listeden katıl" yolu ikinci
/// bir hata kaynağı olurdu ve oyuncu ne olduğunu göremezdi.
/// </summary>
public class JoinLobbyPanel : MonoBehaviour
{
    /// <summary>Listedeki tek oda satırı.</summary>
    [System.Serializable]
    public class RoomRow
    {
        public Button button;
        public TMP_Text nameLabel;
        public TMP_Text codeLabel;
    }

    [SerializeField] private LobbyNetwork network;
    [SerializeField] private TMP_InputField codeField;
    [SerializeField] private TMP_Text statusLabel;

    [Header("Oda listesi")]
    [Tooltip("EOS'un lobi servisi. Boşsa liste hiç görünmüyor ve ekran " +
        "yalnızca kodla çalışıyor. EOS Kurulumu bağlıyor.")]
    [SerializeField] private RelayLobby relayLobby;

    [SerializeField] private Button refreshButton;
    [SerializeField] private TMP_Text refreshLabel;
    [SerializeField] private TMP_Text listHeaderLabel;
    [SerializeField] private RoomRow[] rows;

    private readonly List<RelayLobby.RoomInfo> rooms = new List<RelayLobby.RoomInfo>();

    /// <summary>Listeye ait durum metni — ağ katmanının söyleyeceği yoksa gösteriliyor.</summary>
    private string listStatus = string.Empty;

    private bool listing;
    private bool listedOnce;

    private void OnEnable()
    {
        if (codeField != null)
        {
            codeField.text = string.Empty;
            codeField.Select();
            codeField.ActivateInputField();
        }

        rooms.Clear();
        listStatus = string.Empty;
        listedOnce = false;

        ApplyRows();
        RefreshStatus();
    }

    private void Update()
    {
        RefreshStatus();

        // Ekrana girer girmez bir kez aranıyor: listeyi görmek için ayrıca bir
        // düğmeye basmak gerekmiyor.
        //
        // Ama arama `OnEnable`'da YAPILAMIYOR: EOS girişi asenkron ve oyuncu
        // Play'e basıp hemen buraya gelebiliyor. O anda `CanList` false olur,
        // bölüm gizlenir ve EOS sonradan açılsa bile bir daha geri gelmezdi —
        // ekrandan çıkıp girmeden. Hazır olduğu İLK karede aranıyor.
        if (listedOnce || !CanList)
            return;

        listedOnce = true;
        RefreshRooms();
    }

    /// <summary>"KATIL" düğmesi ve giriş alanında Enter.</summary>
    public void Join()
    {
        if (network == null || codeField == null)
            return;

        network.JoinLobby(codeField.text);
    }

    // ---------- Liste ----------

    /// <summary>Liste kullanılabilir mi: bileşen bağlı ve EOS açılmış.</summary>
    private bool CanList => relayLobby != null && RelayLobby.ServiceReady;

    /// <summary>"ODALARI YENİLE" düğmesi.</summary>
    public void RefreshRooms()
    {
        if (!CanList || listing)
            return;

        listing = true;
        listStatus = "Odalar aranıyor…";
        ApplyRows();

        relayLobby.ListRooms(
            found =>
            {
                listing = false;
                rooms.Clear();

                if (found != null)
                    rooms.AddRange(found);

                int visible = rows != null ? rows.Length : 0;

                if (rooms.Count == 0)
                {
                    listStatus = "Açık oda yok. Arkadaşının kodunu bekliyorsan onu yaz.";
                }
                else if (rooms.Count > visible)
                {
                    // Servis listeden fazlasını döndürebiliyor. Sessizce kesmek,
                    // arkadaşının odasını görmeyen oyuncuya "oda kapanmış"
                    // dedirtirdi — sayıyı söylemek kodu yazdırıyor.
                    listStatus = $"{rooms.Count} oda bulundu, ilk {visible} tanesi " +
                        "gösteriliyor. Aradığın yoksa kodu yaz.";
                }
                else
                {
                    listStatus = string.Empty;
                }

                ApplyRows();
            },
            error =>
            {
                listing = false;
                rooms.Clear();

                // Liste alınamaması katılmayı engellemiyor: kod yolu EOS'un
                // lobi aramasından bağımsız çalışıyor. Mesaj bu yüzden bir hata
                // değil, bir bilgi.
                listStatus = $"Oda listesi alınamadı ({error}). Kodla katılabilirsin.";
                ApplyRows();
            });
    }

    /// <summary>
    /// Listedeki bir satıra basıldı. İndeksi kalıcı dinleyici taşıyor
    /// (`MenuSetup.CreateRoomRow`), yani hangi satır olduğu sahne dosyasından
    /// geliyor ve çalışma anında bağlanacak bir şey kalmıyor.
    /// </summary>
    public void JoinRoomAt(int index)
    {
        if (index < 0 || index >= rooms.Count)
            return;

        string code = rooms[index].Code;

        // Kod alana da yazılıyor: oyuncu neye bastığını görüyor ve bağlantı
        // koparsa aynı kodu tekrar denemek için elinde kalıyor.
        if (codeField != null)
            codeField.text = code;

        if (network != null)
            network.JoinLobby(code);
    }

    private void ApplyRows()
    {
        if (listHeaderLabel != null)
            listHeaderLabel.gameObject.SetActive(CanList);

        if (refreshButton != null)
        {
            refreshButton.gameObject.SetActive(CanList);
            refreshButton.interactable = CanList && !listing;
        }

        if (refreshLabel != null)
            refreshLabel.SetText(listing ? "ARANIYOR…" : "ODALARI YENİLE");

        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            RoomRow row = rows[i];
            if (row == null || row.button == null)
                continue;

            bool used = CanList && i < rooms.Count;
            row.button.gameObject.SetActive(used);

            if (!used)
                continue;

            RelayLobby.RoomInfo info = rooms[i];

            if (row.nameLabel != null)
            {
                // Adsız oda, adını kaydetmemiş bir oyuncunun odası. Boş bırakmak
                // satırı okunmaz yapardı; kodun kendisi her zaman var.
                row.nameLabel.SetText(string.IsNullOrWhiteSpace(info.Name)
                    ? "İsimsiz oda"
                    : info.Name);
            }

            if (row.codeLabel != null)
                row.codeLabel.SetText($"{info.Code}   {info.Players}/{LobbyRoster.MaxPlayers}");
        }
    }

    private void RefreshStatus()
    {
        if (statusLabel == null)
            return;

        // Ağ katmanının söyleyeceği bir şey varsa (bağlanma hatası, "oda
        // aranıyor") o öncelikli: oyuncunun beklediği geri bildirim odur.
        // Liste mesajı ikinci sırada, ipucu en sonda.
        string message = network != null && !string.IsNullOrEmpty(network.StatusMessage)
            ? network.StatusMessage
            : listStatus;

        statusLabel.SetText(string.IsNullOrEmpty(message)
            ? "Arkadaşının verdiği kodu ya da IP adresini gir."
            : message);
    }
}
