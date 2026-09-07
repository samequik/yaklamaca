using UnityEngine;

/// <summary>
/// Gelen konuşma çerçevelerini oyuncunun üstündeki **3B** AudioSource'tan
/// çalar.
///
/// ### Neden 3B ve neden oyuncunun üstünde
///
/// Konuşma bu oyunda bir konum ipucu: koridorda birinin sesini duyup yerini
/// kestirmek oynanışın parçası. Ayrıca 3B kaynak olduğu için **mağara yankısı
/// bedavaya geliyor** — bölüm 12 bunu baştan öngörmüştü: "konuşma oyuncunun
/// üstündeki 3B bir AudioSource'tan çalarsa yankıya kendiliğinden giriyor."
///
/// Kişi başı ses seviyesi ve susturma da bedava: her konuşanın kendi
/// AudioSource'u var, `volume`'unu değiştirmek yetiyor. AudioMixer gerekmiyor
/// — ki script'ten kurulamıyor (bölüm 10, madde 6).
///
/// ### Akan klip: ses THREAD'i çekiyor, biz itmiyoruz
///
/// `AudioClip.Create(..., stream: true, ...)` ile klip her istendiğinde
/// `OnAudioRead`'i çağırıyor. Yani zamanlamayı ses motoru yönetiyor; bizim
/// işimiz sadece halka tamponu doldurmak. Alternatif (`SetData` ile döngüsel
/// klibe yazmak) okuma ve yazma kafalarını elle senkronlamayı gerektiriyor ve
/// kayma biriktikçe cızırdıyor.
///
/// **Halka tampon tek üretici–tek tüketici.** Ana thread yalnızca
/// `writeIndex`'i, ses thread'i yalnızca `readIndex`'i yazıyor; ikisi de
/// `volatile`. Kilit YOK, bilerek: ses thread'inde kilit beklemek doğrudan
/// cızırtı demek.
///
/// ### Jitter tamponu
///
/// Ağdan gelen çerçeveler düzensiz aralıklarla geliyor. Doğrudan çalmak her
/// gecikmede sessizlik deliği açardı. Bu yüzden çalmaya başlamadan önce
/// `startDelay` kadar biriktiriliyor: gecikme pahasına kesintisizlik.
/// Tampon boşalırsa (underrun) yeniden birikmeyi bekliyor — sürekli boş/dolu
/// gidip gelmektense bir kez susup düzgün başlamak daha az rahatsız.
/// </summary>
public class VoicePlayback : MonoBehaviour
{
    [Tooltip("Konuşmanın çalacağı 3B kaynak. AÇIKÇA bağlanmalı: oyuncu " +
        "prefabında üç AudioSource var (adım, konuşma, saldırı) ve " +
        "GetComponent ilkini — yani adım sesininkini — döndürür.")]
    [SerializeField] private AudioSource source;

    [Tooltip("Çalmaya başlamadan önce biriktirilen süre (saniye). Büyük " +
        "değer = daha az kesinti, daha çok gecikme.")]
    [SerializeField] private float startDelay = 0.08f;

    [Tooltip("Halka tamponun uzunluğu (saniye). Bundan fazlası birikirse en " +
        "yeni çerçeve atılıyor — eskiyi atmak ses thread'inin okuduğu yere " +
        "dokunmak olurdu.")]
    [SerializeField] private float bufferLength = 1f;

    private float[] ring;

    private volatile int writeIndex;
    private volatile int readIndex;

    // Yalnızca ses thread'i okuyup yazıyor.
    private bool primed;

    private int startSamples;
    private bool contextAllowed;
    private bool spatialPlayback;
    private volatile bool audible;
    private volatile int bufferVersion;
    private int audioBufferVersion;

    // Only the audio thread advances readIndex, including when discarding
    // speech buffered before a mute or a living/spectator channel change.
    public void SetContext(bool allowed, bool spatial, float hearingRange)
    {
        if (contextAllowed != allowed || spatialPlayback != spatial)
        {
            contextAllowed = allowed;
            spatialPlayback = spatial;
            bufferVersion++;
            LastFrameTime = -999f;
        }

        if (source != null)
        {
            source.spatialBlend = spatial ? 1f : 0f;
            source.bypassReverbZones = !spatial;
            source.maxDistance = Mathf.Max(hearingRange, source.minDistance + 0.01f);
        }

        ApplyVolume();
    }

    /// <summary>Bu oyuncunun sesi kısılmış mı (yerel tercih, ağa gitmiyor).</summary>
    public bool Muted { get; private set; }

    /// <summary>Kişiye özel ses seviyesi çarpanı (0-2).</summary>
    public float PersonalVolume { get; private set; } = 1f;

    /// <summary>Son çerçevenin geldiği an — "şu anda konuşuyor" göstergesi için.</summary>
    public float LastFrameTime { get; private set; } = -999f;

    public bool IsSpeaking => audible && Time.unscaledTime - LastFrameTime < 0.35f;

    private void Awake()
    {
        if (source == null)
            source = GetComponent<AudioSource>();

        ring = new float[Mathf.Max(VoiceCodec.FrameSamples * 4,
            Mathf.RoundToInt(VoiceCodec.SampleRate * bufferLength))];

        startSamples = Mathf.Clamp(
            Mathf.RoundToInt(VoiceCodec.SampleRate * startDelay),
            VoiceCodec.FrameSamples, ring.Length / 2);

        // Klip bir saniyelik ve döngüsel; içeriği her seferinde geri çağrıdan
        // geliyor, yani uzunluğunun oynanışla ilgisi yok.
        AudioClip clip = AudioClip.Create("Konusma", VoiceCodec.SampleRate, 1,
            VoiceCodec.SampleRate, true, OnAudioRead);

        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;

        ApplyVolume();

        // Sürekli çalıyor ve tampon boşken sessizlik döküyor. Duruma göre
        // Play/Stop etmek akan klipte tıklamaya yol açıyor; beş kaynağın boşta
        // dönmesinin maliyeti ölçülemez.
        source.Play();
    }

    private void OnDestroy()
    {
        if (source != null && source.clip != null)
            Destroy(source.clip);
    }

    // ---------- Ağdan gelen ----------

    /// <summary>Bir çerçeveyi tampona yazar. Ana thread'den çağrılıyor.</summary>
    public void Push(byte[] frame)
    {
        if (!audible || frame == null || frame.Length == 0 || ring == null)
            return;

        LastFrameTime = Time.unscaledTime;

        int write = writeIndex;
        int read = readIndex;

        int used = write >= read ? write - read : ring.Length - read + write;
        int free = ring.Length - used - 1;

        // Yer yoksa YENİ çerçeve atılıyor. Eskiyi atmak `readIndex`'e dokunmak
        // demek ve orası ses thread'inin — tek üretici/tek tüketici kuralı
        // bozulurdu.
        if (frame.Length > free)
            return;

        for (int i = 0; i < frame.Length; i++)
        {
            ring[write] = VoiceCodec.DecodeSample(frame[i]);
            write = write + 1 == ring.Length ? 0 : write + 1;
        }

        writeIndex = write;
    }

    // ---------- Ses thread'i ----------

    private void OnAudioRead(float[] data)
    {
        if (ring == null)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        int version = bufferVersion;
        int read = readIndex;
        int write = writeIndex;
        if (!audible || audioBufferVersion != version)
        {
            readIndex = write;
            audioBufferVersion = version;
            primed = false;
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        int used = write >= read ? write - read : ring.Length - read + write;

        if (!primed)
        {
            if (used < startSamples)
            {
                System.Array.Clear(data, 0, data.Length);
                return;
            }

            primed = true;
        }

        for (int i = 0; i < data.Length; i++)
        {
            if (read == write)
            {
                // Tampon kurudu: kalanı sessizlik, yeniden birikmeyi bekle.
                System.Array.Clear(data, i, data.Length - i);
                primed = false;
                break;
            }

            data[i] = ring[read];
            read = read + 1 == ring.Length ? 0 : read + 1;
        }

        readIndex = read;
    }

    // ---------- Yerel tercihler ----------

    public void SetMuted(bool value)
    {
        Muted = value;
        ApplyVolume();
    }

    public void SetPersonalVolume(float value)
    {
        PersonalVolume = Mathf.Clamp(value, 0f, 2f);
        ApplyVolume();
    }

    /// <summary>Ayarlar ekranındaki genel konuşma sesi değişince çağrılıyor.</summary>
    public void ApplyVolume()
    {
        if (source == null)
            return;

        bool enabled = contextAllowed && VoiceSettings.Enabled && !Muted;
        if (audible != enabled)
        {
            audible = enabled;
            bufferVersion++;
            LastFrameTime = -999f;
        }

        source.volume = enabled ? PersonalVolume * VoiceSettings.OutputVolume : 0f;
    }
}
