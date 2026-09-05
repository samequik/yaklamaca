using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ses ekranı: genel ses ve sesli sohbetin tamamı.
///
/// ### Neden ayrı bir ekran
///
/// Hepsi seçeneklerin içindeydi ve ekran **alt alta sığmıyordu**: ad, fare,
/// ses, ters bakış, altı sesli sohbet ayarı ve iki düğme. Diğer oyunların
/// yaptığı gibi kategoriye ayrıldı — seçeneklerde artık oyunun kendisi
/// (ad, fare, bakış) var, ses ve tuşlar birer alt ekran.
///
/// ### Değerler `OnEnable`'da okunuyor, `Awake`'te değil
///
/// Ekran her açılışta güncel değeri göstermeli: ayar başka bir yerden de
/// değişebiliyor (TAB panelindeki kişisel seviyeler genel seviyeyi kullanıyor)
/// ve panel kapalıyken `Awake` bir daha çalışmıyor.
///
/// Okuma sırasında `suppress` bayrağı kalkık: `Slider.value`'ya yazmak
/// dinleyiciyi tetikliyor ve tetiklenen dinleyici **kaydırıcının o anki
/// konumunu ayara geri yazıyor** — yani okunan değerin üstüne varsayılan
/// konum biniyordu. Ayarlar ekranında mikrofon kazancının hep 4.0x görünmesi
/// buydu. `SetValueWithoutNotify` çoğu durumda yetiyor ama Unity düzen
/// yeniden kurulurken de olay atabiliyor; bayrak ikisini birden kapatıyor.
/// </summary>
public class AudioPanel : MonoBehaviour
{
    private const string VolumeKey = "Ayar_SesSeviyesi";

    [Header("Genel")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Text volumeLabel;

    [Header("Sesli Sohbet")]
    [SerializeField] private TMP_Text voiceEnabledLabel;
    [SerializeField] private TMP_Text voiceModeLabel;
    [SerializeField] private TMP_Text voiceDeviceLabel;

    [SerializeField] private Slider micGainSlider;
    [SerializeField] private TMP_Text micGainLabel;

    [SerializeField] private Slider thresholdSlider;
    [SerializeField] private TMP_Text thresholdLabel;

    [SerializeField] private Slider voiceVolumeSlider;
    [SerializeField] private TMP_Text voiceVolumeLabel;

    private bool suppress;

    private void Awake()
    {
        if (volumeSlider != null)
            volumeSlider.onValueChanged.AddListener(_ => ApplyVolume());

        if (micGainSlider != null)
            micGainSlider.onValueChanged.AddListener(_ => ApplyMicGain());

        if (thresholdSlider != null)
            thresholdSlider.onValueChanged.AddListener(_ => ApplyThreshold());

        if (voiceVolumeSlider != null)
            voiceVolumeSlider.onValueChanged.AddListener(_ => ApplyVoiceVolume());

        Pull();
    }

    private void OnEnable() => Pull();

    /// <summary>Kayıtlı değerleri kaydırıcılara yazar — dinleyiciler susarken.</summary>
    private void Pull()
    {
        suppress = true;

        if (volumeSlider != null)
            volumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(VolumeKey, 0.8f));

        if (micGainSlider != null)
            micGainSlider.SetValueWithoutNotify(Mathf.InverseLerp(0.2f, 4f, VoiceSettings.InputGain));

        if (thresholdSlider != null)
            thresholdSlider.SetValueWithoutNotify(Mathf.InverseLerp(0f, 0.15f, VoiceSettings.Threshold));

        if (voiceVolumeSlider != null)
            voiceVolumeSlider.SetValueWithoutNotify(VoiceSettings.OutputVolume * 0.5f);

        suppress = false;

        ApplyVolume();
        Refresh();
    }

    // ---------- Genel ses ----------

    private void ApplyVolume()
    {
        if (volumeSlider == null)
            return;

        AudioListener.volume = volumeSlider.value;

        if (!suppress)
        {
            PlayerPrefs.SetFloat(VolumeKey, volumeSlider.value);
            PlayerPrefs.Save();
        }

        if (volumeLabel != null)
            volumeLabel.SetText("Ses: {0:0}%", volumeSlider.value * 100f);
    }

    // ---------- Sesli sohbet ----------

    public void ToggleVoiceEnabled()
    {
        VoiceSettings.Enabled = !VoiceSettings.Enabled;

        // Sahada duran yakalama varsa anında uygulanıyor: oyuncunun yeniden
        // doğmayı beklemesi gerekmiyor.
        foreach (VoiceChat chat in FindObjectsOfType<VoiceChat>())
            chat.ApplyEnabled();

        Refresh();
    }

    public void ToggleVoiceMode()
    {
        VoiceSettings.Mode = VoiceSettings.Mode == VoiceMode.PushToTalk
            ? VoiceMode.Automatic
            : VoiceMode.PushToTalk;

        Refresh();
    }

    /// <summary>
    /// Mikrofonu sıradakine çevirir. Açılır liste yerine döngü düğmesi: menü
    /// kodla kuruluyor ve dropdown çok daha fazla parça demek.
    /// </summary>
    public void CycleVoiceDevice()
    {
        string[] devices = Microphone.devices;

        if (devices == null || devices.Length == 0)
        {
            Refresh();
            return;
        }

        string current = VoiceSettings.ResolveDevice();
        int index = System.Array.IndexOf(devices, current);

        VoiceSettings.Device = devices[(index + 1) % devices.Length];

        // Yakalama açık cihazı bırakıp yenisini açmalı; yoksa değişiklik ancak
        // yeniden doğunca geçerli olurdu.
        foreach (VoiceCapture capture in FindObjectsOfType<VoiceCapture>())
            capture.Restart();

        Refresh();
    }

    private void ApplyMicGain()
    {
        if (suppress || micGainSlider == null)
            return;

        VoiceSettings.InputGain = Mathf.Lerp(0.2f, 4f, micGainSlider.value);
        Refresh();
    }

    private void ApplyThreshold()
    {
        if (suppress || thresholdSlider == null)
            return;

        VoiceSettings.Threshold = Mathf.Lerp(0f, 0.15f, thresholdSlider.value);
        Refresh();
    }

    private void ApplyVoiceVolume()
    {
        if (suppress || voiceVolumeSlider == null)
            return;

        VoiceSettings.OutputVolume = voiceVolumeSlider.value * 2f;

        // Sahadaki her konuşmacının kaynağı ayrı; genel seviye hepsine yeniden
        // uygulanıyor, kişisel çarpanları korunarak.
        foreach (VoicePlayback playback in FindObjectsOfType<VoicePlayback>())
            playback.ApplyVolume();

        Refresh();
    }

    private void Refresh()
    {
        if (voiceEnabledLabel != null)
        {
            voiceEnabledLabel.SetText(VoiceSettings.Enabled
                ? "Sesli sohbet: AÇIK"
                : "Sesli sohbet: KAPALI");
        }

        if (voiceModeLabel != null)
        {
            voiceModeLabel.SetText(VoiceSettings.Mode == VoiceMode.PushToTalk
                ? $"Konuşma: BAS-KONUŞ ({KeyBindings.Describe(KeyBindings.Get(GameAction.PushToTalk))})"
                : "Konuşma: OTOMATİK");
        }

        if (voiceDeviceLabel != null)
            voiceDeviceLabel.SetText($"Mikrofon: {ShortDeviceName(VoiceSettings.ResolveDevice())}");

        if (micGainLabel != null)
            micGainLabel.SetText("Mikrofon kazancı: {0:0.0}x", VoiceSettings.InputGain);

        if (thresholdLabel != null)
        {
            // Eşiğin sayısı tek başına bir şey anlatmıyor; oyuncu sağ üstteki
            // çubuğa bakarak ayarlıyor.
            thresholdLabel.SetText(VoiceSettings.Mode == VoiceMode.Automatic
                ? $"Konuşma eşiği: {VoiceSettings.Threshold:0.000}   (sağ üstteki çizgi)"
                : "Konuşma eşiği: yalnızca otomatik modda");
        }

        if (voiceVolumeLabel != null)
            voiceVolumeLabel.SetText("Konuşma sesi: {0:0}%", VoiceSettings.OutputVolume * 100f);
    }

    /// <summary>
    /// Cihaz adını düğmeye sığdırır.
    ///
    /// Windows mikrofonları "Microphone (High Definition Audio Device)" gibi
    /// adlanıyor ve tam hâli düğmeyi iki satıra taşırıp taşıyordu. Parantez
    /// içi zaten sürücünün adı; ayırt edici olan baş kısım.
    /// </summary>
    private static string ShortDeviceName(string device)
    {
        if (string.IsNullOrEmpty(device))
            return "YOK";

        int parenthesis = device.IndexOf('(');
        string trimmed = parenthesis > 1 ? device.Substring(0, parenthesis).Trim() : device;

        return trimmed.Length > 28 ? trimmed.Substring(0, 27).Trim() + "…" : trimmed;
    }
}
