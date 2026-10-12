namespace DryTown.Core;

/// <summary>Rum row: an outfit's own boats, sent up to St. Pierre or down to Nassau for liquor, past the weather and the Coast Guard.</summary>
public static partial class Content
{
    /// <summary>What a fast boat and her crew cost, and the most an outfit runs.</summary>
    public const int BoatPrice = 2500, MaxBoats = 3;

    /// <summary>The cases an outfit can keep in its warehouse. A speakeasy pours one a week. A bigger load is sold off to other outfits at the second figure a case.</summary>
    public const int CellarCap = 40, SurplusCase = 40;

    public record RunInfo(Run Run, string Place, string Cargo, int Weeks, int Cases, int Cost, string Season);

    /// <summary>
    /// The two runs: a short one up to St. Pierre for Canadian whisky, rough in the winter gales, and a
    /// long one down to Nassau for rum, which brings back more and sails into the hurricanes from August to October.
    /// </summary>
    public static readonly IReadOnlyDictionary<Run, RunInfo> Runs = new Dictionary<Run, RunInfo>
    {
        [Run.Canada] = new(Run.Canada, "St. Pierre", "Canadian whisky", 2, 12, 250, "the winter gales"),
        [Run.Islands] = new(Run.Islands, "Nassau", "Bahamas rum", 4, 30, 750, "the hurricanes"),
    };

    /// <summary>Weekly chance a boat at sea meets a storm, out of season and in it, and the chance a storm sinks her rather than costing her the cargo.</summary>
    public const double Storm = 0.015, StormSeason = 0.07, Founder = 0.3;

    /// <summary>The weeks of the year each run's bad season covers, first to last.</summary>
    public static bool InSeason(Run run, int weekOfYear) => run switch
    {
        Run.Canada => weekOfYear >= 47 || weekOfYear <= 10,
        Run.Islands => weekOfYear >= 32 && weekOfYear <= 43,
        _ => false,
    };

    /// <summary>
    /// Chance the Coast Guard catches a landing, before and after the Rum Patrol came in 1925, and what
    /// each point of the outfit's heat adds. At the docks an outfit runs, the longshoremen keep watch.
    /// </summary>
    public const double CoastGuard = 0.08, RumPatrol = 0.14, CoastGuardPerHeat = 0.001, DocksLanding = 0.6;
    public const int RumPatrolYear = 1925;

    /// <summary>Chance a caught boat is seized rather than outrunning the cutter with her cargo over the side, and the heat a seizure brings.</summary>
    public const double Seized = 0.5;
    public const int SeizedHeat = 5;

    /// <summary>The planner keeps this much cash after sending a boat out.</summary>
    public const int AiSailReserve = 1000;

    public static readonly string[] BoatNames =
    {
        "the Nellie B.", "the Black Duck", "the Arethusa", "the Sea Gypsy", "the Maid of Gloucester", "the Wanderer",
        "the Josephine K.", "the Mary Ellen", "the Rosalie", "the Kitty Hawk", "the Silver Spray", "the Dauntless",
    };
}

public enum Run { None, Canada, Islands }

/// <summary>One of an outfit's boats, at the dock or out on a run.</summary>
public sealed class Boat
{
    public int Id { get; init; }
    public int GangId { get; set; }
    public required string Name { get; init; }

    /// <summary>Where she's gone, or None while she's at the dock, and the week she's due back.</summary>
    public Run Run { get; set; }
    public int BackWeek { get; set; } = -1;

    /// <summary>The cases aboard: none on the way out, the full load once she's bought it, and none again if she had to dump it.</summary>
    public int Cases { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public bool AtSea => Run != Run.None;
}

public sealed partial class World
{
    public List<Boat> Boats { get; } = new();

    public IEnumerable<Boat> BoatsOf(int gangId) => Boats.Where(b => b.GangId == gangId).OrderBy(b => b.Id);
    public Boat? BoatById(int id) => Boats.FirstOrDefault(b => b.Id == id);

    /// <summary>The speakeasies the gang's supply lines keep in real liquor, before what's in its warehouse.</summary>
    public int LineRoom(int gangId, bool thisWeek = true) =>
        LinesOf(gangId).Where(l => !thisWeek || l.WaylaidWeek != Week).Sum(l => l.Capacity);

    /// <summary>The weekly chance of a storm on a run this week.</summary>
    public double StormChance(Run run) => Content.InSeason(run, WeekOfYear) ? Content.StormSeason : Content.Storm;

    /// <summary>The chance the Coast Guard catches one of the gang's landings.</summary>
    public double CoastGuardChance(Gang g) =>
        ((Year >= Content.RumPatrolYear ? Content.RumPatrol : Content.CoastGuard) + g.Heat * Content.CoastGuardPerHeat) * (DocksGangId == g.Id ? Content.DocksLanding : 1);

    public string? CannotBuyBoat(Gang g)
    {
        if (!Prohibition) return "Since repeal, nobody needs a rum-runner.";
        if (BoatsOf(g.Id).Count() >= Content.MaxBoats) return $"You run {Content.MaxBoats} boats already.";
        return g.Cash < Content.BoatPrice ? $"A fast boat and her crew cost ${Content.BoatPrice:N0}." : null;
    }

    /// <summary>Buy a boat. Returns her, or null.</summary>
    public Boat? BuyBoat(Gang g)
    {
        if (CannotBuyBoat(g) != null) return null;
        g.Cash -= Content.BoatPrice;
        Ledger(g.Id).Spending += Content.BoatPrice;
        var taken = Boats.Select(b => b.Name).ToHashSet();
        string name = Content.BoatNames.FirstOrDefault(n => !taken.Contains(n)) ?? $"Boat No. {Boats.Count + 1}";
        var boat = new Boat { Id = Boats.Count == 0 ? 0 : Boats.Max(b => b.Id) + 1, GangId = g.Id, Name = name };
        Boats.Add(boat);
        Log(EventKind.Booze, g.Id, $"{g.Name} bought a fast boat, {name}, to run liquor in from outside.");
        return boat;
    }

    public string? CannotSail(Gang g, Boat boat, Run run)
    {
        if (!Prohibition) return "Since repeal, nobody needs a rum-runner.";
        if (boat.GangId != g.Id) return "She isn't yours.";
        if (boat.AtSea) return $"She's away to {Content.Runs[boat.Run].Place}.";
        var info = Content.Runs[run];
        if (Week + info.Weeks > (Content.RepealYear - Content.StartYear) * Content.WeeksPerYear) return "She wouldn't be back before repeal.";
        return g.Cash < info.Cost ? $"The load costs ${info.Cost:N0}." : null;
    }

    /// <summary>Send a boat out for a load. She lands on the Sunday of her last week.</summary>
    public bool Sail(Gang g, Boat boat, Run run)
    {
        if (CannotSail(g, boat, run) != null) return false;
        var info = Content.Runs[run];
        g.Cash -= info.Cost;
        Ledger(g.Id).Spending += info.Cost;
        boat.Run = run;
        boat.BackWeek = Week + info.Weeks - 1;
        boat.Cases = info.Cases;
        return true;
    }
}

public sealed partial class Simulation
{
    /// <summary>
    /// Sunday, after the speakeasies have poured: storms at sea, and the boats due back land, unless the
    /// Coast Guard is waiting. What comes ashore goes to the warehouse, and what won't fit is sold.
    /// </summary>
    private void RumRow()
    {
        var w = World;
        foreach (var boat in w.Boats.Where(b => b.AtSea).OrderBy(b => b.Id).ToList())
        {
            var g = w.GangById(boat.GangId);
            var info = Content.Runs[boat.Run];
            if (boat.Cases > 0 && w.Rng.Chance(w.StormChance(boat.Run)))
            {
                if (w.Rng.Chance(Content.Founder))
                {
                    w.Boats.Remove(boat);
                    w.Log(EventKind.Booze, g.Id, $"{g.Name}'s boat {boat.Name} went down in {info.Season} on the run from {info.Place}, with her crew and {boat.Cases} cases of {info.Cargo}.");
                    continue;
                }
                boat.Cases = 0;
                w.Log(EventKind.Booze, g.Id, $"{g.Name}'s boat {boat.Name} ran into {info.Season} on the {info.Place} run, and the crew put the cargo over the side to keep her afloat.");
            }
            if (boat.BackWeek > w.Week) continue;

            boat.Run = Run.None;
            int cases = boat.Cases;
            boat.Cases = 0;
            if (cases == 0) continue;
            if (w.Rng.Chance(w.CoastGuardChance(g)))
            {
                if (w.Rng.Chance(Content.Seized))
                {
                    w.Boats.Remove(boat);
                    g.Heat = Math.Min(100, g.Heat + Content.SeizedHeat);
                    w.Log(EventKind.Booze, g.Id, $"The Coast Guard caught {g.Name}'s boat {boat.Name} landing {cases} cases of {info.Cargo}, and seized her.");
                }
                else
                    w.Log(EventKind.Booze, g.Id, $"A Coast Guard cutter ran {g.Name}'s boat {boat.Name} out to sea. She got away, but only once the crew had dumped the {info.Cargo}.");
                continue;
            }
            int keep = Math.Min(cases, Math.Max(0, Content.CellarCap - g.Cellar));
            g.Cellar += keep;
            int sold = (cases - keep) * Content.SurplusCase;
            if (sold > 0)
            {
                g.Cash += sold;
                w.Ledger(g.Id).Rackets += sold;
            }
            w.Log(EventKind.Booze, g.Id, $"{World.Capital(boat.Name)} landed {cases} cases of {info.Cargo} from {info.Place} for {g.Name}" +
                (sold > 0 ? $", and the {cases - keep} cases there was no room for sold for ${sold:N0}." : "."));
        }
    }

    /// <summary>The cases the gang's speakeasies poured this week beyond what its lines brought in.</summary>
    private void DrinkCellar(Gang g)
    {
        if (g.Cellar <= 0) return;
        int used = World.Supplied(g.Id).Count - World.LineRoom(g.Id);
        if (used > 0) g.Cellar -= Math.Min(used, g.Cellar);
    }

    /// <summary>Repeal: the boats are sold for what they'll fetch as fishing boats, and the warehouse is cleared.</summary>
    private void SellBoats()
    {
        var w = World;
        foreach (var g in w.Gangs)
        {
            g.Cellar = 0;
            int boats = w.BoatsOf(g.Id).Count();
            if (boats == 0 || !g.Alive) continue;
            int price = boats * Content.BoatPrice / 4;
            g.Cash += price;
            w.Log(EventKind.Booze, g.Id, $"With repeal, {g.Name} sold {(boats == 1 ? "its boat" : $"its {boats} boats")} to fishermen for ${price:N0}.");
        }
        w.Boats.Clear();
    }
}
