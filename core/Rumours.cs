namespace DryTown.Core;

/// <summary>Finding things out about the city's residents: asking around, having them tailed, and what the street says.</summary>
public static partial class Content
{
    /// <summary>What it costs to stand a few rounds and ask about someone.</summary>
    public const int AskPrice = 10;

    /// <summary>Weekly chance the street fills in one more line on someone you've met, and how much more where you hold turf.</summary>
    public const double RumourChance = 0.15, RumourOnTurf = 0.15;

    /// <summary>
    /// How easy each thing is to learn by asking: everyone on the block knows a man's name and what he does, fewer know
    /// where he works or lives, and only the well informed know who owns what.
    /// </summary>
    public static double AskOdds(Fact fact) => fact switch
    {
        Fact.Name => 0.8,
        Fact.Age or Fact.Origin => 0.6,
        Fact.Job => 0.55,
        Fact.Home or Fact.Work => 0.35,
        _ => 0.2,
    };

    /// <summary>Every thing there is to know, one at a time, easiest first.</summary>
    public static readonly Fact[] Facts = { Fact.Name, Fact.Age, Fact.Origin, Fact.Job, Fact.Home, Fact.Work, Fact.Landlord, Fact.Owns };

    public static string Label(Fact fact) => fact switch
    {
        Fact.Name => "name",
        Fact.Age => "age",
        Fact.Origin => "people",
        Fact.Job => "line of work",
        Fact.Home => "address",
        Fact.Work => "workplace",
        Fact.Landlord => "landlord",
        Fact.Owns => "property",
        _ => "business",
    };
}

/// <summary>What you know about one resident, and the ward he lives in, so the street can talk about him.</summary>
public sealed partial class Acquaintance
{
    public Fact Known { get; set; }
    public int WardId { get; set; } = -1;
}

public sealed partial class World
{
    /// <summary>The residents you've come across, by their directory id, and what you know of each.</summary>
    public Dictionary<int, Acquaintance> Acquaintances { get; } = new();

    public Fact KnownAbout(int citizen) => Acquaintances.TryGetValue(citizen, out var a) ? a.Known : Fact.None;

    /// <summary>Note that you've come across someone, so word about him can reach you.</summary>
    public Acquaintance Meet(int citizen, int wardId)
    {
        if (!Acquaintances.TryGetValue(citizen, out var a)) Acquaintances[citizen] = a = new Acquaintance();
        if (wardId >= 0) a.WardId = wardId;
        return a;
    }

    /// <summary>Learn things about someone. Returns what was new.</summary>
    public Fact Learn(int citizen, Fact facts, int wardId = -1)
    {
        var a = Meet(citizen, wardId);
        var fresh = facts & ~a.Known;
        a.Known |= facts;
        return fresh;
    }

    /// <summary>Whether a gang holds any turf in a ward, where the street talks to it more freely.</summary>
    public bool HoldsTurfIn(Gang g, int wardId) => Businesses.Any(b => b.ProtectorGangId == g.Id && b.IsOpen && LotOf(b).WardId == wardId);

    /// <summary>The things learned, as words: "name and address".</summary>
    public static string Describe(Fact facts)
    {
        var parts = Content.Facts.Where(f => facts.HasFlag(f)).Select(Content.Label).ToList();
        return parts.Count switch
        {
            0 => "nothing",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }
}

/// <summary>Stand a few rounds and ask about someone.</summary>
public sealed record AskOrder(int GangId, int CitizenId, int LotId, string Who) : Order(GangId);

/// <summary>Have one of your men follow someone for the week.</summary>
public sealed record TailOrder(int GangId, int HoodId, int CitizenId, int LotId, string Who) : Order(GangId);

public sealed partial class Simulation
{
    /// <summary>The chance a man tailing someone finds out each thing, and the chance he's spotted doing it.</summary>
    public static double TailOdds(Hood man) => Math.Min(0.9, 0.4 + man.Stealth * 0.03 + man.Brains * 0.02);
    public static double SpottedChance(Hood man) => Math.Max(0.05, 0.4 - man.Stealth * 0.04);

    private void Ask(Gang gang, AskOrder o)
    {
        var w = World;
        if (gang.Cash < Content.AskPrice) return;
        gang.Cash -= Content.AskPrice;
        w.Ledger(gang.Id).Spending += Content.AskPrice;
        int ward = w.Map.LotAt(o.LotId).WardId;
        double bonus = w.HoldsTurfIn(gang, ward) ? 0.15 : 0;
        var unknown = Content.Facts.Where(f => !w.KnownAbout(o.CitizenId).HasFlag(f)).ToList();
        var heard = Fact.None;
        foreach (var fact in unknown)
            if (w.Rng.Chance(Content.AskOdds(fact) + bonus)) heard |= fact;
        // Somebody always knows something.
        if (heard == Fact.None && unknown.Count > 0) heard = unknown[0];
        var fresh = w.Learn(o.CitizenId, heard, ward);
        int ties = w.LearnTies(o.CitizenId, Content.AskTie + bonus);
        string about = (fresh, ties) switch
        {
            (Fact.None, 0) => "nothing new",
            (Fact.None, _) => $"about {World.Ties(ties)}",
            (_, 0) => $"their {World.Describe(fresh)}",
            _ => $"their {World.Describe(fresh)}, and about {World.Ties(ties)}",
        };
        w.Log(EventKind.Street, gang.Id, $"You asked around about {o.Who} and heard {about}.");
    }

    private void Tail(Gang gang, TailOrder o)
    {
        var w = World;
        if (!TryUseHood(gang, o.HoodId, out var man)) return;
        int ward = w.Map.LotAt(o.LotId).WardId;
        var learned = Fact.None;
        int ties = 0;
        string how;
        if (w.Rng.Chance(SpottedChance(man)))
        {
            // He only got as far as the front door before they made him.
            learned = Fact.Home;
            gang.Heat = Math.Min(100, gang.Heat + 1);
            how = "was spotted, and only found out their address";
        }
        else
        {
            // Following someone for a week shows where they live and work, and a sharp man picks up the rest.
            learned = Fact.Home | Fact.Work | Fact.Job;
            foreach (var fact in Content.Facts)
                if (w.Rng.Chance(TailOdds(man))) learned |= fact;
            // He sees who they work for, and who comes and goes at home.
            w.Meet(o.CitizenId, ward);
            ties = w.LearnTies(o.CitizenId, 1, family: false) + w.LearnTies(o.CitizenId, TailOdds(man), work: false);
            how = "followed them all week";
        }
        var fresh = w.Learn(o.CitizenId, learned, ward);
        string text = $"{man.Name} {how}{(fresh == Fact.None && ties == 0 ? ", and learned nothing new" : fresh == Fact.None ? $" and saw {World.Ties(ties)}" : $" and learned their {World.Describe(fresh)}{(ties > 0 ? $", and saw {World.Ties(ties)}" : "")}")}: {o.Who}.";
        w.Log(EventKind.Street, gang.Id, text);
        w.Act(new ScriptAction(ActionKind.Hire, gang.Id, man.Id, gang.HqLotId, o.LotId, -1, ActionResult.Success, text));
    }

    /// <summary>Each week the street fills in a line or two on people you've come across, more often where you hold turf.</summary>
    private void Rumours()
    {
        var w = World;
        var me = w.Player;
        if (!me.Alive) return;
        int heard = 0;
        foreach (var (id, a) in w.Acquaintances.OrderBy(kv => kv.Key))
        {
            var unknown = Content.Facts.Where(f => !a.Known.HasFlag(f)).ToList();
            var unknownTies = a.UnknownTies.ToList();
            if (unknown.Count == 0 && unknownTies.Count == 0) continue;
            double chance = Content.RumourChance + (a.WardId >= 0 && w.HoldsTurfIn(me, a.WardId) ? Content.RumourOnTurf : 0) + Informants(me, a.WardId);
            if (!w.Rng.Chance(chance)) continue;
            // Gossip is mostly about who's whose. Otherwise the easy things get round first.
            if (unknownTies.Count > 0 && (unknown.Count == 0 || w.Rng.Chance(Content.RumourTie))) w.LearnTie(id, unknownTies[w.Rng.Range(0, unknownTies.Count - 1)]);
            else a.Known |= unknown[w.Rng.Chance(0.6) ? 0 : w.Rng.Range(0, unknown.Count - 1)];
            heard++;
        }
        if (heard > 0) w.Log(EventKind.Street, me.Id, $"Word on the street filled in {heard} card{(heard == 1 ? "" : "s")} in your directory.");
    }
}
