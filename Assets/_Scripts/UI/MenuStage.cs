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
///
/// ### Sahne aynı zamanda kostüm ÖNİZLEMESİ
///
/// Arkadaki iki figür oyuncunun kendi seçtiği kostümleri giyiyor
/// (<see cref="CharacterCatalog"/>), yani seçim ekranı ayrı bir önizleme
/// penceresi kurmuyor: <see cref="SetFocus"/> kamerayı seçilen karakterin
/// üstüne alıyor ve öbürünü gizliyor. Ayrı bir önizleme, ışıkları ve kamerayı
/// ikinci kez kurmak demekti.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MenuStage : MonoBehaviour
{
    /// <summary>Kameranın ne göstereceği.</summary>
    public enum Focus
    {
        /// <summary>İkisi yan yana — menünün olağan arka planı.</summary>
        Pair,

        /// <summary>Yalnızca kaçan, ekranın sağında — kostüm seçimi.</summary>
        Runner,

        /// <summary>Yalnızca canavar, ekranın sağında — kostüm seçimi.</summary>
        Monster,
    }

    /// <summary>Sahne kökünün sahnedeki adı — editör aracı bu adla kuruyor.</summary>
    public const string StageName = "MenuSahnesi";

    // Sahnedeki tek örnek. Dışarı AÇILMIYOR: çağıranlar aşağıdaki statik
    // metotları kullanıyor ve sahne kapalıyken de çağırabiliyorlar. Örneği
    // vermek, her çağıranın null kontrolü yazmasını gerektirirdi.
    private static MenuStage instance;

    /// <summary>
    /// İstenen görünüm. **Statik ve bileşenden bağımsız, bilerek:** sahne
    /// menünün arka planına bağlı ve `MenuController.Show` panelleri arka
    /// plandan ÖNCE açıyor, yani seçim ekranı uyandığında sahne henüz kapalı
    /// olabiliyor. İstek burada beklerse sahne açılınca kendiliğinden
    /// uygulanıyor ve "kim önce uyandı" sorusu ortadan kalkıyor — bölüm
    /// 19'daki `SetOverlayOpen` sırası sorununun aynı çözümü.
    /// </summary>
    private static Focus requested = Focus.Pair;

    [Tooltip("Karakterlerin yavaş dönüş hızı (derece/saniye). Sıfır = sabit.")]
    [SerializeField] private float spinSpeed = 6f;

    [Tooltip("Dönüşün genliği (derece). Tam tur yerine sağa sola salınıyor: " +
        "tam dönüşte karakterin arkası da geliyor ve menüde sırt görmek kötü.")]
    [SerializeField] private float spinRange = 18f;

    [Tooltip("Seçim ekranında karakter ekranın sağına kayıyor, çünkü sol " +
        "tarafı panel kaplıyor. Kamera karakterin SOLUNA gidiyor: metre.")]
    [SerializeField] private float focusSideShift = 0.75f;

    [Tooltip("Seçim ekranında kamera karaktere bu kadar yaklaşıyor (metre).")]
    [SerializeField] private float focusDistance = 2.45f;

    [Tooltip("Kameranın yeni yerine oturma hızı. Anında ışınlamak sert duruyor.")]
    [SerializeField] private float focusLerpSpeed = 7f;

    [SerializeField] private int textureWidth = 1280;
    [SerializeField] private int textureHeight = 720;

    private RawImage image;
    private Camera stageCamera;
    private Transform runner;
    private Transform monster;
    private Renderer[] runnerRenderers;
    private Renderer[] monsterRenderers;
    private float runnerBaseYaw;
    private float monsterBaseYaw;

    // Taban açılar YALNIZCA BİR KEZ okunuyor. Her açılışta okumak sessizce
    // kayma üretirdi: `Update` figürlere taban + salınım yazıyor, yani ikinci
    // açılışta okunan değer tabanın kendisi değil salınımın o anda kaldığı yer
    // olurdu ve menü her açıldığında figürler biraz daha dönerdi.
    private bool basesCaptured;
    private Vector3 pairCameraPosition;
    private RenderTexture texture;

    private static MaterialPropertyBlock tintBlock;
    private static readonly int TintProperty = Shader.PropertyToID("_Color");

    private void OnEnable()
    {
        instance = this;
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
        Transform turntable = stage.transform.Find("Doner");

        if (stageCamera == null || turntable == null)
        {
            image.enabled = false;
            return;
        }

        pairCameraPosition = stageCamera.transform.localPosition;

        runner = turntable.Find("Kacan");
        monster = turntable.Find("Canavar");

        // Taban açılar KURULUMDAN okunuyor, koda yazılmıyor: iki figür
        // birbirine hafifçe dönük duruyor ve o açılar `MenuStageSetup`'ta.
        // Burada tekrar yazmak iki yerde tutulan bir sayı olurdu.
        if (runner != null)
        {
            if (!basesCaptured)
                runnerBaseYaw = runner.localEulerAngles.y;

            runnerRenderers = runner.GetComponentsInChildren<Renderer>(true);
        }

        if (monster != null)
        {
            if (!basesCaptured)
                monsterBaseYaw = monster.localEulerAngles.y;

            monsterRenderers = monster.GetComponentsInChildren<Renderer>(true);
        }

        basesCaptured = true;

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

        ApplyCostumesHere();
        ApplyFocus(instant: true);
    }

    private void OnDisable()
    {
        if (instance == this)
            instance = null;

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

    /// <summary>
    /// Kamerayı tek bir karakterin üstüne alır ya da ikisini birden gösterir.
    ///
    /// Sahne kapalıyken de çağrılabiliyor: istek saklanıyor ve sahne açılınca
    /// uygulanıyor.
    /// </summary>
    public static void SetFocus(Focus value)
    {
        requested = value;

        if (instance != null)
            instance.ApplyFocus(instant: false);
    }

    /// <summary>
    /// Sahnedeki iki figüre oyuncunun seçtiği kostümleri giydirir. Seçim
    /// değişince çağrılıyor, yani arka plan seçimi anında gösteriyor.
    ///
    /// Sahne yoksa hiçbir şey yapmıyor: kostüm zaten `PlayerProfile`'da ve
    /// sahne açılırken oradan okunuyor.
    /// </summary>
    public static void ApplyCostumes()
    {
        if (instance != null)
            instance.ApplyCostumesHere();
    }

    private void ApplyCostumesHere()
    {
        ApplyTint(runnerRenderers, CharacterCatalog.Runner(PlayerProfile.RunnerCostume).Tint);
        ApplyTint(monsterRenderers, CharacterCatalog.Monster(PlayerProfile.MonsterCostume).Tint);
    }

    private void ApplyFocus(bool instant)
    {
        // Odaklanılmayan figür GİZLENİYOR, silinmiyor: seçim ekranından
        // çıkınca geri gelmesi gerekiyor ve yeniden üretmek ışık/ölçek
        // kurulumunu tekrarlamak olurdu.
        if (runner != null)
            runner.gameObject.SetActive(requested != Focus.Monster);

        if (monster != null)
            monster.gameObject.SetActive(requested != Focus.Runner);

        if (instant && stageCamera != null)
            stageCamera.transform.localPosition = TargetCameraPosition();
    }

    private Vector3 TargetCameraPosition()
    {
        Transform target = requested == Focus.Runner ? runner
            : requested == Focus.Monster ? monster
            : null;

        if (target == null)
            return pairCameraPosition;

        // Kamera karakterin SOLUNA kayıyor, yani karakter ekranın sağında
        // kalıyor — solu seçim paneli kaplıyor. Yükseklik değişmiyor: göğüs
        // hizası ikisinde de doğru.
        Vector3 position = target.localPosition;
        position.x -= focusSideShift;
        position.y = pairCameraPosition.y;
        position.z -= focusDistance;

        return position;
    }

    private void Update()
    {
        // Salınım her figüre KENDİ ekseninde uygulanıyor, ortak bir tablaya
        // değil: tabla dönseydi figürler tablanın merkezi etrafında yay
        // çizerdi ve seçim ekranında odaklanılan karakter kadrajdan kayardı.
        float angle = Mathf.Sin(Time.unscaledTime * spinSpeed * Mathf.Deg2Rad) * spinRange;

        if (runner != null)
            runner.localRotation = Quaternion.Euler(0f, runnerBaseYaw + angle, 0f);

        if (monster != null)
            monster.localRotation = Quaternion.Euler(0f, monsterBaseYaw - angle, 0f);

        if (stageCamera == null)
            return;

        stageCamera.transform.localPosition = Vector3.Lerp(
            stageCamera.transform.localPosition, TargetCameraPosition(),
            1f - Mathf.Exp(-focusLerpSpeed * Time.unscaledDeltaTime));
    }

    /// <summary>Bkz. PlayerBodyVisual.ApplyTint — aynı gerekçe, aynı yöntem.</summary>
    private static void ApplyTint(Renderer[] renderers, Color tint)
    {
        if (renderers == null)
            return;

        tintBlock ??= new MaterialPropertyBlock();

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            renderers[i].GetPropertyBlock(tintBlock);
            tintBlock.SetColor(TintProperty, tint);
            renderers[i].SetPropertyBlock(tintBlock);
        }
    }

    /// <summary>Sahneden çıkarken kamerayı ve dokuyu bırakmak şart.</summary>
    private void OnDestroy() => OnDisable();
}
