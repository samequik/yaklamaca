using UnityEngine;

/// <summary>
/// Tutorial koridorundaki bir istasyona girince bir kere alt yazı gösteren
/// tetikleyici. Şablon Türkçe metin `Localization` sözlüğünün anahtarı;
/// `{0}` varsa oyuncunun GÜNCEL tuş atamasından dolduruluyor — sabit "W"
/// yazmak, tuşlarını değiştiren biri için yanlış olurdu (bkz. CLAUDE.md
/// bölüm 13'ün aynı dersi, `Terminal.DirectionLabel`).
/// </summary>
[RequireComponent(typeof(Collider))]
public class TutorialCaptionTrigger : MonoBehaviour
{
    [SerializeField] private string templateKey;
    [SerializeField] private bool hasKeyArg;
    [SerializeField] private GameAction keyArg;
    [SerializeField] private float displaySeconds = 6f;

    private bool fired;

    private void Awake() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (fired)
            return;

        RoundParticipant participant = other.GetComponentInParent<RoundParticipant>();
        if (participant == null)
            return;

        fired = true;

        string text = hasKeyArg
            ? Localization.Format(templateKey, KeyBindings.Describe(KeyBindings.Get(keyArg)))
            : Localization.Get(templateKey);

        TutorialHud.Show(text, displaySeconds);
    }

    public void Configure(string key, bool useKeyArg, GameAction action, float seconds)
    {
        templateKey = key;
        hasKeyArg = useKeyArg;
        keyArg = action;
        displaySeconds = seconds;
    }
}
