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

    [Header("Dil")]
    [Tooltip("Dil düğmesinin yazısı; her zaman SEÇİLİ dilin kendi adıyla yazıyor " +
        "(\"Language: English\" / \"Dil: Türkçe\") — oyuncu henüz İngilizce " +
        "bilmiyorsa bile bu satırı tanıyabilmeli.")]
    [SerializeField] private TMP_Text languageLabel;

    [Header("Korku efektleri")]
    [Tooltip("Vinyet, gren, bloom, sarsıntı ve parazitin ortak şiddeti " +
        "(bölüm 25). Kaydırıcı, açma/kapama değil: gren ve sarsıntı bazı " +
        "oyuncuların gözünü yoruyor ama tamamen kapatmak oyunun görünümünü " +
        "de alıp götürüyor. Arada bir yer isteyen oyuncunun seçeneği olmalı.")]
    [SerializeField] private Slider horrorSlider;

    [SerializeField] private TMP_Text horrorLabel;

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

        if (horrorSlider != null)
        {
            horrorSlider.SetValueWithoutNotify(PlayerProfile.HorrorEffects);
            horrorSlider.onValueChanged.AddListener(_ => ApplyHorror());
        }

        ApplySensitivity();
        ApplyHorror();
        RefreshInvert();
        RefreshLanguage();
    }

    /// <summary>
    /// Korku efektlerinin şiddeti. Hassasiyetle aynı desen: önce cihaza, sonra
    /// sahada duran bileşene.
    ///
    /// Sahada duran bileşeni aramaya gerek yok — çarpan `ScreenEffects`'te
    /// **statik** duruyor, çünkü oyuncu her turda prefabtan yeniden doğuyor.
    /// </summary>
    private void ApplyHorror()
    {
        if (horrorSlider == null)
            return;

        float value = Mathf.Clamp01(horrorSlider.value);

        PlayerProfile.HorrorEffects = value;
        ScreenEffects.Master = value;

        if (horrorLabel != null)
        {
            horrorLabel.SetText(value <= 0.001f
                ? Localization.Get("Korku efektleri: KAPALI")
                : Localization.Format("Korku efektleri: %{0}", Mathf.RoundToInt(value * 100f)));
        }
    }

    /// <summary>
    /// Dil düğmesi. Yazı her zaman seçili dilin KENDİ adıyla yazıyor, ortak
    /// bir şablondan değil — oyuncu henüz İngilizce okuyamıyorsa bile
    /// "Dil: Türkçe" satırını tanıyıp tıklayabilmeli.
    /// </summary>
    public void ToggleLanguage()
    {
        Localization.Current = Localization.Current == GameLanguage.English
            ? GameLanguage.Turkish
            : GameLanguage.English;

        RefreshLanguage();
    }

    private void RefreshLanguage()
    {
        if (languageLabel == null)
            return;

        languageLabel.SetText(Localization.Current == GameLanguage.English
            ? "Language: English"
            : "Dil: Türkçe");
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
        {
            invertLabel.SetText(Localization.Get(
                PlayerProfile.InvertLook ? "Ters bakış: AÇIK" : "Ters bakış: kapalı"));
        }
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
            sensitivityLabel.SetText(Localization.Get("Fare hassasiyeti: {0:1}"), value);
    }

}
