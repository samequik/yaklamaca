using UnityEngine;

/// <summary>
/// Çıkış tetikleyicisinin üstünde durur ve olayı `ExitGate`'e iletir.
///
/// **Neden gerekli:** Unity trigger mesajlarını collider'ın **kendi objesine**
/// gönderiyor (ve collider bir Rigidbody'ye bağlıysa onunkine). `ExitGate`
/// geçidin kökünde, tetikleyici collider ise `Tetik` çocuğunda — yani
/// `ExitGate.OnTriggerEnter` hiç çağrılmıyordu ve **çıkıştan geçmek hiçbir şey
/// yapmıyordu.** Kaçan kapıdan geçiyor, ne kurtuluyor ne izleyiciye düşüyordu.
///
/// Köke kinematik bir Rigidbody eklemek de çözerdi, ama o canavar engelini de
/// aynı gövdeye bağlardı; bu yol fiziğe hiç dokunmuyor.
/// </summary>
public class ExitTriggerRelay : MonoBehaviour
{
    [Tooltip("Olayın iletileceği geçit. Boşsa üst objelerde aranıyor.")]
    [SerializeField] private ExitGate gate;

    private void Awake()
    {
        if (gate == null)
            gate = GetComponentInParent<ExitGate>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (gate != null)
            gate.ReportEscapeTrigger(other);
    }
}
