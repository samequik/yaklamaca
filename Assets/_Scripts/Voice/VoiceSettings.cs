using UnityEngine;

/// <summary>Konuşma nasıl tetikleniyor.</summary>
public enum VoiceMode
{
    /// <summary>Tuşa basılı tutunca. Yanlış tetikleme yok, en güvenlisi.</summary>
    PushToTalk,

    /// <summary>Sesin kendisi tetikliyor (eşik üstü). Elin serbest.</summary>
    Automatic
}

/// <summary>
/// Sesli sohbetin cihaz tercihleri. `PlayerProfile` ile aynı desen: hepsi
/// `PlayerPrefs`'te, oyun durumu değil (bkz. CLAUDE.md bölüm 5).
///
/// **Neden ayrı bir sınıf.** `PlayerProfile` "oyuncunun kim olduğu"nu tutuyor
/// ve lobiye kadar her yerden okunuyor; sesin altı ayarı orayı şişirirdi.
/// İkisi de statik ve aynı kurala tabi, ayrım yalnızca konu başlığı.
///
/// **Mikrofon cihazı ADIYLA saklanıyor, indeksle değil.** USB mikrofon
/// takılıp çıkarıldığında indeksler kayıyor ve oyuncu bir gün kulaklığının
/// mikrofonu yerine web kamerasınınkine konuşmaya başlıyor. Ad kaybolursa
/// varsayılan cihaza düşülüyor.
/// </summary>
public static class VoiceSettings
{
    private const string EnabledKey = "Ses_Acik";
    private const string ModeKey = "Ses_Mod";
    private const string DeviceKey = "Ses_Cihaz";
    private const string ThresholdKey = "Ses_Esik";
    private const string OutputKey = "Ses_Seviye";
    private const string InputGainKey = "Ses_Kazanc";

    /// <summary>
    /// Otomatik modda konuşma sayılan en düşük RMS.
    ///
    /// 0.02 sessiz bir odada nefes ve fan gürültüsünün üstünde, normal konuşma
    /// sesinin belirgin altında. Gürültülü ortamda oyuncu ayarlar ekranından
    /// yükseltiyor — bu yüzden ekranda **canlı seviye çubuğu** var, yoksa
    /// eşik ayarlamak körlemesine olurdu.
    /// </summary>
    public const float DefaultThreshold = 0.02f;

    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
        set { PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static VoiceMode Mode
    {
        get => (VoiceMode)PlayerPrefs.GetInt(ModeKey, (int)VoiceMode.PushToTalk);
        set { PlayerPrefs.SetInt(ModeKey, (int)value); PlayerPrefs.Save(); }
    }

    /// <summary>Seçili mikrofon adı. Boş = sistemin varsayılanı.</summary>
    public static string Device
    {
        get => PlayerPrefs.GetString(DeviceKey, string.Empty);
        set { PlayerPrefs.SetString(DeviceKey, value ?? string.Empty); PlayerPrefs.Save(); }
    }

    public static float Threshold
    {
        get => PlayerPrefs.GetFloat(ThresholdKey, DefaultThreshold);
        set { PlayerPrefs.SetFloat(ThresholdKey, Mathf.Clamp(value, 0f, 0.3f)); PlayerPrefs.Save(); }
    }

    /// <summary>Gelen konuşmanın genel ses seviyesi.</summary>
    public static float OutputVolume
    {
        get => PlayerPrefs.GetFloat(OutputKey, 1f);
        set { PlayerPrefs.SetFloat(OutputKey, Mathf.Clamp(value, 0f, 2f)); PlayerPrefs.Save(); }
    }

    /// <summary>Mikrofon kazancı — kısık mikrofonlar için.</summary>
    public static float InputGain
    {
        get => PlayerPrefs.GetFloat(InputGainKey, 1f);
        set { PlayerPrefs.SetFloat(InputGainKey, Mathf.Clamp(value, 0.2f, 4f)); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// Saklanan cihaz hâlâ takılı mı; değilse varsayılana düşülüyor.
    ///
    /// `Microphone.devices` boşsa (mikrofon yok, izin verilmedi) null dönüyor
    /// ve çağıran yakalamayı hiç başlatmıyor.
    /// </summary>
    public static string ResolveDevice()
    {
        string[] devices = Microphone.devices;

        if (devices == null || devices.Length == 0)
            return null;

        string stored = Device;

        if (!string.IsNullOrEmpty(stored))
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i] == stored)
                    return stored;
            }
        }

        return devices[0];
    }
}
