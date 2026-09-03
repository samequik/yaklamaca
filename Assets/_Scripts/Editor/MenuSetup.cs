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

        // Menüyle Mirror arasındaki köprü. Canvas'ın üstünde duruyor ve
        // NetworkBehaviour DEĞİL: sahnedeki menü objesine NetworkIdentity
        // eklenemiyor (CLAUDE.md bölüm 4).
        LobbyNetwork network = canvasObject.AddComponent<LobbyNetwork>();

        GameObject nameEntry = BuildNameEntryPanel(canvasObject.transform, controller);
        GameObject main = BuildMainPanel(canvasObject.transform, controller, network);
        GameObject settings = BuildSettingsPanel(canvasObject.transform, controller);
        GameObject controls = BuildControlsPanel(canvasObject.transform, controller);
        GameObject lobby = BuildLobbyPanel(canvasObject.transform, network);
        GameObject joinLobby = BuildJoinLobbyPanel(canvasObject.transform, controller, network);
        GameObject pause = BuildPausePanel(canvasObject.transform, controller, network);

        WireController(controller, nameEntry, main, settings, controls, lobby, joinLobby, pause, backdrop);

        SerializedObject serializedNetwork = new SerializedObject(network);
        serializedNetwork.FindProperty("menu").objectReferenceValue = controller;
        serializedNetwork.ApplyModifiedProperties();

        // Kurulumdan sonra hepsi açık kalırsa Scene penceresinde üst üste
        // binmiş paneller görünüyor. Sadece ana menü açık kalsın; hangisinin
        // gerçekten açılacağına MenuController çalışma anında karar veriyor.
        nameEntry.SetActive(false);
        settings.SetActive(false);
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

        TMP_Text volumeLabel = CreateLabel(column, "Ses");
        Slider volumeSlider = CreateSlider(column);

        SettingsPanel settings = panel.AddComponent<SettingsPanel>();

        CreateSpacer(column, 10f);
        Button invertButton = AddButton(column, "Ters bakış: kapalı", settings.ToggleInvertLook);
        TMP_Text invertLabel = invertButton.GetComponentInChildren<TextMeshProUGUI>();

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
        serialized.FindProperty("volumeSlider").objectReferenceValue = volumeSlider;
        serialized.FindProperty("volumeLabel").objectReferenceValue = volumeLabel;
        serialized.FindProperty("nameField").objectReferenceValue = nameField;
        serialized.ApplyModifiedProperties();

        return panel;
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
        AddRowButton(codeRow, "KOPYALA", lobby.CopyCode, 0.25f);

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
        TMP_InputField codeField = CreateInputField(column, $"ÖRN: K7M2QXB  ({LobbyCode.Length} harf)");
        codeField.characterLimit = 15; // IP adresi de kabul ediliyor, kod uzunluğu yetmez

        JoinLobbyPanel join = panel.AddComponent<JoinLobbyPanel>();

        CreateSpacer(column, 14f);
        AddButton(column, "KATIL", join.Join, AccentColor);

        CreateSpacer(column, 8f);
        TMP_Text statusLabel = CreateLabel(column, string.Empty);
        statusLabel.fontSize = 17f;
        statusLabel.color = new Color(0.66f, 0.66f, 0.72f, 1f);

        CreateSpacer(column, 10f);
        AddButton(column, "GERİ", controller.ShowMain);

        SerializedObject serialized = new SerializedObject(join);
        serialized.FindProperty("network").objectReferenceValue = network;
        serialized.FindProperty("codeField").objectReferenceValue = codeField;
        serialized.FindProperty("statusLabel").objectReferenceValue = statusLabel;
        serialized.ApplyModifiedProperties();

        return panel;
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
        GameObject settings, GameObject controls, GameObject lobby, GameObject joinLobby,
        GameObject pause, GameObject backdrop)
    {
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("nameEntryPanel").objectReferenceValue = nameEntry;
        serialized.FindProperty("mainPanel").objectReferenceValue = main;
        serialized.FindProperty("settingsPanel").objectReferenceValue = settings;
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
