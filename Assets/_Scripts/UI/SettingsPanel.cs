using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Seçenekler ekranı. Şimdilik gerçekten işleyen iki ayar var: fare hassasiyeti
/// ve ses seviyesi. Diğerleri eklendikçe buraya biner.
///
/// Değerler PlayerPrefs'e yazılıyor ki oyun kapanınca kaybolmasın.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    private const string VolumeKey = "Ayar_SesSeviyesi";

    [Header("Oyuncu Adı")]
    [Tooltip("itch.io'da Steam gibi hazır isim yok; oyuncu kendi adını giriyor.")]
    [SerializeField] private TMP_InputField nameField;

    [Header("Fare Hassasiyeti")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_Text sensitivityLabel;
    [SerializeField] private Vector2 sensitivityRange = new Vector2(0.5f, 6f);

    [Header("Ses")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Text volumeLabel;

    [Header("Bakış")]
    [Tooltip("Ters bakış düğmesinin yazısı; durumu üstünde gösteriyor.")]
    [SerializeField] private TMP_Text invertLabel;

    private void Awake()
    {
        if (sensitivitySlider != null)
        {
            // Kaydedilen şey kaydırıcının konumu değil, hassasiyetin kendisi:
            // aralık ileride değişirse eski kayıt yanlış bir hıza dönüşmesin.
            sensitivitySlider.SetValueWithoutNotify(Mathf.InverseLerp(
                sensitivityRange.x, sensitivityRange.y, PlayerProfile.MouseSensitivity));

            sensitivitySlider.onValueChanged.AddListener(_ => ApplySensitivity());
        }

        if (volumeSlider != null)
        {
            volumeSlider.value = PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            volumeSlider.onValueChanged.AddListener(_ => ApplyVolume());
        }

        if (nameField != null)
        {
            nameField.characterLimit = PlayerProfile.MaxNameLength;
            nameField.text = PlayerProfile.Name;

            // Yazarken değil, alan terk edilince kaydediyoruz; her harfte
            // PlayerPrefs'e yazmak gereksiz.
            nameField.onEndEdit.AddListener(ApplyName);
        }

        ApplySensitivity();
        ApplyVolume();
        RefreshInvert();
    }

    /// <summary>
    /// Dikey bakışı ters çevirir. Açılır liste yerine durumu üstünde yazan bir
    /// düğme: iki değeri olan bir ayar için en az parçalı çözüm.
    /// </summary>
    public void ToggleInvertLook()
    {
        PlayerProfile.InvertLook = !PlayerProfile.InvertLook;

        // Sahada duran girdi kaynağı varsa anında uygulanıyor.
        foreach (PlayerInputSource source in FindObjectsOfType<PlayerInputSource>())
            source.InvertLook = PlayerProfile.InvertLook;

        RefreshInvert();
    }

    private void RefreshInvert()
    {
        if (invertLabel != null)
            invertLabel.SetText(PlayerProfile.InvertLook ? "Ters bakış: AÇIK" : "Ters bakış: kapalı");
    }

    private void ApplyName(string value)
    {
        PlayerProfile.Name = value;

        // Kırpılmış/temizlenmiş hâli kutuda da görünsün.
        if (nameField != null)
            nameField.SetTextWithoutNotify(PlayerProfile.Name);

        // Odadaysak sunucu eski adı biliyor; yeni adı bildiriyoruz ki lobi
        // listesi ve tur sonu ekranı doğru ismi göstersin.
        RoundParticipant local = RoundParticipant.Local;
        if (local != null)
            local.PushName();
    }

    private void ApplySensitivity()
    {
        if (sensitivitySlider == null)
            return;

        float value = Mathf.Lerp(sensitivityRange.x, sensitivityRange.y, sensitivitySlider.value);

        // Önce cihaza yazılıyor: oyuncu prefabtan doğduğu için ayarın kalıcı
        // yeri orası, sahnedeki bir bileşen değil.
        PlayerProfile.MouseSensitivity = value;

        // Sahada duran girdi kaynağı varsa anında uygulanıyor — oyuncu ayarı
        // değiştirip sonucunu görmek için yeniden doğmayı beklememeli.
        foreach (PlayerInputSource source in FindObjectsOfType<PlayerInputSource>())
            source.MouseSensitivity = value;

        if (sensitivityLabel != null)
            sensitivityLabel.SetText("Fare hassasiyeti: {0:1}", value);
    }

    private void ApplyVolume()
    {
        if (volumeSlider == null)
            return;

        AudioListener.volume = volumeSlider.value;

        if (volumeLabel != null)
            volumeLabel.SetText("Ses: {0}%", Mathf.RoundToInt(volumeSlider.value * 100f));

        PlayerPrefs.SetFloat(VolumeKey, volumeSlider.value);
    }
}
