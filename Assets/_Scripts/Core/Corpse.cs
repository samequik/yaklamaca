using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>Bağımsız görseli olan, taşınabilen ve kabine yerleştirilebilen sunucu ragdoll'u.</summary>
[RequireComponent(typeof(RagdollSync))]
public class Corpse : NetworkBehaviour, IInteractable
{
    [SyncVar] private uint victimNetId;
    [SyncVar] private string victimName;
    [SyncVar(hook = nameof(OnHolderChanged))] private uint carrierNetId;
    [SyncVar(hook = nameof(OnHolderChanged))] private uint stationNetId;
    /// <summary>
    /// Kurbanın kaçan kostümü (bkz. CharacterCatalog): öldüğün kostümde
    /// yatıyorsun.
    ///
    /// Kurbandan okunmuyor, spawn'da kopyalanıyor. Ceset bilerek kurban
    /// objesinden BAĞIMSIZ (bölüm 23): kurban ayrılmış ya da istemci sonradan
    /// katılmış olabilir ve o zaman okunacak bir şey kalmıyor.
    /// </summary>
    [SyncVar] private int costume;

    /// <summary>Tek bir kostümün ceset gövdesi. `Diriltme Sistemini Kur` kuruyor.</summary>
    [System.Serializable]
    private class BodyVariant
    {
        public GameObject prefab;

        /// <summary>Kemik yolları, `HumanBodyBones` indeksiyle.</summary>
        public string[] bonePaths;
    }

    [Tooltip("Kostüm başına bir ceset gövdesi. Sıra CharacterCatalog.Runners " +
        "ile AYNI: kostüm indeksi doğrudan gövde indeksi.")]
    [SerializeField] private BodyVariant[] bodies;
    [SerializeField] private float ragdollMass = 70f;
    [SerializeField] private float maxSpeed = 6f;
    [SerializeField] private float pushStrength = 0.3f;
    [SerializeField] private float pushReach = 0.25f;

    [Tooltip("Fırlatılan ceset yere/duvara çarpınca bir kez çalıyor. " +
        "Sesleri Yerleştir bağlıyor — Corpse.prefab'a LoadPrefabContents ile " +
        "yazıyor, prefabı sıfırdan kurmuyor (bkz. RevivalSetup'ın bodies[] " +
        "için kullandığı aynı yöntem).")]
    [SerializeField] private AudioClip fallClip;
    private AudioSource audioSource;

    /// <summary>
    /// Ses kaynağının kendi transformu — Corpse'un KÖKÜ değil. Kök hiç
    /// hareket etmiyor (ragdoll'un görsel klonu ona parented ve fiziği
    /// bağımsız çalışıyor); sesi çarpma noktasına taşımak için kökü
    /// oynatsaydık klonu da sürüklerdi. Bu, yalnızca çarpma anında konumu
    /// güncellenen ayrı bir çocuk.
    /// </summary>
    private Transform audioAnchor;

    /// <summary>Fırlatmadan sonra "düştü" sesini bir kez beklediğimizi işaretler.</summary>
    private bool awaitingLanding;

    /// <summary>Bunun altındaki temaslar "düştü" sayılmıyor — kayarken sürtünme, hafif itiş.</summary>
    private const float LandingImpactSpeed = 2f;
    /// <summary>
    /// Taşınan cesedin taşıyıcıya göre yeri. Oyuncunun orijini kapsülün
    /// ORTASI (ayaklardan 0.69 m yukarısı), göz hizası ise +0.53 m. Gövdeyi
    /// biraz aşağıya ve öne koymak onu ekranın alt yarısına oturtuyor:
    /// taşıdığını görüyorsun ama görüşünü kapatmıyor.
    /// </summary>
    /// Yükseklik (0.5) uzuvların YERE SÜRTMEMESİ için: kalça ayaklardan
    /// ~1.19 m yukarıda kalıyor, sarkan bacaklar zemine değmiyor. Collider'lar
    /// taşırken açık olduğundan (bkz. ApplyAuthority) alçak tutmak bacakları
    /// zemine takıp gövdeyi çırpındırırdı.
    private static readonly Vector3 CarryOffset = new Vector3(0f, 0.5f, 0.8f);

    /// <summary>Bırakırken cesedin taşıyıcının kaç metre önüne konacağı.</summary>
    private const float DropForward = 0.9f;

    /// <summary>Taşıma noktasının duvar yoklamasında kullandığı küre yarıçapı.</summary>
    private const float CarryProbeRadius = 0.25f;

    /// <summary>Duvara dayanınca ceset en fazla bu kadar yaklaşıyor (metre).</summary>
    private const float CarryMinReach = 0.15f;

    /// <summary>
    /// Ceset taşıyanın hız çarpanı — bölüm 21.2'nin "taşımanın bedeli"
    /// sorusunun cevabı.
    ///
    /// Ölü bir adamı taşımak bedava olmamalı, ama koşmayı büsbütün kesmek de
    /// yanlış: taşıyan zaten canavara açık bir hedef ve elleri dolu, üstüne
    /// bir de yürümeye mahkûm etmek diriltmeyi hiç denenmeyen bir hamleye
    /// çevirirdi. Koşabiliyor, sadece eskisi kadar hızlı değil.
    ///
    /// **Değer BURADA duruyor, `PlayerController`'da değil** — taşımayla
    /// ilgili bütün ayarlar tek dosyada kalsın diye. Sabit olması da
    /// bilinçli: prefaba serileştirilmiş bir alan olsaydı koddaki değeri
    /// değiştirmek hiçbir şey yapmazdı (bölüm 16'daki tuzak).
    /// </summary>
    public const float CarrySpeedMultiplier = 0.85f;

    /// <summary>Fırlatılan cesedin çıkış hızı (m/s).</summary>
    private const float ThrowSpeed = 9f;

    /// <summary>Fırlatma yönünün yukarı bileşeni — düz atış yerde sürünüyor.</summary>
    private const float ThrowRise = 0.35f;

    /// <summary>E'nin fırlatma sayılması için basılı tutulma süresi (saniye).</summary>
    public const float ThrowHoldTime = 0.3f;

    /// <summary>Fırlatmadan sonra hız tavanının gevşek kaldığı süre.</summary>
    private const float ThrowGrace = 0.7f;

    private static readonly List<Corpse> all = new List<Corpse>();
    private readonly Dictionary<Transform, Vector3> lastPlayerPositions = new Dictionary<Transform, Vector3>();
    private readonly Collider[] nearbyPlayers = new Collider[16];
    private List<RagdollFactory.Part> ragdoll;
    private RagdollSync sync;
    private Renderer[] renderers;
    private Vector3[] heldOffsets;
    private Quaternion[] heldRotations;

    /// <summary>Kalçanın taşıyıcıya göre duruşu — alındığı andaki hâli korunuyor.</summary>
    private Quaternion carriedHipsRotation = Quaternion.identity;

    /// <summary>Şu an çarpışması kapatılmış taşıyıcı kapsülü — bkz. IgnoreCarrier.</summary>
    private Collider ignoredCarrier;

    /// <summary>Bu ana kadar hız tavanı gevşek — bkz. ClampSpeeds.</summary>
    private float throwClampUntil;
    private int playerMask;
    public uint VictimNetId => victimNetId;
    public uint StationNetId => stationNetId;
    public string VictimName => victimName;
    public Vector3 Position => ragdoll != null && ragdoll.Count > 0 ? ragdoll[0].Bone.position : transform.position;
    public bool IsHeld => carrierNetId != 0 || stationNetId != 0;

    public static Corpse CarriedBy(RoundParticipant player)
    {
        if (player == null) return null;
        foreach (Corpse corpse in all)
            if (corpse != null && corpse.carrierNetId == player.netId) return corpse;
        return null;
    }
    public static bool LivingRunner(RoundParticipant player) => player != null && player.IsAlive
        && !player.IsEscaped && !player.IsSpectating && player.Role == RoundRole.Runner
        && RoundManager.Instance != null && RoundManager.Instance.Phase == RoundPhase.Playing;
    public static RoundParticipant Resolve(uint id)
    {
        NetworkIdentity identity;
        if (NetworkServer.active && NetworkServer.spawned.TryGetValue(id, out identity))
            return identity.GetComponent<RoundParticipant>();
        return NetworkClient.spawned.TryGetValue(id, out identity) ? identity.GetComponent<RoundParticipant>() : null;
    }
    private void Awake()
    {
        all.Add(this);
        sync = GetComponent<RagdollSync>();
        playerMask = LayerMask.GetMask("Oyuncu");

        // RevivalStation'daki desenin aynısı: bileşen kendi kaynağını
        // kuruyor, ayrı bir prefab alanı ya da kurulum adımı gerekmiyor.
        // Kaynak KÖKE değil, ayrı bir çocuğa (audioAnchor) konuyor — bkz.
        // alanın yorumu.
        GameObject anchor = new GameObject("CesetSesi");
        anchor.transform.SetParent(transform, false);
        audioAnchor = anchor.transform;

        audioSource = anchor.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 2f;
        audioSource.maxDistance = 20f;
    }
    private void OnDestroy() => all.Remove(this);
    [Server] public void ServerInit(uint victim)
    {
        victimNetId = victim;
        var player = Resolve(victim);
        victimName = player != null ? player.DisplayName : "Kaçan";
        costume = player != null ? player.RunnerCostume : 0;
    }
    public override void OnStartServer() { BuildVisual(); ApplyAuthority(); sync.Publish(); }
    public override void OnStartClient() { BuildVisual(); ApplyAuthority(); }

    private void BuildVisual()
    {
        if (ragdoll != null) return;
        // Kostüm indeksi temizleniyor: liste kısalmış olabilir ve dizi sınırı
        // hatası ceset görselini komple yok ederdi.
        BodyVariant variant = bodies != null && bodies.Length > 0
            ? bodies[costume >= 0 && costume < bodies.Length ? costume : 0]
            : null;
        if (variant == null || variant.prefab == null || variant.bonePaths == null)
        {
            Debug.LogError("Ceset görseli bağlı değil: Diriltme Sistemini Kur aracını çalıştır.", this);
            return;
        }
        string[] bonePaths = variant.bonePaths;
        GameObject clone = Instantiate(variant.prefab, transform.position, transform.rotation, transform);
        clone.SetActive(true);
        foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        // Sunucu ölüm klibinin son pozunu alır. İstemci kurban objesine ihtiyaç duymaz.
        RoundParticipant victim = isServer ? Resolve(victimNetId) : null;
        Transform source = victim != null ? victim.CorpseSourceBody : null;
        if (source != null) CopyPose(source, clone.transform);
        Transform ResolveBone(HumanBodyBones bone)
        {
            int index = (int)bone;
            if (index >= bonePaths.Length || string.IsNullOrEmpty(bonePaths[index])) return null;
            return clone.transform.Find(bonePaths[index]);
        }
        renderers = clone.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
        }
        ragdoll = RagdollFactory.Build(ResolveBone, ragdollMass, LayerMask.NameToLayer("Etkilesim"));
        if (ragdoll.Count == 0) { Debug.LogError("Ceset iskeleti kurulamadı.", this); return; }
        RagdollFactory.DisableSelfCollision(ragdoll);

        // Çarpışma olayı parçanın KENDİ objesine geliyor, Corpse'a değil —
        // aktarıcı geri iletiyor (bkz. RagdollImpactRelay, bölüm 21.1'deki
        // "collider başka objede" tuzağının aynısı).
        foreach (var part in ragdoll)
        {
            RagdollImpactRelay relay = part.Collider.gameObject.AddComponent<RagdollImpactRelay>();
            relay.Owner = this;
        }

        sync.Bind(ragdoll);
        ApplyAuthority();
    }
    private static void CopyPose(Transform source, Transform target, bool isRoot = true)
    {
        // Ölçek klonun dinlenme ölçeğinde kalır: birinci şahısta gizlenen kafa taşınmaz.
        if (!isRoot) target.localRotation = source.localRotation;
        foreach (Transform child in target)
        {
            Transform original = source.Find(child.name);
            if (original == null) continue;
            child.localPosition = original.localPosition;
            CopyPose(original, child, false);
        }
    }
    private void OnHolderChanged(uint before, uint after) => ApplyAuthority();
    /// <summary>
    /// Fiziği kimin yürüteceğini ve neyin serbest kalacağını yazar.
    ///
    /// **Taşırken gövde DONMUYOR.** Eskiden bütün parçalar kinematik yapılıyor
    /// ve poz kare kare zorla yazılıyordu; ceset elde tahta gibi duruyordu.
    /// Artık yalnızca KALÇA sabitleniyor (taşıyıcının elindeki nokta), geri
    /// kalan her şey eklemlerden sarkıyor: kollar, bacaklar ve baş yürüdükçe
    /// sallanıyor. Klasik ragdoll taşıma kurulumu — sabit bir kök, serbest
    /// zincir.
    ///
    /// Kabinde ise her şey kinematik: gövde yerleştirildiği pozda durmalı,
    /// yoksa yavaşça kabinden dışarı akardı.
    /// </summary>
    private void ApplyAuthority()
    {
        if (ragdoll == null) return;
        bool carried = carrierNetId != 0;
        for (int i = 0; i < ragdoll.Count; i++)
        {
            var part = ragdoll[i];
            // Kalça (i == 0) taşınırken sabit; kabinde her şey sabit.
            bool pinned = stationNetId != 0 || (carried && i == 0);
            bool simulate = isServer && !pinned;
            if (part.Body.isKinematic != !simulate)
            {
                part.Body.isKinematic = !simulate;
                if (simulate) { part.Body.velocity = Vector3.zero; part.Body.angularVelocity = Vector3.zero; part.Body.WakeUp(); }
            }
            // Taşırken collider'lar AÇIK kalıyor: sarkan uzuvlar duvara ve
            // eşyalara çarpsın, gövde cisimlerin içinden geçmesin. Taşıyanın
            // kendisiyle çarpışma ayrıca kapatılıyor (IgnoreCarrier), yoksa
            // uzuvlar taşıyanı iter ve ikisi birbirine takılırdı.
            //
            // Kabinde kapalı: orada gövde yerleştirildiği pozda duruyor,
            // çarpışmasına gerek yok.
            part.Collider.enabled = stationNetId == 0;

            // **Taşırken katman `Sus`, yerdeyken `Etkilesim`.**
            // `Etkilesim` nişan ışınının maskesinde (bölüm 16): yerdeki cesede
            // bakıp E ile alabilmemizi o sağlıyor. Ama taşırken gövde
            // kameranın 0.8 m önünde duruyor ve collider'ları artık AÇIK —
            // aynı maskede kalsaydı kendi taşıdığın ceset nişan ışınını keser,
            // kabini hedefleyemezdin. `Sus` maskede değil, yani ışın içinden
            // geçiyor; çarpışma ise katmandan bağımsız sürüyor.
            part.Collider.gameObject.layer = LayerMask.NameToLayer(
                carrierNetId != 0 ? "Sus" : "Etkilesim");
        }
        IgnoreCarrier();
        sync.Continuous = IsHeld;
    }

    /// <summary>
    /// Taşıyanın çarpışma kapsülüyle cesedin çarpışmasını kapatır, bırakınca
    /// geri açar. `Physics.IgnoreCollision` collider çifti başına çalışıyor,
    /// o yüzden hangi çiftleri kapattığımızı hatırlamak zorundayız.
    /// </summary>
    private void IgnoreCarrier()
    {
        Collider wanted = null;

        if (carrierNetId != 0)
        {
            RoundParticipant carrier = Resolve(carrierNetId);
            if (carrier != null) wanted = carrier.GetComponent<CharacterController>();
        }

        if (wanted == ignoredCarrier) return;

        SetCarrierIgnored(ignoredCarrier, false);
        ignoredCarrier = wanted;
        SetCarrierIgnored(ignoredCarrier, true);
    }

    private void SetCarrierIgnored(Collider carrier, bool ignore)
    {
        if (carrier == null || ragdoll == null) return;

        foreach (var part in ragdoll)
            if (part.Collider != null && part.Collider.enabled)
                Physics.IgnoreCollision(part.Collider, carrier, ignore);
    }
    // Taşınan ceset ARTIK GİZLENMİYOR. Eskiden taşıyanın ekranında
    // kapatılıyordu (yüzünü kapatmasın diye) ama o zaman elinde bir şey
    // olduğu hiç görünmüyor, kabine yerleştirmek de körlemesine oluyordu.
    // Çözüm gizlemek değil, gövdeyi kameranın ALTINA, öne almak — bkz.
    // CarryOffset.
    private void FixedUpdate()
    {
        if (!isServer || ragdoll == null || ragdoll.Count == 0) return;
        if (stationNetId != 0) return;

        // Hız tavanı TAŞIRKEN DE uygulanıyor. Duvara dayanınca uzuvlar
        // çarpışmayı çözmeye çalışıp çılgınca savruluyordu; tavan o patlamayı
        // kırpıyor.
        ClampSpeeds();

        if (carrierNetId != 0)
        {
            RoundParticipant carrier = Resolve(carrierNetId);
            if (!LivingRunner(carrier) || carrier.GetComponent<PlayerController>()?.IsFocused == true)
            { ServerDrop(); return; }
            // Yalnızca KALÇA sürülüyor; gerisi eklemlerden sarkıp sallanıyor.
            //
            // **`MovePosition`/`MoveRotation`, doğrudan `position` yazmak
            // DEĞİL.** Kinematik bir gövdenin konumunu doğrudan yazmak onu
            // ışınlıyor: çözücü aradaki hareketi görmüyor, dolayısıyla ona
            // bağlı eklemler her fizik adımında sıfırdan bir sıçrama görüp
            // titriyordu — koşarken cesedin zangırdamasının sebebi buydu.
            // `MovePosition` hareketi adım boyunca yayıyor ve çözücüye hız
            // bilgisi veriyor; uzuvlar sarsılmak yerine akıcı sallanıyor.
            Rigidbody hips = ragdoll[0].Body;
            hips.MovePosition(CarryAnchor(carrier.transform));
            hips.MoveRotation(carrier.transform.rotation * carriedHipsRotation);
            return;
        }

        ShoveFromPlayers();
    }

    /// <summary>
    /// Bir ragdoll parçası bir şeye çarptı — `RagdollImpactRelay` iletiyor.
    ///
    /// **`OnCollisionEnter` ile, hız eşiğiyle DEĞİL** (ilk sürüm öyleydi ve
    /// iki sorun çıkardı: ses geç geliyordu, çünkü "bütün ragdoll tamamen
    /// durdu" anını bekliyordu — uzuvlar çarpmadan sonra da bir süre
    /// sallanmaya devam ediyor. Ve bazen HİÇ gelmiyordu, çünkü eklem
    /// çözücüsünün kalıntı titreşimi hızı sönme eşiğinin altına hiç
    /// düşürmeyebiliyordu.) Gerçek çarpışma olayı ikisini de çözüyor: tam
    /// TEMAS ANINDA ateşliyor ve `Collision.contacts[0].point` gerçek çarpma
    /// noktasını veriyor.
    ///
    /// Yalnızca **haritaya ya da propa** (Harita/Sus katmanı) çarpma sayılıyor
    /// — oyuncuya hafifçe değmek ya da ShoveFromPlayers'ın ittirmesi
    /// saymamalı. Hız eşiği de kalıyor: cesedi hafifçe iterken duvara
    /// sürtünmesi "düştü" sesini tetiklememeli.
    /// </summary>
    [Server]
    public void ServerReportImpact(Collision collision)
    {
        if (!awaitingLanding || collision.relativeVelocity.magnitude < LandingImpactSpeed)
            return;

        int mask = LayerMask.GetMask("Harita", "Sus");
        if (((1 << collision.gameObject.layer) & mask) == 0)
            return;

        awaitingLanding = false;
        RpcLanded(collision.GetContact(0).point);
    }

    /// <summary>Herkeste bir kez çalıyor — ses kaynağı ÇARPMA NOKTASINA
    /// taşınıyor, Corpse'un kökü değil (kök hiç hareket etmiyor, bkz.
    /// audioAnchor alanının yorumu).</summary>
    [ClientRpc]
    private void RpcLanded(Vector3 point)
    {
        if (fallClip == null)
            return;

        audioAnchor.position = point;
        audioSource.PlayOneShot(fallClip, 0.9f);
    }
    /// <summary>
    /// Taşınan cesedin tutulacağı nokta — ama **duvara girmeyecek şekilde.**
    ///
    /// Kalça kinematik olduğu için hiçbir şey onu durdurmuyordu: duvara doğru
    /// yürüyünce gövde duvarın içine giriyor, sarkan uzuvlar derin çakışmayı
    /// çözmeye çalışıp çılgınca savruluyordu.
    ///
    /// Artık taşıyıcıdan öne bir küre atılıyor; önü kapalıysa ceset
    /// **taşıyana yaklaşıyor.** Yani gövde illa sabit bir noktada durmuyor,
    /// dar yerde sana sokuluyor. Aynı fikir kameranın duvar payında (bölüm 5)
    /// ve cesedi bırakırken de kullanılıyor.
    /// </summary>
    private static Vector3 CarryAnchor(Transform carrier)
    {
        Vector3 origin = carrier.position + Vector3.up * CarryOffset.y;
        Vector3 forward = Vector3.ProjectOnPlane(carrier.forward, Vector3.up).normalized;
        float reach = CarryOffset.z;

        if (Physics.SphereCast(origin, CarryProbeRadius, forward, out RaycastHit hit, reach,
                LayerMask.GetMask("Harita"), QueryTriggerInteraction.Ignore))
            reach = Mathf.Max(CarryMinReach, hit.distance - 0.05f);

        return origin + forward * reach;
    }

    /// <summary>Taşırken hız tavanı: uzuvlar çarpışmadan patlayıp savrulmasın.</summary>
    private void ClampSpeeds()
    {
        // Fırlatmadan hemen sonra tavan gevşiyor: normal tavan (maxSpeed = 6)
        // fırlatma hızından düşük ve gövdeyi daha havalanmadan kırpardı —
        // "fırlattım ama iki adım öteye düştü" demek olurdu.
        float cap = Time.time < throwClampUntil ? Mathf.Max(maxSpeed, ThrowSpeed) : maxSpeed;
        float limit = cap * cap;

        foreach (var part in ragdoll)
            if (!part.Body.isKinematic && part.Body.velocity.sqrMagnitude > limit)
                part.Body.velocity = part.Body.velocity.normalized * cap;
    }

    private void CaptureHeldPose(Quaternion orientation)
    {
        heldOffsets = new Vector3[ragdoll.Count]; heldRotations = new Quaternion[ragdoll.Count];
        Vector3 anchor = Position;
        Quaternion inverse = Quaternion.Inverse(orientation);
        for (int i = 0; i < ragdoll.Count; i++)
        {
            heldOffsets[i] = inverse * (ragdoll[i].Bone.position - anchor);
            heldRotations[i] = inverse * ragdoll[i].Bone.rotation;
        }

        // Taşırken yalnızca kalça sürülüyor (bkz. FixedUpdate); duruşunu
        // alındığı andan koruyor ki gövde birden ters dönmesin.
        carriedHipsRotation = heldRotations[0];
    }
    private void MoveHeld(Vector3 anchor, Quaternion orientation)
    {
        if (heldOffsets == null) CaptureHeldPose(orientation);
        for (int i = 0; i < ragdoll.Count; i++)
        {
            Vector3 position = anchor + orientation * heldOffsets[i];
            Quaternion rotation = orientation * heldRotations[i];
            ragdoll[i].Bone.SetPositionAndRotation(position, rotation);
            ragdoll[i].Body.position = position;
            ragdoll[i].Body.rotation = rotation;
        }
    }
    public string GetPrompt()
    {
        var local = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<RoundParticipant>() : null;
        return LivingRunner(local) && !IsHeld && CarriedBy(local) == null ? victimName + " — cesedi taşı" : null;
    }
    public void Interact(GameObject user) => CmdPickUp();
    [Command(requiresAuthority = false)]
    private void CmdPickUp(NetworkConnectionToClient sender = null)
    {
        var player = sender?.identity != null ? sender.identity.GetComponent<RoundParticipant>() : null;
        if (!LivingRunner(player) || IsHeld || CarriedBy(player) != null || ragdoll == null || ragdoll.Count == 0) return;
        if ((player.transform.position - Position).sqrMagnitude > 2.6f * 2.6f) return;
        if (!ClearReach(player.transform.position + Vector3.up * 0.8f, Position)) return;
        if (player.GetComponent<PlayerController>()?.IsFocused == true) return;
        CaptureHeldPose(player.transform.rotation);
        carrierNetId = player.netId;

        // Havadayken tekrar tutulursa hiçbir çarpışma "düştü" sayılmamalı —
        // bekleyen bayrağı burada temizlemek bir sonraki fırlatmaya kadar
        // asılı kalmasını önlüyor.
        awaitingLanding = false;

        ApplyAuthority();
    }
    public void Drop() => CmdDrop();
    [Command(requiresAuthority = false)]
    private void CmdDrop(NetworkConnectionToClient sender = null)
    {
        if (sender?.identity != null && sender.identity.netId == carrierNetId) ServerDrop();
    }

    /// <summary>E BASILI TUTULUNCA: cesedi bakılan yöne fırlatır.</summary>
    public void Throw() => CmdThrow();

    [Command(requiresAuthority = false)]
    private void CmdThrow(NetworkConnectionToClient sender = null)
    {
        if (sender?.identity != null && sender.identity.netId == carrierNetId) ServerThrow();
    }

    /// <summary>
    /// Cesedi ileri fırlatır — bırakmanın "uzağa" hâli.
    ///
    /// Kabine uzaktan atmak için var ve gerçekten çalışıyor: kabin gövdesinin
    /// içine düşen SERBEST bir cesedi `RevivalStation.TryAcceptNearbyCorpse`
    /// kendiliğinden kabul ediyor, yani fırlatıp tutturmak yerleştirmenin
    /// ikinci yolu.
    ///
    /// **Hız bütün parçalara AYNI veriliyor.** Yalnızca kalçaya itki vermek
    /// gövdeyi eklemlerden geriye açar ve ceset havada yırtılıyormuş gibi
    /// görünür; hepsine aynı hızı vermek onu tek parça hâlinde yolluyor,
    /// dönüşü eklemlerin kendisi üretiyor.
    /// </summary>
    [Server] private void ServerThrow()
    {
        if (carrierNetId == 0 || ragdoll == null || ragdoll.Count == 0) return;

        RoundParticipant carrier = Resolve(carrierNetId);
        Vector3 direction = transform.forward;

        if (carrier != null)
        {
            // Hafif yukarı: dümdüz ileri atılan gövde hemen zemine sürtüp
            // duruyor, küçük bir kavis onu gerçekten ileri taşıyor.
            Transform owner = carrier.transform;
            direction = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized
                + Vector3.up * ThrowRise;
        }

        direction = direction.normalized;

        // Önce normal bırakma: gövde taşıyıcıdan kopuyor, parçalar fiziğe
        // dönüyor ve taşıyıcının kapsülüyle çarpışması geri açılıyor.
        // Fırlatma yalnızca onun üstüne hız bindiriyor.
        ServerDrop();

        throwClampUntil = Time.time + ThrowGrace;
        awaitingLanding = true;

        foreach (var part in ragdoll)
        {
            if (part.Body.isKinematic) continue;
            part.Body.WakeUp();
            part.Body.velocity = direction * ThrowSpeed;
        }

        sync.Publish();
    }
    [Server] public void ServerDrop()
    {
        if (carrierNetId == 0) return;
        var carrier = Resolve(carrierNetId);
        if (carrier != null)
        {
            // Bakılan yöne bırakılıyor: cesedi ayağının dibine değil, önüne
            // koyuyorsun. Kabine yerleştirirken de, koridorda bırakırken de
            // nereye koyduğunu görmek gerekiyor.
            Transform owner = carrier.transform;
            Vector3 origin = owner.position + Vector3.up * 0.2f;
            Vector3 forward = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
            float reach = DropForward;

            // Duvara dayanmışken öne bırakmak cesedi duvarın içine sokardı;
            // önüne bir şey varsa mesafe oraya kadar kısalıyor. Aynı fikir
            // kameranın duvar payında da var (bölüm 5).
            if (Physics.SphereCast(origin, 0.25f, forward, out RaycastHit hit, reach,
                    LayerMask.GetMask("Harita"), QueryTriggerInteraction.Ignore))
                reach = Mathf.Max(0f, hit.distance - 0.1f);

            MoveHeld(origin + forward * reach + Vector3.up * 0.3f, owner.rotation);
        }
        carrierNetId = 0;
        heldOffsets = null;
        lastPlayerPositions.Clear();
        ApplyAuthority();
        sync.Publish();
    }
    /// <summary>
    /// Kabin cesedi bırakıyor: gövde yeniden fiziğe dönüyor.
    ///
    /// Kabin sıfırlanınca (tur bitişi gibi) `corpseId` temizleniyordu ama ceset
    /// `stationNetId`'yi taşımaya devam ediyordu; `IsHeld` sonsuza kadar doğru
    /// kalıyor, gövde kinematik ve collider'ları kapalı donuyordu. Alınamayan,
    /// itilemeyen, düşmeyen bir beden kalıyordu ortada.
    /// </summary>
    [Server] public void ServerReleaseFromStation()
    {
        if (stationNetId == 0) return;
        stationNetId = 0;
        heldOffsets = null;
        ApplyAuthority();
        sync.Publish();
    }
    [Server] public bool ServerDeposit(RoundParticipant carrier, RevivalStation station)
    {
        if (carrier == null || carrier.netId != carrierNetId) return false;
        return ServerPlaceInStation(station);
    }

    /// <summary>
    /// Cesedi kabine yerleştirir — TAŞINIYOR OLMASI ŞART DEĞİL.
    ///
    /// Kabin, gövdesinin içine bırakılan serbest cesedi kendi de fark ediyor
    /// (`RevivalStation.ServerTick`), yani cesedi kabine atmak da yeterli;
    /// terminale nişan alıp E'ye basmak zorunlu değil. İkisi de aynı yere
    /// çıkıyor.
    /// </summary>
    [Server] public bool ServerPlaceInStation(RevivalStation station)
    {
        if (station == null || stationNetId != 0) return false;

        carrierNetId = 0;
        stationNetId = station.netId;
        ApplyAuthority();

        // Yerden alınmadan doğrudan kabine giren cesedin poz kaydı yok;
        // o anki duruşunu kaydedip kabine öyle taşıyoruz.
        if (heldOffsets == null) CaptureHeldPose(station.BodyAnchor.rotation);

        MoveHeld(station.BodyAnchor.position, station.BodyAnchor.rotation);
        sync.Publish();
        return true;
    }
    public static bool ClearReach(Vector3 from, Vector3 to) => !Physics.Linecast(from, to,
        LayerMask.GetMask("Harita"), QueryTriggerInteraction.Ignore);
    [Server] private void ShoveFromPlayers()
    {
        if (pushStrength <= 0) return;
        int count = Physics.OverlapSphereNonAlloc(Position, 2.5f, nearbyPlayers, playerMask, QueryTriggerInteraction.Ignore);
        if (count == 0) { lastPlayerPositions.Clear(); return; }
        for (int i = 0; i < count; i++)
        {
            Collider player = nearbyPlayers[i];
            Vector3 current = player.transform.position;
            bool known = lastPlayerPositions.TryGetValue(player.transform, out Vector3 previous);
            lastPlayerPositions[player.transform] = current;
            if (!known) continue;
            Vector3 velocity = Vector3.ProjectOnPlane(current - previous, Vector3.up) / Time.fixedDeltaTime;
            velocity = Vector3.ClampMagnitude(velocity, 12f);
            if (velocity.magnitude < 0.5f) continue;
            Bounds reach = player.bounds; reach.Expand(pushReach * 2f);
            foreach (var part in ragdoll)
                if (reach.Intersects(part.Collider.bounds))
                { part.Body.WakeUp(); part.Body.AddForce(velocity * pushStrength * part.Body.mass, ForceMode.Impulse); }
        }
    }
}
