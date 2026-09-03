using UnityEditor;
using UnityEngine;

/// <summary>
/// Etkileşim sistemini denemek için küçük bir alan kurar: düğmeyle açılan kayar
/// kapı ve eğilerek geçilen havalandırma tüneli. Menü: Yakalamaca > Test Kapısı
/// ve Havalandırma Ekle.
///
/// Yardımcı metotlar bilerek bu dosyaya özel; parkur kurucusundan bağımsız olsun
/// ki parkuru silince burası etkilenmesin.
/// </summary>
public static class InteractionSetup
{
    private const string RootName = "TestEtkilesim";
    private const string MaterialRoot = "Assets/_Art";
    private const string MaterialFolder = MaterialRoot + "/Materials";

    // Oyuncunun sol tarafında, parkurun kullanmadığı boş alan.
    private static readonly Vector3 RootPosition = new Vector3(-30f, 0f, 0f);

    // Havalandırma iç ölçüleri. Eğilmiş hull 36u (0.69 m boy, 0.61 m çap),
    // ayakta 72u (1.37 m). Yükseklik ikisinin arasında olmalı ki ayakta
    // geçilemesin, eğilerek geçilebilsin.
    //
    // Genişlik cömert tutuldu: kapsül çapı 0.61 m ama CharacterController'ın
    // skinWidth'i de eklenir, üstüne Source hareketinde momentum var — dar
    // delikte köşeye takılıp geçememek çok kolay.
    private const float VentInteriorHeight = 1.1f;
    private const float VentInteriorWidth = 1.4f;

    [MenuItem("Yakalamaca/Test Kapısı ve Havalandırma Ekle")]
    private static void BuildInteractionArea()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            bool rebuild = EditorUtility.DisplayDialog("Zaten var",
                "Mevcut test alanı silinip yeniden kurulsun mu?", "Yeniden kur", "Vazgeç");
            if (!rebuild)
                return;

            Undo.DestroyObjectImmediate(existing);
        }

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Test Etkilesim Kur");
        root.transform.position = RootPosition;

        Material wallMaterial = GetOrCreateMaterial("Etkilesim_Duvar", new Color(0.5f, 0.5f, 0.55f));
        Material doorMaterial = GetOrCreateMaterial("Etkilesim_Kapi", new Color(0.3f, 0.55f, 0.75f));
        Material buttonMaterial = GetOrCreateMaterial("Etkilesim_Dugme", new Color(0.9f, 0.35f, 0.25f));
        Material ventMaterial = GetOrCreateMaterial("Etkilesim_Havalandirma", new Color(0.55f, 0.5f, 0.35f));

        SlidingDoor door = BuildDoorway(root.transform, wallMaterial, doorMaterial);
        BuildButton(root.transform, buttonMaterial, door);
        BuildVent(root.transform, ventMaterial);

        // Kapı Harita, düğme Etkilesim: kuralı LayerSetup biliyor.
        LayerSetup.AssignHierarchy(root);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log(
            $"Test alanı kuruldu ({RootPosition.x}, {RootPosition.z}) — oyuncunun solunda.\n" +
            "MAVİ kapı: kırmızı düğmeye bakıp E'ye bas, kapı yana kayarak açılır. Kapının kendisine de E basabilirsin.\n" +
            $"SARI havalandırma: iç yüksekliği {VentInteriorHeight} m. Ayakta giremezsin, Ctrl ile eğilip geçeceksin. " +
            "Tünelin içindeyken Ctrl'ü bırak — tavan alçak olduğu için ayağa kalkmadığını göreceksin.\n" +
            "Kovalamaca için: kapının autoCloseDelay alanını 3 yaparsan arkandan kendi kendine kapanır.");
    }

    /// <summary>Ortasında kapı boşluğu olan duvar + kayan panel.</summary>
    private static SlidingDoor BuildDoorway(Transform parent, Material wallMaterial, Material doorMaterial)
    {
        Transform group = CreateGroup("Kapi", parent);

        const float wallHalfWidth = 4f;
        const float wallHeight = 3f;
        const float wallThickness = 0.4f;
        const float doorWidth = 1.2f;
        const float doorHeight = 2.1f;

        float doorHalf = doorWidth / 2f;
        float sideWidth = wallHalfWidth - doorHalf;

        CreateBox("Duvar_Sol", group,
            new Vector3(-(doorHalf + sideWidth / 2f), wallHeight / 2f, 0f),
            new Vector3(sideWidth, wallHeight, wallThickness), wallMaterial);

        CreateBox("Duvar_Sag", group,
            new Vector3(doorHalf + sideWidth / 2f, wallHeight / 2f, 0f),
            new Vector3(sideWidth, wallHeight, wallThickness), wallMaterial);

        // Kapı boşluğunun üstündeki lento
        CreateBox("Lento", group,
            new Vector3(0f, (doorHeight + wallHeight) / 2f, 0f),
            new Vector3(doorWidth, wallHeight - doorHeight, wallThickness), wallMaterial);

        // Panel duvarın önünde durur, açılınca sağa kayıp duvarın arkasına gider.
        GameObject panel = CreateBox("KapiPaneli", group,
            new Vector3(0f, doorHeight / 2f, -(wallThickness / 2f + 0.08f)),
            new Vector3(doorWidth, doorHeight, 0.15f), doorMaterial);

        SlidingDoor door = panel.AddComponent<SlidingDoor>();
        SerializedObject serialized = new SerializedObject(door);
        // Labirentteki kapılarla aynı davranış: panel aşağı gömülerek açılır.
        serialized.FindProperty("slideDirection").vector3Value = Vector3.down;
        serialized.FindProperty("slideDistance").floatValue = doorHeight + 0.05f;
        serialized.FindProperty("allowDirectUse").boolValue = false; // sadece düğmeyle
        serialized.ApplyModifiedProperties();

        return door;
    }

    /// <summary>Duvara monte düğme; kapıya bağlanır.</summary>
    private static void BuildButton(Transform parent, Material material, SlidingDoor door)
    {
        Transform group = CreateGroup("Dugme", parent);

        // Göz hizası 1.22 m; düğmeyi biraz altına koyunca doğal duruyor.
        GameObject buttonBox = CreateBox("DugmeKapagi", group,
            new Vector3(1.2f, 1.05f, -0.28f),
            new Vector3(0.3f, 0.3f, 0.1f), material);

        UseButton button = buttonBox.AddComponent<UseButton>();

        SerializedObject serialized = new SerializedObject(button);
        serialized.FindProperty("prompt").stringValue = "Kapıyı çalıştır";

        SerializedProperty targets = serialized.FindProperty("targets");
        targets.arraySize = 1;
        targets.GetArrayElementAtIndex(0).objectReferenceValue = door;

        // Düğme kendi kendinin görsel parçası: basınca duvara doğru (+Z) girer.
        serialized.FindProperty("pressVisual").objectReferenceValue = buttonBox.transform;
        serialized.FindProperty("pressDirection").vector3Value = Vector3.forward;
        serialized.ApplyModifiedProperties();
    }

    /// <summary>Eğilerek geçilen tünel: alt ortasında delik olan kalın duvar.</summary>
    private static void BuildVent(Transform parent, Material material)
    {
        Transform group = CreateGroup("Havalandirma", parent);
        group.localPosition = new Vector3(7f, 0f, 0f);

        const float blockHalfWidth = 2f;
        const float blockHeight = 3f;
        const float tunnelDepth = 2.5f;

        float holeHalf = VentInteriorWidth / 2f;
        float sideWidth = blockHalfWidth - holeHalf;

        CreateBox("Blok_Sol", group,
            new Vector3(-(holeHalf + sideWidth / 2f), blockHeight / 2f, 0f),
            new Vector3(sideWidth, blockHeight, tunnelDepth), material);

        CreateBox("Blok_Sag", group,
            new Vector3(holeHalf + sideWidth / 2f, blockHeight / 2f, 0f),
            new Vector3(sideWidth, blockHeight, tunnelDepth), material);

        // Deliğin üstündeki tavan — asıl eğilmeye zorlayan parça.
        CreateBox("Tavan", group,
            new Vector3(0f, (VentInteriorHeight + blockHeight) / 2f, 0f),
            new Vector3(VentInteriorWidth, blockHeight - VentInteriorHeight, tunnelDepth), material);
    }

    private static Transform CreateGroup(string name, Transform parent)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    private static GameObject CreateBox(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = scale;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder(MaterialRoot))
            AssetDatabase.CreateFolder("Assets", "_Art");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder(MaterialRoot, "Materials");

        Material material = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
