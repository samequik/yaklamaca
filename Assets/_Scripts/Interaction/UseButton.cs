using UnityEngine;

/// <summary>
/// Duvara monte düğme. Basılınca bağlı tüm Triggerable'ları çalıştırır.
/// Kapı, asansör, alarm — hepsi aynı düğmeye bağlanabilir; düğme neyi
/// tetiklediğiyle ilgilenmez.
/// </summary>
public class UseButton : MonoBehaviour, IInteractable
{
    [SerializeField] private string prompt = "Kullan";
    [SerializeField] private Triggerable[] targets;

    [Tooltip("Bu düğme için en az bekleme. Asıl kural sunucuda ve KİŞİ BAŞI " +
        "(bkz. Triggerable.UserCooldown); buradaki yalnızca yerel geri bildirim " +
        "— hedefin süresi bundan uzunsa o kullanılıyor, yoksa oyuncu basıp " +
        "hiçbir şey olmadığını görürdü.")]
    [SerializeField] private float cooldown = 0.15f;

    [Header("Görsel Geri Bildirim")]
    [SerializeField] private Transform pressVisual; // basınca içeri giren parça
    [SerializeField] private Vector3 pressDirection = Vector3.forward; // parçanın kendi ekseninde duvara doğru
    [SerializeField] private float pressDepth = 0.03f;
    [SerializeField] private float pressDuration = 0.12f;

    private float nextUseTime;
    private Vector3 visualRestPosition;
    private float pressTimer;
    private int sourceId;

    private void Awake()
    {
        if (pressVisual != null)
            visualRestPosition = pressVisual.localPosition;

        // Kimlik kardeş sırasından: sahne dosyasından geldiği için her
        // istemcide aynı ve elle numara vermek gerekmiyor. Aynı kapının iki
        // düğmesi aynı kökün altında farklı sıralarda duruyor.
        sourceId = transform.GetSiblingIndex();
    }

    /// <summary>
    /// Hedefin "çalıştım" haberine abone oluyoruz: karşı oyuncunun bastığı
    /// düğme artık bizim ekranımızda da içeri giriyor. Haber ağdan geliyor
    /// (bkz. Triggerable.ServerNotifyActivated), düğmenin kendisi ağ nesnesi
    /// değil.
    /// </summary>
    private void OnEnable() => SubscribeTargets(true);

    private void OnDisable() => SubscribeTargets(false);

    private void SubscribeTargets(bool subscribe)
    {
        if (targets == null)
            return;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null)
                continue;

            if (subscribe)
                targets[i].Activated += OnTargetActivated;
            else
                targets[i].Activated -= OnTargetActivated;
        }
    }

    /// <summary>
    /// Hedef çalıştı. **Yalnızca basılan düğme** içeri giriyor: aynı kapıya iki
    /// düğme bağlı ve ikisi de bu olaya abone, kimlik kontrolü olmasaydı biri
    /// basılınca ikisi birden hareket ederdi.
    /// </summary>
    private void OnTargetActivated(int activatedSource)
    {
        if (activatedSource == sourceId)
            PlayPressVisual();
    }

    private void PlayPressVisual() => pressTimer = pressDuration;

    /// <summary>
    /// Yerel beklemenin süresi: hedefin kişi başı cooldown'u ile bu düğmenin
    /// kendi değerinden büyük olanı.
    ///
    /// Hedeften okumak şart — iki sayı ayrı ayrı ayarlansaydı sahnedeki
    /// düğmede eski (kısa) değer kalır, oyuncu basar, sunucu reddeder ve
    /// ekranda hiçbir şey olmazdı. Sebebi görünmeyen bir sessizlik.
    /// </summary>
    private float ResolveCooldown()
    {
        float longest = cooldown;

        if (targets == null)
            return longest;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
                longest = Mathf.Max(longest, targets[i].UserCooldown);
        }

        return longest;
    }

    private void Update()
    {
        if (pressVisual == null)
            return;

        // Basılma animasyonu: içeri gir, sonra yerine dön.
        pressTimer = Mathf.Max(pressTimer - Time.deltaTime, 0f);
        float pressed = pressDuration > 0f ? pressTimer / pressDuration : 0f;
        pressVisual.localPosition = visualRestPosition + pressDirection.normalized * (pressDepth * pressed);
    }

    // Hedeflerden biri meşgulse (kapı açılıyor/kapanıyor) yazı hiç çıkmaz.
    // Boş prompt "şu an kullanılamaz" demek, bkz. IInteractable.
    public string GetPrompt() => CanUse() ? prompt : null;

    private bool CanUse()
    {
        if (Time.time < nextUseTime)
            return false;
        if (targets == null)
            return false;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null && !targets[i].CanActivate)
                return false;
        }

        return true;
    }

    public void Interact(GameObject user)
    {
        if (!CanUse())
            return;

        nextUseTime = Time.time + ResolveCooldown();

        // Basanın kendi geri bildirimi anında: ağ turunu beklemek, kendi
        // bastığın düğmenin gecikmeli tepki vermesi demekti. Karşı taraf aynı
        // animasyonu birazdan hedefin haberiyle görüyor (CLAUDE.md bölüm 4:
        // his istemcide, karar sunucuda).
        PlayPressVisual();

        if (targets == null)
            return;

        foreach (Triggerable target in targets)
        {
            if (target != null)
                target.Activate(user, sourceId);
        }
    }
}
