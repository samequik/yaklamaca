using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tutorial sahnesinin tek bilinçli parçası: sahne açılınca kendi kendine
/// bir host açar, tek oyunculuk bir tur başlatır ve tur bitince (koridorun
/// sonundaki çıkıştan geçilince) ana menüye döner.
///
/// **Neden lobi yok.** Tutorial'a giren zaten menüden geliyor; ikinci bir
/// "LOBİ KUR" ekranı göstermek adımı gereksiz yere uzatırdı. `StartHost` +
/// `StartRoundAsRunner` sunucu penceresindeki [2] tuşunun aynısı, yalnızca
/// elle basmak yerine sahne açılır açılmaz kendiliğinden çalışıyor.
///
/// **Neden `StartRoundAsRunner`, düz `StartRound` değil.** Tutorial'da
/// canavar diye biri yok; `forcedRunner` olmadan `PickMonster` tek adayı
/// (oyuncunun kendisi) canavar seçebilirdi ve tur hiç ilerlemezdi.
///
/// **Bitiş, oyuncunun KENDİSİ kaçınca.** Turun bitmesini beklemek yetmiyor:
/// tutorial eğitim botunu diriltmeyi öğretiyor ve dirilen bot sahada kaldığı
/// sürece bölüm 11.1'in kuralı gereği tur bitmiyor — oyuncu çıkıştan geçip
/// izleyiciye düşüyor ve orada asılı kalıyordu.
/// </summary>
public class TutorialBootstrap : MonoBehaviour
{
    private const string FinishedMessage = "Tebrikler, hazırsın! Ana menüye dönülüyor…";
    private const string EndedMessage = "Tutorial bitti. Ana menüye dönülüyor…";

    [SerializeField] private string mainMenuSceneName = "SampleScene";
    [SerializeField] private float readyTimeout = 10f;

    private RoundPhase lastPhase = RoundPhase.Waiting;
    private bool started;
    private bool finishing;
    private bool returning;

    private IEnumerator Start()
    {
        // Bir kare bekleniyor: sahnedeki NetworkIdentity'ler ve RoundManager
        // henüz Awake'lerini tamamlamamış olabilir.
        yield return null;

        if (!NetworkServer.active && !NetworkClient.active)
        {
            NetworkManager manager = NetworkManager.singleton;

            if (manager == null)
            {
                Debug.LogError("TutorialBootstrap: sahnede NetworkManager yok, tutorial başlatılamadı.");
                yield break;
            }

            // DİNLEMEYEN bir host: tutorial tek kişilik, dışarıdan kimsenin
            // bağlanması gerekmiyor (Mirror'ın kendi tarifi: "Single player
            // mode can set listen=false"). Bu satır yokken StartHost o an hangi
            // transport takılıysa onun sunucusunu açıyordu — oyuncu daha önce
            // internet odası kurduysa EOS, kurmadıysa KCP (UDP 7777). KCP
            // yayınlanan oyunda Windows güvenlik duvarı uyarısı çıkarır, EOS
            // internetsiz makinede sunucuyu hiç açamaz; ikisi de bir öğretici
            // için anlamsız bir bağımlılık.
            //
            // Geri almak GEREKMİYOR: StopHost → NetworkServer.Shutdown bayrağı
            // kendisi true'ya çeviriyor, menüdeki bir sonraki LOBİ KUR normal
            // şekilde dinliyor.
            NetworkServer.listen = false;
            manager.StartHost();
        }

        float deadline = Time.unscaledTime + readyTimeout;

        while ((RoundManager.Instance == null || NetworkClient.localPlayer == null)
            && Time.unscaledTime < deadline)
        {
            yield return null;
        }

        if (RoundManager.Instance == null || NetworkClient.localPlayer == null)
        {
            Debug.LogError("TutorialBootstrap: RoundManager ya da yerel oyuncu " +
                $"{readyTimeout} sn içinde hazır olmadı.");
            yield break;
        }

        RoundManager.Instance.StartRoundAsRunner();
        started = true;
    }

    private void Update()
    {
        // **Esc BURADA DİNLENMİYOR, bilerek.** Eskiden tek basışta ana menüye
        // dönülüyordu ve oyuncu yanlışlıkla bastığında koridor kapanıyordu.
        // Artık Esc'yi `MenuController` yakalıyor ve normal oyundaki duraklatma
        // menüsünü açıyor (DEVAM ET / SEÇENEKLER / TUTORIAL'DAN ÇIK); çıkış
        // düğmesi aşağıdaki `ReturnToMenu`'yu çağırıyor. Menü tutorial
        // sahnesine `MenuSetup.BuildTutorialMenu` ile kuruluyor.
        //
        // İkisi birden dinlenemez: aynı Esc hem menüyü açar hem sahneyi
        // kapatırdı (bölüm 13'ün `KeyBindingPanel.BlocksEscape` tuzağının
        // aynısı).
        if (!started || finishing || returning || RoundManager.Instance == null)
            return;

        // Asıl bitiş: oyuncu çıkıştan geçti. Turun kapanmasını beklemiyoruz,
        // sınıf yorumundaki dirilen bot yüzünden hiç kapanmayabilir.
        RoundParticipant local = RoundParticipant.Local;
        if (local != null && local.IsEscaped)
        {
            Finish(FinishedMessage);
            return;
        }

        RoundPhase phase = RoundManager.Instance.Phase;
        if (phase == lastPhase)
            return;

        // Tur kaçmadan kapandıysa (ör. test tuşuyla elenmek) yine menüye
        // dönülüyor, ama "tebrikler" denmiyor — hak edilmemiş bir övgü olurdu.
        if (lastPhase == RoundPhase.Ended && phase == RoundPhase.Waiting)
            Finish(EndedMessage);

        lastPhase = phase;
    }

    private void Finish(string messageKey)
    {
        finishing = true;
        TutorialHud.ShowNow(Localization.Get(messageKey), 4f);
        Invoke(nameof(ReturnToMenu), 3f);
    }

    /// <summary>
    /// Koridoru kapatıp ana menüye döner. **public**, çünkü duraklatma
    /// ekranındaki `TUTORIAL'DAN ÇIK` düğmesi buna kalıcı dinleyiciyle
    /// bağlanıyor (`UnityEventTools.AddPersistentListener` yalnızca public,
    /// parametresiz metotları bağlayabiliyor). Bitiş akışı da (`Finish`)
    /// aynı metodu çağırıyor — iki yol tek çıkışta birleşiyor.
    /// </summary>
    public void ReturnToMenu()
    {
        if (returning)
            return;

        returning = true;
        TutorialHud.Clear();

        NetworkManager manager = NetworkManager.singleton;

        if (manager != null)
        {
            if (NetworkServer.active)
                manager.StopHost();
            else if (NetworkClient.active)
                manager.StopClient();
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }
}
