using System.Text;

namespace DryTown.Core;

public static partial class Reports
{
    /// <summary>Whether the boss runs the outfit from a cell, who minds the street, and what it costs.</summary>
    public static string Inside(World w, int gangId)
    {
        var sb = new StringBuilder();
        var g = w.GangById(gangId);
        var boss = w.HoodById(g.BossHoodId);
        if (w.StreetBoss(g) is { } street)
        {
            int weeks = w.Week - g.InsideSinceWeek;
            sb.AppendLine($"{boss.Name} has run {g.Name} from his cell for {weeks} week{(weeks == 1 ? "" : "s")}, with {boss.JailWeeks} to go. {street.Name} minds the street for him.");
            sb.AppendLine($"The guards get ${w.CellFee(g):N0} a week to carry his word out. Miss it and the chair goes to {street.Name}.");
            sb.AppendLine($"{street.Name}: ambition {street.Ambition}, loyalty {street.Loyalty}. The more ambitious he is, and the longer it goes on, the likelier he decides he doesn't need orders from a cell.");
            sb.AppendLine("A reform DA or mayor, or angry papers, make the warden likelier to move the boss somewhere no word gets out.");
            if (g.IsPlayer) sb.AppendLine($"'inside off' hands {g.Name} to {street.Name} for good at the end of the week, and {boss.Name} won't hold it against him.");
            return sb.ToString();
        }
        string plan = g.RunFromInside
            ? $"If {boss.Name} is sent away for more than {Content.LongStretch} weeks, he keeps the chair and runs {g.Name} from his cell, while the heir minds the street. 'inside off' to hand over to the heir instead."
            : $"If {boss.Name} is sent away for more than {Content.LongStretch} weeks, the heir takes over {g.Name}, and {boss.Name} comes out a sore lieutenant. 'inside on' to have him run it from his cell instead.";
        sb.AppendLine(plan);
        sb.AppendLine($"The guards would want ${w.CellFee(g):N0} a week. No word gets out of a federal pen.");
        return sb.ToString();
    }
}
