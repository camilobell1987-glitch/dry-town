using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The courts, on the City Hall tab: your lawyer, and every man of yours waiting on a trial.</summary>
public partial class Main
{
    private void CourtsSection()
    {
        var p = W.Player;
        if (!p.Alive) return;
        _hallPanel.AddChild(new HSeparator());
        AddLabel(_hallPanel, "The courts", Palette.Player, 17);
        AddLabel(_hallPanel, $"A charge worth more than {Content.PettySentence} weeks goes to a jury {Content.WeeksToTrial} weeks after the arrest. Until then the man sits in a cell, and you can still help him.", Palette.InkQuiet, wrap: true);

        // The lawyer on retainer.
        var row = new HBoxContainer();
        var pick = new OptionButton { ClipText = true, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var (kind, l) in Content.Lawyers)
            pick.AddItem(kind == Lawyer.None ? "No lawyer" : $"{Capital(l.Label)} · ${l.Weekly}/week, -{l.Cut * 100:0} points", (int)kind);
        pick.Select(pick.GetItemIndex((int)p.Lawyer));
        row.AddChild(pick);
        AddButton(row, "Keep him", () => { W.Retain(p, (Lawyer)pick.GetSelectedId()); RefreshAll(); RefreshCityHall(); });
        _hallPanel.AddChild(row);
        AddLabel(_hallPanel, "A lawyer already on retainer when a man is picked up has time to prepare. One hired after the arrest does half as much.", Palette.InkQuiet, 13, wrap: true);

        var cases = W.OnTrial(Me).ToList();
        if (cases.Count == 0) { AddLabel(_hallPanel, "None of your men is waiting on a trial.", Palette.InkQuiet); return; }
        foreach (var man in cases) CaseRow(man);
    }

    private void CaseRow(Hood man)
    {
        var c = man.Case!;
        _hallPanel.AddChild(new HSeparator());
        AddLabel(_hallPanel, man.Name, Palette.Ink);
        AddLabel(_hallPanel, $"Charged {c.Charge}{(c.Federal ? " in federal court" : "")}. Trial in {man.JailWeeks} week{(man.JailWeeks == 1 ? "" : "s")}; he faces " +
            (c.Life ? $"life: he has {man.Convictions} convictions behind him already." : $"{c.Sentence} weeks" + (man.Convictions > 0 ? $", longer for the {man.Convictions} conviction{(man.Convictions == 1 ? "" : "s")} on his record." : ".")),
            c.Life ? Palette.Bad : Palette.InkQuiet, wrap: true);
        if (man.Talking > 0) AddLabel(_hallPanel, "He's talking to the DA, and will sign before his trial. See the Men tab.", Palette.Bad, wrap: true);
        var odds = Simulation.ConvictionChance(W, man);
        string help = string.Join(", ", new[] { c.JudgePaid ? "the judge is paid" : null, c.JuryFixed ? "the jury has been leaned on" : null }.Where(x => x != null));
        AddLabel(_hallPanel, $"{odds * 100:0}% likely the jury convicts" + (help.Length > 0 ? $" ({help})." : "."), odds > 0.5 ? Palette.Bad : Palette.Good);

        var problem = W.CannotPayJudge(W.Player, man);
        if (!c.JudgePaid && !c.Federal)
        {
            var judge = AddButton(_hallPanel, problem ?? $"Pay off the judge, ${W.JudgePrice(man):N0} (-{Content.JudgeCut * 100:0} points)", () =>
            {
                if (W.PayJudge(W.Player, man) < 0) return;
                Audio.Instance?.Play("cash", 0.6f);
                RefreshAll();
                RefreshCityHall();
            });
            judge.Disabled = problem != null;
        }
        else if (c.Federal) AddLabel(_hallPanel, "A federal judge can't be bought.", Palette.InkQuiet, 13);

        if (c.JuryFixed || _shell.Pending.Any(o => o is JuryOrder j && j.DefendantId == man.Id)) return;
        var men = FreeHoods().Where(h => h.Id != W.Player.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3).ToList();
        if (men.Count == 0) return;
        var row = new HBoxContainer();
        var who = new OptionButton { ClipText = true, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in men) who.AddItem($"{h.Name} · {Simulation.JuryChance(h) * 100:0}% he isn't caught", h.Id);
        row.AddChild(who);
        AddButton(row, "Lean on the jury", () => { Give(new JuryOrder(Me, who.GetSelectedId(), man.Id)); RefreshCityHall(); });
        _hallPanel.AddChild(row);
        AddLabel(_hallPanel, $"Jurors who've had a visit are {Content.JuryCut * 100:0} points less sure. A man caught at it faces his own charge.", Palette.InkQuiet, 13, wrap: true);
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
