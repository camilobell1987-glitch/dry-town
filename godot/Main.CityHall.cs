using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The City Hall tab: the mayor, the wards and their aldermen, and elections.</summary>
public partial class Main
{
    private void RefreshCityHall()
    {
        Clear(_hallPanel);
        if (W.Wards.Count == 0) return;
        var hall = W.Hall;
        var p = W.Player;

        AddLabel(_hallPanel, "City Hall", Palette.Player, 17);
        string mayor = hall.Reform
            ? $"Mayor {hall.Mayor} won on a clean-up ticket. His police raid sooner, and aldermen want twice as much to look the other way."
            : $"Mayor {hall.Mayor} is a machine man. The town is wide open.";
        if (hall.FriendGangId >= 0) mayor += hall.FriendGangId == Me ? " He owes you for his election: the police go easy on you." : $" He owes {W.GangById(hall.FriendGangId).Name}, and the police go easy on them.";
        AddLabel(_hallPanel, mayor, hall.Reform ? Palette.Police : Palette.Ink, wrap: true);
        AddLabel(_hallPanel, $"Public outrage {hall.Outrage}/100. Killings in the papers push it up, and reformers do better at the polls when it's high.",
            hall.Outrage > 50 ? Palette.Bad : Palette.InkQuiet, wrap: true);

        var due = Politics.Campaigning(W);
        int nextAld = Politics.NextElection(W, ElectionKind.Alderman), nextMayor = Politics.NextElection(W, ElectionKind.Mayor);
        string When(int week) => $"week {week % Content.WeeksPerYear + 1} of {Content.StartYear + week / Content.WeeksPerYear}";
        if (due is { } d)
            AddLabel(_hallPanel, d.Kind == ElectionKind.Mayor
                ? $"The mayor's race is on: the vote is in {d.Week - W.Week + 1} week{(d.Week - W.Week == 0 ? "" : "s")}. Money behind the machine's man buys his gratitude."
                : $"Aldermen elections in {d.Week - W.Week + 1} week{(d.Week - W.Week == 0 ? "" : "s")}. Money behind a candidate, and the shops you run in his ward, win votes.",
                Palette.Player, wrap: true);
        else
            AddLabel(_hallPanel, $"Next elections: aldermen in {When(nextAld)}, mayor in {When(nextMayor)}. Campaigns open {Content.CampaignWeeks} weeks before.", Palette.InkQuiet, wrap: true);

        if (due is { Kind: ElectionKind.Mayor } && p.Alive)
        {
            long mine = hall.Campaign.GetValueOrDefault(Me);
            var row = new HBoxContainer();
            var amount = new SpinBox { MinValue = 100, MaxValue = Math.Max(100, p.Cash), Step = 100, Value = Math.Min(1000, Math.Max(100, p.Cash / 10)), Prefix = "$" };
            row.AddChild(amount);
            AddButton(row, "Back the machine's man", () => Give(new CampaignOrder(Me, -1, (int)amount.Value)));
            _hallPanel.AddChild(row);
            AddLabel(_hallPanel, (mine > 0 ? $"You've put in ${mine:N0}. " : "") + "The biggest backer has the new mayor's ear, if he wins. Every $400 draws a little heat.", Palette.InkQuiet, wrap: true);
        }

        // The selected ward in full.
        _selectedWard = Math.Clamp(_selectedWard, 0, W.Wards.Count - 1);
        var ward = W.Wards[_selectedWard];
        var biz = Politics.BusinessesIn(W, ward).ToList();
        _hallPanel.AddChild(new HSeparator());
        var colour = ward.OwnerGangId >= 0 ? Palette.Gang(W, ward.OwnerGangId) : ward.Reformer ? Palette.Police : Palette.Ink;
        AddLabel(_hallPanel, ward.Name, colour, 17);
        string status = ward.Reformer ? $"Alderman {ward.Alderman} is a reformer. He won't take an envelope; beat him at the next election."
            : ward.OwnerGangId == Me ? $"Alderman {ward.Alderman} is on your payroll."
            : ward.OwnerGangId >= 0 ? $"Alderman {ward.Alderman} takes {W.GangById(ward.OwnerGangId).Name}'s money."
            : $"Alderman {ward.Alderman} is a party man. Nobody has bought him yet.";
        AddLabel(_hallPanel, status, colour, wrap: true);
        int yours = biz.Count(b => b.ProtectorGangId == Me);
        AddLabel(_hallPanel, $"{biz.Count} businesses, {yours} paying you. His envelope is ${Politics.Retainer(W, ward)} a week.", Palette.InkQuiet, wrap: true);

        if (p.Alive && ward.OwnerGangId != Me && !ward.Reformer)
        {
            long cost = Politics.PayoffCost(W, ward, p);
            bool queued = _shell.Pending.Any(o => o is PayoffOrder q && q.WardId == ward.Id);
            var buy = AddButton(_hallPanel, queued ? "Envelope goes out this week" : $"Put him on the payroll (${cost:N0}, then ${Politics.Retainer(W, ward)} a week)",
                () => Give(new PayoffOrder(Me, ward.Id)));
            bool loyal = Politics.StaysBought(W, ward, p);
            buy.Disabled = queued || p.Cash < cost || loyal;
            if (ward.OwnerGangId >= 0) buy.TooltipText = "He's another outfit's man, so it costs double to turn him.";
            if (loyal)
                AddLabel(_hallPanel, $"He took {W.GangById(ward.OwnerGangId).Name}'s money recently and won't hear offers for another {Content.AldermanLoyalWeeks - (W.Week - ward.BoughtWeek)} weeks.", Palette.InkQuiet, wrap: true);
        }
        if (due is { Kind: ElectionKind.Alderman } && p.Alive)
        {
            long mine = ward.Campaign.GetValueOrDefault(Me);
            var row = new HBoxContainer();
            var amount = new SpinBox { MinValue = 100, MaxValue = Math.Max(100, p.Cash), Step = 100, Value = Math.Min(500, Math.Max(100, p.Cash / 10)), Prefix = "$" };
            row.AddChild(amount);
            AddButton(row, "Back your man here", () => Give(new CampaignOrder(Me, ward.Id, (int)amount.Value)));
            _hallPanel.AddChild(row);
            if (mine > 0) AddLabel(_hallPanel, $"You've put ${mine:N0} behind your man here.", Palette.Player);
        }
        AddLabel(_hallPanel, "An alderman on your payroll tips off your rackets in his ward before a raid, makes shopkeepers' complaints go away, and cools your heat. He stays yours while the envelope keeps coming, unless a rival outbids you or he loses his seat.",
            Palette.InkQuiet, wrap: true);

        // Every ward, to pick from.
        _hallPanel.AddChild(new HSeparator());
        AddLabel(_hallPanel, "The wards (click one here or on the map)", Palette.Ink);
        foreach (var x in W.Wards)
        {
            string owner = x.Reformer ? "reformer" : x.OwnerGangId == Me ? "yours" : x.OwnerGangId >= 0 ? W.GangById(x.OwnerGangId).Name : "for sale";
            int mineHere = Politics.BusinessesIn(W, x).Count(b => b.ProtectorGangId == Me);
            var b = AddButton(_hallPanel, $"{x.Name} · {x.Alderman} · {owner} · your shops {mineHere}", () => { _selectedWard = x.Id; RefreshCityHall(); });
            b.Alignment = HorizontalAlignment.Left;
            b.ClipText = true;
            b.AddThemeColorOverride("font_color", x.OwnerGangId >= 0 ? Palette.Gang(W, x.OwnerGangId) : x.Reformer ? Palette.Police : Palette.Ink);
            if (x.Id == _selectedWard) b.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = Palette.PanelRaised.Lightened(0.1f), ContentMarginLeft = 6, ContentMarginTop = 4, ContentMarginBottom = 4 });
        }
    }
}
