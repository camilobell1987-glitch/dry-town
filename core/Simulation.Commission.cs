namespace DryTown.Core;

/// <summary>Sunday at the Commission: the first sit-down, seats for outfits big enough, and the say-so running out.</summary>
public sealed partial class Simulation
{
    private void Sit()
    {
        var w = World;
        var c = w.Commission;
        if (!c.Sitting)
        {
            if (w.Week < w.CommissionStartWeek) return;
            c.Sitting = true;
            c.FormedWeek = w.Week;
            w.Log(EventKind.Era, -1, $"The city's bosses sat down together and formed a Commission. Every outfit with {Content.CommissionTurf} shops or more has a seat. " +
                "From now on no outfit at the table goes to war with another, or touches another's boss, without the table's say-so.");
        }

        c.Seats.RemoveAll(id => !w.GangById(id).Alive);
        c.Sanctions.RemoveAll(s => s.UntilWeek <= w.Week || !w.GangById(s.Against).Alive || !w.GangById(s.By).Alive);
        foreach (var (id, until) in c.ShelvedUntil.Where(x => x.Value <= w.Week || !w.GangById(x.Key).Alive).ToList()) c.ShelvedUntil.Remove(id);
        foreach (var g in w.LivingGangs.OrderBy(g => g.Id))
        {
            if (c.Seats.Contains(g.Id) || w.Shelved(g.Id) || g.IsPlayer && c.PlayerWalkedOut || w.TurfOf(g.Id).Count() < Content.CommissionTurf) continue;
            c.Seats.Add(g.Id);
            if (c.FormedWeek != w.Week) w.Log(EventKind.Commission, g.Id, $"{g.Name} has a seat at the Commission now.");
        }
    }
}
