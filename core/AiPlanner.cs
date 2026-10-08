namespace DryTown.Core;

/// <summary>
/// Heuristic planner used by rival gangs, and by the player's gang on autopilot
/// (soak tests, or "plan my week for me").
/// </summary>
public static class AiPlanner
{
    public static List<Order> Plan(World w, Gang g)
    {
        var orders = new List<Order>();
        var turf = w.TurfOf(g.Id).ToList();
        int active = w.HoodsOf(g.Id).Count();
        long cash = g.Cash;

        if (g.Heat > 45 && cash > 600)
        {
            int units = Math.Min(5, (g.Heat - 30) / Content.HeatPerBribeUnit);
            int amount = (int)Math.Min(units * Content.BribeUnit, cash / 3 / Content.BribeUnit * Content.BribeUnit);
            if (amount > 0) { orders.Add(new BribeOrder(g.Id, amount)); cash -= amount; }
        }

        int wanted = Math.Max(5, 3 + turf.Count / 3);
        for (int i = 0; i < 2 && active + i < wanted && cash > Content.RecruitCost * 4; i++)
        {
            orders.Add(new RecruitOrder(g.Id));
            cash -= Content.RecruitCost;
        }

        foreach (var biz in turf)
        {
            int rate = biz.Resentment > 55 ? 8 : biz.Resentment < 20 ? 15 : Content.DefaultRatePercent;
            if (rate != biz.ProtectionRate) orders.Add(new SetRateOrder(g.Id, biz.Id, rate));
        }

        var hoods = w.AvailableHoodsOf(g.Id).ToList();
        // The boss stays home once there are soldiers to send.
        if (hoods.Count >= 3) hoods.RemoveAll(h => h.Id == g.BossHoodId);
        hoods = hoods.OrderByDescending(h => h.Strength).ThenBy(h => h.Id).ToList();

        if (g.Heat < 50 && hoods.Count > 0)
        {
            var site = turf
                .Where(b => b.Racket == RacketKind.None && b.IsOpen)
                .OrderByDescending(b => b.Takings).ThenBy(b => b.Id)
                .FirstOrDefault();
            if (site != null)
            {
                var racket = Content.RacketsFor(site.Kind)
                    .Select(k => Content.Rackets[k])
                    .Where(r => !r.NeedsProhibition || w.Prohibition)
                    .OrderByDescending(r => r.WeeklyIncome)
                    .FirstOrDefault();
                if (racket != null && cash > racket.SetupCost + 400)
                {
                    var brains = hoods.OrderByDescending(h => h.Brains).First();
                    orders.Add(new RacketOrder(g.Id, brains.Id, site.Id, racket.Kind));
                    hoods.Remove(brains);
                    cash -= racket.SetupCost;
                }
            }
        }

        var taken = new HashSet<int>();
        foreach (var hood in hoods)
        {
            Business? best = null;
            double bestScore = 0;
            foreach (var biz in w.Businesses)
            {
                if (!biz.IsOpen || biz.ProtectorGangId == g.Id || taken.Contains(biz.Id)) continue;
                double value = biz.Takings * 0.12 + (biz.Racket != RacketKind.None ? 80 : 0);
                double score;
                if (!biz.IsProtected)
                {
                    score = value * Simulation.ExtortChance(hood, biz);
                }
                else
                {
                    var rival = w.GangById(biz.ProtectorGangId);
                    double defence = Simulation.DefenceStrength(w, rival, biz);
                    double edge = (hood.Strength - defence) / 10.0;
                    double win = Math.Clamp(0.5 + edge, 0.05, 0.95);
                    score = value * win * (0.4 + g.Aggression);
                    if (g.Heat > 60) score *= 0.3;
                }
                if (score > bestScore) { bestScore = score; best = biz; }
            }
            if (best == null || bestScore < 8) continue;
            taken.Add(best.Id);
            orders.Add(new ExtortOrder(g.Id, hood.Id, best.Id));
        }

        return orders;
    }
}
