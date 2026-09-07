using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Yalnızca yeni kabinleri ekler; Harita, terminaller ve çıkışlar yeniden üretilmez.</summary>
public static class RevivalSetup
{
    private const string Request = "Temp/RevivalWork/install.request";
    private const string Result = "Temp/RevivalWork/install.result";
    [InitializeOnLoadMethod]
    private static void QueueRequestedInstall() => EditorApplication.delayCall += ProcessRequest;
    private static void ProcessRequest()
    {
        if (!File.Exists(Request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += ProcessRequest; return; }
        File.Delete(Request);
        try { Run(); File.WriteAllText(Result, "OK: Ceset prefabı ve iki kabin kuruldu. Sahne kaydedildi."); }
        catch (Exception e) { File.WriteAllText(Result, "FAILED: " + e); Debug.LogException(e); }
    }
    [MenuItem("Yakalamaca/Diriltme Sistemini Kur", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;
    [MenuItem("Yakalamaca/Diriltme Sistemini Kur")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Önce Play modundan çık.");
        GameObject map = GameObject.Find("Harita");
        if (map == null) throw new InvalidOperationException("Harita sahnesi açık değil.");
        BuildBodyTemplate();
        Physics.SyncTransforms();
        var existing = Object.FindObjectsOfType<RevivalStation>(true);
        if (existing.Length == 0)
        {
            var candidates = FindCandidates(map);
            if (candidates.Count < 2) throw new InvalidOperationException("Kabinler için iki boş alan bulunamadı.");
            Vector3 first = candidates[0], second = candidates[1]; float best = 0;
            foreach (Vector3 a in candidates)
                foreach (Vector3 b in candidates)
                    if ((a - b).sqrMagnitude > best) { best = (a - b).sqrMagnitude; first = a; second = b; }
            if (best < 25 * 25) throw new InvalidOperationException("Kabinler yeterince uzak yerleştirilemiyor.");
            var root = new GameObject("DiriltmeIstasyonlari");
            Undo.RegisterCreatedObjectUndo(root, "Diriltme kabinlerini ekle");
            BuildStation(root.transform, first, "Diriltme_A", 1);
            BuildStation(root.transform, second, "Diriltme_B", 2);
            Debug.Log($"İki diriltme kabini eklendi: {first}, {second}; uzaklık {Mathf.Sqrt(best):0.0} m.");
        }
        else if (existing.Length != 2) throw new InvalidOperationException("Sahnede iki dışında sayıda kabin var; elle kontrol et.");
        var group = GameObject.Find("DiriltmeIstasyonlari");
        if (group != null) Selection.activeGameObject = group;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }
    private static void BuildBodyTemplate()
    {
        const string playerPath = "Assets/_Prefabs/NetworkPlayer.prefab";
        const string corpsePath = "Assets/_Prefabs/Corpse.prefab";
        GameObject player = PrefabUtility.LoadPrefabContents(playerPath);
        GameObject template = null;
        GameObject corpse = null;
        try
        {
            var bodyVisual = player.GetComponent<PlayerBodyVisual>();
            var serialized = new SerializedObject(bodyVisual);
            var source = serialized.FindProperty("runnerRoot").objectReferenceValue as GameObject;
            if (source == null) throw new InvalidOperationException("Kaçan modeli bulunamadı.");
            template = Object.Instantiate(source);
            template.name = "CorpseBody"; template.SetActive(true);
            template.transform.position = Vector3.zero; template.transform.rotation = Quaternion.identity;
            foreach (Transform t in template.GetComponentsInChildren<Transform>(true))
                if (t.localScale == Vector3.zero) t.localScale = Vector3.one;
            var animator = template.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null) throw new InvalidOperationException("İnsan avatarı bulunamadı.");
            var paths = new string[(int)HumanBodyBones.LastBone];
            var transforms = template.GetComponentsInChildren<Transform>(true);
            HumanBone[] human = animator.avatar.humanDescription.human;
            for (int i = 0; i < paths.Length; i++)
            {
                string role = HumanTrait.BoneName[i];
                var mapping = human.FirstOrDefault(h => h.humanName == role || h.humanName == ((HumanBodyBones)i).ToString());
                Transform bone = transforms.FirstOrDefault(t => t.name == mapping.boneName);
                if (bone == null)
                {
                    try { bone = animator.GetBoneTransform((HumanBodyBones)i); } catch { }
                }
                if (bone != null) paths[i] = AnimationUtility.CalculateTransformPath(bone, template.transform);
            }
            if (string.IsNullOrEmpty(paths[(int)HumanBodyBones.Hips])) throw new InvalidOperationException("Kalça kemiği eşlenemedi.");
            foreach (Animator a in template.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            foreach (Renderer r in template.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            GameObject savedBody = PrefabUtility.SaveAsPrefabAsset(template, "Assets/_Prefabs/CorpseBody.prefab");
            corpse = PrefabUtility.LoadPrefabContents(corpsePath);
            var data = new SerializedObject(corpse.GetComponent<Corpse>());
            data.FindProperty("bodyPrefab").objectReferenceValue = savedBody;
            var array = data.FindProperty("bonePaths"); array.arraySize = paths.Length;
            for (int i = 0; i < paths.Length; i++) array.GetArrayElementAtIndex(i).stringValue = paths[i] ?? "";
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(corpse, corpsePath);
        }
        finally
        {
            if (corpse != null) PrefabUtility.UnloadPrefabContents(corpse);
            if (template != null) Object.DestroyImmediate(template);
            PrefabUtility.UnloadPrefabContents(player);
        }
    }
    private static List<Vector3> FindCandidates(GameObject map)
    {
        Collider floor = map.GetComponentsInChildren<Collider>().Where(c => c.name == "Zemin")
            .OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).FirstOrDefault();
        if (floor == null) throw new InvalidOperationException("Haritanın zemini bulunamadı.");
        Bounds bounds = floor.bounds;
        var result = new List<Vector3>();
        // Duvarlar 3.2 m ızgarada. Kabin + önündeki kullanım alanının tamamı boş olmalı.
        for (float x = bounds.min.x + 1.6f; x < bounds.max.x - 1.5f; x += 3.2f)
            for (float z = bounds.min.z + 1.6f; z < bounds.max.z - 1.5f; z += 3.2f)
            {
                Vector3 p = new Vector3(x, bounds.max.y, z);
                Collider[] hits = Physics.OverlapBox(p + Vector3.up * 1.15f, new Vector3(1.25f, 1.08f, 1.25f),
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                bool occupied = hits.Any(c => c != floor && c.gameObject.activeInHierarchy && !(c is CharacterController));
                if (!occupied) result.Add(p);
            }
        return result;
    }
    private static Material Material(string name, Color color, bool emissive = false)
    {
        string path = "Assets/_Prefabs/" + name + ".mat";
        Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result != null) return result;
        result = new Material(Shader.Find("Standard")); result.color = color;
        result.SetFloat("_Glossiness", 0.45f);
        if (emissive) { result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor", color * 1.5f); }
        AssetDatabase.CreateAsset(result, path); return result;
    }
    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool solid = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        go.layer = LayerMask.NameToLayer("Etkilesim");
        if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
    private static void BuildStation(Transform parent, Vector3 position, string name, int number)
    {
        var root = new GameObject(name); root.transform.SetParent(parent); root.transform.position = position;
        // Açık ön yüz: giriş/çıkışı engelleyen kapı veya eşik yok.
        var frame = Material("Diriltme_Metal", new Color(0.12f, 0.19f, 0.22f));
        var panel = Material("Diriltme_Panel", new Color(0.23f, 0.37f, 0.39f));
        var glow = Material("Diriltme_Isik", new Color(0.1f, 0.8f, 0.65f), true);
        Box(root.transform, "Taban", new Vector3(0, 0.025f, 0), new Vector3(1.45f, 0.05f, 1.45f), frame);
        Box(root.transform, "Tavan", new Vector3(0, 2.1f, 0), new Vector3(1.5f, 0.12f, 1.5f), frame);
        Box(root.transform, "ArkaPanel", new Vector3(0, 1.05f, -0.7f), new Vector3(1.4f, 2.1f, 0.08f), panel);
        foreach (float x in new[] {-0.7f, 0.7f})
        {
            Box(root.transform, "OnDirek", new Vector3(x, 1.05f, 0.68f), new Vector3(0.08f, 2.1f, 0.08f), frame);
            Box(root.transform, "YanPanel", new Vector3(x, 1.05f, -0.15f), new Vector3(0.06f, 2.05f, 1.05f), panel);
            Box(root.transform, "IsikSeridi", new Vector3(x * 0.88f, 1.05f, -0.64f), new Vector3(0.025f, 1.8f, 0.02f), glow, false);
        }
        var terminal = Box(root.transform, "DiriltmeTerminali", new Vector3(0.95f, 1.0f, 0.45f), new Vector3(0.4f, 0.55f, 0.22f), frame);
        terminal.transform.localScale = Vector3.one;
        terminal.GetComponent<BoxCollider>().size = new Vector3(0.4f, 0.55f, 0.22f);
        // Gövde mesh'ini ayrı çocukta ölçülendir: NetworkIdentity kökü birim ölçekli.
        Object.DestroyImmediate(terminal.GetComponent<MeshRenderer>()); Object.DestroyImmediate(terminal.GetComponent<MeshFilter>());
        Box(terminal.transform, "Kasa", Vector3.zero, new Vector3(0.4f, 0.55f, 0.22f), frame, false);
        var screen = Box(terminal.transform, "Ekran", new Vector3(0, 0.04f, 0.12f), new Vector3(0.32f, 0.35f, 0.02f), glow, false);
        terminal.AddComponent<NetworkIdentity>();
        var station = terminal.AddComponent<RevivalStation>();
        var body = new GameObject("CesetYuvasi"); body.transform.SetParent(root.transform, false); body.transform.localPosition = new Vector3(0, 0.75f, 0);
        var spawn = new GameObject("DirilmeNoktasi"); spawn.transform.SetParent(root.transform, false); spawn.transform.localPosition = new Vector3(0, 0.76f, 0.1f);
        var settings = new SerializedObject(station);
        settings.FindProperty("bodyAnchor").objectReferenceValue = body.transform;
        settings.FindProperty("revivePoint").objectReferenceValue = spawn.transform;
        settings.FindProperty("indicator").objectReferenceValue = screen.GetComponent<Renderer>();
        settings.FindProperty("workingClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Audio/Terminal_Calisma.mp3");
        settings.FindProperty("warningClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Audio/Terminal_Uyari.mp3");
        settings.ApplyModifiedPropertiesWithoutUndo();
        var sign = new GameObject("Tabela"); sign.transform.SetParent(root.transform, false);
        sign.transform.localPosition = new Vector3(0, 1.85f, 0.77f);
        sign.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var text = sign.AddComponent<TextMeshPro>(); text.font = TMP_Settings.defaultFontAsset;
        text.text = "DİRİLTME " + number; text.fontSize = 2.2f; text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(1.4f, 0.25f); text.color = new Color(0.4f, 1, 0.85f);
    }
}
