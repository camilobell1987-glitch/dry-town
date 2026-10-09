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

    /// <summary>The city's shape for each size: blocks, wards, precinct houses, businesses and how many gangs it holds.</summary>
    public record CityShape(int BlocksX, int BlocksY, int WardsX, int WardsY, int Precincts, int Businesses, int StartingGangs, int MaxGangs);

    public static CityShape Shape(CitySize size) => size switch
    {
        CitySize.Medium => new(7, 5, 2, 2, 2, 90, 4, 8),
        CitySize.Large => new(9, 7, 3, 2, 3, 150, 5, 10),
        CitySize.Huge => new(13, 10, 4, 3, 5, 300, 7, 14),
        _ => new(5, 4, 2, 1, 1, 48, 3, 6),
    };

    // ---- Politics -----------------------------------------------------------

    /// <summary>Aldermen are elected every second November, the first in 1920.</summary>
    public const int AldermanElectionWeek = 45;

    /// <summary>The mayor is elected every fourth April, the first in 1923.</summary>
    public const int MayorElectionWeek = 14;

    /// <summary>Weeks of campaigning before an election, when money can be put behind a candidate.</summary>
    public const int CampaignWeeks = 8;

    public static bool IsAldermanYear(int year) => year % 2 == 0;
    public static bool IsMayorYear(int year) => year % 4 == 3;

    /// <summary>A ward boss's weekly envelope: a base plus a cut for every business in his ward.</summary>
    public static int AldermanRetainer(int businessesInWard) => 25 + businessesInWard * 3;

    /// <summary>One-off payment to get an alderman on the payroll, as a multiple of his retainer. Twice that if another outfit owns him.</summary>
    public const int PayoffWeeks = 5;

    /// <summary>A newly bought alderman won't hear another outfit's offer for half a year.</summary>
    public const int AldermanLoyalWeeks = 26;

    public static readonly string[] WardNames =
    {
        "the Levee", "Tannery Row", "Goat Hill", "the Flats", "Little Palermo", "Canal Bend",
        "Kerry Patch", "Dutch Hollow", "the Stockyards", "Gaslight Square", "Shantytown", "the Bottoms",
        "the Docks", "Brewery Hill", "Lamplight Row", "Old Town", "the Gold Coast", "Cinder Flats",
    };

    public static readonly string[] PoliticianFirstNames =
    {
        "Ambrose", "Cornelius", "Thaddeus", "Horace", "Bartholomew", "Ignatius", "Lucius", "Mortimer",
        "Percival", "Rufus", "Silas", "Wendell", "Clarence", "Elmer", "Virgil", "Otis",
    };

    public static readonly string[] PoliticianNicknames =
    {
        "Bathhouse", "Hinky Dink", "Big Bill", "Honest", "Silver Tongue", "Diamond", "Smiling", "Deacon",
    };
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
        "Rook", "Ashby", "Pullman", "Weller", "Quarry", "Dock",
    };

    public static readonly string[] AvenueNames =
    {
        "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Railroad", "Lake",
        "Seventh", "Eighth", "Union", "Grand", "Ninth", "Tenth", "Harbor", "Elm",
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
