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

    public int LotId { get; init; } = -1;

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

    public int HqLotId { get; set; } = -1;
}

/// <summary>
/// A lieutenant and the men who answer to him. A crew can be sent as one team: it hits harder
/// and guards better than one man, but every member is tied up for the week. Lieutenants set
/// the mood of their crew, and an ambitious one who breaks away takes his crew with him.
/// </summary>
public sealed class Crew
{
    public const int MaxMembers = 3;

    public int Id { get; init; }
    public int GangId { get; set; }
    public int LieutenantHoodId { get; set; }

    /// <summary>The men under the lieutenant, not counting him.</summary>
    public List<int> MemberIds { get; init; } = new();

    public IEnumerable<int> Everyone => MemberIds.Prepend(LieutenantHoodId);
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

public sealed record GameEvent(int Week, EventKind Kind, int GangId, string Text, int Tick = 0);

public enum ActionKind
{
    /// <summary>A hood leans on a shopkeeper.</summary>
    Extort,
    /// <summary>A hood tries to take a business from a rival; the defender fights back.</summary>
    Takeover,
    /// <summary>A hood sets up a racket in a back room.</summary>
    Racket,
    /// <summary>A hood stands guard at a business all week.</summary>
    Guard,
    /// <summary>Police come from the precinct house.</summary>
    Raid,
    /// <summary>Collectors make the Sunday rounds.</summary>
    Collect,
}

public enum ActionResult { None, Success, Failed, Won, Lost, BackedOff, Killed, Arrested }

/// <summary>
/// One thing that happened on the street, for the live view to animate. A hood walks from
/// <see cref="FromLot"/> to <see cref="ToLot"/>, arriving at <see cref="Tick"/>.
/// </summary>
public sealed record ScriptAction(
    ActionKind Kind, int GangId, int HoodId, int FromLot, int ToLot, int BusinessId,
    ActionResult Result, string Text,
    int DefenderGangId = -1, int DefenderHoodId = -1, int CasualtyHoodId = -1, int Tick = 0)
{
    /// <summary>Men who went along as backup, not counting <see cref="HoodId"/>.</summary>
    public IReadOnlyList<int> Backup { get; init; } = Array.Empty<int>();
}
