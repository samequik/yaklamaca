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

            float Noise(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
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
                // <= 1) hiçbir maliyeti yok.
                if (_Pixelate > 1.0)
                {
                    float2 blocks = max(_ScreenParams.xy / _Pixelate, float2(1.0, 1.0));
                    uv = (floor(uv * blocks) + 0.5) / blocks;
                }

                float3 col = float3(0.0, 0.0, 0.0);

                // Renk ayrışması merkezde SIFIR, kenara doğru artıyor —
                // ucuz kamera hissi. Ortada sıfır olması önemli: nişangahın
                // olduğu yer keskin kalmalı.
                if (_Aberration > 0.00001)
                {
                    float2 shift = centered * _Aberration * dist;
                    col.r = tex2D(_MainTex, uv + shift).r;
                    col.g = tex2D(_MainTex, uv).g;
                    col.b = tex2D(_MainTex, uv - shift).b;
                }
                else
                {
                    col = tex2D(_MainTex, uv).rgb;
                }

                // **Kontrast, karanlık bir oyunda en çok işe yarayan ayar.**
                // 0.5 ekseninde açıldığı için 0.5'in ALTI daha da kararıyor,
                // üstü parlıyor: fener konisi keskinleşiyor, çevresi çöküyor.
                // Ambient 0.006 olduğu için taban aydınlığı yükselmiyor, yani
                // bölüm 5'in "fenersiz görülmemeli" ölçütü delinmiyor —
                // tersine güçleniyor.
                col = (col - 0.5) * _Contrast + 0.5;

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

                float n = Noise(uv * _ScreenParams.xy + _GrainSeed);
                col += (n - 0.5) * _Grain * grainMask;

                return fixed4(max(col, 0.0), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
