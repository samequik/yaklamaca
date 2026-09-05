using UnityEditor;
using UnityEngine;

/// <summary>
/// Hareket profillerine tavsiye edilen başlangıç değerlerini yazar.
///
/// **Neden bir araç?** `MovementProfile`'a yeni bir alan eklendiğinde var olan
/// `.asset` dosyaları o alanı C# varsayılanıyla alıyor. Canavarın zıplamaması
/// gerekiyor ama `canJump`'ın varsayılanı `true` — yani kod değişikliği tek
/// başına hiçbir şey yapmıyor, birinin gidip kutuyu açması gerekiyor. Altı
/// sayıyı elle girmek yerine buradan yazılıyor.
///
/// **Bu bir sıfırlama.** Oynayarak ayarladığın değerlerin üstüne yazar; onay
/// soruyor. Sayılar tahmin, ölçüm değil (CLAUDE.md bölüm 10: denge oynanarak
/// ölçülmeli) — buradaki tek iddia "başlamak için makul".
///
/// Menü: Yakalamaca > Hareket Profillerini Sıfırla
/// </summary>
public static class MovementProfileSetup
{
    private const string MonsterPath = "Assets/_ScriptableObjects/CanavarProfili.asset";
    private const string RunnerPath = "Assets/_ScriptableObjects/KacanProfili.asset";

    [MenuItem("Yakalamaca/Hareket Profillerini Sıfırla (canavar = araba modeli)")]
    private static void Reset()
    {
        MovementProfile monster = AssetDatabase.LoadAssetAtPath<MovementProfile>(MonsterPath);
        MovementProfile runner = AssetDatabase.LoadAssetAtPath<MovementProfile>(RunnerPath);

        if (monster == null || runner == null)
        {
            EditorUtility.DisplayDialog("Profil bulunamadı",
                $"Şunlar bekleniyordu:\n{MonsterPath}\n{RunnerPath}", "Tamam");
            return;
        }

        if (!EditorUtility.DisplayDialog("Hareket profillerini sıfırla",
                "Canavar ve kaçan profillerindeki tüm değerlerin üstüne tavsiye edilen " +
                "başlangıç değerleri yazılacak.\n\nOynayarak ayarladığın sayılar varsa " +
                "kaybolur.", "Yaz", "Vazgeç"))
            return;

        ApplyRunner(runner);
        ApplyMonster(monster);

        AssetDatabase.SaveAssets();
        Selection.activeObject = monster;

        Debug.Log(
            "Hareket profilleri yazıldı.\n\n" +
            "KAÇAN — Source modeli, değişmedi: 200/400 u/s, ivme 14, sürtünme 5.5, " +
            "zıplama açık, çarpışma cezası yok.\n\n" +
            "CANAVAR — taban hız 420 u/s ve ivmesi kaçanla AYNI, yani duruştan " +
            "kalkarken ağır değil. Farkı hız payı: 3.5 saniye kesintisiz koşarsa " +
            "+180 u/s açılıyor (600'e çıkıyor). Koşu kesilince 1.2 saniyede " +
            "boşalıyor. ZIPLAMA KAPALI. Duvara 280 u/s'nin üstünde kafa kafaya " +
            "girerse hem hız hem pay sıfırlanıyor.\n\n" +
            "Bu sayılar tahmin. Oynayıp değiştir.");
    }

    /// <summary>Kaçan: Source modeli, olduğu gibi kalıyor.</summary>
    private static void ApplyRunner(MovementProfile profile)
    {
        Undo.RecordObject(profile, "Hareket Profilleri");

        profile.walkSpeed = 200f;
        profile.sprintSpeed = 400f;
        profile.crouchSpeedMultiplier = 0.35f;
        profile.slideBoost = 60f;
        profile.slideFriction = 1.2f;

        profile.accelerate = 14f;
        profile.friction = 5.5f;
        profile.airAccelerate = 100f;

        profile.canJump = true;

        // Kamera hull'un göz hizasında ve eksende: kaçanın modeli (Banana Man)
        // hull'la aynı ölçekte, yani pay gerekmiyor.
        profile.eyeHeightOffset = 0f;
        profile.eyeForwardOffset = 0f;

        // Aşağı bakış 70'e daraltıldı (2026-09-05). Burada uzun süre 89 vardı
        // ve gerekçesi "kaçan hâlâ kapsül, dönecek kafa yok"tu — o gerekçe
        // 2026-08-30'da model bağlanınca düştü ama sayı kalmıştı. Tam dibe
        // bakınca kendi gövdenin içi görünüyordu.
        //
        // Önce 75 denendi, oynanışta hâlâ fazla geldi. Canavarınki kadar dar
        // DEĞİL (55): kaçanın kafası bakış yönüne dönmüyor, sorun yalnızca en
        // alttaki birkaç derece.
        profile.maxLookDownAngle = 70f;

        // Kaçanda çarpışma cezası yok: bhop ve airstrafe onun aracı, duvara
        // sürterek köşe dönmek de öyle. Ceza koymak Source hissini öldürürdü.
        profile.crashSpeed = 0f;
        profile.crashSpeedRetained = 0f;

        // Direksiyon cezası da yok. Zaten etkisiz olurdu — ceza payı siliyor,
        // kaçanın payı yok — ama sıfır yazmak niyeti Inspector'da da gösteriyor.
        profile.steerScrubTime = 0f;

        EditorUtility.SetDirty(profile);
    }

    /// <summary>Canavar: araba modeli.</summary>
    private static void ApplyMonster(MovementProfile profile)
    {
        Undo.RecordObject(profile, "Hareket Profilleri");

        profile.walkSpeed = 200f;

        // Taban hız kaçandan %5 DÜŞÜK (400 → 380). Canavar koşuya kaçandan
        // geride başlıyor; farkı kapatan şey aşağıdaki hız payı.
        //
        // Eskiden 420'ydi, yani canavar baştan hızlıydı ve pay yalnızca üstüne
        // ekliyordu. Böyle olunca kovalamacanın başı da sonu da canavarın
        // lehineydi. Şimdi ilk saniyeler kaçanın: kaçmak istiyorsa köşeyi
        // dönüp koşuyu kesmeli, yoksa pay dolar ve canavar geçer.
        profile.sprintSpeed = 380f;

        profile.crouchSpeedMultiplier = 0.35f;
        profile.slideBoost = 60f;
        profile.slideFriction = 1.2f;

        // İvme ve sürtünme KAÇANLA AYNI. İlk denemede bunları düşürmüştüm ve
        // yanlıştı: canavar duruştan kalkarken de ağırlaşıyordu, her yavaşlama
        // bir cezaya dönüşüyordu. Araba da duruştan seyir hızına çabuk çıkar;
        // yavaş olan kısım SON hıza varmaktır.
        profile.accelerate = 14f;
        profile.friction = 5.5f;
        profile.airAccelerate = 100f;

        profile.canJump = false;

        // Kamera yukarı ve ileri alınıyor (2026-09-05). Canavarın modeli
        // hull'un 1.18 katı (bölüm 17): hull'un göz hizasında duran kamera
        // modelin göğüs hizasına denk geliyor ve oynayınca "kamera gövdenin
        // içinde" gibi duruyordu.
        //
        // 6 unit (0.114 m) yukarı: kamera kapsül tepesinin 3.8 cm altında
        // kalıyor. Sınırı TAVAN belirliyor, duvar değil — düşey bir duvar
        // kapsüle en geniş yerinden değdiği için kameranın duvara uzaklığı
        // yükseklikten bağımsız. Bu haritada tavan 3 m, yani pay bol.
        //
        // 0.10 m ileri: kamerayı göğsün içinden çıkarıyor. Duvara girmesi
        // mümkün değil, UpdateCameraClearance payı yüzeye çarptırıyor.
        profile.eyeHeightOffset = 6f;
        profile.eyeForwardOffset = 0.10f;

        // Kafası bakış yönüne dönüyor (MonsterAnimator bakış IK'sı). Tam dibe
        // bakınca kafa gövdenin içine giriyordu; aşağısı daraltıldı.
        profile.maxLookDownAngle = 55f;

        // Asıl silah: kesintisiz koştukça açılan pay. 380 → 560 u/s.
        // Kaçan 400'de sabit, yani canavar başta GERİDE ama koşuyu
        // sürdürebilirse geçiyor — ve her köşe onu başa döndürüyor.
        profile.boostSpeed = 180f;
        profile.boostBuildTime = 3.5f;
        profile.boostDecayTime = 1.2f;

        // Eşik taban hızın ALTINDA olmalı. 380'de bırakılsaydı canavarın
        // tavanı tam eşiğe oturur ve sürtünme/ivme salınımı yüzünden pay ya
        // hiç dolmaz ya da kesik kesik dolardı — "sonradan hızlanır" fikri
        // sessizce çalışmazdı. 340, yürümenin (200) belirgin üstünde, yani
        // yürüyerek sinsice pay depolamak hâlâ mümkün değil.
        profile.boostMinSpeed = 340f;

        // Direksiyon: köşe dönmek payı siliyor.
        //
        // Bölüm 1 "her köşe onu başa döndürüyor" diyordu ama kodda karşılığı
        // YOKTU: pay `koşuyor && yerde && hız ≥ eşik` iken doluyor ve bu üç
        // şart dönerken de sağlanıyordu. Canavar tam hızla 90° dönüp hiçbir şey
        // kaybetmiyordu — oynayanların "canavar çok güçlü" demesinin sebebi bu.
        //
        // 25°'ye kadar bedava: koşarken rota düzeltmek ceza olmamalı. 80°'de
        // ceza tam. Süre 0.35 sn ama ceza dönüş SÜRESİNCE birikiyor ve açı hız
        // yeni yöne oturdukça kendiliğinden kapanıyor; pratikte 90°'lik bir
        // köşe payın kabaca üçte birini götürüyor, geri dönüş çok daha fazlasını.
        profile.steerFreeAngle = 25f;
        profile.steerFullAngle = 80f;
        profile.steerScrubTime = 0.35f;
        profile.steerMinSpeed = 340f;

        // Duvara kafa kafaya 280 u/s'nin üstünde girerse tam duruş VE pay sıfır.
        profile.crashSpeed = 280f;
        profile.crashSpeedRetained = 0f;

        EditorUtility.SetDirty(profile);
    }
}
