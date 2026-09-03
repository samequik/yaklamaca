using UnityEngine;

/// <summary>
/// Role göre hareket ayarları. Canavar ve kaçan farklı hızlarda koşar ve denge
/// bu iki sayıdan kurulur — prefab'a gömülü kalırsa her denemede sahnedeki
/// objeyi elle düzenlemek gerekirdi.
///
/// Değerler Source biriminde (unit/saniye), bkz. PlayerController.
/// </summary>
[CreateAssetMenu(menuName = "Yakalamaca/Hareket Profili", fileName = "MovementProfile")]
public class MovementProfile : ScriptableObject
{
    [Header("Hız (unit/saniye)")]
    public float walkSpeed = 200f;
    public float sprintSpeed = 400f;

    [Tooltip("Eğilirken hız = walkSpeed × bu değer. Canavar eğilirken de biraz hızlı olmalı.")]
    [Range(0.1f, 1f)]
    public float crouchSpeedMultiplier = 0.35f;

    [Header("Kayma")]
    [Tooltip("Kayma başlarken mevcut yöne eklenen hız (u/s).")]
    public float slideBoost = 60f;

    [Tooltip("Kayarken uygulanan sürtünme. Düşük değer = daha uzun kayma. Normal sürtünme 5.5.")]
    public float slideFriction = 1.2f;

    // Aşağıdakiler eskiden profilin dışındaydı; oradaki not "ikisi de hızla
    // orantılı çalışıyor, role göre değişmesi gerekmiyor" diyordu. Canavara
    // araba benzeri bir hareket verilince o gerekçe düştü: artık iki rolün
    // ivmelenme ve durma karakteri **kasten** farklı.
    [Header("İvme ve sürtünme")]
    [Tooltip("sv_accelerate. Yüksek = anında istenen hıza geçiş (kaçan, 14). " +
        "Düşük = hıza yavaş yavaş ulaşma, araba gibi (canavar).")]
    public float accelerate = 14f;

    [Tooltip("sv_friction. Yüksek = çabuk durma (kaçan, 5.5). Düşük = hızın " +
        "üstünde kalması, tekerlek gibi (canavar).")]
    public float friction = 5.5f;

    [Tooltip("Havadayken ivme. Airstrafe'in kaynağı.")]
    public float airAccelerate = 100f;

    [Header("Zıplama")]
    [Tooltip("Kapalıysa bu rol zıplayamaz — dolayısıyla bhop da yapamaz. " +
        "Canavarda kapalı: hızını momentum hilesinden değil, düz koridorda " +
        "ivmelenerek kazanmalı.")]
    public bool canJump = true;

    [Header("Bakış")]
    [Tooltip("Aşağı bakış sınırı (derece). 89 = serbest. Canavarda ~55: modelin " +
        "kafası bakış yönüne dönüyor, tam dibe bakınca gövdenin içine giriyor.")]
    public float maxLookDownAngle = 89f;

    [Header("Hız birikimi")]
    [Tooltip("Kesintisiz koşunca en yüksek hıza EKLENEN pay (u/s). 0 = kapalı. " +
        "Canavarın asıl silahı bu: normal hızda başlıyor, koştukça açılıyor.")]
    public float boostSpeed;

    [Tooltip("Payın tamamen dolması için gereken kesintisiz koşu süresi (saniye).")]
    public float boostBuildTime = 3.5f;

    [Tooltip("Koşu kesilince payın boşalma süresi (saniye). Doldurmaktan hızlı " +
        "olmalı — kazanması emek, kaybetmesi kolay.")]
    public float boostDecayTime = 1.2f;

    [Tooltip("Pay ancak bu hızın üstünde koşarken doluyor (u/s). Yürüyerek " +
        "birikmemeli, yoksa canavar sinsice dolaşıp hız depolar.")]
    public float boostMinSpeed = 380f;

    [Header("Çarpışma")]
    [Tooltip("Duvara KAFA KAFAYA girilen hız bu eşiği aşarsa hız kesiliyor (u/s). " +
        "0 = kapalı (kaçan). Sıyırarak geçmek cezalandırılmıyor; sadece dosdoğru " +
        "toslamak duruyor — araba gibi.")]
    public float crashSpeed;

    [Tooltip("Çarpmadan sonra korunan yatay hız oranı. 0 = tam duruş.")]
    [Range(0f, 1f)]
    public float crashSpeedRetained;
}
