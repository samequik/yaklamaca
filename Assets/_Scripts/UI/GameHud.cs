using TMPro;
using UnityEngine;

/// <summary>
/// Oyun içi arayüzün kökü: nişangah, nişan yazısı, tur durumu.
///
/// ### Neden var — teknik borç 2'nin karşılığı
///
/// Bunların hepsi `OnGUI` ile çiziliyordu. IMGUI **her zaman Canvas'ın
/// üstünde** kalıyor, yani menü açıldığında tur yazıları menünün üzerine
/// biniyordu ve `MenuController` onları elle kapatmak zorundaydı
/// (`hud.enabled = !menuOpen`). Kapatılması unutulan `Terminal`/`ExitLock`
/// ekranları duraklatma menüsünün üstüne binmeye devam ediyordu.
///
/// Canvas'a taşınınca sıralama kendiliğinden doğru oluyor: HUD menü
/// panellerinden ÖNCE geliyor, yani menü açıldığında üstünü örtüyor. Elle
/// kapatma da tek bir görünürlük kuralına indi.
///
/// ### Görünürlük tek kuralda
///
/// **Menü açıkken HUD kapalı.** Eski davranışın aynısı ama tek yerde: hangi
/// parçanın ne zaman kapatılacağını `MenuController` bilmek zorunda değil,
/// HUD kendi durumunu okuyor. Menü açıkken oyun zaten duraklamış oluyor.
///
/// Ayrı bir "tur oynanıyor mu" şartı YOK, bilerek: lobide menü zaten açık,
/// yani HUD kendiliğinden gizli. İki şart koymak aynı sonucu iki yerden
/// üretmek olurdu.
///
/// ### Neden menü canvas'ının üstünde
///
/// `Menü Kur` bütün arayüzü tek yerden kuruyor ve mikrofon göstergesi ile TAB
/// paneli de orada. Ayrı bir canvas ikinci bir `EventSystem` sırası, ikinci
/// bir ölçekleyici ayarı ve "hangisi üstte" sorusu demekti.
/// </summary>
public class GameHud : MonoBehaviour
{
    [SerializeField] private GameObject root;

    public static GameHud Instance { get; private set; }

    /// <summary>HUD şu an görünür mü — alt parçalar buna bakıyor.</summary>
    public static bool Visible { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Bir karakteri fontta yoksa yedeğine çevirir.
    ///
    /// Varsayılan TMP atlası (`LiberationSans SDF`) **statik** ve yalnızca
    /// temel Latin kapsıyor; ok gibi karakterler ancak **dinamik fallback**'ten
    /// gelebiliyor. Fallback bu projede var ve çalışıyor (Türkçe harfler ondan
    /// geliyor) ama garantisi yok — lobi etiketlerinde bir kez "★" boş kutuya
    /// dönüşmüştü (bölüm 13).
    ///
    /// `tryAddCharacter` açık: sınamakla kalmıyor, bulabildiyse fallback'e
    /// ekliyor da. Yani ilk çağrı hem cevabı veriyor hem sorunu çözüyor.
    ///
    /// Boş kutu göstermektense okunur bir yedek göstermek her zaman daha iyi:
    /// oyuncu ne yapacağını yazıdan anlıyor, süslemeden değil.
    /// </summary>
    public static string Glyph(TMP_Text label, string glyph, string fallback)
    {
        if (label == null || label.font == null || string.IsNullOrEmpty(glyph))
            return fallback;

        return label.font.HasCharacter(glyph[0], searchFallbacks: true, tryAddCharacter: true)
            ? glyph
            : fallback;
    }

    private void Update()
    {
        Visible = MenuController.Instance == null || !MenuController.Instance.IsOpen;

        if (root != null && root.activeSelf != Visible)
            root.SetActive(Visible);
    }
}
