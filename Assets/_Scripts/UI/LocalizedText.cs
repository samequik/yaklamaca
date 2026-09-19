using TMPro;
using UnityEngine;

/// <summary>
/// `Menü Kur` tarafından bir kez basılan (Editor zamanında kurulan) sabit bir
/// yazıya dil anahtarı bağlar. Yalnızca ÇALIŞMA ANINDA hiç değişmeyen
/// başlık/düğme yazılarına ekleniyor — `Refresh()` gibi bir metodun her
/// karede/olayda kendi yazdığı yazılara EKLENMİYOR, çünkü ikisi çakışırdı
/// (bkz. MenuSetup.cs'teki "Loc(...)" çağrılarının seçimi).
///
/// Anahtar Türkçe orijinal metnin kendisi — `Localization` sözlüğü de aynı
/// anahtarı kullanıyor.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    public string key;

    private TMP_Text label;

    private void OnEnable()
    {
        if (label == null)
            label = GetComponent<TMP_Text>();

        Apply();
        Localization.Changed += Apply;
    }

    private void OnDisable() => Localization.Changed -= Apply;

    private void Apply()
    {
        if (label != null && !string.IsNullOrEmpty(key))
            label.SetText(Localization.Get(key));
    }
}
