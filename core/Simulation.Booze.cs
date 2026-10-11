namespace DryTown.Core;

/// <summary>Rum-running on the week: shipments stopped on the road, and bad liquor on Sunday.</summary>
public sealed partial class Simulation
{
    /// <summary>A rival's line whose shipment can be stopped: it has to be shipping somewhere.</summary>
    public static bool CanWaylay(World w, Gang gang, SupplyLine line) =>
        w.Prohibition && line.OwnerGangId >= 0 && line.OwnerGangId != gang.Id && w.DropOf(line) != null;

    /// <summary>
    /// Chance of stopping a shipment and getting away with it. The escort is the owner's muscle,
    /// as for a beer truck; a boat landing has more men on the beach than a truck has in the cab.
    /// </summary>
    public static double WaylayChance(World w, Hood hood, IEnumerable<Hood> backup, SupplyLine line)
    {
        var owner = w.GangById(line.OwnerGangId);
        var drop = w.DropOf(line);
        if (drop == null) return 0;
        double escort = DefenceStrength(w, owner, drop) * (line.Kind == LineKind.Boat ? 1.0 : 0.85);
        return Math.Clamp(TakeoverChance(hood.Strength + BackupStrength(backup) + hood.Stealth * 0.2, escort)
            + Inside(w, hood.GangId, owner.Id, Content.InsideJob), 0, 1);
    }

    /// <summary>What a stopped shipment sells for.</summary>
    public static int Load(SupplyLine line) => line.Capacity * Content.LoadPerSpeakeasy;

    private void Waylay(Gang gang, WaylayOrder o)
    {
        var w = World;
        var line = w.LineById(o.LineId);
        if (line == null || !CanWaylay(w, gang, line)) return;
        if (KeepsTheirWord(gang, line.OwnerGangId)) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        var backupIds = backup.Select(h => h.Id).ToList();
        var owner = w.GangById(line.OwnerGangId);
        var drop = w.DropOf(line)!;
        w.Hostile(gang, owner, Content.GrudgeHijack);
        double chance = WaylayChance(w, hood, backup, line);
        gang.Heat = Math.Min(100, gang.Heat + 3 + GunHeat(backup.Prepend(hood)));

        if (w.Rng.Chance(chance))
        {
            int load = Load(line);
            hood.Deeds.Heists++;
            line.WaylaidWeek = w.Week;
            gang.Cash += load;
            w.Ledger(gang.Id).Rackets += load;
            string text = $"{hood.Name} stopped {owner.Name}'s {(line.Kind == LineKind.Boat ? "landing" : "shipment")} on {line.Name} and sold the load for ${load}. Their speakeasies pour bathtub gin this week.";
            w.Log(EventKind.Hijack, gang.Id, text);
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hijack, gang.Id, hood.Id, gang.HqLotId, drop.LotId, drop.Id, ActionResult.Won, text, owner.Id)));
            // Suppliers who keep losing loads look for a buyer who can hold on to them.
            if (w.Rng.Chance(Content.LineSwitch))
            {
                line.OwnerGangId = gang.Id;
                w.Log(EventKind.Booze, gang.Id, $"The suppliers on {line.Name} decided they would rather deal with {gang.Name} than {owner.Name}.");
            }
            return;
        }

        int casualty = -1;
        string fail = $"{owner.Name}'s men saw off {hood.Name} when he went after their {(line.Kind == LineKind.Boat ? "boats" : "trucks")} on {line.Name}.";
        if (w.Rng.Chance(0.25 * (1 - Getaway(backup.Prepend(hood)))))
        {
            var fallen = backup.Count > 0 && w.Rng.Chance(0.5) ? w.Rng.Pick(backup) : hood;
            Kill(fallen, line.Kind == LineKind.Boat ? "trying to hit a rum-runners' landing" : "trying to stop a rum-runner's truck");
            casualty = fallen.Id;
        }
        w.Log(EventKind.Hijack, gang.Id, fail);
        w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hijack, gang.Id, hood.Id, gang.HqLotId, drop.LotId, drop.Id, ActionResult.Lost, fail,
            owner.Id, -1, casualty)));
    }

    /// <summary>
    /// Sunday, after the takings are in: a speakeasy or still pouring bad liquor poisons someone. The
    /// police close it and smash the back room, the papers blame the outfit, and the city's temper rises.
    /// </summary>
    private void Booze()
    {
        var w = World;
        foreach (var line in w.SupplyLines.Where(l => l.OwnerGangId >= 0 && !w.GangById(l.OwnerGangId).Alive))
        {
            w.Log(EventKind.Booze, -1, $"With {w.GangById(line.OwnerGangId).Name} gone, the suppliers on {line.Name} are looking for a new buyer.");
            line.OwnerGangId = -1;
        }
        if (!w.Prohibition) return;

        foreach (var line in w.SupplyLines.Where(l => l.OwnerGangId >= 0))
        {
            line.IdleWeeks = w.DropOf(line) == null ? line.IdleWeeks + 1 : 0;
            if (line.IdleWeeks < Content.LineIdleWeeks) continue;
            w.Log(EventKind.Booze, line.OwnerGangId, $"The suppliers on {line.Name} gave up on {w.GangById(line.OwnerGangId).Name}, who had nowhere left to pour their liquor.");
            line.OwnerGangId = -1;
            line.IdleWeeks = 0;
        }

        foreach (var gang in w.LivingGangs)
        {
            var supplied = w.Supplied(gang.Id, thisWeek: false);
            foreach (var b in w.TurfOf(gang.Id).Where(b => World.Liquor(b.Racket) && b.IsOpen).ToList())
            {
                if (!w.Rng.Chance(w.BadBatchChance(b, gang, supplied))) continue;
                string what = b.Racket == RacketKind.Speakeasy ? "drinking" : "on liquor from the still";
                int sick = w.Rng.Range(2, 6);
                b.Racket = RacketKind.None;
                b.ShutWeeks = Math.Max(b.ShutWeeks, Content.BadBatchShut);
                gang.Heat = Math.Min(100, gang.Heat + Content.BadBatchHeat);
                w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + Content.BadBatchOutrage);
                w.Log(EventKind.Booze, gang.Id, $"{sick} men went blind {what} at {b.Name}. The police smashed the back room, and the papers blame {gang.Name}.");
            }
        }
    }

    /// <summary>Repeal ends the runs: the suppliers sell to anyone with a licence now.</summary>
    private void CloseLines()
    {
        var w = World;
        foreach (var line in w.SupplyLines) line.OwnerGangId = -1;
        foreach (var gang in w.Gangs) gang.Cut = Cut.Straight;
    }
}
