using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lobi odası: kadro, hazır işareti, canavar seçimi, oda kodu ve başlatma.
///
/// **Hiçbir şeye karar vermiyor.** Ekrandaki her değer sunucudan gelen
/// SyncVar'ların yansıması; her düğme oyuncunun kendi `RoundParticipant`'ı
/// üzerinden bir Command yolluyor ve yetkiyi sunucu doğruluyor. Bu ekranın
/// kodu değiştirilse en fazla yanlış düğme görünür — tur yine başlamaz,
/// canavar yine değişmez (bkz. RoundManager.ServerRequestStart).
///
/// Yoklamayla tazeleniyor: SyncVar'ların toplu bir "değişti" olayı yok ve
/// beş kişilik bir kadroyu saniyede birkaç kez taramanın maliyeti ölçülemez.
/// </summary>
public class LobbyPanel : MonoBehaviour
{
    /// <summary>Kadrodaki tek satırın görsel parçaları.</summary>
    [System.Serializable]
    public class SlotView
    {
        public Image background;
        public TMP_Text nameLabel;
        public TMP_Text statusLabel;
    }

    [Header("Bağlantılar")]
    [SerializeField] private LobbyNetwork network;
    [SerializeField] private LobbyRoster roster;

    [Header("Kadro")]
    [Tooltip("5 satır: 1 canavar + 4 kaçan.")]
    [SerializeField] private SlotView[] slots;

    [SerializeField] private TMP_Text rosterCountLabel;

    [Header("Oda")]
    [SerializeField] private TMP_Text codeLabel;
    [SerializeField] private TMP_Text statusLabel;

    [Header("Düğmeler")]
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyLabel;
    [SerializeField] private Button monsterButton;
    [SerializeField] private TMP_Text monsterLabel;
    [SerializeField] private Button startButton;
    [SerializeField] private TMP_Text startLabel;

    [Header("Renkler")]
    [SerializeField] private Color emptySlotColor = new Color(0.10f, 0.10f, 0.13f, 1f);
    [SerializeField] private Color filledSlotColor = new Color(0.16f, 0.16f, 0.21f, 1f);
    [SerializeField] private Color localSlotColor = new Color(0.24f, 0.20f, 0.16f, 1f);
    [SerializeField] private Color monsterTextColor = new Color(0.90f, 0.35f, 0.28f, 1f);
    [SerializeField] private Color readyTextColor = new Color(0.45f, 0.80f, 0.45f, 1f);
    [SerializeField] private Color waitingTextColor = new Color(0.75f, 0.72f, 0.55f, 1f);
    [SerializeField] private Color emptyTextColor = new Color(0.42f, 0.42f, 0.48f, 1f);

    [Tooltip("Basılabilir düğmenin yazı rengi.")]
    [SerializeField] private Color activeLabelColor = new Color(0.92f, 0.92f, 0.95f, 1f);

    [Tooltip("Basılamaz düğmenin yazı rengi — düğme kapalıyken yazı da solmalı.")]
    [SerializeField] private Color disabledLabelColor = new Color(0.55f, 0.55f, 0.60f, 0.6f);

    [Header("Tazeleme")]
    [Tooltip("Saniyede kaç kez kadro taranacak. Beş kişilik listede yüksek " +
        "olmasının bir maliyeti yok ama 5 Hz göz için zaten anlık.")]
    [SerializeField] private float refreshRate = 5f;

    private const string EmptySlotText = "— boş —";

    private float refreshTimer;

    private void Awake()
    {
        if (roster != null)
            roster.Changed += RefreshRoster;
    }

    private void OnDestroy()
    {
        if (roster != null)
            roster.Changed -= RefreshRoster;
    }

    private void OnEnable()
    {
        refreshTimer = 0f;
        Refresh();
    }

    private void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f)
            return;

        refreshTimer = 1f / Mathf.Max(0.5f, refreshRate);
        Refresh();
    }

    private void Refresh()
    {
        if (roster != null)
            roster.SyncFromNetwork();

        RefreshCode();
        RefreshButtons();
        RefreshStatus();
    }

    // ---------- Düğmeler ----------

    /// <summary>Hazır işaretini ters çevirir.</summary>
    public void ToggleReady()
    {
        RoundParticipant local = RoundParticipant.Local;
        if (local == null)
            return;

        local.SetReady(!local.IsReady);
        Refresh();
    }

    /// <summary>
    /// Canavar seçimini sıradakine çevirir: Rastgele → 1. oyuncu → 2. oyuncu →
    /// … → Rastgele.
    ///
    /// Açılır liste yerine döngü düğmesi: en fazla beş aday var ve döngü
    /// düğmesi hem kodla kurulan bir menüde daha az parça hem de imleçle tek
    /// tıklama.
    /// </summary>
    public void CycleMonsterChoice()
    {
        RoundParticipant local = RoundParticipant.Local;
        if (local == null || !local.IsRoomOwner || roster == null)
            return;

        var members = roster.Members;
        uint current = RoundManager.Instance != null ? RoundManager.Instance.MonsterChoice : 0u;

        // Bot da listede: tek başına test ederken kaçan olarak oynamanın yolu,
        // canavarlığı kovalamayan birine vermek. Rastgele seçimde bot hâlâ
        // aday değil (bkz. RoundManager.PickMonster) — buradaki açık bir tercih.
        int currentIndex = -1;
        var candidates = new System.Collections.Generic.List<uint>();

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].NetId == current)
                currentIndex = candidates.Count;

            candidates.Add(members[i].NetId);
        }

        // -1 (rastgele) ile adaylar arasında dönüyoruz.
        int next = currentIndex + 1;
        uint chosen = next >= candidates.Count ? 0u : candidates[next];

        local.RequestMonsterChoice(chosen);
        Refresh();
    }

    /// <summary>Turu başlatmayı ister. Şartları sunucu yeniden kontrol ediyor.</summary>
    public void StartRound()
    {
        RoundParticipant local = RoundParticipant.Local;
        if (local != null)
            local.RequestStartRound();
    }

    /// <summary>Odadan ayrıl.</summary>
    public void Leave()
    {
        if (network != null)
            network.Leave();
    }

    /// <summary>Oda kodunu panoya kopyalar.</summary>
    public void CopyCode()
    {
        if (network != null)
            network.CopyCode();

        Refresh();
    }

    // ---------- Görüntü ----------

    private void RefreshRoster()
    {
        if (slots == null || roster == null)
            return;

        var members = roster.Members;

        for (int i = 0; i < slots.Length; i++)
        {
            if (i < members.Count)
                ApplySlot(i, members[i]);
            else
                ApplyEmptySlot(i);
        }

        if (rosterCountLabel != null)
            rosterCountLabel.SetText("Oyuncular: {0}/{1}", members.Count, LobbyRoster.MaxPlayers);
    }

    private void ApplySlot(int index, LobbyRoster.Member member)
    {
        SlotView slot = slots[index];
        if (slot == null)
            return;

        if (slot.background != null)
            slot.background.color = member.IsLocal ? localSlotColor : filledSlotColor;

        if (slot.nameLabel != null)
        {
            // Etiketler yazıyla, simgeyle değil: varsayılan TMP fontu
            // (LiberationSans) yalnızca temel Latin kapsıyor. "★" gibi bir
            // karakter fontta bulunamayıp boş kutuya dönüşüyordu.
            string name = member.DisplayName;

            if (member.IsLocal && member.IsRoomOwner)
                name = $"{name}  (sen, oda sahibi)";
            else if (member.IsLocal)
                name = $"{name}  (sen)";
            else if (member.IsRoomOwner)
                name = $"{name}  (oda sahibi)";

            slot.nameLabel.SetText(name);
            slot.nameLabel.color = Color.white;
        }

        if (slot.statusLabel == null)
            return;

        // Canavar seçimi hazır durumunun önüne geçiyor: odadaki herkesin
        // bilmesi gereken ilk şey kimin kovalayacağı.
        if (member.IsChosenMonster)
        {
            slot.statusLabel.SetText("CANAVAR");
            slot.statusLabel.color = monsterTextColor;
        }
        else if (member.IsReady)
        {
            slot.statusLabel.SetText("HAZIR");
            slot.statusLabel.color = readyTextColor;
        }
        else
        {
            slot.statusLabel.SetText("bekliyor");
            slot.statusLabel.color = waitingTextColor;
        }
    }

    private void ApplyEmptySlot(int index)
    {
        SlotView slot = slots[index];
        if (slot == null)
            return;

        if (slot.background != null)
            slot.background.color = emptySlotColor;

        if (slot.nameLabel != null)
        {
            slot.nameLabel.SetText(EmptySlotText);
            slot.nameLabel.color = emptyTextColor;
        }

        if (slot.statusLabel != null)
        {
            slot.statusLabel.SetText(string.Empty);
            slot.statusLabel.color = emptyTextColor;
        }
    }

    private void RefreshButtons()
    {
        RoundParticipant local = RoundParticipant.Local;
        bool owner = local != null && local.IsRoomOwner;

        SetButtonEnabled(readyButton, readyLabel, local != null);

        if (readyLabel != null)
            readyLabel.SetText(local != null && local.IsReady ? "HAZIR DEĞİLİM" : "HAZIRIM");

        // Canavar seçimi ve başlatma yalnızca oda sahibinde. Düğmeler
        // gizlenmiyor, griye alınıyor: gizlemek diğer oyuncuya "böyle bir şey
        // yok" dedirtirdi, gri düğme "bu bende değil" diyor.
        SetButtonEnabled(monsterButton, monsterLabel, owner);

        if (monsterLabel != null)
            monsterLabel.SetText($"Canavar: {DescribeMonsterChoice()}");

        bool canStart = RoundManager.CanStartFromClient(out _);
        SetButtonEnabled(startButton, startLabel, owner && canStart);

        if (startLabel != null)
            startLabel.SetText("BAŞLAT");
    }

    /// <summary>
    /// Düğmeyi açar/kapatır ve **yazısını da soldurur.**
    ///
    /// Yalnızca `interactable` tıklamayı engelliyor; Unity'nin renk geçişi
    /// arka plan görselini soldursa bile yazı parlak kalıyor ve düğme
    /// basılabilir görünüyordu.
    /// </summary>
    private void SetButtonEnabled(Button button, TMP_Text label, bool enabled)
    {
        if (button != null)
            button.interactable = enabled;

        if (label != null)
            label.color = enabled ? activeLabelColor : disabledLabelColor;
    }

    private string DescribeMonsterChoice()
    {
        uint choice = RoundManager.Instance != null ? RoundManager.Instance.MonsterChoice : 0u;
        if (choice == 0 || roster == null)
            return "Rastgele";

        var members = roster.Members;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].NetId == choice)
                return members[i].DisplayName;
        }

        return "Rastgele";
    }

    private void RefreshStatus()
    {
        if (statusLabel == null)
            return;

        // Ağ katmanının söyleyeceği bir şey varsa (bağlanma hatası, kod
        // kopyalandı) o öncelikli — oyuncunun beklediği geri bildirim odur.
        if (network != null && !string.IsNullOrEmpty(network.StatusMessage))
        {
            statusLabel.SetText(network.StatusMessage);
            return;
        }

        RoundParticipant local = RoundParticipant.Local;

        if (local == null)
        {
            statusLabel.SetText("Odaya giriliyor…");
            return;
        }

        string state = RoundManager.CanStartFromClient(out string reason)
            ? (local.IsRoomOwner ? "Herkes hazır — başlatabilirsin." : "Herkes hazır. Oda sahibi başlatacak.")
            : reason;

        // Tur bitip lobiye dönüldüğünde sonucu burada görüyorsun. `result`
        // bir sonraki StartRound'a kadar duruyor, ayrıca saklamaya gerek yok.
        string last = DescribeLastResult();
        statusLabel.SetText(string.IsNullOrEmpty(last) ? state : $"{last}  ·  {state}");
    }

    private static string DescribeLastResult()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null)
            return string.Empty;

        switch (manager.Result)
        {
            case RoundResult.MonsterWins: return "Geçen tur: canavar kazandı";
            case RoundResult.RunnersWin: return "Geçen tur: kaçanlar kazandı";
            case RoundResult.Aborted: return "Geçen tur iptal oldu (canavar ayrıldı)";
            default: return string.Empty;
        }
    }

    private void RefreshCode()
    {
        if (codeLabel == null)
            return;

        codeLabel.SetText(network != null ? network.RoomCode : LobbyCode.Unknown);
    }
}
