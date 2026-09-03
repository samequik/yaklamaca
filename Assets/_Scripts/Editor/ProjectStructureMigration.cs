using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CLAUDE.md'deki hedef klasör yapısına tek seferlik geçiş.
///
/// Neden script? Dosyaları Explorer'dan taşımak .meta eşleşmelerini bozar ve
/// sahnedeki tüm referanslar kopar. AssetDatabase.MoveAsset ise GUID'leri
/// koruyup referansları kendisi günceller — Unity açıkken de güvenli.
///
/// Bu iş bittikten sonra bu dosya silinebilir.
/// </summary>
public static class ProjectStructureMigration
{
    [MenuItem("Yakalamaca/Klasör Yapısını Düzenle")]
    private static void Migrate()
    {
        bool proceed = EditorUtility.DisplayDialog("Klasör yapısı",
            "Assets içeriği CLAUDE.md'deki hedef yapıya taşınacak:\n\n" +
            "Materials  →  _Art/Materials\n" +
            "Scenes     →  _Scenes\n" +
            "Scripts    →  _Scripts\n" +
            "PlayerController.cs  →  _Scripts/Player/\n\n" +
            "GUID'ler korunur, sahne referansları kopmaz.",
            "Taşı", "Vazgeç");

        if (!proceed)
            return;

        // Boş hedef klasörler: proje büyüdükçe doğru yere koyulsun diye şimdiden açılıyor.
        EnsureFolder("Assets/_Art");
        EnsureFolder("Assets/_Audio");
        EnsureFolder("Assets/_Prefabs");
        EnsureFolder("Assets/_ScriptableObjects");

        MoveAsset("Assets/Materials", "Assets/_Art/Materials");
        MoveAsset("Assets/Scenes", "Assets/_Scenes");

        // Scripts en son taşınıyor: bu dosya da onun içinde, taşınınca Unity
        // yeniden derleyecek. Diğer işler o noktada bitmiş olsun.
        MoveAsset("Assets/Scripts", "Assets/_Scripts");

        EnsureFolder("Assets/_Scripts/Player");
        MoveAsset("Assets/_Scripts/PlayerController.cs", "Assets/_Scripts/Player/PlayerController.cs");

        AssetDatabase.Refresh();
        Debug.Log("Klasör yapısı düzenlendi. Sahneyi kaydetmeyi unutma (Ctrl+S).");
    }

    /// <summary>Ara klasörleri de oluşturur; AssetDatabase.CreateFolder tek seviye çalışır.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string folderName = Path.GetFileName(path);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void MoveAsset(string from, string to)
    {
        bool exists = AssetDatabase.IsValidFolder(from) ||
            AssetDatabase.LoadAssetAtPath<Object>(from) != null;

        if (!exists)
        {
            Debug.Log($"Atlandı (bulunamadı): {from}");
            return;
        }

        if (AssetDatabase.IsValidFolder(to) || AssetDatabase.LoadAssetAtPath<Object>(to) != null)
        {
            Debug.Log($"Atlandı (hedef zaten var): {to}");
            return;
        }

        string error = AssetDatabase.MoveAsset(from, to);
        if (string.IsNullOrEmpty(error))
            Debug.Log($"Taşındı: {from}  →  {to}");
        else
            Debug.LogError($"Taşınamadı: {from} → {to}\n{error}");
    }
}
