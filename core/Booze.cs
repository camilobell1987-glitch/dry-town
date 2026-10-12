namespace DryTown.Core;

/// <summary>Rum-running: the supply lines into the city, what good liquor is worth, and what cutting it costs.</summary>
public static partial class Content
{
    /// <summary>The runs that bring liquor in from outside, in the order a bigger city gets them.</summary>
    public static readonly (string Name, LineKind Kind, string Cargo, int Price, int Capacity)[] Lines =
    {
        ("the Windsor run", LineKind.Truck, "Canadian whisky down from Windsor", 3000, 3),
        ("Rum Row", LineKind.Boat, "Caribbean rum off the boats on Rum Row", 5000, 5),
        ("the Detroit road", LineKind.Truck, "bonded Scotch through Detroit", 3000, 3),
        ("the Gulf boats", LineKind.Boat, "Cuban rum up from the Gulf", 5000, 5),
        ("the Montreal road", LineKind.Truck, "Canadian rye from Montreal", 3500, 4),
    };

    public static int LinesFor(CitySize size) => size switch
    {
        CitySize.Small => 2,
        CitySize.Medium or CitySize.Large => 3,
        CitySize.Huge => 4,
        _ => 5,
    };

    /// <summary>A speakeasy pouring the real stuff from a supply line takes this much more.</summary>
    public const double LineBonus = 0.4;

    /// <summary>What stretching the liquor adds to every speakeasy and still.</summary>
    public static double CutIncome(Cut cut) => cut switch { Cut.Watered => 0.2, Cut.Hard => 0.4, _ => 0 };

    /// <summary>
    /// Weekly chance a speakeasy or still sends out a bad batch. Bathtub gin and a back-room still's
    /// rotgut are a risk even served as they come; real liquor is safe until someone cuts it.
    /// </summary>
    public static double BadBatchChance(Cut cut, bool realStuff) =>
        (realStuff ? 0 : 0.002) + cut switch { Cut.Watered => 0.006, Cut.Hard => 0.02, _ => 0 };

    /// <summary>What a bad batch costs: the place shut, the heat, and the city's temper.</summary>
    public const int BadBatchShut = 4, BadBatchHeat = 10, BadBatchOutrage = 5;

    /// <summary>A stopped shipment sells for this much for each speakeasy the line supplies.</summary>
    public const int LoadPerSpeakeasy = 150;

    /// <summary>Weeks the suppliers wait on an outfit with nowhere to pour their liquor before they find another.</summary>
    public const int LineIdleWeeks = 8;

    /// <summary>Chance, after a shipment is stopped, that the suppliers take their business to whoever stopped it.</summary>
    public const double LineSwitch = 0.2;

    public static string Label(Cut cut) => cut switch
    {
        Cut.Watered => "watered down",
        Cut.Hard => "cut with industrial alcohol",
        _ => "served straight",
    };
}

/// <summary>Who holds which supply line, which speakeasies get the good stuff, and how it's poured.</summary>
public sealed partial class World
{
    public List<SupplyLine> SupplyLines { get; } = new();

    public SupplyLine? LineById(int id) => SupplyLines.FirstOrDefault(l => l.Id == id);
    public IEnumerable<SupplyLine> LinesOf(int gangId) => SupplyLines.Where(l => l.OwnerGangId == gangId);

    /// <summary>
    /// The city's supply lines, the same for every city of its size. Placed without the dice, so
    /// an older saved city gets them when it's loaded.
    /// </summary>
    public void PlaceLines()
    {
        if (SupplyLines.Count > 0) return;
        for (int i = 0; i < Content.LinesFor(Settings.Size); i++)
        {
            var (name, kind, cargo, price, capacity) = Content.Lines[i % Content.Lines.Length];
            SupplyLines.Add(new SupplyLine { Id = i, Name = name, Kind = kind, Cargo = cargo, Price = price, Capacity = capacity });
        }
    }

    public static bool Liquor(RacketKind r) => r is RacketKind.Speakeasy or RacketKind.Still;

    /// <summary>
    /// The gang's speakeasies its lines and its warehouse keep in real liquor, oldest first, as many as
    /// the lines carry and a case each from the warehouse. A line whose shipment was stopped this week carries nothing.
    /// </summary>
    public HashSet<int> Supplied(int gangId, bool thisWeek = true)
    {
        int room = LineRoom(gangId, thisWeek) + GangById(gangId).Cellar;
        return TurfOf(gangId).Where(b => b.Racket == RacketKind.Speakeasy && b.IsOpen).OrderBy(b => b.Id)
            .Take(room).Select(b => b.Id).ToHashSet();
    }

    /// <summary>What a speakeasy or still brings in this week, as a share of its usual take: real liquor and a heavy pour both pay.</summary>
    public double BoozeFactor(Business b, Gang gang, HashSet<int>? supplied = null)
    {
        if (!Liquor(b.Racket) || !Prohibition) return 1;
        bool real = b.Racket == RacketKind.Speakeasy && (supplied ?? Supplied(gang.Id)).Contains(b.Id);
        return (1 + Content.CutIncome(gang.Cut)) * (real ? 1 + Content.LineBonus : 1);
    }

    /// <summary>The weekly chance this speakeasy or still poisons someone.</summary>
    public double BadBatchChance(Business b, Gang gang, HashSet<int>? supplied = null) =>
        !Liquor(b.Racket) || !Prohibition ? 0
            : Content.BadBatchChance(gang.Cut, b.Racket == RacketKind.Speakeasy && (supplied ?? Supplied(gang.Id, thisWeek: false)).Contains(b.Id));

    /// <summary>Where a line's shipment is headed: the first speakeasy it supplies. Nothing ships to an outfit without one.</summary>
    public Business? DropOf(SupplyLine line)
    {
        if (line.OwnerGangId < 0) return null;
        var first = Supplied(line.OwnerGangId, thisWeek: false);
        return TurfOf(line.OwnerGangId).Where(b => first.Contains(b.Id)).OrderBy(b => b.Id).FirstOrDefault();
    }

    /// <summary>Why the gang can't buy this line, or null if it can.</summary>
    public string? CannotBuyLine(Gang gang, SupplyLine line)
    {
        if (!Prohibition) return "Since repeal, nobody needs a rum-runner.";
        if (line.OwnerGangId == gang.Id) return "It's already yours.";
        if (line.OwnerGangId >= 0) return $"The suppliers deal with {GangById(line.OwnerGangId).Name}. Stop a few of their shipments.";
        return gang.Cash < line.Price ? $"The suppliers want ${line.Price:N0}." : null;
    }

    /// <summary>Strike a deal with a line's suppliers. Returns what it cost, or -1.</summary>
    public int BuyLine(Gang gang, SupplyLine line)
    {
        if (CannotBuyLine(gang, line) != null) return -1;
        gang.Cash -= line.Price;
        Ledger(gang.Id).Spending += line.Price;
        line.OwnerGangId = gang.Id;
        Log(EventKind.Booze, gang.Id, $"{gang.Name} paid ${line.Price:N0} to take over {line.Name}: {line.Cargo}.");
        return line.Price;
    }

    public void SetCut(Gang gang, Cut cut) => gang.Cut = cut;
}
