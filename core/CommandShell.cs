using System.Text;

namespace DryTown.Core;

/// <summary>
/// Text command front-end for the planning phase. The console runner and the Godot
/// screen both drive the game through this, so they always behave the same.
/// </summary>
public sealed class CommandShell
{
    public Simulation Sim { get; }
    public List<Order> Pending { get; private set; } = new();

    public CommandShell(Simulation sim, IEnumerable<Order>? pending = null)
    {
        Sim = sim;
        if (pending != null) Pending = pending.ToList();
    }

    private World W => Sim.World;
    private int Me => W.Player.Id;

    public string Prompt => $"[{Reports.Date(W)} | ${W.Player.Cash} | heat {W.Player.Heat} | {Pending.Count} orders]";

    public string Welcome => $"You run {W.Player.Name}. {Reports.Date(W)}. Type 'help' for commands.\n";

    public static readonly string Help =
        "gangs | hoods | turf | targets | log\n" +
        "extort <hood> <biz> | racket <hood> <biz> <speakeasy|still|numbers|loanshark|bookie|gamblingden|counterfeit>\n" +
        "guard <hood> <biz> | recruit | bribe <dollars> | rate <biz> <percent>\n" +
        "crews | crew new <lieutenant> | crew add <crew> <hood> | crew drop <hood>\n" +
        "send <crew> <biz> (the crew leans on it or takes it) | post <crew> <biz> (the crew guards it)\n" +
        "heir <hood> (name who takes over) | family (bring a young relative in, $" + Content.FamilyCost + ", once a year)\n" +
        "wards (City Hall) | payoff <ward> (put the alderman on the payroll) | campaign <ward|mayor> <dollars>\n" +
        "stand <hood> <ward> (run him for alderman) | polls <hood> <ward> (lean on voters, last " + Content.PollsWeeks + " weeks) | results (the last vote)\n" +
        "street (people about) | hire <person> | paycop <person> | buystory <person> | silence <hood> <person>\n" +
        "torch <hood> <biz> | hit <hood> <target hood> [backup..] | banks | rob <hood> <bank> [backup..] | hijack <hood> <biz> [backup..]\n" +
        "shops (guns and cars) | arm <hood> <pistol|shotgun|tommygun> | drive <hood> <modelt|packard|cadillac> | hand <from> <to> <gun|car>\n" +
        "fronts (clean and dirty money) | buy <biz> (buy a business you protect, as a front)\n" +
        "cases (men held for trial) | lawyer <none|storefront|mouthpiece> | judge <hood> (pay off his judge) | jury <hood> <defendant>\n" +
        "sheet <hood> (any man's rap sheet) | news (what's going on in the city)\n" +
        "rats (who's talking, and your men inside) | care <hood> (look after a jailed man's family) | seeto <hood> <rat>\n" +
        "hunt <hood> (look for a rat) | plant <hood> <gang> (sign him on with a rival) | recall <hood> (bring him home)\n" +
        "relations (grudges, pacts and offers) | truce <gang> [tribute] | ally <gang> | demand <gang> <dollars> | break <gang> | accept <offer> | decline <offer>\n" +
        "auto (plan the week for me) | orders | clear | end (run the week)\n";

    /// <summary>Run one command. Returns the text to show, or null for "quit".</summary>
    public string? Execute(string line)
    {
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) return "";
        try
        {
            switch (p[0].ToLowerInvariant())
            {
                case "help": return Help;
                case "gangs": return Reports.Gangs(W);
                case "hoods": return Reports.Hoods(W, Me);
                case "turf": return Reports.Businesses(W, b => b.ProtectorGangId == Me);
                case "targets": return Reports.Businesses(W, b => b.ProtectorGangId != Me);
                case "log": return string.Join("\n", W.Events.TakeLast(30).Select(e => $"  {e.Text}")) + "\n";
                case "extort": return Queue(new ExtortOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "racket": return Queue(new RacketOrder(Me, int.Parse(p[1]), int.Parse(p[2]), Enum.Parse<RacketKind>(p[3], true)));
                case "guard": return Queue(new GuardOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "recruit": return Queue(new RecruitOrder(Me));
                case "bribe": return Queue(new BribeOrder(Me, int.Parse(p[1])));
                case "rate": return Queue(new SetRateOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "crews": return Reports.Crews(W, Me);
                case "wards": return Reports.Wards(W, Me);
                case "payoff": return Queue(new PayoffOrder(Me, int.Parse(p[1])));
                case "campaign": return Queue(new CampaignOrder(Me, p[1].ToLowerInvariant() == "mayor" ? -1 : int.Parse(p[1]), int.Parse(p[2])));
                case "stand": return Queue(new StandOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "polls": return Queue(new PollsOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "results": return Reports.Results(W);
                case "news": return Reports.News(W);
                case "street": return Reports.Street(W, Me);
                case "banks": return Reports.Banks(W, Me);
                case "hire": return Queue(new HireOrder(Me, int.Parse(p[1])));
                case "paycop": return Queue(new PayCopOrder(Me, int.Parse(p[1])));
                case "buystory": return Queue(new BuyStoryOrder(Me, int.Parse(p[1])));
                case "silence": return Queue(new SilenceOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "torch": return Queue(new ArsonOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "hit": return Queue(new HitOrder(Me, int.Parse(p[1]), int.Parse(p[2]), Rest(p, 3)));
                case "rob": return Queue(new RobberyOrder(Me, int.Parse(p[1]), int.Parse(p[2]), Rest(p, 3)));
                case "hijack": return Queue(new HijackOrder(Me, int.Parse(p[1]), int.Parse(p[2]), Rest(p, 3)));
                case "shops": return Reports.Shops(W, Me);
                case "fronts": return Reports.Fronts(W, Me);
                case "buy": return BuyFront(int.Parse(p[1]));
                case "cases": return Reports.Cases(W, Me);
                case "relations": return Reports.Relations(W, Me);
                case "sheet":
                    if (W.Hoods.FirstOrDefault(h => h.Id == int.Parse(p[1]) && h.IsActive) is not { } man) return "No such man. Type 'hoods'.\n";
                    return Reports.Sheet(W, man);
                case "truce": return Talk(p[1], g => W.Propose(W.Player, g, PactKind.Truce, p.Length > 2 ? int.Parse(p[2]) : 0));
                case "ally": return Talk(p[1], g => W.Propose(W.Player, g, PactKind.Alliance));
                case "demand": return Talk(p[1], g => W.Demand(W.Player, g, int.Parse(p[2])));
                case "break": return Talk(p[1], g => W.Break(W.Player, g));
                case "accept":
                case "decline":
                    if (W.Offers.FirstOrDefault(o => o.Id == int.Parse(p[1])) is not { } offer) return "No such offer. Type 'relations'.\n";
                    return W.Answer(offer, p[0].ToLowerInvariant() == "accept") + "\n";
                case "lawyer":
                    W.Retain(W.Player, Enum.Parse<Lawyer>(p[1], true));
                    return $"You now keep {Content.Lawyers[W.Player.Lawyer].Label}" + (W.Player.Lawyer == Lawyer.None ? ".\n" : $", ${Content.Lawyers[W.Player.Lawyer].Weekly} a week.\n");
                case "judge": return PayJudge(int.Parse(p[1]));
                case "jury": return Queue(new JuryOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "rats": return Reports.Rats(W, Me);
                case "hunt": return Queue(new HuntOrder(Me, int.Parse(p[1])));
                case "seeto": return Queue(new SeenToOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "plant": return Queue(new PlantOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
                case "care": return Care(int.Parse(p[1]));
                case "recall": return Recall(int.Parse(p[1]));
                case "arm": return Buy(int.Parse(p[1]), gun: Enum.Parse<Weapon>(p[2], true));
                case "drive": return Buy(int.Parse(p[1]), car: Enum.Parse<Car>(p[2], true));
                case "hand": return Hand(int.Parse(p[1]), int.Parse(p[2]), p[3].ToLowerInvariant() == "car");
                case "heir": return NameHeir(int.Parse(p[1]));
                case "family": return Queue(new RecruitOrder(Me, Family: true));
                case "crew": return CrewCommand(p);
                case "send": return CrewOrder(int.Parse(p[1]), int.Parse(p[2]), guard: false);
                case "post": return CrewOrder(int.Parse(p[1]), int.Parse(p[2]), guard: true);
                case "auto":
                    var answers = AiPlanner.TalkForPlayer(W);
                    Pending = AiPlanner.Plan(W, W.Player);
                    return string.Concat(answers.Select(a => a + "\n")) + $"Planned {Pending.Count} orders. Type 'orders' to review or 'end' to run the week.\n";
                case "orders":
                    var sb = new StringBuilder();
                    foreach (var o in Pending) sb.AppendLine($"  {Describe(o)}");
                    return Pending.Count == 0 ? "No orders yet.\n" : sb.ToString();
                case "clear": Pending.Clear(); return "Orders cleared.\n";
                case "end": return EndWeek();
                case "quit": return null;
                default: return "Unknown command. Type 'help'.\n";
            }
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            return "Couldn't read that order. Type 'help'.\n";
        }
    }

    /// <summary>Buy a man a gun or a car on the spot. The money goes now.</summary>
    private string Buy(int hoodId, Weapon? gun = null, Car? car = null)
    {
        if (CheckHood(hoodId, allowJailed: true) is string bad) return bad + "\n";
        var hood = W.HoodById(hoodId);
        if (gun is { } g)
        {
            if (W.CannotArm(W.Player, hood, g) is string no) return no + "\n";
            int cost = W.Arm(W.Player, hood, g);
            return $"{hood.Name} now carries a {Content.Guns[g].Label}. That cost ${cost}.\n";
        }
        var c = car!.Value;
        if (W.CannotDrive(W.Player, hood, c) is string no2) return no2 + "\n";
        int paid = W.Drive(W.Player, hood, c);
        return $"{hood.Name} now drives a {Content.Cars[c].Label}. That cost ${paid}.\n";
    }

    /// <summary>Deal with another gang, picked by its id.</summary>
    private string Talk(string id, Func<Gang, string> deal)
    {
        var g = W.Gangs.FirstOrDefault(x => x.Id == int.Parse(id) && x.Alive && !x.IsPlayer);
        return g == null ? "No such gang. Type 'relations'.\n" : deal(g) + "\n";
    }

    private string Care(int hoodId)
    {
        var man = W.Hoods.FirstOrDefault(h => h.Id == hoodId);
        if (man == null) return "No such man. Type 'rats'.\n";
        if (W.CannotLookAfter(W.Player, man) is string no) return no + "\n";
        int price = W.LookAfter(W.Player, man);
        return $"${price:N0} went to {man.Name}'s family. His loyalty is {man.Loyalty} now.\n";
    }

    private string Recall(int hoodId)
    {
        var man = W.Hoods.FirstOrDefault(h => h.Id == hoodId && h.GangId == Me && h.State == HoodState.Inside);
        if (man == null) return "He isn't inside anyone. Type 'rats'.\n";
        W.Recall(W.Player, man);
        return $"{man.Name} is home.\n";
    }

    private string PayJudge(int hoodId)
    {
        var man = W.Hoods.FirstOrDefault(h => h.Id == hoodId);
        if (man == null) return "No such man. Type 'cases'.\n";
        if (W.CannotPayJudge(W.Player, man) is string no) return no + "\n";
        int price = W.PayJudge(W.Player, man);
        return $"The judge in {man.Name}'s case took ${price:N0}. The odds of a conviction are now {Simulation.ConvictionChance(W, man) * 100:0}%.\n";
    }

    private string BuyFront(int bizId)
    {
        if (CheckBiz(bizId) is string bad) return bad + "\n";
        var b = W.BusinessById(bizId);
        if (W.CannotBuyFront(W.Player, b) is string no) return no + "\n";
        int price = W.BuyFront(W.Player, b);
        return $"{b.Name} is yours, for ${price:N0}. It can wash ${Content.LaunderCapacity(b)} a week.\n";
    }

    private string Hand(int fromId, int toId, bool car)
    {
        if (CheckHood(fromId, allowJailed: true) is string bad) return bad + "\n";
        if (CheckHood(toId, allowJailed: true) is string bad2) return bad2 + "\n";
        var from = W.HoodById(fromId);
        var to = W.HoodById(toId);
        return W.HandOver(from, to, car) ? $"{from.Name} handed his {(car ? "car" : "gun")} to {to.Name}.\n" : $"{from.Name} has no {(car ? "car" : "gun he can hand over")}.\n";
    }

    private static int[]? Rest(string[] p, int from) => p.Length > from ? p.Skip(from).Select(int.Parse).ToArray() : null;

    private string NameHeir(int hoodId)
    {
        if (CheckHood(hoodId, allowJailed: true) is string bad) return bad + "\n";
        var hood = W.HoodById(hoodId);
        if (hood.Id == W.Player.BossHoodId) return "He's already the boss.\n";
        W.NameHeir(W.Player, hood);
        return $"{hood.Name} will take over when {W.HoodById(W.Player.BossHoodId).Name} is gone.\n";
    }

    private string CrewCommand(string[] p)
    {
        switch (p[1].ToLowerInvariant())
        {
            case "new":
            {
                if (CheckHood(int.Parse(p[2]), allowJailed: true) is string bad) return bad + "\n";
                var crew = W.FormCrew(W.HoodById(int.Parse(p[2])));
                return $"{W.HoodById(crew.LieutenantHoodId).Name} now leads crew {crew.Id}.\n";
            }
            case "add":
            {
                var crew = W.CrewById(int.Parse(p[2]));
                if (crew == null || crew.GangId != Me) return "No such crew. Type 'crews'.\n";
                if (CheckHood(int.Parse(p[3]), allowJailed: true) is string bad) return bad + "\n";
                var hood = W.HoodById(int.Parse(p[3]));
                return W.JoinCrew(crew, hood) ? $"{hood.Name} joins {W.HoodById(crew.LieutenantHoodId).Name}'s crew.\n" : $"That crew is full ({Crew.MaxMembers} men plus the lieutenant).\n";
            }
            case "drop":
                W.LeaveCrew(int.Parse(p[2]));
                return "Done.\n";
            default:
                return "crew new <lieutenant> | crew add <crew> <hood> | crew drop <hood>\n";
        }
    }

    /// <summary>Send a whole crew: the lieutenant leads and the free men go with him as backup.</summary>
    public Order? CrewOrderFor(int crewId, int businessId, bool guard, ISet<int>? busy = null)
    {
        var crew = W.CrewById(crewId);
        if (crew == null || crew.GangId != Me) return null;
        // The best man for the job goes in front: the hardest for a fight, the most menacing for a shakedown.
        var biz = W.BusinessById(businessId);
        bool fight = guard || biz.IsProtected;
        var free = crew.Everyone.Where(id => W.HoodById(id).IsAvailable && busy?.Contains(id) != true)
            .OrderByDescending(id => fight ? W.HoodById(id).Strength : W.HoodById(id).Intimidation).ToList();
        if (free.Count == 0) return null;
        int lead = free[0];
        int[]? backup = free.Count > 1 ? free.Skip(1).ToArray() : null;
        return guard ? new GuardOrder(Me, lead, businessId, backup) : new ExtortOrder(Me, lead, businessId, backup);
    }

    private string CrewOrder(int crewId, int businessId, bool guard)
    {
        if (CheckBiz(businessId) is string bad) return bad + "\n";
        var busy = Pending.SelectMany(Simulation.TeamOf).ToHashSet();
        var order = CrewOrderFor(crewId, businessId, guard, busy);
        if (order == null) return "That crew has nobody free. Type 'crews'.\n";
        return Queue(order);
    }

    /// <summary>Start the week with the queued orders, to be run hour by hour.</summary>
    public void StartWeek()
    {
        Sim.BeginWeek(new Dictionary<int, List<Order>> { [Me] = Pending });
        Pending = new List<Order>();
    }

    public string EndWeek()
    {
        int week = W.Week;
        Sim.AdvanceWeek(new Dictionary<int, List<Order>> { [Me] = Pending });
        Pending = new List<Order>();
        return Reports.WeekSummary(W, week);
    }

    private string Queue(Order order)
    {
        string? problem = order switch
        {
            ExtortOrder e => e.Team.Select(id => CheckHood(id)).FirstOrDefault(x => x != null) ?? CheckBiz(e.BusinessId),
            RacketOrder r => CheckHood(r.HoodId) ?? CheckBiz(r.BusinessId)
                ?? (W.BusinessById(r.BusinessId).ProtectorGangId != Me ? "You can only open a racket on your own turf." : null)
                ?? (!Content.RacketsFor(W.BusinessById(r.BusinessId).Kind).Contains(r.Racket) ? "That business can't hide that racket." : null),
            SetRateOrder s => CheckBiz(s.BusinessId),
            RecruitOrder { Family: true } => Pending.Any(o => o is RecruitOrder { Family: true }) ? "Family is already coming this week."
                : !Simulation.CanBringInFamily(W, W.Player) ? $"You can bring family in once a year, for ${Content.FamilyCost}." : null,
            GuardOrder g => g.Team.Select(id => CheckHood(id)).FirstOrDefault(x => x != null) ?? CheckBiz(g.BusinessId)
                ?? (W.BusinessById(g.BusinessId).ProtectorGangId != Me ? "You can only guard your own turf." : null),
            PayoffOrder po => CheckPayoff(po),
            CampaignOrder co => CheckCampaign(co),
            StandOrder so => CheckHood(so.HoodId) ?? (so.WardId < 0 || so.WardId >= W.Wards.Count ? "There's no such ward. Type 'wards'."
                : Pending.Any(q => q is StandOrder x && (x.HoodId == so.HoodId || x.WardId == so.WardId)) ? "You're already putting a man up there this week."
                : Politics.CannotStand(W, W.Player, W.HoodById(so.HoodId), W.Wards[so.WardId])),
            PollsOrder po => CheckHood(po.HoodId) ?? (po.WardId < 0 || po.WardId >= W.Wards.Count ? "There's no such ward. Type 'wards'."
                : Politics.PollsOpen(W) == null ? $"The polls only need working in the last {Content.PollsWeeks} weeks of a campaign." : null),
            ArmOrder a => CheckHood(a.HoodId, allowJailed: true) ?? W.CannotArm(W.Player, W.HoodById(a.HoodId), a.Weapon),
            CarOrder c => CheckHood(c.HoodId, allowJailed: true) ?? W.CannotDrive(W.Player, W.HoodById(c.HoodId), c.Car),
            FrontOrder f => CheckBiz(f.BusinessId) ?? W.CannotBuyFront(W.Player, W.BusinessById(f.BusinessId)),
            JuryOrder j => W.Hoods.FirstOrDefault(h => h.Id == j.DefendantId) is not { Case: not null } d || d.GangId != Me ? "He isn't one of yours waiting on a trial. Type 'cases'."
                : d.Case.JuryFixed || Pending.Any(o => o is JuryOrder q && q.DefendantId == j.DefendantId) ? "Someone is already seeing to that jury."
                : CheckHood(j.HoodId),
            _ => CheckStreet(order),
        };
        if (problem != null) return problem + "\n";
        Pending.Add(order);
        return $"Queued: {Describe(order)}\n" + (Breaks(order) is { } partner ? $"That breaks your {World.Name(W.PactBetween(Me, partner.Id)!.Kind)} with {partner.Name}.\n" : "");
    }

    /// <summary>The gang this order moves on, if the player has a pact with it.</summary>
    public Gang? Breaks(Order order)
    {
        int target = order switch
        {
            ExtortOrder e when e.BusinessId >= 0 && e.BusinessId < W.Businesses.Count => W.Businesses[e.BusinessId].ProtectorGangId,
            ArsonOrder a when a.BusinessId >= 0 && a.BusinessId < W.Businesses.Count => W.Businesses[a.BusinessId].ProtectorGangId,
            HijackOrder h when h.BusinessId >= 0 && h.BusinessId < W.Businesses.Count => W.Businesses[h.BusinessId].ProtectorGangId,
            HitOrder h when W.Hoods.FirstOrDefault(x => x.Id == h.TargetHoodId) is { } t => t.GangId,
            _ => -1,
        };
        return target >= 0 && W.AtPeace(Me, target) ? W.GangById(target) : null;
    }

    public string Describe(Order o) => o switch
    {
        ExtortOrder e => $"{HoodName(e.HoodId)}{With(e.Backup)} {(W.BusinessById(e.BusinessId).IsProtected ? "moves on" : "leans on")} {BizName(e.BusinessId)}",
        RacketOrder r => $"{HoodName(r.HoodId)} opens a {Content.Rackets[r.Racket].Label} behind {BizName(r.BusinessId)}",
        GuardOrder g => $"{HoodName(g.HoodId)}{With(g.Backup)} {(Sim.WeekRunning ? "guard" : "guards")} {BizName(g.BusinessId)} all week",
        RecruitOrder { Family: true } => $"bring a young relative into the business (${Content.FamilyCost})",
        RecruitOrder => $"recruit a new hood (${Content.RecruitCost})",
        BribeOrder b => $"pay the precinct ${b.Amount}",
        SetRateOrder s => $"set {BizName(s.BusinessId)} to {s.RatePercent}%",
        PayoffOrder po when po.WardId >= 0 && po.WardId < W.Wards.Count =>
            $"put Alderman {W.Wards[po.WardId].Alderman} of {W.Wards[po.WardId].Name} on the payroll (${Politics.PayoffCost(W, W.Wards[po.WardId], W.Player)})",
        CampaignOrder co => co.WardId < 0 ? $"put ${co.Amount} behind the machine's man for mayor"
            : $"put ${co.Amount} behind your man in {(co.WardId < W.Wards.Count ? W.Wards[co.WardId].Name : "ward " + co.WardId)}",
        StandOrder so when so.WardId >= 0 && so.WardId < W.Wards.Count => $"{HoodName(so.HoodId)} runs for alderman in {W.Wards[so.WardId].Name}",
        PollsOrder po when po.WardId >= 0 && po.WardId < W.Wards.Count => $"{HoodName(po.HoodId)} works the polls in {W.Wards[po.WardId].Name}",
        ArsonOrder a => $"{HoodName(a.HoodId)} sets fire to {BizName(a.BusinessId)}",
        HitOrder h => $"{HoodName(h.HoodId)}{With(h.Backup)} {(Sim.WeekRunning ? "go" : "goes")} after {HoodName(h.TargetHoodId)}",
        RobberyOrder r => $"{HoodName(r.HoodId)}{With(r.Backup)} {(Sim.WeekRunning ? "hold" : "holds")} up {W.BankById(r.BankId)?.Name ?? "a bank"}",
        HijackOrder h => $"{HoodName(h.HoodId)}{With(h.Backup)} {(Sim.WeekRunning ? "stop" : "stops")} the beer truck for {BizName(h.BusinessId)}",
        HireOrder h => $"sign on {PersonName(h.PersonId)} (${W.PersonById(h.PersonId)?.Fee ?? 0})",
        PayCopOrder c => $"pay {PersonName(c.PersonId)} ${Content.CopPrice} to look away",
        BuyStoryOrder b => $"buy {PersonName(b.PersonId)}'s story (${Content.StoryPrice(W.Player.Heat)})",
        SilenceOrder s => $"{HoodName(s.HoodId)} has a word with {PersonName(s.PersonId)}",
        ArmOrder a => $"buy {HoodName(a.HoodId)} a {Content.Guns[a.Weapon].Label}",
        CarOrder c => $"buy {HoodName(c.HoodId)} a {Content.Cars[c.Car].Label}",
        JuryOrder j => $"{HoodName(j.HoodId)} leans on the jury in {HoodName(j.DefendantId)}'s case",
        LawyerOrder l => $"keep {Content.Lawyers[l.Lawyer].Label}",
        JudgeOrder j => $"pay the judge in {HoodName(j.DefendantId)}'s case",
        FrontOrder f => $"buy {BizName(f.BusinessId)} outright (${Content.FrontPrice(W.BusinessById(f.BusinessId)):N0})",
        _ => o.ToString(),
    };

    private string PersonName(int id) => W.PersonById(id)?.Name ?? $"person {id}";

    /// <summary>Why the player can't give one of the street orders or the heavier jobs, or null.</summary>
    public string? CheckStreet(Order order)
    {
        StreetPerson? Person(int id, PersonKind kind) => W.PersonById(id) is { } p && p.Kind == kind ? p : null;
        bool Already(Func<Order, bool> same) => Pending.Any(same);
        switch (order)
        {
            case HireOrder h:
            {
                var p = Person(h.PersonId, PersonKind.Drifter);
                if (p == null) return "There's nobody like that looking for work. Type 'street'.";
                if (Already(o => o is HireOrder q && q.PersonId == h.PersonId)) return $"You're already signing on {p.Name}.";
                return W.Player.Cash < p.Fee ? $"{p.Name} wants ${p.Fee}." : null;
            }
            case PayCopOrder c:
            {
                var p = Person(c.PersonId, PersonKind.BeatCop);
                if (p == null) return "That's not a beat cop. Type 'street'.";
                if (p.PaidByGangId >= 0 || Already(o => o is PayCopOrder q && q.PersonId == c.PersonId)) return $"{p.Name} has already been paid this week.";
                return W.Player.Cash < Content.CopPrice ? $"It takes ${Content.CopPrice}." : null;
            }
            case BuyStoryOrder b:
            {
                var p = Person(b.PersonId, PersonKind.Reporter);
                if (p == null) return "That's not a reporter. Type 'street'.";
                if (p.GangId != Me) return $"{p.Name} isn't writing about you.";
                if (Already(o => o is BuyStoryOrder q && q.PersonId == b.PersonId)) return "You're already buying that story.";
                return W.Player.Cash < Content.StoryPrice(W.Player.Heat) ? $"{p.Name} wants ${Content.StoryPrice(W.Player.Heat)}." : null;
            }
            case SilenceOrder s:
            {
                var p = Person(s.PersonId, PersonKind.Witness);
                if (p == null) return "That's not a witness. Type 'street'.";
                if (p.GangId != Me) return $"{p.Name} didn't see anything of yours.";
                return CheckHood(s.HoodId);
            }
            case ArsonOrder a:
            {
                var biz = W.Businesses.FirstOrDefault(b => b.Id == a.BusinessId);
                if (biz == null) return CheckBiz(a.BusinessId);
                if (!biz.IsProtected || biz.ProtectorGangId == Me) return "Only a business a rival protects is worth burning.";
                return biz.IsOpen ? CheckHood(a.HoodId) : $"{biz.Name} is already shut.";
            }
            case HitOrder h:
            {
                var target = W.Hoods.FirstOrDefault(x => x.Id == h.TargetHoodId);
                if (target == null || target.GangId == Me || !target.IsActive) return "Pick a man from another gang. Type 'gangs'.";
                if (!target.IsAvailable) return $"{target.Name} is in jail, out of reach.";
                return h.Team.Select(id => CheckHood(id)).FirstOrDefault(x => x != null);
            }
            case RobberyOrder r:
                if (W.BankById(r.BankId) == null) return "There's no such bank. Type 'banks'.";
                return r.Team.Select(id => CheckHood(id)).FirstOrDefault(x => x != null);
            case HijackOrder h:
            {
                var biz = W.Businesses.FirstOrDefault(b => b.Id == h.BusinessId);
                if (biz == null) return CheckBiz(h.BusinessId);
                if (!W.Prohibition) return "Since repeal, nobody's beer is worth stealing.";
                if (!Simulation.CanHijack(W, W.Player, biz)) return "Only a rival's speakeasy or still gets beer trucks worth stopping.";
                return h.Team.Select(id => CheckHood(id)).FirstOrDefault(x => x != null);
            }
            default:
                return null;
        }
    }

    private string With(int[]? backup) => backup is { Length: > 0 } ? $" and {backup.Length} {(backup.Length == 1 ? "man" : "men")}" : "";

    private string? CheckHood(int id, bool allowJailed = false)
    {
        var hood = W.Hoods.FirstOrDefault(h => h.Id == id);
        if (hood == null || hood.GangId != Me || !hood.IsActive) return $"Hood {id} isn't one of yours. Type 'hoods'.";
        if (!hood.IsAvailable && !allowJailed) return $"{hood.Name} is in jail.";
        return null;
    }

    /// <summary>Why the player can't buy this alderman now, or null.</summary>
    public string? CheckPayoff(PayoffOrder o)
    {
        if (o.WardId < 0 || o.WardId >= W.Wards.Count) return "There's no such ward. Type 'wards'.";
        var ward = W.Wards[o.WardId];
        if (ward.OwnerGangId == Me) return $"Alderman {ward.Alderman} is already on your payroll.";
        if (ward.Reformer) return $"Alderman {ward.Alderman} is a reformer. He won't take an envelope.";
        if (Politics.StaysBought(W, ward, W.Player)) return $"Alderman {ward.Alderman} has just taken {W.GangById(ward.OwnerGangId).Name}'s money and won't hear offers yet.";
        if (Pending.Any(p => p is PayoffOrder q && q.WardId == o.WardId)) return "You're already sending him an envelope this week.";
        long cost = Politics.PayoffCost(W, ward, W.Player);
        return W.Player.Cash < cost + Pending.OfType<PayoffOrder>().Sum(q => q.WardId < W.Wards.Count ? Politics.PayoffCost(W, W.Wards[q.WardId], W.Player) : 0)
            ? $"Alderman {ward.Alderman} costs ${cost} to start with." : null;
    }

    /// <summary>Why the player can't back a candidate in this race now, or null.</summary>
    public string? CheckCampaign(CampaignOrder o)
    {
        var due = Politics.Campaigning(W);
        if (due == null) return "There's no election coming up yet.";
        if (o.Amount <= 0) return "Put some money in.";
        if (o.WardId < 0 && due.Value.Kind != ElectionKind.Mayor) return "The mayor isn't up for election this time; back an alderman.";
        if (o.WardId >= 0 && due.Value.Kind != ElectionKind.Alderman) return "This is the mayor's race; back the machine's man.";
        if (o.WardId >= W.Wards.Count) return "There's no such ward. Type 'wards'.";
        return null;
    }

    private string? CheckBiz(int id) =>
        W.Businesses.Any(b => b.Id == id) ? null : $"There's no business {id}. Type 'targets'.";

    private string HoodName(int id) => W.Hoods.FirstOrDefault(h => h.Id == id)?.Name ?? $"hood {id}";
    private string BizName(int id) => W.Businesses.FirstOrDefault(b => b.Id == id)?.Name ?? $"business {id}";
}
