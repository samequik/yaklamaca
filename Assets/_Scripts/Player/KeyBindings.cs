using System;
using UnityEngine;

/// <summary>Oyuncunun yeniden atayabildiği eylemler.</summary>
public enum GameAction
{
    Forward,
    Back,
    Left,
    Right,
    Jump,
    Sprint,
    Crouch,
    Interact,
    Flashlight,
    Attack
}

/// <summary>
/// Tuş atamaları. Cihazda saklanıyor, oyun kapanınca kaybolmuyor.
///
/// **Neden eksen değil de tek tek tuşlar?** Hareket eskiden
/// `Input.GetAxisRaw("Horizontal")` ile okunuyordu. Eski Input Manager'ın
/// eksenleri **çalışma anında değiştirilemiyor** — yeniden atama istiyorsak
/// tuşları tek tek okumak zorundayız. Sonuç aynı: eksen zaten -1/0/+1
/// döndürüyordu, biz de öyle üretiyoruz, hareketin hissi değişmiyor.
///
/// Bunun bir bedeli var ve bilinçli: **ok tuşlarıyla hareket düştü.** Eksen
/// onları da dinliyordu. Ok tuşlarıyla oynamak isteyen artık atamadan
/// değiştiriyor (zaten meselenin kendisi bu).
///
/// `KeyCode` fare düğmelerini de kapsıyor (`Mouse0`, `Mouse1`…), o yüzden
/// saldırı da buradan atanabiliyor; ayrı bir "fare mi tuş mu" ayrımı gerekmedi.
///
/// **Esc ve sunucu test tuşları ([1]-[4]) atanamıyor**, bilerek: Esc her
/// oyunda menüdür ve atanabilir olsaydı yanlış atama oyuncuyu menüsüz
/// bırakabilirdi.
///
/// CLAUDE.md bölüm 0'daki soyutlama kuralı burada işledi: girdi tek dosyadan
/// okunduğu için Input System'e geçiş hâlâ tek dosyalık iş.
/// </summary>
public static class KeyBindings
{
    private const string KeyPrefix = "Tus_";

    private static readonly GameAction[] AllActions =
        (GameAction[])Enum.GetValues(typeof(GameAction));

    /// <summary>Atanabilecek tüm eylemler — ayarlar ekranı bu sırayla çiziyor.</summary>
    public static GameAction[] Actions => AllActions;

    public static KeyCode Default(GameAction action)
    {
        switch (action)
        {
            case GameAction.Forward: return KeyCode.W;
            case GameAction.Back: return KeyCode.S;
            case GameAction.Left: return KeyCode.A;
            case GameAction.Right: return KeyCode.D;
            case GameAction.Jump: return KeyCode.Space;
            case GameAction.Sprint: return KeyCode.LeftShift;
            case GameAction.Crouch: return KeyCode.LeftControl;
            case GameAction.Interact: return KeyCode.E;
            case GameAction.Flashlight: return KeyCode.F;
            case GameAction.Attack: return KeyCode.Mouse0;
            default: return KeyCode.None;
        }
    }

    public static KeyCode Get(GameAction action)
    {
        int stored = PlayerPrefs.GetInt(KeyPrefix + action, (int)Default(action));
        return (KeyCode)stored;
    }

    /// <summary>
    /// Tuşu atar. Tuş başka bir eylemde kullanılıyorsa **ikisi yer değiştiriyor.**
    ///
    /// Diğerini boşa düşürmek daha basit olurdu ama oyuncuyu atanmamış bir
    /// eylemle bırakıyor ve bunu ancak oyunun ortasında fark ediyor. Takas,
    /// hiçbir eylemi tuşsuz bırakmayan tek davranış.
    /// </summary>
    public static void Set(GameAction action, KeyCode key)
    {
        if (key == KeyCode.None)
            return;

        KeyCode previous = Get(action);

        foreach (GameAction other in AllActions)
        {
            if (other == action || Get(other) != key)
                continue;

            PlayerPrefs.SetInt(KeyPrefix + other, (int)previous);
        }

        PlayerPrefs.SetInt(KeyPrefix + action, (int)key);
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        foreach (GameAction action in AllActions)
            PlayerPrefs.SetInt(KeyPrefix + action, (int)Default(action));

        PlayerPrefs.Save();
    }

    // ---------- Okuma ----------

    public static bool Held(GameAction action) => Input.GetKey(Get(action));

    public static bool Pressed(GameAction action) => Input.GetKeyDown(Get(action));

    /// <summary>İki zıt eylemden eksen üretir — eski GetAxisRaw ile aynı sonuç.</summary>
    public static float Axis(GameAction negative, GameAction positive)
    {
        float value = 0f;

        if (Held(positive))
            value += 1f;
        if (Held(negative))
            value -= 1f;

        return value;
    }

    // ---------- Metin ----------

    /// <summary>Eylemin ekranda görünecek adı.</summary>
    public static string DescribeAction(GameAction action)
    {
        switch (action)
        {
            case GameAction.Forward: return "İleri";
            case GameAction.Back: return "Geri";
            case GameAction.Left: return "Sola";
            case GameAction.Right: return "Sağa";
            case GameAction.Jump: return "Zıpla";
            case GameAction.Sprint: return "Koş";
            case GameAction.Crouch: return "Eğil";
            case GameAction.Interact: return "Etkileşim";
            case GameAction.Flashlight: return "Fener";
            case GameAction.Attack: return "Saldırı (canavar)";
            default: return action.ToString();
        }
    }

    /// <summary>
    /// Tuşun ekranda görünecek adı. `KeyCode.ToString()` "Alpha1", "Mouse0",
    /// "LeftShift" gibi çıktılar veriyor; oyuncunun klavyesinde bunlar yazmıyor.
    /// </summary>
    public static string Describe(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.None: return "—";

            case KeyCode.Mouse0: return "SOL FARE";
            case KeyCode.Mouse1: return "SAĞ FARE";
            case KeyCode.Mouse2: return "ORTA FARE";
            case KeyCode.Mouse3: return "FARE 4";
            case KeyCode.Mouse4: return "FARE 5";

            case KeyCode.Space: return "BOŞLUK";
            case KeyCode.LeftShift: return "SOL SHIFT";
            case KeyCode.RightShift: return "SAĞ SHIFT";
            case KeyCode.LeftControl: return "SOL CTRL";
            case KeyCode.RightControl: return "SAĞ CTRL";
            case KeyCode.LeftAlt: return "SOL ALT";
            case KeyCode.RightAlt: return "SAĞ ALT";
            case KeyCode.Return: return "ENTER";
            case KeyCode.KeypadEnter: return "NUM ENTER";
            case KeyCode.Tab: return "TAB";
            case KeyCode.CapsLock: return "CAPS LOCK";
            case KeyCode.Backspace: return "BACKSPACE";

            case KeyCode.UpArrow: return "YUKARI OK";
            case KeyCode.DownArrow: return "AŞAĞI OK";
            case KeyCode.LeftArrow: return "SOL OK";
            case KeyCode.RightArrow: return "SAĞ OK";
        }

        string name = key.ToString();

        // "Alpha1" → "1", "Keypad3" → "NUM 3"
        if (name.StartsWith("Alpha"))
            return name.Substring(5);

        if (name.StartsWith("Keypad"))
            return "NUM " + name.Substring(6);

        return name.ToUpperInvariant();
    }

    /// <summary>
    /// Yeniden atamada kabul edilmeyen tuşlar.
    ///
    /// Esc iptal için ayrılmış ve menüyü açan tuş; ona bir eylem atamak
    /// oyuncuyu menüsüz bırakabilirdi.
    /// </summary>
    public static bool IsForbidden(KeyCode key)
    {
        return key == KeyCode.None
            || key == KeyCode.Escape
            || (key >= KeyCode.JoystickButton0 && key <= KeyCode.Joystick8Button19);
    }
}
