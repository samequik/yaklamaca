using UnityEngine;

/// <summary>
/// Klavye ve mouse'tan girdi okur. Eski Input Manager kullanılıyor
/// (Input System paketi kurulu değil, bkz. CLAUDE.md bölüm 0).
///
/// Tuşlar artık burada sabit değil, <see cref="KeyBindings"/>'ten geliyor:
/// eski Input Manager'ın eksenleri çalışma anında değiştirilemediği için
/// hareket de tek tek tuşlardan okunuyor. Sonuç aynı -1/0/+1, hareketin hissi
/// değişmedi.
///
/// Fare ekseni atanabilir değil ve öyle kalmalı: "Mouse X/Y" bir tuş değil,
/// cihazın kendisi.
/// </summary>
public class PlayerInputSource : MonoBehaviour, IMovementInputSource
{
    [SerializeField] private float mouseSensitivity = 2f;

    /// <summary>Seçenekler ekranının ayarladığı fare hassasiyeti.</summary>
    public float MouseSensitivity
    {
        get => mouseSensitivity;
        set => mouseSensitivity = Mathf.Max(0.05f, value);
    }

    /// <summary>Dikey bakış ters mi. Seçenekler ekranından geliyor.</summary>
    public bool InvertLook { get; set; }

    /// <summary>
    /// Kayıtlı tercihleri uygular. Oyuncu prefabtan doğduğu için Inspector'daki
    /// değer her turda sıfırdan gelir; ayarlar cihazda saklanan yerden okunmalı.
    /// </summary>
    private void Awake()
    {
        mouseSensitivity = PlayerProfile.MouseSensitivity;
        InvertLook = PlayerProfile.InvertLook;
    }

    /// <summary>
    /// Bakış okunuyor mu.
    ///
    /// TAB paneli açıkken kapanıyor: imleç serbest ve fare arayüzde geziniyor,
    /// o hareketi karaktere de vermek ekranı savururdu. Hareket ve zıplama
    /// etkilenmiyor — panel bir **duraklatma değil**, oyuncu koşmaya devam
    /// edebiliyor.
    ///
    /// Girdi kaynağını komple sökmek (menünün yaptığı) burada yanlış olurdu:
    /// o zaman hareket de kesilirdi.
    /// </summary>
    public bool LookEnabled { get; set; } = true;

    public MovementIntent Read()
    {
        float pitch = LookEnabled ? Input.GetAxis("Mouse Y") * mouseSensitivity : 0f;

        return new MovementIntent
        {
            moveRight = KeyBindings.Axis(GameAction.Left, GameAction.Right),
            moveForward = KeyBindings.Axis(GameAction.Back, GameAction.Forward),
            lookYaw = LookEnabled ? Input.GetAxis("Mouse X") * mouseSensitivity : 0f,
            lookPitch = InvertLook ? -pitch : pitch,
            sprint = KeyBindings.Held(GameAction.Sprint),
            jumpPressed = KeyBindings.Pressed(GameAction.Jump),
            jumpHeld = KeyBindings.Held(GameAction.Jump),
            crouch = KeyBindings.Held(GameAction.Crouch),
            crouchPressed = KeyBindings.Pressed(GameAction.Crouch)
        };
    }
}
