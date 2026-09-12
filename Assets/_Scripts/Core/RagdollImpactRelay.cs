using UnityEngine;

/// <summary>
/// Ragdoll parçasının çarpışmasını `Corpse`'a iletir.
///
/// Parçaların Rigidbody'si ve Collider'ı `Corpse`'un kendi objesinde DEĞİL —
/// `RagdollFactory`'nin kurduğu alt kemiklerde (bkz. CLAUDE.md bölüm 21.1).
/// Unity çarpışma olaylarını collider'ın KENDİ objesine gönderiyor, parent'a
/// değil; `ExitTriggerRelay`/`RevivalStationRelay`'deki aynı tuzak ve aynı
/// çözüm (bölüm 18, 23): collider bir objede, mantık başka objede olduğunda
/// araya aktarıcı konuyor.
/// </summary>
public class RagdollImpactRelay : MonoBehaviour
{
    public Corpse Owner;

    private void OnCollisionEnter(Collision collision) => Owner?.ServerReportImpact(collision);
}
