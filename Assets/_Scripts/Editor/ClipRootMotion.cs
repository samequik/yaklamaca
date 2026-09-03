using UnityEditor;

/// <summary>
/// Tek atımlık (yere yatma, yakalama) kliplerin **dikey** kök hareketini poza
/// gömer. `MonsterSetup` ve `RunnerSetup` kullanıyor.
///
/// ### Neden gerekli
///
/// Oyuncunun hareketini `PlayerController` veriyor, o yüzden Animator'da
/// `applyRootMotion = false`. Bu ayar kök hareketini **çıkarıp atıyor** — yürüme
/// ve koşmada istediğimiz tam olarak bu, yoksa karakter animasyonun kendi
/// ilerlemesiyle kayardı.
///
/// Ama yakalama/ölme çifti yere kök hareketiyle iniyor. O hareket atılınca
/// karakterler yatma pozunu oynatıp **ayakta durdukları yükseklikte kalıyor** —
/// havada yatıyor gibi görünüyorlar.
///
/// ### Yalnızca DİKEY gömülüyor
///
/// İlk denemede yatay (XZ) ve dönüş de gömüldü. Sonuç daha kötüydü: canavarın
/// atılışı artık kökten bağımsız olarak mesh'i taşıyordu, canavar ileri uçup
/// kurbandan ayrılıyordu.
///
/// Sebep, elimizdeki kliplerin **gerçek bir Mixamo çifti olmaması**: canavarın
/// klibi ve kurbanınki ayrı ayrı indirilmiş, ortak bir origin'e göre yazılmamış.
/// Her birinin kendi yatay yer değiştirmesi var ve ikisi birbirini tutmuyor.
///
/// Doğru kurulum: **yatay ve dönüş kök hareketi atılsın** (ikisi de yerinde
/// oynasın), yalnızca dikey poza gömülsün ki yere insinler. İki kök zaten
/// `PlayerBodyVisual.ApplyDeathPose` ile aynı noktaya oturtuluyor.
///
/// XZ ve dönüş **açıkça kapatılıyor**, sadece atlanmıyor: önceki sürüm onları
/// açmıştı ve kırpma gibi bu ayar da import dosyasında kalıcı.
///
/// Alan adları serileştirmede farklı görünüyor (`lockRootHeightY` →
/// `loopBlendPositionY`); meta dosyasından doğrulandı.
/// </summary>
public static class ClipRootMotion
{
    /// <summary>Değişiklik yapıldıysa true — çağıran yeniden import etsin.</summary>
    public static bool BakeVerticalIntoPose(ref ModelImporterClipAnimation take)
    {
        bool changed = false;

        // Dikey: poza göm, referans Original. Karakter yere insin.
        if (!take.lockRootHeightY)
        {
            take.lockRootHeightY = true;
            changed = true;
        }

        if (!take.keepOriginalPositionY)
        {
            take.keepOriginalPositionY = true;
            changed = true;
        }

        // Ayak referansı yatan bir karakterde gövdeyi yerden kaldırır.
        if (take.heightFromFeet)
        {
            take.heightFromFeet = false;
            changed = true;
        }

        // Yatay ve dönüş: kök hareketi olarak ATILSIN. Gömülürse karakter
        // kökten uzaklaşıp eşinden ayrılıyor.
        if (take.lockRootPositionXZ)
        {
            take.lockRootPositionXZ = false;
            changed = true;
        }

        if (take.lockRootRotation)
        {
            take.lockRootRotation = false;
            changed = true;
        }

        return changed;
    }
}
