using DryTown.Core;
using Godot;

/// <summary>The boss in a cell: who minds the street, what the guards cost, and handing the outfit over.</summary>
public partial class Main
{
    /// <summary>While the boss runs the outfit from inside.</summary>
    private void InsideSection(Hood boss)
    {
        var p = W.Player;
        int weeks = W.Week - p.InsideSinceWeek;
        AddLabel(_crewsPanel, $"{boss.Name} runs the outfit from his cell, and has for {weeks} week{(weeks == 1 ? "" : "s")}. He has {boss.JailWeeks} to go.", Palette.Police, wrap: true);
        var street = W.StreetBoss(p);
        if (street != null)
            AddLabel(_crewsPanel, $"{street.Name} minds the street for him · ambition {street.Ambition}, loyalty {street.Loyalty}. An ambitious street boss may decide he doesn't need orders from a cell, more so the longer it lasts.", Palette.Ink, wrap: true);
        AddLabel(_crewsPanel, $"The guards get ${W.CellFee(p):N0} a week to carry his word out. If the money runs short, or the warden moves him somewhere no word gets out, the chair goes to {(street?.Name ?? "the heir")}.", Palette.InkQuiet, wrap: true);
        if (street == null) return;
        if (p.RunFromInside)
        {
            var give = AddButton(_crewsPanel, $"Hand the outfit to {street.Name} for good", () => { _shell.Execute("inside off"); RefreshAll(); });
            give.TooltipText = $"On Sunday. {boss.Name} won't hold it against him.";
        }
        else
        {
            AddLabel(_crewsPanel, $"{boss.Name} hands the outfit to {street.Name} on Sunday.", Palette.Player, wrap: true);
            AddButton(_crewsPanel, "Keep running it from inside", () => { _shell.Execute("inside on"); RefreshAll(); });
        }
    }

    /// <summary>What happens if the boss is sent away, while he's still on the street.</summary>
    private void InsidePlan(Hood boss)
    {
        var p = W.Player;
        var keep = new CheckBox { Text = $"If {boss.Name} is sent away for a long stretch, he runs the outfit from his cell", ButtonPressed = p.RunFromInside };
        keep.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        keep.Toggled += on => { _shell.Execute(on ? "inside on" : "inside off"); RefreshAll(); };
        keep.TooltipText = $"The heir minds the street, and the guards want ${W.CellFee(p):N0} a week. Otherwise the heir takes over and the old boss comes out a sore lieutenant. No word gets out of a federal pen.";
        _crewsPanel.AddChild(keep);
    }
}
