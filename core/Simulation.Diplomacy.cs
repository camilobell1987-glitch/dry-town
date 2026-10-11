namespace DryTown.Core;

/// <summary>Sunday among the gangs: tribute changes hands, old grudges fade, and the bosses sit down to talk.</summary>
public sealed partial class Simulation
{
    /// <summary>A rival doesn't send men against a gang it has a pact with. Only the player breaks his word on the street.</summary>
    private bool KeepsTheirWord(Gang gang, int otherId) => !gang.IsPlayer && World.AtPeace(gang.Id, otherId);

    private void Diplomacy()
    {
        var w = World;
        PayTribute();

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
