using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bir animasyon FBX'inin "alım" (take) listesini GÜVENLE okur.
/// `MonsterSetup` ve `RunnerSetup` kullanıyor.
///
/// ### Neden gerekti — sessizce hiçbir şey yapmayan bir onarım (2026-09-23)
///
/// İki kurulum aracı da klip ayarlarını (döngü, kırpma, kök hareketi) şu
/// desenle yazıyor:
///
/// ```csharp
/// var defaults = importer.defaultClipAnimations;
/// var takes    = importer.clipAnimations;
/// if (takes == null || takes.Length != defaults.Length) takes = defaults;
/// for (int i = 0; i < takes.Length; i++) { ...ayarla... }
/// ```
///
/// **`defaultClipAnimations` son BAŞARILI import'u yansıtıyor**, o an
/// `importer` üstünde bekleyen ayarları değil. Rig bozuk bir dosyada liste
/// BOŞ dönüyor — ve o zaman döngü hiç çalışmıyor, hiçbir ayar yazılmıyor,
/// araç "başarılı" diyip çıkıyor.
///
/// Gerçekte olan: 2026-09-20'de `MonsterSetup` ödünç klasörünü (kaçanın
/// klasörünü) domuz katilin avatarıyla yeniden import etmiş ve rig'i
/// bozmuştu (`Transform 'mixamorig:Hips' for human bone 'Hips' not found`).
/// Kod tarafı aynı gün düzeltildi (`LoadClips` artık salt okunur) ve
/// kullanıcı onarım için `Kaçan Modelini Kur`'u çalıştırdı — **ama araç
/// onarmadı.** Avatarı doğru değere geri yazdı, sonra stale boş listeyi
/// okudu ve klip ayarlarını hiç yazmadı.
///
/// Sonuç üç gün fark edilmedi: kaçanın `Walking` klibi `loopTime: 1`'i
/// kaybetmişti, yani 31 kare (≈1 saniye) oynayıp **donuyordu**. Oynayan
/// bunu "yürüyorum, bir saniye sonra animasyon olduğu yerde duruyor" diye
/// görüyor.
///
/// ### Çözüm: boş liste görünce ÖNCE import et
///
/// Liste boşsa bekleyen ayarlar (düzeltilmiş avatar dahil) kaydedilip dosya
/// yeniden import ediliyor, sonra liste TEKRAR okunuyor. Rig o anda
/// onarıldığı için alım artık görünüyor ve çağıran normal yoluna devam
/// ediyor — araç gerçekten kendi kendini onarıyor.
///
/// Hâlâ boşsa **konsola yazılıyor.** Sessiz kalması bu hatanın üç gün
/// yaşamasının tek sebebiydi.
/// </summary>
public static class ClipTakes
{
    /// <summary>
    /// Dosyanın alım listesi. Boş dönerse dosyada içe aktarılabilir bir
    /// animasyon yok demektir ve konsola sebebi yazılmıştır.
    ///
    /// **Çağırmadan önce** `animationType` / `avatarSetup` / `sourceAvatar`
    /// gibi rig ayarları `importer` üstüne yazılmış olmalı: onarım turu tam
    /// da o bekleyen ayarları uyguluyor.
    /// </summary>
    public static ModelImporterClipAnimation[] Resolve(ModelImporter importer, string path)
    {
        if (importer == null)
            return new ModelImporterClipAnimation[0];

        ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;

        if (takes.Length > 0)
            return takes;

        // Boş: büyük ihtimalle son import başarısız oldu. Bekleyen rig
        // ayarlarını uygulayıp bir daha bak.
        importer.SaveAndReimport();
        takes = importer.defaultClipAnimations;

        if (takes.Length > 0)
        {
            Debug.Log($"{Path.GetFileName(path)}: alım listesi boştu, yeniden " +
                "içe aktarıldı ve düzeldi. (Rig ayarı bozuk kalmış olabilir — " +
                "klip ayarları bu turda yazılıyor.)");

            return takes;
        }

        Debug.LogWarning($"{Path.GetFileName(path)}: içe aktarılabilir animasyon " +
            "ALIMI yok, klip ayarları (döngü, kırpma) YAZILAMADI. Dosyanın " +
            "Inspector'ında Rig sekmesine bak — 'Copied Avatar Rig Configuration " +
            "mis-match' hatası varsa kaynak avatar bu iskelete uymuyor demektir. " +
            "Döngü bayrağı yazılmadan klip bir kez oynayıp DONAR.");

        return takes;
    }
}
