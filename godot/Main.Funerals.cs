using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>Funerals on the Gangs tab and on a rival man's card, and set up for the screenshot.</summary>
public partial class Main
{
    /// <summary>Every boss being buried this week, with a wreath to send.</summary>
    private void FuneralSection()
    {
        foreach (var f in W.Funerals.Where(f => W.Week < f.UntilWeek))
        {
            var g = W.GangById(f.GangId);
            if (f.GangId == Me)
            {
                AddLabel(_talkPanel, $"You're burying {f.Boss}. {(f.Flowers.Count == 0 ? "No wreaths have come yet." : $"Wreaths have come from {string.Join(", ", f.Flowers.Select(id => W.GangById(id).Name))}.")} Every one makes the new boss's name.", Palette.Player, wrap: true);
                _talkPanel.AddChild(new HSeparator());
                continue;
            }
            AddLabel(_talkPanel, $"{g.Name} are burying {f.Boss}. Until the funeral, nobody who wants to be thought well of lays a finger on them. Every one of their men will be at the church, so a hit is easier; breaking the peace costs you respect, every outfit holds it against you, and the Commission throws you out.",
                Palette.Gang(W, g.Id), 13, wrap: true);
            if (f.Flowers.Contains(Me)) AddLabel(_talkPanel, "Your wreath is there.", Palette.Good, 13);
            else if (W.Player.Alive)
            {
                var send = AddButton(_talkPanel, $"Send a wreath (${Content.FlowersCost})", () => Say(W.SendFlowers(W.Player, g)));
                send.Disabled = W.CannotSendFlowers(W.Player, g) != null;
                AddLabel(_talkPanel, "A wreath earns a little respect, and they remember who sent one and who didn't.", Palette.InkQuiet, 13, wrap: true);
            }
            _talkPanel.AddChild(new HSeparator());
        }
    }

    /// <summary>A line on a rival man's card while his outfit is burying its boss.</summary>
    private void FuneralWarning(VBoxContainer panel, Gang gang)
    {
        if (W.FuneralOf(gang.Id) is { } f)
            AddLabel(panel, $"{gang.Name} are burying {f.Boss}. Every man of theirs will be at the church, so he's easier to get to, but moving on them now breaks the peace of the funeral.", Palette.Bad, 13, wrap: true);
    }

    /// <summary>For the screenshot: the biggest rival's boss is dead, and it's burying him.</summary>
    private void SetUpFuneral()
    {
        var rival = W.LivingGangs.Where(g => !g.IsPlayer).OrderByDescending(W.Power).First();
        var boss = W.HoodById(rival.BossHoodId);
        boss.State = HoodState.Dead;
        _shell.Execute("end");
        W.SendFlowers(W.LivingGangs.First(g => !g.IsPlayer && g.Id != rival.Id), rival);
        _talkTo = -1;
        RefreshAll();
    }
}
