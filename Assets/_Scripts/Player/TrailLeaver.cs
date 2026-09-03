using Mirror;
using UnityEngine;

/// <summary>
/// Koşarken yerde iz bırakır. Sadece kaçanlar bırakır — canavarın kendi izini
/// takip etmesinin anlamı yok.
///
/// Eşik sprint hızının altında: yürüyerek ve eğilerek gidersen iz kalmıyor.
/// Böylece hız/gizlilik takasının üçüncü ayağı kuruluyor — ses zaten vardı,
/// bu da görsel iz olarak ekleniyor.
///
/// ---
///
/// Ağda kararı yerel istemci veriyor, çünkü gereken tek şey kendi hızın:
/// PlayerController karşıdaki oyuncularda kapalı olduğu için hız orada
/// okunamaz zaten. İstemci sadece "şurada iz bıraktım" diyor; izi kime
/// göstereceğine sunucu karar veriyor (bkz. TrailMarkSystem).
/// </summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(RoundParticipant))]
public class TrailLeaver : NetworkBehaviour
{
    [Tooltip("Bu hızın altında iz bırakılmaz (u/s). Yürüme 200, sprint 400.")]
    [SerializeField] private float minSpeed = 300f;

    [Tooltip("Kaç metrede bir çizik kümesi bırakılacağı. Küçük değer = daha sık iz.")]
    [SerializeField] private float markSpacing = 0.6f;

    [Tooltip("Zeminden bu kadar yüksekteyken iz bırakılır. Zıplama yüksekliği 1.14 m.")]
    [SerializeField] private float maxGroundDistance = 2.5f;

    private PlayerController controller;
    private RoundParticipant participant;
    private float distanceSinceMark;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        participant = GetComponent<RoundParticipant>();
    }

    private void Update()
    {
        // Yalnızca kendi oyuncun. Karşıdakinin izini onun istemcisi bildiriyor.
        if (!isLocalPlayer)
            return;

        RoundManager manager = RoundManager.Instance;

        if (manager == null || manager.Phase != RoundPhase.Playing)
            return;
        if (participant.Role != RoundRole.Runner || !participant.IsAlive)
            return;

        // Zeminde olma şartı yok: bunny hop yaparken oyuncu neredeyse hep
        // havada oluyor ve iz bırakmaması takibi imkânsız kılıyordu. Aşağıdaki
        // ışın zemini bulduğu sürece çizik doğru yere düşüyor.
        float speed = controller.HorizontalSpeed;
        if (speed < minSpeed)
        {
            distanceSinceMark = 0f;
            return;
        }

        distanceSinceMark += speed * PlayerController.UnitsToMeters * Time.deltaTime;
        if (distanceSinceMark < markSpacing)
            return;

        distanceSinceMark = 0f;

        // İz ayak hizasında olmalı; transform kapsülün merkezinde duruyor.
        // Zemini ışınla buluyoruz ki eğimli yüzeyde de, zıplarken de doğru
        // otursun. Işın kapsülün içinden başlıyor, kendi collider'ımıza
        // takılmıyor. Menzil zıplama yüksekliğini (1.14 m) kapsayacak kadar.
        if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit,
                maxGroundDistance, ~0, QueryTriggerInteraction.Ignore))
            return;

        // Çizikler gidiş yönüne uzanmalı, baktığın yöne değil — airstrafe
        // yaparken kafanı çevirsen de iz gerçek rotayı gösteriyor.
        Vector3 forward = controller.HorizontalVelocityDirection;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        CmdLeaveMark(hit.point, forward);
    }

    /// <summary>
    /// Sunucu bildirimi doğrular ve dağıtımı üstlenir. Doğrulama şart: aksi
    /// halde canavar oynayan değiştirilmiş bir istemci sahte iz üretip
    /// kaçanları yanıltabilir ya da kendi izini gizleyebilirdi.
    /// </summary>
    [Command]
    private void CmdLeaveMark(Vector3 position, Vector3 forward)
    {
        RoundManager manager = RoundManager.Instance;

        if (manager == null || manager.Phase != RoundPhase.Playing)
            return;
        if (participant.Role != RoundRole.Runner || !participant.IsAlive)
            return;

        TrailMarkSystem system = TrailMarkSystem.Instance;
        if (system != null)
            system.ServerSpawnCluster(position, forward);
    }
}
