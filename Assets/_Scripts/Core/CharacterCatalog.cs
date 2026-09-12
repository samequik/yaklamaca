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
/// **İkinci canavar bu listeye AİT DEĞİL.** O bir kostüm değil oynanış: ayrı
/// bir `MovementProfile` ve kendine ait bir özellik (bölüm 1, kalan iş 8).
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

        public Costume(string name, string modelPath, string animationFolder = null)
        {
            Name = name;
            ModelPath = modelPath;
            AnimationFolder = animationFolder;
        }
    }

    /// <summary>
    /// Kaçan kostümleri. Sıralama ÖNEMLİ: `PlayerBodyVisual`'ın gövde dizisi
    /// ve cesedin gövde çeşitleri aynı indeksi kullanıyor.
    /// </summary>
    public static readonly Costume[] Runners =
    {
        new Costume("MUZ ADAM",
            "Assets/Plugins/Banana Yellow Games/Characters/Banana Man/Banana Man.fbx"),

        new Costume("UNITY-CHAN",
            "Assets/unity-chan!/Unity-chan! Model/Art/Models/unitychan.fbx",
            "Assets/unity-chan!/Unity-chan! Model/Art/Animations"),
    };

    /// <summary>
    /// Canavar kostümleri. Rolü sunucu dağıtıyor (bölüm 11.1), yani bu seçim
    /// "canavar olursam neye benzeyeceğim" demek — seçen kişiyi canavar
    /// yapmıyor.
    /// </summary>
    public static readonly Costume[] Monsters =
    {
        new Costume("KUKLA",
            "Assets/RamsterZ_FreeDoll/Art/Models/KillerDollUnity_BaseBody.fbx"),
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
