using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Sol üstteki mini harita: çevrendeki dar bir pencereyi gösteriyor ve
/// **baktığın yöne göre dönüyor** — yukarısı her zaman ilerisi.
///
/// ### Neden yeniden yazıldı (2026-09-27)
///
/// İlk sürüm TÜM haritayı 150 pikselin içine sığdırıyordu ve kullanıcı
/// "karışık" bulup kapattırmıştı (2026-09-16). Ölçüldü ve haklıydı: harita
/// ~54 × 96 m, yani 3.2 m'lik bir koridor hücresi ekranda **4 piksel**
/// kalıyordu — okunacak bir şey yok. Üstelik sabit yönlüydü (Pac-Man tarzı),
/// yani koridorda hangi yöne baktığını haritadan çıkaramıyordun.
///
/// Şimdi iki şey birden değişti: pencere DAR (çevrendeki birkaç koridor) ve
/// harita seninle DÖNÜYOR.
///
/// ### Nasıl çalışıyor: tek bir kap döndürülüyor
///
/// Duvar kareleri `mapRoot`'un altında SABİT duruyor — `Menü Kur` onları bir
/// kez yerleştiriyor ve çalışma anında hiçbiri kıpırdamıyor. Her kare
/// yalnızca kabın kendisi döndürülüp kaydırılıyor:
///
/// * `localRotation` = oyuncunun yaw'ı → baktığın yön yukarı geliyor
/// * `anchoredPosition` = döndürülmüş konumunun NEGATİFİ → kendin tam ortada
///
/// 242 kareyi tek tek hesaplamak yerine ebeveyni oynatmak, aynı sonucu bir
/// transform yazısıyla veriyor.
///
/// ### Ek ağ verisi YOK
///
/// Yalnızca KENDİ konumun gösteriliyor — terminal, takım arkadaşı, ceset ya
/// da canavarın yeri yok. 2026-09-14'te tartışılan "çok bilgili mini harita"
/// fikrinin denge ve sızıntı soruları (bölüm 4'ün "istemciye görmesi
/// gerekmeyen bilgiyi gönderme" kuralı) burada hiç doğmuyor: zaten bildiğin
/// kendi konumunu çiziyoruz.
///
/// ### Ölçüler kurulumda yazılıyor
///
/// `worldOrigin` ve `pixelsPerMeter` `Menü Kur` (`MenuSetup.BuildMinimap`)
/// tarafından sahneden ÖLÇÜLÜP yazılıyor; elle değiştirilmemeli. Harita
/// büyürse aracı yeniden çalıştırmak yetiyor.
///
/// ### Görünürlük: burada HİÇBİR ŞEY yapılmıyor
///
/// Panel `OyunHud`'ın çocuğu, yani nişangah ve tur yazılarıyla AYNI
/// `CanvasGroup`'tan açılıp kapanıyor — menü açıkken gizli, turda görünür.
/// Ayrı bir görünürlük kodu yazılmadı.
///
/// 2026-09-16 ile 2026-09-27 arasında burada `gameObject.SetActive(false)`
/// yazan bir `Awake` vardı: önce tasarım beğenilmediği için, sonra da yeni
/// sürüm kayıt altına alınana kadar çalışan hiçbir şeyi bozmasın diye.
/// Kayıt alındı (`944d33c`), panel geri açıldı. **Yeniden gizlemek
/// gerekirse o tek satırı geri koymak yetiyor** — sahneye dokunmadan.
/// </summary>
public class MiniMapView : MonoBehaviour
{
    [Tooltip("Duvar karelerini taşıyan kap. Her karede döndürülüp kaydırılan " +
        "TEK obje bu.")]
    [SerializeField] private RectTransform mapRoot;

    [Tooltip("Pencerenin ortasında duran ok. Harita döndüğü için okun kendisi " +
        "hiç dönmüyor — yukarısı zaten ilerisi.")]
    [SerializeField] private RectTransform selfMarker;

    [SerializeField] private Image selfMarkerImage;

    [Tooltip("Harita uzayının dünya başlangıcı (X, Z). Kurulum yazıyor.")]
    [SerializeField] private Vector2 worldOrigin;

    [Tooltip("Bir dünya metresinin kaç piksel olduğu. Kurulum yazıyor; " +
        "pencerenin kaç metre gösterdiğini bu belirliyor.")]
    [SerializeField] private float pixelsPerMeter = 7.4f;

    [SerializeField] private Color runnerColor = new Color(0.34f, 0.88f, 0.46f, 1f);
    [SerializeField] private Color monsterColor = new Color(0.92f, 0.30f, 0.24f, 1f);

    [Header("Katilin işaretleri")]

    [Tooltip("Terminal/çıkış noktalarının kabı. PENCERENİN çocuğu ve onunla " +
        "aynı boyda: buradaki konumlar doğrudan pencere merkezine göre ve " +
        "kenara yapıştırma sınırı kabın kendi genişliğinden okunuyor.")]
    [SerializeField] private RectTransform markerRoot;

    [SerializeField] private Sprite markerSprite;

    [SerializeField] private float markerSize = 12f;

    [Tooltip("Bitmemiş terminal.")]
    [SerializeField] private Color terminalColor = new Color(0.38f, 0.72f, 1f, 1f);

    [Tooltip("Kaçanın hata yaptığı, alarmı öten terminal — yanıp sönüyor.")]
    [SerializeField] private Color terminalAlarmColor = new Color(1f, 0.32f, 0.26f, 1f);

    [Tooltip("Açılmış çıkış kapısı.")]
    [SerializeField] private Color exitColor = new Color(1f, 0.84f, 0.32f, 1f);

    [Tooltip("Alarm nabzı — saniyedeki yanıp sönme sayısı.")]
    [SerializeField] private float alarmBlinksPerSecond = 2.4f;

    [Tooltip("Pencerenin DIŞINDA kalıp kenara yapışan işaretin küçülme oranı. " +
        "Yakın ile uzak işareti ayırt etmenin tek ipucu bu.")]
    [SerializeField] private float edgeMarkerScale = 0.78f;

    [Header("Perk işaretleri")]

    [Tooltip("Kapı (geçit perki). Eğilme geçidinden AYRI renk, çünkü ikisi " +
        "farklı şekilde geçiliyor: kapı düğme, geçit Ctrl istiyor. Aynı renk " +
        "olsun istenirse ikisine aynı değeri vermek yetiyor.")]
    [SerializeField] private Color doorColor = new Color(0.72f, 0.78f, 0.88f, 1f);

    [Tooltip("Eğilme geçidi (geçit perki).")]
    [SerializeField] private Color crouchGapColor = new Color(0.62f, 0.52f, 0.86f, 1f);

    [Tooltip("Ceset (Kurtarıcı perki).")]
    [SerializeField] private Color corpseColor = new Color(0.86f, 0.42f, 0.42f, 1f);

    [Tooltip("Diriltme kabini (Kurtarıcı perki, yalnızca ceset taşırken). " +
        "Turkuaz: kabinin kendi ışığıyla aynı dil (bölüm 23).")]
    [SerializeField] private Color stationColor = new Color(0.32f, 0.86f, 0.80f, 1f);

    [Tooltip("Süreli katil perklerinin işaret ettiği kaçan.")]
    [SerializeField] private Color pingColor = new Color(1f, 0.78f, 0.28f, 1f);

    [Tooltip("Kapı/geçit çizgisinin kalınlığı (piksel).")]
    [SerializeField] private float lineThickness = 5f;

    /// <summary>Haritanın sahne kökü. Kanat bunun ÇOCUĞU (ölçüldü).</summary>
    private const string MapRootName = "Harita";

    private const string DoorGroupName = "Kapilar";

    private const string DoorPrefix = "Kapi_";

    private const string CrouchGroupName = "EgilmeGecitleri";

    private const string CrouchPrefix = "Gecit_";

    /// <summary>Eğilme geçidinin açıklığını veren çocuk.</summary>
    private const string CrouchCeilingName = "Tavan";

    /// <summary>
    /// Koridoru kesen bir engel: kapı ya da eğilme geçidi.
    ///
    /// **Noktayla değil ÇİZGİYLE çiziliyor** (kullanıcı isteği, 2026-10-04:
    /// *"nokta olarak değil de uzun çizgiler olarak göster, hani orda geçit
    /// var veya o koridoru kapatan bir kapı gibi gözüksünler"*). Bir nokta
    /// "burada bir şey var" diyor; çizgi hangi koridoru kapattığını da
    /// söylüyor.
    ///
    /// ### Kapı ile geçit YÖNLERİYLE ayrışıyor
    ///
    /// İkisi de 3.2 m, yani uzunluk ayırt etmiyor — ayıran şey çizginin
    /// koridora göre durduğu yön (kullanıcı isteği, 2026-10-04):
    ///
    /// | | Çizgi nereye | Okunuşu |
    /// |---|---|---|
    /// | **Kapı** | koridora DİK | "bu koridoru kapatıyor" |
    /// | **Geçit** | koridor BOYUNCA | "duvarın içinden geçilebiliyor" |
    ///
    /// Geçit önce kapıyla aynı yönde ve 1.4 m çiziliyordu (deliğin
    /// genişliği); kullanıcı *"dikey yap, duvarın içinden geçilebilen bir
    /// geçit misali gözüksün"* dedi. Yeni hâli duvar karosunu boydan boya
    /// kesiyor, yani mini haritada o hücre zaten DUVAR olarak çizildiği için
    /// (2026-09-27) çizgi onun içinden geçen bir tünel gibi duruyor.
    ///
    /// Renkleri de ayrı (`doorColor` / `crouchGapColor`), yani ayrıca bir
    /// "bu kapı mı geçit mi" işareti gerekmiyor.
    /// </summary>
    private readonly struct Barrier
    {
        /// <summary>Dünya konumu — hücrenin merkezi.</summary>
        public readonly Vector3 Position;

        /// <summary>Çizgi dünya X'inde mi uzanıyor (değilse Z).</summary>
        public readonly bool AlongX;

        /// <summary>Çizginin dünya uzunluğu, metre.</summary>
        public readonly float Length;

        public Barrier(Vector3 position, bool alongX, float length)
        {
            Position = position;
            AlongX = alongX;
            Length = length;
        }
    }

    /// <summary>
    /// Kapılar ve eğilme geçitleri — <see cref="EnsurePassages"/> bir kez
    /// dolduruyor, sonra hiç dokunulmuyor.
    /// </summary>
    private readonly List<Barrier> doors = new List<Barrier>();

    private readonly List<Barrier> crouchGaps = new List<Barrier>();

    private bool passagesCollected;

    /// <summary>
    /// İşaret havuzu. Terminal sayısı tur içinde değişmiyor ama bitenler
    /// listeden düştüğü için gereken sayı her kare farklı olabiliyor; havuz
    /// `Instantiate`/`Destroy` döngüsünü tamamen kaldırıyor (bölüm 2).
    /// </summary>
    private readonly List<Image> markerPool = new List<Image>();

    /// <summary>
    /// Dünya konumunu (X, Z) harita uzayına çevirir.
    ///
    /// `MenuSetup.BuildMinimap` duvarları yerleştirirken de AYNI metodu
    /// çağırıyor — iki yerde ayrı ayrı yazılan bir dönüşüm, biri değişince
    /// öbürünün sessizce kayması demek olurdu.
    /// </summary>
    public static Vector2 WorldToMapPoint(Vector3 worldPosition, Vector2 worldOrigin,
        float pixelsPerMeter)
    {
        return new Vector2(
            (worldPosition.x - worldOrigin.x) * pixelsPerMeter,
            (worldPosition.z - worldOrigin.y) * pixelsPerMeter);
    }

    /// <summary>
    /// `LateUpdate`, çünkü oyuncunun dönüşünü `PlayerController` `Update`'te
    /// yazıyor: bir kare bayat bir açıyla çizmemek için sonra okunuyor.
    /// </summary>
    private void LateUpdate()
    {
        if (mapRoot == null)
            return;

        RoundParticipant local = RoundParticipant.Local;
        bool hasLocal = local != null;

        if (selfMarker != null)
            selfMarker.gameObject.SetActive(hasLocal);

        // Oyuncu yokken (lobi açılırken, tur arası) haritayı olduğu yerde
        // bırakıyoruz: son kareyi donmuş göstermek, bir anda başka bir yere
        // sıçramasından iyi. İşaretler ise gizleniyor — donmuş bir harita
        // "son gördüğün kare", ama donmuş bir terminal noktası YANLIŞ BİLGİ.
        if (!hasLocal)
        {
            HideMarkersFrom(0);
            return;
        }

        Vector2 point = WorldToMapPoint(local.transform.position, worldOrigin, pixelsPerMeter);

        // Yaw'ı OLDUĞU GİBİ yazmak doğru yönü veriyor. Unity'de ileri yön
        // (sin ψ, cos ψ), yani harita uzayında +X'ten (90° − ψ) açıda; onu
        // yukarıya (90°) taşımak için tam ψ kadar döndürmek gerekiyor.
        Quaternion spin = Quaternion.Euler(0f, 0f, local.transform.eulerAngles.y);

        mapRoot.localRotation = spin;

        // Kendi konumunu pencerenin ortasına getiren kaydırma. Dönüş kabın
        // KENDİ pivotu etrafında olduğu için kaydırmanın da döndürülmüş
        // olması gerekiyor — sırayı ters çevirmek haritayı yay çizdirirdi.
        Vector3 centred = spin * new Vector3(-point.x, -point.y, 0f);
        mapRoot.anchoredPosition = new Vector2(centred.x, centred.y);

        if (selfMarkerImage != null)
            selfMarkerImage.color = local.Role == RoundRole.Monster ? monsterColor : runnerColor;

        TickMarkers(local, point, spin);
    }

    /// <summary>
    /// Katilin haritasındaki noktalar: bitmemiş terminaller, çıkış açıldıktan
    /// sonra da çıkış kapıları.
    ///
    /// ### Ek ağ verisi YOK
    ///
    /// Terminaller ve çıkışlar SABİT sahne objeleri — konumları zaten her
    /// istemcide var. Durumları (`IsCompleted`, `AlarmActive`) da zaten
    /// SyncVar'lardan türüyor, çünkü terminalin kendi göstergesi ve sesi
    /// onları okuyor (bölüm 11.2). Yani bölüm 4'ün "istemciye görmesi
    /// gerekmeyen bilgiyi gönderme" kuralı burada hiç zorlanmıyor: gösterilen
    /// her şey o istemcide çoktan duruyor, biz yalnızca ÇİZİYORUZ.
    ///
    /// Kaçanın haritasında bu noktalar YOK — kaçan zaten terminalleri
    /// biliyor, ve göstermek canavarın yerini ele vermeden bir avantaj
    /// üretmiyor. Karar tamamen yerel (`local.Role`), yani kaçanın istemcisi
    /// bu kodu hiç çalıştırmıyor.
    /// </summary>
    private void TickMarkers(RoundParticipant local, Vector2 selfPoint, Quaternion spin)
    {
        if (markerRoot == null)
            return;

        int used = 0;

        if (local.Role == RoundRole.Monster)
        {
            RoundManager manager = RoundManager.Instance;

            // Çıkış açıldıysa terminaller artık bir şey ifade etmiyor
            // (gereken sayı doldu), yerlerini kapılar alıyor.
            if (manager != null && manager.ExitOpen)
            {
                foreach (ExitGate gate in ExitGate.All)
                {
                    if (gate == null)
                        continue;

                    // Katilin BEDAVA radarı: kenara yapışıyor, yani bugünkü
                    // davranış birebir korunuyor (bkz. 2026-09-28).
                    if (PlaceMarker(used, gate.transform.position, exitColor,
                            selfPoint, spin, blink: false, edgeClamped: true))
                        used++;
                }
            }
            else
            {
                foreach (Terminal terminal in Terminal.All)
                {
                    // Biten terminal haritadan düşüyor: katilin bakması
                    // gereken yer artık orası değil.
                    if (terminal == null || terminal.IsCompleted)
                        continue;

                    bool alarm = terminal.AlarmActive;

                    if (PlaceMarker(used, terminal.transform.position,
                            alarm ? terminalAlarmColor : terminalColor,
                            selfPoint, spin, blink: alarm, edgeClamped: true))
                        used++;
                }
            }
        }

        TickPerkMarkers(local, selfPoint, spin, ref used);

        HideMarkersFrom(used);
    }

    /// <summary>
    /// Perkin gösterdiği noktalar — katilin yukarıdaki BEDAVA radarının
    /// ÜSTÜNE biniyor. Katilin terminal görüşü bilerek perk harcamıyor
    /// (kullanıcı, 2026-09-28: "perke gerek yok onun için, yoksa çok güçsüz
    /// olur").
    ///
    /// ### Ek ağ verisi YOK
    ///
    /// Terminaller, kapılar ve eğilme geçitleri SABİT sahne objeleri:
    /// konumları zaten her istemcide duruyor, terminalin durumu da
    /// SyncVar'dan türüyor. Yani bölüm 4'ün "istemciye görmesi gerekmeyen
    /// bilgiyi gönderme" kuralı bu üç perkte hiç zorlanmıyor — karar tamamen
    /// yerel (`local.ActivePerk`), gösterilen her şey o istemcide çoktan var
    /// ve biz yalnızca ÇİZİYORUZ.
    ///
    /// **Oyuncu ve ceset gösteren perkler burada YOK** (kaçan 3-4, katil
    /// 1-2-4): süreli olanlar sunucuda başlamak zorunda, katilin konumunu
    /// göstermek de bölüm 4'e aykırı bugünkü senkrona yaslanıyor. Onlar ayrı
    /// adımlarda gelecek.
    ///
    /// `used` `ref` geçiyor: kırpılan bir işaret havuzdan slot tüketmiyor,
    /// yani sayaç yalnızca gerçekten çizilenlerde artıyor ve
    /// `HideMarkersFrom` doğru yerden kesiyor.
    /// </summary>
    private void TickPerkMarkers(RoundParticipant local, Vector2 selfPoint,
        Quaternion spin, ref int used)
    {
        PerkCatalog.Perk perk = local.ActivePerk;

        switch (perk.Effect)
        {
            case PerkEffect.KacanTerminaller:
                foreach (Terminal terminal in Terminal.All)
                {
                    // Biten terminal düşüyor: kaçanın bakması gereken yer
                    // artık orası değil.
                    //
                    // Alarm nabzı bilerek YOK. "Burada biri hata yaptı"
                    // KATİLİN bilgisi (bölüm 11.4); kaçana vermek, perkin
                    // tarifinde ("bitmemiş terminaller") olmayan ikinci bir
                    // kazanç olurdu.
                    if (terminal == null || terminal.IsCompleted)
                        continue;

                    if (PlaceMarker(used, terminal.transform.position, terminalColor,
                            selfPoint, spin, blink: false, edgeClamped: perk.EdgeClamped))
                        used++;
                }

                break;

            // İki rol TEK dalda, çünkü kullanıcı ikisini de birebir aynı
            // tarif etti. Kırpılıp kırpılmadığını yine perkin kendi satırı
            // söylüyor (`perk.EdgeClamped`), yani roller ileride ayrışmak
            // isterse KATALOGDAN ayrışıyor, buradan değil.
            case PerkEffect.KacanGecitler:
            case PerkEffect.KatilGecitler:
                EnsurePassages();

                foreach (Barrier door in doors)
                {
                    if (PlaceLine(used, door, doorColor, selfPoint, spin,
                            edgeClamped: perk.EdgeClamped))
                        used++;
                }

                foreach (Barrier gap in crouchGaps)
                {
                    if (PlaceLine(used, gap, crouchGapColor, selfPoint, spin,
                            edgeClamped: perk.EdgeClamped))
                        used++;
                }

                break;

            // Bedenler perk açıkken HEP görünüyor, kabinler yalnızca ceset
            // TAŞIRKEN (kullanıcı kararı, 2026-09-28): kabin bilgisi ancak
            // elinde gövde varken işe yarıyor, sürekli göstermek ekranı boş
            // yere doldururdu.
            case PerkEffect.KacanCesetler:
                foreach (Corpse corpse in Corpse.All)
                {
                    if (corpse == null)
                        continue;

                    // `Corpse.Position` kalçanın GERÇEK yerini veriyor:
                    // ragdoll bağımsız hareket ediyor ve kök hiç kıpırdamıyor
                    // (bölüm 12'nin "ses kökten geliyordu" dersi).
                    if (PlaceMarker(used, corpse.Position, corpseColor,
                            selfPoint, spin, blink: false, edgeClamped: perk.EdgeClamped))
                        used++;
                }

                if (Corpse.CarriedBy(local) != null)
                {
                    foreach (RevivalStation station in RevivalStation.All)
                    {
                        if (station == null)
                            continue;

                        if (PlaceMarker(used, station.transform.position, stationColor,
                                selfPoint, spin, blink: false, edgeClamped: perk.EdgeClamped))
                            used++;
                    }
                }

                break;

            // Katilin yeri. Karşılığı var: bu perki taşıyan kaçan katilin
            // ekranında da görünüyor (`TickMutualRunners`), yani perk bir
            // radar değil karşılıklı bir uyarı.
            case PerkEffect.KacanKatil:
                foreach (RoundParticipant other in RoundParticipant.All)
                {
                    if (other == null || other == local)
                        continue;

                    if (other.Role != RoundRole.Monster || !other.IsAlive)
                        continue;

                    if (PlaceMarker(used, other.transform.position, monsterColor,
                            selfPoint, spin, blink: false, edgeClamped: perk.EdgeClamped))
                        used++;
                }

                break;

            // Ceset taşıyan kaçan. `carrierNetId` zaten SyncVar, yani
            // bıraktığı an işaret kendiliğinden kayboluyor — ayrı bir
            // "bıraktı" mesajı gerekmedi.
            case PerkEffect.KatilCesetTasiyan:
                foreach (Corpse corpse in Corpse.All)
                {
                    if (corpse == null || corpse.CarrierNetId == 0)
                        continue;

                    RoundParticipant carrier = FindByNetId(corpse.CarrierNetId);

                    if (carrier == null)
                        continue;

                    if (PlaceMarker(used, carrier.transform.position, runnerColor,
                            selfPoint, spin, blink: false, edgeClamped: perk.EdgeClamped))
                        used++;
                }

                break;

            // Süreli iki perk: hedefi ve süresini SUNUCU veriyor
            // (`RoundParticipant.TargetPerkPing`), burada yalnızca çiziliyor.
            //
            // Alarm perkinde nabız AÇIK: terminalin kendi alarmıyla aynı
            // dil (bölüm 11.4) ve "bu bilgi geçici" demenin en kısa yolu.
            // "Son Nefes"te kapalı — orada hata yapan kimse yok, yalnızca
            // uzaktaki bir kaçan var.
            case PerkEffect.KatilHataYapan:
            case PerkEffect.KatilEnUzakKacan:
                RoundParticipant pinged = local.PerkPingTarget;

                if (pinged != null
                    && PlaceMarker(used, pinged.transform.position, pingColor,
                        selfPoint, spin,
                        blink: perk.Effect == PerkEffect.KatilHataYapan,
                        edgeClamped: perk.EdgeClamped))
                    used++;

                break;
        }

        // Karşılıklı görünürlük KATİLİN kendi perkine bağlı DEĞİL: kuralı
        // kaçanın perki kuruyor, yani katil bunu her zaman görüyor.
        if (local.Role == RoundRole.Monster)
            TickMutualRunners(local, selfPoint, spin, ref used);
    }

    /// <summary>
    /// "Karşılıklı İz" perkini taşıyan kaçanlar, KATİLİN ekranında.
    ///
    /// Kaçan perk 4 çift taraflı (kullanıcı kararı, 2026-09-28): katili gören
    /// kaçan, katil tarafından da görülüyor. Yani perk tek taraflı bir radar
    /// değil — katil o kaçanı özellikle avlayıp radarı susturmayı ya da
    /// bilerek atlayıp bilmediği diğerlerine yönelmeyi seçebiliyor.
    ///
    /// **İki taraf da KIRPILIYOR** (`edgeClamped: false`): kimse kimseyi
    /// pencerenin dışından göremiyor, yani 20 metre öteden takip mümkün
    /// değil.
    ///
    /// Kaçanın perkini katilin istemcisi okuyabiliyor, çünkü perk SyncVar.
    /// Bu bir sızıntı değil: bilgi zaten karşılıklı olsun diye tasarlandı.
    /// </summary>
    private void TickMutualRunners(RoundParticipant local, Vector2 selfPoint,
        Quaternion spin, ref int used)
    {
        foreach (RoundParticipant other in RoundParticipant.All)
        {
            if (other == null || other == local)
                continue;

            if (other.Role != RoundRole.Runner || !other.IsAlive || other.IsEscaped)
                continue;

            if (!other.HasPerk(PerkEffect.KacanKatil))
                continue;

            if (PlaceMarker(used, other.transform.position, runnerColor,
                    selfPoint, spin, blink: false, edgeClamped: false))
                used++;
        }
    }

    /// <summary>
    /// netId'den katılımcıyı bulur. `RoundParticipant.All` beş kişilik bir
    /// liste, yani doğrusal arama ölçülemez; `NetworkClient.spawned`'a
    /// bakmak da aynı şeyi yapıp fazladan bir sözlük araması eklerdi.
    /// </summary>
    private static RoundParticipant FindByNetId(uint id)
    {
        if (id == 0)
            return null;

        foreach (RoundParticipant participant in RoundParticipant.All)
        {
            if (participant != null && participant.netId == id)
                return participant;
        }

        return null;
    }

    /// <summary>
    /// Kapı ve eğilme geçidi konumlarını BİR KEZ toplar.
    ///
    /// İkisi de sabit sahne objesi, yani her karede taramanın karşılığı yok.
    /// Duvar kareleri gibi ARAÇLA yazılmıyor: bunlar yalnızca bir `Vector3`
    /// listesi, araca bağlamak perki denemek için `Menü Kur` çalıştırmayı
    /// şart koşar ve sahneye serileşmiş bir alan daha eklerdi (bölüm 16'nın
    /// "koddaki varsayılanı değiştirmek yetmiyor" tuzağı).
    ///
    /// **Arama bilinen köke KAPSANMIŞ.** `GameObject.Find("Kapi_...")` gibi
    /// global bir arama yapılmıyor: güney kanadı aynı adlandırma şemasını
    /// paylaşıyor ve `Find` isim çakışmasında hangisini döndüreceğini
    /// garanti etmiyor (bölüm 0.1'in `Duvar_3_0` dersi).
    ///
    /// Kök sahne KÖKLERİ arasında aranıyor (`Mağara Yankısı Kur`'un
    /// `FindMapRoot`'uyla aynı desen), sonra altının TAMAMI taranıyor:
    /// ölçüldü, `Harita_Genisleme_Guney` ayrı bir kök DEĞİL, `Harita`'nın
    /// çocuğu — yani tek geçiş iki haritayı birden yakalıyor ve kanadın adı
    /// buraya hiç yazılmıyor. Harita büyürse bu kod değişmiyor.
    /// </summary>
    private void EnsurePassages()
    {
        if (passagesCollected)
            return;

        passagesCollected = true;

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != MapRootName)
                continue;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == DoorGroupName)
                    CollectDoors(child);
                else if (child.name == CrouchGroupName)
                    CollectCrouchGaps(child);
            }
        }

        if (doors.Count == 0 && crouchGaps.Count == 0)
        {
            // Sessiz kalması, perkin "bozuk" diye okunması demek olurdu ve
            // sebebi hiçbir yerde görünmezdi (bölüm 11.5'in ExitGate dersi).
            Debug.LogWarning($"Mini harita: '{MapRootName}' altında kapı/geçit " +
                "bulunamadı — geçit perki hiçbir şey çizmeyecek.");
        }
    }

    /// <summary>
    /// Kapıları toplar: konum, yön ve koridor genişliği.
    ///
    /// **Yön KÖKTEN okunamıyor.** `MazeMapBuilder` kapı kökünü hiç
    /// döndürmüyor; yönü çocuk panelin ÖLÇÜSÜNE gömüyor (`AxisSize`). Üstüne
    /// `Haritayı Giydir` ölçeği transform'dan collider'a taşıdı
    /// (`UnscalePanel`), yani panelin `localScale`'i (1,1,1) ve gerçek ölçü
    /// yalnızca `BoxCollider`'da duruyor — sahneden ölçüldü:
    /// `size = (3.20, 3.00, 0.20)`.
    ///
    /// Panel ADIYLA değil `SlidingDoor` bileşeniyle bulunuyor: bu projede
    /// ada göre arama iki kez yanlış sonuç verdi (bölüm 0.1'in `Duvar_3_0`'ı,
    /// bölüm 14'ün el kemiği).
    ///
    /// Konum kapının KÖKÜNDEN alınıyor, panelden değil: panel açılırken
    /// tavana çekiliyor (`slideDirection` = yukarı) ve çizgi onunla birlikte
    /// kaymamalı.
    /// </summary>
    private void CollectDoors(Transform group)
    {
        foreach (Transform door in group)
        {
            if (!door.name.StartsWith(DoorPrefix))
                continue;

            SlidingDoor panel = door.GetComponentInChildren<SlidingDoor>(true);
            BoxCollider box = panel != null ? panel.GetComponent<BoxCollider>() : null;

            if (box == null)
            {
                Debug.LogWarning($"Mini harita: '{door.name}' panelinde BoxCollider " +
                    "bulunamadı, bu kapı çizilmeyecek.");
                continue;
            }

            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);

            // Kapı koridoru KAPATIYOR: geniş yatay eksen koridorun genişliği,
            // ince olan kapının kalınlığı. Çizgi geniş eksende uzanıyor.
            bool alongX = Mathf.Abs(size.x) >= Mathf.Abs(size.z);

            doors.Add(new Barrier(door.position, alongX,
                alongX ? Mathf.Abs(size.x) : Mathf.Abs(size.z)));
        }
    }

    /// <summary>
    /// Eğilme geçitlerini toplar.
    ///
    /// Yönü `Tavan` veriyor: geçidin altında iki yan blok ve bir tavan var.
    /// Sahneden ölçüldü — tavan `(3.2, 1.9, 1.4)` ya da `(1.4, 1.9, 3.2)`,
    /// yani GENİŞ yatay eksen koridorun yönü (3.2 m), dar olan eğilerek
    /// geçilen deliğin genişliği (1.4 m).
    ///
    /// **Çizgi GENİŞ eksende**, yani koridor boyunca: duvarın içinden geçen
    /// bir tünel gibi duruyor. Kapı ise tam tersi, koridora dik. Bir süre
    /// burada dar eksen kullanılıyordu (çizgi 1.4 m ve kapıyla aynı yönde) ve
    /// kullanıcı onu "yatay" bulup çevirmemizi istedi.
    ///
    /// Burada ölçü `localScale`'den okunuyor (kapının aksine): geçide
    /// giydirme uygulanmıyor, yani ölçek hâlâ transform'da.
    ///
    /// **Grubun sınır kutusu KULLANILAMAZ:** iki yan blok birlikte hücrenin
    /// tamamını kaplıyor, yani kutu 3.2 × 3.2 — kare çıkıyor ve yönü hiç
    /// söylemiyor. Ölçüm bu yüzden tavana bakıyor.
    /// </summary>
    private void CollectCrouchGaps(Transform group)
    {
        foreach (Transform gap in group)
        {
            if (!gap.name.StartsWith(CrouchPrefix))
                continue;

            Transform ceiling = gap.Find(CrouchCeilingName);

            if (ceiling == null)
            {
                Debug.LogWarning($"Mini harita: '{gap.name}' altında " +
                    $"'{CrouchCeilingName}' yok, bu geçit çizilmeyecek.");
                continue;
            }

            Vector3 size = ceiling.lossyScale;

            // Koridor GENİŞ eksende; çizgi onun boyunca, yani duvarın
            // içinden geçiyormuş gibi uzanıyor.
            bool alongX = Mathf.Abs(size.x) >= Mathf.Abs(size.z);

            crouchGaps.Add(new Barrier(gap.position, alongX,
                Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z))));
        }
    }

    /// <summary>
    /// Bir işareti yerine koyar. Pencerenin dışında kalıyorsa YÖNÜNÜ
    /// koruyarak kenara yapıştırıyor — mini harita haritanın tamamını değil
    /// 26 metrelik bir pencereyi gösterdiği için, uzaktaki bir terminal aksi
    /// hâlde hiç görünmezdi.
    ///
    /// Kırpma KARE'ye yapılıyor, çembere değil: pencere `RectMask2D` ile
    /// kare kesiliyor ve çembere yapıştırılan bir işaret köşelerde pencerenin
    /// içinde, kenarlarda dışında kalırdı.
    ///
    /// Sınır yarım işaret boyu kadar içeride, yoksa maske noktanın yarısını
    /// kesiyor.
    ///
    /// ### `edgeClamped` — kırpılan mı, kenara yapışan mı
    ///
    /// Yukarıdaki yapıştırma artık KOŞULLU. `false` ise işaret pencerenin
    /// dışına çıktığı an hiç çizilmiyor: ne yönü ne mesafesi bilinebiliyor,
    /// yalnızca yaklaşınca öğreniliyor. Kullanıcı bu ayrımı perk perk ayrı
    /// ayrı söyledi (2026-09-28), yani rastgele değil bir denge kaldıracı:
    /// kenara yapışan işaret güçlü bilgi, kırpılan çok daha zayıf.
    ///
    /// **Kararı bu metot VERMİYOR**, perkin kendi satırı veriyor
    /// (`PerkCatalog.Perk.EdgeClamped`). İki yerde tutulan bir kural, biri
    /// değişince öbürünün unutulması demek olurdu (menü figürlerinin
    /// açısında tam olarak bu yaşandı, bölüm 13).
    ///
    /// Dönüş değeri "çizildi mi": `false` dönen işaret havuzdan slot
    /// TÜKETMİYOR, yani çağıran sayacını artırmıyor ve hiç gösterilmeyecek
    /// bir işaret için obje de kurulmuyor.
    /// </summary>
    private bool PlaceMarker(int index, Vector3 worldPosition, Color color,
        Vector2 selfPoint, Quaternion spin, bool blink, bool edgeClamped)
    {
        // Duvar kareleriyle AYNI dönüşüm: kap `spin * (p - selfPoint)`
        // konumuna gidiyor, biz de aynı ifadeyi tek bir nokta için kuruyoruz.
        Vector2 target = WorldToMapPoint(worldPosition, worldOrigin, pixelsPerMeter);
        Vector3 relative = spin * new Vector3(target.x - selfPoint.x, target.y - selfPoint.y, 0f);

        float limit = Mathf.Max(0f, markerRoot.rect.width * 0.5f - markerSize * 0.5f);
        float reach = Mathf.Max(Mathf.Abs(relative.x), Mathf.Abs(relative.y));
        bool outside = reach > limit;

        // Kırpılan işaret: pencerenin dışı HİÇ gösterilmiyor. `GetMarker`
        // bilerek bundan SONRA çağrılıyor — hiç çizilmeyecek bir işaret için
        // havuzda obje kurmanın karşılığı yok.
        if (outside && !edgeClamped)
            return false;

        if (outside && reach > 0.0001f)
            relative *= limit / reach;

        Image marker = GetMarker(index);

        RectTransform rect = marker.rectTransform;
        rect.anchoredPosition = new Vector2(relative.x, relative.y);

        // Havuz `PlaceLine` ile PAYLAŞILIYOR, yani bu slot geçen kare bir
        // çizgi olabilir. Nokta biçimini her seferinde geri yazmak şart:
        // yazılmazsa işaret uzamış ve dönmüş hâlde kalır — bu dosyanın
        // "bayat durum" tuzağı (bölüm 20'nin CanvasGroup dersiyle aynı aile).
        marker.sprite = markerSprite;
        marker.preserveAspect = true;
        rect.sizeDelta = new Vector2(markerSize, markerSize);
        rect.localRotation = Quaternion.identity;

        float scale = outside ? edgeMarkerScale : 1f;
        rect.localScale = new Vector3(scale, scale, 1f);

        if (blink)
        {
            // |sin| kullanılıyor: periyodu tam 1/f, yani alan adı "saniyedeki
            // yanıp sönme sayısı" gerçekten o.
            float pulse = Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI * alarmBlinksPerSecond));
            color.a *= Mathf.Lerp(0.18f, 1f, pulse);
        }

        marker.color = color;

        if (!marker.gameObject.activeSelf)
            marker.gameObject.SetActive(true);

        return true;
    }

    /// <summary>
    /// Bir engeli ÇİZGİ olarak koyar: koridoru kesen bir kapı ya da eğilerek
    /// geçilen bir yarık.
    ///
    /// Kırpma ve kenara yapışma kuralları <see cref="PlaceMarker"/> ile
    /// birebir aynı — iki yerde ayrı yazılan bir kırpma, biri değişince
    /// öbürünün sessizce kayması demek olurdu.
    ///
    /// ### Açı nereden geliyor
    ///
    /// `WorldToMapPoint` dünya X'ini harita X'ine, dünya Z'sini harita
    /// Y'sine çeviriyor (hiçbirini ters çevirmiyor). Harita kabı `yaw` kadar
    /// döndüğü için dünya X'i boyunca uzanan bir çizgi ekranda da `yaw`
    /// kadar dönüyor; Z boyunca uzanan ayrıca 90° alıyor.
    ///
    /// ### Sprite BİLEREK boş
    ///
    /// Nokta sprite'ı yuvarlak bir halka ve uzatılınca elips gibi geriliyor.
    /// Sprite'sız bir `Image` düz bir dikdörtgen çiziyor, rengi de
    /// `Image.color`'dan geliyor — ayrı bir çizgi dokusu üretmeye gerek yok.
    /// </summary>
    private bool PlaceLine(int index, Barrier barrier, Color color, Vector2 selfPoint,
        Quaternion spin, bool edgeClamped)
    {
        Vector2 target = WorldToMapPoint(barrier.Position, worldOrigin, pixelsPerMeter);
        Vector3 relative = spin * new Vector3(target.x - selfPoint.x, target.y - selfPoint.y, 0f);

        float limit = Mathf.Max(0f, markerRoot.rect.width * 0.5f - markerSize * 0.5f);
        float reach = Mathf.Max(Mathf.Abs(relative.x), Mathf.Abs(relative.y));
        bool outside = reach > limit;

        if (outside && !edgeClamped)
            return false;

        if (outside && reach > 0.0001f)
            relative *= limit / reach;

        Image marker = GetMarker(index);
        RectTransform rect = marker.rectTransform;

        rect.anchoredPosition = new Vector2(relative.x, relative.y);
        rect.localScale = Vector3.one;
        rect.sizeDelta = new Vector2(barrier.Length * pixelsPerMeter, lineThickness);
        rect.localRotation = Quaternion.Euler(0f, 0f,
            spin.eulerAngles.z + (barrier.AlongX ? 0f : 90f));

        marker.sprite = null;
        marker.preserveAspect = false;
        marker.color = color;

        if (!marker.gameObject.activeSelf)
            marker.gameObject.SetActive(true);

        return true;
    }

    private void HideMarkersFrom(int index)
    {
        for (int i = index; i < markerPool.Count; i++)
        {
            if (markerPool[i] != null && markerPool[i].gameObject.activeSelf)
                markerPool[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Havuzdaki işareti verir, yoksa kurar.
    ///
    /// **Çalışma anında kuruluyor, araçta değil.** Kaç nokta gerektiği tura
    /// göre değişiyor (terminal sayısı, kaçan sayısı, bitenler) ve araç bunu
    /// bilemez. `Terminal.GetOrCreateStateLight` ve `MonsterAura` ile aynı
    /// gerekçe (bölüm 11.2).
    /// </summary>
    private Image GetMarker(int index)
    {
        while (markerPool.Count <= index)
        {
            GameObject markerObject = new GameObject("Isaret", typeof(RectTransform), typeof(Image));
            markerObject.transform.SetParent(markerRoot, false);

            Image image = markerObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = markerSprite;
            image.preserveAspect = true;

            RectTransform rect = markerObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(markerSize, markerSize);

            markerPool.Add(image);
        }

        return markerPool[index];
    }
}
