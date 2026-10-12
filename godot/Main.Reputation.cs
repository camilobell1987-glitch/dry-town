using DryTown.Core;
using Godot;

/// <summary>The boss's name, under The family on the Men tab.</summary>
public partial class Main
{
    private void NameSection(Hood boss)
    {
        var p = W.Player;
        var color = World.Tier(p) >= 1 ? Palette.Player : Palette.Ink;
        AddLabel(_crewsPanel, $"{boss.Name} is {World.NameOf(p)}. Fear {p.Fear:0}, respect {p.Respect:0}.", color, wrap: true);
        string draws = World.Fame(p) >= Content.NameInPapers ? " A name this big draws the Bureau and the Untouchables." : "";
        AddLabel(_crewsPanel, $"Shakedowns go {p.Fear * Content.FearShakedown:P0} better, juries convict your men {p.Fear * Content.FearJury + p.Respect * Content.RespectJury:P0} less often, and new men come in {(int)(p.Respect * Content.RespectRecruitLoyalty)} points more loyal.{draws}", Palette.InkQuiet, 13, wrap: true);
        var soup = new CheckBox { Text = $"Run a soup kitchen in {boss.Name}'s name (${Content.SoupKitchenWeekly} a week)", ButtonPressed = p.SoupKitchen };
        soup.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        soup.Disabled = W.CannotRunSoupKitchen(p) != null;
        soup.Toggled += on => { _shell.Execute(on ? "soup on" : "soup off"); RefreshAll(); };
        soup.TooltipText = (W.CannotRunSoupKitchen(p) is { } why ? why + " " : "") + $"Earns {W.SoupRespect:0.0} respect a week{(W.Depression ? ", twice as much in the Depression" : ", twice as much once the Depression comes")}. Killings, fires and shops taken by force make a boss feared; acquittals and looking after jailed men's families make him admired.";
        _crewsPanel.AddChild(soup);
    }
}
