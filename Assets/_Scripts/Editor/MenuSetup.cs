using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menü arayüzünü sahneye kurar: isim, ana menü, seçenekler, lobi odası,
/// kodla katılma ve duraklatma ekranları. Hepsi ağa bağlı (bkz. bölüm 13).
///
/// Üretilen şey sıradan uGUI nesneleri — Scene penceresinden istediğin gibi
/// düzenleyebilir, renkleri ve yerleşimi değiştirebilirsin. Düğmeler kalıcı
/// dinleyiciyle bağlanıyor (`UnityEventTools.AddPersistentListener`), yani
/// Inspector'da görünüyor ve elle değiştirilebiliyor.
///
/// **Ağ Kurulumu'ndan SONRA çalıştırılmalı:** bu araç Mirror'ın test HUD'ını
/// kaldırıyor ve ağ kurulumunun kapattığı menü objesini geri açıyor.
///
/// Menü: Yakalamaca > Menü Kur
/// </summary>
public static class MenuSetup
{
    private const string CanvasName = "Menu";

    /// <summary>
    /// Katılma ekranındaki oda satırı sayısı.
    ///
    /// `RelayLobby` bundan fazlasını döndürebiliyor; fazlası gösterilmiyor ve
    /// oyuncuya kaç oda bulunduğu yazılıyor (bkz. `JoinLobbyPanel`). Satırlar
    /// sabit sayıda kuruluyor çünkü menü kodla üretiliyor ve çalışma anında
    /// obje yaratmak bölüm 2'nin havuzlama kuralına takılırdı.
    /// </summary>
    private const int RoomListRows = 6;

    // Menünün görsel dili terminal ve çıkış kilidi panelleriyle aynı aileden
    // (bölüm 18): koyu gövde, ince çerçeve, köşe ayraçları ve TEK renk ailesi.
    // Buradaki aile KIRMIZI — canavarın rengi, yani oyunun kimliği. Eskiden
    // paneller bir gri, düğmeler başka bir gri, vurgu ayrı bir kırmızıydı ve
    // hiçbiri birbirine bağlı değildi; "kimliksiz" görünmesinin sebebi buydu.
    //
    // Panel artık YARI SAYDAM: gövdeyi sütunun kendi kutusu taşıyor, tam ekran
    // dolgu yalnızca arkayı karartıyor. Duraklatmada arkadaki sahnenin
    // görünmesi gerekiyor (bölüm 13), o yüzden opak olamaz.
    private static readonly Color PanelColor = new Color(0.02f, 0.02f, 0.03f, 0.55f);

    /// <summary>İçerik kutusunun gövdesi — terminal panelleriyle aynı koyuluk.</summary>
    private static readonly Color BoxColor = new Color(0.045f, 0.030f, 0.032f, 0.95f);

    private static readonly Color AccentColor = new Color(0.88f, 0.28f, 0.22f, 1f);

    /// <summary>Çerçeve ve ayraçların rengi: aynı kırmızının sönük tonu.</summary>
    private static readonly Color AccentDim = new Color(0.40f, 0.13f, 0.10f, 1f);

    /// <summary>Düğme yazısı ve ikincil vurgular.</summary>
    private static readonly Color AccentLight = new Color(0.96f, 0.72f, 0.66f, 1f);

    private static readonly Color ButtonColor = AccentColor;
    private static readonly Color TextColor = new Color(0.93f, 0.89f, 0.87f, 1f);

    // Mini harita (2026-09-27'de yeniden yazıldı): sol üst köşe, çevrendeki
    // dar bir pencere, bakışa göre DÖNÜYOR.
    //
    // İlk sürüm 150 pikselin içine ~54 × 96 m'lik haritanın TAMAMINI
    // sıkıştırıyordu: 3.2 m'lik bir koridor hücresi 4 piksele düşüyor ve
    // okunmuyordu. Kullanıcı "karışık" deyip kapattırmıştı (2026-09-16).
    private const float MinimapSize = 210f;
    private const float MinimapMargin = 16f;

    /// <summary>Gövde ile pencere arasındaki iç boşluk (çerçeve payı).</summary>
    private const float MinimapPadding = 9f;

    /// <summary>
    /// Pencerenin gösterdiği dünya genişliği. 26 m ≈ 8 koridor hücresi:
    /// yanındaki iki dönüşü görecek kadar geniş, haritayı ezberletmeyecek
    /// kadar dar.
    /// </summary>
    private const float MinimapMetersAcross = 26f;

    private const float MinimapPlayerDotSize = 16f;
    private static readonly Color MinimapBackgroundColor = new Color(0.03f, 0.03f, 0.04f, 0.78f);
    private static readonly Color MinimapWallColor = new Color(0.55f, 0.52f, 0.50f, 1f);
    private static readonly Color MinimapRunnerColor = new Color(0.30f, 0.85f, 0.40f, 1f);

    [MenuItem("Yakalamaca/Menü Kur")]
    private static void Build()
    {
        if (Resources.Load<TMP_Settings>("TMP Settings") == null)
        {
            EditorUtility.DisplayDialog("TextMeshPro kaynakları eksik",
                "Menü yazıları TextMeshPro kullanıyor ama kaynakları henüz içe aktarılmamış.\n\n" +
                "Window > TextMeshPro > Import TMP Essential Resources\n\n" +
                "İndirme gerektirmez, paketin içinde geliyor. Sonra bu komutu tekrar çalıştır.",
                "Tamam");
            return;
        }

        GameObject existing = GameObject.Find(CanvasName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        EnsureEventSystem();

        GameObject canvasObject = CreateCanvas();
        MenuController controller = canvasObject.AddComponent<MenuController>();

        // İlk çocuk: panellerin arkasında duran tam ekran karartma. Tur
        // oynanmıyorken açılıyor (bkz. MenuController.ApplyBackdrop).
        GameObject backdrop = CreateBackdrop(canvasObject.transform);

        // Arkaplanın arkasındaki karakter sahnesi. Menü canvas'ından bağımsız
        // bir sahne kökü kuruyor; `MenuStage` çalışma anında bağlıyor.
        MenuStageSetup.Build();

        // Oyun içi HUD hemen karartmanın üstünde: menü panellerinden ÖNCE
        // geliyor, yani menü açıldığında panel onun üstünü örtüyor. Zaten
        // `GameHud` menü açıkken kendini gizliyor — sıralama ikinci güvence.
        BuildGameHud(canvasObject.transform);

        // Menüyle Mirror arasındaki köprü. Canvas'ın üstünde duruyor ve
        // NetworkBehaviour DEĞİL: sahnedeki menü objesine NetworkIdentity
        // eklenemiyor (CLAUDE.md bölüm 4).
        LobbyNetwork network = canvasObject.AddComponent<LobbyNetwork>();
        WireTransports(network);

        GameObject nameEntry = BuildNameEntryPanel(canvasObject.transform, controller);
        GameObject main = BuildMainPanel(canvasObject.transform, controller, network);
        GameObject settings = BuildSettingsPanel(canvasObject.transform, controller);
        GameObject audio = BuildAudioPanel(canvasObject.transform, controller);
        GameObject controls = BuildControlsPanel(canvasObject.transform, controller);
        GameObject lobby = BuildLobbyPanel(canvasObject.transform, controller, network);
        GameObject joinLobby = BuildJoinLobbyPanel(canvasObject.transform, controller, network);
        GameObject characters = BuildCharacterPanel(canvasObject.transform, controller);
        GameObject pause = BuildPausePanel(canvasObject.transform, controller,
            network.Leave, "ODADAN AYRIL");

        // HUD parçaları: MenuController'ın panel listesine GİRMİYORLAR, çünkü
        // ekran değiştikçe açılıp kapanmamaları gerekiyor. Canvas'ın çocuğu
        // olarak duruyorlar ve görünürlüklerini kendileri yönetiyor.
        BuildVoiceHud(canvasObject.transform);
        BuildScoreboard(canvasObject.transform);

        WireController(controller, nameEntry, main, settings, audio, controls, lobby, joinLobby,
            characters, pause, backdrop);

        SerializedObject serializedNetwork = new SerializedObject(network);
        serializedNetwork.FindProperty("menu").objectReferenceValue = controller;
        serializedNetwork.ApplyModifiedProperties();

        // Kurulumdan sonra hepsi açık kalırsa Scene penceresinde üst üste
        // binmiş paneller görünüyor. Sadece ana menü açık kalsın; hangisinin
        // gerçekten açılacağına MenuController çalışma anında karar veriyor.
        nameEntry.SetActive(false);
        settings.SetActive(false);
        audio.SetActive(false);
        controls.SetActive(false);
        lobby.SetActive(false);
        joinLobby.SetActive(false);
        characters.SetActive(false);
        pause.SetActive(false);

        // Menü ağ öncesinde kapatılmıştı; artık ağın kendisi menüden yönetiliyor.
        canvasObject.SetActive(true);

        RemoveLegacyHud();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Selection.activeGameObject = canvasObject;
        Debug.Log(
            "Menü kuruldu ve ağa bağlandı. Sahne kaydedildi.\n\n" +
            "Test:\n" +
            "1. Play'e bas, LOBİ KUR — ekranda 7 harflik kod çıkar.\n" +
            "2. Build alıp ikinci bir kopya çalıştır, LOBİYE KATIL, kodu gir.\n" +
            "   (Aynı makinede deniyorsan kod yerine 127.0.0.1 yazman yeterli.)\n" +
            "3. İkisi de HAZIRIM'a bassın; oda sahibinde BAŞLAT aktifleşir.\n\n" +
            "Kod, sunucunun IPv4 adresinin kendisi — eşleştirme sunucusu yok. " +
            "Aynı ağda çalışır; internet üzerinden 7777/UDP yönlendirmesi ya da " +
            "sanal ağ (Hamachi/Radmin) gerekir.");
    }

    /// <summary>
    /// Mirror'ın test amaçlı ekran üstü Host/Client butonlarını kaldırır.
    /// Gerçek lobi geldiğine göre işi bitti; kalırsa menünün üstüne biniyor.
    /// </summary>
    private static void RemoveLegacyHud()
    {
        NetworkManagerHUD hud = Object.FindObjectOfType<NetworkManagerHUD>();
        if (hud != null)
            Undo.DestroyObjectImmediate(hud);

        // `RoundHud` sınıfı silindi (teknik borç 2): tur yazıları artık
        // Canvas'ta. Sahnedeki bileşen "missing script" olarak kalıyor ve
        // Unity her açılışta uyarı basıyor — burada temizleniyor ki oyuncu
        // ayrıca `Hataları Temizle` çalıştırmak zorunda kalmasın.
        // `Instance` çalışma anında kuruluyor, editörde null — sahneden
        // aranıyor. Kapalı objeler de taranıyor.
        RoundManager manager = Object.FindObjectOfType<RoundManager>(true);
        if (manager != null)
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(manager.gameObject);
    }

    /// <summary>
    /// Tutorial sahnesinin küçük menüsü: oyun içi HUD (nişangah, tur satırları,
    /// **terminal ve çıkış kilidi ekranları**), duraklatma, seçenekler, ses ve
    /// tuş atamaları. `Tutorial Sahnesi Kur` çağırıyor.
    ///
    /// **Neden gerekliydi.** Terminal ve çıkış kilidi ekranları `OyunHud`'ın
    /// çocuğu olarak kuruluyor (bölüm 20) ve tutorial sahnesinde hiç menü
    /// canvas'ı yoktu: koridordaki gerçek terminale bağlanıyordun ama ekran
    /// çizilmiyordu — "terminal ekranları gözükmüyor" şikâyeti tam olarak buydu.
    /// Aynı eksik Esc'yi de açıklıyor: duraklatma menüsünü `MenuController`
    /// açıyor ve o da bu canvas'ta duruyor.
    ///
    /// **Neden `Build()` çağrılmıyor.** O yapıcı isim, ana menü, lobi, katılma
    /// ve karakter ekranlarını da kuruyor, canvas'a bir `LobbyNetwork` takıyor
    /// ve `MenuStageSetup` ile arka plan sahnesini üretiyor. Tutorial'da oda
    /// diye bir şey yok; üstelik `LobbyNetwork.TickPhase` tur bitince lobiyi
    /// açmaya çalışır ve `TutorialBootstrap`'ın çıkış akışıyla çakışırdı.
    /// Buradaki parçalar `Build()`'in kendi yapıcılarının AYNISI — ikinci bir
    /// menü kodu yazılmadı, yalnızca gereken altısı çağrıldı.
    /// </summary>
    internal static MenuController BuildTutorialMenu(TutorialBootstrap bootstrap)
    {
        // `EnsureEventSystem` KULLANILMIYOR: o metot sahne ayırmıyor
        // (`FindObjectOfType` bütün açık sahnelere bakıyor) ve tutorial aracı
        // çalışırken SampleScene de açık — oradaki EventSystem'i bulup bu
        // sahneyi olaysız bırakırdı. EventSystem'i olmayan bir canvas'ta hiçbir
        // düğme tıklanamaz ve hata da yazmaz.
        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();

        GameObject canvasObject = CreateCanvas();
        MenuController controller = canvasObject.AddComponent<MenuController>();

        // Karartma: `MenuController.ApplyBackdrop` bunu "menü açık ve tur
        // oynanmıyor" kuralıyla yönetiyor, yani tutorial'da duraklatınca
        // arkada koridor görünmeye devam ediyor. İçindeki `MenuStage` sahne
        // kökünü bulamayınca kendini sessizce kapatıyor (`MenuStage.OnEnable`),
        // o yüzden `MenuStageSetup` burada çalıştırılmıyor.
        GameObject backdrop = CreateBackdrop(canvasObject.transform);

        BuildGameHud(canvasObject.transform, withMinimap: false);

        GameObject settings = BuildSettingsPanel(canvasObject.transform, controller);
        GameObject audio = BuildAudioPanel(canvasObject.transform, controller);
        GameObject controls = BuildControlsPanel(canvasObject.transform, controller);

        // Çıkış düğmesi `TutorialBootstrap.ReturnToMenu`'ya bağlanıyor: Esc
        // artık koridordan ANINDA çıkmıyor, normal oyunda olduğu gibi
        // duraklatma menüsü açılıyor ve çıkmak bir seçenek oluyor.
        GameObject pause = BuildPausePanel(canvasObject.transform, controller,
            bootstrap != null ? (UnityEngine.Events.UnityAction)bootstrap.ReturnToMenu : null,
            "TUTORIAL'DAN ÇIK");

        BuildVoiceHud(canvasObject.transform);
        BuildScoreboard(canvasObject.transform);

        // Lobi/katılma/karakter/isim ekranları YOK; `WireController` boş
        // referansa dayanıklı (`SetActive` null kontrolü yapıyor).
        WireController(controller, null, null, settings, audio, controls, null, null, null,
            pause, backdrop);

        // Menü KAPALI başlıyor. Ana menüde `Main` açılıyor ama burada oyun
        // sahne açılır açılmaz başlıyor: ekranı bir panel kaplarsa oyuncu
        // koridoru hiç görmezdi.
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("startScreen").enumValueIndex = (int)MenuController.Screen.None;
        serialized.ApplyModifiedProperties();

        // Kurulumdan sonra hepsi açık kalırsa Scene penceresinde üst üste
        // binmiş paneller görünüyor; hangisinin açılacağına `MenuController`
        // çalışma anında karar veriyor.
        settings.SetActive(false);
        audio.SetActive(false);
        controls.SetActive(false);
        pause.SetActive(false);

        return controller;
    }

    // ---------- Ekranlar ----------

    /// <summary>İlk girişte bir kez çıkan isim ekranı.</summary>
    /// <summary>
    /// Ekranda görünen oyun adı. **İÇ ad `YAKALAMACA` DEĞİŞMİYOR** (bölüm 0):
    /// menü öğeleri, sınıf adları ve klasörler aynı kalıyor — değişen yalnızca
    /// oyuncunun gördüğü başlık.
    ///
    /// `Localization`'a girmiyor, bilerek: özel isim, iki dilde de aynı.
    ///
    /// İki ekranda birden kullanılıyor (isim girişi ve ana menü); tek sabit
    /// olması ikisinin ayrışmasını engelliyor — menü figürlerinin açısında
    /// tam olarak bu yaşanmıştı (bölüm 13).
    /// </summary>
    private const string GameTitle = "TERMINAL FIVE";

    private static GameObject BuildNameEntryPanel(Transform parent, MenuController controller)
    {
        GameObject panel = CreatePanel("Panel_Isim", parent);
        Transform column = CreateColumn(panel.transform);

        CreateTitle(column, GameTitle);
        CreateSpacer(column, 10f);

        Loc(CreateLabel(column, "Seni nasıl çağıralım?"), "Seni nasıl çağıralım?");
        TMP_InputField nameField = CreateInputField(column, PlayerProfile.DefaultName);
        nameField.characterValidation = TMP_InputField.CharacterValidation.None;
        nameField.characterLimit = PlayerProfile.MaxNameLength;

        TMP_Text hint = CreateLabel(column,
            $"Boş bırakırsan \"{PlayerProfile.DefaultName}\" olursun. Sonradan Seçenekler'den değiştirebilirsin.");
        hint.fontSize = 17f;
        hint.color = new Color(0.6f, 0.6f, 0.66f, 1f);
        Loc(hint, $"Boş bırakırsan \"{PlayerProfile.DefaultName}\" olursun. Sonradan Seçenekler'den değiştirebilirsin.");

        NameEntryPanel entry = panel.AddComponent<NameEntryPanel>();

        CreateSpacer(column, 14f);
        Loc(AddButton(column, "DEVAM", entry.Confirm, AccentColor), "DEVAM");

        SerializedObject serialized = new SerializedObject(entry);
        serialized.FindProperty("menu").objectReferenceValue = controller;
        serialized.FindProperty("nameField").objectReferenceValue = nameField;
        serialized.ApplyModifiedProperties();

        return panel;
    }

    private static GameObject BuildMainPanel(Transform parent, MenuController controller,
        LobbyNetwork network)
    {
        GameObject panel = CreatePanel("Panel_Ana", parent);
        Transform column = CreateColumn(panel.transform);

        CreateTitle(column, GameTitle);
        CreateSpacer(column, 18f);

        // "OYNA" düğmesi kalktı: ağ oyununda menüyü kapatmak oyuna girmek
        // değil, oyuncusuz bir sahneye bakmak demekti. Oynamanın tek yolu bir
        // oda kurmak ya da bir odaya katılmak.
        // LOBİ KUR bilerek DOLU DEĞİL: ana menüde "sıradaki adım" diye tek bir
        // doğru yok — oda kurmak da katılmak da eşit derecede geçerli bir
        // başlangıç. Birini vurgulamak öbürünü ikincil gösteriyordu.
        Loc(AddButton(column, "LOBİ KUR", network.HostLobby), "LOBİ KUR");
        Loc(AddButton(column, "LOBİYE KATIL", controller.ShowJoinLobby), "LOBİYE KATIL");
        Loc(AddButton(column, "KARAKTER", controller.ShowCharacters), "KARAKTER");
        Loc(AddButton(column, "NASIL OYNANIR", controller.StartTutorial), "NASIL OYNANIR");
        Loc(AddButton(column, "SEÇENEKLER", controller.ShowSettings), "SEÇENEKLER");
        Loc(AddButton(column, "ÇIKIŞ", controller.QuitGame), "ÇIKIŞ");

        CreateSpacer(column, 14f);
        TMP_Text hint = CreateLabel(column,
            "Lobi kurunca 7 harflik bir kod çıkar. Arkadaşın o kodu girerek katılır.");
        hint.fontSize = 16f;
        hint.color = new Color(0.6f, 0.6f, 0.66f, 1f);
        Loc(hint, "Lobi kurunca 7 harflik bir kod çıkar. Arkadaşın o kodu girerek katılır.");

        return panel;
    }

    private static GameObject BuildSettingsPanel(Transform parent, MenuController controller)
    {
        GameObject panel = CreatePanel("Panel_Secenekler", parent);
        Transform column = CreateColumn(panel.transform);

        Loc(CreateTitle(column, "SEÇENEKLER"), "SEÇENEKLER");
        CreateSpacer(column, 12f);

        Loc(CreateLabel(column, "Oyuncu adı"), "Oyuncu adı");
        TMP_InputField nameField = CreateInputField(column, PlayerProfile.DefaultName);
        nameField.characterValidation = TMP_InputField.CharacterValidation.None;
        nameField.characterLimit = PlayerProfile.MaxNameLength;

        CreateSpacer(column, 12f);
        TMP_Text sensitivityLabel = CreateLabel(column, "Fare hassasiyeti");
        Slider sensitivitySlider = CreateSlider(column);

        SettingsPanel settings = panel.AddComponent<SettingsPanel>();

        CreateSpacer(column, 10f);
        Button invertButton = AddButton(column, "Ters bakış: kapalı", settings.ToggleInvertLook);
        TMP_Text invertLabel = invertButton.GetComponentInChildren<TextMeshProUGUI>();

        // Dil: EN/TR arası. Kendi adını kendi dilinde yazıyor (bkz.
        // SettingsPanel.RefreshLanguage) — oyuncu henüz İngilizce
        // bilmiyorsa bile "Dil: Türkçe" satırını tanıyabilmeli.
        CreateSpacer(column, 10f);
        Button languageButton = AddButton(column, "Language: English", settings.ToggleLanguage);
        TMP_Text languageLabel = languageButton.GetComponentInChildren<TextMeshProUGUI>();

        // Korku efektleri (bölüm 25). Kaydırıcı, açma/kapama değil: gren ve
        // sarsıntı bazı oyuncuların gözünü yoruyor ama tamamen kapatmak oyunun
        // görünümünü de alıp götürüyor.
        CreateSpacer(column, 12f);
        TMP_Text horrorLabel = CreateLabel(column, "Korku efektleri: %100");
        Slider horrorSlider = CreateSlider(column);

        // Ses ve tuşlar birer ALT EKRAN. Hepsi burada dururken ekran alt alta
        // sığmıyordu; seçenekler artık kategori kapısı.
        CreateSpacer(column, 14f);
        Loc(AddButton(column, "SES", controller.ShowAudio), "SES");
        CreateSpacer(column, 10f);
        Loc(AddButton(column, "TUŞ ATAMALARI", controller.ShowControls), "TUŞ ATAMALARI");

        CreateSpacer(column, 12f);

        // ShowMain değil: seçenekler tur ortasındaki duraklatmadan da
        // açılabiliyor ve oraya dönmesi gerekiyor.
        Loc(AddButton(column, "GERİ", controller.CloseSettings), "GERİ");

        SerializedObject serialized = new SerializedObject(settings);
        serialized.FindProperty("invertLabel").objectReferenceValue = invertLabel;
        serialized.FindProperty("languageLabel").objectReferenceValue = languageLabel;
        serialized.FindProperty("sensitivitySlider").objectReferenceValue = sensitivitySlider;
        serialized.FindProperty("sensitivityLabel").objectReferenceValue = sensitivityLabel;
        serialized.FindProperty("nameField").objectReferenceValue = nameField;
        serialized.FindProperty("horrorSlider").objectReferenceValue = horrorSlider;
        serialized.FindProperty("horrorLabel").objectReferenceValue = horrorLabel;
        serialized.ApplyModifiedProperties();

        return panel;
    }

    /// <summary>
    /// Oyun içi HUD: nişangah, nişan yazısı ve tur bilgisi.
    ///
    /// Teknik borç 2'nin karşılığı — üçü de `OnGUI` ile çiziliyordu. IMGUI her
    /// zaman Canvas'ın üstünde kaldığı için menü açıldığında üzerine biniyorlar
    /// ve `MenuController` onları elle kapatmak zorunda kalıyordu.
    ///
    /// `MenuController`'ın panel listesine GİRMİYOR: ekran değiştikçe açılıp
    /// kapanmamalı, görünürlüğünü `GameHud` kendi yönetiyor.
    /// </summary>
    /// <summary>
    /// Görünürlüğü taşıyacak grubu kurar.
    ///
    /// **Neden `SetActive` değil.** Kapalı bir `GameObject` `Update`
    /// çalıştırmıyor; görünürlüğü yöneten bileşen o objenin üstündeyse kendini
    /// kapattığı anda bir daha açamıyor. Terminal ekranı tam olarak böyle
    /// kayboldu — kurulumda kapatılmıştı ve oyunda bir kez bile açılmadı.
    ///
    /// `CanvasGroup` görüntüyü kapatıyor ama objeyi ayakta bırakıyor.
    /// </summary>
    private static CanvasGroup AddVisibilityGroup(GameObject target, bool startVisible)
    {
        CanvasGroup group = target.AddComponent<CanvasGroup>();

        group.alpha = startVisible ? 1f : 0f;
        group.blocksRaycasts = startVisible;
        group.interactable = startVisible;

        return group;
    }

    /// <summary>
    /// `withMinimap` yalnızca TUTORIAL için kapatılıyor. `BuildMinimap` duvar
    /// konumlarını sahneden okuyor (`Harita`, `Harita_Genisleme_Guney`) ve
    /// `Tutorial Sahnesi Kur` çalışırken SampleScene de açık kalıyor: gerçek
    /// haritanın yüzlerce duvarı tutorial canvas'ına çizilirdi. Tutorial'ın
    /// kendi düz koridorunun mini haritası da öğretecek bir şey taşımıyor.
    /// </summary>
    private static void BuildGameHud(Transform parent, bool withMinimap = true)
    {
        GameObject root = new GameObject("OyunHud", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        Stretch(root.GetComponent<RectTransform>());

        CanvasGroup group = AddVisibilityGroup(root, true);
        GameHud hud = root.AddComponent<GameHud>();

        BuildCrosshair(root.transform);
        BuildRoundLines(root.transform);

        if (withMinimap)
            BuildMinimap(root.transform);

        // Ekranlar en sonda: nişangahın ÜSTÜNDE çizilsinler. Zaten ikisi de
        // `PlayerInteractor.InputCaptured` sırasında açılıyor ve o anda
        // nişangah gizli, ama kardeş sırası ikinci güvence.
        BuildTerminalScreen(root.transform);
        BuildExitLockScreen(root.transform);

        SerializedObject serialized = new SerializedObject(hud);
        serialized.FindProperty("group").objectReferenceValue = group;
        serialized.ApplyModifiedProperties();
    }

    /// <summary>
    /// Ekranın ortasındaki nokta ve altındaki nişan yazısı.
    ///
    /// Nokta sprite'sız bir `Image`: Unity boş sprite'ı düz beyaz kare olarak
    /// çiziyor, yani doku üretmeye gerek yok. Eski sürüm 1x1 bir `Texture2D`
    /// yaratıp saklıyordu.
    /// </summary>
    private static void BuildCrosshair(Transform parent)
    {
        GameObject root = new GameObject("Nisangah", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(320f, 80f);
        rect.anchoredPosition = Vector2.zero;

        GameObject dot = new GameObject("Nokta", typeof(RectTransform), typeof(Image));
        dot.transform.SetParent(root.transform, false);

        RectTransform dotRect = dot.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0.5f, 0.5f);
        dotRect.anchorMax = new Vector2(0.5f, 0.5f);
        dotRect.sizeDelta = new Vector2(5f, 5f);
        dotRect.anchoredPosition = Vector2.zero;

        // Yazı noktanın ALTINDA: üstüne koymak bakılan nesneyi kapatıyordu ve
        // nişan alınan yer zaten ekranın tam ortası.
        TMP_Text prompt = CreateAnchoredText(root.transform, "Yazi", string.Empty, 17f,
            TextAlignmentOptions.Top, new Vector2(0f, 0f), new Vector2(1f, 0f), 0f);

        RectTransform promptRect = prompt.rectTransform;
        promptRect.anchoredPosition = new Vector2(0f, -34f);
        promptRect.sizeDelta = new Vector2(0f, 28f);

        CanvasGroup group = AddVisibilityGroup(root, true);
        CrosshairView view = root.AddComponent<CrosshairView>();

        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("group").objectReferenceValue = group;
        serialized.FindProperty("dot").objectReferenceValue = dotRect;
        serialized.FindProperty("dotImage").objectReferenceValue = dot.GetComponent<Image>();
        serialized.FindProperty("promptLabel").objectReferenceValue = prompt;
        serialized.ApplyModifiedProperties();
    }

    /// <summary>Ekranın üstündeki üç satır: terminal sayacı, durum, alarm.</summary>
    private static void BuildRoundLines(Transform parent)
    {
        GameObject root = new GameObject("TurBilgisi", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(720f, 110f);
        rect.anchoredPosition = new Vector2(0f, -12f);

        TMP_Text terminal = CreateHudLine(root.transform, "Terminal", 24f, 0f, TextColor);
        TMP_Text status = CreateHudLine(root.transform, "Durum", 21f, -34f, TextColor);
        TMP_Text alarm = CreateHudLine(root.transform, "Alarm", 22f, -66f, AccentColor);

        alarm.fontStyle = FontStyles.Bold;

        CanvasGroup group = AddVisibilityGroup(root, true);
        RoundHudView view = root.AddComponent<RoundHudView>();

        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("group").objectReferenceValue = group;
        serialized.FindProperty("terminalLabel").objectReferenceValue = terminal;
        serialized.FindProperty("statusLabel").objectReferenceValue = status;
        serialized.FindProperty("alarmLabel").objectReferenceValue = alarm;
        serialized.ApplyModifiedProperties();
    }

    /// <summary>
    /// Sol üst köşede sabit yönlü (Pac-Man tarzı, izlenen bakışla DÖNMEYEN)
    /// kuş bakışı mini harita. Yalnızca KENDİ konumun gösteriliyor —
    /// terminal/takım arkadaşı/ceset/canavar yeri YOK, bilerek (bkz.
    /// `MiniMapView`'in sınıf yorumu — 2026-09-14'te tartışılan çok-rollü
    /// fikrin en sade hâli).
    ///
    /// Gerçek bir üstten kamera + RenderTexture KURULMUYOR, bilerek: harita
    /// tavanlı (bölüm 3, 3 m) — üstten bakan bir kamera tavanı görürdü.
    /// Onun yerine duvar bloklarının GERÇEK dünya konumları sahneden
    /// ÖLÇÜLÜP düz 2B kareler olarak çiziliyor; render maliyeti yok, tavan
    /// sorunu hiç yok.
    ///
    /// Duvar araması `MazeExpansionSetup`'ın "Duvar_3_0 isim çakışması"
    /// dersine uyuyor: `GameObject.Find` ile GLOBAL değil, bilinen bir köke
    /// (`Harita`, `Harita_Genisleme_Guney`) kapsanmış `Transform.Find`
    /// zinciriyle. Harita büyürse (yeni bir kanat gelirse) `Menü Kur`
    /// yeniden çalıştırılınca sınırlar kendiliğinden güncellenir.
    /// </summary>
    /// <summary>
    /// Mini haritayı kurar: gövde, çerçeve, kırpma penceresi, duvar kareleri
    /// ve ortadaki ok.
    ///
    /// Duvarlar **bir kez** yerleştiriliyor ve çalışma anında hiç
    /// kıpırdamıyor; `MiniMapView` yalnızca onları taşıyan kabı döndürüp
    /// kaydırıyor.
    /// </summary>
    private static void BuildMinimap(Transform parent)
    {
        List<Vector3> wallPositions = new List<Vector3>();
        CollectWallPositions("Harita", wallPositions);
        CollectWallPositions("Harita_Genisleme_Guney", wallPositions);

        GameObject panel = new GameObject("MiniHarita", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(MinimapMargin, -MinimapMargin);
        panelRect.sizeDelta = new Vector2(MinimapSize, MinimapSize);

        Image background = panel.GetComponent<Image>();
        background.color = MinimapBackgroundColor;
        background.raycastTarget = false;

        // KENDİ Canvas'ı, bilerek. Harita kabı her karede dönüyor ve bir
        // RectTransform'u oynatmak bulunduğu Canvas'ın TAMAMINI yeniden
        // gruplatıyor. Bu canvas menünün ve HUD'ın hepsini taşıyor; iç içe
        // bir Canvas mini haritayı ayırıyor, yani her kare yeniden gruplanan
        // şey yalnızca duvar kareleri oluyor.
        //
        // `GraphicRaycaster` GEREKMİYOR: mini haritadaki hiçbir grafik
        // tıklanabilir değil (`raycastTarget = false`).
        Canvas nested = panel.AddComponent<Canvas>();
        nested.additionalShaderChannels = AdditionalCanvasShaderChannels.None;

        AddFrame(panel.transform, AccentDim, 1.5f, 10f);

        // Kırpma penceresi. `RectMask2D` seçildi, `Mask` değil: maske
        // sprite'ı gerektirmiyor ve fazladan çizim çağrısı açmıyor.
        GameObject windowObject = new GameObject("Pencere", typeof(RectTransform), typeof(RectMask2D));
        windowObject.transform.SetParent(panel.transform, false);

        RectTransform window = windowObject.GetComponent<RectTransform>();
        window.anchorMin = new Vector2(0.5f, 0.5f);
        window.anchorMax = new Vector2(0.5f, 0.5f);
        window.pivot = new Vector2(0.5f, 0.5f);
        window.anchoredPosition = Vector2.zero;
        window.sizeDelta = new Vector2(MinimapSize - MinimapPadding * 2f,
            MinimapSize - MinimapPadding * 2f);

        // Harita kabı NOKTA boyutunda (sizeDelta sıfır): dönüş kendi
        // pivotunda olsun ve haritanın toplam boyunu burada hesaplamak
        // gerekmesin diye.
        GameObject mapRootObject = new GameObject("Harita", typeof(RectTransform));
        mapRootObject.transform.SetParent(window, false);

        RectTransform mapRoot = mapRootObject.GetComponent<RectTransform>();
        mapRoot.anchorMin = new Vector2(0.5f, 0.5f);
        mapRoot.anchorMax = new Vector2(0.5f, 0.5f);
        mapRoot.pivot = new Vector2(0.5f, 0.5f);
        mapRoot.sizeDelta = Vector2.zero;
        mapRoot.anchoredPosition = Vector2.zero;

        float pixelsPerMeter = (MinimapSize - MinimapPadding * 2f) / MinimapMetersAcross;

        // Başlangıç noktası haritanın ORTASI: harita uzayındaki sayılar
        // küçük kalıyor ve float hassasiyeti hiç sorun olmuyor.
        ComputeWallBounds(wallPositions, out Vector2 worldMin, out Vector2 worldMax);
        Vector2 worldOrigin = (worldMin + worldMax) * 0.5f;

        // Kare tam bir hücre kadar: bitişik duvarlar tek bir kütle hâlinde
        // birleşiyor ve koridorlar boşluk olarak okunuyor.
        float tileSize = MazeMapBuilder.CellSize * pixelsPerMeter;

        foreach (Vector3 wallPosition in wallPositions)
            CreateMinimapWallTile(mapRoot, wallPosition, worldOrigin, pixelsPerMeter, tileSize);

        RectTransform selfMarker = CreateMinimapSelfMarker(window, out Image selfMarkerImage);

        MiniMapView view = panel.AddComponent<MiniMapView>();
        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("mapRoot").objectReferenceValue = mapRoot;
        serialized.FindProperty("selfMarker").objectReferenceValue = selfMarker;
        serialized.FindProperty("selfMarkerImage").objectReferenceValue = selfMarkerImage;
        serialized.FindProperty("worldOrigin").vector2Value = worldOrigin;
        serialized.FindProperty("pixelsPerMeter").floatValue = pixelsPerMeter;
        serialized.ApplyModifiedProperties();

        if (wallPositions.Count == 0)
        {
            Debug.LogWarning("Mini Harita: hiç duvar bulunamadı — 'Harita' ya da " +
                "'Harita_Genisleme_Guney' altında 'Duvarlar' grubu yok. Harita henüz " +
                "kurulmadıysa önce Labirent Harita Kur, sonra Menü Kur'u tekrar çalıştır.");
        }
    }

    /// <summary>
    /// `rootName` altındaki `Duvarlar` grubunun çocuklarını tarar. Kapsam
    /// bilinen bir köke bağlı — `GameObject.Find("Duvar_...")` gibi GLOBAL
    /// bir arama yapmıyoruz, çünkü kanadın kendi duvarları da aynı adlandırma
    /// şemasını (`Duvar_X_Z`) paylaşıyor (bölüm 0.1'in isim çakışması dersi).
    /// Burada risk yok: ikisinden de TÜM duvarları istiyoruz, ama yine de
    /// doğru köke kapsamak alışkanlığı bozmuyoruz.
    /// </summary>
    private static void CollectWallPositions(string rootName, List<Vector3> positions)
    {
        GameObject root = GameObject.Find(rootName);
        if (root == null)
            return;

        Transform walls = root.transform.Find("Duvarlar");
        if (walls == null)
            return;

        foreach (Transform wall in walls)
        {
            if (wall.name.StartsWith("Duvar_"))
                positions.Add(wall.position);
        }
    }

    /// <summary>Bütün duvarları kapsayan sınır kutusu, yarım hücre payla.</summary>
    private static void ComputeWallBounds(List<Vector3> positions, out Vector2 min, out Vector2 max)
    {
        if (positions.Count == 0)
        {
            min = new Vector2(-10f, -10f);
            max = new Vector2(10f, 10f);
            return;
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (Vector3 position in positions)
        {
            minX = Mathf.Min(minX, position.x);
            maxX = Mathf.Max(maxX, position.x);
            minZ = Mathf.Min(minZ, position.z);
            maxZ = Mathf.Max(maxZ, position.z);
        }

        float pad = MazeMapBuilder.CellSize * 0.5f;
        min = new Vector2(minX - pad, minZ - pad);
        max = new Vector2(maxX + pad, maxZ + pad);
    }

    private static void CreateMinimapWallTile(RectTransform mapRoot, Vector3 worldPosition,
        Vector2 worldOrigin, float pixelsPerMeter, float tileSize)
    {
        GameObject tile = new GameObject("Duvar", typeof(RectTransform), typeof(Image));
        tile.transform.SetParent(mapRoot, false);

        Image image = tile.GetComponent<Image>();
        image.color = MinimapWallColor;
        image.raycastTarget = false;

        RectTransform rect = tile.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(tileSize, tileSize);
        rect.anchoredPosition = MiniMapView.WorldToMapPoint(worldPosition, worldOrigin, pixelsPerMeter);
    }

    /// <summary>
    /// Pencerenin ortasındaki ok. Harita baktığın yöne göre döndüğü için ok
    /// hiç DÖNMÜYOR — yukarısı zaten ilerisi.
    ///
    /// Nokta yerine ok, çünkü dönen bir haritada asıl okunması gereken şey
    /// yön; yuvarlak bir nokta onu hiç söylemiyor.
    /// </summary>
    private static RectTransform CreateMinimapSelfMarker(RectTransform window, out Image markerImage)
    {
        GameObject marker = new GameObject("Ben", typeof(RectTransform), typeof(Image));
        marker.transform.SetParent(window, false);

        markerImage = marker.GetComponent<Image>();
        markerImage.color = MinimapRunnerColor;
        markerImage.raycastTarget = false;
        markerImage.sprite = GetOrCreateArrowSprite();
        markerImage.preserveAspect = true;

        RectTransform rect = marker.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(MinimapPlayerDotSize, MinimapPlayerDotSize);
        rect.anchoredPosition = Vector2.zero;

        return rect;
    }

    /// <summary>
    /// Ok sprite'ını üretir ve varlık olarak saklar.
    ///
    /// **Neden prosedürel:** projede hazır bir ok dokusu yok ve TMP'nin
    /// üçgen karakterine güvenmek riskli — varsayılan atlas yalnızca temel
    /// Latin kapsıyor ve eksik karakter boş kutuya dönüyor (bölüm 20'nin
    /// kendi dersi). Sopa mesh'i ve materyali de aynı alışkanlıkla
    /// üretiliyor (`MonsterBatBuilder`).
    ///
    /// Kenarlar yumuşatılıyor: 16 piksellik bir üçgende basamaklar aksi
    /// hâlde açıkça görünüyor.
    /// </summary>
    private static Sprite GetOrCreateArrowSprite()
    {
        const string folder = "Assets/_Art/UI";
        const string path = folder + "/MiniHarita_Ok.png";

        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/_Art", "UI");

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Tepesi yukarıda bir üçgen; tabanın ortasındaki çentik oku
                // "uçak" gibi gösterip yönü daha okunur kılıyor.
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;

                float halfWidth = Mathf.Lerp(0.5f, 0.02f, Mathf.InverseLerp(0.08f, 0.96f, v));
                float distance = Mathf.Abs(u - 0.5f);

                float inside = Mathf.InverseLerp(halfWidth, halfWidth - 0.035f, distance);
                inside *= Mathf.InverseLerp(0.04f, 0.10f, v);

                float notch = Mathf.InverseLerp(0.30f, 0.10f, v) *
                    Mathf.InverseLerp(0.20f, 0f, distance);
                inside *= 1f - Mathf.Clamp01(notch);

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(inside)));
            }
        }

        texture.Apply();
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>
    /// Terminal ekranı: koyu gövde, ince çerçeve, ortada büyük yazı ve çubuk.
    ///
    /// Tek panel beş terminale hizmet ediyor — hareket kilitli olduğu için aynı
    /// anda yalnızca birine bağlanılabiliyor (bkz. `TerminalScreen`).
    /// </summary>
    private static void BuildTerminalScreen(Transform parent)
    {
        GameObject root = CreateScreenBox("Panel_TerminalEkrani", parent,
            new Vector2(380f, 200f), new Color(0.02f, 0.05f, 0.03f, 0.88f),
            out Graphic[] borders);

        TMP_Text header = CreateScreenText(root.transform, "Baslik", 15f,
            TextAlignmentOptions.Left, 14f, -12f, 20f);

        TMP_Text exit = CreateScreenText(root.transform, "Cikis", 13f,
            TextAlignmentOptions.Right, 14f, -12f, 20f);

        TMP_Text big = CreateScreenText(root.transform, "Buyuk", 34f,
            TextAlignmentOptions.Center, 14f, -40f, 46f);

        GameObject bar = CreateScreenBar(root.transform, "Cubuk", -92f, 12f, 14f,
            out RectTransform barFill, out Graphic barFillGraphic, out Graphic barBackGraphic);

        TMP_Text caption = CreateScreenText(root.transform, "Alt", 14f,
            TextAlignmentOptions.Center, 14f, -110f, 22f);

        TMP_Text prompt = CreateScreenText(root.transform, "Sinav", 26f,
            TextAlignmentOptions.Center, 14f, -134f, 34f);

        GameObject promptBar = CreateScreenBar(root.transform, "SinavCubugu", -174f, 6f, 100f,
            out RectTransform promptFill, out Graphic promptFillGraphic,
            out Graphic promptBackGraphic);

        CanvasGroup group = AddVisibilityGroup(root, false);
        TerminalScreen screen = root.AddComponent<TerminalScreen>();

        SerializedObject serialized = new SerializedObject(screen);
        serialized.FindProperty("group").objectReferenceValue = group;
        serialized.FindProperty("headerLabel").objectReferenceValue = header;
        serialized.FindProperty("exitLabel").objectReferenceValue = exit;
        serialized.FindProperty("bigLabel").objectReferenceValue = big;
        serialized.FindProperty("captionLabel").objectReferenceValue = caption;
        serialized.FindProperty("promptLabel").objectReferenceValue = prompt;
        serialized.FindProperty("bar").objectReferenceValue = bar;
        serialized.FindProperty("barFill").objectReferenceValue = barFill;
        serialized.FindProperty("barFillGraphic").objectReferenceValue = barFillGraphic;
        serialized.FindProperty("barBackGraphic").objectReferenceValue = barBackGraphic;
        serialized.FindProperty("promptBar").objectReferenceValue = promptBar;
        serialized.FindProperty("promptBarFill").objectReferenceValue = promptFill;
        serialized.FindProperty("promptBarFillGraphic").objectReferenceValue = promptFillGraphic;
        serialized.FindProperty("promptBarBackGraphic").objectReferenceValue = promptBackGraphic;

        SerializedProperty borderArray = serialized.FindProperty("borders");
        borderArray.arraySize = borders.Length;

        for (int i = 0; i < borders.Length; i++)
            borderArray.GetArrayElementAtIndex(i).objectReferenceValue = borders[i];

        serialized.ApplyModifiedProperties();
    }

    /// <summary>
    /// Çıkış kilidi paneli: başlık şeridi, on hücre, ilerleme şeridi.
    ///
    /// Hücreler önceden kuruluyor ve sonra yalnızca renkleriyle yazıları
    /// değişiyor — çalışma anında obje yaratmak bölüm 2'nin havuzlama kuralına
    /// takılırdı, üstelik dizilim uzunluğu sabit.
    /// </summary>
    private static void BuildExitLockScreen(Transform parent)
    {
        GameObject root = CreateScreenBox("Panel_KilitEkrani", parent,
            new Vector2(520f, 200f), new Color(0.045f, 0.042f, 0.028f, 0.94f),
            out Graphic[] borders);

        Color accent = new Color(0.95f, 0.8f, 0.15f);
        Color accentDim = new Color(0.52f, 0.43f, 0.10f);

        for (int i = 0; i < borders.Length; i++)
            borders[i].color = accentDim;

        TMP_Text title = CreateScreenText(root.transform, "Baslik", 13f,
            TextAlignmentOptions.Center, 16f, -14f, 22f);

        title.SetText("Ç I K I Ş   K İ L İ D İ");
        title.color = accent;
        Loc(title, "Ç I K I Ş   K İ L İ D İ");

        const int count = ExitLock.SequenceLength;
        ExitLockScreen.Cell[] cells = new ExitLockScreen.Cell[count];

        for (int i = 0; i < count; i++)
            cells[i] = CreateLockCell(root.transform, i, count);

        GameObject bar = CreateScreenBar(root.transform, "Cubuk", -134f, 4f, 16f,
            out RectTransform barFill, out Graphic barFillGraphic, out Graphic barBackGraphic);

        barFillGraphic.color = accent;
        barBackGraphic.color = new Color(0.12f, 0.11f, 0.06f);

        TMP_Text caption = CreateScreenText(root.transform, "Alt", 11f,
            TextAlignmentOptions.Center, 16f, -150f, 20f);

        CanvasGroup group = AddVisibilityGroup(root, false);
        ExitLockScreen screen = root.AddComponent<ExitLockScreen>();

        SerializedObject serialized = new SerializedObject(screen);
        serialized.FindProperty("group").objectReferenceValue = group;
        serialized.FindProperty("barFill").objectReferenceValue = barFill;
        serialized.FindProperty("captionLabel").objectReferenceValue = caption;

        SerializedProperty cellArray = serialized.FindProperty("cells");
        cellArray.arraySize = count;

        for (int i = 0; i < count; i++)
        {
            SerializedProperty element = cellArray.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("background").objectReferenceValue = cells[i].background;
            element.FindPropertyRelative("border").objectReferenceValue = cells[i].border;
            element.FindPropertyRelative("label").objectReferenceValue = cells[i].label;
        }

        serialized.ApplyModifiedProperties();
    }

    /// <summary>
    /// Kilit dizilimindeki tek hücre.
    ///
    /// Genişlik YÜZDEYLE veriliyor: panel ölçeklenince hücreler de birlikte
    /// ölçekleniyor ve sabit piksel genişliği on hücrede taşmıyor.
    /// </summary>
    private static ExitLockScreen.Cell CreateLockCell(Transform parent, int index, int count)
    {
        GameObject cell = new GameObject($"Hucre_{index + 1}", typeof(RectTransform), typeof(Image));
        cell.transform.SetParent(parent, false);

        float step = 1f / count;
        const float inset = 1.5f;

        RectTransform rect = cell.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(step * index, 1f);
        rect.anchorMax = new Vector2(step * (index + 1), 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(inset, -126f);
        rect.offsetMax = new Vector2(-inset, -48f);

        // Çerçeve AYRI bir Image: arka planla aynı objede olamaz, ikisi de renk
        // taşıyor ve sıradaki hücrede çerçevenin kapanması gerekiyor.
        GameObject border = CreateStretchedImage("Cerceve", cell.transform, Color.white);

        TMP_Text label = CreateAnchoredText(cell.transform, "Yazi", string.Empty, 24f,
            TextAlignmentOptions.Center, Vector2.zero, Vector2.one, 0f);

        // Yazı çerçevenin üstünde kalmalı; kardeş sırası bunu belirliyor.
        label.transform.SetAsLastSibling();

        return new ExitLockScreen.Cell
        {
            background = cell.GetComponent<Image>(),
            border = border.GetComponent<Image>(),
            label = label
        };
    }

    /// <summary>Ekranın ortasında duran koyu gövde + dört kenarlık.</summary>
    private static GameObject CreateScreenBox(string name, Transform parent, Vector2 size,
        Color back, out Graphic[] borders)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image));
        root.transform.SetParent(parent, false);
        root.GetComponent<Image>().color = back;

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        borders = new Graphic[4];
        borders[0] = CreateEdge(root.transform, "Ust", new Vector2(0f, 1f), new Vector2(1f, 1f), 2f);
        borders[1] = CreateEdge(root.transform, "Alt", new Vector2(0f, 0f), new Vector2(1f, 0f), 2f);
        borders[2] = CreateEdge(root.transform, "Sol", new Vector2(0f, 0f), new Vector2(0f, 1f), 2f);
        borders[3] = CreateEdge(root.transform, "Sag", new Vector2(1f, 0f), new Vector2(1f, 1f), 2f);

        return root;
    }

    /// <summary>
    /// Bir kutuya ince çerçeve + köşe ayraçları takar (bölüm 18'deki desen).
    ///
    /// Çerçevenin tamamını kalınlaştırmak kutuyu ağırlaştırıyor; vurgu
    /// köşelerde toplanınca hem oturaklı hem hafif duruyor.
    ///
    /// **Hepsi `ignoreLayout`**: kutunun kendisi bir `VerticalLayoutGroup`
    /// olabiliyor ve öyleyse kenarlar birer satır sanılıp içeriğin arasına
    /// dizilirdi.
    /// </summary>
    private static void AddFrame(Transform parent, Color color, float thickness, float bracket)
    {
        Graphic[] edges =
        {
            CreateEdge(parent, "Kenar_Ust", new Vector2(0f, 1f), new Vector2(1f, 1f), thickness),
            CreateEdge(parent, "Kenar_Alt", new Vector2(0f, 0f), new Vector2(1f, 0f), thickness),
            CreateEdge(parent, "Kenar_Sol", new Vector2(0f, 0f), new Vector2(0f, 1f), thickness),
            CreateEdge(parent, "Kenar_Sag", new Vector2(1f, 0f), new Vector2(1f, 1f), thickness),
        };

        foreach (Graphic edge in edges)
        {
            edge.color = color;
            edge.raycastTarget = false;
            IgnoreLayout(edge.gameObject);
        }

        if (bracket <= 0f)
            return;

        float thick = thickness * 2f;

        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = new Vector2(i % 2, i / 2);
            CreateCornerBar(parent, "Ayrac_Yatay", corner, new Vector2(bracket, thick), color);
            CreateCornerBar(parent, "Ayrac_Dikey", corner, new Vector2(thick, bracket), color);
        }
    }

    /// <summary>Köşeye oturan kısa çubuk; pivot da köşede olduğu için tam kenara yapışıyor.</summary>
    private static void CreateCornerBar(Transform parent, string name, Vector2 corner,
        Vector2 size, Color color)
    {
        GameObject bar = new GameObject(name, typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);

        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.anchorMin = corner;
        rect.anchorMax = corner;
        rect.pivot = corner;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        Image image = bar.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        IgnoreLayout(bar);
    }

    private static void IgnoreLayout(GameObject target)
    {
        LayoutElement element = target.GetComponent<LayoutElement>()
            ?? target.AddComponent<LayoutElement>();
        element.ignoreLayout = true;
    }

    /// <summary>Başlığın altındaki ince ayraç çizgisi.</summary>
    private static void CreateRule(Transform parent)
    {
        GameObject rule = new GameObject("Ayrac", typeof(RectTransform), typeof(Image));
        rule.transform.SetParent(parent, false);

        Image image = rule.GetComponent<Image>();
        image.color = AccentDim;
        image.raycastTarget = false;

        SetPreferredHeight(rule, 2f);
    }

    private static Graphic CreateEdge(Transform parent, string name, Vector2 min, Vector2 max,
        float thickness)
    {
        GameObject edge = new GameObject(name, typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(parent, false);

        RectTransform rect = edge.GetComponent<RectTransform>();
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // Yatay kenar yükseklik alıyor, dikey kenar genişlik.
        rect.sizeDelta = Mathf.Approximately(min.y, max.y)
            ? new Vector2(0f, thickness)
            : new Vector2(thickness, 0f);

        return edge.GetComponent<Image>();
    }

    /// <summary>Panelin içinde üstten hizalı bir yazı satırı.</summary>
    private static TMP_Text CreateScreenText(Transform parent, string name, float fontSize,
        TextAlignmentOptions alignment, float sideInset, float top, float height)
    {
        TMP_Text label = CreateAnchoredText(parent, name, string.Empty, fontSize,
            alignment, new Vector2(0f, 1f), new Vector2(1f, 1f), 0f);

        RectTransform rect = label.rectTransform;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(sideInset, top - height);
        rect.offsetMax = new Vector2(-sideInset, top);

        return label;
    }

    /// <summary>Arka plan + soldan dolan bir çubuk.</summary>
    private static GameObject CreateScreenBar(Transform parent, string name, float top,
        float height, float sideInset, out RectTransform fill, out Graphic fillGraphic,
        out Graphic backGraphic)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image));
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(sideInset, top - height);
        rect.offsetMax = new Vector2(-sideInset, top);

        backGraphic = root.GetComponent<Image>();

        GameObject fillObject = new GameObject("Dolgu", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(root.transform, false);

        fill = fillObject.GetComponent<RectTransform>();
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;

        fillGraphic = fillObject.GetComponent<Image>();

        return root;
    }

    private static TMP_Text CreateHudLine(Transform parent, string name, float fontSize,
        float y, Color color)
    {
        TMP_Text label = CreateAnchoredText(parent, name, string.Empty, fontSize,
            TextAlignmentOptions.Top, new Vector2(0f, 1f), new Vector2(1f, 1f), 0f);

        RectTransform rect = label.rectTransform;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(0f, 30f);

        label.color = color;

        return label;
    }

    /// <summary>
    /// Ses ekranı: genel ses ve sesli sohbetin tamamı.
    ///
    /// Seçeneklerden ayrıldı çünkü hepsi bir aradayken ekran alt alta
    /// sığmıyordu. Durum düğmeleri açılır liste yerine döngü: menü kodla
    /// kuruluyor ve dropdown çok daha fazla parça demek.
    /// </summary>
    private static GameObject BuildAudioPanel(Transform parent, MenuController controller)
    {
        GameObject panel = CreatePanel("Panel_Ses", parent);
        Transform column = CreateColumn(panel.transform, 560f);

        Loc(CreateTitle(column, "SES"), "SES");
        CreateSpacer(column, 10f);

        AudioPanel audio = panel.AddComponent<AudioPanel>();

        TMP_Text volumeLabel = CreateLabel(column, "Ses");
        Slider volumeSlider = CreateSlider(column);

        CreateSpacer(column, 14f);
        Loc(CreateLabel(column, "SESLİ SOHBET"), "SESLİ SOHBET").color = AccentColor;

        Button enabledButton = AddButton(column, "Sesli sohbet: AÇIK", audio.ToggleVoiceEnabled);
        TMP_Text enabledLabel = enabledButton.GetComponentInChildren<TextMeshProUGUI>();

        Button modeButton = AddButton(column, "Konuşma: BAS-KONUŞ", audio.ToggleVoiceMode);
        TMP_Text modeLabel = modeButton.GetComponentInChildren<TextMeshProUGUI>();

        Button deviceButton = AddButton(column, "Mikrofon: —", audio.CycleVoiceDevice);
        TMP_Text deviceLabel = deviceButton.GetComponentInChildren<TextMeshProUGUI>();

        // Cihaz adı uzun olabiliyor ("Microphone (High Definition Audio
        // Device)") ve düğmeyi iki satıra taşırıyordu. AudioPanel adı zaten
        // kısaltıyor; bu ikisi kalanı da tek satırda tutuyor.
        deviceLabel.enableWordWrapping = false;
        deviceLabel.overflowMode = TextOverflowModes.Ellipsis;

        TMP_Text micGainLabel = CreateLabel(column, "Mikrofon kazancı");
        Slider micGainSlider = CreateSlider(column);

        TMP_Text thresholdLabel = CreateLabel(column, "Konuşma eşiği");
        Slider thresholdSlider = CreateSlider(column);

        TMP_Text voiceVolumeLabel = CreateLabel(column, "Konuşma sesi");
        Slider voiceVolumeSlider = CreateSlider(column);

        CreateSpacer(column, 12f);
        Loc(AddButton(column, "GERİ", controller.CloseAudio), "GERİ");

        SerializedObject serialized = new SerializedObject(audio);
        serialized.FindProperty("volumeSlider").objectReferenceValue = volumeSlider;
        serialized.FindProperty("volumeLabel").objectReferenceValue = volumeLabel;
        serialized.FindProperty("voiceEnabledLabel").objectReferenceValue = enabledLabel;
        serialized.FindProperty("voiceModeLabel").objectReferenceValue = modeLabel;
        serialized.FindProperty("voiceDeviceLabel").objectReferenceValue = deviceLabel;
        serialized.FindProperty("micGainSlider").objectReferenceValue = micGainSlider;
        serialized.FindProperty("micGainLabel").objectReferenceValue = micGainLabel;
        serialized.FindProperty("thresholdSlider").objectReferenceValue = thresholdSlider;
        serialized.FindProperty("thresholdLabel").objectReferenceValue = thresholdLabel;
        serialized.FindProperty("voiceVolumeSlider").objectReferenceValue = voiceVolumeSlider;
        serialized.FindProperty("voiceVolumeLabel").objectReferenceValue = voiceVolumeLabel;
        serialized.ApplyModifiedProperties();

        return panel;
    }

    /// <summary>
    /// Sağ üst köşedeki mikrofon göstergesi: simge + seviye çubuğu + eşik
    /// çizgisi + tuş ipucu.
    ///
    /// **Simge çizgilerle kuruluyor, harfle değil.** Varsayılan TMP fontu
    /// (LiberationSans) yalnızca temel Latin kapsıyor; mikrofon emojisi ya da
    /// "🎤" gibi bir karakter boş kutuya dönüşürdü — lobi etiketlerinde bir kez
    /// yaşandı. Üç dikdörtgen (gövde, sap, taban) küçük boyutta mikrofon olarak
    /// okunuyor ve fonttan bağımsız.
    /// </summary>
    private static void BuildVoiceHud(Transform parent)
    {
        GameObject root = new GameObject("MikrofonGostergesi", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -24f);
        rect.sizeDelta = new Vector2(190f, 34f);

        // --- Mikrofon simgesi: sağda, üç dikdörtgen ---
        GameObject icon = new GameObject("Simge", typeof(RectTransform));
        icon.transform.SetParent(root.transform, false);

        RectTransform iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(1f, 0f);
        iconRect.anchorMax = new Vector2(1f, 1f);
        iconRect.pivot = new Vector2(1f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(22f, 0f);

        Image body = CreatePart(icon.transform, "Govde",
            new Vector2(0.5f, 1f), new Vector2(10f, 16f), new Vector2(0f, -3f));

        Image stand = CreatePart(icon.transform, "Sap",
            new Vector2(0.5f, 0f), new Vector2(3f, 7f), new Vector2(0f, 7f));

        Image micBase = CreatePart(icon.transform, "Taban",
            new Vector2(0.5f, 0f), new Vector2(14f, 3f), new Vector2(0f, 5f));

        // --- Seviye çubuğu: simgenin solunda ---
        GameObject bar = new GameObject("Cubuk", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(root.transform, false);
        bar.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.13f, 0.8f);

        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 0.5f);
        barRect.anchorMax = new Vector2(1f, 0.5f);
        barRect.pivot = new Vector2(0f, 0.5f);
        barRect.offsetMin = new Vector2(0f, -6f);
        barRect.offsetMax = new Vector2(-30f, 6f);

        GameObject fill = new GameObject("Dolgu", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bar.transform, false);

        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        // Eşik çizgisi: çubuğun üstünde ince bir dikey şerit. Yeri
        // VoiceHud'dan sürülüyor, çünkü eşik çalışma anında değişiyor.
        GameObject mark = new GameObject("Esik", typeof(RectTransform), typeof(Image));
        mark.transform.SetParent(bar.transform, false);
        mark.GetComponent<Image>().color = new Color(0.95f, 0.9f, 0.5f, 0.9f);

        RectTransform markRect = mark.GetComponent<RectTransform>();
        markRect.anchorMin = new Vector2(0f, 0f);
        markRect.anchorMax = new Vector2(0f, 1f);
        markRect.pivot = new Vector2(0.5f, 0.5f);
        markRect.sizeDelta = new Vector2(2f, 4f);

        // Tuş ipucu: çubuğun altında, bas-konuş tuşunu yazıyor.
        TMP_Text hint = CreateAnchoredText(root.transform, "Ipucu", "V", 13f,
            TextAlignmentOptions.Right, new Vector2(0f, -0.9f), new Vector2(1f, 0f), 30f);

        CanvasGroup group = AddVisibilityGroup(root, true);
        VoiceHud hud = root.AddComponent<VoiceHud>();

        SerializedObject serialized = new SerializedObject(hud);
        serialized.FindProperty("group").objectReferenceValue = group;
        serialized.FindProperty("levelFill").objectReferenceValue = fillRect;
        serialized.FindProperty("levelFillGraphic").objectReferenceValue = fill.GetComponent<Image>();
        serialized.FindProperty("thresholdMark").objectReferenceValue = markRect;
        serialized.FindProperty("hintLabel").objectReferenceValue = hint;

        SerializedProperty parts = serialized.FindProperty("micParts");
        parts.arraySize = 3;
        parts.GetArrayElementAtIndex(0).objectReferenceValue = body;
        parts.GetArrayElementAtIndex(1).objectReferenceValue = stand;
        parts.GetArrayElementAtIndex(2).objectReferenceValue = micBase;

        serialized.ApplyModifiedProperties();
    }

    /// <summary>Mikrofon simgesinin tek parçası — sabit boyutlu bir dikdörtgen.</summary>
    private static Image CreatePart(Transform parent, string name, Vector2 pivot,
        Vector2 size, Vector2 position)
    {
        GameObject part = new GameObject(name, typeof(RectTransform), typeof(Image));
        part.transform.SetParent(parent, false);

        RectTransform rect = part.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, pivot.y);
        rect.anchorMax = new Vector2(0.5f, pivot.y);
        rect.pivot = new Vector2(0.5f, pivot.y);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        return part.GetComponent<Image>();
    }

    /// <summary>
    /// TAB paneli: kadro, ping ve kişi bazlı ses ayarı.
    ///
    /// Satır sayısı sabit (`LobbyRoster.MaxPlayers`): menü kodla üretiliyor ve
    /// çalışma anında obje yaratmak bölüm 2'nin havuzlama kuralına takılırdı.
    /// </summary>
    private static void BuildScoreboard(Transform parent)
    {
        // Tam ekran panel DEĞİL, ortada bir kutu. Panel açıkken yürümeye
        // devam edilebiliyor (bölüm 19) ve ekranı komple kapatmak o özelliği
        // anlamsız kılardı: yürüyebilirsin ama göremezsin.
        GameObject panel = new GameObject("Panel_Oyuncular", typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        Stretch(panel.GetComponent<RectTransform>());

        GameObject box = new GameObject("Kutu", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(panel.transform, false);

        // Yarı saydam: arkadaki koridor seçiliyor ama liste okunuyor.
        box.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.86f);

        RectTransform boxRect = box.GetComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0.5f, 0.5f);
        boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.sizeDelta = new Vector2(860f, 430f);
        boxRect.anchoredPosition = Vector2.zero;

        Transform column = CreateColumn(box.transform, 760f, framed: false);

        Loc(CreateTitle(column, "OYUNCULAR"), "OYUNCULAR").fontSize = 38f;
        CreateSpacer(column, 8f);

        CanvasGroup group = AddVisibilityGroup(panel, false);
        ScoreboardPanel board = panel.AddComponent<ScoreboardPanel>();

        ScoreboardPanel.Row[] rows = new ScoreboardPanel.Row[LobbyRoster.MaxPlayers];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = CreateScoreRow(column, board, i);

        CreateSpacer(column, 10f);
        TMP_Text hint = CreateLabel(column, string.Empty);
        hint.fontSize = 16f;
        hint.color = new Color(0.66f, 0.66f, 0.72f, 1f);

        SerializedObject serialized = new SerializedObject(board);
        serialized.FindProperty("group").objectReferenceValue = group;
        serialized.FindProperty("hintLabel").objectReferenceValue = hint;

        SerializedProperty array = serialized.FindProperty("rows");
        array.arraySize = rows.Length;

        for (int i = 0; i < rows.Length; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("root").objectReferenceValue = rows[i].root;
            element.FindPropertyRelative("background").objectReferenceValue = rows[i].background;
            element.FindPropertyRelative("nameLabel").objectReferenceValue = rows[i].nameLabel;
            element.FindPropertyRelative("pingLabel").objectReferenceValue = rows[i].pingLabel;
            element.FindPropertyRelative("muteButton").objectReferenceValue = rows[i].muteButton;
            element.FindPropertyRelative("muteLabel").objectReferenceValue = rows[i].muteLabel;
            element.FindPropertyRelative("volumeSlider").objectReferenceValue = rows[i].volumeSlider;
            element.FindPropertyRelative("noteLabel").objectReferenceValue = rows[i].noteLabel;
        }

        serialized.ApplyModifiedProperties();
    }

    /// <summary>
    /// Skor tablosunun tek satırı: ad · ping · ses kaydırıcısı · susturma.
    ///
    /// Susturma düğmesinin indeksi kalıcı dinleyiciyle taşınıyor (tuş atama
    /// satırlarıyla aynı kalıp); kaydırıcı ise çalışma anında bağlanıyor,
    /// çünkü `UnityEventTools`'un float+int taşıyan bir aşırı yüklemesi yok.
    /// </summary>
    private static ScoreboardPanel.Row CreateScoreRow(Transform parent, ScoreboardPanel target,
        int index)
    {
        GameObject row = new GameObject($"Satir_{index + 1}", typeof(RectTransform), typeof(Image));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 1f);

        TMP_Text nameLabel = CreateAnchoredText(row.transform, "Ad", "—", 20f,
            TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0.42f, 1f), 14f);

        TMP_Text pingLabel = CreateAnchoredText(row.transform, "Ping", "—", 18f,
            TextAlignmentOptions.Left, new Vector2(0.42f, 0f), new Vector2(0.58f, 1f), 6f);

        // Kaydırıcı satırın içinde: kendi RectTransform'unu elle konumluyoruz,
        // CreateSlider dikey yerleşim içindir.
        Slider volume = CreateSlider(row.transform);
        RectTransform volumeRect = volume.GetComponent<RectTransform>();
        volumeRect.anchorMin = new Vector2(0.58f, 0.28f);
        volumeRect.anchorMax = new Vector2(0.80f, 0.72f);
        volumeRect.offsetMin = Vector2.zero;
        volumeRect.offsetMax = Vector2.zero;

        LayoutElement volumeLayout = volume.GetComponent<LayoutElement>();
        if (volumeLayout != null)
            Object.DestroyImmediate(volumeLayout);

        Button mute = AddButton(row.transform, "SUSTUR", null);
        UnityEventTools.AddIntPersistentListener(mute.onClick, target.ToggleMute, index);

        RectTransform muteRect = mute.GetComponent<RectTransform>();
        muteRect.anchorMin = new Vector2(0.82f, 0.15f);
        muteRect.anchorMax = new Vector2(0.99f, 0.85f);
        muteRect.offsetMin = Vector2.zero;
        muteRect.offsetMax = Vector2.zero;

        LayoutElement muteLayout = mute.GetComponent<LayoutElement>();
        if (muteLayout != null)
            Object.DestroyImmediate(muteLayout);

        TMP_Text muteLabel = mute.GetComponentInChildren<TextMeshProUGUI>();
        muteLabel.fontSize = 16f;

        // Denetimlerin yerini kaplayan açıklama. Kaydırıcı ve susturma
        // gizlendiğinde burası "neden yok" diye yazıyor — boş bir alan "bozuk"
        // diye okunuyor.
        TMP_Text note = CreateAnchoredText(row.transform, "Not", string.Empty, 15f,
            TextAlignmentOptions.Left, new Vector2(0.58f, 0f), new Vector2(1f, 1f), 6f);

        SetPreferredHeight(row, 46f);

        return new ScoreboardPanel.Row
        {
            root = row,
            background = row.GetComponent<Image>(),
            nameLabel = nameLabel,
            pingLabel = pingLabel,
            muteButton = mute,
            muteLabel = muteLabel,
            volumeSlider = volume,
            noteLabel = note
        };
    }

    /// <summary>
    /// Tuş atama ekranı. Her eylem için bir satır: solda adı, sağda o anki
    /// tuşu taşıyan basılabilir bir düğme.
    /// </summary>
    private static GameObject BuildControlsPanel(Transform parent, MenuController controller)
    {
        GameObject panel = CreatePanel("Panel_Tuslar", parent);
        Transform column = CreateColumn(panel.transform, 620f);

        Loc(CreateTitle(column, "TUŞ ATAMALARI"), "TUŞ ATAMALARI").fontSize = 40f;
        CreateSpacer(column, 8f);

        KeyBindingPanel bindings = panel.AddComponent<KeyBindingPanel>();

        GameAction[] actions = KeyBindings.Actions;
        KeyBindingPanel.Row[] rows = new KeyBindingPanel.Row[actions.Length];

        for (int i = 0; i < actions.Length; i++)
            rows[i] = CreateBindingRow(column, bindings, actions[i], i);

        CreateSpacer(column, 8f);
        TMP_Text hint = CreateLabel(column, string.Empty);
        hint.fontSize = 16f;
        hint.color = new Color(0.6f, 0.6f, 0.66f, 1f);

        CreateSpacer(column, 8f);
        Loc(AddButton(column, "VARSAYILANA DÖN", bindings.ResetToDefaults), "VARSAYILANA DÖN");
        Loc(AddButton(column, "GERİ", bindings.Close), "GERİ");

        SerializedObject serialized = new SerializedObject(bindings);
        serialized.FindProperty("menu").objectReferenceValue = controller;
        serialized.FindProperty("hintLabel").objectReferenceValue = hint;

        SerializedProperty rowArray = serialized.FindProperty("rows");
        rowArray.arraySize = rows.Length;

        for (int i = 0; i < rows.Length; i++)
        {
            SerializedProperty element = rowArray.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("action").enumValueIndex = (int)rows[i].action;
            element.FindPropertyRelative("nameLabel").objectReferenceValue = rows[i].nameLabel;
            element.FindPropertyRelative("keyLabel").objectReferenceValue = rows[i].keyLabel;
            element.FindPropertyRelative("button").objectReferenceValue = rows[i].button;
        }

        serialized.ApplyModifiedProperties();

        return panel;
    }

    /// <summary>
    /// Tek atama satırı. Satırın tamamı düğme: solda eylem adı, sağda tuş.
    /// Küçük bir hedefe nişan almak yerine tüm satıra tıklanabiliyor.
    /// </summary>
    private static KeyBindingPanel.Row CreateBindingRow(Transform parent, KeyBindingPanel target,
        GameAction action, int index)
    {
        Button button = AddButton(parent, string.Empty, null);
        button.GetComponentInChildren<TextMeshProUGUI>().text = string.Empty;

        // İndeksi taşıyan kalıcı dinleyici: hangi satırın dinlemeye geçtiğini
        // bileşen bu sayıdan biliyor.
        UnityEventTools.AddIntPersistentListener(button.onClick, target.BeginListening, index);

        TMP_Text nameLabel = CreateAnchoredText(button.transform, "Eylem",
            KeyBindings.DescribeAction(action), 20f, TextAlignmentOptions.Left,
            new Vector2(0f, 0f), new Vector2(0.6f, 1f), 18f);

        TMP_Text keyLabel = CreateAnchoredText(button.transform, "Tus",
            KeyBindings.Describe(KeyBindings.Default(action)), 20f, TextAlignmentOptions.Right,
            new Vector2(0.6f, 0f), new Vector2(1f, 1f), 18f);

        LayoutElement element = button.GetComponent<LayoutElement>();
        element.minHeight = 38f;
        element.preferredHeight = 38f;

        return new KeyBindingPanel.Row
        {
            action = action,
            nameLabel = nameLabel,
            keyLabel = keyLabel,
            button = button
        };
    }

    /// <summary>
    /// Oda ekranı. Hem sunucu hem istemci burayı görüyor; aradaki tek fark,
    /// canavar seçimi ve başlatma düğmelerinin yalnızca oda sahibinde aktif
    /// olması (gizlenmiyor, griye alınıyor — bkz. LobbyPanel).
    /// </summary>
    /// <summary>
    /// Sahnedeki iki transport'u lobiye bağlar.
    ///
    /// **Bu araç lobiyi SIFIRDAN kuruyor**, yani `EOS Kurulumu`'nun yazdığı
    /// referanslar `Menü Kur` her çalıştığında silinirdi ve oyun sessizce
    /// yerel odaya düşerdi — sebebi hiçbir yerde görünmeden. İki araç da
    /// bağlaması, hangisinin sonra çalıştığından bağımsız olarak doğru sonucu
    /// veriyor.
    ///
    /// EOS kurulu değilse `relayTransport` boş kalıyor; lobi o durumda yerel
    /// odaya düşüyor ve sebebini ekranda yazıyor.
    /// </summary>
    private static void WireTransports(LobbyNetwork network)
    {
        GameObject managerObject = GameObject.Find("NetworkManager");
        if (managerObject == null)
            return;

        SerializedObject serialized = new SerializedObject(network);

        serialized.FindProperty("relayTransport").objectReferenceValue =
            managerObject.GetComponent<EpicTransport.EosTransport>();

        serialized.FindProperty("localTransport").objectReferenceValue =
            managerObject.GetComponent<kcp2k.KcpTransport>();

        // Kısa oda kodunu üreten lobi servisi. Bulunamazsa boş kalıyor ve oda
        // yine kuruluyor — yalnızca kod host'un 32 karakterlik ürün kimliği
        // oluyor (bkz. LobbyNetwork.StartHosting).
        serialized.FindProperty("relayLobby").objectReferenceValue =
            managerObject.GetComponent<RelayLobby>();

        serialized.ApplyModifiedProperties();
    }

    private static GameObject BuildLobbyPanel(Transform parent, MenuController controller,
        LobbyNetwork network)
    {
        GameObject panel = CreatePanel("Panel_Lobi", parent);
        Transform column = CreateColumn(panel.transform, 720f);

        Loc(CreateTitle(column, "LOBİ"), "LOBİ").fontSize = 46f;

        LobbyPanel lobby = panel.AddComponent<LobbyPanel>();
        LobbyRoster roster = panel.AddComponent<LobbyRoster>();

        // Kod satırı: kod ve kopyalama yan yana, dikey yer kazanmak için.
        // "YENİLE" düğmesi kalktı — kod artık rastgele değil, sunucunun
        // adresinin kendisi (bkz. LobbyCode); yenilenecek bir şey yok.
        Transform codeRow = CreateRow(column, 54f);
        TMP_Text codeLabel = CreateRowText(codeRow, LobbyCode.Unknown, 38f, AccentColor, 0.6f);

        // Kod iki farklı uzunlukta gelebiliyor: yerel odada 7 harf, EOS
        // odasında 32 karakterlik ürün kimliği. Sabit punto uzun kodu satıra
        // sığdıramıyor, metin taşıyor ve KOPYALA düğmesini eziyordu.
        // Otomatik küçültme ikisini de aynı satırda tutuyor.
        codeLabel.enableAutoSizing = true;
        codeLabel.fontSizeMin = 14f;
        codeLabel.fontSizeMax = 38f;
        codeLabel.enableWordWrapping = false;
        codeLabel.overflowMode = TextOverflowModes.Ellipsis;

        Button copyButton = AddRowButton(codeRow, "KOPYALA", lobby.CopyCode, 0.25f);
        Loc(copyButton, "KOPYALA");

        // Düğmeye taban genişlik: esnek pay tek başına yetmiyor, uzun metin
        // satırın tamamını isteyince düğme dikey bir şeride dönüşüyordu.
        copyButton.GetComponent<LayoutElement>().minWidth = 96f;

        CreateSpacer(column, 8f);
        TMP_Text countLabel = CreateLabel(column, "Oyuncular: 0/5");
        countLabel.fontSize = 20f;

        LobbyPanel.SlotView[] slots = new LobbyPanel.SlotView[LobbyRoster.MaxPlayers];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = CreateSlotRow(column);

        CreateSpacer(column, 10f);

        Button monsterButton = AddButton(column, "Canavar: Rastgele", lobby.CycleMonsterChoice);
        TMP_Text monsterLabel = monsterButton.GetComponentInChildren<TextMeshProUGUI>();

        // Karakter seçimi lobiden de açılıyor: oyuncu odaya girdikten sonra
        // kostüm değiştirmek isterse ana menüye dönmek bağlantıyı koparmak
        // olurdu. Seçim anında kadroya yansıyor (RoundParticipant.PushCostume).
        Loc(AddButton(column, "KARAKTER", controller.ShowCharacters), "KARAKTER");

        // Seçenekler lobiden de açılıyor (2026-09-26, kullanıcı isteği):
        // oyuncular odada beklerken fare hassasiyetini, sesi ya da tuşlarını
        // ayarlamak istiyor ve ana menüye dönmek bağlantıyı koparmak olurdu —
        // KARAKTER düğmesinin gerekçesinin aynısı.
        //
        // `ShowSettings` geldiği ekranı hatırlıyor, yani GERİ ve Esc lobiye
        // dönüyor; ayrı bir "nereye dön" bilgisi tutmak gerekmedi.
        Loc(AddButton(column, "SEÇENEKLER", controller.ShowSettings), "SEÇENEKLER");

        Button readyButton = AddButton(column, "HAZIRIM", lobby.ToggleReady);
        TMP_Text readyLabel = readyButton.GetComponentInChildren<TextMeshProUGUI>();

        Button startButton = AddButton(column, "BAŞLAT", lobby.StartRound, AccentColor);
        TMP_Text startLabel = startButton.GetComponentInChildren<TextMeshProUGUI>();

        CreateSpacer(column, 6f);
        TMP_Text statusLabel = CreateLabel(column, string.Empty);
        statusLabel.fontSize = 17f;
        statusLabel.color = new Color(0.66f, 0.66f, 0.72f, 1f);

        CreateSpacer(column, 6f);
        Loc(AddButton(column, "AYRIL", lobby.Leave), "AYRIL");

        SerializedObject serialized = new SerializedObject(lobby);
        serialized.FindProperty("network").objectReferenceValue = network;
        serialized.FindProperty("roster").objectReferenceValue = roster;
        serialized.FindProperty("codeLabel").objectReferenceValue = codeLabel;
        serialized.FindProperty("statusLabel").objectReferenceValue = statusLabel;
        serialized.FindProperty("rosterCountLabel").objectReferenceValue = countLabel;
        serialized.FindProperty("readyButton").objectReferenceValue = readyButton;
        serialized.FindProperty("readyLabel").objectReferenceValue = readyLabel;
        serialized.FindProperty("monsterButton").objectReferenceValue = monsterButton;
        serialized.FindProperty("monsterLabel").objectReferenceValue = monsterLabel;
        serialized.FindProperty("startButton").objectReferenceValue = startButton;
        serialized.FindProperty("startLabel").objectReferenceValue = startLabel;

        SerializedProperty slotArray = serialized.FindProperty("slots");
        slotArray.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
        {
            SerializedProperty element = slotArray.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("background").objectReferenceValue = slots[i].background;
            element.FindPropertyRelative("nameLabel").objectReferenceValue = slots[i].nameLabel;
            element.FindPropertyRelative("statusLabel").objectReferenceValue = slots[i].statusLabel;
            element.FindPropertyRelative("kickButton").objectReferenceValue = slots[i].kickButton;
            element.FindPropertyRelative("kickLabel").objectReferenceValue = slots[i].kickLabel;
            element.FindPropertyRelative("banButton").objectReferenceValue = slots[i].banButton;
            element.FindPropertyRelative("banLabel").objectReferenceValue = slots[i].banLabel;
        }

        serialized.ApplyModifiedProperties();

        return panel;
    }

    /// <summary>
    /// Kadro satırı: isim, durum (HAZIR / bekliyor / CANAVAR), AT ve YASAKLA.
    ///
    /// Son ikisinin hedefi (hangi oyuncu) ancak ÇALIŞMA ANINDA, o an bu
    /// satırda kim olduğuna göre belli oluyor — burada yalnızca DÜĞMENİN
    /// kendisi kuruluyor, tıklama eylemi `LobbyPanel.ApplyKickControls`'a
    /// bırakılıyor (bkz. CreateAnchoredButton).
    /// </summary>
    private static LobbyPanel.SlotView CreateSlotRow(Transform parent)
    {
        GameObject row = new GameObject("Satir_Oyuncu", typeof(RectTransform), typeof(Image));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.13f, 1f);

        TMP_Text nameLabel = CreateAnchoredText(row.transform, "Ad", "— boş —", 22f,
            TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0.42f, 1f), 16f);

        TMP_Text statusLabel = CreateAnchoredText(row.transform, "Durum", string.Empty, 17f,
            TextAlignmentOptions.Right, new Vector2(0.42f, 0f), new Vector2(0.60f, 1f), 10f);

        Button kickButton = CreateAnchoredButton(row.transform, "Buton_At", "AT",
            new Vector2(0.60f, 0f), new Vector2(0.80f, 1f), 4f, primary: false);
        Loc(kickButton, "AT");

        Button banButton = CreateAnchoredButton(row.transform, "Buton_Yasakla", "YASAKLA",
            new Vector2(0.80f, 0f), new Vector2(1f, 1f), 4f, primary: true);
        Loc(banButton, "YASAKLA");

        SetPreferredHeight(row, 40f);

        return new LobbyPanel.SlotView
        {
            background = row.GetComponent<Image>(),
            nameLabel = nameLabel,
            statusLabel = statusLabel,
            kickButton = kickButton,
            kickLabel = kickButton.GetComponentInChildren<TextMeshProUGUI>(),
            banButton = banButton,
            banLabel = banButton.GetComponentInChildren<TextMeshProUGUI>()
        };
    }

    private static GameObject BuildJoinLobbyPanel(Transform parent, MenuController controller,
        LobbyNetwork network)
    {
        GameObject panel = CreatePanel("Panel_LobiyeKatil", parent);
        Transform column = CreateColumn(panel.transform);

        Loc(CreateTitle(column, "LOBİYE KATIL"), "LOBİYE KATIL");
        CreateSpacer(column, 12f);

        Loc(CreateLabel(column, "Arkadaşının verdiği kodu gir"), "Arkadaşının verdiği kodu gir");
        TMP_InputField codeField = CreateInputField(column, "KOD YA DA IP ADRESİ");

        // Alan dört biçimi birden almak zorunda ve en uzunu sınırı belirliyor:
        //   6 karakter  → EOS oda kodu (bugünkü olağan yol)
        //   7 karakter  → IP'den üretilmiş yerel kod
        //   15 karakter → IP (255.255.255.255)
        //   32 karakter → EOS ürün kimliği (kısa kod alınamadıysa yedek)
        // Sınır 15'ti; EOS kodu yapıştırılınca sessizce kırpılıyor ve oyuncu
        // neden bağlanamadığını anlamıyordu.
        codeField.characterLimit = 64;

        // Doğrulama KAPALI. `CreateInputField` alfanümerik kuruyor ve o kural
        // NOKTAYI eliyor — yani "ham IP de kabul ediliyor" sözü aslında hiç
        // tutmuyordu, girilen adresten noktalar sessizce düşüyordu. İçeriği
        // zaten `LobbyNetwork.JoinLobby` denetliyor; burada süzmek yalnızca
        // sessiz hata üretiyor.
        codeField.characterValidation = TMP_InputField.CharacterValidation.None;

        JoinLobbyPanel join = panel.AddComponent<JoinLobbyPanel>();

        CreateSpacer(column, 14f);
        Loc(AddButton(column, "KATIL", join.Join, AccentColor), "KATIL");

        CreateSpacer(column, 8f);
        TMP_Text statusLabel = CreateLabel(column, string.Empty);
        statusLabel.fontSize = 17f;
        statusLabel.color = new Color(0.66f, 0.66f, 0.72f, 1f);

        // Açık odalar. Satırlar ve başlık EOS hazır değilken gizleniyor
        // (`JoinLobbyPanel.ApplyRows`), yani yerel odayla oynayan biri hiç
        // görmüyor — çalışmayan bir bölümü göstermek "bozuk" izlenimi verirdi.
        CreateSpacer(column, 14f);
        TMP_Text listHeader = CreateLabel(column, "AÇIK ODALAR");
        listHeader.fontSize = 20f;
        listHeader.color = AccentColor;
        Loc(listHeader, "AÇIK ODALAR");

        Button refreshButton = AddButton(column, "ODALARI YENİLE", join.RefreshRooms);
        TMP_Text refreshLabel = refreshButton.GetComponentInChildren<TextMeshProUGUI>();

        JoinLobbyPanel.RoomRow[] roomRows = new JoinLobbyPanel.RoomRow[RoomListRows];
        for (int i = 0; i < roomRows.Length; i++)
            roomRows[i] = CreateRoomRow(column, join, i);

        CreateSpacer(column, 10f);
        Loc(AddButton(column, "GERİ", controller.ShowMain), "GERİ");

        SerializedObject serialized = new SerializedObject(join);
        serialized.FindProperty("network").objectReferenceValue = network;
        serialized.FindProperty("codeField").objectReferenceValue = codeField;
        serialized.FindProperty("statusLabel").objectReferenceValue = statusLabel;
        serialized.FindProperty("listHeaderLabel").objectReferenceValue = listHeader;
        serialized.FindProperty("refreshButton").objectReferenceValue = refreshButton;
        serialized.FindProperty("refreshLabel").objectReferenceValue = refreshLabel;

        // Lobi servisi NetworkManager'da duruyor. Bulunamazsa alan boş kalıyor
        // ve ekran yalnızca kodla çalışıyor — EOS Kurulumu çalıştırılmamış bir
        // projede menünün yine de kurulabilmesi gerekiyor.
        GameObject managerObject = GameObject.Find("NetworkManager");
        serialized.FindProperty("relayLobby").objectReferenceValue =
            managerObject != null ? managerObject.GetComponent<RelayLobby>() : null;

        SerializedProperty rowArray = serialized.FindProperty("rows");
        rowArray.arraySize = roomRows.Length;
        for (int i = 0; i < roomRows.Length; i++)
        {
            SerializedProperty element = rowArray.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("button").objectReferenceValue = roomRows[i].button;
            element.FindPropertyRelative("nameLabel").objectReferenceValue = roomRows[i].nameLabel;
            element.FindPropertyRelative("codeLabel").objectReferenceValue = roomRows[i].codeLabel;
        }

        serialized.ApplyModifiedProperties();

        return panel;
    }

    /// <summary>
    /// Oda listesi satırı: solda oda adı, sağda kod ve doluluk. Satırın tamamı
    /// düğme — küçük bir hedefe nişan almak yerine tüm satıra tıklanıyor (tuş
    /// atama satırlarıyla aynı kalıp, bkz. <see cref="CreateBindingRow"/>).
    ///
    /// Hangi satır olduğunu **indeksi taşıyan kalıcı dinleyici** söylüyor, yani
    /// bağlantı sahne dosyasında duruyor ve çalışma anında kurulacak bir şey
    /// kalmıyor.
    /// </summary>
    private static JoinLobbyPanel.RoomRow CreateRoomRow(Transform parent, JoinLobbyPanel target,
        int index)
    {
        Button button = AddButton(parent, string.Empty, null);
        button.GetComponentInChildren<TextMeshProUGUI>().text = string.Empty;

        UnityEventTools.AddIntPersistentListener(button.onClick, target.JoinRoomAt, index);

        TMP_Text nameLabel = CreateAnchoredText(button.transform, "Ad", string.Empty, 20f,
            TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0.62f, 1f), 18f);

        TMP_Text codeLabel = CreateAnchoredText(button.transform, "Kod", string.Empty, 20f,
            TextAlignmentOptions.Right, new Vector2(0.62f, 0f), new Vector2(1f, 1f), 18f);

        codeLabel.color = AccentColor;

        // AddButton 54 yazıyor; liste satırı düğmeden alçak olmalı ki altı
        // satır ekrana sığsın.
        SetPreferredHeight(button.gameObject, 42f);

        return new JoinLobbyPanel.RoomRow
        {
            button = button,
            nameLabel = nameLabel,
            codeLabel = codeLabel
        };
    }

    /// <summary>
    /// Karakter seçimi: kaçan ve canavar kostümü (bkz. CharacterSelectPanel).
    ///
    /// **Sütun ekranın SOLUNDA**, çünkü sağ tarafı arka plandaki modele
    /// bıraktık. Panelin tam ekran gövdesi de saydam: modeli karartılmış bir
    /// perdenin ardından göstermek onu görmeyi zorlaştırır. Saydam gövde
    /// ayrıca fare sürüklemesini de engellemiyor — model o boşlukta çevriliyor.
    /// Yazılar sütunun kendi koyu kutusunun üstünde duruyor, yani okunaklılık
    /// gövdeden değil kutudan geliyor.
    ///
    /// Yön düğmeleri "&lt;" ve "&gt;" — üçgen okların (◀ ▶) temel Latin dışında
    /// olması ve varsayılan TMP atlasında bulunmaması gerçek bir ihtimal
    /// (bölüm 20'deki boş kutu sorunu). Burada `GameHud.Glyph` gibi bir yedek
    /// mekanizma kurmaya değmez: iki karakterin garantili karşılığı zaten var.
    /// </summary>
    private static GameObject BuildCharacterPanel(Transform parent, MenuController controller)
    {
        GameObject panel = CreatePanel("Panel_Karakter", parent);
        panel.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.03f, 0f);

        Transform column = CreateColumn(panel.transform, 520f);

        // Oran kullanılıyor, piksel değil: sütun her çözünürlükte ekranın aynı
        // yerinde kalıyor ve modelin payı sabit.
        RectTransform columnRect = column.GetComponent<RectTransform>();
        columnRect.anchorMin = new Vector2(0.3f, 0.5f);
        columnRect.anchorMax = new Vector2(0.3f, 0.5f);

        CharacterSelectPanel select = panel.AddComponent<CharacterSelectPanel>();

        Loc(CreateTitle(column, "KARAKTER"), "KARAKTER").fontSize = 42f;
        CreateSpacer(column, 10f);

        Button roleButton = AddButton(column, "KAÇAN", select.ToggleRole);
        TMP_Text roleLabel = roleButton.GetComponentInChildren<TextMeshProUGUI>();

        CreateSpacer(column, 8f);

        Transform pickRow = CreateRow(column, 62f);
        Button previousButton = AddRowButton(pickRow, "<", select.Previous, 0.18f);
        TMP_Text costumeLabel = CreateRowText(pickRow, "Banana Man", 26f, AccentLight, 0.64f);
        Button nextButton = AddRowButton(pickRow, ">", select.Next, 0.18f);

        TMP_Text counterLabel = CreateLabel(column, "1 / 1");
        counterLabel.fontSize = 18f;

        CreateSpacer(column, 10f);
        TMP_Text statusLabel = CreateLabel(column, string.Empty);
        statusLabel.fontSize = 16f;
        statusLabel.color = new Color(0.66f, 0.66f, 0.72f, 1f);

        CreateSpacer(column, 12f);

        // ShowMain değil: ekran hem ana menüden hem lobiden açılıyor ve
        // lobiden girip ana menüye düşmek odayı ekrandan kaybetmek olurdu.
        Loc(AddButton(column, "GERİ", controller.CloseCharacters), "GERİ");

        SerializedObject serialized = new SerializedObject(select);
        serialized.FindProperty("roleLabel").objectReferenceValue = roleLabel;
        serialized.FindProperty("costumeLabel").objectReferenceValue = costumeLabel;
        serialized.FindProperty("counterLabel").objectReferenceValue = counterLabel;
        serialized.FindProperty("previousButton").objectReferenceValue = previousButton;
        serialized.FindProperty("nextButton").objectReferenceValue = nextButton;
        serialized.FindProperty("statusLabel").objectReferenceValue = statusLabel;
        serialized.ApplyModifiedProperties();

        return panel;
    }

    /// <summary>
    /// Duraklatma ekranı.
    ///
    /// **Ayrılma düğmesinin ne yapacağı ÇAĞIRANDAN geliyor**, çünkü aynı ekran
    /// iki sahnede kullanılıyor: ana oyunda odadan ayrılmak
    /// (`LobbyNetwork.Leave`), tutorial'da koridoru bırakıp ana menüye dönmek
    /// (`TutorialBootstrap.ReturnToMenu`). Oyuncu için ikisi de "buradan çık";
    /// iki ayrı duraklatma ekranı kurmak aynı paneli iki yerde tutmak olurdu.
    /// </summary>
    private static GameObject BuildPausePanel(Transform parent, MenuController controller,
        UnityEngine.Events.UnityAction leaveAction, string leaveLabel)
    {
        GameObject panel = CreatePanel("Panel_Duraklat", parent);
        Transform column = CreateColumn(panel.transform);

        Loc(CreateTitle(column, "DURAKLATILDI"), "DURAKLATILDI");
        CreateSpacer(column, 18f);

        Loc(AddButton(column, "DEVAM ET", controller.CloseMenu, AccentColor), "DEVAM ET");
        Loc(AddButton(column, "SEÇENEKLER", controller.ShowSettings), "SEÇENEKLER");

        // Ana menüye dönmek artık bağlantıyı da koparıyor. Eskiden yalnızca
        // ekran değiştiriyordu; ağ oyununda bu, oyuncunun turda kalmaya devam
        // ettiği ama ekranını göremediği bir hayalet duruma yol açardı.
        //
        // Eylem yoksa düğme HİÇ kurulmuyor: `AddButton` kalıcı dinleyiciyi
        // `UnityEventTools` ile ekliyor ve null bir eylem orada istisna atardı.
        // Basılabilir görünüp hiçbir şey yapmayan bir düğme de "bozuk" diye
        // okunurdu (bölüm 19'un gri kaydırıcı dersi).
        if (leaveAction != null)
            Loc(AddButton(column, leaveLabel, leaveAction), leaveLabel);

        return panel;
    }

    /// <summary>
    /// Panelleri kontrolcüye bağlar.
    ///
    /// Oyuncu bileşenleri artık burada bağlanmıyor: oyuncu prefabtan doğuyor ve
    /// kurulum anında sahnede bağlanacak bir şey yok. `MenuController` onları
    /// yerel oyuncu spawn olduğunda kendisi çözüyor.
    /// </summary>
    private static void WireController(MenuController controller, GameObject nameEntry, GameObject main,
        GameObject settings, GameObject audio, GameObject controls, GameObject lobby,
        GameObject joinLobby, GameObject characters, GameObject pause, GameObject backdrop)
    {
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("nameEntryPanel").objectReferenceValue = nameEntry;
        serialized.FindProperty("mainPanel").objectReferenceValue = main;
        serialized.FindProperty("settingsPanel").objectReferenceValue = settings;
        serialized.FindProperty("audioPanel").objectReferenceValue = audio;
        serialized.FindProperty("controlsPanel").objectReferenceValue = controls;
        serialized.FindProperty("lobbyPanel").objectReferenceValue = lobby;
        serialized.FindProperty("joinLobbyPanel").objectReferenceValue = joinLobby;
        serialized.FindProperty("characterPanel").objectReferenceValue = characters;
        serialized.FindProperty("pausePanel").objectReferenceValue = pause;
        serialized.FindProperty("backdrop").objectReferenceValue = backdrop;
        serialized.ApplyModifiedProperties();
    }

    /// <summary>
    /// Tam ekran, tamamen mat karartma. Panellerden önce yaratılıyor ki
    /// kardeş sırasında arkada kalsın.
    /// </summary>
    private static GameObject CreateBackdrop(Transform parent)
    {
        GameObject backdrop = new GameObject("Arkaplan", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(parent, false);
        Stretch(backdrop.GetComponent<RectTransform>());

        backdrop.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.028f, 1f);

        // **Karakter sahnesi arkaplanın ÇOCUĞU.** `MenuController.ApplyBackdrop`
        // arkaplanı zaten "menü açık ve tur oynanmıyor" kuralıyla açıp
        // kapatıyor (bölüm 13); çocuğu yapmak sahneyi tam doğru anlarda
        // gösteriyor ve `MenuController`'a tek satır eklemek gerekmedi.
        //
        // Duraklatmada arkada oyunun kendisi görünmeli, orada bu kapalı
        // olmalı — aynı kural ikisini birden çözüyor.
        GameObject stage = new GameObject("Sahne",
            typeof(RectTransform), typeof(RawImage), typeof(MenuStage));
        stage.transform.SetParent(backdrop.transform, false);
        Stretch(stage.GetComponent<RectTransform>());

        RawImage stageImage = stage.GetComponent<RawImage>();
        stageImage.color = Color.white;
        stageImage.raycastTarget = false;

        return backdrop;
    }

    // ---------- Yapı taşları ----------

    private static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();

        // Input System paketi kurulu değil; eski girdi modülü doğru olan.
        eventSystem.AddComponent<StandaloneInputModule>();

        Undo.RegisterCreatedObjectUndo(eventSystem, "Menü Kur");
    }

    private static GameObject CreateCanvas()
    {
        GameObject canvasObject = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasObject, "Menü Kur");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // OnGUI prototip arayüzünün üstünde dursun

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        return canvasObject;
    }

    private static GameObject CreatePanel(string name, Transform parent)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        Stretch(rect);

        panel.GetComponent<Image>().color = PanelColor;
        return panel;
    }

    /// <summary>Ortada dikey sıralanan içerik sütunu.</summary>
    private static Transform CreateColumn(Transform parent, float width = 520f,
        bool framed = true)
    {
        GameObject column = new GameObject("Sutun", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        column.transform.SetParent(parent, false);

        RectTransform rect = column.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, 0f);

        VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = column.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // **Sütun artık kendi KUTUSU.** Koyu gövde, ince çerçeve, köşe
        // ayraçları — terminal ve çıkış kilidi panelleriyle aynı dil
        // (bölüm 18). Kutu `ContentSizeFitter` sayesinde içeriğe göre
        // büyüyor, yani her ekran kendi boyunda bir panel oluyor.
        //
        // Dili BURAYA koymanın sebebi: bütün Build*Panel'ler bu yardımcıyı
        // çağırıyor, yani dokuz ekran tek yerden değişiyor. Her panele ayrı
        // çerçeve yazmak dokuz yerde tutarlılık kovalamak olurdu.
        if (framed)
        {
            layout.padding = new RectOffset(34, 34, 30, 30);

            Image body = column.AddComponent<Image>();
            body.color = BoxColor;

            AddFrame(column.transform, AccentDim, 2f, 40f);
        }

        return column.transform;
    }

    private static TMP_Text CreateTitle(Transform parent, string text)
    {
        GameObject label = new GameObject("Baslik", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 46f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = AccentColor;

        // Aralıklı büyük harf: terminal ekranlarındaki yazı hissi. Punto
        // 54'ten 46'ya indi, çünkü aralık zaten genişletiyor ve kutu artık
        // kenar boşluklu.
        tmp.characterSpacing = 14f;

        SetPreferredHeight(label, 62f);

        // Başlığı içerikten ayıran ince çizgi — panelin üst bandı.
        CreateRule(parent);

        return tmp;
    }

    private static TMP_Text CreateLabel(Transform parent, string text)
    {
        GameObject label = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 24f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = TextColor;

        SetPreferredHeight(label, 32f);
        return tmp;
    }

    private static void CreateSpacer(Transform parent, float height)
    {
        GameObject spacer = new GameObject("Bosluk", typeof(RectTransform));
        spacer.transform.SetParent(parent, false);
        SetPreferredHeight(spacer, height);
    }

    private static Button AddButton(Transform parent, string text, UnityEngine.Events.UnityAction action,
        Color? color = null)
    {
        bool primary = color.HasValue;

        GameObject buttonObject = new GameObject($"Buton_{text}", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();

        // **Gövde RENGİ taşıyor, parlaklığı `ColorBlock` veriyor.** Unity durum
        // rengini gövdeyle ÇARPIYOR; gövde koyu griyken varsayılan
        // `highlightedColor` (0.96) hiçbir şey yapmıyordu, yani düğmeler üstüne
        // gelince ölü duruyordu — "kimliksiz" görünmelerinin bir sebebi buydu.
        // Gövdeye tam doygun kırmızıyı verip durumları koyudan açığa
        // sıralayınca hem koyu bir düğme hem gerçek bir tepki çıkıyor.
        image.color = color ?? AccentColor;

        AddFrame(buttonObject.transform, primary ? AccentLight : AccentDim, 1.5f, 12f);

        GameObject labelObject = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        Stretch(labelObject.GetComponent<RectTransform>());

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Center;
        label.characterSpacing = 8f;
        label.raycastTarget = false;

        // Dolu düğmede yazı KOYU. "Sıradaki adım dolu renkte, yazısı koyu"
        // bölüm 18'deki desen: göz sıradakini aramak zorunda kalmıyor.
        label.color = primary ? new Color(0.08f, 0.04f, 0.03f, 1f) : AccentLight;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;

        if (primary)
        {
            colors.normalColor = new Color(0.92f, 0.92f, 0.92f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.68f, 0.68f, 0.68f);
        }
        else
        {
            colors.normalColor = new Color(0.15f, 0.15f, 0.16f);
            colors.highlightedColor = new Color(0.42f, 0.38f, 0.38f);
            colors.pressedColor = new Color(0.68f, 0.62f, 0.62f);
        }

        // Tıklandıktan sonra düğme "seçili" kalıyor; seçili rengi normale
        // eşitlemek ekranda takılı kalan bir vurguyu önlüyor.
        colors.selectedColor = colors.normalColor;

        // Unity'nin varsayılan disabledColor'ı çok soluk bir tint uyguluyor;
        // vurgu rengindeki bir düğme kapalıyken bile canlı kırmızı görünüyor
        // ve basılabilir sanılıyordu.
        colors.disabledColor = new Color(0.10f, 0.10f, 0.11f, 0.7f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        // Kalıcı dinleyici: Inspector'da görünür, elle değiştirilebilir.
        if (action != null)
            UnityEventTools.AddPersistentListener(button.onClick, action);

        SetPreferredHeight(buttonObject, 52f);
        return button;
    }

    /// <summary>
    /// Baked bir yazıya dil anahtarı bağlar (bkz. LocalizedText). Yalnızca
    /// ÇALIŞMA ANINDA hiç değişmeyen başlık/düğme yazılarına eklenmeli —
    /// bir Panel scripti kendi Refresh()'inde zaten üstüne yazan bir yazıya
    /// eklenirse ikisi çakışır, hangisinin kazandığı çalıştırma sırasına
    /// bağlı kalırdı. Bu yüzden her çağrı çağıran tarafta BİLEREK tek tek
    /// seçildi, CreateLabel/AddButton'a otomatik eklenmedi.
    /// </summary>
    /// <summary>
    /// `TMP_Text`'i geri döndürüyor, bilerek: çağıranların çoğu
    /// `CreateTitle(...)`'ın döndürdüğü referansa `.fontSize`/`.color` gibi
    /// bir özellik daha yazıyor (`Loc(CreateTitle(...), "...").fontSize = X`).
    /// `void` dönseydi bu zincirleme derlenmezdi.
    /// </summary>
    private static TMP_Text Loc(TMP_Text label, string key)
    {
        if (label == null)
            return label;

        LocalizedText localized = label.gameObject.AddComponent<LocalizedText>();
        localized.key = key;
        return label;
    }

    private static Button Loc(Button button, string key)
    {
        Loc(button != null ? button.GetComponentInChildren<TMP_Text>(true) : null, key);
        return button;
    }

    private static Slider CreateSlider(Transform parent)
    {
        GameObject sliderObject = new GameObject("Kaydirici", typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(parent, false);

        GameObject background = CreateStretchedImage("Arka", sliderObject.transform,
            new Color(0.1f, 0.1f, 0.13f, 1f));

        GameObject fillArea = new GameObject("DolguAlani", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObject.transform, false);
        Stretch(fillArea.GetComponent<RectTransform>());

        GameObject fill = CreateStretchedImage("Dolgu", fillArea.transform, AccentColor);

        GameObject handleArea = new GameObject("TutamacAlani", typeof(RectTransform));
        handleArea.transform.SetParent(sliderObject.transform, false);
        Stretch(handleArea.GetComponent<RectTransform>());

        GameObject handle = CreateStretchedImage("Tutamac", handleArea.transform, TextColor);
        handle.GetComponent<RectTransform>().sizeDelta = new Vector2(22f, 0f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.value = 0.5f;

        SetPreferredHeight(sliderObject, 30f);
        return slider;
    }

    private static TMP_InputField CreateInputField(Transform parent, string placeholder)
    {
        GameObject fieldObject = new GameObject("KodGirisi", typeof(RectTransform), typeof(Image),
            typeof(TMP_InputField));
        fieldObject.transform.SetParent(parent, false);
        fieldObject.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f, 1f);

        GameObject textArea = new GameObject("Alan", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(fieldObject.transform, false);
        RectTransform areaRect = textArea.GetComponent<RectTransform>();
        Stretch(areaRect);
        areaRect.offsetMin = new Vector2(12f, 6f);
        areaRect.offsetMax = new Vector2(-12f, -6f);

        TextMeshProUGUI placeholderText = CreateFieldText(textArea.transform, "YerTutucu", placeholder,
            new Color(0.5f, 0.5f, 0.55f, 1f));

        // Yer tutucu da bir arayüz yazısı. Unutulunca İngilizce oyunda Türkçe
        // kalıyordu ("KOD YA DA IP ADRESİ"). Ad alanının yer tutucusu
        // `PlayerProfile.DefaultName` ("Player"), tabloda karşılığı yok ve
        // `Get` bilinmeyen anahtarı olduğu gibi döndürüyor — zararsız.
        Loc(placeholderText, placeholder);
        TextMeshProUGUI inputText = CreateFieldText(textArea.transform, "Metin", "", TextColor);

        TMP_InputField field = fieldObject.GetComponent<TMP_InputField>();
        field.textViewport = areaRect;
        field.textComponent = inputText;
        field.placeholder = placeholderText;
        field.characterLimit = 6;
        field.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;

        SetPreferredHeight(fieldObject, 52f);
        return field;
    }

    private static TextMeshProUGUI CreateFieldText(Transform parent, string name, string text, Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        Stretch(textObject.GetComponent<RectTransform>());

        TextMeshProUGUI tmp = textObject.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 28f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;

        return tmp;
    }

    private static GameObject CreateStretchedImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Stretch(imageObject.GetComponent<RectTransform>());
        imageObject.GetComponent<Image>().color = color;
        return imageObject;
    }

    /// <summary>Yatay sıralanan satır — kod, rol ve test butonları için.</summary>
    private static Transform CreateRow(Transform parent, float height)
    {
        GameObject row = new GameObject("Satir", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        SetPreferredHeight(row, height);
        return row.transform;
    }

    private static TMP_Text CreateRowText(Transform row, string text, float fontSize, Color color,
        float widthWeight)
    {
        GameObject label = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(row, false);

        TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;

        LayoutElement element = label.AddComponent<LayoutElement>();
        element.flexibleWidth = widthWeight;

        return tmp;
    }

    private static Button AddRowButton(Transform row, string text, UnityEngine.Events.UnityAction action,
        float widthWeight, Color? color = null)
    {
        Button button = AddButton(row, text, action, color);

        // Satır içindeki butonlar SetPreferredHeight'ten gelen sabit yüksekliği
        // değil, satırın yüksekliğini kullansın.
        LayoutElement element = button.GetComponent<LayoutElement>();
        element.minHeight = -1f;
        element.preferredHeight = -1f;
        element.flexibleWidth = widthWeight;

        button.GetComponentInChildren<TextMeshProUGUI>().fontSize = 18f;
        return button;
    }

    /// <summary>
    /// Kadro satırındaki AT/YASAKLA gibi küçük düğmeler için. `AddButton`'ın
    /// aksine satırın kendi oranına (`anchorMin`/`anchorMax`) oturuyor ve
    /// sabit yükseklik ZORLAMIYOR — satır zaten tek bir 40 piksellik
    /// yüksekliğe sahip, `SetPreferredHeight`'ın 52'si oraya sığmazdı.
    ///
    /// Tıklama eylemini KENDİSİ bağlamıyor, bilerek: bu düğmelerin hedefi
    /// (hangi oyuncu) ancak ÇALIŞMA ANINDA, o an bu satırda kim olduğuna göre
    /// belli oluyor — `AddButton`'ın `UnityEventTools.AddPersistentListener`
    /// ile kurduğu kalıcı (editör zamanı, sabit bir metoda bağlı) dinleyici
    /// bunun için uygun değil. `LobbyPanel.ApplyKickControls` her tazelemede
    /// kendi `onClick.AddListener`'ını takıyor.
    ///
    /// Renk/durum mantığı `AddButton`'la BİREBİR aynı (bkz. oradaki yorum):
    /// gövde gerçek rengi taşıyor, `ColorBlock` yalnızca parlaklık veriyor.
    /// `primary` burada "daha ciddi eylem" anlamına geliyor (YASAKLA), tıpkı
    /// `AddButton`'da "sıradaki adım" anlamına geldiği gibi (bölüm 18'in
    /// "dolu düğme = vurgu" deseni).
    /// </summary>
    private static Button CreateAnchoredButton(Transform parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax, float padding, bool primary)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(padding, 5f);
        rect.offsetMax = new Vector2(-padding, -5f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = primary ? AccentColor : AccentDim;

        GameObject labelObject = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        Stretch(labelObject.GetComponent<RectTransform>());

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 14f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.color = primary ? new Color(0.08f, 0.04f, 0.03f, 1f) : AccentLight;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = primary ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.85f, 0.80f, 0.80f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = primary ? new Color(0.68f, 0.68f, 0.68f) : new Color(0.60f, 0.55f, 0.55f);
        colors.selectedColor = colors.normalColor;
        colors.disabledColor = new Color(0.10f, 0.10f, 0.11f, 0.55f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        return button;
    }

    /// <summary>Satır içinde belirli bir orana yaslanan yazı.</summary>
    private static TMP_Text CreateAnchoredText(Transform parent, string name, string text, float fontSize,
        TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax, float padding)
    {
        GameObject label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(parent, false);

        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(padding, 0f);
        rect.offsetMax = new Vector2(-padding, 0f);

        TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = TextColor;

        return tmp;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetPreferredHeight(GameObject target, float height)
    {
        LayoutElement element = target.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;
    }
}
