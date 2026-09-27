using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sol üstteki mini harita: çevrendeki dar bir pencereyi gösteriyor ve
/// **baktığın yöne göre dönüyor** — yukarısı her zaman ilerisi.
///
/// ### Neden yeniden yazıldı (2026-09-27)
///
/// İlk sürüm TÜM haritayı 150 pikselin içine sığdırıyordu ve kullanıcı
/// "karışık" bulup kapattırmıştı (2026-09-16). Ölçüldü ve haklıydı: harita
/// ~54 × 96 m, yani 3.2 m'lik bir koridor hücresi ekranda **4 piksel**
/// kalıyordu — okunacak bir şey yok. Üstelik sabit yönlüydü (Pac-Man tarzı),
/// yani koridorda hangi yöne baktığını haritadan çıkaramıyordun.
///
/// Şimdi iki şey birden değişti: pencere DAR (çevrendeki birkaç koridor) ve
/// harita seninle DÖNÜYOR.
///
/// ### Nasıl çalışıyor: tek bir kap döndürülüyor
///
/// Duvar kareleri `mapRoot`'un altında SABİT duruyor — `Menü Kur` onları bir
/// kez yerleştiriyor ve çalışma anında hiçbiri kıpırdamıyor. Her kare
/// yalnızca kabın kendisi döndürülüp kaydırılıyor:
///
/// * `localRotation` = oyuncunun yaw'ı → baktığın yön yukarı geliyor
/// * `anchoredPosition` = döndürülmüş konumunun NEGATİFİ → kendin tam ortada
///
/// 242 kareyi tek tek hesaplamak yerine ebeveyni oynatmak, aynı sonucu bir
/// transform yazısıyla veriyor.
///
/// ### Ek ağ verisi YOK
///
/// Yalnızca KENDİ konumun gösteriliyor — terminal, takım arkadaşı, ceset ya
/// da canavarın yeri yok. 2026-09-14'te tartışılan "çok bilgili mini harita"
/// fikrinin denge ve sızıntı soruları (bölüm 4'ün "istemciye görmesi
/// gerekmeyen bilgiyi gönderme" kuralı) burada hiç doğmuyor: zaten bildiğin
/// kendi konumunu çiziyoruz.
///
/// ### Ölçüler kurulumda yazılıyor
///
/// `worldOrigin` ve `pixelsPerMeter` `Menü Kur` (`MenuSetup.BuildMinimap`)
/// tarafından sahneden ÖLÇÜLÜP yazılıyor; elle değiştirilmemeli. Harita
/// büyürse aracı yeniden çalıştırmak yetiyor.
/// </summary>
public class MiniMapView : MonoBehaviour
{
    [Tooltip("Duvar karelerini taşıyan kap. Her karede döndürülüp kaydırılan " +
        "TEK obje bu.")]
    [SerializeField] private RectTransform mapRoot;

    [Tooltip("Pencerenin ortasında duran ok. Harita döndüğü için okun kendisi " +
        "hiç dönmüyor — yukarısı zaten ilerisi.")]
    [SerializeField] private RectTransform selfMarker;

    [SerializeField] private Image selfMarkerImage;

    [Tooltip("Harita uzayının dünya başlangıcı (X, Z). Kurulum yazıyor.")]
    [SerializeField] private Vector2 worldOrigin;

    [Tooltip("Bir dünya metresinin kaç piksel olduğu. Kurulum yazıyor; " +
        "pencerenin kaç metre gösterdiğini bu belirliyor.")]
    [SerializeField] private float pixelsPerMeter = 7.4f;

    [SerializeField] private Color runnerColor = new Color(0.34f, 0.88f, 0.46f, 1f);
    [SerializeField] private Color monsterColor = new Color(0.92f, 0.30f, 0.24f, 1f);

    /// <summary>
    /// Dünya konumunu (X, Z) harita uzayına çevirir.
    ///
    /// `MenuSetup.BuildMinimap` duvarları yerleştirirken de AYNI metodu
    /// çağırıyor — iki yerde ayrı ayrı yazılan bir dönüşüm, biri değişince
    /// öbürünün sessizce kayması demek olurdu.
    /// </summary>
    public static Vector2 WorldToMapPoint(Vector3 worldPosition, Vector2 worldOrigin,
        float pixelsPerMeter)
    {
        return new Vector2(
            (worldPosition.x - worldOrigin.x) * pixelsPerMeter,
            (worldPosition.z - worldOrigin.y) * pixelsPerMeter);
    }

    /// <summary>
    /// **GEÇİCİ OLARAK KAPALI (2026-09-27, kullanıcı kararı).** Mini harita
    /// yeni bir özellik; kayıt alınana kadar çalışan hiçbir şeyi
    /// etkilememesi için panel kapatılıyor.
    ///
    /// `MiniMapView` panelin KENDİSİNE ekli (gövde, çerçeve, pencere, duvar
    /// kareleri ve ok hepsi bu objenin altında), yani objeyi burada kapatmak
    /// hepsini birden gizliyor. Sahneyi yeniden kurmaya gerek yok.
    ///
    /// **Açmak için bu satırı sil** — başka hiçbir şey gerekmiyor.
    ///
    /// Bölüm 20'nin "kendi objesini kapatan bileşen kendini bir daha
    /// açamaz" dersine takılmıyor: burada istenen tam olarak kalıcı kapanma.
    /// </summary>
    private void Awake() => gameObject.SetActive(false);

    /// <summary>
    /// `LateUpdate`, çünkü oyuncunun dönüşünü `PlayerController` `Update`'te
    /// yazıyor: bir kare bayat bir açıyla çizmemek için sonra okunuyor.
    /// </summary>
    private void LateUpdate()
    {
        if (mapRoot == null)
            return;

        RoundParticipant local = RoundParticipant.Local;
        bool hasLocal = local != null;

        if (selfMarker != null)
            selfMarker.gameObject.SetActive(hasLocal);

        // Oyuncu yokken (lobi açılırken, tur arası) haritayı olduğu yerde
        // bırakıyoruz: son kareyi donmuş göstermek, bir anda başka bir yere
        // sıçramasından iyi.
        if (!hasLocal)
            return;

        Vector2 point = WorldToMapPoint(local.transform.position, worldOrigin, pixelsPerMeter);

        // Yaw'ı OLDUĞU GİBİ yazmak doğru yönü veriyor. Unity'de ileri yön
        // (sin ψ, cos ψ), yani harita uzayında +X'ten (90° − ψ) açıda; onu
        // yukarıya (90°) taşımak için tam ψ kadar döndürmek gerekiyor.
        Quaternion spin = Quaternion.Euler(0f, 0f, local.transform.eulerAngles.y);

        mapRoot.localRotation = spin;

        // Kendi konumunu pencerenin ortasına getiren kaydırma. Dönüş kabın
        // KENDİ pivotu etrafında olduğu için kaydırmanın da döndürülmüş
        // olması gerekiyor — sırayı ters çevirmek haritayı yay çizdirirdi.
        Vector3 centred = spin * new Vector3(-point.x, -point.y, 0f);
        mapRoot.anchoredPosition = new Vector2(centred.x, centred.y);

        if (selfMarkerImage != null)
            selfMarkerImage.color = local.Role == RoundRole.Monster ? monsterColor : runnerColor;
    }
}
