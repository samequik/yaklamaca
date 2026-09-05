using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Konuşma çerçevelerinin ağ yolu: sahibi gönderir, **sunucu kimin
/// duyacağına karar verir**, dinleyiciye çalınmak üzere iner.
///
/// ### Kimin duyacağını neden sunucu seçiyor
///
/// Herkese yollayıp istemcide mesafe süzmek daha kolay olurdu ama o zaman
/// konuşan her oyuncunun **sesi ve dolaylı olarak yeri** tüm istemcilere
/// gitmiş olurdu — değiştirilmiş bir istemci koridorun öbür ucundaki konuşmayı
/// dinleyebilirdi. Bu, bölüm 4'teki "istemciye görmesi gerekmeyen bilgiyi
/// gönderme" kuralının aynısı; izlerin yalnızca canavara gönderilmesiyle aynı
/// gerekçe.
///
/// ### Duyma kuralları
///
/// | Durum | Kim duyar |
/// |---|---|
/// | Tur oynanmıyor (lobi, tur sonu) | Herkes herkesi, mesafesiz |
/// | Sahadaki → sahadaki | Yalnızca `hearingRange` içinde |
/// | Elenen/kurtulan → elenen/kurtulan | Hepsi, mesafesiz |
/// | Elenen → sahadaki | **Duyulmaz** |
///
/// **Ölüler yaşayanlardan koparılıyor, bilerek.** Bölüm 5 elenen oyuncunun
/// canavarı asla izleyememesini "sesli konuşulan bir oyunda doğrudan hile
/// olurdu" diye gerekçelendiriyor. Aynı gerekçe sese de aynen uyuyor: ölü
/// oyuncu canavarın yerini duyup yaşayanlara söyleyebilirdi. Ölüler kendi
/// aralarında serbestçe konuşuyor — izleyicilik cezalandırılmamalı.
///
/// **Lobide mesafe yok:** kadro kurulurken herkesin birbirini duyması gerek,
/// üstelik lobide harita zaten oynanmıyor.
///
/// ### Kanal
///
/// Gönderim **unreliable**: geciken bir ses çerçevesi işe yaramaz, yeniden
/// gönderilmesi yalnızca gecikmeyi büyütür. Kaybolan çerçevenin karşılığı
/// tek bir 20 ms'lik boşluk ve jitter tamponu onu zaten yutuyor.
/// </summary>
public class VoiceChat : NetworkBehaviour
{
    [Tooltip("Sahadaki oyuncuların birbirini duyduğu en uzak mesafe (metre). " +
        "Sis görüşü ~25 m; sesin biraz kısası, konuşmanın koridor boyu " +
        "taşınmaması için.")]
    [SerializeField] private float hearingRange = 18f;

    [SerializeField] private VoicePlayback playback;
    [SerializeField] private VoiceCapture capture;

    /// <summary>Bu oyuncunun sesini yerel olarak kısmak için (lobi listesi).</summary>
    public VoicePlayback Playback => playback;

    private void Awake()
    {
        if (playback == null)
            playback = GetComponent<VoicePlayback>();

        if (capture == null)
            capture = GetComponent<VoiceCapture>();

        // Yakalama yalnızca sahibinde. Burada kapatılıyor, `OnStartAuthority`
        // açıyor: `NetworkPlayerSetup`'ın listesine eklemek de olurdu ama o
        // liste prefabta serileştirilmiş ve eski prefablara ulaşmıyor
        // (bölüm 16'daki tuzak).
        if (capture != null)
            capture.enabled = false;
    }

    public override void OnStartAuthority()
    {
        base.OnStartAuthority();

        if (capture != null)
            capture.enabled = VoiceSettings.Enabled;
    }

    /// <summary>Ayarlar ekranı sesli sohbeti açıp kapatınca.</summary>
    public void ApplyEnabled()
    {
        if (capture == null || !isOwned)
            return;

        capture.enabled = VoiceSettings.Enabled;
    }

    // ---------- Gönderim ----------

    /// <summary><see cref="VoiceCapture"/> her çerçevede çağırıyor.</summary>
    public void SendFrame(byte[] frame)
    {
        if (!isOwned || frame == null)
            return;

        CmdVoice(frame);
    }

    [Command(channel = Channels.Unreliable, requiresAuthority = true)]
    private void CmdVoice(byte[] frame)
    {
        if (frame == null || frame.Length == 0 || frame.Length > VoiceCodec.FrameSamples * 2)
            return;

        ServerRoute(frame);
    }

    [Server]
    private void ServerRoute(byte[] frame)
    {
        IReadOnlyList<RoundParticipant> everyone = RoundParticipant.All;
        RoundParticipant speaker = GetComponent<RoundParticipant>();

        bool roundRunning = RoundManager.Instance != null
            && RoundManager.Instance.Phase == RoundPhase.Playing;

        bool speakerOnField = OnField(speaker);

        for (int i = 0; i < everyone.Count; i++)
        {
            RoundParticipant listener = everyone[i];

            if (listener == null || listener == speaker)
                continue;

            NetworkConnectionToClient connection = listener.connectionToClient;
            if (connection == null)
                continue; // bot

            if (!CanHear(roundRunning, speakerOnField, listener))
                continue;

            TargetVoice(connection, frame);
        }
    }

    private bool CanHear(bool roundRunning, bool speakerOnField, RoundParticipant listener)
    {
        // Tur oynanmıyorken kimse elenmiş sayılmıyor ve mesafenin anlamı yok.
        if (!roundRunning)
            return true;

        bool listenerOnField = OnField(listener);

        if (speakerOnField != listenerOnField)
            return false;

        // Elenenler ve kurtulanlar birbirini her yerden duyuyor: haritada
        // değiller, mesafenin karşılığı yok.
        if (!speakerOnField)
            return true;

        return (listener.transform.position - transform.position).sqrMagnitude
            <= hearingRange * hearingRange;
    }

    private static bool OnField(RoundParticipant participant) =>
        participant != null
        && participant.IsAlive
        && !participant.IsEscaped
        && !participant.IsSpectating;

    [TargetRpc(channel = Channels.Unreliable)]
    private void TargetVoice(NetworkConnectionToClient target, byte[] frame)
    {
        if (playback != null)
            playback.Push(frame);
    }
}
