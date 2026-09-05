using UnityEngine;

/// <summary>
/// Sesli sohbetin sıkıştırması: **G.711 µ-law**, 8 kHz, 20 ms çerçeveler.
///
/// ### Neden kendi kodumuz, neden Opus değil
///
/// Opus daha iyi sıkıştırır (24 kbit/s'e iner) ama projeye bir paket daha
/// sokmak demek — bölüm 0'ın "bağımlılık eklemeden önce iki kez düşün"
/// kuralı. µ-law otuz satır, sıfır bağımlılık ve **kayıpsız denecek kadar
/// ucuz**: her örnek 16 bitten 8 bite iniyor, yani yarı yarıya.
///
/// Kalan yük konuşurken **64 kbit/s**. Ses algılama sayesinde yalnızca
/// konuşan gönderiyor, yani pratikte aynı anda bir-iki kişi. EOS relay'i bunu
/// taşıyor. Dar geldiği gün Opus eklenir ve değişecek tek yer bu dosya —
/// çağıranlar çerçevenin nasıl sıkıştığını bilmiyor.
///
/// ### Neden 8 kHz
///
/// Telefon kalitesi: konuşma tamamen anlaşılır, tiz kaybı korku oyununda
/// telsiz hissi bile veriyor. Yönü belirleyen şey içerik değil Unity'nin 3B
/// panlaması, o yüzden bant genişliği yön ipucunu bozmuyor (bölüm 12).
///
/// ### µ-law nedir
///
/// Logaritmik ölçekleme: sessiz kısımlara çok, gürültülü kısımlara az bit
/// ayırıyor. Kulak da öyle çalışıyor — 8 bit µ-law, 12 bit doğrusal PCM
/// kadar iyi duyuluyor. Telefon şebekesinin standardı (G.711) ve tablosu
/// kırk yıldır değişmedi.
/// </summary>
public static class VoiceCodec
{
    /// <summary>Ağda taşınan örnekleme hızı.</summary>
    public const int SampleRate = 8000;

    /// <summary>Bir çerçevedeki örnek sayısı — 20 ms.</summary>
    public const int FrameSamples = 160;

    // G.711 sabitleri. İsimleri standarttan; anlamları:
    //   Bias  — logaritmanın sıfırda patlamaması için eklenen kaydırma
    //   Clip  — kırpma sınırı; üstü zaten en yüksek segmente düşüyor
    private const int Bias = 0x84;
    private const int Clip = 32635;

    /// <summary>Bir çerçeveyi sıkıştırır. Kaynak -1..1 aralığında float.</summary>
    public static void Encode(float[] source, int sourceOffset, byte[] destination, int count)
    {
        for (int i = 0; i < count; i++)
            destination[i] = EncodeSample(source[sourceOffset + i]);
    }

    /// <summary>Bir çerçeveyi açar.</summary>
    public static void Decode(byte[] source, int count, float[] destination, int destinationOffset)
    {
        for (int i = 0; i < count; i++)
            destination[destinationOffset + i] = DecodeSample(source[i]);
    }

    public static byte EncodeSample(float value)
    {
        int sample = Mathf.Clamp(Mathf.RoundToInt(value * 32767f), -32768, 32767);

        // İşaret ayrı taşınıyor; büyüklük hep pozitif işleniyor.
        int sign = (sample >> 8) & 0x80;
        if (sign != 0)
            sample = -sample;

        if (sample > Clip)
            sample = Clip;

        sample += Bias;

        // Üs = en yüksek kurulu bitin yeri. Standart bunu 256'lık bir tabloyla
        // yapıyor; döngü aynı sonucu veriyor ve tabloyu taşımaya değmiyor.
        int exponent = 7;
        for (int mask = 0x4000; (sample & mask) == 0 && exponent > 0; exponent--, mask >>= 1)
        {
        }

        int mantissa = (sample >> (exponent + 3)) & 0x0F;

        // Sonuç ters çevriliyor: hatlarda uzun sessizlik 0xFF olsun diye
        // (standardın kendi tercihi, uyumlu kalmak için korunuyor).
        return (byte)~(sign | (exponent << 4) | mantissa);
    }

    public static float DecodeSample(byte value)
    {
        int folded = ~value;

        int sign = folded & 0x80;
        int exponent = (folded >> 4) & 0x07;
        int mantissa = folded & 0x0F;

        int sample = (((mantissa << 3) + Bias) << exponent) - Bias;

        return (sign != 0 ? -sample : sample) / 32768f;
    }

    /// <summary>
    /// Çerçevenin ortalama gücü — ses var/yok kararı buna bakıyor.
    ///
    /// Tepe değeri değil RMS: tek bir tıkırtı (klavye, masaya vurma) tepeyi
    /// fırlatıyor ama ortalamayı zor kaldırıyor, yani RMS yanlış tetiklemeye
    /// çok daha dayanıklı.
    /// </summary>
    public static float Rms(float[] samples, int offset, int count)
    {
        if (count <= 0)
            return 0f;

        double total = 0d;

        for (int i = 0; i < count; i++)
        {
            float sample = samples[offset + i];
            total += sample * sample;
        }

        return Mathf.Sqrt((float)(total / count));
    }
}
