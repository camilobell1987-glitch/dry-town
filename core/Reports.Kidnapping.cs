using System.Text;

namespace DryTown.Core;

public static partial class Reports
{
    /// <summary>The men the gang holds, and its own men held by others.</summary>
    public static string Hostages(World w, int gangId)
    {
        var sb = new StringBuilder();
        var mine = w.HeldBy(gangId).ToList();
        var theirs = w.TakenFrom(gangId).ToList();
        if (mine.Count == 0 && theirs.Count == 0) return "Nobody is holding anyone of yours, and you aren't holding anyone.\n";
        foreach (var h in mine)
        {
            var man = w.HoodById(h.HoodId);
            sb.AppendLine($"  {man.Id,4} {man.Name} of {w.GangById(man.GangId).Name}: you want ${h.Ransom:N0} for him by week {h.DueWeek % Content.WeeksPerYear + 1}. If it doesn't come, he's killed. 'release {man.Id}' lets him go.");
        }
        foreach (var h in theirs)
        {
            var man = w.HoodById(h.HoodId);
            var offer = w.Offers.FirstOrDefault(o => o.Kind == OfferKind.Ransom && o.HoodId == man.Id);
            sb.AppendLine($"  {man.Id,4} {man.Name} is held by {w.GangById(h.HeldByGangId).Name}, who want ${h.Ransom:N0} by week {h.DueWeek % Content.WeeksPerYear + 1}." +
                (offer != null ? $" 'accept {offer.Id}' pays it." : ""));
        }
        return sb.ToString();
    }
}
