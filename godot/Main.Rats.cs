using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>Rats and planted men, on the Men tab: who's talking, who's been found out, and your men inside other outfits.</summary>
public partial class Main
{
    private void RatsSection()
    {
        var p = W.Player;
        if (!p.Alive) return;
        _crewsPanel.AddChild(new HSeparator());
        AddLabel(_crewsPanel, "Rats", Palette.Player, 17);
        AddLabel(_crewsPanel, $"A man in a cell who isn't loyal may take the DA's deal and name his friends, and the longer the stretch he faces, the more loyalty it takes to keep him quiet. A man on the street with loyalty under {Content.InformerLoyalty} may start telling the police what you're up to, and you won't know until someone asks around.",
            Palette.InkQuiet, 13, wrap: true);

        foreach (var man in W.TalkingOf(Me).ToList())
        {
            AddLabel(_crewsPanel, $"{man.Name} is talking to the DA. He signs in {man.Talking} week{(man.Talking == 1 ? "" : "s")} unless his loyalty ({man.Loyalty}) reaches {Content.RatLine(man)}.", Palette.Bad, wrap: true);
            if (!man.LookedAfter)
            {
                var problem = W.CannotLookAfter(p, man);
                bool enough = man.Loyalty + Content.FamilyLoyalty >= Content.RatLine(man);
                var care = AddButton(_crewsPanel, problem ?? $"Look after his family, ${Content.FamilyCare(man):N0} (+{Content.FamilyLoyalty} loyalty{(enough ? "" : ", not enough")})", () =>
                {
                    if (W.LookAfter(W.Player, man) < 0) return;
                    Audio.Instance?.Play("cash", 0.6f);
                    RefreshAll();
                });
                care.Disabled = problem != null;
            }
            SeeToRow(man);
        }

        foreach (var rat in W.ExposedIn(Me).ToList())
        {
            int left = Math.Max(0, Content.RunWeeks - (W.Week - rat.ExposedWeek));
            string who = rat.State == HoodState.Inside ? $"{rat.Name} is one of {W.GangById(rat.GangId).Name}'s men, planted on you." : $"{rat.Name} has been talking to the police.";
            AddLabel(_crewsPanel, $"{who} He runs {(left <= 1 ? "this week" : $"in {left} weeks")} if he's left alone" + (rat.State == HoodState.Inside ? "." : ", straight to the DA."), Palette.Bad, wrap: true);
            SeeToRow(rat);
        }

        if (W.Week - p.TalkWeek <= 8 && !W.ExposedIn(Me).Any())
            AddLabel(_crewsPanel, "The police seem to know a lot about you lately. Somebody may be talking.", Palette.Bad, wrap: true);
        var askers = FreeHoods().Where(h => !h.Exposed).OrderByDescending(h => h.Brains).ThenBy(h => h.Id).ToList();
        if (askers.Count > 0 && !_shell.Pending.Any(o => o is HuntOrder))
        {
            var row = new HBoxContainer();
            var pick = Picker(row, askers.Select(h => ($"{h.Name} · {Simulation.HuntChance(h) * 100:0}% to find each rat", h.Id)));
            AddButton(row, "Ask around", () => { Give(new HuntOrder(Me, pick.GetSelectedId())); RefreshAll(); });
            _crewsPanel.AddChild(row);
        }

        // Men inside other outfits.
        _crewsPanel.AddChild(new HSeparator());
        AddLabel(_crewsPanel, "Men inside", Palette.Player, 17);
        AddLabel(_crewsPanel, $"A man planted in a rival outfit tells you every Monday what it means to do to you. Your jobs against it go better, and its jobs against you go worse. Each week there's a chance he gives himself away, and they'll kill him for it.",
            Palette.InkQuiet, 13, wrap: true);
        foreach (var man in W.PlantsOf(Me).ToList())
        {
            var host = W.GangById(man.InsideGangId);
            var head = new HBoxContainer();
            var line = AddLabel(head, $"{man.Name} inside {host.Name} · ${host.Cash:N0} in their safe, heat {host.Heat}, {W.HoodsOf(host.Id).Count()} men"
                + (man.Exposed ? ". They've found him out." : $" · {Simulation.CaughtInsideChance(man) * 100:0}% a week he's caught"),
                man.Exposed ? Palette.Bad : Palette.Ink, wrap: true);
            line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddButton(head, "Bring him home", () => { W.Recall(W.Player, man); RefreshAll(); });
            _crewsPanel.AddChild(head);
        }

        var spies = FreeHoods().Where(h => !h.Exposed && h.Id != p.BossHoodId && h.Id != p.HeirHoodId).OrderByDescending(Simulation.PlantChance).ThenBy(h => h.Id).ToList();
        var hosts = W.Outfits.Where(g => g.Id != Me && !W.HasManInside(Me, g.Id)).ToList();
        if (spies.Count == 0 || hosts.Count == 0) return;
        var plantRow = new HBoxContainer();
        var spy = Picker(plantRow, spies.Select(h => ($"{h.Name} · {Simulation.PlantChance(h) * 100:0}%", h.Id)));
        var into = Picker(plantRow, hosts.Select(g => (g.Name, g.Id)));
        AddButton(plantRow, "Plant him", () => { Give(new PlantOrder(Me, spy.GetSelectedId(), into.GetSelectedId())); RefreshAll(); });
        _crewsPanel.AddChild(plantRow);
        AddLabel(_crewsPanel, "A quiet man nobody has seen at work gets in more easily.", Palette.InkQuiet, 13, wrap: true);
    }

    /// <summary>A row to send a man after a rat, with his chances.</summary>
    private void SeeToRow(Hood rat)
    {
        if (_shell.Pending.Any(o => o is SeenToOrder s && s.TargetHoodId == rat.Id)) { AddLabel(_crewsPanel, "Someone is on his way.", Palette.InkQuiet, 13); return; }
        var men = FreeHoods().Where(h => h.Id != rat.Id && !h.Exposed && (h.Id != W.Player.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3))
            .OrderByDescending(h => Simulation.SeenToChance(h, rat)).ThenBy(h => h.Id).ToList();
        if (men.Count == 0) return;
        var row = new HBoxContainer();
        var pick = Picker(row, men.Select(h => ($"{h.Name} · {Simulation.SeenToChance(h, rat) * 100:0}%", h.Id)));
        AddButton(row, "See to him", () => { Give(new SeenToOrder(Me, pick.GetSelectedId(), rat.Id)); RefreshAll(); }).TooltipText =
            rat.State == HoodState.Jailed ? "If the guards catch your man, he faces his own charge and the rat signs at once" : "If he sees your man coming, he runs";
        _crewsPanel.AddChild(row);
    }

    private static OptionButton Picker(Container row, IEnumerable<(string Text, int Id)> items)
    {
        var pick = new OptionButton { ClipText = true, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 0) };
        foreach (var (text, id) in items) pick.AddItem(text, id);
        row.AddChild(pick);
        return pick;
    }
}
