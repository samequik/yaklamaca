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
    private const string RunnerCostumeKey = "Kostum_Kacan";
    private const string MonsterCostumeKey = "Kostum_Canavar";
    private const string HorrorKey = "Ayar_KorkuEfekti";

    public const float DefaultSensitivity = 2f;

    /// <summary>
    /// Seçilen kaçan kostümü (bkz. CharacterCatalog).
    ///
    /// Ad ve hassasiyetle aynı sınıftan bir **cihaz tercihi**: oyuncu prefabtan
    /// doğduğu için bir sahne nesnesine yazmak her turda varsayılana dönerdi.
    /// Turdaki karşılığı `RoundParticipant`'ın SyncVar'ı; burası yalnızca
    /// "bu makinede en son neyi seçmiştim" sorusunu cevaplıyor.
    ///
    /// Okurken de temizleniyor: liste kısalırsa kayıtlı indeks geçersiz kalır
    /// ve doğrudan kullanmak dizi sınırı hatası verirdi.
    /// </summary>
    public static int RunnerCostume
    {
        get => CharacterCatalog.SanitizeRunner(PlayerPrefs.GetInt(RunnerCostumeKey, 0));
        set
        {
            PlayerPrefs.SetInt(RunnerCostumeKey, CharacterCatalog.SanitizeRunner(value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Seçilen canavar kostümü. Rolü sunucu dağıtıyor; bu yalnızca görünüş.</summary>
    public static int MonsterCostume
    {
        get => CharacterCatalog.SanitizeMonster(PlayerPrefs.GetInt(MonsterCostumeKey, 0));
        set
        {
            PlayerPrefs.SetInt(MonsterCostumeKey, CharacterCatalog.SanitizeMonster(value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Korku ekran efektlerinin şiddeti (0 = kapalı, 1 = tam). Bölüm 25.
    ///
    /// Kapatılabilir olması bir konfor ayarı değil gereklilik: efekt kare
    /// başına bir blit ve zayıf bir GPU'da yüksek çözünürlükte ölçülebilir hâle
    /// geliyor. Ayrıca gren ve sarsıntı bazı oyuncuların gözünü yoruyor.
    /// </summary>
    public static float HorrorEffects
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(HorrorKey, 1f));
        set
        {
            PlayerPrefs.SetFloat(HorrorKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }
    }

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
