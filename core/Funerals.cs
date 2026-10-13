namespace DryTown.Core;

/// <summary>Gangland funerals: when a boss dies, the city stands still until he's buried, and everyone watches who sends flowers.</summary>
public static partial class Content
{
    /// <summary>Weeks from a boss's death to the end of his funeral, while nobody is supposed to lay a finger on his outfit.</summary>
    public const int FuneralWeeks = 2;

    /// <summary>What a wreath costs and the respect it earns the sender. It doesn't settle old scores; it only keeps the sender off the list of those who sent none.</summary>
    public const int FlowersCost = 250;
    public const double FlowersRespect = 1;

    /// <summary>The respect a big funeral earns the new boss for every outfit that sent flowers, and the grudge the grieving outfit keeps against those who sent none.</summary>
    public const double FuneralRespect = 2;
    public const int NoFlowersGrudge = 5;

    /// <summary>Breaking the truce: the respect lost, the grudge the grieving outfit and every other outfit hold.</summary>
    public const double RespectFuneralBreach = 15;
    public const int GrudgeFuneralBreach = 30, GrudgeFuneralWitnessed = 10;

    /// <summary>Every man of the outfit is at the church: a hit on one of them is this much likelier.</summary>
    public const double FuneralHit = 0.15;

    /// <summary>The planner sends flowers at this chance when it isn't at war with the outfit and doesn't hate it.</summary>
    public const double AiFlowers = 0.8;
}

/// <summary>A boss being buried: whose, until when, and who sent flowers.</summary>
public sealed class Funeral
{
    public int GangId { get; init; }
    public string Boss { get; init; } = "";
    public int Week { get; init; }
    public int UntilWeek { get; init; }
    public List<int> Flowers { get; set; } = new();
    public List<int> Breakers { get; set; } = new();
}

public sealed partial class World
{
    public List<Funeral> Funerals { get; } = new();

    /// <summary>The outfit an order moves on, or -1: whose shop, line, local or man it goes after.</summary>
    public int VictimOf(Order order) => order switch
    {
        ExtortOrder e when e.BusinessId >= 0 && e.BusinessId < Businesses.Count => Businesses[e.BusinessId].ProtectorGangId,
        ArsonOrder a when a.BusinessId >= 0 && a.BusinessId < Businesses.Count => Businesses[a.BusinessId].ProtectorGangId,
        HijackOrder h when h.BusinessId >= 0 && h.BusinessId < Businesses.Count => Businesses[h.BusinessId].ProtectorGangId,
        WaylayOrder l when LineById(l.LineId) is { } line => line.OwnerGangId,
        UnionOrder u when LocalById(u.LocalId) is { } local => local.OwnerGangId,
        HitOrder h when Hoods.FirstOrDefault(x => x.Id == h.TargetHoodId) is { } t => t.GangId,
        PoachOrder p when Hoods.FirstOrDefault(x => x.Id == p.TargetHoodId) is { } t => t.GangId,
        WipeOutOrder x => x.TargetGangId,
        KidnapOrder k when Hoods.FirstOrDefault(x => x.Id == k.TargetHoodId) is { } t => t.GangId,
        _ => -1,
    };

    /// <summary>The funeral this outfit is holding now, if it's burying its boss.</summary>
    public Funeral? FuneralOf(int gangId) => Funerals.FirstOrDefault(f => f.GangId == gangId && Week < f.UntilWeek);

    /// <summary>True while the outfit is burying its boss and nobody is supposed to touch it.</summary>
    public bool Mourning(int gangId) => FuneralOf(gangId) != null;

    public string? CannotSendFlowers(Gang from, Gang to)
    {
        if (from.Id == to.Id) return "It's your own funeral.";
        if (FuneralOf(to.Id) is not { } f) return $"Nobody in {to.Name} has died.";
        if (f.Flowers.Contains(from.Id)) return "Your wreath is already there.";
        return from.Cash < Content.FlowersCost ? $"A wreath fit for a boss costs ${Content.FlowersCost}." : null;
    }

    /// <summary>Send a wreath to a boss's funeral. It earns a little respect, and the grieving outfit remembers who didn't.</summary>
    public string SendFlowers(Gang from, Gang to)
    {
        if (CannotSendFlowers(from, to) is { } why) return why;
        var f = FuneralOf(to.Id)!;
        from.Cash -= Content.FlowersCost;
        Ledger(from.Id).Spending += Content.FlowersCost;
        f.Flowers.Add(from.Id);
        Renown(from, respect: Content.FlowersRespect);
        string text = $"{from.Name} sent a wreath to {f.Boss}'s funeral.";
        Log(EventKind.Diplomacy, from.Id, text);
        return text;
    }

    /// <summary>A boss is dead: his outfit buries him, and the city holds off until it's done.</summary>
    public void Bury(Gang gang, Hood boss)
    {
        // A small gang buries its own without the city stopping for it.
        if (gang.IsSmall || Funerals.Any(f => f.GangId == gang.Id && f.Boss == boss.Name)) return;
        Funerals.Add(new Funeral { GangId = gang.Id, Boss = boss.Name, Week = Week, UntilWeek = Week + Content.FuneralWeeks });
        Log(EventKind.Diplomacy, gang.Id, $"{gang.Name} will bury {boss.Name} next week. Until then, nobody who wants to be thought well of will lay a finger on them.");
    }

    /// <summary>An outfit moved against one burying its boss. Nobody forgets it, and the Commission throws it out.</summary>
    private void BreakFuneral(Gang breaker, Gang victim, Funeral f)
    {
        if (f.Breakers.Contains(breaker.Id)) return;
        f.Breakers.Add(breaker.Id);
        Renown(breaker, respect: -Content.RespectFuneralBreach);
        AddGrudge(victim.Id, breaker.Id, Content.GrudgeFuneralBreach);
        foreach (var other in Outfits)
            if (other.Id != breaker.Id && other.Id != victim.Id) AddGrudge(other.Id, breaker.Id, Content.GrudgeFuneralWitnessed);
        Log(EventKind.Diplomacy, breaker.Id, $"{breaker.Name} moved against {victim.Name} while they were burying {f.Boss}. The whole city is talking about it.");
        if (Seated(breaker.Id)) Breach(breaker, victim, $"broke the peace of {f.Boss}'s funeral");
    }

    /// <summary>Sunday: funerals that are over are held, with the new boss's name made by the wreaths.</summary>
    public void HoldFunerals()
    {
        foreach (var f in Funerals.Where(f => f.UntilWeek == Week + 1).ToList())
        {
            var g = GangById(f.GangId);
            if (!g.Alive) continue;
            var sent = f.Flowers.Select(GangById).Select(x => x.Name).ToList();
            Renown(g, respect: Content.FuneralRespect * f.Flowers.Count);
            foreach (var other in Outfits.Where(x => x.Id != g.Id && !f.Flowers.Contains(x.Id))) AddGrudge(g.Id, other.Id, Content.NoFlowersGrudge);
            Log(EventKind.Diplomacy, g.Id, sent.Count == 0
                ? $"{f.Boss} of {g.Name} was buried with nobody's flowers but his own outfit's."
                : $"{f.Boss} of {g.Name} was buried. Wreaths came from {Join(sent)}.");
        }
        Funerals.RemoveAll(f => f.UntilWeek <= Week - Content.WeeksPerYear);
    }

    private static string Join(List<string> names) =>
        names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
}
