using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Diskte silmek açık Editor sahnesinden tekrar kaydedilmesini engellemiyor.
// Eski prototip oyuncusunu edit modunda, açık sahnenin kendisinden kaldır.
[InitializeOnLoad]
public static class LegacyPlayerCleanup
{
    static LegacyPlayerCleanup()
    {
        EditorApplication.update += TryCleanup;
    }

    private static void TryCleanup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.path != "Assets/_Scenes/SampleScene.unity")
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        bool hasNetworkPlayer = false;
        foreach (GameObject root in roots)
        {
            NetworkManager manager = root.GetComponent<NetworkManager>();
            if (manager != null && manager.playerPrefab != null
                && manager.playerPrefab.GetComponent<NetworkPlayerSetup>() != null)
                hasNetworkPlayer = true;
        }

        if (!hasNetworkPlayer)
            return;

        EditorApplication.update -= TryCleanup;
        foreach (GameObject root in roots)
        {
            if (root.name != "Player" || root.GetComponent<PlayerController>() == null
                || root.GetComponent<NetworkIdentity>() != null
                || root.GetComponent<NetworkPlayerSetup>() != null)
                continue;

            Undo.DestroyObjectImmediate(root);
            EditorSceneManager.MarkSceneDirty(scene);
            if (EditorSceneManager.SaveScene(scene))
                Debug.Log("Eski sahne Player'ı kaldırıldı ve sahne kaydedildi. NetworkPlayer korunuyor.");
            else
                Debug.LogWarning("Eski Player kaldırıldı; sahne kaydedilemedi, sahneyi kaydedin.");
        }
    }
}
