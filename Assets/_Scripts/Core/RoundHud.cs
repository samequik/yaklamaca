using Mirror;
using UnityEngine;

/// <summary>
/// Tur durumunu gösteren geçici arayüz: süre, kalan kaçan sayısı, rolün, sonuç.
///
/// Prototip. Gerçek UI'a (TextMeshPro + Canvas) geçilince silinecek —
/// PlayerInteractor.OnGUI ile birlikte, bkz. CLAUDE.md teknik borç 2.
/// Yine de metin her karede değil, sadece gösterilen saniye değişince
/// kuruluyor; OnGUI her karede string üretmesin diye.
/// </summary>
public class RoundHud : MonoBehaviour
{
    [SerializeField] private RoundManager manager;

    // Yerel oyuncu artık sahnede sabit değil, ağdan doğuyor. Her karede
    // aramamak için bulunca saklıyoruz.
    private RoundParticipant cachedLocal;

    private RoundParticipant localParticipant
    {
        get
        {
            if (cachedLocal != null)
                return cachedLocal;

            if (NetworkClient.localPlayer != null)
                cachedLocal = NetworkClient.localPlayer.GetComponent<RoundParticipant>();

            return cachedLocal;
        }
    }

    // İzleyici de yerel oyuncunun üstünde; katılımcıyla aynı yoldan bulunuyor.
    private SpectatorController cachedSpectator;

    private SpectatorController spectator
    {
        get
        {
            if (cachedSpectator != null)
                return cachedSpectator;

            if (NetworkClient.localPlayer != null)
                cachedSpectator = NetworkClient.localPlayer.GetComponent<SpectatorController>();

            return cachedSpectator;
        }
    }

    private GUIStyle style;
    private string timerText = "";
    private string statusText = "";
    private int lastShownProgress = -1;
    private int lastShownAlive = -1;
    private RoundPhase lastShownPhase = RoundPhase.Waiting;
    private string lastShownTarget;

    private void Update()
    {
        if (manager == null)
            return;

        // Süre sayacı yok artık; üstteki satır terminal ilerlemesini gösteriyor.
        // İki sayacı birden izliyoruz: gereken sayı tur başında ve her ölümde
        // değişiyor, yalnızca tamamlananı izlemek o değişimleri kaçırıyordu.
        int progressKey = manager.CompletedTerminals * 100 + manager.RequiredTerminals;
        if (progressKey != lastShownProgress)
        {
            lastShownProgress = progressKey;
            timerText = manager.ExitOpen
                ? "ÇIKIŞ AÇIK"
                : $"TERMİNAL  {manager.CompletedTerminals} / {manager.RequiredTerminals}";
        }

        // İzlenen kişi de metnin parçası; Space ile değişince yenilenmeli.
        string spectatorTarget = spectator != null ? spectator.CurrentTargetName : null;

        if (manager.AliveRunnerCount != lastShownAlive
            || manager.Phase != lastShownPhase
            || spectatorTarget != lastShownTarget)
        {
            lastShownAlive = manager.AliveRunnerCount;
            lastShownPhase = manager.Phase;
            lastShownTarget = spectatorTarget;
            statusText = BuildStatusText();
        }
    }

    private string BuildStatusText()
    {
        switch (manager.Phase)
        {
            case RoundPhase.Playing:
                // Elenmişsek kimi izlediğimizi yazıyoruz; rol yazısı artık işe yaramaz.
                if (spectator != null && spectator.IsSpectating)
                {
                    string watching = string.IsNullOrEmpty(spectator.CurrentTargetName)
                        ? "..."
                        : spectator.CurrentTargetName;
                    return $"ELENDİN   —   İzlenen: {watching}   [Sol tık] değiştir" +
                        $"   —   Kalan kaçan: {manager.AliveRunnerCount}";
                }

                string role = localParticipant == null ? "" :
                    localParticipant.Role == RoundRole.Monster ? "CANAVARSIN" :
                    localParticipant.IsAlive ? "KAÇIYORSUN" : "ELENDİN";
                return $"{role}   —   Kalan kaçan: {manager.AliveRunnerCount}";

            case RoundPhase.Ended:
                switch (manager.Result)
                {
                    case RoundResult.MonsterWins: return "CANAVAR KAZANDI";
                    case RoundResult.RunnersWin: return "KAÇANLAR KURTULDU";
                    default: return "CANAVAR OYUNDAN AYRILDI — tur iptal";
                }

            default:
                // Rol seçimi artık burada değil: canavarı sunucu belirliyor,
                // turu da sunucu başlatıyor (lobi arayüzü 4. adımda bağlanacak).
                return "LOBİ   —   oyuncular bekleniyor";
        }
    }

    private void OnGUI()
    {
        if (manager == null)
            return;

        style ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 20
        };

        float width = 460f;
        float x = (Screen.width - width) / 2f;

        if (manager.Phase == RoundPhase.Playing)
            GUI.Label(new Rect(x, 12f, width, 30f), timerText, style);

        GUI.Label(new Rect(x, 44f, width, 30f), statusText, style);

        DrawTerminalAlarm(x, width);
    }

    /// <summary>
    /// Terminal alarmı — yalnızca canavarın ekranında beliriyor, çünkü uyarı
    /// TargetRpc ile sadece ona gönderiliyor. Kaçanın yanlış tuşa basması
    /// canavara "şu terminalde biri var" bilgisini veriyor; devriye rotasını
    /// belirleyen asıl ipucu bu.
    /// </summary>
    private void DrawTerminalAlarm(float x, float width)
    {
        const float duration = 6f;

        if (string.IsNullOrEmpty(Terminal.AlarmMessage) || Time.time - Terminal.AlarmTime > duration)
            return;

        float distance = NetworkClient.localPlayer != null
            ? Vector3.Distance(NetworkClient.localPlayer.transform.position, Terminal.AlarmPosition)
            : 0f;

        GUIStyle alarmStyle = new GUIStyle(style)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
        alarmStyle.normal.textColor = new Color(1f, 0.4f, 0.2f);

        GUI.Label(new Rect(x, 80f, width, 30f),
            $"{Terminal.AlarmMessage}   —   {distance:0} m", alarmStyle);
    }
}
