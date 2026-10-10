using UnityEngine;

/// <summary>
/// Bir perkin ait olduğu SLOT TÜRÜ.
///
/// Slot sayılı değil **türlü** (kullanıcı kararı, 2026-09-28). Bugün tek slot
/// var ve tamamen harita perklerine ait. İleride başka türde perkler gelirse
/// (kullanıcının örneği: "bir tuşa basınca ileri atılma") onlar için AYRI bir
/// slot açılacak — ama o gün geldiğinde bile kimse iki harita perki
/// takamayacak.
///
/// Kategori baştan burada duruyor, sonradan eklenmiyor: kategorisiz düz bir
/// liste, ikinci slot açıldığı gün iki harita perkine izin verir ve o noktada
/// geri almak kayıtlı seçimleri taşımak demek olurdu.
/// </summary>
public enum PerkSlot
{
    /// <summary>Mini haritada ne gördüğünü değiştiren perkler.</summary>
    Harita,
}

/// <summary>
/// Perkin NE YAPTIĞI. Kod bu kimliğe göre dallanıyor, ada ya da indekse
/// değil — ad değişirse oynanış bozulmasın, liste yeniden sıralanırsa
/// `switch` sessizce başka bir perke kaymasın.
/// </summary>
public enum PerkEffect
{
    /// <summary>Slot boş.</summary>
    Yok,

    // ---------- Kaçan ----------

    /// <summary>Bitmemiş terminaller; pencerenin dışında GÖRÜNMÜYOR.</summary>
    KacanTerminaller,

    /// <summary>Kapılar ve eğilme geçitleri; pencerenin dışında GÖRÜNMÜYOR.</summary>
    KacanGecitler,

    /// <summary>Bedenler her zaman, diriltme kabinleri ceset TAŞIRKEN; ikisi de uzaktan.</summary>
    KacanCesetler,

    /// <summary>Katilin yeri — ama katil de onu görüyor. İki taraf da kırpılıyor.</summary>
    KacanKatil,

    // ---------- Katil ----------

    /// <summary>Terminalde hata yapan kaçan, 10 sn, uzaktan da.</summary>
    KatilHataYapan,

    /// <summary>Ceset taşıyan kaçan, taşıdığı sürece; pencerenin dışında GÖRÜNMÜYOR.</summary>
    KatilCesetTasiyan,

    /// <summary>Kapılar ve eğilme geçitleri; pencerenin dışında GÖRÜNMÜYOR.</summary>
    KatilGecitler,

    /// <summary>Bir kaçanı öldürünce ona EN UZAK kaçan, 5 sn, uzaktan da.</summary>
    KatilEnUzakKacan,
}

/// <summary>
/// Seçilebilir perklerin listesi — `CharacterCatalog`'un perk karşılığı ve
/// bilerek onunla aynı desende: iki liste, indeksle seçim, `Sanitize`,
/// `Step`. Aynı işi iki farklı şekilde yapan iki ekran, ikisini de okuyan
/// herkesi yavaşlatırdı.
///
/// ### Tek slot, ROL BAŞINA seçim
///
/// Rolü sunucu dağıtıyor (bölüm 11.1) ve oyuncu hangi rolde olacağını
/// seçemiyor — kostümde olduğu gibi (bölüm 13) iki seçim birden yapılıyor:
/// "kaçan olursam şu perk, katil olursam bu perk". Tek bir liste teknik
/// olarak mümkün değil.
///
/// **Perk ile karakter arasında bağ YOK** (kullanıcı kararı): her perk her
/// karakterle takılabiliyor, "bu perk yalnızca şu katilde çalışır" gibi bir
/// kısıt istenmedi.
///
/// ### Kırpılan mı, kenara yapışan mı — tasarımın asıl ekseni
///
/// Kullanıcı bunu perk perk ayrı ayrı söyledi, yani rastgele değil:
///
/// * **Kenara yapışan** (`EdgeClamped`): yönünü her zaman biliyorsun,
///   mesafeyi bilmiyorsun. Güçlü bilgi.
/// * **Kırpılan**: ancak yaklaşınca öğreniyorsun. Çok daha zayıf.
///
/// Bu yüzden bayrak burada, perkin kendi satırında duruyor — çizen kod
/// (`MiniMapView`) kararı vermiyor, yalnızca uyguluyor. İki yerde tutulan bir
/// kural, biri değişince öbürünün unutulması demek (menü figürlerinin
/// açısında tam olarak bu yaşandı, bölüm 13).
///
/// ### İndeks, kimlik DEĞİL — kostümle aynı takas
///
/// Seçim bir `int` olarak saklanıyor (`PlayerProfile`) ve senkronlanıyor
/// (`RoundParticipant`). Liste yeniden sıralanırsa kayıtlı seçimler kayar;
/// kostüm sistemi 2026-09-12'den beri aynı takası kabul ediyor ve ayrı bir
/// kimlik alanı senkronlanacak üçüncü bir sayı olurdu. **Listeye ekleme
/// SONA yapılmalı**, araya değil.
///
/// Oynanış kararları indekse değil `Effect`'e bakıyor, yani sıralama
/// değişse bile kodun hangi perki çalıştırdığı hiç kaymıyor.
/// </summary>
public static class PerkCatalog
{
    /// <summary>Tek bir perk.</summary>
    public readonly struct Perk
    {
        /// <summary>Seçim ekranında görünen ad.</summary>
        public readonly string Name;

        /// <summary>Tek satırlık açıklama — seçim ekranında adın altında.</summary>
        public readonly string Summary;

        /// <summary>Hangi slota giriyor. Bugün hepsi `Harita`.</summary>
        public readonly PerkSlot Slot;

        /// <summary>Ne yaptığı. Oynanış kodu buna bakıyor.</summary>
        public readonly PerkEffect Effect;

        /// <summary>
        /// İşaret mini haritanın penceresinin DIŞINDA kalınca ne oluyor:
        /// `true` ise yönünü koruyarak kenara yapışıyor, `false` ise hiç
        /// görünmüyor.
        /// </summary>
        public readonly bool EdgeClamped;

        /// <summary>
        /// Tetiklenince kaç saniye görünür kalıyor. **0 = sürekli** (perk
        /// açık olduğu sürece).
        ///
        /// Süreli perkler sunucuda başlamak zorunda: tetikleyen olay
        /// (terminalde hata, öldürme) sunucu kararı ve süreyi istemcide
        /// tutmak, değiştirilmiş bir istemcide süresiz radar demek.
        /// </summary>
        public readonly float Duration;

        public Perk(string name, string summary, PerkSlot slot, PerkEffect effect,
            bool edgeClamped = false, float duration = 0f)
        {
            Name = name;
            Summary = summary;
            Slot = slot;
            Effect = effect;
            EdgeClamped = edgeClamped;
            Duration = duration;
        }

        /// <summary>Slot boş mu.</summary>
        public bool IsEmpty => Effect == PerkEffect.Yok;
    }

    /// <summary>
    /// Kaçanın seçebilecekleri.
    ///
    /// **0 = "Yok" ve bu bilinçli.** Seçim ekranını hiç açmayan oyuncuya
    /// seçmediği bir perk verilmemeli; ayrıca tek kişiyle test ederken
    /// perkli/perksiz karşılaştırmanın tek yolu bu. Kullanıcı "herkes 1 perk
    /// alır" dedi — boş slotu tamamen kaldırmak istenirse bu satırı silmek
    /// yetiyor.
    /// </summary>
    public static readonly Perk[] RunnerPerks =
    {
        new Perk("Perk yok", "Harita slotun boş.",
            PerkSlot.Harita, PerkEffect.Yok),

        new Perk("Terminal Sezgisi",
            "Bitmemiş terminaller mini haritanda — yalnızca pencerenin içinde.",
            PerkSlot.Harita, PerkEffect.KacanTerminaller),

        new Perk("Geçit Sezgisi",
            "Kapılar ve eğilme geçitleri mini haritanda — yalnızca pencerenin içinde.",
            PerkSlot.Harita, PerkEffect.KacanGecitler),

        new Perk("Kurtarıcı",
            "Ölen arkadaşların haritanın her yerinden; ceset taşırken kabinler de.",
            PerkSlot.Harita, PerkEffect.KacanCesetler, edgeClamped: true),

        new Perk("Karşılıklı İz",
            "Katili mini haritanda görüyorsun — ama o da seni görüyor.",
            PerkSlot.Harita, PerkEffect.KacanKatil),
    };

    /// <summary>
    /// Katilin seçebilecekleri.
    ///
    /// **Terminal radarı bu listede YOK, bilerek.** Katil terminalleri,
    /// çıkışları ve alarmı 2026-09-28'den beri perk harcamadan görüyor
    /// (kullanıcı: "perke gerek yok onun için, yoksa çok güçsüz olur").
    /// Buradaki perk onun ÜSTÜNE geliyor.
    /// </summary>
    public static readonly Perk[] MonsterPerks =
    {
        new Perk("Perk yok", "Harita slotun boş.",
            PerkSlot.Harita, PerkEffect.Yok),

        new Perk("Alarm Takibi",
            "Terminalde hata yapan kaçanın yeri 10 saniye görünüyor.",
            PerkSlot.Harita, PerkEffect.KatilHataYapan, edgeClamped: true, duration: 10f),

        new Perk("Ceset Takibi",
            "Ceset taşıyan kaçan mini haritanda — bıraktığı an kayboluyor.",
            PerkSlot.Harita, PerkEffect.KatilCesetTasiyan),

        new Perk("Geçit Sezgisi",
            "Kapılar ve eğilme geçitleri mini haritanda — yalnızca pencerenin içinde.",
            PerkSlot.Harita, PerkEffect.KatilGecitler),

        new Perk("Son Nefes",
            "Bir kaçanı öldürünce ona en uzak kaçan 10 saniye görünüyor.",
            PerkSlot.Harita, PerkEffect.KatilEnUzakKacan, edgeClamped: true, duration: 10f),
    };

    /// <summary>
    /// Geçersiz indeksi SIFIRA düşürür, kırpmaz.
    ///
    /// `Clamp` yanlış olurdu: liste kısalırsa son perki seçmiş herkes
    /// sessizce yeni son perke kayardı ve kimse neden değiştiğini anlamazdı
    /// (bölüm 13'ün kostüm dersi). Sıfır "seçimin artık yok" demek ve
    /// oyuncu bunu ekranda görüyor.
    /// </summary>
    public static int Sanitize(Perk[] list, int index) =>
        list == null || list.Length == 0 || index < 0 || index >= list.Length ? 0 : index;

    public static int SanitizeRunner(int index) => Sanitize(RunnerPerks, index);

    public static int SanitizeMonster(int index) => Sanitize(MonsterPerks, index);

    public static Perk Runner(int index) => RunnerPerks[SanitizeRunner(index)];

    public static Perk Monster(int index) => MonsterPerks[SanitizeMonster(index)];

    /// <summary>Listede ileri/geri dolaşır; uçlarda başa sarıyor.</summary>
    public static int Step(Perk[] list, int index, int delta)
    {
        if (list == null || list.Length == 0)
            return 0;

        int count = list.Length;
        return ((Sanitize(list, index) + delta) % count + count) % count;
    }
}
