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

    public CommandShell(Simulation sim) => Sim = sim;

    private World W => Sim.World;
    private int Me => W.Player.Id;

    public string Prompt => $"[{Reports.Date(W)} | ${W.Player.Cash} | heat {W.Player.Heat} | {Pending.Count} orders]";

    public string Welcome => $"You run {W.Player.Name}. {Reports.Date(W)}. Type 'help' for commands.\n";

    public const string Help =
        "gangs | hoods | turf | targets | log\n" +
        "extort <hood> <biz> | racket <hood> <biz> <speakeasy|still|numbers|loanshark>\n" +
        "recruit | bribe <dollars> | rate <biz> <percent>\n" +
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
                case "recruit": return Queue(new RecruitOrder(Me));
                case "bribe": return Queue(new BribeOrder(Me, int.Parse(p[1])));
                case "rate": return Queue(new SetRateOrder(Me, int.Parse(p[1]), int.Parse(p[2])));
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
            ExtortOrder e => CheckHood(e.HoodId) ?? CheckBiz(e.BusinessId),
            RacketOrder r => CheckHood(r.HoodId) ?? CheckBiz(r.BusinessId)
                ?? (W.BusinessById(r.BusinessId).ProtectorGangId != Me ? "You can only open a racket on your own turf." : null)
                ?? (!Content.RacketsFor(W.BusinessById(r.BusinessId).Kind).Contains(r.Racket) ? "That business can't hide that racket." : null),
            SetRateOrder s => CheckBiz(s.BusinessId),
            _ => null,
        };
        if (problem != null) return problem + "\n";
        Pending.Add(order);
        return $"Queued: {Describe(order)}\n";
    }

    public string Describe(Order o) => o switch
    {
        ExtortOrder e => $"{HoodName(e.HoodId)} leans on {BizName(e.BusinessId)}",
        RacketOrder r => $"{HoodName(r.HoodId)} opens a {Content.Rackets[r.Racket].Label} behind {BizName(r.BusinessId)}",
        RecruitOrder => $"recruit a new hood (${Content.RecruitCost})",
        BribeOrder b => $"pay the precinct ${b.Amount}",
        SetRateOrder s => $"set {BizName(s.BusinessId)} to {s.RatePercent}%",
        _ => o.ToString(),
    };

    private string? CheckHood(int id)
    {
        var hood = W.Hoods.FirstOrDefault(h => h.Id == id);
        if (hood == null || hood.GangId != Me || !hood.IsActive) return $"Hood {id} isn't one of yours. Type 'hoods'.";
        if (!hood.IsAvailable) return $"{hood.Name} is in jail.";
        return null;
    }

    private string? CheckBiz(int id) =>
        W.Businesses.Any(b => b.Id == id) ? null : $"There's no business {id}. Type 'targets'.";

    private string HoodName(int id) => W.Hoods.FirstOrDefault(h => h.Id == id)?.Name ?? $"hood {id}";
    private string BizName(int id) => W.Businesses.FirstOrDefault(b => b.Id == id)?.Name ?? $"business {id}";
}
