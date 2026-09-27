using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameLanguage
{
    English,
    Turkish
}

/// <summary>
/// Oyunun dili. Kaynak kod baştan sona Türkçe yazıldı, o yüzden sözlük
/// Türkçe metni ANAHTAR olarak kullanıp İngilizcesini döndürüyor — ayrı bir
/// kısa kod listesi (ör. "menu.start") tutmuyoruz. İki avantajı var: çağıran
/// tarafta yazım hatası riski yok (değiştirilen literal aynen kopyalanıyor)
/// ve çevirisi eksik bir satır oyunu bozmuyor, yalnızca Türkçe görünmeye
/// devam ediyor.
///
/// **Varsayılan İngilizce.** itch.io'daki demo İngilizce konuşan bir
/// kitleye gidiyor; oyunu ilk açan biri Türkçe bilmeyebilir. Türkçe'ye
/// geçmek Seçenekler'den bir tık.
/// </summary>
public static class Localization
{
    private const string LanguageKey = "Ayar_Dil";
    private const GameLanguage DefaultLanguage = GameLanguage.English;

    public static event Action Changed;

    public static GameLanguage Current
    {
        get => (GameLanguage)PlayerPrefs.GetInt(LanguageKey, (int)DefaultLanguage);
        set
        {
            if (Current == value)
                return;

            PlayerPrefs.SetInt(LanguageKey, (int)value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Türkçe metni döndürür; İngilizce'deysek ve çevirisi varsa onu.</summary>
    public static string Get(string turkish)
    {
        if (turkish == null)
            return null;

        if (Current == GameLanguage.Turkish)
            return turkish;

        return Map.TryGetValue(turkish, out string english) ? english : turkish;
    }

    /// <summary>
    /// `Get` + `string.Format`. Çağıran taraf eskiden `$"..."` ile önceden
    /// doldurulmuş bir metin kuruyordu; artık şablon Türkçe hâliyle anahtar
    /// oluyor ve doldurma çeviriden SONRA yapılıyor.
    /// </summary>
    public static string Format(string turkishTemplate, params object[] args) =>
        string.Format(Get(turkishTemplate), args);

    private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
    {
        // ---------- Menü: isim ekranı, ana menü, seçenekler ----------
        ["Seni nasıl çağıralım?"] = "What should we call you?",
        ["Boş bırakırsan \"Player\" olursun. Sonradan Seçenekler'den değiştirebilirsin."] =
            "Leave it blank and you'll be \"Player\". You can change it later from Options.",
        ["DEVAM"] = "CONTINUE",
        ["LOBİ KUR"] = "HOST LOBBY",
        ["LOBİYE KATIL"] = "JOIN LOBBY",
        ["KARAKTER"] = "CHARACTER",
        ["SEÇENEKLER"] = "OPTIONS",
        ["ÇIKIŞ"] = "QUIT",
        ["Lobi kurunca 7 harflik bir kod çıkar. Arkadaşın o kodu girerek katılır."] =
            "Hosting a lobby gives you a 7-letter code. Your friend joins by entering it.",
        ["GERİ"] = "BACK",
        ["SES"] = "AUDIO",
        ["TUŞ ATAMALARI"] = "KEY BINDINGS",
        ["Oyuncu adı"] = "Player name",
        ["Fare hassasiyeti: {0:1}"] = "Mouse sensitivity: {0:1}",
        ["Korku efektleri: KAPALI"] = "Horror effects: OFF",
        ["Korku efektleri: %{0}"] = "Horror effects: {0}%",
        ["Ters bakış: AÇIK"] = "Invert look: ON",
        ["Ters bakış: kapalı"] = "Invert look: off",

        // ---------- Ses ve sesli sohbet ----------
        ["SESLİ SOHBET"] = "VOICE CHAT",
        ["Ses: {0:0}%"] = "Volume: {0:0}%",
        ["Sesli sohbet: AÇIK"] = "Voice chat: ON",
        ["Sesli sohbet: KAPALI"] = "Voice chat: OFF",
        ["Konuşma: BAS-KONUŞ ({0})"] = "Talk: PUSH-TO-TALK ({0})",
        ["Konuşma: OTOMATİK"] = "Talk: AUTOMATIC",
        ["Mikrofon: {0}"] = "Microphone: {0}",
        ["Mikrofon kazancı: {0:0.0}x"] = "Microphone gain: {0:0.0}x",
        ["Konuşma eşiği: {0:0.000}   (sağ üstteki çizgi)"] = "Talk threshold: {0:0.000}   (top-right line)",
        ["Konuşma eşiği: yalnızca otomatik modda"] = "Talk threshold: automatic mode only",
        ["Konuşma sesi: {0:0}%"] = "Voice volume: {0:0}%",
        ["YOK"] = "NONE",

        // ---------- Tuş atamaları ----------
        ["bir tuşa bas…"] = "press a key…",
        ["Yeni tuşa bas. Vazgeçmek için Esc."] = "Press a new key. Press Esc to cancel.",
        ["Değiştirmek istediğin tuşa tıkla. Tuş başkasındaysa ikisi yer değiştirir."] =
            "Click the key you want to change. If it's taken, the two swap.",
        ["VARSAYILANA DÖN"] = "RESET TO DEFAULT",

        // Eylem adları (KeyBindings.DescribeAction)
        ["İleri"] = "Forward",
        ["Geri"] = "Back",
        ["Sola"] = "Left",
        ["Sağa"] = "Right",
        ["Zıpla"] = "Jump",
        ["Koş"] = "Sprint",
        ["Eğil"] = "Crouch",
        ["Etkileşim"] = "Interact",
        ["Fener"] = "Flashlight",
        ["Saldırı (canavar)"] = "Attack (monster)",
        ["Bas-konuş"] = "Push-to-talk",
        ["Oyuncu paneli"] = "Player panel",

        // Tuş adları (KeyBindings.Describe)
        ["SOL FARE"] = "LEFT MOUSE",
        ["SAĞ FARE"] = "RIGHT MOUSE",
        ["ORTA FARE"] = "MIDDLE MOUSE",
        ["FARE 4"] = "MOUSE 4",
        ["FARE 5"] = "MOUSE 5",
        ["BOŞLUK"] = "SPACE",
        ["SOL SHIFT"] = "LEFT SHIFT",
        ["SAĞ SHIFT"] = "RIGHT SHIFT",
        ["SOL CTRL"] = "LEFT CTRL",
        ["SAĞ CTRL"] = "RIGHT CTRL",
        ["SOL ALT"] = "LEFT ALT",
        ["SAĞ ALT"] = "RIGHT ALT",
        ["YUKARI OK"] = "UP ARROW",
        ["AŞAĞI OK"] = "DOWN ARROW",
        ["SOL OK"] = "LEFT ARROW",
        ["SAĞ OK"] = "RIGHT ARROW",

        // ---------- Lobi ----------
        ["LOBİ"] = "LOBBY",
        ["AYRIL"] = "LEAVE",
        ["AT"] = "KICK",
        ["YASAKLA"] = "BAN",
        ["Arkadaşının verdiği kodu gir"] = "Enter the code your friend gave you",
        ["KATIL"] = "JOIN",
        ["AÇIK ODALAR"] = "OPEN ROOMS",
        ["ODALARI YENİLE"] = "REFRESH ROOMS",
        ["DURAKLATILDI"] = "PAUSED",
        ["DEVAM ET"] = "RESUME",
        ["ODADAN AYRIL"] = "LEAVE ROOM",
        ["KOPYALA"] = "COPY",

        ["— boş —"] = "— empty —",
        ["{0}  (sen, oda sahibi)"] = "{0}  (you, room owner)",
        ["{0}  (sen)"] = "{0}  (you)",
        ["{0}  (oda sahibi)"] = "{0}  (room owner)",
        ["CANAVAR"] = "MONSTER",
        ["HAZIR"] = "READY",
        ["bekliyor"] = "waiting",
        ["Oyuncular: {0}/{1}"] = "Players: {0}/{1}",
        ["Rastgele"] = "Random",
        ["Odaya giriliyor…"] = "Joining room…",
        ["Herkes hazır — başlatabilirsin."] = "Everyone's ready — you can start.",
        ["Herkes hazır. Oda sahibi başlatacak."] = "Everyone's ready. The room owner will start.",
        ["Geçen tur: canavar kazandı"] = "Last round: the monster won",
        ["Geçen tur: kaçanlar kazandı"] = "Last round: the runners escaped",
        ["Geçen tur iptal oldu (canavar ayrıldı)"] = "Last round was cancelled (the monster left)",
        ["HAZIR DEĞİLİM"] = "NOT READY",
        ["HAZIRIM"] = "READY UP",
        ["Canavar: {0}"] = "Monster: {0}",
        ["BAŞLAT"] = "START",

        // ---------- Ağ / bağlantı durumu ----------
        ["Zaten bir odadasın."] = "You're already in a room.",
        ["EOS bağlanıyor…"] = "Connecting to EOS…",
        ["EOS hazır değil, yerel oda kuruldu. Katılacak kişi şu adreslerden birini yazmalı:\n{0}"] =
            "EOS isn't ready, a local room was created instead. Whoever's joining should type one of these addresses:\n{0}",
        ["İnternet odası. Kodu kopyalayıp arkadaşına ver."] = "Online room. Copy the code and give it to your friend.",
        ["Oda kuruluyor…"] = "Creating room…",
        ["İnternet odası. Kodu arkadaşına söyle."] = "Online room. Tell your friend the code.",
        ["Kısa kod alınamadı. Uzun kodla devam: kodu kopyalayıp arkadaşına ver."] =
            "Couldn't get a short code. Continuing with the long code: copy it and give it to your friend.",
        ["Bağlanılıyor…"] = "Connecting…",
        ["Kod okunamadı. {0} harflik oda kodu, {1} harflik yerel kod ya da IP adresi bekleniyor."] =
            "Couldn't read the code. Expected a {0}-letter room code, a {1}-letter local code, or an IP address.",
        ["Kod okunamadı. {0} harf ya da bir IP adresi bekleniyor. (EOS hazır değil, oda koduyla katılamazsın.)"] =
            "Couldn't read the code. Expected {0} letters or an IP address. (EOS isn't ready, so you can't join with a room code.)",
        ["Bu senin kendi odan. Aynı bilgisayardaki iki kopya aynı EOS kimliğini paylaşıyor, yani kendine bağlanamazsın — test için ikinci bir makine gerekiyor."] =
            "This is your own room. Two copies on the same computer share the same EOS identity, so you can't connect to yourself — testing needs a second machine.",
        ["Oda aranıyor…"] = "Searching for room…",
        ["Kod panoya kopyalandı."] = "Code copied to clipboard.",
        ["Oda sahibi seni bu oturum için yasakladı."] = "The room owner banned you for this session.",
        ["Oda sahibi seni odadan çıkardı."] = "The room owner kicked you from the room.",
        ["Bağlantı koptu — oda sahibi çıkmış olabilir."] = "Connection lost — the room owner may have left.",
        ["Bağlanılamadı. Kodu kontrol et; sunucu aynı ağda mı?"] = "Couldn't connect. Check the code — is the host on the same network?",
        ["Bağlanılamadı (zaman aşımı). Kodu kontrol et; oda hâlâ açık mı?"] =
            "Couldn't connect (timed out). Check the code — is the room still open?",

        ["EOS hazır değil."] = "EOS isn't ready.",
        ["Oda kurulamadı: EOS cevap vermedi."] = "Couldn't create room: EOS didn't respond.",
        ["Oda aranırken EOS cevap vermedi."] = "EOS didn't respond while searching for the room.",
        ["{0} kodlu oda bulunamadı. Oda hâlâ açık mı?"] = "No room found with code {0}. Is the room still open?",
        ["Odaya girildi ama host adresi okunamadı."] = "Joined the room but couldn't read the host's address.",
        ["Oda listesi alınamadı: EOS cevap vermedi."] = "Couldn't get the room list: EOS didn't respond.",

        ["Odalar aranıyor…"] = "Searching for rooms…",
        ["Açık oda yok. Arkadaşının kodunu bekliyorsan onu yaz."] = "No open rooms. If you're waiting for your friend's code, type it in.",
        ["{0} oda bulundu, ilk {1} tanesi gösteriliyor. Aradığın yoksa kodu yaz."] =
            "Found {0} rooms, showing the first {1}. If yours isn't there, type the code.",
        ["Oda listesi alınamadı ({0}). Kodla katılabilirsin."] = "Couldn't get the room list ({0}). You can still join with a code.",
        ["ARANIYOR…"] = "SEARCHING…",
        ["İsimsiz oda"] = "Unnamed room",
        ["Arkadaşının verdiği kodu ya da IP adresini gir."] = "Enter the code or IP address your friend gave you.",

        // ---------- TAB paneli ----------
        ["{0} ile kapat  ·  yürümeye devam edebilirsin  ·  ses ayarı yalnızca seni etkiler"] =
            "Press {0} to close  ·  you can keep walking  ·  volume only affects you",
        ["kendi sesini duymuyorsun"] = "you don't hear yourself",
        ["sesi yok"] = "no audio",
        ["AÇ"] = "UNMUTE",
        ["SUSTUR"] = "MUTE",
        ["OYUNCULAR"] = "PLAYERS",

        // ---------- Karakter seçimi ----------
        ["CANAVAR KOSTÜMÜ"] = "MONSTER COSTUME",
        ["KAÇAN KOSTÜMÜ"] = "RUNNER COSTUME",
        ["Şimdilik tek kostüm var. Yenileri eklenince burada çıkacak. Modeli fareyle sürükleyerek çevirebilirsin."] =
            "There's only one costume for now. New ones will show up here. Drag with the mouse to spin the model.",
        ["Canavarı oda sahibi seçiyor. Bu yalnızca canavar olursan görünüşün."] =
            "The room owner picks the monster. This is only your look if you end up being the monster.",
        ["Kostüm yalnızca görünüş: hız, boy ve menzil değişmiyor."] = "The costume is only visual: speed, height and reach don't change.",

        // ---------- Diriltme ekranı ----------
        ["DİRİLTME — {0}"] = "REVIVAL — {0}",
        ["KAÇAN"] = "RUNNER",
        ["BU KABİN: {0} DİRİLTME"] = "THIS STATION: {0} REVIVED",
        ["SİSTEM KİLİTLİ"] = "SYSTEM LOCKED",
        ["Diziliyi gir — ilerleme sıfırlandı"] = "Enter the sequence — progress reset",
        ["{0:0.0} saniye içinde bas"] = "press within {0:0.0} seconds",
        ["YAŞAM DESTEĞİ ÇALIŞIYOR"] = "LIFE SUPPORT RUNNING",
        [" ile bırak — bırakınca ilerleme sıfırlanır"] = " to let go — letting go resets progress",

        // ---------- Terminal ekranı / çıkış kilidi ----------
        ["[{0}] bırak"] = "[{0}] let go",
        ["{0} / {1}      yanlış tuş başa sarar"] = "{0} / {1}      a wrong key resets it",

        // ---------- Tur bilgisi (üst şerit) ----------
        ["ÇIKIŞ AÇIK"] = "EXIT OPEN",
        ["TERMİNAL  {0} / {1}"] = "TERMINAL  {0} / {1}",
        ["ELENDİN   —   İzlenen: {0}   [Sol tık] değiştir   —   Kalan kaçan: {1}"] =
            "ELIMINATED   —   Watching: {0}   [Left click] switch   —   Runners left: {1}",
        ["CANAVARSIN"] = "YOU'RE THE MONSTER",
        ["KAÇIYORSUN"] = "YOU'RE RUNNING",
        ["ELENDİN"] = "ELIMINATED",
        ["{0}   —   Kalan kaçan: {1}"] = "{0}   —   Runners left: {1}",
        ["CANAVAR KAZANDI"] = "THE MONSTER WON",
        ["KAÇANLAR KURTULDU"] = "THE RUNNERS ESCAPED",
        ["CANAVAR OYUNDAN AYRILDI — tur iptal"] = "THE MONSTER LEFT THE GAME — round cancelled",
        ["LOBİ   —   oyuncular bekleniyor"] = "LOBBY   —   waiting for players",
        ["TERMİNAL ALARMI"] = "TERMINAL ALARM",

        // ---------- Terminal (Interaction/Terminal.cs) ----------
        ["Terminal tamamlandı  (%100)"] = "Terminal complete  (100%)",
        ["Kilitli  (%{0})"] = "Locked  ({0}%)",
        ["Kilitleniyor…  (%{0})"] = "Locking…  ({0}%)",
        ["Kilitle  (%{0})"] = "Lock  ({0}%)",
        ["Kilidi aç  (%{0})"] = "Unlock  ({0}%)",
        ["Bırak  (%{0})"] = "Let go  ({0}%)",
        ["Meşgul  (%{0})"] = "Busy  ({0}%)",
        ["Terminali çalıştır  (%{0})"] = "Run terminal  ({0}%)",
        ["KİLİTLEME"] = "LOCKING",
        ["hareket edemezsin"] = "you can't move",
        ["ilerleme %{0} — donduruldu    ·    sırayla gir"] = "progress {0}% — frozen    ·    enter in sequence",
        ["BAĞLANTI"] = "CONNECTION",
        ["BAĞLANIYOR…"] = "CONNECTING…",
        ["VERİ AKTARIMI"] = "DATA TRANSFER",
        ["aktarım sürüyor"] = "transfer in progress",

        // ---------- Çıkış kilidi (Interaction/ExitLock.cs) ----------
        ["Çıkış açık"] = "Exit open",
        ["Kilitli — önce terminaller ({0}/{1})"] = "Locked — finish the terminals first ({0}/{1})",
        ["Bırak  ({0}/{1})"] = "Let go  ({0}/{1})",
        ["Meşgul"] = "Busy",
        ["Çıkışı aç"] = "Open exit",

        // ---------- Diriltme kabini (Interaction/RevivalStation.cs) ----------
        [" bu kabinde diriltildi — diğer kabini dene"] = " was revived at this station — try the other one",
        ["Diriltme terminali kullanımda"] = "Revival terminal in use",
        ["Cesedi kabine yerleştir"] = "Place the body in the station",
        ["Diriltme kabini — bir ceset getir"] = "Revival station — bring a body",
        ["Kabin dolu — taşıdığın cesedi önce bırak"] = "Station full — drop the body you're carrying first",
        ["Diriltme terminalinin kilidini aç"] = "Unlock the revival terminal",
        ["Diriltmeyi başlat — {0:0} saniye"] = "Start reviving — {0:0} seconds",

        // ---------- Kapılar ve düğmeler ----------
        ["Kapıyı aç"] = "Open door",
        ["Kapıyı kapat"] = "Close door",
        ["Kullan"] = "Use",

        // ---------- Ceset taşıma ----------
        [" — cesedi taşı"] = " — carry body",
        ["Kaçan"] = "Runner",

        // ---------- Çıkış kilidi paneli (menüde baked başlık) ----------
        ["Ç I K I Ş   K İ L İ D İ"] = "E X I T   L O C K",

        // ---------- Tutorial koridoru ----------
        ["TUTORIAL"] = "TUTORIAL",
        ["Terminal Five'a hoş geldin! Bu koridor sana oyunun temellerini gösterecek. " +
            "İlerlemek için {0}. Esc ile duraklatıp ayarlara girebilir ya da çıkabilirsin."] =
            "Welcome to Terminal Five! This corridor will teach you the basics. " +
            "Press {0} to move forward. Press Esc to pause, open the settings or leave.",
        ["Burası karanlık. {0} ile fenerini açıp kapatabilirsin — fener görmeni " +
            "sağlar ama canavara da yerini gösterir."] =
            "It's dark in here. Press {0} to toggle your flashlight — it helps you " +
            "see, but it also shows the monster where you are.",
        ["Alçak bir geçit. Geçmek için {0} ile eğil."] =
            "A low passage. Press {0} to crouch and get through.",
        ["Bir kapı. Yanındaki düğmeye {0} ile bas."] =
            "A door. Press {0} on the button next to it.",
        ["Bu bir terminal. Bağlanmak için {0}. Dolarken ekranda çıkan yöne " +
            "hareket tuşlarınla hızlıca bas — yoksa kilitlenir."] =
            "This is a terminal. Press {0} to connect. While it fills, hit the " +
            "movement key it shows on screen — miss it and it locks.",
        ["Bu canavar. Fenerin onu görmeni sağlar ama seni de gösterir. " +
            "Koşarsan duyulursun, yerde iz bırakırsın. Yakalarsa anında elenirsin. " +
            "Gerçek oyunda ondan kaçıp saklanacaksın."] =
            "This is the monster. Your flashlight helps you see it, but it also " +
            "reveals you. Sprinting makes noise and leaves a trail. If it catches " +
            "you, you're eliminated instantly. In the real game you'll be running " +
            "and hiding from it.",
        ["Elenen bir arkadaşının cesedi böyle kalıyor. Taşımak için {0} ile al."] =
            "A downed friend's body stays on the map like this. Press {0} to pick it up.",
        ["Cesedi diriltme kabinine bırak, sonra kabinin yanındaki terminalden " +
            "diriltmeyi başlat. Orada da terminaldeki gibi yön sınavı var."] =
            "Drop the body into the revival booth, then start the revival from the " +
            "terminal next to it. It has the same direction challenge as a terminal.",
        ["Terminal bitince çıkış kilidi çalışır. Kapının yanındaki panele {0} ile " +
            "bağlan, on adımlık yön dizilimini gir ve açılan kapıdan geçip kaç."] =
            "Once the terminal is done, the exit lock works. Connect to the panel " +
            "next to the door with {0}, enter the ten-step direction sequence, then " +
            "escape through the open door.",
        ["Tebrikler, hazırsın! Ana menüye dönülüyor…"] =
            "Congratulations, you're ready! Returning to the main menu…",
        ["Tutorial bitti. Ana menüye dönülüyor…"] =
            "Tutorial over. Returning to the main menu…",
        ["NASIL OYNANIR"] = "HOW TO PLAY",

        // Tutorial'ın duraklatma ekranındaki çıkış düğmesi. Ana oyundaki
        // karşılığı "ODADAN AYRIL" — aynı ekran, çağırana göre değişen düğme
        // (bkz. `MenuSetup.BuildPausePanel`).
        ["TUTORIAL'DAN ÇIK"] = "LEAVE TUTORIAL",

        // ---------- 2026-09-26 denetiminde bulunan boşluklar ----------
        //
        // Üçü de arayüzde görünüyordu ama `Localization`'dan hiç geçmiyordu,
        // yani İngilizce seçiliyken Türkçe kalıyorlardı. `Get` bilinmeyen
        // anahtarı SESSİZCE geri döndürdüğü için hiçbir yerde hata yoktu.
        ["KOD YA DA IP ADRESİ"] = "CODE OR IP ADDRESS",
        ["Ceset taşınıyor — bırak · basılı tut: fırlat"] =
            "Carrying a body — press to drop · hold to throw",
        ["NetworkManager yok. Yakalamaca > Ağ Kurulumu (1. adım)."] =
            "No NetworkManager in the scene. Run Yakalamaca > Ağ Kurulumu (step 1).",

        // ---------- 2026-09-26: ikinci tur ----------
        //
        // `Kapıyı çalıştır` SAHNEDE serileşmiş bir değer (`UseButton.prompt`,
        // 17 düğme) ve `GetPrompt` onu zaten `Localization.Get`'ten
        // geçiriyordu — eksik olan tek şey tablo satırıydı. İlk denetim bunu
        // kaçırdı çünkü yalnızca `.cs` dosyalarına bakıyordu.
        ["Kapıyı çalıştır"] = "Operate the door",

        // Diriltme kabininin tabelası. Numara kaldırıldı (bkz.
        // `RevivalStation.BindSign`).
        ["DİRİLTME"] = "REVIVAL",

        // Karakter ekranı: artık "kostüm" demiyoruz. İki canavar gerçekten
        // ayrı katil (vuruşları farklı), kaçanlar ise yalnızca görünüş.
        ["KATİL"] = "KILLER",
        ["Katili oda sahibi seçiyor. Bu, katil olursan hangisini oynayacağın."] =
            "The room owner picks who plays the killer. This is which one you play if it's you.",
        ["Kaçanlar yalnızca görünüş olarak farklı: hız, boy ve menzil aynı."] =
            "Runners differ in looks only: speed, height and reach are the same.",

        // Karakter ADLARI tabloya GİRMİYOR: dördü de özel isim, iki dilde de
        // aynı yazılıyor. (`MUZ ADAM` bir süre burada çevriliyordu; ad 2026-09-26'da
        // Türkçe'de de BANANA MAN oldu ve satır ÖLDÜ, o yüzden silindi —
        // hiçbir şey yapmayan bir satır sonraki okuyanı yanıltıyor.)
    };
}
