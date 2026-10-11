namespace DryTown.Core;

public static partial class AiPlanner
{
    /// <summary>
    /// Now and then pull a local the outfit runs out of the best shop near home that doesn't pay it, and every so
    /// often send a man to take over a local: nobody's, when the members look easy to frighten, or a
    /// rival's, when the outfit is spoiling for a fight and the odds are good.
    /// </summary>
    private static void PlanUnions(World w, Gang g, List<Hood> hoods, List<Order> orders)
    {
        bool Wary(int gangId) => w.AtPeace(g.Id, gangId)
            || w.GangById(gangId).IsPlayer && w.RivalWariness < 1;

        foreach (var local in w.LocalsOf(g.Id).Where(l => !l.OnStrike && (w.Week + g.Id * 3 + l.Id) % 4 == 0))
        {
            var shop = w.MembersOf(local)
                .Where(b => w.CannotStrike(g, local, b) == null && w.BlocksFromHq(g, b) <= 4)
                .Where(b => !b.IsProtected || g.Aggression > 0.5 && !Wary(b.ProtectorGangId))
                .OrderBy(b => b.IsProtected).ThenByDescending(b => b.Takings).ThenBy(b => b.Id).FirstOrDefault();
            if (shop != null) orders.Add(new StrikeOrder(g.Id, local.Id, shop.Id));
        }

        if ((w.Week + g.Id * 5) % 3 != 0 || hoods.Count < 3 || g.Heat >= 45) return;
        var lead = hoods.OrderByDescending(h => h.Intimidation + h.Strength).ThenBy(h => h.Id).First();
        var backup = hoods.Where(h => h.Id != lead.Id).Take(1).ToList();
        var target = w.Locals.Where(l => Simulation.CanTakeLocal(w, g, l))
            .Where(l => l.OwnerGangId < 0 || g.Aggression > 0.5 && !Wary(l.OwnerGangId))
            .Select(l => (Local: l, Chance: Simulation.LocalChance(w, g, lead, backup, l)))
            .Where(x => x.Chance >= (x.Local.OwnerGangId < 0 ? 0.45 : 0.65))
            .OrderByDescending(x => x.Chance * (w.DuesOf(x.Local) + (x.Local.IsDocks ? 100 : 0))).ThenBy(x => x.Local.Id).FirstOrDefault();
        if (target.Local == null) return;
        hoods.Remove(lead);
        foreach (var b in backup) hoods.Remove(b);
        orders.Add(new UnionOrder(g.Id, lead.Id, target.Local.Id, backup.Select(b => b.Id).ToArray()));
    }
}
