using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The City Hall tab: the mayor, the wards and their aldermen, and elections.</summary>
public partial class Main
{
    /// <summary>The selected ward's name at the head of its section, to scroll to.</summary>
    private Label? _wardHeader;

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
        string da = hall.DAReform
            ? $"District Attorney {hall.DA} is a reformer. Every case in the city courts is pushed {Content.DaReformCase * 100:0} points harder."
            : hall.DAFriendGangId == Me ? $"District Attorney {hall.DA} owes you for his election. The city's cases against your men are {Content.DaFriendCase * 100:0} points softer."
            : hall.DAFriendGangId >= 0 ? $"District Attorney {hall.DA} owes {W.GangById(hall.DAFriendGangId).Name}, and lets their cases go soft."
            : $"District Attorney {hall.DA} is a machine man who owes nobody in particular.";
        AddLabel(_hallPanel, da, hall.DAReform ? Palette.Police : Palette.Ink, wrap: true);
        AddLabel(_hallPanel, $"Public outrage {hall.Outrage}/100. Killings in the papers push it up, and reformers do better at the polls when it's high.",
            hall.Outrage > 50 ? Palette.Bad : Palette.InkQuiet, wrap: true);

        var due = Politics.Campaigning(W);
        int nextAld = Politics.NextElection(W, ElectionKind.Alderman), nextMayor = Politics.NextElection(W, ElectionKind.Mayor);
        string When(int week) => $"week {week % Content.WeeksPerYear + 1} of {Content.StartYear + week / Content.WeeksPerYear}";
        if (due is { } d)
            AddLabel(_hallPanel, d.Kind == ElectionKind.Mayor
                ? $"The mayor's race is on, with the district attorney on the same ballot: the vote is in {d.Week - W.Week + 1} week{(d.Week - W.Week == 0 ? "" : "s")}. Money behind the machine's ticket buys its gratitude."
                : $"Aldermen elections in {d.Week - W.Week + 1} week{(d.Week - W.Week == 0 ? "" : "s")}. Money behind a candidate, and the shops you run in his ward, win votes. Better still, put up one of your own men.",
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
            AddLabel(_hallPanel, (mine > 0 ? $"You've put in ${mine:N0}. " : "") + "The biggest backer has the new mayor's ear and the DA's, if they win. Every $400 draws a little heat.", Palette.InkQuiet, wrap: true);
        }

        CourtsSection();

        // The selected ward in full.
        _selectedWard = Math.Clamp(_selectedWard, 0, W.Wards.Count - 1);
        var ward = W.Wards[_selectedWard];
        var biz = Politics.BusinessesIn(W, ward).ToList();
        _hallPanel.AddChild(new HSeparator());
        var colour = ward.OwnerGangId >= 0 ? Palette.Gang(W, ward.OwnerGangId) : ward.Reformer ? Palette.Police : Palette.Ink;
        _wardHeader = AddLabel(_hallPanel, ward.Name, colour, 17);
        string status = ward.Reformer ? $"Alderman {ward.Alderman} is a reformer. He won't take an envelope; beat him at the next election."
            : ward.OwnMan && ward.OwnerGangId == Me ? $"Alderman {ward.Alderman} is one of your own men. He takes no envelope, and nobody can buy him away from you."
            : ward.OwnMan ? $"Alderman {ward.Alderman} is one of {W.GangById(ward.OwnerGangId).Name}'s own men. Nobody can buy him; beat him at the next election."
            : ward.OwnerGangId == Me ? $"Alderman {ward.Alderman} is on your payroll."
            : ward.OwnerGangId >= 0 ? $"Alderman {ward.Alderman} takes {W.GangById(ward.OwnerGangId).Name}'s money."
            : $"Alderman {ward.Alderman} is a party man. Nobody has bought him yet.";
        AddLabel(_hallPanel, status, colour, wrap: true);
        int yours = biz.Count(b => b.ProtectorGangId == Me);
        AddLabel(_hallPanel, $"{biz.Count} businesses, {yours} paying you." + (ward.OwnMan ? "" : $" His envelope is ${Politics.Retainer(W, ward)} a week."), Palette.InkQuiet, wrap: true);

        if (p.Alive && ward.OwnerGangId != Me && !ward.Reformer && !ward.OwnMan)
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
            Ballot(ward);
        }
        if (Politics.PollsOpen(W) is { } polls && p.Alive) PollsRow(ward, polls.Kind);
        LastVote(ward);
        AddLabel(_hallPanel, "An alderman on your payroll tips off your rackets in his ward before a raid, makes shopkeepers' complaints go away, and cools your heat. He stays yours while the envelope keeps coming, unless a rival outbids you or he loses his seat.",
            Palette.InkQuiet, wrap: true);

        // Every ward, to pick from.
        _hallPanel.AddChild(new HSeparator());
        AddLabel(_hallPanel, "The wards (click one here or on the map)", Palette.Ink);
        foreach (var x in W.Wards)
        {
            string owner = x.Reformer ? "reformer" : x.OwnerGangId == Me ? x.OwnMan ? "your own man" : "yours" : x.OwnerGangId >= 0 ? W.GangById(x.OwnerGangId).Name : "for sale";
            int mineHere = Politics.BusinessesIn(W, x).Count(b => b.ProtectorGangId == Me);
            var b = AddButton(_hallPanel, $"{x.Name} · {x.Alderman} · {owner} · your shops {mineHere}", () => { _selectedWard = x.Id; RefreshCityHall(); });
            b.Alignment = HorizontalAlignment.Left;
            b.ClipText = true;
            b.AddThemeColorOverride("font_color", x.OwnerGangId >= 0 ? Palette.Gang(W, x.OwnerGangId) : x.Reformer ? Palette.Police : Palette.Ink);
            if (x.Id == _selectedWard) b.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = Palette.PanelRaised.Lightened(0.1f), ContentMarginLeft = 6, ContentMarginTop = 4, ContentMarginBottom = 4 });
        }
    }

    /// <summary>Who's running in this ward, and a way to put one of your own men up.</summary>
    private void Ballot(Ward ward)
    {
        var names = new List<string>();
        foreach (var (gangId, hoodId) in ward.Candidates.OrderBy(kv => kv.Key))
            names.Add($"{W.HoodById(hoodId).Name} ({(gangId == Me ? "yours" : W.GangById(gangId).Name)})");
        if (ward.OwnMan) names.Add($"Alderman {ward.Alderman} ({(ward.OwnerGangId == Me ? "yours" : W.GangById(ward.OwnerGangId).Name)}, sitting)");
        AddLabel(_hallPanel, "On the ballot: " + string.Join(", ", names.Append("a reformer").Append("a party man")) + ".", Palette.Ink, 13, wrap: true);
        if (ward.Candidates.ContainsKey(Me) || _shell.Pending.Any(o => o is StandOrder s && s.WardId == ward.Id)) return;
        var men = W.AvailableHoodsOf(Me).Where(h => Politics.CannotStand(W, W.Player, h, ward) == null && !_shell.Pending.Any(o => o is StandOrder s && s.HoodId == h.Id))
            .OrderByDescending(h => h.Brains).ThenBy(h => h.Id).ToList();
        if (men.Count == 0)
        {
            AddLabel(_hallPanel, $"To run one of your men here he needs brains of {Content.CandidateBrains} or more and a clean record, and he can't be the boss or your heir.", Palette.InkQuiet, 13, wrap: true);
            return;
        }
        var row = new HBoxContainer();
        var who = new OptionButton { ClipText = true, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in men) who.AddItem($"{h.Name} · brains {h.Brains}", h.Id);
        row.AddChild(who);
        AddButton(row, "Run him for alderman", () => { Give(new StandOrder(Me, who.GetSelectedId(), ward.Id)); RefreshCityHall(); });
        _hallPanel.AddChild(row);
        AddLabel(_hallPanel, "Your own man counts for more than money alone. If he wins he leaves the street for good, but the seat is yours: no envelope, and nobody can buy him. If he's jailed before the vote, he's off the ballot.",
            Palette.InkQuiet, 13, wrap: true);
    }

    /// <summary>Send a man to lean on voters in this ward, in the last weeks of the campaign.</summary>
    private void PollsRow(Ward ward, ElectionKind race)
    {
        var men = FreeHoods().Where(h => h.Id != W.Player.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3).OrderByDescending(h => h.Intimidation).ThenBy(h => h.Id).ToList();
        if (men.Count == 0) return;
        // The picker gets a line of its own: each man's votes and his risk of being picked up are worth reading in full.
        var who = new OptionButton { ClipText = true, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in men) who.AddItem($"{h.Name} · {Content.PollsVotes(h)} votes, {Politics.PollsRisk(W, ward, h) * 100:0}% risk", h.Id);
        _hallPanel.AddChild(who);
        AddButton(_hallPanel, $"Work the polls in {ward.Name}", () => { Give(new PollsOrder(Me, who.GetSelectedId(), ward.Id)); RefreshCityHall(); });
        string forWhom = race == ElectionKind.Mayor ? "the machine's ticket" : "your man here";
        AddLabel(_hallPanel, $"The polls are open. A man leaning on voters turns out votes for {forWhom}, as much as a few hundred dollars would. It draws heat and outrage, and a man who's caught is charged.",
            Palette.InkQuiet, 13, wrap: true);
    }

    /// <summary>How the last count went in this ward and for mayor and DA.</summary>
    private void LastVote(Ward ward)
    {
        var races = W.Hall.Results.Where(r => r.WardId == ward.Id || r.WardId < 0).OrderBy(r => r.WardId).ToList();
        if (races.Count == 0) return;
        _hallPanel.AddChild(new HSeparator());
        AddLabel(_hallPanel, "The last vote", Palette.Ink);
        foreach (var race in races)
        {
            AddLabel(_hallPanel, $"{race.Race}, {Reports.Date(W, race.Week)}", Palette.InkQuiet, 13);
            int top = race.Field.Max(t => t.Votes);
            foreach (var t in race.Field.OrderByDescending(t => t.Votes))
            {
                var colour = t.GangId >= 0 ? Palette.Gang(W, t.GangId) : t.Ticket == "reform" ? Palette.Police : Palette.Ink;
                AddLabel(_hallPanel, $"{(t.Won ? "✓ " : "")}{t.Name} · {t.Ticket}", colour, 13);
                var line = new HBoxContainer();
                var bar = new ColorRect { Color = colour with { A = t.Won ? 0.9f : 0.5f }, CustomMinimumSize = new Vector2(220f * t.Votes / Math.Max(1, top), 9), SizeFlagsVertical = SizeFlags.ShrinkCenter };
                var slot = new Control { CustomMinimumSize = new Vector2(220, 9), SizeFlagsVertical = SizeFlags.ShrinkCenter };
                slot.AddChild(bar);
                line.AddChild(slot);
                var count = AddLabel(line, $"{t.Votes:N0} votes, {race.Share(t)}%", colour, 13);
                count.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                _hallPanel.AddChild(line);
            }
        }
    }
}
