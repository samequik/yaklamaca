using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ekranın ortasındaki nokta ve altındaki nişan yazısı.
///
/// `PlayerInteractor.OnGUI`'nin yerini alıyor (teknik borç 2). Davranış birebir
/// korundu: nokta kullanılabilir bir şeye bakınca **büyüyüp renk değiştiriyor**
/// — düğmeye nişan aldığını yazıyı okumadan da anlıyorsun.
///
/// ### Neden veri kaynağı her karede aranıyor
///
/// Nişangah sahnede duran bir HUD parçası, `PlayerInteractor` ise oyuncuyla
/// birlikte **ağdan doğuyor ve odadan çıkınca yok oluyor**. Sabit bir referans
/// tutmak, ikinci turda ölü bir bileşene bakmak olurdu. Bulunan kaynak
/// saklanıyor ve yalnızca kaybolunca yeniden aranıyor.
///
/// ### `InputCaptured` nişangahı da susturuyor
///
/// Terminal ya da kilit paneli girdiyi üstlendiğinde nokta gizleniyor: o
/// ekranlar tam ortada çiziliyor ve noktanın üstüne biniyor. Zaten nişan
/// alınacak bir şey de yok — hareket bile kilitli.
/// </summary>
public class CrosshairView : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform dot;
    [SerializeField] private Image dotImage;
    [SerializeField] private TMP_Text promptLabel;

    [Header("Nokta")]
    [SerializeField] private float size = 5f;

    [Tooltip("Kullanılabilir bir şeye bakınca noktanın büyüme oranı.")]
    [SerializeField] private float highlightScale = 2f;

    [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.65f);
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.4f, 0.95f);

    private PlayerInteractor interactor;

    private void Update()
    {
        bool active = GameHud.Visible && !PlayerInteractor.InputCaptured && ResolveInteractor();

        GameHud.SetVisible(group, active);

        if (!active)
            return;

        bool hasTarget = interactor.CurrentTarget != null;

        if (dot != null)
        {
            float current = hasTarget ? size * highlightScale : size;
            dot.sizeDelta = new Vector2(current, current);
        }

        if (dotImage != null)
            dotImage.color = hasTarget ? highlightColor : idleColor;

        if (promptLabel == null)
            return;

        string prompt = interactor.CurrentPrompt;

        if (string.IsNullOrEmpty(prompt))
        {
            promptLabel.SetText(string.Empty);
            return;
        }

        // Tuş her karede okunuyor: oyuncu atamayı değiştirince yazı da
        // değişmeli, yoksa ekranda artık geçersiz bir tuş yazar.
        string key = KeyBindings.Describe(KeyBindings.Get(GameAction.Interact));
        promptLabel.SetText($"[{key}] {prompt}");
    }

    private bool ResolveInteractor()
    {
        if (interactor != null)
            return true;

        if (NetworkClient.localPlayer != null)
            interactor = NetworkClient.localPlayer.GetComponent<PlayerInteractor>();

        return interactor != null;
    }
}
