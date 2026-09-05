using UnityEngine;

/// <summary>
/// Kaçan modelinin animasyonlarını sürer.
///
/// Hız, eğilme ve bakış IK ortak tabanda (`CharacterAnimatorBase`). Burada
/// kaçana özel iki şey var: **havada olma** ve **yakalanma**.
///
/// ### Havada olma neden ağdan gelmiyor
///
/// Kaçan zıplayabiliyor (canavar zıplayamıyor, bu yüzden onda hiç gerekmedi).
/// `PlayerController.IsGrounded` var ama **uzak oyuncuda geçersiz**: orada
/// bileşen kapalı, `Update` çalışmıyor, değer donmuş kalıyor.
///
/// Bu yüzden havada olma da hız gibi **pozisyondan çıkarılıyor**. Ham eşik
/// yetmiyordu: zıplamanın tepe noktasında dikey hız sıfırdan geçiyor ve
/// karakter bir kare yere basmış görünüyordu. Onun yerine küçük bir durum
/// makinesi var — kalkışta ya da düşüşte havalanıyor, ancak **düştükten sonra
/// duruldu**ğunda iniyor:
///
/// ```
/// dikey hız > rise        → havada
/// dikey hız < -fall       → havada + düşüyor
/// düşüyor && |hız| < land → yere indi
/// |hız| ≈ 0, settleTime   → yere indi (emniyet — düşüş şart değil)
/// ```
///
/// Tepe noktası `düşüyor` henüz false olduğu için havada sayılıyor; kenardan
/// düşmek de yakalanıyor (zıplamadan da `düşüyor` kuruluyor).
///
/// ### Dördüncü satır olmadan bayrak SIKIŞIYORDU (2026-09-05)
///
/// Üçüncü satır `düşüyor`a bağlı ve `düşüyor` yalnızca hızlı düşüşte
/// kuruluyor. Kasaya çıkmak, basamağa binmek, zıplayıp hemen bir yüzeye
/// konmak — hepsi `havada`yı kuruyor ama `düşüyor`u hiç kurmuyor. O durumda
/// bayrağı indirecek **hiçbir şart kalmıyordu**: karakter yerde dururken
/// sonsuza kadar zıplama animasyonunda kalıyordu.
///
/// Dördüncü satır düşüşe değil **durulmaya** bakıyor ve süre eşiği tepe
/// noktasından uzun, `airborneGrace`ten kısa seçildi — yani sıkışma
/// animasyona hiç yansımadan temizleniyor.
///
/// ### Süre payı, eşikten daha önemli
///
/// Basamağa çıkmak **tek karede** yarım metre kaldırabiliyor; 60 FPS'te bu
/// 20 m/s dikey hız demek, yani hiçbir hız eşiği onu eleyemez. Ufak bir
/// tümsekte zıplama animasyonuna girilmesinin sebebi buydu.
///
/// Filtre bu yüzden **süre**: havada olma `airborneGrace` kadar sürmeden
/// animasyona geçilmiyor. Basamak bir kare sürüyor, gerçek zıplama neredeyse
/// bir saniye. İniş ise gecikmesiz — yoksa karakter yere bastıktan sonra bir
/// süre zıplama pozunda kayardı.
///
/// Alternatif `PlayerPoseSync`'e bir bool eklemekti; tek bit ucuz ama zaten
/// gönderilen pozisyondan çıkarılabilen bir bilgiyi ikinci kez göndermek
/// olurdu (CLAUDE.md bölüm 4).
/// </summary>
public class RunnerAnimator : CharacterAnimatorBase
{
    public const string AirborneParameter = "Airborne";
    public const string DeathTrigger = "Death";

    private static readonly string[] AllTriggers = { DeathTrigger };

    [Header("Havada Olma")]
    [Tooltip("Bu dikey hızın üstünde kalkış sayılıyor (m/s). Zıplama 5.1 m/s " +
        "çıkıyor; basamak çıkarken oluşan küçük sıçramalar elenmeli.")]
    [SerializeField] private float riseThreshold = 2f;

    [Tooltip("Bu dikey hızın altında düşüş sayılıyor (m/s). Kenardan " +
        "yürüyerek düşmek de havada sayılsın diye var.")]
    [SerializeField] private float fallThreshold = 3f;

    [Tooltip("Düştükten sonra dikey hız bunun altına inince yere basmış " +
        "sayılıyor (m/s).")]
    [SerializeField] private float landThreshold = 0.6f;

    [Tooltip("Animasyona geçmeden önce beklenen süre (saniye). ASIL FİLTRE BU: " +
        "basamağa çıkmak tek karede 20 m/s dikey hız üretiyor, yani hiçbir hız " +
        "eşiği onu eleyemez. Süre eliyor — basamak bir kare sürüyor, zıplama " +
        "neredeyse bir saniye.")]
    [SerializeField] private float airborneGrace = 0.18f;

    [Header("Sıkışma Emniyeti")]
    [Tooltip("Dikey hız bu değerin altındayken 'yerleşmiş' sayılıyor (m/s). " +
        "Zıplamanın tepe noktasından KÜÇÜK olmalı ki tepe yanlışlıkla iniş " +
        "sanılmasın.")]
    [SerializeField] private float settleSpeed = 0.35f;

    [Tooltip("Dikey hız bu süre boyunca yerleşik kalırsa havada olma iptal " +
        "ediliyor — DÜŞÜŞ YAŞANMAMIŞ OLSA BİLE (saniye).\n\n" +
        "İki sınır arasında olmalı: tepe noktasının süresinden UZUN (yerçekimi " +
        "600 u/s ve eşik 0.35'te ≈ 0.06 sn), airborneGrace'ten KISA (0.18 sn) " +
        "ki sıkışma animasyona hiç yansımasın.")]
    [SerializeField] private float settleTime = 0.12f;

    private bool airborne;
    private bool falling;
    private float airborneTime;
    private float settleTimer;

    protected override string[] Triggers => AllTriggers;

    protected override void Update()
    {
        base.Update();

        if (animator == null || !animator.isActiveAndEnabled)
            return;

        UpdateAirborne();
    }

    private void UpdateAirborne()
    {
        float vertical = VerticalSpeed;

        if (vertical > riseThreshold)
        {
            airborne = true;
        }
        else if (vertical < -fallThreshold)
        {
            airborne = true;
            falling = true;
        }
        else if (airborne && falling && Mathf.Abs(vertical) < landThreshold)
        {
            airborne = false;
            falling = false;
        }

        // ---- Sıkışma emniyeti (2026-09-05) ----
        //
        // Yukarıdaki çıkış şartı `falling`'e bağlıydı ve `falling` yalnızca
        // HIZLI DÜŞÜŞTE kuruluyordu. Kasanın üstüne çıkmak, basamağa binmek ya
        // da zıplayıp hemen bir yüzeye konmak `airborne`'u kuruyor ama
        // `falling`'i hiç kurmuyor — ve o durumda bayrağı indirecek hiçbir şart
        // kalmıyordu. Karakter yerde dururken sonsuza kadar zıplama
        // animasyonunda kalıyordu; oyunda görülen hata buydu.
        //
        // Panzehir düşüşe değil DURULMAYA bakıyor: dikey hız bir süre boyunca
        // sıfıra yakın kaldıysa ayaklar yerdedir. Tepe noktası da sıfırdan
        // geçiyor ama orada sadece ~0.06 saniye kalıyor, bu eşiğin altında.
        //
        // Sayaç kendini sıfırlıyor: kalkış ve düşüş eşikleri `settleSpeed`'ten
        // büyük olduğu için o karelerde `else` dalına düşülüyor.
        settleTimer = Mathf.Abs(vertical) < settleSpeed ? settleTimer + Time.deltaTime : 0f;

        if (airborne && settleTimer >= settleTime)
        {
            airborne = false;
            falling = false;
        }

        // Havalanma GECİKMELİ, iniş ANINDA. Küçük engellere takılıp bir kare
        // havalanmak animasyonu tetiklemesin, ama yere değince poz hemen
        // düzelsin — gecikmeli iniş, zıplama pozunda kayan bir karakter demek.
        airborneTime = airborne ? airborneTime + Time.deltaTime : 0f;

        animator.SetBool(AirborneParameter, airborne && airborneTime >= airborneGrace);
    }

    /// <summary>
    /// Canavara yakalanma. `RoundParticipant` çağırıyor — karar sunucuda
    /// verildiği için herkeste aynı anda oynuyor.
    ///
    /// Havada olma bayrağı temizleniyor: yakalanan yere yatıyor, o sırada
    /// düşme animasyonuna geçmesi görüntüyü bozardı.
    /// </summary>
    public void PlayDeath()
    {
        airborne = false;
        falling = false;
        airborneTime = 0f;
        settleTimer = 0f;

        if (animator != null && animator.isActiveAndEnabled)
            animator.SetBool(AirborneParameter, false);

        SetTriggerExclusive(DeathTrigger);
    }
}
