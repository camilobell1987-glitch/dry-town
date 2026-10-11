namespace DryTown.Core;

public static partial class AiPlanner
{
    /// <summary>
    /// Keep a man in a cell from talking, by paying his family if that will do and otherwise by
    /// having him seen to; deal with rats who've been found out; look for one when the police seem
    /// to know too much; and now and then plant a man on the outfit the gang hates most.
    /// </summary>
    private static long PlanRats(World w, Gang g, List<Hood> hoods, long cash, List<Order> orders)
    {
        long spent = 0;
        // Nobody sends a man he knows is a rat.
        Hood? Take(Func<Hood, double> by)
        {
            var h = hoods.Where(x => !x.Exposed).OrderByDescending(by).ThenBy(x => x.Id).FirstOrDefault();
            if (h != null) hoods.Remove(h);
            return h;
        }

        foreach (var man in w.TalkingOf(g.Id).ToList())
        {
            int price = Content.FamilyCare(man);
            if (w.CannotLookAfter(g, man) == null && man.Loyalty + Content.FamilyLoyalty >= Content.RatLine(man) && cash - spent > price * 3)
            {
                orders.Add(new FamilyOrder(g.Id, man.Id));
                spent += price;
            }
            else if (Take(h => Simulation.SeenToChance(h, man)) is { } hand)
                orders.Add(new SeenToOrder(g.Id, hand.Id, man.Id));
        }

        foreach (var rat in w.ExposedIn(g.Id).ToList())
            if (Take(h => Simulation.SeenToChance(h, rat)) is { } hand)
                orders.Add(new SeenToOrder(g.Id, hand.Id, rat.Id));

        if (w.Week - g.TalkWeek <= 6 && hoods.Count >= 4 && Take(h => h.Brains) is { } asker)
            orders.Add(new HuntOrder(g.Id, asker.Id));

        // A man inside the outfit it hates most, if it has men to spare and none there yet. The outfit
        // that runs the city doesn't bother, and planting one of yours is your own call.
        if (!g.IsPlayer && !w.TopDog(g) && hoods.Count >= 6 && g.Aggression > 0.4 && w.Rng.Chance(0.03))
        {
            var enemy = w.LivingGangs.Where(r => r.Id != g.Id && w.GrudgeOf(g.Id, r.Id) >= 30 && !w.HasManInside(g.Id, r.Id))
                .OrderByDescending(r => w.GrudgeOf(g.Id, r.Id)).ThenBy(r => r.Id).FirstOrDefault();
            var crews = w.CrewsOf(g.Id).Select(c => c.LieutenantHoodId).ToHashSet();
            var spy = hoods.Where(h => h.Id != g.BossHoodId && h.Id != g.HeirHoodId && !crews.Contains(h.Id) && Simulation.PlantChance(h) >= 0.6)
                .OrderByDescending(Simulation.PlantChance).ThenBy(h => h.Id).FirstOrDefault();
            if (enemy != null && spy != null && !(enemy.IsPlayer && w.Settings.RivalWariness(w.Week / Content.WeeksPerYear) < 1))
            {
                hoods.Remove(spy);
                orders.Add(new PlantOrder(g.Id, spy.Id, enemy.Id));
            }
        }
        return spent;
    }
}
