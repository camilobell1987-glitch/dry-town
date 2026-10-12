using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The courts, on the City Hall tab: your lawyer, every man of yours waiting on a trial, and the men out of town.</summary>
public partial class Main
{
    private void CourtsSection()
    {
        var p = W.Player;
        if (!p.Alive) return;
        _hallPanel.AddChild(new HSeparator());
        AddLabel(_hallPanel, "The courts", Palette.Player, 17);
        AddLabel(_hallPanel, $"A charge worth more than {Content.PettySentence} weeks goes to a jury {Content.WeeksToTrial} weeks after the arrest. Until then the man sits in a cell, unless you put up his bail, and you can still help him.", Palette.InkQuiet, wrap: true);

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
        if (cases.Count == 0) AddLabel(_hallPanel, "None of your men is waiting on a trial.", Palette.InkQuiet);
        foreach (var man in cases) CaseRow(man);
        LamSection();
    }

    /// <summary>Bail for a man in a cell, or a run for one out on it.</summary>
    private void BailRow(Hood man)
    {
        if (man.Bail > 0)
        {
            AddLabel(_hallPanel, $"Out on ${man.Bail:N0} bail. You get it back when he shows up for his trial.", Palette.Good, 13, wrap: true);
            SkipTownButton(man, $"Skip town ahead of the trial (lose the bail, ${Content.HideoutFee(W.Player, man)}/week)");
            return;
        }
        var problem = W.CannotBail(W.Player, man);
        if (problem != null && !problem.StartsWith("The judge set")) { AddLabel(_hallPanel, problem, Palette.InkQuiet, 13, wrap: true); return; }
        var bail = AddButton(_hallPanel, problem ?? $"Put up bail, ${W.BailFor(man):N0}", () =>
        {
            if (W.PostBail(W.Player, man) < 0) return;
            Audio.Instance?.Play("cash", 0.6f);
            RefreshAll();
            RefreshCityHall();
        });
        bail.Disabled = problem != null;
    }

    private void SkipTownButton(Hood man, string label)
    {
        var problem = W.CannotSkipTown(W.Player, man);
        var go = AddButton(_hallPanel, problem ?? label, () =>
        {
            if (!W.SkipTown(W.Player, man)) return;
            Audio.Instance?.Play("horn", 0.4f);
            RefreshAll();
            RefreshCityHall();
        });
        go.Disabled = problem != null;
    }

    /// <summary>Men out of town and men home with a warrant out, and a way to send a man to lie low.</summary>
    private void LamSection()
    {
        _hallPanel.AddChild(new HSeparator());
        AddLabel(_hallPanel, "On the lam", Palette.Player, 17);
        AddLabel(_hallPanel, $"Out of town, nobody can arrest a man, hit him or pick him up in a raid, but he does no work. A hideout costs ${Content.HideoutWeekly} a week, ${Content.BossHideoutWeekly} for the boss, and while the boss is away his men's loyalty slips. A wanted man's case goes cold as he stays away, but the longer he's gone the likelier he's found, and a man who jumped bail faces a jury that knows it.", Palette.InkQuiet, 13, wrap: true);
        foreach (var man in W.OutOfTown(Me))
        {
            _hallPanel.AddChild(new HSeparator());
            int weeks = W.Week - man.AwaySinceWeek;
            AddLabel(_hallPanel, $"{man.Name}{(man.Id == W.Player.BossHoodId ? ", the boss" : "")}", Palette.Ink);
            AddLabel(_hallPanel, man.Case is { } c
                ? $"On the lam {Weeks(weeks)}, wanted, charged {c.Charge}. If he's caught, {Simulation.ConvictionChance(W, man) * 100:0}% likely the jury convicts" + (c.JumpedBail ? $", {Content.JumpedBailCase * 100:0} points more because he ran." : ".")
                : $"Lying low {Weeks(weeks)}. Nobody is looking for him.", man.Case != null ? Palette.Bad : Palette.InkQuiet, 13, wrap: true);
            AddButton(_hallPanel, man.Case != null ? $"Bring him home ({Content.WantedPickup(W.Player) * 100:0}% a week the police pick him up)" : "Bring him home", () =>
            {
                W.ComeHome(W.Player, man);
                RefreshAll();
                RefreshCityHall();
            });
        }
        foreach (var man in W.WantedAtHome(Me))
        {
            _hallPanel.AddChild(new HSeparator());
            AddLabel(_hallPanel, man.Name, Palette.Ink);
            AddLabel(_hallPanel, $"Home with a warrant out, charged {man.Case!.Charge}. {Content.WantedPickup(W.Player) * 100:0}% a week the police pick him up.", Palette.Bad, 13, wrap: true);
            SkipTownButton(man, $"Skip town (${Content.HideoutFee(W.Player, man)}/week)");
        }

        var free = W.AvailableHoodsOf(Me).Where(h => h.Bail == 0 && h.Case == null).ToList();
        if (free.Count == 0) return;
        var row = new HBoxContainer();
        var who = new OptionButton { ClipText = true, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in free) who.AddItem($"{h.Name}{(h.Id == W.Player.BossHoodId ? " (the boss)" : "")} · ${Content.HideoutFee(W.Player, h)}/week", h.Id);
        row.AddChild(who);
        AddButton(row, "Lie low out of town", () =>
        {
            var man = W.HoodById(who.GetSelectedId());
            if (!W.SkipTown(W.Player, man)) return;
            RefreshAll();
            RefreshCityHall();
        });
        _hallPanel.AddChild(row);
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
        BailRow(man);

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

    /// <summary>How long he's been gone, as "since this week" or "6 weeks".</summary>
    private static string Weeks(int n) => n <= 0 ? "since this week" : n == 1 ? "a week" : $"{n} weeks";

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
