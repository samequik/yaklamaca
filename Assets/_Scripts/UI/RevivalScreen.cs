using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Diriltme kabininin ekranı. Tek panel iki kabine birden hizmet ediyor —
/// terminal başında hareket kilitli olduğu için aynı anda yalnızca birine
/// bağlanılabiliyor (`RevivalStation.ActiveLocal`), bkz. bölüm 20'deki
/// "beş terminal, TEK panel" gerekçesi.
///
/// **Görsel dil terminal ve çıkış kilidi panelleriyle aynı** (bölüm 18):
/// koyu gövde, ince çerçeve, köşe ayraçları, tek renk ailesi. Fark yalnızca
/// renkte — kabinin kendi ışığı turkuaz, ekran da onu izliyor. Kilitliyken
/// bütün panel kırmızıya dönüyor, yani "bir şey ters gitti" tek bakışta
/// okunuyor.
///
/// Panel tam ekran DEĞİL, bilerek: terminal başındaki oyuncunun tek savunması
/// etrafını duyup görebilmek (bölüm 11.2).
/// </summary>
public class RevivalScreen : MonoBehaviour
{
    private const int UnlockSteps = 4;

    private static readonly Color Accent = new Color(0.18f, 0.95f, 0.78f);
    private static readonly Color AccentDim = new Color(0.09f, 0.42f, 0.35f);
    private static readonly Color Alarm = new Color(1f, 0.34f, 0.22f);
    private static readonly Color AlarmDim = new Color(0.48f, 0.15f, 0.09f);

    private static RevivalScreen instance;

    private CanvasGroup group;
    private TMP_Text title, charges, big, caption, hint;
    private Image barBack, barFill;
    private Graphic[] frame;
    private Image[] cellBoxes;
    private TMP_Text[] cellLabels;
    private GameObject cellRow;

    public static void Ensure()
    {
        if (instance != null)
            return;

        GameObject root = new GameObject("DiriltmeEkrani",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 12;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        instance = root.AddComponent<RevivalScreen>();
        instance.Build();
    }

    private void Build()
    {
        GameObject panel = Box(transform, "Panel", new Vector2(560f, 250f), Vector2.zero,
            new Color(0.02f, 0.055f, 0.06f, 0.94f));
        group = panel.AddComponent<CanvasGroup>();

        // Çerçeve: dört ince kenar + dört köşe ayracı. Ayraçlar kalın,
        // kenarlar ince — çerçevenin tamamını kalınlaştırmak paneli
        // ağırlaştırıyor (bölüm 18).
        frame = new Graphic[]
        {
            Edge(panel.transform, new Vector2(560f, 2f), new Vector2(0f, 124f)),
            Edge(panel.transform, new Vector2(560f, 2f), new Vector2(0f, -124f)),
            Edge(panel.transform, new Vector2(2f, 250f), new Vector2(-279f, 0f)),
            Edge(panel.transform, new Vector2(2f, 250f), new Vector2(279f, 0f)),
            Edge(panel.transform, new Vector2(46f, 5f), new Vector2(-255f, 122f)),
            Edge(panel.transform, new Vector2(46f, 5f), new Vector2(255f, 122f)),
            Edge(panel.transform, new Vector2(46f, 5f), new Vector2(-255f, -122f)),
            Edge(panel.transform, new Vector2(46f, 5f), new Vector2(255f, -122f)),
            Edge(panel.transform, new Vector2(5f, 34f), new Vector2(-277f, 106f)),
            Edge(panel.transform, new Vector2(5f, 34f), new Vector2(277f, 106f)),
            Edge(panel.transform, new Vector2(5f, 34f), new Vector2(-277f, -106f)),
            Edge(panel.transform, new Vector2(5f, 34f), new Vector2(277f, -106f)),
        };

        title = Label(panel.transform, 16f, TextAlignmentOptions.Left, 100f, 24f);
        charges = Label(panel.transform, 14f, TextAlignmentOptions.Right, 100f, 24f);
        big = Label(panel.transform, 40f, TextAlignmentOptions.Center, 42f, 54f);
        caption = Label(panel.transform, 15f, TextAlignmentOptions.Center, 4f, 24f);

        GameObject bar = Box(panel.transform, "Cubuk", new Vector2(480f, 12f),
            new Vector2(0f, -34f), AccentDim * 0.5f);
        barBack = bar.GetComponent<Image>();

        GameObject fill = Box(bar.transform, "Doluluk", Vector2.zero, Vector2.zero, Accent);
        barFill = fill.GetComponent<Image>();
        RectTransform fillRect = barFill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        fillRect.pivot = new Vector2(0f, 0.5f);

        // Kilit dizilimi: hücreler ÖNCEDEN kuruluyor, sonra yalnızca renkleri
        // ve yazıları değişiyor. Çalışma anında obje yaratmak bölüm 2'nin
        // havuzlama kuralına takılırdı; adım sayısı da sabit.
        cellRow = new GameObject("Hucreler", typeof(RectTransform));
        cellRow.transform.SetParent(panel.transform, false);
        cellBoxes = new Image[UnlockSteps];
        cellLabels = new TMP_Text[UnlockSteps];

        for (int i = 0; i < UnlockSteps; i++)
        {
            float x = (i - (UnlockSteps - 1) * 0.5f) * 82f;
            GameObject cell = Box(cellRow.transform, "Hucre", new Vector2(70f, 56f),
                new Vector2(x, -72f), AccentDim * 0.35f);
            cellBoxes[i] = cell.GetComponent<Image>();
            cellLabels[i] = Label(cell.transform, 26f, TextAlignmentOptions.Center, 0f, 40f);
        }

        hint = Label(panel.transform, 13f, TextAlignmentOptions.Center, -108f, 22f);

        GameHud.SetVisible(group, false);
    }

    private static GameObject Box(Transform parent, string name, Vector2 size,
        Vector2 position, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return go;
    }

    private static Graphic Edge(Transform parent, Vector2 size, Vector2 position)
        => Box(parent, "Kenar", size, position, AccentDim).GetComponent<Image>();

    private static TMP_Text Label(Transform parent, float size, TextAlignmentOptions align,
        float y, float height)
    {
        GameObject go = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TMP_Text text = go.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = align;
        text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(500f, height);
        text.rectTransform.anchoredPosition = new Vector2(0f, y);
        return text;
    }

    private static string Key(int direction)
        => KeyBindings.Describe(KeyBindings.Get(RevivalStation.DirectionAction(direction)));

    private void Update()
    {
        RevivalStation station = RevivalStation.ActiveLocal;
        bool visible = station != null && GameHud.Visible;

        GameHud.SetVisible(group, visible);

        if (!visible)
            return;

        bool locked = station.Locked;
        Color accent = locked ? Alarm : Accent;
        Color dim = locked ? AlarmDim : AccentDim;

        for (int i = 0; i < frame.Length; i++)
            frame[i].color = dim;

        title.color = accent;
        charges.color = dim;
        big.color = accent;
        caption.color = dim;
        hint.color = dim;
        barBack.color = dim * 0.5f;
        barFill.color = accent;

        title.text = "DİRİLTME — " + (station.Body != null ? station.Body.VictimName : "KAÇAN");
        charges.text = "HAK " + station.ChargesLeft;

        cellRow.SetActive(locked);

        if (locked)
        {
            big.text = "SİSTEM KİLİTLİ";
            caption.text = "Diziliyi gir — ilerleme sıfırlandı";

            for (int i = 0; i < UnlockSteps; i++)
            {
                bool done = i < station.UnlockEntered;
                bool next = i == station.UnlockEntered;

                // Sıradaki hücre DOLU renk, yazısı koyu: göz sıradakini
                // aramak zorunda kalmıyor (bölüm 18).
                cellBoxes[i].color = next ? accent : done ? dim : dim * 0.35f;
                cellLabels[i].color = next ? new Color(0.05f, 0.05f, 0.05f) : accent;
                cellLabels[i].text = Key(((station.UnlockCode >> (2 * i)) & 3) + 1);
            }
        }
        else if (station.Prompt != 0)
        {
            double left = System.Math.Max(0d, station.Deadline - Mirror.NetworkTime.time);
            big.text = "[ " + Key(station.Prompt) + " ]";
            caption.text = $"{left:0.0} saniye içinde bas";
        }
        else
        {
            big.text = $"{station.Remaining:0.0}";
            caption.text = "YAŞAM DESTEĞİ ÇALIŞIYOR";
        }

        barFill.rectTransform.anchorMax = new Vector2(station.Progress, 1f);
        hint.text = KeyBindings.Describe(KeyBindings.Get(GameAction.Interact))
            + " ile bırak — bırakınca ilerleme sıfırlanır";
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
