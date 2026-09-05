using UnityEngine;

/// <summary>
/// Canavarın etrafındaki kırmızı ışık. Feneri değiştiriyor: canavarda fener
/// yok, gördüğü kadarını bu ışık gösteriyor.
///
/// ### Neden fener değil
///
/// Fener kaçanın aracı ve bir takas: açarsan görürsün ama görünürsün
/// (CLAUDE.md bölüm 5). Canavarda o takas yok — zaten avlanan o. Kırmızı bir
/// hâle iki işi birden yapıyor: canavar önünü görüyor, kaçan da köşeyi dönmeden
/// kırmızının yaklaştığını fark ediyor.
///
/// ### Neden gölge açık
///
/// Gölgesiz nokta ışık duvar tanımaz; kırmızı hâle koridorun öbür tarafına
/// sızsaydı canavarın yeri duvarın arkasından belli olurdu. Bu, bölüm 4'ün
/// "istemciye görmesi gerekmeyen bilgiyi gönderme" kuralının görsel karşılığı —
/// iz sisteminin yalnızca canavara gönderilmesiyle aynı gerekçe.
///
/// Gölge `Hard`: nokta ışığın gölgesi altı yüzlü ve pahalı, oyunda böyle tek
/// ışık var ve yumuşaklık burada bir şey kazandırmıyor.
///
/// ### Rol değişince açılıp kapanıyor
///
/// `RoundParticipant.ApplyRole` sürüyor. O metot `role` SyncVar'ının hook'undan
/// çağrıldığı için her istemcide çalışıyor: hâleyi herkes aynı anda görüyor,
/// ayrı bir mesaj gerekmiyor.
/// </summary>
public class MonsterAura : MonoBehaviour
{
    [Tooltip("Canavarın etrafındaki kırmızı nokta ışığı. Ağ Kurulumu bağlıyor; " +
        "boşsa Awake kendi kuruyor.")]
    [SerializeField] private Light auraLight;

    [Tooltip("Hâlenin menzili (metre). Canavarın görme mesafesi bu — feneri " +
        "olmadığı için gördüğü kadarını bu ışık belirliyor.")]
    [SerializeField] private float range = 10f;

    [Tooltip("Hâlenin şiddeti.")]
    [SerializeField] private float intensity = 2.2f;

    [Tooltip("Hâlenin rengi.")]
    [SerializeField] private Color color = new Color(1f, 0.13f, 0.08f);

    [Tooltip("Işığın gövde üzerindeki yüksekliği (metre). Hull 1.372 m; 0.6 " +
        "göğüs hizası, ışık ne yere ne tavana yapışıyor.")]
    [SerializeField] private float height = 0.6f;

    [Header("Bakış ışığı")]
    [Tooltip("Canavarın baktığı yöne giden kırmızı huzme. Kameranın çocuğu, " +
        "yani fener gibi bakışı takip ediyor — ama kapatılamıyor.")]
    [SerializeField] private Light viewLight;

    [Tooltip("Huzmenin menzili (metre). Fener 26 m; bu bilerek çok daha kısa. " +
        "Canavar avlanan değil avlayan: koridorun sonunu görmesi kovalamacayı " +
        "bitirir, ayağının önünü görmesi yeter.")]
    [SerializeField] private float viewRange = 13f;

    [Tooltip("Huzmenin şiddeti. Fener 2.6; bu daha düşük, çünkü hâleyle " +
        "toplanıyor ve ikisi birden canavarın etrafını fazla aydınlatmamalı.")]
    [SerializeField] private float viewIntensity = 1.4f;

    [Tooltip("Huzmenin açısı (derece). Fenerinkiyle (55) yakın: dar bir huzme " +
        "canavarı el feneriyle arayan biri gibi gösteriyor, oysa o avcı.")]
    [SerializeField] private float viewAngle = 60f;

    private void Awake()
    {
        auraLight = GetOrCreateLight();
        viewLight = GetOrCreateViewLight();
    }

    /// <summary>
    /// Bakış yönüne giden kırmızı huzmeyi kurar.
    ///
    /// **Hâlenin yerine değil, YANINA.** İkisi farklı iş yapıyor: hâle
    /// canavarın çevresini gösteriyor (yandaki duvar, ayağının dibi), huzme
    /// baktığı yeri. Yalnızca hâle olunca canavar önünü yeterince göremiyordu.
    ///
    /// **Kameranın çocuğu**, hâleden farklı olarak: bakışı takip etmesi
    /// gerekiyor, fener gibi.
    ///
    /// **Kapatılamıyor**, bilerek. Fener kaçanın takası — "açarsan görürsün
    /// ama görünürsün" (bölüm 5). Canavarda o takas yok: gizlenmesi gereken o
    /// değil, zaten avlayan o. Kapatılabilir olsaydı canavar hem görünmez hem
    /// gören olurdu ve kaçanın tek erken uyarısı (kırmızının yaklaşması)
    /// ortadan kalkardı.
    ///
    /// **Gölgesi açık:** gölgesiz huzme duvarı delip yan koridora sızar ve
    /// canavarın yeri duvarın arkasından belli olur (bölüm 4).
    /// </summary>
    private Light GetOrCreateViewLight()
    {
        if (viewLight != null)
            return viewLight;

        Camera camera = GetComponentInChildren<Camera>(true);

        if (camera == null)
            return null;

        GameObject holder = new GameObject("CanavarHuzmesi");
        holder.transform.SetParent(camera.transform, false);
        holder.layer = gameObject.layer;

        Light light = holder.AddComponent<Light>();
        light.type = LightType.Spot;
        light.range = viewRange;
        light.spotAngle = viewAngle;
        light.intensity = viewIntensity;
        light.color = color;
        light.shadows = LightShadows.Hard;
        // `lightmapBakeType` YALNIZCA EDITORDE var: pisirme ayari oldugu
        // icin Unity onu build'e koymuyor ve derleme hatasi veriyordu.
        // Calisma aninda eklenen bir isik zaten pisirilemez; Unity'nin
        // varsayilani da Realtime, yani build'de davranis ayni kaliyor.
        // Editorde yine de aciyoruz: Inspector'da modun ne oldugu belli olsun.
#if UNITY_EDITOR
        light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
        light.enabled = false;

        return light;
    }

    /// <summary>
    /// Hâleyi bulur, yoksa kurar.
    ///
    /// **Neden çalışma anında.** Işığı `Ağ Kurulumu` da kuruyor, ama o araç
    /// oyuncu prefabını SIFIRDAN kuruyor: bir kez çalıştırmak canavar ve kaçan
    /// modellerini, menüyü, sesleri ve yankıyı da yeniden kurmayı gerektiriyor
    /// (bölüm 7'deki sıra). Var olan bir projede yalnızca hâle eklemek için o
    /// zinciri çalıştırmak, oturmuş şeyleri riske atmak demek.
    ///
    /// Alan serileştirilmiş olduğu için elle ya da araçla bağlanmış bir ışık
    /// varsa ona dokunulmuyor.
    /// </summary>
    private Light GetOrCreateLight()
    {
        if (auraLight != null)
            return auraLight;

        GameObject holder = new GameObject("CanavarHalesi");

        // KAMERANIN değil KÖKÜN çocuğu, bilerek: fener bakışı takip etmeli ama
        // hâle canavarın etrafında durmalı. Kameraya bağlansaydı canavar
        // başını çevirince ışık da savrulurdu.
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = new Vector3(0f, height, 0f);
        holder.layer = gameObject.layer;

        Light light = holder.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = color;

        // Gölgesiz nokta ışık duvar tanımaz; hâle yan koridora sızsaydı
        // canavarın yeri duvarın arkasından belli olurdu (bölüm 4).
        light.shadows = LightShadows.Hard;
        // `lightmapBakeType` YALNIZCA EDITORDE var: pisirme ayari oldugu
        // icin Unity onu build'e koymuyor ve derleme hatasi veriyordu.
        // Calisma aninda eklenen bir isik zaten pisirilemez; Unity'nin
        // varsayilani da Realtime, yani build'de davranis ayni kaliyor.
        // Editorde yine de aciyoruz: Inspector'da modun ne oldugu belli olsun.
#if UNITY_EDITOR
        light.lightmapBakeType = LightmapBakeType.Realtime;
#endif

        // Rol gelene kadar kapalı. RoundParticipant.ApplyRole açıyor.
        light.enabled = false;

        return light;
    }

    /// <summary>
    /// Canavar mı — yalnızca canavarda yanıyor.
    ///
    /// Rol bilinmiyorken (lobide, tur başlamadan) da kapalı: `RoundRole.None`
    /// canavar değil.
    /// </summary>
    public void SetMonster(bool value)
    {
        if (viewLight != null)
            viewLight.enabled = value;

        if (auraLight != null)
            auraLight.enabled = value;
    }
}
