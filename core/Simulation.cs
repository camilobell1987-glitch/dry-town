namespace DryTown.Core;

/// <summary>
/// Resolves one week. Each order happens at its own hour between Monday and Saturday;
/// Sunday is collection day and the reckoning. Everything that happens on the street is
/// written to <see cref="World.Script"/> so the live view can replay the week.
/// </summary>
public sealed partial class Simulation
{
    public World World { get; }
    public Director Director { get; }
    public Metrics Metrics { get; } = new();

    public Simulation(World world)
    {
        World = world;
        Director = new Director(world);
    }

    public static Simulation New(WorldSettings settings)
    {
        var sim = new Simulation(World.Create(settings));
        if (settings.Scenario != ScenarioKind.None) Scenarios.Setup(sim);
        return sim;
    }

    /// <summary>Run one week with the given orders. Gangs with no entry get AI orders, including the player when autopilot is true.</summary>
    public void AdvanceWeek(IReadOnlyDictionary<int, List<Order>>? orders = null, bool playerAutopilot = false)
    {
        BeginWeek(orders, playerAutopilot);
        FinishWeek();
    }

    /// <summary>A job on this week's timeline: an order and the hour it happens.</summary>
    public sealed record Job(int Tick, int GangId, Order Order);

    private List<Job> _timeline = new();
    private int _cursor;
    private readonly HashSet<int> _usedHoods = new();
    private readonly Dictionary<int, int> _recruits = new();

    /// <summary>True between <see cref="BeginWeek"/> and <see cref="FinishWeek"/>, while the week can still take orders.</summary>
    public bool WeekRunning { get; private set; }

    /// <summary>Jobs still to happen this week, in time order.</summary>
    public IEnumerable<Job> Upcoming => _timeline.Skip(_cursor);

    /// <summary>The last hour at which a new order can still be carried out this week.</summary>
    public const int LastOrderTick = Content.CollectionTick - 4;

    /// <summary>
    /// Start a week: every gang's orders are given an hour, but nothing happens until
    /// <see cref="RunUntil"/> reaches it. The player can add orders while it runs.
    /// </summary>
    public void BeginWeek(IReadOnlyDictionary<int, List<Order>>? orders = null, bool playerAutopilot = false)
    {
        if (WeekRunning) throw new InvalidOperationException("The week is already running.");
        var w = World;
        NoteStartOfWeek();
        w.LastLedger.Clear();
        w.Script.Clear();
        w.Tick = 0;
        _guards.Clear();
        _usedHoods.Clear();
        _protectorWhenOrdered.Clear();
        _recruits.Clear();

        var plans = new List<(Gang gang, List<Order> orders)>();
        foreach (var g in w.LivingGangs.ToList())
        {
            List<Order> list;
            if (orders != null && orders.TryGetValue(g.Id, out var given)) list = given;
            else if (g.IsPlayer && !playerAutopilot) list = new List<Order>();
            else
            {
                if (g.IsPlayer) AiPlanner.TalkForPlayer(w);
                list = g.IsSmall ? AiPlanner.PlanSmall(w, g) : AiPlanner.Plan(w, g);
            }
            plans.Add((g, list));
        }

        // Every order gets an hour of the working week. Guards take up their posts first thing Monday.
        var timeline = new List<Job>();
        foreach (var (gang, list) in plans)
            foreach (var order in list.Where(o => o.GangId == gang.Id))
                timeline.Add(new Job(TickFor(order), gang.Id, order));
        w.Rng.Shuffle(timeline);
        _timeline = timeline.OrderBy(t => t.Tick).ToList();
        _cursor = 0;

        _protectorAtPlanning = w.Businesses.ToDictionary(b => b.Id, b => b.ProtectorGangId);
        WordFromInside(plans);
        WeekRunning = true;
    }

    /// <summary>Carry out every job due at or before the given hour.</summary>
    public void RunUntil(int tick)
    {
        var w = World;
        while (_cursor < _timeline.Count && _timeline[_cursor].Tick <= tick)
        {
            var (jobTick, gangId, order) = _timeline[_cursor++];
            var gang = w.GangById(gangId);
            if (!gang.Alive) continue;
            w.Tick = jobTick;
            switch (order)
            {
                case GuardOrder o: Guard(gang, o); break;
                case ExtortOrder o: Extort(gang, o); break;
                case RacketOrder o: OpenRacket(gang, o); break;
                case RecruitOrder { Family: true }: BringInFamily(gang); break;
                case RecruitOrder:
                    if (_recruits.GetValueOrDefault(gang.Id) < 2) { _recruits[gang.Id] = _recruits.GetValueOrDefault(gang.Id) + 1; Recruit(gang); }
                    break;
                case BribeOrder o: Bribe(gang, o.Amount); break;
                case SetRateOrder o: SetRate(gang, o); break;
                case PayoffOrder o when o.WardId >= 0 && o.WardId < w.Wards.Count: Politics.Payoff(w, gang, w.Wards[o.WardId]); break;
                case CampaignOrder o: Politics.Campaign(w, gang, o.WardId, o.Amount); break;
                case StandOrder o when o.WardId >= 0 && o.WardId < w.Wards.Count && w.Hoods.FirstOrDefault(h => h.Id == o.HoodId) is { } man:
                    Politics.Stand(w, gang, man, w.Wards[o.WardId]); break;
                case PollsOrder o: WorkPolls(gang, o); break;
                case ArsonOrder o: Arson(gang, o); break;
                case HitOrder o: Hit(gang, o); break;
                case KidnapOrder o: Kidnap(gang, o); break;
                case RobberyOrder o: Robbery(gang, o); break;
                case HijackOrder o: Hijack(gang, o); break;
                case WaylayOrder o: Waylay(gang, o); break;
                case LineOrder o when w.LineById(o.LineId) is { } line: w.BuyLine(gang, line); break;
                case CutOrder o: w.SetCut(gang, o.Cut); break;
                case BoatOrder: w.BuyBoat(gang); break;
                case SailOrder o when w.BoatById(o.BoatId) is { } boat: w.Sail(gang, boat, o.Run); break;
                case FixOrder o: FixCard(gang, o); break;
                case PoachOrder o: Poach(gang, o); break;
                case AbsorbOrder o: Absorb(gang, o); break;
                case WipeOutOrder o: WipeOut(gang, o); break;
                case UnionOrder o: TakeLocal(gang, o); break;
                case StrikeOrder o when w.LocalById(o.LocalId) is { } local && o.BusinessId >= 0 && o.BusinessId < w.Businesses.Count: w.CallStrike(gang, local, w.BusinessById(o.BusinessId)); break;
                case HireOrder o: Hire(gang, o); break;
                case PayCopOrder o: PayCop(gang, o); break;
                case BuyStoryOrder o: BuyStory(gang, o); break;
                case SilenceOrder o: Silence(gang, o); break;
                case AskOrder o: Ask(gang, o); break;
                case TailOrder o: Tail(gang, o); break;
                case ArmOrder o when w.Hoods.FirstOrDefault(h => h.Id == o.HoodId) is { } man: w.Arm(gang, man, o.Weapon); break;
                case CarOrder o when w.Hoods.FirstOrDefault(h => h.Id == o.HoodId) is { } man: w.Drive(gang, man, o.Car); break;
                case JuryOrder o: Jury(gang, o); break;
                case LawyerOrder o: w.Retain(gang, o.Lawyer); break;
                case BailOrder o when w.Hoods.FirstOrDefault(h => h.Id == o.HoodId) is { } man: w.PostBail(gang, man); break;
                case LamOrder o when w.Hoods.FirstOrDefault(h => h.Id == o.HoodId) is { } man: w.SkipTown(gang, man); break;
                case HomeOrder o when w.Hoods.FirstOrDefault(h => h.Id == o.HoodId) is { } man: w.ComeHome(gang, man); break;
                case SoupOrder o when !o.On || w.CannotRunSoupKitchen(gang) == null: gang.SoupKitchen = o.On; break;
                case JudgeOrder o when w.Hoods.FirstOrDefault(h => h.Id == o.DefendantId) is { } man: w.PayJudge(gang, man); break;
                case HuntOrder o: Hunt(gang, o); break;
                case SeenToOrder o: SeeTo(gang, o); break;
                case PlantOrder o: Plant(gang, o); break;
                case RecallOrder o when w.Hoods.FirstOrDefault(h => h.Id == o.HoodId) is { } man: w.Recall(gang, man); break;
                case FamilyOrder o when w.Hoods.FirstOrDefault(h => h.Id == o.HoodId) is { } man: w.LookAfter(gang, man); break;
                case FrontOrder o when o.BusinessId >= 0 && o.BusinessId < w.Businesses.Count: w.BuyFront(gang, w.Businesses[o.BusinessId]); break;
            }
        }
        w.Tick = Math.Max(w.Tick, Math.Min(tick, Content.HoursPerWeek - 1));
    }

    /// <summary>Hours a man takes to walk from his headquarters to a business.</summary>
    public static float WalkHours(World w, int fromLot, int toLot) =>
        Math.Clamp(w.Map.Distance(w.Map.LotAt(fromLot), w.Map.LotAt(toLot)) / 7f, 0.75f, 3f);

    /// <summary>
    /// Give an order while the week is running. The men set out now and it happens when they
    /// arrive. Returns the hour it will happen, or null if it's too late in the week.
    /// </summary>
    public int? OrderNow(Order order, float now)
    {
        if (!WeekRunning) return null;
        var gang = World.GangById(order.GangId);
        int toLot = TargetLot(World, order);
        int tick = Math.Max((int)Math.Ceiling(now + TravelHours(World, gang.HqLotId, toLot, TeamOf(order).Select(World.HoodById))), _timeline.Take(_cursor).LastOrDefault()?.Tick ?? 0);
        if (tick > LastOrderTick) return null;
        var job = new Job(tick, gang.Id, order);
        if (order is ExtortOrder e2) _protectorWhenOrdered[order] = World.BusinessById(e2.BusinessId).ProtectorGangId;
        int at = _cursor;
        while (at < _timeline.Count && _timeline[at].Tick <= tick) at++;
        _timeline.Insert(at, job);
        return tick;
    }

    /// <summary>Where the men carrying out an order are headed. Orders that need nobody to go out stay at headquarters.</summary>
    public static int TargetLot(World w, Order order)
    {
        int hq = w.GangById(order.GangId).HqLotId;
        return order switch
        {
            ExtortOrder e => w.BusinessById(e.BusinessId).LotId,
            GuardOrder g => w.BusinessById(g.BusinessId).LotId,
            RacketOrder r => w.BusinessById(r.BusinessId).LotId,
            ArsonOrder a => w.BusinessById(a.BusinessId).LotId,
            HijackOrder h => w.BusinessById(h.BusinessId).LotId,
            WaylayOrder l when w.LineById(l.LineId) is { } line && w.DropOf(line) is { } drop => drop.LotId,
            UnionOrder u when w.LocalById(u.LocalId) is { LotId: >= 0 } local => local.LotId,
            HitOrder h => HauntOf(w, w.HoodById(h.TargetHoodId)),
            PoachOrder p => HauntOf(w, w.HoodById(p.TargetHoodId)),
            AbsorbOrder a when w.Gangs.FirstOrDefault(g => g.Id == a.TargetGangId) is { HqLotId: >= 0 } small => small.HqLotId,
            WipeOutOrder x when w.Gangs.FirstOrDefault(g => g.Id == x.TargetGangId) is { HqLotId: >= 0 } small => small.HqLotId,
            KidnapOrder k => HauntOf(w, w.HoodById(k.TargetHoodId)),
            RobberyOrder r => w.BankById(r.BankId)?.LotId ?? hq,
            SilenceOrder s => w.PersonById(s.PersonId)?.LotId ?? hq,
            TailOrder t => t.LotId,
            JuryOrder => w.PrecinctNear(w.Map.LotAt(hq)).Id,
            SeenToOrder s when w.Hoods.FirstOrDefault(h => h.Id == s.TargetHoodId) is { } t =>
                t.State == HoodState.Jailed ? w.PrecinctNear(w.Map.LotAt(hq)).Id : HauntOf(w, t),
            PlantOrder p when w.Gangs.FirstOrDefault(g => g.Id == p.TargetGangId) is { HqLotId: >= 0 } host => host.HqLotId,
            PollsOrder p when p.WardId >= 0 && p.WardId < w.Wards.Count => Politics.PollingPlace(w, w.Wards[p.WardId], hq),
            _ => hq,
        };
    }

    /// <summary>Men of a gang who have done a job this week or are on their way to one.</summary>
    public HashSet<int> CommittedHoods(int gangId)
    {
        var set = World.Hoods.Where(h => h.GangId == gangId && _usedHoods.Contains(h.Id)).Select(h => h.Id).ToHashSet();
        foreach (var job in Upcoming.Where(j => j.GangId == gangId))
            foreach (var id in TeamOf(job.Order)) set.Add(id);
        return set;
    }

    public static IEnumerable<int> TeamOf(Order o) => o switch
    {
        ExtortOrder e => e.Team,
        GuardOrder g => g.Team,
        RacketOrder r => new[] { r.HoodId },
        ArsonOrder a => new[] { a.HoodId },
        HitOrder h => h.Team,
        WipeOutOrder x => x.Team,
        AbsorbOrder a => new[] { a.HoodId },
        KidnapOrder k => k.Team,
        RobberyOrder r => r.Team,
        HijackOrder h => h.Team,
        WaylayOrder l => l.Team,
        UnionOrder u => u.Team,
        SilenceOrder s => new[] { s.HoodId },
        TailOrder t => new[] { t.HoodId },
        JuryOrder j => new[] { j.HoodId },
        PollsOrder p => new[] { p.HoodId },
        HuntOrder h => new[] { h.HoodId },
        SeenToOrder s => new[] { s.HoodId },
        PlantOrder p => new[] { p.HoodId },
        FixOrder f => new[] { f.HoodId },
        _ => Array.Empty<int>(),
    };

    /// <summary>Run the rest of the week, then Sunday: collections, the police, the Treasury, and the gangs settling up.</summary>
    public void FinishWeek()
    {
        var w = World;
        RunUntil(Content.CollectionTick - 1);
        WeekRunning = false;

        int takeoversThisWeek = w.Events.Count(e => e.Week == w.Week && e.Kind == EventKind.Takeover);

        w.Tick = Content.CollectionTick;
        Collect();
        Booze();
        Unions();
        PayWages();
        PayLawyers();
        Launder();
        Diplomacy();
        w.Tick = Content.ReckoningTick;
        Police();
        OnTheLam();
        Rats();
        Street();
        Rumours();
        Politics.Step(w, w.Events.Count(e => e.Week == w.Week && e.Kind == EventKind.HoodKilled));
        // After the city's temper has cooled for the week, so the war's share of it stays.
        Wars();
        Feds();
        Untouchables();
        Gmen();
        Reputation();
        w.HoldFunerals();
        Sit();
        CityNews();
        Loyalty();
        Aging();
        Successions();
        Dissolutions();
        w.TidyCrews();
        RivalCrews();
        Director.Step();
        NewSmallGangs();

        Metrics.Record(w, takeoversThisWeek);
        Scenarios.Check(w);

        int yearBefore = w.Year;
        w.Week++;
        if (w.Year == Content.RepealYear && yearBefore != w.Year)
        {
            w.Log(EventKind.Era, -1, "Prohibition is repealed. Liquor is legal again and bootleg margins collapse.");
            CloseLines();
            Repealed();
        }
    }

    // ---- Orders -------------------------------------------------------------

    private Dictionary<int, int> _protectorAtPlanning = new();

    /// <summary>For orders given mid-week, who protected the target when the order was given.</summary>
    private readonly Dictionary<Order, int> _protectorWhenOrdered = new(ReferenceEqualityComparer.Instance);

    /// <summary>Business id to the hood guarding it this week, and the strength his backup adds.</summary>
    private readonly Dictionary<int, (Hood Hood, double Backup)> _guards = new();

    private int TickFor(Order order)
    {
        var rng = World.Rng;
        return order switch
        {
            SetRateOrder or ArmOrder or CarOrder or FrontOrder or LawyerOrder or JudgeOrder or BailOrder or SoupOrder or LamOrder or HomeOrder or RecallOrder or FamilyOrder or LineOrder or CutOrder or BoatOrder or SailOrder or StrikeOrder => 0,
            GuardOrder => 7 + rng.Range(0, 2),
            BribeOrder or PayoffOrder => rng.Range(0, 5) * 24 + rng.Range(19, 22),
            CampaignOrder => rng.Range(0, 5) * 24 + rng.Range(12, 20),
            RecruitOrder or HireOrder => rng.Range(0, 5) * 24 + rng.Range(10, 17),
            PayCopOrder or BuyStoryOrder or AskOrder => rng.Range(0, 5) * 24 + rng.Range(12, 20),
            // Fires are set and trucks stopped after dark; banks are robbed while the doors are open.
            ArsonOrder or HijackOrder or WaylayOrder => rng.Range(0, 5) * 24 + rng.Range(20, 23),
            RobberyOrder => rng.Range(0, 5) * 24 + rng.Range(10, 15),
            _ => rng.Range(0, 5) * 24 + rng.Range(9, 22),
        };
    }

    private void Guard(Gang gang, GuardOrder o)
    {
        var w = World;
        var biz = w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || biz.ProtectorGangId != gang.Id || _guards.ContainsKey(biz.Id)) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        _guards[biz.Id] = (hood, BackupStrength(backup));
        string with = backup.Count > 0 ? $" with {backup.Count} {(backup.Count == 1 ? "man" : "men")}" : "";
        w.Act(new ScriptAction(ActionKind.Guard, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Success,
            $"{hood.Name} is watching {biz.Name}{with} for the rest of the week.") { Backup = backup.Select(h => h.Id).ToList() });
    }

    /// <summary>Backup men who are free to go along; anyone already busy or not available stays behind.</summary>
    private List<Hood> TakeBackup(Gang gang, int[]? ids)
    {
        var team = new List<Hood>();
        foreach (var id in ids ?? Array.Empty<int>())
            if (team.Count < Crew.MaxMembers && TryUseHood(gang, id, out var h)) team.Add(h);
        return team;
    }

    /// <summary>What backup adds to a fight: a share of each man's strength, since only one of them is in front.</summary>
    public static double BackupStrength(IEnumerable<Hood> backup) => backup.Sum(h => h.Strength * 0.35);

    /// <summary>Who answers the door when a rival comes for a business: its guard, else its handler.</summary>
    private Hood? DefenderAt(Gang rival, Business biz)
    {
        if (_guards.TryGetValue(biz.Id, out var guard) && guard.Hood.IsAvailable && guard.Hood.GangId == rival.Id) return guard.Hood;
        var handler = World.Hoods.FirstOrDefault(h => h.Id == biz.HandlerHoodId);
        return handler is { IsAvailable: true } && handler.GangId == rival.Id ? handler : null;
    }

    private bool TryUseHood(Gang gang, int hoodId, out Hood hood)
    {
        hood = World.Hoods.FirstOrDefault(h => h.Id == hoodId)!;
        if (hood == null || hood.GangId != gang.Id || !hood.IsAvailable || _usedHoods.Contains(hoodId)) return false;
        _usedHoods.Add(hoodId);
        return true;
    }

    /// <summary>
    /// Chance a shopkeeper pays up. Owners take a gang less seriously the further its
    /// headquarters is, so spreading out means opening more bases (a later phase).
    /// </summary>
    public static double ExtortChance(World w, Gang gang, Hood hood, Business biz, int backup = 0) =>
        Math.Clamp(0.35 + (hood.Intimidation - biz.Toughness) * 0.08 - Math.Max(0, w.BlocksFromHq(gang, biz) - 2) * 0.04
            + Math.Min(backup, Crew.MaxMembers) * 0.06 + Content.Guns[hood.Weapon].Extort
            + (w.Wards.Count == 0 ? 0 : w.AtHome(hood, biz) ? Content.HomeShakedown : -Content.StrangerShakedown)
            + gang.Fear * Content.FearShakedown, 0.05, 0.95);

    private void Extort(Gang gang, ExtortOrder o)
    {
        var w = World;
        var biz = w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || !biz.IsOpen || biz.ProtectorGangId == gang.Id || !w.CanReach(gang, biz)) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        var backupIds = backup.Select(h => h.Id).ToList();

        // Orders were given against last week's map. If another gang got there first this week,
        // a hood sent to shake down a shopkeeper doesn't start a war on his own initiative.
        int expected = _protectorWhenOrdered.TryGetValue(o, out var p) ? p : _protectorAtPlanning.GetValueOrDefault(biz.Id, -1);
        if (expected != biz.ProtectorGangId)
        {
            string text = $"{hood.Name} found {biz.Name} already under {w.GangById(biz.ProtectorGangId).Name}'s protection and backed off.";
            w.Log(EventKind.ExtortFailed, gang.Id, text);
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Extort, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.BackedOff, text)));
            return;
        }

        if (!biz.IsProtected)
        {
            if (w.Rng.Chance(ExtortChance(w, gang, hood, biz, backup.Count)))
            {
                biz.ProtectorGangId = gang.Id;
                biz.HandlerHoodId = hood.Id;
                biz.ProtectionRate = Content.DefaultRatePercent;
                biz.Resentment = Math.Min(100, biz.Resentment + 10);
                gang.Heat = Math.Min(100, gang.Heat + 1);
                hood.Deeds.Shakedowns++;
                string text = $"{hood.Name} of {gang.Name} now protects {biz.Name}.";
                w.Log(EventKind.Extorted, gang.Id, text);
                w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Extort, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Success, text)));
            }
            else
            {
                biz.Resentment = Math.Min(100, biz.Resentment + 15);
                gang.Heat = Math.Min(100, gang.Heat + 2);
                string text = $"{biz.Name} threw {hood.Name} out.";
                w.Log(EventKind.ExtortFailed, gang.Id, text);
                bool arrested = w.Rng.Chance((0.06 + (10 - hood.Stealth) * 0.01) * (1 - Getaway(backup.Prepend(hood))));
                if (arrested) Jail(hood, w.Rng.Range(2, 8), "for menacing a shopkeeper");
                w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Extort, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id,
                    arrested ? ActionResult.Arrested : ActionResult.Failed, arrested ? $"{text} The police picked him up." : text,
                    CasualtyHoodId: arrested ? hood.Id : -1)));
            }
            return;
        }

        var rival = w.GangById(biz.ProtectorGangId);
        if (KeepsTheirWord(gang, rival.Id)) return;
        w.Hostile(gang, rival, Content.GrudgeTakeover);
        var defender = DefenderAt(rival, biz);
        _guards.TryGetValue(biz.Id, out var guard);
        bool guarded = guard.Hood is { IsAvailable: true } && guard.Hood.GangId == rival.Id;
        double defence = DefenceStrength(w, rival, biz, guarded ? guard.Hood : null, guarded ? guard.Backup : 0);
        // A man inside opens a door for his own side, or warns them who's coming.
        if (w.HasManInside(rival.Id, gang.Id)) defence += Content.TippedOffDefence;
        double attack = StormingStrength(hood, backup) + (w.HasManInside(gang.Id, rival.Id) ? Content.InsideStorm : 0) + w.Rng.Range(0, 6);
        double roll = defence + w.Rng.Range(0, 6);
        gang.Heat = Math.Min(100, gang.Heat + 5 + GunHeat(backup.Prepend(hood)));
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
            w.Renown(gang, fear: Content.FearTakeover);
            hood.Deeds.Takeovers++;
            int casualty = -1;
            // A Thompson gun on the job leaves fewer men standing.
            if (w.Rng.Chance(backup.Prepend(hood).Any(h => h.Weapon == Weapon.TommyGun) ? 0.45 : 0.3))
            {
                var victim = defender ?? PickVictim(rival);
                if (victim != null) { Kill(victim, $"in a fight over {biz.Name}"); casualty = victim.Id; hood.Deeds.Kills++; }
            }
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Takeover, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Won, text,
                rival.Id, defender?.Id ?? -1, casualty)));
        }
        else
        {
            string text = $"{rival.Name} saw off {hood.Name} at {biz.Name}.";
            w.Log(EventKind.TakeoverRepelled, rival.Id, text);
            int casualty = -1;
            if (w.Rng.Chance(0.25 * (1 - Getaway(backup.Prepend(hood)))))
            {
                // Whoever is in front takes the bullet; with backup along, it may be one of them.
                var fallen = backup.Count > 0 && w.Rng.Chance(0.5) ? w.Rng.Pick(backup) : hood;
                Kill(fallen, $"trying to take {biz.Name}");
                casualty = fallen.Id;
                // Whoever answered the door gets the credit.
                if (guarded) guard.Hood!.Deeds.Kills++;
                else if (defender != null) defender.Deeds.Kills++;
            }
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Takeover, gang.Id, hood.Id, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Lost, text,
                rival.Id, defender?.Id ?? -1, casualty)));
        }
    }

    private static ScriptAction BackedBy(List<int> backup, ScriptAction action) => action with { Backup = backup };

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
    public static double DefenceStrength(World world, Gang rival, Business biz, Hood? guard = null, double guardBackup = 0)
    {
        var available = world.AvailableHoodsOf(rival.Id).ToList();
        if (available.Count == 0) return 0;
        double avg = available.OrderByDescending(h => h.Strength).Take(3).Average(h => h.Strength);
        var handler = available.FirstOrDefault(h => h.Id == biz.HandlerHoodId);
        double front = Math.Max(handler?.Strength ?? 0, avg * 0.5);
        // A man posted on the door all week, expecting trouble, fights harder than one called in.
        if (guard != null) front = Math.Max(front, guard.Strength + 3 + guardBackup);
        // A casino keeps its own men on the door.
        if (biz.Racket == RacketKind.Casino && World.Working(biz)) front += Content.CasinoDoor;
        int turf = world.TurfOf(rival.Id).Count();
        double coverage = Math.Min(1.0, available.Count * (double)BusinessesPerHandler / Math.Max(1, turf * 2));
        return front + 3 * coverage;
    }

    private void OpenRacket(Gang gang, RacketOrder o)
    {
        var w = World;
        var biz = w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId);
        if (biz == null || w.CannotOpenRacket(gang, biz, o.Racket) != null) return;
        var info = Content.Rackets[o.Racket];
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;

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
        // A known name brings men in already loyal to it, and a feared one draws harder men.
        hood.Loyalty = Math.Min(100, hood.Loyalty + (int)(gang.Respect * Content.RespectRecruitLoyalty));
        if (gang.Fear >= Content.FearedRecruit) hood.Intimidation = Math.Min(10, hood.Intimidation + 1);
        World.Log(EventKind.Recruited, gang.Id, $"{gang.Name} took on {hood.Name}.");
    }

    /// <summary>Whether a gang can bring a relative in this week: once a year, if it has the money.</summary>
    public static bool CanBringInFamily(World w, Gang gang) =>
        gang.Cash >= Content.FamilyCost && w.Week - gang.LastFamilyWeek >= Content.WeeksPerYear && w.HoodById(gang.BossHoodId).IsActive;

    private void BringInFamily(Gang gang)
    {
        if (!CanBringInFamily(World, gang)) return;
        gang.Cash -= Content.FamilyCost;
        gang.LastFamilyWeek = World.Week;
        World.Ledger(gang.Id).Spending += Content.FamilyCost;
        World.NewRelative(gang);
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
        var supplied = new Dictionary<int, HashSet<int>>();
        foreach (var biz in w.Businesses)
        {
            if (biz.ShutWeeks > 0) { biz.ShutWeeks--; continue; }
            if (!biz.IsProtected) { biz.Resentment = Math.Max(0, biz.Resentment - 2); continue; }

            var gang = w.GangById(biz.ProtectorGangId);
            var ledger = w.Ledger(gang.Id);
            if (!KeepHandler(gang, biz)) continue;
            bool hasMuscle = w.AvailableHoodsOf(gang.Id).Any();
            // Nobody collects from his own front: its money comes in through the books instead.
            bool own = biz.OwnerGangId == gang.Id;
            if (hasMuscle && !own)
            {
                int pay = (int)(biz.Takings * w.TakingsFactor(biz)) * biz.ProtectionRate / 100;
                gang.Cash += pay;
                ledger.Protection += pay;
                w.Act(new ScriptAction(ActionKind.Collect, gang.Id, biz.HandlerHoodId, gang.HqLotId, biz.LotId, biz.Id, ActionResult.Success,
                    $"Collected ${pay} from {biz.Name}."));
            }

            // A shopkeeper minds less paying his own people.
            int home = w.PeopleOf(gang) == w.Neighbourhood(biz) ? Content.HomeResentment : 0;
            biz.Resentment = own ? 0 : Math.Clamp(biz.Resentment + (biz.ProtectionRate - 12) / 2 - 1 - home, 0, 100);

            // A casino or a club is its owners' own business. Whoever takes the place from them gets the rooms, not the tables.
            if (biz.Racket is RacketKind.Casino or RacketKind.Nightclub && !World.Working(biz))
            {
                w.Log(EventKind.Lapsed, gang.Id, $"With {(biz.OwnerGangId >= 0 ? w.GangById(biz.OwnerGangId).Name : "its owners")} gone from it, the {Content.Rackets[biz.Racket].Label} at {biz.Name} shut its doors.");
                biz.Racket = RacketKind.None;
            }

            if (biz.Racket != RacketKind.None)
            {
                var info = Content.Rackets[biz.Racket];
                // Since repeal a brewery is an honest business, at honest prices.
                bool brewing = biz.Racket == RacketKind.Brewery && !w.Prohibition;
                bool legalNow = info.NeedsProhibition && !w.Prohibition && !brewing;
                if (!supplied.TryGetValue(gang.Id, out var real)) supplied[gang.Id] = real = w.Supplied(gang.Id);
                int income = (int)((legalNow ? info.WeeklyIncome * 3 / 10 : brewing ? info.WeeklyIncome / 2 : info.WeeklyIncome) * w.RacketFactor(biz.Racket) * w.BoozeFactor(biz, gang, real));
                int heat = legalNow || brewing ? 0 : w.RacketHeat(biz);
                if (biz.Racket == RacketKind.Bookie) income = Wire(gang, income);
                gang.Cash += income;
                if (brewing)
                {
                    gang.CleanCash += income;
                    ledger.Legit += income;
                }
                else if (biz.Racket == RacketKind.Nightclub && !w.Prohibition)
                {
                    // Since repeal a club pours legal liquor and keeps honest books.
                    gang.CleanCash += income;
                    ledger.Legit += income;
                    heat = 0;
                }
                else if (biz.Racket == RacketKind.Casino)
                {
                    // A casino keeps books like any hotel. Its money comes in clean, but the ward has to look the other way.
                    gang.CleanCash += income;
                    ledger.Legit += income;
                    if (!Politics.Covered(w, gang, biz)) heat = Content.CasinoHeatUncovered;
                }
                else ledger.Rackets += income;
                gang.Heat = Math.Min(100, gang.Heat + heat);
                if (biz.Racket == RacketKind.LoanShark) biz.Resentment = Math.Min(100, biz.Resentment + 1);
            }

            if (biz.Resentment > 60 && w.Rng.Chance((biz.Resentment - 60) / 150.0))
            {
                biz.Resentment -= 20;
                if (Politics.Covered(w, gang, biz))
                {
                    gang.Heat = Math.Min(100, gang.Heat + 2);
                    w.Log(EventKind.Squeal, gang.Id, $"The owner of {biz.Name} complained about {gang.Name}, and Alderman {Politics.WardOf(w, biz).Alderman} made it go away.");
                }
                else
                {
                    gang.Heat = Math.Min(100, gang.Heat + 6);
                    w.Log(EventKind.Squeal, gang.Id, $"The owner of {biz.Name} talked to the police about {gang.Name}.");
                }
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
        FunnyMoney();
        foreach (var gang in w.LivingGangs.ToList())
        {
            // Money that has been through a front's books has a story behind it; the rest doesn't.
            long dirty = gang.Dirty;
            if (dirty < Content.TaxCaseDirty || !w.Rng.Chance(Math.Min(0.04, dirty / 2_500_000.0) * w.TaxCaseFactor)) continue;
            long seized = dirty * 4 / 10;
            gang.Cash -= seized;
            w.Ledger(gang.Id).Fines += seized;
            var boss = w.HoodById(gang.BossHoodId);
            if (boss.IsAvailable)
            {
                Jail(boss, w.Rng.Range(52, 260), "with tax evasion", federal: true);
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
            long bill = hoods.Sum(h => h.State == HoodState.Jailed ? h.Wage / 2 : h.Wage) + MattressBill(w, gang);
            // A small gang's men take a cut of what the corner brings in, not a wage.
            if (gang.IsSmall) bill = (long)(bill * Content.SmallGangWages);
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
            // Friends at City Hall cool things down; a reform mayor keeps the precincts on their toes.
            int aldermen = Math.Min(2, w.Wards.Count(x => x.OwnerGangId == gang.Id));
            int cooling = w.HeatStays ? 0 : 2 + aldermen + (w.Hall.FriendGangId == gang.Id ? 2 : 0);
            gang.Heat = Math.Clamp(gang.Heat - cooling + size / 10, 0, 100);
            // A man on the inside tells the precinct where to look and when.
            bool informer = w.AvailableHoodsOf(gang.Id).Any(h => h.Informer);
            int threshold = (w.Hall.Reform ? 30 : 35) - w.RaidSooner - (informer ? 8 : 0);
            if (gang.Heat <= threshold || !w.Rng.Chance((gang.Heat - threshold) / 120.0)) continue;

            var ledger = w.Ledger(gang.Id);
            var rackets = w.TurfOf(gang.Id).Where(b => b.Racket != RacketKind.None).ToList();
            // An alderman on the payroll tips off the rackets in his ward before the wagons roll.
            var racketSite = rackets.Where(b => informer || !Politics.Covered(w, gang, b) && !CopLooksAway(gang, b)).ToList();
            string what = rackets.Count > racketSite.Count && racketSite.Count == 0 ? ", but the rackets had been tipped off" : "";
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
            if (informer && w.Rng.Chance(0.5))
            {
                gang.TalkWeek = w.Week;
                w.Log(EventKind.Whisper, gang.Id, $"The police knew just where to look. Somebody in {gang.Name} is talking.");
            }

            int arrested = -1;
            var suspects = w.AvailableHoodsOf(gang.Id).ToList();
            if (suspects.Count > 0)
            {
                var hood = w.Rng.Pick(suspects);
                if (!w.Rng.Chance(hood.Stealth * 0.05)) { Jail(hood, w.Rng.Range(6, 30), "after a raid"); arrested = hood.Id; }
            }
            w.Act(new ScriptAction(ActionKind.Raid, -1, -1, w.PrecinctNear(w.Map.LotAt(targetLot)).Id, targetLot, targetBiz,
                arrested >= 0 ? ActionResult.Arrested : ActionResult.Success, text, gang.Id, -1, arrested));
            gang.Heat = Math.Max(0, gang.Heat - 15);
        }

        foreach (var hood in w.Hoods.Where(h => h.State == HoodState.Jailed).ToList())
        {
            if (--hood.JailWeeks > 0) continue;
            if (hood.Case != null) { Trial(hood); continue; }
            hood.State = HoodState.Free;
            hood.LookedAfter = false;
            hood.Loyalty = Math.Max(0, hood.Loyalty - 10);
            // The detectives had weeks to work on him.
            if (hood.Loyalty < Content.InformerLoyalty && !hood.Family && w.Rng.Chance(0.15)) hood.Informer = true;
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
                // A small gang is the block's own boys, and a thin week doesn't change that.
                else if (net < 0 && !gang.IsSmall) hood.Loyalty = Math.Max(0, hood.Loyalty - 1);

                // A crew takes its mood from its lieutenant.
                var crew = w.CrewOfHood(hood.Id);
                if (crew != null && crew.LieutenantHoodId != hood.Id && w.Rng.Chance(0.3))
                {
                    var lt = w.HoodById(crew.LieutenantHoodId);
                    if (lt.Loyalty < hood.Loyalty - 15) hood.Loyalty--;
                    else if (lt.Loyalty > 70 && hood.Loyalty < lt.Loyalty) hood.Loyalty++;
                }

                // Ambitious hoods grow restless as the gang grows around them.
                if (hood.Ambition > 60 && w.TurfOf(gang.Id).Count(b => b.HandlerHoodId == hood.Id) >= 3 && w.Rng.Chance(0.15 + share))
                    hood.Loyalty = Math.Max(0, hood.Loyalty - 1);

                if (!hood.IsAvailable || hood.Loyalty >= 20 || !w.Rng.Chance(0.12)) continue;

                // There is only room for so many outfits: with every place taken, a sour man just walks out.
                if (!gang.IsSmall && hood.Ambition > 65 && w.Outfits.Count() < w.Settings.MaxGangs)
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
        // A lieutenant's own crew goes with him first.
        var crew = w.CrewOfHood(leader.Id) is { } c && c.LieutenantHoodId == leader.Id ? c.MemberIds.ToHashSet() : new HashSet<int>();
        var friends = w.AvailableHoodsOf(from.Id)
            .Where(h => h.Id != leader.Id && h.Id != from.BossHoodId && (h.Loyalty < 60 || crew.Contains(h.Id)))
            .OrderByDescending(h => crew.Contains(h.Id)).ThenBy(h => h.Loyalty).ThenBy(h => h.Id)
            .Take(Math.Max(following, crew.Count)).ToList();
        w.LeaveCrew(leader.Id);
        foreach (var f in friends) w.LeaveCrew(f.Id);

        var gang = w.FoundGang(isPlayer: false, cash: stake + 400, hoods: 1, boss: leader);
        foreach (var f in friends) { f.GangId = gang.Id; f.Loyalty = 60; }
        var defectors = friends.Select(f => f.Id).Append(leader.Id).ToHashSet();
        foreach (var biz in w.Businesses.Where(b => b.ProtectorGangId == from.Id && defectors.Contains(b.HandlerHoodId)))
            biz.ProtectorGangId = gang.Id;
        // Nobody forgives a man who walks out with their men and their turf.
        w.AddGrudge(from.Id, gang.Id, 30);

        w.Log(EventKind.Breakaway, from.Id,
            $"{leader.Name} broke with {from.Name} and formed {gang.Name}, taking {friends.Count} men and {w.TurfOf(gang.Id).Count()} businesses.");
        return gang;
    }

    /// <summary>
    /// Time passes for everyone. Young men learn on the job, old ones slow down, and sooner or
    /// later every man dies, in his bed if he's lucky. The heir is groomed for the chair.
    /// </summary>
    private void Aging()
    {
        var w = World;
        foreach (var gang in w.LivingGangs.ToList())
        {
            foreach (var hood in w.HoodsOf(gang.Id).ToList())
            {
                int age = hood.Age(w.Week);
                if (w.Rng.Chance(Content.YearlyDeathChance(age) / Content.WeeksPerYear))
                {
                    string where = hood.State == HoodState.Jailed ? " in prison" : "";
                    hood.State = HoodState.Dead;
                    string role = hood.Id == gang.BossHoodId ? $", boss of {gang.Name}," : $" of {gang.Name}";
                    w.Log(EventKind.DiedNaturally, gang.Id, $"{hood.Name}{role} died{where} at {age}.");
                    continue;
                }

                bool birthday = (w.Week - hood.BornWeek) % Content.WeeksPerYear == 0;
                if (birthday && age < 30 && w.Rng.Chance(0.6)) Improve(hood);
                if (birthday && age >= 55 && w.Rng.Chance(0.5)) hood.Muscle = Math.Max(1, hood.Muscle - 1);
                if (birthday && age >= 65 && w.Rng.Chance(0.3)) hood.Stealth = Math.Max(1, hood.Stealth - 1);
                if (hood.Id == gang.HeirHoodId && hood.IsAvailable && w.Rng.Chance(1 / 13.0))
                {
                    if (hood.Brains < 10 && w.Rng.Chance(0.6)) hood.Brains++;
                    else Improve(hood);
                }
            }

            // Once a year, an outfit with nobody lined up settles on the obvious man. The player can always name someone else.
            var heir = w.Hoods.FirstOrDefault(h => h.Id == gang.HeirHoodId);
            if (heir == null || heir.GangId != gang.Id || !heir.IsActive) gang.HeirHoodId = -1;
            if (gang.HeirHoodId < 0 && w.Week % Content.WeeksPerYear == 0)
            {
                var pick = BestSuccessor(gang).FirstOrDefault();
                if (pick != null)
                {
                    w.NameHeir(gang, pick, announce: false);
                    if (gang.IsPlayer)
                        w.Log(EventKind.Heir, gang.Id, $"Your men expect {pick.Name} to take over if anything happens to {w.HoodById(gang.BossHoodId).Name}. You can name someone else on the Men tab.");
                }
            }
        }
    }

    private void Improve(Hood hood)
    {
        switch (World.Rng.Range(0, 3))
        {
            case 0: hood.Intimidation = Math.Min(10, hood.Intimidation + 1); break;
            case 1: hood.Muscle = Math.Min(10, hood.Muscle + 1); break;
            case 2: hood.Brains = Math.Min(10, hood.Brains + 1); break;
            default: hood.Stealth = Math.Min(10, hood.Stealth + 1); break;
        }
    }

    /// <summary>Who could run the gang, best first. The named heir comes first if he's free to take over.</summary>
    private IEnumerable<Hood> BestSuccessor(Gang gang) =>
        World.HoodsOf(gang.Id)
            .Where(h => h.IsAvailable && h.Id != gang.BossHoodId)
            .OrderByDescending(h => h.Id == gang.HeirHoodId)
            .ThenByDescending(h => h.Brains * 2 + h.Strength + h.Loyalty / 10 + (h.Family ? 4 : 0))
            .ThenBy(h => h.Id);

    private void Successions()
    {
        var w = World;
        foreach (var gang in w.LivingGangs.ToList())
        {
            var boss = w.Hoods.First(h => h.Id == gang.BossHoodId);
            if (w.RunsFromInside(gang) && boss.IsAvailable) { BackInTheChair(gang, boss); continue; }
            bool inside = w.RunsFromInside(gang) && boss.State == HoodState.Jailed;
            bool longStretch = boss.State == HoodState.Jailed && boss.JailWeeks > Content.LongStretch;
            if (!inside) gang.InsideSinceWeek = -1;
            if (boss.IsActive && !longStretch && !inside) continue;
            if ((longStretch || inside) && FromInside(gang, boss)) continue;
            if (longStretch)
            {
                // He'll come out a lieutenant, and not a happy one.
                boss.Loyalty = 30;
                boss.Ambition = Math.Max(boss.Ambition, 75);
            }

            var heirs = BestSuccessor(gang).ToList();
            if (heirs.Count == 0) continue; // handled by Dissolutions
            if (boss.State == HoodState.Dead) w.Bury(gang, boss);

            var heir = heirs[0];
            bool named = heir.Id == gang.HeirHoodId;
            gang.BossHoodId = heir.Id;
            gang.HeirHoodId = -1;
            heir.Loyalty = 100;
            w.LeaveCrew(heir.Id);
            w.Log(EventKind.Succession, gang.Id, named ? $"{heir.Name} takes over {gang.Name}, as {boss.Name} wanted." : $"{heir.Name} takes over {gang.Name}.");

            var rival = heirs.Skip(1).FirstOrDefault(h => h.Ambition > 70 && h.Loyalty < 55 && h.IsAvailable);
            if (rival != null && !gang.IsSmall && w.Outfits.Count() < w.Settings.MaxGangs)
            {
                // Family by marriage stands behind the new boss, and nobody dares split from him.
                if (w.InLawsOf(gang.Id).OrderBy(g => g.Id).FirstOrDefault() is { } family)
                    w.Log(EventKind.Succession, gang.Id, $"{rival.Name} thought about striking out on his own, but {gang.Name}'s family in {family.Name} stood behind {heir.Name}.");
                else Breakaway(gang, rival);
            }
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
            Politics.Forget(w, gang);
            // A boss with nothing left retires; his men go to the family by marriage, if there is one.
            var family = w.TakeIn(gang, active.Where(h => h.Id != gang.BossHoodId).ToList());
            if (family != null)
            {
                w.HoodById(gang.BossHoodId).State = w.HoodById(gang.BossHoodId).IsActive ? HoodState.Gone : w.HoodById(gang.BossHoodId).State;
                w.Log(EventKind.GangDissolved, gang.Id, $"{gang.Name} is finished. What was left of it went to {family.Name}, its family by marriage.");
                w.ForgetDiplomacy(gang);
                continue;
            }
            foreach (var h in active) h.State = HoodState.Gone;
            foreach (var biz in w.TurfOf(gang.Id).ToList())
            {
                biz.ProtectorGangId = -1;
                biz.HandlerHoodId = -1;
                biz.Racket = RacketKind.None;
            }
            foreach (var biz in w.FrontsOf(gang.Id).ToList()) biz.OwnerGangId = -1;
            w.ForgetDiplomacy(gang);
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
        hood.Weapon = Weapon.None;
        hood.Car = Car.None;
        var gang = World.GangById(hood.GangId);
        gang.Heat = Math.Min(100, gang.Heat + 3);
        World.Log(EventKind.HoodKilled, gang.Id, $"{hood.Name} of {gang.Name} was killed {where}.");
    }

}
