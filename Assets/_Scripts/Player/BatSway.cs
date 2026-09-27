using UnityEngine;

/// <summary>
/// Elde taşınan uzun bir nesnenin (domuz katilin sopası) savrulmasını
/// yumuşatır: sap elde kalır, UCU geriden gelir.
///
/// ### Neden gerekti (2026-09-24)
///
/// Sopa sağ el kemiğine bağlı (bkz. `MonsterBatBuilder`). Koşu animasyonunda
/// el hızlı ve geniş bir yay çiziyor; sopa 0.78 m uzunluğunda olduğu için
/// elin küçük bir açısal hareketi ucunda büyük bir süpürmeye dönüşüyor.
/// Kullanıcı bunu "sopa çok sola sağa yukarı aşağı gidiyor ve çok seri
/// oluyor" diye bildirdi.
///
/// ### Neden ANİMASYON HIZI değil
///
/// İlk deneme koşu animasyonunun oynatma hızını kısmaktı (1.6 → 1.15).
/// Sopayı gerçekten yavaşlatıyordu ama **animasyonun kendisini bozuyordu**:
/// bacaklar 6.4 m/s yerine 4.6 m/s ilerliyor, ayak kayması %12'den %36'ya
/// çıkıyordu. Kullanıcı açıkça "sopa için sadece, domuzun animasyonlarını
/// bozmadan" dedi ve o yol tamamen geri alındı.
///
/// Buradaki çözüm animasyona hiç dokunmuyor: kemikler aynen oynuyor,
/// yalnızca sopanın kendi dünya dönüşü süzülüyor.
///
/// ### Yalnızca DÖNÜŞ süzülüyor, konum değil
///
/// Sopanın pivotu topuz ucunda (mesh yerel Y'de 0→1 uzanıyor) ve elin
/// hemen dibinde duruyor. Dönüşü süzüp konumu olduğu gibi bırakmak, sopayı
/// sapından çivili tutup yalnızca ucunu geciktiriyor — yani elden kopmuş
/// görünmüyor. Konum da süzülseydi sopa elin dışına kayardı.
///
/// ### Alçak geçiren süzgeç: HIZLI hareketi kesiyor, yavaşı geçiriyor
///
/// `1 - exp(-k·dt)` birinci dereceden bir süzgeç. Genlik zayıflaması
/// `1/√(1+(2πfτ)²)`, yani frekansla artıyor:
///
/// | Hareket | Frekans | k=4 (τ=0.25 sn) ile kalan |
/// |---|---|---|
/// | Koşu çevrimi (~0.39 sn) | 2.6 Hz | **%24** — dörtte üçü kesiliyor |
/// | Saldırı savurması (~2.4 sn klip) | ~0.4 Hz | %84 — okunaklılığı duruyor |
///
/// Şikâyet edilen şey zaten "çok seri" olandı; süzgeç tam onu hedefliyor ve
/// saldırının okunabilirliğini bozmuyor. Bu yüzden ayrıca "yalnızca
/// koşarken çalış" diye bir durum kontrolü eklenmedi — gereksiz bağ olurdu.
///
/// ### Süzgeç GÖVDE uzayında çalışıyor, dünya uzayında DEĞİL (2026-09-26)
///
/// İlk sürüm sopanın **dünya** dönüşünü süzüyordu ve o dönüş iki ayrı şeyi
/// birden taşıyor: animasyonun el hareketi VE oyuncunun kendi dönüşü.
/// Süzgeç ikisini ayırt edemediği için fare dönüşünü de yumuşatıyordu —
/// kullanıcı bunu "sağa sola, arkamı döndüğümde sopa benle gelmiyor, elime
/// göre gelmiyor, o yüzden elimde gözükmüyor" diye bildirdi.
///
/// Ölçüldü (k = 8, sopa 0.78 m): sopanın ELDEN sapması
///
/// | Durum | Dünya uzayı | Gövde uzayı |
/// |---|---|---|
/// | Durarak koşma | 17.6° (0.24 m) | 17.6° (0.24 m) |
/// | 90° dönüş / 0.6 sn | 33.6° (0.45 m) | **17.6°** |
/// | 180° dönüş / 0.4 sn | **59.9° (0.78 m)** | **17.6°** |
/// | 180° flick / 0.2 sn | **60.0° (0.78 m)** | **17.6°** |
///
/// Yani normal her dönüş `maxLagDegrees` tavanına ÇARPIYORDU ve sopanın ucu
/// tam bir sopa boyu elden uzakta kalıyordu — "elimde gözükmüyor" tam olarak
/// bu.
///
/// Süzgeç artık hedefi gövde kökünün (`Animator`'ı taşıyan obje) uzayına
/// çevirip orada yumuşatıyor, sonra geri dünyaya taşıyor. Oyuncu dönünce
/// çerçeve de dönüyor, yani gövdeye göre duruş HİÇ DEĞİŞMİYOR: sopa dönüşe
/// katı bir bütün olarak katılıyor. Süzülen tek şey animasyonun eli.
///
/// **Yumuşatmanın kendisi hiç azalmadı** — tablonun ilk satırı, yani
/// şikâyet edilen koşu savrulması, iki sürümde de aynı.
///
/// Yan kazanç: ışınlanma artık süzgeci hiç ilgilendirmiyor. Tur başı ve
/// yakalama ışınlaması çerçeveyi de sopayı da birlikte taşıyor, aradaki
/// gövde-uzayı farkı sıfır kalıyor.
///
/// ### Süzgeç KENDİ ÇIKTISINI okuyordu (2026-09-26, aynı gün)
///
/// İlk iki sürüm hedefi `transform.rotation`'dan okuyordu. Ama sopa el
/// kemiğinin ÇOCUĞU ve biz her karede onun dünya dönüşünü yazıyoruz — yani
/// `localRotation` artık authored duruş değil, BİZİM geçen kareki
/// çıktımız. Sonraki kare onu okuyunca süzgeç kendi çıktısıyla besleniyordu.
///
/// İki somut sonucu vardı:
///
/// | | Geri beslemeli | Şimdi |
/// |---|---|---|
/// | Sopaya 30° sapma ver, el sabit, 2 sn bekle | **30° kalıyor** | 0° |
/// | 1 öldürmeden sonra kalıcı sapma | 2.9° | 0° |
/// | 5 öldürme | 14.6° | 0° |
/// | 10 öldürme | **29.1°** | 0° |
///
/// Sebebi tek cümlede: el sabitken hedef `h · (h⁻¹ · çıktı)` = çıktının
/// kendisi, yani hata özdeş olarak sıfır ve sopayı doğru duruşa çeken
/// **hiçbir geri getirme kuvveti yok**. Savurmanın gidiş ve dönüş yolu
/// birebir aynı olmadığı için her savurma küçük bir kalıntı bırakıyor ve
/// kalıntılar birikiyordu. Kullanıcı bunu "sopayla her birini öldürdüğümde
/// sopanın açısı değişiyor" diye bildirdi.
///
/// Hedef artık `hand.rotation · baseLocal` ile ÜRETİLİYOR: süzgecin girdisi
/// çıktısından tamamen bağımsız ve tek denge noktası authored duruş.
///
/// **Sönüm de yeniden ayarlandı.** Hatalı döngü her şeyi frekanstan bağımsız
/// %87.5 kısıyordu; düzeltilmiş süzgeç k=8'de koşmanın %45'ini geçirirdi,
/// yani sopa birden 3.6 kat daha çok savrulurdu. `k = 4` onaylanan hisse
/// yakın duruyor (koşma %24) ve saldırıyı da koruyor (%84).
///
/// > Ders: **bir süzgecin girdisi, yazdığı yerden okunuyorsa o süzgeç
/// > değildir.** Geri besleme hiçbir yerde hata vermiyor; yalnızca
/// > mekanizmayı sessizce başka bir şeye çeviriyor — burada frekans
/// > ayrımı olmayan, geri getirme kuvveti bulunmayan bir integratöre.
/// </summary>
[DefaultExecutionOrder(100)]
public class BatSway : MonoBehaviour
{
    /// <summary>
    /// Ayarların TEK KAYNAĞI. Alanlar prefaba serileşiyor, yani bu sayıları
    /// değiştirmek tek başına yetmiyor (bölüm 16): `MonsterBatBuilder` onları
    /// her çalıştırmada prefaba yazıyor — sopanın duruşunda zaten uygulanan
    /// kuralın aynısı. İki yerde ayrı sayı tutmamak için araç da buradaki
    /// sabitleri okuyor.
    /// </summary>
    public const float DefaultFollowSharpness = 4f;

    /// <inheritdoc cref="DefaultFollowSharpness"/>
    public const float DefaultMaxLagDegrees = 35f;

    /// <inheritdoc cref="DefaultFollowSharpness"/>
    public const float DefaultSnapDegrees = 90f;

    [Tooltip("Takip sıkılığı (1/saniye). BÜYÜK = sopa ele daha sıkı yapışır " +
        "(az yumuşatma), KÜÇÜK = daha çok geriden gelir. Zaman sabiti 1/k: " +
        "8 ≈ 0.125 sn ve koşu savrulmasının yarısından fazlasını kesiyor.")]
    [SerializeField] private float followSharpness = DefaultFollowSharpness;

    [Tooltip("Sopanın hedefin ne kadar gerisinde kalabileceği (derece). " +
        "Yalnızca bir emniyet: ölçülen en büyük gerçek gecikme 16.8° " +
        "(koşma), yani bu tavan normal oyunda hiç devreye girmiyor.")]
    [SerializeField] private float maxLagDegrees = DefaultMaxLagDegrees;

    [Tooltip("Bu açıdan büyük sıçramalarda süzgeç atlanıyor ve sopa ANINDA " +
        "yerine oturuyor. Süzgeç gövde uzayında çalıştığı için ışınlanma " +
        "artık buraya hiç düşmüyor; kalan durum animasyonun sert kesilmesi " +
        "(geçişsiz bir durum değişimi).")]
    [SerializeField] private float snapDegrees = DefaultSnapDegrees;

    /// <summary>Inspector'dan elle ayarlamayı fark etme eşiği (derece).</summary>
    private const float TuningEpsilon = 0.01f;

    private Quaternion smoothed;
    private Transform frame;
    private bool ready;

    /// <summary>
    /// Sopanın AUTHORED yerel duruşu — `MonsterBatBuilder.TunedEuler`'ın
    /// prefaba yazdığı değer. Süzgecin hedefi bundan üretiliyor.
    ///
    /// `Awake`'te okunuyor, çünkü o an henüz hiçbir şey yazmadık: bileşen ilk
    /// kez etkinleştiğinde `localRotation` prefabtaki değeri taşıyor.
    /// </summary>
    private Quaternion baseLocal;

    /// <summary>Geçen karede BİZİM yazdığımız yerel duruş.</summary>
    private Quaternion written;

    private bool hasWritten;

    private void Awake() => baseLocal = transform.localRotation;

    // Gövde kapatılıp açılıyor (kostüm değişimi, tur başı): eski dönüşle
    // devam etmek sopayı bir kare yanlış yerde gösterirdi.
    private void OnEnable()
    {
        ready = false;
        frame = null;
        hasWritten = false;

        // Süzgecin bıraktığı duruşu geri al. Gövde kostüm değişiminde
        // kapatılıp açılıyor ve `localRotation` bizim son yazdığımızı
        // taşıyor; onu "authored duruş" sanmak hatayı kalıcılaştırırdı.
        transform.localRotation = baseLocal;
    }

    /// <summary>
    /// Süzgecin ÖLÇÜM ÇERÇEVESİ: gövdenin kökü, yani `Animator`'ı taşıyan
    /// obje. El kemiği onun ALTINDA duruyor, dolayısıyla bu çerçeveye göre
    /// ölçmek "animasyon eli nereye götürdü" sorusunu soruyor — "oyuncu
    /// nereye döndü" sorusunu değil.
    ///
    /// Gövde kökü seçildi, oyuncu kökü değil: `PlayerBodyVisual.LateUpdate`
    /// gövde köküne bir yaw payı yazıyor (`locomotionYawOffset`) ve o bir
    /// gövde duruşu, el savrulması değil — çerçevenin İÇİNDE kalması gerekiyor.
    /// O metot varsayılan çalışma sırasında, bu bileşen 100'de, yani çerçeve
    /// okunduğunda güncel.
    ///
    /// Bir kez çözülüp saklanıyor: hiyerarşi sabit. Gövde yok edilirse
    /// referans Unity'nin `==` kuralıyla null'a düşüyor ve yeniden çözülüyor.
    /// </summary>
    private Transform ResolveFrame()
    {
        if (frame != null)
            return frame;

        Animator animator = GetComponentInParent<Animator>(true);
        frame = animator != null ? animator.transform : transform.root;
        return frame;
    }

    /// <summary>
    /// Animatör kemikleri yazdıktan SONRA çalışıyor: `LateUpdate` ve
    /// `DefaultExecutionOrder(100)`.
    ///
    /// Yüz numara, gövde köküne yazan `PlayerBodyVisual.LateUpdate`'ten
    /// sonra gelmek için. O da bu zincirin üstünde: önce kök, sonra kemikler,
    /// en son sopa — yoksa süzgeç bir kare bayat bir hedefe bakardı.
    /// </summary>
    private void LateUpdate()
    {
        Transform hand = transform.parent;

        if (hand == null)
            return;

        // Play sırasında Inspector'dan/Scene'den ayarlandıysa yeni duruşu
        // benimse. Ölçüt "bizim yazdığımızdan farklı mı": `localRotation`
        // ebeveyn hareket edince değişmiyor, yani fark ancak DIŞARIDAN
        // gelebilir. Duruşu Play modunda deneyip beğenileni koda yazmak bu
        // projenin yerleşik yolu (bölüm 25) ve süzgeç onu engellememeli.
        if (!hasWritten ||
            Quaternion.Angle(transform.localRotation, written) > TuningEpsilon)
            baseLocal = transform.localRotation;

        Quaternion basis = ResolveFrame().rotation;

        // Animasyonun bu kare için İSTEDİĞİ duruş — GÖVDEYE göre.
        //
        // `transform.rotation` OKUNMUYOR: o bizim geçen karede yazdığımızı
        // taşıyor ve okumak süzgeci KENDİ ÇIKTISIYLA beslerdi (sınıf
        // dokümanındaki kutu). Oyuncunun dönmesi hem `basis`'i hem `hand`'i birlikte
        // döndürdüğü için bu değeri hiç değiştirmiyor.
        Quaternion target = Quaternion.Inverse(basis) * (hand.rotation * baseLocal);

        if (!ready || Quaternion.Angle(smoothed, target) > snapDegrees)
        {
            smoothed = target;
            ready = true;
        }
        else
        {
            smoothed = Quaternion.Slerp(smoothed, target,
                1f - Mathf.Exp(-followSharpness * Time.deltaTime));

            if (Quaternion.Angle(smoothed, target) > maxLagDegrees)
                smoothed = Quaternion.RotateTowards(target, smoothed, maxLagDegrees);
        }

        // Her dalda YAZILIYOR. Eskiden erken çıkışlar yazmadan dönüyordu ve
        // o zaman doğruydu (`transform.rotation` zaten hedefe eşitti); artık
        // hedef ayrı hesaplandığı için yazmamak bayat duruşu bırakırdı.
        transform.rotation = basis * smoothed;
        written = transform.localRotation;
        hasWritten = true;
    }
}
