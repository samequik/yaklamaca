using Mirror;
using TMPro;
using UnityEngine;

/// <summary>
/// Ekranın üstündeki tur bilgisi: terminal sayacı, durum satırı ve canavara
/// giden terminal alarmı.
///
/// `RoundHud.OnGUI`'nin yerini alıyor (teknik borç 2). Metin kurma mantığı
/// birebir taşındı; değişen tek şey nereye çizildiği.
///
/// ### Metin her karede kurulmuyor
///
/// Eski sürüm bunu OnGUI'nin her karede string üretmemesi için yapıyordu.
/// Canvas'ta gerekçe değişti ama kural aynı kaldı: TextMeshPro'ya yeni metin
/// vermek **mesh'i yeniden ördürüyor**, yani değişmeyen bir yazıyı her karede
/// yazmak bedava değil. Gösterilen değerler değişmedikçe dokunulmuyor.
///
/// ### Yerel oyuncu her karede aranmıyor ama sabit de tutulmuyor
///
/// Katılımcı ve izleyici oyuncuyla birlikte ağdan doğuyor, odadan çıkınca yok
/// oluyor. Bulunanı saklayıp yalnızca kaybolunca yeniden aramak ikisini birden
/// çözüyor.
/// </summary>
public class RoundHudView : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text terminalLabel;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_Text alarmLabel;

    [Tooltip("Terminal alarmının ekranda kalma süresi (saniye).")]
    [SerializeField] private float alarmDisplayTime = 6f;

    [SerializeField] private Color alarmColor = new Color(1f, 0.4f, 0.2f, 1f);

    private RoundParticipant cachedLocal;
    private SpectatorController cachedSpectator;

    private int lastProgressKey = -1;
    private int lastAlive = -1;
    private RoundPhase lastPhase = RoundPhase.Waiting;
    private string lastTarget;

    private RoundParticipant Local
    {
        get
        {
            if (cachedLocal == null && NetworkClient.localPlayer != null)
                cachedLocal = NetworkClient.localPlayer.GetComponent<RoundParticipant>();

            return cachedLocal;
        }
    }

    private SpectatorController Spectator
    {
        get
        {
            if (cachedSpectator == null && NetworkClient.localPlayer != null)
                cachedSpectator = NetworkClient.localPlayer.GetComponent<SpectatorController>();

            return cachedSpectator;
        }
    }

    private void Update()
    {
        RoundManager manager = RoundManager.Instance;
        bool active = GameHud.Visible && manager != null;

        if (root != null && root.activeSelf != active)
            root.SetActive(active);

        if (!active)
            return;

        UpdateTerminalLine(manager);
        UpdateStatusLine(manager);
        UpdateAlarmLine();
    }

    /// <summary>
    /// Terminal sayacı. **İki sayaç birden izleniyor:** gereken sayı tur
    /// başında ve her ölümde değişiyor, yalnızca tamamlananı izlemek o
    /// değişimleri kaçırıyordu.
    /// </summary>
    private void UpdateTerminalLine(RoundManager manager)
    {
        bool playing = manager.Phase == RoundPhase.Playing;

        if (terminalLabel == null)
            return;

        if (terminalLabel.gameObject.activeSelf != playing)
            terminalLabel.gameObject.SetActive(playing);

        if (!playing)
            return;

        int key = manager.CompletedTerminals * 100 + manager.RequiredTerminals;
        if (key == lastProgressKey)
            return;

        lastProgressKey = key;

        terminalLabel.SetText(manager.ExitOpen
            ? "ÇIKIŞ AÇIK"
            : $"TERMİNAL  {manager.CompletedTerminals} / {manager.RequiredTerminals}");
    }

    private void UpdateStatusLine(RoundManager manager)
    {
        if (statusLabel == null)
            return;

        // İzlenen kişi de metnin parçası; sol tıkla değişince yenilenmeli.
        string target = Spectator != null ? Spectator.CurrentTargetName : null;

        if (manager.AliveRunnerCount == lastAlive
            && manager.Phase == lastPhase
            && target == lastTarget)
            return;

        lastAlive = manager.AliveRunnerCount;
        lastPhase = manager.Phase;
        lastTarget = target;

        statusLabel.SetText(BuildStatusText(manager));
    }

    private string BuildStatusText(RoundManager manager)
    {
        switch (manager.Phase)
        {
            case RoundPhase.Playing:
                // Elenmişsek kimi izlediğimizi yazıyoruz; rol yazısı artık işe
                // yaramaz.
                if (Spectator != null && Spectator.IsSpectating)
                {
                    string watching = string.IsNullOrEmpty(Spectator.CurrentTargetName)
                        ? "..."
                        : Spectator.CurrentTargetName;

                    return $"ELENDİN   —   İzlenen: {watching}   [Sol tık] değiştir" +
                        $"   —   Kalan kaçan: {manager.AliveRunnerCount}";
                }

                string role = Local == null ? string.Empty
                    : Local.Role == RoundRole.Monster ? "CANAVARSIN"
                    : Local.IsAlive ? "KAÇIYORSUN" : "ELENDİN";

                return $"{role}   —   Kalan kaçan: {manager.AliveRunnerCount}";

            case RoundPhase.Ended:
                switch (manager.Result)
                {
                    case RoundResult.MonsterWins: return "CANAVAR KAZANDI";
                    case RoundResult.RunnersWin: return "KAÇANLAR KURTULDU";
                    default: return "CANAVAR OYUNDAN AYRILDI — tur iptal";
                }

            default:
                return "LOBİ   —   oyuncular bekleniyor";
        }
    }

    /// <summary>
    /// Terminal alarmı — yalnızca canavarın ekranında beliriyor, çünkü uyarı
    /// `TargetRpc` ile sadece ona gönderiliyor. Kaçanın yanlış tuşa basması
    /// canavara "şu terminalde biri var" bilgisini veriyor; devriye rotasını
    /// belirleyen asıl ipucu bu.
    ///
    /// Mesafe her karede hesaplanıyor ve yazı da her karede kuruluyor —
    /// diğerlerinden farklı olarak burada değer sürekli değişiyor (canavar
    /// yürüdükçe), yani karşılaştırıp atlamanın kazancı yok.
    /// </summary>
    private void UpdateAlarmLine()
    {
        if (alarmLabel == null)
            return;

        bool active = !string.IsNullOrEmpty(Terminal.AlarmMessage)
            && Time.time - Terminal.AlarmTime <= alarmDisplayTime;

        if (alarmLabel.gameObject.activeSelf != active)
            alarmLabel.gameObject.SetActive(active);

        if (!active)
            return;

        float distance = NetworkClient.localPlayer != null
            ? Vector3.Distance(NetworkClient.localPlayer.transform.position, Terminal.AlarmPosition)
            : 0f;

        alarmLabel.color = alarmColor;
        alarmLabel.SetText($"{Terminal.AlarmMessage}   —   {distance:0} m");
    }
}
