using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Tek Canvas paneli; kabinler kendi durumlarını yayınlar.</summary>
public class RevivalScreen : MonoBehaviour
{
    private static RevivalScreen instance;
    private CanvasGroup group;
    private TMP_Text title, status, keys, hint;
    private Image fill;
    public static void Ensure()
    {
        if (instance != null) return;
        GameObject root = new GameObject("DiriltmeEkrani", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 12;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        instance = root.AddComponent<RevivalScreen>();
        instance.Build();
    }
    private void Build()
    {
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        panel.transform.SetParent(transform, false);
        var rect = panel.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(600, 300);
        panel.GetComponent<Image>().color = new Color(0.02f, 0.055f, 0.065f, 0.95f);
        group = panel.GetComponent<CanvasGroup>();
        title = Label(panel.transform, 105, 28); status = Label(panel.transform, 52, 24);
        keys = Label(panel.transform, -6, 30); hint = Label(panel.transform, -110, 18);
        var bar = new GameObject("Ilerleme", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(panel.transform, false);
        bar.GetComponent<RectTransform>().sizeDelta = new Vector2(520, 12);
        bar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -66);
        bar.GetComponent<Image>().color = new Color(0.1f, 0.2f, 0.23f);
        var foreground = new GameObject("Doluluk", typeof(RectTransform), typeof(Image));
        foreground.transform.SetParent(bar.transform, false);
        fill = foreground.GetComponent<Image>(); fill.color = new Color(0.1f, 0.9f, 0.7f);
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        GameHud.SetVisible(group, false);
    }
    private static TMP_Text Label(Transform parent, float y, float size)
    {
        var go = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(560, 60); text.rectTransform.anchoredPosition = new Vector2(0, y);
        return text;
    }
    private static string Key(int direction) => KeyBindings.Describe(KeyBindings.Get(RevivalStation.DirectionAction(direction)));
    private void Update()
    {
        var station = RevivalStation.ActiveLocal;
        bool visible = station != null && GameHud.Visible;
        GameHud.SetVisible(group, visible);
        if (!visible) return;
        title.text = "DİRİLTME • " + (station.Body != null ? station.Body.VictimName : "Kaçan")
            + "   (kalan hak: " + station.ChargesLeft + ")";
        status.text = station.Locked ? "İŞLEM SIFIRLANDI — KİLİDİ AÇ" : $"İyileştirme: {station.Remaining:0.0} saniye kaldı";
        if (station.Locked)
        {
            string sequence = "";
            for (int i = 0; i < 4; i++)
            {
                string key = Key(((station.UnlockCode >> (2 * i)) & 3) + 1);
                sequence += i < station.UnlockEntered ? "<color=#44DD99>" + key + "</color>  " : key + "  ";
            }
            keys.text = sequence;
        }
        else keys.text = station.Prompt != 0 ? $"[ {Key(station.Prompt)} ]   {System.Math.Max(0, station.Deadline - Mirror.NetworkTime.time):0.0} sn" : "Yaşam desteği çalışıyor…";
        fill.rectTransform.anchorMax = new Vector2(station.Progress, 1);
        hint.text = KeyBindings.Describe(KeyBindings.Get(GameAction.Interact)) + " ile bırak • bırakınca ilerleme sıfırlanır";
    }
    private void OnDestroy() { if (instance == this) instance = null; }
}
