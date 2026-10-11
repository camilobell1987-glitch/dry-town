namespace DryTown.Core;

/// <summary>The docks and the unions on the week: taking over a local, the dues, the pickets, and the boats.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// How hard a local is to take from the outfit that runs it: its man in the hall, or the
    /// outfit's muscle if he's gone, and the members who've got used to him.
    /// </summary>
    public static double LocalDefence(World w, UnionLocal local)
    {
        var available = w.AvailableHoodsOf(local.OwnerGangId).ToList();
        if (available.Count == 0) return 0;
        double avg = available.OrderByDescending(h => h.Strength).Take(3).Average(h => h.Strength);
        var president = available.FirstOrDefault(h => h.Id == local.PresidentHoodId);
        return Math.Max(president?.Strength ?? 0, avg * 0.5) + local.Toughness * 0.5;
    }

    /// <summary>
    /// Chance of taking the local. Nobody's local is the members to frighten, like a shopkeeper;
    /// a rival's is a fight with the rival's man in the hall.
    /// </summary>
    public static double LocalChance(World w, Gang gang, Hood hood, IEnumerable<Hood> backup, UnionLocal local)
    {
        var team = backup.ToList();
        if (local.OwnerGangId < 0)
            return Math.Clamp(0.3 + (hood.Intimidation - local.Toughness) * 0.08 + Math.Min(team.Count, Crew.MaxMembers) * 0.06 + Content.Guns[hood.Weapon].Extort, 0.05, 0.9);
        return TakeoverChance(StormingStrength(hood, team), LocalDefence(w, local));
    }

    public static bool CanTakeLocal(World w, Gang gang, UnionLocal local) =>
        local.OwnerGangId != gang.Id && (local.OwnerGangId < 0 || w.GangById(local.OwnerGangId).Alive);

    private void TakeLocal(Gang gang, UnionOrder o)
    {
        var w = World;
        var local = w.LocalById(o.LocalId);
        if (local == null || !CanTakeLocal(w, gang, local)) return;
        if (local.OwnerGangId >= 0 && KeepsTheirWord(gang, local.OwnerGangId)) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        var backupIds = backup.Select(h => h.Id).ToList();
        int hq = gang.HqLotId, hall = local.LotId >= 0 ? local.LotId : hq;
        double chance = LocalChance(w, gang, hood, backup, local);

        if (local.OwnerGangId < 0)
        {
            gang.Heat = Math.Min(100, gang.Heat + 2);
            if (w.Rng.Chance(chance))
            {
                Install(local, gang, hood);
                string won = $"{hood.Name} of {gang.Name} took over {local.Name}. Its dues go through {gang.Name}'s books now.";
                w.Log(EventKind.Union, gang.Id, won);
                w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Union, gang.Id, hood.Id, hq, hall, -1, ActionResult.Success, won)));
                return;
            }
            string text = $"The members of {local.Name} threw {hood.Name} out of the hall.";
            w.Log(EventKind.Union, gang.Id, text);
            bool arrested = w.Rng.Chance((0.06 + (10 - hood.Stealth) * 0.01) * (1 - Getaway(backup.Prepend(hood))));
            if (arrested) Jail(hood, w.Rng.Range(2, 8), "for strong-arming a union meeting");
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Union, gang.Id, hood.Id, hq, hall, -1,
                arrested ? ActionResult.Arrested : ActionResult.Failed, arrested ? $"{text} The police picked him up." : text, CasualtyHoodId: arrested ? hood.Id : -1)));
            return;
        }

        var rival = w.GangById(local.OwnerGangId);
        var president = w.Hoods.FirstOrDefault(h => h.Id == local.PresidentHoodId && h.IsAvailable && h.GangId == rival.Id);
        w.Hostile(gang, rival, Content.GrudgeTakeover);
        gang.Heat = Math.Min(100, gang.Heat + 5 + GunHeat(backup.Prepend(hood)));
        rival.Heat = Math.Min(100, rival.Heat + 2);
        if (w.Rng.Chance(chance))
        {
            if (local.OnStrike) w.EndStrike(local, $"With {rival.Name} pushed out of {local.Name}, its pickets went home from {w.BusinessById(local.StrikeBusinessId).Name}.");
            Install(local, gang, hood);
            string text = $"{gang.Name} pushed {rival.Name} out of {local.Name}.";
            w.Log(EventKind.Union, gang.Id, text);
            int casualty = -1;
            if (president != null && w.Rng.Chance(0.3)) { Kill(president, $"in a fight at the hall of {local.Name}"); casualty = president.Id; hood.Deeds.Kills++; }
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Union, gang.Id, hood.Id, hq, hall, -1, ActionResult.Won, text, rival.Id, president?.Id ?? -1, casualty)));
        }
        else
        {
            string text = $"{rival.Name} saw off {hood.Name} at the hall of {local.Name}.";
            w.Log(EventKind.Union, rival.Id, text);
            int casualty = -1;
            if (w.Rng.Chance(0.25 * (1 - Getaway(backup.Prepend(hood)))))
            {
                var fallen = backup.Count > 0 && w.Rng.Chance(0.5) ? w.Rng.Pick(backup) : hood;
                Kill(fallen, $"trying to take over {local.Name}");
                casualty = fallen.Id;
                if (president != null) president.Deeds.Kills++;
            }
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Union, gang.Id, hood.Id, hq, hall, -1, ActionResult.Lost, text, rival.Id, president?.Id ?? -1, casualty)));
        }
    }

    private static void Install(UnionLocal local, Gang gang, Hood hood)
    {
        local.OwnerGangId = gang.Id;
        local.PresidentHoodId = hood.Id;
        hood.Deeds.Takeovers++;
    }

    /// <summary>
    /// Sunday, after the takings: locals whose outfit is gone go back to the members, a struck owner
    /// gives in or the pickets give up, the dues come in, and outfits whose boats land pay the docks.
    /// </summary>
    private void Unions()
    {
        var w = World;
        foreach (var local in w.Locals.Where(l => l.OwnerGangId >= 0))
        {
            var gang = w.GangById(local.OwnerGangId);
            if (!gang.Alive)
            {
                local.StrikeBusinessId = -1;
                local.OwnerGangId = local.PresidentHoodId = -1;
                w.Log(EventKind.Union, -1, $"With {gang.Name} gone, {local.Name} elected its own officers again.");
                continue;
            }
            // A new man takes the chair when the old one is gone, inside, or dead.
            if (w.Hoods.FirstOrDefault(h => h.Id == local.PresidentHoodId) is not { IsAvailable: true } p || p.GangId != gang.Id)
                local.PresidentHoodId = w.AvailableHoodsOf(gang.Id).Where(h => h.Id != gang.BossHoodId)
                    .OrderByDescending(h => h.Intimidation).ThenBy(h => h.Id).FirstOrDefault()?.Id ?? -1;

            if (local.OnStrike) Picket(gang, local);

            int dues = w.DuesOf(local);
            gang.Cash += dues;
            w.Ledger(gang.Id).Dues += dues;
        }

        // Nothing comes off a boat at the docks unless the longshoremen unload it.
        int docks = w.DocksGangId;
        if (docks < 0 || !w.Prohibition) return;
        var boss = w.GangById(docks);
        foreach (var line in w.SupplyLines.Where(l => l.Kind == LineKind.Boat && l.OwnerGangId >= 0 && l.OwnerGangId != docks && w.DropOf(l) != null))
        {
            var payer = w.GangById(line.OwnerGangId);
            long fee = Math.Min(Content.Wharfage, Math.Max(0, payer.Cash));
            payer.Cash -= fee;
            w.Ledger(payer.Id).Spending += fee;
            boss.Cash += fee;
            w.Ledger(boss.Id).Dues += fee;
        }
    }

    /// <summary>A week on the picket line: the owner gives in, or the strike runs out, or it goes on.</summary>
    private void Picket(Gang gang, UnionLocal local)
    {
        var w = World;
        var b = w.BusinessById(local.StrikeBusinessId);
        if (b.ProtectorGangId == gang.Id || !b.IsOpen)
        {
            w.EndStrike(local, $"The pickets of {local.Name} went home from {b.Name}.");
            return;
        }
        gang.Heat = Math.Min(100, gang.Heat + 1);
        b.Resentment = Math.Min(100, b.Resentment + 2);
        if (w.Rng.Chance(b.IsProtected ? Content.GiveInProtected : Content.GiveIn))
        {
            var handler = w.Hoods.FirstOrDefault(h => h.Id == local.PresidentHoodId && h.IsAvailable)
                ?? w.AvailableHoodsOf(gang.Id).Where(h => h.Id != gang.BossHoodId).OrderBy(h => h.Id).FirstOrDefault();
            if (handler == null) return;
            string was = b.IsProtected ? $" and dropped {w.GangById(b.ProtectorGangId).Name}" : "";
            if (b.IsProtected) w.Hostile(gang, w.GangById(b.ProtectorGangId), Content.GrudgeStrike);
            b.ProtectorGangId = gang.Id;
            b.HandlerHoodId = handler.Id;
            b.ProtectionRate = Content.DefaultRatePercent;
            local.StrikeBusinessId = -1;
            w.Log(EventKind.Union, gang.Id, $"The owner of {b.Name} gave in to the pickets{was}. {gang.Name} protects it now.");
            return;
        }
        if (w.Week - local.StrikeWeek + 1 >= Content.StrikeWeeks)
            w.EndStrike(local, $"The strike at {b.Name} fell apart, and the members of {local.Name} went back to work.");
    }
}
