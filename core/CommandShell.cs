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

    public const string Help =
        "gangs | hoods | turf | targets | log\n" +
        "extort <hood> <biz> | racket <hood> <biz> <speakeasy|still|numbers|loanshark>\n" +
        "guard <hood> <biz> | recruit | bribe <dollars> | rate <biz> <percent>\n" +
        "crews | crew new <lieutenant> | crew add <crew> <hood> | crew drop <hood>\n" +
        "send <crew> <biz> (the crew leans on it or takes it) | post <crew> <biz> (the crew guards it)\n" +
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
                case "crew": return CrewCommand(p);
                case "send": return CrewOrder(int.Parse(p[1]), int.Parse(p[2]), guard: false);
                case "post": return CrewOrder(int.Parse(p[1]), int.Parse(p[2]), guard: true);
                case "auto":
                    Pending = AiPlanner.Plan(W, W.Player);
                    return $"Planned {Pending.Count} orders. Type 'orders' to review or 'end' to run the week.\n";
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
            GuardOrder g => g.Team.Select(id => CheckHood(id)).FirstOrDefault(x => x != null) ?? CheckBiz(g.BusinessId)
                ?? (W.BusinessById(g.BusinessId).ProtectorGangId != Me ? "You can only guard your own turf." : null),
            _ => null,
        };
        if (problem != null) return problem + "\n";
        Pending.Add(order);
        return $"Queued: {Describe(order)}\n";
    }

    public string Describe(Order o) => o switch
    {
        ExtortOrder e => $"{HoodName(e.HoodId)}{With(e.Backup)} {(W.BusinessById(e.BusinessId).IsProtected ? "moves on" : "leans on")} {BizName(e.BusinessId)}",
        RacketOrder r => $"{HoodName(r.HoodId)} opens a {Content.Rackets[r.Racket].Label} behind {BizName(r.BusinessId)}",
        GuardOrder g => $"{HoodName(g.HoodId)}{With(g.Backup)} {(Sim.WeekRunning ? "guard" : "guards")} {BizName(g.BusinessId)} all week",
        RecruitOrder => $"recruit a new hood (${Content.RecruitCost})",
        BribeOrder b => $"pay the precinct ${b.Amount}",
        SetRateOrder s => $"set {BizName(s.BusinessId)} to {s.RatePercent}%",
        _ => o.ToString(),
    };

    private string With(int[]? backup) => backup is { Length: > 0 } ? $" and {backup.Length} {(backup.Length == 1 ? "man" : "men")}" : "";

    private string? CheckHood(int id, bool allowJailed = false)
    {
        var hood = W.Hoods.FirstOrDefault(h => h.Id == id);
        if (hood == null || hood.GangId != Me || !hood.IsActive) return $"Hood {id} isn't one of yours. Type 'hoods'.";
        if (!hood.IsAvailable && !allowJailed) return $"{hood.Name} is in jail.";
        return null;
    }

    private string? CheckBiz(int id) =>
        W.Businesses.Any(b => b.Id == id) ? null : $"There's no business {id}. Type 'targets'.";

    private string HoodName(int id) => W.Hoods.FirstOrDefault(h => h.Id == id)?.Name ?? $"hood {id}";
    private string BizName(int id) => W.Businesses.FirstOrDefault(b => b.Id == id)?.Name ?? $"business {id}";
}
