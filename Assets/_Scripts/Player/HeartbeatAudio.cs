using UnityEngine;

/// <summary>
/// Canavar yaklaştıkça hızlanan/yükselen kalp atışı — bölüm 10, kalan iş 1'in
/// karşılığı.
///
/// ### 2B, BİLEREK
///
/// Bölüm 12'nin kuralı: bu ses yönü belli olan bir kaynak olsaydı "geliyor
/// ama nereden" gerilimi kaybolup radara dönerdi. `spatialBlend = 0` hem
/// yönü siliyor hem de otomatik olarak mağara yankısının dışında tutuyor —
/// 2B kaynaklar zaten yankıya girmiyor.
///
/// ### Aynı `ScreenEffects.DreadAt` sayısından besleniyor
///
/// Ekran, ışık (fener titremesi) ve kamera (sarsıntı) hep aynı sayıyı
/// kullanıyor (bölüm 25); kalp atışı şimdi dördüncü tüketici. Canavarın
/// konumu zaten senkron olduğu için ek ağ trafiği sıfır — her istemci aynı
/// sonucu kendi hesaplıyor.
///
/// `DreadAt` HAM/anlık değeri döndürüyor; `ScreenEffects`'in kendi
/// yumuşatması private, buradan erişilemiyor. O yüzden burada da aynı
/// yükseliş/düşüş mantığı ayrıca uygulanıyor — sayılar bilerek `ScreenEffects`
/// ile aynı, ikisi birbirinden kopmasın diye.
///
/// ### Durdurmuyoruz, SUSTURUYORUZ
///
/// Dehşet sıfıra inince `AudioSource.Stop()` çağırmak yerine yalnızca ses
/// seviyesi sıfıra iniyor — döngü sessizce çalmaya devam ediyor. Stop/Play
/// ile açıp kapatmak, dehşet eşik civarında titrediğinde tekrar başlangıçtan
/// çalan bir klip demek (çıt sesi + ritmin sıfırlanması); sessiz bir döngünün
/// maliyeti bunun yanında önemsiz.
///
/// ### Kurulum gerekmiyor
///
/// `ScreenEffects` ve `MonsterAura`/`Terminal`'in kullandığı aynı gerekçe:
/// yerel oyuncunun kamerasına ÇALIŞMA ANINDA takılıyor. Prefaba serileştirilmiş
/// bir alan olsaydı `Ağ Kurulumu` zincirinin tamamını yeniden çalıştırmak
/// gerekirdi (bölüm 7). Klibin kendisi `NetworkPlayerSetup.heartbeatClip`
/// alanında duruyor — `Sesleri Yerleştir` bağlıyor — çünkü bu bileşenin
/// kendisi hiçbir prefabta serileşmiyor ve klibi build'e sokacak başka bir
/// referans yok.
/// </summary>
public class HeartbeatAudio : MonoBehaviour
{
    [SerializeField] private float minVolume = 0f;

    // 0.85'ten 0.65'e indirildi (2026-09-13, oynanış geri bildirimi): dehşet
    // katmanının dört tüketicisinden biri (ekran, kamera, fener ile birlikte)
    // ve hepsi aynı yönde fazla agresifti.
    [SerializeField] private float maxVolume = 0.65f;
    [SerializeField] private float minPitch = 0.9f;
    [SerializeField] private float maxPitch = 1.3f;

    // ScreenEffects'in dreadRise/dreadFall'ıyla aynı: kalp atışı ekranla aynı
    // hızda "korkmalı", biri diğerinden önce tepki verirse ikisi ayrışır.
    [SerializeField] private float rise = 0.9f;
    [SerializeField] private float fall = 0.30f;

    private AudioSource source;
    private RoundParticipant owner;
    private float dread;

    /// <summary>Yerel oyuncunun kamerasına takar; ikinci kez takmıyor, klip yoksa hiç eklemiyor.</summary>
    public static void Attach(Camera target, AudioClip clip)
    {
        if (target == null || clip == null || target.GetComponent<HeartbeatAudio>() != null)
            return;

        HeartbeatAudio heartbeat = target.gameObject.AddComponent<HeartbeatAudio>();
        heartbeat.source.clip = clip;
        heartbeat.source.Play();
    }

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
    }

    private void Update()
    {
        if (owner == null)
            owner = GetComponentInParent<RoundParticipant>();

        float target = ScreenEffects.DreadAt(owner);
        float rate = target > dread ? rise : fall;
        dread = Mathf.MoveTowards(dread, target, Time.deltaTime * rate);

        source.volume = Mathf.Lerp(minVolume, maxVolume, dread);
        source.pitch = Mathf.Lerp(minPitch, maxPitch, dread);
    }
}
