using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Sahnedeki "The referenced script (Unknown) on this Behaviour is missing!"
/// uyarılarını temizler.
///
/// Bir script silindiğinde sahnedeki objede o bileşenin boş kaydı kalır; Unity
/// bunu her yüklemede uyarı olarak bildirir ama kendi kendine silmez. Elle
/// silmek için her objeyi tek tek gezmek gerekir, bu komut hepsini tarar.
///
/// Menü: Yakalamaca > Eksik Script'leri Temizle
/// </summary>
public static class MissingScriptCleaner
{
    [MenuItem("Yakalamaca/Eksik Script'leri Temizle")]
    private static void Clean()
    {
        GameObject[] all = Object.FindObjectsOfType<GameObject>(true);

        int removedTotal = 0;
        int affectedObjects = 0;

        foreach (GameObject target in all)
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(target);
            if (missing == 0)
                continue;

            Undo.RegisterCompleteObjectUndo(target, "Eksik Script'leri Temizle");
            removedTotal += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(target);
            affectedObjects++;

            Debug.Log($"{target.name}: {missing} eksik bileşen kaydı silindi.", target);
        }

        if (removedTotal == 0)
        {
            Debug.Log("Eksik script bulunamadı, sahne temiz.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Toplam {removedTotal} eksik bileşen kaydı, {affectedObjects} objeden silindi. Sahneyi kaydet (Ctrl+S).");
    }
}
