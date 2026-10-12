using System.Linq;
using DryTown.Core;

/// <summary>Setting up rum row for a screenshot.</summary>
public partial class Main
{
    /// <summary>Five speakeasies, a truck line for three of them, and two boats: one away to Nassau, one at the dock.</summary>
    private void SetUpRumRow()
    {
        var p = W.Player;
        p.Cash += 12_000;
        var men = W.AvailableHoodsOf(Me).Where(h => h.Id != p.BossHoodId).OrderBy(h => h.Id).ToList();
        int i = 0;
        foreach (var b in W.Businesses.Where(b => b.IsOpen && (b.ProtectorGangId < 0 || b.ProtectorGangId == Me) && Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy))
                     .OrderBy(b => W.BlocksFromHq(p, b)).Take(5))
        {
            b.ProtectorGangId = Me;
            b.HandlerHoodId = men[i++ % men.Count].Id;
            b.Racket = RacketKind.Speakeasy;
        }
        if (!W.LinesOf(Me).Any()) W.BuyLine(p, W.SupplyLines.Where(l => l.OwnerGangId < 0 && l.Kind == LineKind.Truck).OrderBy(l => l.Id).First());
        var away = W.BuyBoat(p)!;
        W.BuyBoat(p);
        W.Sail(p, away, Run.Islands);
        p.Cellar = 14;
        _map.SelectedBusiness = W.TurfOf(Me).Where(b => b.Racket == RacketKind.Speakeasy).OrderByDescending(b => b.Id).First().Id;
        RefreshAll();
    }
}
