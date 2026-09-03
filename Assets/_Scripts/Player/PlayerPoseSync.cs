using Mirror;
using UnityEngine;

/// <summary>
/// Kök transformun altında kalan iki duruş değerini karşı tarafa taşır:
/// dikey bakış (pitch) ve eğilme oranı.
///
/// NetworkTransform yalnızca **kök** objeyi senkronluyor. Yatay dönüş köke
/// yazıldığı için karşıya geçiyor, ama:
///
/// - Dikey bakış kameranın kendi localRotation'ında duruyor. Karşı tarafta
///   PlayerController kapalı olduğu için oraya kimse yazmıyordu; canavarın
///   bıçağı kameranın child'ı olduğundan, bıçak nereye bakılırsa bakılsın
///   sabit açıda kalıyordu.
/// - Eğilme, CharacterController'ın height/center'ını değiştiriyor ve gövde
///   mesh'i (CapsuleBodyVisual) buna bakıyor. Karşı tarafta height hiç
///   değişmediği için eğilen oyuncu diğer ekranlarda dimdik duruyordu.
///
/// Değerleri sahibi yazıyor: syncDirection ClientToServer. Mirror bunları
/// sunucuya taşıyor, sunucu da bileşeni kirli işaretleyip diğer istemcilere
/// yayıyor. Hareket zaten istemci otoriteli olduğu için duruşun da aynı yoldan
/// gitmesi tutarlı — ikisi de sadece görsel, oyunun kararları sunucuda.
///
/// Bunlar SyncVar; yani kaybolmuyorlar. Sonradan katılan bir istemci de
/// oyuncuları doğru duruşta görüyor.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerPoseSync : NetworkBehaviour
{
    [SyncVar] private float pitch;
    [SyncVar] private float duckFraction;

    private PlayerController controller;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
    }

    // LateUpdate: PlayerController bakışı ve eğilmeyi Update'te hesaplıyor,
    // biz de o karenin sonucunu okuyoruz. Update'te okusaydık bir kare geriden
    // gelirdi.
    private void LateUpdate()
    {
        if (isLocalPlayer)
        {
            // Mirror yalnızca değer gerçekten değiştiğinde kirli işaretliyor,
            // yani sabit dururken paket gitmiyor.
            pitch = controller.LookPitch;
            duckFraction = controller.DuckFraction;
            return;
        }

        controller.ApplyRemoteLook(pitch);
        controller.ApplyDuckGeometry(duckFraction);
    }
}
