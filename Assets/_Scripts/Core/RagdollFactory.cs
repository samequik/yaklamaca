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
    /// Eklem kopmasına karşı projeksiyon: bağlı iki gövde bu mesafeden fazla
    /// ayrılırsa PhysX onları zorla geri çekiyor.
    ///
    /// **Bu, uzuvların uçup gitmesine karşı asıl emniyet kemeri.** Eklem
    /// normalde yumuşak bir kısıt: yeterince güçlü bir kuvvet altında esniyor
    /// ve zincir "kopmuş" gibi görünüyor. Projeksiyon bunu geometrik olarak
    /// düzeltiyor, kuvvet hesabına hiç girmeden.
    /// </summary>
    private const float ProjectionDistance = 0.05f;
    private const float ProjectionAngle = 15f;

    /// <summary>
    /// Ragdoll'u kurar. Kemikler eksikse (humanoid olmayan rig) boş liste
    /// döndürüyor — çağıran buna bakıp sessizce statik görselle devam ediyor,
    /// çünkü yarım kurulmuş bir ragdoll hiç ragdoll olmamasından kötü.
    ///
    /// İlk eleman DAİMA kalça (hips): `RagdollSync` pozisyonu ondan okuyor.
    /// </summary>
    public static List<Part> Build(Animator animator, float totalMass, int layer)
    {
        List<Part> parts = new List<Part>();

        if (animator == null)
            return parts;

        // Animatör KAPALI olmamalı: `GetBoneTransform` humanoid eşlemesini
        // ancak bağlıyken çözüyor, kapalıyken null döndürüyor ve ragdoll
        // sessizce hiç kurulmuyor. Çağıran (Corpse.BuildVisual) bu yüzden
        // kemikleri okumayı animatörü kapatmadan ÖNCE yapıyor.
        if (!animator.isHuman)
        {
            Debug.LogWarning($"RagdollFactory: {animator.name} humanoid değil " +
                "(ya da avatar çözülemedi), ragdoll kurulmadı.");
            return parts;
        }

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        Transform torso = chest != null ? chest : spine;
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);

        Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

        Transform rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        Transform leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

        Transform rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

        // Kalça ve gövde olmadan ragdoll kurulamaz; gerisi eksik olabilir.
        if (hips == null || torso == null)
        {
            Debug.LogWarning($"RagdollFactory: {animator.name} üzerinde kalça " +
                $"(hips={hips != null}) ya da gövde (torso={torso != null}) kemiği " +
                "çözülemedi, ragdoll kurulmadı. Animatör kemik sorgusu sırasında " +
                "AÇIK olmalı.");
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

        DisableSelfCollision(parts);

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
    private static void DisableSelfCollision(List<Part> parts)
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

        CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = DominantAxis(local);
        capsule.height = length;
        capsule.radius = Mathf.Max(length * RadiusRatio, 0.02f);
        capsule.center = local * 0.5f;

        return Finish(parts, bone, capsule, mass, layer);
    }

    private static Part AddSphere(List<Part> parts, Transform bone, float radius,
        float mass, int layer)
    {
        SphereCollider sphere = bone.gameObject.AddComponent<SphereCollider>();
        sphere.radius = Mathf.Max(radius, 0.03f);

        return Finish(parts, bone, sphere, mass, layer);
    }

    private static Part Finish(List<Part> parts, Transform bone, Collider collider,
        float mass, int layer)
    {
        if (layer >= 0)
            bone.gameObject.layer = layer;

        Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
        body.mass = Mathf.Max(mass, 0.1f);
        body.useGravity = true; // varsayılan zaten böyle; ceset "havada kaldı" hatasından sonra açıkça yazılıyor
        // Doğrusal sürtünme düşük (ceset itilince kaymalı), AÇISAL sürtünme
        // yüksek: gerçek bir uzuv dönerek savrulmaz, hemen sönümlenir. Düşük
        // tutmak parçaları birbirinin etrafında fır fır döndürüyordu.
        body.drag = 0.1f;
        body.angularDrag = 1.5f;

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

        // Uzuv kopmasına karşı: kısıt esnerse PhysX gövdeleri geometrik olarak
        // geri çekiyor (bkz. ProjectionDistance).
        joint.enableProjection = true;
        joint.projectionDistance = ProjectionDistance;
        joint.projectionAngle = ProjectionAngle;

        joint.lowTwistLimit = new SoftJointLimit { limit = -twistLow };
        joint.highTwistLimit = new SoftJointLimit { limit = twistHigh };
        joint.swing1Limit = new SoftJointLimit { limit = swing };
        joint.swing2Limit = new SoftJointLimit { limit = swing };
    }

    private static int DominantAxis(Vector3 v)
    {
        Vector3 abs = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        if (abs.x >= abs.y && abs.x >= abs.z)
            return 0;

        return abs.y >= abs.z ? 1 : 2;
    }
}
