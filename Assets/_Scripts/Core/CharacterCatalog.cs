using UnityEngine;

/// <summary>
/// Seçilebilir karakterlerin listesi: kaçan kostümleri ve canavar kostümleri.
///
/// ### Kostüm YALNIZCA görsel
///
/// Bölüm 17'nin ölçek kuralı gereği kaçanın görünen gövdesi çarpışma kutusuyla
/// örtüşmek zorunda: şişirilmiş bir kaçan, isabet etmesi gereken vuruşları
/// ıskalatır. O yüzden kostüm ne boyu ne hızı ne menzili değiştiriyor — tek
/// dokunduğu şey renk (ve ileride gövde modeli).
///
/// **İkinci canavar bu listeye AİT DEĞİL.** O bir kostüm değil oynanış: ayrı
/// bir `MovementProfile` ve kendine ait bir özellik (bölüm 1, kalan iş 8).
/// Buradaki canavar girdileri yalnızca aynı canavarın farklı görünüşleri.
///
/// ### Liste bugün RENKLERDEN oluşuyor, sebebi var
///
/// Elde iki model var: Banana Man (kaçan) ve KillerDoll (canavar). Kullanıcının
/// kendi ifadesiyle bugünkü kaçan modeli de "bir kostüm sayılır", yani liste
/// oradan başlıyor. Tek elemanlı bir seçim ekranı ölü bir ekran olurdu, o
/// yüzden aynı modelin renk çeşitlemeleri eklendi: bugün gerçekten çalışan,
/// gerçekten görünen ve model gerektirmeyen kostümler.
///
/// Yeni modeller geldiğinde <see cref="Costume.Body"/> alanı dolduruluyor ve
/// `PlayerBodyVisual` o indeksteki gövdeyi açıyor. Renk yolu aynen duruyor:
/// yeni modelin de renk çeşitlemesi olabilir.
///
/// ### Sıfırıncı giriş DOKUNULMAMIŞ hâl
///
/// Her iki listenin ilk elemanı beyaz (1,1,1) ve gövde 0, yani modelin
/// olduğu gibi hâli. Seçim hiç yapılmamış bir oyuncu da, kaydı bozulmuş bir
/// oyuncu da oraya düşüyor — yani varsayılan her zaman bugünkü görünüm.
/// </summary>
public static class CharacterCatalog
{
    /// <summary>Tek bir kostüm: bir gövde ve onun rengi.</summary>
    public readonly struct Costume
    {
        /// <summary>Seçim ekranında görünen ad.</summary>
        public readonly string Name;

        /// <summary>
        /// Gövdenin albedo çarpanı. Beyaz = modele hiç dokunma.
        ///
        /// `MaterialPropertyBlock` ile veriliyor, materyalin kendisine
        /// yazılmıyor: materyal bir varlık ve ona yazmak dosyayı değiştirip
        /// **bütün** oyuncuları aynı renge boyardı.
        /// </summary>
        public readonly Color Tint;

        /// <summary>
        /// Hangi gövde modeli. Bugün her kostümde 0 — elde tek model var.
        /// Yeni bir model eklendiğinde `PlayerBodyVisual`'ın gövde dizisine
        /// girip indeksi buraya yazılıyor.
        /// </summary>
        public readonly int Body;

        public Costume(string name, Color tint, int body = 0)
        {
            Name = name;
            Tint = tint;
            Body = body;
        }
    }

    /// <summary>
    /// Kaçan kostümleri. Sıfırıncı, bugünkü Banana Man'in dokunulmamış hâli.
    /// </summary>
    public static readonly Costume[] Runners =
    {
        new Costume("MUZ ADAM", Color.white),
        new Costume("KAN", new Color(0.92f, 0.30f, 0.26f)),
        new Costume("BATAKLIK", new Color(0.44f, 0.66f, 0.35f)),
        new Costume("GECE YARISI", new Color(0.36f, 0.44f, 0.78f)),
        new Costume("KÜL", new Color(0.58f, 0.57f, 0.60f)),
        new Costume("MOR SİS", new Color(0.66f, 0.42f, 0.82f)),
    };

    /// <summary>
    /// Canavar kostümleri. Rolü sunucu dağıtıyor (bölüm 11.1), yani bu seçim
    /// "canavar olursam neye benzeyeceğim" demek — seçen kişiyi canavar
    /// yapmıyor.
    /// </summary>
    public static readonly Costume[] Monsters =
    {
        new Costume("KUKLA", Color.white),
        new Costume("KÖZ", new Color(0.95f, 0.42f, 0.18f)),
        new Costume("SOLGUN", new Color(0.82f, 0.84f, 0.88f)),
        new Costume("KATRAN", new Color(0.34f, 0.31f, 0.36f)),
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

    /// <summary>Bir rolün kostümü — çağıranların rol dallanmasını tekrarlamaması için.</summary>
    public static Costume For(RoundRole role, int runnerIndex, int monsterIndex) =>
        role == RoundRole.Monster ? Monster(monsterIndex) : Runner(runnerIndex);

    /// <summary>Listede ileri/geri dolaşır; uçlarda başa sarıyor.</summary>
    public static int Step(Costume[] list, int index, int delta)
    {
        if (list == null || list.Length == 0)
            return 0;

        int count = list.Length;
        return ((Sanitize(list, index) + delta) % count + count) % count;
    }
}
