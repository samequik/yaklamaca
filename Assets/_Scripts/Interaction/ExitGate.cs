using Mirror;
using UnityEngine;

/// <summary>
/// Kaçış kapısı. Gereken terminaller bitince açılır; içinden geçen kaçan
/// kurtulur.
///
/// **Canavar geçemez.** Bunu bir kural olarak değil, fiziksel bir engelle
/// çözüyoruz: kapının ağzında duran `monsterBlocker` yalnızca canavarın
/// istemcisinde açık. Kaçanlarda kapalı olduğu için onlar geçiyor, canavar ise
/// duvara çarpıyor. Hareket istemci otoriteli olduğu için engelin de istemcide
/// olması gerekiyor — sunucudan itmeye çalışmak, tam da Source hissini
/// bozacak olan prediction kavgasını başlatırdı.
///
/// Kurtulma kararı ise **sunucuda**: tetikleyiciyi sunucu okuyor. İstemci
/// "kaçtım" diyemiyor; NetworkTransform pozisyonu zaten sunucuya taşıdığı için
/// sunucunun kendi kopyası tetikleyiciye girdiğinde karar veriliyor.
///
/// Kapının kendisi normal bir SlidingDoor — açılmasını burası tetikliyor.
/// </summary>
public class ExitGate : NetworkBehaviour
{
    [Tooltip("Kaçanın içinden geçtiği tetikleyici hacim. isTrigger açık olmalı.")]
    [SerializeField] private Collider escapeTrigger;

    [Tooltip("Sadece canavarın istemcisinde açılan katı engel. Kapının ağzını kapatmalı.")]
    [SerializeField] private Collider monsterBlocker;

    [Tooltip("Terminaller bitince açılacak kapı. Boş bırakılabilir — o zaman " +
        "geçit hep açıktır ve sadece tetikleyici çalışır.")]
    [SerializeField] private SlidingDoor door;

    [Tooltip("Kapının yanındaki kilit paneli. VARSA kapı kendiliğinden açılmaz; " +
        "kaçanın on adımlık yön dizilimini girmesi gerekir.")]
    [SerializeField] private ExitLock exitLock;

    private bool doorOpened;

    private void Update()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null)
            return;

        UpdateBlocker(manager);

        if (!isServer)
            return;

        // Kapı artık terminaller bitince KENDİLİĞİNDEN AÇILMIYOR: yanındaki
        // panelde on adımlık yön dizilimi girilmeli (ExitLock). Terminaller
        // bitince kapının açılıvermesi turun son perdesini bedavaya veriyordu.
        //
        // Panel yoksa eski davranışa düşülüyor — aksi hâlde kapı hiç açılmayan,
        // sebebi görünmeyen bir tur kilidine dönüşürdü.
        if (exitLock == null && !doorOpened
            && manager.Phase == RoundPhase.Playing && manager.ExitOpen)
        {
            doorOpened = true;

            if (door != null)
                door.SetOpen(true);
        }

        // Yeni tur: kapı kapanıyor, dizilim sıfırlanıyor. Çıkış kapısının
        // autoCloseDelay'i 0 (bir daha kapanmaz), o yüzden kapatmak buranın işi.
        if (manager.Phase == RoundPhase.Waiting)
        {
            doorOpened = false;

            if (exitLock != null)
                exitLock.ServerReset();

            if (door != null)
                door.SetOpen(false);
        }
    }

    /// <summary>
    /// Engel yalnızca canavarda katı. Rol SyncVar olduğu için her istemci
    /// kendi oyuncusunun rolünü zaten biliyor; ayrıca bir şey yollamaya gerek yok.
    /// </summary>
    private void UpdateBlocker(RoundManager manager)
    {
        if (monsterBlocker == null)
            return;

        bool localIsMonster = false;

        if (NetworkClient.localPlayer != null)
        {
            RoundParticipant local = NetworkClient.localPlayer.GetComponent<RoundParticipant>();
            localIsMonster = local != null && local.Role == RoundRole.Monster;
        }

        if (monsterBlocker.enabled != localIsMonster)
            monsterBlocker.enabled = localIsMonster;
    }

    /// <summary>
    /// Kurtulma kararı sunucuda. Kaçan kapıdan geçtiği anda haritadan çekiliyor
    /// ve izleyici moduna düşüyor — elenen biriyle aynı şekilde, ama "öldü"
    /// değil "kurtuldu" sayılıyor.
    ///
    /// Tur burada bitmiyor: sahada başka kaçan varsa onlar oynamaya devam
    /// ediyor, kurtulan da onları izliyor.
    /// </summary>
    private void OnTriggerEnter(Collider other) => ReportEscapeTrigger(other);

    /// <summary>
    /// Kaçan tetikleyiciden geçti.
    ///
    /// **Ayrı bir public metot, çünkü Unity mesajı buraya göndermiyor.** Trigger
    /// olayları collider'ın kendi objesine gidiyor; tetikleyici `Tetik`
    /// çocuğunda, bu bileşen ise geçidin kökünde. `ExitTriggerRelay` iletiyor.
    /// Aynı objede bir collider olursa OnTriggerEnter de buraya düşüyor.
    /// </summary>
    public void ReportEscapeTrigger(Collider other)
    {
        if (!isServer)
            return;

        RoundManager manager = RoundManager.Instance;
        if (manager == null)
            return;

        // Tetik doğru çalışıyor olsa bile bu iki şart sessizce reddediyordu —
        // "tetiğe çarpıyoruz ama hiçbir şey olmuyor" burada geliyordu ve
        // kablolamada hiçbir hata yokken bile hata gibi görünüyordu
        // (2026-09-13, oynanış geri bildirimi). En sık sebep: gereken
        // terminal sayısı henüz tamamlanmadan çıkış denenmesi — ExitOpen
        // tam olarak bunu ölçüyor.
        if (manager.Phase != RoundPhase.Playing)
        {
            Debug.Log($"{name}: tetik çalıştı ama tur oynanmıyor (Phase={manager.Phase}).", this);
            return;
        }

        if (!manager.ExitOpen)
        {
            Debug.Log($"{name}: tetik çalıştı ama çıkış henüz açık DEĞİL " +
                $"({manager.CompletedTerminals}/{manager.RequiredTerminals} terminal tamamlandı).", this);
            return;
        }

        RoundParticipant participant = other.GetComponentInParent<RoundParticipant>();
        if (participant == null || participant.Role != RoundRole.Runner)
        {
            Debug.Log($"{name}: tetik çalıştı ama geçen kaçan değil ({other.name}).", this);
            return;
        }

        manager.ReportEscaped(participant);
    }

    /// <summary>
    /// override şart: NetworkBehaviour'un kendi OnValidate'i NetworkIdentity
    /// kontrolünü yapıyor. Gizleseydik o kontrol sessizce devre dışı kalırdı.
    /// </summary>
    protected override void OnValidate()
    {
        base.OnValidate();

        // Tetikleyicinin isTrigger'ı kapalıysa kaçan kapıya çarpar ve hiç
        // geçemez; sessizce çalışmayan bir kapı yerine burada uyarıyoruz.
        if (escapeTrigger != null && !escapeTrigger.isTrigger)
            Debug.LogWarning($"{name}: escapeTrigger'ın isTrigger'ı kapalı.", this);

        if (monsterBlocker != null && monsterBlocker.isTrigger)
            Debug.LogWarning($"{name}: monsterBlocker katı olmalı, isTrigger kapatılmalı.", this);
    }
}
