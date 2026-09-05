using UnityEngine;

/// <summary>
/// Mikrofonu okur, 8 kHz'e indirir, 20 ms'lik çerçeveler hâlinde
/// <see cref="VoiceChat"/>'e verir.
///
/// **Yalnızca yerel oyuncuda çalışıyor.** `VoiceChat` bileşeni sahibi
/// değilse bu bileşeni hiç açmıyor.
///
/// ### Cihaz hızında yakalanıp indiriliyor
///
/// `Microphone.Start`'a 8000 vermek çoğu cihazda tutmuyor: donanım 44100 ya da
/// 48000 dayatıyor ve Unity sessizce en yakınına yuvarlıyor. Bu yüzden cihazın
/// kendi hızında yakalayıp **ortalama alarak** indiriyoruz. Ortalama aynı
/// zamanda kaba bir alçak geçiren filtre: doğrudan örnek atlamak (decimation)
/// takma frekans üretir ve ses metalik çıkar.
///
/// ### Halka tampondan okuma
///
/// `Microphone` klibi döngüsel yazıyor; `GetPosition` yazma kafasını veriyor.
/// Son okuduğumuz yerden yazma kafasına kadar olan kısım yeni ses. Sarma
/// noktasını atlamak, saniyede bir "tık" duyulması demek — o yüzden iki
/// parçada okunuyor.
///
/// ### Ses var/yok
///
/// İki mod var (bkz. <see cref="VoiceMode"/>). Otomatikte ölçüt RMS ve
/// **bırakma gecikmesi (hangover)** var: eşiğin altına düşer düşmez kesmek
/// kelimelerin sonunu yiyordu, çünkü konuşmanın sonu her zaman sönümlenerek
/// biter. Gecikme olmadan "tamam" → "tama" duyuluyordu.
/// </summary>
public class VoiceCapture : MonoBehaviour
{
    [Tooltip("Mikrofon halka tamponunun uzunluğu (saniye). Kare atlanırsa " +
        "bu süre kadar geriden okumak hâlâ mümkün.")]
    [SerializeField] private int deviceBufferSeconds = 1;

    [Tooltip("Konuşma bittikten sonra gönderime devam edilen süre (saniye). " +
        "Kelimelerin sonu sönümlenerek bittiği için eşiğin altına düşer " +
        "düşmez kesmek son heceyi yiyor.")]
    [SerializeField] private float hangover = 0.35f;

    [Tooltip("Bas-konuş modunda tuş bırakıldıktan sonraki kuyruk (saniye).")]
    [SerializeField] private float releaseTail = 0.12f;

    private VoiceChat chat;

    private string device;
    private AudioClip micClip;
    private int deviceRate;
    private int lastReadPosition;

    // Cihaz hızındaki ham örnekler; buradan indirgeyip çerçeve üretiyoruz.
    private float[] deviceScratch;
    private float[] pending;
    private int pendingCount;

    private readonly float[] frame = new float[VoiceCodec.FrameSamples];
    private readonly byte[] encoded = new byte[VoiceCodec.FrameSamples];

    private float openUntil;

    /// <summary>Son çerçevenin ölçülen gücü — ayarlar ekranındaki seviye çubuğu.</summary>
    public static float CurrentLevel { get; private set; }

    /// <summary>Şu an gönderiyor muyuz — arayüzdeki "konuşuyorsun" göstergesi.</summary>
    public static bool Transmitting { get; private set; }

    private void Awake() => chat = GetComponent<VoiceChat>();

    private void OnEnable() => Begin();

    private void OnDisable() => End();

    /// <summary>Ayarlar ekranı cihazı değiştirince yeniden başlatıyor.</summary>
    public void Restart()
    {
        End();
        Begin();
    }

    private void Begin()
    {
        if (!VoiceSettings.Enabled)
            return;

        device = VoiceSettings.ResolveDevice();

        if (device == null)
        {
            Debug.LogWarning("Mikrofon bulunamadı; sesli sohbet kapalı kalıyor.");
            return;
        }

        // Cihazın desteklediği hız. 0/0 dönerse cihaz her hızı kabul ediyor
        // demek ve 48000 güvenli bir tercih.
        Microphone.GetDeviceCaps(device, out int minRate, out int maxRate);
        deviceRate = maxRate <= 0 ? 48000 : Mathf.Clamp(48000, minRate, maxRate);

        micClip = Microphone.Start(device, true, Mathf.Max(1, deviceBufferSeconds), deviceRate);

        if (micClip == null)
        {
            Debug.LogWarning($"Mikrofon açılamadı: {device}");
            return;
        }

        deviceRate = micClip.frequency;
        lastReadPosition = 0;
        pendingCount = 0;

        deviceScratch = new float[deviceRate];
        pending = new float[deviceRate];
    }

    private void End()
    {
        if (device != null && Microphone.IsRecording(device))
            Microphone.End(device);

        micClip = null;
        Transmitting = false;
        CurrentLevel = 0f;
    }

    private void Update()
    {
        if (micClip == null || chat == null)
            return;

        int position = Microphone.GetPosition(device);
        if (position < 0 || position == lastReadPosition)
            return;

        ReadDeviceSamples(position);
        EmitFrames();
    }

    /// <summary>
    /// Mikrofon halkasından yeni örnekleri alıp `pending`'e ekler — cihaz
    /// hızından 8 kHz'e indirerek.
    /// </summary>
    private void ReadDeviceSamples(int position)
    {
        int available = position >= lastReadPosition
            ? position - lastReadPosition
            : micClip.samples - lastReadPosition + position;

        if (available <= 0)
            return;

        // Çok geride kaldıysak (kare donması) eskiyi atıyoruz: konuşmanın
        // yarım saniye gecikmiş hâlini göndermek, atlamaktan kötü.
        if (available > deviceScratch.Length)
        {
            lastReadPosition = position;
            return;
        }

        // Sarma noktası varsa iki parçada okunuyor; tek `GetData` çağrısı
        // sarmayı geçemiyor ve arada saniyede bir "tık" duyuluyordu.
        if (position >= lastReadPosition)
        {
            micClip.GetData(deviceScratch, lastReadPosition);
            Downsample(deviceScratch, 0, available);
        }
        else
        {
            int tail = micClip.samples - lastReadPosition;

            micClip.GetData(deviceScratch, lastReadPosition);
            Downsample(deviceScratch, 0, tail);

            micClip.GetData(deviceScratch, 0);
            Downsample(deviceScratch, 0, position);
        }

        lastReadPosition = position;
    }

    /// <summary>
    /// Cihaz hızından 8 kHz'e indirir: her çıkış örneği, karşılık gelen giriş
    /// örneklerinin ORTALAMASI. Örnek atlamak takma frekans üretiyor ve ses
    /// metalik çıkıyor.
    /// </summary>
    private void Downsample(float[] source, int offset, int count)
    {
        float ratio = deviceRate / (float)VoiceCodec.SampleRate;
        int outputCount = Mathf.FloorToInt(count / ratio);
        float gain = VoiceSettings.InputGain;

        for (int i = 0; i < outputCount; i++)
        {
            int start = offset + Mathf.FloorToInt(i * ratio);
            int end = offset + Mathf.FloorToInt((i + 1) * ratio);

            if (end > offset + count)
                end = offset + count;

            float total = 0f;
            int taken = 0;

            for (int s = start; s < end; s++, taken++)
                total += source[s];

            if (taken == 0)
                continue;

            if (pendingCount >= pending.Length)
                return; // tampon dolu; kalanı at

            pending[pendingCount++] = Mathf.Clamp(total / taken * gain, -1f, 1f);
        }
    }

    private void EmitFrames()
    {
        while (pendingCount >= VoiceCodec.FrameSamples)
        {
            System.Array.Copy(pending, 0, frame, 0, VoiceCodec.FrameSamples);

            pendingCount -= VoiceCodec.FrameSamples;
            System.Array.Copy(pending, VoiceCodec.FrameSamples, pending, 0, pendingCount);

            float level = VoiceCodec.Rms(frame, 0, VoiceCodec.FrameSamples);
            CurrentLevel = level;

            if (!ShouldTransmit(level))
            {
                Transmitting = false;
                continue;
            }

            Transmitting = true;

            VoiceCodec.Encode(frame, 0, encoded, VoiceCodec.FrameSamples);
            chat.SendFrame(encoded);
        }
    }

    /// <summary>
    /// Bu çerçeve gönderilsin mi. Her iki modda da kapanış gecikmeli: ses
    /// kesilir kesilmez susmak son heceyi yiyor.
    /// </summary>
    private bool ShouldTransmit(float level)
    {
        if (VoiceSettings.Mode == VoiceMode.PushToTalk)
        {
            if (Input.GetKey(KeyBindings.Get(GameAction.PushToTalk)))
                openUntil = Time.unscaledTime + releaseTail;
        }
        else if (level >= VoiceSettings.Threshold)
        {
            openUntil = Time.unscaledTime + hangover;
        }

        return Time.unscaledTime < openUntil;
    }
}
