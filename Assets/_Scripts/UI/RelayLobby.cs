using System;
using System.Collections;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using EpicTransport;
using UnityEngine;

// `Attribute` iki yerde birden tanımlı: `System.Attribute` (öznitelik tabanı)
// ve Epic'in lobi özniteliği. `using System;` durduğu sürece çıplak ad belirsiz
// kalıyor ve derleyici hata veriyor — takma ad ikisini ayırıyor.
using EosAttribute = Epic.OnlineServices.Lobby.Attribute;

/// <summary>
/// EOS'un lobi servisi üzerine **kısa oda kodu**.
///
/// ### Neden gerekti
///
/// EOS relay'i çalışıyor ama adres host'un `ProductUserId`'si: 32 karakterlik
/// bir metin. Kopyalanabiliyor, **söylenemiyor** — oysa oyun sesli sohbette
/// oynanıyor ve "odama gel" demenin yolu kodu okumak. Lobi servisi araya
/// giriyor: host 6 harflik rastgele bir kod üretip odaya öznitelik olarak
/// yazıyor, katılan o kodla arıyor ve odanın içindeki uzun adresi EOS'tan
/// alıyor. Uzun kod kaybolmuyor, yalnızca artık oyuncunun gözüne görünmüyor.
///
/// ### Neden `EOSLobby`'den türüyor
///
/// Paket lobi işini zaten yapıyor (`Lobby/EOSLobby.cs`): oda kurma, arama,
/// katılma, öznitelik yazma. Onu kopyalamak dört yüz satır SDK borusunu ikinci
/// kez yazmak olurdu; **doğrudan düzenlemek** ise pakete beşinci bir yerel yama
/// eklemek demekti (CLAUDE.md bölüm 9 — her yama paket güncellenince
/// kayboluyor). Türetmek ikisinden de kaçınıyor.
///
/// Bu sınıfın eklediği üç şey: kısa kod, **cevapsız kalmama garantisi** ve EOS
/// açılmamışken patlamama.
///
/// ### Base'in `Start`'ı çağrılmıyor — bilerek
///
/// `EOSLobby.Start()` ilk satırında `EOSSDKComponent.GetLobbyInterface()`
/// çağırıyor ve o metot `Instance.EOS`'a null kontrolü yapmadan dokunuyor. EOS
/// açılmamışsa (kimlik yanlış, P2P izni kapalı, internet yok) sahne yüklenir
/// yüklenmez `NullReferenceException` atardı — üstelik yerel odayla oynamak
/// isteyen birinin EOS'la hiç işi yokken. Bildirim kaydı bu yüzden **EOS hazır
/// olunca**, ilk istekte yapılıyor.
///
/// ### Her isteğin tam olarak bir cevabı var
///
/// EOS geri çağrıları asenkron ve **hiç gelmeyebilir** (ağ koptu, servis
/// takıldı). Cevapsız kalan bir istek oyuncuyu "Oda kuruluyor…" ekranında
/// sonsuza kadar asılı bırakırdı. Her istek bir sayaçla başlıyor: cevap gelirse
/// sayaç iptal, gelmezse sayaç başarısızlığı kendisi bildiriyor. Sonuç iki
/// uçtan hangisi önce gelirse o — ama <b>yalnızca biri</b> (bkz.
/// <see cref="Claim"/>).
/// </summary>
public class RelayLobby : EOSLobby
{
    /// <summary>Odanın kısa kodunu taşıyan öznitelik.</summary>
    public const string CodeKey = "room_code";

    /// <summary>Oda listesinde görünecek ad — host'un oyuncu adı.</summary>
    public const string NameKey = "room_name";

    /// <summary>
    /// Base'in her odaya yazdığı "bu oyunun odası" işareti.
    ///
    /// Değer `EOSLobby.DefaultAttributeKey`'in kopyası ve orada **private**:
    /// erişilemiyor, o yüzden burada tekrar yazılı. Paket bu sabiti
    /// değiştirirse kod araması sessizce boş dönmeye başlar — tek bağımlılık
    /// noktası burası.
    /// </summary>
    private const string DefaultKey = "default";

    /// <summary>Oda listesinin tek satırı.</summary>
    public struct RoomInfo
    {
        public string Code;
        public string Name;
        public int Players;
    }

    [Tooltip("EOS'tan cevap beklenecek en uzun süre (saniye). Süre dolarsa " +
        "istek başarısız sayılıyor; sonsuza kadar beklemek oyuncuyu asılı " +
        "bırakırdı.")]
    [SerializeField] private float requestTimeout = 12f;

    [Tooltip("Oda listesinde gösterilecek en fazla oda.")]
    [SerializeField] private uint maxListedRooms = 20;

    /// <summary>Kurduğumuz odanın kodu; oda yoksa boş.</summary>
    public string CurrentCode { get; private set; } = string.Empty;

    /// <summary>
    /// Lobi servisi kullanılabilir mi.
    ///
    /// `LobbyNetwork.UseRelay` ile aynı ölçüt: SDK açılmış ve kimlik alınmış.
    /// Burada tekrar soruluyor çünkü bu bileşen menüden bağımsız da
    /// çağrılabilmeli.
    /// </summary>
    public static bool ServiceReady =>
        EOSSDKComponent.Initialized
        && !string.IsNullOrEmpty(EOSSDKComponent.LocalUserProductIdString);

    private bool notificationsRegistered;

    // ---------- İstek defteri ----------

    /// <summary>
    /// Tek bir isteğin durumu. `Cleanup` olay aboneliklerini söküyor ve
    /// <b>yalnızca bir kez</b> çalışıyor: cevabı EOS de verse sayaç da verse
    /// abonelikler tam olarak bir kere kalkıyor.
    /// </summary>
    private sealed class Pending
    {
        public bool Answered;
        public Action Cleanup;
    }

    private Pending BeginRequest(Action<string> onFailed, string timeoutMessage)
    {
        Pending pending = new Pending();
        StartCoroutine(FailWhenSilent(pending, onFailed, timeoutMessage));
        return pending;
    }

    private IEnumerator FailWhenSilent(Pending pending, Action<string> onFailed, string message)
    {
        // Gerçek zaman: menü açıkken oyun duraklatılmış olabiliyor
        // (`Time.timeScale` 0) ve normal sayaç hiç ilerlemezdi.
        yield return new WaitForSecondsRealtime(requestTimeout);

        if (!Claim(pending))
            yield break;

        onFailed?.Invoke(message);
    }

    /// <summary>Cevap hakkını alır. İkinci çağıran false görüyor ve susuyor.</summary>
    private static bool Claim(Pending pending)
    {
        if (pending.Answered)
            return false;

        pending.Answered = true;
        pending.Cleanup?.Invoke();
        pending.Cleanup = null;

        return true;
    }

    // ---------- Hazırlık ----------

    /// <summary>
    /// Base'in bildirim kaydını devre dışı bırakır. Gerçek kayıt
    /// <see cref="EnsureReady"/> içinde, EOS açıldıktan sonra yapılıyor.
    /// </summary>
    public override void Start()
    {
    }

    private bool EnsureReady(Action<string> onFailed)
    {
        if (!ServiceReady)
        {
            onFailed?.Invoke("EOS hazır değil.");
            return false;
        }

        if (!notificationsRegistered)
        {
            base.Start();
            notificationsRegistered = true;
        }

        return true;
    }

    // ---------- Oda kurma ----------

    /// <summary>
    /// Yeni bir oda kurar ve kısa kodunu döndürür.
    ///
    /// Kod odaya öznitelik olarak yazılıyor, yani onu saklayan bir sunucu yok:
    /// arayan kişi EOS'un kendi lobi aramasıyla buluyor.
    /// </summary>
    /// <param name="roomName">Oda listesinde görünecek ad (host'un oyuncu adı).</param>
    public void HostRoom(string roomName, Action<string> onReady, Action<string> onFailed)
    {
        if (!EnsureReady(onFailed))
            return;

        string code = LobbyCode.NewRoomCode();
        Pending pending = BeginRequest(onFailed, "Oda kurulamadı: EOS cevap vermedi.");

        CreateLobbySuccess success = null;
        CreateLobbyFailure failure = null;

        success = _ =>
        {
            if (!Claim(pending))
                return;

            CurrentCode = code;
            onReady?.Invoke(code);
        };

        failure = error =>
        {
            if (!Claim(pending))
                return;

            onFailed?.Invoke(error);
        };

        pending.Cleanup = () =>
        {
            CreateLobbySucceeded -= success;
            CreateLobbyFailed -= failure;
        };

        CreateLobbySucceeded += success;
        CreateLobbyFailed += failure;

        CreateLobby(
            (uint)LobbyRoster.MaxPlayers,
            LobbyPermissionLevel.Publicadvertised,
            false,
            new[]
            {
                new AttributeData { Key = CodeKey, Value = LobbyCode.ToSearchForm(code) },
                new AttributeData { Key = NameKey, Value = roomName ?? string.Empty }
            });
    }

    // ---------- Odaya katılma ----------

    /// <summary>
    /// Kodla oda arar, bulursa katılır ve host'un EOS adresini döndürür.
    ///
    /// İki aşama: önce arama, sonra katılma. Arama boş dönerse oda kapanmış ya
    /// da kod yanlış demek — ikisi de aynı mesajı hak ediyor, çünkü oyuncunun
    /// yapacağı şey aynı: kodu tekrar sormak.
    /// </summary>
    public void JoinRoom(string code, Action<string> onFound, Action<string> onFailed)
    {
        if (!EnsureReady(onFailed))
            return;

        string display = code != null ? code.Trim().ToUpperInvariant() : string.Empty;
        Pending pending = BeginRequest(onFailed, "Oda aranırken EOS cevap vermedi.");

        FindLobbiesSuccess found = null;
        FindLobbiesFailure searchFailed = null;

        found = lobbies =>
        {
            if (lobbies == null || lobbies.Count == 0)
            {
                if (Claim(pending))
                    onFailed?.Invoke($"{display} kodlu oda bulunamadı. Oda hâlâ açık mı?");

                return;
            }

            // Aramanın abonelikleri sökülüyor ama istek BİTMİYOR: aynı sayaç
            // katılma aşamasını da koruyor, yani "buldum ama katılamadım" da
            // cevapsız kalmıyor.
            pending.Cleanup?.Invoke();
            EnterLobby(lobbies[0], pending, onFound, onFailed);
        };

        searchFailed = error =>
        {
            if (Claim(pending))
                onFailed?.Invoke(error);
        };

        pending.Cleanup = () =>
        {
            FindLobbiesSucceeded -= found;
            FindLobbiesFailed -= searchFailed;
        };

        FindLobbiesSucceeded += found;
        FindLobbiesFailed += searchFailed;

        // İki ölçüt birden veriliyor ve EOS ikisini VE'liyor.
        //
        // `default` tek başına base'in kendi aramasının ölçütü ve çalıştığı
        // bilinen tek şekil o; kod ölçütünü onun yanına koymak aramayı bilinen
        // şekle en çok yaklaştıran yol. Ayrıca sonucu bu ürünün odalarıyla
        // sınırlıyor — kod çakışsa bile alakasız bir lobi dönmüyor.
        FindLobbies(1, new[]
        {
            new LobbySearchSetParameterOptions
            {
                ComparisonOp = ComparisonOp.Equal,
                Parameter = new AttributeData { Key = DefaultKey, Value = DefaultKey }
            },
            new LobbySearchSetParameterOptions
            {
                ComparisonOp = ComparisonOp.Equal,
                Parameter = new AttributeData
                {
                    Key = CodeKey,
                    Value = LobbyCode.ToSearchForm(code)
                }
            }
        });
    }

    private void EnterLobby(LobbyDetails lobby, Pending pending,
        Action<string> onFound, Action<string> onFailed)
    {
        JoinLobbySuccess joined = null;
        JoinLobbyFailure joinFailed = null;

        joined = attributes =>
        {
            if (!Claim(pending))
                return;

            string address = ReadJoinedAddress(attributes);

            if (string.IsNullOrEmpty(address))
            {
                onFailed?.Invoke("Odaya girildi ama host adresi okunamadı.");
                return;
            }

            onFound?.Invoke(address);
        };

        joinFailed = error =>
        {
            if (Claim(pending))
                onFailed?.Invoke(error);
        };

        pending.Cleanup = () =>
        {
            JoinLobbySucceeded -= joined;
            JoinLobbyFailed -= joinFailed;
        };

        JoinLobbySucceeded += joined;
        JoinLobbyFailed += joinFailed;

        JoinLobby(lobby, new[] { CodeKey, NameKey });
    }

    /// <summary>
    /// Katılma cevabındaki host adresini okur.
    ///
    /// Base `host_address`'i listenin başına koyuyor ama <b>sıraya
    /// güvenmiyoruz</b>: öznitelik bulunamazsa `CopyAttributeByKey` null `Data`
    /// ile dönüyor ve paketin kendi örneği tam orada `NullReferenceException`
    /// atıyor. Anahtara göre aramak hem sırayı hem null'ı çözüyor.
    /// </summary>
    private static string ReadJoinedAddress(List<EosAttribute> attributes)
    {
        if (attributes == null)
            return string.Empty;

        foreach (EosAttribute attribute in attributes)
        {
            if (attribute?.Data == null || attribute.Data.Key != hostAddressKey)
                continue;

            return attribute.Data.Value?.AsUtf8 ?? string.Empty;
        }

        return string.Empty;
    }

    // ---------- Oda listesi ----------

    /// <summary>
    /// Açık odaları listeler.
    ///
    /// Liste <b>yalnızca kod üretiyor</b>, `LobbyDetails` saklamıyor: listeden
    /// seçmek de kodla katılmakla aynı yoldan geçiyor (<see cref="JoinRoom"/>).
    /// Tutulan bir handle'ın oyuncu düğmeye basana kadar bayatlaması (oda
    /// kapanır, dolar) ikinci bir hata yolu açardı; arama zaten milisaniyeler
    /// sürüyor.
    /// </summary>
    public void ListRooms(Action<List<RoomInfo>> onListed, Action<string> onFailed)
    {
        if (!EnsureReady(onFailed))
            return;

        Pending pending = BeginRequest(onFailed, "Oda listesi alınamadı: EOS cevap vermedi.");

        FindLobbiesSuccess found = null;
        FindLobbiesFailure searchFailed = null;

        found = lobbies =>
        {
            if (!Claim(pending))
                return;

            List<RoomInfo> rooms = new List<RoomInfo>();

            if (lobbies != null)
            {
                foreach (LobbyDetails lobby in lobbies)
                {
                    string code = ReadAttribute(lobby, CodeKey);

                    // Kodsuz oda bu oyunun odası değil (ya da eski bir sürüm):
                    // listede gösterilse tıklandığında hiçbir şey yapamazdık.
                    if (string.IsNullOrEmpty(code))
                        continue;

                    rooms.Add(new RoomInfo
                    {
                        Code = code.ToUpperInvariant(),
                        Name = ReadAttribute(lobby, NameKey),
                        Players = (int)lobby.GetMemberCount(new LobbyDetailsGetMemberCountOptions())
                    });
                }
            }

            onListed?.Invoke(rooms);
        };

        searchFailed = error =>
        {
            if (Claim(pending))
                onFailed?.Invoke(error);
        };

        pending.Cleanup = () =>
        {
            FindLobbiesSucceeded -= found;
            FindLobbiesFailed -= searchFailed;
        };

        FindLobbiesSucceeded += found;
        FindLobbiesFailed += searchFailed;

        // Arama ölçütü verilmiyor: base her odaya yazdığı `default`
        // özniteliğiyle arıyor, yani bu ürünün bütün açık odaları geliyor.
        FindLobbies(maxListedRooms);
    }

    private static string ReadAttribute(LobbyDetails lobby, string key)
    {
        EosAttribute attribute;

        Result result = lobby.CopyAttributeByKey(
            new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key }, out attribute);

        if (result != Result.Success || attribute?.Data?.Value == null)
            return string.Empty;

        return attribute.Data.Value.AsUtf8 ?? string.Empty;
    }

    // ---------- Kapatma ----------

    /// <summary>
    /// Odayı kapatır (host'sak yok ediyor, değilsek çıkıyor).
    ///
    /// Base çıkarken bildirim kayıtlarını da siliyor, o yüzden bayrak
    /// sıfırlanıyor: aynı oturumda ikinci bir oda kurulursa
    /// <see cref="EnsureReady"/> kayıtları yeniden yapıyor. Sıfırlanmasaydı
    /// ikinci odada "host odayı kapattı" bildirimi hiç gelmezdi.
    /// </summary>
    public void CloseRoom()
    {
        CurrentCode = string.Empty;

        if (!ConnectedToLobby || !ServiceReady)
            return;

        LeaveLobby();
        notificationsRegistered = false;
    }
}
