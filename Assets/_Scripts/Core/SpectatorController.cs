using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Elenen oyuncunun kamerasını hayattaki kaçanlara çevirir.
///
/// Yakalanan anında ölüyor (yerde sürünme, kaldırılma yok) ama tur bitene kadar
/// izleyerek bekliyor — hem beklemeyi katlanılır kılıyor hem kimin ne yaptığını
/// görmek turun sonunu heyecanlı tutuyor.
///
/// Kamera oyuncunun child'ı olarak kalıyor; PlayerController elenince
/// kapatıldığı için kimse pozisyonunu yazmıyor, biz doğrudan sürebiliyoruz.
///
/// ---
///
/// **Canavar asla izlenmez.** Elenen oyuncunun canavarın nerede olduğunu
/// görmesi, sesli konuştuğunuz bir oyunda doğrudan hile olur: ölen kişi
/// hayattakilere "arkanda" der. Bu yüzden hedef listesine yalnızca hayattaki
/// kaçanlar giriyor; kimse kalmadıysa kamera son hedefte kalıyor, canavara
/// düşmüyor.
///
/// Hedefler sunucudan sorulmuyor: RoundManager.Participants yalnızca sunucuda
/// dolu. İstemcide zaten tüm oyuncu objeleri spawn edilmiş durumda, rol ve
/// canlılık da SyncVar — liste sahneden toplanıp netId'ye göre sıralanıyor,
/// böylece tıklayarak geçiş her istemcide aynı sırayla ilerliyor.
/// </summary>
public class SpectatorController : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;

    [Tooltip("Ölünce söndürülür — izlerken fener taşımak anlamsız.")]
    [SerializeField] private Flashlight flashlight;

    [Tooltip("Sıradaki hedefe geçiş. Elenmişken sol tık boşta — canavar olmadığın için bıçak yok.")]
    [SerializeField] private KeyCode nextTargetKey = KeyCode.Mouse0;

    [Tooltip("Başlangıç duruşu: hedefin arkasında/üstünde durulacak nokta. Mesafe ve " +
        "yükseklik buradan okunuyor, sonrasını fare çeviriyor.")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 1.9f, -3f);

    [Tooltip("Kameranın dikey açı sınırı (derece). Negatif alt sınır = hedefin altından yukarı bakış.")]
    [SerializeField] private Vector2 orbitPitchLimits = new Vector2(-15f, 75f);

    [Tooltip("Yumuşatma. Fareyle çevrilen bir kamerada düşük değer sünger gibi hissettiriyor.")]
    [SerializeField] private float followSmoothing = 14f;

    [Tooltip("Kamera duvara girmesin diye ışın atılır; çarparsa içeri çekilir. " +
        "Yakalamaca > Katmanları Kur bunu Harita yapıyor; ~0 bırakılırsa " +
        "arkadan geçen bir oyuncu da kamerayı öne zıplatır.")]
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Tooltip("Hedef listesinin kaç saniyede bir yenileneceği. Her karede sahne taramak israf.")]
    [SerializeField] private float targetRefreshInterval = 0.5f;

    private readonly List<RoundParticipant> targets = new List<RoundParticipant>();
    private RoundParticipant self;
    private IMovementInputSource inputSource;
    private bool isSpectating;
    private float nextRefreshTime;

    // Yörünge açıları. Hedefin dönüşünden bağımsız tutuluyor: kamera hedefin
    // arkasına yapışık olsaydı, izlediğin kişi her dönüşünde senin bakışın da
    // sürüklenirdi.
    private float orbitYaw;
    private float orbitPitch;
    private float orbitDistance;
    private float defaultOrbitPitch;

    // İzleme başlamadan önceki kamera duruşu; bittiğinde geri konuyor.
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;

    // Hedefi indeksle değil netId ile takip ediyoruz: liste yenilenince biri
    // elenip düşerse indeks kayar ve kamera kendiliğinden başkasına atlardı.
    private uint currentTargetNetId;

    /// <summary>Arayüzün kimi izlediğimizi yazabilmesi için.</summary>
    public string CurrentTargetName { get; private set; }

    public bool IsSpectating => isSpectating;

    private void Awake()
    {
        self = GetComponent<RoundParticipant>();

        // Fare hassasiyeti oyunla aynı kaynaktan gelsin: girdi kaynağı zaten
        // ayarlardaki hassasiyeti uygulayıp derece olarak veriyor. Elenince
        // PlayerController kapanıyor ama girdi kaynağı açık kalıyor.
        inputSource = GetComponent<IMovementInputSource>();

        // Beğenilen mesafe ve yükseklik followOffset'te duruyor; yörünge onu
        // koruyarak devralsın diye uzaklığa ve açıya çeviriyoruz.
        orbitDistance = followOffset.magnitude;

        float flatDistance = new Vector2(followOffset.x, followOffset.z).magnitude;
        defaultOrbitPitch = Mathf.Atan2(followOffset.y, flatDistance) * Mathf.Rad2Deg;
        orbitPitch = defaultOrbitPitch;
    }

    /// <summary>
    /// Bu obje bizim oyuncumuz mu. NetworkBehaviour'a çevirip isLocalPlayer
    /// kullanmıyoruz: bu bileşenin senkronlanan hiçbir durumu yok, ayrıca
    /// sahnede ağ öncesinden kalan kapalı Player'ın üstünde de duruyor — orası
    /// NetworkIdentity'siz olduğu için Mirror hata yazardı.
    /// </summary>
    private bool IsLocalPlayerObject =>
        NetworkClient.localPlayer != null &&
        NetworkClient.localPlayer.gameObject == gameObject;

    private void LateUpdate()
    {
        if (!IsLocalPlayerObject)
            return;

        RoundManager manager = RoundManager.Instance;

        // Üç yoldan izleyici olunuyor: elenmek, kurtulmak ve tura geç katılmak.
        // Üçünün de sonucu aynı — sahada değilsin, hayattaki kaçanları izliyorsun.
        bool shouldSpectate = manager != null
            && manager.Phase == RoundPhase.Playing
            && self != null
            && (!self.IsAlive || self.IsEscaped || self.IsSpectating);

        if (shouldSpectate != isSpectating)
        {
            isSpectating = shouldSpectate;
            OnSpectatingChanged();
        }

        if (!isSpectating || cameraTransform == null)
            return;

        if (Time.time >= nextRefreshTime)
        {
            nextRefreshTime = Time.time + targetRefreshInterval;
            RefreshTargets();
        }

        if (targets.Count == 0)
        {
            CurrentTargetName = null;
            return;
        }

        int index = targets.FindIndex(target => target.netId == currentTargetNetId);
        if (index < 0)
            index = 0;

        if (Input.GetKeyDown(nextTargetKey))
            index = (index + 1) % targets.Count;

        RoundParticipant current = targets[index];

        if (current.netId != currentTargetNetId)
        {
            currentTargetNetId = current.netId;
            SnapOrbitBehind(current);
        }

        CurrentTargetName = current.DisplayName;

        UpdateOrbit();
        FollowTarget(current);
    }

    /// <summary>
    /// Yeni hedefe geçince kamerayı onun arkasına alır — tanıdık bir başlangıç.
    /// Sonrasını fare devralıyor, bir daha kendiliğinden oynamıyor.
    /// </summary>
    private void SnapOrbitBehind(RoundParticipant target)
    {
        orbitYaw = target.transform.eulerAngles.y;
        orbitPitch = defaultOrbitPitch;
    }

    private void UpdateOrbit()
    {
        if (inputSource == null)
            return;

        MovementIntent intent = inputSource.Read();

        orbitYaw += intent.lookYaw;

        // Fare yukarı → kamera aşağı iner ve hedefe alttan bakarsın. İşaret
        // birinci şahıstakiyle aynı: PlayerController da lookPitch'i çıkararak
        // uyguluyor, böylece ölmeden önceki alışkanlığın bozulmuyor.
        orbitPitch = Mathf.Clamp(
            orbitPitch - intent.lookPitch, orbitPitchLimits.x, orbitPitchLimits.y);
    }

    private void OnSpectatingChanged()
    {
        if (flashlight != null)
            flashlight.SetOn(!isSpectating);

        if (cameraTransform == null)
            return;

        if (isSpectating)
        {
            // İzlerken kamerayı dünya koordinatıyla sürüyoruz. Tur bitip lobiye
            // dönünce kimse local değerleri geri koymuyor — PlayerController
            // yalnızca göz yüksekliğini (y) ve pitch'i yazıyor, x/z izleyici
            // kamerasının bıraktığı yerde kalırdı. Bu yüzden saklıyoruz.
            restLocalPosition = cameraTransform.localPosition;
            restLocalRotation = cameraTransform.localRotation;
            return;
        }

        cameraTransform.localPosition = restLocalPosition;
        cameraTransform.localRotation = restLocalRotation;
    }

    /// <summary>
    /// Hayattaki kaçanlar. Canavar bilerek listeye alınmıyor — bkz. sınıf notu.
    /// </summary>
    private void RefreshTargets()
    {
        targets.Clear();

        foreach (RoundParticipant other in FindObjectsOfType<RoundParticipant>())
        {
            if (other == null || other == self)
                continue;
            if (other.Role != RoundRole.Runner || !other.IsAlive || other.IsEscaped)
                continue;

            targets.Add(other);
        }

        // FindObjectsOfType'ın sırası garanti değil; netId sabit ve her
        // istemcide aynı, tıklayarak geçiş bu yüzden tutarlı ilerliyor.
        targets.Sort((a, b) => a.netId.CompareTo(b.netId));
    }

    private void FollowTarget(RoundParticipant target)
    {
        Vector3 pivot = target.transform.position + Vector3.up * 1f;

        // Yörünge hedefin dönüşünü değil farenin açısını kullanıyor. Eskiden
        // offset hedefin rotasyonuyla çarpılıyordu; kamera arkasına yapışıktı ve
        // izlenen kişi döndükçe senin bakışın da sürükleniyordu.
        Quaternion orbit = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 desired = pivot + orbit * Vector3.back * orbitDistance;

        // Labirentte üçüncü şahıs kamera sürekli duvara girer; hedefe doğru
        // ışın atıp çarptığımız noktanın biraz berisinde duruyoruz.
        Vector3 direction = desired - pivot;
        float distance = direction.magnitude;

        if (distance > 0.01f &&
            Physics.Raycast(pivot, direction / distance, out RaycastHit hit, distance,
                obstacleMask, QueryTriggerInteraction.Ignore))
        {
            desired = hit.point - direction / distance * 0.3f;
        }

        cameraTransform.position = Vector3.Lerp(
            cameraTransform.position, desired, followSmoothing * Time.deltaTime);

        cameraTransform.rotation = Quaternion.Slerp(
            cameraTransform.rotation,
            Quaternion.LookRotation(pivot - cameraTransform.position),
            followSmoothing * Time.deltaTime);
    }
}
