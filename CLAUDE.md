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
ölüm) · sürgülü kapılar ve düğmeler (bölüm 15) · izleyici modu · terminal +
kaçış sistemi (bölüm 11) · harita giydirme ve prop dağıtımı · mağara yankısı
(bölüm 12) · menü, lobi, ayarlar ve tuş atamaları (bölüm 13) · canavar modeli
ve animasyonları (bölüm 14) · katman düzeni ve daraltılmış fizik maskeleri
(bölüm 16) · kaçan modeli, animasyonları ve yakalanma animasyonu (bölüm 17) ·
çıkış görünümü ve on adımlık kilit paneli (bölüm 18) · git deposu.

**Işık tarafı TAMAMEN BİTTİ** (2026-09-03), ikisi de sahne dosyasından
doğrulandı:

- **Lightmap.** `Assets/_Scenes/SampleScene/` altında `LightingData.asset`, bir
  lightmap atlası ve bir yansıma probe'u var. Sahnedeki 17 ışığın 16'sı
  **`Mixed` (Shadowmask)**, gölgeleri Soft; tek gerçek zamanlı olan `Fener`.
  Lamba şiddeti 0.75.
- **Occlusion culling.** Sahne artık veriye bağlı:
  `m_OcclusionCullingData: {fileID: 36300000, guid: 3945ca91…}`.

**Oyunda doğrulandı (2026-09-04):** lambalar aydınlatıyor, kapalı kapı ışığı
kesiyor, oyuncular gölge düşürüyor, lambaların arası zifiri kalıyor.

Buraya gelene kadar dört ayrı arıza vardı ve hepsi ayrı ayrı belgelendi
(bölüm 3): pişirme sonrası sahnenin kaydedilmemesi · giydirme bayrağının
prefabın alt objelerine yazılmaması · iki modun farklı düşüş eğrisi kullanması ·
kapıların static olmadığı için pişmiş ışıkta hiç görünmemesi.

> **Bu iki madde 2026-09-03'e kadar "16 ışık hâlâ Mixed" ve "occlusion hiç
> pişirilmedi" diye yazıyordu; ikisi de yanlıştı.** Sahne dosyasındaki
> `m_Lightmapping: 2` **Baked** demek, Mixed değil (`LightmapBakeType`:
> Mixed=1, Baked=2, Realtime=4) — ışıklar en baştan doğru pişmişti. Occlusion
> da pişmişti: `OcclusionCullingData.asset` diskte duruyordu ve commit'e bile
> girmişti, ama pişirme sonrası sahne kaydedilmediği için referansı
> kaybolmuştu. Düzeltildiğinde asset **bayt bayt aynı** kaldı, yani veri baştan
> beri geçerliydi — eksik olan tek şey sahnedeki o satırdı.
>
> Ders: durumu belgeye bakarak değil, **sahne dosyasından okuyarak** doğrula.
> "Diskte dosya var" pişmiş demek değil.

**Hiç başlanmamış:** yakınlık sesi (kalp atışı — **ses dosyası oyuncudan
gelecek, sentezlenmeyecek**) · yakınlık sesli sohbet.

**Kapsam dışı bırakıldı:** fener pili. Fener açık/kapalı olarak kalıyor, şarj
ya da tükenme mekaniği olmayacak (2026-09-03 kararı).

**Test edilip çalıştığı doğrulanan (2026-08-29):** terminal doldurma, yön tuşu
sınavı, kilitlenme, kilit açma örüntüsü, canavarın kilitleme yetkisi, çıkış
kapısı · menü, lobi ve ayarlar akışının tamamı (lobi kurma, kodla katılma,
kadro senkronu, hazır işareti, tur başlatma, tur bitince lobiye dönüş, odadan
ayrılma, tuş atama ekranı) · canavarın araba modeli hareketi · canavar modeli,
animasyonları ve saldırı akışı.

**Bu oturumda oynanışta doğrulananlar (2026-09-03):** çıkış akışının TAMAMI —
kilit paneli, on adımlık yön dizilimi, yanlış tuşta başa sarma, dizilim bitince
kapının açılması, kapıdan geçen kaçanın izleyici moduna düşmesi · çıkış
kapısının kit gövdesi ve sahanlığı · eğilme kamerası · kaçan modelinin
locomotion animasyonları · materyal onarımı (canavar ve kaçan artık kendi
dokularıyla görünüyor) · düğmelerin tek tek basılması · fenerin ağ üzerinden
doğru çalışması.

**Bu oturumda bulunan ama HENÜZ ÇÖZÜLMEYEN:** yakalama ve ölme animasyonlarının
göreli duruşu (bölüm 17, bilinen eksikler).

**Harita elden geçirildi (2026-09-03).** Elle düzenlendi: duvar panelleri, zemin
karoları, EXIT tabelası eklendi, terminaller elle yerleştirildi. Bundan sonra
haritayı silen araçlar çalıştırılmayacak — bkz. bölüm 0'daki kural.

**Sırada lightmap + occlusion pişirme var.** İkisi de haritaya eklenen her
static parçayla geçersiz olduğu için sona bırakılmıştı; harita kesinleştiğine
göre artık yapılabilir (bölüm 3 ve 10).

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
düzenleme öncesi hâli. Kayıt noktaları `git log`, son kayda dönüş
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
| Taban koşu | 400 u/s | **420 u/s** |
| İvme (`accelerate`) | 14 | **14 — aynı** |
| Sürtünme (`friction`) | 5.5 | **5.5 — aynı** |
| Hız payı | yok | **+180 u/s**, 3.5 sn koşuyla dolar |
| Zıplama | var | **yok** |
| Duvara toslama | ceza yok | **hız ve pay sıfır** |
| Aşağı bakış | serbest | **55°** |

**İlk deneme yanlıştı ve düzeltildi.** İvme 0.8'e düşürülmüştü; bu canavarı
duruştan kalkarken de ağırlaştırıyordu, her yavaşlama bir cezaya dönüşüyordu.
Doğrusu tabanı normal bırakıp **üstüne** eklemek — araba da duruştan seyir
hızına çabuk çıkar, yavaş olan kısım SON hıza varmaktır.

Mantığı: canavar hızını *momentum hilesinden* değil, **kesintisiz koşarak**
kazanıyor. Zıplayamadığı için bhop da yapamıyor. Pay yalnızca 380 u/s üstünde
koşarken doluyor (yürüyerek sinsice hız depolayamıyor) ve koşu kesilince 1.2
saniyede boşalıyor: kazanması emek, kaybetmesi kolay.

**Çarpışmanın ölçütü temas değil, yüzeye GİREN hız bileşeni**
(`PlayerController.TryCrash`). Koridorda duvarı sıyırarak koşmak neredeyse
sıfır bileşen üretiyor ve cezalandırılmıyor; dosdoğru toslamak tam hızı
üretiyor. Zemin hariç tutuluyor — yerçekimi her karede zemine bastırdığı için
zemin de sayılsaydı yürürken sürekli duruyorduk.

Çarpmanın asıl cezası hız değil **pay**: anlık hızı kaybetmek birkaç saniyelik
iş, payı yeniden doldurmak koridoru baştan koşmak demek. Labirent böylece
canavarın rakibi oluyor, kaçanın keskin dönüşleri gerçek bir savunma hâline
geliyor.

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

**Karanlık oynanışın parçası.**
Ortam ışığı ~0.018, sis yoğunluğu 0.045 (görüş ~25 m), 14 loş lamba ve
aralarında zifiri bölgeler. Sis süs değil: 48 metreye uzayabilen koridorlarda
görüş kısıtlanmazsa labirent labirent olmaktan çıkar.

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
| Menü Kur | Menü, lobi, ayarlar ve tuş atama ekranları (bkz. bölüm 13) |
| Terminal ve Çıkış Kur | 5 terminali duvarlara, 2 çıkışı en uzak iki gediğe kurar |
| Sesleri Yerleştir | Sesleri adlandırır, mono yapar, kapılara bağlar |
| Mağara Yankısı Kur (reverb) | Yankı bölgesi + mesafeye bağlı yankı eğrisi (bkz. bölüm 12) |
| Canavar Modelini Kur | Model + animasyonlar + animator + prefaba bağlama (bkz. bölüm 14) |
| Kaçan Modelini Kur | Banana Man + animasyonlar + animator + prefaba bağlama (bkz. bölüm 17) |
| Hareket Profillerini Sıfırla | Kaçan = Source, canavar = araba modeli (bkz. bölüm 1) |
| Katmanları Kur | Dört katman tanımlar, sahneye ve prefaba atar, maskeleri daraltır (bkz. bölüm 16) |
| Işığı Pişir (lightmap) | Lightmap UV'si üretir, ışıkları Baked yapar, probe kurar, pişirir |
| Test Botu Ekle/Kaldır | Tek başına test için sahte kaçan |
| Hataları Temizle (Sahne Onarımı) | Eksik NetworkIdentity ekler, ağ öncesi artıkları söker |

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
5b. Menü Kur                      → ağ kurulumundan SONRA (bkz. bölüm 13)
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
2. **Prototip arayüz.** `RoundHud`, `PlayerInteractor` ve terminal ekranı
   `OnGUI` kullanıyor. IMGUI her zaman Canvas'ın üstünde kaldığı için menü
   açılırken bunların elle kapatılması gerekiyor; gerçek UI'a (TextMeshPro +
   Canvas) geçilince hepsi silinecek.
3. ~~**Katman düzeni yok.**~~ **ÇÖZÜLDÜ** (2026-08-30) — dört katman kuruldu ve
   maskeler daraltıldı. Bkz. bölüm 16. Madde numarası, koddaki atıflar bozulmasın
   diye yerinde bırakıldı.
4. ~~**Kaçan kapsül, animasyon yok.**~~ **ÇÖZÜLDÜ** (2026-08-30) — Banana Man
   modeli, locomotion/eğilme/havada animasyonları ve yakalanma animasyonu
   bağlandı (bölüm 17). Kapsül yer tutucu olarak duruyor: model takılı değilse
   ona düşülüyor. Madde numarası, koddaki atıflar bozulmasın diye yerinde
   bırakıldı.
5. **Oda listesi yok.** Lobideki "herkese açık oda" fikri arayüzden kalktı:
   oda listesi tutmak bir eşleştirme sunucusu gerektiriyor (bölüm 13).
   Katılmanın tek yolu kod. Edgegap'e geçilirse kutudan çıkıyor (bölüm 10).
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

**3. Yakalama ve ölme animasyonlarının göreli duruşu — AÇIK.**

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

**ÇÖZÜLDÜ (2026-09-03) — iki ayrı sebep vardı, oynanışta doğrulanacak.**

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
| Canavar (KillerDoll) | `(0, -0.6858, 0)` | 0.7048 | **1.619 m** |
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

**6. Sesli sohbet.** Menü tarafında kalan tek şey: mikrofon ayarları ve oyun
içi kişi bazlı susturma/ses seviyesi, sesli sohbetin kendisi gelmeden
yapılamaz.

Ses şu an **tek kanal** (`AudioListener.volume`). Ayrı efekt/konuşma kanalları
bir AudioMixer gerektiriyor ve **AudioMixer script'ten oluşturulamıyor** —
Unity'nin böyle bir API'si yok, elle Audio Mixer penceresinden kurulması
gerekiyor. Bu yüzden konuşma sesi geldiğinde kişi başı ses seviyesi doğrudan
`AudioSource.volume` üzerinden verilecek; o zaman susturma ve ses seviyesi
bedava geliyor.

**Bağımlılık kararı bekliyor.** Mirror sesli sohbet getirmiyor. Üç yol:
Dissonance (Asset Store, ücretli, Mirror entegrasyonu resmi, listenin çoğu
kutudan çıkıyor) · kendimiz yazmak (`Microphone` → Opus/Concentus saf C# →
Mirror → 3B AudioSource; bölüm 0'ın "bağımlılık eklemeden önce iki kez düşün"
kuralına en uygunu) · Vivox (bulut, 3B karışımı sunucuda yaptığı için mağara
yankısıyla çelişiyor — **elenmeli**).

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

Seçenekler, yayına yaklaşınca değerlendirilecek:

| Yol | Para | Herkeste çalışır mı |
|---|---|---|
| **EOS** (Epic Online Services) | Bedava, oyuncuda Epic hesabı gerekmiyor | Evet (relay yedeği) |
| **Edgegap** — Mirror'ın içinde hazır geliyor (`Transports/Edgegap`, `Examples/EdgegapLobby`), oda listesi de kutudan çıkıyor | Ücretsiz katman + sonrası ücretli | Evet |
| NAT punchthrough (kendimiz) | Bedava | %70-85, CGNAT'ta çalışmıyor |
| Steam | 100$ giriş, sonra bedava | Evet |

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

### 11.2 Terminaller

> Karanlık uyarısı: sahne ortam ışığı ~0.018. Terminal göstergesi ve çıkış
> kapısı gibi "uzaktan görünmesi gereken" yüzeyler **ışıktan etkilenmeyen**
> materyal kullanmalı (Sprites/Default), yoksa rengi ne olursa olsun siyah
> görünürler. TrailMarkSystem de aynı sebeple öyle yapıyor.

- Haritada **5 terminal**, hepsi **duvara monte** (`ObjectiveSetup.TerminalCount`).
- **E** ile etkileşim. İlerleme **yüzde** olarak dolar.
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
| `Kapi` | Kapı açılma/kapanma |
| `Olum` | Yakalanma |
| `Bicak_Savurma`, `Bicak_Isabet` | Bıçak (hâlâ yer tutucu) |

**Adım sesi klipleri tek adım olmalı, döngü değil.** Adımlar zamanla değil kat
edilen mesafeyle tetikleniyor; koşarken kendiliğinden sıklaşıyor. Koşu adım
aralığı 1.8→2.6 m açılıyor, yoksa tempo iki katına çıkıp makineli tüfek gibi
duyuluyor.

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

### Kod = sunucunun IP adresi

Lobi kodu rastgele değil, **sunucunun IPv4 adresinin 32 harflik alfabeyle
yazılmış hâli** (`LobbyCode`). 32 bit, 7 karaktere sığıyor. Alfabede karışan
harfler yok (I, O, 0, 1) — kod sesli sohbette söylenecek.

Neden böyle: rastgele kod bir eşleştirme sunucusunda saklanmayı gerektirir, o
da ayakta tutulacak bir servis demek. Bölüm 0'ın "bağımlılık eklemeden önce
iki kez düşün" kuralı burada da geçerli.

**Sınırı açıkça bilerek kabul ettik:** bu doğrudan bağlantı. Aynı ağda çalışır;
internet üzerinden 7777/UDP yönlendirmesi ya da sanal ağ (Hamachi, Radmin)
gerekir. Katılma alanı ham IP de kabul ediyor, tam da bu yüzden.

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

**Ağ Kurulumu'nu tekrar çalıştırırsan Menü Kur'u da tekrar çalıştır.**

---

## 14. Canavar: model, animasyon ve saldırı

`Yakalamaca > Canavar Modelini Kur` her şeyi kuruyor. Elle yapılırsa on beş
Inspector alanı doldurmak gerekiyor ve `Ağ Kurulumu` prefabı sıfırdan
kurduğu için hepsi bir sonraki çalıştırmada uçuyor.

**Model:** `RamsterZ_FreeDoll` (Asset Store, ücretsiz). Rig zaten Humanoid ve
hatasız geliyor — Mixamo'ya rig için yüklemeye gerek yok, animasyonlar
`Copy From Other Avatar` ile retarget ediliyor.

**Kaçan hâlâ kapsül.** Bebek yalnızca canavarda; insan kiti sonra gelecek.
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
dibe bakınca kafa gövdenin içine giriyordu. Kaçanda sınır yok (89) — onun
kapsülünde dönecek kafa yok.

### Ayak kayması

Canavar 9.9 m/s'ye çıkıyor, Mixamo koşusu ~4 m/s ilerliyor. Oynatma hızı
orantılanıyor ama **sınırlı** (0.7–1.6): tam orantı bacakları gülünç şekilde
çırpıyor. Biraz kayma, çok hızlı animasyondan iyi.

### Bilinen eksikler

- ~~Kurbanın animasyonu yok.~~ **Çözüldü** (bölüm 17): beden ölüm klibi bitene
  kadar sahnede kalıyor ve canavarın önüne oturtuluyor.
- **Havada olma animasyonu yok.** Canavar zıplayamıyor ama düşebilir. (Kaçanda
  var — bölüm 17.)
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

Canavarda hull boyunun üstüne **1.18** çarpanı var — kovalayan şeyin olduğundan
büyük görünmesi istenen etki. Kaçanda çarpan **1**: canavar ona nişan alıyor,
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
