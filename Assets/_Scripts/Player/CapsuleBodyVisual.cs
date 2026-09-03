using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// CharacterController'ın görünmez kapsülüne bir mesh giydirir ve her boy
/// değişiminde (eğilme) mesh'i hull'a eşitler.
///
/// Birinci şahısta kendi gövdeni görmemen için varsayılan olarak sadece gölge
/// düşürür. Gövdeyi gerçekten görmek istersen (üçüncü şahıs bakış, ileride
/// diğer oyuncular, kovalayan bot) showBody'yi aç.
///
/// PlayerController'a değil ayrı bir bileşene koyuldu: hareket mantığıyla
/// görsel temsil ayrı sorumluluklar, ayrıca bu bileşen CharacterController'ı
/// olan her karaktere takılabilir.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CapsuleBodyVisual : MonoBehaviour
{
    [Tooltip("Collider'ı olmayan kapsül mesh. CharacterController'ın child'ı olmalı.")]
    [SerializeField] private Transform body;

    [Tooltip("Kapalıyken gövde sadece gölge düşürür — birinci şahısta görüşü kapatmaz.")]
    [SerializeField] private bool showBody;

    private CharacterController controller;
    private Renderer bodyRenderer;
    private float lastHeight = -1f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        CacheRenderer();
        ApplyVisibility();
    }

    /// <summary>
    /// Gövdeyi göster/gizle. Çok oyunculuda gerekiyor: kendi gövdeni birinci
    /// şahısta görmemelisin ama karşı oyuncunun gövdesi görünmeli.
    /// </summary>
    public void SetVisible(bool visible)
    {
        showBody = visible;
        CacheRenderer();
        ApplyVisibility();
    }

    private void LateUpdate()
    {
        if (body == null)
            return;

        // Boy değişmediyse transform'a yazmıyoruz; her karede yazmak gereksiz
        // dirty flag üretir. Eğilme dışında boy zaten sabit.
        if (Mathf.Approximately(controller.height, lastHeight))
            return;

        lastHeight = controller.height;

        // Unity'nin kapsül primitive'i 2 birim boyunda ve 0.5 yarıçapında,
        // ölçekler buna göre hesaplanıyor.
        body.localPosition = controller.center;
        body.localScale = new Vector3(
            controller.radius * 2f,
            controller.height / 2f,
            controller.radius * 2f);
    }

    private void OnValidate()
    {
        CacheRenderer();
        ApplyVisibility();
    }

    private void CacheRenderer()
    {
        bodyRenderer = body != null ? body.GetComponent<Renderer>() : null;
    }

    private void ApplyVisibility()
    {
        if (bodyRenderer == null)
            return;

        bodyRenderer.shadowCastingMode = showBody ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
    }
}
