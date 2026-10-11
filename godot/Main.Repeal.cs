using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>After repeal in the side panel and the report: the wire room, casinos, and the Bureau's public enemy.</summary>
public partial class Main
{
    /// <summary>What the back room holds. A speakeasy is only a bar since repeal.</summary>
    private string RacketLabel(Business b) =>
        b.Racket == RacketKind.Speakeasy && !W.Prohibition ? "a bar, since repeal" : Content.Rackets[b.Racket].Label;

    /// <summary>A line under a shop's details when it's the wire room, or a casino.</summary>
    private void RepealLine(Business b)
    {
        if (W.IsWireRoom(b))
        {
            int wire = W.WireGangId;
            string text = wire == Me
                ? $"The racing wire comes in here. You run it: ${W.WireWorth(Me)} a week from the city's bookies."
                : wire >= 0
                    ? $"The racing wire comes in here. {W.GangById(wire).Name} runs it, and every other outfit's bookie pays them ${Content.WireFee} a week. Take this place and it's worth ${W.WireWorth(Me)} a week to you."
                    : $"The racing wire comes in here, and nobody runs it. Whoever protects this place takes a cut of every bookie in the city: ${W.WireWorth(Me)} a week to you.";
            AddLabel(_businessPanel, text, wire >= 0 ? Palette.Gang(W, wire) : Palette.Ink, 13, wrap: true);
        }
        if (b.Racket == RacketKind.Casino && b.IsProtected)
            AddLabel(_businessPanel, Politics.Covered(W, W.GangById(b.ProtectorGangId), b)
                ? "The ward's alderman looks the other way, so the casino runs quietly. Its money comes in clean."
                : $"The casino's money comes in clean, but without the ward's alderman it draws {Content.CasinoHeatUncovered} heat a week.", Palette.InkQuiet, 13, wrap: true);
    }

    /// <summary>For screenshots after repeal: a casino in your best hotel, slot machines, the wire room, and the Bureau on your tail.</summary>
    private void SetUpRepeal(string select)
    {
        var p = W.Player;
        p.Cash += 30_000;
        var hq = W.HqOf(p);
        Business Take(Business b)
        {
            b.ProtectorGangId = Me;
            b.HandlerHoodId = W.AvailableHoodsOf(Me).Where(h => h.Id != p.BossHoodId).OrderBy(h => h.Id).First().Id;
            b.ShutWeeks = 0;
            return b;
        }
        var hotel = Take(W.Businesses.Where(b => b.Kind == BusinessKind.Hotel && !W.IsWireRoom(b)).OrderBy(b => W.BlocksFromHq(p, b)).First());
        if (hotel.OwnerGangId != Me) W.BuyFront(p, hotel);
        hotel.Racket = RacketKind.Casino;
        Politics.WardOf(W, hotel).OwnerGangId = Me;
        foreach (var b in W.TurfOf(Me).Where(b => b.IsOpen && b.Racket == RacketKind.None && Content.RacketsFor(b.Kind).Contains(RacketKind.Slots)).Take(3)) b.Racket = RacketKind.Slots;
        var room = Take(W.BusinessById(W.WireBusinessId));
        room.Racket = RacketKind.Bookie;
        W.Repeal.PublicEnemyGangId = Me;
        _map.SelectedBusiness = select == "wire" ? room.Id : hotel.Id;
        _news.Text = WeekReport(W.Week - 1);
    }

    /// <summary>The Bureau's public enemy, at the top of the report when it's you. BBCode.</summary>
    private string PublicEnemySection()
    {
        if (W.PublicEnemy is not { } enemy) return "";
        string C(Color c) => "#" + Palette.Hex(c);
        if (enemy.Id != Me)
            return $"[color={C(Palette.InkQuiet)}]PUBLIC ENEMY NUMBER ONE[/color]  {Escape(W.HoodById(enemy.BossHoodId).Name)} of {Escape(enemy.Name)}\n\n";
        return $"[color={C(Palette.Bad)}]PUBLIC ENEMY NUMBER ONE[/color]\n[color={C(Palette.Bad)}]The Bureau has named you. Each week there's a {W.GmenChance(enemy):P0} chance the G-men pick up one of your men, or you. They can't be bought, but less heat means fewer raids.[/color]\n\n";
    }
}
