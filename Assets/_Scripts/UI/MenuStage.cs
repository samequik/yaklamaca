using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
/// ### Sahne aynı zamanda karakter ÖNİZLEMESİ
///
/// Seçim ekranı ayrı bir önizleme penceresi kurmuyor: <see cref="SetFocus"/>
/// kamerayı seçilen karakterin üstüne alıyor ve öbürünü gizliyor. Ayrı bir
/// önizleme, ışıkları ve kamerayı ikinci kez kurmak demekti.
///
/// Odaktayken figürü **fareyle sürükleyerek** döndürebiliyorsun; arka planda
/// ise kendiliğinden salınıyor.
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

    [Tooltip("Arka plandaki yavaş salınımın hızı (derece/saniye). Sıfır = sabit.")]
    [SerializeField] private float spinSpeed = 6f;

    [Tooltip("Salınımın genliği (derece). Tam tur yerine sağa sola salınıyor: " +
        "arka planda tam dönüşte karakterin arkası da geliyor ve menüde sırt " +
        "görmek kötü. Seçim ekranında sınır yok, orada sen döndürüyorsun.")]
    [SerializeField] private float spinRange = 18f;

    [Tooltip("Figürlerin kameraya dönük duruşu (derece). 180 = tam karşıdan; " +
        "sapma ikisini birbirine hafifçe çeviriyor. Kurulumdan DEĞİL buradan " +
        "geliyor: duruş bir sunum tercihi ve salınımla aynı yerde durmalı.")]
    [SerializeField] private float runnerYaw = 164f;

    [SerializeField] private float monsterYaw = 198f;

    [Tooltip("Seçim ekranında fareyi sürüklerken piksel başına dönüş " +
        "(derece). 0.4 ≈ ekranı bir uçtan bir uca sürükleyince tam tur.")]
    [SerializeField] private float dragDegreesPerPixel = 0.4f;

    [Tooltip("Seçim ekranında karakter kadrajın ortasından ne kadar sağda " +
        "dursun. 0 = tam orta, 1 = kenar. Solunu seçim paneli kaplıyor.")]
    [SerializeField] private float focusSideOffset = 0.44f;

    [Tooltip("Seçim ekranında karakter dikey kadrajın ne kadarını doldursun. " +
        "1 = tam sığar, kenar payı kalmaz.")]
    [SerializeField] private float focusFill = 0.82f;

    [Tooltip("Boy ölçülemezse kullanılacak yedek (metre).")]
    [SerializeField] private float fallbackHeight = 1.6f;

    [Tooltip("Kameranın yeni yerine oturma hızı. Anında ışınlamak sert duruyor.")]
    [SerializeField] private float focusLerpSpeed = 7f;

    [SerializeField] private int textureWidth = 1280;
    [SerializeField] private int textureHeight = 720;

    private RawImage image;
    private GameObject stageRoot;
    private Camera stageCamera;
    // Kaçanın kostüm figürleri; hepsi aynı noktada, yalnızca seçili olan
    // açık. `runner` o an seçili olanı gösteriyor.
    private Transform[] runners;
    private Transform runner;
    private Transform monster;
    // Kullanıcının seçim ekranında sürükleyerek eklediği dönüş. Odak
    // değişince sıfırlanıyor: her karakter sana dönük başlamalı.
    private float dragYaw;
    private float lastMouseX;
    private bool dragging;

    // Kameranın taban konumu YALNIZCA BİR KEZ okunuyor. Her açılışta okumak
    // sessizce kayma üretirdi: `Update` kamerayı hedefe doğru kaydırıyor, yani
    // ikinci açılışta okunan şey tabanın kendisi değil hareketin kaldığı yer
    // olurdu ve menü her açıldığında kamera biraz daha kayardı.
    //
    // Figürlerin duruşu aynı sebeple transformdan okunmuyor: `runnerYaw` ve
    // `monsterYaw` alanlarından geliyor.
    private bool basesCaptured;
    private Vector3 pairCameraPosition;
    private Vector3 targetCameraPosition;
    private RenderTexture texture;

    private void OnEnable()
    {
        instance = this;
        image = GetComponent<RawImage>();

        stageRoot = FindStage(gameObject.scene);

        if (stageRoot == null)
        {
            // Araç çalıştırılmamışsa sahne yok. Sessizce kapanıyoruz: menü
            // yine çalışıyor, arkası düz siyah kalıyor.
            image.enabled = false;
            return;
        }

        stageRoot.SetActive(true);

        stageCamera = stageRoot.GetComponentInChildren<Camera>(true);
        Transform turntable = stageRoot.transform.Find("Doner");

        if (stageCamera == null || turntable == null)
        {
            image.enabled = false;
            return;
        }

        ResolveRunners(turntable);
        monster = turntable.Find("Canavar");

        // Taban açılar KURULUMDAN okunuyor, koda yazılmıyor: iki figür
        // birbirine hafifçe dönük duruyor ve o açılar `MenuStageSetup`'ta.
        // Burada tekrar yazmak iki yerde tutulan bir sayı olurdu.
        if (!basesCaptured)
        {
            pairCameraPosition = stageCamera.transform.localPosition;
            basesCaptured = true;
        }

        // **Retro görünüm (bölüm 25): menü oyunla AYNI piksel boyunda.** Bir
        // süre menü oyunun iki katı satırla, yani daha ince piksellerle
        // çiziliyordu: oyun 3×3 ve 4×4 iken kullanıcı "ana menüdeki
        // karakterler fazla pikselli" demişti. Sonra oyun için de 2×2'yi seçti
        // ("İnce en iyisi, diğerleri çok pikselli") ve o gerekçe düştü: 2×2
        // en ince düzgün seviye, bir altı pikselleme yok demek. Menü 1080p'de
        // yine 2×2 — önceki turdaki görüntünün aynısı.
        //
        // Hesap `ScreenEffects`'te (tek kaynak), genişlik dokunun kendi 16:9
        // oranından — `RawImage` onu ekrana zaten o oranda geriyor.
        //
        // Boy serileştirilmiş alanlarda DEĞİŞTİRİLMEDİ, burada çalışma anında
        // seçiliyor: sahnede duran 1280×720 değerleri koddaki varsayılanı
        // değiştirmekle güncellenmezdi (bölüm 16'daki tuzak). Retro dokusu
        // normal dokudan büyük olmamalı; İnce'de retro yüksekliği her ekranda
        // 405 ile 675 satır arasında, yani 720'lik şart hep tutuyor.
        int width = textureWidth;
        int height = textureHeight;
        int retroHeight = ScreenEffects.RetroHeightFor(Screen.height);
        bool retro = retroHeight > 0 && retroHeight <= textureHeight;

        if (retro)
        {
            height = retroHeight;
            width = Mathf.Max(1, Mathf.RoundToInt(retroHeight * (float)textureWidth / textureHeight));
        }

        texture = new RenderTexture(width, height, 24)
        {
            name = "MenuSahnesiDokusu",
            // Kenar yumuşatma retro'da KAPALI: iri piksellerin kenarını
            // bulandırırdı. Nokta süzgeci de pikselleri büyütürken kare tutuyor.
            antiAliasing = retro ? 1 : 2,
            filterMode = retro ? FilterMode.Point : FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };

        stageCamera.targetTexture = texture;
        stageCamera.enabled = true;

        image.texture = texture;
        image.enabled = true;

        ApplyCostumesHere();
        ApplyFocus(instant: true);
    }

    /// <summary>
    /// Sahnedeki kostüm figürlerini toplar. Adları `Kacan_0`, `Kacan_1`, …
    /// ve sıraları `CharacterCatalog.Runners` ile aynı — araç ikisini birlikte
    /// kuruyor.
    /// </summary>
    private void ResolveRunners(Transform turntable)
    {
        List<Transform> found = new List<Transform>();

        for (int i = 0; ; i++)
        {
            Transform figure = turntable.Find("Kacan_" + i);

            if (figure == null)
                break;

            found.Add(figure);

            // Denetleyicisi olmayan bir `Animator` hiçbir şey oynatmıyor ve
            // ekranda T-poz olarak görünüyor. Unity bunu hata saymıyor: boş
            // referans geçerli bir durum. Bir kez yaşandı ve sebebi bulmak
            // birkaç tur sürdü — artık sahne kendisi söylüyor.
            Animator animator = figure.GetComponent<Animator>();

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError($"Menü sahnesi: '{figure.name}' figürünün animatör denetleyicisi " +
                    "YOK, ekranda T-poz duracak. `Yakalamaca > Menü Kur` çalıştır — " +
                    "denetleyici dosyası `Kaçan Modelini Kur`'dan sonra bağlanıyor.", figure);
            }
        }

        runners = found.ToArray();
    }

    /// <summary>
    /// Seçili kostümün figürünü öne alır, öbürlerini gizler. Seçim ekranı her
    /// değişiklikte çağırıyor, yani önizleme anında güncelleniyor.
    ///
    /// Sahne yoksa hiçbir şey yapmıyor: seçim zaten `PlayerProfile`'da ve
    /// sahne açılırken oradan okunuyor.
    /// </summary>
    public static void ApplyCostumes()
    {
        if (instance != null)
        {
            instance.ApplyCostumesHere();
            instance.ApplyFocus(instant: false);
        }
    }

    private void ApplyCostumesHere()
    {
        if (runners == null || runners.Length == 0)
        {
            runner = null;
            return;
        }

        int index = CharacterCatalog.SanitizeRunner(PlayerProfile.RunnerCostume);
        runner = index < runners.Length ? runners[index] : runners[0];
    }

    /// <summary>
    /// Sahne kökünü bulur.
    ///
    /// > **`GameObject.Find` KULLANILAMIYOR ve bu sessizce her şeyi bozuyordu.**
    /// > O metot yalnızca AÇIK objeleri buluyor; sahne kökü ise bilerek kapalı
    /// > kuruluyor (menü açılana kadar üç ışığı ve kamerası motorda durmasın
    /// > diye). Yani arama her seferinde null dönüyor, `RawImage` kapanıyor ve
    /// > menünün arkası hiç değişmemiş gibi düz siyah kalıyordu — hiçbir yerde
    /// > hata yazmadan.
    /// >
    /// > `Scene.GetRootGameObjects` kapalı kökleri de veriyor ve sahne kökü
    /// > gerçekten bir kök obje: `MenuStageSetup` onu ebeveynsiz kuruyor.
    /// </summary>
    private static GameObject FindStage(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == StageName)
                return roots[i];
        }

        return null;
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
        }

        if (stageRoot != null)
            stageRoot.SetActive(false);

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

    private void ApplyFocus(bool instant)
    {
        // Odaklanılmayan figür GİZLENİYOR, silinmiyor: seçim ekranından
        // çıkınca geri gelmesi gerekiyor ve yeniden üretmek ışık/ölçek
        // kurulumunu tekrarlamak olurdu.
        //
        // Gizleme ÖLÇÜMDEN ÖNCE: hedef figürün açık olduğundan emin olmalıyız,
        // yoksa `Renderer.bounds` bayat bir değer dönebiliyor.
        // SEÇİLİ OLMAYAN kostümler her durumda kapalı: açık kalsalardı
        // hepsi aynı noktada durduğu için iç içe geçmiş figürler görünürdü.
        if (runners != null)
        {
            for (int i = 0; i < runners.Length; i++)
            {
                if (runners[i] != null)
                    runners[i].gameObject.SetActive(runners[i] == runner && requested != Focus.Monster);
            }
        }

        if (monster != null)
            monster.gameObject.SetActive(requested != Focus.Runner);

        // Sürükleme sıfırlanıyor: her karakter sana dönük başlamalı, önceki
        // figürü çevirdiğin açıyla değil.
        dragYaw = 0f;
        dragging = false;

        targetCameraPosition = ComputeCameraPosition();

        if (instant && stageCamera != null)
            stageCamera.transform.localPosition = targetCameraPosition;
    }

    /// <summary>
    /// Odaklanılan figürü kadraja oturtan kamera konumu.
    ///
    /// Mesafe figürün GERÇEK boyundan hesaplanıyor, sabit yazılmıyor: canavar
    /// kaçandan belirgin şekilde iri (bölüm 17'deki bilinçli karar) ve tek bir
    /// mesafe ikisine birden uymuyor. Yeni bir model geldiğinde de elle
    /// ayarlanacak bir sayı çıkmıyor.
    /// </summary>
    private Vector3 ComputeCameraPosition()
    {
        Transform target = requested == Focus.Runner ? runner
            : requested == Focus.Monster ? monster
            : null;

        if (target == null || stageCamera == null || stageRoot == null)
            return pairCameraPosition;

        Bounds bounds = MeasureBounds(target);
        float height = bounds.size.y > 0.01f ? bounds.size.y : fallbackHeight;

        float halfTan = Mathf.Tan(stageCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float distance = height * 0.5f / Mathf.Max(0.02f, halfTan * Mathf.Clamp01(focusFill));

        // Kamera hafifçe aşağı bakıyor (kurulumdaki 4°), yani merkez ışını
        // mesafeyle birlikte aşağı kayıyor. Figürü kadrajın ortasında tutmak
        // için kamera o kadar yukarı çıkıyor.
        float drop = distance * Mathf.Tan(stageCamera.transform.localEulerAngles.x * Mathf.Deg2Rad);

        // Yatay kayma da ORANDAN geliyor, metreden değil: kadrajın genişliği
        // mesafeye bağlı ve sabit bir metre değeri iri figürü kenara iterdi.
        float halfWidth = distance * halfTan * stageCamera.aspect;

        Vector3 center = stageRoot.transform.InverseTransformPoint(bounds.center);

        return new Vector3(
            center.x - focusSideOffset * halfWidth,
            center.y + drop,
            center.z - distance);
    }

    /// <summary>
    /// Seçim ekranında fareyle döndürme.
    ///
    /// **Piksel farkı kullanılıyor, `Input.GetAxis("Mouse X")` değil.** O eksen
    /// projedeki fare hassasiyeti ayarından etkileniyor (bölüm 13) ve kare
    /// hızına göre değişiyor: aynı el hareketi farklı makinelerde farklı açı
    /// üretirdi. Piksel farkı hem sabit hem de "ekranı baştan sona sürükleyince
    /// tam tur" gibi anlaşılır bir ayar veriyor.
    ///
    /// Sürükleme yalnızca odaktayken çalışıyor; arka planda tıklamak menüyle
    /// ilgili bir iş ve figürü kazara çevirmemeli.
    /// </summary>
    private void TickDrag(bool focused)
    {
        if (!focused || !Input.GetMouseButton(0))
        {
            dragging = false;
            return;
        }

        float x = Input.mousePosition.x;

        // İlk kare yalnızca başlangıç noktasını alıyor: basıldığı anda fark
        // hesaplamak, imlecin önceki konumundan gelen bir sıçrama üretirdi.
        if (!dragging)
        {
            dragging = true;
            lastMouseX = x;
            return;
        }

        // Sağa sürüklemek figürün yüzünü sağa çeviriyor: model tarayıcılarının
        // olağan yönü, yakınındaki yüzey sürüklediğin yöne gidiyor.
        dragYaw -= (x - lastMouseX) * dragDegreesPerPixel;
        lastMouseX = x;
    }

    private static Bounds MeasureBounds(Transform target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
            return new Bounds(target.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private void Update()
    {
        bool focused = requested != Focus.Pair;

        TickDrag(focused);

        // Salınım her figüre KENDİ ekseninde uygulanıyor, ortak bir tablaya
        // değil: tabla dönseydi figürler tablanın merkezi etrafında yay
        // çizerdi ve seçim ekranında odaklanılan karakter kadrajdan kayardı.
        //
        // **Odaktayken salınım DURUYOR.** Kendiliğinden dönen bir figürü
        // fareyle çevirmek, elinden kaçan bir şeyi tutmaya benziyor: bıraktığın
        // anda kayıyor. Arka planda ise salınım figürleri canlı tutuyor.
        float angle = focused
            ? dragYaw
            : Mathf.Sin(Time.unscaledTime * spinSpeed * Mathf.Deg2Rad) * spinRange;

        if (runner != null)
            runner.localRotation = Quaternion.Euler(0f, runnerYaw + angle, 0f);

        if (monster != null)
            monster.localRotation = Quaternion.Euler(0f, monsterYaw + (focused ? angle : -angle), 0f);

        if (stageCamera == null)
            return;

        stageCamera.transform.localPosition = Vector3.Lerp(
            stageCamera.transform.localPosition, targetCameraPosition,
            1f - Mathf.Exp(-focusLerpSpeed * Time.unscaledDeltaTime));
    }

    /// <summary>Sahneden çıkarken kamerayı ve dokuyu bırakmak şart.</summary>
    private void OnDestroy() => OnDisable();
}
