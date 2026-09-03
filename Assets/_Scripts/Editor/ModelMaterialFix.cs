using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dışarıdan gelen karakter modellerinin iki sessiz materyal sorununu çözer.
/// `MonsterSetup` ve `RunnerSetup` kullanıyor.
///
/// ### 1. URP slotu Standard slotuna taşınmıyordu
///
/// URP için yazılmış materyallerde albedo `_BaseMap` slotunda duruyor; Built-in
/// Standard ise `_MainTex` okuyor. Shader'ı Standard'a çevirmek dokuyu
/// taşımıyor — sonuç **düz gri karakter**. Normal harita korunuyordu çünkü
/// `_BumpMap` iki tarafta da aynı ada sahip; kafa karıştıran da buydu, model
/// aydınlatmaya tepki veriyor ama renksiz duruyordu.
///
/// CLAUDE.md bölüm 14 uzun süre "Built-in slotları zaten dolu" diyordu; doğru
/// değildi, yalnızca ad çakışan slotlar doluydu.
///
/// **Okuma `SerializedObject` üzerinden.** `material.GetTexture("_BaseMap")`
/// işe yaramıyor: `HasProperty` mevcut **shader'ın** özelliklerine bakıyor ve
/// Standard'da `_BaseMap` yok. Doku `.mat` dosyasında hâlâ duruyor (Unity
/// sahipsiz özellikleri silmiyor), o yüzden serileştirilmiş veriden okunuyor.
///
/// ### 2. Modelin materyal yuvaları dış `.mat` dosyalarına bağlı olmayabiliyor
///
/// Model kendi ürettiği materyali kullanıyor, klasördeki hazır `.mat` dosyası
/// kenarda duruyor ve **hiçbir yerde hata yazmıyor** — sonuç yine gri karakter.
/// Banana Man'de olan buydu: `Body.mat` doğru dokuya (muz sarısı albedo)
/// sahipti ama modele bağlı değildi.
///
/// Çözüm Unity'nin eşleme kurallarına güvenmek yerine bağlantıyı **açıkça**
/// kurmak (`AddRemap`), yani sonuç projeden projeye değişmiyor.
/// </summary>
public static class ModelMaterialFix
{
    /// <summary>
    /// URP slotlarını Built-in karşılıklarına kopyalar. Yalnızca hedef slot
    /// BOŞSA yazıyor — elle ayarlanmış bir materyali ezmiyor.
    /// </summary>
    /// <returns>Düzeltilen materyal sayısı.</returns>
    public static int FillBuiltinSlots(IEnumerable<Material> materials, string undoLabel)
    {
        int count = 0;

        foreach (Material material in materials)
        {
            if (material == null || material.shader == null)
                continue;

            bool changed = false;

            if (CopyTexture(material, "_BaseMap", "_MainTex", undoLabel))
                changed = true;

            if (CopyColor(material, "_BaseColor", "_Color", undoLabel))
                changed = true;

            if (changed)
            {
                EditorUtility.SetDirty(material);
                count++;
            }
        }

        return count;
    }

    private static bool CopyTexture(Material material, string from, string to, string undoLabel)
    {
        if (!material.HasProperty(to) || material.GetTexture(to) != null)
            return false;

        Texture source = ReadSavedTexture(material, from);
        if (source == null)
            return false;

        Undo.RecordObject(material, undoLabel);
        material.SetTexture(to, source);
        return true;
    }

    private static bool CopyColor(Material material, string from, string to, string undoLabel)
    {
        if (!material.HasProperty(to))
            return false;

        // Rengin "boş"u yok; beyaz kabul ediyoruz. Standard'ın varsayılanı da o.
        if (material.GetColor(to) != Color.white)
            return false;

        if (!TryReadSavedColor(material, from, out Color source) || source == Color.white)
            return false;

        Undo.RecordObject(material, undoLabel);
        material.SetColor(to, source);
        return true;
    }

    /// <summary>
    /// Shader artık tanımasa bile `.mat` içinde duran dokuyu okur.
    /// `material.GetTexture` burada işe yaramıyor — bkz. sınıf açıklaması.
    /// </summary>
    private static Texture ReadSavedTexture(Material material, string property)
    {
        SerializedProperty entries =
            new SerializedObject(material).FindProperty("m_SavedProperties.m_TexEnvs");

        if (entries == null)
            return null;

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);

            if (entry.FindPropertyRelative("first").stringValue != property)
                continue;

            return entry.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
        }

        return null;
    }

    private static bool TryReadSavedColor(Material material, string property, out Color color)
    {
        color = Color.white;

        SerializedProperty entries =
            new SerializedObject(material).FindProperty("m_SavedProperties.m_Colors");

        if (entries == null)
            return false;

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);

            if (entry.FindPropertyRelative("first").stringValue != property)
                continue;

            color = entry.FindPropertyRelative("second").colorValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Modelin materyal yuvalarını klasördeki `.mat` dosyalarına AÇIKÇA bağlar.
    ///
    /// Unity'nin kendi eşleme kuralına güvenmek yerine `AddRemap` kullanıyor:
    /// eşleme sessizce başarısız olabiliyor ve sonuç gri bir karakter oluyor.
    ///
    /// **`slots` tablosu şart olabiliyor.** Otomatik eşleşme yuva adıyla dosya
    /// adının aynı olmasını istiyor; canavarda tutmuyor (yuva
    /// `Unity_KillerDoll_Body`, dosya `KillerDollBodyPaintedWood`). Tablo
    /// verildiğinde bağlantı doğrudan ondan kuruluyor — modelin alt varlık
    /// taraması boş dönse bile çalışıyor, çünkü `AddRemap` yalnızca yuva adını
    /// istiyor.
    ///
    /// Tabloda olmayan yuvalar için ada göre eşleşmeye düşülüyor; Banana Man
    /// öyle çözüldü (`Body` → `Body.mat`).
    /// </summary>
    /// <returns>Bağlanan yuva sayısı.</returns>
    public static int RemapExternalMaterials(string modelPath,
        IDictionary<string, string> slots = null)
    {
        if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
            return 0;

        string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
        Dictionary<AssetImporter.SourceAssetIdentifier, Object> current =
            importer.GetExternalObjectMap();

        int count = 0;
        List<string> unmatched = new List<string>();

        if (slots != null)
        {
            foreach (KeyValuePair<string, string> pair in slots)
            {
                Material external = FindMaterialAsset(pair.Value, folder);

                if (external == null)
                {
                    Debug.LogWarning($"{pair.Value}.mat bulunamadı; " +
                        $"'{pair.Key}' yuvası bağlanmadı.");
                    continue;
                }

                if (TryRemap(importer, current, pair.Key, external))
                    count++;
            }
        }

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (!(asset is Material embedded) || string.IsNullOrEmpty(embedded.name))
                continue;

            if (slots != null && slots.ContainsKey(embedded.name))
                continue;

            Material external = FindMaterialAsset(embedded.name, folder);

            if (external == null)
            {
                // Eşleşme ADA göre: yuva adıyla aynı adda bir .mat gerekiyor.
                // Tutmadığında sessizce gri kalmasın diye adı yazıyoruz.
                unmatched.Add(embedded.name);
                continue;
            }

            // Zaten bağlı olan materyalin kendisi.
            if (external == embedded)
                continue;

            if (TryRemap(importer, current, embedded.name, external))
                count++;
        }

        if (unmatched.Count > 0)
        {
            Debug.LogWarning($"{Path.GetFileName(modelPath)}: şu materyal yuvaları " +
                $"için aynı adda .mat bulunamadı → {string.Join(", ", unmatched)}. " +
                "Model bu yuvalarda kendi (gri) materyalini kullanmaya devam edecek.");
        }

        if (count > 0)
            importer.SaveAndReimport();

        return count;
    }

    private static bool TryRemap(ModelImporter importer,
        Dictionary<AssetImporter.SourceAssetIdentifier, Object> current,
        string slot, Material external)
    {
        AssetImporter.SourceAssetIdentifier id =
            new AssetImporter.SourceAssetIdentifier(typeof(Material), slot);

        if (current.TryGetValue(id, out Object bound) && bound == external)
            return false;

        importer.AddRemap(id, external);
        return true;
    }

    /// <summary>
    /// Modelin klasöründen başlayıp iki üst klasöre kadar aynı adda `.mat` arar.
    /// Paketler materyalleri genelde modelin yanındaki `Materials` klasöründe
    /// tutuyor, bazen bir üst seviyede.
    /// </summary>
    private static Material FindMaterialAsset(string name, string folder)
    {
        string search = folder;

        for (int depth = 0; depth < 3 && !string.IsNullOrEmpty(search); depth++)
        {
            if (AssetDatabase.IsValidFolder(search))
            {
                foreach (string guid in AssetDatabase.FindAssets($"t:Material {name}", new[] { search }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    if (Path.GetFileNameWithoutExtension(path) != name)
                        continue;

                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material != null)
                        return material;
                }
            }

            search = Path.GetDirectoryName(search)?.Replace('\\', '/');
        }

        return null;
    }
}
