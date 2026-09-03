using Mirror;
using UnityEngine;

/// <summary>
/// Labirentte amaçsızca dolaşan sahte kaçan. Yapay zekâ değil, **test aracı**:
/// tek başına oynarken turun başlaması için ikinci bir katılımcı, elendikten
/// sonra da izlenecek biri olsun diye var.
///
/// Bilerek basit: NavMesh yok (haritada pişirilmiş bir NavMesh de yok), yol
/// bulma yok. Bir yön seçip yürüyor, önü kapanınca veya süre dolunca yeni yön
/// seçiyor. Kovalamacaya katkısı olmayan, sadece hareket eden bir hedef.
///
/// Hareketi **sunucu** sürüyor, NetworkTransform istemcilere dağıtıyor. Botun
/// bağlantısı olmadığı için istemci otoritesi söz konusu değil — oyuncu
/// prefabının aksine burada yön ServerToClient.
///
/// Botu sahneye koymak/kaldırmak: Yakalamaca > Test Botu Ekle (kaçan)
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(RoundParticipant))]
public class TestRunnerBot : NetworkBehaviour
{
    [Tooltip("Yürüme hızı (m/s). Oyuncunun yürümesi ~3.8, sprinti ~7.6.")]
    [SerializeField] private float speed = 3.4f;

    [Tooltip("Yeni yöne dönme hızı (derece/sn).")]
    [SerializeField] private float turnSpeed = 220f;

    [Tooltip("Önünde bu mesafede duvar varsa yön değiştirir.")]
    [SerializeField] private float wallCheckDistance = 1.8f;

    [Tooltip("Duvara çarpmasa bile bu aralıkta bir süre sonra yön değiştirir (sn).")]
    [SerializeField] private Vector2 turnInterval = new Vector2(2f, 5f);

    [SerializeField] private float gravity = 20f;

    private CharacterController controller;
    private RoundParticipant participant;

    private Vector3 heading = Vector3.forward;
    private float verticalSpeed;
    private float nextTurnTime;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        participant = GetComponent<RoundParticipant>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        PickNewHeading();
    }

    private void Update()
    {
        // Karşı taraf botu NetworkTransform üzerinden görüyor; hareketi
        // istemcide de sürseydik iki farklı simülasyon çakışırdı.
        if (!isServer)
            return;

        // Elenince olduğu yerde kalsın — yakalandıktan sonra koşmaya devam
        // eden bir ceset tuhaf durur.
        if (participant != null && !participant.IsAlive)
            return;

        if (Time.time >= nextTurnTime ||
            Physics.Raycast(transform.position, heading, wallCheckDistance, ~0, QueryTriggerInteraction.Ignore))
            PickNewHeading();

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(heading, Vector3.up),
            turnSpeed * Time.deltaTime);

        // Küçük bir negatif hız zeminde kalmasını garantiliyor; sıfır olsaydı
        // CharacterController rampalarda havada sayılıp zıplayarak inerdi.
        verticalSpeed = controller.isGrounded ? -1f : verticalSpeed - gravity * Time.deltaTime;

        controller.Move((heading * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
    }

    /// <summary>
    /// Test: botu verilen noktaya ışınlar.
    ///
    /// CharacterController açıkken transform'a doğrudan yazmak güvenilir değil,
    /// kapatıp açıyoruz. NetworkTransform'un kendi teleport yolu da çağrılıyor;
    /// çağrılmazsa istemciler botu yeni noktaya doğru odanın içinden süzülerek
    /// giderken görür — ara değerleme sıçramayı yumuşatmaya çalışır.
    /// </summary>
    [Server]
    public void ServerTeleportTo(Vector3 destination)
    {
        controller.enabled = false;
        transform.position = destination;
        controller.enabled = true;

        NetworkTransformBase netTransform = GetComponent<NetworkTransformBase>();
        if (netTransform != null)
            netTransform.ServerTeleport(destination, transform.rotation);

        verticalSpeed = 0f;
        PickNewHeading();
    }

    /// <summary>
    /// Duvara bakmayan rastgele bir yön arar. Bulamazsa geri döner — çıkmaz
    /// sokakta sıkışıp titremesin diye.
    /// </summary>
    private void PickNewHeading()
    {
        for (int i = 0; i < 12; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

            if (Physics.Raycast(transform.position, candidate, wallCheckDistance * 2f, ~0,
                    QueryTriggerInteraction.Ignore))
                continue;

            heading = candidate;
            nextTurnTime = Time.time + Random.Range(turnInterval.x, turnInterval.y);
            return;
        }

        heading = -heading;
        nextTurnTime = Time.time + 1f;
    }
}
