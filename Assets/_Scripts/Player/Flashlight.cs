using Mirror;
using UnityEngine;

/// <summary>
/// Açılıp kapanan el feneri.
///
/// Korku oyununda fener bir denge aracı: açıkken görüyorsun ama ışığın
/// koridorun ucundan fark ediliyor, kapalıyken görünmezsin ama kör kalırsın.
/// Bu yüzden kapatma seçeneği oynanışın parçası, sadece bir ayar değil
/// (CLAUDE.md bölüm 5: hız/gizlilik takası).
///
/// ### Neden NetworkBehaviour
///
/// Fenerin **karşı taraftan görünmesi** mekaniğin kendisi. Durum ağda
/// taşınmazsa "açarsan görünürsün" kuralı hiç işlemiyor.
///
/// Eskiden düz bir `MonoBehaviour`'dı ve `Update` **yerel oyuncu kontrolü
/// yapmadan** klavyeyi okuyordu: F'ye basınca o istemcideki BÜTÜN oyuncu
/// nesnelerinin feneri açılıyordu — kendininki de, karşıdakinin görüntüsü de.
/// Herkes birbirinin fenerini kendi tuşuyla açıp kapatıyordu.
///
/// Şimdi: girdiyi yalnızca sahibi okuyor, kararı sunucu yazıyor, sonucu
/// SyncVar herkese taşıyor. Bölüm 4'ün kuralı — his istemcide, karar sunucuda;
/// burada "his" zaten anlık değil, ışığın yanması ağ turunu bekleyebilir.
///
/// Kameranın child'ı olan bir Spot Light'a takılır.
/// </summary>
public class Flashlight : NetworkBehaviour
{
    [SerializeField] private Light spotLight;

    [Tooltip("Ayak sesiyle PAYLAŞILAN 3B kaynak (Sesleri Yerleştir bağlıyor) — " +
        "açma/kapama tıkı karşı tarafça da duyulmalı, yeni bir kaynak eklemeye " +
        "gerek yok.")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip toggleClip;

    [Tooltip("Klibin kendi seviyesi fazla geldi, oynanınca yarıya indirildi " +
        "(2026-09-13). Kaynağın kalıcı volume'una DOKUNMUYOR — o ayak sesiyle " +
        "paylaşılıyor, onu değiştirmek adım sesini de kısardı. PlayOneShot'un " +
        "kendi volumeScale parametresi burada.")]
    [SerializeField] private float toggleVolume = 0.5f;

    /// <summary>
    /// Prefabtaki şiddetin çarpanı. 2026-09-19: retro görünüm gelince
    /// kullanıcı "el fenerinin ışığı çok parlak, bloom mu ne fazla gibi" dedi.
    /// Prefabta 2.6 → sahada 1.95.
    ///
    /// **Bloom'a değil fenere dokunuldu, bilerek.** Bloom eşikli (bölüm 25):
    /// yalnızca 0.32'nin üstündeki parlaklık taşıyor. Fener kısılınca onun
    /// aydınlattığı duvarlar eşiği daha az aşıyor, yani fenerin halesi de
    /// kendiliğinden küçülüyor — ama lambaların halesi (kullanıcının
    /// 2026-09-13'te ayarlattığı) hiç değişmiyor. Bloom'u kısmak ikisini
    /// birden kısardı.
    ///
    /// **Sabit, serileştirilmiş alan değil.** Prefabtaki değeri değiştirmek
    /// `Ağ Kurulumu` zincirini gerektirirdi (bölüm 7); serileştirilmiş bir
    /// alan da sonraki ayarlarda koddaki değişikliği yutardı (bölüm 16).
    ///
    /// Canavarın huzmesi (1.4, `MonsterAura`) hâlâ fenerden sönük: bölüm 5'in
    /// "huzme fenerden kısa ve sönük" kuralı korunuyor.
    /// </summary>
    private const float IntensityScale = 0.75f;

    /// <summary>
    /// Sahadaki taban şiddet (prefab × `IntensityScale`); titreme buna göre
    /// ölçekleniyor. -1 = daha okunmadı.
    /// </summary>
    private float baseIntensity = -1f;
    private RoundParticipant participant;
    [SerializeField] private bool startOn = true;

    [SyncVar(hook = nameof(OnStateChanged))]
    private bool isOn = true;

    /// <summary>
    /// Rol fener taşımaya izin veriyor mu. Canavarda fener yok — onun yerine
    /// kırmızı bir hâle var (`MonsterAura`).
    ///
    /// Bu ayrı bir bayrak, `isOn`'u sıfırlamak yerine: canavar olurken fener
    /// kapanıyor ama oyuncunun kendi tercihi korunuyor, kaçana dönünce feneri
    /// bıraktığı gibi buluyor.
    ///
    /// Ağda taşınmıyor, taşınması da gerekmiyor: `RoundParticipant.ApplyRole`
    /// rolün SyncVar hook'undan çağrılıyor, yani her istemci aynı sonucu kendi
    /// hesaplıyor (bölüm 4).
    /// </summary>
    private bool available = true;

    public bool IsOn => isOn && available;

    public override void OnStartServer() => ServerSetOn(startOn);

    // Sonradan katılan istemci de fenerleri doğru durumda görmeli.
    public override void OnStartClient() => ApplyLight(isOn);

    /// <summary>
    /// Rol feneri kaldırıyorsa ışığı söndürür ve tuşu sağırlaştırır.
    /// `RoundParticipant.ApplyRole` sürüyor.
    /// </summary>
    public void SetAvailable(bool value)
    {
        available = value;
        ApplyLight(isOn);
    }

    private void Update()
    {
        // Titreme HERKESTE çalışıyor, aşağıdaki sahiplik kontrolünden ÖNCE:
        // karşı tarafın feneri de titremeli, yoksa aynı ışığı iki oyuncu
        // farklı görürdü. `FootstepAudio`'nun uzak oyuncuda kapalı kalıp
        // adım seslerini yok etmesiyle (bölüm 12) aynı tuzak.
        TickFlicker();

        // Girdiyi YALNIZCA sahibi okuyor. Bu kontrol olmadan tuş, o istemcideki
        // her oyuncunun fenerini birden değiştiriyordu.
        if (!isLocalPlayer || !available)
            return;

        if (KeyBindings.Pressed(GameAction.Flashlight))
            SetOn(!isOn);
    }

    /// <summary>
    /// Tur sistemi de kullanıyor: elenince sönüyor, dirilince yanıyor
    /// (`SpectatorController`).
    /// </summary>
    public void SetOn(bool on)
    {
        if (isServer)
            ServerSetOn(on);
        else if (isOwned)
            CmdSetOn(on);
    }

    [Command]
    private void CmdSetOn(bool on) => ServerSetOn(on);

    [Server]
    private void ServerSetOn(bool on)
    {
        bool changed = isOn != on;
        isOn = on;

        // Sunucunun kendi ekranı hook'tan geçmiyor; host oynuyorsa ışık
        // burada uygulanmazsa yalnızca ona kapalı görünürdü.
        ApplyLight(on);

        // Yalnızca GERÇEKTEN değiştiyse çalıyor. `OnStartServer` da bu
        // metodu çağırıyor (round başında başlangıç durumunu yazmak için);
        // koruma olmasaydı host'un kulağında her oyuncu için bir tık
        // birikirdi, hiçbiri gerçek bir açma/kapama değilken.
        if (changed)
            PlayToggle();
    }

    private void OnStateChanged(bool oldValue, bool newValue)
    {
        ApplyLight(newValue);
        PlayToggle();
    }

    /// <summary>
    /// Fener tık sesi. Bilerek 3B ve karşı taraftan da duyuluyor: "açarsan
    /// görünürsün" takasının (bölüm 5) ses karşılığı — birinin feneri
    /// açtığını/kapattığını sesle de anlayabilmelisin.
    /// </summary>
    private void PlayToggle()
    {
        if (audioSource == null || toggleClip == null)
            return;

        // Kaynak FootstepAudio ile paylaşılıyor ve o her adımda `pitch`i
        // rastgele değiştiriyor; tık her zaman doğal perdede çalsın diye
        // burada sıfırlanıyor.
        audioSource.pitch = 1f;
        audioSource.PlayOneShot(toggleClip, toggleVolume);
    }

    /// <summary>
    /// Canavar yaklaştıkça fener titriyor — dehşetin ışık tarafındaki
    /// karşılığı.
    ///
    /// **Neden en etkili efekt bu:** ekranı karartan efektler zaten simsiyah
    /// bir sahnede kayboluyor (bölüm 25), ama fener sahnedeki neredeyse tek
    /// ışık kaynağı. Onu oynatmak görünürlüğü GERÇEKTEN değiştiriyor.
    /// Üstelik oynanış: tek ışığın seni ele veriyor ve tam ihtiyacın olduğu
    /// anda güvenilmez oluyor.
    ///
    /// **Her istemcide aynı hesaplanıyor.** `ScreenEffects.DreadAt` canavarın
    /// zaten senkron olan konumundan türetiliyor, yani karşı taraf da aynı
    /// titremeyi görüyor ve ağa tek bayt gitmiyor (bölüm 4'ün "zaten
    /// gönderilenden türet" kuralı).
    ///
    /// Işık AÇIK/KAPALI durumuna dokunmuyor: yalnızca şiddeti oynuyor.
    /// `isOn` bir SyncVar ve onu yerel bir efekt için kurcalamak durumu ağa
    /// yazardı.
    /// </summary>
    private void TickFlicker()
    {
        if (spotLight == null || !spotLight.enabled)
            return;

        // Bir kez, ışığa henüz hiç yazılmamışken okunuyor: prefabın değeri.
        // Sonraki karelerde `spotLight.intensity` zaten ölçeklenmiş ya da
        // titremiş oluyor, tekrar okunsaydı çarpan üst üste binerdi.
        if (baseIntensity < 0f)
            baseIntensity = spotLight.intensity * IntensityScale;

        if (participant == null)
            participant = GetComponent<RoundParticipant>();

        float dread = ScreenEffects.DreadAt(participant);

        if (dread <= 0.01f)
        {
            spotLight.intensity = baseIntensity;
            return;
        }

        // Perlin, Random değil: rastgele gürültü stroboskop gibi çırpar,
        // Perlin sürekli olduğu için ışık "bozuluyor" gibi davranıyor.
        // İki ayrı frekans üst üste binince ritim ezberlenmiyor.
        float noise = Mathf.PerlinNoise(Time.time * 11f, 0f)
            * Mathf.PerlinNoise(Time.time * 27f, 5f);

        // Karesi alınıyor: çoğu zaman tam parlaklıkta, arada sırada sert
        // düşüş. Doğrusal olsaydı sürekli yarı sönük bir fener olurdu.
        float dip = Mathf.Lerp(1f, noise * noise * 4f, dread);

        spotLight.intensity = baseIntensity * Mathf.Clamp01(dip);
    }

    private void ApplyLight(bool on)
    {
        if (spotLight != null)
            spotLight.enabled = on && available;
    }
}
