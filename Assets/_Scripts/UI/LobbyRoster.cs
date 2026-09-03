using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lobideki oyuncu listesinin **görünüm modeli**. Kendi başına hiçbir şeye
/// karar vermiyor: her tazelemede sunucudan gelen katılımcıları okuyup
/// ekranın çizeceği hâle getiriyor.
///
/// Eskiden burada kontenjan ve rol seçimi mantığı vardı; ağ katmanı gelince
/// hepsi düştü. Rol dağıtımı `RoundManager`'ın işi, kimin odada olduğu da
/// Mirror'ın spawn ettiği `RoundParticipant`'lardan okunuyor. Geriye burada
/// yalnızca sunucunun bilmesine gerek olmayan tek şey kaldı: **aynı adı
/// taşıyanları numaralandırmak.**
///
/// Bu ayrım bilinçli (CLAUDE.md bölüm 5, "tur verisi tek yerde"): iki yerde
/// tutulan kadro, er ya da geç birbirini tutmaz.
/// </summary>
public class LobbyRoster : MonoBehaviour
{
    /// <summary>1 canavar + 4 kaçan.</summary>
    public const int MaxPlayers = 5;

    public class Member
    {
        /// <summary>Katılımcının ağ kimliği — canavar seçimi bununla gönderiliyor.</summary>
        public uint NetId;

        /// <summary>Oyuncunun kendi girdiği ad.</summary>
        public string BaseName;

        /// <summary>Çakışma varsa numaralandırılmış hâli — ekranda bu görünür.</summary>
        public string DisplayName;

        public bool IsLocal;
        public bool IsReady;
        public bool IsRoomOwner;
        public bool IsBot;

        /// <summary>Oda sahibi bu kişiyi canavar olarak seçti mi.</summary>
        public bool IsChosenMonster;
    }

    private readonly List<Member> members = new List<Member>();

    public IReadOnlyList<Member> Members => members;

    /// <summary>Kadro gerçekten değişince tetiklenir; arayüz buna bakıp çiziyor.</summary>
    public event System.Action Changed;

    public Member Local
    {
        get
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].IsLocal)
                    return members[i];
            }

            return null;
        }
    }

    /// <summary>
    /// Sunucudan gelen katılımcıları okuyup listeyi tazeler.
    ///
    /// Her karede çağrılabilir: gerçekten bir şey değiştiyse `Changed`
    /// tetikleniyor. SyncVar'ların toplu bir "değişti" olayı yok, o yüzden
    /// yoklama yapıyoruz — beş kişilik bir liste için bunun maliyeti sıfır,
    /// karşılığında ayrı bir liste mesajı yazmaktan kurtuluyoruz.
    /// </summary>
    public void SyncFromNetwork()
    {
        uint chosenMonster = RoundManager.Instance != null ? RoundManager.Instance.MonsterChoice : 0u;

        List<RoundParticipant> live = new List<RoundParticipant>();
        foreach (RoundParticipant participant in RoundParticipant.All)
        {
            if (participant != null)
                live.Add(participant);
        }

        // Sıra sabit olmalı: netId'ye göre diziyoruz, yoksa satırlar her
        // tazelemede yer değiştirip okunmaz hâle gelirdi.
        live.Sort((a, b) => a.netId.CompareTo(b.netId));

        if (!HasChanged(live, chosenMonster))
            return;

        members.Clear();

        for (int i = 0; i < live.Count && i < MaxPlayers; i++)
        {
            RoundParticipant participant = live[i];

            members.Add(new Member
            {
                NetId = participant.netId,
                BaseName = PlayerProfile.Sanitize(participant.DisplayName),
                DisplayName = participant.DisplayName,
                IsLocal = participant.isLocalPlayer,
                IsReady = participant.IsReady,
                IsRoomOwner = participant.IsRoomOwner,
                IsBot = participant.IsBot,
                IsChosenMonster = chosenMonster != 0 && participant.netId == chosenMonster
            });
        }

        RefreshDisplayNames();
        Changed?.Invoke();
    }

    /// <summary>
    /// Yeniden çizmeye değer bir fark var mı. Ad, hazır durumu, sahiplik ve
    /// canavar seçimi — ekranda görünen her şey.
    /// </summary>
    private bool HasChanged(List<RoundParticipant> live, uint chosenMonster)
    {
        int expected = Mathf.Min(live.Count, MaxPlayers);
        if (expected != members.Count)
            return true;

        for (int i = 0; i < expected; i++)
        {
            RoundParticipant participant = live[i];
            Member member = members[i];

            if (member.NetId != participant.netId
                || member.BaseName != PlayerProfile.Sanitize(participant.DisplayName)
                || member.IsReady != participant.IsReady
                || member.IsRoomOwner != participant.IsRoomOwner
                || member.IsChosenMonster != (chosenMonster != 0 && participant.netId == chosenMonster))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Aynı adı taşıyan birden fazla kişi varsa hepsine sıra numarası verilir:
    /// tek kişiyse "Player", ikisi varsa "Player 1" ve "Player 2".
    ///
    /// Numarayı sadece sonradan gelene vermek ilkini ayrıcalıklı kılıyordu;
    /// isimleri kimin önce girdiğine göre farklı göstermek kafa karıştırıcı.
    ///
    /// Sunucu bunu yapmıyor, bilerek: bu tamamen bir gösterim meselesi ve
    /// sunucunun ad çakışmasıyla ilgilenmesi gereken bir kararı yok.
    /// </summary>
    private void RefreshDisplayNames()
    {
        Dictionary<string, int> totals = new Dictionary<string, int>();

        for (int i = 0; i < members.Count; i++)
        {
            string key = members[i].BaseName;
            totals[key] = totals.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        Dictionary<string, int> assigned = new Dictionary<string, int>();

        for (int i = 0; i < members.Count; i++)
        {
            string key = members[i].BaseName;

            if (totals[key] == 1)
            {
                members[i].DisplayName = key;
                continue;
            }

            int index = assigned.TryGetValue(key, out int used) ? used + 1 : 1;
            assigned[key] = index;
            members[i].DisplayName = $"{key} {index}";
        }
    }
}
