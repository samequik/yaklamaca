/// <summary>
/// Bir karede karakterin ne yapmak istediği. Klavye/mouse'tan da gelebilir,
/// AI'dan da — PlayerController hangisinden geldiğini bilmez.
/// </summary>
public struct MovementIntent
{
    public float moveRight;   // -1..1
    public float moveForward; // -1..1
    public float lookYaw;     // bu karede dönülecek derece (yatay)
    public float lookPitch;   // bu karede dönülecek derece (dikey)
    public bool sprint;
    public bool jumpPressed;   // bu kare basıldı
    public bool jumpHeld;      // basılı tutuluyor
    public bool crouch;        // basılı tutuluyor
    public bool crouchPressed; // bu kare basıldı — kaymayı tetikler
}

/// <summary>
/// Karakteri süren girdi kaynağı. İki uygulaması var: oyuncunun klavyesi ve
/// canavar AI'ı. Ayrım spekülatif değil — canavar oyuncusu oyundan koptuğunda
/// AI aynı karakteri devralacak (bkz. CLAUDE.md, soyutlama kuralı).
/// </summary>
public interface IMovementInputSource
{
    MovementIntent Read();
}
