namespace DryTown.Core;

public enum HoodState { Free, Jailed, Dead, Gone }

public enum BusinessKind { Grocer, Diner, Barber, Tailor, Garage, Laundry, Hotel, Pharmacy, PoolHall, Warehouse }

public enum RacketKind { None, Speakeasy, Numbers, LoanShark, Still }

public sealed class Hood
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int GangId { get; set; }

    // Skills, 1 to 10.
    public int Intimidation { get; set; }
    public int Muscle { get; set; }
    public int Brains { get; set; }
    public int Stealth { get; set; }

    // 0 to 100. Low loyalty plus high ambition is how gangs split.
    public int Loyalty { get; set; }
    public int Ambition { get; set; }

    public int Wage { get; set; }
    public HoodState State { get; set; } = HoodState.Free;
    public int JailWeeks { get; set; }
    public int JoinedWeek { get; set; }

    public bool IsAvailable => State == HoodState.Free;
    public bool IsActive => State is HoodState.Free or HoodState.Jailed;
    public int Strength => Intimidation + Muscle;
}

public sealed class Business
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public BusinessKind Kind { get; init; }

    /// <summary>Weekly takings in dollars before protection.</summary>
    public int Takings { get; init; }

    /// <summary>How hard the owner is to lean on, 1 to 10.</summary>
    public int Toughness { get; init; }

    public int ProtectorGangId { get; set; } = -1;
    public int HandlerHoodId { get; set; } = -1;
    public int ProtectionRate { get; set; }

    /// <summary>0 to 100. Rises with high rates and violence; high resentment makes owners talk to police.</summary>
    public int Resentment { get; set; }

    public RacketKind Racket { get; set; } = RacketKind.None;
    public int ShutWeeks { get; set; }

    public bool IsProtected => ProtectorGangId >= 0;
    public bool IsOpen => ShutWeeks == 0;
}

public sealed class Gang
{
    public int Id { get; init; }
    public required string Name { get; set; }
    public bool IsPlayer { get; init; }
    public bool Alive { get; set; } = true;
    public int FoundedWeek { get; init; }
    public int DissolvedWeek { get; set; } = -1;

    public int BossHoodId { get; set; } = -1;
    public long Cash { get; set; }

    /// <summary>Police attention, 0 to 100.</summary>
    public int Heat { get; set; }

    /// <summary>AI temperament, 0 to 1. Higher means more willing to muscle into rival turf.</summary>
    public double Aggression { get; init; }

    public int ConsecutiveUnpaidWeeks { get; set; }
}

public sealed class WeekLedger
{
    public long Protection { get; set; }
    public long Rackets { get; set; }
    public long Wages { get; set; }
    public long Spending { get; set; }
    public long Fines { get; set; }
    public long Net => Protection + Rackets - Wages - Spending - Fines;
}

public enum EventKind
{
    Extorted, ExtortFailed, Takeover, TakeoverRepelled, HoodKilled, HoodJailed, HoodReleased,
    Raid, Squeal, RacketOpened, Recruited, Deserted, Breakaway, NewGang, GangDissolved,
    Succession, Bribe, Era, Lapsed,
}

public sealed record GameEvent(int Week, EventKind Kind, int GangId, string Text);
