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
/// Doğru kurulum: **yatay ve dönüş kök hareketi atılsın** (ikisi de yerinde
/// oynasın), yalnızca dikey poza gömülsün ki yere insinler. İki kök zaten
/// `PlayerBodyVisual.ApplyDeathPose` ile aynı noktaya oturtuluyor.
///
/// ### Yatay referans: ağırlık merkezi, "Original" değil
///
/// Yerinde oynatmak tek başına yetmedi — canavar kurbanın **bir buçuk metre
/// arkasında** diz çöküyordu. Sebep `keepOriginalPositionXZ` ("Based Upon:
/// Original"): klipte yazılı özgün dünya offseti pozun içinde kalıyor. Mixamo'nun
/// eşli takedown'ında iki karakter sahnenin ayrı noktalarında yazılmış, o yüzden
/// aynı köke oturtulsalar bile aralarında o mesafe duruyordu.
///
/// Ağırlık merkezine geçince her klip kendi kökünde ortalanıyor ve ikisi iç içe
/// geçiyor — yakalama koreografisinin istediği de bu.
///
/// **Bu, uzun süre yazılı olan teşhisi çürüttü.** Belgede "klipler gerçek bir
/// Mixamo çifti değil, ayrı ayrı indirilmiş" yazıyordu. Meta dosyaları bunun
/// tersini söylüyor: ikisi de `KillerDollUnity_BaseBody` rig'inde, ikisi de
/// 78 kare, import ayarları birebir aynı. Eşleşen bir çiftti; bozuk olan tek
/// şey bu referans ayarıydı.
///
/// **Dönüşe dokunulmuyor.** Yön zaten doğruydu (CLAUDE.md bölüm 10'daki deneme
/// tablosunda "zıt rotasyon → doğru yön, hâlâ tam oturmuyor"). Bozuk olan
/// mesafeydi.
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

        // Yatay referans ağırlık merkezi, klipte yazılı özgün konum DEĞİL.
        //
        // Aradaki fark bu çiftte 1.5 metre: Mixamo'nun eşli takedown'ında iki
        // karakter sahnenin ayrı noktalarında yazılmış ve "Original" o offseti
        // pozun içinde tutuyor. İki kökü `ApplyDeathPose` ile aynı noktaya
        // oturtsan bile karakterler o kadar ayrı duruyordu — canavar kurbanın
        // bir buçuk metre arkasında diz çöküyordu.
        //
        // Ağırlık merkezine geçince her klip kendi kökünde ortalanıyor ve ikisi
        // iç içe geçiyor; yakalama koreografisinin istediği de bu.
        //
        // **Dönüşe dokunulmuyor** (`keepOriginalOrientation`). Yön zaten
        // doğruydu — CLAUDE.md'deki deneme tablosunda "zıt rotasyon → doğru
        // yön, hâlâ tam oturmuyor" satırı bunu söylüyor. Bozuk olan tek şey
        // mesafeydi.
        if (take.keepOriginalPositionXZ)
        {
            take.keepOriginalPositionXZ = false;
            changed = true;
        }

        return changed;
    }
}
