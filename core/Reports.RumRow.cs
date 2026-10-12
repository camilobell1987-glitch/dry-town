using System.Text;

namespace DryTown.Core;

public static partial class Reports
{
    /// <summary>The gang's boats, where they are, the warehouse, and what the runs risk this week.</summary>
    public static string Boats(World w, int gangId)
    {
        if (!w.Prohibition) return "Since repeal, liquor comes in by the front door like anything else.\n";
        var g = w.GangById(gangId);
        var sb = new StringBuilder();
        var boats = w.BoatsOf(gangId).ToList();
        if (boats.Count == 0) sb.AppendLine($"You have no boats. A fast boat and her crew cost ${Content.BoatPrice:N0}: type 'buyboat'.");
        foreach (var b in boats)
        {
            string where = !b.AtSea ? "at the dock"
                : $"away to {Content.Runs[b.Run].Place}, due back {(b.BackWeek == w.Week ? "this Sunday" : $"in {b.BackWeek - w.Week + 1} weeks")}" + (b.Cases == 0 ? ", her cargo dumped" : $" with {b.Cases} cases");
            sb.AppendLine($"{b.Id,3} {World.Capital(b.Name)}, {where}");
        }
        int speakeasies = w.TurfOf(gangId).Count(b => b.Racket == RacketKind.Speakeasy && b.IsOpen);
        int dry = Math.Max(0, speakeasies - w.LineRoom(gangId, thisWeek: false));
        sb.AppendLine($"In the warehouse: {g.Cellar} cases of {Content.CellarCap}. " +
            (speakeasies == 0 ? "You run no speakeasies." : dry == 0 ? "Your supply lines keep all your speakeasies in real liquor." : $"{dry} of your speakeasies have no supply line and pour a case a week from it."));
        foreach (var info in Content.Runs.Values)
            sb.AppendLine($"  {info.Run.ToString().ToLowerInvariant(),-8} {info.Place}: {info.Weeks} weeks, {info.Cases} cases of {info.Cargo} for ${info.Cost}, storm chance {w.StormChance(info.Run):0.0%} a week" +
                (Content.InSeason(info.Run, w.WeekOfYear) ? $" ({info.Season})" : ""));
        sb.AppendLine($"Chance the Coast Guard catches a landing: {w.CoastGuardChance(g):0%}" + (w.DocksGangId == gangId ? ", less at your own docks." : "."));
        return sb.ToString();
    }
}
