namespace DryTown.Core;

/// <summary>
/// Tunable numbers and name pools. Everything here is original to this project.
/// Phase 4 moves these into moddable data files.
/// </summary>
public static class Content
{
    public const int StartYear = 1920;
    public const int WeeksPerYear = 52;

    /// <summary>Prohibition is repealed at the start of this year.</summary>
    public const int RepealYear = 1934;

    public const int RecruitCost = 120;

    /// <summary>Bringing family in costs more (a place to live, a suit, a start) and can only be done once a year.</summary>
    public const int FamilyCost = 300;

    /// <summary>
    /// Chance a man of the given age dies of natural causes within a year. Close to nothing
    /// before forty, doubling about every six years after; the city's bosses rarely see eighty.
    /// </summary>
    public static double YearlyDeathChance(int age) => age < 30 ? 0.001 : Math.Min(0.6, 0.005 * Math.Exp((age - 40) / 9.0));
    public const int BribeUnit = 100;
    public const int HeatPerBribeUnit = 4;
    public const int MaxGangs = 6;
    public const int DefaultRatePercent = 12;

    public record RacketInfo(RacketKind Kind, string Label, int SetupCost, int WeeklyIncome, int WeeklyHeat, bool NeedsProhibition);

    public static readonly IReadOnlyDictionary<RacketKind, RacketInfo> Rackets = new Dictionary<RacketKind, RacketInfo>
    {
        [RacketKind.Speakeasy] = new(RacketKind.Speakeasy, "speakeasy", 600, 190, 2, true),
        [RacketKind.Still] = new(RacketKind.Still, "back-room still", 450, 140, 2, true),
        [RacketKind.Numbers] = new(RacketKind.Numbers, "numbers game", 350, 95, 1, false),
        [RacketKind.LoanShark] = new(RacketKind.LoanShark, "loan book", 400, 85, 1, false),
    };

    /// <summary>Which racket a business can hide.</summary>
    public static RacketKind[] RacketsFor(BusinessKind kind) => kind switch
    {
        BusinessKind.Diner or BusinessKind.Hotel or BusinessKind.PoolHall => new[] { RacketKind.Speakeasy, RacketKind.Numbers },
        BusinessKind.Garage or BusinessKind.Warehouse or BusinessKind.Laundry => new[] { RacketKind.Still, RacketKind.LoanShark },
        BusinessKind.Barber or BusinessKind.Grocer or BusinessKind.Pharmacy => new[] { RacketKind.Numbers, RacketKind.LoanShark },
        _ => new[] { RacketKind.LoanShark },
    };

    public static (int min, int max) TakingsFor(BusinessKind kind) => kind switch
    {
        BusinessKind.Hotel => (380, 640),
        BusinessKind.Warehouse => (300, 520),
        BusinessKind.Garage => (220, 400),
        BusinessKind.Diner => (180, 340),
        BusinessKind.Pharmacy => (180, 320),
        BusinessKind.PoolHall => (160, 300),
        BusinessKind.Grocer => (140, 260),
        BusinessKind.Laundry => (120, 220),
        BusinessKind.Tailor => (110, 200),
        _ => (90, 170),
    };

    public static readonly string[] FirstNames =
    {
        "Abe", "Benny", "Carmine", "Dutch", "Eddie", "Frankie", "Gus", "Harry", "Izzy", "Jimmy",
        "Kip", "Lou", "Mickey", "Nate", "Ollie", "Pete", "Quint", "Ray", "Sal", "Tommy",
        "Vic", "Walt", "Moe", "Leo", "Augie", "Dom", "Fritz", "Hank", "Jack", "Sid",
    };

    public static readonly string[] LastNames =
    {
        "Abbate", "Brennan", "Castellano", "Doyle", "Esposito", "Fitzgerald", "Greco", "Hanrahan",
        "Iannucci", "Janowski", "Kowalczyk", "Lombardo", "Malloy", "Novak", "O'Hare", "Petrakis",
        "Quigley", "Russo", "Schultz", "Tierney", "Ulrich", "Vitale", "Weiss", "Zielinski",
        "Morello", "Kaplan", "Sullivan", "Barone", "Lindqvist", "Dombrowski",
    };

    public static readonly string[] Nicknames =
    {
        "the Knife", "Two-Times", "Sugar", "the Brick", "Lucky", "Fingers", "the Priest",
        "Bones", "Silk", "the Ox", "Sleepy", "Ace", "Smiles", "the Clerk",
    };

    public static readonly string[] GangPatterns =
    {
        "The {0} Outfit", "The {0} Mob", "{0}'s Boys", "The {0} Crew", "The {0} Combine",
    };

    public static readonly string[] StreetNames =
    {
        "Canal", "Halsted", "Mercer", "Larkin", "Foundry", "Orchard", "Tanner", "Vine", "Kessler", "Market",
    };

    public static readonly string[] AvenueNames =
    {
        "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Railroad", "Lake",
    };

    public const int HoursPerWeek = 168;

    /// <summary>Sunday morning: collectors make the rounds.</summary>
    public const int CollectionTick = 6 * 24 + 10;

    /// <summary>Sunday night: the police and the gangs settle the week's accounts.</summary>
    public const int ReckoningTick = 6 * 24 + 21;

    public static string Label(BusinessKind kind) => kind switch
    {
        BusinessKind.PoolHall => "Pool Hall",
        _ => kind.ToString(),
    };
}
