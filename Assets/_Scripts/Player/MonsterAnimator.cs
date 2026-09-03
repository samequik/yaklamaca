using UnityEngine;

/// <summary>
/// Canavar modelinin animasyonlarını sürer.
///
/// Hız, eğilme ve bakış IK ortak tabanda (`CharacterAnimatorBase`) — kaçanla
/// birebir aynı iş. Burada yalnızca canavara özel olan kalıyor: saldırının iki
/// aşamalı tetikleyicisi.
///
/// Saldırı tetikleyicilerini `MonsterAttack` çağırıyor — biri girdiden
/// (atılma), diğeri sunucunun isabet onayından (yakalama). Bkz. CLAUDE.md
/// bölüm 4 ve 14.
/// </summary>
public class MonsterAnimator : CharacterAnimatorBase
{
    public const string AttackTrigger = "Attack";
    public const string KillTrigger = "Kill";

    private static readonly string[] AllTriggers = { AttackTrigger, KillTrigger };

    protected override string[] Triggers => AllTriggers;

    // ---------- MonsterAttack'in çağırdıkları ----------

    /// <summary>
    /// Sol tık: saldırı (atılıp tutamama). Girdiden, anında.
    ///
    /// Ayrı bir "ıskaladı" tetikleyicisi yok — saldırı klibi zaten atılıp
    /// tutamamayı ve kalkmayı içeriyor. İsabet gelirse `Kill` araya girip onu
    /// kesiyor.
    /// </summary>
    public void PlayAttack() => SetTriggerExclusive(AttackTrigger);

    /// <summary>Sunucu isabeti onayladı: yakalama ve yumruklama.</summary>
    public void PlayKill() => SetTriggerExclusive(KillTrigger);
}
