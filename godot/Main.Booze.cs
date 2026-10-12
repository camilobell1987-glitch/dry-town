using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>Rum-running in the side panel: where a speakeasy's liquor comes from, the supply lines, how it's poured, and a rival's shipments.</summary>
public partial class Main
{
    /// <summary>A line under a speakeasy or still's details: real liquor or bathtub gin, and the risk of a bad batch.</summary>
    private void BoozeLine(Business b)
    {
        if (!World.Liquor(b.Racket) || !b.IsProtected || !W.Prohibition || !b.IsOpen) return;
        var g = W.GangById(b.ProtectorGangId);
        bool real = b.Racket == RacketKind.Speakeasy && W.Supplied(g.Id, thisWeek: false).Contains(b.Id);
        var lines = W.LinesOf(g.Id).ToList();
        string from = lines.Count == 0 ? $"{g.Name}'s boats brought in"
            : (lines.Count == 1 ? $"off {lines[0].Name}" : $"off {g.Name}'s supply lines") + (g.Cellar > 0 ? " and boats" : "");
        string pour = b.Racket == RacketKind.Still ? "It makes its own rotgut" : real ? $"It pours real liquor {from}" : "It sells bathtub gin";
        if (g.Id == Me)
            AddLabel(_businessPanel, $"{pour}, {Content.Label(g.Cut)}. Chance of a bad batch this week: {W.BadBatchChance(b, g):P1}.", W.BadBatchChance(b, g) >= 0.01 ? Palette.Bad : Palette.InkQuiet, 13, wrap: true);
        else
            AddLabel(_businessPanel, $"{pour}.", Palette.InkQuiet, 13, wrap: true);
    }

    /// <summary>On one of your speakeasies or stills: the supply lines to buy, and how far to stretch the liquor.</summary>
    private void BoozeActions(Business b)
    {
        if (!World.Liquor(b.Racket) || !W.Prohibition) return;
        var p = W.Player;
        _businessPanel.AddChild(new HSeparator());
        AddLabel(_businessPanel, "Rum-running", Palette.Ink);
        int speakeasies = W.TurfOf(Me).Count(x => x.Racket == RacketKind.Speakeasy && x.IsOpen);
        int real = W.Supplied(Me, thisWeek: false).Count;
        AddLabel(_businessPanel, speakeasies == 0
                ? "A supply line keeps speakeasies in real liquor. You have none to pour it in yet."
                : $"{real} of your {speakeasies} speakeasies pour real liquor, which takes {Content.LineBonus:P0} more than bathtub gin and never poisons anyone served straight. Rivals can stop your shipments.",
            Palette.InkQuiet, 13, wrap: true);
        foreach (var line in W.SupplyLines)
        {
            string kind = line.Kind == LineKind.Boat ? "by boat" : "by truck";
            if (line.OwnerGangId == Me)
            {
                AddLabel(_businessPanel, $"You hold {line.Name}: {line.Cargo}, {kind}, enough for {line.Capacity} speakeasies." + (line.WaylaidWeek == W.Week ? " This week's shipment was stopped." : ""), Palette.Player, 13, wrap: true);
                continue;
            }
            if (line.OwnerGangId >= 0)
            {
                AddLabel(_businessPanel, $"{line.Name} ({kind}, {line.Capacity} speakeasies) is held by {W.GangById(line.OwnerGangId).Name}.", Palette.Gang(W, line.OwnerGangId), 13, wrap: true);
                continue;
            }
            var problem = W.CannotBuyLine(p, line);
            var buy = AddButton(_businessPanel, problem ?? $"Take over {line.Name}: {line.Capacity} speakeasies {kind}, ${line.Price:N0}", () =>
            {
                if (W.BuyLine(W.Player, line) < 0) return;
                Audio.Instance?.Play("cash", 0.6f);
                RefreshAll();
            });
            buy.TooltipText = line.Cargo;
            buy.Disabled = problem != null;
        }

        RumRowActions();

        AddLabel(_businessPanel, "How to pour it", Palette.Ink);
        var row = new HBoxContainer();
        foreach (var cut in Enum.GetValues<Cut>())
        {
            var button = AddButton(row, cut switch { Cut.Watered => "Watered", Cut.Hard => "Cut hard", _ => "Straight" }, () => { W.SetCut(W.Player, cut); RefreshAll(); });
            button.ToggleMode = true;
            button.ButtonPressed = p.Cut == cut;
            button.TooltipText = $"{Content.CutIncome(cut):+0%;-0%;+0%} on every speakeasy and still. A bad batch blinds customers: the police close the place for {Content.BadBatchShut} weeks and smash the back room, and it brings heat.";
        }
        _businessPanel.AddChild(row);
        AddLabel(_businessPanel, $"Your liquor is {Content.Label(p.Cut)}{(p.Cut == Cut.Straight ? "" : $", {Content.CutIncome(p.Cut):P0} more on every speakeasy and still")}.", p.Cut == Cut.Straight ? Palette.InkQuiet : Palette.Bad, 13, wrap: true);
    }

    /// <summary>The outfit's own boats: buy one, send the ones at the dock out, and what's in the warehouse.</summary>
    private void RumRowActions()
    {
        var p = W.Player;
        AddLabel(_businessPanel, "Your own boats", Palette.Ink);
        int dry = Math.Max(0, W.TurfOf(Me).Count(x => x.Racket == RacketKind.Speakeasy && x.IsOpen) - W.LineRoom(Me, thisWeek: false));
        AddLabel(_businessPanel, $"Your own boats bring liquor in where no supply line does. A speakeasy without a line pours a case a week from the warehouse, which holds {p.Cellar} of {Content.CellarCap} cases" +
            (dry == 0 ? "." : $"; {dry} of yours {(dry == 1 ? "draws" : "draw")} on it.") + $" The Coast Guard catches {W.CoastGuardChance(p):0%} of landings" + (W.DocksGangId == Me ? ", fewer at your own docks." : "."),
            Palette.InkQuiet, 13, wrap: true);
        foreach (var boat in W.BoatsOf(Me).ToList())
        {
            if (boat.AtSea)
            {
                var away = Content.Runs[boat.Run];
                int weeks = boat.BackWeek - W.Week + 1;
                AddLabel(_businessPanel, $"{World.Capital(boat.Name)} is away to {away.Place}, due back {(weeks <= 1 ? "this Sunday" : $"in {weeks} weeks")}" +
                    (boat.Cases == 0 ? ", her cargo dumped in a storm." : $" with {boat.Cases} cases of {away.Cargo}."), Palette.Player, 13, wrap: true);
                continue;
            }
            var row = new HBoxContainer();
            AddLabel(row, $"{World.Capital(boat.Name)}:", Palette.Player, 13);
            foreach (var info in Content.Runs.Values)
            {
                var problem = W.CannotSail(p, boat, info.Run);
                var sail = AddButton(row, $"{info.Place}, ${info.Cost}", () =>
                {
                    if (!W.Sail(W.Player, boat, info.Run)) return;
                    Audio.Instance?.Play("cash", 0.6f);
                    RefreshAll();
                });
                sail.Disabled = problem != null;
                sail.TooltipText = problem ?? $"{info.Weeks} weeks there and back for {info.Cases} cases of {info.Cargo}. Storm chance {W.StormChance(info.Run):0.0%} a week" +
                    (Content.InSeason(info.Run, W.WeekOfYear) ? $": it's the season for {info.Season}." : ".");
            }
            _businessPanel.AddChild(row);
        }
        var cannot = W.CannotBuyBoat(p);
        var buy = AddButton(_businessPanel, cannot ?? $"Buy a fast boat, ${Content.BoatPrice:N0}", () =>
        {
            if (W.BuyBoat(W.Player) == null) return;
            Audio.Instance?.Play("cash", 0.6f);
            RefreshAll();
        });
        buy.Disabled = cannot != null;
        buy.TooltipText = $"Up to {Content.MaxBoats} boats. St. Pierre is close but stormy in winter; Nassau brings back more but sails into the hurricanes from August to October.";
    }

    /// <summary>On a rival's speakeasy that pours real liquor: stop the shipment on its supply line.</summary>
    private void WaylayActions(Business b, Gang rival, System.Collections.Generic.List<Hood> hoods)
    {
        if (b.Racket != RacketKind.Speakeasy || !W.Supplied(rival.Id, thisWeek: false).Contains(b.Id)) return;
        foreach (var line in W.LinesOf(rival.Id).Where(l => Simulation.CanWaylay(W, W.Player, l)).ToList())
        {
            if (AlreadyGiven(o => o is WaylayOrder q && q.LineId == line.Id)) continue;
            var panel = _businessPanel;
            panel.AddChild(new HSeparator());
            string where = line.Kind == LineKind.Boat ? "the boats landing" : "the trucks";
            AddLabel(panel, $"Stop {where} on {line.Name}: about ${Simulation.Load(line)} for the load, and {rival.Name}'s speakeasies pour bathtub gin this week. Now and then the suppliers decide to deal with whoever stopped them instead.", Palette.Ink, wrap: true);
            if (hoods.Count > 0)
            {
                var who = HoodPicker(hoods, h => $"{Simulation.WaylayChance(W, h, Array.Empty<Hood>(), line):P0} chance");
                AddButton(panel, "Stop the shipment", () => Give(new WaylayOrder(Me, who.GetSelectedId(), line.Id)));
            }
            CrewPicker(panel, "Send the crew after it", team => $"{Simulation.WaylayChance(W, team[0], team.Skip(1), line):P0} chance",
                (lead, backup) => new WaylayOrder(Me, lead, line.Id, backup));
        }
    }
}
