using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Karakter seçim ekranı: kaçan kostümü ve canavar kostümü
/// (bkz. <see cref="CharacterCatalog"/>).
///
/// ### İKİ kostüm birden seçiliyor, bir "karakter" değil
///
/// Rolü sunucu dağıtıyor (bölüm 11.1) ve oda sahibi canavarı lobiden seçiyor.
/// Yani bu ekran "kim olacağım"ı değil "hangi rolde neye benzeyeceğim"i
/// soruyor: ikisi de önceden seçiliyor, tur başlayınca rolüne uyan gösteriliyor.
///
/// Tek bir liste yapmak (kaçanlar ve canavarlar aynı listede) daha basit
/// görünürdü ama yanlış bir söz verirdi: listeden canavarı seçen oyuncu
/// canavar olacağını sanardı.
///
/// ### Önizleme ayrı bir pencere DEĞİL
///
/// Seçilen karakter menünün arkasındaki sahnede duruyor (<see cref="MenuStage"/>)
/// ve ekran açılınca kamera onun üstüne gidiyor. İkinci bir kamera, ikinci bir
/// ışık takımı ve ikinci bir `RenderTexture` kurmanın karşılığı yoktu.
///
/// Panel bu yüzden ekranın SOLUNDA duruyor: sağ taraf modele ayrıldı.
///
/// ### Ekran hiçbir şeye karar vermiyor
///
/// Seçim cihazda saklanıyor (`PlayerProfile`) ve odadaysak sunucuya
/// bildiriliyor. Sunucu indeksi kendi temizliyor
/// (`RoundParticipant.CmdSetCostume`), yani değiştirilmiş bir istemci en fazla
/// kendi ekranında olmayan bir kostüm gösterir.
/// </summary>
public class CharacterSelectPanel : MonoBehaviour
{
    [Header("Başlık")]
    [Tooltip("Hangi rolün kostümünü seçtiğimizi söyleyen düğmenin yazısı.")]
    [SerializeField] private TMP_Text roleLabel;

    [Header("Kostüm")]
    [SerializeField] private TMP_Text costumeLabel;

    [Tooltip("Kaçıncı kostümdeyiz — listenin uzunluğu görünmezse oyuncu " +
        "sonuna geldiğini anlamıyor.")]
    [SerializeField] private TMP_Text counterLabel;

    [Tooltip("Seçili kostümün rengini gösteren küçük kare. Renk adı tek " +
        "başına ne olduğunu anlatmıyor.")]
    [SerializeField] private Image swatch;

    [Header("Durum")]
    [SerializeField] private TMP_Text statusLabel;

    /// <summary>Canavar kostümünü mü seçiyoruz. Kaçanla başlıyor: olağan rol o.</summary>
    private bool monsterMode;

    private void OnEnable()
    {
        Refresh();
        ApplyFocus();
    }

    private void OnDisable()
    {
        // Arka plan olağan hâline dönüyor: ikisi yan yana.
        MenuStage.SetFocus(MenuStage.Focus.Pair);
    }

    // ---------- Düğmeler ----------

    /// <summary>Kaçan ve canavar kostümleri arasında geçiş yapar.</summary>
    public void ToggleRole()
    {
        monsterMode = !monsterMode;
        Refresh();
        ApplyFocus();
    }

    public void Previous() => Step(-1);

    public void Next() => Step(1);

    private void Step(int delta)
    {
        if (monsterMode)
        {
            PlayerProfile.MonsterCostume =
                CharacterCatalog.Step(CharacterCatalog.Monsters, PlayerProfile.MonsterCostume, delta);
        }
        else
        {
            PlayerProfile.RunnerCostume =
                CharacterCatalog.Step(CharacterCatalog.Runners, PlayerProfile.RunnerCostume, delta);
        }

        // Arka plandaki figür anında değişiyor: seçimin karşılığını görmek için
        // ekranı kapatmak gerekmiyor.
        MenuStage.ApplyCostumes();

        // Odadaysak kadrodaki herkes de anında görüyor. Oda yoksa `Local` null
        // ve hiçbir şey olmuyor — seçim yine cihazda duruyor ve oyuncu objesi
        // doğduğunda `OnStartLocalPlayer` onu bildiriyor.
        RoundParticipant local = RoundParticipant.Local;
        if (local != null)
            local.PushCostume();

        Refresh();
    }

    // ---------- Görünüm ----------

    private void ApplyFocus() =>
        MenuStage.SetFocus(monsterMode ? MenuStage.Focus.Monster : MenuStage.Focus.Runner);

    private void Refresh()
    {
        CharacterCatalog.Costume[] list =
            monsterMode ? CharacterCatalog.Monsters : CharacterCatalog.Runners;

        int index = monsterMode
            ? CharacterCatalog.SanitizeMonster(PlayerProfile.MonsterCostume)
            : CharacterCatalog.SanitizeRunner(PlayerProfile.RunnerCostume);

        CharacterCatalog.Costume costume = list[index];

        if (roleLabel != null)
            roleLabel.SetText(monsterMode ? "CANAVAR KOSTÜMÜ" : "KAÇAN KOSTÜMÜ");

        if (costumeLabel != null)
            costumeLabel.SetText(costume.Name);

        if (counterLabel != null)
            counterLabel.SetText("{0} / {1}", index + 1, list.Length);

        if (swatch != null)
            swatch.color = costume.Tint;

        if (statusLabel == null)
            return;

        // Ekranın tek gerçek sınırı bu ve söylenmesi gerekiyor: oyuncu canavar
        // kostümünü seçip canavar olacağını sanmamalı.
        statusLabel.SetText(monsterMode
            ? "Canavarı oda sahibi seçiyor. Bu yalnızca canavar olursan görünüşün."
            : "Kostüm yalnızca görünüş: hız, boy ve menzil değişmiyor.");
    }
}
