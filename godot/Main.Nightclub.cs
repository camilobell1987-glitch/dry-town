using System.Linq;
using DryTown.Core;

/// <summary>Nightclubs in the side panel, and set up for the screenshot.</summary>
public partial class Main
{
    /// <summary>A line under a shop's details when it's a nightclub.</summary>
    private void ClubLine(Business b)
    {
        if (b.Kind is BusinessKind.Hotel or BusinessKind.Diner && b.ProtectorGangId == Me && b.OwnerGangId != Me && W.ClubOf(Me) == null && b.IsOpen)
            AddLabel(_businessPanel, "Buy it outright and you could open a nightclub here, where the city's names and its politicians come to be seen.", Palette.InkQuiet, 13, wrap: true);
        if (b.Racket != RacketKind.Nightclub || !World.Working(b)) return;
        string text = b.OwnerGangId == Me
            ? $"Your club. The city's names come to be seen here, and the boss is seen with them: his respect grows every week. Aldermen drink on the house: they take {Content.ClubPayoff:0%} less to go on your payroll, and your money behind a candidate counts for {Content.ClubCampaign:0%} more."
            : $"{W.GangById(b.OwnerGangId).Name}'s club. Aldermen drink on the house here, and they come cheaper to {W.GangById(b.OwnerGangId).Name}. Take the place and the club shuts.";
        AddLabel(_businessPanel, text, b.OwnerGangId == Me ? Palette.Player : Palette.Gang(W, b.OwnerGangId), 13, wrap: true);
    }

    /// <summary>For the screenshot: a club in your best hotel or diner, and the ward's alderman on the payroll.</summary>
    private void SetUpNightclub()
    {
        var p = W.Player;
        p.Cash += 30_000;
        var place = W.Businesses.Where(b => b.Kind == BusinessKind.Hotel && !W.IsWireRoom(b) && b.Racket == RacketKind.None)
            .OrderBy(b => W.BlocksFromHq(p, b)).First();
        place.ProtectorGangId = Me;
        place.HandlerHoodId = W.AvailableHoodsOf(Me).Where(h => h.Id != p.BossHoodId).OrderBy(h => h.Id).First().Id;
        place.ShutWeeks = 0;
        if (place.OwnerGangId != Me) W.BuyFront(p, place);
        place.Racket = RacketKind.Nightclub;
        _map.SelectedBusiness = place.Id;
        RefreshAll();
    }
}
