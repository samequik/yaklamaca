/// <summary>Turun hangi aşamada olduğu.</summary>
public enum RoundPhase
{
    Waiting, // tur başlamadı
    Playing, // süre işliyor
    Ended    // kazanan belli
}

/// <summary>Bir katılımcının turdaki rolü.</summary>
public enum RoundRole
{
    None,
    Monster,
    Runner
}

/// <summary>Turun sonucu.</summary>
public enum RoundResult
{
    None,
    MonsterWins, // süre dolmadan tüm kaçanlar elendi
    RunnersWin,  // süre doldu, hayatta kalan var
    Aborted      // canavar oyundan ayrıldı, tur geçersiz
}
