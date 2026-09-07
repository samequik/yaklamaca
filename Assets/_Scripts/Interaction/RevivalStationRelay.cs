using UnityEngine;

/// <summary>
/// Kabinin GÖVDESİNE bakınca da terminal çalışsın diye araya giren aktarıcı.
///
/// ### Neden gerekti
///
/// `RevivalStation` kabinin küçük terminal kutusunda duruyor; taban, tavan,
/// arka panel ve yan paneller ise onun KARDEŞLERİ. `PlayerInteractor` hedefi
/// `GetComponentInParent&lt;IInteractable&gt;()` ile buluyor, yani kabinin
/// gövdesine bakınca yukarı doğru arayınca terminale hiç ulaşamıyordu:
/// gövde ışını kesiyor ama kullanılabilir bir şey çıkmıyordu.
///
/// Sonuç oynanışta şöyle görünüyordu: ceset taşırken kabine bakıp E'ye
/// basıyorsun, yerleştirme yerine ceset **bırakılıyor** — üstelik bakılan yöne
/// bırakıldığı için tam kabinin içine düşüyor ve yerleşmiş gibi duruyor. Kabin
/// ise "bir ceset getir" demeye devam ediyordu.
///
/// Aktarıcı kabin köküne biniyor, böylece gövdenin herhangi bir parçasına
/// bakmak terminali buluyor. Aynı desen `ExitTriggerRelay`'de de var (bölüm 18):
/// collider bir objede, mantık başka objede olduğunda araya bir aktarıcı
/// konuyor.
///
/// **Çalışma anında kendini kuruyor** (`RevivalStation.Awake`), sahne
/// değişikliği gerektirmiyor.
/// </summary>
[DisallowMultipleComponent]
public class RevivalStationRelay : MonoBehaviour, IInteractable
{
    [SerializeField] private RevivalStation station;

    public void Bind(RevivalStation value) => station = value;

    public string GetPrompt() => station != null ? station.GetPrompt() : null;

    public void Interact(GameObject user)
    {
        if (station != null)
            station.Interact(user);
    }
}
