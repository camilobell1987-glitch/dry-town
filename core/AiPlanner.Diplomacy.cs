namespace DryTown.Core;

/// <summary>How an outfit talks to the others: once a month it looks for a way out of a fight, a partner against the biggest gang, or a weaker one to lean on.</summary>
public static partial class AiPlanner
{
    /// <summary>One round of talks for a gang. The player's gang never goes back on its word on its own.</summary>
    public static void Negotiate(World w, Gang g)
    {
        if (PlanWar(w, g) || TearUp(w, g) || SeekTruce(w, g) || SeekAlliance(w, g)) return;
        DemandTribute(w, g);
    }

    /// <summary>
    /// Do the player's talking as the planner would: take any truce or alliance on offer, pay
    /// tribute only to an outfit much stronger, then look for terms of his own once a month like
    /// any rival. Returns what came of it.
    /// </summary>
    public static List<string> TalkForPlayer(World w)
    {
        var me = w.Player;
        if (!me.Alive) return new List<string>();
        int before = w.Events.Count;
        foreach (var o in w.Offers.ToList())
            w.Answer(o, o.Kind != OfferKind.Demand || w.Power(w.GangById(o.FromGangId)) > w.Power(me) * Content.TributeRatio);
        if (TalksThisWeek(w, me)) Negotiate(w, me);
        return w.Events.Skip(before).Where(e => e.Kind == EventKind.Diplomacy).Select(e => e.Text).ToList();
    }

    /// <summary>Whether this is the week the gang takes stock of its friends and enemies.</summary>
    public static bool TalksThisWeek(World w, Gang g) => (w.Week + g.Id) % 4 == 0;

    /// <summary>Settle a proposal between two outfits, or put it to the player if he's the one being asked.</summary>
    private static bool Propose(World w, Gang from, Gang to, PactKind kind, int tribute)
    {
        if (to.IsPlayer)
        {
            w.Put(from, kind == PactKind.Alliance ? OfferKind.Alliance : OfferKind.Truce, tribute);
            return true;
        }
        if (w.Refusal(from, to, kind, tribute) != null) return false;
        w.Sign(from, to, kind, tribute > 0 ? from.Id : -1, tribute);
        return true;
    }

    /// <summary>An aggressive outfit that has outgrown its partner stops pretending.</summary>
    private static bool TearUp(World w, Gang g)
    {
        if (g.IsPlayer || g.Aggression <= 0.55) return false;
        var pact = w.Pacts.Where(p => p.Involves(g.Id) && p.PayerId != p.Other(g.Id))
            .FirstOrDefault(p => w.Power(g) > 2 * w.Power(w.GangById(p.Other(g.Id))));
        if (pact == null || !w.Rng.Chance(0.25)) return false;
        w.Betray(g, w.GangById(pact.Other(g.Id)), pact);
        return true;
    }

    /// <summary>A gang taking a beating from an outfit its own size or bigger looks for a way out, paying for it if it has to.</summary>
    private static bool SeekTruce(World w, Gang g)
    {
        double mine = w.Power(g);
        var enemy = w.LivingGangs
            .Where(r => r.Id != g.Id && !w.AtPeace(g.Id, r.Id) && w.Power(r) >= mine)
            .Select(r => (Gang: r, Fight: w.GrudgeOf(g.Id, r.Id)))
            .Where(x => x.Fight >= Content.GrudgeSuesForPeace)
            .OrderByDescending(x => x.Fight).ThenBy(x => x.Gang.Id).Select(x => x.Gang).FirstOrDefault();
        if (enemy == null) return false;
        int tribute = 0;
        if (w.WantsPaying(g, enemy))
        {
            tribute = Content.TributeAsk(w.Income(g));
            if (g.Cash < tribute * 6) return false;
        }
        return Propose(w, g, enemy, PactKind.Truce, tribute);
    }

    /// <summary>When one outfit runs a third of the city, two of the others band together against it.</summary>
    private static bool SeekAlliance(World w, Gang g)
    {
        if (w.TopDog(g) || w.AlliesOf(g.Id).Any()) return false;
        var big = w.LivingGangs.FirstOrDefault(x => x.Id != g.Id && w.TopDog(x));
        if (big == null) return false;
        var partner = w.LivingGangs
            .Where(h => h.Id != g.Id && h.Id != big.Id && !w.AtPeace(g.Id, h.Id) && !w.AlliesOf(h.Id).Any())
            .Where(h => w.GrudgeOf(h.Id, g.Id) <= 20 && w.GrudgeOf(g.Id, h.Id) <= 20)
            .OrderByDescending(h => w.Power(h)).ThenBy(h => h.Id).FirstOrDefault();
        if (partner == null) return false;
        if (!Propose(w, g, partner, PactKind.Alliance, 0)) return false;
        if (w.PactBetween(g.Id, partner.Id) != null)
        {
            // They signed against someone.
            w.AddGrudge(g.Id, big.Id, 20);
            w.AddGrudge(partner.Id, big.Id, 20);
        }
        return true;
    }

    /// <summary>A hard outfit leans on a much weaker one for a weekly envelope, and goes after it if it won't pay.</summary>
    private static void DemandTribute(World w, Gang g)
    {
        if (g.Aggression <= 0.6 || (w.Week / 4 + g.Id) % 2 != 0) return;
        double mine = w.Power(g);
        var mark = w.LivingGangs
            .Where(r => r.Id != g.Id && !w.AtPeace(g.Id, r.Id) && mine > w.Power(r) * Content.TributeRatio && w.GrudgeOf(r.Id, g.Id) < 50)
            .Where(r => r.Cash >= Content.TributeAsk(w.Income(r)) * 4)
            .OrderByDescending(r => w.Income(r)).ThenBy(r => r.Id).FirstOrDefault();
        if (mark == null) return;
        int ask = Content.TributeAsk(w.Income(mark));
        if (mark.IsPlayer) { w.Put(g, OfferKind.Demand, ask); return; }
        if (w.DemandRefusal(g, mark, ask) == null) { w.Sign(g, mark, PactKind.Truce, mark.Id, ask); return; }
        w.AddGrudge(g.Id, mark.Id, Content.GrudgeRefused);
    }
}
