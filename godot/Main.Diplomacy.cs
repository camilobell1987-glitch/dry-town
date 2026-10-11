using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The Gangs tab: every outfit in town on top, and underneath, where you stand with the one you pick.</summary>
public partial class Main
{
    private VBoxContainer _talkPanel = null!;
    private int _talkTo = -1;
    private string _answer = "";

    private void BuildGangsTab()
    {
        var tab = new VSplitContainer { Name = "Gangs" };
        _tabs.AddChild(tab);
        _gangsTree = TreeTab("Gangs", "Gang", "Turf", "Men", "Cash", "You");
        _gangsTree.GetParent().RemoveChild(_gangsTree);
        _gangsTree.Name = "List";
        _gangsTree.CustomMinimumSize = new Vector2(0, 150);
        _gangsTree.SizeFlagsVertical = SizeFlags.ExpandFill;
        _gangsTree.Visible = true;
        _gangsTree.ItemSelected += () =>
        {
            int id = _gangsTree.GetSelected()?.GetMetadata(0).AsInt32() ?? -1;
            if (id == _talkTo) return;
            _talkTo = id;
            _answer = "";
            RefreshDiplomacy();
        };
        tab.AddChild(_gangsTree);
        _talkPanel = ScrollTab("Talk");
        var scroll = (Control)_talkPanel.GetParent().GetParent();
        scroll.GetParent().RemoveChild(scroll);
        scroll.CustomMinimumSize = new Vector2(0, 200);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.SizeFlagsStretchRatio = 2.4f;
        scroll.Visible = true;
        tab.AddChild(scroll);
    }

    private void RefreshGangs()
    {
        _gangsTree.Clear();
        var root = _gangsTree.CreateItem();
        foreach (var g in W.LivingGangs.OrderByDescending(g => W.TurfOf(g.Id).Count()))
        {
            var it = _gangsTree.CreateItem(root);
            var boss = W.HoodById(g.BossHoodId);
            int wards = W.Wards.Count(x => x.OwnerGangId == g.Id);
            string[] cols = { g.IsPlayer ? $"{g.Name} (you)" : g.Name, $"{W.TurfOf(g.Id).Count()}", $"{W.HoodsOf(g.Id).Count()}", $"${g.Cash:N0}", g.IsPlayer ? "" : Short(g) };
            string tip = $"{g.Name}, run by {boss.Name}. Heat {g.Heat}." + (wards > 0 ? $"\nAldermen on the payroll: {string.Join(", ", W.Wards.Where(x => x.OwnerGangId == g.Id).Select(x => x.Name))}" : "")
                + (W.Hall.FriendGangId == g.Id ? $"\nMayor {W.Hall.Mayor} owes them" : "")
                + (g.IsPlayer ? "" : $"\n{Capital(Reports.Standing(W, W.Player, g))}");
            for (int i = 0; i < cols.Length; i++) { it.SetText(i, cols[i]); it.SetTooltipText(i, tip); }
            it.SetMetadata(0, g.Id);
            it.SetCustomColor(0, Palette.Gang(W, g.Id));
            if (!g.IsPlayer) it.SetCustomColor(4, W.AtPeace(Me, g.Id) ? Palette.Good : W.GrudgeOf(g.Id, Me) > Content.GrudgeTooDeep ? Palette.Bad : Palette.InkQuiet);
            if (g.Id == _talkTo) it.Select(0);
        }
        RefreshDiplomacy();
    }

    /// <summary>Where you stand with a gang, in a word for the list.</summary>
    private string Short(Gang g)
    {
        var p = W.PactBetween(Me, g.Id);
        if (p == null) return W.GrudgeOf(g.Id, Me) > Content.GrudgeTooDeep ? "war" : W.GrudgeOf(g.Id, Me) > 20 ? "sore" : "";
        return p.Kind == PactKind.Alliance ? "ally" : p.PayerId == Me ? "you pay" : p.PayerId >= 0 ? "pays you" : "truce";
    }

    private void RefreshDiplomacy()
    {
        foreach (var child in _talkPanel.GetChildren()) child.QueueFree();
        var me = W.Player;
        if (!me.Alive) return;

        foreach (var offer in W.Offers.ToList())
        {
            AddLabel(_talkPanel, W.Describe(offer), Palette.Player, wrap: true);
            int left = offer.ExpiresWeek - W.Week;
            AddLabel(_talkPanel, $"They want an answer within {left} week{(left == 1 ? "" : "s")}." + (offer.Kind == OfferKind.Demand ? " Turning them down makes an enemy." : ""), Palette.InkQuiet, 13);
            var row = new HBoxContainer();
            AddButton(row, "Accept", () => Say(W.Answer(offer, true)));
            AddButton(row, "Turn it down", () => Say(W.Answer(offer, false)));
            _talkPanel.AddChild(row);
            _talkPanel.AddChild(new HSeparator());
        }

        var them = W.Gangs.FirstOrDefault(g => g.Id == _talkTo && g.Alive && !g.IsPlayer);
        if (them == null)
        {
            if (_answer.Length > 0) AddLabel(_talkPanel, _answer, Palette.Player, wrap: true);
            AddLabel(_talkPanel, "Pick a gang above to talk terms with them.", Palette.InkQuiet, wrap: true);
            AddLabel(_talkPanel, $"A truce keeps both sides off each other's turf for {Content.TruceWeeks} weeks. An outfit much stronger than you wants paying for one, and one much weaker might pay you. Allies take up each other's quarrels. Break your word and nobody signs with you for a year.", Palette.InkQuiet, 13, wrap: true);
            if (W.Untrusted(me)) AddLabel(_talkPanel, "You broke your word this year. Nobody will sign with you until it's forgotten.", Palette.Bad, 13, wrap: true);
            return;
        }

        AddLabel(_talkPanel, them.Name, Palette.Gang(W, them.Id), 17);
        AddLabel(_talkPanel, $"Run by {W.HoodById(them.BossHoodId).Name}. Their muscle {W.Power(them):0} against your {W.Power(me):0}.", Palette.Ink, wrap: true);
        int hate = W.GrudgeOf(them.Id, Me), mine = W.GrudgeOf(Me, them.Id);
        AddLabel(_talkPanel, (hate > Content.GrudgeTooDeep ? $"They hate you ({hate}): it will cost you to make peace." : hate > 0 ? $"They hold a grudge against you ({hate})." : "They have nothing against you.")
            + (mine > 0 ? $" Your men have their own score to settle with them ({mine})." : ""), hate > Content.GrudgeTooDeep ? Palette.Bad : Palette.InkQuiet, 13, wrap: true);
        AddLabel(_talkPanel, Capital(Reports.Standing(W, me, them)) + ".", W.AtPeace(Me, them.Id) ? Palette.Good : Palette.InkQuiet);

        if (W.PactBetween(Me, them.Id) is { } pact)
        {
            AddButton(_talkPanel, $"Break the {World.Name(pact.Kind)}", () => Say(W.Break(me, them)));
            if (pact.Kind == PactKind.Truce && pact.PayerId < 0) AddButton(_talkPanel, "Propose an alliance", () => Say(W.Propose(me, them, PactKind.Alliance)));
        }
        else
        {
            AddButton(_talkPanel, "Propose a truce", () => Say(W.Propose(me, them, PactKind.Truce)));
            int ask = Content.TributeAsk(W.Income(me));
            if (W.WantsPaying(me, them))
                AddButton(_talkPanel, $"Offer ${ask} a week for a truce", () => Say(W.Propose(me, them, PactKind.Truce, ask)));
            AddButton(_talkPanel, "Propose an alliance", () => Say(W.Propose(me, them, PactKind.Alliance)));
            int demand = Content.TributeAsk(W.Income(them));
            AddButton(_talkPanel, $"Demand ${demand} a week to leave them alone", () => Say(W.Demand(me, them, demand)));
        }
        if (_answer.Length > 0) AddLabel(_talkPanel, _answer, Palette.Player, wrap: true);
    }

    private void Say(string answer)
    {
        _answer = answer;
        RefreshAll();
    }
}
