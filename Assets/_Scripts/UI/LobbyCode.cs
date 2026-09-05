using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Lobi kodu ile IP adresi arasında çeviri.
///
/// **Kod, sunucunun IPv4 adresinin kendisi.** Rastgele üretilip bir eşleştirme
/// sunucusunda saklanmıyor — öyle olsaydı ayakta tutulacak bir servis gerekirdi
/// ve oyunun tek bağımlılığı Mirror kalmazdı (CLAUDE.md bölüm 0). 32 bitlik
/// IPv4, 32 harflik alfabeyle 7 karaktere sığıyor.
///
/// Alfabede karışan harfler yok (I, O, 0, 1): kod telefonda okunacak, sesli
/// sohbette söylenecek.
///
/// **Sınır: bu doğrudan bağlantı.** Aynı ağdaki (LAN) oyuncular kodu girip
/// bağlanabilir. İnternet üzerinden oynamak için sunucunun 7777 UDP portunu
/// yönlendirmesi ya da Radmin/Hamachi gibi bir sanal ağ kullanılması gerekiyor.
/// Relay servisi yok ve bilerek yok.
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
