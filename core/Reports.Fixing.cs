using System.Text;

namespace DryTown.Core;

public static partial class Reports
{
    /// <summary>This week's card, what fixing it costs and pays, and the odds.</summary>
    public static string Card(World w, int gangId)
    {
        var g = w.GangById(gangId);
        var sb = new StringBuilder();
        var bout = w.Card;
        if (bout == Bout.None)
            return "There's nothing on this week. The fights are at the arena every fourth week, and the track runs from mid-April to October.\n";
        sb.AppendLine($"This week: {w.CardLabel()}.");
        sb.AppendLine($"Fixing it costs ${World.FixPrice(bout):N0}. It would pay you ${w.FixPays(g):N0} on what you lay off around town and your {w.Bookies(gangId)} bookies" +
            (bout == Bout.Race && w.WireGangId == gangId ? ", with the wire telling them first" : "") + $", and every other outfit's bookies pay ${World.FixLoss(bout)} each.");
        sb.AppendLine($"Chance {(bout == Bout.Race ? "the stewards" : "the boxing commission")} smell it and call the bets off: {w.FixFoundChance(g):0%}.");
        if (w.CannotFix(g) is { } no) sb.AppendLine(no);
        else
            foreach (var h in w.AvailableHoodsOf(gangId).OrderByDescending(World.FixChance).ThenBy(h => h.Id).Take(5))
                sb.AppendLine($"  {h.Id,4} {h.Name}: {World.FixChance(h):0%} he gets to him");
        return sb.ToString();
    }
}
