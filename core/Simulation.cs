namespace DryTown.Core;

/// <summary>
/// Resolves one week. Each order happens at its own hour between Monday and Saturday;
/// Sunday is collection day and the reckoning. Everything that happens on the street is
/// written to <see cref="World.Script"/> so the live view can replay the week.
/// </summary>
public sealed class Simulation
{
    public World World { get; }
    public Director Director { get; }
    public Metrics Metrics { get; } = new();

    public Simulation(World world)
    {
        World = world;
        Director = new Director(world);
    }

    public static Simulation New(WorldSettings settings) => new(World.Create(settings));

    /// <summary>Run one week with the given orders. Gangs with no entry get AI orders, including the player when autopilot is true.</summary>
    public void AdvanceWeek(IReadOnlyDictionary<int, List<Order>>? orders = null, bool playerAutopilot = false)
    {
        var w = World;
        w.LastLedger.Clear();
        w.Script.Clear();
        w.Tick = 0;
        _guards.Clear();

        var plans = new List<(Gang gang, List<Order> orders)>();
        foreach (var g in w.LivingGangs.ToList())
        {
            List<Order> list;
            if (orders != null && orders.TryGetValue(g.Id, out var given)) list = given;
            else if (g.IsPlayer && !playerAutopilot) list = new List<Order>();
            else list = AiPlanner.Plan(w, g);
            plans.Add((g, list));
        }

        // Every order gets an hour of the working week. Guards take up their posts first thing Monday.
        var timeline = new List<(int Tick, Gang Gang, Order Order)>();
        foreach (var (gang, list) in plans)
            foreach (var order in list.Where(o => o.GangId == gang.Id))
                timeline.Add((TickFor(order), gang, order));
        w.Rng.Shuffle(timeline);
        timeline = timeline.OrderBy(t => t.Tick).ToList();

        _protectorAtPlanning = w.Businesses.ToDictionary(b => b.Id, b => b.ProtectorGangId);
        var usedHoods = new HashSet<int>();
        var recruits = new Dictionary<int, int>();
        foreach (var (tick, gang, order) in timeline)
        {
            if (!gang.Alive) continue;
            w.Tick = tick;
            switch (order)
            {
                case GuardOrder o: Guard(gang, o, usedHoods); break;
                case ExtortOrder o: Extort(gang, o, usedHoods); break;
                case RacketOrder o: OpenRacket(gang, o, usedHoods); break;
                case RecruitOrder:
                    if (recruits.GetValueOrDefault(gang.Id) < 2) { recruits[gang.Id] = recruits.GetValueOrDefault(gang.Id) + 1; Recruit(gang); }
                    break;
                case BribeOrder o: Bribe(gang, o.Amount); break;
                case SetRateOrder o: SetRate(gang, o); break;
            }
        }

        int takeoversThisWeek = w.Events.Count(e => e.Week == w.Week && e.Kind == EventKind.Takeover);

        w.Tick = Content.CollectionTick;
        Collect();
        PayWages();
        w.Tick = Content.ReckoningTick;
        Police();
        Feds();
        Loyalty();
        Successions();
        Dissolutions();
        Director.Step();

        Metrics.Record(w, takeoversThisWeek);

        int yearBefore = w.Year;
        w.Week++;
        if (w.Year == Content.RepealYear && yearBefore != w.Year)
            w.Log(EventKind.Era, -1, "Prohibition is repealed. Liquor is legal again and bootleg margins collapse.");
    }

    // ---- Orders -------------------------------------------------------------

    private Dictionary<int, int> _protectorAtPlanning = new();

    /// <summary>Business id to the hood guarding it this week.</summary>
    private readonly Dictionary<int, Hood> _guards = new();

    private int TickFor(Order order)
    {
        var rng = World.Rng;
        return order switch
        {
            SetRateOrder => 0,
            GuardOrder => 7 + rng.Range(0, 2),
            BribeOrder => rng.Range(0, 5) * 24 + rng.Range(19, 22),
            RecruitOrder => rng.Range(0, 5) * 24 + rng.Range(10, 17),
            _ => rng.Range(0, 5) * 24 + rng.Range(9, 22),
        };
    }

    private void Guard(Gang gang, GuardOrder o, HashSet<int> used)
    {
        var w = World;
        var biz = w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || biz.ProtectorGangId != gang.Id || _guards.ContainsKey(biz.Id)) return;
        if (!TryUseHood(gang, o.HoodId, used, out var hood)) return;
        _guards[biz.Id] = hood;
        w.Act(new ScriptAction(ActionKind.Guard, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Success,
            $"{hood.Name} is watching {biz.Name} this week."));
    }

    /// <summary>Who answers the door when a rival comes for a business: its guard, else its handler.</summary>
    private Hood? DefenderAt(Gang rival, Business biz)
    {
        if (_guards.TryGetValue(biz.Id, out var guard) && guard.IsAvailable && guard.GangId == rival.Id) return guard;
        var handler = World.Hoods.FirstOrDefault(h => h.Id == biz.HandlerHoodId);
        return handler is { IsAvailable: true } && handler.GangId == rival.Id ? handler : null;
    }

    private bool TryUseHood(Gang gang, int hoodId, HashSet<int> used, out Hood hood)
    {
        hood = World.Hoods.FirstOrDefault(h => h.Id == hoodId)!;
        if (hood == null || hood.GangId != gang.Id || !hood.IsAvailable || used.Contains(hoodId)) return false;
        used.Add(hoodId);
        return true;
    }

    /// <summary>
    /// Chance a shopkeeper pays up. Owners take a gang less seriously the further its
    /// headquarters is, so spreading out means opening more bases (a later phase).
    /// </summary>
    public static double ExtortChance(World w, Gang gang, Hood hood, Business biz) =>
        Math.Clamp(0.35 + (hood.Intimidation - biz.Toughness) * 0.08 - Math.Max(0, w.BlocksFromHq(gang, biz) - 2) * 0.04, 0.05, 0.95);

    private void Extort(Gang gang, ExtortOrder o, HashSet<int> used)
    {
        var w = World;
        var biz = w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || !biz.IsOpen || biz.ProtectorGangId == gang.Id) return;
        if (!TryUseHood(gang, o.HoodId, used, out var hood)) return;

        // Orders were given against last week's map. If another gang got there first this week,
        // a hood sent to shake down a shopkeeper doesn't start a war on his own initiative.
        if (_protectorAtPlanning.GetValueOrDefault(biz.Id, -1) != biz.ProtectorGangId)
        {
            string text = $"{hood.Name} found {biz.Name} already under {w.GangById(biz.ProtectorGangId).Name}'s protection and backed off.";
            w.Log(EventKind.ExtortFailed, gang.Id, text);
            w.Act(new ScriptAction(ActionKind.Extort, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.BackedOff, text));
            return;
        }

        if (!biz.IsProtected)
        {
            if (w.Rng.Chance(ExtortChance(w, gang, hood, biz)))
            {
                biz.ProtectorGangId = gang.Id;
                biz.HandlerHoodId = hood.Id;
                biz.ProtectionRate = Content.DefaultRatePercent;
                biz.Resentment = Math.Min(100, biz.Resentment + 10);
                gang.Heat = Math.Min(100, gang.Heat + 1);
                string text = $"{hood.Name} of {gang.Name} now protects {biz.Name}.";
                w.Log(EventKind.Extorted, gang.Id, text);
                w.Act(new ScriptAction(ActionKind.Extort, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Success, text));
            }
            else
            {
                biz.Resentment = Math.Min(100, biz.Resentment + 15);
                gang.Heat = Math.Min(100, gang.Heat + 2);
                string text = $"{biz.Name} threw {hood.Name} out.";
                w.Log(EventKind.ExtortFailed, gang.Id, text);
                bool arrested = w.Rng.Chance(0.06 + (10 - hood.Stealth) * 0.01);
                if (arrested) Jail(hood, w.Rng.Range(2, 8), "for menacing a shopkeeper");
                w.Act(new ScriptAction(ActionKind.Extort, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id,
                    arrested ? ActionResult.Arrested : ActionResult.Failed, arrested ? $"{text} The police picked him up." : text,
                    CasualtyHoodId: arrested ? hood.Id : -1));
            }
            return;
        }

        var rival = w.GangById(biz.ProtectorGangId);
        var defender = DefenderAt(rival, biz);
        _guards.TryGetValue(biz.Id, out var guard);
        double defence = DefenceStrength(w, rival, biz, guard is { IsAvailable: true } && guard.GangId == rival.Id ? guard : null);
        double attack = hood.Strength + w.Rng.Range(0, 6);
        double roll = defence + w.Rng.Range(0, 6);
        gang.Heat = Math.Min(100, gang.Heat + 5);
        rival.Heat = Math.Min(100, rival.Heat + 2);
        biz.Resentment = Math.Min(100, biz.Resentment + 12);

        if (attack > roll)
        {
            biz.ProtectorGangId = gang.Id;
            biz.HandlerHoodId = hood.Id;
            biz.ProtectionRate = Content.DefaultRatePercent;
            string text = biz.Racket != RacketKind.None
                ? $"{gang.Name} seized {biz.Name} and its {Content.Rackets[biz.Racket].Label} from {rival.Name}."
                : $"{gang.Name} muscled {rival.Name} out of {biz.Name}.";
            w.Log(EventKind.Takeover, gang.Id, text);
            int casualty = -1;
            if (w.Rng.Chance(0.3))
            {
                var victim = defender ?? PickVictim(rival);
                if (victim != null) { Kill(victim, $"in a fight over {biz.Name}"); casualty = victim.Id; }
            }
            w.Act(new ScriptAction(ActionKind.Takeover, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Won, text,
                rival.Id, defender?.Id ?? -1, casualty));
        }
        else
        {
            string text = $"{rival.Name} saw off {hood.Name} at {biz.Name}.";
            w.Log(EventKind.TakeoverRepelled, rival.Id, text);
            int casualty = -1;
            if (w.Rng.Chance(0.25)) { Kill(hood, $"trying to take {biz.Name}"); casualty = hood.Id; }
            w.Act(new ScriptAction(ActionKind.Takeover, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Lost, text,
                rival.Id, defender?.Id ?? -1, casualty));
        }
    }

    /// <summary>Exact chance an attacker of the given strength beats a defence, given both roll 0 to 6 on top.</summary>
    public static double TakeoverChance(double attackerStrength, double defence)
    {
        int wins = 0;
        for (int a = 0; a <= 6; a++)
            for (int d = 0; d <= 6; d++)
                if (attackerStrength + a > defence + d) wins++;
        return wins / 49.0;
    }

    /// <summary>
    /// How hard a business is to take. Mostly it's the hood who handles it; the rest of the gang
    /// only helps if it isn't spread thin, so sprawling gangs are easy to pick at around the edges.
    /// </summary>
    public static double DefenceStrength(World world, Gang rival, Business biz, Hood? guard = null)
    {
        var available = world.AvailableHoodsOf(rival.Id).ToList();
        if (available.Count == 0) return 0;
        double avg = available.OrderByDescending(h => h.Strength).Take(3).Average(h => h.Strength);
        var handler = available.FirstOrDefault(h => h.Id == biz.HandlerHoodId);
        double front = Math.Max(handler?.Strength ?? 0, avg * 0.5);
        // A man posted on the door all week, expecting trouble, fights harder than one called in.
        if (guard != null) front = Math.Max(front, guard.Strength + 3);
        int turf = world.TurfOf(rival.Id).Count();
        double coverage = Math.Min(1.0, available.Count * (double)BusinessesPerHandler / Math.Max(1, turf * 2));
        return front + 3 * coverage;
    }

    private void OpenRacket(Gang gang, RacketOrder o, HashSet<int> used)
    {
        var w = World;
        var biz = w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || biz.ProtectorGangId != gang.Id || biz.Racket != RacketKind.None || !biz.IsOpen) return;
        if (!Content.Rackets.TryGetValue(o.Racket, out var info)) return;
        if (!Content.RacketsFor(biz.Kind).Contains(o.Racket)) return;
        if (info.NeedsProhibition && !w.Prohibition) return;
        if (gang.Cash < info.SetupCost) return;
        if (!TryUseHood(gang, o.HoodId, used, out var hood)) return;

        gang.Cash -= info.SetupCost;
        w.Ledger(gang.Id).Spending += info.SetupCost;
        bool ok = w.Rng.Chance(0.6 + hood.Brains * 0.04);
        string text = ok
            ? $"{gang.Name} opened a {info.Label} behind {biz.Name}."
            : $"{hood.Name} botched setting up a {info.Label}; the money is gone.";
        if (ok) biz.Racket = o.Racket;
        else gang.Heat = Math.Min(100, gang.Heat + 4);
        w.Log(EventKind.RacketOpened, gang.Id, text);
        w.Act(new ScriptAction(ActionKind.Racket, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ok ? ActionResult.Success : ActionResult.Failed, text));
    }

    private void Recruit(Gang gang)
    {
        if (gang.Cash < Content.RecruitCost) return;
        gang.Cash -= Content.RecruitCost;
        World.Ledger(gang.Id).Spending += Content.RecruitCost;
        var hood = World.NewHood(gang.Id, bossQuality: false);
        World.Log(EventKind.Recruited, gang.Id, $"{gang.Name} took on {hood.Name}.");
    }

    private void Bribe(Gang gang, int amount)
    {
        int units = Math.Clamp(amount / Content.BribeUnit, 0, 5);
        units = (int)Math.Min(units, gang.Cash / Content.BribeUnit);
        if (units <= 0) return;
        gang.Cash -= units * Content.BribeUnit;
        World.Ledger(gang.Id).Spending += units * Content.BribeUnit;
        gang.Heat = Math.Max(0, gang.Heat - units * Content.HeatPerBribeUnit);
        World.Log(EventKind.Bribe, gang.Id, $"{gang.Name} paid off the precinct (${units * Content.BribeUnit}).");
    }

    private void SetRate(Gang gang, SetRateOrder o)
    {
        var biz = World.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || biz.ProtectorGangId != gang.Id) return;
        biz.ProtectionRate = Math.Clamp(o.RatePercent, 5, 30);
    }

    // ---- Week resolution ----------------------------------------------------

    private void Collect()
    {
        var w = World;
        foreach (var biz in w.Businesses)
        {
            if (biz.ShutWeeks > 0) { biz.ShutWeeks--; continue; }
            if (!biz.IsProtected) { biz.Resentment = Math.Max(0, biz.Resentment - 2); continue; }

            var gang = w.GangById(biz.ProtectorGangId);
            var ledger = w.Ledger(gang.Id);
            if (!KeepHandler(gang, biz)) continue;
            bool hasMuscle = w.AvailableHoodsOf(gang.Id).Any();
            if (hasMuscle)
            {
                int pay = biz.Takings * biz.ProtectionRate / 100;
                gang.Cash += pay;
                ledger.Protection += pay;
                w.Act(new ScriptAction(ActionKind.Collect, gang.Id, biz.HandlerHoodId, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Success,
                    $"Collected ${pay} from {biz.Name}."));
            }

            biz.Resentment = Math.Clamp(biz.Resentment + (biz.ProtectionRate - 12) / 2 - 1, 0, 100);

            if (biz.Racket != RacketKind.None)
            {
                var info = Content.Rackets[biz.Racket];
                bool legalNow = info.NeedsProhibition && !w.Prohibition;
                int income = legalNow ? info.WeeklyIncome * 3 / 10 : info.WeeklyIncome;
                int heat = legalNow ? 0 : info.WeeklyHeat;
                gang.Cash += income;
                ledger.Rackets += income;
                gang.Heat = Math.Min(100, gang.Heat + heat);
                if (biz.Racket == RacketKind.LoanShark) biz.Resentment = Math.Min(100, biz.Resentment + 1);
            }

            if (biz.Resentment > 60 && w.Rng.Chance((biz.Resentment - 60) / 150.0))
            {
                gang.Heat = Math.Min(100, gang.Heat + 6);
                biz.Resentment -= 20;
                w.Log(EventKind.Squeal, gang.Id, $"The owner of {biz.Name} talked to the police about {gang.Name}.");
            }
        }
    }

    public const int BusinessesPerHandler = 4;

    /// <summary>
    /// Every protected business needs a hood to answer to. When its handler is gone the gang
    /// reassigns it if someone has room; otherwise the owner may simply stop paying.
    /// </summary>
    private bool KeepHandler(Gang gang, Business biz)
    {
        var w = World;
        var handler = w.Hoods.FirstOrDefault(h => h.Id == biz.HandlerHoodId);
        if (handler is { IsAvailable: true } && handler.GangId == gang.Id) return true;

        var load = w.TurfOf(gang.Id).Where(b => b.HandlerHoodId >= 0).GroupBy(b => b.HandlerHoodId).ToDictionary(g => g.Key, g => g.Count());
        var spare = w.AvailableHoodsOf(gang.Id)
            .Where(h => load.GetValueOrDefault(h.Id) < BusinessesPerHandler)
            .OrderBy(h => load.GetValueOrDefault(h.Id)).ThenBy(h => h.Id)
            .FirstOrDefault();
        if (spare != null) { biz.HandlerHoodId = spare.Id; return true; }

        if (!w.Rng.Chance(0.08)) return true;
        biz.ProtectorGangId = -1;
        biz.HandlerHoodId = -1;
        biz.Racket = RacketKind.None;
        w.Log(EventKind.Lapsed, gang.Id, $"With nobody coming round to collect, {biz.Name} stopped paying {gang.Name}.");
        return false;
    }

    /// <summary>
    /// Federal tax cases. Unlike the precinct, the Treasury can't be bribed, and it goes after
    /// visible wealth: the richer a gang gets, the likelier its boss goes away.
    /// </summary>
    private void Feds()
    {
        var w = World;
        foreach (var gang in w.LivingGangs.ToList())
        {
            if (gang.Cash < 25_000 || !w.Rng.Chance(Math.Min(0.04, gang.Cash / 2_500_000.0))) continue;
            long seized = gang.Cash * 4 / 10;
            gang.Cash -= seized;
            w.Ledger(gang.Id).Fines += seized;
            var boss = w.HoodById(gang.BossHoodId);
            if (boss.IsAvailable)
            {
                Jail(boss, w.Rng.Range(52, 260), "for tax evasion");
                w.Log(EventKind.Raid, gang.Id, $"Treasury agents brought a tax case against {gang.Name}: ${seized} seized and {boss.Name} convicted.");
            }
            else
            {
                w.Log(EventKind.Raid, gang.Id, $"Treasury agents seized ${seized} from {gang.Name}.");
            }
        }
    }

    private void PayWages()
    {
        var w = World;
        foreach (var gang in w.LivingGangs)
        {
            var hoods = w.HoodsOf(gang.Id).Where(h => h.Id != gang.BossHoodId).ToList();
            long bill = hoods.Sum(h => h.State == HoodState.Jailed ? h.Wage / 2 : h.Wage);
            var ledger = w.Ledger(gang.Id);
            if (gang.Cash >= bill)
            {
                gang.Cash -= bill;
                ledger.Wages += bill;
                gang.ConsecutiveUnpaidWeeks = 0;
            }
            else
            {
                ledger.Wages += gang.Cash;
                gang.Cash = 0;
                gang.ConsecutiveUnpaidWeeks++;
                foreach (var h in hoods) h.Loyalty = Math.Max(0, h.Loyalty - 6);
            }
        }
    }

    private void Police()
    {
        var w = World;
        foreach (var gang in w.LivingGangs)
        {
            // Big organisations are hard to hide.
            int size = w.TurfOf(gang.Id).Count();
            gang.Heat = Math.Clamp(gang.Heat - 2 + size / 10, 0, 100);
            if (gang.Heat <= 35 || !w.Rng.Chance((gang.Heat - 35) / 120.0)) continue;

            var ledger = w.Ledger(gang.Id);
            var racketSite = w.TurfOf(gang.Id).Where(b => b.Racket != RacketKind.None).ToList();
            string what = "";
            int targetLot = gang.HqLotId, targetBiz = -1;
            if (racketSite.Count > 0)
            {
                var site = w.Rng.Pick(racketSite);
                what = $" and closed the {Content.Rackets[site.Racket].Label} at {site.Name}";
                site.Racket = RacketKind.None;
                site.ShutWeeks = 6;
                targetLot = site.LotId;
                targetBiz = site.Id;
            }
            long fine = Math.Min(gang.Cash, 200 + gang.Heat * 5 + gang.Cash / 50);
            gang.Cash -= fine;
            ledger.Fines += fine;
            string text = $"Police raided {gang.Name}{what}. Fines ${fine}.";
            w.Log(EventKind.Raid, gang.Id, text);

            int arrested = -1;
            var suspects = w.AvailableHoodsOf(gang.Id).ToList();
            if (suspects.Count > 0)
            {
                var hood = w.Rng.Pick(suspects);
                if (!w.Rng.Chance(hood.Stealth * 0.05)) { Jail(hood, w.Rng.Range(6, 30), "after a raid"); arrested = hood.Id; }
            }
            w.Act(new ScriptAction(ActionKind.Raid, -1, -1, w.Precinct.Id, targetLot, targetBiz,
                arrested >= 0 ? ActionResult.Arrested : ActionResult.Success, text, gang.Id, -1, arrested));
            gang.Heat = Math.Max(0, gang.Heat - 15);
        }

        foreach (var hood in w.Hoods.Where(h => h.State == HoodState.Jailed))
        {
            if (--hood.JailWeeks > 0) continue;
            hood.State = HoodState.Free;
            hood.Loyalty = Math.Max(0, hood.Loyalty - 10);
            w.Log(EventKind.HoodReleased, hood.GangId, $"{hood.Name} is out of jail.");
        }
    }

    private void Loyalty()
    {
        var w = World;
        foreach (var gang in w.LivingGangs.ToList())
        {
            long net = w.Ledger(gang.Id).Net;
            double share = (double)w.TurfOf(gang.Id).Count() / w.Businesses.Count;
            foreach (var hood in w.HoodsOf(gang.Id).ToList())
            {
                if (hood.Id == gang.BossHoodId) continue;
                if (net > 0 && hood.Loyalty < 85) hood.Loyalty++;
                else if (net < 0) hood.Loyalty = Math.Max(0, hood.Loyalty - 1);

                // Ambitious hoods grow restless as the gang grows around them.
                if (hood.Ambition > 60 && w.TurfOf(gang.Id).Count(b => b.HandlerHoodId == hood.Id) >= 3 && w.Rng.Chance(0.15 + share))
                    hood.Loyalty = Math.Max(0, hood.Loyalty - 1);

                if (!hood.IsAvailable || hood.Loyalty >= 20 || !w.Rng.Chance(0.12)) continue;

                if (hood.Ambition > 65 && w.LivingGangs.Count() < Content.MaxGangs)
                    Breakaway(gang, hood);
                else
                {
                    hood.State = HoodState.Gone;
                    w.Log(EventKind.Deserted, gang.Id, $"{hood.Name} walked out on {gang.Name}.");
                }
            }
        }
    }

    /// <summary>A hood quits with the businesses he handles and a few friends, and founds a new gang.</summary>
    public Gang Breakaway(Gang from, Hood leader)
    {
        var w = World;
        long stake = from.Cash * 15 / 100;
        from.Cash -= stake;
        // A split in a big outfit is a faction, not a man and his cousin.
        int following = Math.Max(2, w.HoodsOf(from.Id).Count() / 4);
        var friends = w.AvailableHoodsOf(from.Id)
            .Where(h => h.Id != leader.Id && h.Id != from.BossHoodId && h.Loyalty < 60)
            .OrderBy(h => h.Loyalty).ThenBy(h => h.Id).Take(following).ToList();

        var gang = w.FoundGang(isPlayer: false, cash: stake + 400, hoods: 1, boss: leader);
        foreach (var f in friends) { f.GangId = gang.Id; f.Loyalty = 60; }
        var defectors = friends.Select(f => f.Id).Append(leader.Id).ToHashSet();
        foreach (var biz in w.Businesses.Where(b => b.ProtectorGangId == from.Id && defectors.Contains(b.HandlerHoodId)))
            biz.ProtectorGangId = gang.Id;

        w.Log(EventKind.Breakaway, from.Id,
            $"{leader.Name} broke with {from.Name} and formed {gang.Name}, taking {friends.Count} men and {w.TurfOf(gang.Id).Count()} businesses.");
        return gang;
    }

    private void Successions()
    {
        var w = World;
        foreach (var gang in w.LivingGangs.ToList())
        {
            var boss = w.Hoods.First(h => h.Id == gang.BossHoodId);
            bool longStretch = boss.State == HoodState.Jailed && boss.JailWeeks > 26;
            if (boss.IsActive && !longStretch) continue;
            if (longStretch)
            {
                // He'll come out a lieutenant, and not a happy one.
                boss.Loyalty = 30;
                boss.Ambition = Math.Max(boss.Ambition, 75);
            }

            var heirs = w.HoodsOf(gang.Id).Where(h => h.IsAvailable).OrderByDescending(h => h.Brains * 2 + h.Strength + h.Loyalty / 10).ToList();
            if (heirs.Count == 0) continue; // handled by Dissolutions

            var heir = heirs[0];
            gang.BossHoodId = heir.Id;
            heir.Loyalty = 100;
            w.Log(EventKind.Succession, gang.Id, $"{heir.Name} takes over {gang.Name}.");

            var rival = heirs.Skip(1).FirstOrDefault(h => h.Ambition > 70 && h.Loyalty < 55 && h.IsAvailable);
            if (rival != null && w.LivingGangs.Count() < Content.MaxGangs) Breakaway(gang, rival);
        }
    }

    private void Dissolutions()
    {
        var w = World;
        foreach (var gang in w.LivingGangs.ToList())
        {
            var active = w.HoodsOf(gang.Id).ToList();
            bool broke = !w.AvailableHoodsOf(gang.Id).Any() && gang.Cash < Content.RecruitCost && !w.TurfOf(gang.Id).Any();
            // A boss with no men, no turf and no money to hire is just a man. Rival gangs in that
            // state fold so they don't take up room in the district; the player decides for himself.
            bool spent = !gang.IsPlayer && active.Count <= 1 && !w.TurfOf(gang.Id).Any()
                && gang.Cash < Content.RecruitCost * 4 && w.Week - gang.FoundedWeek > 8;
            if (active.Count > 0 && !broke && !spent) continue;

            gang.Alive = false;
            gang.DissolvedWeek = w.Week;
            w.ReleaseHeadquarters(gang);
            foreach (var h in active) h.State = HoodState.Gone;
            foreach (var biz in w.TurfOf(gang.Id).ToList())
            {
                biz.ProtectorGangId = -1;
                biz.HandlerHoodId = -1;
                biz.Racket = RacketKind.None;
            }
            w.Log(EventKind.GangDissolved, gang.Id, $"{gang.Name} is finished.");
        }
    }

    // ---- Violence and arrests ----------------------------------------------

    private Hood? PickVictim(Gang gang)
    {
        var victims = World.AvailableHoodsOf(gang.Id).ToList();
        if (victims.Count == 0) return null;
        // Bosses are hard to reach while they have soldiers around them.
        var soldiers = victims.Where(h => h.Id != gang.BossHoodId).ToList();
        return soldiers.Count > 0 && !World.Rng.Chance(0.08) ? World.Rng.Pick(soldiers) : World.Rng.Pick(victims);
    }

    private void Kill(Hood hood, string where)
    {
        hood.State = HoodState.Dead;
        var gang = World.GangById(hood.GangId);
        gang.Heat = Math.Min(100, gang.Heat + 3);
        World.Log(EventKind.HoodKilled, gang.Id, $"{hood.Name} of {gang.Name} was killed {where}.");
    }

    private void Jail(Hood hood, int weeks, string why)
    {
        hood.State = HoodState.Jailed;
        hood.JailWeeks = weeks;
        World.Log(EventKind.HoodJailed, hood.GangId, $"{hood.Name} got {weeks} weeks {why}.");
    }
}
