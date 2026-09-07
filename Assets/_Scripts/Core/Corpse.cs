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
/// ### Fizik: yalnızca SUNUCU simüle ediyor
///
/// Prefabtaki `NetworkRigidbodyReliable`, `syncDirection = ServerToClient`
/// ile kuruluyor (Editor > CorpseSetup). Bu, Mirror'ın kendi mekanizması:
/// sunucuda (host'ta) Rigidbody gerçekten simüle ediliyor, her istemcide
/// `isKinematic = true` yapılıp yalnızca gelen pozisyon uygulanıyor. Aksi
/// hâlde her istemci kendi başına fizik yürütür ve birkaç itme sonra herkes
/// cesedi FARKLI yerde görürdü — bu oyunun "his istemcide, karar sunucuda"
/// kuralının (CLAUDE.md bölüm 4) fizik karşılığı: kim nereye düştü kararını
/// tek yer veriyor.
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
[RequireComponent(typeof(Rigidbody))]
public class Corpse : NetworkBehaviour
{
    /// <summary>
    /// Hangi oyuncunun bedeni kopyalanacak. Spawn'dan ÖNCE sunucuda yazılıyor
    /// (bkz. RoundManager.ServerSpawnCorpse), yani her istemciye İLK durum
    /// olarak gidiyor — `OnStartClient` çalıştığında zaten dolu.
    /// </summary>
    [SyncVar] private uint victimNetId;

    /// <summary>
    /// Sunucu tarafında hız tavanı — güvenlik payı. Ceset canavarın TAM
    /// üstünde doğuyor (bölüm 17: kill animasyonu ikisini iç içe varsayıyor);
    /// `RoundManager.ServerSpawnCorpse` doğar doğmaz oradaki oyuncu
    /// collider'larıyla çarpışmayı geçici kapatıyor, ama beklenmedik bir
    /// çakışma (ör. duvara çok yakın bir ölüm) yine de tek karelik bir
    /// patlama üretebilir. Sprintin (~7.6 m/s) belirgin üstünde bir tavan,
    /// normal itmeleri hiç etkilemeden bu uç durumu kırpıyor.
    /// </summary>
    [SerializeField] private float maxSpeed = 8f;

    private Rigidbody body;

    private void Awake() => body = GetComponent<Rigidbody>();

    /// <summary>Yalnızca sunucu — kinematik (istemci) kopyalarda velocity zaten anlamsız.</summary>
    private void FixedUpdate()
    {
        if (!isServer || body.isKinematic)
            return;

        if (body.velocity.sqrMagnitude > maxSpeed * maxSpeed)
            body.velocity = body.velocity.normalized * maxSpeed;
    }

    /// <summary>Yalnızca sunucu çağırır, spawn'dan önce.</summary>
    [Server]
    public void ServerInit(uint victim) => victimNetId = victim;

    public override void OnStartClient()
    {
        base.OnStartClient();
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
        Transform source = ResolveVictimBody();
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
        // taşınmamalı.
        clone.transform.localScale = Vector3.one;

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
    /// `victimNetId`'den kurbanın şu anki gövde transform'unu çözer.
    /// İstemcide ve sunucuda (host) ayrı sözlükler var; host ikisine de
    /// sahip, adanmış sunucuda yalnızca ikincisi dolu (bölüm 17'deki
    /// `ResolveKiller`'la aynı desen).
    /// </summary>
    private Transform ResolveVictimBody()
    {
        RoundParticipant victim = ResolveParticipant(victimNetId);
        return victim != null ? victim.CorpseSourceBody : null;
    }

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
