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

    private static readonly Color PanelColor = new Color(0.05f, 0.05f, 0.07f, 0.93f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.16f, 0.2f, 1f);
    private static readonly Color AccentColor = new Color(0.75f, 0.2f, 0.16f, 1f);
    private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.95f, 1f);

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
        GameObject lobby = BuildLobbyPanel(canvasObject.transform, network);
        GameObject joinLobby = BuildJoinLobbyPanel(canvasObject.transform, controller, network);
        GameObject pause = BuildPausePanel(canvasObject.transform, controller, network);

        // HUD parçaları: MenuController'ın panel listesine GİRMİYORLAR, çünkü
        // ekran değiştikçe açılıp kapanmamaları gerekiyor. Canvas'ın çocuğu
        // olarak duruyorlar ve görünürlüklerini kendileri yönetiyor.
        BuildVoiceHud(canvasObject.transform);
        BuildScoreboard(canvasObject.transform);

        WireController(controller, nameEntry, main, settings, audio, controls, lobby, joinLobby, pause, backdrop);

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

    // ---------- Ekranlar ----------

    /// <summary>İlk girişte bir kez çıkan isim ekranı.</summary>
    private static GameObject BuildNameEntryPanel(Transform parent, MenuController controller)
    {
        GameObject panel = CreatePanel("Panel_Isim", parent);
        Transform column = CreateColumn(panel.transform);

        CreateTitle(column, "YAKALAMACA");
        CreateSpacer(column, 10f);

        CreateLabel(column, "Seni nasıl çağıralım?");
        TMP_InputField nameField = CreateInputField(column, PlayerProfile.DefaultName);
        nameField.characterValidation = TMP_InputField.CharacterValidation.None;
        nameField.characterLimit = PlayerProfile.MaxNameLength;

        TMP_Text hint = CreateLabel(column,
            $"Boş bırakırsan \"{PlayerProfile.DefaultName}\" olursun. Sonradan Seçenekler'den değiştirebilirsin.");
        hint.fontSize = 17f;
        hint.color = new Color(0.6f, 0.6f, 0.66f, 1f);

        NameEntryPanel entry = panel.AddComponent<NameEntryPanel>();

        CreateSpacer(column, 14f);
        AddButton(column, "DEVAM", entry.Confirm, AccentColor);

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

        CreateTitle(column, "YAKALAMACA");
        CreateSpacer(column, 18f);

        // "OYNA" düğmesi kalktı: ağ oyununda menüyü kapatmak oyuna girmek
        // değil, oyuncusuz bir sahneye bakmak demekti. Oynamanın tek yolu bir
        // oda kurmak ya da bir odaya katılmak.
        AddButton(column, "LOBİ KUR", network.HostLobby, AccentColor);
        AddButton(column, "LOBİYE KATIL", controller.ShowJoinLobby);
        AddButton(column, "SEÇENEKLER", controller.ShowSettings);
        AddButton(column, "ÇIKIŞ", controller.QuitGame);

        CreateSpacer(column, 14f);
        TMP_Text hint = CreateLabel(column,
            "Lobi kurunca 7 harflik bir kod çıkar. Arkadaşın o kodu girerek katılır.");
        hint.fontSize = 16f;
        hint.color = new Color(0.6f, 0.6f, 0.66f, 1f);

        return panel;
    }

    private static GameObject BuildSettingsPanel(Transform parent, MenuController controller)
    {
        GameObject panel = CreatePanel("Panel_Secenekler", parent);
        Transform column = CreateColumn(panel.transform);

        CreateTitle(column, "SEÇENEKLER");
        CreateSpacer(column, 12f);

        CreateLabel(column, "Oyuncu adı");
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

        // Ses ve tuşlar birer ALT EKRAN. Hepsi burada dururken ekran alt alta
        // sığmıyordu; seçenekler artık kategori kapısı.
        CreateSpacer(column, 14f);
        AddButton(column, "SES", controller.ShowAudio);
        CreateSpacer(column, 10f);
        AddButton(column, "TUŞ ATAMALARI", controller.ShowControls);

        CreateSpacer(column, 12f);

        // ShowMain değil: seçenekler tur ortasındaki duraklatmadan da
        // açılabiliyor ve oraya dönmesi gerekiyor.
        AddButton(column, "GERİ", controller.CloseSettings);

        SerializedObject serialized = new SerializedObject(settings);
        serialized.FindProperty("invertLabel").objectReferenceValue = invertLabel;
        serialized.FindProperty("sensitivitySlider").objectReferenceValue = sensitivitySlider;
        serialized.FindProperty("sensitivityLabel").objectReferenceValue = sensitivityLabel;
        serialized.FindProperty("nameField").objectReferenceValue = nameField;
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
    private static void BuildGameHud(Transform parent)
    {
        GameObject root = new GameObject("OyunHud", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        Stretch(root.GetComponent<RectTransform>());

        GameHud hud = root.AddComponent<GameHud>();

        BuildCrosshair(root.transform);
        BuildRoundLines(root.transform);

        // Ekranlar en sonda: nişangahın ÜSTÜNDE çizilsinler. Zaten ikisi de
        // `PlayerInteractor.InputCaptured` sırasında açılıyor ve o anda
        // nişangah gizli, ama kardeş sırası ikinci güvence.
        BuildTerminalScreen(root.transform);
        BuildExitLockScreen(root.transform);

        SerializedObject serialized = new SerializedObject(hud);
        serialized.FindProperty("root").objectReferenceValue = root;
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

        CrosshairView view = root.AddComponent<CrosshairView>();

        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("root").objectReferenceValue = root;
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

        RoundHudView view = root.AddComponent<RoundHudView>();

        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("root").objectReferenceValue = root;
        serialized.FindProperty("terminalLabel").objectReferenceValue = terminal;
        serialized.FindProperty("statusLabel").objectReferenceValue = status;
        serialized.FindProperty("alarmLabel").objectReferenceValue = alarm;
        serialized.ApplyModifiedProperties();
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

        TerminalScreen screen = root.AddComponent<TerminalScreen>();

        SerializedObject serialized = new SerializedObject(screen);
        serialized.FindProperty("root").objectReferenceValue = root;
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

        root.SetActive(false);
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

        ExitLockScreen screen = root.AddComponent<ExitLockScreen>();

        SerializedObject serialized = new SerializedObject(screen);
        serialized.FindProperty("root").objectReferenceValue = root;
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

        root.SetActive(false);
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

        CreateTitle(column, "SES");
        CreateSpacer(column, 10f);

        AudioPanel audio = panel.AddComponent<AudioPanel>();

        TMP_Text volumeLabel = CreateLabel(column, "Ses");
        Slider volumeSlider = CreateSlider(column);

        CreateSpacer(column, 14f);
        CreateLabel(column, "SESLİ SOHBET").color = AccentColor;

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
        AddButton(column, "GERİ", controller.CloseAudio);

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

        VoiceHud hud = root.AddComponent<VoiceHud>();

        SerializedObject serialized = new SerializedObject(hud);
        serialized.FindProperty("root").objectReferenceValue = root;
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
        GameObject panel = CreatePanel("Panel_Oyuncular", parent);
        Transform column = CreateColumn(panel.transform, 760f);

        CreateTitle(column, "OYUNCULAR").fontSize = 38f;
        CreateSpacer(column, 8f);

        ScoreboardPanel board = panel.AddComponent<ScoreboardPanel>();

        ScoreboardPanel.Row[] rows = new ScoreboardPanel.Row[LobbyRoster.MaxPlayers];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = CreateScoreRow(column, board, i);

        CreateSpacer(column, 10f);
        TMP_Text hint = CreateLabel(column, string.Empty);
        hint.fontSize = 16f;
        hint.color = new Color(0.66f, 0.66f, 0.72f, 1f);

        SerializedObject serialized = new SerializedObject(board);
        serialized.FindProperty("panel").objectReferenceValue = panel;
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
        }

        serialized.ApplyModifiedProperties();

        panel.SetActive(false);
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

        SetPreferredHeight(row, 46f);

        return new ScoreboardPanel.Row
        {
            root = row,
            background = row.GetComponent<Image>(),
            nameLabel = nameLabel,
            pingLabel = pingLabel,
            muteButton = mute,
            muteLabel = muteLabel,
            volumeSlider = volume
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

        CreateTitle(column, "TUŞ ATAMALARI").fontSize = 40f;
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
        AddButton(column, "VARSAYILANA DÖN", bindings.ResetToDefaults);
        AddButton(column, "GERİ", bindings.Close);

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

    private static GameObject BuildLobbyPanel(Transform parent, LobbyNetwork network)
    {
        GameObject panel = CreatePanel("Panel_Lobi", parent);
        Transform column = CreateColumn(panel.transform, 720f);

        CreateTitle(column, "LOBİ").fontSize = 46f;

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

        Button readyButton = AddButton(column, "HAZIRIM", lobby.ToggleReady);
        TMP_Text readyLabel = readyButton.GetComponentInChildren<TextMeshProUGUI>();

        Button startButton = AddButton(column, "BAŞLAT", lobby.StartRound, AccentColor);
        TMP_Text startLabel = startButton.GetComponentInChildren<TextMeshProUGUI>();

        CreateSpacer(column, 6f);
        TMP_Text statusLabel = CreateLabel(column, string.Empty);
        statusLabel.fontSize = 17f;
        statusLabel.color = new Color(0.66f, 0.66f, 0.72f, 1f);

        CreateSpacer(column, 6f);
        AddButton(column, "AYRIL", lobby.Leave);

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
        }

        serialized.ApplyModifiedProperties();

        return panel;
    }

    /// <summary>Kadro satırı: solda isim, sağda durum (HAZIR / bekliyor / CANAVAR).</summary>
    private static LobbyPanel.SlotView CreateSlotRow(Transform parent)
    {
        GameObject row = new GameObject("Satir_Oyuncu", typeof(RectTransform), typeof(Image));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.13f, 1f);

        TMP_Text nameLabel = CreateAnchoredText(row.transform, "Ad", "— boş —", 22f,
            TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0.68f, 1f), 16f);

        TMP_Text statusLabel = CreateAnchoredText(row.transform, "Durum", string.Empty, 18f,
            TextAlignmentOptions.Right, new Vector2(0.68f, 0f), new Vector2(1f, 1f), 16f);

        SetPreferredHeight(row, 40f);

        return new LobbyPanel.SlotView
        {
            background = row.GetComponent<Image>(),
            nameLabel = nameLabel,
            statusLabel = statusLabel
        };
    }

    private static GameObject BuildJoinLobbyPanel(Transform parent, MenuController controller,
        LobbyNetwork network)
    {
        GameObject panel = CreatePanel("Panel_LobiyeKatil", parent);
        Transform column = CreateColumn(panel.transform);

        CreateTitle(column, "LOBİYE KATIL");
        CreateSpacer(column, 12f);

        CreateLabel(column, "Arkadaşının verdiği kodu gir");
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
        AddButton(column, "KATIL", join.Join, AccentColor);

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

        Button refreshButton = AddButton(column, "ODALARI YENİLE", join.RefreshRooms);
        TMP_Text refreshLabel = refreshButton.GetComponentInChildren<TextMeshProUGUI>();

        JoinLobbyPanel.RoomRow[] roomRows = new JoinLobbyPanel.RoomRow[RoomListRows];
        for (int i = 0; i < roomRows.Length; i++)
            roomRows[i] = CreateRoomRow(column, join, i);

        CreateSpacer(column, 10f);
        AddButton(column, "GERİ", controller.ShowMain);

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

    private static GameObject BuildPausePanel(Transform parent, MenuController controller,
        LobbyNetwork network)
    {
        GameObject panel = CreatePanel("Panel_Duraklat", parent);
        Transform column = CreateColumn(panel.transform);

        CreateTitle(column, "DURAKLATILDI");
        CreateSpacer(column, 18f);

        AddButton(column, "DEVAM ET", controller.CloseMenu, AccentColor);
        AddButton(column, "SEÇENEKLER", controller.ShowSettings);

        // Ana menüye dönmek artık bağlantıyı da koparıyor. Eskiden yalnızca
        // ekran değiştiriyordu; ağ oyununda bu, oyuncunun turda kalmaya devam
        // ettiği ama ekranını göremediği bir hayalet duruma yol açardı.
        AddButton(column, "ODADAN AYRIL", network.Leave);

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
        GameObject joinLobby, GameObject pause, GameObject backdrop)
    {
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("nameEntryPanel").objectReferenceValue = nameEntry;
        serialized.FindProperty("mainPanel").objectReferenceValue = main;
        serialized.FindProperty("settingsPanel").objectReferenceValue = settings;
        serialized.FindProperty("audioPanel").objectReferenceValue = audio;
        serialized.FindProperty("controlsPanel").objectReferenceValue = controls;
        serialized.FindProperty("lobbyPanel").objectReferenceValue = lobby;
        serialized.FindProperty("joinLobbyPanel").objectReferenceValue = joinLobby;
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

        // Alfa 1: panellerin kendi 0.93'ü ardında sahne görünmesin diye.
        backdrop.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.045f, 1f);

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
    private static Transform CreateColumn(Transform parent, float width = 520f)
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

        return column.transform;
    }

    private static TMP_Text CreateTitle(Transform parent, string text)
    {
        GameObject label = new GameObject("Baslik", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 54f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = TextColor;

        SetPreferredHeight(label, 72f);
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
        GameObject buttonObject = new GameObject($"Buton_{text}", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color ?? ButtonColor;

        GameObject labelObject = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        Stretch(labelObject.GetComponent<RectTransform>());

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = TextColor;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        // Unity'nin varsayılan disabledColor'ı çok soluk bir tint uyguluyor;
        // vurgu rengindeki bir düğme kapalıyken bile canlı kırmızı görünüyor
        // ve basılabilir sanılıyordu. Değer görüntüyü belirgin şekilde
        // karartıyor (renkler çarpılıyor, o yüzden 0.3 gerçekten karartır).
        ColorBlock colors = button.colors;
        colors.disabledColor = new Color(0.3f, 0.3f, 0.32f, 0.75f);
        button.colors = colors;

        // Kalıcı dinleyici: Inspector'da görünür, elle değiştirilebilir.
        if (action != null)
            UnityEventTools.AddPersistentListener(button.onClick, action);

        SetPreferredHeight(buttonObject, 54f);
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
