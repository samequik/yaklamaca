using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menünün arkasındaki karakter sahnesi: haritadan uzakta duran küçük bir
/// platform, kendi ışıkları ve kendi kamerası. Kamera bir `RenderTexture`'a
/// çiziyor, menüdeki `RawImage` onu gösteriyor.
///
/// ### Neden RenderTexture, neden sahneyi doğrudan göstermiyoruz
///
/// Menü iki farklı durumda açılıyor ve ikisinde de **farklı bir kamera**
/// çiziyor: ana menüde sahnedeki menü kamerası, lobide ise çoktan doğmuş
/// oyuncunun kamerası (bölüm 13'teki "ödünç alınan kamera"). Arkaya karakter
/// koymanın tek yolu o kameralara karışmak olurdu — yani oyuncunun bakışına.
///
/// Ayrı bir kamera + `RenderTexture` ikisini de bağımsız kılıyor: menü hangi
/// durumda olursa olsun aynı görüntüyü alıyor, oyun kamerasına hiç
/// dokunulmuyor.
///
/// ### Görünürlük bedava geliyor
///
/// Bileşen `Arkaplan`'ın ÇOCUĞU. `MenuController.ApplyBackdrop` o objeyi
/// "menü açık ve tur oynanmıyor" kuralıyla açıp kapatıyor (bölüm 13), yani
/// sahne de tam olarak doğru anlarda görünüyor ve `MenuController`'a tek
/// satır eklemek gerekmedi. Duraklatmada arkada sahne görünmeli, orada bu
/// kapalı olmalı — kural zaten öyle diyor.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MenuStage : MonoBehaviour
{
    /// <summary>Sahne kökünün sahnedeki adı — editör aracı bu adla kuruyor.</summary>
    public const string StageName = "MenuSahnesi";

    [Tooltip("Karakterlerin yavaş dönüş hızı (derece/saniye). Sıfır = sabit.")]
    [SerializeField] private float spinSpeed = 6f;

    [Tooltip("Dönüşün genliği (derece). Tam tur yerine sağa sola salınıyor: " +
        "tam dönüşte karakterin arkası da geliyor ve menüde sırt görmek kötü.")]
    [SerializeField] private float spinRange = 18f;

    [SerializeField] private int textureWidth = 1280;
    [SerializeField] private int textureHeight = 720;

    private RawImage image;
    private Camera stageCamera;
    private Transform turntable;
    private RenderTexture texture;

    private void OnEnable()
    {
        image = GetComponent<RawImage>();

        GameObject stage = GameObject.Find(StageName);
        if (stage == null)
        {
            // Araç çalıştırılmamışsa sahne yok. Sessizce kapanıyoruz: menü
            // yine çalışıyor, arkası düz siyah kalıyor.
            image.enabled = false;
            return;
        }

        stage.SetActive(true);

        stageCamera = stage.GetComponentInChildren<Camera>(true);
        turntable = stage.transform.Find("Doner");

        if (stageCamera == null)
        {
            image.enabled = false;
            return;
        }

        texture = new RenderTexture(textureWidth, textureHeight, 24)
        {
            name = "MenuSahnesiDokusu",
            antiAliasing = 2,
            hideFlags = HideFlags.HideAndDontSave,
        };

        stageCamera.targetTexture = texture;
        stageCamera.enabled = true;

        image.texture = texture;
        image.enabled = true;
    }

    private void OnDisable()
    {
        // Kamera KAPANIYOR: menü kapalıyken küçük bir sahneyi her karede
        // çizmenin karşılığı yok. Sahne kökü de kapanıyor, yoksa ışıkları
        // motorda duruyor.
        if (stageCamera != null)
        {
            stageCamera.targetTexture = null;
            stageCamera.enabled = false;

            GameObject stage = GameObject.Find(StageName);
            if (stage != null) stage.SetActive(false);
        }

        if (image != null)
            image.texture = null;

        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
            texture = null;
        }
    }

    private void Update()
    {
        if (turntable == null || spinSpeed <= 0f)
            return;

        // Salınım, tam tur değil: tam dönüşte karakterin sırtı da geliyor ve
        // menüde sırt görmek kötü duruyor.
        float angle = Mathf.Sin(Time.unscaledTime * spinSpeed * Mathf.Deg2Rad) * spinRange;
        turntable.localRotation = Quaternion.Euler(0f, angle, 0f);
    }

    /// <summary>Sahneden çıkarken kamerayı ve dokuyu bırakmak şart.</summary>
    private void OnDestroy() => OnDisable();
}
