namespace DryTown.Core;

/// <summary>A boss in a cell who still runs his outfit, and the ways it ends: he walks out, the guards stop carrying his word, the warden moves him, or the street boss takes the chair.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// The boss is in a cell this week. Returns true if the chair is settled: he still runs the outfit from inside,
    /// or it has passed on already. False means the usual succession should follow.
    /// </summary>
    private bool FromInside(Gang gang, Hood boss)
    {
        var w = World;
        int fee = w.CellFee(gang);
        if (!w.RunsFromInside(gang))
        {
            bool wants = gang.IsPlayer ? gang.RunFromInside : boss.Brains >= Content.InsideBrains;
            if (!wants || !BestSuccessor(gang).Any()) return false;
            if (w.CannotRunFromInside(gang) is { } why)
            {
                if (gang.IsPlayer) w.Log(EventKind.Inside, gang.Id, $"{boss.Name} can't run {gang.Name} from his cell. {why}");
                return false;
            }
            var street = StreetBossFor(gang)!;
            gang.InsideSinceWeek = w.Week;
            PayGuards(gang, fee);
            w.Log(EventKind.Inside, gang.Id, $"{boss.Name} will run {gang.Name} from his cell. {street.Name} minds the street for him, and the guards get ${fee:N0} a week to carry his word out.");
            return true;
        }

        var streetBoss = StreetBossFor(gang);
        string? lost = null;
        if (gang.IsPlayer && !gang.RunFromInside && streetBoss != null)
        {
            HandOver(gang, boss, streetBoss);
            return true;
        }
        if (boss.FederalTime) lost = $"{boss.Name} was moved to a federal pen, where no word gets out.";
        else if (gang.Cash < fee) lost = $"The guards stopped carrying {boss.Name}'s word when {gang.Name}'s money ran short.";
        else if (w.Rng.Chance(Content.TransferChance(w))) lost = $"The warden had {boss.Name} moved upstate, where no word gets out.";
        if (lost != null)
        {
            gang.InsideSinceWeek = -1;
            w.Log(EventKind.Inside, gang.Id, lost);
            return false;
        }

        PayGuards(gang, fee);
        if (streetBoss != null && w.Rng.Chance(Content.CoupChance(streetBoss, boss, w.Week - gang.InsideSinceWeek)))
        {
            gang.InsideSinceWeek = -1;
            gang.BossHoodId = streetBoss.Id;
            gang.HeirHoodId = -1;
            streetBoss.Loyalty = 100;
            w.LeaveCrew(streetBoss.Id);
            boss.Loyalty = 20;
            boss.Ambition = Math.Max(boss.Ambition, 80);
            w.Log(EventKind.Inside, gang.Id, $"{streetBoss.Name} stopped taking orders from a cell. He runs {gang.Name} now, and {boss.Name} will come out to find it isn't his.");
        }
        return true;
    }

    /// <summary>The boss is out, and takes the chair back from the street boss.</summary>
    private void BackInTheChair(Gang gang, Hood boss)
    {
        var w = World;
        gang.InsideSinceWeek = -1;
        var street = w.Hoods.FirstOrDefault(h => h.Id == gang.HeirHoodId && h.IsActive);
        w.Log(EventKind.Inside, gang.Id, $"{boss.Name} walked out and took back the chair at {gang.Name}." + (street != null ? $" {street.Name} goes back to minding the street." : ""));
    }

    /// <summary>The player lets the street boss have the chair for good. The old boss doesn't hold it against him.</summary>
    private void HandOver(Gang gang, Hood boss, Hood street)
    {
        var w = World;
        gang.InsideSinceWeek = -1;
        gang.BossHoodId = street.Id;
        gang.HeirHoodId = -1;
        street.Loyalty = 100;
        w.LeaveCrew(street.Id);
        boss.Loyalty = Math.Max(boss.Loyalty, 70);
        w.Log(EventKind.Inside, gang.Id, $"{boss.Name} handed {gang.Name} to {street.Name} from his cell.");
    }

    /// <summary>The heir minds the street; if he's away too, the next best man does, and becomes the heir.</summary>
    private Hood? StreetBossFor(Gang gang)
    {
        var w = World;
        var heir = w.Hoods.FirstOrDefault(h => h.Id == gang.HeirHoodId);
        if (heir is { IsAvailable: true } && heir.GangId == gang.Id) return heir;
        var next = BestSuccessor(gang).FirstOrDefault();
        if (next == null) return null;
        // Standing in for a man who's away passes nobody over.
        gang.HeirHoodId = next.Id;
        next.Loyalty = Math.Max(next.Loyalty, 70);
        if (w.RunsFromInside(gang)) w.Log(EventKind.Inside, gang.Id, $"{next.Name} minds the street for {w.HoodById(gang.BossHoodId).Name} now.");
        return next;
    }

    private void PayGuards(Gang gang, int fee)
    {
        gang.Cash -= fee;
        World.Ledger(gang.Id).Spending += fee;
    }
}
