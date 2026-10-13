namespace DryTown.Core;

/// <summary>Sunday among the gangs: tribute changes hands, old grudges fade, and the bosses sit down to talk.</summary>
public sealed partial class Simulation
{
    /// <summary>A rival holds off an outfit it has a pact with, or one that's burying its boss.</summary>
    private bool KeepsTheirWord(Gang gang, int otherId) => !gang.IsPlayer && (World.AtPeace(gang.Id, otherId) || World.Mourning(otherId));

    /// <summary>On the day of a funeral, the rival outfits decide whether to send a wreath: most do, unless they're at war with the dead man's outfit or hate it. The player sends his own, or the planner does on Monday.</summary>
    private void SendFlowers()
    {
        var w = World;
        foreach (var f in w.Funerals.Where(f => f.UntilWeek == w.Week + 1).ToList())
        {
            var g = w.GangById(f.GangId);
            if (!g.Alive) continue;
            foreach (var other in w.LivingGangs.Where(x => !x.IsPlayer && x.Id != g.Id).ToList())
                if (AiPlanner.WouldSendFlowers(w, other, g) && w.Rng.Chance(Content.AiFlowers))
                    w.SendFlowers(other, g);
        }
    }

    private void Diplomacy()
    {
        var w = World;
        w.KeepFamily();
        PayTribute();
        Hostages();
        SendFlowers();

        foreach (var g in w.Grudges.ToList()) w.AddGrudge(g.From, g.To, -Content.GrudgeFade);
        foreach (var pact in w.Pacts.Where(p => p.UntilWeek <= w.Week + 1).ToList())
        {
            w.Pacts.Remove(pact);
            w.Log(EventKind.Diplomacy, pact.A, $"The {World.Name(pact.Kind)} between {w.GangById(pact.A).Name} and {w.GangById(pact.B).Name} ran out.");
        }
        foreach (var offer in w.Offers.Where(o => o.ExpiresWeek <= w.Week + 1).ToList()) w.Answer(offer, false, lapsed: true);

        foreach (var g in w.LivingGangs.ToList())
        {
            // The player does his own talking, or the planner does it for him on Monday.
            if (g.IsPlayer) continue;
            if (AiPlanner.TalksThisWeek(w, g)) AiPlanner.Negotiate(w, g);
        }
    }

    private void PayTribute()
    {
        var w = World;
        foreach (var pact in w.Pacts.Where(p => p.PayerId >= 0).ToList())
        {
            var payer = w.GangById(pact.PayerId);
            var paid = w.GangById(pact.Other(payer.Id));
            if (payer.Cash < pact.Tribute)
            {
                w.Pacts.Remove(pact);
                w.AddGrudge(paid.Id, payer.Id, Content.GrudgeRefused);
                w.Log(EventKind.Diplomacy, payer.Id, $"{payer.Name} couldn't pay {paid.Name} this week, and the truce is off.");
                continue;
            }
            payer.Cash -= pact.Tribute;
            paid.Cash += pact.Tribute;
            w.Ledger(payer.Id).TributePaid += pact.Tribute;
            w.Ledger(paid.Id).TributeIn += pact.Tribute;
        }
    }
}
