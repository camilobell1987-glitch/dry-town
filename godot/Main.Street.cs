using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The side panel for whoever or whatever else is clicked on the map: people on the street, rival men and banks.</summary>
public partial class Main
{
    /// <summary>Fill the Selected tab for a person, a hood or a bank. False when a business (or nothing) is selected.</summary>
    private bool RefreshOtherSelection()
    {
        if (_map.SelectedPerson >= 0) { PersonPanel(W.PersonById(_map.SelectedPerson)); return true; }
        if (_map.SelectedHood >= 0) { HoodPanel(W.HoodById(_map.SelectedHood)); return true; }
        if (_map.SelectedBank >= 0 && W.BankById(_map.SelectedBank) is { } bank) { BankPanel(bank); return true; }
        if (_map.SelectedShop >= 0) { ShopPanel(W.Map.LotAt(_map.SelectedShop)); return true; }
        return false;
    }

    private void OnMapSelection()
    {
        // Clicking something mid-week stops the clock so you can decide what to do.
        if (_map.Live && _playing) { _playing = false; _pause.Text = "Play"; }
        _tabs.CurrentTab = TabIndex("Selected");
        // One of yours clicked on the map: turn the rolodex to his card.
        if (_map.SelectedHood >= 0 && W.HoodById(_map.SelectedHood).GangId == Me) _rolodex.Show(_map.SelectedHood);
        RefreshBusiness();
    }

    /// <summary>Whether the side panel can still give orders, with a note when it can't. Adds the live-week note.</summary>
    private bool CanGiveOrders(VBoxContainer panel)
    {
        panel.AddChild(new HSeparator());
        if (!W.Player.Alive) return false;
        if (!_map.Live) return true;
        if (_map.Clock >= Simulation.LastOrderTick - 1) { AddLabel(panel, "It's too late in the week for that. Orders resume Monday.", Palette.InkQuiet, wrap: true); return false; }
        AddLabel(panel, "The week is under way. Men you send now set out from headquarters straight away.", Palette.InkQuiet, wrap: true);
        return true;
    }

    private bool AlreadyGiven(Func<Order, bool> same) =>
        _map.Live ? _shell.Sim.Upcoming.Any(j => j.GangId == Me && same(j.Order)) : _shell.Pending.Any(same);

    private void ShowQueued(VBoxContainer panel, Func<Order, bool> concerns)
    {
        var orders = _map.Live ? _shell.Sim.Upcoming.Where(j => j.GangId == Me).Select(j => j.Order) : _shell.Pending;
        foreach (var o in orders.Where(concerns))
            AddLabel(panel, (_map.Live ? "On the way: " : "Queued: ") + _shell.Describe(o), Palette.Player, wrap: true);
    }

    // ---- People on the street ---------------------------------------------

    private void PersonPanel(StreetPerson? p)
    {
        var panel = _businessPanel;
        if (p == null)
        {
            AddLabel(panel, "He's gone: moved on, signed up, or had his say.", Palette.InkQuiet, wrap: true);
            return;
        }
        var ward = W.Wards.ElementAtOrDefault(p.WardId)?.Name ?? "the district";
        var lot = W.Map.LotAt(p.LotId);
        AddLabel(panel, p.Name, MapView.MarkerOf(p.Kind), 18);
        bool Concerns(Order o) => o switch
        {
            HireOrder h => h.PersonId == p.Id,
            PayCopOrder c => c.PersonId == p.Id,
            BuyStoryOrder b => b.PersonId == p.Id,
            SilenceOrder s => s.PersonId == p.Id,
            _ => false,
        };

        switch (p.Kind)
        {
            case PersonKind.Drifter:
            {
                AddLabel(panel, $"Drifter, {(W.Week - p.BornWeek) / Content.WeeksPerYear}, hanging about on {W.Map.StreetOf(lot)} St in {ward}. Looking for work.", Palette.InkQuiet, wrap: true);
                AddLabel(panel, $"Intimidation {p.Intimidation} · Muscle {p.Muscle} · Brains {p.Brains} · Stealth {p.Stealth}", Palette.Ink);
                AddLabel(panel, $"Asks ${p.Fee} to sign on, then about ${14 + (p.Strength + p.Brains) * 2} a week. Moves on in {p.WeeksLeft} week{(p.WeeksLeft == 1 ? "" : "s")}.", Palette.InkQuiet, wrap: true);
                AddLabel(panel, $"A stranger off the street costs ${Content.RecruitCost}, but you don't know what you're getting.", Palette.InkQuiet, wrap: true);
                ShowQueued(panel, Concerns);
                if (!CanGiveOrders(panel) || AlreadyGiven(Concerns)) return;
                var hire = AddButton(panel, $"Sign him on (${p.Fee})", () => Give(new HireOrder(Me, p.Id)));
                hire.Disabled = W.Player.Cash < p.Fee;
                break;
            }
            case PersonKind.BeatCop:
            {
                AddLabel(panel, $"Walks the beat in {ward}.", Palette.InkQuiet, wrap: true);
                AddLabel(panel, p.Nerve >= 8 ? "He has the look of a straight arrow. He may not take money." : "He looks like a man with a family to feed.", Palette.InkQuiet, wrap: true);
                if (p.PaidByGangId >= 0)
                    AddLabel(panel, p.PaidByGangId == Me ? "He's on your money this week." : $"{W.GangById(p.PaidByGangId).Name} has paid him this week.", Palette.Gang(W, p.PaidByGangId), wrap: true);
                AddLabel(panel, $"Paid off, he looks away for the week: heat -{Content.CopCooling}, and the police leave your rackets in {ward} alone if they raid you. An honest one writes it up.", Palette.InkQuiet, wrap: true);
                ShowQueued(panel, Concerns);
                if (!CanGiveOrders(panel) || p.PaidByGangId >= 0 || AlreadyGiven(Concerns)) return;
                var pay = AddButton(panel, $"Slip him ${Content.CopPrice}", () => Give(new PayCopOrder(Me, p.Id)));
                pay.Disabled = W.Player.Cash < Content.CopPrice;
                break;
            }
            case PersonKind.Reporter:
            {
                var gang = W.GangById(p.GangId);
                bool mine = p.GangId == Me;
                AddLabel(panel, $"Asking questions about {(mine ? "you" : gang.Name)} on {W.Map.StreetOf(lot)} St. His story runs in {p.WeeksLeft} week{(p.WeeksLeft == 1 ? "" : "s")}.", mine ? Palette.Bad : Palette.InkQuiet, wrap: true);
                AddLabel(panel, "When it runs, the gang's heat jumps and the city's outrage with it, which helps the reformers at the polls.", Palette.InkQuiet, wrap: true);
                ShowQueued(panel, Concerns);
                if (!mine) { AddLabel(panel, "It's not your name in the paper. Let him write.", Palette.InkQuiet, wrap: true); return; }
                if (!CanGiveOrders(panel) || AlreadyGiven(Concerns)) return;
                int price = Content.StoryPrice(W.Player.Heat);
                var buy = AddButton(panel, $"Buy the story (${price})", () => Give(new BuyStoryOrder(Me, p.Id)));
                buy.Disabled = W.Player.Cash < price;
                break;
            }
            case PersonKind.Witness:
            {
                var gang = W.GangById(p.GangId);
                bool mine = p.GangId == Me;
                AddLabel(panel, $"Saw {p.About}.", mine ? Palette.Bad : Palette.InkQuiet, wrap: true);
                AddLabel(panel, $"Goes to the police in {p.WeeksLeft} week{(p.WeeksLeft == 1 ? "" : "s")} unless someone changes his mind. " +
                               (mine ? "If he testifies, your man goes away for months." : $"That's {gang.Name}'s problem."), Palette.InkQuiet, wrap: true);
                ShowQueued(panel, Concerns);
                if (!mine || !CanGiveOrders(panel) || AlreadyGiven(Concerns)) return;
                var hoods = FreeHoods().OrderByDescending(h => h.Intimidation).ToList();
                if (hoods.Count == 0) { AddLabel(panel, "All your men are busy.", Palette.InkQuiet); return; }
                AddLabel(panel, "Send someone to have a word. If he won't scare, he goes straight to the police.", Palette.Ink, wrap: true);
                var who = HoodPicker(hoods, h => $"{Simulation.SilenceChance(h, p):P0} he keeps quiet");
                AddButton(panel, "Have a word", () => Give(new SilenceOrder(Me, who.GetSelectedId(), p.Id)));
                break;
            }
        }
    }

    // ---- Men of other gangs -------------------------------------------------

    private void HoodPanel(Hood target)
    {
        var panel = _businessPanel;
        var gang = W.GangById(target.GangId);
        bool boss = gang.BossHoodId == target.Id;
        AddLabel(panel, target.Name, Palette.Gang(W, gang.Id), 18);
        AddLabel(panel, $"{(boss ? "Boss of" : "With")} {gang.Name}, {target.Age(W.Week)}.", Palette.InkQuiet);
        AddLabel(panel, $"Intimidation {target.Intimidation} · Muscle {target.Muscle} · Brains {target.Brains} · Stealth {target.Stealth}", Palette.Ink);
        int handles = W.Businesses.Count(b => b.HandlerHoodId == target.Id && b.ProtectorGangId == gang.Id);
        if (handles > 0) AddLabel(panel, $"Collects from {handles} business{(handles == 1 ? "" : "es")}.", Palette.InkQuiet);
        if (target.GangId != Me && W.CrewOfHood(target.Id) is { } crew)
            AddLabel(panel, crew.LieutenantHoodId == target.Id
                ? $"Lieutenant, with {crew.MemberIds.Count} men under him. If he goes, the best of them takes over the crew."
                : $"In {W.HoodById(crew.LieutenantHoodId).Name}'s crew.", Palette.InkQuiet, wrap: true);
        RapSheet(panel, target);
        if (target.GangId == Me)
        {
            AddLabel(panel, "One of yours. Give him work from a business, a bank or someone on the street.", Palette.InkQuiet, wrap: true);
            OwnManGear(panel, target);
            return;
        }
        if (target.Weapon != Weapon.None || target.Car != Car.None) AddLabel(panel, GearLine(target), Palette.InkQuiet, wrap: true);
        if (!target.IsAvailable) { AddLabel(panel, "He's out of reach.", Palette.InkQuiet); return; }
        if (boss && Simulation.IsGuardedBoss(W, target)) AddLabel(panel, "A boss keeps soldiers round him. He's much harder to get to.", Palette.InkQuiet, wrap: true);
        ShowQueued(panel, o => o is HitOrder h && h.TargetHoodId == target.Id);
        if (!CanGiveOrders(panel) || AlreadyGiven(o => o is HitOrder h && h.TargetHoodId == target.Id)) return;

        AddLabel(panel, "Put out a hit. Killing a man in the street brings heat, and someone may see it. If it goes wrong your man may not come back.", Palette.Ink, wrap: true);
        var hoods = FreeHoods().Where(h => h.Id != W.Player.BossHoodId).ToList();
        if (hoods.Count > 0)
        {
            var who = HoodPicker(hoods, h => $"{Simulation.HitChance(W, h, Array.Empty<Hood>(), target):P0} chance");
            AddButton(panel, "Send him", () => Give(new HitOrder(Me, who.GetSelectedId(), target.Id)));
        }
        CrewPicker(panel, "Send the crew", team => $"{Simulation.HitChance(W, team[0], team.Skip(1), target):P0} chance",
            (lead, backup) => new HitOrder(Me, lead, target.Id, backup), byStealth: true);
        if (hoods.Count == 0 && !W.CrewsOf(Me).Any()) AddLabel(panel, "All your men are busy.", Palette.InkQuiet);
    }

    /// <summary>What the man has done, and what the police have on him: the last few lines of it.</summary>
    private void RapSheet(VBoxContainer panel, Hood man)
    {
        AddLabel(panel, Reports.Known(man), Palette.Ink, 13, wrap: true);
        AddLabel(panel, Reports.Priors(man), man.Convictions >= Content.HabitualOffender ? Palette.Bad : Palette.InkQuiet, 13, wrap: true);
        foreach (var e in man.Record.TakeLast(4).Reverse())
            AddLabel(panel, $"{Reports.Date(W, e.Week)}: {e.Text}", e.Kind == RapKind.Convicted ? Palette.Bad : e.Kind == RapKind.Acquitted ? Palette.Good : Palette.InkQuiet, 12, wrap: true);
        if (man.Record.Count > 4) AddLabel(panel, $"…and {man.Record.Count - 4} older. The whole sheet is on the back of his card.", Palette.InkQuiet, 12, wrap: true);
    }

    // ---- Banks ----------------------------------------------------------------

    private void BankPanel(Bank bank)
    {
        var panel = _businessPanel;
        var lot = W.Map.LotAt(bank.LotId);
        AddLabel(panel, bank.Name, Palette.Ink, 18);
        AddLabel(panel, $"About ${bank.Vault / 500 * 500:N0} in the vault, of ${bank.Capacity:N0} when full. {bank.Guards} guard{(bank.Guards == 1 ? "" : "s")} on the door.", Palette.InkQuiet, wrap: true);
        if (W.Map.Distance(W.PrecinctNear(lot), lot) <= CityMap.StrideX * 2) AddLabel(panel, "There's a precinct house round the corner.", Palette.InkQuiet);
        if (W.Week - bank.RobbedWeek < 12) AddLabel(panel, "Still on its guard after the last hold-up.", Palette.Bad);
        AddLabel(panel, $"{W.Map.Distance(W.HqOf(W.Player), lot) / (double)CityMap.StrideX:0.#} blocks from your HQ", Palette.InkQuiet);
        bool Concerns(Order o) => o is RobberyOrder r && r.BankId == bank.Id;
        ShowQueued(panel, Concerns);
        if (!CanGiveOrders(panel) || AlreadyGiven(Concerns)) return;

        AddLabel(panel, "Hold it up. The man in front plans it, so brains count; backup holds the room. A big take, and a lot of heat. If it goes wrong, men are shot or go away for a year or more.", Palette.Ink, wrap: true);
        var hoods = FreeHoods().OrderByDescending(h => h.Brains).ToList();
        if (hoods.Count > 0)
        {
            var who = HoodPicker(hoods, h => $"brains {h.Brains}, {Simulation.RobberyChance(W, h, Array.Empty<Hood>(), bank):P0} alone");
            AddButton(panel, "Send him alone", () => Give(new RobberyOrder(Me, who.GetSelectedId(), bank.Id)));
        }
        CrewPicker(panel, "Send the crew", team => $"{Simulation.RobberyChance(W, team[0], team.Skip(1), bank):P0} chance",
            (lead, backup) => new RobberyOrder(Me, lead, bank.Id, backup), byBrains: true);
        if (hoods.Count == 0 && !W.CrewsOf(Me).Any()) AddLabel(panel, "All your men are busy.", Palette.InkQuiet);
    }

    // ---- Heavier work on a rival's business ---------------------------------

    private void RivalBusinessCrimes(Business b)
    {
        var panel = _businessPanel;
        var hoods = FreeHoods().Where(h => h.Id != W.Player.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3).ToList();
        var rival = W.GangById(b.ProtectorGangId);

        if (Simulation.CanHijack(W, W.Player, b) && !AlreadyGiven(o => o is HijackOrder h && h.BusinessId == b.Id))
        {
            panel.AddChild(new HSeparator());
            var info = Content.Rackets[b.Racket];
            AddLabel(panel, $"Hijack the beer truck for its {info.Label}: about ${info.WeeklyIncome * 2} for the load, and {rival.Name} is out of pocket. The drivers ride armed.", Palette.Ink, wrap: true);
            if (hoods.Count > 0)
            {
                var who = HoodPicker(hoods, h => $"{Simulation.HijackChance(W, h, Array.Empty<Hood>(), b):P0} chance");
                AddButton(panel, "Stop the truck", () => Give(new HijackOrder(Me, who.GetSelectedId(), b.Id)));
            }
            CrewPicker(panel, "Send the crew after it", team => $"{Simulation.HijackChance(W, team[0], team.Skip(1), b):P0} chance",
                (lead, backup) => new HijackOrder(Me, lead, b.Id, backup));
        }

        if (hoods.Count > 0 && !AlreadyGiven(o => o is ArsonOrder a && a.BusinessId == b.Id))
        {
            panel.AddChild(new HSeparator());
            string lose = b.Racket != RacketKind.None ? $" and its {Content.Rackets[b.Racket].Label}" : "";
            AddLabel(panel, $"Burn it down. {rival.Name} loses the business for two months{lose}. A guard on the door makes it much harder, and fire brings heat.", Palette.Ink, wrap: true);
            var torch = HoodPicker(hoods.OrderByDescending(h => h.Stealth).ToList(), h => $"stealth {h.Stealth}, {Simulation.ArsonChance(h, guarded: false):P0} if unguarded");
            AddButton(panel, "Set the fire", () => Give(new ArsonOrder(Me, torch.GetSelectedId(), b.Id)));
        }
    }

    /// <summary>Pick a crew to send: the lieutenant or whoever suits the job best goes in front, the free men behind him.</summary>
    private void CrewPicker(VBoxContainer panel, string button, Func<List<Hood>, string> odds, Func<int, int[]?, Order> make, bool byBrains = false, bool byStealth = false)
    {
        var busy = BusyHoods();
        var teams = new List<(Crew Crew, List<Hood> Team)>();
        foreach (var crew in W.CrewsOf(Me))
        {
            var team = crew.Everyone.Select(W.HoodById).Where(h => h.IsAvailable && !busy.Contains(h.Id))
                .OrderByDescending(h => byBrains ? h.Brains : byStealth ? h.Stealth + h.Strength : h.Strength).ToList();
            if (team.Count > 0) teams.Add((crew, team));
        }
        if (teams.Count == 0) return;
        var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var (crew, team) in teams) pick.AddItem($"{CrewName(crew)} ({team.Count} men) · {odds(team)}", crew.Id);
        panel.AddChild(pick);
        AddButton(panel, button, () =>
        {
            var team = teams.First(t => t.Crew.Id == pick.GetSelectedId()).Team;
            Give(make(team[0].Id, team.Count > 1 ? team.Skip(1).Select(h => h.Id).ToArray() : null));
        });
    }
}
