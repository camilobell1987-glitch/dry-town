using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The Commission on the Gangs tab: who sits at the table, asking its say-so, and walking out.</summary>
public partial class Main
{
    private void CommissionSection()
    {
        var c = W.Commission;
        _talkPanel.AddChild(new HSeparator());
        AddLabel(_talkPanel, "The Commission", Palette.Ink, 15);
        if (!c.Sitting)
        {
            AddLabel(_talkPanel, $"In {Content.CommissionYear} the bosses sit down together. After that, no outfit at the table goes to war with another, or touches another's boss, without the table's say-so.", Palette.InkQuiet, 13, wrap: true);
            return;
        }
        var chair = W.Chair;
        AddLabel(_talkPanel, "At the table: " + string.Join(", ", W.Members.OrderByDescending(g => W.TurfOf(g.Id).Count()).Select(g => g.IsPlayer ? "you" : g.Name)) +
            (chair != null ? $". {(chair.IsPlayer ? "You chair it" : $"{chair.Name} chairs it")}, and breaks a tied vote." : "."), Palette.Ink, 13, wrap: true);
        AddLabel(_talkPanel, "No outfit at the table goes to war with another, or touches another's boss, without its say-so. Pick a gang above to put it to a vote. Break the rules and the table throws you out for a year, and every outfit at it holds it against you.", Palette.InkQuiet, 13, wrap: true);
        foreach (var s in c.Sanctions.Where(s => s.UntilWeek > W.Week && (s.By == Me || s.Against == Me)))
            AddLabel(_talkPanel, s.By == Me
                ? $"You may {(s.Kind == SanctionKind.War ? "go to war with" : "hit the boss of")} {W.GangById(s.Against).Name} for {s.UntilWeek - W.Week} more weeks."
                : $"{W.GangById(s.By).Name} may {(s.Kind == SanctionKind.War ? "go to war with you" : "go after you")} for {s.UntilWeek - W.Week} more weeks.", s.By == Me ? Palette.Player : Palette.Bad, 13, wrap: true);
        if (W.Seated(Me)) AddButton(_talkPanel, "Walk out of the Commission", () => Say(W.Walk(W.Player, leave: true)));
        else if (W.Shelved(Me)) AddLabel(_talkPanel, "You broke the rules and have no seat. Nobody needs the table's say-so to go after you.", Palette.Bad, 13, wrap: true);
        else AddButton(_talkPanel, "Ask for a seat", () => Say(W.Walk(W.Player, leave: false)));
    }

    /// <summary>Above the war button, when the table would have to say yes: put it to a vote, for a war or for their boss.</summary>
    private void AskTheTable(Gang them, string warning)
    {
        AddLabel(_talkPanel, warning, Palette.Bad, 13, wrap: true);
        var problem = W.CannotAsk(W.Player, them);
        if (problem != null) { AddLabel(_talkPanel, problem, Palette.InkQuiet, 13, wrap: true); return; }
        var votes = W.Votes(W.Player, them);
        AddLabel(_talkPanel, votes.Count == 0 ? "Nobody else at the table would vote." :
            $"{votes.Count} other{(votes.Count == 1 ? "" : "s")} at the table would vote. Members vote yes against an outfit they hold a grudge against or one that's grown too big, and no when they're sore at you or allied to {them.Name}.", Palette.InkQuiet, 13, wrap: true);
        var row = new HBoxContainer();
        AddButton(row, "Ask to go to war", () => Say(W.Ask(W.Player, them, SanctionKind.War)));
        AddButton(row, "Ask for their boss", () => Say(W.Ask(W.Player, them, SanctionKind.Hit)));
        _talkPanel.AddChild(row);
    }
}
