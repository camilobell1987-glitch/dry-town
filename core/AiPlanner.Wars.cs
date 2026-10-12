namespace DryTown.Core;

public static partial class AiPlanner
{
    /// <summary>
    /// An aggressive outfit with a deep score to settle against one no stronger than itself goes to
    /// the mattresses, if it can keep its men there for a couple of months. It calls the war off
    /// once its anger has cooled, the fight is going against it, or the money is running out.
    /// The player's autopilot ends wars but never starts one.
    /// </summary>
    private static bool PlanWar(World w, Gang g)
    {
        if (w.WarOf(g.Id) is { } war)
        {
            if (war.A != g.Id) return false;
            var foe = w.GangById(war.B);
            bool done = w.Week - war.StartWeek >= 8 && (w.GrudgeOf(g.Id, foe.Id) < 25 || w.Power(g) < w.Power(foe) * 0.7
                || g.Cash < Simulation.MattressBill(w, g) * 4);
            if (done) w.CallOff(g);
            return done;
        }
        if (g.IsPlayer || g.Aggression <= 0.5 || w.TopDog(g)) return false;
        bool wary = w.RivalWariness < 1;
        var enemy = w.LivingGangs
            .Where(r => w.CannotDeclare(g, r) == null && w.GrudgeOf(g.Id, r.Id) >= 60 && w.Power(g) >= w.Power(r) * 0.9 && !(r.IsPlayer && wary))
            .OrderByDescending(r => w.GrudgeOf(g.Id, r.Id)).ThenBy(r => r.Id).FirstOrDefault();
        if (enemy == null || g.Cash < w.HoodsOf(g.Id).Count() * Content.MattressCost * 8 || !w.Rng.Chance(0.5)) return false;
        if (w.BreaksRules(g.Id, enemy.Id, SanctionKind.War))
        {
            // At the table, an outfit asks first. Only a hothead with murder in his heart goes ahead when the answer is no.
            if (w.CannotAsk(g, enemy) == null && w.WouldPass(g, enemy)) w.Ask(g, enemy, SanctionKind.War);
            else if (!(g.Aggression > Content.BreachAggression && w.GrudgeOf(g.Id, enemy.Id) >= Content.BreachGrudge && w.Rng.Chance(Content.BreachChance))) return false;
        }
        w.Declare(g, enemy);
        return true;
    }

    /// <summary>In a war, every other week the outfit sends men after the enemy's best man it can reach, or its boss if the odds are good.</summary>
    private static bool PlanWarHit(World w, Gang g, List<Hood> hoods, List<Order> orders)
    {
        if (w.WarOf(g.Id) is not { } war || hoods.Count < 3 || (w.Week + g.Id) % 2 != 0) return false;
        var enemy = w.GangById(war.Other(g.Id));
        var lead = hoods[0];
        var backup = hoods.Skip(1).Take(1).ToList();
        var target = w.AvailableHoodsOf(enemy.Id)
            .OrderByDescending(h => Simulation.HitChance(w, lead, backup, h) * (h.Id == enemy.BossHoodId ? 2 : 1)).ThenBy(h => h.Id).FirstOrDefault();
        if (target == null || Simulation.HitChance(w, lead, backup, target) < 0.5) return false;
        hoods.Remove(lead);
        foreach (var b in backup) hoods.Remove(b);
        orders.Add(new HitOrder(g.Id, lead.Id, target.Id, backup.Select(b => b.Id).ToArray()));
        return true;
    }
}
