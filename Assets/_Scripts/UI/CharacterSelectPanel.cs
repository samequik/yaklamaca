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

    [Header("Perk")]
    [Tooltip("Seçili perkin adı.")]
    [SerializeField] private TMP_Text perkLabel;

    [Tooltip("Perkin tek satırlık açıklaması — ad tek başına ne yaptığını söylemiyor.")]
    [SerializeField] private TMP_Text perkSummaryLabel;

    [Tooltip("Perkin sembolü. Görsel YOKSA gizleniyor — boş bir Image beyaz " +
        "çizdiği için ekranda anlamsız bir kare bırakır ve oynayan onu " +
        "'bozuk' diye okur (bölüm 13'ün kostüm renk kutusu dersi).")]
    [SerializeField] private Image perkIcon;

    [Tooltip("Perk sembolleri, PerkEffect SIRASINDA. 'Menü Kur' " +
        "Assets/_Art/UI/Perk/<PerkEffect adı>.png dosyalarından dolduruyor; " +
        "dosyası olmayan perkin yuvası boş kalıyor.")]
    [SerializeField] private Sprite[] perkIcons;

    [SerializeField] private Button perkPreviousButton;

    [SerializeField] private Button perkNextButton;

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

    public void PerkPrevious() => StepPerk(-1);

    public void PerkNext() => StepPerk(1);

    /// <summary>
    /// Perk listesinde dolaşır. Kostümle AYNI rol anahtarını kullanıyor
    /// (`monsterMode`): ekranın üstündeki KAÇAN/KATİL düğmesi hem kostümü
    /// hem perki birlikte çeviriyor.
    ///
    /// İkisini ayrı düğmelere bağlamak "kaçan kostümü + katil perki" gibi
    /// anlamsız bir karışım gösterebilirdi.
    /// </summary>
    private void StepPerk(int delta)
    {
        if (monsterMode)
        {
            PlayerProfile.MonsterPerk =
                PerkCatalog.Step(PerkCatalog.MonsterPerks, PlayerProfile.MonsterPerk, delta);
        }
        else
        {
            PlayerProfile.RunnerPerk =
                PerkCatalog.Step(PerkCatalog.RunnerPerks, PlayerProfile.RunnerPerk, delta);
        }

        // Kostümdeki gerekçenin aynısı: odadaysak kadro anında görüyor, oda
        // yoksa `Local` null ve seçim yine cihazda duruyor.
        RoundParticipant local = RoundParticipant.Local;
        if (local != null)
            local.PushPerks();

        Refresh();
    }

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
            roleLabel.SetText(Localization.Get(monsterMode ? "KATİL" : "KAÇAN"));

        // Bugün karakter adlarının HEPSİ özel isim (Banana Man, Unity-chan,
        // The Marionette, The Pig K.) ve tabloya hiçbiri girmiyor, yani `Get`
        // şu an hepsini olduğu gibi döndürüyor.
        //
        // Çağrı yine de duruyor: ekrana giden her yazının bu yoldan geçmesi
        // bu projenin kuralı ve ileride çevrilecek bir ad eklenirse sessizce
        // Türkçe kalmasın. Kaldırmak, o hatayı gelecekteki birine bırakırdı.
        if (costumeLabel != null)
            costumeLabel.SetText(Localization.Get(costume.Name));

        if (counterLabel != null)
            counterLabel.SetText("{0} / {1}", index + 1, list.Length);

        // Tek girişlik listede yön düğmeleri KAPALI: basılabilir görünüp
        // hiçbir şey yapmayan bir düğme oyuncuya "bozuk" diye okunuyor.
        bool many = list.Length > 1;

        if (previousButton != null)
            previousButton.interactable = many;

        if (nextButton != null)
            nextButton.interactable = many;

        RefreshPerk();

        if (statusLabel == null)
            return;

        if (!many)
        {
            statusLabel.SetText(Localization.Get("Şimdilik tek kostüm var. Yenileri eklenince burada çıkacak. " +
                "Modeli fareyle sürükleyerek çevirebilirsin."));
            return;
        }

        // Ekranın tek gerçek sınırı bu ve söylenmesi gerekiyor: oyuncu canavar
        // kostümünü seçip canavar olacağını sanmamalı.
        // İki taraf AYNI ŞEYİ anlatmıyor, o yüzden iki ayrı cümle:
        // katiller gerçekten farklı (vuruşları ayrı, ileride özellikleri de),
        // kaçanlar ise yalnızca görünüş olarak farklı.
        statusLabel.SetText(Localization.Get(monsterMode
            ? "Katili oda sahibi seçiyor. Bu, katil olursan hangisini oynayacağın."
            : "Kaçanlar yalnızca görünüş olarak farklı: hız, boy ve menzil aynı."));
    }

    /// <summary>
    /// Perk satırını tazeler.
    ///
    /// **Tek slot ve o slot HARİTA slotu** (bölüm "PERK SİSTEMİ"): burada
    /// yalnızca bir seçim var, listeden hangisi seçilirse o. İleride başka
    /// türde bir slot açılırsa burası ikinci bir satır kazanır — perkin
    /// kendi `Slot` alanı zaten hangisine ait olduğunu söylüyor.
    /// </summary>
    private void RefreshPerk()
    {
        PerkCatalog.Perk[] list =
            monsterMode ? PerkCatalog.MonsterPerks : PerkCatalog.RunnerPerks;

        int index = monsterMode
            ? PerkCatalog.SanitizeMonster(PlayerProfile.MonsterPerk)
            : PerkCatalog.SanitizeRunner(PlayerProfile.RunnerPerk);

        PerkCatalog.Perk perk = list[index];

        if (perkLabel != null)
            perkLabel.SetText(Localization.Get(perk.Name));

        if (perkSummaryLabel != null)
            perkSummaryLabel.SetText(Localization.Get(perk.Summary));

        ApplyPerkIcon(perk.Effect);

        // Kostümdeki kuralın aynısı: tek girişlik listede yön düğmeleri
        // KAPALI — basılabilir görünüp hiçbir şey yapmayan bir düğme "bozuk"
        // diye okunuyor.
        bool many = list.Length > 1;

        if (perkPreviousButton != null)
            perkPreviousButton.interactable = many;

        if (perkNextButton != null)
            perkNextButton.interactable = many;
    }

    /// <summary>
    /// Perkin sembolünü uygular.
    ///
    /// **Görseli olmayan perkte `Image` KAPATILIYOR.** `Image`'in varsayılanı
    /// beyaz, yani boş bir yuva ekranda anlamsız bir beyaz kare bırakır —
    /// bölüm 13'te kostüm renk göstergesinde tam olarak bu yaşandı ve
    /// kullanıcı onu "bozuk" diye okudu. Yuvanın kapladığı YER duruyor
    /// (düzen hiç değişmiyor), yalnızca kutu çizilmiyor; semboller
    /// geldiğinde satır kaymıyor.
    ///
    /// Dizi `PerkEffect` SIRASINDA, perkin listedeki indeksinde değil:
    /// liste yeniden sıralanırsa semboller kaymasın — `PerkCatalog`'un kendi
    /// kuralı ("oynanış `Effect`'e bakıyor, İNDEKSE değil").
    /// </summary>
    private void ApplyPerkIcon(PerkEffect effect)
    {
        if (perkIcon == null)
            return;

        int slot = (int)effect;

        Sprite sprite = perkIcons != null && slot >= 0 && slot < perkIcons.Length
            ? perkIcons[slot]
            : null;

        perkIcon.sprite = sprite;
        perkIcon.enabled = sprite != null;
    }
}
