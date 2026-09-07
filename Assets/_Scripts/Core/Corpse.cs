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

    [Tooltip("Sunucudaki hız tavanı (m/s). Beklenmedik bir çakışma tek " +
        "karelik bir patlama üretirse kırpıyor; normal itmelerin çok üstünde.")]
    [SerializeField] private float maxSpeed = 8f;

    private RagdollSync sync;
    private List<RagdollFactory.Part> ragdoll;
    private bool built;

    private void Awake() => sync = GetComponent<RagdollSync>();

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
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        EnsureBuilt();
    }

    private void EnsureBuilt()
    {
        if (built)
            return;

        built = true;
        BuildVisual();
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

        // Animator KAPATILMALI, yoksa bir sonraki karede kendi varsayılan
        // durumuna (Locomotion/idle) döner ve az önce kopyaladığımız ölüm
        // pozu kaybolur. Aynı satırda, Instantiate'tan hemen sonra: Animator
        // hiç Update çalıştırmadan donduruluyor.
        Animator animator = clone.GetComponentInChildren<Animator>(true);
        if (animator != null)
            animator.enabled = false;

        // Kaynak birinci-şahıs kurallarına göre kafa kemiğini/gölge modunu
        // ayarlamış olabilir (PlayerBodyVisual.ApplyHead) — kopya artık
        // kimsenin "birinci şahsı" değil, sahnede duran sabit bir ceset.
        // Kemik ölçeği kopyalandığı için (Instantiate her şeyi taşır) kafa
        // sıfıra küçülmüş kalabilir; düzeltiyoruz.
        ResetHeadBone(animator);

        // `SetActive(true)` yalnızca OBJENİN kendisini açıyor — kaynak o an
        // gizliyken `PlayerBodyVisual.ApplyMode` her `Renderer`'ı AYRICA
        // `enabled = false` yapmıştı (bölüm 14: gölge düşsün diye SetActive
        // değil renderer.enabled kullanılıyor). Instantiate bu durumu da
        // birebir kopyalıyor, yani objeyi açmak tek başına yetmiyor.
        Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = true;

        BuildRagdoll(animator);
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
    private void BuildRagdoll(Animator animator)
    {
        if (animator == null)
            return;

        // Katman adı elle yazılı: `LayerSetup` editör derlemesinde, çalışma
        // anındaki bu sınıf ona ulaşamıyor (aynı gerekçe RoundManager'da da
        // yazılı). -1 = katman tanımlı değil, o zaman dokunulmuyor.
        int layer = LayerMask.NameToLayer("Sus");

        ragdoll = RagdollFactory.Build(animator, ragdollMass, layer);

        if (ragdoll.Count == 0)
        {
            Debug.LogWarning("Corpse: ragdoll kurulamadı (humanoid rig değil), " +
                "ceset hareketsiz kalacak.");
            return;
        }

        for (int i = 0; i < ragdoll.Count; i++)
            ragdoll[i].Body.isKinematic = !isServer;

        if (sync != null)
            sync.Bind(ragdoll);
    }

    /// <summary>
    /// `PlayerBodyVisual.HideBone` birinci şahısta kafa kemiğini sıfıra
    /// ölçekliyordu (bölüm 14); o an kaynak birinci şahıssa kopya da aynı
    /// sıfırlanmış kemikle gelir. Ceset üçüncü şahıstan görüleceği için kafa
    /// GÖRÜNMELİ — kemiği 1'e geri alıyoruz.
    /// </summary>
    private static void ResetHeadBone(Animator animator)
    {
        if (animator == null || !animator.isHuman)
            return;

        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
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
