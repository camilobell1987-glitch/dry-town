using System.Linq;
using DryTown.Core;

/// <summary>Setting up a brewery for the screenshot.</summary>
public partial class Main
{
    /// <summary>Eight speakeasies near home, a truck line for three of them, and a brewery in the nearest warehouse for the rest. One that pours its beer is picked.</summary>
    private void SetUpBrewery()
    {
        var p = W.Player;
        p.Cash += 12_000;
        var men = W.AvailableHoodsOf(Me).Where(h => h.Id != p.BossHoodId).OrderBy(h => h.Id).ToList();
        int i = 0;
        foreach (var b in W.Businesses.Where(b => b.IsOpen && (b.ProtectorGangId < 0 || b.ProtectorGangId == Me) && Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy))
                     .OrderBy(b => W.BlocksFromHq(p, b)).Take(8))
        {
            b.ProtectorGangId = Me;
            b.HandlerHoodId = men[i++ % men.Count].Id;
            b.Racket = RacketKind.Speakeasy;
        }
        if (!W.LinesOf(Me).Any()) W.BuyLine(p, W.SupplyLines.Where(l => l.OwnerGangId < 0 && l.Kind == LineKind.Truck).OrderBy(l => l.Id).First());
        if (W.BreweryOf(Me) == null)
        {
            var house = W.Businesses.Where(b => b.IsOpen && b.Kind == BusinessKind.Warehouse && (b.ProtectorGangId < 0 || b.ProtectorGangId == Me))
                .OrderBy(b => W.BlocksFromHq(p, b)).First();
            house.ProtectorGangId = Me;
            house.HandlerHoodId = men[i++ % men.Count].Id;
            house.Racket = RacketKind.Brewery;
        }
        _map.SelectedBusiness = W.OnTap(Me).OrderByDescending(id => id).First();
        RefreshAll();
    }
}
