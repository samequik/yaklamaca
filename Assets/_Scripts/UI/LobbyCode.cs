using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Lobi kodları. **İki ayrı kod biçimi var ve ikisi de burada.**
///
/// ### 7 harf — yerel kod, IP'nin kendisi
///
/// Rastgele üretilip bir eşleştirme sunucusunda saklanmıyor: kod, sunucunun
/// IPv4 adresinin yazılışı. Öyle olmasaydı ayakta tutulacak bir servis gerekirdi
/// (CLAUDE.md bölüm 0). 32 bitlik IPv4, 32 harflik alfabeyle 7 karaktere
/// sığıyor.
///
/// **Sınırı bu doğrudan bağlantı olması.** Aynı ağdaki (LAN) oyuncular kodu
/// girip bağlanabilir; internet üzerinden 7777/UDP yönlendirmesi ya da sanal ağ
/// (Radmin, Hamachi) gerekiyor. EOS açılmadığında düşülen yol bu.
///
/// ### 6 harf — EOS oda kodu, rastgele
///
/// EOS relay'i çalıştığında kullanılan kod. Adresle hiç ilgisi yok: rastgele
/// üretilip odanın kendisine öznitelik olarak yazılıyor ve arayan kişi EOS'un
/// lobi servisinden buluyor (bkz. `RelayLobby`). Saklayan sunucu yine yok —
/// kaydı Epic tutuyor ve o zaten relay için kullanılıyor.
///
/// ### Ortak alfabe
///
/// Karışan harfler yok (I, O, 0, 1): kod telefonda okunacak, sesli sohbette
/// söylenecek. İki biçimi **uzunluk** ayırıyor, içerik değil.
///
/// Katılma ekranı ham IP de kabul ediyor: "192.168.1.42" yazan biri koda
/// çevirmek zorunda kalmasın (sanal ağ adresleri, dışarıdan verilen adresler).
/// </summary>
public static class LobbyCode
{
    /// <summary>32 karakter = 5 bit. I, O, 0, 1 yok — okunurken karışıyorlar.</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>7 karakter × 5 bit = 35 bit; IPv4'ün 32 biti rahat sığıyor.</summary>
    public const int Length = 7;

    /// <summary>Adres çözülemezse gösterilecek metin.</summary>
    public const string Unknown = "-------";

    // ---------- EOS oda kodu ----------

    /// <summary>
    /// EOS odasının kısa kodu — 6 karakter, IP kodundan bir eksik.
    ///
    /// **Uzunluk farkı bilerek.** Katılma alanı dört biçimi birden almak
    /// zorunda ve hepsi tek bakışta ayrılabilmeli:
    ///
    ///   6 karakter   → EOS oda kodu (bu)
    ///   7 karakter   → IP'den üretilmiş yerel kod (<see cref="Length"/>)
    ///   noktalı      → ham IP
    ///   32 karakter  → EOS ürün kimliği (yedek yol)
    ///
    /// İkisi aynı uzunlukta olsaydı girilen kodun hangisi olduğu anlaşılamazdı:
    /// alfabe ortak olduğu için içeriğe bakmak da ayırt etmiyor.
    ///
    /// 32^6 ≈ 1.07 milyar bileşim. Benzersizlik sunucuda **doğrulanmıyor** —
    /// kod rastgele üretiliyor, kimse çakışma kontrolü yapmıyor. Alternatifi
    /// ayakta tutulacak bir eşleştirme servisi olurdu (bölüm 0). Aynı anda açık
    /// birkaç odada çakışma ihtimali ölçülemez; olursa katılan yanlış odaya
    /// düşer ve kod tekrar istenir.
    /// </summary>
    public const int RoomCodeLength = 6;

    /// <summary>Yeni bir rastgele oda kodu üretir.</summary>
    public static string NewRoomCode()
    {
        StringBuilder builder = new StringBuilder(RoomCodeLength);

        for (int i = 0; i < RoomCodeLength; i++)
            builder.Append(Alphabet[UnityEngine.Random.Range(0, Alphabet.Length)]);

        return builder.ToString();
    }

    /// <summary>Girilen metin bir EOS oda kodu olabilir mi.</summary>
    public static bool IsRoomCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string normalized = input.Trim().ToUpperInvariant();
        if (normalized.Length != RoomCodeLength)
            return false;

        foreach (char character in normalized)
        {
            if (Alphabet.IndexOf(character) < 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Kodu EOS'a yazılacak/aranacak biçime çevirir: küçük harf.
    ///
    /// EOS'un lobi araması metin özniteliklerinde büyük/küçük harfi her sürümde
    /// aynı ele almıyor. Kodu hem yazarken hem ararken küçük harfe çevirmek
    /// sorunun tamamını ortadan kaldırıyor. Ekranda gösterilen hâli yine büyük
    /// harf — söylenmesi ve okunması kolay olan o.
    /// </summary>
    public static string ToSearchForm(string code) =>
        code != null ? code.Trim().ToLowerInvariant() : string.Empty;

    // ---------- Kodlama ----------

    /// <summary>IPv4 adresini koda çevirir. Adres geçersizse <see cref="Unknown"/>.</summary>
    public static string FromAddress(string address)
    {
        if (!IPAddress.TryParse(address, out IPAddress parsed))
            return Unknown;

        byte[] bytes = parsed.GetAddressBytes();
        if (bytes.Length != 4)
            return Unknown;

        uint value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16)
            | ((uint)bytes[2] << 8) | bytes[3];

        StringBuilder builder = new StringBuilder(Length);

        // En anlamlı beşliden başlıyoruz ki kod hep aynı uzunlukta olsun.
        for (int i = Length - 1; i >= 0; i--)
        {
            int index = (int)((value >> (i * 5)) & 0x1F);
            builder.Append(Alphabet[index]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Kullanıcının yazdığını bağlanılacak adrese çevirir.
    ///
    /// Önce ham IP olarak deneniyor: sanal ağ (Hamachi) ya da dışarıdan verilen
    /// bir adresi koda çevirmeye zorlamak gereksiz bir engel olurdu.
    /// </summary>
    public static bool TryToAddress(string input, out string address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        string trimmed = input.Trim();

        if (IPAddress.TryParse(trimmed, out IPAddress direct)
            && direct.AddressFamily == AddressFamily.InterNetwork)
        {
            address = direct.ToString();
            return true;
        }

        string normalized = trimmed.ToUpperInvariant();
        if (normalized.Length != Length)
            return false;

        uint value = 0;

        foreach (char character in normalized)
        {
            int index = Alphabet.IndexOf(character);
            if (index < 0)
                return false;

            value = (value << 5) | (uint)index;
        }

        address = $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}." +
            $"{(value >> 8) & 0xFF}.{value & 0xFF}";

        return true;
    }

    // ---------- Yerel adres ----------

    /// <summary>
    /// Bu makinenin BÜTÜN IPv4 adresleri, tek satırda.
    ///
    /// `LocalAddress()` internete çıkan adaptörü veriyor ve kod ondan
    /// üretiliyor — aynı ağda oynarken doğru olan bu. Ama **sanal ağda**
    /// (Radmin, Hamachi) gereken adres sanal adaptörünki ve o adres internete
    /// çıkmadığı için `LocalAddress()` onu asla seçmiyor: kod yanlış çıkıyor.
    ///
    /// Liste bilerek filtrelenmiyor. Hangisinin doğru olduğunu makine bilemez,
    /// ama oyuncu Radmin penceresindeki adresi ekranda görünce tanıyor ve
    /// arkadaşına onu veriyor (katılma alanı ham IP kabul ediyor).
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
            // Ağ yoksa liste boş kalıyor.
        }

        return found.Count > 0 ? string.Join("  ·  ", found) : "127.0.0.1";
    }

    /// <summary>
    /// Bu makinenin ağdaki IPv4 adresi — sunucu olurken kodu bundan üretiyoruz.
    ///
    /// Yöntem: dışarı bir UDP soketi "bağlanıyor". Veri gönderilmiyor; UDP'de
    /// Connect yalnızca yerel bir işlem ve işletim sistemine "bu hedefe hangi
    /// arayüzden çıkardın" diye sormanın en güvenilir yolu. Makinede birden
    /// fazla arayüz olduğunda (VPN, sanal makine adaptörü, Wi-Fi + Ethernet)
    /// ana makine adından adres çözmek yanlış olanı seçebiliyor.
    /// </summary>
    public static string LocalAddress()
    {
        try
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);

                if (socket.LocalEndPoint is IPEndPoint endPoint)
                    return endPoint.Address.ToString();
            }
        }
        catch (SocketException)
        {
            // Ağ yoksa aşağıdaki yönteme düşüyoruz.
        }

        try
        {
            foreach (IPAddress candidate in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (candidate.AddressFamily == AddressFamily.InterNetwork)
                    return candidate.ToString();
            }
        }
        catch (SocketException)
        {
        }

        // Tek başına test: kendi makinene bağlanmak yine de çalışıyor.
        return "127.0.0.1";
    }
}
