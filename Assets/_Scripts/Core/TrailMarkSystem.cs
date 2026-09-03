using Mirror;
using UnityEngine;

/// <summary>
/// Koşan kaçanların yerde bıraktığı izleri yönetir. İzleri **sadece canavar
/// görür**; kaçan için görünmez olması gerekiyor, yoksa kendi izini takip
/// ederek nerede olduğunu unutmaz.
///
/// İzler oyun boyunca sürekli yaratılıp yok olacağı için havuzlanıyor:
/// Instantiate/Destroy döngüsü GC tıkanmasına yol açardı (CLAUDE.md bölüm 2).
/// Havuz oyun başında doldurulur, izler SetActive ile açılıp kapanır.
///
/// ---
///
/// Ağda iz **herkese değil, yalnızca canavarın bağlantısına** gönderiliyor
/// (TargetRpc). Herkese yollayıp istemcide filtrelemek daha kolay olurdu ama
/// o zaman koşan her kaçanın konumu tüm istemcilere gitmiş olurdu — oyunun
/// gizlilik mekaniğini değiştirilmiş bir istemciye bedava veren tam olarak
/// bu. Görsel filtre (ShouldShowMarks) yine de duruyor; ikinci savunma hattı.
/// </summary>
public class TrailMarkSystem : NetworkBehaviour
{
    [Tooltip("Havuz boyutu. Sprintte saniyede ~38 iz düşüyor, ömür 2.5 sn → tek oyuncu için ~95 gerekiyor.")]
    [SerializeField] private int poolSize = 256;

    [Tooltip("Bir izin ekranda kalma süresi.")]
    [SerializeField] private float markLifetime = 2.5f;

    [Header("Çizik Biçimi")]
    [Tooltip("Çiziğin uzunluğu (metre). Kısa ve ince olması ayak izi değil tırmık izi gibi durmasını sağlıyor.")]
    [SerializeField] private Vector2 markLengthRange = new Vector2(0.18f, 0.34f);

    [SerializeField] private float markWidth = 0.045f;
    [SerializeField] private Color markColor = new Color(1f, 0.18f, 0.13f);

    [Header("Küme")]
    [Tooltip("Her tetiklemede bırakılan çizik sayısı. Tek çizik yerine küme, DBD'deki dağınık görünümü veriyor.")]
    [SerializeField] private int marksPerCluster = 3;

    [Tooltip("Çiziklerin gidiş yolundan sapma yarıçapı (metre).")]
    [SerializeField] private float clusterSpread = 0.3f;

    [Tooltip("Çiziklerin gidiş yönünden sapma açısı (derece). Dağınık dursun diye.")]
    [SerializeField] private float angleJitter = 40f;

    [Tooltip("HATA AYIKLAMA: izi rolden bağımsız herkese gönderir. Yalnızca tek başına " +
        "test ederken aç. Açık bırakılırsa kaçan kendi izini görür ve takip mekaniği çöker — " +
        "bu yüzden varsayılanı kapalı.")]
    [SerializeField] private bool debugShowToEveryone;

    /// <summary>
    /// Sahnedeki tek iz sistemi. Oyuncu prefabtan doğduğu için sahnedeki
    /// bileşene referansla bağlanamıyor (bkz. RoundManager.Instance).
    /// </summary>
    public static TrailMarkSystem Instance { get; private set; }

    // Yerel oyuncu artık sahnede sabit değil, ağdan doğuyor. Her karede
    // aramamak için bulunca saklıyoruz — RoundHud ile aynı desen.
    private RoundParticipant cachedLocalViewer;

    private RoundParticipant localViewer
    {
        get
        {
            if (cachedLocalViewer != null)
                return cachedLocalViewer;

            if (NetworkClient.localPlayer != null)
                cachedLocalViewer = NetworkClient.localPlayer.GetComponent<RoundParticipant>();

            return cachedLocalViewer;
        }
    }

    private Transform[] marks;
    private Renderer[] renderers;
    private float[] expiryTimes;
    private int nextIndex;

    private MaterialPropertyBlock propertyBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private bool ShouldShowMarks =>
        debugShowToEveryone ||
        (localViewer != null && localViewer.Role == RoundRole.Monster);

    private void Awake()
    {
        Instance = this;

        propertyBlock = new MaterialPropertyBlock();
        BuildPool();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ---------- Sunucu: izi kime göstereceğine karar verir ----------

    /// <summary>
    /// Kaçanın bildirdiği izi dağıtır. Normalde yalnızca canavarın bağlantısına
    /// gider; canavar henüz belli değilse iz kimseye gösterilmez (tur başlamadan
    /// iz zaten oluşmamalı, bu sadece emniyet).
    /// </summary>
    [Server]
    public void ServerSpawnCluster(Vector3 position, Vector3 forward)
    {
        if (debugShowToEveryone)
        {
            RpcSpawnCluster(position, forward);
            return;
        }

        RoundManager manager = RoundManager.Instance;
        if (manager == null)
            return;

        RoundParticipant monster = manager.ServerFindMonster();
        if (monster == null || monster.connectionToClient == null)
            return;

        TargetSpawnCluster(monster.connectionToClient, position, forward);
    }

    [TargetRpc]
    private void TargetSpawnCluster(NetworkConnectionToClient target, Vector3 position, Vector3 forward)
        => SpawnCluster(position, forward);

    /// <summary>Sadece test anahtarı açıkken kullanılıyor.</summary>
    [ClientRpc]
    private void RpcSpawnCluster(Vector3 position, Vector3 forward)
        => SpawnCluster(position, forward);

    private void BuildPool()
    {
        marks = new Transform[poolSize];
        renderers = new Renderer[poolSize];
        expiryTimes = new float[poolSize];

        // Sprites/Default ışıktan etkilenmeyen, saydamlığı destekleyen bir
        // shader; karanlık koridorda iz görünmezse takip mekaniği çalışmaz.
        Material material = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < poolSize; i++)
        {
            GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mark.name = $"Iz_{i}";
            Destroy(mark.GetComponent<Collider>()); // iz fiziksel bir engel değil

            mark.transform.SetParent(transform, false);

            Renderer renderer = mark.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            mark.SetActive(false);

            marks[i] = mark.transform;
            renderers[i] = renderer;
        }
    }

    private void Update()
    {
        bool visible = ShouldShowMarks;

        for (int i = 0; i < poolSize; i++)
        {
            if (!marks[i].gameObject.activeSelf)
                continue;

            float remaining = expiryTimes[i] - Time.time;
            if (remaining <= 0f)
            {
                marks[i].gameObject.SetActive(false);
                continue;
            }

            renderers[i].enabled = visible;
            if (!visible)
                continue;

            // Sönerek kaybolsun: taze iz parlak, eskisi soluk. Canavar hangi
            // yöne gidildiğini bundan okuyor.
            Color color = markColor;
            color.a = Mathf.Clamp01(remaining / markLifetime);

            propertyBlock.SetColor(ColorId, color);
            renderers[i].SetPropertyBlock(propertyBlock);
        }
    }

    /// <summary>
    /// Verilen noktaya bir çizik kümesi bırakır. Tek çizik yerine küme olması
    /// ve her birinin biraz kayık/eğik durması, ayak izi yerine tırmık izi
    /// görüntüsü veriyor. Havuz doluysa en eskiler geri alınır.
    ///
    /// Dışarıdan doğrudan çağrılmıyor: giriş kapısı ServerSpawnCluster, böylece
    /// izin kime gideceği kararı tek yerde kalıyor.
    /// </summary>
    private void SpawnCluster(Vector3 position, Vector3 forward)
    {
        if (marks == null || poolSize == 0)
            return;

        Vector3 right = Vector3.Cross(Vector3.up, forward);

        for (int i = 0; i < marksPerCluster; i++)
        {
            Vector3 offset =
                right * Random.Range(-clusterSpread, clusterSpread) +
                forward * Random.Range(-clusterSpread, clusterSpread);

            SpawnSingle(position + offset, forward, Random.Range(-angleJitter, angleJitter));
        }
    }

    private void SpawnSingle(Vector3 position, Vector3 forward, float angleOffset)
    {
        Transform mark = marks[nextIndex];

        // Zeminle z-fighting olmasın diye birkaç santim yukarıda.
        mark.position = position + Vector3.up * 0.02f;

        // Quad'ın görünen yüzü kendi +Z'sidir; yukarı bakması için LookRotation'ın
        // ileri parametresi Vector3.up olmalı. "up" parametresine gidiş yönünü
        // veriyoruz ki çizik koşulan yöne uzansın, sonra rastgele eğiyoruz.
        mark.rotation = Quaternion.AngleAxis(angleOffset, Vector3.up)
            * Quaternion.LookRotation(Vector3.up, forward);

        // Uzunluk her çizikte biraz farklı; birbirinin kopyası durmasın.
        mark.localScale = new Vector3(
            markWidth,
            Random.Range(markLengthRange.x, markLengthRange.y),
            1f);

        expiryTimes[nextIndex] = Time.time + markLifetime;
        mark.gameObject.SetActive(true);

        nextIndex = (nextIndex + 1) % poolSize;
    }
}
