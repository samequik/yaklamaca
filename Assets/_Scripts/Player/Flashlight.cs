using Mirror;
using UnityEngine;

/// <summary>
/// Açılıp kapanan el feneri.
///
/// Korku oyununda fener bir denge aracı: açıkken görüyorsun ama ışığın
/// koridorun ucundan fark ediliyor, kapalıyken görünmezsin ama kör kalırsın.
/// Bu yüzden kapatma seçeneği oynanışın parçası, sadece bir ayar değil
/// (CLAUDE.md bölüm 5: hız/gizlilik takası).
///
/// ### Neden NetworkBehaviour
///
/// Fenerin **karşı taraftan görünmesi** mekaniğin kendisi. Durum ağda
/// taşınmazsa "açarsan görünürsün" kuralı hiç işlemiyor.
///
/// Eskiden düz bir `MonoBehaviour`'dı ve `Update` **yerel oyuncu kontrolü
/// yapmadan** klavyeyi okuyordu: F'ye basınca o istemcideki BÜTÜN oyuncu
/// nesnelerinin feneri açılıyordu — kendininki de, karşıdakinin görüntüsü de.
/// Herkes birbirinin fenerini kendi tuşuyla açıp kapatıyordu.
///
/// Şimdi: girdiyi yalnızca sahibi okuyor, kararı sunucu yazıyor, sonucu
/// SyncVar herkese taşıyor. Bölüm 4'ün kuralı — his istemcide, karar sunucuda;
/// burada "his" zaten anlık değil, ışığın yanması ağ turunu bekleyebilir.
///
/// Kameranın child'ı olan bir Spot Light'a takılır.
/// </summary>
public class Flashlight : NetworkBehaviour
{
    [SerializeField] private Light spotLight;
    [SerializeField] private bool startOn = true;

    [SyncVar(hook = nameof(OnStateChanged))]
    private bool isOn = true;

    public bool IsOn => isOn;

    public override void OnStartServer() => ServerSetOn(startOn);

    // Sonradan katılan istemci de fenerleri doğru durumda görmeli.
    public override void OnStartClient() => ApplyLight(isOn);

    private void Update()
    {
        // Girdiyi YALNIZCA sahibi okuyor. Bu kontrol olmadan tuş, o istemcideki
        // her oyuncunun fenerini birden değiştiriyordu.
        if (!isLocalPlayer)
            return;

        if (KeyBindings.Pressed(GameAction.Flashlight))
            SetOn(!isOn);
    }

    /// <summary>
    /// Tur sistemi de kullanıyor: elenince sönüyor, dirilince yanıyor
    /// (`SpectatorController`).
    /// </summary>
    public void SetOn(bool on)
    {
        if (isServer)
            ServerSetOn(on);
        else if (isOwned)
            CmdSetOn(on);
    }

    [Command]
    private void CmdSetOn(bool on) => ServerSetOn(on);

    [Server]
    private void ServerSetOn(bool on)
    {
        isOn = on;

        // Sunucunun kendi ekranı hook'tan geçmiyor; host oynuyorsa ışık
        // burada uygulanmazsa yalnızca ona kapalı görünürdü.
        ApplyLight(on);
    }

    private void OnStateChanged(bool oldValue, bool newValue) => ApplyLight(newValue);

    private void ApplyLight(bool on)
    {
        if (spotLight != null)
            spotLight.enabled = on;
    }
}
