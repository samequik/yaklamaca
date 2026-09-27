using UnityEngine;

/// <summary>
/// Seçilebilir karakterlerin listesi: kaçan kostümleri ve canavar kostümleri.
///
/// ### Kostüm bir MODEL, renk değil
///
/// İlk sürüm aynı modelin renk çeşitlemelerini listeliyordu. Oynanınca
/// istenmedi: kostümler gerçek modeller olacak. Renk yolu tamamen kaldırıldı.
///
/// ### Liste hem ÇALIŞMA ANININ hem ARACIN tek kaynağı
///
/// `ModelPath` yalnızca editör aracının (`Kaçan Modelini Kur`) okuduğu bir
/// alan; çalışma anında kimse ona bakmıyor. Yine de burada duruyor, çünkü
/// alternatifi listeyi iki yerde tutmaktı: araçta modeller, burada adlar. İki
/// liste er ya da geç birbirini tutmaz (bölüm 5) — ad eklenir model eklenmez
/// ve seçim ekranı var olmayan bir gövdeyi açmaya çalışır.
///
/// **Kostümün indeksi gövdenin indeksi.** `PlayerBodyVisual`'ın gövde dizisi
/// bu listeyle aynı sırada kuruluyor, yani ayrı bir "hangi gövde" alanı yok —
/// olsaydı senkronlanması gereken üçüncü bir sayı olurdu.
///
/// ### Kostüm YALNIZCA görsel
///
/// Bölüm 17'nin ölçek kuralı gereği her kaçan gövdesi **hull boyuna**
/// normalleniyor (`RunnerSetup.ResolveScale`): görünen gövde çarpışma
/// kutusuyla örtüşmeli, yoksa isabet etmesi gereken vuruşlar ıskalar. Model
/// kendi içinde kısa ya da uzun olabilir, ekrandaki boyu aynı.
///
/// ### Canavar kostümü oynanış da TAŞIYABİLİR — kaçanınki taşımaz
///
/// Burada bir süre "ikinci canavar bu listeye AİT DEĞİL, o bir kostüm değil
/// oynanış" yazıyordu. 2026-09-20'de domuz katil gelince bu ayrım çöktü: o
/// hem farklı bir model hem farklı bir oynanış (yakalama koreografisi yok,
/// kurban vurulduğu yerde uçuyor) ve ikisini ayrı listelerde tutmak aynı
/// şeyi iki yerde senkronlamak olurdu.
///
/// O yüzden `Costume` artık canavara özel birkaç oynanış bayrağı taşıyor.
/// **Kaçan kostümleri hâlâ yalnızca görsel** ve öyle kalmalı: canavar onlara
/// nişan alıyor, oynanışı değişen bir kaçan isabet dengesini bozar.
/// </summary>
public static class CharacterCatalog
{
    /// <summary>Tek bir kostüm.</summary>
    public readonly struct Costume
    {
        /// <summary>Seçim ekranında görünen ad.</summary>
        public readonly string Name;

        /// <summary>
        /// Modelin proje içindeki yolu. **Yalnızca editör aracı okuyor.**
        /// Çalışma anında gövdeler prefabta hazır duruyor, dosya yoluna
        /// ihtiyaç yok.
        /// </summary>
        public readonly string ModelPath;

        /// <summary>
        /// Bu kostümün KENDİ animasyon klasörü; boşsa ortak klasör kullanılıyor.
        ///
        /// Kostümün kendi klipleri ortağın ÜSTÜNE yazılıyor, onun yerine
        /// geçmiyor: modelin kendi yürüyüşü varsa o oynuyor, yoksa ortak
        /// klipler dolduruyor. Humanoid klipler kas uzayında saklandığı için
        /// ortak olanlar her iskelette çalışıyor (bölüm 17).
        ///
        /// **Ölüm klibi bilerek ortaktan geliyor**: yakalanma koreografisi
        /// canavarın klibiyle iç içe geçmek zorunda (bölüm 17), modelin kendi
        /// "yenildim" klibi oraya oturmuyor.
        /// </summary>
        public readonly string AnimationFolder;

        /// <summary>
        /// **Yalnızca canavar kostümlerinde.** Doğruysa bu canavarın yakalama
        /// koreografisi YOK: kurban özel bir poza alınmıyor, vuruş anında
        /// olduğu yerde ragdoll olup canavarın baktığı yöne fırlıyor.
        ///
        /// Klibin var olup olmamasına bakmak daha "otomatik" olurdu ama sessiz
        /// olurdu: eksik bir klip yanlışlıkla oynanış değiştirirdi ve kimse
        /// sebebini göremezdi. Açık bir bayrak, niyeti okunur kılıyor.
        ///
        /// **Denge notu:** yakalama klibi bilerek bir bedeldi — canavar 2.6 sn
        /// meşgulken diğer kaçanlara pencere açılıyordu (bölüm 14). Bu bayrak
        /// o pencereyi kapatıyor, yani böyle bir canavar belirgin biçimde
        /// güçlü. Karşılığı vuruş sonrası yavaşlamadan verilmeli.
        /// </summary>
        public readonly bool LaunchesVictim;

        /// <summary>
        /// **Yalnızca canavar kostümlerinde.** Doğruysa `Canavar Modelini Kur`
        /// bu gövdenin sağ eline prosedürel bir beyzbol sopası kuruyor
        /// (`MonsterBatBuilder`). Sopa TAMAMEN GÖRSEL: menzil, hasar ve isabet
        /// kararı sunucunun ışınında kalıyor (bölüm 4), collider'ı yok.
        /// </summary>
        public readonly bool CarriesBat;

        /// <summary>
        /// KENDİ klasöründe olmayıp **kaçanın** klasöründen alınacak roller.
        ///
        /// ### Neden açık liste, neden otomatik değil
        ///
        /// İlk sürüm "eksik rolü nerede bulursan doldur" diye bir zincir
        /// kuruyordu: kendi klasörü → ortak set → ödünç klasörü. Oynanınca
        /// üç klip birden sessizce sızdı — domuz katil KUKLA'nın `ıskalama`,
        /// `kill` ve `Running Crawl` kliplerini devraldı. Oysa ilk ikisinin
        /// HİÇ olmaması gerekiyordu (yakalama koreografisi yok) ve üçüncüsü
        /// kaçandan gelmeliydi.
        ///
        /// Doğru kural: **kendi animasyon klasörü olan bir kostüm hiçbir şeyi
        /// sessizce miras almaz.** Ya kendi klibi vardır, ya burada açıkça
        /// ödünç yazılmıştır, ya da o rol YOKTUR — ve olmayan rol için
        /// animatör durumu hiç kurulmaz.
        ///
        /// Ders bu projenin kendi dersi (bölüm 13): "eksikse bir yerden bul"
        /// mantığı yanlış klibi sessizce yakalıyor.
        /// </summary>
        public readonly string[] BorrowedRoles;

        /// <summary>
        /// Gövdenin yürürken/koşarken çevrileceği açı (derece). Sıfır =
        /// dokunma.
        ///
        /// Sebebi: bazı paketlerin yürüme/koşma klipleri "elinde bir şey
        /// tutarak" yazılmış oluyor ve gövde gidiş yönüne göre yan duruyor.
        /// Domuz katilde tam bu var — sopalı yürüyüş ve koşu yana bakıyor,
        /// boşta durma ve saldırı klipleri düzgün.
        ///
        /// ### YALNIZCA BAŞKALARININ ekranında uygulanıyor
        ///
        /// İlk sürüm açıyı klibin import ayarına GÖMÜYORDU (`rotationOffset`).
        /// O zaman açı herkeste aynı: canavarı oynayan kendi gövdesini de
        /// yamuk görüyordu ve birinci şahısta bu rahatsız ediyor.
        ///
        /// Açı artık `PlayerBodyVisual` tarafından çalışma anında gövde
        /// köküne yazılıyor ve **birinci şahısta sıfır**. Gövde kökü ağda hiç
        /// yok (bölüm 17), yani bu tamamen yerel bir görüntü — ek trafik yok
        /// ve her istemci "bu gövde benim mi" sorusunu kendi cevaplıyor.
        ///
        /// Yalnızca yürüme/koşma sırasında devreye giriyor
        /// (`CharacterAnimatorBase.LocomotionBlend` ile çarpılıyor): boşta
        /// durma, eğilme ve saldırı klipleri düzgün duruyor, onlara
        /// dokunulmamalı.
        /// </summary>
        public readonly float LocomotionYawOffset;

        /// <summary>
        /// Ayaktaki göz hizasına EK pay (Source unit; 1 unit = 1.905 cm).
        /// `MovementProfile.eyeHeightOffset`'in ÜSTÜNE biniyor.
        ///
        /// Neden kostüm başına: profildeki pay ROLE bağlı, yani bütün canavar
        /// kostümleri için aynı. Her modelin kafası hull'a göre farklı
        /// yükseklikte oturuyor — KUKLA'ya göre ayarlanmış tek bir sayı domuz
        /// katilde kamerayı kafanın altında bırakıyordu.
        /// </summary>
        /// <summary>
        /// Savurmayla isabet arasındaki gecikme (saniye). Sıfır = eski
        /// davranış, isabet savurma başlar başlamaz aranıyor.
        ///
        /// Her canavarın saldırı klibinde darbe farklı anda düşüyor: KUKLA
        /// hemen atılıp yumrukluyor, domuz katil sopayı önce kaldırıp sonra
        /// indiriyor (klip 2.4 sn). Gecikme olmadan kurban sopa daha inmeden
        /// uçarak ölüyordu.
        ///
        /// **Pencere KISALMIYOR, KAYIYOR.** `MonsterAttack.swingDuration`
        /// yalnızca 0.22 sn; gecikmeyi onun İÇİNDEN almak isabeti neredeyse
        /// imkânsız yapardı. Savurmanın toplam süresi gecikme kadar uzuyor ve
        /// isabet penceresi sonuna kayıyor.
        /// </summary>
        public readonly float HitWindup;

        public readonly float EyeHeightOffset;

        /// <summary>
        /// EĞİLMİŞ göz hizasına ek pay (Source unit).
        ///
        /// Ayrı bir alan, çünkü `PlayerController` payı yalnızca AYAKTAKİ
        /// hizaya ekliyordu: eğilince kamera herkes için 28 unit'e düşüyor ve
        /// 1.30 ölçekli bir canavarın kafası orada kameranın üstünde kalıyor.
        ///
        /// **Fazla büyütmek tavanı gösterir.** Eğilme geçidi 1.1 m, eğilmiş
        /// göz hizası 28 unit = 0.533 m. Buradaki pay o farkı yemeyecek kadar
        /// küçük kalmalı (bölüm 14'ün "sınırı tavan belirliyor" notu).
        /// </summary>
        public readonly float DuckedEyeHeightOffset;

        /// <summary>
        /// Bu kostümün saldırı seslerinin ad öneki. Boş = ortak sesi kullan.
        ///
        /// Araç (`Sesleri Yerleştir`) `{önek}_Savurma` ve `{önek}_Isabet`
        /// dosyalarını arayıp `MonsterAttack`'in kostüm dizilerine yazıyor.
        ///
        /// **Neden kostüm başına:** KUKLA elle saldırıyor, domuz katil sopa
        /// savuruyor. Tek bir ortak ses ikisine birden konulursa biri her
        /// zaman yanlış duyuluyor — sopa sesi elle vuran bir canavarda,
        /// ya da tersi.
        ///
        /// Önek boşsa ya da dosya bulunamazsa `MonsterAttack` ortak klibe
        /// (`Bicak_Savurma` / `Bicak_Isabet`) düşüyor: yeni bir kostüm ses
        /// getirmeden de sessiz kalmıyor.
        /// </summary>
        public readonly string AttackSoundPrefix;

        public Costume(string name, string modelPath, string animationFolder = null,
            bool launchesVictim = false, bool carriesBat = false,
            string[] borrowedRoles = null, float locomotionYawOffset = 0f,
            float eyeHeightOffset = 0f, float duckedEyeHeightOffset = 0f,
            float hitWindup = 0f, string attackSoundPrefix = null)
        {
            LocomotionYawOffset = locomotionYawOffset;
            HitWindup = hitWindup;
            EyeHeightOffset = eyeHeightOffset;
            DuckedEyeHeightOffset = duckedEyeHeightOffset;
            AttackSoundPrefix = attackSoundPrefix;
            Name = name;
            ModelPath = modelPath;
            AnimationFolder = animationFolder;
            LaunchesVictim = launchesVictim;
            CarriesBat = carriesBat;
            BorrowedRoles = borrowedRoles ?? new string[0];
        }
    }

    /// <summary>
    /// Kaçan kostümleri. Sıralama ÖNEMLİ: `PlayerBodyVisual`'ın gövde dizisi
    /// ve cesedin gövde çeşitleri aynı indeksi kullanıyor.
    /// </summary>
    public static readonly Costume[] Runners =
    {
        new Costume("Banana Man",
            "Assets/Plugins/Banana Yellow Games/Characters/Banana Man/Banana Man.fbx"),

        // Kendi animasyon klasörü BİLEREK verilmiyor (2026-09-12, ikinci
        // tur): kendi paketi dört ayrı hataya sebep oldu (T-poz, yanlış rol
        // eşleşmesi, asılı tetik, döngü olmayan zıplama klibi) ve oyun
        // yayına yaklaşırken kararlı olan tercih edildi. Muz adamla aynı
        // paylaşılan Mixamo setini kullanıyor — humanoid klipler kas
        // uzayında olduğu için sorunsuz oynuyor (bölüm 17). Kendi paketi
        // sonraki bir cilalama turunda geri getirilebilir.
        new Costume("Unity-chan",
            "Assets/unity-chan!/Unity-chan! Model/Art/Models/unitychan.fbx"),
    };

    /// <summary>
    /// Canavar kostümleri. Rolü sunucu dağıtıyor (bölüm 11.1), yani bu seçim
    /// "canavar olursam neye benzeyeceğim" demek — seçen kişiyi canavar
    /// yapmıyor.
    /// </summary>
    public static readonly Costume[] Monsters =
    {
        new Costume("The Marionette",
            "Assets/RamsterZ_FreeDoll/Art/Models/KillerDollUnity_BaseBody.fbx"),

        // Domuz katil (2026-09-20). Kendi klasöründe BEŞ klip var; eksik
        // kalan tek rol eğilerek yürüme ve o KAÇANIN klibinden ödünç
        // alınıyor (`MonsterSetup.BorrowedClips`) — humanoid klipler kas
        // uzayında saklandığı için başka bir iskelette yazılmış olması
        // sorun değil (bölüm 17).
        //
        // Iskalama ve yakalama klipleri BİLEREK yok: ıskalayınca doğrudan
        // yürüyüşe dönüyor, yakalayınca da kurban uçuyor (`LaunchesVictim`).
        new Costume("The Pig K.",
            "Assets/_Art/Models/domuz katil/Character_Killer_05.fbx",
            "Assets/_Art/Models/domuz katil/katil domuz animasyon",
            launchesVictim: true,
            carriesBat: true,
            // Kendi klasöründe eğilerek yürüme YOK; kullanıcının isteği bunu
            // KAÇANIN klibinden almak. Listede olmayan hiçbir rol başka bir
            // yerden doldurulmuyor — `ıskalama` ve `kill` bu yüzden hiç yok.
            borrowedRoles: new[] { "running crawl" },
            // **Gövde açısı SIFIR — 2026-09-23'te kaldırıldı.**
            //
            // Bir süre -30 idi. Sebebi eski `Standing Run Forward` /
            // `Standing Walk Forward` kliplerinin "sopayı tutarak" yazılmış
            // olması ve gövdeyi gidiş yönüne göre yan tutmasıydı; açı o
            // yamukluğu telafi ediyordu. (İlk deneme +30'du ve TERS tarafa
            // döndürdü — doğrusu eksi yöndü.)
            //
            // Koşma klibi `Sword And Shield Run` ile değişti ve kullanıcı
            // "dümdüz, normal olsun" dedi: telafi edilecek bir yamukluk
            // kalmadı, telafi de kalkıyor.
            //
            // **Mekanizma duruyor, yalnızca bu kostümde kapalı.**
            // `PlayerBodyVisual.LateUpdate` açıyı hâlâ okuyor ve sıfırda
            // hiçbir şey yapmıyor. Kodu sökmek, aynı sorunu yaşayan bir
            // sonraki kostümde her şeyi baştan yazmak olurdu — üstelik
            // çözümün NEDEN iki aşamada (önce klibe gömme, sonra çalışma
            // anına taşıma) oturduğu da kaybolurdu.
            locomotionYawOffset: 0f,
            // Kendi sesleri: `Sopa_Savurma` (her savuruşta) ve `Sopa_Isabet`
            // (yalnızca gerçek isabette — jumpscare). KUKLA ortak
            // `Bicak_*` seslerinde kalıyor.
            attackSoundPrefix: "Sopa",
            // Kamera domuzun kafasına oturmuyordu. Ayaktaki pay profilden
            // gelen +6'nın ÜSTÜNE, eğilmedeki ise sıfırın üstüne biniyor.
            //
            // İlk deneme (4/6) hâlâ alçak kaldı. Eğilmedeki paya bir TAVAN
            // var: eğilmiş hull 36 unit ve kamera onun üstüne çıkarsa
            // çarpışma kutusunun dışında kalır — alçak bir geçitte hull
            // sığarken kameranın tavana girmesi demek. 12 pay ile hiza 40
            // unit (0.762 m) ve eğilme geçidinin (1.1 m) altında 0.34 m
            // boşluk kalıyor, yani bugünkü haritada güvenli.
            eyeHeightOffset: 11f,
            duckedEyeHeightOffset: 12f,
            // Sopa klibin başında değil ortasında iniyor; isabet o ana
            // kaydırılıyor. İlk deneme 0.5 sn'ydi ve hâlâ erkendi — klip
            // 2.4 sn, sopa gerçekten geç iniyor. Kurban erken/geç uçuyorsa
            // tek sayı bu.
            hitWindup: 1.0f),
    };

    /// <summary>
    /// Geçersiz indeksi sıfıra çeviriyor, kırpmıyor.
    ///
    /// Kırpmak (`Clamp`) yanlış olurdu: liste kısalırsa son kostümü seçmiş
    /// herkes sessizce yeni son kostüme kayardı. Sıfıra düşmek "seçimin artık
    /// yok, varsayılandasın" demek ve oyuncu bunu ekranda görüyor.
    /// </summary>
    public static int Sanitize(Costume[] list, int index) =>
        list != null && index >= 0 && index < list.Length ? index : 0;

    public static int SanitizeRunner(int index) => Sanitize(Runners, index);

    public static int SanitizeMonster(int index) => Sanitize(Monsters, index);

    public static Costume Runner(int index) => Runners[SanitizeRunner(index)];

    public static Costume Monster(int index) => Monsters[SanitizeMonster(index)];

    /// <summary>Listede ileri/geri dolaşır; uçlarda başa sarıyor.</summary>
    public static int Step(Costume[] list, int index, int delta)
    {
        if (list == null || list.Length == 0)
            return 0;

        int count = list.Length;
        return ((Sanitize(list, index) + delta) % count + count) % count;
    }
}
