using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Yakalanan kaçanın kalıcı bedeni. `RoundParticipant.DeathHold` bitince
/// `RoundManager.ServerSpawnCorpse` bunu sunucuda doğuruyor; sahnede kalıyor,
/// fizik motoruyla itilebiliyor — CLAUDE.md'deki "dümdüz kalmasın, biri
/// yürüyerek ittirebilsin" isteğinin karşılığı.
///
/// ### Neden ayrı bir obje, oyuncunun kendi gövdesi değil
///
/// Oyuncu objesi (ve onun Banana Man modeli) bir SONRAKİ turda yeniden
/// kullanılıyor — o kişi yeni bir rolle yeniden doğacak. Bedeni kalıcı
/// yapmak için oyuncunun kendi gövdesini fiziğe bağlamak, o modeli bir daha
/// geri alamayacak şekilde elden çıkarmak demek olurdu. Onun yerine gövdenin
/// o anki DÜNYA pozundan bağımsız bir KOPYASI alınıyor.
///
/// ### Fizik: gerçek ragdoll, yalnızca SUNUCUDA simüle ediliyor
///
/// İlk sürüm tek bir kapsül + Rigidbody kullanıyordu ve ceset donmuş bir
/// heykel gibi duruyordu. Artık `RagdollFactory` iskeletin her ana kemiğine
/// Rigidbody + Collider + `CharacterJoint` kuruyor: gövde kendi ağırlığıyla
/// yığılıyor, üstünden geçince kolu bacağı savruluyor.
///
/// Simülasyon yalnızca sunucuda çalışıyor; istemcilerdeki gövdeler kinematik
/// ve pozu `RagdollSync`'ten alıyor. Aksi hâlde 11 Rigidbody'lik bir zincir
/// her makinede farklı otururdu ve yan yana duran iki oyuncu cesedi FARKLI
/// yerde görürdü — bu oyunun "his istemcide, karar sunucuda" kuralının
/// (CLAUDE.md bölüm 4) fizik karşılığı.
///
/// Sonucu: itme, Source/HL2'deki fizik prop'ları gibi küçük bir ağ
/// gecikmesiyle görünür. Bu oyunun zaten Source hareketinden esinlenmesiyle
/// tutarlı ve kabul edilebilir — ceset kozmetik bir öge, hiçbir tur kararını
/// etkilemiyor.
///
/// ### Görsel: her istemci KENDİ kopyasını yerel olarak üretiyor
///
/// Mesh verisini ağdan göndermek gerekmiyor: her istemcide zaten AYNI Banana
/// Man varlığı var. `victimNetId` yalnızca "hangi oyuncunun gövdesini
/// kopyala" diyor; her istemci `OnStartClient`'ta kendi yerel `Instantiate`
/// çağrısını yapıyor (bölüm 4 — aynı bilgiyi ikinci kez ağdan göndermek
/// yerine, herkes zaten sahip olduğu veriden aynı sonucu üretiyor; animatör
/// hızının pozisyon farkından çıkarılmasıyla aynı desen).
/// </summary>
[RequireComponent(typeof(RagdollSync))]
public class Corpse : NetworkBehaviour
{
    /// <summary>
    /// Hangi oyuncunun bedeni kopyalanacak. Spawn'dan ÖNCE sunucuda yazılıyor
    /// (bkz. RoundManager.ServerSpawnCorpse), yani her istemciye İLK durum
    /// olarak gidiyor — `OnStartClient` çalıştığında zaten dolu.
    /// </summary>
    [SyncVar] private uint victimNetId;

    [Tooltip("Ragdoll'un toplam kütlesi (kg). Parçalara insan vücudu " +
        "oranlarında dağıtılıyor — bkz. RagdollFactory.")]
    [SerializeField] private float ragdollMass = 70f;

    [Tooltip("Sunucudaki hız tavanı (m/s). Yalnızca acil fren: bu değere " +
        "normal oturma ve itilme sırasında ASLA yaklaşılmamalı. Düşük tutmak " +
        "eklem çözücüsüyle kavga edip titremeye yol açıyor — asıl patlama " +
        "koruması RagdollFactory'deki maxDepenetrationVelocity.")]
    [SerializeField] private float maxSpeed = 20f;

    [Tooltip("Üstünden geçen oyuncunun hızının ne kadarının cesede aktarılacağı. " +
        "0.6 = yürüyerek geçmek gövdeyi belirgin şekilde kaydırıyor, koşarak " +
        "dalmak savuruyor. 0 = itme kapalı.")]
    [SerializeField] private float pushStrength = 0.6f;

    [Tooltip("Oyuncunun çarpışma kutusu bir parçaya bu kadar yaklaşınca itme " +
        "uygulanıyor (metre).")]
    [SerializeField] private float pushReach = 0.25f;

    // Oyuncu hızı POZİSYON FARKINDAN çıkarılıyor. Sunucuda uzak oyuncuların
    // `PlayerController`'ı kapalı, yani hızını ondan okumak mümkün değil —
    // animatörlerin ve FootstepAudio'nun yaptığının aynısı (bölüm 4, 14, 17).
    private readonly Dictionary<Transform, Vector3> lastPlayerPositions =
        new Dictionary<Transform, Vector3>();

    private int playerMask;
    private bool loggedShove;

    private RagdollSync sync;
    private List<RagdollFactory.Part> ragdoll;
    private bool built;

    private void Awake()
    {
        sync = GetComponent<RagdollSync>();

        int layer = LayerMask.NameToLayer("Oyuncu");
        playerMask = layer >= 0 ? 1 << layer : 0;
    }

    /// <summary>
    /// Ragdoll fiziği yalnızca SUNUCUDA çalışıyor (bkz. RagdollSync), o yüzden
    /// hız tavanı da yalnızca orada anlamlı.
    /// </summary>
    private void FixedUpdate()
    {
        if (!isServer || ragdoll == null)
            return;

        float limitSqr = maxSpeed * maxSpeed;

        for (int i = 0; i < ragdoll.Count; i++)
        {
            Rigidbody part = ragdoll[i].Body;

            if (part != null && !part.isKinematic && part.velocity.sqrMagnitude > limitSqr)
                part.velocity = part.velocity.normalized * maxSpeed;
        }

        ShoveFromPlayers();
    }

    /// <summary>
    /// Üstünden geçen oyuncular cesedi itiyor.
    ///
    /// ### Neden `OnControllerColliderHit` yetmiyor
    ///
    /// İlk deneme itmeyi `PlayerController`'ın çarpışma geri çağrısına
    /// bağlamıştı. İki sorunu vardı:
    ///
    /// - O geri çağrı yalnızca `CharacterController.Move()` ÇAĞIRAN tarafta
    ///   çalışıyor. Sunucuda uzak oyuncular `NetworkTransform` ile taşınıyor,
    ///   `Move` çağrılmıyor — yani uzaktaki hiç kimse cesedi itemiyordu.
    /// - Fizik sunucu otoriteli olduğu için istemcideki itme zaten kinematik
    ///   bir kopyaya uygulanıyordu, hiçbir etkisi yoktu.
    ///
    /// Burada ise sunucu, oyuncuların KENDİ gördüğü konumlarından hızı
    /// çıkarıp itmeyi kendisi uyguluyor: host da uzak oyuncu da aynı yoldan
    /// geçiyor.
    ///
    /// ### Neden kütleyle ölçekleniyor
    ///
    /// Eklemler 11 parçayı tek bir ~70 kg'lık gövde gibi davrandırıyor. Sabit
    /// bir itki (ilk denemede ~4.5 N·s) bu kütlede santimetre/saniye hız
    /// üretiyor, yani gözle görülmüyordu. Kütleyle çarpınca `pushStrength`
    /// doğrudan "hızının yüzde kaçı aktarılıyor" anlamına geliyor ve hangi
    /// uzva denk gelirse gelsin sonuç tutarlı oluyor.
    /// </summary>
    [Server]
    private void ShoveFromPlayers()
    {
        if (pushStrength <= 0f || playerMask == 0)
            return;

        Transform hips = ragdoll[0].Bone;

        if (hips == null)
            return;

        Collider[] players = Physics.OverlapSphere(hips.position, 2.5f, playerMask,
            QueryTriggerInteraction.Ignore);

        if (players.Length == 0)
        {
            // Kimse yakında değilse takip sözlüğü şişmesin.
            if (lastPlayerPositions.Count > 0)
                lastPlayerPositions.Clear();

            return;
        }

        for (int i = 0; i < players.Length; i++)
        {
            Collider player = players[i];

            if (player == null)
                continue;

            Transform owner = player.transform;
            Vector3 current = owner.position;

            bool known = lastPlayerPositions.TryGetValue(owner, out Vector3 previous);
            lastPlayerPositions[owner] = current;

            if (!known)
                continue; // ilk kare: hız bilinmiyor

            Vector3 step = current - previous;
            Vector3 horizontal = new Vector3(step.x, 0f, step.z);
            float speed = horizontal.magnitude / Mathf.Max(Time.fixedDeltaTime, 0.0001f);

            // Durup üstünde beklemek itmemeli; yalnızca gerçekten yürümek.
            if (speed < 0.5f)
                continue;

            Vector3 direction = horizontal.normalized;
            Bounds reach = player.bounds;
            reach.Expand(pushReach * 2f);

            for (int p = 0; p < ragdoll.Count; p++)
            {
                Collider part = ragdoll[p].Collider;
                Rigidbody body = ragdoll[p].Body;

                if (part == null || body == null || body.isKinematic)
                    continue;

                if (!reach.Intersects(part.bounds))
                    continue;

                body.WakeUp();
                body.AddForce(direction * pushStrength * speed * body.mass, ForceMode.Impulse);

                if (!loggedShove)
                {
                    loggedShove = true;
                    Debug.Log($"Corpse: ilk itme uygulandı — oyuncu hızı {speed:0.0} m/s.");
                }
            }
        }
    }

    /// <summary>Yalnızca sunucu çağırır, spawn'dan önce.</summary>
    [Server]
    public void ServerInit(uint victim) => victimNetId = victim;

    /// <summary>
    /// Hem sunucuda hem istemcide kuruluyor — ikisi de iskelete ihtiyaç
    /// duyuyor: sunucu simüle etmek, istemci gelen pozu uygulamak için.
    /// Host'ta ikisi de tetikleniyor, `built` bayrağı ikinci çağrıyı yutuyor.
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        EnsureBuilt();
        ApplyAuthority();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        EnsureBuilt();
        ApplyAuthority();
    }

    private void EnsureBuilt()
    {
        if (built)
            return;

        built = true;
        BuildVisual();
    }

    /// <summary>
    /// Fiziği kimin yürüteceğini yazar: sunucuda gerçek simülasyon, istemcide
    /// kinematik + `RagdollSync`'ten gelen poz.
    ///
    /// **İki geri çağrının İKİSİNDEN DE çağrılıyor, bilerek.** Host'ta
    /// `OnStartServer` ve `OnStartClient` arka arkaya geliyor; hangisi önce
    /// çalışırsa çalışsın sonuç aynı olmalı. Tek bir yerde, kurulum anındaki
    /// `isServer` değerine bakmak sıraya bağımlı bir hata bırakıyordu —
    /// yanlış tarafa düşerse ceset kinematik kalır, yani havada asılı durur
    /// ve itilemez.
    /// </summary>
    private void ApplyAuthority()
    {
        if (ragdoll == null)
            return;

        bool simulate = isServer;

        for (int i = 0; i < ragdoll.Count; i++)
        {
            Rigidbody part = ragdoll[i].Body;

            if (part == null)
                continue;

            part.isKinematic = !simulate;

            // Uyuyan bir gövde yerçekimini biriktirmiyor: doğduğu anda uyanık
            // olduğundan emin oluyoruz, yoksa havada kalabilir.
            if (simulate)
                part.WakeUp();
        }
    }

    /// <summary>
    /// Kurbanın o anki gövdesinden bir kopya çıkarıp kendi üstümüze koyuyor.
    ///
    /// `Instantiate(kaynak, konum, dönüş, ebeveyn)` kaynağın o anki DÜNYA
    /// pozunu kopyalıyor — kurbanın gövdesi tam bu sırada `ApplyDeathPose`'un
    /// bıraktığı hâlde (canavarın önünde diz çökmüş), yani ayrı bir kemik/poz
    /// taşıma kodu gerekmiyor.
    /// </summary>
    private void BuildVisual()
    {
        RoundParticipant victim = ResolveParticipant(victimNetId);
        Transform source = victim != null ? victim.CorpseSourceBody : null;

        if (source == null)
        {
            Debug.LogWarning("Corpse: kurbanın gövdesi bulunamadı, ceset görselsiz kalacak.");
            return;
        }

        GameObject clone = Instantiate(source.gameObject, source.position, source.rotation, transform);

        // KOŞULSUZ aktif: `RoundParticipant.DeathHold` bu kopyayı ClearDeathPose/
        // RefreshBodyState'ten ÖNCE almaya çalışıyor (bkz. oradaki yorum), ama
        // ağ gecikmesi payı ne kadar cömert olursa olsun sıfır garanti değil.
        // Kaynak o an kapalıysa `Instantiate` de kapalı bir kopya üretir —
        // Unity aktiflik durumunu birebir taşır. Burada zorlamak, zamanlamaya
        // bakılmaksızın cesedin GÖRÜNMESİNİ garanti ediyor: çarpışma kutusu
        // (Rigidbody, ayrı bir obje) doğru yerde duruyordu, eksik olan hep
        // buydu.
        clone.SetActive(true);

        // Ölüm klibi sırasında canavarla ölçek eşitlemesi olabilir
        // (PlayerBodyVisual.deathScaleMatch, bölüm 10) — o yalnızca kill
        // animasyonu boyunca geçerli bir görsel numara, kalıcı cesede
        // taşınmamalı. `1` yazmak da yanlış olurdu: modelin kendi ölçeği
        // hull'a oranlanarak hesaplanıyor. Poz öncesi değeri kurbandan alıyoruz.
        clone.transform.localScale = victim.CorpseSourceScale;

        // `SetActive(true)` yalnızca OBJENİN kendisini açıyor — kaynak o an
        // gizliyken `PlayerBodyVisual.ApplyMode` her `Renderer`'ı AYRICA
        // `enabled = false` yapmıştı (bölüm 14: gölge düşsün diye SetActive
        // değil renderer.enabled kullanılıyor). Instantiate bu durumu da
        // birebir kopyalıyor, yani objeyi açmak tek başına yetmiyor.
        Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = true;

            // Ragdoll'un bilinen tuzağı: `SkinnedMeshRenderer` görünürlük
            // kutusunu bind pozundan hesaplıyor. Kemikler ragdoll ile o
            // kutunun dışına çıkınca Unity mesh'i "ekranda değil" sayıp
            // çizmeyi bırakıyor — gövde kaybolur, çarpışma kutuları yerinde
            // kalır. `updateWhenOffscreen` kutuyu her kare kemiklerden
            // yeniden hesaplatıyor.
            if (renderers[i] is SkinnedMeshRenderer skinned)
                skinned.updateWhenOffscreen = true;
        }

        // Kemikler KURBANIN CANLI animatöründen çözülüyor, klonunkinden DEĞİL.
        //
        // `Animator.GetBoneTransform` yalnızca animatör bağlı ve başlatılmışken
        // çalışıyor; taze `Instantiate` edilmiş bir animatör bu şartı aynı
        // karede sağlamayabiliyor ve null dönüyor. O zaman ragdoll sessizce
        // hiç kurulmuyor: ceset havada asılı kalıyor, ne düşüyor ne itiliyor.
        //
        // Kurbanın animatörü ise saniyelerdir ölüm klibini oynatıyor, yani
        // kesinlikle başlatılmış. Ondan aldığımız kemiğin KÖKE GÖRE YOLUNU
        // klonda arıyoruz — hiyerarşi birebir aynı olduğu için tutuyor.
        Animator sourceAnimator = source.GetComponentInChildren<Animator>(true);
        Animator cloneAnimator = clone.GetComponentInChildren<Animator>(true);

        BuildRagdoll(bone => MapBone(sourceAnimator, source, clone.transform, bone));

        ResetHeadBone(sourceAnimator, source, clone.transform);

        // Klonun animatörü KAPATILIYOR: açık kalırsa bir sonraki karede kendi
        // varsayılan durumuna dönüp kemikleri her karede yeniden yazar ve
        // fiziği tamamen ezerdi.
        if (cloneAnimator != null)
            cloneAnimator.enabled = false;
    }

    /// <summary>
    /// Canlı gövdedeki bir kemiğin klondaki karşılığını bulur: rolü canlı
    /// animatörden çözülüyor, sonra köke göre yolu klonda aranıyor.
    /// </summary>
    private static Transform MapBone(Animator sourceAnimator, Transform sourceRoot,
        Transform cloneRoot, HumanBodyBones bone)
    {
        if (sourceAnimator == null || !sourceAnimator.isHuman)
            return null;

        Transform sourceBone = sourceAnimator.GetBoneTransform(bone);

        if (sourceBone == null)
            return null;

        string path = PathFrom(sourceRoot, sourceBone);

        if (path == null)
            return null;

        return path.Length == 0 ? cloneRoot : cloneRoot.Find(path);
    }

    /// <summary>
    /// Cesedin doğduğu noktada, cesede AİT OLMAYAN açık collider var mı diye
    /// bakıp konsola yazar — "gövdenin içinde görünmez bir şey var" şikâyetini
    /// tahminle değil isimle çözmek için.
    ///
    /// Haritanın kendisi (zemin, duvar) elenmiyor; ceset zaten yerde duruyor,
    /// zemini görmek normal. Beklenmeyen bir isim çıkarsa aranan şey odur.
    /// </summary>
    private void ReportGhostColliders()
    {
        Collider[] nearby = Physics.OverlapSphere(transform.position, 1.2f, ~0,
            QueryTriggerInteraction.Collide);

        List<string> names = new List<string>();

        for (int i = 0; i < nearby.Length; i++)
        {
            Collider other = nearby[i];

            if (other == null || other.transform.IsChildOf(transform))
                continue;

            names.Add($"{other.name} ({LayerMask.LayerToName(other.gameObject.layer)}" +
                $"{(other.isTrigger ? ", trigger" : string.Empty)})");
        }

        Debug.Log($"Corpse: ragdoll {ragdoll.Count} parça | çevredeki yabancı " +
            $"collider sayısı {names.Count}" +
            (names.Count > 0 ? " → " + string.Join(", ", names) : string.Empty));
    }

    /// <summary>Bir çocuğun köke göre "a/b/c" yolu. Kökün altında değilse null.</summary>
    private static string PathFrom(Transform root, Transform child)
    {
        if (child == root)
            return string.Empty;

        string path = child.name;
        Transform current = child.parent;

        while (current != null && current != root)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return current == root ? path : null;
    }

    /// <summary>
    /// İskelete gerçek fizik kurar: ceset artık tek parça bir heykel değil,
    /// üstünden geçince kolu bacağı savrulan bir gövde.
    ///
    /// **Katman `Sus`.** Varil ve kasayla aynı gerekçe (CLAUDE.md bölüm 16):
    /// gövdeyi durdurur ama canavarın vuruş ışınını KESMEZ — koridorda yatan
    /// bir cesedin arkasına saklanmak kalkan olmamalı.
    ///
    /// **Fizik yalnızca sunucuda.** İstemcilerdeki gövdeler kinematik ve pozu
    /// `RagdollSync`'ten alıyor; yoksa herkes cesedi farklı yerde görürdü.
    ///
    /// Kurulamazsa (humanoid olmayan bir rig) görsel öylece duruyor: hareketsiz
    /// bir ceset, hiç olmayandan iyi.
    /// </summary>
    private void BuildRagdoll(System.Func<HumanBodyBones, Transform> resolve)
    {
        // Katman adı elle yazılı: `LayerSetup` editör derlemesinde, çalışma
        // anındaki bu sınıf ona ulaşamıyor (aynı gerekçe RoundManager'da da
        // yazılı). -1 = katman tanımlı değil, o zaman dokunulmuyor.
        int layer = LayerMask.NameToLayer("Sus");

        ragdoll = RagdollFactory.Build(resolve, ragdollMass, layer);

        if (ragdoll.Count == 0)
        {
            Debug.LogWarning("Corpse: ragdoll kurulamadı, ceset hareketsiz kalacak.");
            return;
        }

        // ÖNCE hayati bağlantılar: fizik otoritesi ve ağ senkronu. Kendi
        // kendine çarpışmayı kapatmak yalnızca bir kararlılık ayarı, o yüzden
        // en sona alındı — orada bir aksilik olursa ragdoll'un tamamını
        // götürmesin.
        ApplyAuthority();

        if (sync != null)
            sync.Bind(ragdoll);

        RagdollFactory.DisableSelfCollision(ragdoll);

        if (isServer)
            ReportGhostColliders();
    }

    /// <summary>
    /// `PlayerBodyVisual.HideBone` birinci şahısta kafa kemiğini sıfıra
    /// ölçekliyordu (bölüm 14); o an kaynak birinci şahıssa kopya da aynı
    /// sıfırlanmış kemikle gelir. Ceset üçüncü şahıstan görüleceği için kafa
    /// GÖRÜNMELİ — kemiği 1'e geri alıyoruz.
    /// </summary>
    private static void ResetHeadBone(Animator sourceAnimator, Transform sourceRoot,
        Transform cloneRoot)
    {
        Transform head = MapBone(sourceAnimator, sourceRoot, cloneRoot, HumanBodyBones.Head);

        if (head != null && head.localScale == Vector3.zero)
            head.localScale = Vector3.one;
    }

    /// <summary>
    /// `victimNetId`'den kurbanın katılımcısını çözer. İstemcide ve sunucuda
    /// (host) ayrı sözlükler var; host ikisine de sahip, adanmış sunucuda
    /// yalnızca ikincisi dolu (bölüm 17'deki `ResolveKiller`'la aynı desen).
    /// </summary>
    private static RoundParticipant ResolveParticipant(uint netId)
    {
        if (NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null)
            return identity.GetComponent<RoundParticipant>();

        if (NetworkServer.active
            && NetworkServer.spawned.TryGetValue(netId, out identity) && identity != null)
            return identity.GetComponent<RoundParticipant>();

        return null;
    }
}
