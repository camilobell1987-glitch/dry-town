namespace DryTown.Core;

/// <summary>After repeal: slot machines, casinos, the racing wire, and the G-men.</summary>
public static partial class Content
{
    /// <summary>What every other outfit's bookie pays whoever runs the wire each week, and what the wire adds to his own.</summary>
    public const int WireFee = 25;
    public const double WireBonus = 0.5;

    /// <summary>A casino's heat each week while the ward's alderman isn't the outfit's man.</summary>
    public const int CasinoHeatUncovered = 4;

    /// <summary>What a casino's own men on the door add to its defence.</summary>
    public const int CasinoDoor = 5;

    /// <summary>Weekly chance the G-men pick up one of Public Enemy Number One's men, and what each point of heat adds to it.</summary>
    public const double GmenRaid = 0.02, GmenRaidPerHeat = 0.0004;

    /// <summary>How often the boss himself is the one they pick up, when he's on the street.</summary>
    public const double GmenTakeBoss = 0.25;

    /// <summary>The Bureau doesn't bother naming an outfit smaller than this.</summary>
    public const int PublicEnemyTurf = 6;
}

/// <summary>The bureau's choice of public enemy, and who runs the wire.</summary>
public sealed class RepealState
{
    /// <summary>The outfit whose man sits in the wire room, or -1. Kept so a change of hands makes the news.</summary>
    public int WireGangId { get; set; } = -1;

    /// <summary>The outfit the Bureau has named Public Enemy Number One, or -1, and the week it named it.</summary>
    public int PublicEnemyGangId { get; set; } = -1;
    public int NamedWeek { get; set; } = -1;
}

public sealed partial class World
{
    public RepealState Repeal { get; set; } = new();

    /// <summary>The wire room: the city's first pool hall, where the race results come in over the wire. Placed without the dice.</summary>
    public int WireBusinessId =>
        (Businesses.Where(b => b.Kind == BusinessKind.PoolHall).OrderBy(b => b.Id).FirstOrDefault()
         ?? Businesses.Where(b => b.Kind == BusinessKind.Barber).OrderBy(b => b.Id).FirstOrDefault()
         ?? Businesses.OrderBy(b => b.Id).FirstOrDefault())?.Id ?? -1;

    /// <summary>The racing wire comes to town with repeal.</summary>
    public bool WireUp => !Prohibition && WireBusinessId >= 0;

    /// <summary>The outfit that runs the wire now: whoever protects the wire room, while it's open.</summary>
    public int WireGangId
    {
        get
        {
            if (!WireUp) return -1;
            var room = BusinessById(WireBusinessId);
            return room.IsOpen && room.IsProtected ? room.ProtectorGangId : -1;
        }
    }

    public bool IsWireRoom(Business b) => WireUp && b.Id == WireBusinessId;

    /// <summary>A back room that's free for a racket: an empty one, or since repeal a speakeasy that's only a bar now.</summary>
    public bool FreeBackRoom(Business b) => b.Racket == RacketKind.None || b.Racket == RacketKind.Speakeasy && !Prohibition;

    /// <summary>What the wire is worth to this outfit in a week: the other outfits' bookies' fees, and the lift to its own.</summary>
    public int WireWorth(int gangId) => !WireUp ? 0 :
        Businesses.Where(b => b.Racket == RacketKind.Bookie && b.IsOpen && b.IsProtected)
            .Sum(b => b.ProtectorGangId == gangId ? (int)(Content.Rackets[RacketKind.Bookie].WeeklyIncome * Content.WireBonus) : Content.WireFee);

    public Gang? PublicEnemy => Repeal.PublicEnemyGangId >= 0 && GangById(Repeal.PublicEnemyGangId) is { Alive: true } g ? g : null;

    /// <summary>The weekly chance the G-men pick up one of this outfit's men.</summary>
    public double GmenChance(Gang g) => PublicEnemy?.Id != g.Id ? 0 : Content.GmenRaid + g.Heat * Content.GmenRaidPerHeat;

    /// <summary>Why the gang can't open this racket here, or null if it can. A casino takes over whatever was in the back room.</summary>
    public string? CannotOpenRacket(Gang gang, Business b, RacketKind kind)
    {
        if (!Content.Rackets.TryGetValue(kind, out var info) || !Content.RacketsFor(b.Kind).Contains(kind)) return $"A {Label(b)} can't hide that.";
        if (b.ProtectorGangId != gang.Id) return "It doesn't pay you.";
        if (!b.IsOpen) return "It's shut.";
        if (info.NeedsProhibition && !Prohibition) return "Since repeal, anyone can sell a drink.";
        if (info.AfterRepeal && Prohibition) return "Not while Prohibition lasts.";
        if (kind == RacketKind.Brewery && BreweryOf(gang.Id) is { } brewery && brewery.Id != b.Id) return $"{gang.Name} brews at {brewery.Name} already.";
        if (kind == RacketKind.Nightclub)
        {
            if (ClubOf(gang.Id) is { } club) return club.Id == b.Id ? "It's a nightclub already." : $"{gang.Name} has a club at {club.Name} already.";
            if (!Working(b) || b.OwnerGangId != gang.Id) return $"A nightclub needs a {Label(b)} you own outright.";
        }
        // A casino or a club takes over whatever was in the back room.
        if (kind is RacketKind.Casino or RacketKind.Nightclub)
        {
            if (b.Racket == kind) return $"It's a {Content.Rackets[kind].Label} already.";
            if (kind == RacketKind.Casino && (!Working(b) || b.OwnerGangId != gang.Id)) return "A casino needs a hotel you own outright.";
        }
        // Since repeal a speakeasy is only a bar, and the back room can be put to better use.
        else if (b.Racket != RacketKind.None && !(b.Racket == RacketKind.Speakeasy && !Prohibition)) return "The back room is taken.";
        return gang.Cash < info.SetupCost ? $"That costs ${info.SetupCost:N0}." : null;
    }

    private static string Label(Business b) => Content.Label(b.Kind).ToLowerInvariant();
}
