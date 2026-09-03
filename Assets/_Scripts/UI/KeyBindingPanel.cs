using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tuş atama ekranı. Her satır bir eylem: solda adı, sağda basılabilir bir
/// düğmede o anki tuşu. Düğmeye basınca satır "dinleme" moduna geçiyor ve bir
/// sonraki tuşu alıyor.
///
/// **Tuş yakalama neden bütün KeyCode'ları tarıyor?** Eski Input Manager'da
/// "hangi tuşa basıldı" diye soran bir API yok; `Event.current.keyCode` var ama
/// o `OnGUI` gerektiriyor ve projede IMGUI'den çıkılıyor (teknik borç 2).
/// Tarama yalnızca dinleme sırasında çalışıyor ve birkaç yüz enum değerini
/// gezmenin ölçülebilir bir maliyeti yok.
///
/// Esc dinlemeyi iptal ediyor; bu yüzden Esc'nin kendisi atanamıyor
/// (<see cref="KeyBindings.IsForbidden"/>).
/// </summary>
public class KeyBindingPanel : MonoBehaviour
{
    [System.Serializable]
    public class Row
    {
        public GameAction action;
        public TMP_Text nameLabel;
        public TMP_Text keyLabel;
        public Button button;
    }

    [SerializeField] private MenuController menu;
    [SerializeField] private Row[] rows;
    [SerializeField] private TMP_Text hintLabel;

    [SerializeField] private Color listeningColor = new Color(0.75f, 0.2f, 0.16f, 1f);
    [SerializeField] private Color idleColor = new Color(0.16f, 0.16f, 0.2f, 1f);

    private static readonly KeyCode[] AllKeys =
        (KeyCode[])System.Enum.GetValues(typeof(KeyCode));

    private int listeningIndex = -1;

    // Dinlemeyi başlatan tıklamanın kendisi yakalanmasın diye bir kare bekleniyor.
    private bool skipFrame;

    private static bool listening;
    private static int lastListeningFrame = -1;

    /// <summary>
    /// Esc bu ekranda tuş atamasını iptal ediyor; menünün de aynı Esc'yle geri
    /// gitmesi tek basışta iki iş yapardı.
    ///
    /// Kare numarası da tutuluyor çünkü Update sırası garanti değil: dinleme
    /// bu karede iptal edilmiş olsa bile MenuController hangi sırayla çalışırsa
    /// çalışsın doğru cevabı alıyor.
    /// </summary>
    public static bool BlocksEscape => listening || lastListeningFrame == Time.frameCount;

    private void OnEnable()
    {
        StopListening();
        Refresh();
    }

    private void OnDisable() => StopListening();

    private void Update()
    {
        if (listeningIndex < 0)
            return;

        lastListeningFrame = Time.frameCount;

        if (skipFrame)
        {
            skipFrame = false;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            StopListening();
            Refresh();
            return;
        }

        foreach (KeyCode key in AllKeys)
        {
            if (KeyBindings.IsForbidden(key) || !Input.GetKeyDown(key))
                continue;

            KeyBindings.Set(rows[listeningIndex].action, key);
            StopListening();
            Refresh();
            return;
        }
    }

    /// <summary>Satır düğmesi bunu çağırıyor; index kalıcı dinleyiciden geliyor.</summary>
    public void BeginListening(int index)
    {
        if (rows == null || index < 0 || index >= rows.Length)
            return;

        listeningIndex = index;
        listening = true;
        lastListeningFrame = Time.frameCount;
        skipFrame = true;
        Refresh();
    }

    /// <summary>Tüm atamaları fabrika ayarına döndürür.</summary>
    public void ResetToDefaults()
    {
        KeyBindings.ResetToDefaults();
        StopListening();
        Refresh();
    }

    /// <summary>GERİ düğmesi.</summary>
    public void Close()
    {
        StopListening();

        // ShowSettings değil: o metot "nereden gelindi" diye kaydediyor ve
        // buradan çağrılırsa Seçenekler'in GERİ'si tuşlara geri atardı.
        if (menu != null)
            menu.CloseControls();
    }

    private void StopListening()
    {
        listeningIndex = -1;
        listening = false;
    }

    private void Refresh()
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            Row row = rows[i];
            if (row == null)
                continue;

            if (row.nameLabel != null)
                row.nameLabel.SetText(KeyBindings.DescribeAction(row.action));

            if (row.keyLabel != null)
            {
                row.keyLabel.SetText(i == listeningIndex
                    ? "bir tuşa bas…"
                    : KeyBindings.Describe(KeyBindings.Get(row.action)));
            }

            if (row.button != null && row.button.targetGraphic != null)
                row.button.targetGraphic.color = i == listeningIndex ? listeningColor : idleColor;
        }

        if (hintLabel != null)
        {
            hintLabel.SetText(listeningIndex >= 0
                ? "Yeni tuşa bas. Vazgeçmek için Esc."
                : "Değiştirmek istediğin tuşa tıkla. Tuş başkasındaysa ikisi yer değiştirir.");
        }
    }
}
