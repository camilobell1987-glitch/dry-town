namespace DryTown.Core;

/// <summary>
/// Tunable numbers and name pools. Everything here is original to this project.
/// Phase 4 moves these into moddable data files.
/// </summary>
public static partial class Content
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

    /// <summary>Every city is run by four outfits, the player's among them. Everyone else is a small gang.</summary>
    public const int Outfits = 4;

    /// <summary>The city's shape for each size: blocks, wards, precinct houses, businesses and how many gangs it holds.</summary>
    public record CityShape(int BlocksX, int BlocksY, int WardsX, int WardsY, int Precincts, int Businesses, int StartingGangs, int MaxGangs, bool River = false, int Parks = 0, int SmallGangs = 0);

    public static CityShape Shape(CitySize size) => size switch
    {
        CitySize.Medium => new(7, 5, 2, 2, 2, 90, Outfits, Outfits, SmallGangs: 4),
        CitySize.Large => new(9, 7, 3, 2, 3, 150, Outfits, Outfits, SmallGangs: 6),
        CitySize.Huge => new(13, 10, 4, 3, 5, 300, Outfits, Outfits, Parks: 2, SmallGangs: 11),
        CitySize.Metropolis => new(18, 14, 5, 4, 8, 600, Outfits, Outfits, River: true, Parks: 3, SmallGangs: 20),
        CitySize.Greater => new(24, 18, 6, 5, 12, 1050, Outfits, Outfits, River: true, SmallGangs: 34),
        _ => new(5, 4, 2, 1, 1, 48, Outfits, Outfits, SmallGangs: 2),
    };

    // ---- Politics -----------------------------------------------------------

    /// <summary>Aldermen are elected every second November, the first in 1920.</summary>
    public const int AldermanElectionWeek = 45;

    /// <summary>The mayor is elected every fourth April, the first in 1923.</summary>
    public const int MayorElectionWeek = 14;

    /// <summary>Weeks of campaigning before an election, when money can be put behind a candidate.</summary>
    public const int CampaignWeeks = 8;

    /// <summary>The last weeks of a campaign, when men can be sent to work the polls.</summary>
    public const int PollsWeeks = 2;

    /// <summary>The votes a man leaning on voters for a week is worth, in the same weight as campaign money.</summary>
    public static int PollsVotes(Hood man) => 40 * man.Intimidation + 10 * man.Muscle;

    /// <summary>A man needs this much brains to run for alderman, and a clean record.</summary>
    public const int CandidateBrains = 5;

    /// <summary>What one of the gang's own men on the ballot is worth to it, over and above the money behind him.</summary>
    public static int CandidateWeight(Hood man) => 150 + man.Brains * 30;

    /// <summary>A sitting alderman who is one of the outfit's own men has a stronger machine than a bought one.</summary>
    public const int OwnManIncumbency = 400;

    /// <summary>How many turn out to vote in a ward.</summary>
    public static int WardTurnout(int businesses) => 600 + businesses * 45;

    /// <summary>How lopsided the count runs: closer to 0, the closer the races.</summary>
    public const double VoteSpread = 0.4;

    /// <summary>The weight a winning mayor's coat-tails lend his side's man for district attorney.</summary>
    public const int CoatTails = 200;

    /// <summary>A reform DA pushes every city case harder; a machine DA lets his backer's cases go soft.</summary>
    public const double DaReformCase = 0.05, DaFriendCase = 0.1;

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
        "Little Warsaw", "Maxwell Street", "Bridgeport", "Hester Street",
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

    public record RacketInfo(RacketKind Kind, string Label, int SetupCost, int WeeklyIncome, int WeeklyHeat, bool NeedsProhibition, bool AfterRepeal = false);

    public static readonly IReadOnlyDictionary<RacketKind, RacketInfo> Rackets = new Dictionary<RacketKind, RacketInfo>
    {
        [RacketKind.Speakeasy] = new(RacketKind.Speakeasy, "speakeasy", 600, 190, 2, true),
        [RacketKind.Still] = new(RacketKind.Still, "back-room still", 450, 140, 2, true),
        [RacketKind.Numbers] = new(RacketKind.Numbers, "numbers game", 350, 95, 1, false),
        [RacketKind.LoanShark] = new(RacketKind.LoanShark, "loan book", 400, 85, 1, false),
        [RacketKind.Bookie] = new(RacketKind.Bookie, "bookie's counter", 450, 110, 1, false),
        [RacketKind.GamblingDen] = new(RacketKind.GamblingDen, "gambling den", 900, 230, 3, false),
        [RacketKind.Counterfeit] = new(RacketKind.Counterfeit, "counterfeiting press", 1200, 260, 1, false),
        [RacketKind.Slots] = new(RacketKind.Slots, "row of slot machines", 300, 100, 1, false, AfterRepeal: true),
        [RacketKind.Casino] = new(RacketKind.Casino, "casino", 5000, 600, 1, false, AfterRepeal: true),
        [RacketKind.Brewery] = new(RacketKind.Brewery, "brewery", 3000, 200, 3, true),
        [RacketKind.Nightclub] = new(RacketKind.Nightclub, "nightclub", 2500, 220, 1, false),
    };

    /// <summary>Weekly chance the Treasury finds each counterfeiting press a gang runs.</summary>
    public const double CounterfeitRisk = 0.015;

    /// <summary>Which racket a business can hide.</summary>
    public static RacketKind[] RacketsFor(BusinessKind kind) => kind switch
    {
        BusinessKind.Hotel => new[] { RacketKind.Speakeasy, RacketKind.Numbers, RacketKind.GamblingDen, RacketKind.Slots, RacketKind.Casino, RacketKind.Nightclub },
        BusinessKind.PoolHall => new[] { RacketKind.Speakeasy, RacketKind.Numbers, RacketKind.Bookie, RacketKind.GamblingDen, RacketKind.Slots },
        BusinessKind.Diner => new[] { RacketKind.Speakeasy, RacketKind.Numbers, RacketKind.Bookie, RacketKind.Slots, RacketKind.Nightclub },
        BusinessKind.Warehouse => new[] { RacketKind.Still, RacketKind.LoanShark, RacketKind.GamblingDen, RacketKind.Counterfeit, RacketKind.Brewery },
        BusinessKind.Garage or BusinessKind.Laundry => new[] { RacketKind.Still, RacketKind.LoanShark, RacketKind.Counterfeit },
        BusinessKind.Barber => new[] { RacketKind.Numbers, RacketKind.LoanShark, RacketKind.Bookie, RacketKind.Slots },
        BusinessKind.Grocer or BusinessKind.Pharmacy => new[] { RacketKind.Numbers, RacketKind.LoanShark, RacketKind.Slots },
        BusinessKind.Tailor or BusinessKind.Haberdasher => new[] { RacketKind.LoanShark, RacketKind.Bookie },
        BusinessKind.Hardware => new[] { RacketKind.Numbers, RacketKind.LoanShark, RacketKind.Still },
        BusinessKind.Court => new[] { RacketKind.Speakeasy, RacketKind.Still, RacketKind.Numbers, RacketKind.GamblingDen, RacketKind.LoanShark },
        _ => new[] { RacketKind.LoanShark, RacketKind.Slots },
    };

    /// <summary>The small shops that can share a building's ground floor in a row, and how often a row is built.</summary>
    public static readonly BusinessKind[] ShopRowKinds =
    {
        BusinessKind.Grocer, BusinessKind.Diner, BusinessKind.Barber, BusinessKind.Tailor, BusinessKind.Laundry,
        BusinessKind.Pharmacy, BusinessKind.PoolHall, BusinessKind.Hardware, BusinessKind.Haberdasher,
    };
    public const double ShopRowChance = 0.5;

    public static (int min, int max) TakingsFor(BusinessKind kind) => kind switch
    {
        BusinessKind.Hotel => (380, 640),
        BusinessKind.Court => (60, 120),
        BusinessKind.Warehouse => (300, 520),
        BusinessKind.Garage => (220, 400),
        BusinessKind.Diner => (180, 340),
        BusinessKind.Pharmacy => (180, 320),
        BusinessKind.PoolHall => (160, 300),
        BusinessKind.Grocer => (140, 260),
        BusinessKind.Laundry => (120, 220),
        BusinessKind.Tailor => (110, 200),
        BusinessKind.Hardware => (130, 240),
        BusinessKind.Haberdasher => (100, 190),
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

    // ---- The street -----------------------------------------------------------

    /// <summary>What it costs to have the beat cop look the other way for a week, and the heat it takes off.</summary>
    public const int CopPrice = 150;
    public const int CopCooling = 8;

    /// <summary>A reporter's price goes up with how much there is to write about.</summary>
    public static int StoryPrice(int heat) => 200 + heat * 8;

    public static readonly string[] Newspapers =
    {
        "the Daily Clarion", "the Evening Star", "the Courier", "the Tribune-Ledger", "the Sentinel",
    };

    public static readonly string[] BankNames =
    {
        "Merchants' Trust", "First Federal", "Corn Exchange Bank", "Union Savings", "Farmers' & Mechanics'",
        "Citizens' National", "Lakeshore Savings", "Drovers' Bank", "Second National", "Liberty Trust",
        "Commerce Bank", "Harbor Savings",
    };

    // ---- Guns and cars ---------------------------------------------------------

    /// <summary>
    /// A gun adds to a man's strength in a fight and makes shopkeepers quicker to pay, but
    /// gunplay draws more heat. A man who is killed loses what he carried.
    /// </summary>
    public sealed record GunInfo(string Label, int Price, int Firepower, int Heat, double Extort);

    public static readonly IReadOnlyDictionary<Weapon, GunInfo> Guns = new Dictionary<Weapon, GunInfo>
    {
        [Weapon.None] = new("bare hands", 0, 0, 0, 0),
        [Weapon.Pistol] = new(".38 revolver", 90, 1, 0, 0.02),
        [Weapon.Shotgun] = new("sawed-off shotgun", 220, 2, 0, 0.03),
        [Weapon.TommyGun] = new("Thompson gun", 700, 3, 2, 0.04),
    };

    /// <summary>
    /// A car takes a man to the job faster, and gets him away when it goes wrong: the getaway
    /// share is how much less likely he is to be killed or caught. The best car on a job counts.
    /// </summary>
    public sealed record CarInfo(string Label, int Price, float Speed, double Getaway);

    public static readonly IReadOnlyDictionary<Car, CarInfo> Cars = new Dictionary<Car, CarInfo>
    {
        [Car.None] = new("on foot", 0, 1, 0),
        [Car.ModelT] = new("Ford Model T", 450, 1.6f, 0.25),
        [Car.Packard] = new("Packard sedan", 1500, 2.2f, 0.4),
        [Car.Cadillac] = new("armoured Cadillac", 4000, 1.8f, 0.55),
    };

    /// <summary>What a gun or a car fetches when it's traded in for a better one.</summary>
    public static int TradeIn(int price) => price / 2;

    // ---- Lockups -------------------------------------------------------------------

    /// <summary>What a lockup in a back alley costs a week.</summary>
    public const int LockupRent = 15;

    /// <summary>Weeks the police look for a car seen at a bank job or a killing, and the chance each week they spot it out on the street.</summary>
    public const int CarWantedWeeks = 3;
    public const double SpotWantedCar = 0.15;

    /// <summary>
    /// A hijacked truck kept in a lockup until the heat's off: the weeks it waits, and how much more its load fetches
    /// then than sold off in a hurry the night it was taken.
    /// </summary>
    public const int TruckCoolWeeks = 2;
    public const double TruckPatience = 1.5;

    /// <summary>Spaces under a motor garage, a hotel and a precinct house.</summary>
    public const int MotorGarage = 40, HotelGarage = 25, PrecinctGarage = 12;

    /// <summary>The chance each week, at full heat, that the police find what's in one of a gang's lockups.</summary>
    public const double LockupSearched = 0.1;

    /// <summary>How much harder a man in an armoured car is to hit.</summary>
    public const double ArmouredCar = 0.15;

    public const int HoursPerWeek = 168;

    /// <summary>Sunday morning: collectors make the rounds.</summary>
    public const int CollectionTick = 6 * 24 + 10;

    /// <summary>Sunday night: the police and the gangs settle the week's accounts.</summary>
    public const int ReckoningTick = 6 * 24 + 21;

    /// <summary>Heat a racket in a tenement court draws less each week: the beat cop can't see it from the street.</summary>
    public const int CourtHeatOff = 1;

    public static string Label(BusinessKind kind) => kind switch
    {
        BusinessKind.PoolHall => "Pool Hall",
        BusinessKind.Hardware => "Hardware Store",
        BusinessKind.Haberdasher => "Haberdashery",
        _ => kind.ToString(),
    };

    // ---- Fronts and laundering -------------------------------------------------

    /// <summary>What the owner of a business the gang already protects will sell it for: about half a year's takings.</summary>
    public static int FrontPrice(Business b) => b.Takings * 25;

    /// <summary>A front's own honest profit, as a share of its takings. It comes in clean.</summary>
    public const int FrontProfitPercent = 15;

    /// <summary>Dirty money a front can pass off as its own takings in a week: half of what it really takes.</summary>
    public static int LaunderCapacity(Business b) => b.Takings / 2;

    /// <summary>The bookkeeper's cut of every dirty dollar he runs through the books.</summary>
    public const int LaunderFeePercent = 15;

    /// <summary>Dirty money above this draws the Treasury's tax men. Clean money is beyond them.</summary>
    public const int TaxCaseDirty = 25_000;

    // ---- Lawyers and trials ----------------------------------------------------

    /// <summary>A charge that carries no more than this many weeks is dealt with by a magistrate the same day. Anything longer goes to a jury.</summary>
    public const int PettySentence = 8;

    /// <summary>Weeks a man is held between his arrest and his trial.</summary>
    public const int WeeksToTrial = 3;

    /// <summary>How likely a jury is to convict with no help: a city court, and the Treasury's.</summary>
    public const double CityCase = 0.85, FederalCase = 0.9;

    /// <summary>A lawyer on retainer: his weekly fee, and how much less likely each of the gang's men is to be convicted.</summary>
    public sealed record LawyerInfo(string Label, int Weekly, double Cut);

    public static readonly IReadOnlyDictionary<Lawyer, LawyerInfo> Lawyers = new Dictionary<Lawyer, LawyerInfo>
    {
        [Lawyer.None] = new("no lawyer", 0, 0),
        [Lawyer.Storefront] = new("a storefront lawyer", 60, 0.15),
        [Lawyer.Mouthpiece] = new("a downtown mouthpiece", 220, 0.3),
    };

    /// <summary>What a judge wants to see a case thrown out: more for a longer stretch, and twice as much under a reform mayor.</summary>
    public static int JudgePrice(Case c, bool reform) => (400 + c.Sentence * 20) * (reform ? 2 : 1);

    /// <summary>How much less likely a conviction is with the judge paid, or with the jury leaned on.</summary>
    public const double JudgeCut = 0.35, JuryCut = 0.25;

    // ---- Diplomacy -------------------------------------------------------------

    /// <summary>How long a truce holds before it has to be renewed, and an alliance.</summary>
    public const int TruceWeeks = 13, AllianceWeeks = 52;

    /// <summary>A gang that holds this much against an outfit at least its own size wants out of the fight.</summary>
    public const int GrudgeSuesForPeace = 45;

    /// <summary>How much a gang comes to hate the one that did this to it.</summary>
    public const int GrudgeTakeover = 15, GrudgeArson = 20, GrudgeHit = 25, GrudgeBossHit = 50, GrudgeHijack = 10;

    /// <summary>A gang that breaks its word: what its partner holds against it, and what everyone else does.</summary>
    public const int GrudgeBetrayed = 40, GrudgeWitnessed = 10;

    /// <summary>A refused demand for tribute is an insult.</summary>
    public const int GrudgeRefused = 25;

    /// <summary>How much a grudge fades each week.</summary>
    public const int GrudgeFade = 2;

    /// <summary>Above this, a gang wants paying before it will call off the feud.</summary>
    public const int GrudgeTooDeep = 50;

    /// <summary>How long a proposal waits on an answer before it lapses.</summary>
    public const int OfferWeeks = 2;

    /// <summary>A gang this many times stronger than another wants tribute before it agrees to leave it alone.</summary>
    public const double TributeRatio = 1.8;

    /// <summary>Tribute worth asking for: a fifth of what the payer earns, in round fifties, and never less than $100.</summary>
    public static int TributeAsk(long weeklyIncome) => (int)Math.Max(100, weeklyIncome / 5 / 50 * 50);

    // ---- Rap sheets ------------------------------------------------------------

    /// <summary>Each conviction already on a man's record adds this share to the next sentence.</summary>
    public const double PriorSentence = 0.5;

    /// <summary>The habitual offender law: a man with this many convictions who is convicted again goes away for life.</summary>
    public const int HabitualOffender = 3;

    /// <summary>A sentence for a man with this record: longer for every conviction behind him.</summary>
    public static int Sentence(int weeks, int convictions) => (int)Math.Round(weeks * (1 + PriorSentence * convictions));
}
