using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Seçenekler ekranı: oyuncu adı, fare hassasiyeti, ters bakış.
///
/// **Ses buradan ÇIKARILDI** (2026-09-06). Genel ses ve sesli sohbetin altı
/// ayarı da buradayken ekran alt alta sığmıyordu; hepsi `AudioPanel`'e taşındı
/// ve buraya bir SES düğmesi kondu. Tuş atamaları zaten öyleydi — seçenekler
/// artık kategori kapısı, ayar deposu değil.
///
/// Değerler PlayerPrefs'te, oyun kapanınca kaybolmasın diye.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Oyuncu Adı")]
    [Tooltip("itch.io'da Steam gibi hazır isim yok; oyuncu kendi adını giriyor.")]
    [SerializeField] private TMP_InputField nameField;

    [Header("Fare Hassasiyeti")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_Text sensitivityLabel;
    [SerializeField] private Vector2 sensitivityRange = new Vector2(0.5f, 6f);

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

        if (nameField != null)
        {
            nameField.characterLimit = PlayerProfile.MaxNameLength;
            nameField.text = PlayerProfile.Name;

            // Yazarken değil, alan terk edilince kaydediyoruz; her harfte
            // PlayerPrefs'e yazmak gereksiz.
            nameField.onEndEdit.AddListener(ApplyName);
        }

        ApplySensitivity();
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

}
