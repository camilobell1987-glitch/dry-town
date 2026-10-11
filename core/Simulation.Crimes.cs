namespace DryTown.Core;

/// <summary>
/// The rougher work: arson, hits, bank jobs and hijacked beer trucks, and the people on the
/// street who see it, write about it, or can be paid to look away.
/// </summary>
public sealed partial class Simulation
{
    // ---- Odds ---------------------------------------------------------------

    /// <summary>Chance a man gets a fire going and away before anyone sees. A guard on the door makes it much harder.</summary>
    public static double ArsonChance(Hood hood, bool guarded) =>
        Math.Clamp(0.5 + hood.Stealth * 0.04 - (guarded ? 0.35 : 0), 0.05, 0.9);

    /// <summary>A boss with soldiers around him is hard to get to.</summary>
    public static bool IsGuardedBoss(World w, Hood target) =>
        w.GangById(target.GangId).BossHoodId == target.Id && (w.AvailableHoodsOf(target.GangId).Count() >= 3 || w.AtWar(target.GangId));

    /// <summary>Chance a hit comes off: stealth to get close, then strength against the target's.</summary>
    public static double HitChance(World w, Hood hood, IEnumerable<Hood> backup, Hood target) =>
        Math.Clamp(0.45 + hood.Stealth * 0.03 + (hood.Strength + BackupStrength(backup) - target.Strength) * 0.03
            - (IsGuardedBoss(w, target) ? 0.25 : 0) - (target.Car == Car.Cadillac ? Content.ArmouredCar : 0)
            + Inside(w, hood.GangId, target.GangId, w.GangById(target.GangId).BossHoodId == target.Id ? Content.InsideBossHit : Content.InsideHit)
            + (w.AtWar(hood.GangId, target.GangId) ? Content.WarHit : 0), 0.05, 0.9);

    /// <summary>What men planted on either side do to a job's chances: help their own side's, spoil the other's.</summary>
    public static double Inside(World w, int gangId, int targetGangId, double help) =>
        (w.HasManInside(gangId, targetGangId) ? help : 0) - (w.HasManInside(targetGangId, gangId) ? Content.TippedOff : 0);

    /// <summary>
    /// Chance of getting into a vault and out again. The man in front plans it; the backup holds
    /// the room. Guards, a precinct round the corner and a recent hold-up all count against it.
    /// </summary>
    public static double RobberyChance(World w, Hood lead, IEnumerable<Hood> backup, Bank bank)
    {
        var team = backup.Prepend(lead).ToList();
        double power = lead.Brains * 1.5 + team.Sum(h => h.Muscle) * 0.5 + team.Average(h => h.Stealth);
        var lot = w.Map.LotAt(bank.LotId);
        double defence = bank.Guards * 6 + (w.Map.Distance(w.PrecinctNear(lot), lot) <= CityMap.StrideX * 2 ? 4 : 0)
            + (w.Week - bank.RobbedWeek < 12 ? 6 : 0);
        return Math.Clamp(0.3 + (power - defence) * 0.04 + Getaway(team) * 0.15, 0.1, 0.85);
    }

    /// <summary>Chance of stopping a rival's truck and getting away with the load. The drivers ride armed.</summary>
    public static double HijackChance(World w, Hood hood, IEnumerable<Hood> backup, Business biz)
    {
        var rival = w.GangById(biz.ProtectorGangId);
        return Math.Clamp(TakeoverChance(hood.Strength + BackupStrength(backup) + hood.Stealth * 0.2, DefenceStrength(w, rival, biz) * 0.85)
            + Inside(w, hood.GangId, rival.Id, Content.InsideJob), 0, 1);
    }

    /// <summary>Chance a witness can be talked out of testifying.</summary>
    public static double SilenceChance(Hood hood, StreetPerson witness) =>
        Math.Clamp(0.4 + (hood.Intimidation - witness.Nerve) * 0.08, 0.1, 0.95);

    /// <summary>Businesses whose beer trucks can be stopped: rival speakeasies and stills, while Prohibition lasts.</summary>
    public static bool CanHijack(World w, Gang gang, Business biz) =>
        w.Prohibition && biz.IsOpen && biz.IsProtected && biz.ProtectorGangId != gang.Id
        && biz.Racket is RacketKind.Speakeasy or RacketKind.Still;

    // ---- Crimes -------------------------------------------------------------

    private void Arson(Gang gang, ArsonOrder o)
    {
        var w = World;
        var biz = w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || !biz.IsOpen || !biz.IsProtected || biz.ProtectorGangId == gang.Id) return;
        if (KeepsTheirWord(gang, biz.ProtectorGangId)) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var rival = w.GangById(biz.ProtectorGangId);
        w.Hostile(gang, rival, Content.GrudgeArson);
        _guards.TryGetValue(biz.Id, out var guard);
        bool guarded = guard.Hood is { IsAvailable: true } && guard.Hood.GangId == rival.Id;
        var lot = w.LotOf(biz);

        if (w.Rng.Chance(Math.Clamp(ArsonChance(hood, guarded) + Inside(w, gang.Id, biz.ProtectorGangId, Content.InsideJob), 0.05, 0.95)))
        {
            string lost = biz.Racket != RacketKind.None ? $" and its {Content.Rackets[biz.Racket].Label}" : "";
            biz.ShutWeeks = 8;
            biz.BurnedWeek = w.Week;
            hood.Deeds.Fires++;
            biz.Racket = RacketKind.None;
            biz.Resentment = Math.Min(100, biz.Resentment + 10);
            gang.Heat = Math.Min(100, gang.Heat + 8);
            w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + 2);
            string text = $"{biz.Name} burned down{lost}. {rival.Name} lost a good earner, and people say {gang.Name} lit the match.";
            w.Log(EventKind.Arson, gang.Id, text);
            w.Act(new ScriptAction(ActionKind.Arson, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Success, text, rival.Id));
            MaybeWitness(gang, hood, lot, $"{hood.Name} running from the fire at {biz.Name}", 0.3);
            return;
        }

        gang.Heat = Math.Min(100, gang.Heat + 4);
        int casualty = -1;
        ActionResult result = ActionResult.Failed;
        string fail = $"{hood.Name} was seen off before he could set fire to {biz.Name}.";
        double stay = 1 - Getaway(new[] { hood });
        if (guarded && w.Rng.Chance(0.3 * stay))
        {
            Kill(hood, $"caught with a can of gasoline at {biz.Name}");
            casualty = hood.Id;
            result = ActionResult.Killed;
            fail = $"{guard.Hood.Name} of {rival.Name} caught {hood.Name} with a can of gasoline at {biz.Name}.";
        }
        else if (w.Rng.Chance((0.25 + (10 - hood.Stealth) * 0.02) * stay))
        {
            Jail(hood, w.Rng.Range(10, 30), "with attempted arson");
            casualty = hood.Id;
            result = ActionResult.Arrested;
            fail = $"{hood.Name} was picked up with a can of gasoline outside {biz.Name}.";
        }
        w.Log(EventKind.Arson, gang.Id, fail);
        w.Act(new ScriptAction(ActionKind.Arson, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, result, fail,
            rival.Id, guarded ? guard.Hood.Id : -1, casualty));
    }

    /// <summary>Where a man can be found during the week: at a business he collects from, else his gang's headquarters.</summary>
    public static int HauntOf(World w, Hood hood)
    {
        var biz = w.Businesses.Where(b => b.HandlerHoodId == hood.Id && b.ProtectorGangId == hood.GangId).OrderBy(b => b.Id).FirstOrDefault();
        return biz?.LotId ?? w.GangById(hood.GangId).HqLotId;
    }

    private void Hit(Gang gang, HitOrder o)
    {
        var w = World;
        var target = w.Hoods.FirstOrDefault(h => h.Id == o.TargetHoodId);
        if (target == null || !target.IsAvailable || target.GangId == gang.Id) return;
        if (KeepsTheirWord(gang, target.GangId)) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        var backupIds = backup.Select(h => h.Id).ToList();
        var rival = w.GangById(target.GangId);
        w.Hostile(gang, rival, rival.BossHoodId == target.Id ? Content.GrudgeBossHit : Content.GrudgeHit);
        int where = HauntOf(w, target);

        if (w.Rng.Chance(HitChance(w, hood, backup, target)))
        {
            bool boss = rival.BossHoodId == target.Id;
            Kill(target, boss ? $"in a hit ordered by {gang.Name}" : $"on the street, in a hit ordered by {gang.Name}");
            hood.Deeds.Kills++;
            // In a war the police write it off as gangland business, and look a little less hard.
            gang.Heat = Math.Min(100, gang.Heat + (w.AtWar(gang.Id, rival.Id) ? 5 : 10) + GunHeat(backup.Prepend(hood)));
            string text = boss
                ? $"{hood.Name} gunned down {target.Name}, boss of {rival.Name}."
                : $"{hood.Name} of {gang.Name} gunned down {target.Name} of {rival.Name}.";
            w.Log(EventKind.Hit, gang.Id, text);
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hit, gang.Id, hood.Id, gang.HqLotId, where, -1, ActionResult.Won, text,
                rival.Id, target.Id, target.Id)));
            MaybeWitness(gang, hood, w.Map.LotAt(where), $"{hood.Name} shoot {target.Name}", 0.35);
            return;
        }

        gang.Heat = Math.Min(100, gang.Heat + 5 + GunHeat(backup.Prepend(hood)));
        int casualty = -1;
        string fail = $"{target.Name} of {rival.Name} got away from {hood.Name}.";
        if (w.Rng.Chance(0.35 * (1 - Getaway(backup.Prepend(hood)))))
        {
            var fallen = backup.Count > 0 && w.Rng.Chance(0.5) ? w.Rng.Pick(backup) : hood;
            Kill(fallen, $"trying to kill {target.Name}");
            casualty = fallen.Id;
            fail = $"{target.Name} of {rival.Name} shot first: {fallen.Name} of {gang.Name} is dead.";
        }
        w.Log(EventKind.Hit, gang.Id, fail);
        w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hit, gang.Id, hood.Id, gang.HqLotId, where, -1, ActionResult.Lost, fail,
            rival.Id, target.Id, casualty)));
    }

    private void Robbery(Gang gang, RobberyOrder o)
    {
        var w = World;
        var bank = w.BankById(o.BankId);
        if (bank == null) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        var backupIds = backup.Select(h => h.Id).ToList();
        double chance = RobberyChance(w, hood, backup, bank);
        bank.RobbedWeek = w.Week;

        if (w.Rng.Chance(chance))
        {
            int take = bank.Vault * w.Rng.Range(50, 90) / 100;
            bank.Vault -= take;
            hood.Deeds.Heists++;
            gang.Cash += take;
            w.Ledger(gang.Id).Rackets += take;
            gang.Heat = Math.Min(100, gang.Heat + 20 + GunHeat(backup.Prepend(hood)));
            w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + 3);
            string text = $"{hood.Name} and {(backup.Count == 0 ? "nobody else" : backup.Count == 1 ? "one other man" : $"{backup.Count} men")} held up {bank.Name} and got away with ${take}.";
            w.Log(EventKind.Robbery, gang.Id, text);
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Robbery, gang.Id, hood.Id, gang.HqLotId, bank.LotId, -1, ActionResult.Success, text)));
            MaybeWitness(gang, hood, w.Map.LotAt(bank.LotId), $"{hood.Name}'s face when his mask slipped at {bank.Name}", 0.4);
            return;
        }

        int casualty = -1;
        var team = backup.Prepend(hood).ToList();
        gang.Heat = Math.Min(100, gang.Heat + 12 + GunHeat(team));
        double stay = 1 - Getaway(team);
        var pinched = new List<Hood>();
        foreach (var man in team)
        {
            if (w.Rng.Chance(0.12 * stay)) { Kill(man, $"in a shoot-out at {bank.Name}"); casualty = man.Id; continue; }
            if (w.Rng.Chance((0.45 - man.Stealth * 0.025) * stay)) { Jail(man, w.Rng.Range(26, 78), $"with the hold-up at {bank.Name}"); pinched.Add(man); }
        }
        string fail = $"The hold-up at {bank.Name} went wrong for {gang.Name}" +
            (casualty >= 0 ? $": {w.HoodById(casualty).Name} was shot" : "") +
            (pinched.Count > 0 ? $"{(casualty >= 0 ? " and" : ":")} {pinched.Count} taken in" : "") + ".";
        w.Log(EventKind.Robbery, gang.Id, fail);
        w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Robbery, gang.Id, hood.Id, gang.HqLotId, bank.LotId, -1,
            casualty >= 0 ? ActionResult.Killed : pinched.Count > 0 ? ActionResult.Arrested : ActionResult.Failed, fail,
            CasualtyHoodId: casualty >= 0 ? casualty : pinched.FirstOrDefault()?.Id ?? -1)));
    }

    private void Hijack(Gang gang, HijackOrder o)
    {
        var w = World;
        var biz = w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || !CanHijack(w, gang, biz)) return;
        if (KeepsTheirWord(gang, biz.ProtectorGangId)) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        var backupIds = backup.Select(h => h.Id).ToList();
        var rival = w.GangById(biz.ProtectorGangId);
        w.Hostile(gang, rival, Content.GrudgeHijack);
        var info = Content.Rackets[biz.Racket];
        double chance = HijackChance(w, hood, backup, biz);
        gang.Heat = Math.Min(100, gang.Heat + 3 + GunHeat(backup.Prepend(hood)));

        if (w.Rng.Chance(chance))
        {
            int load = info.WeeklyIncome * 2;
            hood.Deeds.Heists++;
            long lost = Math.Min(rival.Cash, info.WeeklyIncome);
            rival.Cash -= lost;
            w.Ledger(rival.Id).Fines += lost;
            gang.Cash += load;
            w.Ledger(gang.Id).Rackets += load;
            string text = $"{hood.Name} hijacked {rival.Name}'s beer truck on its way to {biz.Name} and sold the load for ${load}.";
            w.Log(EventKind.Hijack, gang.Id, text);
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hijack, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Won, text, rival.Id)));
            return;
        }

        int casualty = -1;
        string fail = $"{rival.Name}'s drivers saw off {hood.Name} on the road to {biz.Name}.";
        if (w.Rng.Chance(0.25 * (1 - Getaway(backup.Prepend(hood)))))
        {
            var fallen = backup.Count > 0 && w.Rng.Chance(0.5) ? w.Rng.Pick(backup) : hood;
            Kill(fallen, $"trying to hijack a beer truck");
            casualty = fallen.Id;
        }
        w.Log(EventKind.Hijack, gang.Id, fail);
        w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hijack, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Lost, fail,
            rival.Id, -1, casualty)));
    }

    // ---- People on the street ----------------------------------------------

    private void Hire(Gang gang, HireOrder o)
    {
        var w = World;
        var p = w.PersonById(o.PersonId);
        if (p is not { Kind: PersonKind.Drifter } || gang.Cash < p.Fee) return;
        gang.Cash -= p.Fee;
        w.Ledger(gang.Id).Spending += p.Fee;
        int lot = p.LotId;
        var hood = w.HireDrifter(p, gang);
        string text = $"{gang.Name} signed on {hood.Name} for ${p.Fee}.";
        w.Log(EventKind.Recruited, gang.Id, text);
        w.Act(new ScriptAction(ActionKind.Hire, gang.Id, hood.Id, lot, gang.HqLotId, -1, ActionResult.Success, text));
    }

    private void PayCop(Gang gang, PayCopOrder o)
    {
        var w = World;
        var cop = w.PersonById(o.PersonId);
        if (cop is not { Kind: PersonKind.BeatCop } || cop.PaidByGangId >= 0 || gang.Cash < Content.CopPrice) return;
        string text;
        if (cop.Nerve >= 8)
        {
            gang.Heat = Math.Min(100, gang.Heat + 5);
            text = $"{cop.Name} wouldn't take {gang.Name}'s money and wrote it up.";
            w.Act(new ScriptAction(ActionKind.Hire, gang.Id, -1, gang.HqLotId, cop.LotId, -1, ActionResult.Failed, text));
        }
        else
        {
            gang.Cash -= Content.CopPrice;
            w.Ledger(gang.Id).Spending += Content.CopPrice;
            gang.Heat = Math.Max(0, gang.Heat - Content.CopCooling);
            cop.PaidByGangId = gang.Id;
            text = $"{cop.Name} took ${Content.CopPrice} from {gang.Name} to look the other way in {w.Wards.ElementAtOrDefault(cop.WardId)?.Name ?? "his ward"}.";
            w.Act(new ScriptAction(ActionKind.Hire, gang.Id, -1, gang.HqLotId, cop.LotId, -1, ActionResult.Success, text));
        }
        w.Log(EventKind.Street, gang.Id, text);
    }

    private void BuyStory(Gang gang, BuyStoryOrder o)
    {
        var w = World;
        var reporter = w.PersonById(o.PersonId);
        if (reporter is not { Kind: PersonKind.Reporter } || reporter.GangId != gang.Id) return;
        int price = Content.StoryPrice(gang.Heat);
        if (gang.Cash < price) return;
        gang.Cash -= price;
        w.Ledger(gang.Id).Spending += price;
        w.People.Remove(reporter);
        string text = $"{reporter.Name} dropped his story on {gang.Name} for ${price}.";
        w.Log(EventKind.Street, gang.Id, text);
        w.Act(new ScriptAction(ActionKind.Hire, gang.Id, -1, gang.HqLotId, reporter.LotId, -1, ActionResult.Success, text));
    }

    private void Silence(Gang gang, SilenceOrder o)
    {
        var w = World;
        var witness = w.PersonById(o.PersonId);
        if (witness is not { Kind: PersonKind.Witness } || witness.GangId != gang.Id) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        string text;
        bool ok = w.Rng.Chance(SilenceChance(hood, witness));
        if (ok)
        {
            w.People.Remove(witness);
            gang.Heat = Math.Min(100, gang.Heat + 2);
            text = $"{hood.Name} had a word with {witness.Name}, who has forgotten what he saw.";
        }
        else
        {
            // He goes straight to the precinct.
            witness.WeeksLeft = 1;
            gang.Heat = Math.Min(100, gang.Heat + 6);
            text = $"{witness.Name} wouldn't be leaned on, and went to the police about {hood.Name}'s visit.";
        }
        w.Log(EventKind.Street, gang.Id, text);
        w.Act(new ScriptAction(ActionKind.Silence, gang.Id, hood.Id, gang.HqLotId, witness.LotId, -1, ok ? ActionResult.Success : ActionResult.Failed, text));
    }

    /// <summary>Someone may have seen it, and may go to the police within a few weeks.</summary>
    private void MaybeWitness(Gang gang, Hood hood, Lot lot, string saw, double chance)
    {
        var w = World;
        if (!w.Rng.Chance(chance)) return;
        var witness = w.NewPerson(PersonKind.Witness, lot, 3, gang.Id, hood.Id, saw);
        w.Log(EventKind.Street, gang.Id, $"{witness.Name} saw {saw}, and is thinking about going to the police.");
    }

    /// <summary>A beat cop on this gang's payroll this week walks the ward the business is in.</summary>
    private bool CopLooksAway(Gang gang, Business biz)
    {
        int ward = World.LotOf(biz).WardId;
        return World.People.Any(p => p.Kind == PersonKind.BeatCop && p.PaidByGangId == gang.Id && p.WardId == ward);
    }

    /// <summary>
    /// Sunday on the street: witnesses testify, stories go to print, drifters move on, and new
    /// faces turn up. Banks fill their vaults again and the Treasury looks for funny money.
    /// </summary>
    private void Street()
    {
        var w = World;
        foreach (var p in w.People.ToList())
        {
            if (p.Kind == PersonKind.BeatCop) { p.PaidByGangId = -1; continue; }
            if (--p.WeeksLeft > 0) continue;
            w.People.Remove(p);
            var gang = p.GangId >= 0 ? w.GangById(p.GangId) : null;
            if (p.Kind == PersonKind.Witness && gang is { Alive: true })
            {
                var hood = w.Hoods.FirstOrDefault(h => h.Id == p.HoodId);
                if (hood is not { IsAvailable: true } || hood.GangId != gang.Id) continue;
                gang.Heat = Math.Min(100, gang.Heat + 5);
                Jail(hood, w.Rng.Range(20, 40), $"on {p.Name}'s evidence", strength: 0.9);
            }
            else if (p.Kind == PersonKind.Reporter && gang is { Alive: true })
            {
                gang.Heat = Math.Min(100, gang.Heat + 8);
                w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + 6);
                w.Log(EventKind.Street, gang.Id, $"{p.Name} ran a front-page story on {gang.Name}. The city is talking.");
            }
        }

        foreach (var bank in w.Banks) bank.Vault = Math.Min(bank.Capacity, bank.Vault + bank.Capacity / 8);

        // A beat cop in every ward.
        foreach (var ward in w.Wards)
        {
            if (w.People.Any(p => p.Kind == PersonKind.BeatCop && p.WardId == ward.Id)) continue;
            var lot = StreetCorner(ward.Id);
            if (lot != null) w.NewPerson(PersonKind.BeatCop, lot, 0);
        }

        // Drifters drift in, more in a bigger city.
        int drifters = Math.Max(2, w.Map.BlocksX * w.Map.BlocksY / 12);
        for (int i = 0; i < 2 && w.People.Count(p => p.Kind == PersonKind.Drifter) < drifters; i++)
        {
            if (!w.Rng.Chance(0.7)) continue;
            var lot = StreetCorner(-1);
            if (lot != null) w.NewPerson(PersonKind.Drifter, lot, w.Rng.Range(4, 8));
        }

        // A gang that runs hot gets a reporter on its tail.
        foreach (var gang in w.LivingGangs)
        {
            if (gang.Heat < 55 || w.People.Any(p => p.Kind == PersonKind.Reporter && p.GangId == gang.Id) || !w.Rng.Chance(0.25)) continue;
            var turf = w.TurfOf(gang.Id).ToList();
            var lot = turf.Count > 0 ? w.LotOf(w.Rng.Pick(turf)) : w.HqOf(gang);
            var reporter = w.NewPerson(PersonKind.Reporter, lot, 3, gang.Id, about: $"{gang.Name}'s business");
            w.Log(EventKind.Street, gang.Id, $"{reporter.Name} is asking questions about {gang.Name}.");
        }
    }

    /// <summary>A lot to stand outside: one with a business in the ward if there is one, else anywhere on land.</summary>
    private Lot? StreetCorner(int wardId)
    {
        var w = World;
        var lots = w.Map.Lots.Where(l => l.Terrain == Terrain.Land && (wardId < 0 || l.WardId == wardId)).ToList();
        var busy = lots.Where(l => l.Use == LotUse.Business).ToList();
        var pool = busy.Count > 0 ? busy : lots;
        return pool.Count == 0 ? null : w.Rng.Pick(pool);
    }

    /// <summary>The Treasury finds a counterfeiting press now and then. It can't be bribed.</summary>
    private void FunnyMoney()
    {
        var w = World;
        foreach (var biz in w.Businesses.Where(b => b.Racket == RacketKind.Counterfeit && b.IsProtected).ToList())
        {
            if (!w.Rng.Chance(Content.CounterfeitRisk)) continue;
            var gang = w.GangById(biz.ProtectorGangId);
            long fine = Math.Min(gang.Cash, 1000);
            gang.Cash -= fine;
            w.Ledger(gang.Id).Fines += fine;
            gang.Heat = Math.Min(100, gang.Heat + 10);
            biz.Racket = RacketKind.None;
            biz.ShutWeeks = 6;
            var printer = w.Hoods.FirstOrDefault(h => h.Id == biz.HandlerHoodId);
            if (printer is { IsAvailable: true }) Jail(printer, w.Rng.Range(26, 52), "with counterfeiting", federal: true);
            w.Log(EventKind.Raid, gang.Id, $"Treasury agents found the counterfeiting press behind {biz.Name} and fined {gang.Name} ${fine}.");
        }
    }
}
