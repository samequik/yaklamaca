using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sol üstteki mini haritanın çalışma anı parçası: kendi konumunu haritaya
/// göre bir noktaya çevirip rolüne göre renklendiriyor.
///
/// **Sabit yönlü — Pac-Man tarzı.** İzlenen bakışa göre DÖNMÜYOR, harita hep
/// aynı yöne bakıyor.
///
/// **Yalnızca KENDİ konumun gösteriliyor, bilerek.** 2026-09-14'te
/// tartışılan "kaçan rolleri + çok bilgili mini harita" fikrinin (CLAUDE.md)
/// en sade hâli: terminal, takım arkadaşı, ceset ya da canavarın yeri YOK.
/// O yüzden o fikirdeki denge/sızıntı sorularının hiçbiri burada geçerli
/// değil — zaten bildiğin kendi konumunu görmek kimseye bir avantaj
/// vermiyor, hiçbir yeni ağ verisi de gerektirmiyor (konum zaten
/// `NetworkTransform` ile senkron, bölüm 4).
///
/// Duvar konumları ve harita sınırları (`worldMin`/`worldMax`) çalışma
/// anında değil, **kurulumda** (`Yakalamaca > Menü Kur` →
/// `MenuSetup.BuildMinimap`) sahneden ÖLÇÜLÜP yazılıyor — elle
/// değiştirilmemeli. Harita büyürse (yeni bir kanat gelirse) `Menü Kur`
/// yeniden çalıştırılınca sınırlar kendiliğinden güncellenir.
/// </summary>
public class MiniMapView : MonoBehaviour
{
    [SerializeField] private Vector2 worldMin;
    [SerializeField] private Vector2 worldMax;
    [SerializeField] private RectTransform drawArea;
    [SerializeField] private RectTransform selfDot;
    [SerializeField] private Image selfDotImage;

    [SerializeField] private Color runnerColor = new Color(0.30f, 0.85f, 0.40f, 1f);
    [SerializeField] private Color monsterColor = new Color(0.88f, 0.28f, 0.22f, 1f);

    /// <summary>
    /// Demo geri bildiriminden sonra GEÇİCİ olarak kapatıldı (2026-09-16):
    /// istenen görünüme henüz oturmadı ve karışık duruyor. `MiniMapView` bu
    /// panelin KENDİSİNE ekli (bkz. `MenuSetup.BuildMinimap` — panel arka
    /// planı, çerçeve, duvar noktaları ve kendi noktan hepsi bu objenin
    /// altında), yani objeyi burada kapatmak hepsini birden gizliyor. Sahneyi
    /// yeniden kurmaya gerek yok; tasarım oturunca bu satır kaldırılacak.
    /// </summary>
    private void Awake() => gameObject.SetActive(false);

    /// <summary>
    /// Dünya konumunu (X, Z) çizim alanı içinde YEREL bir noktaya çevirir.
    /// `MenuSetup.BuildMinimap` duvar noktalarını çizerken de AYNI formülü
    /// kullanıyor — iki yerde aynı matematiği ayrı ayrı yazmamak için tek,
    /// paylaşılan bir metot.
    /// </summary>
    public static Vector2 WorldToMapPoint(Vector3 worldPosition, Vector2 worldMin, Vector2 worldMax,
        Vector2 areaSize)
    {
        Vector2 span = worldMax - worldMin;
        float normX = span.x > 0.01f ? Mathf.InverseLerp(worldMin.x, worldMax.x, worldPosition.x) : 0.5f;
        float normZ = span.y > 0.01f ? Mathf.InverseLerp(worldMin.y, worldMax.y, worldPosition.z) : 0.5f;

        return new Vector2((normX - 0.5f) * areaSize.x, (normZ - 0.5f) * areaSize.y);
    }

    private void LateUpdate()
    {
        if (drawArea == null || selfDot == null)
            return;

        RoundParticipant local = RoundParticipant.Local;
        if (local == null)
        {
            selfDot.gameObject.SetActive(false);
            return;
        }

        selfDot.gameObject.SetActive(true);
        selfDot.anchoredPosition = WorldToMapPoint(
            local.transform.position, worldMin, worldMax, drawArea.rect.size);

        if (selfDotImage != null)
            selfDotImage.color = local.Role == RoundRole.Monster ? monsterColor : runnerColor;
    }
}
