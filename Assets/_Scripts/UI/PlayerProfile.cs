using UnityEngine;

/// <summary>
/// Yerel oyuncunun adı. Cihazda saklanır, oyun kapanınca kaybolmaz.
///
/// itch.io'da Steam gibi hazır bir isim kaynağı yok, o yüzden oyuncu adını
/// kendisi giriyor. Steam sürümü gelirse buraya Steam adı beslenecek ve
/// çağıranların hiçbiri değişmeyecek.
///
/// Statik ama oyun durumu tutmuyor; sadece bir cihaz tercihi. Rol, canlılık
/// gibi tur verileri her zaman RoundManager'da (bkz. CLAUDE.md).
/// </summary>
public static class PlayerProfile
{
    public const string DefaultName = "Player";
    public const int MaxNameLength = 16;

    private const string NameKey = "Oyuncu_Adi";
    private const string SensitivityKey = "Ayar_FareHassasiyeti";
    private const string InvertLookKey = "Ayar_TersBakis";

    public const float DefaultSensitivity = 2f;

    /// <summary>Dikey bakış ters mi (uçak kontrolü). Klasik bir tercih meselesi.</summary>
    public static bool InvertLook
    {
        get => PlayerPrefs.GetInt(InvertLookKey, 0) != 0;
        set
        {
            PlayerPrefs.SetInt(InvertLookKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Fare hassasiyeti. Ada benzer bir cihaz tercihi, o yüzden burada.
    ///
    /// Oyuncu prefabtan doğduğu için ayarı bir sahne nesnesine yazmak
    /// yetmiyordu: her yeni doğan oyuncu varsayılana dönüyordu. Değer artık
    /// burada duruyor, `PlayerInputSource` doğarken onu okuyor.
    /// </summary>
    public static float MouseSensitivity
    {
        get => PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity);
        set
        {
            PlayerPrefs.SetFloat(SensitivityKey, Mathf.Max(0.05f, value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Oyuncu daha önce ad girdi mi. Girmediyse ana menü yerine isim ekranı
    /// açılıyor — bir kez sorulup bir daha rahatsız edilmiyor.
    /// </summary>
    public static bool HasName => PlayerPrefs.HasKey(NameKey);

    public static string Name
    {
        get
        {
            string stored = PlayerPrefs.GetString(NameKey, DefaultName);
            return string.IsNullOrWhiteSpace(stored) ? DefaultName : stored;
        }
        set
        {
            PlayerPrefs.SetString(NameKey, Sanitize(value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Boş ad ve aşırı uzunluk lobide satırları bozuyor.</summary>
    public static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultName;

        string trimmed = value.Trim();
        return trimmed.Length > MaxNameLength ? trimmed.Substring(0, MaxNameLength) : trimmed;
    }
}
