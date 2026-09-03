using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Yana kayan kapı. Düğmeyle veya doğrudan E ile açılıp kapanır.
/// Kovalamacada işe yarayan kısım autoCloseDelay: kapı arkandan kendi kendine
/// kapanır, kovalayan geride kalır.
/// </summary>
public class SlidingDoor : Triggerable, IInteractable
{
    [Header("Hareket")]
    [Tooltip("Kapının açılırken kaydığı yön, kendi ekseninde. Vector3.down = panel zemine gömülür, açıklık üstten aşağı büyür.")]
    [SerializeField] private Vector3 slideDirection = Vector3.down;

    [Tooltip("Dikey kapıda panel yüksekliğinden biraz fazla olmalı ki açıklık tamamen boşalsın.")]
    [SerializeField] private float slideDistance = 2.15f;

    [SerializeField] private bool startOpen;

    // Süreler kapının 3 metrelik yoluna göre ayarlı. Kapanma süresi aynı zamanda
    // "son anda altından kayarak geçme" penceresini belirliyor: eğilmiş oyuncu
    // 0.75 m açıklıktan geçebiliyor, ayaktaki 1.43 m istiyor. Aradaki fark
    // yolun ~%22'si, yani kaçan için ~0.25 sn, canavar için ~0.42 sn'lik bir an.
    [Header("Açılma/Kapanma Süresi (saniye)")]
    [Tooltip("Kaçan çalıştırdığında. Hızlı olması kapıyı kaçanın lehine bir araç yapıyor.")]
    [SerializeField] private float humanMoveTime = 1.1f;

    [Tooltip("Canavar çalıştırdığında. Yavaş olması canavarın kapıda vakit kaybetmesi için.")]
    [SerializeField] private float monsterMoveTime = 1.9f;

    [Tooltip("0 = kendi kendine kapanmaz. Kovalamacada 2-4 saniye iyi çalışır.")]
    [SerializeField] private float autoCloseDelay;

    [Tooltip("Kapı hareket hâlindeyken tekrar basılırsa olduğu yerde bu kadar " +
        "durup ters yöne gidiyor. Anında dönmek mekanik değil lastik gibi " +
        "hissettiriyordu; kısa bir duraksama yön değişimini okunur kılıyor.")]
    [SerializeField] private float reverseDelay = 0.3f;

    [Tooltip("Aynı oyuncunun kapıyı tekrar çalıştırabilmesi için geçmesi gereken " +
        "süre. KİŞİ BAŞI: başkasının basması seni bekletmiyor, iki oyuncu kapı " +
        "başında çekişebiliyor. Tek kişinin düğmeyi tıkırdatıp kapıyı yerinde " +
        "dondurmasını engelliyor.")]
    [SerializeField] private float perUserCooldown = 0.5f;

    [Header("Kullanım")]
    // Varsayılan kapalı: kapılar sadece düğmeyle açılır. Kaçarken kapıya koşup
    // doğrudan açabilmek kovalamacadaki düğme gerilimini yok ediyor.
    [SerializeField] private bool allowDirectUse;
    [SerializeField] private string openPrompt = "Kapıyı aç";
    [SerializeField] private string closePrompt = "Kapıyı kapat";

    [Header("Ses")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip moveClip;
    [SerializeField] private float moveVolume = 0.8f;

    private Vector3 closedPosition;

    // Kapının açık/kapalı olması dünya durumu: karar sunucuda, herkes aynı
    // değeri görüyor. Animasyon (progress) ise yerel — her istemci hedefe
    // kendisi yumuşatarak gidiyor, ara kareleri ağdan taşımaya gerek yok.
    [SyncVar] private bool isOpen;

    // Hız da senkron: kapıyı canavar mı insan mı çalıştırdı, animasyon
    // herkeste aynı sürede oynamalı.
    [SyncVar] private float moveTime;

    /// <summary>
    /// Yön değişiminden sonra hareketin yeniden başlayacağı **ağ zamanı**.
    ///
    /// Yerel bir sayaç değil: duraksama herkeste aynı anda başlayıp aynı anda
    /// bitmeli, yoksa kapı istemcilerde farklı yerlerde olur ve çarpışma
    /// ayrışır.
    /// </summary>
    [SyncVar] private double holdUntil;

    private float progress; // 0 = kapalı, 1 = açık
    private float autoCloseTimer;

    // Sesin bir kez çalması için: değişiklik ister sunucudan ister SyncVar'dan
    // gelsin, geçişi burada yakalıyoruz.
    private bool lastAppliedOpen;

    // Ses duraksama bitince çalıyor: kapı dururken hareket sesi duymak yanlış.
    private bool pendingMoveSound;

    // Yalnızca sunucuda: oyuncunun netId'si -> tekrar basabileceği ağ zamanı.
    // İstemcideki sayaç sadece geri bildirim; asıl engel burada.
    private readonly Dictionary<uint, double> nextUseByUser = new Dictionary<uint, double>();

    public bool IsOpen => isOpen;

    /// <summary>Açılma veya kapanma animasyonu sürüyor mu.</summary>
    public bool IsMoving => !Mathf.Approximately(progress, isOpen ? 1f : 0f);

    /// <summary>
    /// Düğme **hiçbir zaman kilitlenmiyor.** Kapı hareket hâlindeyken de
    /// basılabiliyor; son basana göre yön değiştiriyor.
    ///
    /// Eskiden hareket sırasında kilitliydi, gerekçe "yarı yolda yön değiştiren
    /// kapı zıplıyor gibi görünüyor"du. O gerekçe geçersiz: animasyon hedefe
    /// `MoveTowards` ile gidiyor, yani yön değişince bulunduğu yerden devam
    /// ediyor, hiçbir yere sıçramıyor. Kilit ise kovalamacanın tam ortasında
    /// düğmeyi ölü bir nesneye çeviriyordu.
    /// </summary>
    public override bool CanActivate => true;

    public override float UserCooldown => perUserCooldown;

    private void Awake()
    {
        closedPosition = transform.localPosition;
        isOpen = startOpen;
        lastAppliedOpen = startOpen;
        progress = startOpen ? 1f : 0f;
        moveTime = humanMoveTime;
        ApplyProgress();
    }

    /// <summary>
    /// Sonradan katılan istemci kapıyı doğru durumda bulsun; yoksa spawn anında
    /// kapı gözünün önünde kayarak açılırdı.
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        lastAppliedOpen = isOpen;
        progress = isOpen ? 1f : 0f;
        ApplyProgress();
    }

    private void Update()
    {
        // Durum değişimini tek yerde yakalıyoruz: sunucuda doğrudan, istemcide
        // SyncVar geldiğinde. İkisini ayrı ayrı ele alsaydık host'ta ses iki kez
        // çalardı.
        if (isOpen != lastAppliedOpen)
        {
            lastAppliedOpen = isOpen;
            pendingMoveSound = true;
        }

        // Yön değişiminden sonraki duraksama. Kapı olduğu yerde bekliyor;
        // ilerleme de ses de bu süre boyunca durmuş durumda.
        if (NetworkTime.time < holdUntil)
            return;

        if (pendingMoveSound)
        {
            pendingMoveSound = false;
            PlayMoveSound();
        }

        float target = isOpen ? 1f : 0f;
        if (!Mathf.Approximately(progress, target))
        {
            progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(moveTime, 0.01f));
            ApplyProgress();
        }

        // Otomatik kapanmayı yalnızca sunucu sayıyor; her istemci kendi
        // sayacını işletseydi kapılar farklı anlarda kapanırdı.
        if (!isServer || !isOpen || autoCloseDelay <= 0f)
            return;

        autoCloseTimer -= Time.deltaTime;
        if (autoCloseTimer <= 0f)
            SetOpen(false);
    }

    /// <summary>
    /// Düğmeden gelen tetikleme. İstemci karar vermiyor, sunucuya soruyor.
    /// </summary>
    public override void Activate(GameObject user, int sourceId)
    {
        if (isServer)
            ServerToggle(user, sourceId);
        else
            CmdActivate(sourceId);
    }

    /// <summary>
    /// Kapı sahnede duruyor, kimsenin sahipliğinde değil — o yüzden yetki
    /// aranmıyor. Kimin bastığını istemcinin söylemesine de gerek yok:
    /// Mirror gönderen bağlantıyı kendisi veriyor, sunucu rolü oradan okuyor.
    /// </summary>
    [Command(requiresAuthority = false)]
    private void CmdActivate(int sourceId, NetworkConnectionToClient sender = null)
    {
        ServerToggle(sender != null && sender.identity != null
            ? sender.identity.gameObject
            : null, sourceId);
    }

    [Server]
    private void ServerToggle(GameObject user, int sourceId)
    {
        if (!ServerAllowUse(user))
            return;

        // Hareket hâlindeyken basıldıysa kapı önce olduğu yerde duruyor.
        // Yalnızca yön değişiminde: kapalı kapıya basınca beklemeden açılmalı.
        holdUntil = IsMoving ? NetworkTime.time + reverseDelay : 0d;

        // Kapıyı **en son** kimin çalıştırdığı hızını belirliyor: canavar yarı
        // yolda müdahale ederse kapı oradan itibaren canavar hızında gidiyor.
        moveTime = ResolveMoveTime(user);
        SetOpen(!isOpen);

        // Düğme animasyonu buradan dağılıyor. Otomatik kapanma bu yoldan
        // geçmiyor: kapı kendi kapandığında düğme basılmış görünmemeli.
        ServerNotifyActivated(sourceId);
    }

    /// <summary>
    /// Bu oyuncu şu an basabilir mi. Basabiliyorsa sayacı da burada kuruluyor.
    ///
    /// Kontrol sunucuda çünkü istemcideki cooldown bir kural değil, nezaket:
    /// değiştirilmiş bir istemci onu atlayıp kapıyı tıkırdatarak yerinde
    /// dondurabilirdi (her basış duraksamayı uzatıyor).
    ///
    /// Kimliği okunamayan çağrı (doğrudan sunucu tetiklemesi, test) serbest
    /// bırakılıyor — cooldown oyuncular içindir.
    /// </summary>
    [Server]
    private bool ServerAllowUse(GameObject user)
    {
        if (perUserCooldown <= 0f || user == null)
            return true;

        NetworkIdentity identity = user.GetComponent<NetworkIdentity>();
        if (identity == null)
            return true;

        if (nextUseByUser.TryGetValue(identity.netId, out double allowedAt)
            && NetworkTime.time < allowedAt)
            return false;

        nextUseByUser[identity.netId] = NetworkTime.time + perUserCooldown;
        return true;
    }

    private float ResolveMoveTime(GameObject user)
    {
        if (user == null)
            return humanMoveTime;

        RoundParticipant participant = user.GetComponent<RoundParticipant>();
        return participant != null && participant.Role == RoundRole.Monster
            ? monsterMoveTime
            : humanMoveTime;
    }

    [Server]
    public void SetOpen(bool open)
    {
        isOpen = open;
        if (open)
            autoCloseTimer = autoCloseDelay;
    }

    /// <summary>
    /// Açılma/kapanma sesi. Tek klip iki yönde de kullanılıyor; kapanışta
    /// perdesi hafif düşürülüyor ki kulak ikisini ayırabilsin — kovalamacada
    /// arkandaki kapının kapandığını duymak bilgi.
    /// </summary>
    private void PlayMoveSound()
    {
        if (audioSource == null || moveClip == null)
            return;

        // isOpen artık yeni değer: açılıyorsak normal, kapanıyorsak biraz pes.
        // Kulak ikisini ayırabilsin diye — arkandaki kapının kapandığını duymak
        // kovalamacada bilgi.
        audioSource.pitch = isOpen ? 1f : 0.92f;
        audioSource.PlayOneShot(moveClip, moveVolume);
    }

    // allowDirectUse kapalıyken boş dönüyoruz: kapı sadece düğmeyle açılır,
    // oyuncu kapıya bakınca "Kapıyı aç" yazısı çıkmaz.
    public string GetPrompt()
    {
        if (!allowDirectUse)
            return null;

        return isOpen ? closePrompt : openPrompt;
    }

    public void Interact(GameObject user)
    {
        if (allowDirectUse)
            Activate(user, DirectUseSource);
    }

    private void ApplyProgress()
    {
        transform.localPosition = closedPosition + slideDirection.normalized * (slideDistance * progress);
    }
}
