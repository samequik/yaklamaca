using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ekranın sağ üst köşesindeki mikrofon göstergesi.
///
/// İki soruyu birden cevaplıyor: **mikrofon sesimi alıyor mu** ve **şu anda
/// gidiyor mu.** İkisi ayrı şeyler ve karıştırılmaları en sinir bozucu
/// durumu üretiyor: konuştuğunu sanıp kimsenin duymaması.
///
/// - Çubuk **her zaman** mikrofonun duyduğu seviyeyi gösteriyor, gönderilse de
///   gönderilmese de. Böylece bas-konuş tuşuna basmadan önce bile mikrofonun
///   çalıştığını görüyorsun.
/// - Çubuğun ve simgenin **rengi** gönderim durumunu söylüyor: sönük gri =
///   duyuyor ama göndermiyor, vurgu rengi = gidiyor.
/// - Otomatik modda çubuğun üstünde bir **eşik çizgisi** var. Eşiği körlemesine
///   ayarlamak imkânsızdı; şimdi "sesim çizgiyi geçiyor mu" diye bakılıyor.
///
/// ### Seviye neden karekökle çiziliyor
///
/// RMS doğrusal, kulak logaritmik. Ham değeri doğrudan genişliğe çevirince
/// normal konuşma çubuğun ilk beşte birinde kalıyor ve hiçbir şey
/// ayırt edilemiyordu. Karekök, düşük seviyeleri açıp çubuğu okunur yapıyor.
///
/// ### Sönümlenme
///
/// Çubuk hızlı çıkıp yavaş iniyor. Ham değeri her karede yazmak, konuşmanın
/// doğal boşluklarında çubuğu titretiyordu. Aynı numara kilitli terminalin
/// alarm ışığında da var (bölüm 12): tepe anında alınıyor, yavaş bırakılıyor.
/// </summary>
public class VoiceHud : MonoBehaviour
{
    [Header("Parçalar")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private Graphic[] micParts;
    [SerializeField] private RectTransform levelFill;
    [SerializeField] private Graphic levelFillGraphic;
    [SerializeField] private RectTransform thresholdMark;
    [SerializeField] private TMP_Text hintLabel;

    [Header("Ölçek")]
    [Tooltip("Çubuğu dolduran RMS değeri. Normal konuşma 0.05-0.15 arasında; " +
        "0.25 bağırmaya denk geliyor.")]
    [SerializeField] private float fullScaleLevel = 0.25f;

    [Tooltip("Çubuğun düşme hızı (birim/saniye). Yükselme anında.")]
    [SerializeField] private float falloff = 1.6f;

    [Header("Renkler")]
    [SerializeField] private Color idleColor = new Color(0.45f, 0.45f, 0.5f, 0.75f);
    [SerializeField] private Color activeColor = new Color(0.85f, 0.25f, 0.2f, 1f);

    private float shown;

    private void Update()
    {
        bool available = VoiceSettings.Enabled && Microphone.devices.Length > 0;

        GameHud.SetVisible(group, available);

        if (!available)
            return;

        UpdateLevel();
        UpdateColors();
        UpdateThreshold();
        UpdateHint();
    }

    private void UpdateLevel()
    {
        // Karekök: RMS doğrusal, kulak değil. Ham değer çubuğun ilk beşte
        // birinde kalıyor ve okunmuyordu.
        float target = Mathf.Sqrt(Mathf.Clamp01(VoiceCapture.CurrentLevel / fullScaleLevel));

        // Hızlı çık, yavaş in: ham değeri yazmak konuşmanın doğal
        // boşluklarında çubuğu titretiyor.
        shown = target > shown
            ? target
            : Mathf.MoveTowards(shown, target, falloff * Time.unscaledDeltaTime);

        if (levelFill != null)
            levelFill.anchorMax = new Vector2(shown, 1f);
    }

    private void UpdateColors()
    {
        Color color = VoiceCapture.Transmitting ? activeColor : idleColor;

        if (levelFillGraphic != null)
            levelFillGraphic.color = color;

        if (micParts == null)
            return;

        for (int i = 0; i < micParts.Length; i++)
        {
            if (micParts[i] != null)
                micParts[i].color = color;
        }
    }

    private void UpdateThreshold()
    {
        if (thresholdMark == null)
            return;

        bool automatic = VoiceSettings.Mode == VoiceMode.Automatic;

        if (thresholdMark.gameObject.activeSelf != automatic)
            thresholdMark.gameObject.SetActive(automatic);

        if (!automatic)
            return;

        // Çizgi çubukla AYNI ölçeği kullanmalı, yoksa "sesim çizgiyi geçti ama
        // gitmedi" gibi bir yalan söyler.
        float position = Mathf.Sqrt(Mathf.Clamp01(VoiceSettings.Threshold / fullScaleLevel));

        thresholdMark.anchorMin = new Vector2(position, 0f);
        thresholdMark.anchorMax = new Vector2(position, 1f);
        thresholdMark.anchoredPosition = Vector2.zero;
    }

    private void UpdateHint()
    {
        if (hintLabel == null)
            return;

        // Bas-konuşta tuşu yazıyoruz: oyuncu tuşu değiştirdiyse ekranda
        // güncel olanı görsün, kılavuza bakmasın.
        string text = VoiceSettings.Mode == VoiceMode.PushToTalk
            ? KeyBindings.Describe(KeyBindings.Get(GameAction.PushToTalk))
            : "OTO";

        hintLabel.SetText(text);
        hintLabel.color = VoiceCapture.Transmitting ? activeColor : idleColor;
    }
}
