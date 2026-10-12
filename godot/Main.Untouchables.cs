using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The Untouchables in the report and on a shop's details.</summary>
public partial class Main
{
    /// <summary>The squad, at the top of the report while it's in town. BBCode.</summary>
    private string SquadSection()
    {
        if (W.SquadTarget is not { } target) return "";
        string C(Color c) => "#" + Palette.Hex(c);
        var s = W.Untouchables;
        if (target.Id != Me)
            return $"[color={C(Palette.InkQuiet)}]THE UNTOUCHABLES[/color]  after {Escape(W.HoodById(target.BossHoodId).Name)} of {Escape(target.Name)}, {s.Evidence} of {Content.SquadIndictment} raids toward a grand jury\n\n";
        string boss = Escape(W.HoodById(W.Player.BossHoodId).Name);
        return $"[color={C(Palette.Bad)}]THE UNTOUCHABLES[/color]\n[color={C(Palette.Bad)}]The squad is after you. Each week there's a {W.SquadChance(target):P0} chance they smash one of your stills or speakeasies, and they can't be bought. " +
               $"They have {s.Evidence} of the {Content.SquadIndictment} raids a grand jury needs to indict {boss}. A stealthy man handling a place may get word in time, and a boss out of town when the indictment comes is only wanted.[/color]\n\n";
    }

    /// <summary>A line under a still or speakeasy's details while the squad is after its outfit.</summary>
    private void SquadLine(Business b)
    {
        if (!Content.IsLiquor(b.Racket) || W.SquadTarget is not { } target || target.Id != b.ProtectorGangId) return;
        string text = b.ProtectorGangId == Me
            ? b.HandlerHoodId >= 0
                ? $"The Untouchables are after your liquor. {W.HoodById(b.HandlerHoodId).Name} has a {W.Warning(b):P0} chance of getting word before they raid this place. A stealthier man would do better."
                : "The Untouchables are after your liquor, and nobody handles this place to get word before they raid it."
            : $"The Untouchables are after {target.Name}'s liquor.";
        AddLabel(_businessPanel, text, Palette.Bad, 13, wrap: true);
    }

    /// <summary>For screenshots: the squad after you, with a raid or two behind it.</summary>
    private void SetUpUntouchables()
    {
        var p = W.Player;
        p.Cash += 10_000;
        var men = W.AvailableHoodsOf(Me).Where(h => h.Id != p.BossHoodId).OrderBy(h => h.Id).ToList();
        int i = 0;
        foreach (var b in W.Businesses.Where(b => b.IsOpen && b.ProtectorGangId < 0 && Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy))
                     .OrderBy(b => W.BlocksFromHq(p, b)).Take(6))
        {
            b.ProtectorGangId = Me;
            b.HandlerHoodId = men[i++ % men.Count].Id;
            b.Racket = i % 3 == 0 && Content.RacketsFor(b.Kind).Contains(RacketKind.Still) ? RacketKind.Still : RacketKind.Speakeasy;
        }
        for (int week = 0; week < 30 && (W.SquadTarget?.Id != Me || W.Untouchables.Evidence < 2); week++)
        {
            W.Untouchables.Formed = true;
            W.Untouchables.TargetGangId = Me;
            _replayWeek = W.Week;
            _shell.Execute("auto");
            _shell.EndWeek();
        }
        // Open the smashed places again, so there's something left to raid.
        foreach (var b in W.TurfOf(Me).Where(b => b.IsOpen && b.Racket == RacketKind.None && Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy)).Take(4))
            b.Racket = RacketKind.Speakeasy;
        _news.Text = WeekReport(W.Week - 1);
        _map.SelectedBusiness = W.LiquorOf(Me).OrderBy(b => b.Id).First().Id;
        RefreshAll();
    }
}
