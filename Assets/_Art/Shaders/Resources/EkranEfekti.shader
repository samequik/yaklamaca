// Yakalamaca'nın korku kaplaması — Built-in RP tam ekran efekti.
//
// Paket YOK (bölüm 0'ın bağımlılık kuralı): Built-in'in kendi OnRenderImage
// yolu bunun için yeterli. Post Processing Stack ya da URP'nin Volume sistemi
// gerekmiyor ve ikisi de projeye girmemeli.
//
// **Hiçbir efekt YÖN BİLDİRMİYOR.** Hepsi ekranın merkezine göre simetrik.
// Kalp atışının 2B olma kuralıyla (bölüm 12) aynı gerekçe: canavarın hangi
// tarafta olduğunu söyleyen bir efekt gerilimi radara çevirir.
//
// **Gren SIFIR ORTALAMALI** (n - 0.5). Ortalama parlaklığı değiştirmiyor,
// yani bölüm 5'in "fenersiz görülmemeli" ölçütünü delmiyor. Gürültü
// geometriyle ilişkisiz olduğu için karanlıkta bir şey de ele vermiyor.
//
// **Retro PSP görünümü (2026-09-19).** Bu geçiş artık DÜŞÜK çözünürlüklü bir
// hedefe çiziliyor (1080p'de 960×540) ve `ScreenEffects` sonucu nokta
// süzgeciyle ekrana büyütüyor. Yani her efekt büyük piksel başına bir kez
// hesaplanıyor: gren de, renk azaltma deseni de piksellerle aynı boyda.
// `_RetroSize` o hedefin boyu — piksel hesabı yapan her satır `_ScreenParams`
// yerine onu kullanıyor, çünkü `_ScreenParams` KAMERANIN boyunu veriyor,
// üstüne çizdiğimiz küçük hedefi değil.
Shader "Yakalamaca/EkranEfekti"
{
    Properties
    {
        _MainTex ("Kaynak", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            // 3.0: renk azaltma ve blok ortalaması komut sayısını artırdı;
            // 2.5'in (varsayılan) sınırına takılmasın. Hedef PC olduğu için
            // dışarıda kalan bir platform yok.
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float _Vignette;       // kenar karartmanın gücü (0-1)
            float _VignetteStart;  // merkeze uzaklık: kararmanın başladığı yer
            float _VignetteEnd;    // tam karardığı yer
            float _Grain;          // gren genliği
            float _GrainFloor;     // grenin tam güce ulaştığı parlaklık
            float _GrainSeed;      // her karede değişiyor, gren animasyonu
            float _Aberration;     // renk ayrışması (UV birimi)
            float _Pixelate;       // 0/1 = kapalı, >1 = blok kenarı (piksel)
            float _Desaturate;     // renk kaybı (0-1)
            float _Contrast;       // 1 = dokunma, >1 = aydınlık parlar karanlık çöker
            float _ContrastPivot;  // kontrastın döndüğü eksen — sahnenin ORTA parlaklığı
            float _Glitch;         // yatay bant kayması (VHS)
            float _Bloom;          // parlak yerlerin taşma gücü
            float _BloomThreshold; // bu parlaklığın üstü taşıyor
            float _BloomRadius;    // taşmanın yarıçapı (UV)
            float4 _RetroSize;     // çizilen hedefin boyu: xy = piksel, zw = 1/xy
            float _RetroBlock;     // 1 = her büyük piksel kaynağın ORTALAMASI
            float _ColorLevels;    // kanal başına renk seviyesi; 2'nin altı = kapalı

            float Noise(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Kaynağı okur. Retro açıkken tek bir nokta değil, büyük pikselin
            // kapladığı bloğun ORTALAMASI alınıyor: dört çift doğrusal örnek,
            // bloğun dört çeyreğinde. 2× küçültmede (1080p) bu, bloktaki 4
            // kaynak pikselin tam ortalaması; 4× küçültmede 16'sının.
            //
            // **Neden tek nokta değil.** Tek nokta gerçek bir düşük
            // çözünürlüklü çizimin aynısı olurdu ama hızlı harekette (bölüm 1'in Source
            // hızları) ince ayrıntılar — duvar derzleri, ızgaralar — her
            // karede görünüp kaybolup kaynıyordu. Ortalama, pikselleri iri ve
            // net bırakıp o kaynamayı sakinleştiriyor.
            //
            // İki dal da AYNI değişkene yazıyor, erken `return` yok: erken
            // dönüşlü sürüm derleyicide "başlatılmamış değişken" uyarısı
            // veriyordu (zararsız ama Unity konsolunda her açılışta görünürdü).
            float3 SampleSource(float2 uv)
            {
                float3 col;

                if (_RetroBlock > 0.5)
                {
                    float2 q = _RetroSize.zw * 0.25;

                    col = (tex2D(_MainTex, uv + float2(-q.x, -q.y)).rgb
                         + tex2D(_MainTex, uv + float2( q.x, -q.y)).rgb
                         + tex2D(_MainTex, uv + float2(-q.x,  q.y)).rgb
                         + tex2D(_MainTex, uv + float2( q.x,  q.y)).rgb) * 0.25;
                }
                else
                {
                    col = tex2D(_MainTex, uv).rgb;
                }

                return col;
            }

            // 2×2 Bayer değeri (0-3), p her eksende 0 ya da 1. Tamsayı bit
            // işlemi yok: shader modeli 2.5'te de derlensin diye aritmetik.
            float Bayer2(float2 p)
            {
                return 2.0 * p.x + 3.0 * p.y - 4.0 * p.x * p.y;
            }

            // 4×4 Bayer eşiği (0-1 arası, ortalaması 0.5). Sıralı titreşim
            // (ordered dithering): eski konsolların 16-bit renk modunda
            // gradyanların üstünde gördüğün düzenli ızgara desenini üreten şey.
            float Bayer4(float2 pixel)
            {
                float2 p = fmod(pixel, 4.0);
                float value = 4.0 * Bayer2(fmod(p, 2.0)) + Bayer2(floor(p * 0.5));
                return (value + 0.5) / 16.0;
            }

            // Doğrusal ↔ gamma, TAM formül. Unity'nin hızlı sürümleri
            // (`GammaToLinearSpace`) en karanlık tonlarda gerçek değerin
            // yarısına kadar sapıyor — renk azaltma tam orada, siyaha yakın
            // basamaklarda çalışıyor ve bu oyunda ekranın çoğu o tonlarda.
            // Yaklaşık dönüşüm karanlık basamakları sessizce kısar, ortalama
            // parlaklığı korumayan bir titreşim çıkardı.
            float3 ToGamma(float3 c)
            {
                return float3(LinearToGammaSpaceExact(c.r),
                    LinearToGammaSpaceExact(c.g), LinearToGammaSpaceExact(c.b));
            }

            float3 ToLinear(float3 c)
            {
                return float3(GammaToLinearSpaceExact(c.r),
                    GammaToLinearSpaceExact(c.g), GammaToLinearSpaceExact(c.b));
            }

            // Tek geçişli bloom: eşiğin üstündeki parlaklığı çevreye yayıyor.
            //
            // **Karanlık bir oyunda "lambaları patlatmanın" doğru yolu bu.**
            // Kontrastı zorlamak her şeyi birden oynatıyor ve karanlığı da
            // bozuyor; bloom ise TOPLAMSAL ve eşikli: yalnızca zaten parlak
            // olan yerlerden ışık taşıyor, ambient'e (0.006) hiç dokunmuyor.
            // Yani bölüm 5'in "fenersiz görülmemeli" ölçütü etkilenmiyor.
            //
            // Örnekler altın açı sarmalında: düzenli halkalar gözle görülür
            // bant üretiyor, sarmal onları dağıtıyor. Çok geçişli bir bulanık
            // daha yumuşak olurdu ama ayrı RenderTexture'lar ve ek blit'ler
            // demek — lamba halesi için gereğinden pahalı.
            float3 BloomSample(float2 uv, float aspect)
            {
                float3 sum = float3(0.0, 0.0, 0.0);

                [unroll]
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * 2.39996;                  // altın açı
                    float radius = sqrt((i + 0.5) / 12.0);      // eşit alan
                    float2 offset = float2(cos(angle) / aspect, sin(angle))
                        * radius * _BloomRadius;

                    float3 s = tex2D(_MainTex, uv + offset).rgb;
                    sum += max(s - _BloomThreshold, 0.0);
                }

                return sum / 12.0;
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;

                // Vinyet ve ayrışma geometrisi ORİJİNAL uv'den okunuyor:
                // pikselleme yalnızca görüntüyü bloklara ayırmalı, kenar
                // karartmasını basamaklandırmamalı.
                float2 centered = input.uv - 0.5;
                // 1.414 ile köşe ~1.0 oluyor: eşikler en boy oranından bağımsız.
                float dist = length(centered) * 1.41421356;

                // Pikselleme: UV'yi bloklara oturtuyor. Kapalıyken (_Pixelate
                // <= 1) hiçbir maliyeti yok. Blok boyu çizilen HEDEFİN
                // pikseliyle ölçülüyor (`_RetroSize`), ekranınkiyle değil.
                if (_Pixelate > 1.0)
                {
                    float2 blocks = max(_RetroSize.xy / _Pixelate, float2(1.0, 1.0));
                    uv = (floor(uv * blocks) + 0.5) / blocks;
                }

                // VHS parazit: ekranı yatay bantlara bölüp bazılarını yana
                // kaydırıyor. Bantlar _GrainSeed'e bağlı, yani her karede
                // başkaları kayıyor. Bu bir YER DEĞİŞTİRME efekti — parlaklığa
                // hiç dokunmadığı için karanlık sahnede de görünüyor,
                // çarpımsal efektlerin aksine.
                if (_Glitch > 0.0001)
                {
                    float band = floor(uv.y * 26.0);
                    float pick = Noise(float2(band, floor(_GrainSeed) * 0.017));
                    float active = step(0.80, pick);
                    float shift = (Noise(float2(band, floor(_GrainSeed) * 0.031)) - 0.5);
                    uv.x = saturate(uv.x + shift * _Glitch * active);
                }

                float3 col = float3(0.0, 0.0, 0.0);

                // Renk ayrışması merkezde SIFIR, kenara doğru artıyor —
                // ucuz kamera hissi. Ortada sıfır olması önemli: nişangahın
                // olduğu yer keskin kalmalı.
                if (_Aberration > 0.00001)
                {
                    float2 shift = centered * _Aberration * dist;
                    col.r = SampleSource(uv + shift).r;
                    col.g = SampleSource(uv).g;
                    col.b = SampleSource(uv - shift).b;
                }
                else
                {
                    col = SampleSource(uv);
                }

                // **Kontrast, karanlık bir oyunda en çok işe yarayan ayar.**
                // 0.5 ekseninde açıldığı için 0.5'in ALTI daha da kararıyor,
                // üstü parlıyor: fener konisi keskinleşiyor, çevresi çöküyor.
                // Ambient 0.006 olduğu için taban aydınlığı yükselmiyor, yani
                // bölüm 5'in "fenersiz görülmemeli" ölçütü delinmiyor —
                // tersine güçleniyor.
                col = (col - _ContrastPivot) * _Contrast + _ContrastPivot;

                // Bloom kontrasttan SONRA, doygunluktan ÖNCE: kontrast
                // lambaları zaten yükseltmiş oluyor, hale onun üstüne biniyor
                // ve renk kaybı ikisine birden uygulanıyor.
                if (_Bloom > 0.0001)
                    col += BloomSample(uv, _RetroSize.x / max(_RetroSize.y, 1.0)) * _Bloom;

                float grey = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, float3(grey, grey, grey), saturate(_Desaturate));

                float edge = 1.0 - smoothstep(_VignetteStart, _VignetteEnd, dist);
                col *= lerp(1.0, edge, saturate(_Vignette));

                // **Gren PARLAKLIĞA bağlı.** Sabit genlikli gren simsiyah bir
                // zemine binince göreli kontrast devasa oluyor ve ekran statik
                // gibi görünüyor — oyunun büyük kısmı karanlık olduğu için de
                // her yerde. Maske karanlıkta greni tamamen kapatıyor,
                // aydınlıkta tam güce çıkarıyor.
                //
                // Gerçek kamera gürültüsünün tersi (o karanlıkta artar) ama
                // burada ölçüt gerçekçilik değil göz konforu; ayrıca vinyetle
                // kararan köşeler de kendiliğinden temizleniyor.
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                float grainMask = saturate(lum / max(_GrainFloor, 0.0001));

                float n = Noise(uv * _RetroSize.xy + _GrainSeed);
                col += (n - 0.5) * _Grain * grainMask;

                col = max(col, 0.0);

                // **Renk azaltma — eski konsolların 16-bit rengi.** Her kanal
                // `_ColorLevels` basamağa iniyor (bugün 64 = kanal başına 6
                // bit; PSP'nin kendi 16-bit rengi 32, yani 5 bit) ve
                // basamaklar arası 4×4 Bayer deseniyle titreştiriliyor. Sisli
                // gradyanlar pürüzsüz değil, ince bir ızgara desenli
                // bantlarla sönüyor — PSP görünümünün ikinci yarısı bu.
                //
                // **GAMMA uzayında yapılıyor.** Proje Linear renk uzayında ve
                // shader'daki değerler doğrusal; doğrusal uzayda eşit
                // basamaklar karanlık tonları birkaç kaba basamağa yığardı —
                // bu karanlık oyunda ekranın çoğu tam orası. Gerçek donanım
                // da basamakları ekrana giden (gamma) değerde tutuyordu.
                //
                // Sıralı titreşim ORTALAMAYI korur: karanlık bir bölge ne
                // aydınlanıyor ne kararıyor, yani bölüm 5'in "fenersiz
                // görülmemeli" ölçütü olduğu gibi kalıyor.
                //
                // Desen, çizilen hedefin pikseline oturuyor (`_RetroSize`):
                // büyük piksellerle aynı boyda, ekranın ince pikselinde değil.
                if (_ColorLevels > 1.5)
                {
                    float steps = _ColorLevels - 1.0;
                    float threshold = Bayer4(floor(input.uv * _RetroSize.xy));

                    col = saturate(col);
#if !defined(UNITY_COLORSPACE_GAMMA)
                    col = ToGamma(col);
#endif
                    col = floor(col * steps + threshold) / steps;
#if !defined(UNITY_COLORSPACE_GAMMA)
                    col = ToLinear(col);
#endif
                }

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
