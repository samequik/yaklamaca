using UnityEngine;

/// <summary>
/// Seçilebilir karakterlerin listesi: kaçan kostümleri ve canavar kostümleri.
///
/// ### Kostüm bir MODEL, renk değil
///
/// İlk sürüm aynı modelin renk çeşitlemelerini listeliyordu: elde tek model
/// vardı ve tek elemanlı bir seçim ekranı ölü bir ekran olurdu. Oynanınca
/// kullanıcı bunu istemedi — kostümler gerçek modeller olacak ve modelleri
/// kendisi verecek. Renk yolu tamamen kaldırıldı; tutulsaydı gelmeyecek bir
/// özelliğin kodu ortada kalırdı.
///
/// Bugün her listede **tek giriş** var: bugünkü iki model. Kullanıcının kendi
/// ifadesiyle bugünkü kaçan modeli de "bir kostüm sayılır", yani liste oradan
/// başlıyor.
///
/// ### Kostüm YALNIZCA görsel
///
/// Bölüm 17'nin ölçek kuralı gereği kaçanın görünen gövdesi çarpışma kutusuyla
/// örtüşmek zorunda: şişirilmiş bir kaçan, isabet etmesi gereken vuruşları
/// ıskalatır. Kostüm ne boyu ne hızı ne menzili değiştiriyor.
///
/// **İkinci canavar bu listeye AİT DEĞİL.** O bir kostüm değil oynanış: ayrı
/// bir `MovementProfile` ve kendine ait bir özellik (bölüm 1, kalan iş 8).
/// Buradaki canavar girdileri aynı canavarın farklı görünüşleri.
///
/// ### Yeni model nasıl ekleniyor
///
/// 1. Model `Assets/_Art/Models` altına giriyor.
/// 2. `Kaçan Modelini Kur` (ya da canavar aracı) onu oyuncu prefabına ikinci
///    bir gövde olarak kuruyor ve `PlayerBodyVisual`'ın gövde dizisine ekliyor.
/// 3. Buraya bir satır: ad ve o gövdenin indeksi.
///
/// İkinci adımın kodu **henüz yazılmadı**, çünkü ortada ikinci bir model yok
/// (bölüm 5'in kuralı: soyutlama ancak somut bir ikinci kullanım varsa
/// eklenir). Model geldiğinde araç ve `PlayerBodyVisual` birlikte elden
/// geçecek; seçim, ağ ve menü tarafı zaten hazır.
/// </summary>
public static class CharacterCatalog
{
    /// <summary>Tek bir kostüm.</summary>
    public readonly struct Costume
    {
        /// <summary>Seçim ekranında görünen ad.</summary>
        public readonly string Name;

        /// <summary>
        /// Oyuncu prefabındaki hangi gövde. Bugün tek gövde var, yani hepsi 0.
        /// Yeni bir model eklendiğinde `PlayerBodyVisual`'ın gövde dizisindeki
        /// indeksi buraya yazılıyor.
        /// </summary>
        public readonly int Body;

        public Costume(string name, int body = 0)
        {
            Name = name;
            Body = body;
        }
    }

    /// <summary>Kaçan kostümleri. Bugün yalnızca Banana Man.</summary>
    public static readonly Costume[] Runners =
    {
        new Costume("MUZ ADAM"),
    };

    /// <summary>
    /// Canavar kostümleri. Rolü sunucu dağıtıyor (bölüm 11.1), yani bu seçim
    /// "canavar olursam neye benzeyeceğim" demek — seçen kişiyi canavar
    /// yapmıyor.
    /// </summary>
    public static readonly Costume[] Monsters =
    {
        new Costume("KUKLA"),
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
