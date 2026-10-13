using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The small gangs on the Gangs tab: hoodlums, street gangs and loan sharks, each on a corner of its
/// own. You can take their shops one at a time, hire their men away, buy the whole gang in, or wipe it out.
/// </summary>
public partial class Main
{
    /// <summary>The small gangs under the outfits in the list, nearest your headquarters first.</summary>
    private void SmallGangRows(TreeItem root)
    {
        var small = W.SmallGangs.OrderBy(g => W.Map.Distance(W.HqOf(W.Player), W.HqOf(g))).ThenBy(g => g.Id).ToList();
        if (small.Count == 0) return;
        var header = _gangsTree.CreateItem(root);
        header.SetText(0, $"Small gangs ({small.Count})");
        header.SetTooltipText(0, "Hoodlums, street gangs and loan sharks, nearest your headquarters first.");
        header.SetCustomColor(0, Palette.InkQuiet);
        header.SetSelectable(0, false);
        for (int i = 1; i < 5; i++) header.SetSelectable(i, false);
        foreach (var g in small)
        {
            var it = _gangsTree.CreateItem(header);
            string[] cols = { g.Name, $"{W.TurfOf(g.Id).Count()}", $"{W.HoodsOf(g.Id).Count()}", $"${g.Cash:N0}", Content.Label(g.Kind) };
            string tip = $"{g.Name}: {Content.Label(g.Kind)}, run by {W.HoodById(g.BossHoodId).Name}. They hold {W.BlocksOf(g).Count} of the {Content.MaxBlocks(g.Kind)} block{(Content.MaxBlocks(g.Kind) == 1 ? "" : "s")} they can.";
            for (int i = 0; i < cols.Length; i++) { it.SetText(i, cols[i]); it.SetTooltipText(i, tip); }
            it.SetMetadata(0, g.Id);
            it.SetCustomColor(0, Palette.Gang(W, g.Id));
            it.SetCustomColor(4, Palette.InkQuiet);
            if (g.Id == _talkTo) it.Select(0);
        }
    }

    /// <summary>A small gang's corner was clicked on the map: open it on the Gangs tab.</summary>
    private void ShowCorner(int gangId)
    {
        if (_map.Live && _playing) { _playing = false; _pause.Text = "Play"; }
        _talkTo = gangId;
        _answer = "";
        _tabs.CurrentTab = TabIndex("Gangs");
        RefreshGangs();
    }

    private void SmallGangSection(Gang them)
    {
        var me = W.Player;
        var panel = _talkPanel;
        int blocks = W.BlocksOf(them).Count, most = Content.MaxBlocks(them.Kind);
        AddLabel(panel, them.Name, Palette.Gang(W, them.Id), 17);
        AddLabel(panel, $"{Capital(Content.Label(them.Kind))}, run by {W.HoodById(them.BossHoodId).Name} from a corner of {W.Map.StreetOf(W.HqOf(them))} St. "
            + $"They hold {blocks} block{(blocks == 1 ? "" : "s")} and will never hold more than {most}. "
            + $"{W.TurfOf(them.Id).Count()} shops pay them, and they have ${them.Cash:N0} put by.", Palette.Ink, wrap: true);
        AddLabel(panel, $"Their muscle {W.Power(them):0} against your {W.Power(me):0}." + (W.GrudgeOf(them.Id, Me) > 0 ? $" They hold a grudge against you ({W.GrudgeOf(them.Id, Me)})." : ""), Palette.InkQuiet, 13, wrap: true);
        AddLabel(panel, "Expand into their blocks by shaking down their shops: click one on the map and send men to take it.", Palette.InkQuiet, 13, wrap: true);
        ShowQueued(panel, o => o is AbsorbOrder a && a.TargetGangId == them.Id || o is WipeOutOrder x && x.TargetGangId == them.Id
            || o is PoachOrder p && W.HoodById(p.TargetHoodId).GangId == them.Id);
        if (!CanGiveOrders(panel)) { if (_answer.Length > 0) AddLabel(panel, _answer, Palette.Player, wrap: true); return; }

        // Their men, and what it would take to hire each away.
        AddLabel(panel, "Recruit their men", Palette.Ink, 15);
        foreach (var man in W.HoodsOf(them.Id).OrderByDescending(h => h.Id == them.BossHoodId).ThenByDescending(h => h.Strength).ThenBy(h => h.Id))
        {
            string line = $"{man.Name}{(man.Id == them.BossHoodId ? ", the boss" : "")} · Int {man.Intimidation} Mus {man.Muscle} Brn {man.Brains} Stl {man.Stealth}";
            var no = W.CannotPoach(me, man) ?? (AlreadyGiven(o => o is PoachOrder p && p.TargetHoodId == man.Id) ? "You're already talking to him." : null);
            string why = man.Id == them.BossHoodId ? "" : $" · {no}";
            AddLabel(panel, no == null ? $"{line} · ${W.PoachFee(man)}, {W.PoachChance(me, man):P0} chance he takes it" : line + why, no == null ? Palette.Ink : Palette.InkQuiet, 13, wrap: true);
            if (no != null) continue;
            var b = AddButton(panel, "Hire him away", () => Give(new PoachOrder(Me, man.Id)));
            b.TooltipText = "If he takes the money he walks out on them and signs on with you. If not, it costs nothing, but they won't like it.";
        }

        var hoods = FreeHoods().Where(h => h.Id != me.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3).ToList();

        // The whole gang, men, shops and money, for a price.
        panel.AddChild(new HSeparator());
        AddLabel(panel, "Absorb them", Palette.Ink, 15);
        AddLabel(panel, $"Send a man to put it to them: ${W.AbsorbPrice(them):N0} and they come in with you, {W.HoodsOf(them.Id).Count()} men, {W.TurfOf(them.Id).Count()} shops and their ${them.Cash:N0}. They only listen to an outfit at least {Content.AbsorbPower:0.#} times their size, and a hard man makes the case better.",
            Palette.InkQuiet, 13, wrap: true);
        var cannot = W.CannotAbsorb(me, them) ?? (AlreadyGiven(o => o is AbsorbOrder a && a.TargetGangId == them.Id) ? $"Someone is already on his way to {them.Name}." : null);
        if (cannot != null) AddLabel(panel, cannot, Palette.Bad, 13, wrap: true);
        else if (hoods.Count == 0) AddLabel(panel, "All your men are busy.", Palette.InkQuiet, 13);
        else
        {
            var envoy = HoodPicker(hoods, h => $"{W.AbsorbChance(me, them, h):P0} they say yes", panel);
            AddButton(panel, "Send him to make the offer", () => Give(new AbsorbOrder(Me, envoy.GetSelectedId(), them.Id)));
        }

        // Or finish them.
        panel.AddChild(new HSeparator());
        AddLabel(panel, "Wipe them out", Palette.Ink, 15);
        AddLabel(panel, "Send men round to their corner. If they win, the men they find are shot or run and the gang is finished; their shops are left for anybody to take. It brings heat, and if it goes wrong your men may not come back.",
            Palette.InkQuiet, 13, wrap: true);
        var stop = W.CannotWipeOut(me, them) ?? (AlreadyGiven(o => o is WipeOutOrder x && x.TargetGangId == them.Id) ? $"Men are already on their way to {them.Name}." : null);
        if (stop != null) AddLabel(panel, stop, Palette.Bad, 13, wrap: true);
        else
        {
            if (hoods.Count > 0)
            {
                var who = HoodPicker(hoods, h => $"{Simulation.WipeOutChance(W, h, Array.Empty<Hood>(), them):P0} chance alone", panel);
                AddButton(panel, "Send him", () => Give(new WipeOutOrder(Me, who.GetSelectedId(), them.Id)));
            }
            CrewPicker(panel, "Send the crew", team => $"{Simulation.WipeOutChance(W, team[0], team.Skip(1), them):P0} chance",
                (lead, backup) => new WipeOutOrder(Me, lead, them.Id, backup));
            if (hoods.Count == 0 && !W.CrewsOf(Me).Any()) AddLabel(panel, "All your men are busy.", Palette.InkQuiet, 13);
        }
        if (_answer.Length > 0) AddLabel(panel, _answer, Palette.Player, wrap: true);
    }

    /// <summary>On a small gang man's card: what it would take to hire him away.</summary>
    private void PoachAction(VBoxContainer panel, Hood target)
    {
        if (!W.GangById(target.GangId).IsSmall || target.Id == W.GangById(target.GangId).BossHoodId) return;
        panel.AddChild(new HSeparator());
        if (AlreadyGiven(o => o is PoachOrder p && p.TargetHoodId == target.Id)) return;
        if (W.CannotPoach(W.Player, target) is { } no) { AddLabel(panel, no, Palette.InkQuiet, 13, wrap: true); return; }
        AddLabel(panel, $"Hire him away from {W.GangById(target.GangId).Name}: ${W.PoachFee(target)}, {W.PoachChance(W.Player, target):P0} chance he takes it.", Palette.Ink, wrap: true);
        AddButton(panel, "Make him the offer", () => Give(new PoachOrder(Me, target.Id)));
    }
}
