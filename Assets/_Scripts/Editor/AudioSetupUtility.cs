using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Kurulum script'lerinin `_Audio` klasöründeki klipleri isme göre bulup
/// bağlamasına yardım eder. Kullanıcının on tane dosyayı elle sürüklemesi
/// gerekmesin diye.
/// </summary>
public static class AudioSetupUtility
{
    private const string AudioFolder = "Assets/_Audio";

    /// <summary>Adı verilen ön ekle başlayan tüm klipleri diziye doldurur.</summary>
    public static void AssignClips(SerializedProperty arrayProperty, string namePrefix)
    {
        if (arrayProperty == null || !arrayProperty.isArray)
            return;

        List<AudioClip> clips = FindClips(namePrefix);

        arrayProperty.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++)
            arrayProperty.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];

        if (clips.Count == 0)
            Debug.LogWarning($"'{namePrefix}' ile başlayan ses bulunamadı. " +
                "Önce Yakalamaca > Yer Tutucu Sesleri Üret çalıştır.");
    }

    /// <summary>Tam adı verilen tek klibi bağlar.</summary>
    public static void AssignClip(SerializedProperty property, string clipName)
    {
        if (property == null)
            return;

        // Uzantı sabit değil: yer tutucular .wav üretilmişti, gerçek sesler
        // .mp3 geldi. Adı tutan ilk dosyayı alıyoruz.
        AudioClip clip = null;

        foreach (string extension in new[] { ".wav", ".mp3", ".ogg", ".aiff" })
        {
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{clipName}{extension}");
            if (clip != null)
                break;
        }

        property.objectReferenceValue = clip;

        if (clip == null)
            Debug.LogWarning($"'{clipName}.wav' bulunamadı. " +
                "Önce Yakalamaca > Yer Tutucu Sesleri Üret çalıştır.");
    }

    private static List<AudioClip> FindClips(string namePrefix)
    {
        List<AudioClip> clips = new List<AudioClip>();

        if (!AssetDatabase.IsValidFolder(AudioFolder))
            return clips;

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(namePrefix))
                continue;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
                clips.Add(clip);
        }

        // Varyantlar isim sırasına göre dursun; rastgele seçim zaten üstte.
        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return clips;
    }
}
