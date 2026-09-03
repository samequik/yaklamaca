using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Yer tutucu ses efektlerini kodla üretip `.wav` olarak yazar.
///
/// Neden? Projede hiç ses dosyası yok ve indirmek hem zaman hem veri harcıyor.
/// Bu sesler gürültü + zarf + basit filtreden ibaret; bir ses tasarımcısının
/// işi değil ama mekaniği bugün test etmeye yetiyor. Gerçek dosyalar gelince
/// aynı isimlerle değiştirmen yeterli, bağlantıları bozulmaz.
///
/// Menü: Yakalamaca > Yer Tutucu Sesleri Üret
/// </summary>
public static class PlaceholderAudioGenerator
{
    private const string AudioFolder = "Assets/_Audio";
    private const int SampleRate = 44100;
    private const int StepVariantCount = 4;

    [MenuItem("Yakalamaca/Yer Tutucu Sesleri Üret")]
    private static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(AudioFolder))
            AssetDatabase.CreateFolder("Assets", "_Audio");

        // Aynı sesin birebir tekrarı yürüyüşü makineleştiriyor; birkaç varyant
        // üretip rastgele seçmek tek başına büyük fark yaratıyor.
        for (int i = 0; i < StepVariantCount; i++)
        {
            WriteWav($"Adim_Hafif_{i + 1}", GenerateFootstep(seed: 10 + i, heavy: false));
            WriteWav($"Adim_Agir_{i + 1}", GenerateFootstep(seed: 50 + i, heavy: true));
        }

        WriteWav("Ziplama", GenerateJump());
        WriteWav("Bicak_Savurma", GenerateSwing());
        WriteWav("Bicak_Isabet", GenerateHit());

        AssetDatabase.Refresh();

        Debug.Log(
            $"Yer tutucu sesler üretildi → {AudioFolder}\n" +
            $"{StepVariantCount} hafif adım (kaçan), {StepVariantCount} ağır adım (canavar), " +
            "bıçak savurma ve isabet.\n" +
            "Gerçek ses dosyaları bulunca aynı isimlerle değiştir, bağlantılar bozulmaz.");
    }

    // ---------- Sentez ----------

    /// <summary>
    /// Adım sesi: yumuşatılmış gürültü (ayakkabı sürtünmesi) + alçak sinüs
    /// vuruşu (topuk). Ağır sürüm daha pes ve daha uzun sönümlü.
    /// </summary>
    private static float[] GenerateFootstep(int seed, bool heavy)
    {
        System.Random random = new System.Random(seed);

        float duration = heavy ? 0.24f : 0.15f;
        float thumpFrequency = heavy ? 62f : 108f;
        float noiseDecay = heavy ? 20f : 36f;
        float thumpDecay = heavy ? 16f : 28f;
        float smoothing = heavy ? 0.10f : 0.24f;

        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count];
        float lowPass = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);

            // Tek kutuplu alçak geçiren filtre — ham gürültünün cızırtısını alır.
            lowPass += (noise - lowPass) * smoothing;

            float body = lowPass * Mathf.Exp(-t * noiseDecay);
            float thump = Mathf.Sin(2f * Mathf.PI * thumpFrequency * t) * Mathf.Exp(-t * thumpDecay);

            data[i] = body * 0.75f + thump * 0.55f;
        }

        return ApplyFadeOut(data);
    }

    /// <summary>
    /// Zıplama: frekansı düşen kısa bir ton + nefes gibi gürültü. Bhop yaparken
    /// sürekli çalacağı için kasten kısa ve yumuşak; sert bir ses tekrarlanınca
    /// rahatsız ediyor.
    /// </summary>
    private static float[] GenerateJump()
    {
        System.Random random = new System.Random(33);

        const float duration = 0.18f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count];

        float lowPass = 0f;
        float phase = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float progress = i / (float)count;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);

            lowPass += (noise - lowPass) * 0.28f;

            // Frekans düşüyor: "hup" hissi. Faz biriktirerek süpürüyoruz,
            // doğrudan sin(2πft) yazmak süpürmede yanlış sonuç verir.
            float frequency = Mathf.Lerp(210f, 95f, progress);
            phase += 2f * Mathf.PI * frequency / SampleRate;

            float envelope = Mathf.Exp(-t * 15f);
            data[i] = (Mathf.Sin(phase) * 0.45f + lowPass * 0.3f) * envelope;
        }

        return ApplyFadeOut(data);
    }

    /// <summary>
    /// Bıçak savurma: kesim frekansı süpürülen gürültü. Filtrenin açılması
    /// "vınn" hissini veriyor, sabit gürültü sadece hışırtı olurdu.
    /// </summary>
    private static float[] GenerateSwing()
    {
        System.Random random = new System.Random(7);

        const float duration = 0.3f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count];
        float lowPass = 0f;

        for (int i = 0; i < count; i++)
        {
            float progress = i / (float)count;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);

            // Kesim frekansı boyunca açılıyor: pes başlayıp tizleşiyor.
            float cutoff = Mathf.Lerp(0.04f, 0.45f, progress);
            lowPass += (noise - lowPass) * cutoff;

            // Zarf hızlı yükselip yavaş sönüyor.
            float envelope = Mathf.Sin(Mathf.PI * Mathf.Pow(progress, 0.65f));

            data[i] = lowPass * envelope * 0.85f;
        }

        return ApplyFadeOut(data);
    }

    /// <summary>İsabet: sert gürültü transienti + kısa pes gümbürtü.</summary>
    private static float[] GenerateHit()
    {
        System.Random random = new System.Random(21);

        const float duration = 0.35f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count];
        float lowPass = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);

            lowPass += (noise - lowPass) * 0.35f;

            float crack = lowPass * Mathf.Exp(-t * 55f);
            float thud = Mathf.Sin(2f * Mathf.PI * 85f * t) * Mathf.Exp(-t * 14f);

            data[i] = crack * 0.7f + thud * 0.6f;
        }

        return ApplyFadeOut(data);
    }

    /// <summary>Son birkaç milisaniyede sıfıra indirir; yoksa kesikte klik duyulur.</summary>
    private static float[] ApplyFadeOut(float[] data)
    {
        int fadeLength = Mathf.Min(data.Length, SampleRate / 200); // ~5 ms

        for (int i = 0; i < fadeLength; i++)
        {
            int index = data.Length - fadeLength + i;
            data[index] *= 1f - i / (float)fadeLength;
        }

        return data;
    }

    // ---------- WAV yazımı ----------

    /// <summary>16-bit mono PCM WAV. AudioClip.Create ile üretilen klipler
    /// asset olarak kaydedilemediği için dosyayı doğrudan yazıyoruz.</summary>
    private static void WriteWav(string name, float[] samples)
    {
        string path = $"{AudioFolder}/{name}.wav";

        using (FileStream stream = new FileStream(path, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int dataSize = samples.Length * 2;

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);              // fmt bloğu uzunluğu
            writer.Write((short)1);        // PCM
            writer.Write((short)1);        // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);  // byte/saniye
            writer.Write((short)2);        // blok hizası
            writer.Write((short)16);       // bit derinliği

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            for (int i = 0; i < samples.Length; i++)
                writer.Write((short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
        }
    }
}
