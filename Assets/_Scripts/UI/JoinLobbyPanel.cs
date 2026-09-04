using TMPro;
using UnityEngine;

/// <summary>
/// Kodla odaya katılma ekranı. Tek işi girilen metni
/// <see cref="LobbyNetwork"/>'e vermek; kodu çözmek ve bağlanmak onun işi.
///
/// Alan ham IP de kabul ediyor: yerel test odasına girmenin tek yolu o. Sanal ağ
/// üzerinden ya da dışarıdan verilen bir adresle oynayan birini kod üretmeye
/// zorlamak gereksiz bir engel olurdu.
/// </summary>
public class JoinLobbyPanel : MonoBehaviour
{
    [SerializeField] private LobbyNetwork network;
    [SerializeField] private TMP_InputField codeField;
    [SerializeField] private TMP_Text statusLabel;

    private void OnEnable()
    {
        if (codeField != null)
        {
            codeField.text = string.Empty;
            codeField.Select();
            codeField.ActivateInputField();
        }

        RefreshStatus();
    }

    private void Update() => RefreshStatus();

    /// <summary>"KATIL" düğmesi ve giriş alanında Enter.</summary>
    public void Join()
    {
        if (network == null || codeField == null)
            return;

        network.JoinLobby(codeField.text);
    }

    private void RefreshStatus()
    {
        if (statusLabel == null)
            return;

        statusLabel.SetText(network != null && !string.IsNullOrEmpty(network.StatusMessage)
            ? network.StatusMessage
            : $"Arkadaşının verdiği {LobbyCode.Length} harflik kodu ya da IP adresini gir.");
    }
}
