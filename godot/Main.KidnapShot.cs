using System.Linq;
using DryTown.Core;

/// <summary>Setting up a kidnapping for the screenshot.</summary>
public partial class Main
{
    /// <summary>You're holding one of the biggest rival's men already, and its heir is picked, with the men to send after him.</summary>
    private void SetUpKidnapping()
    {
        W.Pacts.RemoveAll(p => p.Involves(Me));
        foreach (var h in W.HeldBy(Me).ToList()) W.LetGo(W.Player, h);
        var rival = W.LivingGangs.Where(g => !g.IsPlayer).OrderByDescending(W.Power).First();
        var men = W.AvailableHoodsOf(rival.Id).Where(h => h.Id != rival.BossHoodId).OrderBy(h => h.Id).ToList();
        var mark = men.FirstOrDefault(h => h.Id == rival.HeirHoodId) ?? men[0];
        var held = men.First(h => h.Id != mark.Id);
        W.Snatch(W.Player, held);
        _map.Select(hood: mark.Id);
        RefreshAll();
    }
}
