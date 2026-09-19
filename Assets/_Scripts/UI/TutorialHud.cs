using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tutorial koridorundaki alt yazı kutusu. Menüdeki gerçek arayüzden BİLEREK
/// bağımsız: `Menü Kur` çalıştırılmamış bir sahnede de çalışsın diye kendi
/// Canvas'ını `RevivalScreen.Ensure()` ile aynı yöntemle çalışma anında
/// kuruyor.
///
/// `TutorialCaptionTrigger`'lar `Show` çağırıyor, kutu birkaç saniye görünüp
/// kendiliğinden soluyor.
///
/// **Yazılar sıraya giriyor.** İstasyonlar koridorda birkaç metre arayla
/// duruyor; yürüyen oyuncu art arda iki tetiğe girince ikinci yazı birincinin
/// üstüne anında biniyor ve ilki okunamadan kayboluyordu. Artık her yazı en az
/// `MinimumVisibleSeconds` ekranda kalıyor, gelen yenisi o süre dolunca
/// gösteriliyor.
/// </summary>
public class TutorialHud : MonoBehaviour
{
    private const float MinimumVisibleSeconds = 3f;

    private struct Caption
    {
        public string Text;
        public float Seconds;
    }

    private static TutorialHud instance;

    private readonly Queue<Caption> pending = new Queue<Caption>();

    private CanvasGroup group;
    private TMP_Text label;
    private float shownAt;
    private float hideAt;

    /// <summary>Yazıyı gösterir; ekrandaki henüz okunacak kadar kalmadıysa sıraya koyar.</summary>
    public static void Show(string message, float seconds = 6f)
    {
        Ensure();

        if (instance.IsReadingTime())
        {
            instance.pending.Enqueue(new Caption { Text = message, Seconds = seconds });
            return;
        }

        instance.Display(message, seconds);
    }

    /// <summary>Sırayı atlayıp hemen gösterir — tutorial'ın bittiğini söyleyen yazı için.</summary>
    public static void ShowNow(string message, float seconds = 6f)
    {
        Ensure();
        instance.pending.Clear();
        instance.Display(message, seconds);
    }

    /// <summary>
    /// Kutuyu kapatıp sırayı boşaltır. Kutu DontDestroyOnLoad, yani ana menüye
    /// dönerken temizlenmezse sırada bekleyen tutorial yazıları menünün üstünde
    /// belirirdi.
    /// </summary>
    public static void Clear()
    {
        if (instance == null)
            return;

        instance.pending.Clear();
        instance.group.alpha = 0f;
    }

    private bool IsReadingTime() =>
        group.alpha > 0f && Time.unscaledTime - shownAt < MinimumVisibleSeconds;

    private void Display(string message, float seconds)
    {
        label.SetText(message);
        group.alpha = 1f;
        shownAt = Time.unscaledTime;
        hideAt = shownAt + seconds;
    }

    private static void Ensure()
    {
        if (instance != null)
            return;

        GameObject root = new GameObject("TutorialHud",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        instance = root.AddComponent<TutorialHud>();
        instance.Build(root.transform);

        DontDestroyOnLoad(root);
    }

    private void Build(Transform parent)
    {
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 90f);
        rect.sizeDelta = new Vector2(980f, 110f);

        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.02f, 0.02f, 0.03f, 0.82f);
        background.raycastTarget = false;

        GameObject labelObject = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(panel.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(28f, 10f);
        labelRect.offsetMax = new Vector2(-28f, -10f);

        label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.92f, 0.92f, 0.95f, 1f);
        label.enableWordWrapping = true;
        label.raycastTarget = false;

        group = panel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
    }

    private void Update()
    {
        if (group == null)
            return;

        if (pending.Count > 0 && !IsReadingTime())
        {
            Caption next = pending.Dequeue();
            Display(next.Text, next.Seconds);
            return;
        }

        if (group.alpha <= 0f)
            return;

        // Son bir saniye yumuşakça soluyor; öncesi tam görünür kalıyor —
        // oyuncu okurken birden kaybolmasın.
        group.alpha = Mathf.Clamp01(hideAt - Time.unscaledTime);
    }
}
