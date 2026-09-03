using Mirror;
using UnityEngine;

/// <summary>
/// Bir düğmenin tetikleyebildiği her şeyin ortak atası: kapı, asansör, ışık,
/// alarm. Düğme neyi tetiklediğini bilmez, sadece Activate çağırır — böylece
/// yeni bir tetiklenebilir tür eklerken düğme kodunu değiştirmen gerekmez.
///
/// **NetworkBehaviour**, çünkü tetiklenebilir olan her şey dünya durumunu
/// değiştiriyor ve dünya durumu herkeste aynı olmak zorunda. Düz MonoBehaviour
/// olduğu sürece düğmeye basan oyuncunun kapısı açılıyor, karşı taraf kapalı
/// görüyordu; çarpışma bile istemciler arasında ayrışıyordu.
/// </summary>
public abstract class Triggerable : NetworkBehaviour
{
    /// <summary>
    /// Şu an tetiklenebilir mi. Düğme buna bakıp kendini kilitliyor ve
    /// kilitliyken yazı bile çıkmıyor.
    ///
    /// Varsayılan açık. Kapı bir ara bunu hareket sırasında kapatıyordu; o
    /// kilit kaldırıldı (bkz. SlidingDoor.CanActivate) çünkü kovalamacanın tam
    /// ortasında düğmeyi ölü bir nesneye çeviriyordu. Yeni bir tetiklenebilir
    /// türü gerçekten "şimdi olmaz" diyorsa burayı geçersiz kılabilir.
    /// </summary>
    public virtual bool CanActivate => true;

    /// <summary>
    /// Düğmesiz doğrudan kullanım (kapıya bakıp E). Hiçbir düğmenin
    /// kardeş sırası negatif olmadığı için hiçbiri basılmış görünmüyor.
    /// </summary>
    public const int DirectUseSource = -1;

    /// <summary>
    /// Tetikleyen oyuncu; kimin çalıştırdığı önemliyse kullanılır.
    ///
    /// `sourceId` hangi düğmenin bastığını taşıyor — bkz. Activated.
    /// </summary>
    public abstract void Activate(GameObject user, int sourceId);

    /// <summary>
    /// Aynı oyuncunun bunu tekrar çalıştırabilmesi için geçmesi gereken süre.
    ///
    /// **Kişi başı**, nesne başı değil: başkasının basması seni bekletmemeli.
    /// Düğme bu değeri okuyup kendi yerel geri bildirimini aynı süreye
    /// ayarlıyor — asıl kural sunucuda, buradaki yalnızca "bastım ama olmadı"
    /// hissini önlemek için.
    /// </summary>
    public virtual float UserCooldown => 0f;

    /// <summary>
    /// Bu tetiklenebilir çalıştı — bağlı düğmeler basılma animasyonunu buradan
    /// alıyor. Her istemcide tetikleniyor.
    ///
    /// Parametre **hangi düğmenin** bastığı. Aynı kapıya iki düğme bağlı ve
    /// ikisi de bu olaya abone; kimlik taşınmasaydı biri basılınca ikisi birden
    /// içeri girerdi.
    /// </summary>
    public event System.Action<int> Activated;

    /// <summary>
    /// Sunucu bunu çağırınca herkesin düğmesi basılmış görünür.
    ///
    /// **Neden düğmeye NetworkIdentity eklemiyoruz?** Haritada 10 düğme var ve
    /// hepsi tamamen görsel. Her birini ağ nesnesi yapmak, hiçbir karar
    /// taşımayan on kimlik demek. Zaten ağda olan hedef haber verince aynı
    /// sonucu bedavaya alıyoruz.
    ///
    /// Kimlik olarak düğmenin **kardeş sırası** taşınıyor: sahne dosyasından
    /// geldiği için her istemcide aynı ve kimsenin elle numara vermesi
    /// gerekmiyor. Varsayım, aynı hedefe bağlı düğmelerin aynı ebeveyn altında
    /// farklı sıralarda durması — labirentte ikisi de kapının kökünün altında.
    /// </summary>
    [Server]
    protected void ServerNotifyActivated(int sourceId) => RpcActivated(sourceId);

    [ClientRpc]
    private void RpcActivated(int sourceId) => Activated?.Invoke(sourceId);
}
