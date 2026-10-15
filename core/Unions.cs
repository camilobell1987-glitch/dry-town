namespace DryTown.Core;

/// <summary>The docks and the unions: the city's locals, what their dues are worth, and what a strike costs.</summary>
public static partial class Content
{
    /// <summary>The locals, in the order a bigger city gets them. The longshoremen come first: every city has its docks.</summary>
    public static readonly (string Name, BusinessKind[] Trades, int Toughness, int DuesPerShop)[] Locals =
    {
        ("the Longshoremen's Local 791", new[] { BusinessKind.Warehouse }, 7, 10),
        ("Teamsters Local 710", new[] { BusinessKind.Garage, BusinessKind.Grocer, BusinessKind.Pharmacy, BusinessKind.Hardware, BusinessKind.Bakery, BusinessKind.Butcher, BusinessKind.FuneralParlor, BusinessKind.GasStation, BusinessKind.Scrapyard }, 6, 4),
        ("the Hotel and Restaurant Workers' Local 6", new[] { BusinessKind.Hotel, BusinessKind.Diner, BusinessKind.PictureHouse, BusinessKind.DanceHall }, 5, 5),
        ("the Laundry Workers' Local 3", new[] { BusinessKind.Laundry, BusinessKind.Tailor, BusinessKind.Haberdasher, BusinessKind.Florist }, 4, 4),
        ("the Barbers' and Pool Room Local 21", new[] { BusinessKind.Barber, BusinessKind.PoolHall, BusinessKind.CigarStore, BusinessKind.Pawnshop }, 4, 4),
    };

    public static int LocalsFor(CitySize size) => size switch
    {
        CitySize.Small => 2,
        CitySize.Medium => 3,
        CitySize.Large => 4,
        _ => 5,
    };

    /// <summary>The trade in the plural, for lists: "pharmacies", "pool halls".</summary>
    public static string Plural(BusinessKind kind) => kind switch
    {
        BusinessKind.Pharmacy => "pharmacies",
        BusinessKind.Laundry => "laundries",
        BusinessKind.Haberdasher => "haberdasheries",
        BusinessKind.Bakery => "bakeries",
        BusinessKind.Butcher => "butcher shops",
        BusinessKind.Florist => "flower shops",
        BusinessKind.FuneralParlor => "funeral parlors",
        BusinessKind.GasStation => "filling stations",
        _ => Label(kind).ToLowerInvariant() + "s",
    };

    /// <summary>The most a local's dues bring in a week, however many shops it has.</summary>
    public const int DuesCap = 240;

    /// <summary>Weeks the pickets stay before the strike falls apart.</summary>
    public const int StrikeWeeks = 4;

    /// <summary>Weekly chance a struck owner gives in: one with nobody behind him, and one whose protectors are in the way.</summary>
    public const double GiveIn = 0.2, GiveInProtected = 0.08;

    /// <summary>What striking a rival's shop does to the rival's temper.</summary>
    public const int GrudgeStrike = 10;

    /// <summary>The longshoremen tip off whoever runs them when a rival's boat comes in, and look after his own.</summary>
    public const double DocksWaylay = 0.15, DocksEscort = 1.25;

    /// <summary>What an outfit pays whoever runs the docks, each week its boats land there.</summary>
    public const int Wharfage = 80;
}

/// <summary>Who runs which local, what its dues bring in, and strikes.</summary>
public sealed partial class World
{
    public List<UnionLocal> Locals { get; } = new();

    public UnionLocal? LocalById(int id) => Locals.FirstOrDefault(l => l.Id == id);
    public IEnumerable<UnionLocal> LocalsOf(int gangId) => Locals.Where(l => l.OwnerGangId == gangId);

    /// <summary>The local a business's workers belong to, if the city has one for its trade.</summary>
    public UnionLocal? LocalOf(Business b) => Locals.FirstOrDefault(l => l.Trades.Contains(b.Kind));

    public IEnumerable<Business> MembersOf(UnionLocal local) => Businesses.Where(b => local.Trades.Contains(b.Kind));

    /// <summary>The outfit that runs the docks, or -1.</summary>
    public int DocksGangId => Locals.FirstOrDefault(l => l.IsDocks)?.OwnerGangId ?? -1;

    /// <summary>The local whose members have walked out of this business, if any.</summary>
    public UnionLocal? StrikeAt(Business b) => Locals.FirstOrDefault(l => l.StrikeBusinessId == b.Id);

    /// <summary>
    /// The city's locals, the same for every city of its size. Placed without the dice, so an older
    /// saved city gets them when it's loaded.
    /// </summary>
    public void PlaceLocals()
    {
        if (Locals.Count > 0) return;
        for (int i = 0; i < Content.LocalsFor(Settings.Size); i++)
        {
            var (name, trades, toughness, dues) = Content.Locals[i % Content.Locals.Length];
            var hall = Businesses.Where(b => trades.Contains(b.Kind)).OrderBy(b => b.Id).FirstOrDefault();
            Locals.Add(new UnionLocal { Id = i, Name = name, Trades = trades, Toughness = toughness, DuesPerShop = dues, LotId = hall?.LotId ?? -1 });
        }
    }

    /// <summary>What the local's dues bring its outfit this week. A local on strike pays its pickets instead.</summary>
    public int DuesOf(UnionLocal local, bool evenOnStrike = false) =>
        local.OnStrike && !evenOnStrike ? 0 : Math.Min(Content.DuesCap, MembersOf(local).Count(b => b.IsOpen) * local.DuesPerShop);

    /// <summary>Why the gang can't call this local out of this business, or null if it can.</summary>
    public string? CannotStrike(Gang gang, UnionLocal local, Business b)
    {
        if (local.OwnerGangId != gang.Id) return $"You don't run {local.Name}.";
        if (!local.Trades.Contains(b.Kind)) return $"Nobody at {b.Name} belongs to {local.Name}.";
        if (local.OnStrike) return $"The local is already out at {BusinessById(local.StrikeBusinessId).Name}.";
        if (b.ProtectorGangId == gang.Id) return "They already pay you.";
        if (!b.IsOpen) return "It's shut already.";
        if (b.IsProtected && AtPeace(gang.Id, b.ProtectorGangId)) return $"You have a pact with {GangById(b.ProtectorGangId).Name}.";
        return null;
    }

    /// <summary>Pull the local's members out of a shop until its owner pays the gang. Returns why not, or null.</summary>
    public string? CallStrike(Gang gang, UnionLocal local, Business b)
    {
        if (CannotStrike(gang, local, b) is string no) return no;
        local.StrikeBusinessId = b.Id;
        local.StrikeWeek = Week;
        if (b.IsProtected) Hostile(gang, GangById(b.ProtectorGangId), Content.GrudgeStrike, violent: false);
        Log(EventKind.Union, gang.Id, $"{Capital(local.Name)} walked out of {b.Name}. The pickets stay until the owner pays {gang.Name}.");
        return null;
    }

    public void EndStrike(UnionLocal local, string why)
    {
        if (!local.OnStrike) return;
        local.StrikeBusinessId = -1;
        Log(EventKind.Union, local.OwnerGangId, why);
    }

    public static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
