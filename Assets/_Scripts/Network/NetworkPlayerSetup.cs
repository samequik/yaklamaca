using Mirror;
using UnityEngine;

/// <summary>
/// Doğan oyuncunun "benim mi, karşıdakinin mi" olduğuna karar verip ona göre
/// yapılandırır.
///
/// Kamera, ses dinleyicisi ve girdi yalnızca yerel oyuncuda açık olmalı; aksi
/// halde iki kamera aynı anda çizer, iki AudioListener uyarı verir ve senin
/// klavyen karşıdakinin karakterini de sürer.
///
/// Sıra güvenliği için iki kanca birlikte kullanılıyor: OnStartClient herkeste
/// çalışıp uzak yapılandırmayı uyguluyor, OnStartLocalPlayer ise sadece yerel
/// oyuncuda sonradan çalışıp üzerine yazıyor. Böylece isLocalPlayer'ın ne zaman
/// atandığına bağımlı kalmıyoruz.
/// </summary>
public class NetworkPlayerSetup : NetworkBehaviour
{
    [Header("Sadece yerel oyuncuda açık")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;

    [Tooltip("Girdi okuyan veya yalnızca sahibini ilgilendiren bileşenler.")]
    [SerializeField] private Behaviour[] localOnlyComponents;

    [Header("Görünüm")]
    [SerializeField] private CapsuleBodyVisual bodyVisual;

    [Tooltip("Rol bazlı gövde. Birinci şahıs olup olmadığını buradan öğreniyor: " +
        "kendi kafanı görmemelisin ama ellerini görmelisin.")]
    [SerializeField] private PlayerBodyVisual playerBody;

    // Devri teslim aldığımız sahne nesneleri. Oyuncu yok olunca geri veriliyor:
    // odadan ayrılan oyuncu menüye dönüyor ve o sırada sahnede çizen bir kamera
    // kalmazsa Unity "Display 1 — No cameras rendering" yazısını basıyor.
    private readonly System.Collections.Generic.List<GameObject> borrowedCameras =
        new System.Collections.Generic.List<GameObject>();

    private readonly System.Collections.Generic.List<AudioListener> borrowedListeners =
        new System.Collections.Generic.List<AudioListener>();

    public override void OnStartClient()
    {
        base.OnStartClient();
        Configure(isLocal: false);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Configure(isLocal: true);

        // Menü sahnedeki kamerayı kullanıyordu; oyuncu doğunca devri teslim.
        DisableOtherCameras();
        DisableOtherAudioListeners();

        // İmleç kilidi MenuController'ın işi. Menü varsa ona karışmıyoruz:
        // lobiye düşer düşmez imleci kilitlemek, oyuncunun daha kadroya
        // bakamadan fareyi kaybetmesi demekti. Menü yoksa (menüsüz ağ testi)
        // kilidi burası veriyor, yoksa kimse vermez.
        if (MenuController.Instance == null)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>
    /// Birinci şahıs kameranın yakın kırpma düzlemi (metre).
    ///
    /// Unity'nin varsayılanı 0.3 ve **duvarın içini gösteriyordu**. Kırpma
    /// düzlemi bir nokta değil **dikdörtgen**: 60° görüş açısı ve 16:9'da
    /// köşesi kameradan `0.3 × 1.55 ≈ 0.46 m` uzakta kalıyor. Duvar ise en
    /// fazla `yarıçap − skinWidth = 0.3048 − 0.0305 ≈ 0.274 m` yaklaşıyor, yani
    /// köşe duvarı deliyordu.
    ///
    /// **İlk denemede 0.15 yapıldı ve YETMEDİ.** İki sebep vardı:
    ///
    /// - Hesap tam sınırdaydı: 0.15'te köşe 16:9'da 0.232 m, ama `CameraBob`
    ///   kamerayı 0.032 m yana kaydırıyor (kalan pay 10 mm) ve 21:9'da köşe
    ///   0.266 m'ye çıkıp payı tüketiyor.
    /// - Asıl sebep ise kırpma değil **kameranın yeri**: eğilirken 0.25 m öne
    ///   kayıyor (bölüm 1) ve duvara yaslanıp çömelen oyuncunun kamerası duvara
    ///   0.024 m kalıyor. O mesafede hiçbir kırpma değeri iş görmez.
    ///
    /// İkincisi `PlayerController.UpdateCameraClearance` ile çözüldü: kamera
    /// artık yüzeye çarpıp duruyor. Bu sabit yalnızca **eksende duran** kamerayı
    /// karşılıyor ve 0.08'de köşe 21:9'da bile 0.142 m — sallanma payı
    /// düşüldükten sonra kalan 0.242 m'ye karşı rahat bir pay.
    ///
    /// **Sıfıra yaklaştırmak bedava değil:** kendi gövdeni birinci şahısta
    /// görüyorsun (bölüm 14) ve gizlenen kafa/boynun çevresinde kalan
    /// gerdirilmiş üçgenler kameraya yakın duruyor; yakın düzlem onları da
    /// kırpıyor.
    /// </summary>
    public const float FirstPersonNearClip = 0.08f;

    private void Configure(bool isLocal)
    {
        if (playerCamera != null)
        {
            playerCamera.enabled = isLocal;
            playerCamera.nearClipPlane = FirstPersonNearClip;
        }

        if (audioListener != null)
            audioListener.enabled = isLocal;

        if (localOnlyComponents != null)
        {
            for (int i = 0; i < localOnlyComponents.Length; i++)
            {
                if (localOnlyComponents[i] == null)
                    continue;

                // Ayak sesi HERKESTE çalmalı: kapalıysa kimse kimsenin adımını
                // duymaz ve canavarın yaklaştığı anlaşılmaz (bölüm 5'teki
                // hız/gizlilik takası). Ağ Kurulumu artık onu bu listeye
                // koymuyor, ama **prefab eski olabilir** — liste orada
                // serileştirilmiş duruyor ve kodu değiştirmek tek başına
                // yetmiyor (bölüm 16'daki tuzağın aynısı). Burada açıkça
                // dışarıda tutmak, aracı yeniden çalıştırmayı gerektirmiyor.
                if (localOnlyComponents[i] is FootstepAudio)
                {
                    localOnlyComponents[i].enabled = true;
                    continue;
                }

                localOnlyComponents[i].enabled = isLocal;
            }
        }

        // Kendi gövdeni birinci şahısta görmemelisin (sadece gölge),
        // karşıdakinin gövdesi ise görünmeli.
        if (bodyVisual != null)
            bodyVisual.SetVisible(!isLocal);

        // "Bu gövde benim mi" bilgisi yalnızca burada var: PlayerBodyVisual
        // düz bir MonoBehaviour, ağı bilmiyor ve bilmemeli.
        if (playerBody != null)
            playerBody.SetFirstPerson(isLocal);
    }

    /// <summary>
    /// Sahnede menü için duran kamerayı kapatır. Açık kalırsa iki kamera birden
    /// çizer ve hangisinin göründüğü belirsizleşir.
    /// </summary>
    private void DisableOtherCameras()
    {
        Camera[] cameras = FindObjectsOfType<Camera>();

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == playerCamera)
                continue;

            // Başka bir oyuncunun kamerası olabilir — sonradan katıldıysak
            // onunki sahnede zaten duruyor. Objesini kapatmak altındaki bıçağı
            // da görünmez yapardı; o kameranın Camera bileşenini zaten kendi
            // NetworkPlayerSetup'ı Configure(false) ile kapatıyor.
            if (cameras[i].GetComponentInParent<NetworkIdentity>() != null)
                continue;

            if (!cameras[i].gameObject.activeSelf)
                continue;

            borrowedCameras.Add(cameras[i].gameObject);
            cameras[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Kapattığımız sahne kamerasını ve dinleyicisini geri açar.
    ///
    /// Odadan ayrılınca oyuncu objesi yok ediliyor ve onunla birlikte tek çizen
    /// kamera da gidiyordu; menü ekranında "Display 1 — No cameras rendering"
    /// yazısı bundan çıkıyordu. Ödünç aldığımızı geri vermek zorundayız.
    /// </summary>
    private void OnDestroy()
    {
        for (int i = 0; i < borrowedCameras.Count; i++)
        {
            if (borrowedCameras[i] != null)
                borrowedCameras[i].SetActive(true);
        }

        for (int i = 0; i < borrowedListeners.Count; i++)
        {
            if (borrowedListeners[i] != null)
                borrowedListeners[i].enabled = true;
        }

        borrowedCameras.Clear();
        borrowedListeners.Clear();
    }

    /// <summary>
    /// Sahnede başka AudioListener bırakmaz. Unity ikinci bir dinleyici görünce
    /// uyarı yazıyor ve hangisinin duyduğu belirsizleşiyor.
    ///
    /// Kapalı objeler de taranıyor: ağ öncesinden kalan Player kapalı duruyor
    /// ama kamerasında hâlâ bir dinleyici var; ileride bir şey onu açarsa
    /// sorunun kaynağını aramak yerine baştan susturuyoruz.
    /// </summary>
    private void DisableOtherAudioListeners()
    {
        AudioListener[] listeners = FindObjectsOfType<AudioListener>(true);

        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] == audioListener)
                continue;

            // Başka bir oyuncunun dinleyicisi: onu kendi NetworkPlayerSetup'ı
            // yönetiyor, karışmıyoruz.
            if (listeners[i].GetComponentInParent<NetworkIdentity>() != null)
                continue;

            if (!listeners[i].enabled)
                continue;

            borrowedListeners.Add(listeners[i]);
            listeners[i].enabled = false;
        }
    }
}
