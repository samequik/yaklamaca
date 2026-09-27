using UnityEditor;
using UnityEngine;

/// <summary>
/// Canavarın eline takılan **beyzbol sopası** — model dosyası yok, mesh
/// prosedürel üretiliyor.
///
/// ### Neden prosedürel
///
/// Kullanıcının elinde sopa modeli yoktu ve bir sopa aslında eksen etrafında
/// döndürülmüş bir profil eğrisinden ibaret. Dışarıdan model almak yeni bir
/// varlık, yeni bir materyal ve yeni bir lisans sorusu demekti; otuz satırlık
/// bir lathe (torna) onu tamamen çözüyor. Bu projenin "elle sahne düzenlemek
/// yerine araç yaz" alışkanlığının (bölüm 7) aynısı.
///
/// ### Sopa TAMAMEN GÖRSEL
///
/// Collider'ı YOK ve olmamalı. İsabet kararı sunucunun ışınında
/// (`MonsterAttack`, menzil 2.3 m, `hitHeight` 90 cm — bölüm 4): sopa vermek
/// menzili uzatmıyor, hasarı değiştirmiyor. Bir collider eklemek fiziğe
/// karışır ve canavarın kendi kapsülüyle itişirdi.
///
/// Katman da `Sus`: gövdeyi durdurmayan, ışını kesmeyen süs katmanı
/// (bölüm 16). Sopanın canavarın KENDİ nişan ışınını kesmesi, elinde tuttuğu
/// şeyin hedefi gizlemesi demek olurdu.
///
/// ### Ölçek modele göre TELAFİ EDİLİYOR
///
/// Mesh 1 birim uzunlukta üretiliyor ve kemiğin dünya ölçeği ölçülüp
/// (`lossyScale`) yerel ölçek ona göre yazılıyor. Sebebi: gövde kökü
/// `MonsterSetup.ResolveScale(...) * ExtraScale` ile ölçekleniyor ve bu çarpan
/// MODELE göre değişiyor — sabit bir yerel ölçek, bir modelde oyuncak bir
/// modelde direk üretirdi. Böylece `BatLength` gerçek metre olarak okunuyor.
/// </summary>
public static class MonsterBatBuilder
{
    /// <summary>Sopanın el kemiğine takıldığındaki adı.</summary>
    public const string BatName = "Sopa";

    private const string MeshFolder = "Assets/_Art/Meshes";
    private const string MeshPath = MeshFolder + "/BeyzbolSopasi.asset";
    private const string MaterialFolder = "Assets/_Art/Materials";
    private const string MaterialPath = MaterialFolder + "/Sopa.mat";

    // ---- Ölçüler (gerçek dünya metresi) ----

    /// <summary>Toplam boy. Gerçek bir beyzbol sopası 0.7–0.87 m.</summary>
    private const float BatLength = 0.78f;

    private const float KnobRadius = 0.026f;
    private const float HandleRadius = 0.016f;
    private const float BarrelRadius = 0.032f;

    // ---- Mesh çözünürlüğü ----
    //
    // Karanlık bir koridorda birkaç metreden görünen bir sopa için 16 dilim
    // fazlasıyla yeterli; siluet zaten silindirik. Yükseklikteki 24 halka
    // profilin yumuşak kısmını (sapın namluya geçişi) basamaksız gösteriyor.
    private const int RadialSegments = 16;
    private const int HeightSegments = 24;

    /// <summary>
    /// Sopayı üretip <paramref name="bodyRoot"/>'un SAĞ ELİNE takar ve takılan
    /// objeyi döndürür. Kemik bulunamazsa null döner ve sebebini yazar.
    ///
    /// **Duruşu HER ÇALIŞTIRMADA yeniden yazıyor.** İlk sürüm var olanı
    /// koruyordu ama bu yanlış tarafa düşen bir tercihti: varsayılan yerleşim
    /// değişince araç tekrar çalıştırılsa bile eski duruş yerinde kalıyordu ve
    /// "düzeltme uygulanmadı" gibi görünüyordu.
    ///
    /// Bu projenin kuralı zaten bu (bölüm 16): prefab değerleri koddan yazılır,
    /// elle değil. Sopanın duruşunu ayarlamak isteyen aşağıdaki sabitleri
    /// değiştirip aracı tekrar çalıştırır — tek kaynak burası.
    /// </summary>
    public static GameObject Attach(GameObject bodyRoot)
    {
        if (bodyRoot == null)
            return null;

        Animator animator = bodyRoot.GetComponentInChildren<Animator>(true);

        if (animator == null || !animator.isHuman)
        {
            Debug.LogWarning($"Sopa: {bodyRoot.name} humanoid bir Animator taşımıyor, " +
                "el kemiği bulunamadı. Modelin Rig > Animation Type = Humanoid olmalı.");
            return null;
        }

        // Kapalı bir Animator'da GetBoneTransform null döner (bölüm 21.1'in
        // yedi tuzağından biri). Gövdeler kablolama bitince kapatılıyor, yani
        // burada açık olduğundan emin olmak gerekiyor.
        bool wasActive = bodyRoot.activeSelf;

        if (!wasActive)
            bodyRoot.SetActive(true);

        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);

        if (hand == null)
        {
            Debug.LogWarning($"Sopa: {bodyRoot.name} modelinde sağ el kemiği yok. " +
                "Avatar'ın Configure ekranında RightHand eşlenmemiş olabilir.");

            if (!wasActive)
                bodyRoot.SetActive(false);

            return null;
        }

        Transform existing = hand.Find(BatName);

        if (existing != null)
        {
            ApplyVisual(existing.gameObject);
            PlaceDefault(existing, hand, lowerArm);

            if (!wasActive)
                bodyRoot.SetActive(false);

            return existing.gameObject;
        }

        // `Undo.RegisterCreatedObjectUndo` BİLEREK YOK. Bu metot prefab
        // içeriği (`PrefabUtility.LoadPrefabContents`) üstünde çalışıyor ve
        // orası ayrı, gizli bir önizleme sahnesi — Undo oraya uygulanmıyor,
        // kaydetmek yalnızca Undo yığınını kirletiyor.
        GameObject bat = new GameObject(BatName);
        bat.transform.SetParent(hand, false);

        ApplyVisual(bat);
        PlaceDefault(bat.transform, hand, lowerArm);

        if (!wasActive)
            bodyRoot.SetActive(false);

        return bat;
    }

    /// <summary>Mesh, materyal ve katmanı yazar; duruşa dokunmaz.</summary>
    private static void ApplyVisual(GameObject bat)
    {
        // ?? KULLANILMIYOR, bilerek. Unity `==` operatörünü kendi "yok
        // edilmiş nesne" mantığıyla AŞIRI YÜKLÜYOR ama `??` operatörünü
        // yükleyemiyor: `??` saf referans eşitliğine bakıyor, yani
        // GetComponent'ın döndürdüğü sahte-null'ı "dolu" sanıp AddComponent'ı
        // hiç çağırmıyor. Sonuç, bileşen yokken null olmayan bir referans ve
        // ilk kullanımda `MissingComponentException`.
        //
        // Bu tam olarak 2026-09-20'de yaşandı: araç burada patlayıp prefabı
        // hiç yazamadı. Açık `== null` kontrolü Unity'nin kendi operatörünü
        // kullandığı için doğru çalışıyor.
        MeshFilter filter = bat.GetComponent<MeshFilter>();

        if (filter == null)
            filter = bat.AddComponent<MeshFilter>();

        MeshRenderer renderer = bat.GetComponent<MeshRenderer>();

        if (renderer == null)
            renderer = bat.AddComponent<MeshRenderer>();

        // Savrulmayı yumuşatan bileşen (2026-09-24). Aynı `== null` kuralı:
        // yukarıdaki kutu bunun için de geçerli.
        //
        BatSway sway = bat.GetComponent<BatSway>();

        if (sway == null)
            sway = bat.AddComponent<BatSway>();

        ApplySwaySettings(sway);

        filter.sharedMesh = GetOrCreateMesh();
        renderer.sharedMaterial = GetOrCreateMaterial();

        // Gölge düşürüyor ama gölge ALMIYOR: küçük ve elde sallanan bir
        // nesnenin üstündeki gölge haritası çözünürlüğü yetmiyor ve lekeli
        // görünüyor. Düşürdüğü gölge ise canavarın siluetine katkı veriyor.
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = false;

        LayerSetup.Apply(bat, LayerSetup.Sus);
    }

    /// <summary>
    /// `BatSway`'in ayarlarını HER ÇALIŞTIRMADA yeniden yazar.
    ///
    /// ### Neden yazmak zorunda
    ///
    /// Burada bir süre "ayarlar kodda duruyor, araç yazmıyor" yazıyordu ve
    /// **yanlıştı**: alanlar `[SerializeField]`, yani bileşen prefaba bir kez
    /// girdikten sonra koddaki varsayılan hiçbir şey yapmıyor. Ölçüldü —
    /// bileşen 2026-09-24'te prefaba girmişti ve `maxLagDegrees` orada 60
    /// olarak donmuştu; kodu 35 yapmak ekranda hiçbir şey değiştirmezdi
    /// (bölüm 16'nın tuzağı).
    ///
    /// Sopanın DURUŞU zaten aynı gerekçeyle her çalıştırmada yeniden
    /// yazılıyor (bkz. `Attach`); ayarlar da o kurala alındı.
    ///
    /// **Sayılar `BatSway`'de**, burada değil: iki yerde tutulan bir sayı,
    /// biri değişince öbürünün unutulması demek (menü figürlerinin açısında
    /// tam olarak bu yaşandı, bölüm 13).
    ///
    /// `SerializedObject` gerekiyor, çünkü alanlar private — Inspector'ın
    /// kendi yolundan yazmak `MonsterSetup`'ın prefab alanlarını yazarken
    /// kullandığı yöntemin aynısı.
    /// </summary>
    private static void ApplySwaySettings(BatSway sway)
    {
        SerializedObject serialized = new SerializedObject(sway);

        serialized.FindProperty("followSharpness").floatValue =
            BatSway.DefaultFollowSharpness;
        serialized.FindProperty("maxLagDegrees").floatValue =
            BatSway.DefaultMaxLagDegrees;
        serialized.FindProperty("snapDegrees").floatValue =
            BatSway.DefaultSnapDegrees;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// İlk yerleşim. **Tahmin, ölçüm değil** — her rig'in el kemiği farklı
    /// yöne bakıyor ve Unity'nin humanoid soyutlaması kemik EKSENLERİNİ
    /// standartlaştırmıyor, yalnızca hangi kemiğin ne olduğunu söylüyor.
    ///
    /// Seçilen yön rig'den bağımsız: **elden ön kola giden doğru**, yani
    /// kolun tersi. Kol aşağı sarkarken bu yön YUKARI bakıyor ve sopa omuza
    /// doğru dikiliyor.
    ///
    /// İlk sürüm ters yöndeydi (ön koldan ele) ve sopa bacağın yanında aşağı
    /// sarkıyordu — oynanışta görülüp düzeltildi. Yön rig'e değil kolun kendi
    /// geometrisine bağlı olduğu için animasyon boyunca doğal takip ediyor.
    /// </summary>
    private static void PlaceDefault(Transform bat, Transform hand, Transform lowerArm)
    {
        if (UseTunedPose)
        {
            bat.localPosition = TunedPosition;
            bat.localRotation = Quaternion.Euler(TunedEuler);
            bat.localScale = Vector3.one * TunedScale;
            return;
        }

        Vector3 axis = Vector3.up;

        if (lowerArm != null)
        {
            // ELDEN ÖN KOLA: kol aşağı sarkarken bu yön yukarıyı gösteriyor.
            Vector3 forearm = lowerArm.position - hand.position;

            if (forearm.sqrMagnitude > 1e-8f)
                axis = hand.InverseTransformDirection(forearm.normalized);
        }

        // Mesh +Y boyunca uzanıyor; onu seçilen eksene çeviriyoruz.
        bat.localRotation = Quaternion.FromToRotation(Vector3.up, axis);

        // Kavrama noktası sapın dibi değil, sapın alt ÜÇTE BİRİ: elin sopanın
        // ucundan tutması gerçekçi değil ve topuz avucun içinde kalıyor.
        // Eksen artık yukarı baktığı için pay NEGATİF: topuz elin biraz
        // altında kalıyor, namlu yukarı uzanıyor.
        float scale = WorldScale(hand);
        bat.localScale = Vector3.one * (BatLength / Mathf.Max(scale, 1e-4f));
        bat.localPosition = axis * (GripOffset * BatLength / Mathf.Max(scale, 1e-4f));
    }

    // ---- Elle bulunan duruş (2026-09-20) ----
    //
    // Aşağıdaki üç değer HESAPLANMIYOR, kullanıcının Play modunda sopayı elde
    // gözle ayarlayıp verdiği sayılar. Play modunda yapılan değişiklik kalıcı
    // olmadığı için (bölüm 25'in kendi notu) buraya yazılıyorlar — böylece
    // araç her çalıştırmada aynı duruşu üretiyor.
    //
    // **Bu sayılar DOMUZ KATİLİN rig'ine ait.** Başka bir model sopa taşırsa
    // el kemiğinin ekseni farklı olacağı için yeniden ayarlanması gerekir;
    // o yüzden `UseTunedPose` kapatılırsa ön koldan hesaplanan eski yol
    // devreye giriyor.
    // `const` DEĞİL `static readonly`: `const` olsaydı derleyici dalı
    // sabit katlayıp aşağıdaki hesaplanan yolu "ulaşılamaz kod" diye
    // uyarırdı. Yedek yol bilerek duruyor — başka bir model sopa taşırsa
    // buradan başlanacak.
    private static readonly bool UseTunedPose = true;

    private static readonly Vector3 TunedPosition =
        new Vector3(-0.053f, 0.1234f, -0.0324f);

    private static readonly Vector3 TunedEuler =
        new Vector3(7.353f, -193.051f, 97.261f);

    private const float TunedScale = 0.8292655f;

    /// <summary>
    /// Kavrama noktasının sopa boyuna oranı. Negatif = topuz elin altında.
    /// Yalnızca <see cref="UseTunedPose"/> kapalıyken kullanılıyor.
    /// </summary>
    private const float GripOffset = -0.16f;

    /// <summary>
    /// Kemiğin dünya ölçeği. Gövde kökü modele göre ölçekleniyor, yani bu sayı
    /// her kostümde farklı — sabit bir yerel ölçek yazmak sopayı bir modelde
    /// oyuncak, bir modelde direk yapardı.
    /// </summary>
    private static float WorldScale(Transform bone)
    {
        Vector3 s = bone.lossyScale;
        return (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f;
    }

    // ---------- Mesh ----------

    private static Mesh GetOrCreateMesh()
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);

        if (existing != null)
            return existing;

        Mesh mesh = BuildMesh();
        EnsureFolder(MeshFolder);
        AssetDatabase.CreateAsset(mesh, MeshPath);

        return mesh;
    }

    /// <summary>
    /// Profili eksen etrafında döndürerek sopayı üretir (lathe).
    ///
    /// Yarıçap iki uçta da sıfıra iniyor, yani gövde KAPALI çıkıyor ve ayrıca
    /// kapak (cap) üçgenleri üretmek gerekmiyor.
    /// </summary>
    private static Mesh BuildMesh()
    {
        int rings = HeightSegments + 1;
        int cols = RadialSegments + 1; // dikiş için son sütun tekrarlanıyor

        Vector3[] vertices = new Vector3[rings * cols];
        Vector2[] uv = new Vector2[rings * cols];

        for (int y = 0; y < rings; y++)
        {
            float t = (float)y / HeightSegments;
            float radius = Profile(t);

            for (int x = 0; x < cols; x++)
            {
                float u = (float)x / RadialSegments;
                float angle = u * Mathf.PI * 2f;
                int i = y * cols + x;

                vertices[i] = new Vector3(
                    Mathf.Cos(angle) * radius, t, Mathf.Sin(angle) * radius);
                uv[i] = new Vector2(u, t);
            }
        }

        int[] triangles = new int[HeightSegments * RadialSegments * 6];
        int tri = 0;

        for (int y = 0; y < HeightSegments; y++)
        {
            for (int x = 0; x < RadialSegments; x++)
            {
                int a = y * cols + x;
                int b = a + 1;
                int c = a + cols;
                int d = c + 1;

                triangles[tri++] = a; triangles[tri++] = c; triangles[tri++] = b;
                triangles[tri++] = b; triangles[tri++] = c; triangles[tri++] = d;
            }
        }

        Mesh mesh = new Mesh { name = "BeyzbolSopasi" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Sopanın yarıçap profili. <paramref name="t"/> 0 (topuz) ile 1 (namlu
    /// ucu) arasında; dönen değer 1 birimlik boya göre oranlı yarıçap.
    ///
    /// Beş bölge: topuzun yuvarlağı · topuz · ince sap · sapın namluya
    /// açılması · namlu ve yuvarlak uç.
    /// </summary>
    private static float Profile(float t)
    {
        float knob = KnobRadius / BatLength;
        float handle = HandleRadius / BatLength;
        float barrel = BarrelRadius / BatLength;

        if (t < 0.02f)                       // topuzun alt yuvarlağı
            return Mathf.Lerp(0f, knob, Smooth(t / 0.02f));
        if (t < 0.06f)                       // topuz sapa iniyor
            return Mathf.Lerp(knob, handle, Smooth((t - 0.02f) / 0.04f));
        if (t < 0.42f)                       // sap
            return handle;
        if (t < 0.72f)                       // sap namluya açılıyor
            return Mathf.Lerp(handle, barrel, Smooth((t - 0.42f) / 0.30f));
        if (t < 0.94f)                       // namlu
            return barrel;

        return Mathf.Lerp(barrel, 0f, Smooth((t - 0.94f) / 0.06f)); // yuvarlak uç
    }

    /// <summary>Kübik yumuşatma — köşeli `Lerp` profilde basamak gösteriyor.</summary>
    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // ---------- Materyal ----------

    /// <summary>
    /// Ahşap görünümlü **Standard** materyal — ışıksız (`Sprites/Default`)
    /// DEĞİL, bilerek: terminal göstergeleri gibi "uzaktan okunması gereken"
    /// yüzeyler ışıksız olmalı (bölüm 11.2), ama sopa bir nesne. Fenerin ve
    /// canavarın kırmızı hâlesinin (bölüm 5) onu aydınlatması, sahnenin geri
    /// kalanıyla aynı dili konuşması demek.
    /// </summary>
    private static Material GetOrCreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (existing != null)
            return existing;

        Shader shader = Shader.Find("Standard");

        if (shader == null)
            return null;

        Material material = new Material(shader)
        {
            name = "Sopa",
            color = new Color(0.34f, 0.20f, 0.11f), // koyu ahşap
        };

        material.SetFloat("_Glossiness", 0.28f);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;

        EnsureFolder(MaterialFolder);
        AssetDatabase.CreateAsset(material, MaterialPath);

        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string leaf = path.Substring(slash + 1);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
