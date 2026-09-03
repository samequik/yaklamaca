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

    private bool doorOpened;

    private void Update()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null)
            return;

        UpdateBlocker(manager);

        if (!isServer)
            return;

        // Çıkış açıldığı anda kapı bir kez açılır; SlidingDoor gerisini
        // kendi halleder ve durumu SyncVar ile herkese taşır.
        if (!doorOpened && manager.Phase == RoundPhase.Playing && manager.ExitOpen)
        {
            doorOpened = true;

            if (door != null)
                door.SetOpen(true);
        }

        // Yeni tur için sıfırla.
        if (manager.Phase == RoundPhase.Waiting)
            doorOpened = false;
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
    private void OnTriggerEnter(Collider other)
    {
        if (!isServer)
            return;

        RoundManager manager = RoundManager.Instance;
        if (manager == null || manager.Phase != RoundPhase.Playing || !manager.ExitOpen)
            return;

        RoundParticipant participant = other.GetComponentInParent<RoundParticipant>();
        if (participant == null || participant.Role != RoundRole.Runner)
            return;

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
