using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TAB paneli: kadro, gecikme (ping) ve **kişi bazlı ses ayarı.**
///
/// ### Neden basılı tutma değil, aç/kapa
///
/// Susturma düğmesine ve ses kaydırıcısına tıklamak imleç gerektiriyor; turda
/// imleç kilitli. Basılı tutulan bir panelde bunlara ulaşmanın yolu yok. Bu
/// yüzden tuş **aç/kapa** çalışıyor ve panel açıkken imleç serbest bırakılıp
/// bakış kesiliyor — duraklatma menüsünün kullandığı mekanizmanın aynısı
/// (`MenuController.SetOverlayOpen`).
///
/// Bedeli açık: panel açıkken oynayamıyorsun. Kovalanırken ses ayarı yapmak
/// zaten yapılacak iş değil; ayar anı ölüyken, lobide ya da tur başında.
///
/// ### Ses ayarı neden ağa gitmiyor
///
/// Susturma ve kişisel seviye **senin kulağının** tercihi: karşıdakinin
/// mikrofonuna dokunmuyor, yalnızca senin makinendeki `AudioSource`'unun
/// `volume`'unu değiştiriyor (`VoicePlayback`). Ağa taşımak, kimin kimi
/// susturduğunu herkese söylemek olurdu — gereksiz ve kırıcı.
///
/// ### Ping
///
/// Her istemci kendi `NetworkTime.rtt`'sini sunucuya bildiriyor, sunucu
/// SyncVar'a yazıyor (`RoundParticipant.PingMs`). Sunucunun her bağlantı için
/// RTT'yi kendi ölçmesi de mümkündü ama Mirror bunu her sürümde aynı yerde
/// vermiyor; bildirmek hem taşınabilir hem tek satır.
/// </summary>
public class ScoreboardPanel : MonoBehaviour
{
    /// <summary>Tek oyuncu satırının parçaları.</summary>
    [System.Serializable]
    public class Row
    {
        public GameObject root;
        public Image background;
        public TMP_Text nameLabel;
        public TMP_Text pingLabel;
        public Button muteButton;
        public TMP_Text muteLabel;
        public Slider volumeSlider;

        /// <summary>Denetim gösterilemediğinde SEBEBİ yazan satır.</summary>
        public TMP_Text noteLabel;
    }

    [SerializeField] private CanvasGroup group;
    [SerializeField] private Row[] rows;
    [SerializeField] private TMP_Text hintLabel;

    [Header("Renkler")]
    [SerializeField] private Color localColor = new Color(0.24f, 0.20f, 0.16f, 1f);
    [SerializeField] private Color otherColor = new Color(0.12f, 0.12f, 0.16f, 1f);
    [SerializeField] private Color speakingColor = new Color(0.85f, 0.25f, 0.2f, 1f);
    [SerializeField] private Color normalTextColor = new Color(0.92f, 0.92f, 0.95f, 1f);
    [SerializeField] private Color mutedTextColor = new Color(0.55f, 0.55f, 0.60f, 1f);

    private readonly List<RoundParticipant> shown = new List<RoundParticipant>();

    private bool open;

    private void Awake()
    {
        // Kaydırıcıların dinleyicisi indeksle bağlanıyor: satır sayısı sabit ve
        // hangi satırın kime ait olduğu her tazelemede değişiyor, o yüzden
        // hedef çalışma anında `shown` listesinden bulunuyor.
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            int index = i;

            if (rows[i]?.volumeSlider != null)
                rows[i].volumeSlider.onValueChanged.AddListener(value => SetVolume(index, value));
        }

        Apply(false);
    }

    private void Update()
    {
        // Menü açılırsa panel kapanıyor: ikisi birden imleci ve bakışı
        // yönetmeye kalkarsa hangisinin kapattığı belirsizleşir.
        if (open && MenuController.Instance != null && MenuController.Instance.IsOpen)
        {
            Apply(false);
            return;
        }

        if (KeyBindings.Pressed(GameAction.Scoreboard))
            Apply(!open);

        if (!open)
            return;

        // Durum her karede yeniden bildiriliyor. `SetOverlayOpen` değişmemişse
        // hemen çıkıyor, yani bedeli yok — ama panel açılırken `MenuController`
        // henüz uyanmamışsa (Awake sırası garanti değil) tek seferlik bildirim
        // kaybolur ve imleç serbest bırakılmazdı.
        if (MenuController.Instance != null)
            MenuController.Instance.SetOverlayOpen(true);

        Refresh();
    }

    private void OnDisable() => Apply(false);

    private void Apply(bool value)
    {
        open = value;

        GameHud.SetVisible(group, value);

        if (value)
            Refresh();

        if (MenuController.Instance != null)
            MenuController.Instance.SetOverlayOpen(value);
    }

    /// <summary>Satırı hangi oyuncuya ait olduğunu bilerek dolduruyor.</summary>
    private void Refresh()
    {
        shown.Clear();

        IReadOnlyList<RoundParticipant> all = RoundParticipant.All;

        for (int i = 0; i < all.Count && i < (rows?.Length ?? 0); i++)
        {
            if (all[i] != null)
                shown.Add(all[i]);
        }

        if (hintLabel != null)
        {
            // Metin araya girişle kuruluyor: TMP'nin SetText(string, ...)
            // aşırı yüklemeleri yalnızca SAYI alıyor, string almıyor.
            string key = KeyBindings.Describe(KeyBindings.Get(GameAction.Scoreboard));
            hintLabel.SetText($"{key} ile kapat  ·  yürümeye devam edebilirsin  ·  " +
                "ses ayarı yalnızca seni etkiler");
        }

        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            if (i < shown.Count)
                ApplyRow(rows[i], shown[i]);
            else
                ApplyEmpty(rows[i]);
        }
    }

    private void ApplyRow(Row row, RoundParticipant participant)
    {
        if (row?.root == null)
            return;

        row.root.SetActive(true);

        bool local = participant == RoundParticipant.Local;
        VoicePlayback playback = FindPlayback(participant);
        bool speaking = playback != null && playback.IsSpeaking;

        if (row.background != null)
            row.background.color = local ? localColor : otherColor;

        if (row.nameLabel != null)
        {
            row.nameLabel.SetText(local
                ? $"{participant.DisplayName}  (sen)"
                : participant.DisplayName);

            // Konuşan satır renkleniyor: kimin sesi geldiğini görmek, sesi
            // kısacak kişiyi bulmanın en hızlı yolu.
            row.nameLabel.color = speaking ? speakingColor : normalTextColor;
        }

        if (row.pingLabel != null)
        {
            int ping = participant.PingMs;
            row.pingLabel.SetText(ping > 0 ? $"{ping} ms" : "—");
        }

        // Kendi sesini kısmanın anlamı yok — kendine göndermiyoruz — ve botun
        // sesi hiç yok. İkisinde de denetimler **gizleniyor, griye
        // alınmıyor**: griye alınmış bir kaydırıcı görünüşte çalışıyor ve
        // oynayan "bozuk" diye okuyor. Yoksa, olmadığı belli.
        bool controllable = !local && playback != null;

        if (row.muteButton != null && row.muteButton.gameObject.activeSelf != controllable)
            row.muteButton.gameObject.SetActive(controllable);

        if (row.volumeSlider != null && row.volumeSlider.gameObject.activeSelf != controllable)
            row.volumeSlider.gameObject.SetActive(controllable);

        // Denetim yoksa SEBEBİ yazılıyor. Boş bir alan "bozuk" diye okunuyor;
        // tek başına test edildiğinde tam olarak bu yaşandı — ekranda yalnızca
        // kendi satırın ve botunki oluyor, ikisinde de ayarlanacak bir şey yok.
        if (row.noteLabel != null)
        {
            bool showNote = !controllable;

            if (row.noteLabel.gameObject.activeSelf != showNote)
                row.noteLabel.gameObject.SetActive(showNote);

            if (showNote)
            {
                row.noteLabel.color = mutedTextColor;
                row.noteLabel.SetText(local ? "kendi sesini duymuyorsun" : "sesi yok");
            }
        }

        if (!controllable)
            return;

        // Kaydırıcı her karede tazeleniyor: değer başka bir yerden de
        // değişebilir (genel konuşma sesi) ve gidiş-dönüş birebir olduğu için
        // sürüklerken çakışmıyor — 0-1 ile 0-2 arasındaki dönüşüm ikinin
        // kuvvetiyle çarpma, yani kayıpsız.
        if (row.volumeSlider != null)
            row.volumeSlider.SetValueWithoutNotify(playback.PersonalVolume * 0.5f);

        if (row.muteLabel == null)
            return;

        row.muteLabel.SetText(playback.Muted ? "AÇ" : "SUSTUR");
        row.muteLabel.color = normalTextColor;
    }

    private static void ApplyEmpty(Row row)
    {
        if (row?.root != null)
            row.root.SetActive(false);
    }

    /// <summary>Satır düğmesi — indeks kalıcı dinleyiciden geliyor.</summary>
    public void ToggleMute(int index)
    {
        VoicePlayback playback = PlaybackAt(index);

        if (playback != null)
            playback.SetMuted(!playback.Muted);

        Refresh();
    }

    private void SetVolume(int index, float value)
    {
        // Kaydırıcı 0-1, kişisel seviye 0-2: bire ortada denk geliyor, yani
        // oyuncu hem kısabiliyor hem yükseltebiliyor.
        PlaybackAt(index)?.SetPersonalVolume(value * 2f);
    }

    private VoicePlayback PlaybackAt(int index)
    {
        if (index < 0 || index >= shown.Count)
            return null;

        RoundParticipant participant = shown[index];

        return participant == RoundParticipant.Local ? null : FindPlayback(participant);
    }

    private static VoicePlayback FindPlayback(RoundParticipant participant) =>
        participant != null ? participant.GetComponent<VoicePlayback>() : null;
}
