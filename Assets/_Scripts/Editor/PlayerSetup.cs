using UnityEditor;
using UnityEngine;

/// <summary>
/// Oyuncuyu kurar ve **her çalıştırıldığında eksikleri tamamlar**.
///
/// Eskiden Player varsa "önce sil" deyip vazgeçiyordu; yeni bir bileşen
/// eklendiğinde kullanıcının oyuncuyu silip baştan kurması gerekiyordu ve
/// unutulduğunda sistemler sessizce çalışmıyordu. Artık var olan objeye
/// dokunmadan sadece eksik parçaları ekliyor, referansları tazeliyor.
///
/// Menü: Yakalamaca > Sahneye Oyuncu Kur
/// </summary>
public static class PlayerSetup
{
    private const string PlayerName = "Player";
    private const string BodyName = "Govde";
    private const string FlashlightName = "Fener";

    [MenuItem("Yakalamaca/Sahneye Oyuncu Kur")]
    private static void SetupPlayer()
    {
        GameObject player = GameObject.Find(PlayerName);
        bool createdNow = player == null;

        if (createdNow)
        {
            player = new GameObject(PlayerName);
            Undo.RegisterCreatedObjectUndo(player, "Oyuncu Kur");

            // Kapsülün merkezi boyun yarısı kadar yukarıda: ayaklar tam zeminde.
            player.transform.position = new Vector3(0f, 36f * PlayerController.UnitsToMeters, 0f);
        }

        CharacterController controller = EnsureController(player);
        Camera camera = EnsureCamera(player);
        Transform body = EnsureBody(player, controller);
        Light flashlightSpot = EnsureFlashlightLight(camera);

        // Bileşenler: yoksa eklenir, varsa ayarlarına dokunulmaz.
        Ensure<PlayerInputSource>(player);
        PlayerController playerController = Ensure<PlayerController>(player);
        PlayerInteractor interactor = Ensure<PlayerInteractor>(player);
        CapsuleBodyVisual bodyVisual = Ensure<CapsuleBodyVisual>(player);
        Flashlight flashlight = Ensure<Flashlight>(player);
        FootstepAudio footsteps = Ensure<FootstepAudio>(player);
        CameraBob cameraBob = Ensure<CameraBob>(player);

        AudioSource footstepSource = EnsureFootstepSource(player, footsteps);

        // Referanslar her seferinde tazeleniyor; yeniden atamak güvenli.
        Wire(playerController, "cameraTransform", camera.transform);
        Wire(interactor, "viewTransform", camera.transform);
        Wire(bodyVisual, "body", body);
        Wire(flashlight, "spotLight", flashlightSpot);
        Wire(cameraBob, "cameraTransform", camera.transform);

        SerializedObject serializedFootsteps = new SerializedObject(footsteps);
        serializedFootsteps.FindProperty("source").objectReferenceValue = footstepSource;
        AudioSetupUtility.AssignClips(serializedFootsteps.FindProperty("lightSteps"), "Adim_Hafif_");
        AudioSetupUtility.AssignClips(serializedFootsteps.FindProperty("heavySteps"), "Adim_Agir_");
        AudioSetupUtility.AssignClip(serializedFootsteps.FindProperty("jumpClip"), "Ziplama");
        serializedFootsteps.ApplyModifiedProperties();

        Selection.activeGameObject = player;
        EditorGUIUtility.PingObject(player);

        Debug.Log(
            (createdNow ? "Oyuncu kuruldu. " : "Oyuncu güncellendi, eksik bileşenler tamamlandı. ") +
            "WASD hareket, Shift sprint, Space zıplama, Ctrl eğilme/kayma, E kullan, F fener.\n" +
            "Sahneyi kaydetmeyi unutma (Ctrl+S) — kaydetmezsen Unity kapanınca bu kurulum kaybolur.");
    }

    private static CharacterController EnsureController(GameObject player)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
            return controller;

        // Source oyuncu hull'u: 32x32x72 unit. Zıplama yüksekliği karaktere
        // oranla algılandığı için bu ölçüler GMod hissi açısından kritik.
        controller = Undo.AddComponent<CharacterController>(player);
        controller.height = 72f * PlayerController.UnitsToMeters;  // 1.37 m
        controller.radius = 16f * PlayerController.UnitsToMeters;  // 0.30 m
        controller.center = Vector3.zero;
        controller.slopeLimit = 45f;
        controller.stepOffset = 18f * PlayerController.UnitsToMeters;

        // Unity'nin 0.08 varsayılanı 0.5 yarıçaplı controller için. Bizim
        // yarıçapımızda kapsülü şişirip dar geçitlerde sıkıştırıyor.
        controller.skinWidth = controller.radius * 0.1f;

        return controller;
    }

    private static Camera EnsureCamera(GameObject player)
    {
        Camera camera = player.GetComponentInChildren<Camera>();
        if (camera != null)
            return camera;

        // Sahnedeki mevcut kamerayı yeniden kullanıyoruz — ikinci bir
        // AudioListener oluşturup uyarı almamak için.
        camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            Undo.RegisterCreatedObjectUndo(cameraObject, "Oyuncu Kur");
        }

        Undo.SetTransformParent(camera.transform, player.transform, "Oyuncu Kur");

        // Source göz hizası ayaktan 64u; kapsül merkezi 36u'da.
        camera.transform.localPosition = new Vector3(0f, (64f - 36f) * PlayerController.UnitsToMeters, 0f);
        camera.transform.localRotation = Quaternion.identity;

        return camera;
    }

    private static Transform EnsureBody(GameObject player, CharacterController controller)
    {
        Transform existing = player.transform.Find(BodyName);
        if (existing != null)
            return existing;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = BodyName;
        Object.DestroyImmediate(body.GetComponent<Collider>()); // çarpışmayı CharacterController yapıyor
        body.transform.SetParent(player.transform, false);

        // Unity kapsülü 2 birim boyunda, 0.5 yarıçapında.
        body.transform.localPosition = controller.center;
        body.transform.localScale = new Vector3(
            controller.radius * 2f, controller.height / 2f, controller.radius * 2f);

        Undo.RegisterCreatedObjectUndo(body, "Oyuncu Kur");
        return body.transform;
    }

    private static Light EnsureFlashlightLight(Camera camera)
    {
        Transform existing = camera.transform.Find(FlashlightName);
        if (existing != null)
            return existing.GetComponent<Light>();

        GameObject flashlightObject = new GameObject(FlashlightName);
        flashlightObject.transform.SetParent(camera.transform, false);

        Light spot = flashlightObject.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 26f;
        spot.spotAngle = 55f;
        spot.intensity = 2.6f;
        spot.color = new Color(0.95f, 0.95f, 0.85f);
        spot.shadows = LightShadows.Hard; // gölgeyi asıl bu veriyor

        Undo.RegisterCreatedObjectUndo(flashlightObject, "Oyuncu Kur");
        return spot;
    }

    /// <summary>
    /// Ayak sesi kaynağı. Bileşende zaten bağlı bir kaynak varsa onu kullanır —
    /// komut tekrar çalıştırıldığında AudioSource'lar birikmesin.
    /// </summary>
    private static AudioSource EnsureFootstepSource(GameObject player, FootstepAudio footsteps)
    {
        SerializedObject serialized = new SerializedObject(footsteps);
        AudioSource existing = serialized.FindProperty("source").objectReferenceValue as AudioSource;
        if (existing != null)
            return existing;

        AudioSource source = Undo.AddComponent<AudioSource>(player);
        source.playOnAwake = false;
        source.spatialBlend = 1f; // 3D: diğerleri yerini sesten bulabilsin
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 28f;

        return source;
    }

    private static T Ensure<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    private static void Wire(Object target, string propertyName, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);

        if (property == null)
        {
            Debug.LogError($"{target.GetType().Name}: '{propertyName}' alanı bulunamadı.");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }
}
