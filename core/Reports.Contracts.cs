using System.Text;

namespace DryTown.Core;

public static partial class Reports
{
    /// <summary>The city's contracts: who holds each, what it pays, what it takes to win one, and when the next letting is.</summary>
    public static string Contracts(World w, int gangId)
    {
        if (w.Wards.Count == 0) return "This city has no City Hall.\n";
        var g = w.GangById(gangId);
        var sb = new StringBuilder();
        int next = Core.Contracts.NextLetting(w);
        sb.AppendLine($"Each contract pays ${Core.Contracts.Pay(w)} a week, clean, for a year. The aldermen let the free ones in week {next % Content.WeeksPerYear + 1}.");
        sb.AppendLine($"It takes {Core.Contracts.VotesNeeded(w)} aldermen in your pocket to win one (the mayor's friend counts {Content.MayorVotes} more); you have {Core.Contracts.Votes(w, g)}.");
        if (Core.Contracts.CannotWin(w, g) is { } why) sb.AppendLine(why);
        foreach (var c in w.Contracts)
        {
            string who = c.GangId == gangId ? $"yours, until week {c.UntilWeek % Content.WeeksPerYear + 1} of {Content.StartYear + c.UntilWeek / Content.WeeksPerYear}"
                : c.GangId >= 0 ? w.GangById(c.GangId).Name : "an honest firm";
            var front = Core.Contracts.FrontFor(w, g, c.Kind);
            string needs = front != null ? $"you'd do it through {front.Name}" : $"needs a {Core.Contracts.NeedsLabel(c.Kind)} you own";
            sb.AppendLine($"  {Core.Contracts.Label(c.Kind),-28} {Trim(who, 34),-34} {needs}");
        }
        return sb.ToString();
    }
}
