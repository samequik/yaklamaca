using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Çıkış kilidi paneli: on adımlık yön dizilimi.
///
/// `ExitLock.OnGUI`'nin yerini alıyor (teknik borç 2). Görsel dil korundu —
/// **her şey sarının bir tonu.** Tamamlanan adımlar bir ara yeşildi; panelin
/// kimliği renkten geldiği için sönük sarıya çevrilmişti (bölüm 18).
///
/// ### Hücreler önceden kuruluyor
///
/// On hücre menü kurulurken bir kez yaratılıyor ve sonra yalnızca renkleri ile
/// yazıları değişiyor. Çalışma anında obje yaratmak bölüm 2'nin havuzlama
/// kuralına takılırdı — üstelik dizilim uzunluğu sabit.
///
/// ### Sıradaki hücre DOLU, yazısı koyu
///
/// Göz sıradakini aramak zorunda kalmıyor. Tamamlananlar sönük, gelecekler
/// daha da sönük; okunan tek şey "şimdi hangisi".
///
/// ### Ok yerine tuş harfine düşebiliyor
///
/// Oklar temel Latin dışında ve varsayılan TMP atlası statik. `GameHud.Glyph`
/// karakteri sınayıp yoksa **basılacak tuşun harfine** çeviriyor — boş kutu
/// göstermektense W/S/A/D göstermek her açıdan daha iyi, üstelik oyuncunun
/// gerçekten basacağı şey o.
/// </summary>
public class ExitLockScreen : MonoBehaviour
{
    /// <summary>Tek adım hücresi.</summary>
    [System.Serializable]
    public class Cell
    {
        public Image background;
        public Image border;
        public TMP_Text label;
    }

    [SerializeField] private CanvasGroup group;
    [SerializeField] private Cell[] cells;

    [SerializeField] private RectTransform barFill;
    [SerializeField] private TMP_Text captionLabel;

    [Header("Renkler")]
    [SerializeField] private Color accent = new Color(0.95f, 0.8f, 0.15f);
    [SerializeField] private Color accentDim = new Color(0.52f, 0.43f, 0.10f);
    [SerializeField] private Color ink = new Color(0.05f, 0.045f, 0.02f);
    [SerializeField] private Color doneCell = new Color(0.16f, 0.14f, 0.05f);
    [SerializeField] private Color futureCell = new Color(0.085f, 0.082f, 0.06f);
    [SerializeField] private Color futureBorder = new Color(0.17f, 0.17f, 0.14f);
    [SerializeField] private Color futureText = new Color(0.42f, 0.41f, 0.35f);
    [SerializeField] private Color captionColor = new Color(0.55f, 0.52f, 0.38f);

    private void Update()
    {
        ExitLock lockPanel = ExitLock.ActiveLocal;
        bool active = GameHud.Visible && lockPanel != null;

        GameHud.SetVisible(group, active);

        if (!active || cells == null)
            return;

        int entered = lockPanel.Entered;
        int length = ExitLock.SequenceLength;

        for (int i = 0; i < cells.Length; i++)
            ApplyCell(cells[i], lockPanel, i, entered, i < length);

        if (barFill != null)
            barFill.anchorMax = new Vector2(length > 0 ? entered / (float)length : 0f, 1f);

        if (captionLabel == null)
            return;

        captionLabel.color = captionColor;
        captionLabel.SetText($"{entered} / {length}      yanlış tuş başa sarar");
    }

    private void ApplyCell(Cell cell, ExitLock lockPanel, int index, int entered, bool used)
    {
        if (cell?.background == null)
            return;

        if (cell.background.gameObject.activeSelf != used)
            cell.background.gameObject.SetActive(used);

        if (!used)
            return;

        bool done = index < entered;
        bool current = index == entered;

        cell.background.color = current ? accent : done ? doneCell : futureCell;

        if (cell.border != null)
        {
            // Sıradaki hücrenin çerçevesi YOK: dolu sarı zaten kendini
            // ayırıyor, üstüne çerçeve koymak onu kutulaştırıyordu.
            cell.border.enabled = !current;
            cell.border.color = done ? accentDim : futureBorder;
        }

        if (cell.label == null)
            return;

        cell.label.color = current ? ink : done ? accentDim : futureText;
        cell.label.SetText(GameHud.Glyph(cell.label, lockPanel.ArrowAt(index),
            lockPanel.KeyAt(index)));
    }
}
