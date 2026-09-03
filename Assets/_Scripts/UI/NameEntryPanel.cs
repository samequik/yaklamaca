using TMPro;
using UnityEngine;

/// <summary>
/// Oyuna ilk kez girildiğinde bir kez sorulan isim ekranı.
///
/// itch.io'da Steam gibi hazır bir isim kaynağı olmadığı için oyuncunun kendini
/// tanıtması gerekiyor. Bir kez soruluyor: girildikten sonra bir daha çıkmıyor,
/// değiştirmek isteyen Seçenekler'den değiştiriyor.
///
/// Boş bırakılırsa PlayerProfile varsayılanı ("Player") devreye giriyor; lobide
/// çakışırsa numaralandırma hallediyor (bkz. LobbyRoster).
/// </summary>
public class NameEntryPanel : MonoBehaviour
{
    [SerializeField] private MenuController menu;
    [SerializeField] private TMP_InputField nameField;

    private void Awake()
    {
        if (nameField == null)
            return;

        nameField.characterLimit = PlayerProfile.MaxNameLength;

        // Enter'a basmak da onaylasın; kutuya yazıp butona uzanmak zorunda kalma.
        nameField.onSubmit.AddListener(_ => Confirm());
    }

    private void OnEnable()
    {
        if (nameField == null)
            return;

        nameField.SetTextWithoutNotify(PlayerProfile.HasName ? PlayerProfile.Name : string.Empty);

        // İmleç doğrudan kutuda olsun, tıklamaya gerek kalmasın.
        nameField.Select();
        nameField.ActivateInputField();
    }

    /// <summary>Butonun ve Enter'ın çağırdığı onay.</summary>
    public void Confirm()
    {
        PlayerProfile.Name = nameField != null ? nameField.text : PlayerProfile.DefaultName;

        if (menu != null)
            menu.ShowMain();
    }
}
