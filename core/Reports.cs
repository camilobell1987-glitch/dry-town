using System.Text;

namespace DryTown.Core;

/// <summary>Plain-text reports shared by the console runner and the Godot screen.</summary>
public static class Reports
{
    public static string Date(World w) => $"Week {w.WeekOfYear}, {w.Year}";

    public static string WeekSummary(World w, int week)
    {
        var sb = new StringBuilder();
        var player = w.Player;
        int year = Content.StartYear + week / Content.WeeksPerYear;
        sb.AppendLine($"=== Week {week % Content.WeeksPerYear + 1}, {year} ===");
        if (player.Alive && w.LastLedger.TryGetValue(player.Id, out var l))
        {
            sb.AppendLine($"Protection ${l.Protection}  Rackets ${l.Rackets}  Wages -${l.Wages}  Spending -${l.Spending}  Fines -${l.Fines}  Net ${l.Net}");
            sb.AppendLine($"Cash ${player.Cash}  Heat {player.Heat}  Turf {w.TurfOf(player.Id).Count()}/{w.Businesses.Count}  Men {w.HoodsOf(player.Id).Count()}");
        }
        else if (!player.Alive)
        {
            sb.AppendLine("Your outfit is finished. The city goes on without you.");
        }
        sb.AppendLine();
        sb.AppendLine("Headlines:");
        var news = w.Events.Where(e => e.Week == week && IsHeadline(e)).ToList();
        if (news.Count == 0) sb.AppendLine("  A quiet week in the district.");
        foreach (var e in news) sb.AppendLine($"  {(e.GangId == player.Id ? "*" : "-")} {e.Text}");
        return sb.ToString();
    }

    public static bool IsHeadline(GameEvent e) => e.Kind is not (EventKind.Recruited or EventKind.Bribe or EventKind.HoodReleased);

    public static string Gangs(World w)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Gang",-28} {"Boss",-28} {"Turf",4} {"Men",4} {"Cash",8} {"Heat",4}");
        foreach (var g in w.LivingGangs.OrderByDescending(g => w.TurfOf(g.Id).Count()))
        {
            var boss = w.HoodById(g.BossHoodId);
            string name = g.IsPlayer ? $"{g.Name} (you)" : g.Name;
            sb.AppendLine($"{Trim(name, 28),-28} {Trim(boss.Name, 28),-28} {w.TurfOf(g.Id).Count(),4} {w.HoodsOf(g.Id).Count(),4} {g.Cash,8} {g.Heat,4}");
        }
        return sb.ToString();
    }

    public static string Hoods(World w, int gangId)
    {
        var sb = new StringBuilder();
        var gang = w.GangById(gangId);
        sb.AppendLine($"{"Id",4} {"Name",-30} {"Int",3} {"Mus",3} {"Brn",3} {"Stl",3} {"Loy",3} {"Wage",4}  State");
        foreach (var h in w.HoodsOf(gangId).OrderBy(h => h.Id))
        {
            string state = h.State == HoodState.Jailed ? $"jailed {h.JailWeeks}w" : h.Id == gang.BossHoodId ? "boss" : "free";
            sb.AppendLine($"{h.Id,4} {Trim(h.Name, 30),-30} {h.Intimidation,3} {h.Muscle,3} {h.Brains,3} {h.Stealth,3} {h.Loyalty,3} {h.Wage,4}  {state}");
        }
        return sb.ToString();
    }

    public static string Businesses(World w, Func<Business, bool>? filter = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Id",3} {"Business",-36} {"Take",5} {"Tgh",3} {"Rate",4} {"Res",3}  Protected by / racket");
        foreach (var b in w.Businesses.Where(filter ?? (_ => true)))
        {
            string owner = b.IsProtected ? w.GangById(b.ProtectorGangId).Name : "nobody";
            string racket = b.Racket != RacketKind.None ? $", {Content.Rackets[b.Racket].Label}" : "";
            string shut = b.IsOpen ? "" : $" (shut {b.ShutWeeks}w)";
            string rate = b.IsProtected ? $"{b.ProtectionRate}%" : "";
            sb.AppendLine($"{b.Id,3} {Trim(b.Name, 36),-36} {b.Takings,5} {b.Toughness,3} {rate,4} {b.Resentment,3}  {owner}{racket}{shut}");
        }
        return sb.ToString();
    }

    public static string YearLine(Simulation sim, int yearIndex)
    {
        var samples = sim.Metrics.Samples.Where(s => s.Week / Content.WeeksPerYear == yearIndex).ToList();
        if (samples.Count == 0) return "";
        var last = samples[^1];
        return $"{Content.StartYear + yearIndex}: gangs {samples.Min(s => s.LivingGangs)}-{samples.Max(s => s.LivingGangs)}, " +
               $"top share {samples.Max(s => s.TopShare):P0}, takeovers {samples.Sum(s => s.Takeovers)}, " +
               $"your cash ${last.PlayerCash}{(last.PlayerAlive ? "" : " (out)")}";
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + ".";
}
