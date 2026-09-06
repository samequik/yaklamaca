using UnityEngine;

/// <summary>
/// Kameradan ileri ışın atar, bakılan nesne IInteractable ise E tuşuyla
/// kullanır. Menzil Source'un +use mesafesine denk (85 unit ≈ 1.6 m) — kısa
/// olması kasıtlı, kovalamacada koşarken yanlışlıkla düğmeye basmayı önler.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private Transform viewTransform; // kamera
    [SerializeField] private float interactRange = 85f; // Source unit
    [Tooltip("Nişan ışınının göreceği katmanlar. Yakalamaca > Katmanları Kur " +
        "bunu Harita + Etkilesim yapıyor: duvar nişanı keser, varil kesmez.")]
    [SerializeField] private LayerMask interactMask = ~0;

    // Nişangah ve nişan yazısı ARTIK BURADA ÇİZİLMİYOR: ikisi de Canvas'a
    // taşındı (`CrosshairView`, teknik borç 2). Bu bileşen yalnızca ışını
    // atıyor ve sonucu `CurrentTarget`/`CurrentPrompt` ile yayınlıyor —
    // görüntüyü kimin çizdiğini bilmiyor.

    private IInteractable currentTarget;
    private string currentPrompt;

    /// <summary>Şu an nişan alınan kullanılabilir nesne — UI veya ses için.</summary>
    public IInteractable CurrentTarget => currentTarget;

    public string CurrentPrompt => currentPrompt;

    /// <summary>
    /// Bir bileşen etkileşim tuşunu üstlendiğinde nişan ışını duruyor.
    ///
    /// Terminal için gerekli: terminale bağlıyken oyuncu dar bir açıda etrafına
    /// bakabiliyor ve bakışı terminalden kayınca ışın onu bulamıyordu — yani
    /// **çıkmak için tekrar terminale nişan almak gerekiyordu.** Ayrıca ışın
    /// hâlâ terminali gördüğünde hem burası hem terminal aynı E'yi işleyip
    /// bağlantıyı açıp kapatıyor, ekrandaki iki yazı da üst üste biniyordu.
    /// </summary>
    public static bool InputCaptured { get; set; }

    private void OnDisable() => InputCaptured = false;

    private void Update()
    {
        if (InputCaptured)
        {
            // Nişan yazısı da susuyor: terminal kendi ekranını çiziyor.
            currentTarget = null;
            currentPrompt = null;
            return;
        }

        FindTarget();

        if (currentTarget != null && KeyBindings.Pressed(GameAction.Interact))
            currentTarget.Interact(gameObject);
    }

    private void FindTarget()
    {
        currentTarget = null;
        currentPrompt = null;

        if (viewTransform == null)
            return;

        float range = interactRange * PlayerController.UnitsToMeters;
        if (!Physics.Raycast(viewTransform.position, viewTransform.forward,
                out RaycastHit hit, range, interactMask, QueryTriggerInteraction.Ignore))
            return;

        // Çarpılan collider bir alt parça olabilir (kapı paneli, düğme kapağı),
        // bu yüzden üst objelerde de arıyoruz.
        IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
        if (interactable == null)
            return;

        // Boş prompt "şu an kullanılamaz" anlamına geliyor; hedef olarak saymıyoruz.
        string prompt = interactable.GetPrompt();
        if (string.IsNullOrEmpty(prompt))
            return;

        currentTarget = interactable;
        currentPrompt = prompt;
    }

    private void OnDrawGizmosSelected()
    {
        if (viewTransform == null)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(viewTransform.position,
            viewTransform.forward * (interactRange * PlayerController.UnitsToMeters));
    }
}
