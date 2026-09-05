using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Lobi kodu: odayı bulmaya yarayan kısa metin.
///
/// **Kod artık IP DEĞİL.** Eski sürümde kod, sunucunun IPv4 adresinin 32
/// harflik alfabeyle yazılmış hâliydi; eşleştirme sunucusu gerektirmediği için
/// öyle seçilmişti (bölüm 0). Ama o kurulum yalnızca aynı ağda çalışıyordu:
/// Türkiye'de CGNAT yaygın olduğu için itch.io'dan indiren biri arkadaşıyla
/// oynayamıyordu.
///
/// Relay'e geçilince (bölüm 10) kod bir **oda adı** oldu. Host rastgele bir kod
/// üretip odayı o adla açıyor; katılan kişi kodu yazınca lobi listesinde o ad
/// aranıyor. Oyuncu açısından hiçbir şey değişmiyor — kod ver, kod yaz, gir.
///
/// Alfabede karışan harfler yok (I, O, 0, 1): kod telefonda okunacak, sesli
/// sohbette söylenecek.
///
/// **Neden rastgele kod, ham lobi kimliği değil?** Edgegap'in verdiği
/// `lobby_id` uzun ve okunamaz bir metin; sesli sohbette söylenemez. Kısa kodu
/// oda adı yapıp listeden aramak, kimliği kullanıcıdan tamamen gizliyor.
/// </summary>
public static class LobbyCode
{
    /// <summary>32 karakter. I, O, 0, 1 yok — okunurken karışıyorlar.</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Kod uzunluğu. 6 karakter × 5 bit ≈ 1 milyar ihtimal; aynı anda açık
    /// birkaç odada çakışma pratikte imkânsız.
    /// </summary>
    public const int Length = 6;

    /// <summary>Kod bilinmiyorken gösterilecek metin.</summary>
    public const string Unknown = "------";

    /// <summary>
    /// Bu makinenin bütün IPv4 adresleri, virgülle ayrılmış.
    ///
    /// **Neden hepsi, biri değil.** Eski sürüm 8.8.8.8'e bir UDP soketi açıp
    /// "hangi arayüzden çıktın" diye soruyordu; o, internete çıkan adaptörü
    /// veriyor. Ama yerel oda çoğunlukla **sanal ağ** (Radmin, Hamachi)
    /// üzerinden oynanıyor ve orada gereken adres sanal adaptörünki. Tek adres
    /// göstermek, oyuncuya yanlış olanı vermek demekti.
    ///
    /// Liste bilerek filtrelenmiyor: hangisinin doğru olduğunu makine bilemez,
    /// ama oyuncu Radmin penceresindeki adresi görünce listeden tanır.
    /// </summary>
    public static string LocalAddresses()
    {
        List<string> found = new List<string>();

        try
        {
            foreach (IPAddress candidate in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (candidate.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                string text = candidate.ToString();
                if (!found.Contains(text))
                    found.Add(text);
            }
        }
        catch (SocketException)
        {
            // Ağ yoksa liste boş kalıyor; aşağıdaki geri dönüş devreye giriyor.
        }

        return found.Count > 0 ? string.Join("  ·  ", found) : "127.0.0.1";
    }

    /// <summary>Yeni bir oda kodu üretir.</summary>
    public static string Generate()
    {
        StringBuilder builder = new StringBuilder(Length);

        for (int i = 0; i < Length; i++)
            builder.Append(Alphabet[UnityEngine.Random.Range(0, Alphabet.Length)]);

        return builder.ToString();
    }

    /// <summary>
    /// Kullanıcının yazdığını aranabilir koda çevirir.
    ///
    /// Boşluk ve küçük harf affediliyor: kod sesli sohbette söylenip elle
    /// yazılıyor, "abc def" yazan biri geri çevrilmemeli. Alfabede olmayan bir
    /// karakter varsa kod geçersiz — kullanıcıya söylemek, sessizce bağlanmayı
    /// denemekten iyi.
    /// </summary>
    public static bool TryNormalize(string input, out string code)
    {
        code = null;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        StringBuilder builder = new StringBuilder(Length);

        foreach (char character in input.Trim().ToUpperInvariant())
        {
            if (character == ' ' || character == '-')
                continue;

            if (Alphabet.IndexOf(character) < 0)
                return false;

            builder.Append(character);
        }

        if (builder.Length != Length)
            return false;

        code = builder.ToString();
        return true;
    }
}
