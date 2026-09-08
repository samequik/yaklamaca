# Yakalamaca

Karanlık bir labirentte geçen, asimetrik çok oyunculu kovalamaca. Her turda
sunucu **bir kişiyi canavar** seçer, kalan herkes **kaçan** olur. Kaçanların işi
terminalleri açıp çıkıştan kurtulmak, canavarınki herkesi elemek. Süre sınırı
yoktur — tur, sahada oynayan kaçan kalmayınca biter (bkz. bölüm 11).

Bu dosya kodda dağınık duran "neden böyle" cevaplarını tek yerde topluyor.
Kaynak dosyalar buraya `CLAUDE.md bölüm N` ve `CLAUDE.md teknik borç N` diye
atıf yapıyor — numaraları değiştirirken kodu da güncelle.

---

## Şu an neredeyiz

**Baştan sona çalışan tur:** oyuncular lobide buluşur, oda sahibi canavarı
seçip turu başlatır, kaçanlar duvardaki terminalleri doldurur, gereken sayı
bitince çıkış açılır, kaçan oradan geçip kurtulur. Canavar yakalayıp eler,
terminalleri kilitleyerek geciktirir. Elenen ve kurtulan izleyici moduna
geçer. Tur bitince herkes lobiye döner.

**Bitmiş sistemler:** Source hareketi (kaçan) · araba modeli hareket (canavar,
bölüm 1) · Mirror ağ katmanı · rol dağıtımı · saldırı (sunucu otoriteli
isabet) · izler (yalnızca canavara) · duruş senkronu · ses (adım, iniş, kapı,
ölüm, terminal) · sürgülü kapılar ve düğmeler (bölüm 15) · izleyici modu ·
terminal + kaçış sistemi (bölüm 11) · harita giydirme ve prop dağıtımı ·
mağara yankısı (bölüm 12) · menü, lobi, ayarlar ve tuş atamaları (bölüm 13) ·
canavar modeli, animasyonları ve ışıkları (bölüm 14) · katman düzeni
(bölüm 16) · kaçan modeli ve yakalanma animasyonu (bölüm 17) · çıkış görünümü
ve kilit paneli (bölüm 18) · lightmap + occlusion · **EOS relay'i** ·
**kısa lobi kodu ve oda listesi** (bölüm 13) · **sesli sohbet, mikrofon
göstergesi ve TAB paneli** (bölüm 19) · **gerçek UI** (bölüm 20) ·
**fizik motorlu ceset/ragdoll** (bölüm 21) ·
**ceset taşıma ve diriltme** (bölüm 23) · git · **GitHub** (bölüm 24).

**Diriltme de bitti** (bölüm 23). Ceset haritada duruyor, taşınıyor, kabine
konuyor; terminalde 15 saniyelik işlem hatasız biterse kaçan orada diriliyor.
21.2'de açık bırakılan altı tasarım sorusunun beşi cevaplandı; kalan tek soru
**canavarın karşı hamlesi** — bugün diriltmeyi kesintiye uğratacak hiçbir aracı
yok.

**Sıradaki büyük iş, oynanarak ölçmek.** Yazılmış ama iki makineyle hiç
denenmemiş iki sistem var (sesli sohbetin ağ yolu ve ceset senkronunun istemci
tarafı) ve bütün denge sayıları hâlâ tahmin.

---

### 2026-09-05 oturumunda yapılanlar

Uzun bir oturumdu, 35 commit. Dört başlıkta topladım.

#### 1. Işık — dört ayrı arıza vardı, hepsi çözüldü

"Lambalar ışık vermiyor" şikâyeti tek bir hata değil, üst üste binmiş dört
ayrı sebep çıktı. Ayrıntılar bölüm 3'te:

- Pişirme sonrası **sahne kaydedilmiyordu** — occlusion verisi diskte duruyor
  ama sahne ona bakmıyordu.
- Giydirme bayrağı prefabın **alt objelerine** yazılmıyordu; lamba gövdeleri
  lightmap'e hiç girmiyordu. `Işığı Pişir`'e **adım 0** eklendi.
- **Pişirici ters-kare düşüş kullanıyor**, gerçek zamanlı ise affedici bir
  eğri. Aynı `0.75` iki modda bambaşka sonuç veriyor — ışığın tamamı tavanda
  toplanıyordu. Ölçümle doğrulandı (`Pişmiş ışığı ÖLÇ` düğmesi eklendi).
- Kapılar hareketli olduğu için **tam Baked onları hiç görmüyordu**; ışık
  kapalı kapıdan geçiyordu. **Mixed (Shadowmask)**'e geçildi.

Ardından **harita karartıldı** (bölüm 5): ortam 0.018 → 0.006, sis rengi
0.02 → 0.008, yansıma 1 → 0.2, `indirectScale`/`albedoBoost` 2/1.6 → 1/1.
Ölçüt tek cümle: **fenersiz görülmemeli.**

> Bu oturumun en pahalı dersi: **bir teşhis çürüdüğünde, o teşhis için yapılan
> değişiklikleri de geri al.** `indirectScale`/`albedoBoost` yanlış bir teşhis
> için konmuştu, teşhis düştü ama çarpanlar kaldı ve sonraki sorunun sebebi
> oldu. Aynı şekilde eklenen bir "şiddet çarpanı" geri alma adımında dengesiz
> bölme yapıp haritayı büsbütün karartmıştı.

#### 2. Canavar ve denge

- **Fener kaldırıldı.** Yerine kırmızı **hâle** (gövdede, 10 m) ve kırmızı
  **huzme** (kamerada, 13 m) geldi; huzme kapatılamıyor (bölüm 5).
- **Taban koşu 420 → 380 u/s**, yani kaçanınkinin %5 *altında*. Canavar
  kovalamacaya geride başlıyor, öne geçmesi tamamen hız payına bağlı.
  `boostMinSpeed` de 380 → 340 yapılmak zorundaydı, yoksa pay hiç dolmazdı.
- Saldırı ve yakalama boyunca **bakış kilitli** (`lockYawLimit` = 0): savurmak
  artık bir taahhüt.

#### 3. Terminal ve çıkış

- Her terminal **göstergesiyle aynı renkte az ışık** döküyor.
- **Çalışma ve uyarı sesi** eklendi (kullanıcının verdiği dosyalar). Çalışma
  sesi E'ye basar basmaz başlıyor ve kesintisiz.
- **Alarm süreli**: kaçanın hatasında ses + yanıp sönen kırmızı, süre bitince
  sabit kırmızı ve sessizlik. Canavarın kurduğu kilit hiç ötmüyor (bölüm 11.4).
- Alarm ışığı **sesin anlık genliğinden** sürülüyor — ayrı sayaç kullanılsaydı
  ikisi zamanla kayardı (bölüm 12).
- **Çıkış kapısı sessizdi**, çünkü ses aracı yalnızca `Harita/Kapilar` altını
  tarıyordu; artık sahnedeki bütün `SlidingDoor`'ları buluyor. Çıkış kilit
  paneli de terminalle aynı çalışma sesini kullanıyor.

#### 4. Ağ: internetten oynama

**Edgegap denendi ve bırakıldı.** Mirror kutuda getiriyordu, entegrasyon
yazıldı ve kod tarafı çalıştı; ama ücretsiz katmanda lobi servisi bir türlü
dağıtılamadı (`status: Error`, boş `url`, destekten dönüş yok). Çalıştırılamayan
bir servis kullanılamaz — Edgegap'e ait her şey projeden **tamamen silindi**.

**EOS (Epic Online Services) kuruldu ve ÇALIŞIYOR.** Lobi kurulduğunda kod 32
karakterlik EOS ürün kimliği olarak geliyor ve ekran "İnternet odası" diyor.
Buraya gelene kadar dört engel aşıldı, hepsi bölüm 9'da yazılı: Mirror'ın hata
olayının imzası · SDK kütüphanesinin koda gömülü yolu · `LoadLibrary`'nin ileri
eğik çizgiyi kabul etmemesi ve ANSI dönüşümü · girişin asenkron olması.

Lobi **iki transport** arasında seçim yapıyor (bölüm 13): EOS hazırsa internet
odası, değilse yerel oda. Katılma alanı hem kodu hem ham IP'yi alıyor.

**EOS iki makinede denendi ve bağlantı kuruldu** (2026-09-05). Relay tarafında
belirsizlik kalmadı.

#### 5. Kısa lobi kodu ve oda listesi

Relay çalışıyordu ama adres host'un 32 karakterlik `ProductUserId`'siydi:
kopyalanabiliyor, **söylenemiyor**. Oyun sesli sohbette oynandığı için "odama
gel" demenin yolu kodu okumak.

EOS'un **lobi servisi** devreye alındı (`RelayLobby`, bölüm 13). Host 6 harflik
rastgele bir kod üretip odaya öznitelik olarak yazıyor, katılan o kodla arayıp
uzun adresi EOS'tan alıyor. Uzun kod kaybolmadı, yalnızca artık görünmüyor.

Aynı servisten **oda listesi** de geliyor — teknik borç 5 kapandı. Katılma
ekranı altı odayı adı, kodu ve doluluğuyla gösteriyor; satıra basmak kodu alana
yazıp normal katılma yolunu işletiyor (ayrı bir "listeden katıl" yolu ikinci bir
hata kaynağı olurdu).

Pakete **beşinci bir yerel yama eklenmedi**: `RelayLobby`, paketin
`EOSLobby`'sinden türüyor. Bölüm 9'daki dört yama hâlâ dört.

#### 6. Oynanışta çıkan dört hata

İnternetten oynandığında görülenler. Dördü de kod tarafında; **hiçbiri için
araç çalıştırmak gerekmiyor**, yeni alanların C# varsayılanları zaten istenen
değer.

| Hata | Sebep | Nerede |
|---|---|---|
| Canavarın adım sesi kimseye ulaşmıyor | `FootstepAudio` `localOnlyComponents`'teydi: uzak oyuncuda **bileşen kapalıydı** | bölüm 12 |
| Duvara yaslanınca içi görünüyor | Kameranın yakın kırpma düzlemi 0.3 — köşesi gövde yarıçapını aşıyordu | bölüm 5 |
| Canavar kendi kafasının içini görüyor | Kafa sıfırlanıyordu ama **boyun** duruyordu; aradaki gerdirilmiş üçgenler kameradan geçiyordu | bölüm 14 |
| Kaçan zıplama animasyonunda takılı kalıyor | Çıkış şartı `falling`'e bağlıydı; kasaya çıkmak `airborne`'u kurup `falling`'i kurmuyordu ve bayrağı indirecek şart kalmıyordu | bölüm 17 |

> **Üçü de aynı sınıftan: "kapalı bileşenden veri okumak."** `PlayerController`
> uzak oyuncuda kapalı, yani `IsGrounded` ve `HorizontalSpeed` orada donmuş
> duruyor. Bölüm 14 ve 17 bu tuzağı zaten yazıyordu — ama yalnızca animatörler
> için. `FootstepAudio` aynı tuzağa düşmüştü ve kimse fark etmemişti, çünkü
> **kendi adımını duyuyordun**.
>
> Ders: bir bileşen `localOnlyComponents`'e konurken "bunun ÇIKTISINI başkası
> görüyor/duyuyor mu" diye sorulmalı. Görüyorsa liste yanlış yer.

---

### 2026-09-06 oturumunda yapılanlar

On commit. Üç büyük blok ve arkalarından gelen düzeltmeler.

#### 1. Doğum yerleşimi rol bazlı oldu

Canavar bazen bir kaçanın dibinde doğuyordu; artık kaçanlar bir arada, canavar
onlardan en uzak noktada başlıyor. Ölçüldü: kaçan noktası hangisi seçilirse
seçilsin canavar **en az 36.7 m** uzakta (harita 54.4 m).

Sebep şuydu: **Mirror doğum noktasını oyuncu objesi spawn olurken seçiyor** —
yani lobide, rol dağıtılmadan önce. Rol ancak tur başlarken belli olduğu için
yerleştirme `RoundManager`'a taşındı. Hareket istemci otoriteli olduğundan
ışınlamayı sahibine giden bir `TargetRpc` yapıyor. Ayrıntı bölüm 11.1'de.

#### 2. Sesli sohbet — sıfırdan yazıldı (bölüm 19)

Yeni paket YOK. Dissonance ücretli olduğu için elendi, Vivox 3B karışımı
sunucuda yaptığı için mağara yankısıyla çelişiyordu. Kendi kodumuz: G.711
µ-law, 8 kHz, 20 ms çerçeveler, konuşurken 64 kbit/s.

Gelen parçalar:

- **Yakınlık tabanlı konuşma.** Kimin duyacağına sunucu karar veriyor; lobide
  herkes herkesi, turda 18 m içinde, elenenler yalnızca kendi aralarında.
- **Mikrofon göstergesi** (sağ üst): çubuk mikrofonun duyduğunu, renk
  gönderilip gönderilmediğini söylüyor. Otomatik modda eşik çizgisi var.
- **TAB paneli**: kadro, ping, kişi bazlı susturma ve ses seviyesi.
- **Ayarlar ikiye ayrıldı**: seçenekler artık kategori kapısı, ses ve sesli
  sohbetin tamamı ayrı bir SES ekranında.

Mimarinin iki öngörüsü tuttu: 3B kaynak olduğu için **mağara yankısı bedavaya
geldi** ve kişi başı seviye `AudioSource.volume`'dan geldiği için
**AudioMixer gerekmedi**.

**Doğrulanmadı:** iki makineyle hiç denenmedi. Kendi sesimizi kendimize
göndermediğimiz için tek makinede ağ yolu sınanamıyor.

#### 3. Gerçek UI — teknik borç 2 kapandı (bölüm 20)

Oyun içi arayüzün tamamı `OnGUI`'den Canvas'a taşındı. Çalışma anında artık
hiç IMGUI yok.

Asıl sorun sıralamaydı: IMGUI her zaman Canvas'ın üstünde çiziliyor, o yüzden
`MenuController` tur yazılarını elle kapatmak zorundaydı — ve terminal/kilit
ekranlarını kapatmayı kimse yazmamıştı, yani **terminal başındayken Esc'ye
basınca ekran duraklatma menüsünün üstünde kalıyordu.** Canvas'ta sıralama
kendiliğinden doğru.

#### 4. Taşımanın ardından çıkan dört hata

Hepsi oynanırken bulundu ve dördü de öğreticiydi:

| Belirti | Gerçek sebep |
|---|---|
| Terminal ekranı hiç açılmıyor | Bileşen **kendi objesini** kapatıyordu; kapalı obje `Update` çalıştırmaz, yani kendini bir daha açamıyor |
| TAB açıkken hareket kesiliyor | Kaplama, menüyle aynı "tam duraklatma" yolundan geçiyordu |
| Panel ekranı kaplıyor | Skor tablosu tam ekran ve mat panel kullanıyordu — "yürümeye devam edebilirsin" özelliğini anlamsız kılıyordu |
| İmleç görünmüyor | `Cursor.visible`'ın **getter'ı yalan söylüyor**; "farklıysa yaz" koruması yazıyı atlıyordu |

Birincisi altı bileşende birden vardı ve üçü zaten ölüydü — TAB paneli hiç
açılmamış, mikrofon göstergesi kapatılınca geri gelmiyor, `GameHud` menü ilk
açıldığında bütün HUD'ı kalıcı söndürüyormuş. Terminal sadece ilk fark edilendi.

#### 5. Küçük ayarlar

- **Canavar ölçeği 1.18 → 1.30.** Ekrandaki boy 1.784 m; çarpışma kutusu
  değişmedi.
- **`Terminal.alarmDuration` kodda da 20 oldu** — sahnede zaten 20'ydi.
- **Canavarın havada animasyonu kapsam dışına alındı**: haritada düşülecek
  yüksek bir yer yok ve canavar zıplayamıyor.

#### Bu oturumun üç dersi

> **1. Bir bileşen kendi `GameObject`'ini kapatıyorsa, onu geri açacak kod
> nerede?** Cevap "aynı bileşende" ise o kod hiç çalışmayacak demektir.
> Görünürlük `CanvasGroup.alpha` ile yönetilmeli.
>
> **2. Bir Unity özelliğinin getter'ı, motorun uyguladığı durumu değil senin
> yazdığın değeri döndürebilir.** `Cursor.visible` kilitliyken "true" diyor
> ama imleç gizli. "Zaten doğru" varsayımıyla yazıyı atlamak burayı bozdu.
>
> **3. Devre dışı bir denetim "bozuk" diye okunuyor.** Ya sebebini yaz ya
> tamamen kaldır — ortası oynayanı yanıltıyor. Kendi satırındaki gri ses
> kaydırıcısı tam olarak bunu yaptı.

---

### 2026-09-07 oturumunda yapılanlar

Oturumun tamamı tek bir hedefe gitti: **ceset artık haritada kalan, itilebilen
bir gövde** (bölüm 21.1). Yanında hareketle ilgili birkaç ayar ve test
altyapısı geldi.

#### 1. Zıplama: havada kalma süresi kısaldı

Yerçekimi 600 → **900**, zıplama gücü 268.3 → **328.6**. İkisi BİRLİKTE
büyütüldü ki zıplama yüksekliği aynı kalsın (hâlâ ~60 unit / 1.14 m — iniş
sesi eşiği ve iz sistemi bu sayıya bağlı). Sabit yükseklikte havada geçen süre
`t = 2·√(2h/g)`, yani g büyüdükçe kısalıyor: **0.894 → 0.730 sn (%18 daha az).**

#### 2. Minecraft usulü sprint sıçraması

Koşarken zıplarsan gidiş yönüne bir seferlik **+60 u/s** ek itki biniyor
(`PlayerController.sprintJumpLunge`). Yalnızca zıplama anında, bir kez —
havada airstrafe ile kazanılan hızdan (bölüm 1, `airSpeedCap`) tamamen ayrı.

#### 3. Ceset ve ragdoll — oturumun asıl işi

Ayrıntı **bölüm 21.1**'de: `Corpse`, `RagdollFactory`, `RagdollSync` ve
`Ceset Sistemini Kur`. Yedi ayrı tuzağa düşüldü ve hepsi orada tablo hâlinde
yazılı — özellikle "kapalı `Animator`'da `GetBoneTransform` null döner" ve
"eklem projeksiyonu zinciri yerinde çiviler" maddeleri tekrar düşülmeye çok
müsait.

#### 4. Test botu artık insan modeli taşıyor

Bölüm 17'deki "botta model yok, kapsül yer tutucu" sınırı kapandı:
`RunnerSetup.AttachToSceneObject` kaçan modelini ve animasyonlarını bota da
bağlıyor (`Test Botu Ekle` çalıştırılınca). Bot yalnızca yürüme/koşma/zıplama
gösteriyor; ölüm koreografisi hâlâ yok ve `TestRunnerBot` zaten kendini
elendirmiyor.

Botun mavi işaret ışığı da elenince sönüyor artık — sönmezken ortada gövdesiz
bir parıltı kalıyordu.

#### 5. Test için tur bitişini kapatma

`RoundManager.disableRoundEndForTesting` — **varsayılan kapalı.** Açıkken tur,
kaçan kalmayınca bitmiyor. Tek bot kaçan olduğu turda onu öldürmek normalde
turu anında kapatıyor ve ceset/animasyon denemek için sürekli tur yeniden
başlatmak gerekiyordu. **İşin bitince kapat**: gerçek oynanışta bölüm 11.1'in
kuralı hep geçerli kalmalı.

#### Bu oturumun dersi

> **Fizikte bir belirtiyi tek başına okumak yanıltıyor.** "Ceset havada
> duruyor" cümlesi üç bambaşka sebebe uyuyordu: gövde kinematik kalmış,
> ragdoll hiç kurulamamış, ya da bir kısıt zinciri çivilemiş. Tahminle tek tek
> denemek dört tur sürdü; çözümü getiren şey **teşhis logu** oldu — parça
> sayısı, simülasyonun hangi tarafta olduğu, kalçanın kinematic/gravity/uyku
> durumu. Fizik hatalarında ölçmek, denemekten ucuz.

---

### 2026-09-08 oturumunda yapılanlar

Tek hedef: **diriltme** (bölüm 23). GPT-6'nın yarım bıraktığı uygulama
tamamlandı, sonra dört tur oynanış geri bildirimiyle elden geçirildi. Sonunda
proje GitHub'a gönderildi (bölüm 24).

#### 1. Diriltme: spesifikasyondan çalışan mekaniğe

Bölüm 21.2'deki akış bitti. 21.2'de karara bağlanmak üzere bırakılan **altı
tasarım sorusunun beşi cevaplandı** — tablo bölüm 23'te. Kalan tek soru
canavarın karşı hamlesi ve bilinçli olarak açık bırakıldı: diriltme şu an tek
taraflı bir kazanç.

#### 2. Diriltme hakkı: sınırsız diriltme turu bitmez yapıyordu

`RevivalStation.charges` (varsayılan **1**, her tur başında yenileniyor). Bölüm
11.1'e göre tur ancak sahada oynayan kaçan kalmayınca bitiyor; dolu kadroda
herkes geri gelebiliyorsa o an hiç gelmiyor. İki kabin var, yani tur başına
iki diriltme.

Hak **tur başında** yenileniyor, kabin her sıfırlandığında değil — sıfırlama
başarılı bir diriltmeden sonra da çalışıyor ve orada yenilemek sınırı büsbütün
anlamsız kılardı.

#### 3. Ölü test botu

`Test Botu Ekle (ölü — ceset testi)` tur başlar başlamaz elenen ikinci bir bot
koyuyor: taşımayı denemek için artık önce birini öldürmek gerekmiyor. Eleme
**normal yoldan** (`RoundManager.ReportCaught`) yapılıyor, yani ölüm
animasyonu, ceset doğumu ve sayaçlar gerçek turdaki gibi işliyor — test edilen
şey gerçekten oyunun kendisi oluyor.

#### 4. Dört tur oynanış geri bildirimi

Hepsinin ayrıntısı bölüm 23'te; özet:

| Tur | Şikâyet | Ne yapıldı |
|---|---|---|
| 1 | Ceset kaygan ve hafif · taşırken görünmüyor · ayağın dibine bırakılıyor · kabin ekranı çirkin | Sürtünme materyali + sönümleme, elde taşıma, bakılan yöne bırakma, terminal görsel diline geçiş |
| 2 | Kabin cesedi saymıyor · taşınan ceset donuyor | `RevivalStationRelay`; yalnızca kalça sabitleniyor, uzuvlar sarkıyor |
| 3 | Koşarken titriyor · duvardan geçiyor · terminal zor algılıyor | `MovePosition`/`MoveRotation`, collider'lar açık + `IgnoreCarrier`, menzil 2.8 → 4 m |
| 4 | Kabine bırakmak saymıyor · kalça duvara giriyor, uzuvlar çıldırıyor | `TryAcceptNearbyCorpse`; taşıma noktası küre ışınıyla sınırlı — önü kapalıysa ceset taşıyana yaklaşıyor |

#### Bu oturumun iki dersi

> **1. Bir iyileştirme, var olan bir boşluğu görünür hâle getirebilir.**
> "Cesedi bakılan yöne bırak" değişikliği gövdeyi tam kabinin içine düşürdü ve
> kabin onu saymadığı için "bozuldu" gibi göründü. Oysa kabinin GÖVDESİ hiçbir
> zaman tıklanabilir değildi (`GetComponentInParent` terminale ulaşamıyordu);
> o güne kadar kimse tam oraya bakıp E'ye basmamıştı.
>
> Ders: yeni bir hata gibi görünen şey, eski bir boşluğun yeni yoludur.
> "Değişiklikten önce çalışıyordu" cümlesi tek başına kanıt değil.
>
> **2. Kinematik bir gövdeyi hiçbir şey durdurmaz.** Taşınan cesedin kalçası
> duvarın içine giriyor, sarkan uzuvlar o derin çakışmayı çözmeye çalışıp
> savruluyordu — yani "uzuvlar çıldırıyor" bir sebep değil **sonuçtu.** İki tur
> boyunca uzuvlar ayarlandı ve hiçbiri işe yaramadı; çözüm kalçayı durdurmak
> oldu. Bölüm 21.1'in dersinin aynısı: fizikte belirtiyi tek başına okumak
> yanıltıyor.

---

### Sıradaki adımlar

#### Önce: bekleyen araç çalıştırması

```
Yakalamaca > Menü Kur
```

Skor tablosu satırlarındaki açıklama etiketi (`Not`) sahnede henüz yok. Onsuz
kendi satırında ve botta boş bir alan kalıyor ve "bozuk" gibi duruyor.
2026-09-06'dan beri bekliyor.

> Bir sahne değişikliğinin gerçekten uygulanıp uygulanmadığını **sahne
> dosyasından** doğrulayabilirsin, tahmin etmeden:
> `grep -c "m_Name: Not$" Assets/_Scenes/SampleScene.unity`

Ceset sisteminin araçları (`Ceset Sistemini Kur`, `Test Botu Ekle`) 2026-09-07'de
çalıştırıldı ve sahneden doğrulandı; onlar için bekleyen bir şey yok.

#### Sonra: iki doğrulama, ikisi de oynayarak

**1. Sesli sohbeti iki makinede dene.** Yazıldı, derlendi, kuruldu ama **ağ
yolu hiç sınanmadı** — kendi sesimizi kendimize göndermiyoruz. Listenin en
tepesindeki iş, çünkü altında bir sürü varsayım var: jitter tamponu,
unreliable kanal, sunucu taraflı mesafe süzmesi.

Bakılacaklar: sağ üstteki çubuk kırmızıya dönüyor mu (gri = duyuyor ama
göndermiyor) · karşı taraf 18 m içinde mi · elenen biri konuşabiliyor mu
(konuşmamalı) · TAB panelinde karşı tarafın satırında kaydırıcı ve SUSTUR
çıkıyor mu.

**2. Cesedi iki makinede dene** — sesli sohbetle **aynı sınıftan bir boşluk.**
Ceset host'ta çalışıyor, ama host'ta `RagdollSync.Update` ilk satırda
`isServer` görüp çıkıyor: yani **senkron yolunun istemci tarafı bugüne kadar
bir kez bile çalışmadı.** Ragdoll'un tamamı orada kinematik ve pozu ağdan
alıyor; sınanmamış varsayım az değil (paketin çözülmesi, kemik indekslerinin
iki tarafta tutması, yumuşatma).

Bakılacaklar: ceset karşı tarafta da aynı pozda mı · biri iterken öbürü
hareketi görüyor mu · ceset oturunca iki ekranda aynı yerde mi duruyor.

**3. Denge ölçümü.** Bütün sayılar hâlâ tahmin. Özellikle **direksiyon cezası**
(bölüm 1) yepyeni ve hiç ölçülmedi: canavar artık hem %5 yavaş başlıyor hem
köşelerde pay kaybediyor, fazla zayıflamış olabilir. Profiller Play modunda
değiştirilince kalıcı.

Zıplama da bu oturumda değişti (yerçekimi 900, sprint sıçraması +60 u/s):
koşarken zıplamak artık gözle görülür bir mesafe kazandırıyor, kaçanın canavara
karşı yeni bir aracı. Ölçülmedi.

#### Kalan işler

| # | İş | Not |
|---|---|---|
| 0 | ~~**Diriltme sistemi**~~ | **YAPILDI** (2026-09-08, bölüm 23). Açık kalan tasarım soruları: canavarın karşı hamlesi, bilgi sızıntısı, diriltme sayısı sınırı |
| 1 | **Yakınlık sesi (kalp atışı)** | Ses dosyası **oyuncudan gelecek**, sentezlenmeyecek. `Assets/_Audio/KalpAtisi.*`. **2B olmalı** — yönü belli olursa gerilim radara döner (bölüm 12) |
| 2 | **Bıçak sesleri** (teknik borç 1) | Hâlâ sentetik yer tutucu, üstelik bıçak kaldırıldı; elle saldırıya göre yeniden seçilmeli |
| 3 | **Çıkış engelinin adanmış sunucu farkı** | Bölüm 16'nın sonunda; host modunda oynadığımız için bugün görünmüyor |
| 4 | **Kapıdan vuruş** | İki oyuncu da kapıya 0.3 m mesafedeyken ışın kapıya varmadan kesiliyor ve isabet sayılıyor |
| 5 | **`EosApiKey.asset` client secret** | Depo **GİZLİ** olduğu sürece sorun yok. Herkese açık yapmadan önce Epic'ten **anahtar yenilenmeli** — dosyayı silmek yetmiyor, anahtar git geçmişinde (bölüm 24) |

**Yedek yol duruyor:** yerel oda + Radmin/Hamachi. EOS'a hiç bağlı değil,
bugün çalışıyor. Host olurken makinenin bütün IPv4 adresleri ekranda yazıyor.

**Kapsam dışı bırakıldı:**
- **Fener pili.** Fener açık/kapalı olarak kalıyor, şarj ya da tükenme
  mekaniği olmayacak (2026-09-03 kararı).
- **Canavarın havada animasyonu.** Haritada düşülecek yüksek bir yer yok ve
  canavar zıplayamıyor, yani o durum hiç oluşmuyor (2026-09-06 kararı).
  Haritaya yükseklik eklenirse geri gelir.

---

## 0. Ortam ve değişmez kurallar

| | |
|---|---|
| Unity | 2022.3.62f3 |
| Render pipeline | **Built-in** (URP/HDRP değil) |
| Ağ | Mirror 96.11.0, KcpTransport, UDP 7777 |
| Girdi | **Eski Input Manager** — Input System paketi kurulu değil |
| Hedef | PC (Windows, Mac, Linux), itch.io |

**Bağımlılık eklemeden önce iki kez düşün.** Şu ana kadar her şey bu dörtlüyle
çözüldü. Yeni bir paket, taşınabilirliği ve derleme süresini bozuyor.

Girdi konusu özellikle önemli. Tuşlar artık koda gömülü değil,
`KeyBindings`'ten okunuyor (bölüm 13) — ama altta hâlâ eski Input Manager var
ve yeni kod da onu kullanmalı, ikisi karışmamalı.

**Eksenler bırakıldı.** Hareket eskiden `Input.GetAxisRaw("Horizontal")` ile
okunuyordu; eski Input Manager'ın eksenleri **çalışma anında
değiştirilemediği** için yeniden atama isteyince tek tek tuşlara geçildi.
Sonuç aynı -1/0/+1, hareketin hissi değişmedi. Geriye yalnızca `Mouse X/Y`
eksenleri kaldı — onlar tuş değil, cihazın kendisi.

Input System'e geçilecekse hâlâ tek dosya değişir: `PlayerInputSource`
(bkz. bölüm 5, soyutlama kuralı).

---

### HARİTA ELLE DÜZENLENDİ — DOKUNMA

**2026-08-31'den itibaren harita elle düzenleniyor.** Aşağıdaki iki araç elle
yapılan her şeyi siler ve sahneyi **kendileri kaydettiği** için Ctrl+Z kurtarmaz:

| Araç | Ne siler |
|---|---|
| `Labirent Harita Kur` | `Harita`nın **tamamı** |
| `Atmosfer Kur` | `Lambalar` grubu ve tavan |

**Kural: bu ikisini asla kendiliğinden çalıştırma, çalıştırılmasını da önerme.**
Gerçekten gerektiğine inanıyorsan **önce sor** ve neyin kaybolacağını say.

Aynı kural yeni kod için de geçerli: `Harita` altındaki objeleri silen, toptan
taşıyan ya da yeniden üreten bir araç yazmadan önce sor. "Nasıl olsa araç
yeniden üretir" varsayımı artık geçersiz — üretilen şey elle düzenlenmiş olanı
geri getirmiyor.

**Güvenli araçlar** (kendi gruplarını yeniden kuruyorlar, haritaya dokunmuyorlar):
`Katmanları Kur` · `Haritayı Giydir` · `Harita Süsle` · `Mağara Yankısı Kur` ·
`Hataları Temizle` · `Ağ Kurulumu` · `Canavar/Kaçan Modelini Kur` ·
`Işığı Pişir`

**`Terminal ve Çıkış Kur` kısmen güvenli:**

- **Terminallere dokunmuyor.** Var olanlar olduğu yerde kalıyor, yalnızca eksik
  olan tamamlanıyor. (2026-09-03'e kadar öyle DEĞİLDİ: araç `HedefSistemi`'ni
  komple silip her şeyi yeniden kuruyordu ve elle taşınmış terminaller her
  çalıştırmada rastgele yerlere dağılıyordu. Bir kez gerçekten kaybedildi ve
  git'ten geri alındı.)
- **Çıkışları yeniden kuruyor.** Gedik deterministik seçildiği için aynı yere
  geliyor, ama çıkış kapısını/panelini elle ayarladıysan o ayar gider.

**Yedek var.** Proje 2026-08-31'de git deposuna alındı; ilk commit haritanın
düzenleme öncesi hâli. 2026-09-08'den beri **GitHub'da gizli bir depoda** da
duruyor (bölüm 24) — yani disk giderse proje gitmiyor. Kayıt noktaları `git log`, son kayda dönüş
`git checkout -- .`, belirli bir noktaya dönüş `git reset --hard <commit>`.
Düzenleme sırasında ara ara `git add -A && git commit -m "..."` yapılmalı.

---

## 1. Hareket: Source modeli

`PlayerController` Half-Life 2 / Garry's Mod hareket mantığının uyarlaması.
Modern platformer kontrolcülerinden farkı: **hedef hıza yumuşatarak yaklaşmaz.**
Her kare önce hıza sürtünme uygular, sonra bakış yönüne ivme ekler. Airstrafe,
momentum koruma ve bunny hop bu iki adımın doğal sonucu — ayrıca kodlanmış
özellikler değil.

Bu yüzden hareket koduna "daha kolay olsun" diye Lerp sokmak, oyunun his
karakterini bozar.

### Birimler

Tüm hız ve ivme değerleri **Source biriminde** (unit/saniye) tutulur, böylece
bilinen cvar değerlerini (`sv_friction`, `sv_accelerate`) doğrudan yazabilirsin.
Unity'ye taşınırken metreye çevrilir:

```
PlayerController.UnitsToMeters = 0.01905
```

### Gövde ölçüleri (hull)

| | Unit | Metre |
|---|---|---|
| Ayakta boy | 72 | 1.372 |
| Eğilmiş boy | 36 | 0.686 |
| Yarıçap | 16 | 0.305 |
| Ayakta göz hizası | 64 | 1.219 |
| Eğilmiş göz hizası | 28 | 0.533 |
| Basamak yüksekliği | 18 | 0.343 |

Yürüme 200 u/s, sprint 400 u/s, zıplama 268.3, yerçekimi 600.
**Zıplama yüksekliği 1.14 m** — iniş sesi eşiği ve iz sistemi bu sayıya bağlı.

### Canavar Source modelini kullanmıyor: araba modeli

**Yukarıdaki her şey kaçan için.** Canavarın hareketi 2026-08-29'da bilinçli
olarak ayrıldı ve `MovementProfile` üzerinden veriliyor:

| | Kaçan | Canavar |
|---|---|---|
| Taban koşu | 400 u/s | **380 u/s — %5 DÜŞÜK** |
| İvme (`accelerate`) | 14 | **14 — aynı** |
| Sürtünme (`friction`) | 5.5 | **5.5 — aynı** |
| Hız payı | yok | **+180 u/s**, 3.5 sn koşuyla dolar |
| Zıplama | var | **yok** |
| Duvara toslama | ceza yok | **hız ve pay sıfır** |
| Keskin dönüş | ceza yok | **pay siliniyor** |
| Aşağı bakış | **70°** | **55°** |
| Kamera payı | yok | **+6 unit yukarı, +0.10 m ileri** |

**İlk deneme yanlıştı ve düzeltildi.** İvme 0.8'e düşürülmüştü; bu canavarı
duruştan kalkarken de ağırlaştırıyordu, her yavaşlama bir cezaya dönüşüyordu.
Doğrusu tabanı normal bırakıp **üstüne** eklemek — araba da duruştan seyir
hızına çabuk çıkar, yavaş olan kısım SON hıza varmaktır.

Mantığı: canavar hızını *momentum hilesinden* değil, **kesintisiz koşarak**
kazanıyor. Zıplayamadığı için bhop da yapamıyor. Pay yalnızca 340 u/s üstünde
koşarken doluyor (yürüyerek sinsice hız depolayamıyor) ve koşu kesilince 1.2
saniyede boşalıyor: kazanması emek, kaybetmesi kolay.

**Taban hız 2026-09-04'te 420'den 380'e indirildi** — artık kaçanınkinin %5
*altında*. Canavar kovalamacaya geride başlıyor ve öne geçmesi tamamen paya
bağlı: 3.5 saniye kesintisiz koşabilirse 560 u/s'ye (10.67 m/s) çıkıp kaçanın
7.62'sini rahatlıkla geçiyor, ama her köşe onu başa döndürüyor. Kovalamacanın
ilk saniyeleri artık kaçanın.

> **Eşik taban hızın ALTINDA olmalı.** `boostMinSpeed` 380'de bırakılsaydı
> canavarın tavanı tam eşiğe oturur, sürtünme/ivme salınımı yüzünden pay ya hiç
> dolmaz ya da kesik kesik dolardı — "sonradan hızlanır" fikri sessizce
> çalışmazdı. 340 yapıldı; yürümenin (200) hâlâ belirgin üstünde.

**Çarpışmanın ölçütü temas değil, yüzeye GİREN hız bileşeni**
(`PlayerController.TryCrash`). Koridorda duvarı sıyırarak koşmak neredeyse
sıfır bileşen üretiyor ve cezalandırılmıyor; dosdoğru toslamak tam hızı
üretiyor. Zemin hariç tutuluyor — yerçekimi her karede zemine bastırdığı için
zemin de sayılsaydı yürürken sürekli duruyorduk.

Çarpmanın asıl cezası hız değil **pay**: anlık hızı kaybetmek birkaç saniyelik
iş, payı yeniden doldurmak koridoru baştan koşmak demek. Labirent böylece
canavarın rakibi oluyor, kaçanın keskin dönüşleri gerçek bir savunma hâline
geliyor.

### Direksiyon: köşe dönmek payı siliyor (2026-09-05)

> **Yukarıdaki paragraf bir süre YALAN söylüyordu.** "Her köşe onu başa
> döndürüyor" yazıyordu ama kodda karşılığı yoktu: pay `koşuyor && yerde &&
> hız ≥ eşik` iken doluyor ve bu üç şart **dönerken de** sağlanıyordu. Canavar
> tam hızla 90° dönüp hiçbir şey kaybetmiyordu. Var olan tek ceza duvara
> toslamaktı ve iyi oynayan hiç toslamıyor.
>
> Arkadaşlarla oynandığında "canavar çok güçlü olmuş" denmesinin sebebi buydu.
>
> Ders: **bir belge cümlesi mekaniğin var olduğunu kanıtlamaz.** Niyet
> yazılmıştı, uygulaması hiç gelmemişti ve aradaki fark yalnızca oynanınca
> ortaya çıktı.

Ölçüt **bakış hızı değil**, gidilen yön ile gidilmek istenen yön arasındaki açı
(`PlayerController.SteerPenalty`). Bakışı ölçmek düz koşarken etrafa bakınmayı
cezalandırırdı ve canavarı kör hâle getirirdi; burada ölçülen şey direksiyon
açısı. Tuşa basılmıyorsa istek yok, ceza da yok.

| Alan | Değer | Ne yapıyor |
|---|---|---|
| `steerFreeAngle` | 25° | Buraya kadar bedava — rota düzeltmek ceza olmamalı |
| `steerFullAngle` | 80° | Burada ceza tam; arada doğrusal |
| `steerScrubTime` | 0.35 sn | Tam cezada dolu payın boşalma süresi |
| `steerMinSpeed` | 340 u/s | Altında dönmek bedava (pay da zaten dolmuyor) |

**Ceza dönüşün süresince birikiyor ve kendiliğinden ölçekleniyor.** Açı, hız
yeni yöne oturdukça kapanıyor: 90°'lik bir köşe payın kabaca üçte birini
götürüyor, geri dönüş çok daha fazlasını. Ayrı bir "kaç derece döndü" sayacı
gerekmedi.

**Dönerken pay dolmuyor da.** Dolmaya devam etseydi ceza ile kazanç aynı karede
birbirini yer, köşe yine bedava kalırdı.

**Kaçana hiç dokunmuyor** — ceza payı siliyor ve kaçanın payı yok, o yüzden
`TickBoost`'un ilk satırındaki erken çıkış onu zaten dışarıda bırakıyor.

Sayılar yine tahmin. Çok sert gelirse `steerScrubTime` büyütülür (0.6 kabaca
yarı ceza), yumuşak gelirse küçültülür.

Sayılar tahmin, ölçüm değil. `Yakalamaca > Hareket Profillerini Sıfırla`
tavsiye edilen başlangıcı yazıyor; gerisi oynayarak ayarlanacak (bölüm 10).

**Yeni bir `MovementProfile` alanı eklersen o menüyü tekrar çalıştır:** var
olan `.asset` dosyaları yeni alanı C# varsayılanıyla alıyor, yani kod
değişikliği tek başına hiçbir şey yapmıyor.

### Koridor genişliği

Hızlı hareket için koridor **3–3.6 m** olmalı. Labirent 3.2 m kullanıyor
(`MazeMapBuilder.CellSize`). Daha dar olursa Source hızında duvara sürtmeden
koşmak imkânsızlaşır, daha geniş olursa kovalamaca gerilimi kaybolur.

Eğilme geçidi 1.4 m genişlik × 1.1 m yükseklik.

**Eğilirken kamera öne de kayıyor** (`duckedCameraForward`, 0.25 m). Eğilme
animasyonunda gövde öne eğilip kafa ileri çıkıyor; kamerayı yalnızca aşağı
indirmek gözü gövdenin içinde bırakıyor ve karakterin kafası ekranın önünde
kalıyordu.

**Değer `CameraBob` üzerinden geçiyor.** İlk denemede pay yalnızca
`ApplyDuckGeometry` içinde yazıldı ve **hiçbir etkisi olmadı**: `CameraBob`
her `LateUpdate`'te `localPosition.z`'yi sıfıra sabitliyordu ("PlayerController
sadece y'ye dokunuyor" varsayımıyla, ki artık doğru değil). Inspector'dan değeri
300 yapsan bile hiçbir şey değişmiyordu. Şimdi tek kaynak
`PlayerController.CameraForwardOffset` ve CameraBob onu okuyor.

---

## 2. Bellek: havuzlama

Oyun boyunca sürekli yaratılıp yok edilen hiçbir şey `Instantiate`/`Destroy`
kullanmaz. GC tıkanması, kovalamaca ortasında kare atlaması demek — ve tam o
anda kare atlaması oyunu kaybettirir.

`TrailMarkSystem` bunun örneği: 256 izlik havuz oyun başında doldurulur, izler
`SetActive` ile açılıp kapanır. Yeni bir sürekli efekt (kan, kıvılcım, duman)
eklerken aynı deseni kullan.

---

## 3. Performans: batching, occlusion, lightmap

Harita 54.4 m kare, 17×17 hücre, giydirmeyle birlikte 600–800 parça.

**Static işaretleme şart.** Üretilen her harita parçası
`BatchingStatic | OccluderStatic | OccludeeStatic | ContributeGI` alır.
Prefab'ların **alt objelerine de** uygulanmalı — kök objeyi işaretlemek yetmez,
asıl mesh'ler alt objelerde ve onlar batching'e girmez.

**Materyaller `enableInstancing = true`.** Yüzlerce blok aynı materyali
paylaşıyor.

**Lightmap PİŞTİ** (2026-09-03). `Yakalamaca > Işığı Pişir (lightmap)`
hazırlık adımlarını ve pişirmeyi yapıyor; sahnedeki 16 ışık artık `Baked`, tek
gerçek zamanlı kalan `Fener`. Haritaya static bir parça eklenirse yeniden
pişirmek gerekir (dakikalar sürer).

Aracın kararları — değiştirmeden önce sebeplerini oku:

**Mixed (Shadowmask) — 2026-09-04'te Baked'den çevrildi.** Burada uzun süre
"Baked, Mixed değil" yazıyordu; gerekçesi "lambaların gölgesi zaten kapalı,
Mixed hiçbir şey kazandırmaz"dı. O gerekçe pişirmeyle birlikte **gölgeler
açılınca geçersizleşti** ve tam Baked oynanışta bozuk çıktı: kapılar hareketli
olduğu için static değiller, pişirici onları hiç görmüyor — ışık **kapalı
kapının içinden geçiyordu** ve oyuncular gölge düşürmüyordu. Karanlığın ve
gölgenin oynanışın kendisi olduğu bir oyunda bu kabul edilemez (bölüm 5).

Mixed'de dolaylı ışık ve static gölgeler yine pişiyor; yalnızca doğrudan ışık
çalışma anında veriliyor, böylece hareketli her şey gölge düşürüyor. Bedeli
14 lambanın motordan tamamen düşmemesi — `pixelLightCount` 4'te ve gölge
mesafesi 35 m'de (sis görüşü ~25 m) tutulduğu için sınırlı.

Pencerede **"Karışık aydınlatma"** onay kutusu kapatılırsa tam Baked'e
dönülüyor. Dönülürse lamba şiddeti ~5 yapılmalı — sebebi hemen aşağıda.

**Gölgeler pişirme için AÇILIYOR.** Ters gibi görünüyor. Gerçek zamanlı
gölgesiz nokta ışık duvarı tanımaz — bugün 8 m menzilli lamba yan koridora
sızıyor. Pişirmede gölge bir çalışma anı maliyeti değil, "ışık geometriyi
görsün mü" anahtarı; kapalı bırakılırsa lightmap de duvarların içinden geçen
ışıkla pişer. Pişmiş gölgenin çalışma anı maliyeti sıfır.

**Pişirilmiş nokta ışığı, gerçek zamanlıdan ÇOK daha sönük — şiddet
yükseltilmeli.** İlk pişirmelerden sonra "lambalar hiç ışık vermiyor" diye
bildirildi ve günlerce yanlış yerlerde arandı. Sebep şu:

**İki mod farklı düşüş eğrisi kullanıyor.** Built-in'in gerçek zamanlı nokta
ışığı menzile göre normalize edilmiş, affedici bir eğri kullanıyor; **pişirici
ise fiziksel ters-kare.** Aynı şiddet değeri iki modda bambaşka sonuç veriyor.

**Ölçümle doğrulandı** (`Işığı Pişir > Pişmiş ışığı ÖLÇ`). 0.75 şiddette:

| Ölçüm | Değer | Neye denk geliyor |
|---|---|---|
| En parlak texel | 1.803 | Lambanın 0.4 m üstündeki tavan: `0.75/0.4² × 0.38 ≈ 1.78` |
| Aydınlık texel | %9.6 | 14 lambanın havuzları — beklenen oran |
| Zemin (hesap) | ~0.042 | `0.75/2.6² × 0.38`, ortam ışığı zaten 0.018 |

Yani ışığın neredeyse tamamı **kimsenin bakmadığı tavanda** toplanıyordu;
oyuncunun bastığı zemine ambient'in iki katı düşüyordu ve bu, sisle birlikte
"hiç ışık yok" olarak görünüyordu. Atlas parlaktı, ışık doğru pişmişti —
yanlış olan tek şey sayının kendisiydi.

`AtmosphereSetup.LightIntensity` artık **5** (zemin ≈ 0.28). Aynı sebeple test
botunun işaret ışığı 1.8 → 12.

**Yalnızca nokta ve spot ışıkları etkileniyor.** `Directional`ın mesafeye bağlı
düşüşü yok, o yüzden 0.05'e dokunulmadı. Fener zaten gerçek zamanlı kalıyor.

**Gerçek zamanlıya dönülürse fazla parlak gelecek, bilerek:** gönderilecek hâl
pişirilmiş, gerçek zamanlı bir hata ayıklama yedeği.

Ayrıca sekme ışığı için iki ayar var:

| Alan | Değer | Ne yapıyor |
|---|---|---|
| `indirectScale` | 2 | Sekme ışığı — köşeleri dolduran şey |
| `albedoBoost` | 1.6 | Koyu duvarlar gerçekçi albedo'da hiç yansıtmıyor |

İkisi de "doğruluk" değil **okunabilirlik** ayarı, bilerek gerçekçinin üstünde.
İkisi de yalnızca pişirme ayarı; hiçbir ışığa dokunmuyorlar.

> **Araç ışıkların ŞİDDETİNE dokunmuyor — bir kez denendi ve geri alındı.**
> Bir sürüm "pişmiş ışık daha sönük" gerekçesiyle Baked'e geçerken şiddeti 3'le
> çarpıyor, "geri al"da bölüyordu. Simetri yalnızca ikisi de **aynı sürümle**
> çalıştırılırsa tutuyor: önceki oturumda pişirilmiş bir sahnede "geri al" hiç
> çarpılmamış şiddetleri böldü, 14 lamba 0.75'ten **0.25'e** düştü ve harita
> büsbütün karardı. Üstelik teşhis edilmeye çalışılan sorunun sebebi de o
> değildi.
>
> Ders: **bir aracın geri alma adımı, ileri adımın çalıştığını varsayamaz.**
> Sahne aracın önceki sürümüyle, elle ya da hiç işlenmemiş olabilir. Simetrik
> çarpan/bölen yerine ya değer hiç değiştirilmemeli ya da özgün değer
> saklanmalı. Işık şiddeti `AtmosphereSetup`'ın işi (`LightIntensity = 0.75`),
> pişirme aracının değil.

**Işık probe'ları şart, süs değil.** Oyuncular static değil; bütün ışık
lightmap'e girerse hareket eden hiçbir şey ondan pay almaz ve ortam ışığı
0.018 olduğu için kaçan da canavar da simsiyah kesilir. Araç labirentin
yürünebilir hücrelerine probe ızgarası kuruyor (yürünebilirlik fizikle
ölçülüyor, AtmosphereSetup'ın lamba yerleştirmesiyle aynı yöntem). Fener
gerçek zamanlı kalıyor: oyuncuyu aydınlatan ve dinamik gölge düşüren tek
kaynak o.

**Lightmap UV'si iki ayrı sorun.** SciFi Kit'in 83 FBX'inde
`generateSecondaryUV` kapalı geliyor — açılıp yeniden import edilmeleri
gerekiyor. Primitif küplerde (zemin, tavan, eğilme geçitleri) ise import ayarı
yok ve yerleşik küpün UV'sinde altı yüz aynı kareye biniyor; araç `Unwrapping`
ile ayrık adalı bir kopya üretip `Assets/_Art/Meshes` altına kaydediyor.
Çarpışma BoxCollider'dan geldiği için mesh'i değiştirmek hiçbir şeyi bozmuyor.

**Static bayrağı prefabın ALT objelerine de yazılmalı — yoksa sessizce sönük
kalıyorlar.** Giydirme araçları bayrağı prefabın **köküne** yazıyor. Kullanılan
16 kit prefabının 14'ünde renderer zaten kökte, o yüzden yıllarca sorun
çıkmadı. İkisinde mesh alt objede duruyor:

| Prefab | Sahnede | Renderer nerede |
|---|---|---|
| `Wall Plain`, `Floor Tile 01`, `Ceiling Closed`, kasa, varil… | 625 | kökte |
| **`Hanging Light`** | 14 lamba | 2 alt objede |
| **`Wall BayDoor`** | 7 kapı | 3 alt objede |

Alt obje bayrağı almayınca zincir şöyle işliyor: ContributeGI yok →
`CollectGiRenderers` onu görmüyor → `generateSecondaryUV` hiç açılmıyor →
lightmap UV'si olmayan mesh pişmiş ışık alamıyor. Işıklar tam Baked olduğu
için başka kaynak da yok, ambient 0.018 — **lamba gövdeleri kapkara kalıyor.**

Oyunda bu "lambalar ışık vermiyor" diye görünüyordu. Zemin ve duvarlar aslında
doğru aydınlanıyordu; kararan yalnızca lambanın kendi gövdesiydi. Hiçbir yerde
hata yazmıyor.

`Işığı Pişir` penceresine **adım 0** eklendi: ContributeGI'lı bir atası olup
kendisi olmayan renderer'lara atanın bayraklarını yazıyor. Denetim raporu da
bu durumu sayıyor.

**Kapılara dokunmuyor, bilerek.** Adım 0 yalnızca ContributeGI'lı ataya sahip
renderer'lara yazıyor; kapı giydirmelerinin (`Giydirme_Kapi`) kökünde hiç
bayrak yok çünkü kapı hareket ediyor — pişmiş ışık kapıyla birlikte kaymaz.
Onlar ışığı probe'lardan alıyor ve öyle kalmalı.

**Occlusion culling: pişirmek yetmiyor, sahneyi de kaydetmek gerekiyor.**
Occluder/Occludee bayrakları harita kurulurken atanıyor. Pişirme aynı pencerede
ayrı bir düğme — ama pişirmenin **iki** çıktısı var: diske yazılan
`OcclusionCullingData.asset` ve sahnedeki ona bakan referans. İkincisi sahne
kaydedilmezse kayboluyor.

Tam olarak bu oldu: dosya pişti, commit'e bile girdi, ama sahne
`m_OcclusionCullingData: {fileID: 0}` kaldı ve motor duvarın arkasını çizmeye
devam etti. Diskte dosya olduğu için "pişirilmiş" görünüyordu; hiçbir yerde
hata yazmıyordu.

Araç artık pişirmenin **bitmesini bekleyip** sahneyi kendisi kaydediyor
(`LightBakeWindow.StartOcclusionBake`). Hemen kaydetmek işe yaramıyor —
`GenerateInBackground` adı gibi arka planda çalışıyor, referans o an daha
yazılmamış oluyor. Bekleyici pencereye değil `EditorApplication`'a bağlı, yani
pencere kapatılsa da kayıt yapılıyor. Denetim raporu da artık occlusion
verisinin boyutunu yazıyor, "var mı yok mu" tahmin edilmiyor.

---

## 4. Ağ mimarisi

**His istemcide, karar sunucuda.**

Bu tek cümle bütün ağ kodunu belirliyor. Somut hâli:

| İstemcide | Sunucuda |
|---|---|
| Girdi, atılma, bıçak animasyonu, savurma sesi | Kimin elendiği |
| Kamera, kamera sallanması | Rol dağıtımı, terminal sayacı, tur sonucu, fener durumu |
| İz görselleştirme | İzin kime gösterileceği |
| Canavar animasyonu (hızdan çıkarılıyor) | Isabetin olup olmadığı |
| Kapı animasyonu (progress) | Kapının açık/kapalı olması |

Hareket **istemci otoriteli** (`NetworkTransform` → `ClientToServer`). Sunucu
otoriteli hareket + prediction, Source hissini kaybettirirdi.

Ama hareketin otoritesi kararların otoritesi değil. Bıçakta istemci sadece
`CmdSwing` ile "savurdum" der; menzil, koni ve görüş hattı kontrolünü sunucu
kendi gördüğü pozisyonlarla baştan yapar. Değiştirilmiş bir istemci menzili
büyütüp duvar arkasından vuramaz.

### Bilgi sızdırma

İzler yalnızca canavarın bağlantısına gider (`TargetRpc`). Herkese yollayıp
istemcide filtrelemek daha kolay olurdu ama o zaman koşan her kaçanın konumu
tüm istemcilere gitmiş olurdu — oyunun gizlilik mekaniğini değiştirilmiş bir
istemciye bedava vermek demek.

Aynı kural yeni özelliklerde de geçerli: **istemciye, görmesi gerekmeyen bilgiyi
gönderme.**

### Sahne nesneleri

Mirror, sunucu açılınca sahnedeki **bütün** `NetworkIdentity`'leri — kapalı
olanlar dahil — `SetActive(true)` yapıp spawn eder (`NetworkServer.SpawnObjects`).

Bu yüzden ağ öncesinden kalan kapalı objelere (`Player`, `Menu`, `TestKacanlar`)
**NetworkIdentity eklenmemeli** — eklenirse ağ testi için kapattığımız eski
oyuncu geri açılır ve kendi kamerasıyla çakışır.

Ağa taşınmış sahne nesneleri: `RoundManager`, `TrailMarkSystem`, kapı panelleri
(`Triggerable`), `TestBot`.

---

## 5. Tasarım kuralları

**Soyutlama kuralı — spekülatif değil, planlı.**
`IMovementInputSource` var çünkü canavar oyuncusu oyundan koptuğunda AI aynı
karakteri devralacak. Soyutlama ancak somut bir ikinci kullanım varsa eklenir.

**Tur verisi tek yerde.**
Rol, canlılık, kurtulma, faz, terminal sayaçları — hepsi `RoundManager` ve
`RoundParticipant`'ta,
`SyncVar` olarak. `PlayerProfile` sadece bir cihaz tercihi (oyuncu adı) tutar,
oyun durumu tutmaz.

**Sunucudaki tek yönetici.**
`RoundManager.Instance` ve `TrailMarkSystem.Instance` sahne nesnesi oldukları
için Mirror onları spawn edip aktifleştirene kadar null kalır. Çağıranlar null
kontrolü yapmak zorunda.

**Hız/gizlilik takası üç ayaklı.**
Ses (koşarsan duyulursun), iz (koşarsan yerde iz bırakırsın, sadece canavar
görür), fener (açarsan görürsün ama görünürsün). Üçü birlikte çalışıyor; birini
bozan değişiklik diğer ikisini de anlamsızlaştırır.

**Fener KAÇANIN aracı** (2026-09-04). Canavarda fener yok; etrafında sönmeyen
kırmızı bir hâle var (`MonsterAura`). Takas canavarda zaten yoktu — gizlenmesi
gereken o değil. Hâle iki işi birden yapıyor: canavar önünü görüyor, kaçan da
köşeyi dönmeden kırmızının yaklaştığını fark ediyor.

Hâlenin **gölgesi açık**, bilerek: gölgesiz nokta ışık duvar tanımaz ve kırmızı
yan koridora sızsaydı canavarın yeri duvarın arkasından belli olurdu. Bu,
bölüm 4'teki "istemciye görmesi gerekmeyen bilgiyi gönderme" kuralının görsel
karşılığı — izlerin yalnızca canavara gönderilmesiyle aynı gerekçe.

**Işık ikiye ayrıldı** (2026-09-05). Yalnızca hâle varken canavar önünü
yeterince göremiyordu:

| Işık | Nerede | İşi |
|---|---|---|
| **Hâle** (nokta, 10 m) | Gövdede | Çevresini gösteriyor: yandaki duvar, ayağının dibi |
| **Huzme** (spot, 13 m) | Kamerada | Baktığı yeri gösteriyor — fener gibi, bakışı takip ediyor |

Huzme fenerden bilerek **kısa ve sönük** (fener 26 m / 2.6): canavar avlanan
değil avlayan, koridorun sonunu görmesi kovalamacayı bitirir.

**Huzme kapatılamıyor.** Fener kaçanın takası; canavarda o takasın karşılığı
yok. Kapatılabilir olsaydı canavar hem görünmez hem gören olurdu ve kaçanın tek
erken uyarısı — kırmızının yaklaşması — ortadan kalkardı.

> **Düzeltme (2026-08-31).** Fenerin üçüncü ayağı uzun süre **hiç çalışmıyordu.**
> `Flashlight` düz bir `MonoBehaviour`'dı, durumu ağda taşınmıyordu ve `Update`
> yerel oyuncu kontrolü yapmadan klavyeyi okuyordu: F'ye basınca o istemcideki
> BÜTÜN oyuncu nesnelerinin feneri açılıyordu. Yani "açarsan görünürsün" kuralı
> işlemiyor, herkes birbirinin fenerini kendi tuşuyla açıp kapatıyordu.
>
> Artık `NetworkBehaviour`: girdiyi yalnızca sahibi okuyor, kararı sunucu
> yazıyor, sonucu SyncVar taşıyor.

**Tek vuruşta ölüm.**
Yakalanan anında elenir — yerde sürünme, kaldırılma yok. Bilinçli karar.
Elenen oyuncu izleyici moduna geçer ve **canavarı asla izleyemez** (sesli
konuşulan bir oyunda bu doğrudan hile olurdu).

**Duvarın içini görmek: İKİ ayrı sebep vardı** (2026-09-05). Biri kırpma
düzlemi, öbürü kameranın yeri — ve **ilk düzeltme yalnızca birincisini
çözdüğü için sorun devam etti.**

**Sebep 1 — kırpma düzlemi bir nokta değil, dikdörtgen.** Unity'nin varsayılanı
0.3; 60° görüş açısı ve 16:9'da köşesi kameradan `0.3 × 1.55 ≈ 0.46 m` uzakta.
Duvar ise en fazla `yarıçap − skinWidth = 0.3048 − 0.0305 ≈ 0.274 m`
yaklaşıyor, yani köşe duvarı deliyordu.

**0.08** yapıldı (`NetworkPlayerSetup.FirstPersonNearClip`): köşe 21:9'da bile
0.142 m. İlk denemedeki 0.15 hesabı **tam sınırdaydı** — köşe 16:9'da 0.232 m,
`CameraBob` kamerayı 0.032 m yana kaydırınca kalan pay 10 mm ve ultra geniş
ekranda hiç pay kalmıyordu.

**Sebep 2 — kamera eksende durmuyor.** Eğilirken 0.25 m öne kayıyor
(`duckedCameraForward`, bölüm 1) ve yakalama kilidinde 0.7 m geriye
(`killCameraPullBack`, bölüm 14). Duvara yaslanıp çömelen oyuncunun kamerası
duvara **0.024 m** kalıyor; o mesafede hiçbir kırpma değeri iş görmez. Asıl
sebep buydu ve ilk düzeltme ona hiç dokunmuyordu.

`PlayerController.UpdateCameraClearance` payı artık geometriye çarptırıyor:
kameranın paysız konumundan istenen yöne bir küre atılıyor, bir şeye çarparsa
pay oraya kadar kısalıyor. Küre yarıçapı (0.18) kırpma köşesinden büyük
seçildiği için kamera yüzeye hep o kadar uzak kalıyor.

> **Ders: bir düzeltme sorunu bitirmediyse, çözdüğü şeyi geri alma — ikinci
> sebebi ara.** Kırpma düzlemi gerçekten bozuktu ve düzeltilmesi doğruydu;
> yalnızca tek başına yetmiyordu. Sayıyı büyütüp küçülterek aramak burada
> sonuçsuz kalırdı, çünkü aranan şey sayı değildi.
>
> Kendi çarpışma kutumuz ışından eleniyor. `groundMask` daraltılmadı (bölüm
> 16'daki bilinçli tercih), o yüzden filtre maskeyle değil objeyle yapılıyor.
>
> Kırpmayı sıfıra yaklaştırmak bedava değil: kendi gövdeni birinci şahısta
> görüyorsun (bölüm 14) ve gizlenen kafa/boynun çevresinde kalan gerdirilmiş
> üçgenler kameraya yakın duruyor.
>
> Değer **çalışma anında** yazılıyor, çünkü kamera prefabta serileştirilmiş ve
> yalnızca kodu değiştirmek eski prefaba ulaşmazdı. `Ağ Kurulumu` da aynı sabiti
> yazıyor — Inspector'daki sayı dürüst kalsın diye.

**Karanlık oynanışın parçası.**
Ortam ışığı **0.006**, sis yoğunluğu 0.045 (görüş ~25 m), 14 loş lamba ve
aralarında zifiri bölgeler. Sis süs değil: 48 metreye uzayabilen koridorlarda
görüş kısıtlanmazsa labirent labirent olmaktan çıkar.

**Ölçüt tek cümle: fenersiz görülmemeli.** Fenerin "açarsan görürsün ama
görünürsün" takası ancak fenersiz GÖRÜLMÜYORSA bir takas. 2026-09-05'te
oynandığında lambasız koridorda fenersiz yürünebiliyordu; taban aydınlığı dört
ayrı yerden besleniyordu ve hepsi birden indirildi:

| Kaynak | Eski | Yeni | Neden |
|---|---|---|---|
| `RenderSettings.ambientLight` | 0.018 | **0.006** | Doğrudan taban aydınlık |
| `RenderSettings.fogColor` | 0.02 | **0.008** | Sis rengi de bir taban: yoğunluk arttıkça yüzeyler ona yaklaşıyor, ortamdan parlaksa uzak duvarlar ışıksız yerde bile görünür kalıyor |
| `RenderSettings.reflectionIntensity` | 1 | **0.2** | Yansıma varsayılan gökyüzünden pişiyor; kapalı labirentte karşılığı yok |
| `indirectScale` / `albedoBoost` | 2 / 1.6 | **1 / 1** | Aşağıdaki kutu |

> **Son ikisi bir hatanın kalıntısıydı.** 2026-09-04'te "pişmiş ışık çok sönük"
> diye yanlış teşhis kovalanırken köşeleri doldurmak için kondu. Teşhis yanlış
> çıktı (sorun lamba şiddetiydi, bölüm 3) ama çarpanlar kaldı ve haritayı
> fenersiz yürünebilir hâle getirdi.
>
> Ders: **bir teşhis çürüdüğünde, o teşhis için yapılan değişiklikleri de geri
> al.** Yoksa sebebi unutulmuş ayarlar birikiyor ve sonraki sorunun kaynağı
> oluyor.

---

## 6. Klasör yapısı

```
Assets/
  _Art/          materyaller, modeller
  _Audio/        ses klipleri
  _Prefabs/      NetworkPlayer.prefab, _Eski/ (ağ öncesi yedekler)
  _Scenes/       SampleScene.unity
  _ScriptableObjects/  MovementProfile varlıkları
  _Scripts/
    Core/        tur sistemi, bıçak, izler, izleyici, bot
    Editor/      kurulum araçları (aşağıdaki menü)
    Interaction/ IInteractable, Triggerable, kapı, düğme
    Network/     NetworkPlayerSetup
    Player/      hareket, girdi, fener, ses, duruş senkronu
    UI/          menü, lobi, profil
  Mirror/                ağ kütüphanesi (yamalı — bölüm 9)
  SciFi Warehouse Kit/   harita modelleri
```

Alt çizgiyle başlayan klasörler bize ait; diğerleri dışarıdan gelen paketler.

---

## 7. Editör araçları

Her şey `Yakalamaca` menüsünden kuruluyor. Elle sahne düzenlemek yerine araç
yazma alışkanlığı, haritayı istediğin zaman sıfırdan üretebilmeni sağlıyor.

| Menü | Ne yapar |
|---|---|
| Labirent Harita Kur | Labirenti üretir, flood fill ile bağlantıyı doğrular |
| Atmosfer Kur | Tavan, lambalar, sis, ortam ışığı |
| Haritayı Giydir (SciFi Kit) | Küplerin üstünü kit modelleriyle kaplar |
| Harita Süsle (prop dağıt) | Duvar diplerine varil/kasa dağıtır |
| Ağ Kurulumu (1. adım) | Oyuncu prefabı + NetworkManager + doğum noktaları |
| EOS Kurulumu (relay) | EOS transport'unu ve lobi servisini kurar, lobiye bağlar; hiçbir şey silmiyor |
| Menü Kur | Menü, lobi, ayarlar ve tuş atama ekranları (bkz. bölüm 13) |
| Terminal ve Çıkış Kur | 5 terminali duvarlara, 2 çıkışı en uzak iki gediğe kurar |
| Sesleri Yerleştir | Sesleri adlandırır, mono yapar, kapılara ve terminallere bağlar |
| Mağara Yankısı Kur (reverb) | Yankı bölgesi + mesafeye bağlı yankı eğrisi (bkz. bölüm 12) |
| Canavar Modelini Kur | Model + animasyonlar + animator + prefaba bağlama (bkz. bölüm 14) |
| Kaçan Modelini Kur | Banana Man + animasyonlar + animator + prefaba bağlama (bkz. bölüm 17) |
| Hareket Profillerini Sıfırla | Kaçan = Source, canavar = araba modeli (bkz. bölüm 1) |
| Katmanları Kur | Dört katman tanımlar, sahneye ve prefaba atar, maskeleri daraltır (bkz. bölüm 16) |
| Işığı Pişir (lightmap) | Lightmap UV'si üretir, ışıkları Baked yapar, probe kurar, pişirir |
| Ceset Sistemini Kur | Ceset prefabı (ragdoll) + NetworkManager ve RoundManager bağlantısı (bkz. bölüm 21) |
| Test Botu Ekle/Kaldır | Tek başına test için sahte kaçan — kaçan modeli ve animasyonlarıyla |
| Test Botu Ekle (ölü) | Tur başında elenen ikinci bot: taşıma/diriltme testi için hazır ceset (bkz. bölüm 23) |
| Diriltme Sistemini Kur | Ceset gövde prefabı + haritanın iki ucuna diriltme kabini (bkz. bölüm 23) |
| Hataları Temizle (Sahne Onarımı) | Eksik NetworkIdentity ekler, ağ öncesi artıkları söker |

> ### Editörde çalışan her API build'de yok
>
> `Light.lightmapBakeType` **yalnızca editörde var** — pişirme ayarı olduğu için
> Unity onu build'e koymuyor. Çalışma anında ışık kuran üç yerde kullanılmıştı
> ve **hata ancak build alınırken çıktı**; editörde her şey yolunda görünüyordu.
>
> Üçü de `#if UNITY_EDITOR` içine alındı. Silmek yerine korumaya almanın sebebi:
> Inspector'da modun ne olduğu belli olsun. Build'de davranış değişmiyor, çünkü
> çalışma anında eklenen bir ışık zaten pişirilemez ve Unity'nin varsayılanı da
> Realtime.
>
> **Ders: yeni bir Unity API'si kullanmadan önce build'de var mı diye düşün.**
> Aynı sınıf hatalar `Renderer.scaleInLightmap`, `receiveGI`, `shadowRadius` ve
> `UnityEditor` altındaki her şey için geçerli. `MenuController.QuitGame` bunu
> baştan doğru yapıyor (`#if UNITY_EDITOR` / `#else Application.Quit()`).
>
> Tarama komutu — build almadan önce çalıştırılabilir:
> ```
> grep -rn "UnityEditor\." --include=*.cs Assets/_Scripts/ | grep -v "/Editor/"
> ```

**Kurulum araçları sahneyi kendileri kaydeder.** Etmezlerse Unity kapanınca
kurulum geri gider — bu tuzağa bir kez düşüldü, saatler kaybedildi. Aynı
sebeple hepsi Play modunda gri: o sırada yapılan sahne değişiklikleri Play
bitince geri alınır.

### Sıfırdan kurulum sırası

Yeni bir sahnede ya da her şey bozulduğunda bu sırayla:

```
0. Katmanları Kur                 → ÖNCE: katmanlar tanımlı olmalı ki sonraki
                                    araçlar ürettiklerine katman atayabilsin
1. Labirent Harita Kur
2. Atmosfer Kur (tavan + ışık)
3. Haritayı Giydir (SciFi Kit)
4. Harita Süsle (prop dağıt)      → "SciFi Kit prop'larını yükle" → Dağıt
5. Ağ Kurulumu (1. adım)
5a. EOS Kurulumu (relay)          → ağ kurulumundan SONRA (NetworkManager'a ekliyor)
5b. Menü Kur                      → EOS kurulumundan SONRA (bkz. bölüm 13)
6. Terminal ve Çıkış Kur
7. Sesleri Yerleştir
7b. Canavar Modelini Kur          → ağ kurulumundan SONRA (prefabı değiştiriyor)
7c. Kaçan Modelini Kur            → ağ kurulumundan SONRA (prefabı değiştiriyor)
7c. Hareket Profillerini Sıfırla  → MovementProfile'a yeni alan eklendiyse ŞART
8. Mağara Yankısı Kur (reverb)
9. Işığı Pişir (lightmap)         → "0-4'ü yap ve PİŞİR", sonra occlusion
10. Test Botu Ekle (isteğe bağlı)
11. Katmanları Kur (tekrar)       → kurulum sırasında elle eklenen varsa
```

Işık pişirme **en sona** kalmalı: haritaya sonradan eklenen her static parça
lightmap'i geçersiz kılar. Süsleme veya terminal taşırsan yeniden pişir.

Sonra **File > Build Settings > Build** ile bir build al; test için biri
Editor'de Host, öbürü build'de Client olacak.

### Test tuşları (sunucu penceresinde)

Lobi geldi ama bunlar duruyor: tek başına test ederken lobi kurmadan hızlıca
tur başlatmak işe yarıyor. Özellikle [2] lobiden yapılamıyor — canavarı
seçebiliyorsun ama "beni canavar YAPMA" diyemiyorsun.

| Tuş | Ne yapar |
|---|---|
| **1** | Turu başlatır (canavar gerçek oyunculardan seçilir) |
| **2** | Turu, sen kaçan olacak şekilde başlatır — tek başına test için |
| **3** | Kendini elendirir (izleyici modunu denemek için) |
| **4** | Test botlarını önüne ışınlar |

`minimumPlayers = 2`. Tek başına test ederken sahnedeki `TestBot` ikinci
katılımcı sayılıyor.

**Bot rastgele seçimde canavar adayı değil** — kovalayamayan bir canavar turu
sürüncemede bırakır. Ama lobide **elle seçilebiliyor**: kaçan olarak oynayıp
terminalleri ve kaçışı tek başına test etmenin yolu bu. Fark, bunun bilinçli
bir tercih olması.

---

## 8. Teknik borç

1. **Bıçak sesleri yer tutucu.** `Bicak_Savurma.wav` ve `Bicak_Isabet.wav`
   sentetik. Diğer sesler gerçek dosyalarla değiştirildi. Artık bıçak da yok
   (canavar elle saldırıyor), yani sesler saldırıya göre yeniden seçilmeli.
2. ~~**Prototip arayüz.**~~ **ÇÖZÜLDÜ** (2026-09-06) — oyun içi arayüzün
   tamamı TextMeshPro + Canvas'a taşındı, çalışma anında `OnGUI` kalmadı
   (bölüm 20). Madde numarası, koddaki atıflar bozulmasın diye yerinde
   bırakıldı.
3. ~~**Katman düzeni yok.**~~ **ÇÖZÜLDÜ** (2026-08-30) — dört katman kuruldu ve
   maskeler daraltıldı. Bkz. bölüm 16. Madde numarası, koddaki atıflar bozulmasın
   diye yerinde bırakıldı.
4. ~~**Kaçan kapsül, animasyon yok.**~~ **ÇÖZÜLDÜ** (2026-08-30) — Banana Man
   modeli, locomotion/eğilme/havada animasyonları ve yakalanma animasyonu
   bağlandı (bölüm 17). Kapsül yer tutucu olarak duruyor: model takılı değilse
   ona düşülüyor. Madde numarası, koddaki atıflar bozulmasın diye yerinde
   bırakıldı.
5. ~~**Oda listesi yok.**~~ **ÇÖZÜLDÜ** (2026-09-05) — EOS'un lobi servisi
   geldiğinde liste de bedava geldi: kaydı Epic tutuyor, bizim ayakta
   tutacağımız bir eşleştirme sunucusu yok. Katılma ekranı açık odaları
   listeliyor (bölüm 13). Madde numarası, koddaki atıflar bozulmasın diye
   yerinde bırakıldı.
6. ~~**Kaçış kapısı tek.**~~ **ÇÖZÜLDÜ** (2026-08-30) — iki çıkış var, birbirinden
   en uzak iki dış duvar gediğinde (bölüm 11.5). Madde numarası, koddaki atıflar
   bozulmasın diye yerinde bırakıldı.
7. ~~**Aynı kapının iki düğmesi birden basılmış görünüyor.**~~ **ÇÖZÜLDÜ**
   (2026-08-31) — hangi düğmenin bastığı artık ağdan taşınıyor (bölüm 15).
   "Bilinçli bırakıldı" diyordu; oynandığında ilk göze batan şeylerden biri
   oldu. Madde numarası, koddaki atıflar bozulmasın diye yerinde bırakıldı.

---

## 9. Yerel yamalar

`Assets/Mirror/` üçüncü parti klasör ama **bir satır değiştirildi**:

`Components/NetworkTransform/NetworkTransformBase.cs` → `Reset()` metodunun
başına `Configure()` eklendi.

Sebep: `Reset()` editörde AddComponent anında çalışıyor, `Awake()` henüz
çalışmadığı için `target` null oluyor ve `ResetState()` NullReference atıyor.
İstisna `Reset()`'i yarıda kestiği için altındaki `syncInterval = 0.05f`
satırı hiç çalışmıyor, transform istenenden ~3 kat sık gidiyordu.

**Mirror güncellenirse bu yama kaybolur.** Pratik sonucu ayrıca
`NetworkSetup.BuildPlayerPrefab` içinde `syncInterval` açıkça yazılarak da
güvenceye alındı.

### EpicOnlineTransport (2026-09-05)

EOS transport'u `Assets/Plugins/Mirror/Runtime/Transport/EpicOnlineTransport`
altında duruyor. **Üçüncü parti ve bir satırı değiştirildi:**

`Server.cs` → `CreateServer` içinde
`transport.OnServerError.Invoke(id, exception)` iki parametreliydi; Mirror'ın
hata olayı artık **üç** parametre istiyor `(int, TransportError, string)`.
`TransportError.Unexpected` + `exception.Message` veriliyor.

**Bu paket Mirror v44 dönemine ait** (son commit 3 yıl önce, son sürüm 5 yıl
önce) ve biz 96.11.0'dayız. Buna rağmen tek uyumsuzluk bu çıktı: Mirror'ın
`Transport` sınıfındaki 14 abstract üyenin hepsi karşılanıyor ve
`NetworkServer` obsolete `OnServerConnected`'a **hâlâ abone** (satır 228), yani
eski çağrı çalışıyor.

**İki yama daha (2026-09-05):**

- `EOSSDKComponent.cs` → yerel kütüphane yolu **sabit yazılmıştı**
  (`"Assets/Mirror/Runtime/Transport/EpicOnlineTransport/EOSSDK/"`). Paket başka
  bir klasöre konunca DLL bulunamıyor ve `Awake` istisna atıyor. Artık dosya
  projede **adıyla aranıyor** (`FindEditorLibrary`), yani paketin yeri serbest.
  Yalnızca editörde gerekiyor; build'de DLL normal eklenti yolundan yükleniyor.
- `EosTransport.Shutdown()` → metrik bloğuna `EOSSDKComponent.Initialized`
  şartı eklendi. EOS açılamadığında `EOS` null kalıyor ve `GetMetricsInterface()`
  çıkışta `NullReferenceException` atıyordu: **asıl hatanın üstüne ikinci bir
  hata biniyor** ve sebebi görünmez oluyordu.

- `EOSSDKComponent.cs` → `LoadLibrary`'nin `DllImport`'una
  **`CharSet = CharSet.Unicode`** eklendi ve bulunan yol `Path.GetFullPath`
  ile normalleniyor. DLL bulunuyor ama yüklenemiyordu; iki ayrı sebep vardı
  ve ikisi de proje klasörünün adından geliyordu:

  - **`LoadLibrary` ileri eğik çizgiyi kabul etmiyor.** `Application.dataPath`
    `/` veriyor, `Directory.GetFiles` `\` veriyor; ortaya
    `C:/Users/.../Assets\Plugins\...` gibi karışık bir yol çıkıyordu.
  - **Varsayılan `DllImport` ANSI**, yani `LoadLibraryA` çağrılıyor ve yol
    sistem kod sayfasına çevriliyor. Proje yolu `Yeni klasör` — ASCII dışı
    bir karakter taşıyor ve dönüşüm bozulabiliyor. Unicode ile
    `LoadLibraryW` çağrılıyor, yol olduğu gibi gidiyor.

  `GetProcAddress`'e dokunulmadı: ikinci parametresi Windows'ta her zaman
  ANSI (`LPCSTR`), W sürümü yok.

  DLL'in kendisi elendi: 22 MB ve geçerli `MZ` başlığı taşıyor, yani ZIP
  indirmede Git LFS işaretçisi inmiş değil.

> **Proje yolu ASCII dışı karakter ve boşluk içeriyor**
> (`C:\Users\TR\Desktop\Yeni klasör`). Yerel eklenti yükleyen her
> kütüphane bu yüzden takılabilir. Benzer bir hata çıkarsa akla ilk gelmesi
> gereken şey bu; kalıcı çözüm proje klasörünü ASCII bir ada taşımak.

**Paket güncellenirse dört yama da kaybolur.** Aynı hataları tekrar verirse
çözüm bu satırlar.

**Konum bilinçli.** `Assets/Plugins` altındaki script'ler
`Assembly-CSharp-firstpass`'e giriyor ve o, `Assembly-CSharp`'tan **önce**
derleniyor. Sonuç bizim için doğru yönde: transport Mirror'ı görüyor, bizim
kodumuz transport'u görüyor, ama transport bizim kodumuzu göremiyor — zaten
görmesi de gerekmiyor. `Assets/Mirror`'ın içine karıştırılmadı: orası üçüncü
parti ve orada zaten başka bir yama var (yukarıdaki madde).

---

## 10. Sırada ne var

Terminal ve kaçış sistemi, çıkış kilidi ve harita düzenlemesi bitti. Sıradaki
işler, tavsiye edilen sıralamayla:

**1. ~~Haritayı elden geçirmek.~~ YAPILDI (2026-09-03).** Harita elle düzenlendi
ve artık **dokunulmuyor** — bölüm 0'daki kural. O seansta yapılanlar:

- ~~Terminal sayısını 5-6'ya çıkar.~~ **YAPILDI (2026-08-30): 5 terminal.**
  Bu maddedeki uyarı geçerliliğini koruyor: dolu kadroda (4 kaçan) gereken
  4+1=5, yani beşi de zorunlu ve tur başında seçim kalmıyor. Bilinçle kabul
  edildi — gerekçe ve tablo bölüm 11.1'de. Seçim istenirse `TerminalCount` 6.
- ~~İkinci bir çıkış aç.~~ **YAPILDI (2026-08-30): 2 çıkış**, birbirinden en uzak
  iki dış duvar gediğinde (teknik borç 6).
- Lamba yerleşimini elle düzelt.
- ~~Çıkış kapıları tak diye açılmayacak.~~ **YAPILDI (2026-09-03).** Her çıkışın
  yanında bir kilit paneli var; on adımlık yön dizilimi doğru girilince kapı
  açılıyor (bölüm 11.5).

> **Uyarı:** `Labirent Harita Kur` ve `Atmosfer Kur` menüleri elle yaptığın
> her şeyi siler (birincisi `Harita`'nın tamamını, ikincisi `Lambalar`
> grubunu). Elle düzenlemeye başladıktan sonra o ikisine basma.

**2. ~~Lightmap + occlusion.~~ YAPILDI (2026-09-03).**

İkisi de bitti ve sahne dosyasından doğrulandı; ayrıntı ve yanlış çıkan eski
kayıtlar yukarıdaki "Şu an neredeyiz" bölümünde.

Haritaya sonradan static bir parça eklenirse ikisi de geçersiz olur ve yeniden
pişirmek gerekir: `Yakalamaca > Işığı Pişir (lightmap)` → "0-4'ü yap ve PİŞİR",
sonra aynı pencereden "Occlusion culling'i pişir". Lightmap'i geri almak için
aynı pencerede "Pişirmeyi sil, ışıkları gerçek zamanlıya döndür" var; UV'ler ve
probe'lar kalıyor.

**Doğrulaması göz kararı değil:** sahne dosyasında `m_OcclusionCullingData`
`{fileID: 0}` olmamalı ve ışıkların `m_Lightmapping` değeri 2 (Baked) olmalı.
Bir kez tam da bu satırlar yüzünden aylarca yanlış bilindi.

**3. ~~Yakalama ve ölme animasyonlarının göreli duruşu.~~ YAPILDI (2026-09-04),
oynanışta doğrulandı.**

Altyapı bitti (bölüm 17): beden ölüm klibi boyunca sahnede kalıyor, öldürenin
`netId`'si taşınıyor, gövde kökü canavarınkine oturtuluyor, süreler eşitlendi,
dikey kök hareketi poza gömüldü.

**Ama iki karakter hâlâ birbirine oturmuyor.** Denenenler ve sonuçları:

| Deneme | Sonuç |
|---|---|
| `deathForwardOffset` 0.85, kurban canavara dönük | Sırt sırta, uzak |
| Offset 0, aynı rotasyon | Üst üste ama yanlış yön |
| Offset 0, zıt rotasyon | Doğru yön, hâlâ tam oturmuyor |
| XZ + dönüş de poza gömüldü | Canavar ileri uçtu, daha kötü |
| Yalnızca dikey gömüldü | Havada yatma çözüldü, duruş açık kaldı |

**ÇÖZÜLDÜ — iki ayrı sebep vardı; düzeltildi ve oynanışta doğrulandı.**

Ekran görüntüsünde canavar kurbanın **bir buçuk metre arkasında** diz
çöküyordu. İki sebep bulundu; ikisi de düzeltildi.

### Sebep 1: klibin yatay referansı "Original"

İki klip de `keepOriginalPositionXZ: 1` ile import ediliyordu — yani "Based
Upon: Original". Bu, klipte yazılı **özgün dünya offsetini pozun içinde
tutuyor.** Mixamo'nun eşli takedown'ında iki karakter sahnenin ayrı
noktalarında yazılmış, o yüzden `ApplyDeathPose` ikisini aynı köke oturtsa
bile aralarında o mesafe kalıyordu. Resimdeki 1.5 m tam olarak buydu.

`ClipRootMotion` artık ağırlık merkezine geçiriyor: her klip kendi kökünde
ortalanıyor ve ikisi iç içe geçiyor.

> **Bu, eski teşhisi çürüttü.** Belge "klipler gerçek bir Mixamo çifti değil,
> ayrı ayrı indirilmiş, ortak origin'i yok" diyordu. Meta dosyaları tersini
> söylüyor: ikisi de `KillerDollUnity_BaseBody` rig'inde, ikisi de **78 kare**,
> import ayarları birebir aynı — eşleşen bir çift. Yeni klip indirmeye gerek
> yoktu.
>
> Dönüşe dokunulmadı: yukarıdaki tabloda "zıt rotasyon → **doğru yön**, hâlâ
> tam oturmuyor" yazıyor, yani yön zaten çözülmüştü. Bozuk olan mesafeydi.

### Sebep 2: iki karakter aynı ölçekte değil

Prefabtan okunan gerçek değerler:

| | Gövde kökü yerel konumu | Yerel ölçek | Ekranda boy |
|---|---|---|---|
| Canavar (KillerDoll) | `(0, -0.6858, 0)` | 0.7048 | **1.619 m** (bugün 1.784) |
| Kaçan (Banana Man) | `(0, -0.6858, 0)` | 0.8630 | **1.372 m** |

İki gövde kökü de hull'un tabanında, aynı yerel konumda — `ApplyDeathPose` de
kurbanı canavarın kökünün XZ'sine oturtuyor, yani **hizalama hatası yok.**
Farklı olan tek şey ölçek: canavarda hull boyunun üstüne bölüm 17'deki **1.18**
çarpanı var, kaçanda 1.

`localScale` altındaki her şeyi dünya uzayında ölçekliyor — animasyonun bütün
kemik hareketleri dahil. Yani canavarın yakalama koreografisi kurbanınkinden
**%18 daha büyük** oynuyor: elleri kurbanın gövdesinin olmadığı yere iniyor.

**Bunun sonucu: Mixamo'dan eşleşen bir çift indirmek tek başına ÇÖZMEZ.** Eşli
bir set bile %18 ölçek farkıyla iç içe geçmez. Yukarıdaki tabloda beş deneme
başarısız olduysa sebebi buydu — hepsi konumu ve dönüşü kurcaladı, ölçeğe hiç
dokunmadı.

**Seçilen yol:** kurbanın gövdesi ölüm klibi boyunca canavarın ölçeğine
çıkıyor (`PlayerBodyVisual.deathScaleMatch`), `ClearDeathPose` geri alıyor.
Canavarın "olduğundan büyük görünmesi" etkisi kovalamacada korunuyor; kurban
yalnızca 2.6 saniye boyunca %18 büyüyor ve o sırada zaten yerde yatıyor.

Diğer yol canavarın 1.18'ini büsbütün kaldırmaktı; bölüm 17'deki bilinçli
tasarım kararını geri alacağı için seçilmedi.

**Çarpanı `Kaçan Modelini Kur` yazıyor**, iki aracın `ExtraScale` sabitlerinin
oranından. Elle değiştirilirse bir sonraki kurulum geri alır — sayı iki yere
elle yazılmıyor.

### Kilit sırasında kamera artık sabit

Yakalama kilidinde kamera geriye çekilirken "hızlı gir, tut, yavaş çık"
kayması vardı. Yakalama animasyonu zaten hareketli olduğu için kameranın da
kayması görüntüyü okunmaz yapıyordu. Rampalar sıfırlandı: kamera anında
yerine oturuyor ve kilit boyunca kıpırdamıyor — ortaya sabit bir omuz üstü
çekim çıkıyor.

Rampalar `MonsterAttack`'te alan olarak duruyor (`killCameraRampIn`,
`killCameraRampOut`); yumuşak geçiş istenirse büyütmek yetiyor.

### Bunu denemek için

Klip import ayarı ve ölçek çarpanı **kurulum araçlarından** yazılıyor, yani
kodu değiştirmek tek başına yetmiyor. Sırayla:

1. `Yakalamaca > Canavar Modelini Kur` (yakalama klibi)
2. `Yakalamaca > Kaçan Modelini Kur` (ölüm klibi + ölçek çarpanı)

Bu sırayla, çünkü kaçanın ölüm klibi canavarın `kill` klibiyle aynı süreye
kırpılıyor (bölüm 17). Kamera değişikliği için araç çalıştırmak gerekmiyor,
yeni alanların C# varsayılanı zaten istenen değer.

### Kalan büyük işler

**4. ~~Kaçan modeli.~~ YAPILDI (2026-08-30).** Banana Man bağlandı (bölüm 17).
`Idle` klibi kaçanın kendisine ait değil, canavarınkinden ödünç alınıyor —
humanoid klipler avatardan bağımsız olduğu için sorunsuz oynuyor ve **böyle
kalması kabul edildi** (2026-09-03). Kaçan klasörüne bir Idle klibi atılırsa araç
kendiliğinden ona geçer.

**5. Yakınlık sesi (kalp atışı).** Canavar yaklaştıkça yükselen kalp atışı.
Karanlığı "göremiyorum"dan "geliyor ama nereden"e çeviriyor. **Ses dosyası
oyuncudan gelecek — sentezlenmeyecek**, denendi ve beğenilmedi. Dosya
`Assets/_Audio/KalpAtisi.*` olarak konulacak.

Kritik tasarım notu: bu ses **2B olmalı**, 3B değil. Yönü belli olursa
"geliyor ama nereden" gerilimi kaybolur ve radar hâline gelir. Bölüm 12'nin
kuralı gereği 2B kaynakların `reverbZoneMix`'i de sıfırlanıyor.

**6. ~~Sesli sohbet.~~ YAZILDI (2026-09-06), bölüm 19.**

Karar **kendimiz yazmak** oldu: Dissonance ücretli, Vivox 3B karışımı sunucuda
yaptığı için mağara yankısıyla çelişiyordu. Yeni paket eklenmedi — µ-law otuz
satır (bölüm 0'ın bağımlılık kuralı korundu).

Buradaki iki öngörü de tuttu: konuşma oyuncunun üstündeki 3B kaynaktan çaldığı
için **mağara yankısı bedavaya geldi**, kişi başı seviye `AudioSource.volume`'dan
geldiği için **AudioMixer gerekmedi** (ki script'ten kurulamıyor).

Menü tarafı da bitti: ayrı bir SES ekranı, mikrofon seçimi, bas-konuş/otomatik,
eşik, kazanç ve TAB panelinde kişi bazlı susturma/seviye.

**Kalan tek şey doğrulama:** iki makineyle denenmedi, çünkü kendi sesimizi
kendimize göndermiyoruz.

**7. Denge ölçümü.** Canavar hızı, hız payı, terminal süreleri, kilit süreleri
— hepsi tahmin. Arkadaşlarla oynanarak ölçülmeli. Ölçerken bilinmesi gereken:
`_ScriptableObjects` altındaki hareket profilleri **Play modunda değiştirilince
kalıcı** (ScriptableObject), ama terminal ve kapı ayarları sahne nesnesinde
olduğu için Play bitince geri gider.

### İnternet üzerinden oynatmak

Şu anki bağlantı **doğrudan**: lobi kodu sunucunun IPv4 adresi (bölüm 13).
Aynı ağda çalışıyor; internette port yönlendirme ya da sanal ağ (Hamachi,
Radmin) gerekiyor. İnsanların çoğu modem arkasında ve Türkiye'de CGNAT yaygın,
yani itch.io'dan indiren biri şu hâliyle arkadaşıyla oynayamaz.

Çözüm bir **relay**: iki taraf da dışarı bağlanır, trafik ortadaki sunucudan
geçer. "Host'a bağlanmak" ile relay zıt şeyler değil — kararları yine host
veriyor, relay yalnızca paketleri taşıyor.

Seçenekler:

| Yol | Para | Oyuncudan istenen | Bağımlılık riski |
|---|---|---|---|
| **EOS** (Epic Online Services) — **seçildi** | Bedava | Hiçbir şey; Epic hesabı bile gerekmiyor | Epic'in kapanma ihtimali yok denecek kadar az |
| **LRM** (Light Reflective Mirror) | ~5$/ay VPS | Hiçbir şey | Yok — sunucu senin, kaynak açık |
| Steam (FizzySteamworks) | 100$ giriş | Steam hesabı + oyun Steam'de | Yok, ama itch.io planıyla uyuşmuyor |
| NAT punchthrough (kendimiz) | Bedava | Hiçbir şey | %70-85, CGNAT'ta çalışmıyor |

> ### Edgegap denendi ve BIRAKILDI (2026-09-05)
>
> Mirror kutuda `Transports/Edgegap` + `Examples/EdgegapLobby` getiriyordu ve
> yeni bağımlılık gerektirmediği için ilk tercih oydu. Entegrasyon yazıldı ve
> çalıştı; takılan yer **Edgegap'in kendi servisi** oldu.
>
> Ücretsiz katmanda lobi servisi bir türlü dağıtılamadı: `POST /v1/lobbies`
> başarılı oluyor, ama `GET /v1/lobbies/{name}` sonsuza kadar `status: Error`
> ve boş `url` döndürüyordu. Relay ağı "operational" görünüyordu, panelde
> `Deployments` boştu, Unity konsolunda hata yoktu. Servis adı 4-5 karaktere
> düşürülerek (bilinen 503 hatası) ve terminate/retry ile denendi, değişmedi.
> Destek kanalından da dönüş olmadı.
>
> **Kod tarafında bir sorun yoktu** — bu yüzden entegrasyon `856735a..eef1ee3`
> aralığında git'te duruyor, gerekirse geri alınabilir. Bırakılma sebebi
> teknik değil: çalıştırılamayan bir servis kullanılamaz.
>
> Edgegap'e ait her şey projeden **tamamen silindi**: `Transports/Edgegap`,
> `Examples/EdgegapLobby` ve hiç kullanılmayan `Hosting/Edgegap` (sunucu
> kiralama sihirbazı).

**Seçilen yol: EOS.** Bedava, kalıcı, oyuncuya sıfır sürtünme. Bedeli Epic
SDK'sını projeye sokmak — bölüm 0'ın "bağımlılık eklemeden önce iki kez düşün"
kuralına takılıyor, ama karşılığında sunucu bakımı gerektirmeyen kalıcı bir
çözüm geliyor. Mirror transport'u topluluk tarafından yazılmış
(`FakeByte/EpicOnlineTransport`), resmi değil.

**Durum (2026-09-05): ÇALIŞIYOR, iki makinede doğrulandı.** Lobi kurulduğunda
ekran "İnternet odası" diyor ve kod **6 harflik** kısa kod olarak geliyor — yani
SDK açıldı, kimlik alındı, oda relay üzerinden kuruldu ve lobi servisi kodu
yazdı. Port yönlendirmesi ya da sanal ağ gerekmiyor.

Kodun **uzunluğu nerede olduğunu söylüyor** ve hata ayıklarken ilk bakılacak
yer o:

| Kod | Ne demek |
|---|---|
| **6 harf** | Her şey çalışıyor: relay + lobi servisi |
| **32 harf** | Relay çalışıyor, lobi servisi cevap vermedi — oda oynanabilir, kod uzun |
| **7 harf** | EOS hiç açılmadı, yerel odaya düşüldü |

Buraya gelene kadar dört ayrı engel vardı ve hepsi bölüm 9'da:
Mirror'ın hata olayının imzası · SDK kütüphanesinin koda gömülü yolu ·
`LoadLibrary`'nin ileri eğik çizgiyi kabul etmemesi ve ANSI dönüşümü ·
EOS girişinin asenkron olması.

**Kurulum:** paket
`Assets/Plugins/Mirror/Runtime/Transport/EpicOnlineTransport` altında (dört
yerel yama, bölüm 9), Epic portalında ürün ve istemci açık, `EosApiKey`
dolduruldu, `Yakalamaca > EOS Kurulumu (relay)` bileşenleri kurup lobiye
bağlıyor. Lobi iki transport arasında seçim yapıyor (bölüm 13).

**İkisi de bitti** (2026-09-05):

1. ~~**Oynanışta doğrulama.**~~ İki makineden bağlanıldı, tur oynandı. (EOS
   açılmazsa ilk bakılacak yer Epic'teki istemci politikası: **P2P izni yoksa
   SDK başlamıyor.**)
2. ~~**Kısa kod.**~~ EOS'un lobi servisi devrede (`RelayLobby`); kod 6 harf ve
   oda listesi de aynı yerden geliyor (bölüm 13).

**Yerel yol duruyor ve hiçbir şeyi EOS'a borçlu değil.** EOS açılmazsa oyun
yerel odaya düşüyor; aynı ağda çalışıyor, internette sanal ağ (Radmin, Hamachi)
gerekiyor. Lobi ekranı o hâlde host olurken makinenin **bütün IPv4 adreslerini**
yazıyor (`LobbyCode.LocalAddresses`), çünkü kod internete çıkan adaptörden
üretiliyor ve sanal ağ adresi orada görünmüyor — oyuncu doğrusunu listeden
tanıyıp veriyor.

**Bugünkü lobinin neredeyse tamamı korunuyor:** kadro senkronu, hazır işareti,
canavar seçimi, tur akışı, yetki kontrolleri — hiçbiri baytların nasıl
taşındığını bilmiyor. Değişecek olan yalnızca `LobbyNetwork`'ün bağlanma kısmı
ve `LobbyCode` (kod artık IP olmaz, lobi adı olur).

Ayrıca `Assets/Mirror/Components/Discovery` projede duruyor: aynı ağdaki
oyuncular kod yazmadan birbirinin odasını listede görebilir. Bağımlılık yok,
hesap yok, internet gerekmiyor.

---

## 11. Terminal ve kaçış sistemi

Kaçanların yapacak bir işi yoktu: tur "5 dakika kaç, süre dolsun, kazan"
şeklindeydi. Bhop yapan kaçan yakalanamıyor, kazanmak pasif, canavarın
karşılaşmayı zorlayacak aracı yok. Bu bölüm o açığı kapatan tasarım.

**Bu bölüm bir spesifikasyon, uygulanma sırası aşağıda.** Maddeleri
değiştirirken kodu da güncelle.

### 11.1 Tur kuralları — süre kaldırıldı

- **Tur süresi yok.** Eskiden 5 dakika vardı; kaldırıldı. Oyun **herkes ölene
  ya da kaçana kadar** sürüyor.
- **Kaçanlar kazanır:** haritada canlı kaçan kalmayıp en az biri kaçmışsa.
- **Canavar kazanır:** kimse kaçamadan hepsi elenirse.
- **Canavar oyundan ayrılırsa tur anında biter.** Kovalayanı olmayan turda
  terminal doldurmak başarı değil.
- Gereken terminal sayısı tur başında **hayattaki kaçan + 1** olarak belirlenir
  (haritadaki toplam terminal sayısıyla sınırlı). İki kişilik bir turda tek
  kaçanın dört terminali doldurması imkânsıza yakındı; bu hem ölçekleniyor hem
  de hangi terminali yapacağın seçimini bırakıyor.
- Bir kaçan **öldüğünde gereken sayı 1 azalır** (her seferinde).
  Kartopunu bu dengeliyor: ölüm, kalanlara iş yükü bindirmiyor.
  Kaçan **kaçtığında** sayı azalmaz — sadece ölümde.

**Hedeflenen kadro 5 oyuncu: 1 canavar + 4 kaçan.** Gereken sayının dolu
kadrodaki seyri:

| Durum | Hayattaki kaçan | Gereken terminal |
|---|---|---|
| Tur başı | 4 | **5** (4+1, tavan 5) |
| 1 ölü | 3 | 4 |
| 2 ölü | 2 | 3 |
| 3 ölü | 1 | **2** |

**Dolu kadroda beş terminalin beşi de zorunlu** — "+1" kuralının bıraktığı seçim
tur başında yok. Bilinçli kabul edildi: ölümler geldikçe gereken sayı düşüyor ve
son kaçan tek kaldığında 2'ye iniyor. O noktaya gelene kadar zaten en az 2
terminal yapılmış oluyor, yani "tek başına 2 terminal" cezası pratikte oluşmuyor.

Seçim tur başında da olsun istenirse `ObjectiveSetup.TerminalCount` 6 yapılır;
`terminalGoal` kendini ona göre günceller.

#### Doğum: kaçanlar bir arada, canavar uzakta (2026-09-06)

**Mirror doğum noktasını oyuncu objesi spawn olurken seçiyor** — yani lobide,
rol dağıtılmadan önce. Herkes rastgele bir noktaya düşüyordu ve **canavarın bir
kaçanın dibinde doğması işten değildi**: kovalamaca daha başlamadan bitiyordu.
Oynandığında şikâyet edilen buydu.

Rol ancak `AssignRoles`'den sonra belli olduğu için yerleştirme de oraya
taşındı (`RoundManager.ServerPlaceParticipants`). Mirror'ın kendi doğum
noktaları duruyor ve hâlâ işe yarıyor: lobide nerede duracağını onlar
belirliyor, tur başında burası üstüne yazıyor.

**Nokta seçimi her turda değişiyor, mesafe değişmiyor.** Kaçanların noktası
sahnedeki doğum noktalarından rastgele seçiliyor, canavarınki **ona en uzak**
olan. Sabit bir çift birkaç turda ezberlenir ve harita ölürdü; sabit olan şey
mesafenin kendisi, yeri değil.

Bugünkü altı nokta için ölçüldü: en uzak çift 45.5 m, ve kaçan noktası hangisi
seçilirse seçilsin canavar **en az 36.7 m** uzakta. Harita 54.4 m — yani
garanti gerçek. `minimumSpawnSeparation` (25 m) altına düşülürse konsola uyarı
yazılıyor; sessiz kalsaydı sorun yine "canavar dibimde doğdu" olarak geri
dönerdi.

**Kaçanlar üst üste doğmuyor:** ilki çapada, kalanlar 1.6 m yarıçaplı bir
halkada. Hepsini aynı noktaya koymak `CharacterController`'ları birbirini
itmeye zorluyor ve oyuncular tur başlar başlamaz fırlıyordu. Halkadaki bir yer
duvara denk gelirse çapaya düşülüyor — iki kaçanın aynı noktada doğması,
birinin duvara gömülmesinden iyi.

**Çapalar elle de konabilir:** `RoundManager`'daki `runnerSpawn` /
`monsterSpawn` alanlarına birer boş obje sürüklenirse hesaplama devre dışı
kalıyor. **İkisi birden dolu olmalı** — yarısı elle yarısı otomatik bir çift
mesafeyi garanti etmez.

> ### Işınlamayı sunucu tek başına yapamıyor
>
> Hareket **istemci otoriteli** (bölüm 4): sunucudaki konumu yazmak, sahibinin
> bir sonraki `NetworkTransform` güncellemesinde eziliyor. Asıl taşımayı
> sahibine giden `TargetRpc` yapıyor (`RoundParticipant.ServerPlaceAt`).
>
> Sunucuda da uygulanıyor, çünkü isabet ve tur kararlarını sunucu kendi gördüğü
> pozisyonlarla veriyor; bir ağ turu boyunca eski konumda görünmek yanlış
> kararlara kapı bırakırdı.
>
> `NetworkTransform.ServerTeleport` ayrıca çağrılıyor ki **diğer** istemciler
> sıçramayı ara değerlemesin — yoksa oyuncular haritanın bir ucundan öbürüne
> duvarların içinden süzülerek gidiyor görünür.
>
> `CharacterController` açıkken transform'a yazmak güvenilir değil; kapatılıp
> açılıyor. Hız **ve hız payı** sıfırlanıyor: lobide koşarak biriktirilen
> momentumla tura başlamak, canavarın da dolu payla doğması demekti ve bölüm
> 1'in "payı koşarak kazan" kuralını tur başında delerdi. Dikey bakış da
> sıfırlanıyor, yoksa lobide yere bakan oyuncu tura yere bakarak başlıyordu.

### 11.2 Terminaller

> Karanlık uyarısı: sahne ortam ışığı ~0.018. Terminal göstergesi ve çıkış
> kapısı gibi "uzaktan görünmesi gereken" yüzeyler **ışıktan etkilenmeyen**
> materyal kullanmalı (Sprites/Default), yoksa rengi ne olursa olsun siyah
> görünürler. TrailMarkSystem de aynı sebeple öyle yapıyor.

- Haritada **5 terminal**, hepsi **duvara monte** (`ObjectiveSetup.TerminalCount`).
- **E** ile etkileşim. İlerleme **yüzde** olarak dolar.
- Her terminal **göstergesiyle aynı renkte az ışık** döküyor (2026-09-04):
  boşta mavi, çalışırken parlak, kilitliyken kırmızı, bitince yeşil. Şiddet
  bilerek düşük (0.6) ve menzil kısa (4 m) — terminal koridoru aydınlatan bir
  lamba değil, uzaktan rengi okunan bir işaret. Yükseltmek karanlığı oynanıştan
  çıkarır (bölüm 5).

  Renk **tek kaynaktan** çıkıyor: `UpdateVisual` göstergeye ne yazıyorsa ışığa
  da onu veriyor. İki yerde ayrı renk tutulsaydı biri değişince öbürü unutulurdu.
  Renk normalleştiriliyor, çünkü Unity ışık rengini şiddetle çarpıyor ve doygun
  `lockedColor` ile sönük `idleColor` aynı şiddette çok farklı parlıyordu.

  **Çıkış kilit paneli de aynı çalışma sesini kullanıyor** (`ExitLock`,
  2026-09-05). İkisi de "makinenin başında duruyorsun" mekaniği; ayrı ses
  ikisini farklı şeylermiş gibi gösterirdi. Dizilim çözülünce susuyor.

  Işık **çalışma anında kuruluyor** (`Terminal.GetOrCreateStateLight`).
  Terminaller elle yerleştirildi ve `Terminal ve Çıkış Kur` var olanlara bilerek
  dokunmuyor (bölüm 0), yani editör aracına eklemek mevcut beş terminale hiç
  ulaşmazdı. Aynı desen `MonsterAura`'da da var.
- **Ses** (2026-09-05): dolarken çalışma sesi, kilitliyken uyarı — ikisi de
  döngü, uyarı kilit açılana kadar sürüyor. Ayrıntı bölüm 12'de.

  Hoparlör de ışık gibi çalışma anında kuruluyor, ama **klipler varlık olduğu
  için çalışma anında bulunamıyor**: onları `Sesleri Yerleştir` bağlıyor. O
  araç hiçbir şey silmiyor ve kurulum sırasında terminallerden sonra çalışıyor,
  yani hem mevcut beş terminale hem yeni kurulumlara ulaşıyor.
- İlerleme **kalıcı**: yarıda bırakılan terminal sıfırlanmaz, başkası devam
  eder. Ölen kişinin emeği kaybolmaz.
- Terminal başındayken kaçan:
  - **hareket edemez**,
  - yalnızca **sağa-sola çok az** bakabilir (dar bir açı).
  Durmak, canavara fırsat vermek demek — mekaniğin bedeli bu.

**Çıkarken terminale nişan almak gerekmiyor.** Bağlıyken terminal etkileşim
tuşunu üstleniyor (`PlayerInteractor.InputCaptured`). Öncesinde bakış
terminalden azıcık kayınca ışın onu bulamıyordu ve E hiçbir şey yapmıyordu —
yani çıkmak için yeniden nişan almak gerekiyordu. Aynı bayrak nişan yazısını
da susturuyor: terminal kendi ekranını çiziyor, ikisi üst üste biniyordu.

**Terminal ekranı**: nişangahın olduğu yerde, ekranın **tam ortasında** küçük
bir yeşil fosfor panel. Yüzde, dolum çubuğu, sınav yönü ve kalan süre orada.
Nişangah bu sırada hiç çizilmiyor — nişan alınacak bir şey yok, hareket bile
kilitli.

Panel **tam ekran değil, bilerek**. Terminal başındaki oyuncunun tek savunması
etrafını duyup görebilmek; ekranı kaplayan bir arayüz mekaniğin bedelini
haksız hâle getirirdi.

### 11.3 Yön tuşu mini oyunu

- Doldurma sırasında ekranda arada bir **yön işareti** çıkar (yukarı, aşağı,
  sağ, sol).
- Oyuncu **belirli bir süre içinde WASD** ile doğru yöne basmalı.
- **Sınav ekrandayken ilerleme durur**; doğru yöne basılınca devam eder. Yani
  sınav "arada bir çıkan engel" değil, ilerlemenin şartı — ekrana bakmadan
  terminal doldurulamıyor.
- Süre kontrolüne ağ gecikmesi payı ekleniyor (`rtt + 0.15`), yoksa yüksek
  pingli oyuncu zamanında bastığı hâlde kaybederdi.
- **Sınav sayacı kat edilen dolum süresiyle ilerler, duvar saatiyle değil.**
  Duvar saati kullanılınca E'ye basıp bırakarak sayaç sıfırlanıyor ve sınav hiç
  çıkmıyordu; terminal bedavaya doldurulabiliyordu. Bağlantıyı kesmek artık
  yalnızca kendi ilerlemeni durduruyor, sınavı ertelemiyor.
- E'ye bastıktan sonra **1 saniyelik bağlanma gecikmesi** var (`startDelay`);
  terminali tıkırdatmayı büsbütün anlamsız kılıyor.
- **Yanlış basılırsa veya süre geçerse:**
  - **canavara bildirim gider** (hangi terminal olduğu),
  - **terminal kilitlenir**.
- **Kilidi açmak:** rastgele üretilen **kısa bir yön örüntüsü** doğru sırayla
  girilir. Doğru girilince kilit açılır ve ilerleme kaldığı yerden devam eder.

### 11.4 Canavarın terminal üzerindeki gücü

- Canavar bir terminale **baktığında yüzdesini görür**.
- **E** ile terminali **kilitleme moduna** alabilir.
- Kilitlerken canavar da **1.5 saniye** ekrana bakar ve **hareket edemez**
  (`monsterLockDuration`). Başta 3.5 sn'ydi; oynandığında canavarı fazla uzun
  savunmasız bırakıp kilitlemeyi hiç yapılmayan bir hamleye çeviriyordu. Küçük ama gerçek bir bedel: kilitlemek bedava değil,
  o sırada savunmasız — kaçan yanından geçip gidebilir.
- Terminalin başında aynı anda tek kişi olabiliyor: kaçan doldururken canavar
  kilitlemeye başlayamıyor (zaten onu öldürmesi daha mantıklı).
- Canavarın kurduğu kilitte **alarm gönderilmiyor** — zaten orada duruyor.
- **Alarm süreli, kilit kalıcı** (2026-09-05). Üç hâl var:

  | Durum | Ses | Işık |
  |---|---|---|
  | Kaçan hata yaptı, ilk **10 sn** | var | yanıp sönüyor |
  | 10 sn sonra, hâlâ kilitli | yok | **sabit kırmızı** |
  | **Canavar kilitledi** | yok | **sabit kırmızı** |

  **Neden süreli.** Amaç canavara "burada biri hata yaptı" diye bir uyarı
  vermek, terminali kalıcı sirene çevirmek değil. Kilit açılana kadar ötseydi
  üç kilitli terminal birikince ses kirliliğinden başkası kalmaz, üstelik
  sürekli çalan alarm duyulmaz olurdu. Süre bitince terminal susuyor ama
  **kırmızı kalıyor**: durum bilgisi kaybolmuyor, yalnızca dikkat çekmeyi
  bırakıyor.

  **Neden canavarınki hiç ötmüyor.** Uyarı sesi 18 metreden duyuluyor; öten bir
  terminal "canavar az önce buradaydı" diye bağırırdı. Işık ise 9 m — kilitli
  terminali yanına gelen görüyor, bu zaten olması gereken. Sesle ışığın
  ayrılma sebebi **menzil farkı**.

  Üçünü de tek ölçüt sürüyor (`Terminal.AlarmActive`), yani ses ve nabız
  hiçbir durumda ayrışamıyor. Canavarın kilidinde alarm penceresi zaten sıfır
  kuruluyor, o yüzden ayrı bir "kilidi kim kurdu" bayrağı gerekmedi — bir tane
  eklenmiş ve hiçbir yerde okunmadığı görülünce kaldırılmıştı.

### 11.5 Çıkış

- Haritada **iki çıkış** var (`ObjectiveSetup.ExitCount`). İkisi de dış duvar
  halkasında, **birbirinden en uzak** iki gedikte: aynı kenara düşen iki çıkış
  canavarın ikisini birden görmesi demek olurdu ve ikincisi hiçbir şey
  değiştirmezdi.
- **Terminaller bitince kapı KENDİLİĞİNDEN AÇILMIYOR.** Her kapının yanında bir
  **kilit paneli** var (`ExitLock`); kaçanın oraya gelip **on adımlık yön
  dizilimini** doğru girmesi gerekiyor.
  - Panel yalnızca terminaller bitince çalışıyor — terminal sistemi hâlâ kapıyı
    kilitleyen şey.
  - Panel başında **hareket kilitli**, bakış dar bir koniye sıkışıyor —
    terminaldeki odak mekanizmasının aynısı.
  - **Yanlış tuş başa sarıyor.** Dizilim değişmiyor: yenisini üretmek ekrandaki
    diziyi okumayı anlamsız kılar ve cezayı orantısız yapardı.
  - Panelden ayrılmak ilerlemeyi sıfırlıyor; her bağlanışta yeni dizilim
    üretiliyor, yani aynı kapıyı ikinci kez açan ezberden geçemiyor.
  - **Yalnızca kaçan kullanabiliyor.** Canavara panel hiçbir şey yazmıyor.

  Gerekçe: terminaller bitince kapının açılıvermesi turun son perdesini bedavaya
  veriyordu — kaçan koşup çıkıyordu, canavarın yapabileceği bir şey yoktu. On
  adım seni bir yere çiviliyor ve canavara son bir pencere açıyor.

  **Panel yoksa eski davranışa düşülüyor** (terminaller bitince kapı açılır):
  aksi hâlde eksik bir referans, kapının hiç açılmadığı ve sebebi görünmeyen bir
  tur kilidine dönüşürdü.
- Kaçan ancak oradan geçerse kurtulur.
- **Canavar çıkıştan geçemez.** Kural olarak değil, fiziksel engelle: kapının
  ağzındaki katı collider yalnızca canavarın istemcisinde açık. Hareket istemci
  otoriteli olduğu için engelin de istemcide olması gerekiyor.
- Kurtulan oyuncu **elenen gibi haritadan kaybolur** ve izleyici moduna geçer;
  sahada kalanları izler. Ama "öldü" değil "kurtuldu" sayılır — tur sonucunu
  bu fark belirliyor.
- **Bir kişinin kaçması turu bitirmez.** Sahada kaçan kaldığı sürece oyun
  sürer; tur ancak sahada oynayan kalmayınca biter.

### 11.6 Ayarlanabilir sayılar

Hepsi Inspector'da, `Terminal` bileşeninde. Oynayarak ayarlanacak:

| Alan | Değer | Ne yapar |
|---|---|---|
| `fillDuration` | 18 sn | Kesintisiz doldurma süresi |
| `promptInterval` | 3.5–7 sn | İki sınav arası (dolum süresiyle ölçülüyor) |
| `promptWindow` | 1.6 sn | Doğru tuşa basma süresi |
| `startDelay` | 1 sn | E'den sonra dolumun başlaması |
| `unlockLength` | 4 | Kilit örüntüsündeki yön sayısı |
| `monsterLockDuration` | 1.5 sn | Canavarın kilitleme süresi |
| `focusYawLimit` | 35° | Terminal başında sağa-sola bakış |
| `focusPitchLimit` | 12° | Terminal başında yukarı-aşağı bakış |

Çıkış kilidinde ayarlanabilenler (`ExitLock`):

| Alan | Değer | Ne yapar |
|---|---|---|
| `SequenceLength` | 10 | Yön dizilimindeki adım sayısı (sabit, kod içinde) |
| `focusYawLimit` | 35° | Panel başında sağa-sola bakış |
| `focusPitchLimit` | 12° | Panel başında yukarı-aşağı bakış |

`RoundManager.terminalGoal` = 5 (haritadaki terminal sayısı, aynı zamanda
gereken sayının **tavanı**).

**Bu sayı elle ayarlanmıyor.** `Terminal ve Çıkış Kur` her kurulumda gerçekte
yerleşen terminal sayısına çekiyor (`SyncTerminalGoal`). Tavan yerleşen sayıdan
büyük kalırsa tur kilitleniyor: RoundManager var olmayan bir terminali bekler ve
çıkış hiç açılmaz. Terminal yerleştirme prosedürel olduğu için bu gerçek bir
risk — aralık eşiği tutmazsa araç kademeli gevşetiyor, yine de eksik kalırsa
tavanı düşürüp konsola uyarı yazıyor.

### Uygulama durumu — TAMAMLANDI

Sekiz adımın hepsi yazıldı ve oyunda test edildi:
tur kuralları · terminal bileşeni · odaklanma kısıtı · yön tuşu mini oyunu ·
kilit açma örüntüsü · canavarın kilitleme yetkisi · çıkış kapısı · editör aracı.

İlgili dosyalar: `Interaction/Terminal.cs`, `Interaction/ExitGate.cs`,
`Core/RoundManager.cs`, `Editor/ObjectiveSetup.cs`.

---

## 12. Ses sistemi

Klipler `Assets/_Audio` altında, `AudioSetupUtility` **dosya adına göre**
bağlıyor. Aynı isimle üzerine yazarsan referanslar bozulmaz.

| Dosya | Nerede |
|---|---|
| `Adim_Kacan` | Kaçan adım sesi |
| `Adim_Canavar` | Canavar adım sesi |
| `Inis` | Yere değme |
| `Kapi` | Kapı açılma/kapanma — **labirent ve çıkış kapıları** |
| `Olum` | Yakalanma |
| `Bicak_Savurma`, `Bicak_Isabet` | Bıçak (hâlâ yer tutucu) |
| `Terminal_Calisma` | Terminal dolarken dönen çalışma sesi |
| `Terminal_Uyari` | Terminal kilitliyken dönen uyarı |

**Terminal sesleri 3B ve döngü.** İkisini de `Terminal.UpdateAudio` sürüyor,
klipleri `Sesleri Yerleştir` bağlıyor. Karar veren dört alan (`locked`,
`activeUserNetId`, `prompt`, `fillReadyTime`) zaten SyncVar olduğu için
**ağdan hiçbir şey gelmiyor** — her istemci aynı sonucu kendi hesaplıyor.

3B olması bilinçli: terminalde çalışmak **ses çıkarmak** demek, canavar duyup
gelebiliyor. Bölüm 5'teki hız/gizlilik takasının aynı mantığı — ilerleme
kaydetmek kendini ele vermek.

**Çalışma sesi E'ye basar basmaz başlıyor ve bağlantı boyunca kesintisiz.**
İki şart tek tek denendi ve ikisi de kaldırıldı:

- `prompt == 0` — ilerleme sınav ekrandayken durduğu için mantıklı görünüyordu,
  ama her sınavda kesilip başlayan ses kesik kesik duyuluyordu.
- `fillReadyTime` — E'den sonraki bir saniyelik bağlanma gecikmesini bekliyordu,
  ses geç geliyordu.

Doğru ölçüt "dolum ilerliyor mu" değil, **makine çalışıyor mu**. Klibin adı da
bunu söylüyor: *açılma* ve çalışma sesi. Dolumun ne zaman başladığını ekran
anlatıyor (`BAĞLANTI` → `VERİ AKTARIMI`), sesin işi değil.

### Alarm: ışık sesi takip ediyor

Kilitli terminalin kırmızı ışığı **sesin anlık genliğinden** sürülüyor
(`Terminal.UpdateAlarmLevel`), ayrı bir sayaçla yanıp sönmüyor.

Sebebi kayma: "saniyede iki kez yanıp sön" demek, ses ve ışığı bağımsız iki
saate bağlamak olurdu. Klip uzunluğu sayacın periyoduna tam bölünmediği sürece
ikisi yavaş yavaş ayrışır ve birkaç saniye sonra ışık sessizlikte yanar; klip
değişirse baştan ayar gerekir. Işığı doğrudan dalga biçiminden sürünce kayma
diye bir şey kalmıyor — bip varsa parlıyor, sessizlik varsa sönüyor, hangi klip
konursa konsun kendiliğinden uyuyor.

Ham genlik saniyede yüzlerce kez sıfırdan geçtiği için doğrudan bağlanmıyor:
tepe anında alınıp yavaş bırakılıyor (`alarmFalloff`), yani dalga zarfa
dönüşüyor. Bip kısa, ışığın izi biraz daha uzun.

**Alarmda gösterge ve ışık ayrışıyor, bilerek.** Gösterge durumu OKUTUYOR
(kırmızı = kilitli), ışık ise UYARI VERİYOR: daha doygun kırmızı, birkaç kat
parlak, daha geniş menzil. Diğer bütün durumlarda ikisi aynı renkten besleniyor.
Sönüm noktasında ışık tamamen sönmüyor (`alarmDimIntensity`) — sıfıra inen ışık
bozuk lamba gibi duruyor, kısılan ışık nabız gibi.

**Adım sesi klipleri tek adım olmalı, döngü değil.** Adımlar zamanla değil kat
edilen mesafeyle tetikleniyor; koşarken kendiliğinden sıklaşıyor. Koşu adım
aralığı 1.8→2.6 m açılıyor, yoksa tempo iki katına çıkıp makineli tüfek gibi
duyuluyor.

> ### Adım sesi HERKESTE çalmalı — bir süre çalmıyordu (2026-09-05)
>
> `FootstepAudio` `NetworkSetup`'ın `localOnlyComponents` listesindeydi, yani
> **uzak oyuncularda bileşen tamamen kapalıydı.** Herkes yalnızca kendi adımını
> duyuyordu: canavar koşarak arkandan gelirken hiçbir ses çıkmıyordu.
>
> Bu, bölüm 5'teki hız/gizlilik takasının bir ayağını sessizce yok ediyordu —
> "koşarsan duyulursun" kuralı işlemiyordu. Fenerin aynı şekilde yıllarca
> çalışmamasıyla (bölüm 5'teki düzeltme kutusu) birebir aynı hata sınıfı:
> mekanik yazılmış, ağ tarafında bağlanmamış.
>
> **Kendi sesini duyduğun için fark edilmiyordu.** Tek başına test ederken her
> şey doğru görünüyor; hata ancak iki oyuncuyla ortaya çıkıyor.
>
> Listeden çıkarıldı. `NetworkPlayerSetup` ayrıca **eski prefabta listede kalmış
> olsa bile** bu bileşeni açık tutuyor: alan prefabta serileştirilmiş duruyor ve
> kodu değiştirmek tek başına yetmezdi (bölüm 16'daki tuzak). Böylece `Ağ
> Kurulumu` zincirini yeniden çalıştırmak gerekmiyor.
>
> Bileşen artık her oyuncuda çalıştığı için hız ve zemin bilgisini
> `PlayerController`'dan **okuyamıyor**: o bileşen uzakta kapalı ve değerleri
> donmuş. İkisi de gerektiğinde pozisyon farkından çıkarılıyor — animatörlerin
> yaptığının aynısı (bölüm 14, 17), ek ağ trafiği sıfır. Ölçüt
> `controller.enabled`: açıksa hareket kodundan al, kapalıysa pozisyondan çıkar.
> İniş sesi de aynı yoldan geliyor (en yüksek nokta ile yere değme arasındaki
> fark düşülen mesafe), çünkü `Landed` olayı uzakta hiç tetiklenmiyor.

**Zıplama tuşuna ses bağlı değil, bilerek.** İniş sesi yere değince çalıyor ve
**düşülen mesafeye** bakıyor (eşik 0.95 m): kutunun üstüne zıplamak sessiz,
zıplayıp yere inmek sesli. İniş hızı yerine mesafe kullanmanın sebebi
ayarlanabilirlik — "1 metreden alçak düşüşte ses olmasın" demek anlaşılır.

**Dosyalar mono olmalı.** Unity'de stereo klipler 3B konumlandırılmıyor; bu
oyunda sesin yönü doğrudan oynanış. `Yakalamaca > Sesleri Yerleştir` bunu
otomatik yapıyor.

### Mağara yankısı

`Yakalamaca > Mağara Yankısı Kur (reverb)` sahnenin köküne bir
`AudioReverbZone` (`MagaraYankisi`) koyup bütün 3B kaynaklara mesafeye bağlı
yankı eğrisi bağlıyor. Bölge haritanın **çocuğu değil**: `Labirent Harita Kur`
haritayı komple sildiği için çocuğu olsa her düzenlemede kaybolurdu.

**Dozu oynanışa bağlı.** Unity'de yankı sinyali yönsüzdür — dry panlanır, wet
panlanmaz — yani yankıyı sonuna kadar açmak canavarın yönünü silmek demek.
Yukarıdaki mono kuralıyla aynı sebepten iki tedbir var:

- **Yankı mesafeyle açılıyor** (`reverbZoneMix` eğrisi 0.5 → 1.0). Yakındaki
  ses kuru kalıp yönünü belli ediyor, uzaktaki ıslanıp mekânı büyütüyor.
  "Arkamda — hangi tarafta?" sorusu hep yakın mesafede soruluyor.
- **Kuyruğun tizi kısık** (`roomHF = -1500`, `decayHFRatio = 0.7`). Kulak yönü
  büyük ölçüde tizden çıkarıyor; kuyruk pesleşince mağara hissi kalıyor ama
  yön ipuçlarının önüne geçmiyor. Unity'nin hazır `Cave` ayarı tizi hiç
  kısmıyor, o yüzden hazır ayar değil özel değerler yazılı.

`decayTime` 2.2 sn (Cave 2.91): koridorlar 3.2 m, dar bir taş koridor o kadar
çınlamaz — ayrıca uzun kuyruk arka arkaya gelen adımları birbirine karıştırıp
tempoyu okunmaz yapıyor.

**2B sesler yankılanmıyor.** `spatialBlend == 0` olan kaynakların
`reverbZoneMix`'i sıfırlanıyor. Kalp atışı ve arayüz sesleri kafanın içinde
çalıyor, mekânda değiller. Yeni bir 2B ses eklerken bu kural geçerli.

**Sesli sohbet geldiğinde ekstra iş yok**: konuşma oyuncunun üstündeki 3B bir
AudioSource'tan çalarsa yankıya kendiliğinden giriyor.

Ayarlamak için sahnedeki `MagaraYankisi` objesini seç — Inspector'daki değerler
**Play modunda canlı** çalışıyor. `Reverb Preset`'i Cave / StoneCorridor /
Hangar yapıp karşılaştırabilir, User'a dönünce özel değerlere geri
gelebilirsin.

**Ağ Kurulumu'nu tekrar çalıştırırsan** oyuncu prefabı sıfırdan kurulduğu için
eğri gider; bu menüyü de tekrar çalıştır.

---

## 13. Menü ve lobi

Tur artık sunucudaki [1] tuşuyla değil, gerçek bir lobiden başlıyor. Akış:

```
İsim ekranı (ilk açılışta bir kez)
  ↓
ANA MENÜ ── LOBİ KUR ──────────────► LOBİ (oda)
        └─ LOBİYE KATIL → kod gir ──►
                                      ├─ kadro, hazır işaretleri
                                      ├─ [oda sahibi] canavar seçimi
                                      ├─ [oda sahibi] BAŞLAT
                                      └─ AYRIL
  ↓ tur başlar (menü kapanır)
  ↑ tur biter (lobiye dönülür)
```

### Menüye NetworkIdentity eklenemez

Bu, bütün lobi mimarisini belirleyen kısıt. Mirror sunucu açılışında sahnedeki
**bütün** `NetworkIdentity`'leri aktifleştirip spawn ediyor (bölüm 4), yani
menü canvas'ına kimlik eklemek onu sunucunun kontrolüne sokardı.

Sonuç olarak menü ağ durumunu **yalnızca statiklerden okuyor**
(`NetworkServer.active`, `NetworkClient.isConnected`, `RoundManager.Instance`,
`RoundParticipant.All`), komut göndermesi gerektiğinde ise **oyuncunun kendi
objesini** kullanıyor: `RoundParticipant.Local.RequestStartRound()` gibi.
Oyuncu objesinin kimliği zaten var.

`LobbyNetwork` bu köprü: sunucu açma, kodla bağlanma, ayrılma, bağlantı
hatalarını metne çevirme ve tur başlayınca menüyü kapatma.

### İki kod biçimi — hangisi olduğunu UZUNLUK söylüyor

İkisi de `LobbyCode`'da, ikisi de aynı 32 harflik alfabeyi kullanıyor. Alfabede
karışan harfler yok (I, O, 0, 1) — kod sesli sohbette söylenecek.

| | Uzunluk | Nereden geliyor | Ne zaman |
|---|---|---|---|
| **EOS oda kodu** | 6 | Rastgele; odaya öznitelik olarak yazılıyor | Relay çalışırken (olağan hâl) |
| **Yerel kod** | 7 | Sunucunun IPv4 adresinin yazılışı | EOS açılmadığında |

**Yerel kod neden adresin kendisi:** rastgele bir kod, onu saklayacak bir
eşleştirme sunucusu gerektirir — ayakta tutulacak bir servis demek (bölüm 0).
32 bitlik IPv4 ise 32 harflik alfabeyle tam 7 karaktere sığıyor ve hiçbir yerde
saklanmıyor. Sınırı açık: bu doğrudan bağlantı, aynı ağda çalışır; internette
7777/UDP yönlendirmesi ya da sanal ağ (Hamachi, Radmin) gerekir.

**EOS kodu neden rastgele olabiliyor:** kaydı Epic tutuyor ve o servis zaten
relay için ayakta. Yani "saklamayalım" kuralı burada bedelsiz kalkıyor —
tuttuğumuz yeni bir şey yok.

**İkisini uzunluk ayırıyor, içerik değil.** Alfabe ortak olduğu için "K7M2QX"
ile "K7M2QXB" arasındaki tek fark bir karakter; aynı uzunlukta olsalardı
girilen kodun hangisi olduğu anlaşılamazdı. Katılma alanı ham IP de kabul
ediyor (nokta içeriyorsa IP), ayrıca kısa kod alınamadığında yedeğe düşen 32
karakterlik ürün kimliğini de.

> **6 harfin benzersizliği garanti DEĞİL.** 32^6 ≈ 1.07 milyar bileşim var ama
> kod rastgele üretiliyor ve kimse çakışma kontrolü yapmıyor — kontrol için bir
> sunucu gerekirdi ve kaçındığımız şey tam olarak o. Aynı anda açık birkaç
> odada çakışma ihtimali ölçülemez; olursa katılan yanlış odaya düşer ve kod
> tekrar istenir.

> **Katılma alanının iki sessiz kısıtı vardı; ikisi de EOS'la ortaya çıktı.**
>
> - `characterLimit` **15**'ti — `255.255.255.255` tam sığsın diye seçilmiş.
>   EOS kodu 32 karakter; yapıştırınca **sessizce kırpılıyor** ve oyuncu
>   neden bağlanamadığını anlamıyordu. **64** yapıldı: alan dört biçimi birden
>   almak zorunda (6 karakter oda kodu, 7 karakter yerel kod, 15 karakter IP,
>   32 karakter EOS kimliği).
> - `characterValidation` **alfanümerikti** ve o kural **noktayı eliyordu**.
>   Yani "ham IP de kabul ediliyor" sözü aslında hiç tutmuyordu: girilen
>   adresten noktalar düşüyordu. **Kapatıldı** — içeriği zaten
>   `LobbyNetwork.JoinLobby` denetliyor, alanda ikinci kez süzmek yalnızca
>   sessiz hata üretiyor.
>
> Ders: **bir giriş alanına kısıt koyarken, o alana gelebilecek EN UZUN ve
> EN GENİŞ biçimi düşün.** İkisi de yıllarca fark edilmedi çünkü tek bir
> biçim (7 harflik kod) deneniyordu.

**Host olurken bütün IPv4 adresleri ekranda yazıyor**
(`LobbyCode.LocalAddresses`). Kod, `LocalAddress()`'in bulduğu **internete
çıkan** adaptörden üretiliyor; sanal ağda (Radmin) gereken adres başka bir
adaptörde olduğu ve internete çıkmadığı için kod orada yanlış çıkıyor. Liste
bilerek filtrelenmiyor — hangisinin doğru olduğunu makine bilemez, ama oyuncu
Radmin penceresindeki adresi listeden tanıyor.

### İki transport: EOS ve yerel (2026-09-05)

`NetworkManager`'da **iki** transport birden duruyor, `LobbyNetwork` başlamadan
önce seçiyor (`UseTransport`):

| Transport | Ne zaman | Kod nasıl görünüyor |
|---|---|---|
| `EosTransport` | EOS hazırsa, LOBİ KUR | **6 harflik oda kodu** (lobi servisi cevap vermezse 32 karakterlik ProductUserId) |
| `KcpTransport` | EOS hazır değilse, ya da IP ile katılırken | 7 harflik kod / ham IP |

**Aynı objede durabiliyorlar.** `EosTransport` doğrudan `Transport`'tan
türüyor; `KcpTransport`'un `[DisallowMultipleComponent]`'i yalnızca kendinden
türeyenleri engelliyor. (Edgegap denemesinde tam bu duvara çarpılmış, alt obje
kurmak gerekmişti.)

**`UseRelay` üç şartı birden arıyor:** transport bağlı, `EOSSDKComponent`
açılmış ve kimlik alınmış. Kimlik bilgileri yanlışsa ya da Epic'teki istemci
politikasında **P2P izni yoksa** SDK açılmıyor ve şart tutmuyor.

**EOS girişi ASENKRON ve bu bir tuzak.** Device ID üretiliyor, sonra `Connect`
girişi yapılıyor; Play'e basıp hemen LOBİ KUR diyen biri için henüz hazır
olmuyor. İlk sürüm o anı görüp **sessizce yerel odaya düşüyordu** — her şey
doğru kurulmuşken bile. Artık `HostWhenRelayReady` hazır olana kadar bekliyor
(`relayWaitTimeout`, 12 sn).

Beklerken `IsConnecting`'e **bakılmıyor, bilerek**: giriş daha başlamadan
önceki ilk karelerde o da false oluyor ve o anı yakalayan bir kontrol EOS'a hiç
şans vermeden düşerdi. Ölçüt tek: hazır mı, değil mi.

**Süre dolarsa yerel odaya düşülüyor** ve ekranda sebebi yazıyor. Sonsuza kadar
beklemek oyuncuyu asılı bırakırdı; hiç düşmemek ise EOS kurulumu tamamlanmamış
bir projede oyunu büsbütün oynanamaz yapardı.

**Katılma alanı dört biçimi de kabul ediyor** ve ayırt etmek kolay: nokta
içeriyorsa IP, 6 harfse EOS oda kodu, 7 harfse yerel kod, daha uzunsa EOS ürün
kimliği. Kod alfabesinde nokta yok.

**Oyuncularda Epic hesabı GEREKMİYOR.** Transport `Connect` arayüzünü
`DeviceidAccessToken` ile kullanıyor: kimlik cihazda sessizce üretiliyor,
oyuncu hiçbir şey fark etmiyor. `authInterfaceLogin` bilerek kapalı — açık
olsaydı herkesin Epic hesabıyla giriş yapması gerekirdi.

> ### Aynı bilgisayarda iki kopyayla EOS test edilemez
>
> Kimlik **cihaz** başına üretiliyor, oyuncu başına değil. Editördeki oyun
> da build de aynı `ProductUserId`'yi alıyor; biri öbürünün koduna
> bağlanmaya çalıştığında **kendine** bağlanmış oluyor ve EOS bunu
> reddediyor. Geriye yalnızca zaman aşımı kalıyor ve sebep hiçbir yerde
> yazmıyordu.
>
> `LobbyNetwork.JoinLobby` artık bu durumu yakalayıp açıkça söylüyor.
>
> **KCP'de böyle bir sorun yok** — orada kimlik adres, iki kopya `127.0.0.1`
> üzerinden birbirini görüyor. Yerel test bu yüzden hâlâ tek makinede
> yapılabiliyor.
>
> **EOS'u denemenin iki yolu var:**
>
> 1. **İkinci bir makine** (arkadaş). Ek kurulum gerekmiyor ve zaten
>    gönderilecek yapılandırma bu.
> 2. **EOS Dev Auth Tool** — pakette hazır geliyor
>    (`EpicOnlineTransport/DevAuthTool/`). Epic hesabıyla iki ayrı kimlik
>    üretip tek makinede test etmeyi sağlıyor: zip'i sonu `~` ile biten bir
>    klasöre aç (Unity öyle bir klasörü içe aktarmıyor), aracı çalıştır, iki
>    ayrı kimlik adı oluştur, sonra `EOSSDKComponent`'te
>    `authInterfaceLogin` aç, tür `Developer`, `devAuthToolCredentialName`
>    her kopyada farklı olacak şekilde ayarla (`devAuthToolPort` 7878).
>
>    İki kopyaya farklı ad vermek build başına ayrı yapılandırma demek, o
>    yüzden zahmetli. Arkadaş varsa birinci yol her zaman daha hızlı.

### Kısa kod ve oda listesi: EOS lobi servisi (2026-09-05)

`RelayLobby`, paketin `EOSLobby`'sinin üstüne kısa kodu ekliyor. Akış:

```
HOST                              KATILAN
 kod üret (6 harf)
 odayı kur, kodu öznitelik yaz
 ── kod hazır ──► StartHost()
                                  kodu gir
                                  EOS'ta odayı ara
                                  odaya katıl, host_address'i oku
                                  ── adres ──► StartClient()
```

**Türetiliyor, yamalanmıyor.** Paket lobi işini zaten yapıyor; kopyalamak dört
yüz satır SDK borusunu ikinci kez yazmak, doğrudan düzenlemek ise pakete
**beşinci bir yerel yama** eklemek olurdu (bölüm 9 — her yama paket
güncellenince kayboluyor). Türetmek ikisinden de kaçınıyor.

**Base'in `Start`'ı çağrılmıyor.** `EOSLobby.Start()` ilk satırında
`GetLobbyInterface()` çağırıyor ve o metot `Instance.EOS`'a null kontrolü
yapmadan dokunuyor. EOS açılmamışsa sahne yüklenir yüklenmez
`NullReferenceException` atardı — üstelik yerel odayla oynamak isteyen birinin
EOS'la hiç işi yokken. Bildirim kaydı EOS hazır olunca, ilk istekte yapılıyor.

**Her isteğin tam olarak bir cevabı var.** EOS geri çağrıları asenkron ve **hiç
gelmeyebilir**; cevapsız kalan bir istek oyuncuyu "Oda kuruluyor…" ekranında
sonsuza kadar asılı bırakırdı. Her istek bir sayaçla başlıyor ve sonucu iki
uçtan hangisi önce gelirse o veriyor — ama yalnızca biri.

**Sunucu, kod hazır olduktan SONRA açılıyor.** Önce açıp kodu sonradan
yazdırmak daha hızlı olurdu ama ekrandaki kod bir anda 32 karakterden 6
karaktere dönerdi; oyuncu arkadaşına hangisini vereceğini bilemez ve muhtemelen
ilk gördüğünü verirdi.

**Lobi servisinin çökmesi odayı çökertmiyor.** Kısa kod alınamazsa uzun kodla
devam ediliyor ve oda yine kuruluyor: çalışan bir yolu (relay) çalışmayan bir
yol (lobi servisi) yüzünden kapatmak yanlış olurdu. Konsola uyarı yazılıyor.

**Kod hem yazılırken hem aranırken küçük harfe çevriliyor**
(`LobbyCode.ToSearchForm`). EOS'un lobi araması metin özniteliklerinde
büyük/küçük harfi her sürümde aynı ele almıyor; iki uçta da küçültmek sorunun
tamamını ortadan kaldırıyor. Ekranda gösterilen hâli yine büyük harf.

**Oda listesi yalnızca KOD üretiyor**, `LobbyDetails` saklamıyor: listeden
seçmek de kodla katılmakla aynı yoldan geçiyor. Tutulan bir handle'ın oyuncu
düğmeye basana kadar bayatlaması (oda kapanır, dolar) ikinci bir hata yolu
açardı; arama zaten milisaniyeler sürüyor.

**Liste EOS hazır değilken hiç görünmüyor.** Çalışmayan bir bölümü göstermek
"oyun bozuk" izlenimi verir. Ama arama `OnEnable`'da **yapılamıyor**: EOS girişi
asenkron, oyuncu Play'e basıp hemen katılma ekranına gelebiliyor ve o an gizlenen
bölüm bir daha geri gelmezdi. `Update` hazır olduğu **ilk karede** bir kez
arıyor.

**Vazgeçilirse EOS'taki kayıt kapatılıyor.** Oda kurulurken ya da aranırken
AYRIL'a basılabiliyor; EOS'taki kayıt o sırada çoktan oluşmuş oluyor ve
kapatılmazsa kimsenin giremeyeceği bir oda listede asılı kalırdı. Aynı sebeple
bağlanma zaman aşımında da kapatılıyor.

**`OnApplicationQuit`'te oda kapatılmıyor, bilerek.** `EOSSDKComponent` çıkışta
`EOS.Release()` çağırıyor ama `initialized` bayrağını **indirmiyor**; Unity'de
bileşenler arası çıkış sırası da garanti değil. Bizimki sonra çalışsaydı
"açılmış görünen ama serbest bırakılmış" bir platforma dokunup çıkışta ikinci
bir hata üretirdi — bölüm 9'daki "asıl hatanın üstüne ikinci hata biniyor"
tuzağının aynısı. Kalan oda kaydını EOS host'un bağlantısı düşünce kendisi
temizliyor.

**Kendi odana bağlanma kontrolü artık ÇÖZÜLEN adreste.** Eskiden girilen metin
kendi `ProductUserId`'mizle karşılaştırılıyordu; kısa kodla o karşılaştırma hiç
tutmaz (kod 6 harf, kimlik 32 karakter). Kontrol EOS'tan dönen host adresine
taşındı, mesaj aynı kaldı.

> **`EosApiKey.asset` içinde CLIENT SECRET var.** Depo şu an yerel; **herkese
> açık bir GitHub deposuna gönderilmeden önce bu dosya çıkarılmalı**, yoksa
> anahtar sızar. Sızarsa Epic portalından yeni bir client oluşturup eskisini
> silmek gerekiyor.

### Yetki: arayüz tahmin eder, sunucu karar verir

`isRoomOwner` ve `isReady` birer SyncVar ve **arayüz onlara bakıp düğme
çiziyor**. Ama sunucu hiçbirine güvenmiyor:

- `ServerIsHost()` yetkiyi her komutta kendi katılımcı listesinden yeniden
  hesaplıyor
- `ServerRequestStart()` oyuncu sayısını ve hazır durumlarını baştan kontrol
  ediyor
- `ServerSetMonsterChoice()` seçilen kişinin gerçekten odada ve bot olmadığını
  doğruluyor

Değiştirilmiş bir istemci en fazla kendi ekranında gri düğmeyi aktif gösterir.
`RoundManager.CanStartFromClient()` adında "FromClient" geçmesi bu yüzden:
o metot bir karar değil, bir tahmin.

**Oda sahipliği devrediliyor.** Sahip çıkarsa kalanlardan biri devralıyor
(`ServerRefreshHost`), yoksa lobi kimsenin başlatamadığı bir ölü odaya
dönerdi. Bot asla sahip olamaz — bağlantısı yok, düğmeye basamaz.

### Kadro ayrı bir mesajla gönderilmiyor

Lobi listesi `RoundParticipant.All`'dan okunuyor: katılımcılar zaten spawn
edilmiş kimlikler, herkes hepsini görüyor, adı ve hazır durumu SyncVar olarak
geliyor. Ayrı bir liste mesajı aynı veriyi ikinci kez göndermek olurdu.

`LobbyRoster` artık **yalnızca görünüm modeli**: tek işi aynı adı taşıyanları
numaralandırmak (`Player 1`, `Player 2`). Eskiden kontenjan ve rol seçimi de
onda duruyordu; ağ katmanıyla hepsi `RoundManager`'a geçti — iki yerde tutulan
kadro er ya da geç birbirini tutmaz (bölüm 5).

SyncVar'ların toplu bir "değişti" olayı olmadığı için lobi ekranı 5 Hz
yokluyor. Beş kişilik bir listede maliyeti ölçülemez.

### Arkaplan: ölçüt ekran değil, tur oynanıyor mu

Menünün arkasındaki tam ekran karartma (`Arkaplan`) **tur oynanmıyorken**
açılıyor, hangi ekranda olduğuna bakılmaksızın.

Sebep: lobide arkada haritayı göstermek "oyun arkada çalışıyor, Esc'ye basınca
dönerim" izlenimi veriyordu — oysa dönülecek bir tur yok. Tur sürerken
duraklatmada ise arkayı görmek gerekiyor, nerede durduğunu unutmadan devam
edebilesin. Tek kural ikisini de doğru çözüyor, seçenekler ekranı dahil:
ana menüden açılınca karanlık, duraklatmadan açılınca sahne görünür.

### Menü açılırken oyuncu henüz doğmamış olabilir

`Show(Screen.Lobby)` sunucu açılırken çağrılıyor; oyuncu objesi birkaç kare
sonra spawn oluyor. `MenuController` duraklatılacak bileşenleri o anda
bulamadığı için **lobide hareket ve bakış hiç kesilmiyordu** — arkadaki
haritada dolaşılabiliyordu.

Bu yüzden `Update` yerel oyuncunun değişip değişmediğine bakıyor ve
değiştiyse duraklatma durumunu yeniden uyguluyor. Menüden önce var olduğu
varsayılabilecek hiçbir oyuncu bileşeni yok.

### Faz geçişi: fazın kendisine bakılıyor, geçişin yönüne değil

Tur `Playing → Ended → Waiting` diye ilerliyor; arada 4 saniyelik sonuç
ekranı var. "Playing'den Waiting'e geçince lobiye dön" kuralı aradaki `Ended`
yüzünden **hiç tutmuyordu** ve tur bitince oyuncu boş sahnede kalıyordu.

`LobbyNetwork.TickPhase` artık fazın kendisine bakıyor: `Playing` → menüyü
kapat, `Waiting` → lobiyi aç, `Ended` → dokunma (sonuç okunsun).

Lobiye dönünce geçen turun sonucu durum satırında görünüyor: `result`
SyncVar'ı bir sonraki `StartRound`'a kadar duruyor, ayrıca saklamaya gerek yok.

### Ödünç alınan kamera geri veriliyor

`NetworkPlayerSetup` yerel oyuncu doğunca sahnedeki menü kamerasını ve ses
dinleyicisini kapatıyor. Odadan ayrılınca oyuncu objesi yok ediliyor ve onunla
birlikte çizen tek kamera da gidiyordu: ekranda Unity'nin
**"Display 1 — No cameras rendering"** yazısı kalıyordu.

Bileşen artık kapattıklarını listeliyor ve `OnDestroy`'da geri açıyor. Kural:
ödünç aldığın sahne nesnesini geri ver.

### Seçenekler geldiği yere döner

`ShowSettings()` açılış ekranını hatırlıyor; GERİ ve Esc oraya dönüyor. Sabit
"ana menüye dön" davranışı, tur ortasında duraklatıp seçeneklere giren
oyuncuyu **bağlantısı sürerken ana menüde** bırakıyordu ve oradan tura geri
dönmenin yolu yoktu.

### Ayarlar ve tuş atamaları

Seçenekler ekranı: oyuncu adı · fare hassasiyeti · ses · ters bakış ·
tuş atamaları. Hepsi `PlayerPrefs`'te, `PlayerProfile` üzerinden.

**Ayarlar sahnedeki bir bileşene yazılamaz.** Oyuncu prefabtan doğduğu için
her turda yeni bir `PlayerInputSource` geliyor ve Inspector'daki değer
varsayılana dönüyordu. Kalıcı yer `PlayerProfile`; bileşen `Awake`'te oradan
okuyor. Ayarlar ekranı ayrıca sahada duran kaynağa da anında uyguluyor ki
oyuncu sonucu görmek için yeniden doğmayı beklemesin.

**Tuş atama** (`KeyBindings` + `KeyBindingPanel`):

- Tuş başka bir eylemde kullanılıyorsa **ikisi yer değiştiriyor.** Diğerini
  boşa düşürmek daha basitti ama oyuncuyu tuşsuz bir eylemle bırakıyor ve
  bunu ancak oyunun ortasında fark ediyor.
- Yakalama bütün `KeyCode` değerlerini tarıyor. Eski Input Manager'da "hangi
  tuşa basıldı" diye soran bir API yok; `Event.current` var ama `OnGUI`
  gerektiriyor ve projede IMGUI'den çıkılıyor (teknik borç 3). Tarama yalnızca
  dinleme sırasında çalışıyor.
- **Esc atanamıyor**, bilerek: dinlemeyi iptal eden tuş o, ayrıca menüyü açan
  tuş — yanlış atama oyuncuyu menüsüz bırakabilirdi. Sunucu test tuşları
  ([1]-[4]) de atanamıyor.
- Esc çakışması: tuş beklenirken menünün de aynı Esc'yle geri gitmesi tek
  basışta iki iş yapardı. `KeyBindingPanel.BlocksEscape` bunu engelliyor ve
  kare numarası tutuyor — `Update` sırası garanti olmadığı için hangi bileşen
  önce çalışırsa çalışsın sonuç aynı.
- **Terminal mini oyunu da atamaları kullanıyor**, sabit WASD'yi değil
  (bölüm 11.3). Tuşlarını değiştiren oyuncu için sınav aksi hâlde oynanamaz
  hâle gelirdi.

`KeyCode` fare düğmelerini de kapsadığı için (`Mouse0`, `Mouse1`…) saldırı da
aynı sistemden atanıyor; ayrı bir "fare mi tuş mu" ayrımı gerekmedi.

### Test tuşları duruyor

Sunucu penceresindeki [1]-[4] kaldırılmadı: tek başına test ederken lobi
kurmadan hızlıca tur başlatmak hâlâ işe yarıyor. Özellikle **[2]** (kaçan
olarak başlat) lobiden yapılamıyor — canavarı seçebiliyorsun ama "beni canavar
YAPMA" diyemiyorsun.

### Kurulum sırası

Menü, `Yakalamaca > Menü Kur` ile kuruluyor ve **Ağ Kurulumu'ndan sonra**
çalıştırılmalı: menü Mirror'ın test HUD'ını kaldırıyor ve ağ kurulumunun
kapattığı menü objesini geri açıyor.

**EOS Kurulumu'ndan da sonra olmalı.** Katılma ekranındaki oda listesi lobi
servisini `NetworkManager`'da **arayarak** bağlıyor; bileşen henüz yoksa alan
boş kalıyor ve liste hiç görünmüyor (oyun çalışmaya devam ediyor, yalnızca
kodla). Doğru sıra: `Ağ Kurulumu` → `EOS Kurulumu` → `Menü Kur`.

**Ağ Kurulumu'nu tekrar çalıştırırsan Menü Kur'u da tekrar çalıştır.**

---

## 14. Canavar: model, animasyon ve saldırı

`Yakalamaca > Canavar Modelini Kur` her şeyi kuruyor. Elle yapılırsa on beş
Inspector alanı doldurmak gerekiyor ve `Ağ Kurulumu` prefabı sıfırdan
kurduğu için hepsi bir sonraki çalıştırmada uçuyor.

**Model:** `RamsterZ_FreeDoll` (Asset Store, ücretsiz). Rig zaten Humanoid ve
hatasız geliyor — Mixamo'ya rig için yüklemeye gerek yok, animasyonlar
`Copy From Other Avatar` ile retarget ediliyor.

**Kaçanın kendi modeli var** (Banana Man, bölüm 17). Burada uzun süre "kaçan
hâlâ kapsül" yazıyordu; model 2026-08-30'da bağlandı ve bu satır güncellenmeden
kaldı. Kapsül yalnızca **yer tutucu** olarak duruyor: `Kaçan Modelini Kur` hiç
çalıştırılmamış bir projede kimse görünmez olmasın diye.

`PlayerBodyVisual` rol değişince gövdeyi değiştiriyor ve üçüncü bir gövde
eklemek çağıranların hiçbirini değiştirmiyor.

### Aracın çözdüğü üç sessiz tuzak

**Materyaller URP için yazılmış**, projede URP yok, shader referansları
çözülmüyor ve model **pembe** görünüyor. Shader'ı `Standard` yapmak gerekiyor.
Klasördeki `Floor`/`Walls`/`Skybox` demo materyallerine dokunulmuyor — biri
**materyal varyantı** ve varyantın shader'ı ebeveyninden geliyor, yazmaya
çalışmak Unity'ye hata bastırıyor.

> **Düzeltme (2026-08-30).** Burada uzun süre "Built-in slotları zaten dolu,
> shader'ı çevirmek yetiyor" yazıyordu. **Yanlıştı** ve canavarın gri
> görünmesinin sebebi buydu: yalnızca **adı iki tarafta da aynı olan** slotlar
> doluydu (`_BumpMap`, `_MetallicGlossMap`, `_OcclusionMap`). Albedo URP'de
> `_BaseMap`, Standard'da `_MainTex` — shader dönüşümü onu taşımıyor ve
> `_MainTex` boş kalıyor.
>
> Model aydınlatmaya tepki verdiği için sorun "eksik doku" gibi görünmüyordu,
> sadece renksizdi. Araç artık `_BaseMap`'i `_MainTex`'e kopyalıyor
> (`ModelMaterialFix`). Kopyalama **serileştirilmiş veriden** okuyor:
> `material.GetTexture("_BaseMap")` çalışmıyor, çünkü `HasProperty` mevcut
> shader'a bakıyor ve Standard'da öyle bir slot yok — ama doku `.mat` dosyasında
> hâlâ duruyor.
>
> Ayrıca eski kod shader zaten Standard olduğunda döngünün **en başında**
> çıkıyordu, yani ikinci çalıştırmada hiçbir kontrol yapılmıyordu.
>
> **Ve bunu düzeltmek de yetmedi.** Dokular `.mat` dosyalarında düzeldiği hâlde
> canavar gri kaldı: model o `.mat` dosyalarını **hiç kullanmıyordu**. FBX
> materyal yuvası boştu (`externalObjects: {}`) ve model kendi ürettiği gri
> materyali çiziyordu — `Art/Materials` altındaki dosyalar kenarda duruyordu.
> Kaçandaki (bölüm 17) sorunun aynısı, iki modelde birden.
>
> Ders: **materyali düzeltmeden önce modelin o materyali kullandığını doğrula.**
> Araç artık ikisini de yapıyor ve eşleşmeyen yuva kalırsa adını konsola yazıyor.
>
> **Ada göre otomatik eşleşme de yetmedi.** FBX'in yuva adlarıyla `.mat` dosya
> adları hiç tutmuyor, o yüzden `MonsterSetup.MaterialSlots` tablosu var:
>
> | FBX yuvası | Dosya |
> |---|---|
> | `Unity_KillerDoll_Body` | `KillerDollBodyPaintedWood.mat` |
> | `Unity_KillerDoll_Head` | `KillerDollHeadPaintedWood.mat` |
> | `Unity_KillerDoll_Eyes` | `KillerDollEyesRed.mat` |
>
> Yuva adları FBX'in içinden okundu. Tablo, `AddRemap` yalnızca yuva adını
> istediği için modelin alt varlık taraması boş dönse bile çalışıyor.
>
> Gözler kırmızı seçildi; `KillerDollEyesGrey` de klasörde duruyor.

**Dokular 4K iniyor** (tek normal map 27 MB). Karanlık koridorda 3 metreden
görünen bir karakter için israf; 1024'e indiriliyor.

**Yakalama klibi 6 saniye.** Kovalamacada çok uzun. `KillClipSeconds` = 2.6
ile kare aralığından kırpılıyor — Mixamo'ya dönüp yeniden indirmeye gerek
kalmıyor. Kare hızı dosyadan okunuyor, 30 varsayılmıyor.

### Animasyon durumu ağdan gönderilmiyor

Hız, **iki kare arasındaki pozisyon farkından** çıkarılıyor
(`MonsterAnimator`). Herkes zaten karşı oyuncunun pozisyonunu görüyor
(NetworkTransform); "şu an koşuyorum" mesajı yollamak aynı bilgiyi ikinci kez
göndermek olurdu. Ek trafik sıfır ve aynı kod hem yerel hem uzak oyuncuda
çalışıyor — uzakta `PlayerController` kapalı olduğu için ondan hız zaten
okunamazdı.

Eğilme pozisyondan çıkarılamıyor, o yüzden `PlayerController.DuckFraction`'dan
okunuyor: `PlayerPoseSync` onu zaten senkronluyor.

> **Düzeltme (2026-08-31).** Bu uzun süre **çalışmıyordu.** `PlayerPoseSync`
> değeri taşıyıp `ApplyDuckGeometry` ile uyguluyordu, ama o metot yalnızca
> `controller.height`/`center` yazıyor, `duckFraction` **alanını** yazmıyordu.
> Alan sadece `Update`'te yazılıyor ve uzak oyuncuda `PlayerController` kapalı
> — yani `DuckFraction` uzakta sonsuza kadar 0 kalıyordu.
>
> Kapsül gövde doğru çalıştığı için sorun görünmüyordu: o `height`'a bakıyor,
> animatör ise `DuckFraction`'a. Model gelene kadar kimse fark etmedi.
>
> `ApplyDuckGeometry` artık alanı da yazıyor.

### Saldırı: üç aşama, dallanmayı sunucu belirliyor

```
Sol tık → ATILMA (attack 15-45, 1.0 sn)
            ├─ isabet yoksa → KALKMA (ıskalama, 1.6 sn) → yürüyüş
            └─ RpcHit gelirse → YAKALAMA (kill 0-78, 2.6 sn) → yürüyüş
```

**Yakalama yalnızca `RpcHit`'ten tetikleniyor**, yani sunucu menzil/koni/görüş
hattını doğruladıysa. Iskalarsan hiç çağrılmıyor.

**Atılma girdiden, anında** — ağ turunu beklemek kendi saldırının gecikmeli
hissetmesi demekti (bölüm 4). **Karşı taraf aynı animasyonu `RpcSwing` ile
görüyor.**

> **Düzeltme (2026-08-31).** `RpcSwing` uzun süre yalnızca sesi çalıp durumu
> kuruyordu, animasyonu oynatmıyordu: atılmayı **sadece saldıran** görüyordu,
> karşı taraf canavarı hiç saldırmadan koşarken görüyordu. Yakalama (`RpcHit`)
> baştan doğruydu, eksik olan atılmaydı. Ayrıca `ReleaseSwing` içinde
> `PlayAttack()` yanlışlıkla iki kez çağrılıyordu; o da temizlendi.

Atılma 1 saniye çünkü isabet cevabını beklemesi gerekiyor: LAN'da ~30 ms ama
internette 150 ms sürebiliyor. Kısa olsaydı cevap gelmeden kalkma başlar,
sonra birden yakalamaya atlardı.

**Atılma → kalkma geçişi normalden uzun (0.25 sn).** İki klip ayrı
dosyalardan geliyor ve kesim noktasındaki pozlar birebir tutmuyor; uzun
karışım o farkı yutuyor.

### Kliplerin kırpılması: iki ders

Kırpma `MonsterSetup.TrimFrames` tablosunda, **kare aralığı** olarak. Sayılar
Inspector'daki `Start`/`End` ile birebir aynı (klipler 30 FPS).

```
attack → 15 - 45    (atılış; öncesi hazırlık, sonrası yakalama+yumruk)
kill   → 0 - 78     (≈2.6 sn; ham klip 6 sn ve sonu tekrar tekrar yumruk)
```

**Ders 1: doğru aralık gözle bulunur, hesapla değil.** İlk denemede atılış
olarak `attack`in ilk 0.55 saniyesi alındı ve bozuktu — o dilim atılış değil,
hazırlıktı. İkinci denemede klip tamamen çıkarıldı. Doğrusu ancak klip
Unity'de izlenince bulundu (15-45).

**Ders 2: kırpma kalıcıdır, geri alınmaz.** Kırpma FBX'in import ayarına
yazılıyor; tablodan çıkarmak eski hâlini geri getirmiyordu. Araç artık kare
aralığını **her çalıştırmada dosyanın tam aralığından başlatıp** tablodakini
uyguluyor, yani kendi kendini onarıyor.

**Inspector'da elle kesme kalıcı değil** — araç bir sonraki çalıştırmasında
sıfırlar. Aralığı görmek için Inspector kullanılır, saklamak için tablo.

### Saldırı sırasında hareket kilitli

Her iki animasyon boyunca canavar hareket edemiyor. Terminalin kullandığı odak
mekanizması (`BeginFocus`) kullanılıyor: girdi kesiliyor, bakış dar bir koniye
sıkışıyor, hareket kodunun sürtünme/ivme akışına hiç dokunulmuyor.

**Bakış da kilitli: `lockYawLimit` = 0.** Saldırı ve yakalama boyunca kamera
sağa-sola hiç dönmüyor. Önceden 45 derecelik bir koni vardı; iki sorunu birden
üretiyordu — animasyon zaten hareketliyken kameranın da dönmesi görüntüyü
sallıyor, ve savurduktan SONRA nişan düzeltmeye izin veriyordu. Kapalıyken
savurmak bir taahhüt: kaçanın keskin dönüşü gerçek bir savunmaya dönüşüyor
(bölüm 1'deki "labirent canavarın rakibi" fikrinin aynısı).

Yukarı-aşağı sınır duruyor (`lockPitchLimit` = 25): şikâyet edilen sağa-sola
dönmeydi ve iki ekseni birden çivilemek gereksiz.

**Değeri `Canavar Modelini Kur` yazıyor.** Alan prefabta serileştirilmiş, yani
koddaki varsayılanı değiştirmek tek başına hiçbir şey yapmıyor — bölüm 16'daki
tuzağın aynısı.

**Momentum davranışı ikisinde farklı:**

- **Saldırıda momentum KESİLMİYOR** (`stopMomentum: false`). Atılmanın kendisi
  bir hız itmesi; kesersek canavar olduğu yerde çırpınır. Yerden kalkarken
  yürüyebilmek görüntüyü bozuyordu, kilit onu düzeltiyor.
- **Yakalamada kesiliyor.** Kurbanın üstünde duruyor.

**Yakalamadaki 2.6 saniye bilinçli bir takas** (Dead by Daylight'ın "mori"si
gibi): canavar tek kişiyle meşgulken diğer kaçanlara bedava bir pencere
açılıyor. Süreler klipten **ölçülüyor** — animasyonu değiştirip aracı tekrar
çalıştırınca kilit süreleri de kendiliğinden güncelleniyor.

Kilit sayacı `Update`'in en başında işliyor: tur biterse ya da canavar
elenirse aşağıdaki erken çıkışlar devreye giriyor ve kilit sonsuza kadar
kalırdı.

### Yakalarken kamera geriye çekiliyor

Birinci şahısta canavar **kendi öldürme animasyonunu göremiyordu.** Yakalama
kilidi boyunca kamera geriye çekilip sonunda yumuşakça geri geliyor
(`MonsterAttack.killCameraPullBack`, 0.7 m): hızlı gir, tut, yavaş çık. 0
yapılırsa kapanıyor.

Değer `PlayerController.CameraForwardOffset` üzerinden geçiyor — eğilme payıyla
aynı kanal. `CameraBob` kameranın yerel z'sini MUTLAK yazdığı için tek kaynak
şart; ayrı bir yazıcı eklemek bölüm 1'deki tuzağı tekrar üretirdi.

### Bıçak kaldırıldı

Animasyonlar elle saldırıyor. Bıçak hem gereksiz hem de saldırı animasyonunun
içinden geçiyordu. `MonsterAttack` bıçağa her yerde null kontrolüyle dokunuyor
ve vuruş kararı zaten menzile bakıyor, o yüzden prefabtan silmek hiçbir şeyi
bozmuyor.

### Birinci şahıs gövde

Canavar kendi gövdesini **görüyor** — aşağı bakınca ellerini ve bacaklarını
görmek, saldırı animasyonunu hissettiren şey.

**Kafa gizleniyor:** `headBone.localScale = 0`. Renderer kapatılmıyor çünkü
gövde tek bir skinned mesh, kafayı ayrı kapatmanın yolu yok. Kemik ölçeği
**senkronlanmıyor** (NetworkTransform yalnızca kökü taşıyor), yani bu yalnızca
kendi ekranını etkiliyor — karşıdakiler seni kafanla görüyor.

> **Kafayı sıfırlamak tek başına yetmedi: BOYUN da gizleniyor** (2026-09-05).
> Canavar koşarken kendi kafasının içini görüyordu.
>
> Sebep sıfırlanan kemiğin kendisi değil **komşusu.** Boyun ile kafa arasında
> ağırlığı paylaşan vertex'ler var; kafa bir noktaya çökünce o vertex'ler
> boyundan o noktaya doğru uzun ince üçgenlere dönüşüyor. Koşu animasyonu
> gövdeyi öne eğdiğinde bu huni kameranın önünden geçiyor ve arka yüzleri
> görünüyor — "kafamın içi" denen şey o.
>
> Boyun da sıfırlanınca huninin ucu omuz hizasına, kameradan belirgin şekilde
> uzağa iniyor. Bedeli birinci şahısta omuz/yakanın hafif deforme olması —
> yalnızca kendi ekranında.
>
> **Boyun kemiği prefabta alan DEĞİL**, çalışma anında `Animator`'dan
> çözülüyor (`HumanBodyBones.Neck`). Alan eklemek prefabı yeniden kurmayı, o da
> canavar/kaçan modellerini yeniden kurmayı gerektirirdi (bölüm 7'deki sıra).
> Aynı desen `MonsterAura` ve `Terminal`'de de var. Kemiğe adıyla değil
> **rolüyle** ulaşılıyor: model değişirse kemik adı değişir, `Neck` değişmez.

Kaçanın kapsülü birinci şahısta gizli kalıyor: suratının önünde duran bir
kapsül kimseye bir şey anlatmıyor.

### Kafa bakış yönüne dönüyor

Animator'ın kendi bakış IK'sı (`OnAnimatorIK` + `SetLookAtPosition`).
Animasyonun üstüne yazmıyor, **karıştırıyor** — elle kemik döndürmek
animasyonu ezerdi ve koşarken kafa gövdeden kopuk dururdu.

Çalışması için katmanın **IK Pass'i açık** olmalı; kurulum aracı açıyor.
Kapalıyken `OnAnimatorIK` hiç çağrılmıyor ve sessizce hiçbir şey olmuyor.

Gövde payı kasten çok düşük (0.15): dönmesi gereken kafa. Yüksek değer
karakteri belden büküyor ve koşu animasyonunu bozuyor.

**Aşağı bakış 55 derecede sınırlı** (`MovementProfile.maxLookDownAngle`). Tam
dibe bakınca kafa gövdenin içine giriyordu.

> **Kaçanda da sınır var artık: 70°** (2026-09-05). Burada uzun süre "kaçanda
> sınır yok (89) — onun kapsülünde dönecek kafa yok" yazıyordu. O gerekçe
> 2026-08-30'da Banana Man bağlanınca **düştü** ama sayı kalmıştı: kaçan tam
> dibe bakınca kendi gövdesinin içini görüyordu.
>
> Canavarınki kadar dar değil, çünkü kaçanın kafası bakış yönüne dönmüyor —
> sorun yalnızca en alttaki birkaç derece.
>
> Ders: **bir sayının gerekçesi düştüğünde sayıyı da gözden geçir.** Model
> bağlanırken bakış sınırı kimsenin aklına gelmedi ve arada iki hafta geçti.

### Kamera canavarda yukarı ve ileri alınıyor

Canavarın modeli hull'un **1.18 katı** (bölüm 17). Kamera ise hull'un göz
hizasında ve ekseninde: yani modelin göğüs hizasına denk geliyor ve oynayınca
"kamera gövdenin içinde" gibi duruyor.

| Alan | Canavar | Kaçan |
|---|---|---|
| `eyeHeightOffset` | **+6 unit** (0.114 m) | 0 |
| `eyeForwardOffset` | **+0.10 m** | 0 |

Kaçanda ikisi de sıfır: Banana Man hull'la aynı ölçekte, telafi edilecek bir şey
yok.

**İleri payının duvarla sorunu yok** — `UpdateCameraClearance` onu yüzeye
çarptırıyor (bölüm 5). Zaten aynı kanaldan geçiyor: eğilme payı, yakalama geri
çekmesi ve bu, hepsi `RawCameraForward`'da toplanıyor.

**Yukarı payının sınırını TAVAN belirliyor, duvar değil.**

> Burada önce yanlış bir gerekçe yazmıştım: "kapsülün üst yarım küresi 1.067
> m'de başlıyor, yani kamera yükseldikçe yatay açıklık daralıyor." **Düşey bir
> duvar için yanlış.** Duvar bir düzlem ve kapsüle **en geniş yerinden**
> değiyor; yani duvar düzlemi eksenden hep `yarıçap − skinWidth ≈ 0.274 m`
> uzakta, kameranın yüksekliğinden bağımsız olarak.
>
> Yukarı çıkarken daralan şey kapsülün **tepesine** kalan pay. 6 unit'te kamera
> kapsül tepesinin 3.8 cm altında; yakın düzlemin üst kenarı (0.046 m) o payı
> biraz aşıyor, yani kafasını tavana dayamış bir canavar yukarı bakarsa tavanın
> içini görebilir. Bu haritada tavan 3 m olduğu için gerçekleşmiyor — **alçak
> bir geçit eklenirse burası gözden geçirilmeli.**
>
> Ders: bir kısıtın gerekçesini yazarken **hangi geometrinin sınırladığını**
> doğrula. Yanlış gerekçe, sayıyı ilerde yanlış yerden ayarlatır.

Pay yalnızca **ayaktaki** hizaya biniyor; eğilme geçidi 1.1 m ve orada kamerayı
yukarı almak tavanın içini gösterirdi.

### Ayak kayması

Canavar 11.43 m/s'ye çıkıyor (600 u/s), Mixamo koşusu ~4 m/s ilerliyor. Oynatma hızı
orantılanıyor ama **sınırlı** (0.7–1.6): tam orantı bacakları gülünç şekilde
çırpıyor. Biraz kayma, çok hızlı animasyondan iyi.

### Bilinen eksikler

- ~~Kurbanın animasyonu yok.~~ **Çözüldü** (bölüm 17): beden ölüm klibi bitene
  kadar sahnede kalıyor ve canavarın önüne oturtuluyor.
- ~~Havada olma animasyonu yok.~~ **KAPSAM DIŞI** (2026-09-06). Canavar
  zıplayamıyor ve haritada düşülecek yüksek bir yer yok, yani o durum hiç
  oluşmuyor. Haritaya yükseklik eklenirse geri gelir. (Kaçanda var —
  bölüm 17.)
- ~~Kaçan modeli yok.~~ **Çözüldü** (bölüm 17).

### Unity'nin derlemeyi atlaması

Bu bölümün kodu yazılırken üç kez "menü görünmüyor / eski davranış sürüyor"
yaşandı. Sebebi hep aynıydı: **Unity kaynak dosyaları değişmiş olmasına rağmen
derlememişti.**

Teşhis, tahmin etmeden:

```
Library/ScriptAssemblies/Assembly-CSharp-Editor.dll  zamanı
Assets/_Scripts/.../DegisenDosya.cs                  zamanı
```

DLL kaynaktan eskiyse Unity derlememiş demektir. Çözüm: Unity'ye odak vermek,
`Ctrl+R`, ya da `Edit > Preferences > Asset Pipeline > Auto Refresh` açık mı
diye bakmak. Kapalıyken dışarıdan yapılan hiçbir değişiklik görünmüyor.

---

## 15. Kapılar ve düğmeler

### Düğme kilitlenmiyor, kapı son basana uyuyor

Kapı hareket hâlindeyken de düğmeye basılabiliyor. Eskiden kilitliydi;
gerekçe "yarı yolda yön değiştiren kapı zıplıyor gibi görünüyor"du ve o
gerekçe **geçersizdi**: animasyon hedefe `MoveTowards` ile gidiyor, yani yön
değişince kapı bulunduğu yerden devam ediyor, hiçbir yere sıçramıyor. Kilit
ise kovalamacanın tam ortasında düğmeyi ölü bir nesneye çeviriyordu.

**Yön değişiminde 0.3 saniye duraksama** (`reverseDelay`): kapı olduğu yerde
duruyor, sonra ters yöne gidiyor. Yalnızca hareket hâlindeyken basılırsa —
kapalı kapıya basınca beklemeden açılıyor.

Duraksama **ağ zamanıyla** yönetiliyor (`holdUntil` SyncVar), yerel sayaçla
değil. Yerel olsaydı kapı istemcilerde farklı yerlerde durur ve çarpışma
ayrışırdı: kapının altından kayarak geçmek bir oyuncuda olur, diğerinde
olmazdı.

**Hız son basana göre.** `moveTime` her tetiklemede yeniden hesaplanıyor:
canavar yarı yolda müdahale ederse kapı oradan itibaren canavar hızında
(1.9 sn) gidiyor, kaçan basarsa 1.1 sn'ye dönüyor.

**Ses duraksamayı bekliyor.** Önceden `isOpen` değişir değişmez çalıyordu;
kapı dururken hareket sesi duymak yanlıştı.

### Cooldown kişi başı ve sunucuda

`SlidingDoor.perUserCooldown` = 0.5 sn. **Başkasının basması seni
bekletmiyor** — iki oyuncu kapı başında çekişebiliyor, o bilinçli.

Kontrol **sunucuda**: kapı `netId → tekrar basabileceği ağ zamanı` tutuyor.
Eski cooldown yalnızca düğmenin üstündeydi ve sunucu hiç bakmıyordu, yani bir
kural değil nezaketti — değiştirilmiş bir istemci düğmeyi tıkırdatarak kapıyı
**yerinde dondurabilirdi** (her basış duraksamayı uzatıyor).

Düğmenin kendi yerel beklemesi süreyi **hedefinden okuyor**
(`Triggerable.UserCooldown`). İki sayı ayrı ayrı ayarlansaydı sahnedeki
düğmede eski kısa değer kalır, oyuncu basar, sunucu reddeder ve ekranda
hiçbir şey olmazdı — sebebi görünmeyen bir sessizlik.

### Basma animasyonu ağda

Karşı oyuncu artık düğmenin içeri girdiğini görüyor. **Düğmeye NetworkIdentity
eklenmedi**: haritada 10 düğme var ve hepsi tamamen görsel, hiçbir karar
taşımıyor. Onun yerine zaten ağda olan **kapı** haber veriyor —
`Triggerable.Activated` olayı + `ClientRpc`.

Basanın kendi geri bildirimi yine anında (ağ turunu beklemiyor), karşı taraf
birazdan kapıdan gelen haberle görüyor. Bölüm 4'ün kuralı: his istemcide,
karar sunucuda.

Otomatik kapanma bu yoldan geçmiyor — kapı kendi kapandığında düğme basılmış
görünmemeli.

**Hangi düğmeye basıldığı taşınıyor.** Eskiden kapının "çalıştım" haberinde
kimlik yoktu ve iki düğme de aynı kapıya abone olduğu için **ikisi birden**
içeri giriyordu. CLAUDE.md bunu "bilinçli bırakıldı, aynı anda görmek zor"
diye geçiştiriyordu; oynandığında ilk göze batan şeylerden biri oldu.

Kimlik olarak düğmenin **kardeş sırası** (`transform.GetSiblingIndex()`)
taşınıyor. Sahne dosyasından geldiği için her istemcide aynı ve kimsenin elle
numara vermesi gerekmiyor — düğmeye hâlâ NetworkIdentity eklenmiyor.

Varsayım: aynı hedefe bağlı düğmeler aynı ebeveyn altında farklı sıralarda
duruyor. Labirentte ikisi de kapının kökünün altında (`Panel`, `Dugme_-1`,
`Dugme_1`), yani tutuyor.

Düğmesiz doğrudan kullanımda (`allowDirectUse`) `Triggerable.DirectUseSource`
(-1) gönderiliyor: hiçbir düğmenin kardeş sırası negatif olmadığı için hiçbiri
basılmış görünmüyor.

---

## 16. Katman düzeni

Teknik borç 3'ün karşılığı. Projede her nesne `Default` katmanındaydı ve bütün
`LayerMask` alanları `~0` ("her şeye bak") duruyordu — yani ışın atan sistemler
hiçbir şey elemiyordu.

### Neyi düzeltiyor

**Varil kurşun geçirmez kalkandı.** Canavarın vuruş ışını yerden **90 cm**'den
yatay gidiyor (`MonsterAttack.hitHeight`), menzil 2.3 m. `Harita Süsle`
varilleri **duvar diplerine** dağıtıyor ve collider'larını koruyor
(`keepColliders = true`). Köşeye sıkışan kaçanın önündeki varil ışını kesiyor,
sunucu vuruşu reddediyor, canavarın ekranında ıskalama animasyonu oynuyor ve
**sebebi hiçbir yerde görünmüyor**. Üstelik sıkışan kaçanın gittiği yer tam da
prop'ların olduğu yer.

**İzleyici kamerası oyunculara takılıyordu.** Duvar kaçınma ışını diğer
oyuncuların kapsüllerine de çarpıyor; biri arkadan geçtiğinde kamera öne
zıplıyordu.

### Dört katman

| Katman | Ne girer | Işını keser mi |
|---|---|---|
| `Harita` | Zemin, duvar, tavan, kapı, çıkış | **Evet** |
| `Sus` | Varil, kasa, giydirme | Hayır — gövdeyi durdurur, görüşü kesmez |
| `Oyuncu` | Kaçan, canavar, TestBot | Hayır |
| `Etkilesim` | Düğme, terminal | Yalnızca nişan ışınına |

### Maskeler

| Alan | Eskiden | Şimdi |
|---|---|---|
| `MonsterAttack.obstacleMask` | `~0` | `Harita` |
| `SpectatorController.obstacleMask` | `~0` | `Harita` |
| `PlayerInteractor.interactMask` | `~0` | `Harita + Etkilesim` |
| `PlayerController.groundMask` | `~0` | **`~0` — dokunulmadı** |

`groundMask` bilerek dışarıda: bozuk değil, ve aynı maske iki yerde kullanılıyor
— zemin kontrolü **ve** eğilmeden kalkarken tepede yer var mı sınaması
(`CanStandUp`). Daraltmak, kutunun içinde kalkabilmek gibi sessiz hatalar
üretebilirdi. Kazancı olmayan bir risk.

### Çarpışma matrisine dokunulmuyor

Katmanlar burada **yalnızca ışın filtresi**. Physics ayarlarındaki çarpışma
matrisi elden geçmedi, yani her şey eskisi gibi her şeyle çarpışıyor. Hareketin
hissi bu değişiklikten hiç etkilenmiyor — matrisi kurcalamak, Source hareketini
sessizce bozabilecek tek adımdı.

### Kapı `Etkilesim` değil `Harita`

`SlidingDoor` de `IInteractable` ama katman **bileşene göre değil işleve göre**
seçiliyor: kapalı kapının arkasından vurulmamalı. Kapı açılınca panel tavana
çekildiği için 90 cm'deki vuruş ışınını zaten kesmiyor, ayrı bir kural
gerekmedi.

Aynı sebeple `Cikis_Gecidi/Engel` (canavarı çıkıştan geçirmeyen engel) de
`Harita`: bugün de vuruş ışınını kesiyor ve katman düzeni **davranışı
değiştirmemeli**.

### Katmanı kim atıyor

İki yol birden, çünkü ikisi de tek başına yetmiyor:

1. **Kurulum araçları** ürettikleri şeye katmanı anında atıyor
   (`LayerSetup.Apply`). Böylece `Labirent Harita Kur`, `Ağ Kurulumu` veya
   `Canavar Modelini Kur` tekrar çalıştırıldığında katman geri gitmiyor.
2. **`Yakalamaca > Katmanları Kur`** sahnenin tamamını gezip bileşenlere göre
   katman atıyor ve maskeleri yeniden yazıyor.

İkincisi olmadan mevcut sahne düzelmezdi (haritayı yeniden kurmak gerekirdi);
birincisi olmadan bir sonraki kurulumda her şey `Default`a dönerdi.

**Araç hiçbir şey silmiyor** — yalnızca katman ve maske alanlarına yazıyor.
Haritayı elle düzenlemeye başladıktan sonra da güvenle çalıştırılabilir.

### Koddaki varsayılanı değiştirmek yetmiyor

`MovementProfile` tuzağının aynısı (bölüm 1): `= ~0` alan başlatıcısı yalnızca
**yeni eklenen** bileşende çalışıyor, sahnede ve prefabta duran bileşenler eski
değeri serileştirilmiş hâlde taşıyor. O yüzden araç maskeleri hem sahnedeki
kopyalara hem **oyuncu prefabına** yazıyor — asıl oyuncu ağdan doğduğu için
prefabtaki değer olan bitiyor.

Alan başlatıcıları `~0` olarak bırakıldı (elle eklenen bir bileşen bugünkü
davranışla başlasın diye), ama üçünün de Tooltip'i aracı işaret ediyor.

### Katman tanımlı değilse

`LayerSetup.Apply` sessizce hiçbir şey yapmıyor, `LayerSetup.Mask` ise **uyarı
yazıyor**. Fark bilinçli: atanmamış katman yalnızca "eski hâli" demek, ama eksik
katmandan kurulan maske `0` olur — yani "hiçbir şey ışını kesmiyor" — ve canavar
duvarın arkasından vurmaya başlar. Sessiz kalması en kötü sonucu verecek yer
orası.

### Bu geçişte bulunan, düzeltilmeyen iki şey

1. **`Cikis_Gecidi/Engel` host ile adanmış sunucuda farklı davranıyor.** Engel
   yalnızca canavarın istemcisinde açık (`ExitGate.UpdateBlocker`), ama isabet
   kararı sunucuda veriliyor. Host'ta canavar oynuyorsa engel açık ve vuruş
   ışınını kesiyor; adanmış sunucuda `NetworkClient.localPlayer` null olduğu
   için engel kapalı ve canavar çıkışın öbür yanındaki kaçanı vurabiliyor.
   Doğru olan host davranışı (bölüm 11.5: canavar geçemez).
2. **Kapıdan vuruş.** Işın hedefin kapsülüne girmeden `bodyRadius` (0.4 m) kadar
   kısaltılıyor. Kapı 0.2 m kalınlığında, yani iki oyuncu da kapıya 0.3 m
   mesafede dururken ışın kapıya varmadan kesiliyor ve isabet sayılıyor. Labirent
   duvarları 3.2 m kalınlığında olduğu için orada mümkün değil.

İkisi de bu geçişten önce de vardı; katman düzeni ikisini de değiştirmiyor.
Düzeltmek isabet geometrisine dokunmak demek, o da oynanarak ayarlanacak
(bölüm 10, madde 7).

---

## 17. Kaçan: model, animasyon ve yakalanma

`Yakalamaca > Kaçan Modelini Kur` her şeyi kuruyor — `Canavar Modelini Kur`'un
kaçan karşılığı. **Ağ Kurulumu'ndan SONRA** çalıştırılmalı, çünkü oyuncu
prefabını değiştiriyor ve o araç prefabı sıfırdan kuruyor.

**Model:** Banana Man (Banana Yellow Games). Rig zaten Humanoid ve hatasız,
materyalleri de **Built-in Standard** — canavardaki "URP shader projede yok,
model pembe" tuzağı burada hiç çıkmadı. Araç yine de shader kontrolü yapıyor:
model değiştirilirse aynı tuzak orada patlardı.

**Kapsül silinmedi.** `PlayerBodyVisual` model takılı değilse ona düşüyor, yani
`Kaçan Modelini Kur` hiç çalıştırılmamış bir projede kimse görünmez olmuyor.

### Kaynak avatar dosyaya göre seçiliyor

Bu bölümün en kolay gözden kaçan kısmı. Animasyon FBX'i **kendi iskeletini**
taşıyor ve `Copy From Other Avatar` o iskelete uyan bir avatar istiyor.
`Assets/_Art/Models/Kacan` altındaki klipler Mixamo'dan **KillerDoll** rigiyle
indirilmiş (`KillerDollUnity_BaseBody@...`), yani kaynak avatar **canavarınki**
olmalı — Banana Man'inki verilirse kemikler tutmaz.

Buna rağmen klipler Banana Man'de oynuyor: humanoid klipler **kas uzayında**
saklanıyor, hangi avatarla import edildiklerinden bağımsız olarak her humanoid
iskelette çalışıyorlar.

Araç bu yüzden dosya adının `@` **öncesine** bakıp avatarı seçiyor. İleride
Banana Man rigiyle indirilmiş bir klip atılırsa o da doğru avatarla import
ediliyor, elle ayar gerekmiyor.

### Eksik klip hata değil

`Idle` bugün yok. Kaçanın hiç kımıldamadan durduğu hâl bu, boş bırakmak
karakteri T-poza düşürürdü — o yüzden **canavarın `Idle` klibi ödünç
alınıyor** (yukarıdaki kas uzayı kuralı sayesinde sorunsuz oynuyor). Kaçan
klasörüne kendi Idle klibi atıldığı anda araç ona geçiyor.

Diğer eksik klipler yalnızca o durumu boş bırakıyor ve konsola uyarı yazıyor.

### Animator yapısı

```
Locomotion       (Speed karışımı: idle → yürüme → koşma)
    ↕ Crouch eşiği (0.6 / 0.4 — tam eşikte titremeyi önlüyor)
CrouchLocomotion (Speed karışımı: eğik idle → eğik yürüme)

AnyState --Airborne--> Havada --(Airborne biter)--> Locomotion
AnyState --Death-----> Olum   (çıkışı YOK)
```

**Ölüm durumunun çıkışı bilerek yok.** Yeni tur başlayınca gövde kökü
`PlayerBodyVisual` tarafından kapatılıp açılıyor; Animator kapalı bir objede
yeniden aktifleşince varsayılan duruma dönüyor. Ayrı bir "dirildi" parametresi
eklemek aynı işi ikinci kez yapmak olurdu.

### Havada olma pozisyondan çıkarılıyor

Kaçan zıplayabiliyor (canavar zıplayamıyor, orada hiç gerekmedi).
`PlayerController.IsGrounded` var ama **uzak oyuncuda geçersiz**: orada bileşen
kapalı, `Update` çalışmıyor, değer donmuş kalıyor.

Ham eşik yetmedi: zıplamanın tepe noktasında dikey hız sıfırdan geçiyor ve
karakter bir kare yere basmış görünüyordu. Onun yerine küçük bir durum makinesi
var:

```
dikey hız > rise        → havada
dikey hız < -fall       → havada + düşüyor
düşüyor && |hız| < land → yere indi
```

Tepe noktası `düşüyor` henüz kurulmadığı için havada sayılıyor; kenardan
zıplamadan düşmek de yakalanıyor.

**Asıl filtre eşik değil, süre.** Basamağa çıkmak **tek karede** yarım metre
kaldırabiliyor; 60 FPS'te bu 20 m/s dikey hız demek, yani hiçbir hız eşiği onu
eleyemez — ufacık bir tümsekte zıplama animasyonuna girilmesinin sebebi buydu.
Bu yüzden havada olma `airborneGrace` (0.18 sn) kadar sürmeden animasyona
geçilmiyor: basamak bir kare sürüyor, gerçek zıplama neredeyse bir saniye.

> ### Bayrak SIKIŞIYORDU: dördüncü bir çıkış şartı eklendi (2026-09-05)
>
> Oyunda kaçanlar zıplamadıkları hâlde sürekli zıplama animasyonunda
> kalıyordu. Sebep yukarıdaki durum makinesinin **tek çıkışının `düşüyor`a
> bağlı** olmasıydı — ve `düşüyor` yalnızca hızlı düşüşte kuruluyor.
>
> Kasanın üstüne çıkmak, basamağa binmek, zıplayıp hemen bir yüzeye konmak:
> hepsi `havada`yı kuruyor ama `düşüyor`u hiç kurmuyor. O durumda bayrağı
> indirecek **hiçbir şart kalmıyordu** ve karakter yerde dururken sonsuza kadar
> havada sayılıyordu. Yere gömülmüyordu; sorun tamamen animasyon durumunda.
>
> Yeni şart düşüşe değil **durulmaya** bakıyor: dikey hız `settleTime` (0.12 sn)
> boyunca sıfıra yakın kaldıysa ayaklar yerdedir.
>
> **İki sınır arasına sıkışıyor ve ikisi de gerçek:**
> - Zıplamanın tepe noktası da sıfırdan geçiyor. Yerçekimi 600 u/s ve eşik
>   0.35 m/s'de orada ~0.06 saniye kalınıyor — `settleTime` bundan **uzun**
>   olmalı, yoksa her zıplamanın tepesinde animasyon bir an sönerdi.
> - `airborneGrace` 0.18 sn. `settleTime` bundan **kısa** olmalı ki basamak
>   kaynaklı sıkışma animasyona hiç yansımadan temizlensin.
>
> Sayaç kendini sıfırlıyor: kalkış (2 m/s) ve düşüş (3 m/s) eşikleri
> `settleSpeed`ten büyük olduğu için o karelerde sayaç zaten sıfırlanıyor.
> Ayrı bir sıfırlama satırı gerekmedi.

**İniş gecikmesiz.** Payı iki yöne de koymak, yere bastıktan sonra zıplama
pozunda kayan bir karakter demekti.

Eşikler ve pay Inspector'da.

Alternatif `PlayerPoseSync`'e bir bool eklemekti — tek bit ucuz, ama zaten
gönderilen pozisyondan çıkarılabilen bir bilgiyi ikinci kez göndermek olurdu
(bölüm 4).

### Ortak animatör tabanı

Hız, eğilme ve bakış IK kaçanla canavarda **birebir aynı iş**, o yüzden
`CharacterAnimatorBase`'e çekildi; `MonsterAnimator` ve `RunnerAnimator` ondan
türüyor. Bölüm 5'in kuralına uygun: soyutlama spekülatif değil, iki somut
kullanım var.

`MonsterAnimator`'ın **adı ve alan adları korundu** — değiştirilseydi prefabtaki
mevcut canavar bağlantıları kopardı ve `MonsterSetup` da yeniden yazılmak
zorunda kalırdı.

### Yakalanan yerde kalıyor

Eskiden kurban aynı karede gizleniyordu ve canavar havayı yumrukluyordu.

Artık `RoundParticipant` ölümde **hareketle görüntüyü ayırıyor**: hareket ve
etkileşim anında kapanıyor (ölen ölmüştür), ama beden ölüm klibi boyunca
sahnede kalıyor. Süre klipten **ölçülüyor** — animasyonu değiştirip aracı
tekrar çalıştırınca bekleme de güncelleniyor.

**Sayaç ağdan gelmiyor.** `alive` zaten SyncVar ve hook her istemcide
çalışıyor; herkes kendi sayacını başlatıyor. Ayrıca bir "şimdi öl" mesajı
yollamak aynı bilgiyi ikinci kez göndermek olurdu.

### Havada yatma: kök hareketi poza gömülüyor

İlk çalışan sürümde iki karakter de yatma pozunu oynatıyor ama **ayakta
durdukları yükseklikte kalıyordu** — havada yatıyor gibi.

Sebep: hareketi `PlayerController` verdiği için Animator'da
`applyRootMotion = false`, bu da kök hareketini **çıkarıp atıyor**. Yürüme ve
koşmada istediğimiz tam olarak bu. Ama yakalama/ölme çifti yere kök hareketiyle
iniyor; o hareket atılınca karakter aşağı hiç gelmiyor.

Çözüm klibin **dikey** kök hareketini "Bake Into Pose" yapmak
(`ClipRootMotion`): yer değiştirme kök hareketi olarak çıkarılmak yerine pozun
içinde kalıyor, `applyRootMotion` kapalı olsa bile karakter yere iniyor.
Yalnızca tek atımlık kliplerde — koşu döngüsüne uygulanırsa karakter kökten
uzaklaşarak süzülür.

**Yalnızca DİKEY gömülüyor.** İlk denemede yatay (XZ) ve dönüş de gömüldü ve
sonuç daha kötü oldu: canavarın atılışı kökten bağımsız olarak mesh'i taşıyordu,
canavar ileri uçup kurbandan ayrılıyordu. Sebep, elimizdeki kliplerin **gerçek
bir Mixamo çifti olmaması** — canavarınki ve kurbanınki ayrı ayrı indirilmiş,
ortak bir origin'e göre yazılmamış. Doğru kurulum: yatay ve dönüş kök hareketi
atılsın (ikisi de yerinde oynasın), yalnızca dikey poza gömülsün. İki kök zaten
`PlayerBodyVisual.ApplyDeathPose` ile aynı noktaya oturtuluyor.

XZ ve dönüş **açıkça kapatılıyor**, sadece atlanmıyor: kırpma gibi bu ayar da
import dosyasında kalıcı, yani önceki sürümün açtığını geri almak gerekiyor.

Dikeyde "Based Upon" **Original**: başka bir
karakteri birbirinden kaydırıyor.

Alan adları serileştirmede farklı görünüyor (`lockRootHeightY` →
`loopBlendPositionY`); meta dosyasından doğrulandı.

### Ölüm süresi canavardan geliyor

Kurbanın ölüm klibi ham hâlde canavarın yakalama klibinden uzundu: canavar
işini bitirip yürümeye başlarken kurban hâlâ yere düşüyordu. `Kaçan Modelini
Kur` artık klibi canavarın `kill` klibiyle **aynı süreye kırpıyor** ve
`deathHoldDuration`'ı da ona eşitliyor — iki sayı elle senkronlanmıyor.

Kırpma kare aralığına yazılıyor ve kalıcı, o yüzden aralık **her çalıştırmada
dosyanın tam aralığından** hesaplanıyor; yoksa her seferinde üst üste binerdi.
Aynı desen `MonsterSetup`'ta da var (bölüm 14).

**Ölen oyuncu üçüncü şahsa geçiyor**, o yüzden ölürken birinci şahıs görünümü
kapatılıyor: birinci şahıs için gizlenen kafa, izleyici kamerasından bakınca
kafasız bir ceset olarak görünürdü. Dirilince geri açılıyor.

### Ölüm klibi hızlandırılıyor, bekleme süresi hızlandırılmıyor

Ham klipte kurban yere geç düşüyordu: canavar çoktan yumruklamaya başlamışken
kaçan hâlâ havadaydı, arada bir saniyeye yakın fark vardı. `RunnerSetup.DeathSpeed`
(1.7) yalnızca `Olum` durumunun oynatma hızını artırıyor.

**Yalnızca ölüm klibine uygulanıyor.** Locomotion'ın hızı zaten karakterin
gerçek hızından hesaplanıyor; oraya sabit bir çarpan koymak ayak kaymasını
bozardı.

**`deathHoldDuration` bu çarpana BÖLÜNMÜYOR, bilerek.** Kurban yere daha erken
iniyor ama beden yine canavarın `kill` klibi bitene kadar sahnede duruyor;
aradaki farkta kurban yerde yatıyor, canavar yumruklamayı bitiriyor. Süreyi de
kısaltmak cesedi canavarın altından çekip alırdı.

Ayarlamak: `DeathSpeed`'i büyüt (daha erken düşer) ya da 1'e yaklaştır (daha
yumuşak), sonra `Kaçan Modelini Kur`.

### Kurban canavarla AYNI noktaya oturtuluyor

Beden öldüğü yerde kalsaydı yandan yakalanınca canavar bir yöne yumruk atarken
kurban başka yöne düşerdi. Öldüren canavarın `netId`'si kurbana taşınıyor
(`RoundParticipant.killerNetId`) ve beden canavarın transformuna oturtuluyor.

**İkisi aynı noktada ama ZIT yöne bakıyor.** Klip çifti böyle yapılmış: canavar
atlıyor, adamı düşürüyor, üstüne çıkıp yumrukluyor — tek bir koreografi ve iki
karakterin iç içe geçmesi gerekiyor.

İki deneme de yanlıştı ve ikisi de aynı sonucu verdi (karakterler ayrı duruyor):
önce 0.85 m offset kondu, sonra offset sıfırlanırken rotasyon da canavarınkiyle
aynı yapıldı. Doğrusu **offset 0 + zıt rotasyon**.

`deathForwardOffset` alanı duruyor: klipler değişirse ince ayar gerekebilir.
Ama araç onu her kurulumda 0'a çekiyor, yani elle değiştirirsen bir sonraki
`Kaçan Modelini Kur` geri alır.

**Taşınan gövde kökü, oyuncunun kökü DEĞİL.** Kök NetworkTransform ile taşınıyor
ve istemci otoriteli; başka bir istemciden yazmak tam da Source hissini bozacak
prediction kavgasını başlatırdı (bölüm 4). Gövde kökü ağda hiç yok, yani her
istemci canavarın zaten bildiği pozisyonundan **aynı sonucu kendi hesaplıyor** —
ek trafik tek bir netId.

**Yalnızca yatay düzlemde taşınıyor.** Y olduğu gibi bırakılıyor: kurbanın ayak
hizası zaten doğru, Y'yi de canavardan almak eğimli bir yerde bedeni zemine
gömerdi.

**`killerNetId`, `alive`den ÖNCE tanımlı.** Mirror aynı güncellemedeki
SyncVar'ları tanım sırasına göre çözüyor, yani ölüm hook'u çalıştığında öldüren
belli oluyor. Yine de tek karelik bir yeniden deneme var: nesne o an
çözülemezse bir kare sonra bir kez daha bakılıyor.

Öldüren bilinmiyorsa (test tuşuyla kendini elendirme) beden olduğu yerde
yatıyor — kural değil, `killerNetId` sıfır olduğu için doğal sonuç.

### Ölçek: canavardan farklı

Canavarda hull boyunun üstüne **1.30** çarpanı var (2026-09-06'da 1.18'den
büyütüldü) — kovalayan şeyin olduğundan büyük görünmesi istenen etki. Ekrandaki
boy 1.784 m; çarpışma kutusu 1.372 m'de kalıyor. Kaçanda çarpan **1**: canavar ona nişan alıyor,
görünen gövde çarpışma kutusuyla örtüşmeli. Şişirilmiş bir kaçan, isabet etmesi
gerekirken etmeyen vuruşlar üretirdi.

### Materyaller modele bağlı değildi

Banana Man ilk kurulumda **düz gri** çıktı. Materyal bozuk değildi:
`Body.mat`'in `_MainTex`'i doğru dokuyu (muz sarısı albedo) gösteriyordu.
Sorun **bağlantıdaydı** — modelin materyal yuvası o dosyaya bağlı değildi
(`externalObjects: {}` boş), yani model kendi ürettiği materyali kullanıyordu
ve `Body.mat` kenarda duruyordu. Hiçbir yerde hata yazmıyor, o yüzden
bulunması zor.

Araç artık bağlantıyı Unity'nin eşleme kurallarına bırakmıyor, `AddRemap` ile
**açıkça** kuruyor (`ModelMaterialFix.RemapExternalMaterials`): modelin
klasöründen başlayıp iki üst klasöre kadar aynı adda `.mat` arıyor.

Yuva listesi modelin **alt varlıklarından** okunuyor (`LoadAllAssetsAtPath`).
`ModelImporter`'ın materyal listesi veren bir özelliği **yok** — ilk denemede
`importer.sourceMaterials` yazılmıştı ve derleme hatası verdi.

Aynı yardımcı canavarın gri kalmasını da düzeltiyor — o farklı bir sorundu,
bkz. bölüm 14'teki düzeltme kutusu. İki model iki ayrı sebepten gri
görünüyordu, o yüzden ikisi ayrı ayrı ele alınıyor.

### Bilinen eksikler

- **Ölüm klibi kırpması canavarın `kill` klibine bağlı.** `Kaçan Modelini Kur`,
  ölüm klibini canavarın yakalama klibiyle aynı süreye kırpıyor ve
  `deathHoldDuration`'ı da ona eşitliyor. Canavarın klibi değişirse kaçan aracını
  da tekrar çalıştır, yoksa süreler ayrışır.
- **Test botu ölüm animasyonunu gösteremiyor.** Botta model yok: `TestBotSetup`
  ona yalnızca `CharacterController`, `RoundParticipant` ve `TestRunnerBot`
  takıyor, `PlayerBodyVisual` ve `RunnerAnimator` yok. `BeginDeathHold`
  animatör bulamayıp erken çıkıyor ve bot eskisi gibi anında kayboluyor.
  **Yakalama animasyonunu test etmek iki istemci gerektiriyor** (Editor'de Host,
  build'de Client). Botu donatmak mümkün ama bilerek yapılmadı — botun işi
  kadroyu doldurmak, kaçanı taklit etmek değil.
- **Havada olma klibi `Jumping`.** `Falling Idle` varsa araç onu tercih ediyor;
  gerçek bir döngü olduğu için uzun düşüşlerde daha doğru duruyor.

---

## 18. Çıkışın görünümü ve tetikleyicisi

Çıkış oyun içinde bozuk duruyordu: gedikte havada asılı duran çıplak sarı bir
kutu ve arkasında gökyüzü. Sebebi iki ayrı şeydi ve ikisi de sessizdi.

### 1. Kapı hiç giydirilmemişti

`Haritayı Giydir` labirent kapılarına `Wall BayDoor` kit gövdesi takıyor — ama
yalnızca `Harita/Kapilar` altını tarıyor. Çıkış kapısı `HedefSistemi` altında
**ve giydirmeden SONRA** kuruluyor (kurulum sırasında adım 3 vs adım 6), yani o
araç çalışırken ortada bile yok. Görünen şey, giydirilmemiş ham kutuydu.

Diğer kapılar düzgün göründüğü için sorun "çıkışa özel bir bozukluk" gibi
duruyordu; oysa aynı sistemin görmediği bir yerdi.

Çözüm: `ObjectiveSetup` kendi kapısını kendisi giydiriyor (`DressExitDoor`).
Ölçek collider'a taşınıyor (`MapDressWindow.UnscalePanel` ile aynı numara):
kutu 0.25 × 3 × 3.2 ölçekli ve o ölçek altındaki kit gövdesini ezerdi.

### 2. Çıkışın arkasında hiçbir şey yoktu

`Cikis_Gecidi` yalnızca bir zemin koyuyordu; yan duvar, tavan, arka duvar yok.
Kapıdan gökyüzü ve boş zemin görünüyordu.

**Geometri doğruydu** — kapı `(0.25, 3, 3.2)`, gedik `(3.2, 3)`, birebir
doluyor. "Kapı küçük kalmış" gibi görünen boşluk aslında haritanın dışıydı.
Ölçüler sahne dosyasından okunarak doğrulandı; tahminle uğraşmak gerekmedi.

`BuildVestibule` gediğin dışına kapalı bir sahanlık kuruyor: iki yan duvar,
arka duvar, tavan; iç yüzleri `Wall Plain` ile giydirilmiş.

Sahanlık duvar halkasının **dış yüzünden** başlayıp `Cikis_Gecidi/Zemin`'in
bittiği yerde bitiyor. Halkanın içine taşsaydı yan duvarlar komşu duvar
bloklarının içinde kalır ve yüzeyler çakışırdı.

### 3. Çıkıştan geçmek hiçbir şey yapmıyordu

Bu en ciddisiydi ve sistem yazıldığından beri öyleydi.

`ExitGate` geçidin kökünde, tetikleyici collider ise `Tetik` **çocuğunda**.
Unity trigger mesajlarını collider'ın **kendi objesine** gönderiyor (ve collider
bir Rigidbody'ye bağlıysa onunkine) — parent'a değil. Yani
`ExitGate.OnTriggerEnter` **bir kez bile çağrılmamıştı**: kaçan kapıdan geçiyor,
ne kurtuluyor ne izleyiciye düşüyordu.

`ExitTriggerRelay` tetikleyicinin üstünde durup olayı geçide iletiyor
(`ExitGate.ReportEscapeTrigger`). Köke kinematik bir Rigidbody eklemek de
çözerdi ama o, canavar engelini de aynı gövdeye bağlardı; bu yol fiziğe hiç
dokunmuyor.

**Ders:** trigger olayı beklerken collider'ın hangi objede olduğuna bak.

### 4. Kilit paneli ışın atmıyor

İlk sürüm paneli terminaller gibi yerleştiriyordu: dört yöne ışın at, en yakın
duvarı bul. Panel **hiç kurulmadı** — `TryFindWall` ışından önce 0.5 m yarıçaplı
bir boşluk sınaması yapıyor (`Physics.CheckSphere`) ve çıkışın önündeki koridorda
takılıp sessizce vazgeçiyordu.

Yer artık sabit hesaplanıyor: gediğin yanındaki halka duvarının iç yüzü, kapının
solunda. Halka iki gedik dışında dolu olduğu için orada duvar olduğu garanti.

**Panel `Cikis_` ile başlıyor**, yani `Terminal ve Çıkış Kur` her çalıştığında
çıkış objeleriyle birlikte yeniden kuruluyor — elle taşınırsa sabit yerine
döner. Beğenilen bir konum bulunursa koda yazılmalı.

### Panel ekranı

Terminaldekiyle aynı yerde: nişangahın olduğu nokta, ekranın tam ortası.
Nişangah o sırada çizilmiyor (`PlayerInteractor.InputCaptured`).

Görsel dil tek renk ailesi — **her şey sarının bir tonu.** Tamamlanan adımlar
bir ara yeşildi; panelin kimliği renkten geldiği için sönük sarıya çevrildi.

- Tam çerçeve + **köşe ayraçları**. Çerçevenin tamamını kalınlaştırmak paneli
  ağırlaştırıyordu; vurgu köşelerde toplanınca hem oturaklı hem hafif duruyor.
- Her adım kendi hücresinde. **Sıradaki hücre dolu sarı, yazısı koyu** — göz
  sıradakini aramak zorunda kalmıyor.
- Panel tam ekran değil, bilerek: panel başındaki oyuncunun tek savunması
  etrafını duyup görebilmek.

---

## 19. Sesli sohbet

Yakınlık tabanlı konuşma. Kendi kodumuz: **yeni paket yok.** Dissonance
ücretli olduğu için elendi, Vivox 3B karışımı sunucuda yaptığı için mağara
yankısıyla çelişiyordu (bölüm 10, madde 6).

### Sıkıştırma: µ-law, 8 kHz, 20 ms

`VoiceCodec` — G.711, telefon standardı, otuz satır. Her örnek 16 bitten 8
bite iniyor: konuşurken **64 kbit/s**. Opus 24 kbit/s'e indirirdi ama bir
paket daha demekti (bölüm 0). Dar geldiği gün değişecek tek yer bu dosya;
çağıranlar çerçevenin nasıl sıkıştığını bilmiyor.

8 kHz telefon kalitesi: konuşma tamamen anlaşılır, tiz kaybı korku oyununda
telsiz hissi bile veriyor. Yönü belirleyen şey içerik değil Unity'nin 3B
panlaması, o yüzden bant genişliği yön ipucunu bozmuyor.

### Kimin duyacağına SUNUCU karar veriyor

| Durum | Kim duyar |
|---|---|
| Lobide / tur bitince | Herkes herkesi, mesafesiz |
| Turda, sahadakiler | Yalnızca 18 m içinde |
| Elenenler kendi aralarında | Hepsi, mesafesiz |
| **Elenen → sahadaki** | **Duyulmaz** |

Herkese yollayıp istemcide mesafe süzmek daha kolay olurdu ama o zaman
konuşanın sesi ve dolaylı olarak **yeri** tüm istemcilere giderdi — bölüm
4'teki "istemciye görmesi gerekmeyen bilgiyi gönderme" kuralı, izlerin
yalnızca canavara gönderilmesiyle aynı gerekçe.

**Ölüler yaşayanlardan koparıldı, bilerek.** Bölüm 5 elenen oyuncunun canavarı
izleyememesini "sesli konuşulan bir oyunda doğrudan hile olurdu" diye
gerekçelendiriyor; aynı gerekçe sese birebir uyuyor. Ölüler kendi aralarında
serbestçe konuşuyor — izleyicilik cezalandırılmamalı.

Kanal **unreliable**: geciken bir ses çerçevesi işe yaramaz, yeniden gönderimi
yalnızca gecikmeyi büyütür. Kayıp çerçevenin karşılığı 20 ms'lik bir boşluk ve
jitter tamponu onu yutuyor.

### Oynatma: ses thread'i çekiyor, biz itmiyoruz

`VoicePlayback` akan bir `AudioClip` kuruyor (`stream: true`); klip her
istendiğinde geri çağrıyı tetikliyor, yani zamanlamayı ses motoru yönetiyor.
Alternatif (`SetData` ile döngüsel klibe yazmak) okuma/yazma kafalarını elle
senkronlamayı gerektiriyor ve kayma biriktikçe cızırdıyor.

Halka tampon **tek üretici–tek tüketici**: ana thread yalnızca `writeIndex`'i,
ses thread'i yalnızca `readIndex`'i yazıyor, ikisi de `volatile`. **Kilit yok,
bilerek** — ses thread'inde kilit beklemek doğrudan cızırtı demek. Tampon
dolarsa YENİ çerçeve atılıyor; eskiyi atmak ses thread'inin okuduğu yere
dokunmak olurdu.

Çalmadan önce 80 ms biriktiriliyor (jitter tamponu): gecikme pahasına
kesintisizlik. Boşalırsa yeniden birikmeyi bekliyor.

**Mimarinin öngördüğü iki şey tuttu.** Konuşma oyuncunun üstündeki 3B
kaynaktan çaldığı için **mağara yankısı bedavaya geldi** (bölüm 12 bunu baştan
yazmıştı) ve kişi başı seviye `AudioSource.volume`'dan geldiği için
**AudioMixer gerekmedi** — ki script'ten kurulamıyor.

### Mikrofon göstergesi (sağ üst)

İki ayrı soruyu cevaplıyor ve karıştırılmaları en sinir bozucu durumu üretir
(konuştuğunu sanıp kimsenin duymaması):

- **Çubuk her zaman** mikrofonun duyduğu seviyeyi gösteriyor — gönderilmese
  bile. Bas-konuşa basmadan önce mikrofonun çalıştığını görüyorsun.
- **Renk** gönderimi söylüyor: sönük gri = duyuyor ama göndermiyor, kırmızı =
  gidiyor.
- Otomatik modda çubuğun üstünde **eşik çizgisi** var. Eşiği körlemesine
  ayarlamak imkânsızdı.

Seviye **karekökle** çiziliyor: RMS doğrusal, kulak logaritmik; ham değerle
normal konuşma çubuğun ilk beşte birinde kalıp okunmuyordu. Çubuk hızlı çıkıp
yavaş iniyor — aynı numara kilitli terminalin alarm ışığında da var (bölüm 12).

Simge **üç dikdörtgenle** çiziliyor, harfle değil: varsayılan TMP fontu
yalnızca temel Latin kapsıyor ve mikrofon emojisi boş kutuya dönerdi (lobi
etiketlerinde bir kez yaşandı).

### TAB paneli: basılı tutma DEĞİL, aç-kapa

`ScoreboardPanel` kadroyu, pingi ve kişi bazlı ses ayarını gösteriyor.

Susturma düğmesine ve kaydırıcıya tıklamak **imleç gerektiriyor**, turda ise
imleç kilitli — basılı tutulan bir panelde bunlara ulaşmanın yolu yok. Bu
yüzden tuş aç-kapa çalışıyor ve panel açıkken imleç serbest bırakılıp bakış
kesiliyor. Mekanizma duraklatma menüsünün kullandığının aynısı
(`MenuController.SetOverlayOpen`) — imleç yönetimi tek yerde kalmalı, iki
bileşen birden `Cursor.lockState` yazarsa oyuncu bazen imleçsiz kalıyor.

**Panel tam ekran DEĞİL, ortada yarı saydam bir kutu.** İlk sürüm tam ekran
paneldi ve "yürümeye devam edebilirsin" özelliğini anlamsız kılıyordu:
yürüyebiliyorsun ama göremiyorsun.

**İmleç HER KAREDE doğrulanıyor** (`MenuController.ApplyCursor`), yalnızca
geçişte değil. Unity pencere odağı değişince (alt-tab, editörde Game view'a
tıklamak) imleç durumunu kendi başına değiştiriyor ve tek seferlik bir yazı
geri gelmiyordu — panel açık olduğu hâlde imleç kayıp kalıyordu.

> **`Cursor.visible` KOŞULSUZ yazılıyor, "farklıysa" değil.** İlk düzeltme
> ikisini de `if (mevcut != istenen)` ile koruyordu ve **hiçbir şeyi
> çözmedi.** Sebep: `Cursor.visible`'ın getter'ı gerçeği yansıtmıyor — imleç
> kilitliyken Unity onu zorla gizliyor ama özellik hâlâ en son yazdığın değeri
> döndürüyor. Koruma bu yüzden yazıyı atlıyor ve imleç bir daha gelmiyordu.
>
> Kilit ise hâlâ korumalı yazılıyor: onu her karede koşulsuz yazmak editörde
> Game view'dan çıkmayı imkânsızlaştırırdı.
>
> Ders: **bir Unity özelliğinin getter'ı, motorun o an uyguladığı durumu değil
> senin yazdığın değeri döndürebilir.** "Zaten doğru" varsayımı buradan
> geliyordu ve yanlıştı.

`ScoreboardPanel` ayrıca kaplama durumunu **her karede** bildiriyor:
`SetOverlayOpen` değişmemişse hemen çıkıyor, ama panel açılırken
`MenuController` henüz uyanmamışsa (Awake sırası garanti değil) tek seferlik
bildirim kaybolur ve imleç hiç serbest bırakılmazdı.

**Panel açıkken yürümeye ve zıplamaya devam ediliyor** (2026-09-06). Kesilen
tek şey **bakış** — imleç serbestken farenin arayüzdeki hareketi karaktere de
gitseydi ekran savrulurdu. Eylemler (etkileşim, saldırı, fener) de kapalı:
tıklama arayüze gidiyor, aynı tıkla canavarın savurması istenmez.

Menü ise tam duraklatma: orada girdi kaynağı **sökülüyor**. İkisi
`MenuController.ApplyGameplayState`'te ayrı ayrı ele alınıyor — panel bir
duraklatma değil, kovalanırken listeye bakmak yüzünden yakalanmak saçma
olurdu.

**Ses ayarı ağa gitmiyor:** susturma ve kişisel seviye senin kulağının
tercihi, karşıdakinin mikrofonuna dokunmuyor. Ağa taşımak kimin kimi
susturduğunu herkese söylemek olurdu.

**Anlamsız denetimler GİZLENİYOR, griye alınmıyor.** Kendi satırında ses
kaydırıcısı ve susturma yok (kendini duymuyorsun), botta da yok (sesi yok).
Griye alınmış bir kaydırıcı görünüşte çalışıyor ve oynayan onu "bozuk" diye
okuyor — tek başına test edildiğinde tam olarak bu yaşandı. Yoksa, olmadığı
belli.

**Ping'i sahibi bildiriyor, sunucu yazıyor** (`RoundParticipant.pingMs`,
saniyede bir). Mirror'ın `NetworkTime.rtt`'si yalnızca yerel istemcide
anlamlı; sunucunun her bağlantı için aynı ölçümü kendi yapması da mümkün ama
Mirror bunu her sürümde aynı yerde vermiyor. Ping bir oyun kararı değil
gösterge — yanlış bildiren istemci yalnızca kendi pingini yanlış gösterir.

### Ayarlar ayrı bir ekranda

Sesli sohbetin altı ayarı seçeneklere eklenince ekran **alt alta sığmadı**:
ad, fare, ses, ters bakış, altı ses ayarı ve iki düğme. Diğer oyunların
yaptığı gibi kategoriye ayrıldı.

| Ekran | İçerik |
|---|---|
| **SEÇENEKLER** | Ad, fare hassasiyeti, ters bakış + `SES` ve `TUŞ ATAMALARI` kapıları |
| **SES** (`AudioPanel`) | Genel ses + sesli sohbetin tamamı |
| **TUŞ ATAMALARI** | Zaten ayrıydı |

Esc zinciri kırılmıyor: ses/tuşlar → seçenekler → geldiği yer (ana menü ya da
duraklatma).

> **Kaydırıcılar açılışta yanlış değer gösteriyordu.** Mikrofon kazancı hep
> 4.0x, konuşma sesi %200 çıkıyordu. Sebep: `Slider.value`'ya yazmak
> dinleyiciyi tetikliyor ve tetiklenen dinleyici **kaydırıcının o anki
> konumunu ayara geri yazıyor** — yani okunan değerin üstüne varsayılan konum
> biniyordu. `AudioPanel` artık okuma sırasında bir `suppress` bayrağı
> kaldırıyor ve değerleri `Awake` yerine **`OnEnable`**'da okuyor: ekran her
> açılışta güncel değeri gösteriyor.
>
> Cihaz adı da düğmeye sığmıyordu ("Microphone (High Definition Audio
> Device)"). Parantez içi sürücünün adı, ayırt edici olan baş kısım; artık
> orası kesiliyor ve yazı tek satıra kilitli.

### Kurulum

```
Yakalamaca > Sesli Sohbet Kur   → prefaba bileşenleri ekler
Yakalamaca > Menü Kur           → ayarlar, gösterge, TAB paneli, tuş satırları
```

`Sesli Sohbet Kur` bilerek `Ağ Kurulumu`'ndan ayrı: o araç oyuncu prefabını
sıfırdan kuruyor, yani üç bileşen için model/menü/ses/yankı/katman zincirinin
tamamı gerekirdi. `EOS Kurulumu` da aynı gerekçeyle ayrı duruyor. İkisi de
hiçbir şey silmiyor.

### Bilinen sınır

**Tek makinede denenemez.** Kendi sesini kendine göndermiyoruz, yani yakalama
zinciri çalışsa bile ağ yolu ancak iki makineyle doğrulanıyor — EOS'taki gibi.
Sağ üstteki çubuk en azından mikrofonun duyduğunu tek başına gösteriyor.

---

## 20. Gerçek UI: OnGUI'den Canvas'a

Teknik borç 2'nin karşılığı. Oyun içi arayüzün tamamı `OnGUI` ile çiziliyordu:
nişangah, nişan yazısı, tur durumu, terminal ekranı ve çıkış kilidi paneli.
**Çalışma anında artık hiç IMGUI yok.**

### Asıl sorun sıralamaydı

IMGUI **her zaman Canvas'ın üstünde** çiziliyor. Yani menü açıldığında tur
yazıları menünün üzerine biniyordu ve `MenuController` onları elle kapatmak
zorundaydı:

```csharp
RoundHud hud = RoundManager.Instance.GetComponent<RoundHud>();
hud.enabled = !menuOpen;
```

Bu, kapatılması unutulan her yeni IMGUI parçası için sessizce bozuluyordu —
nitekim `Terminal` ve `ExitLock` ekranları hiç kapatılmıyordu: terminal
başındayken Esc'ye basınca ekran duraklatma menüsünün üstünde kalıyordu.

Canvas'a taşınınca sıralama kendiliğinden doğru oldu ve elle kapatma tek bir
görünürlük kuralına indi: **menü açıkken HUD kapalı** (`GameHud`). Ayrı bir
"tur oynanıyor mu" şartı yok — lobide menü zaten açık.

### Ne nereye gitti

| Eski | Yeni |
|---|---|
| `RoundHud.OnGUI` | `RoundHudView` (dosya silindi) |
| `PlayerInteractor.OnGUI` | `CrosshairView` |
| `Terminal.OnGUI` | `TerminalScreen` |
| `ExitLock.OnGUI` | `ExitLockScreen` |

Hepsi `Menü Kur` ile kuruluyor; mikrofon göstergesi ve TAB paneli zaten
oradaydı, yani bütün arayüz tek araçtan çıkıyor.

### Karar bileşende, çizim panelde

`Terminal.BuildScreenState()` ekranda ne yazacağını hesaplayıp bir yapı
döndürüyor; `TerminalScreen` yalnızca yazıyı, rengi ve çubuk oranını
uyguluyor. Kural terminalin kendi durumundan çıktığı için arayüz değişse de
tek yerde kalıyor (bölüm 5).

### Beş terminal, TEK panel

Eskiden her terminal kendi ekranını çiziyordu. Canvas'ta beş ayrı panel
kurmanın anlamı yok: terminal başında hareket kilitli, yani aynı anda yalnızca
birine bağlanılabiliyor. Panel `Terminal.ActiveLocal`'e bakıp o an hangisi
bağlıysa onu gösteriyor; kayıt `Update`'te kuruluyor ve `OnDisable`'da
bırakılıyor — yok olan bir terminale bakan panel ekranda asılı kalırdı.
`ExitLock` da aynı desende.

### Oklar fontta olmayabilir — kendini onaran yedek

Yön okları (`↑ ↓ ← →`) temel Latin dışında. Varsayılan `LiberationSans SDF`
atlası **statik** ve yalnızca temel Latin kapsıyor; oklar ancak **dinamik
fallback**'ten gelebiliyor. Fallback bu projede var ve çalışıyor (Türkçe
harfler ondan geliyor) ama garantisi yok — lobi etiketlerinde bir kez "★" boş
kutuya dönüşmüştü (bölüm 13).

`GameHud.Glyph` karakteri `HasCharacter(..., tryAddCharacter: true)` ile
sınıyor: bulabildiyse fallback'e **ekliyor**, bulamadıysa yedeğe düşüyor. Yani
ilk çağrı hem cevabı veriyor hem sorunu çözüyor.

Kilit panelinde yedek **basılacak tuşun harfi** (W/S/A/D). Boş kutu
göstermektense onu göstermek her açıdan daha iyi: oyuncunun gerçekten basacağı
şey o. Terminal sınavında zaten ok ve tuş yan yana yazılıyor.

> **Oklar gerçekten çalışıyor — ölçüldü.** Oynandıktan sonra
> `LiberationSans SDF - Fallback` varlığına `m_Unicode: 8594` (→) ve `8595`
> (↓) eklenmiş hâlde bulundu, yani TMP karakterleri kaynak fonttan bulup
> atlasa yazdı. Yedeğe düşülmüyor; yedek yalnızca sigorta.
>
> **Bu yüzden o varlık git'te sık sık değişiyor.** Türkçe harfler ve şimdi
> oklar oraya çalışma anında ekleniyor — elle yapılmış bir düzenleme değil,
> commit'lemekte sakınca yok.

### Görünürlük `SetActive` ile YÖNETİLMİYOR — CanvasGroup ile

İlk sürüm her paneli `SetActive` ile açıp kapatıyordu ve **terminal ekranı
oyunda bir kez bile görünmedi.** Sebep tek satırlık ama sinsi:

> **Kapalı bir `GameObject` `Update` çalıştırmıyor.** Görünürlüğü yöneten
> bileşen o objenin ÜSTÜNDEyse, kendini kapattığı anda bir daha açamıyor —
> tek yönlü bir kapı.

Terminal ekranı kurulumda kapatılmıştı (Scene penceresinde üst üste binmesin
diye), yani `TerminalScreen.Update` hiç çalışmadı ve panel hiç açılmadı.

**Aynı hata altı bileşende birden vardı** ve üçü zaten ölüydü: TAB paneli hiç
açılmıyordu, mikrofon göstergesi sesli sohbet bir kez kapatılınca geri
gelmiyordu, ve `GameHud` menü ilk açıldığında bütün HUD'ı kalıcı olarak
söndürüyordu. Terminal ekranı sadece ilk fark edileniydi.

Hepsi `CanvasGroup.alpha`'ya geçirildi: görüntü kapanıyor, obje ayakta kalıyor,
bileşen kararını her karede gözden geçirebiliyor. `blocksRaycasts` da kapanıyor
— görünmez bir panel tıklamaları yutmamalı (TAB panelindeki düğmeler bunu
gerektiriyor).

`GameHud.Visible`'ın varsayılanı **true**: bileşen sahneden düşerse yanlış
tarafa değil, görünür tarafa düşülüyor. Eksik bir gizleme fark edilir, eksik
bir arayüz "oyun bozuk" diye okunur.

> **Ders:** bir bileşen kendi `GameObject`'ini kapatıyorsa, onu geri açacak
> kod nerede? Cevap "aynı bileşende" ise kod hiç çalışmayacak demektir.

### Silinen dosya sahnede iz bırakıyor

`RoundHud` sınıfı silindi ama bileşen `RoundManager` objesinde
serileştirilmişti: Unity onu "missing script" olarak gösterip her açılışta
uyarı basardı. `Menü Kur` artık `RemoveMonoBehavioursWithMissingScript` ile
temizliyor, yani ayrıca `Hataları Temizle` çalıştırmak gerekmiyor.

> **Ders:** çalışma anındaki bir `MonoBehaviour` sınıfını silmeden önce
> sahnede/prefabta serileştirilmiş olup olmadığına bak. Derleme temiz çıkar,
> hata yalnızca Unity açılırken görünür.

### Kurulum

```
Yakalamaca > Menü Kur
```

Menü canvas'ı sıfırdan kurulduğu için oyun HUD'ı, terminal ve kilit ekranları
hep birlikte geliyor.

---

## 21. Ceset ve diriltme

### 21.1 Ceset: kurulan sistem (2026-09-07)

Yakalanan kaçanın bedeni artık yok olmuyor: **fizik motorlu bir ragdoll olarak
haritada kalıyor.** Kendi ağırlığıyla yığılıyor, üstünden geçen oyuncu onu
itiyor, bir sonraki tur başında temizleniyor.

| Parça | İşi |
|---|---|
| `Core/Corpse.cs` | Cesedin kendisi: kurbanın gövdesinden kopya alıyor, ragdoll'u kuruyor, itmeyi uyguluyor |
| `Core/RagdollFactory.cs` | Humanoid iskeletin 11 ana kemiğine Rigidbody + Collider + `CharacterJoint` |
| `Core/RagdollSync.cs` | Ragdoll pozunu sunucudan istemcilere taşıyor |
| `Editor/CorpseSetup.cs` | `Yakalamaca > Ceset Sistemini Kur` — prefabı kurup NetworkManager ve RoundManager'a bağlıyor |

**Ceset ayrı bir obje, oyuncunun kendi gövdesi DEĞİL.** Oyuncu objesi bir
sonraki turda yeniden kullanılıyor; gövdesini kalıcı olarak fiziğe bağlamak o
modeli geri alamamak demekti. Onun yerine `RoundParticipant.DeathHold` ölüm
klibi bitince gövdenin o anki pozundan bağımsız bir KOPYA alıyor.

**Fizik yalnızca sunucuda.** 11 Rigidbody'lik bir zincir her makinede farklı
oturuyor; her istemci kendi simülasyonunu yürütseydi yan yana duran iki oyuncu
cesedi farklı yerde görürdü. İstemcilerdeki gövdeler kinematik, pozu
`RagdollSync`'ten alıyor — bölüm 4'ün "his istemcide, karar sunucuda"
kuralının fizik karşılığı. Paket 56 bayt (kalça konumu + sıkıştırılmış kemik
dönüşleri) ve yalnızca gövde hareket ederken gidiyor; ceset oturunca trafik
tamamen kesiliyor.

**Katman `Sus`** (bölüm 16): gövdeyi durdurur ama canavarın vuruş ışınını
kesmez. Koridorda yatan bir cesedin arkasına saklanmak kalkan olmamalı.

**İtme sunucuda hesaplanıyor** (`Corpse.ShoveFromPlayers`): oyuncu hızı
pozisyon farkından çıkarılıyor — animatörlerin ve `FootstepAudio`'nun yaptığının
aynısı, çünkü sunucuda uzak oyuncuların `PlayerController`'ı kapalı. İtki
parçanın KÜTLESİYLE ölçekleniyor, böylece `pushStrength` doğrudan "oyuncunun
hızının yüzde kaçı aktarılıyor" anlamına geliyor.

#### Bu sistemi kurarken düşülen yedi tuzak

Hepsi oynanırken bulundu ve hepsi aynı aileden: **fizik kısıtları birbiriyle
kavga edince "hiç hareket etmiyor" ile "paramparça oluyor" aynı sebebin iki
ucu oluyor.**

| Belirti | Gerçek sebep |
|---|---|
| Beden görünmez ama dokunuluyor | Elenince `PlayerController` kapanıyordu ama altındaki **`CharacterController` hiç kapanmıyordu** — görünmez, katı bir engel koridorda kalıyordu |
| Ceset haritanın dışına fırlıyor | `deathForwardOffset = 0` cesedi canavarın TAM üstünde doğuruyor (bölüm 17); PhysX bu derin çakışmayı patlatıyordu. Çözüm elle çarpışma kapatmak değil, `maxDepenetrationVelocity` |
| Ceset hiç görünmüyor | `Corpse` görselini `OnStartClient`'ta alıyor, `DeathHold` ise aynı karede kaynağı gizliyordu. **Kapalı kaynaktan `Instantiate` kapalı kopya üretiyor** |
| Parçalar birbirinden kopuyor | Gövde ile uyluk eklemle bağlı **değil** (ikisi de kalçaya bağlı, birbirine değil); kapsülleri kalçada iç içe geçip her karede itişiyorlardı |
| Ceset havada donuyor | Eklem projeksiyonu (`enableProjection`) kinematik bir işlem: zinciri yerinde çiviliyordu |
| Ragdoll hiç kurulmuyor | **Kapalı bir `Animator`'da `GetBoneTransform` null dönüyor.** Kemikler artık kurbanın CANLI animatöründen çözülüp klona yol üzerinden eşleniyor |
| Ceset itilemiyor | İtki ~4.5 N·s idi; eklemler 11 parçayı tek bir ~70 kg gövde gibi davrandırıyor, yani 6 cm/s. Ayrıca itme `OnControllerColliderHit`'e bağlıydı ve o geri çağrı sunucuda uzak oyuncular için hiç çalışmıyor |

> **Ders:** ragdoll'da bir belirtiyi tek başına okumak yanıltıyor. "Havada
> duruyor" hem kinematik kalmış olabilir, hem kurulamamış olabilir, hem de bir
> kısıt tarafından çivilenmiş olabilir. Üçünü ayırmanın tek ucuz yolu
> **teşhis logu**: parça sayısı, simülasyonun hangi tarafta olduğu, kalçanın
> kinematic/gravity/uyku durumu. Bu oturumda çözümü getiren şey buydu.

---

### 21.2 Diriltme — spesifikasyon (UYGULANDI, bkz. bölüm 23)

Ceset artık haritada durduğuna göre asıl amaç şu: **onu oyuna geri sokmak.**
Aşağıdaki akış 2026-09-07'de kullanıcı tarafından tarif edildi.

```
Kaçan elenir  →  cesedi yerde kalır (21.1)
                      ↓
        başka bir kaçan cesedi ALIP TAŞIR
                      ↓
        haritadaki İKİ diriltme makinesinden birine koyar
                      ↓
        15 saniyelik işlem başlar — terminaldeki gibi BECERİ SINAVI var
                      ↓
   hata → kilit + ilerleme SIFIR        hatasız 15 sn → oyuncu DİRİLİR
   (baştan başlanacak)
```

**Sabitler:**

| | Değer |
|---|---|
| Haritadaki diriltme yeri | **2 adet** |
| İşlem süresi | **15 saniye** |
| Hata cezası | Kilit + **ilerleme 0'a döner**, baştan |

**Terminalden bilinçli olarak farklı.** Bölüm 11.2 terminal için "ilerleme
**kalıcı**: yarıda bırakılan terminal sıfırlanmaz, başkası devam eder" diyor.
Diriltmede tersi isteniyor: hata ilerlemeyi siliyor. İkisi aynı görünüp farklı
davrandığı için kodda da ayrı tutulmalı — `Terminal`'e bir bayrak eklemek iki
mekaniği tek yerde birbirine karıştırır.

**Hazır olan altyapı:**

- `Corpse.victimNetId` cesedin kime ait olduğunu **zaten taşıyor** — makine
  kimi dirilteceğini biliyor, yeni bir eşleme gerekmiyor.
- `Terminal`'in sınav sistemi (bölüm 11.3) olduğu gibi kullanılabilir: yön
  işareti, `promptWindow`, tuş atamalarından okuma, ağ gecikmesi payı.
- `RoundParticipant.ServerSetAlive(true)` + `ServerPlaceAt` diriltmenin son
  adımı; ikisi de yazılı ve çalışıyor.
- `RoundManager.ServerClearCorpses` cesetleri tur başında temizliyor.

#### Uygulamadan ÖNCE karara bağlanacak sorular

Bunlar tasarım kararı, kod sorusu değil — cevaplanmadan yazılırsa yanlış yere
çakılır.

1. **`requiredTerminals` geri artacak mı?** Bugün bir kaçan ölünce gereken
   terminal sayısı 1 azalıyor (bölüm 11.1, "kartopunu bu dengeliyor").
   Dirilme onu geri artırırsa **diriltmek cezalandırılmış** olur; artırmazsa
   ölmek kalıcı bir indirim hâline gelir ve ölmek işe yarayabilir.

2. **Bilgi sızıntısı.** Bölüm 5: elenen oyuncu izleyiciye geçiyor ve
   **canavarı asla izleyemiyor** — çünkü sesli konuşulan bir oyunda bu
   doğrudan hile. Dirilen oyuncu ölüyken gördüğü her şeyi yanında geri
   getiriyor. Ölü kaçanın kamerası kendi cesedine kilitlensin mi?

3. **Canavarın karşı hamlesi ne?** Canavar terminali kilitleyebiliyor
   (bölüm 11.4). Diriltme makinesini de kilitleyebilmeli mi? Taşınan cesedi
   düşürtebilmeli mi? Karşı hamle yoksa diriltme tek taraflı bir kazanç olur
   ve turu uzatır.

4. **Taşımanın bedeli.** Ceset taşıyan yavaşlıyor mu? Taşırken fener ve
   saldırı kullanılabiliyor mu? Canavar ceset taşıyabiliyor mu (diriltmeyi
   engellemek için cesedi uzağa götürmek)?

5. **Makineler haritaya nasıl konacak?** Harita **elle düzenleniyor** ve onu
   silen araçlar yasak (bölüm 0). İki makine ya elle konulacak ya da
   `Terminal ve Çıkış Kur` gibi "var olana dokunmayan" bir araçla.

6. **Kaç kez dirilebilir?** Sınır yoksa dolu kadroda tur bitmeyebilir —
   bölüm 11.1'in "süre sınırı yok" kuralıyla birleşince sonsuz tur riski var.

#### Teknik not: ceset taşınırken fizik

Ragdoll sunucu otoriteli (21.1). Taşımanın iki yolu var:

- Ragdoll'u kinematik yapıp taşıyıcıya bağlamak — poz korunur, ama 11 gövdeyi
  taşırken senkronlamak gerekir.
- Cesedi gizleyip taşıyıcının omzunda **ayrı bir görsel** göstermek — çok daha
  ucuz, oyunların çoğunun yaptığı. Ceset makineye konunca gerçek ragdoll geri
  doğuruluyor.

İkincisi tercih edilmeli; `Corpse` zaten "gövdeyi çalışma anında klonla"
desenini kullanıyor, aynı desen omuz görseli için de işler.

---

## 22. Codex: sesli sohbet düzeltmesi (2026-09-07)

**Uygulandı:** `VoiceChat` yerel dinleyici ve konuşmacının tur durumuna göre
`VoicePlayback.SetContext` çağırıyor. Lobide/tur sonunda ve sahada olmayanların
kendi arasında konuşma 2B, mesafesiz ve reverb bölgesinden bağımsız oynatılıyor.
Sahadaki oyuncuların konuşması 3B kalıyor; AudioSource menzili VoiceChat'in
`hearingRange` değeriyle eşleniyor. Kimin ses paketini alacağına yine sunucu
karar veriyor; yaşayanlarla elenen/kurtulan/izleyiciler ayrı gruplarda.

**Sesli sohbet kapalı:** hem yerel mikrofon hem gelen konuşmalar kapanıyor.
Kişisel ses seviyesi ve susturma tercihleri korunuyor. Oynatma tamponu,
susturma/açma ve grup değişimlerinde eski sesi tekrar çalmıyor. Ana thread
okuma indeksine dokunmuyor; temizliği ses thread'i sürüm sayacıyla yapıyor.

**Doğrulama:** Unity 2022.3.62f3 ile gelen Roslyn ve mevcut Bee referanslarıyla
runtime C# derlemesi geçti; çıktılar TEMP altında, Unity'nin derleme dosyaları
üzerine yazılmadı. Gerçek VoicePlayback ve VoiceCodec kodlarıyla, Unity ses
motorunu taklit eden bağımsız testte 10 davranış kontrolü geçti. Bu kontrol
Mirror bağlantısını, Unity ses motorunu veya mikrofon donanımını sınamaz.
İki makinede lobi konuşması, 18 m yakınlık, ölüm/kaçış sonrası grup ayrımı ve
sesli sohbeti kapatıp açma hâlâ oynanarak doğrulanmalı. Menü/Ağ Kurulumu
araçlarını tekrar çalıştırmak gerekmiyor; değişiklik çalışma anında uygulanıyor.

**Sırada:** ceset görselini mevcut kurban objesine bağımlılıktan kurtarmak
(ayrılan kurban / geç katılan istemci), ardından bölüm 21.2'deki taşıma ve
diriltme. Bu düzeltme paketinde ceset veya diriltme kodu değiştirilmedi.

---

## 23. Diriltme sistemi: kurulan hâli (2026-09-08)

Bölüm 21.2'deki spesifikasyon **uygulandı**. Akış tarif edildiği gibi çalışıyor:
ceset taşınıyor, haritanın iki ucundaki kabinlerden birine konuyor, yanındaki
terminalde 15 saniyelik iyileştirme işletiliyor, hatasız biterse kaçan orada
diriliyor.

### Parçalar

| Dosya | İşi |
|---|---|
| `Core/Corpse.cs` | Taşınma, kabine yerleştirme, itilme. `carrierNetId` / `stationNetId` SyncVar'ları |
| `Interaction/RevivalStation.cs` | Kabin terminali: ceset kabulü, operatör kilidi, sınavlar, diriltme kararı |
| `UI/RevivalScreen.cs` | Terminal ekranı (kendi Canvas'ını kuruyor) |
| `Editor/RevivalSetup.cs` | `Yakalamaca > Diriltme Sistemini Kur` — ceset gövde prefabı + iki kabin |
| `RoundManager.ServerRevive` | Cesedi tüketip oyuncuyu sahaya geri alan tek sunucu işlemi |

### Ceset görseli artık kurbandan bağımsız

Eskiden `Corpse` görselini kurbanın canlı gövdesinden klonluyordu. Kurban
ayrılırsa ya da istemci sonradan katılırsa ortada kopyalanacak bir şey
kalmıyordu. Artık `Diriltme Sistemini Kur` kaçan modelinden ayrı bir
`CorpseBody.prefab` üretiyor ve kemik yollarını (`bonePaths`) Corpse prefabına
yazıyor; istemci kurban objesine hiç ihtiyaç duymuyor.

Sunucu yine ölüm klibinin son pozunu kurbandan kopyalıyor (`CopyPose`), ama bu
yalnızca sunucuda; istemciler pozu `RagdollSync`'ten alıyor. `OnStartServer`
içinde `sync.Publish()` çağrılması bunun için: SyncVar spawn mesajına giriyor,
yani ceset istemcide daha ilk karede doğru pozda beliriyor.

### Taşıma

`E` ile alınıyor (2.6 m ve görüş hattı şartı, sunucuda doğrulanıyor). Taşınırken
ragdoll kinematik ve collider'ları kapalı; gövde taşıyıcının omzuna
sabitleniyor. Taşıyan kişi cesedi kendi ekranında görmüyor (yüzünü kapatırdı),
başkaları görüyor.

Taşıyan ölür, kaçar ya da bir terminale bağlanırsa ceset düşüyor. `E`
taşırken bırakma tuşu; kabine bakıyorsan yerleştirme tuşu.

### Terminal

15 saniye, arada **üç beceri sınavı** (3., 7. ve 11. saniyelerde). Sınav
ekrandayken ilerleme duruyor — bölüm 11.3'teki terminalle aynı mantık.

**Terminalden bilinçli farkı:** hata **ilerlemeyi sıfırlıyor.** Bölüm 11.2'de
terminal ilerlemesi kalıcı ("yarıda bırakılan terminal sıfırlanmaz"); burada
tersi isteniyordu. Hata ayrıca kabini kilitliyor ve dört adımlık bir yön
dizilimi girilene kadar açılmıyor; alarm 20 saniye ötüyor.

Kabini bırakmak da (E) ilerlemeyi sıfırlıyor ve ekranda böyle yazıyor.

### Diriltme anı

`RoundManager.ServerRevive` tek sunucu işlemi olarak: doğum noktasının boş
olduğunu sınıyor (`Harita` + `Oyuncu` maskesiyle kapsül testi), kurbanı
canlandırıp oraya yerleştiriyor, sayacı artırıyor ve cesedi yok ediyor.

Nokta doluysa işlem **başarısız olmuyor, bekliyor**: `elapsed` tavanda kalıyor
ve alan boşalınca kendiliğinden tamamlanıyor. Oyuncuya bunu söyleyen bir yazı
YOK — bilinen eksik.

### 21.2'deki altı sorudan hangileri cevaplandı

| Soru | Durum |
|---|---|
| 1. `requiredTerminals` geri artacak mı | **Cevaplandı:** artmıyor. Ayrıca indirim kurban başına BİR KEZ uygulanıyor (`terminalDiscountedVictims`), yani ölüp dirilip tekrar ölmek sayıyı ikinci kez düşürmüyor |
| 5. Kabinler haritaya nasıl konacak | **Cevaplandı:** `RevivalSetup` zemin ızgarasında boş hücre arayıp birbirine EN UZAK ikisini seçiyor (en az 25 m). Haritaya dokunmuyor, yalnızca yeni kök ekliyor |
| 2. Bilgi sızıntısı | **Cevaplandı (2026-09-08).** İzleyici zaten YALNIZCA hayattaki kaçanları izleyebiliyor, canavarı asla (`SpectatorController.RefreshTargets` — bölüm 5'ten beri böyle). Kullanıcı bunu yeterli buldu, ek kısıt getirilmedi |
| 3. Canavarın karşı hamlesi | **AÇIK.** Canavar kabini kilitleyemiyor, cesedi taşıyamıyor, diriltmeyi kesintiye uğratamıyor. Diriltme şu an tek taraflı bir kazanç |
| 4. Taşımanın bedeli | **KISMEN.** Taşırken yavaşlama yok; ama terminal kullanılamıyor ve odaklanınca ceset düşüyor |
| 6. Kaç kez dirilebilir | **Cevaplandı (2026-09-08).** Kabin başına hak: `RevivalStation.charges`, varsayılan **1**, her tur başında yenileniyor. İki kabin olduğu için tur başına toplam 2 diriltme. Sayı oynanarak ayarlanacak |

### Diriltme hakkı (2026-09-08)

Sınırsız diriltme turu bitmez hâle getiriyordu: bölüm 11.1'e göre tur ancak
sahada oynayan kaçan kalmayınca bitiyor, dolu kadroda her ölen geri
gelebiliyorsa o an hiç gelmiyor.

`RevivalStation.charges` (varsayılan **1**) kabinin bir TURDA kaç diriltme
yapabileceğini söylüyor. Hak **tur başında yenileniyor**, kabin her
sıfırlandığında değil — sıfırlama başarılı bir diriltmeden sonra da çalışıyor,
orada yenilemek sınırı tamamen anlamsız kılardı.

Hakkı biten kabin ceset kabul etmiyor ve nişan yazısı bunu ilk satırda
söylüyor ("Bu kabinin diriltme hakkı bu turda bitti") — ceset boşuna
taşınmasın diye. Kalan hak terminal ekranının başlığında da yazıyor.

### Ölü test botu (2026-09-08)

Taşıma ve diriltmeyi denemek için önce birini öldürmek gerekiyordu.
`Yakalamaca > Test Botu Ekle (ölü — ceset testi)` ikinci bir bot koyuyor;
`RoundParticipant.startEliminated` işaretli olduğu için tur başlar başlamaz
eleniyor ve ~3 saniye sonra doğum halkasında hazır bir ceset bırakıyor.

Eleme **normal yoldan** (`RoundManager.ReportCaught`) yapılıyor: ölüm
animasyonu, ceset doğumu ve sayaçlar gerçek turdaki gibi işliyor, yani test
edilen şey gerçekten oyunun kendisi oluyor.

**Neden ayrı bir bot:** tur `minimumPlayers` = 2 ile başlıyor ve sahada kaçan
kalmayınca bitiyor. Tek bot ölü doğsaydı tur anında kapanırdı; canlı botun
sahada kalması gerekiyor.

**Taşımak için KAÇAN olmak şart** (`Corpse.LivingRunner`), yani test ederken
[2] ile kaçan olarak başla. [1] ile canavar olursan cesedi alamazsın.

### Taşıma ve ağırlık ayarları (2026-09-08, oynanış geri bildirimi)

Dört şikâyet, dördü de düzeltildi:

- **Ceset fazla kaygan ve hafifti** — bir kez itilen gövde koridorda kayıp
  gidiyordu. Üç ayar birden değişti: collider'lara yüksek sürtünmeli bir fizik
  materyali (`RagdollFactory.Surface`, `frictionCombine = Maximum` — zemin ne
  olursa olsun yüksek olan kazanıyor), doğrusal sönümleme 0.1 → 0.9, açısal
  sönümleme 1.5 → 3. İtme gücü 0.6 → 0.3 ve hız tavanı 20 → 6 m/s.
- **Taşınan ceset görünmüyordu.** Taşıyanın ekranında gizleniyordu (yüzünü
  kapatmasın diye), ama o zaman elinde bir şey olduğu hiç belli olmuyor ve
  kabine yerleştirmek körlemesine oluyordu. Gizleme kaldırıldı; gövde bunun
  yerine kameranın ALTINA, 0.95 m öne alındı (`Corpse.CarryOffset`).
- **Bırakma yönü.** Ceset artık ayağının dibine değil, **baktığın yöne**
  bırakılıyor (`DropForward` 0.9 m). Duvara dayanmışken gövdeyi duvarın içine
  sokmamak için mesafe küre ışınıyla kısaltılıyor — kameranın duvar payıyla
  (bölüm 5) aynı fikir.
- **Kabin ekranı** terminal ve çıkış kilidi panellerinin görsel diline
  çekildi: koyu gövde, ince çerçeve, köşe ayraçları, tek renk ailesi
  (kabinin turkuaz ışığı). Kilitliyken bütün panel kırmızıya dönüyor, kilit
  dizilimi hücrelerde gösteriliyor ve sıradaki hücre dolu renkte — göz
  sıradakini aramak zorunda kalmıyor (bölüm 18'deki desen).

### Kabin gövdesi tıklanamıyordu (2026-09-08)

Şikâyet: "cesedi kabine koyunca kabin saymıyor, hâlâ ceset istiyor."

Sebep: `RevivalStation` kabinin küçük TERMİNAL kutusunda duruyor; taban,
tavan, arka panel ve yan paneller onun **kardeşleri**. `PlayerInteractor`
hedefi `GetComponentInParent<IInteractable>()` ile buluyor, yani kabinin
gövdesine bakınca yukarı arayınca terminale hiç ulaşamıyordu — gövde ışını
kesiyor ama kullanılabilir bir şey çıkmıyordu.

Oynanışta şöyle görünüyordu: ceset taşırken kabine bakıp E'ye basıyorsun,
yerleştirme yerine ceset **bırakılıyor**. Bırakma artık bakılan yöne
yapıldığı için gövde tam kabinin içine düşüyor ve yerleşmiş gibi duruyor;
kabin ise "bir ceset getir" demeye devam ediyor. Yani bırakma iyileştirmesi
var olan bir boşluğu görünür hâle getirdi.

`RevivalStationRelay` kabin köküne biniyor ve gövdenin herhangi bir parçasına
bakmayı terminale yönlendiriyor. `RevivalStation.Awake` içinde çalışma anında
kuruluyor, sahneyi yeniden kurmak gerekmiyor. Aynı desen `ExitTriggerRelay`'de
de var (bölüm 18): collider bir objede, mantık başka objede olduğunda araya
aktarıcı konuyor.

### Taşınan ceset artık sarkıyor

Eskiden taşırken bütün parçalar kinematik yapılıp poz kare kare zorla
yazılıyordu: ceset elde tahta gibi duruyordu. Artık yalnızca **kalça**
sabitleniyor (elindeki nokta), geri kalan her şey eklemlerden sarkıyor —
kollar, bacaklar ve baş yürüdükçe sallanıyor.

Collider'lar taşırken kapalı kalıyor: açık olsalardı sarkan uzuvlar taşıyanı
itip zemine takılırdı. Kabine yerleştirilince her şey yeniden kinematik
oluyor, yoksa gövde yavaşça kabinden dışarı akardı.

### Taşıma ince ayarları (2026-09-08, ikinci geri bildirim)

- **Koşarken ceset titriyordu.** Kalça kinematik ve konumu her fizik adımında
  DOĞRUDAN yazılıyordu; bu bir ışınlama sayılıyor, çözücü aradaki hareketi
  görmüyor ve ona bağlı eklemler her adımda sıfırdan bir sıçrama görüp
  zangırdıyordu. `MovePosition`/`MoveRotation`'a geçildi: hareket adım boyunca
  yayılıyor ve çözücü hız bilgisi alıyor.
- **Elde taşınan ceset cisimlerin içinden geçiyordu.** Collider'lar taşırken
  kapalıydı; artık AÇIK, yani sarkan uzuvlar duvara ve eşyalara çarpıyor.
  Taşıyanın kendi kapsülüyle çarpışma ayrıca kapatılıyor
  (`Corpse.IgnoreCarrier`), yoksa uzuvlar taşıyanı iter ve ikisi birbirine
  takılırdı.
- **Taşıma yüksekliği 0.05 → 0.5 m.** Collider'lar açılınca alçak tutmak
  sarkan bacakları zemine sürtüp gövdeyi çırpındırıyordu.
- **Taşırken katman `Sus`, yerdeyken `Etkilesim`.** Collider'lar açılınca
  taşınan ceset kendi nişan ışınını kesmeye başladı ve kabin hedeflenemez
  oldu. `Etkilesim` ışın maskesinde, `Sus` değil — çarpışma katmandan
  bağımsız sürdüğü için taşırken `Sus`'a geçmek ikisini birden çözüyor.
- **Kabin menzili 2.8 → 4 m**: terminal cesedi zor algılıyordu.

### Kabine bırakmak yeterli, taşıma duvara uyum sağlıyor (2026-09-08, üçüncü geri bildirim)

- **Cesedi kabine BIRAKMAK artık yetiyor.** Kabul etmenin tek yolu terminale
  nişan alıp E'ye basmaktı; oyuncu için doğal olan ise gövdeyi kabinin içine
  bırakmak (hatta uzaktan atmak). `RevivalStation.TryAcceptNearbyCorpse` her
  karede gövde yuvasının çevresine bakıyor ve orada SERBEST bir ceset bulursa
  kendiliğinden kabul ediyor. E ile yerleştirme de duruyor — ikisi de aynı
  sunucu yoluna (`Corpse.ServerPlaceInStation`) çıkıyor.
- **Kalça duvarın içine giriyordu.** Kinematik olduğu için onu hiçbir şey
  durdurmuyordu; gövde duvara gömülünce sarkan uzuvlar derin çakışmayı çözmeye
  çalışıp çılgınca savruluyordu. Taşıma noktası artık taşıyıcıdan öne atılan
  bir küreyle sınırlanıyor: **önü kapalıysa ceset taşıyana yaklaşıyor**, illa
  sabit bir noktada durmuyor. Kameranın duvar payındaki (bölüm 5) fikrin
  aynısı.
- **Hız tavanı taşırken de uygulanıyor.** Duvara dayanınca uzuvların patlayıp
  savrulmasını kırpan ikinci emniyet.

### Bu oturumda düzeltilen iki kusur

- **Kabin sıfırlanınca ceset donuyordu.** `ResetStation` yalnızca `corpseId`'yi
  siliyor, ceset `stationNetId`'yi taşımaya devam ediyordu: `IsHeld` sonsuza
  kadar doğru, gövde kinematik ve collider'ları kapalı kalıyordu — alınamayan,
  itilemeyen, düşmeyen bir beden. `Corpse.ServerReleaseFromStation` eklendi.
- **Nişan yazısı yalan söylüyordu.** Elinde ceset varken dolu bir kabine
  bakınca "Diriltmeyi başlat" yazıyordu ama `CmdUse` reddediyordu; oyuncu E'ye
  basıp hiçbir şey olmadığını görüyordu. Yazı artık "Kabin dolu — taşıdığın
  cesedi önce bırak" diyor.

### Kurulum

```
Yakalamaca > Diriltme Sistemini Kur
```

Var olan kabinleri yeniden üretmiyor, haritaya ve terminallere dokunmuyor.
Sahnede ikiden farklı sayıda kabin bulursa durup uyarıyor.

---

## 24. GitHub: depo, gizli anahtar ve büyük dosyalar (2026-09-08)

Proje 2026-08-31'den beri yerel bir git deposu (bölüm 0'ın "yedek var" kutusu);
2026-09-08'de GitHub'a taşındı. Burada yazılı olan şey **kararlar** — komutlar
her yerde bulunur, ama bu depoya özgü üç tuzak var ve üçü de sessiz.

### Depo GİZLİ (private) olmalı — anahtar geçmişte duruyor

`Assets/_ScriptableObjects/EosApiKey.asset` içinde **gerçek bir client secret**
var (bölüm 13'ün sonundaki uyarı). Dosya EOS kurulduğundan beri takip ediliyor,
yani anahtar **git geçmişinin içinde**: bugünkü commit'ten silmek onu
geçmişten silmiyor. `git rm` bir dosyayı gelecekten çıkarır, geçmişten değil.

Sonuç iki maddede:

- **Depo gizli kaldığı sürece sorun yok.** Anahtarı yalnızca depoya erişimi
  olanlar görebiliyor ve bugün o kişi tek başına sensin.
- **Herkese açık yapmadan önce anahtar YENİLENMELİ.** Epic portalından yeni bir
  client oluşturup eskisini silmek gerekiyor. Geçmişi temizlemek
  (`git filter-repo`) 77 commit ve 824 MB'lık bir depoda hem yavaş hem
  kırılgan — üstelik anahtar bir kez sızdıysa geçmişi temizlemek onu geri
  almıyor, yalnızca izini siliyor.

Doğru sıra: **önce anahtarı yenile, sonra herkese açık yap.** Tersi işe
yaramıyor.

> Bu, teknik borç değil bir **kapı**. `EosApiKey.asset` bugün bilerek takip
> ediliyor: tek geliştiricili gizli bir depoda dosyanın gitmesi, klonlayınca
> EOS'un kendiliğinden çalışması demek. Depo herkese açılacaksa hem anahtar
> yenilenmeli hem dosya `.gitignore`'a girmeli.

### Büyük dosyalar: sınıra girmiyor ama yakın

GitHub tek dosyada **100 MB**'ı reddediyor ve 50 MB üstünde uyarı basıyor.
Depodaki en büyük üçü:

| Dosya | Boyut |
|---|---|
| `EOS_DevAuthTool-win32-x64-1.0.1.zip` | 74 MB |
| `EOS_DevAuthTool-darwin-x64-1.0.1.zip` | 57 MB |
| `libEOSSDK-Mac-Shipping.dylib` | 27 MB |

Üçü de sınırın altında, yani **Git LFS gerekmiyor** — LFS kurmak ayrı bir
bağımlılık ve ayrı bir kota, gereksizken açılmamalı.

İlk ikisi EOS'un geliştirici kimlik aracı (bölüm 13'teki "aynı bilgisayarda
iki kopyayla EOS test edilemez" kutusu). Kullanılmıyorsa silinip depo 130 MB
küçültülebilir, ama zip'ler pakete ait olduğu için paket güncellenince geri
gelirler.

Depo toplamı **824 MB**. GitHub 1 GB üstünde e-posta uyarısı gönderiyor;
bugün altındayız. İlk push bu yüzden uzun sürüyor (bağlantıya göre 10-40
dakika) ve **yarıda kesilirse baştan başlıyor** — git bir push'u parçalara
bölmüyor.

### `Library/` gönderilmiyor, bu doğru

`.gitignore` `Library/`, `Temp/`, `obj/`, `Build/` ve IDE dosyalarını dışarıda
bırakıyor. Unity `Library`'yi `Assets` + `ProjectSettings`'ten kendisi kuruyor;
gigabaytlarca import önbelleğini göndermek hem gereksiz hem zararlı (iki
makinenin önbelleği birbirini tutmuyor).

**Bunun görünen bedeli var:** depoyu başka bir makineye klonlayınca Unity'nin
ilk açılışı uzun sürüyor, çünkü bütün varlıkları yeniden import ediyor. Bu bir
hata değil, `Library` yokluğunun doğal sonucu — beklenmezse "proje bozuk" diye
okunuyor.

### Klonlanan projede ÇALIŞTIRILMASI gereken hiçbir araç yok

Sahne, prefablar, lightmap ve occlusion verisi depoda. Yani başka bir makinede
bölüm 7'deki "sıfırdan kurulum sırası" **gerekmiyor** — o liste yalnızca her
şey bozulduğunda geçerli. Klonla, aç, bekle, Play.

### Ne zaman commit'lenir

Bölüm 0 zaten "düzenleme sırasında ara ara commit" diyor. GitHub gelince
pratik kural netleşti: **her oynanabilir duruma geldiğinde.** Bu oturumdaki
dört geri bildirim turu dört ayrı commit oldu ve bir şey bozulduğunda hangi
turun bozduğu tek bakışta görüldü — tek büyük commit olsaydı dördü birbirine
karışırdı.
