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
/// ışık takımı ve ikinci bir `RenderTexture` kurmanın karşılığı yoktu. Figürü
/// fareyle sürükleyerek çevirebiliyorsun.
///
/// Panel bu yüzden ekranın SOLUNDA duruyor: sağ taraf modele ayrıldı.
///
/// ### Liste bugün TEK elemanlı
///
/// Kullanıcı renk çeşitlemelerini istemedi, gerçek modeller verecek
/// (bkz. <see cref="CharacterCatalog"/>). Yani ekran bugün bir seçici değil
/// bir **görüntüleyici**: karakterine bakıyor, çeviriyorsun. Yön düğmeleri
/// listede tek giriş varken KAPALI — basılabilir görünüp hiçbir şey yapmayan
/// bir düğme "bozuk" diye okunur (bölüm 19'daki gri kaydırıcı dersi).
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

    [Tooltip("Yön düğmeleri. Listede tek giriş varsa kapatılıyorlar.")]
    [SerializeField] private Button previousButton;

    [SerializeField] private Button nextButton;

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

        // Arka plandaki figür anında değişiyor: seçimin karşılığını görmek
        // için ekranı kapatmak gerekmiyor.
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

        // Tek girişlik listede yön düğmeleri KAPALI: basılabilir görünüp
        // hiçbir şey yapmayan bir düğme oyuncuya "bozuk" diye okunuyor.
        bool many = list.Length > 1;

        if (previousButton != null)
            previousButton.interactable = many;

        if (nextButton != null)
            nextButton.interactable = many;

        if (statusLabel == null)
            return;

        if (!many)
        {
            statusLabel.SetText("Şimdilik tek kostüm var. Yenileri eklenince burada çıkacak. " +
                "Modeli fareyle sürükleyerek çevirebilirsin.");
            return;
        }

        // Ekranın tek gerçek sınırı bu ve söylenmesi gerekiyor: oyuncu canavar
        // kostümünü seçip canavar olacağını sanmamalı.
        statusLabel.SetText(monsterMode
            ? "Canavarı oda sahibi seçiyor. Bu yalnızca canavar olursan görünüşün."
            : "Kostüm yalnızca görünüş: hız, boy ve menzil değişmiyor.");
    }
}
