using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Menü ekranları arasında geçişi ve oyunun duraklatılmasını yönetir.
///
/// Tek sorumluluğu gezinme: hangi panel açık, imleç kilitli mi, oyuncu girdisi
/// kapalı mı. Lobi ayarları ve seçenekler kendi bileşenlerinde.
///
/// İmleç kilidi buraya taşındı; eskiden PlayerController açılışta kilitliyordu
/// ve menü varken bu yanlış — menüde imleç serbest olmalı.
/// </summary>
public class MenuController : MonoBehaviour
{
    public enum Screen
    {
        None,        // menü kapalı, oyun oynanıyor
        NameEntry,   // ilk girişte bir kez
        Main,
        Settings,
        Audio,       // ses ve sesli sohbet
        Controls,    // tuş atamaları
        Lobby,       // oda: kadro, hazır, canavar seçimi, başlat
        JoinLobby,   // kod girme
        Pause
    }

    [Header("Paneller")]
    [SerializeField] private GameObject nameEntryPanel;
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject audioPanel;
    [SerializeField] private GameObject controlsPanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject joinLobbyPanel;
    [SerializeField] private GameObject pausePanel;

    [Tooltip("Panellerin arkasındaki tam ekran karartma. Tur oynanmıyorken " +
        "açılıyor: lobide arkada haritayı göstermek, oyun sürüyormuş gibi bir " +
        "izlenim veriyordu.")]
    [SerializeField] private GameObject backdrop;

    [Header("Duraklatma")]
    [Tooltip("Menü açıkken kapatılacak bileşenler: etkileşim, saldırı, fener.")]
    [SerializeField] private Behaviour[] disableWhileOpen;

    [Tooltip("Menü açıkken girdisi kesilecek karakter.")]
    [SerializeField] private PlayerController playerController;

    [Tooltip("Oyuncunun klavye girdi kaynağı; menü kapanınca geri bağlanır.")]
    [SerializeField] private PlayerInputSource playerInput;

    [Header("Davranış")]
    [SerializeField] private Screen startScreen = Screen.Main;
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

    private Screen current = Screen.None;

    // Bileşenleri her karede aramamak için: hangi oyuncu objesi için çözdüğümüz.
    private GameObject resolvedPlayer;

    // Seçenekler kapanınca dönülecek ekran.
    private Screen settingsReturn = Screen.Main;

    /// <summary>Menü açık mı — başka sistemler duraklamayı buradan öğrenebilir.</summary>
    public bool IsOpen => current != Screen.None;

    // Menü kapalıyken de imleci serbest bırakan kaplamalar (skor tablosu).
    private bool overlayOpen;

    /// <summary>
    /// Sahnedeki tek menü. Oyuncu prefabı doğduğunda imleci kilitleyip
    /// kilitlemeyeceğini buradan soruyor (bkz. NetworkPlayerSetup).
    /// </summary>
    public static MenuController Instance { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        // Adını hiç girmemiş oyuncuya önce isim ekranı; girenler doğrudan menüye.
        Show(PlayerProfile.HasName ? startScreen : Screen.NameEntry);
    }

    private void Update()
    {
        // Yerel oyuncu menü açıldıktan SONRA doğuyor: lobi ekranı sunucu
        // açılırken geliyor, oyuncu objesi bir kaç kare sonra. Yeniden
        // uygulamazsak hareket ve bakış hiç kesilmiyor ve lobide arkadaki
        // haritada dolaşılabiliyordu.
        if (NetworkClient.localPlayer != null
            && NetworkClient.localPlayer.gameObject != resolvedPlayer)
            ApplyGameplayState(IsOpen || overlayOpen);

        // Faz, ekran değişmeden de değişebiliyor (tur sunucudan başlatılınca).
        ApplyBackdrop();

        if (!Input.GetKeyDown(toggleKey))
            return;

        // Tuş atama ekranı bir tuş bekliyorsa Esc oranın iptali; menünün de
        // aynı Esc'yle geri gitmesi tek basışta iki iş yapardı.
        if (KeyBindingPanel.BlocksEscape)
            return;

        switch (current)
        {
            case Screen.None:
                Show(Screen.Pause);
                break;

            // Seçenekler nereden açıldıysa oraya dönüyor. Sabit "ana menüye
            // dön" davranışı, tur ortasında duraklatıp seçeneklere giren
            // oyuncuyu bağlantısı sürerken ana menüde bırakıyordu — oradan
            // tura geri dönmenin yolu yoktu.
            case Screen.Settings:
                Show(settingsReturn);
                break;

            // Ses ve tuş atamaları seçeneklerin ALTINDA duruyor: Esc bir üst
            // ekrana, yani seçeneklere dönüyor. Oradaki Esc de geldiği yere
            // (ana menü ya da duraklatma) dönüyor, yani zincir kırılmıyor.
            case Screen.Audio:
            case Screen.Controls:
                Show(Screen.Settings);
                break;

            case Screen.JoinLobby:
                Show(Screen.Main);
                break;

            case Screen.Pause:
                Show(Screen.None);
                break;

            case Screen.Main:
            case Screen.NameEntry:
            case Screen.Lobby:
                // Bu üçünde Esc'nin gidecek yeri yok. Ana menüden oyuna Oyna
                // ile dönülüyor, isim ekranı onaylanmadan geçilmiyor; lobiden
                // Esc ile çıkmak ise bağlantıyı sessizce koparırdı — orada
                // "AYRIL" düğmesi var.
                break;
        }
    }

    // ---------- Butonların çağırdığı metotlar ----------

    public void ShowMain() => Show(Screen.Main);
    public void ShowLobby() => Show(Screen.Lobby);

    /// <summary>
    /// Seçenekler. Nereden açıldığı hatırlanıyor ki GERİ ve Esc oraya dönsün —
    /// hem ana menüden hem tur ortasındaki duraklatmadan açılabiliyor.
    ///
    /// Seçenekler'in kendisi ve **tuş atamaları** dönüş adresi olarak
    /// kaydedilmiyor. Tuş atamaları Seçenekler'in çocuğu: oradan geri dönmek
    /// "Seçenekler'e tuş atamalarından gelindi" diye kaydedilseydi Seçenekler'in
    /// GERİ'si tekrar tuşlara atardı ve ikisi arasında sonsuz döngü olurdu.
    /// </summary>
    public void ShowSettings()
    {
        if (current != Screen.Settings && current != Screen.Controls)
            settingsReturn = current;

        Show(Screen.Settings);
    }

    /// <summary>Seçenekler ekranındaki GERİ düğmesi.</summary>
    public void CloseSettings() => Show(settingsReturn);

    /// <summary>Seçenekler ekranındaki "TUŞ ATAMALARI" düğmesi.</summary>
    public void ShowControls() => Show(Screen.Controls);

    /// <summary>Ses ekranı. Seçeneklerin altında; GERİ oraya dönüyor.</summary>
    public void ShowAudio() => Show(Screen.Audio);

    public void CloseAudio() => Show(Screen.Settings);

    /// <summary>Tuş atama ekranındaki GERİ düğmesi — her zaman Seçenekler'e döner.</summary>
    public void CloseControls() => Show(Screen.Settings);
    public void ShowJoinLobby() => Show(Screen.JoinLobby);
    public void ShowPause() => Show(Screen.Pause);

    /// <summary>Menüyü kapatıp oyuna döner.</summary>
    public void CloseMenu() => Show(Screen.None);

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- İç işleyiş ----------

    private void Show(Screen screen)
    {
        current = screen;

        SetActive(nameEntryPanel, screen == Screen.NameEntry);
        SetActive(mainPanel, screen == Screen.Main);
        SetActive(settingsPanel, screen == Screen.Settings);
        SetActive(audioPanel, screen == Screen.Audio);
        SetActive(controlsPanel, screen == Screen.Controls);
        SetActive(lobbyPanel, screen == Screen.Lobby);
        SetActive(joinLobbyPanel, screen == Screen.JoinLobby);
        SetActive(pausePanel, screen == Screen.Pause);

        ApplyGameplayState(IsOpen || overlayOpen);
        ApplyBackdrop();
    }

    /// <summary>
    /// Arkadaki karartma. Ölçüt ekran değil, **tur oynanıyor mu**:
    ///
    /// Tur sürerken duraklatma açıksa arkada sahneyi görmek istiyorsun; nerede
    /// durduğunu unutmadan devam edebilesin. Tur yokken (isim, ana menü, lobi)
    /// arkada gösterilecek bir oyun yok — haritayı göstermek "oyun arkada
    /// çalışıyor, Esc'ye basınca dönerim" izlenimi veriyordu.
    ///
    /// Seçenekler ekranı da bu sayede doğru davranıyor: ana menüden açılınca
    /// karanlık, tur ortasında duraklatmadan açılınca sahne görünür.
    /// </summary>
    private void ApplyBackdrop()
    {
        if (backdrop == null)
            return;

        bool inRound = RoundManager.Instance != null
            && RoundManager.Instance.Phase == RoundPhase.Playing;

        SetActive(backdrop, IsOpen && !inRound);
    }

    /// <summary>
    /// Menü dışında bir kaplama (skor tablosu) açıldığını bildirir.
    ///
    /// İmleç ve bakış yönetimi tek yerde kalmalı: iki bileşen birden
    /// `Cursor.lockState` yazmaya kalkarsa hangisinin kapattığı duruma göre
    /// değişir ve oyuncu bazen imleçsiz kalır.
    /// </summary>
    public void SetOverlayOpen(bool value)
    {
        if (overlayOpen == value)
            return;

        overlayOpen = value;
        ApplyGameplayState(IsOpen || overlayOpen);
    }

    private void ApplyGameplayState(bool menuOpen)
    {
        Cursor.lockState = menuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = menuOpen;

        ResolveLocalPlayer();

        // Girdi kaynağını sökmek, bileşeni kapatmaktan daha güvenilir: kapalı
        // MonoBehaviour'ın metotları yine de çağrılabiliyor.
        if (playerController != null)
            playerController.SetInputSource(menuOpen ? null : playerInput);

        // Tur HUD'ı OnGUI ile çiziyor ve IMGUI her zaman Canvas'ın üstünde
        // kalıyor: kapatılmazsa terminal sayacı menü yazılarının üzerine biner.
        if (RoundManager.Instance != null)
        {
            RoundHud hud = RoundManager.Instance.GetComponent<RoundHud>();
            if (hud != null)
                hud.enabled = !menuOpen;
        }

        if (disableWhileOpen == null)
            return;

        for (int i = 0; i < disableWhileOpen.Length; i++)
        {
            if (disableWhileOpen[i] != null)
                disableWhileOpen[i].enabled = !menuOpen;
        }
    }

    /// <summary>
    /// Duraklatılacak bileşenleri **çalışma anında** bulur.
    ///
    /// Menü kurulurken sahnedeki oyuncuya bağlanıyordu; ağ katmanıyla birlikte
    /// oyuncu artık prefabtan doğuyor ve sahnede bağlanacak bir şey yok. Bu
    /// yüzden yerel oyuncu spawn olduğunda referanslar bir kez buradan
    /// çözülüyor.
    ///
    /// Bıçak ve etkileşim ayrıca kapatılmak zorunda: ikisi de girdiyi kendisi
    /// okuyor, yalnızca hareket girdisini kesmek onları durdurmuyordu — menüde
    /// bir düğmeye basmak aynı anda bıçak da savurturdu.
    /// </summary>
    private void ResolveLocalPlayer()
    {
        NetworkIdentity local = NetworkClient.localPlayer;
        if (local == null || local.gameObject == resolvedPlayer)
            return;

        resolvedPlayer = local.gameObject;

        playerController = resolvedPlayer.GetComponentInChildren<PlayerController>(true);
        playerInput = resolvedPlayer.GetComponentInChildren<PlayerInputSource>(true);

        List<Behaviour> pausable = new List<Behaviour>();
        AddIfFound<PlayerInteractor>(pausable);
        AddIfFound<MonsterAttack>(pausable);
        AddIfFound<Flashlight>(pausable);

        disableWhileOpen = pausable.ToArray();
    }

    private void AddIfFound<T>(List<Behaviour> target) where T : Behaviour
    {
        T found = resolvedPlayer.GetComponentInChildren<T>(true);
        if (found != null)
            target.Add(found);
    }

    private static void SetActive(GameObject panel, bool active)
    {
        if (panel != null && panel.activeSelf != active)
            panel.SetActive(active);
    }
}
