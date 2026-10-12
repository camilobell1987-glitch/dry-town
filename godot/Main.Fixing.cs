using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>Fixing the week's fight or race, from one of your bookies.</summary>
public partial class Main
{
    /// <summary>On one of your bookies: what's on this week, and a man to send to the fighter or the jockey.</summary>
    private void FixActions(Business b)
    {
        if (b.Racket != RacketKind.Bookie) return;
        var p = W.Player;
        var bout = W.Card;
        _businessPanel.AddChild(new HSeparator());
        AddLabel(_businessPanel, "The card", Palette.Ink);
        if (bout == Bout.None)
        {
            AddLabel(_businessPanel, "There's nothing on this week. The fights are at the arena every fourth week, and the track runs from mid-April to October.", Palette.InkQuiet, 13, wrap: true);
            return;
        }
        string body = bout == Bout.Race ? "the stewards" : "the boxing commission";
        AddLabel(_businessPanel, $"This week: {W.CardLabel()}. Buying {(bout == Bout.Race ? "the jockey" : "him")} costs ${World.FixPrice(bout):N0} and would pay you ${W.FixPays(p):N0}, " +
            $"and every other outfit's bookies pay ${World.FixLoss(bout)} each. {W.FixFoundChance(p):0%} chance {body} call the bets off.", Palette.InkQuiet, 13, wrap: true);
        if (AlreadyGiven(o => o is FixOrder)) return;
        if (W.CannotFix(p) is { } no) { AddLabel(_businessPanel, no, Palette.InkQuiet, 13, wrap: true); return; }
        var men = FreeHoods().OrderByDescending(World.FixChance).ToList();
        if (men.Count == 0) return;
        var who = HoodPicker(men, h => $"{World.FixChance(h):0%} he gets to him");
        AddButton(_businessPanel, $"Fix it, ${World.FixPrice(bout):N0}", () => Give(new FixOrder(Me, who.GetSelectedId())));
    }
}
