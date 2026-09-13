using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// `RevivalStation`'ın sayısal alanlarını (özellikle `duration`) sahnedeki
/// BÜTÜN istasyonlarda kodun GÜNCEL varsayılanına zorlar.
///
/// ### Neden gerekti — gerçek bir hata, sahne dosyasından bulundu (2026-09-13)
///
/// `RevivalStation.duration`'ın C# varsayılanı aynı gün DAHA ÖNCE 15'ten
/// 10'a indirilmişti (oynanış geri bildirimi, CLAUDE.md bölüm 23) ve
/// "YAPILDI" diye işaretlenmişti. **Bu yanlıştı** — yalnızca kodun
/// varsayılanı değişti, sahnedeki `Diriltme_A`/`Diriltme_B` o değişiklikten
/// ÇOK ÖNCE kurulmuştu ve `duration` alanları o an SERİLEŞTİRİLDİ. Bir
/// C# alan varsayılanını değiştirmek, ZATEN VAR OLAN bir bileşenin
/// serileştirilmiş değerini GERİYE DÖNÜK değiştirmiyor — bölüm 16'nın
/// "koddaki varsayılanı değiştirmek yetmiyor" tuzağının (`MovementProfile`,
/// katman maskeleri) BİREBİR AYNISI, bu kez fark edilmeden burada da oldu.
///
/// **Kullanıcı elle bir istasyon daha kopyalayınca (Ctrl+D) ortaya çıktı:**
/// sahne dosyası doğrudan okundu, `Diriltme_A`, `Diriltme_B` VE kullanıcının
/// kopyaladığı üçüncü istasyon — ÜÇÜ DE hâlâ `duration: 15` taşıyordu
/// (kopya, orijinalin O ANKİ serileştirilmiş değerini miras aldığı için
/// hatayı da miras aldı). Yani CLAUDE.md'de "15→10 YAPILDI" yazmasına
/// rağmen gerçek oyunda istasyonlar hâlâ 15 saniye sürüyordu — kod ile
/// sahne arasında sessiz bir uyuşmazlık.
///
/// `useDistance`/`acceptRadius` bu hatadan etkilenmiyor (kod varsayılanları
/// hiç değişmedi), ama araç ileride BENZER bir drift olursa diye ikisini de
/// aynı yöntemle kontrol ediyor.
///
/// ### Sayı ikinci kez YAZILMIYOR
///
/// "10f" gibi bir sabiti burada da yazmak, ileride biri `RevivalStation`'ın
/// varsayılanını TEKRAR değiştirirse bu aracı da unutup aynı hatayı üçüncü
/// kez üretebilirdi. Onun yerine geçici, sahneye hiç eklenmeyen bir örnek
/// (`MapDressWindow.MeasurePrefab`'ın "ölç, hemen sil" deseniyle aynı)
/// üzerinden KODUN O ANKİ varsayılanı okunuyor — tek doğruluk kaynağı
/// `RevivalStation.cs`'in kendisi kalıyor.
///
/// Menü: Yakalamaca > Diriltme Süresini Senkronize Et
/// </summary>
public static class RevivalDurationSync
{
    [MenuItem("Yakalamaca/Diriltme Süresini Senkronize Et")]
    private static void Sync()
    {
        RevivalStation[] stations = Object.FindObjectsOfType<RevivalStation>(true);
        if (stations.Length == 0)
        {
            EditorUtility.DisplayDialog("İstasyon yok",
                "Sahnede hiç RevivalStation bulunamadı.", "Tamam");
            return;
        }

        // Geçici bir örnekten kodun ŞU ANKİ varsayılanlarını oku — sabiti
        // burada ikinci kez yazmamak için (sınıf yorumuna bak).
        GameObject scratch = new GameObject("__RevivalDurationSync_scratch__");
        RevivalStation template = scratch.AddComponent<RevivalStation>();
        SerializedObject templateSo = new SerializedObject(template);
        float defaultDuration = templateSo.FindProperty("duration").floatValue;
        float defaultUseDistance = templateSo.FindProperty("useDistance").floatValue;
        float defaultAcceptRadius = templateSo.FindProperty("acceptRadius").floatValue;
        Object.DestroyImmediate(scratch);

        int changed = 0;

        foreach (RevivalStation station in stations)
        {
            SerializedObject so = new SerializedObject(station);
            bool dirty = false;

            dirty |= SyncField(so, "duration", defaultDuration, station);
            dirty |= SyncField(so, "useDistance", defaultUseDistance, station);
            dirty |= SyncField(so, "acceptRadius", defaultAcceptRadius, station);

            if (dirty)
            {
                so.ApplyModifiedProperties();
                changed++;
            }
        }

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }

        Debug.Log($"{stations.Length} diriltme istasyonu tarandı, {changed} tanesinde eski değer " +
            $"güncellendi (kod varsayılanı: duration={defaultDuration}, useDistance={defaultUseDistance}, " +
            $"acceptRadius={defaultAcceptRadius}).");
    }

    private static bool SyncField(SerializedObject so, string propertyName, float codeDefault, RevivalStation station)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null || Mathf.Approximately(prop.floatValue, codeDefault))
            return false;

        Undo.RecordObject(station, "Diriltme Süresini Senkronize Et");
        float old = prop.floatValue;
        prop.floatValue = codeDefault;
        Debug.Log($"'{station.name}': {propertyName} {old} → {codeDefault}", station);
        return true;
    }
}
