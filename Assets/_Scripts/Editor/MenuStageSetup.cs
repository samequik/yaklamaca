using UnityEditor;
using UnityEngine;

/// <summary>
/// Menünün arkasındaki karakter sahnesini kurar. `Menü Kur` çağırıyor.
///
/// Sahne haritadan **200 metre aşağıda** duruyor. Ayrı bir katman açmak yerine
/// uzaklık kullanmanın sebebi: projede dört katman var (bölüm 16) ve beşinci
/// bir katman `LayerSetup`'ı, maskeleri ve çarpışma matrisini ilgilendirirdi.
/// Uzaklık aynı işi hiçbir şeye dokunmadan yapıyor — kameranın görüş alanına
/// haritadan hiçbir şey girmiyor.
///
/// **Kök KAPALI kuruluyor.** Işıkları ve kamerası menü açılana kadar motorda
/// hiç görünmesin diye; `MenuStage` gerektiğinde açıyor.
/// </summary>
internal static class MenuStageSetup
{
    private static readonly Vector3 StagePosition = new Vector3(0f, -200f, 0f);

    internal static void Build()
    {
        // Kendi grubunu yeniden kuran güvenli araç deseni (bölüm 0): yalnızca
        // kendi kökünü siliyor, haritaya ve menüye dokunmuyor.
        GameObject existing = GameObject.Find(MenuStage.StageName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        GameObject root = new GameObject(MenuStage.StageName);
        root.transform.position = StagePosition;
        Undo.RegisterCreatedObjectUndo(root, "Menü Kur");

        BuildCamera(root.transform);
        BuildLights(root.transform);

        GameObject turntable = new GameObject("Doner");
        turntable.transform.SetParent(root.transform, false);

        // Kaçan solda, canavar sağda ve ikisi de hafifçe içe dönük: birbirine
        // bakan iki figür, yan yana dizilmiş iki heykelden daha canlı duruyor.
        AddCharacter(turntable.transform, "Kacan", RunnerSetup.ModelPath,
            RunnerSetup.ControllerPath, new Vector3(-0.62f, 0f, 0f), 16f, 1.40f);

        AddCharacter(turntable.transform, "Canavar", RunnerSetup.MonsterModelPath,
            MonsterSetup.ControllerPath, new Vector3(0.68f, 0f, 0.25f), -18f, 1.80f);

        root.SetActive(false);
    }

    private static void BuildCamera(Transform parent)
    {
        GameObject cameraObject = new GameObject("Kamera", typeof(Camera), typeof(MenuStageCamera));
        cameraObject.transform.SetParent(parent, false);

        // Göğüs hizasından, hafif yukarıdan. Ayaklar bilerek kadraj dışında:
        // zemin koymaya gerek kalmıyor ve karakterlerin havada durduğu
        // görünmüyor.
        cameraObject.transform.localPosition = new Vector3(0f, 1.35f, -3.1f);
        cameraObject.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);

        Camera camera = cameraObject.GetComponent<Camera>();
        camera.fieldOfView = 34f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 25f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.018f, 0.013f, 0.016f, 1f);
        camera.allowHDR = false;
        camera.allowMSAA = true;

        // Kapalı: `MenuStage` hedef dokusunu verip açıyor. Açık bırakılsaydı
        // hedefi olmayan bir kamera doğrudan ekrana çizerdi.
        camera.enabled = false;
    }

    /// <summary>
    /// Üç ışık: anahtar, kenar ve dolgu.
    ///
    /// **Hiçbiri Directional DEĞİL, bilerek.** Yönlü ışığın konumu yok, yani
    /// 200 m öteden bile bütün haritayı aydınlatırdı ve bölüm 5'in "fenersiz
    /// görülmemeli" kuralını tek başına delerdi. Menzilli ışıklar yalnızca
    /// sahneyi görüyor.
    /// </summary>
    private static void BuildLights(Transform parent)
    {
        Light key = CreateLight(parent, "Isik_Anahtar", LightType.Spot,
            new Vector3(-1.9f, 2.9f, -2.3f), new Color(1f, 0.94f, 0.88f), 9f, 9f);
        key.spotAngle = 62f;
        key.transform.localRotation = Quaternion.LookRotation(
            (new Vector3(-0.25f, 1.1f, 0f) - key.transform.localPosition).normalized);

        // Kenar ışığı KIRMIZI: oyunun kimliği (bölüm 5'teki canavar hâlesi) ve
        // iki figürü koyu arka plandan ayıran şey.
        CreateLight(parent, "Isik_Kenar", LightType.Point,
            new Vector3(1.7f, 2.0f, 1.9f), new Color(0.95f, 0.26f, 0.2f), 7f, 7f);

        CreateLight(parent, "Isik_Dolgu", LightType.Point,
            new Vector3(1.5f, 1.2f, -2.6f), new Color(0.55f, 0.62f, 0.82f), 2.2f, 8f);
    }

    private static Light CreateLight(Transform parent, string name, LightType type,
        Vector3 position, Color color, float intensity, float range)
    {
        GameObject lightObject = new GameObject(name, typeof(Light));
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localPosition = position;

        Light light = lightObject.GetComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;

#if UNITY_EDITOR
        // Pişirmeye girmemeli: sahne static değil ve zaten menüde açılıyor.
        // `lightmapBakeType` yalnızca editörde var (bölüm 7).
        light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
        return light;
    }

    private static void AddCharacter(Transform parent, string name, string modelPath,
        string controllerPath, Vector3 position, float yaw, float targetHeight)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogWarning($"Menü sahnesi: {modelPath} bulunamadı, {name} eklenmedi.");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        instance.name = name;
        instance.transform.localPosition = position;
        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        ScaleToHeight(instance, targetHeight);

        Animator animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
        animator.runtimeAnimatorController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);

        // Kök hareketi KAPALI: oyunda da kapalı (bölüm 17) ve açık olsaydı
        // boştaki klip karakteri yavaşça platformdan kaydırırdı.
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    /// <summary>
    /// Modeli görünen boyuna göre ölçekliyor.
    ///
    /// İki model bambaşka ölçülerde geliyor ve oyundaki ölçekleri oyuncu
    /// kapsülünden hesaplanıyor (bölüm 17) — burada kapsül yok. Renderer
    /// sınırlarından ölçeklemek aynı oranı bağımsızca üretiyor: canavar
    /// kaçandan görünür şekilde iri kalıyor, ki kovalayanın büyük görünmesi
    /// bilinçli bir tasarım kararı.
    /// </summary>
    private static void ScaleToHeight(GameObject instance, float targetHeight)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        if (bounds.size.y <= 0.001f)
            return;

        float scale = targetHeight / bounds.size.y;
        instance.transform.localScale *= scale;
    }
}
