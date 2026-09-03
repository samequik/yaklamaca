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

    [Header("Nişangah")]
    [Tooltip("Ekranın ortasındaki nokta. Düğmelere nişan almayı kolaylaştırıyor.")]
    [SerializeField] private bool showCrosshair = true;

    [SerializeField] private float crosshairSize = 4f;

    [Tooltip("Kullanılabilir bir şeye bakınca noktanın büyüme oranı.")]
    [SerializeField] private float crosshairHighlightScale = 2f;

    [SerializeField] private Color crosshairColor = new Color(1f, 1f, 1f, 0.65f);
    [SerializeField] private Color crosshairHighlightColor = new Color(1f, 0.85f, 0.4f, 0.95f);

    [Tooltip("Geçici prototip arayüzü. Gerçek UI'a geçince kapat.")]
    [SerializeField] private bool showDebugPrompt = true;

    private IInteractable currentTarget;
    private string currentPrompt;
    private GUIStyle promptStyle;
    private Texture2D dotTexture;

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

    // Prototip arayüzü: nişangah + kullanma yazısı. Canvas kurmaya gerek
    // kalmasın diye OnGUI ile; kalıcı UI yapınca burası silinecek.
    private void OnGUI()
    {
        // Başka bir şey girdiyi üstlendiyse (terminal) nişangah da susuyor:
        // terminal ekranı tam ortada çiziliyor ve noktanın üstüne biniyor.
        // Zaten nişan alacak bir şey de yok — hareket bile kilitli.
        if (InputCaptured)
            return;

        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        if (showCrosshair)
            DrawCrosshair(centerX, centerY);

        if (!showDebugPrompt || string.IsNullOrEmpty(currentPrompt))
            return;

        promptStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16
        };

        GUI.Label(new Rect(centerX - 150f, centerY + 30f, 300f, 24f),
            $"[{KeyBindings.Describe(KeyBindings.Get(GameAction.Interact))}] {currentPrompt}",
            promptStyle);
    }

    /// <summary>
    /// Ortadaki nokta. Kullanılabilir bir şeye bakınca büyüyüp renk değiştiriyor —
    /// düğmeye nişan aldığını yazıyı okumadan da anlıyorsun.
    /// </summary>
    private void DrawCrosshair(float centerX, float centerY)
    {
        // 1x1 beyaz doku bir kez üretiliyor; her karede yaratmak çöp üretirdi.
        if (dotTexture == null)
        {
            dotTexture = new Texture2D(1, 1);
            dotTexture.SetPixel(0, 0, Color.white);
            dotTexture.Apply();
            dotTexture.hideFlags = HideFlags.HideAndDontSave;
        }

        bool hasTarget = currentTarget != null;
        float size = hasTarget ? crosshairSize * crosshairHighlightScale : crosshairSize;

        Color previous = GUI.color;
        GUI.color = hasTarget ? crosshairHighlightColor : crosshairColor;

        GUI.DrawTexture(new Rect(centerX - size / 2f, centerY - size / 2f, size, size), dotTexture);

        GUI.color = previous;
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
