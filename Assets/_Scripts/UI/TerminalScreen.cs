using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Terminal başındayken ekranın ortasında beliren yeşil fosfor panel.
///
/// `Terminal.OnGUI`'nin yerini alıyor (teknik borç 2). Görüntü aynı, çizen
/// katman değişti.
///
/// ### Tek panel, beş terminal
///
/// Eskiden her terminal kendi ekranını çiziyordu ve hangisinin çizeceğini
/// `IsUsedByLocalPlayer` belirliyordu. Canvas'ta beş ayrı panel kurmanın
/// anlamı yok: hareket kilitli olduğu için aynı anda yalnızca bir terminale
/// bağlanılabiliyor. Panel `Terminal.ActiveLocal`'e bakıp o an hangisi
/// bağlıysa onu gösteriyor.
///
/// ### Karar terminalde, çizim burada
///
/// Hangi metnin görüneceğini `Terminal.BuildScreenState()` hesaplıyor; burası
/// yalnızca yazıyı, rengi ve çubuk oranını uyguluyor. Kural terminalin kendi
/// durumundan çıktığı için arayüz değişse de tek yerde kalıyor (bölüm 5).
///
/// ### Panel tam ekran değil, bilerek
///
/// Terminal başındaki oyuncunun tek savunması etrafını duyup görebilmek;
/// ekranı kaplayan bir arayüz mekaniğin bedelini haksız hâle getirirdi
/// (bölüm 11.2).
/// </summary>
public class TerminalScreen : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;

    [Header("Çerçeve")]
    [Tooltip("Vurgu rengini alan kenarlıklar. Gövde koyu kalıyor: durumu " +
        "anlatan şey çerçevenin ve yazının rengi.")]
    [SerializeField] private Graphic[] borders;

    [Header("Yazılar")]
    [SerializeField] private TMP_Text headerLabel;
    [SerializeField] private TMP_Text exitLabel;
    [SerializeField] private TMP_Text bigLabel;
    [SerializeField] private TMP_Text captionLabel;
    [SerializeField] private TMP_Text promptLabel;

    [Header("Çubuklar")]
    [SerializeField] private GameObject bar;
    [SerializeField] private RectTransform barFill;
    [SerializeField] private Graphic barFillGraphic;
    [SerializeField] private Graphic barBackGraphic;

    [SerializeField] private GameObject promptBar;
    [SerializeField] private RectTransform promptBarFill;
    [SerializeField] private Graphic promptBarFillGraphic;
    [SerializeField] private Graphic promptBarBackGraphic;

    private void Update()
    {
        Terminal terminal = Terminal.ActiveLocal;
        bool active = GameHud.Visible && terminal != null;

        GameHud.SetVisible(group, active);

        if (!active)
            return;

        Apply(terminal.BuildScreenState());
    }

    private void Apply(Terminal.ScreenState state)
    {
        Color accent = state.Accent;

        if (borders != null)
        {
            for (int i = 0; i < borders.Length; i++)
            {
                if (borders[i] != null)
                    borders[i].color = Fade(accent, 0.55f);
            }
        }

        SetLine(headerLabel, state.Header, accent);

        if (exitLabel != null)
        {
            // Tuş her karede okunuyor: oyuncu atamayı değiştirirse ekrandaki
            // yazı da değişmeli.
            string key = KeyBindings.Describe(KeyBindings.Get(GameAction.Interact));
            exitLabel.SetText($"[{key}] bırak");
            exitLabel.color = Fade(accent, 0.55f);
        }

        if (bigLabel != null)
        {
            bigLabel.fontSize = state.BigSize;
            SetLine(bigLabel, state.Big, accent);
        }

        SetLine(captionLabel, state.Caption, Fade(accent, 0.6f));

        ApplyBar(bar, barFill, barFillGraphic, barBackGraphic,
            state.ShowBar, state.Bar, accent);

        SetLine(promptLabel, state.ShowPrompt ? state.Prompt : null, state.PromptTint);

        ApplyBar(promptBar, promptBarFill, promptBarFillGraphic, promptBarBackGraphic,
            state.ShowPrompt, state.PromptBar, state.PromptTint);
    }

    /// <summary>
    /// Metni yazar; boşsa yazıyı komple gizler.
    ///
    /// Boş metin bırakmak yerine objeyi kapatmak, altındaki yerleşimin de
    /// kaymasını sağlıyor — dolum ekranında sınav yokken alt satır yukarı
    /// çıkıyor.
    /// </summary>
    private static void SetLine(TMP_Text label, string text, Color color)
    {
        if (label == null)
            return;

        bool visible = !string.IsNullOrEmpty(text);

        if (label.gameObject.activeSelf != visible)
            label.gameObject.SetActive(visible);

        if (!visible)
            return;

        label.color = color;
        label.SetText(text);
    }

    private static void ApplyBar(GameObject barRoot, RectTransform fill, Graphic fillGraphic,
        Graphic backGraphic, bool visible, float value, Color accent)
    {
        if (barRoot != null && barRoot.activeSelf != visible)
            barRoot.SetActive(visible);

        if (!visible)
            return;

        if (fill != null)
            fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);

        if (fillGraphic != null)
            fillGraphic.color = accent;

        if (backGraphic != null)
            backGraphic.color = Fade(accent, 0.18f);
    }

    private static Color Fade(Color color, float alpha) =>
        new Color(color.r, color.g, color.b, alpha);
}
