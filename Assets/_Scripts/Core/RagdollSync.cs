using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Ragdoll pozunu sunucudan istemcilere taşır.
///
/// ### Neden gerekli
///
/// Ragdoll 11 ayrı Rigidbody demek ve PhysX makineler arasında birebir aynı
/// sonucu vermiyor. Her istemci kendi başına simüle etseydi, birinin ittiği
/// ceset yalnızca onun ekranında kayardı — yan yana duran iki oyuncu cesedi
/// FARKLI yerde görürdü. Sesli sohbetle oynanan bir oyunda bu anında fark
/// edilir. Bu yüzden **fizik yalnızca sunucuda** çalışıyor, istemcilerdeki
/// gövdeler kinematik ve buradan gelen pozu uyguluyor — CLAUDE.md bölüm 4'ün
/// "his istemcide, karar sunucuda" kuralının fizik karşılığı.
///
/// ### Ne gönderiliyor
///
/// Kalçanın YEREL konumu + her kemiğin YEREL dönüşü. Humanoid bir iskelette
/// hareketin tamamı budur: kemikler dönüyor, yer değiştiren tek şey kök.
/// Kemik hiyerarşisi her istemcide birebir aynı olduğu için (aynı model,
/// aynı sırayla kuruluyor) indeksler tutuyor ve isim/eşleme göndermek
/// gerekmiyor.
///
/// Dönüşler `Compression.CompressQuaternion` ile 4 bayta iniyor: 11 kemikte
/// paket 12 + 44 = **56 bayt**. Üstelik yalnızca gövde HAREKET EDERKEN
/// gönderiliyor; ceset oturunca (bütün Rigidbody'ler uykuya geçince) trafik
/// tamamen kesiliyor.
///
/// ### Neden SyncVar, ClientRpc değil
///
/// SyncVar'ın ilk durumu spawn mesajına giriyor, yani **sonradan bağlanan**
/// oyuncu da cesedi doğru pozda görüyor. ClientRpc yalnızca o an bağlı
/// olanlara gider ve geç gelen, çoktan yere yığılmış bir cesedi hâlâ diz
/// çökmüş hâlde görürdü.
/// </summary>
public class RagdollSync : NetworkBehaviour
{
    [Tooltip("Saniyede kaç kez poz gönderilecek (yalnızca gövde hareket ederken).")]
    [SerializeField] private float sendRate = 12f;

    [Tooltip("İstemcide gelen poza yumuşak geçiş hızı. Paketler seyrek geldiği " +
        "için ham atama kesik kesik görünüyor.")]
    [SerializeField] private float smoothing = 18f;

    [SyncVar(hook = nameof(OnPoseChanged))]
    private byte[] pose;

    private Transform[] bones;
    private Rigidbody[] bodies;

    private Vector3 targetRootPosition;
    private Quaternion[] targetRotations;
    private bool hasTarget;

    private float nextSend;
    private bool sentWhileAsleep;

    /// <summary>
    /// `Corpse` ragdoll'u kurduktan sonra çağırıyor. İlk parça kalça olmalı
    /// (bkz. RagdollFactory.Build).
    /// </summary>
    public void Bind(List<RagdollFactory.Part> parts)
    {
        if (parts == null || parts.Count == 0)
            return;

        bones = new Transform[parts.Count];
        bodies = new Rigidbody[parts.Count];

        for (int i = 0; i < parts.Count; i++)
        {
            bones[i] = parts[i].Bone;
            bodies[i] = parts[i].Body;
        }

        targetRotations = new Quaternion[parts.Count];

        // Bağlanmadan önce gelmiş bir poz varsa (spawn mesajıyla birlikte
        // gelen ilk durum, `Bind`'dan ÖNCE işlenmiş olabilir) şimdi uygula.
        if (!isServer && pose != null && pose.Length > 0)
            Unpack(pose);
    }

    private void FixedUpdate()
    {
        if (!isServer || bones == null || Time.time < nextSend)
            return;

        bool asleep = AllAsleep();

        // Uykudayken susuyoruz — ama uykuya daldıktan SONRA bir kez daha
        // gönderiyoruz ki son duruş herkese ulaşsın. Bunu atlarsak ceset
        // istemcilerde "neredeyse oturmuş" hâlde donup kalır.
        if (asleep && sentWhileAsleep)
            return;

        nextSend = Time.time + 1f / Mathf.Max(sendRate, 1f);
        sentWhileAsleep = asleep;

        pose = Pack();
    }

    private void Update()
    {
        // Sunucuda (host dahil) gerçek fizik zaten çalışıyor; poz uygulamak
        // onu ezerdi.
        if (isServer || bones == null || !hasTarget)
            return;

        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);

        bones[0].localPosition = Vector3.Lerp(bones[0].localPosition, targetRootPosition, t);

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null)
                bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, targetRotations[i], t);
        }
    }

    private bool AllAsleep()
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null && !bodies[i].IsSleeping())
                return false;
        }

        return true;
    }

    private byte[] Pack()
    {
        using (NetworkWriterPooled writer = NetworkWriterPool.Get())
        {
            writer.WriteVector3(bones[0].localPosition);

            for (int i = 0; i < bones.Length; i++)
                writer.WriteUInt(Compression.CompressQuaternion(bones[i].localRotation));

            return writer.ToArray();
        }
    }

    private void OnPoseChanged(byte[] oldValue, byte[] newValue) => Unpack(newValue);

    private void Unpack(byte[] data)
    {
        // `Bind`'dan önce gelen paket saklanmıyor: SyncVar değeri duruyor,
        // Bind sonunda bir kez daha okunuyor.
        if (data == null || data.Length == 0 || bones == null)
            return;

        using (NetworkReaderPooled reader = NetworkReaderPool.Get(data))
        {
            targetRootPosition = reader.ReadVector3();

            for (int i = 0; i < bones.Length; i++)
            {
                if (reader.Remaining < 4)
                    return;

                targetRotations[i] = Compression.DecompressQuaternion(reader.ReadUInt());
            }
        }

        // İlk pakette yumuşatmaya gerek yok: ceset zaten oraya "ait", araya
        // geçiş koymak onu havada süzülür gibi gösterirdi.
        if (!hasTarget)
        {
            hasTarget = true;
            bones[0].localPosition = targetRootPosition;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                    bones[i].localRotation = targetRotations[i];
            }
        }
    }
}
