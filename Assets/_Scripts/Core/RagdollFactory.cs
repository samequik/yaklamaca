using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Humanoid bir iskelete çalışma anında ragdoll kurar: her ana kemiğe
/// Rigidbody + Collider, aralarına `CharacterJoint`.
///
/// **Neden çalışma anında, editör aracıyla değil.** Ceset görselini `Corpse`
/// zaten çalışma anında kurbanın gövdesinden klonluyor (bkz. Corpse.cs) —
/// ortada önceden hazırlanmış bir prefab yok. Ayrıca ragdoll yalnızca ölünce
/// gerekiyor; canlı oyuncunun iskeletinde 11 Rigidbody taşımanın anlamı yok.
///
/// **Kemiklere adıyla değil ROLÜYLE ulaşılıyor** (`HumanBodyBones`): model
/// değişirse kemik adları değişir, `Hips`/`Spine`/`LeftUpperArm` değişmez.
/// Aynı desen `PlayerBodyVisual.ResolveNeck` ve `MonsterAura`'da da var
/// (CLAUDE.md bölüm 14).
///
/// ### Patlamaya karşı: maxDepenetrationVelocity
///
/// Ceset canavarın TAM üstünde doğuyor (bölüm 17: kill animasyonu ikisini iç
/// içe varsayıyor). Üst üste binen iki collider'ı PhysX varsayılan ayarlarla
/// ayırmaya kalkınca ortaya roket gibi fırlayan bir gövde çıkıyordu. Her
/// parçaya `maxDepenetrationVelocity` konuyor: çakışma yine çözülüyor ama
/// ayrılma hızı sınırlı, yani gövde itilip kenara kayıyor, uçmuyor. Bu,
/// motorun kendi ayarı olduğu için elle çarpışma kapatmaktan çok daha güvenli.
/// </summary>
public static class RagdollFactory
{
    /// <summary>Kurulan tek bir ragdoll parçası.</summary>
    public sealed class Part
    {
        public Transform Bone;
        public Rigidbody Body;
        public Collider Collider;
    }

    /// <summary>
    /// Kütle payları (toplamın oranı). Kabaca insan vücudu dağılımı — tam
    /// doğru olması gerekmiyor, önemli olan gövdenin kollardan ağır olması:
    /// tersi olsaydı ceset kollarının üstünde sallanırdı.
    /// </summary>
    private const float HipsShare = 0.15f;
    private const float TorsoShare = 0.30f;
    private const float HeadShare = 0.08f;
    private const float UpperArmShare = 0.03f;
    private const float LowerArmShare = 0.02f;
    private const float UpperLegShare = 0.11f;
    private const float LowerLegShare = 0.06f;

    /// <summary>Kemik uzunluğuna oranla collider yarıçapı.</summary>
    private const float RadiusRatio = 0.18f;

    /// <summary>
    /// Ayrılma hızı tavanı (m/s). Doğduğu anda bir oyuncunun içinde kalan
    /// gövdenin fırlamasını engelleyen asıl ayar — yukarıdaki kutuya bak.
    /// </summary>
    private const float MaxDepenetration = 2f;


    /// <summary>
    /// Ragdoll'u kurar. Kemikler eksikse (humanoid olmayan rig) boş liste
    /// döndürüyor — çağıran buna bakıp sessizce statik görselle devam ediyor,
    /// çünkü yarım kurulmuş bir ragdoll hiç ragdoll olmamasından kötü.
    ///
    /// İlk eleman DAİMA kalça (hips): `RagdollSync` pozisyonu ondan okuyor.
    /// </summary>
    /// <param name="resolve">
    /// Kemik rolünü transform'a çeviren fonksiyon. **Animator doğrudan
    /// alınmıyor, bilerek:** `Animator.GetBoneTransform` yalnızca animatör
    /// bağlı ve başlatılmışken çalışıyor; taze `Instantiate` edilmiş,
    /// kapatılmak üzere olan bir animatörde null dönebiliyor ve ragdoll
    /// sessizce hiç kurulmuyor. Çağıran (`Corpse`) bu yüzden kemikleri
    /// CANLI kurbanın animatöründen çözüp yola göre klonda buluyor.
    /// </param>
    public static List<Part> Build(Func<HumanBodyBones, Transform> resolve,
        float totalMass, int layer)
    {
        List<Part> parts = new List<Part>();

        if (resolve == null)
            return parts;

        Transform hips = resolve(HumanBodyBones.Hips);
        Transform spine = resolve(HumanBodyBones.Spine);
        Transform chest = resolve(HumanBodyBones.Chest);
        Transform torso = chest != null ? chest : spine;
        Transform head = resolve(HumanBodyBones.Head);
        Transform neck = resolve(HumanBodyBones.Neck);

        Transform leftUpperArm = resolve(HumanBodyBones.LeftUpperArm);
        Transform leftLowerArm = resolve(HumanBodyBones.LeftLowerArm);
        Transform leftHand = resolve(HumanBodyBones.LeftHand);

        Transform rightUpperArm = resolve(HumanBodyBones.RightUpperArm);
        Transform rightLowerArm = resolve(HumanBodyBones.RightLowerArm);
        Transform rightHand = resolve(HumanBodyBones.RightHand);

        Transform leftUpperLeg = resolve(HumanBodyBones.LeftUpperLeg);
        Transform leftLowerLeg = resolve(HumanBodyBones.LeftLowerLeg);
        Transform leftFoot = resolve(HumanBodyBones.LeftFoot);

        Transform rightUpperLeg = resolve(HumanBodyBones.RightUpperLeg);
        Transform rightLowerLeg = resolve(HumanBodyBones.RightLowerLeg);
        Transform rightFoot = resolve(HumanBodyBones.RightFoot);

        // Kalça ve gövde olmadan ragdoll kurulamaz; gerisi eksik olabilir.
        if (hips == null || torso == null)
        {
            Debug.LogWarning("RagdollFactory: kalça " +
                $"(hips={hips != null}) ya da gövde (torso={torso != null}) kemiği " +
                "çözülemedi, ragdoll kurulmadı.");
            return parts;
        }

        Part hipsPart = AddCapsule(parts, hips, torso.position, HipsShare * totalMass, layer);
        Part torsoPart = AddCapsule(parts, torso,
            HeadAnchor(head, neck, torso), TorsoShare * totalMass, layer);

        Join(torsoPart, hipsPart, 20f, 20f, 25f);

        if (head != null)
        {
            // Kafa küre: uca doğru uzayan bir kemiği yok, kapsül kurmak için
            // ölçü kalmıyor.
            float headRadius = neck != null
                ? Vector3.Distance(head.position, neck.position) * 0.6f
                : 0.12f;

            Part headPart = AddSphere(parts, head, headRadius, HeadShare * totalMass, layer);
            Join(headPart, torsoPart, 25f, 25f, 25f);
        }

        AddLimb(parts, torsoPart, leftUpperArm, leftLowerArm, leftHand,
            UpperArmShare * totalMass, LowerArmShare * totalMass, layer);

        AddLimb(parts, torsoPart, rightUpperArm, rightLowerArm, rightHand,
            UpperArmShare * totalMass, LowerArmShare * totalMass, layer);

        AddLimb(parts, hipsPart, leftUpperLeg, leftLowerLeg, leftFoot,
            UpperLegShare * totalMass, LowerLegShare * totalMass, layer);

        AddLimb(parts, hipsPart, rightUpperLeg, rightLowerLeg, rightFoot,
            UpperLegShare * totalMass, LowerLegShare * totalMass, layer);

        return parts;
    }

    /// <summary>
    /// Aynı ragdoll'un parçalarını birbirine çarpmaz yapar.
    ///
    /// ### Bunu atlamak cesedi paramparça ediyordu
    ///
    /// Kemikten üretilen kapsüller eklem yerlerinde **kaçınılmaz olarak iç içe
    /// geçiyor**: gövde kapsülü ile iki uyluk kalçada üst üste biniyor, iki
    /// uyluk da kasıkta birbirine giriyor. Eklemle bağlı çiftler zaten
    /// çarpışmıyor (`CharacterJoint` varsayılanı), ama **gövde ile uyluk
    /// eklemle bağlı DEĞİL** — ikisi de kalçaya bağlı, birbirine değil. PhysX
    /// bu çakışmayı her karede ayırmaya çalışıyor, eklemler geri çekiyor,
    /// ortaya sonu gelmeyen bir itiş kakış çıkıyor: gövde yerinde durmuyor,
    /// parçalar birbirinin etrafında dönüyor ve sonunda zincir esneyip kopmuş
    /// gibi görünüyor.
    ///
    /// Bedeli: uzuvlar birbirinin içinden geçebiliyor. Yerde yatan bir ceset
    /// için görünmez bir kusur; karşılığında gövde sakin sakin oturup uykuya
    /// geçiyor. Oyunların çoğu ragdoll'da tam olarak bu takası yapıyor.
    /// </summary>
    public static void DisableSelfCollision(List<Part> parts)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Collider == null)
                continue;

            for (int j = i + 1; j < parts.Count; j++)
            {
                if (parts[j].Collider != null)
                    Physics.IgnoreCollision(parts[i].Collider, parts[j].Collider, true);
            }
        }
    }

    /// <summary>Gövde kapsülünün uzayacağı nokta: kafa varsa kafa, yoksa boyun.</summary>
    private static Vector3 HeadAnchor(Transform head, Transform neck, Transform torso)
    {
        if (head != null)
            return head.position;

        if (neck != null)
            return neck.position;

        return torso.position + torso.up * 0.3f;
    }

    /// <summary>
    /// Üst + alt uzuv çifti. Biri eksikse o parça atlanıyor; kol/bacak
    /// olmadan da ceset ayakta (yatıyor) kalıyor.
    /// </summary>
    private static void AddLimb(List<Part> parts, Part parent, Transform upper, Transform lower,
        Transform end, float upperMass, float lowerMass, int layer)
    {
        if (upper == null || lower == null)
            return;

        Part upperPart = AddCapsule(parts, upper, lower.position, upperMass, layer);
        Join(upperPart, parent, 20f, 20f, 45f);

        if (end == null)
            return;

        Part lowerPart = AddCapsule(parts, lower, end.position, lowerMass, layer);

        // Dirsek ve diz tek yönde bükülüyor: burulma dar, salınım geniş.
        Join(lowerPart, upperPart, 5f, 5f, 60f);
    }

    /// <summary>
    /// Kemikten hedef noktaya uzanan bir kapsül + Rigidbody kurar.
    ///
    /// Kapsülün ekseni, hedefin kemik yerel uzayındaki BASKIN eksenine
    /// oturtuluyor. Humanoid rig'lerde kemikler zaten tek eksende uzanıyor,
    /// yani bu yaklaşım silüeti doğru sarıyor; birebir hizalama gerekmiyor,
    /// ortada bir ceset var, bir çarpışma kutusu değil.
    /// </summary>
    private static Part AddCapsule(List<Part> parts, Transform bone, Vector3 endPoint,
        float mass, int layer)
    {
        Vector3 local = bone.InverseTransformPoint(endPoint);
        float length = local.magnitude;

        if (length < 0.01f)
            length = 0.1f;

        // Collider AYRI bir çocuk objede duruyor ve kemiğin yönüne tam
        // hizalanıyor. Doğrudan kemiğe koyulunca kapsül yalnızca X/Y/Z'den
        // birine yaslanabiliyordu; çapraz duran bir kemikte çarpışma hacmi
        // görünen uzuvdan belirgin şekilde taşıyor ve oyuncu "gövdenin
        // içinde görünmez bir şey var" diye hissediyordu.
        GameObject holder = new GameObject("RagdollCollider");
        holder.transform.SetParent(bone, false);
        holder.transform.localPosition = Vector3.zero;
        holder.transform.localRotation = Quaternion.FromToRotation(Vector3.up, local.normalized);

        if (layer >= 0)
            holder.layer = layer;

        CapsuleCollider capsule = holder.AddComponent<CapsuleCollider>();
        capsule.direction = 1; // hizalandığı için daima Y
        capsule.height = length;
        capsule.radius = Mathf.Max(length * RadiusRatio, 0.02f);
        capsule.center = new Vector3(0f, length * 0.5f, 0f);

        return Finish(parts, bone, capsule, mass, layer);
    }

    private static Part AddSphere(List<Part> parts, Transform bone, float radius,
        float mass, int layer)
    {
        SphereCollider sphere = bone.gameObject.AddComponent<SphereCollider>();
        sphere.radius = Mathf.Max(radius, 0.03f);

        return Finish(parts, bone, sphere, mass, layer);
    }

    /// <summary>
    /// Ceset yüzeyinin sürtünmesi. Unity'nin varsayılanı (0.6) bir kasa için
    /// makul ama ölü bir gövde için fazla kaygan: bir kez itilen ceset
    /// koridorda kayıp gidiyordu.
    ///
    /// `Maximum` birleştirmesi bilerek: zeminin materyali ne olursa olsun
    /// yüksek olan kazanıyor, yani ceset her zemin üstünde aynı ağırlıkta
    /// duruyor. Sıçrama sıfır — ölü bir gövde zıplamaz.
    ///
    /// Çalışma anında üretiliyor, varlık olarak değil: ragdoll'un tamamı
    /// zaten çalışma anında kuruluyor (bkz. sınıf notu), tek bir materyal
    /// için proje dosyası eklemenin karşılığı yok.
    /// </summary>
    private static PhysicMaterial surface;

    private static PhysicMaterial Surface()
    {
        if (surface != null)
            return surface;

        surface = new PhysicMaterial("Ceset")
        {
            dynamicFriction = 0.9f,
            staticFriction = 0.95f,
            bounciness = 0f,
            frictionCombine = PhysicMaterialCombine.Maximum,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };

        return surface;
    }

    private static Part Finish(List<Part> parts, Transform bone, Collider collider,
        float mass, int layer)
    {
        collider.sharedMaterial = Surface();

        if (layer >= 0)
            bone.gameObject.layer = layer;

        Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
        body.mass = Mathf.Max(mass, 0.1f);
        body.useGravity = true; // varsayılan zaten böyle; ceset "havada kaldı" hatasından sonra açıkça yazılıyor
        // **Ağırlık hissi buradan geliyor.** İlk ayarda ceset "kaygan ve hafif"
        // hissettiriyordu: bir kez itince kayıp gidiyordu. Üç şey birden
        // değişti — yüzey sürtünmesi (aşağıdaki fizik materyali), doğrusal
        // sönümleme ve açısal sönümleme. Ölü bir gövde zemine yapışır, buz
        // gibi kaymaz.
        body.drag = 0.9f;
        body.angularDrag = 3f;

        // Eklemler varsayılan çözücü adımıyla yaylanıp titriyor; ragdoll için
        // yükseltmek oturmayı belirgin şekilde sakinleştiriyor.
        body.solverIterations = 16;
        body.solverVelocityIterations = 8;

        // Doğuş anındaki çakışmanın patlamaya dönmesini engelleyen ayar —
        // sınıf açıklamasındaki kutuya bak.
        body.maxDepenetrationVelocity = MaxDepenetration;

        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // **Interpolate KAPALI, bilerek.** Ragdoll kemikleri birbirinin ÇOCUĞU;
        // interpolasyon her gövdeyi dünya uzayında ayrı ayrı yumuşatıyor ve
        // çocuk, ebeveyninin yumuşatılmış konumundan kendi yerelini yeniden
        // hesaplıyor. Hata zincir boyunca birikiyor ve model gözle görülür
        // şekilde dağılmış gibi görünüyor.
        body.interpolation = RigidbodyInterpolation.None;

        Part part = new Part { Bone = bone, Body = body, Collider = collider };
        parts.Add(part);
        return part;
    }

    /// <summary>
    /// İki parçayı `CharacterJoint` ile bağlar. Bağlantı noktası kemiğin
    /// kendi orijini — humanoid rig'te eklem zaten orada, o yüzden `anchor`
    /// sıfırda bırakılıyor.
    ///
    /// `enablePreprocessing` kapalı: açıkken çok bağlı ve iç içe geçmiş
    /// zincirler (ragdoll tam olarak öyle) çözücüde savrulabiliyor.
    /// </summary>
    private static void Join(Part child, Part parent, float twistLow, float twistHigh, float swing)
    {
        if (child == null || parent == null)
            return;

        CharacterJoint joint = child.Bone.gameObject.AddComponent<CharacterJoint>();
        joint.connectedBody = parent.Body;
        joint.enablePreprocessing = false;

        // **Projeksiyon KAPALI.** Bir ara uzuv kopmasına karşı açılmıştı, ama
        // projeksiyon kinematik bir işlem: gövdeleri kısıtı sağlamak için
        // zorla taşıyor ve zincirin tamamını yerinde çivileyebiliyor —
        // ceset havada asılı kalıp hiç düşmüyordu. Kopmanın gerçek sebebi
        // zaten parçaların birbiriyle çarpışmasıydı ve o kapatıldı
        // (DisableSelfCollision); projeksiyona gerek kalmadı.
        joint.enableProjection = false;

        joint.lowTwistLimit = new SoftJointLimit { limit = -twistLow };
        joint.highTwistLimit = new SoftJointLimit { limit = twistHigh };
        joint.swing1Limit = new SoftJointLimit { limit = swing };
        joint.swing2Limit = new SoftJointLimit { limit = swing };
    }

}
