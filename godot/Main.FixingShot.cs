using System.Linq;
using DryTown.Core;

/// <summary>Setting up a fix for the screenshot.</summary>
public partial class Main
{
    /// <summary>Three bookies near home, the nearest one picked, with the week's card on its panel.</summary>
    private void SetUpFixing()
    {
        var p = W.Player;
        p.Cash += 5_000;
        var men = W.AvailableHoodsOf(Me).Where(h => h.Id != p.BossHoodId).OrderBy(h => h.Id).ToList();
        int i = 0;
        var bookies = W.Businesses.Where(b => b.IsOpen && (b.ProtectorGangId < 0 || b.ProtectorGangId == Me) && Content.RacketsFor(b.Kind).Contains(RacketKind.Bookie)
                && (b.Racket == RacketKind.None || b.Racket == RacketKind.Bookie))
            .OrderBy(b => W.BlocksFromHq(p, b)).Take(3).ToList();
        foreach (var b in bookies)
        {
            b.ProtectorGangId = Me;
            b.HandlerHoodId = men[i++ % men.Count].Id;
            b.Racket = RacketKind.Bookie;
        }
        _map.SelectedBusiness = bookies[0].Id;
        RefreshAll();
    }
}
