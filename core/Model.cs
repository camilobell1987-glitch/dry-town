namespace DryTown.Core;

public enum HoodState { Free, Jailed, Dead, Gone }

public enum BusinessKind { Grocer, Diner, Barber, Tailor, Garage, Laundry, Hotel, Pharmacy, PoolHall, Warehouse }

/// <summary>What a man carries. Better guns win fights but bring more heat.</summary>
public enum Weapon { None, Pistol, Shotgun, TommyGun }

/// <summary>What a man drives. A car gets him there faster and away when it goes wrong.</summary>
public enum Car { None, ModelT, Packard, Cadillac }

public enum RacketKind { None, Speakeasy, Numbers, LoanShark, Still, Bookie, GamblingDen, Counterfeit }

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

    /// <summary>The game week he was born, counted from the start of 1920, so usually negative.</summary>
    public int BornWeek { get; set; }

    /// <summary>Family of the boss who brought him in: they start green and loyal.</summary>
    public bool Family { get; set; }

    public int Age(int week) => (week - BornWeek) / Content.WeeksPerYear;

    public Weapon Weapon { get; set; }
    public Car Car { get; set; }

    public bool IsAvailable => State == HoodState.Free;
    public bool IsActive => State is HoodState.Free or HoodState.Jailed;

    /// <summary>What he brings to a fight: his own menace and muscle, and what he's carrying.</summary>
    public int Strength => Intimidation + Muscle + Content.Guns[Weapon].Firepower;
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

    /// <summary>The week it was last burned out, for the charred shell the map shows while it's shut.</summary>
    public int BurnedWeek { get; set; } = -1000;

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

    /// <summary>Who takes over when the boss dies or goes away for a long stretch, if he's still around and willing.</summary>
    public int HeirHoodId { get; set; } = -1;

    /// <summary>The week the gang last brought a relative into the business.</summary>
    public int LastFamilyWeek { get; set; } = -1000;

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
    Succession, Bribe, Era, Lapsed, DiedNaturally, Heir, Family, Politics,
    Arson, Hit, Robbery, Hijack, Street, Gear,
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
    /// <summary>A hood sets fire to a rival's business.</summary>
    Arson,
    /// <summary>A hood goes after a named man in a rival gang.</summary>
    Hit,
    /// <summary>A crew holds up a bank.</summary>
    Robbery,
    /// <summary>A crew stops a rival's beer truck on its way to a speakeasy.</summary>
    Hijack,
    /// <summary>A hood has a quiet word with a witness.</summary>
    Silence,
    /// <summary>Someone on the street is hired, paid off or bought.</summary>
    Hire,
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

/// <summary>Who someone on the street is. Shopkeepers and gangsters are clicked through their businesses and gangs.</summary>
public enum PersonKind { Drifter, BeatCop, Reporter, Witness }

/// <summary>
/// Someone on the street who isn't in a gang: a drifter looking for work, the beat cop, a
/// reporter working on a story about a gang, or a witness who saw one of its men do something.
/// </summary>
public sealed class StreetPerson
{
    public int Id { get; init; }
    public PersonKind Kind { get; init; }
    public required string Name { get; init; }

    /// <summary>Where he hangs about.</summary>
    public int LotId { get; set; }
    public int WardId { get; init; }

    // A drifter's skills, 1 to 10, which he keeps if he's hired.
    public int Intimidation { get; init; }
    public int Muscle { get; init; }
    public int Brains { get; init; }
    public int Stealth { get; init; }
    public int BornWeek { get; init; }

    /// <summary>A reporter's or a witness's gang, and the man the witness saw.</summary>
    public int GangId { get; init; } = -1;
    public int HoodId { get; init; } = -1;

    /// <summary>What he saw or is writing about.</summary>
    public string About { get; init; } = "";

    /// <summary>How hard he is to scare or buy, 1 to 10.</summary>
    public int Nerve { get; init; }

    /// <summary>Weeks until the drifter moves on, the reporter goes to print or the witness testifies.</summary>
    public int WeeksLeft { get; set; }

    /// <summary>The gang that has paid this beat cop to look away this week, or -1.</summary>
    public int PaidByGangId { get; set; } = -1;

    public int Strength => Intimidation + Muscle;

    /// <summary>What a drifter asks to sign on: more for a better man.</summary>
    public int Fee => 60 + (Intimidation + Muscle + Brains + Stealth) * 8;
}

/// <summary>A bank: a vault that fills up through the week, and guards on the door.</summary>
public sealed class Bank
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int LotId { get; init; }
    public int Capacity { get; init; }
    public int Vault { get; set; }
    public int Guards { get; init; }

    /// <summary>The week it was last held up. A bank stays on its guard for a while afterwards.</summary>
    public int RobbedWeek { get; set; } = -1000;
}
