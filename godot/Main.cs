using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The game screen. Plan the week on the map and the side panel, press End week, and watch
/// the week play out on the streets before the Sunday report. You can step in mid-week.
/// </summary>
public partial class Main : Control
{
    /// <summary>Game hours a second at 1×: a day takes eight minutes, so people can be watched going about it.</summary>
    private const float HoursPerSecond = 24f / (8 * 60);

    /// <summary>The live week's speeds: a day takes eight minutes at 1×, four at 2×, two at 4× and one at 8×.</summary>
    private static readonly float[] Speeds = { 1, 2, 4, 8 };

    private CommandShell _shell = null!;
    private World W => _shell.Sim.World;
    private int Me => W.Player.Id;

    private MapView _map = null!;
    private Rolodex _rolodex = null!;
    private Label _gangName = null!, _date = null!, _cash = null!, _heat = null!, _turf = null!, _men = null!;
    private Button _endWeek = null!;
    private HBoxContainer _liveBar = null!;
    private Label _clock = null!;
    private Button _pause = null!;
    private readonly List<Button> _speedButtons = new();
    private RichTextLabel _ticker = null!;
    private TabContainer _tabs = null!;
    private VBoxContainer _businessPanel = null!, _ordersPanel = null!, _crewsPanel = null!, _hallPanel = null!;
    private int _selectedWard;

    /// <summary>The size of city a new game starts in.</summary>
    private CitySize _citySize = CitySize.Metropolis;
    private MenuButton _gameMenu = null!;
    private Button _soundButton = null!;
    private ulong _lastCashSound;
    private Tree _menTree = null!, _gangsTree = null!;
    private RichTextLabel _news = null!, _console = null!;
    private LineEdit _consoleInput = null!;

    private bool _playing;
    private float _speed = 1;
    private int _replayWeek;

    public override void _Ready()
    {
        Theme = BuildTheme();
        AddChild(new Audio());
        BuildLayout();
        // Carry on from the last autosave; the city is meant to go on and on.
        if (OS.GetCmdlineUserArgs().Length > 0 || !FileAccess.FileExists(SlotPath(0)) || !LoadGame(0))
            NewGame(1920UL + (ulong)GD.Randi() % 100000);
        Audio.Instance?.StartLoops();
        RunCommandLine();
    }

    // ---- Game flow ------------------------------------------------------------

    private void NewGame(ulong seed)
    {
        StopReplay();
        Palette.Reset();
        _shell = new CommandShell(Simulation.New(NewSettings(seed)));
        _map.World = W;
        _map.Select();
        _selectedWard = W.HqOf(W.Player).WardId;
        _map.ResetView(W.HqOf(W.Player));
        _ticker.Clear();
        _ticker.AppendText($"[color=#{Palette.Hex(Palette.InkQuiet)}]You run {W.Player.Name} out of a back room on {W.Map.StreetOf(W.HqOf(W.Player))} St. " +
                           "Click a business on the map to give orders, or press Plan for me. Then End week.[/color]\n");
        _news.Text = GoalSection() + "No reports yet. Your first week starts Monday.";
        RefreshAll();
        ShowBriefing();
    }

    private int _bossAtStartOfWeek = -1;

    private void EndWeek()
    {
        if (_map.Live || !W.Player.Alive) return;
        _bossAtStartOfWeek = W.Player.BossHoodId;
        _replayWeek = W.Week;
        _ticker.Clear();
        _shell.StartWeek();
        // Monday morning: what your men inside other outfits say is coming.
        foreach (var e in W.Events.Where(e => e.Week == _replayWeek && e.Kind == EventKind.Whisper && e.GangId == Me && e.Text.StartsWith("Word from ")))
            AppendTicker(e.Tick, e.GangId, e.Text);
        SetLive(true);
        _map.BeginReplay(new Dictionary<int, (int, RacketKind)>(_shell.Sim.StartOfWeek), _shell.Sim);
        _playing = true;
        Audio.Instance?.Play("horn", 0.35f);
        RefreshAll();
    }

    private void FinishReplay()
    {
        _map.EndReplay();
        _playing = false;
        SetLive(false);
        // Raids were already shown as the police reached them.
        var shown = W.Script.Where(a => a.Kind == ActionKind.Raid).Select(a => a.Text).ToHashSet();
        foreach (var e in W.Events.Where(e => e.Week == _replayWeek && e.Tick >= Content.ReckoningTick && (Reports.IsHeadline(e) || e.Kind == EventKind.Whisper && e.GangId == Me) && !shown.Contains(e.Text)))
            AppendTicker(e.Tick, e.GangId, e.Text);
        _news.Text = WeekReport(_replayWeek);
        _tabs.CurrentTab = TabIndex("Report");
        Audio.Instance?.Play("paper");
        SaveGameTo(0);
        AnnounceNewBoss();
        AnnounceNews();
        AnnounceVerdict();
        RefreshAll();
    }

    public override void _Process(double delta)
    {
        if (!_map.Live) return;
        if (_playing) _map.Advance((float)delta * HoursPerSecond * _speed);
        _clock.Text = MapView.ClockMinutes(_map.Clock);
        if (_map.ReplayFinished) FinishReplay();
    }

    private void OnAction(ScriptAction a)
    {
        PlaySound(a);
        if (a.BusinessId >= 0 && a.BusinessId == _map.SelectedBusiness) RefreshBusiness();
        else if (a.Kind != ActionKind.Collect && (_map.SelectedPerson >= 0 || _map.SelectedHood >= 0 || _map.SelectedBank >= 0)) RefreshBusiness();
        if (a.Kind == ActionKind.Collect) return;
        int gang = a.Kind == ActionKind.Raid ? a.DefenderGangId : a.GangId;
        AppendTicker(a.Tick, gang, a.Text, a.DefenderGangId == Me);
    }

    private void AppendTicker(int tick, int gangId, string text, bool concernsMe = false)
    {
        bool mine = gangId == Me || concernsMe;
        var colour = mine ? Palette.Ink : Palette.InkQuiet;
        string time = MapView.ClockText(tick);
        string swatch = gangId >= 0 ? $"[color=#{Palette.Hex(Palette.Gang(W, gangId))}]■[/color] " : "";
        _ticker.AppendText($"[color=#{Palette.Hex(Palette.InkQuiet)}]{time}[/color]  {swatch}[color=#{Palette.Hex(colour)}]{Escape(text)}[/color]\n");
    }

    /// <summary>When the chair changes hands, say so plainly: it's the biggest thing that happens to the player.</summary>
    private void AnnounceNewBoss()
    {
        var p = W.Player;
        if (_bossAtStartOfWeek < 0 || p.BossHoodId == _bossAtStartOfWeek) return;
        var old = W.HoodById(_bossAtStartOfWeek);
        string fate = old.State switch
        {
            HoodState.Dead => $"{old.Name} is dead at {old.Age(W.Week)}.",
            HoodState.Jailed when old.Case != null => $"{old.Name} is held for trial, charged {old.Case.Charge}.",
            HoodState.Jailed => $"{old.Name} is going away for {old.JailWeeks} weeks.",
            _ => $"{old.Name} is out.",
        };
        string text = p.Alive
            ? $"{fate}\n\n{W.HoodById(p.BossHoodId).Name} runs {p.Name} now. The outfit is yours to carry on."
            : $"{fate}\n\nThere was nobody left to take over. {p.Name} is finished, but the city goes on. Start a new outfit from the Game menu.";
        var dialog = new AcceptDialog { Title = "The chair changes hands", DialogText = text, OkButtonText = "Carry on" };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(440, 0));
        _bossAtStartOfWeek = -1;
    }

    private void PlaySound(ScriptAction a)
    {
        var audio = Audio.Instance;
        if (audio == null) return;
        bool near = a.GangId == Me || a.DefenderGangId == Me;
        float v = near ? 1f : 0.55f;
        switch (a.Kind)
        {
            case ActionKind.Extort when a.Result == ActionResult.Success: audio.Play("bell", v * 0.8f); break;
            case ActionKind.Extort when a.Result == ActionResult.Failed: audio.Play("door", v * 0.7f); break;
            case ActionKind.Extort when a.Result == ActionResult.Arrested: audio.Play("whistle", v * 0.6f); break;
            case ActionKind.Takeover: audio.Play(a.CasualtyHoodId >= 0 ? "gunshots" : "gunshot", v * 0.8f); break;
            case ActionKind.Racket: audio.Play("knock", v * 0.8f); break;
            case ActionKind.Raid: audio.Play("whistle", v * 0.6f); break;
            case ActionKind.Hit: audio.Play(a.Result == ActionResult.Won ? "gunshots" : "gunshot", v * 0.9f); break;
            case ActionKind.Robbery: audio.Play(a.Result == ActionResult.Success ? "cash" : "whistle", v * 0.8f); break;
            case ActionKind.Union: audio.Play(a.CasualtyHoodId >= 0 ? "gunshots" : a.Result is ActionResult.Success or ActionResult.Won ? "bell" : "door", v * 0.7f); break;
            case ActionKind.Hijack: audio.Play(a.CasualtyHoodId >= 0 ? "gunshots" : "horn", v * 0.6f); break;
            case ActionKind.Arson when a.Result == ActionResult.Success: audio.Play("door", v * 0.8f); break;
            case ActionKind.Hire when a.GangId == Me: audio.Play("cash", 0.5f); break;
            case ActionKind.Collect when a.GangId == Me && Time.GetTicksMsec() - _lastCashSound > 350:
                _lastCashSound = Time.GetTicksMsec();
                audio.Play("cash", 0.45f, 0.08f);
                break;
        }
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");

    /// <summary>The Sunday report: the books, then the week's headlines with your own in bold.</summary>
    private string WeekReport(int week)
    {
        var p = W.Player;
        string C(Color c) => "#" + Palette.Hex(c);
        var sb = new System.Text.StringBuilder();
        sb.Append($"[font_size=18]Week {week % Content.WeeksPerYear + 1}, {Content.StartYear + week / Content.WeeksPerYear}[/font_size]\n\n");
        sb.Append(GoalSection());
        if (W.LastLedger.TryGetValue(p.Id, out var l))
        {
            void Line(string label, long amount, bool cost)
            {
                if (amount == 0) return;
                var colour = cost ? Palette.Bad : Palette.Good;
                sb.Append($"[color={C(Palette.InkQuiet)}]{label}[/color]  [color={C(colour)}]{(cost ? "-" : "+")}${amount:N0}[/color]\n");
            }
            Line("Protection", l.Protection, false);
            Line("Rackets", l.Rackets, false);
            Line("Wages", l.Wages, true);
            Line("Spending", l.Spending, true);
            Line("Fines and seizures", l.Fines, true);
            Line("Fronts' own profit", l.Legit, false);
            Line("Bookkeeper", l.LaunderFee, true);
            Line("Tribute received", l.TributeIn, false);
            Line("Tribute paid", l.TributePaid, true);
            Line("Union dues", l.Dues, false);
            Line("The racing wire", l.Wire, false);
            var net = l.Net >= 0 ? Palette.Good : Palette.Bad;
            sb.Append($"[b]Net [color={C(net)}]{(l.Net >= 0 ? "+" : "-")}${Math.Abs(l.Net):N0}[/color][/b]   Cash ${p.Cash:N0} (${p.Clean:N0} clean) · Heat {p.Heat}\n");
            if (l.Laundered > 0) sb.Append($"[color={C(Palette.InkQuiet)}]Washed ${l.Laundered:N0} through your fronts.[/color]\n");
            sb.Append('\n');
        }
        else if (!p.Alive) sb.Append($"[color={C(Palette.Bad)}]Your outfit is finished. The city goes on without you.[/color]\n\n");

        sb.Append(InThePapers());
        sb.Append(PublicEnemySection());
        sb.Append(SquadSection());
        sb.Append($"[color={C(Palette.InkQuiet)}]HEADLINES[/color]\n");
        foreach (var e in W.Events.Where(e => e.Week == week && (Reports.IsHeadline(e) || e.Kind == EventKind.Whisper && e.GangId == p.Id)).OrderBy(e => e.Tick))
        {
            bool mine = e.GangId == p.Id;
            string swatch = e.GangId >= 0 ? $"[color={C(Palette.Gang(W, e.GangId))}]■[/color] " : "";
            sb.Append(mine ? $"{swatch}[b]{Escape(e.Text)}[/b]\n" : $"{swatch}[color={C(Palette.InkQuiet)}]{Escape(e.Text)}[/color]\n");
        }
        return sb.ToString();
    }

    // ---- Orders ---------------------------------------------------------------

    private void Queue(Order order)
    {
        // One standing order per business for rates; one job per hood.
        if (order is SetRateOrder rate) _shell.Pending.RemoveAll(o => o is SetRateOrder r && r.BusinessId == rate.BusinessId);
        _shell.Pending.Add(order);
        RefreshAll();
    }

    /// <summary>Give an order in the middle of the week: the men set out now.</summary>
    private void OrderNow(Order order)
    {
        int? tick = _shell.Sim.OrderNow(order, _map.Clock);
        if (tick == null) AppendTicker((int)_map.Clock, Me, "Too late in the week for that. It'll have to wait for Monday.", true);
        else AppendTicker((int)_map.Clock, Me, $"{_shell.Describe(order)}: setting out now, there by {MapView.ClockText(tick.Value)}.", true);
        RefreshAll();
    }

    private static int BusinessOf(Order o) => o switch
    {
        ExtortOrder e => e.BusinessId,
        RacketOrder r => r.BusinessId,
        GuardOrder g => g.BusinessId,
        SetRateOrder s => s.BusinessId,
        _ => -1,
    };

    /// <summary>Men who have a job: queued for next week while planning, or done or under way while the week runs.</summary>
    private HashSet<int> BusyHoods() =>
        _map.Live ? _shell.Sim.CommittedHoods(Me) : _shell.Pending.SelectMany(Simulation.TeamOf).ToHashSet();

    private List<Hood> FreeHoods()
    {
        var busy = BusyHoods();
        return W.AvailableHoodsOf(Me).Where(h => !busy.Contains(h.Id)).OrderByDescending(h => h.Strength).ToList();
    }

    /// <summary>A crew order with everyone in the crew who is free.</summary>
    private Order? CrewOrder(Crew crew, int businessId, bool guard) => _shell.CrewOrderFor(crew.Id, businessId, guard, BusyHoods());

    private string CrewName(Crew c) => $"{W.HoodById(c.LieutenantHoodId).Name}'s crew";

    // ---- Refresh --------------------------------------------------------------

    private void RefreshAll()
    {
        var p = W.Player;
        _gangName.Text = p.Alive ? p.Name : $"{p.Name} (finished)";
        _gangName.AddThemeColorOverride("font_color", Palette.Player);
        _date.Text = Reports.Date(W) + (W.Prohibition ? "" : " · after repeal");
        if (p.Alive)
        {
            var boss = W.HoodById(p.BossHoodId);
            _date.Text += $" · {boss.Name}, {boss.Age(W.Week)}" + (W.RunsFromInside(p) ? ", inside" : "");
        }
        CashLabel(p);
        _heat.Text = $"Heat {p.Heat}";
        _heat.AddThemeColorOverride("font_color", p.Heat > 60 ? Palette.Bad : p.Heat > 35 ? Palette.Player : Palette.Ink);
        _turf.Text = $"Turf {W.TurfOf(Me).Count()}/{W.Businesses.Count}";
        _men.Text = $"Men {W.HoodsOf(Me).Count()}";
        if (_map.Live) _endWeek.Text = "Week in progress";
        else _endWeek.Text = _shell.Pending.Count > 0 ? $"End week  ({_shell.Pending.Count} order{(_shell.Pending.Count == 1 ? "" : "s")})" : "End week";
        _endWeek.Disabled = !p.Alive || _map.Live;

        RefreshBusiness();
        RefreshMen();
        RefreshRolodex();
        RefreshOrders();
        RefreshCrews();
        RefreshGangs();
        RefreshCityHall();
        _map.QueueRedraw();
    }

    private void RefreshBusiness()
    {
        Clear(_businessPanel);
        if (RefreshOtherSelection()) return;
        int id = _map.SelectedBusiness;
        if (id < 0)
        {
            AddLabel(_businessPanel, "Click a business, a bank or someone on the street.", Palette.InkQuiet);
            AddLabel(_businessPanel, "Everyone on the map is a dot coloured by what he is: grey a citizen (the larger ones are drifters looking for work), bright blue a police officer, dark blue a detective, white a reporter and gold a witness. Gang men are black: a small gang's plain, and a big outfit's with its colour round the rim, yours in brass. Click anyone in the crowd to pull their card from the city directory.", Palette.InkQuiet, wrap: true);
            AddLabel(_businessPanel, "A shop's rooftop sign is framed, and its awning striped, in the colours of the gang it pays; yours are brass. Plain ones pay nobody yet. A red badge on the roof means a racket in the back room.", Palette.InkQuiet, wrap: true);
            AddLabel(_businessPanel, "Scroll to zoom the map. Drag with the right mouse button to move it.", Palette.InkQuiet, wrap: true);
            Acquaintances(_businessPanel);
            return;
        }

        var b = W.BusinessById(id);
        var lot = W.LotOf(b);
        AddLabel(_businessPanel, b.Name, Palette.Ink, 18);
        if (b.Kind == BusinessKind.Court)
        {
            AddLabel(_businessPanel, $"Tenement court · the block pays ${b.Takings} a week · toughness {b.Toughness}/10", Palette.InkQuiet);
            AddLabel(_businessPanel, $"Out of sight of the street: a racket here draws {Content.CourtHeatOff} less heat a week. Nobody owns it, so nobody can sell it.", Palette.InkQuiet, wrap: true);
        }
        else AddLabel(_businessPanel, $"{Content.Label(b.Kind)} · takes ${b.Takings} a week · owner toughness {b.Toughness}/10", Palette.InkQuiet);
        AddLabel(_businessPanel, $"{W.BlocksFromHq(W.Player, b):0.#} blocks from your HQ", Palette.InkQuiet);
        if (lot.WardId < W.Wards.Count)
        {
            var ward = W.Wards[lot.WardId];
            string whose = ward.Reformer ? "a reformer who can't be bought" : ward.OwnerGangId == Me ? "on your payroll" : ward.OwnerGangId >= 0 ? $"on {W.GangById(ward.OwnerGangId).Name}'s payroll" : "a party man, for sale";
            AddLabel(_businessPanel, $"In {ward.Name}. Alderman {ward.Alderman} is {whose}.", ward.OwnerGangId >= 0 ? Palette.Gang(W, ward.OwnerGangId) : Palette.InkQuiet, wrap: true);
        }

        if (b.IsProtected)
        {
            var g = W.GangById(b.ProtectorGangId);
            if (b.OwnerGangId == g.Id) AddLabel(_businessPanel, $"Run by {g.Name}'s own people.", Palette.Gang(W, g.Id));
            else if (W.StrikeAt(b) != null) AddLabel(_businessPanel, $"Pays {g.Name} {b.ProtectionRate}%, but nothing while the pickets are out.", Palette.Gang(W, g.Id), wrap: true);
            else AddLabel(_businessPanel, $"Pays {g.Name}: {b.ProtectionRate}% (${b.Takings * b.ProtectionRate / 100}/week)", Palette.Gang(W, g.Id));
            var handler = W.Hoods.FirstOrDefault(h => h.Id == b.HandlerHoodId);
            if (handler != null) AddLabel(_businessPanel, $"Handled by {handler.Name}", Palette.InkQuiet);
        }
        else AddLabel(_businessPanel, "Pays nobody.", Palette.InkQuiet);
        if (W.OwnerInPocket(W.Player, b)) AddLabel(_businessPanel, "The owner is in your pocket.", Palette.Good);
        if (!b.IsFront || b.OwnerGangId != b.ProtectorGangId) AddLabel(_businessPanel, $"Resentment {b.Resentment}/100" + (b.Resentment > 60 ? " · may talk to the police" : ""), b.Resentment > 60 ? Palette.Bad : Palette.InkQuiet);
        NeighbourhoodLine(b);
        ShopRowLine(b);
        FrontLines(b);
        if (b.Racket != RacketKind.None) AddLabel(_businessPanel, $"Back room: {RacketLabel(b)}", Palette.Bad);
        BoozeLine(b);
        RepealLine(b);
        ClubLine(b);
        ContractLine(b);
        SquadLine(b);
        UnionLine(b);
        if (!b.IsOpen) AddLabel(_businessPanel, W.Week - b.BurnedWeek <= 8 ? $"Burned out. Rebuilding for {b.ShutWeeks} more weeks." : $"Closed by the police for {b.ShutWeeks} more weeks.", Palette.Bad);

        if (_map.Live)
            foreach (var job in _shell.Sim.Upcoming.Where(j => j.GangId == Me && BusinessOf(j.Order) == id))
                AddLabel(_businessPanel, $"On the way: {_shell.Describe(job.Order)}, there by {MapView.ClockText(job.Tick)}", Palette.Player, wrap: true);
        else
            foreach (var o in _shell.Pending.Where(o => BusinessOf(o) == id))
                AddLabel(_businessPanel, "Queued: " + _shell.Describe(o), Palette.Player, wrap: true);

        _businessPanel.AddChild(new HSeparator());
        if (!W.Player.Alive) return;

        if (_map.Live)
        {
            if (_map.Clock >= Simulation.LastOrderTick - 1) { AddLabel(_businessPanel, "It's too late in the week to send anyone. Orders resume Monday.", Palette.InkQuiet, wrap: true); return; }
            AddLabel(_businessPanel, "The week is under way. Men you send now set out from headquarters straight away.", Palette.InkQuiet, wrap: true);
        }
        // When you run the shop's local, the strike comes first.
        bool unionFirst = W.LocalOf(b)?.OwnerGangId == Me;
        if (unionFirst) UnionActions(b);
        if (b.ProtectorGangId == Me) { BoozeActions(b); FixActions(b); FrontOffer(b); OwnBusinessActions(b); }
        else if (b.IsOpen)
        {
            TargetActions(b);
            if (b.IsProtected) RivalBusinessCrimes(b);
        }
        if (!unionFirst) UnionActions(b);
    }

    /// <summary>Queue an order while planning, or send the men at once during the live week.</summary>
    private void Give(Order? order)
    {
        if (order == null) return;
        if (_map.Live) OrderNow(order);
        else Queue(order);
    }

    private void TargetActions(Business b)
    {
        var hoods = FreeHoods().Where(h => h.Id != W.Player.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3).ToList();
        var crews = W.CrewsOf(Me).Select(c => (Crew: c, Order: CrewOrder(c, b.Id, guard: false) as ExtortOrder)).Where(x => x.Order != null).ToList();
        if (hoods.Count == 0 && crews.Count == 0) { AddLabel(_businessPanel, _map.Live ? "All your men are busy this week." : "All your men have jobs this week. Cancel one on the Orders tab to free him.", Palette.InkQuiet, wrap: true); return; }

        bool rival = b.IsProtected;
        if (rival && W.PactBetween(Me, b.ProtectorGangId) is { } pact)
            AddLabel(_businessPanel, $"You have {(pact.Kind == PactKind.Truce ? "a truce" : "an alliance")} with {W.GangById(b.ProtectorGangId).Name}. Moving on their turf breaks it, and nobody will take your word for a year.", Palette.Bad, 13, wrap: true);
        string Odds(Hood h, IReadOnlyList<Hood> backup)
        {
            if (!rival) return $"{Simulation.ExtortChance(W, W.Player, h, b, backup.Count):P0} chance";
            double defence = Simulation.DefenceStrength(W, W.GangById(b.ProtectorGangId), b);
            return $"{Simulation.TakeoverChance(Simulation.StormingStrength(h, backup), defence):P0} to win if unguarded";
        }

        AddLabel(_businessPanel, rival ? $"Take it from {W.GangById(b.ProtectorGangId).Name}. Expect a fight; men can die." : "Lean on the owner for protection money.", Palette.Ink, wrap: true);
        string verb = _map.Live ? "now" : "";
        if (hoods.Count > 0)
        {
            var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            foreach (var h in hoods) pick.AddItem($"{h.Name} · {Odds(h, Array.Empty<Hood>())}{(rival ? "" : Among(h, b))}", h.Id);
            _businessPanel.AddChild(pick);
            AddButton(_businessPanel, (rival ? "Send him to take it " : "Send him ") + verb, () => Give(new ExtortOrder(Me, pick.GetSelectedId(), b.Id)));
        }
        if (crews.Count > 0)
        {
            var crewPick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            foreach (var (crew, order) in crews)
            {
                var team = order!.Team.Select(W.HoodById).ToList();
                crewPick.AddItem($"{CrewName(crew)} ({team.Count} men) · {Odds(team[0], team.Skip(1).ToList())}", crew.Id);
            }
            _businessPanel.AddChild(crewPick);
            AddButton(_businessPanel, "Send the crew " + verb, () => Give(CrewOrder(W.CrewById(crewPick.GetSelectedId())!, b.Id, guard: false)));
        }
    }

    private void OwnBusinessActions(Business b)
    {
        var hoods = FreeHoods();
        var crews = W.CrewsOf(Me).Where(c => CrewOrder(c, b.Id, guard: true) != null).ToList();
        if (_map.Live)
        {
            if (hoods.Count == 0 && crews.Count == 0) { AddLabel(_businessPanel, "All your men are busy this week.", Palette.InkQuiet, wrap: true); return; }
            GuardActions(b, hoods, crews);
            return;
        }

        // Nobody charges himself for protection.
        if (b.OwnerGangId != Me)
        {
            AddLabel(_businessPanel, "Protection rate", Palette.Ink);
            var rateRow = new HBoxContainer();
            var rate = new SpinBox { MinValue = 5, MaxValue = 30, Value = b.ProtectionRate, Suffix = "%" };
            rateRow.AddChild(rate);
            AddButton(rateRow, "Set rate", () => Queue(new SetRateOrder(Me, b.Id, (int)rate.Value)));
            _businessPanel.AddChild(rateRow);
            AddLabel(_businessPanel, "Above 12% the owner grows resentful; below it he warms to you.", Palette.InkQuiet, wrap: true);
        }

        if (hoods.Count == 0 && crews.Count == 0) { AddLabel(_businessPanel, "All your men have jobs this week. Cancel one on the Orders tab to free him.", Palette.InkQuiet, wrap: true); return; }

        // Rackets the back room could take, short of the money for them.
        var rackets = Content.RacketsFor(b.Kind)
            .Where(k => W.CannotOpenRacket(W.Player, b, k) is not { } why || why.StartsWith("That costs"))
            .Select(k => Content.Rackets[k])
            .ToList();
        if (rackets.Count > 0 && hoods.Count > 0)
        {
            _businessPanel.AddChild(new HSeparator());
            AddLabel(_businessPanel, b.Racket == RacketKind.None ? "Open a racket in the back room" : $"Put the back room to better use than a {RacketLabel(b)}", Palette.Ink);
            var racketPick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            foreach (var r in rackets)
                racketPick.AddItem($"{r.Label} · costs ${r.SetupCost}, pays ~${r.WeeklyIncome}/week{(r.Kind == RacketKind.Casino || r.Kind == RacketKind.Nightclub && !W.Prohibition ? " clean" : "")}, +{r.WeeklyHeat} heat" +
                    (r.Kind == RacketKind.Counterfeit ? ", Treasury risk" : r.Kind == RacketKind.Casino && !Politics.Covered(W, W.Player, b) ? $" (+{Content.CasinoHeatUncovered} without the ward's alderman)"
                    : r.Kind == RacketKind.Nightclub ? ", respect, pull at City Hall" : ""), (int)r.Kind);
            _businessPanel.AddChild(racketPick);
            var who = HoodPicker(hoods, h => $"brains {h.Brains}");
            AddButton(_businessPanel, "Open it", () => Queue(new RacketOrder(Me, who.GetSelectedId(), b.Id, (RacketKind)racketPick.GetSelectedId())));
        }

        GuardActions(b, hoods, crews);
    }

    private void GuardActions(Business b, List<Hood> hoods, List<Crew> crews)
    {
        _businessPanel.AddChild(new HSeparator());
        AddLabel(_businessPanel, _map.Live ? "Post a guard for the rest of the week" : "Post a guard for the week", Palette.Ink);
        if (hoods.Count > 0)
        {
            var guard = HoodPicker(hoods, h => $"strength {h.Strength}");
            AddButton(_businessPanel, _map.Live ? "Post him now" : "Post him", () => Give(new GuardOrder(Me, guard.GetSelectedId(), b.Id)));
        }
        if (crews.Count > 0)
        {
            var crewPick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            foreach (var c in crews)
            {
                var team = Simulation.TeamOf(CrewOrder(c, b.Id, guard: true)!).Select(W.HoodById).ToList();
                crewPick.AddItem($"{CrewName(c)} ({team.Count} men) · strength {team[0].Strength}+{Simulation.BackupStrength(team.Skip(1)):0}", c.Id);
            }
            _businessPanel.AddChild(crewPick);
            AddButton(_businessPanel, _map.Live ? "Post the crew now" : "Post the crew", () => Give(CrewOrder(W.CrewById(crewPick.GetSelectedId())!, b.Id, guard: true)));
        }
    }

    private OptionButton HoodPicker(List<Hood> hoods, Func<Hood, string> note, Container? into = null)
    {
        var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in hoods) pick.AddItem($"{h.Name} · {note(h)}", h.Id);
        (into ?? _businessPanel).AddChild(pick);
        return pick;
    }

    private void RefreshMen()
    {
        _menTree.Clear();
        var root = _menTree.CreateItem();
        var jobs = new Dictionary<int, string>();
        var orders = _map.Live ? _shell.Sim.Upcoming.Where(j => j.GangId == Me).Select(j => j.Order) : _shell.Pending;
        foreach (var o in orders)
            foreach (var id in Simulation.TeamOf(o)) jobs.TryAdd(id, _shell.Describe(o));
        var busy = BusyHoods();
        foreach (var h in W.HoodsOf(Me).OrderBy(h => h.Id))
        {
            var it = _menTree.CreateItem(root);
            jobs.TryGetValue(h.Id, out var job);
            string state = h.Id == W.Player.BossHoodId ? (h.State == HoodState.Lam ? "Boss, away" : "Boss")
                : h.State == HoodState.Jailed ? (h.Case != null ? $"Trial {h.JailWeeks}w" : $"Jail {h.JailWeeks}w")
                : h.State == HoodState.Lam ? (World.IsWanted(h) ? "On the lam" : "Away")
                : h.Bail > 0 ? $"Bail {h.JailWeeks}w" : World.IsWanted(h) ? "Wanted"
                : busy.Contains(h.Id) ? "Job" : "Free";
            if (h.Id == W.Player.HeirHoodId) state = h.State == HoodState.Jailed || h.State == HoodState.Lam || h.Case != null ? state : W.RunsFromInside(W.Player) ? "Street" : "Heir";
            var crew = W.CrewOfHood(h.Id);
            string crewText = crew == null ? "" : crew.LieutenantHoodId == h.Id ? "Lt" : World.Surname(W.HoodById(crew.LieutenantHoodId).Name);
            string[] cols = { h.Name, $"{h.Age(W.Week)}", $"{h.Intimidation}", $"{h.Muscle}", $"{h.Brains}", $"{h.Stealth}", $"{h.Loyalty}", crewText, state };
            for (int i = 0; i < cols.Length; i++) it.SetText(i, cols[i]);
            string tip = $"{h.Name}, {h.Age(W.Week)}, {Content.Label(h.Origin)}" + (h.Family ? ", family" : "") + (W.MarriageOf(h) is { } wed ? $", married to {wed.Bride} of {W.GangById(wed.Other(Me)).Name}" : "") + $"\nIntimidation {h.Intimidation}, Muscle {h.Muscle}, Brains {h.Brains}, Stealth {h.Stealth}\nLoyalty {h.Loyalty}/100, ambition {h.Ambition}/100, wage ${h.Wage}/week\n{GearLine(h)}" + (job != null ? $"\nThis week: {job}" : "");
            for (int i = 0; i < cols.Length; i++) it.SetTooltipText(i, tip);
            if (h.Loyalty < 30) it.SetCustomColor(6, Palette.Bad);
            if (h.Age(W.Week) >= 65) it.SetCustomColor(1, Palette.Bad);
            if (h.Family) it.SetCustomColor(0, Palette.Player);
            if (h.State == HoodState.Jailed || World.IsWanted(h)) it.SetCustomColor(8, Palette.Police);
            else if (busy.Contains(h.Id)) it.SetCustomColor(8, Palette.Player);
        }
    }

    private void RefreshRolodex()
    {
        var jobs = new Dictionary<int, string>();
        var orders = _map.Live ? _shell.Sim.Upcoming.Where(j => j.GangId == Me).Select(j => j.Order) : _shell.Pending;
        foreach (var o in orders)
            foreach (var id in Simulation.TeamOf(o)) jobs.TryAdd(id, (_map.Live ? "On the way: " : "") + _shell.Describe(o));
        _rolodex.World = W;
        _rolodex.GangId = Me;
        _rolodex.JobOf = id => jobs.GetValueOrDefault(id);
        _rolodex.Refresh();
    }

    private void RefreshOrders()
    {
        Clear(_ordersPanel);
        if (_map.Live)
        {
            AddLabel(_ordersPanel, "The week is under way. Your men still to do their jobs:", Palette.InkQuiet, wrap: true);
            foreach (var job in _shell.Sim.Upcoming.Where(j => j.GangId == Me))
                AddLabel(_ordersPanel, $"{MapView.ClockText(job.Tick)}: {_shell.Describe(job.Order)}", Palette.Ink, wrap: true);
            AddLabel(_ordersPanel, "To send more men, pause and click a business, a bank or someone on the street.", Palette.InkQuiet, wrap: true);
            return;
        }
        var buttons = new HBoxContainer();
        AddButton(buttons, "Plan for me", () => { _shell.Execute("auto"); RefreshAll(); });
        AddButton(buttons, "Clear all", () => { _shell.Pending.Clear(); RefreshAll(); });
        _ordersPanel.AddChild(buttons);
        if (_shell.Pending.Count == 0)
        {
            AddLabel(_ordersPanel, "No orders yet. Rackets still pay and protection is still collected on Sunday.", Palette.InkQuiet, wrap: true);
            return;
        }
        foreach (var o in _shell.Pending.ToList())
        {
            var row = new HBoxContainer();
            var label = new Label { Text = _shell.Describe(o), AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(label);
            var remove = new Button { Text = "✕", TooltipText = "Cancel this order" };
            remove.Pressed += () => { _shell.Pending.Remove(o); RefreshAll(); };
            row.AddChild(remove);
            _ordersPanel.AddChild(row);
        }
    }

    private void RefreshCrews()
    {
        Clear(_crewsPanel);
        SuccessionSection();
        RatsSection();
        _crewsPanel.AddChild(new HSeparator());
        AddLabel(_crewsPanel, "Crews", Palette.Player, 17);
        AddLabel(_crewsPanel, "A crew is a lieutenant and up to three men. Send a crew and they go as one team: they lean harder and fight better, but every man is tied up for the week. A crew takes its mood from its lieutenant, and an ambitious lieutenant who breaks away takes his crew with him.", Palette.InkQuiet, wrap: true);
        if (!W.Player.Alive) return;
        var loose = W.HoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId && W.CrewOfHood(h.Id) == null).OrderByDescending(h => h.Brains + h.Strength).ToList();

        foreach (var crew in W.CrewsOf(Me).ToList())
        {
            _crewsPanel.AddChild(new HSeparator());
            var lt = W.HoodById(crew.LieutenantHoodId);
            var head = new HBoxContainer();
            var title = AddLabel(head, $"{CrewName(crew)}", Palette.Player, 17);
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var disband = AddButton(head, "Break up", () => { W.LeaveCrew(lt.Id); RefreshAll(); });
            disband.TooltipText = "The men go back to working alone";
            _crewsPanel.AddChild(head);
            AddLabel(_crewsPanel, $"Lieutenant {lt.Name} · strength {lt.Strength}, brains {lt.Brains}, loyalty {lt.Loyalty}, ambition {lt.Ambition}",
                lt.Loyalty < 30 ? Palette.Bad : Palette.InkQuiet, wrap: true);
            foreach (var member in crew.MemberIds.Select(W.HoodById).ToList())
            {
                var row = new HBoxContainer();
                var name = AddLabel(row, $"{member.Name} · strength {member.Strength}, loyalty {member.Loyalty}", member.Loyalty < 30 ? Palette.Bad : Palette.Ink);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var remove = AddButton(row, "✕", () => { W.LeaveCrew(member.Id); RefreshAll(); });
                remove.TooltipText = "Take him out of the crew";
                _crewsPanel.AddChild(row);
            }
            if (crew.MemberIds.Count < Crew.MaxMembers && loose.Count > 0)
            {
                var row = new HBoxContainer();
                var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
                foreach (var h in loose) pick.AddItem($"{h.Name} · strength {h.Strength}", h.Id);
                row.AddChild(pick);
                AddButton(row, "Add", () => { W.JoinCrew(crew, W.HoodById(pick.GetSelectedId())); RefreshAll(); });
                _crewsPanel.AddChild(row);
            }
        }

        _crewsPanel.AddChild(new HSeparator());
        if (loose.Count == 0) { AddLabel(_crewsPanel, "Every man is already in a crew.", Palette.InkQuiet); return; }
        AddLabel(_crewsPanel, "Make a man a lieutenant", Palette.Ink);
        var newRow = new HBoxContainer();
        var lieutenant = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in loose) lieutenant.AddItem($"{h.Name} · brains {h.Brains}, loyalty {h.Loyalty}", h.Id);
        newRow.AddChild(lieutenant);
        AddButton(newRow, "Form crew", () => { W.FormCrew(W.HoodById(lieutenant.GetSelectedId())); RefreshAll(); });
        _crewsPanel.AddChild(newRow);
    }

    /// <summary>The boss, who follows him, and bringing the family in.</summary>
    private void SuccessionSection()
    {
        var p = W.Player;
        if (!p.Alive) return;
        var boss = W.HoodById(p.BossHoodId);
        AddLabel(_crewsPanel, "The family", Palette.Player, 17);
        int age = boss.Age(W.Week);
        NameSection(boss);
        if (W.RunsFromInside(p))
        {
            InsideSection(boss);
            return;
        }
        AddLabel(_crewsPanel, $"{boss.Name} runs the outfit. He is {age}" + (age >= 60 ? ", and not getting any younger." : "."), age >= 65 ? Palette.Bad : Palette.Ink, wrap: true);
        var heir = W.Hoods.FirstOrDefault(h => h.Id == p.HeirHoodId);
        AddLabel(_crewsPanel, heir != null
            ? $"Heir: {heir.Name}, {heir.Age(W.Week)} · brains {heir.Brains}, strength {heir.Strength}, loyalty {heir.Loyalty}. He learns the business while he waits."
            : "No heir named. If the boss dies or goes away for long, the outfit picks whoever seems strongest, and a rival may split off.",
            Palette.InkQuiet, wrap: true);

        var candidates = W.HoodsOf(Me).Where(h => h.Id != boss.Id && h.Id != p.HeirHoodId).OrderByDescending(h => h.Family).ThenByDescending(h => h.Brains * 2 + h.Strength).ToList();
        if (candidates.Count > 0)
        {
            var row = new HBoxContainer();
            var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            foreach (var h in candidates) pick.AddItem($"{h.Name}, {h.Age(W.Week)}{(h.Family ? " (family)" : "")} · brains {h.Brains}", h.Id);
            row.AddChild(pick);
            var name = AddButton(row, "Name heir", () => { _shell.Execute($"heir {pick.GetSelectedId()}"); RefreshAll(); });
            name.TooltipText = "The most ambitious man passed over will take it badly";
            _crewsPanel.AddChild(row);
        }

        InsidePlan(boss);

        if (!_map.Live)
        {
            bool queued = _shell.Pending.Any(o => o is RecruitOrder { Family: true });
            var family = AddButton(_crewsPanel, queued ? "Family arrives this week" : $"Bring a son or nephew into the business (${Content.FamilyCost})",
                () => { _shell.Execute("family"); RefreshAll(); });
            family.Disabled = queued || !Simulation.CanBringInFamily(W, p);
            family.TooltipText = "Family starts young and green, but loyal, and grows into the job. Once a year.";
        }
    }

    // ---- Saving ---------------------------------------------------------------

    public override void _Notification(int what)
    {
        // Quitting while planning keeps this week's orders for next time.
        // Mid-week too: the autosave picks the week up at the hour it was left.
        if (what == NotificationWMCloseRequest && _shell != null && W.Player.Alive && OS.GetCmdlineUserArgs().Length == 0) SaveGameTo(0);
    }

    private const int Slots = 3;

    /// <summary>Slot 0 is the autosave written every Sunday.</summary>
    private static string SlotPath(int slot) => slot == 0 ? "user://saves/autosave.json" : $"user://saves/slot{slot}.json";

    private static string? SlotLabel(int slot)
    {
        if (!FileAccess.FileExists(SlotPath(slot))) return null;
        try { var d = SaveGame.Read(FileAccess.GetFileAsString(SlotPath(slot))); return $"{d.Label} ({d.SavedAt:d MMM, HH:mm})"; }
        catch (Exception) { return "unreadable"; }
    }

    private void SaveGameTo(int slot)
    {
        DirAccess.MakeDirRecursiveAbsolute("user://saves");
        using var file = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Write);
        if (file == null) { GD.PushWarning($"Couldn't write {SlotPath(slot)}: {FileAccess.GetOpenError()}"); return; }
        file.StoreString(SaveGame.Write(_shell.Sim, _shell.Pending));
        if (slot > 0) _ticker.AppendText($"[color=#{Palette.Hex(Palette.InkQuiet)}]Saved to slot {slot}.[/color]\n");
    }

    private bool LoadGame(int slot)
    {
        try
        {
            var data = SaveGame.Read(FileAccess.GetFileAsString(SlotPath(slot)));
            Palette.Reset();
            _shell = new CommandShell(SaveGame.Restore(data), data.Pending);
        }
        catch (Exception e)
        {
            GD.PushWarning($"Couldn't load {SlotPath(slot)}: {e.Message}");
            return false;
        }
        StopReplay();
        _map.World = W;
        _map.Select();
        _selectedWard = W.HqOf(W.Player).WardId;
        _map.ResetView(W.HqOf(W.Player));
        _ticker.Clear();
        _news.Text = W.Week > 0 ? WeekReport(W.Week - 1) : "No reports yet.";
        if (_shell.Sim.WeekRunning) ResumeWeek();
        else _ticker.AppendText($"[color=#{Palette.Hex(Palette.InkQuiet)}]Back in {W.Map.StreetOf(W.HqOf(W.Player))} St. {Reports.Date(W)}. Click a business to give orders, then End week.[/color]\n");
        RefreshAll();
        return true;
    }

    /// <summary>Stop any week playing out on the map, as when another city is started or loaded.</summary>
    private void StopReplay()
    {
        _map.EndReplay();
        _playing = false;
        SetLive(false);
    }

    /// <summary>A save made partway through a week: show what had happened so far, and wait on Play to carry on.</summary>
    private void ResumeWeek()
    {
        _replayWeek = W.Week;
        _bossAtStartOfWeek = W.Player.BossHoodId;
        _ticker.AppendText($"[color=#{Palette.Hex(Palette.InkQuiet)}]Back in {W.Map.StreetOf(W.HqOf(W.Player))} St., {Reports.Date(W)}, {Reports.Hour(W.Tick)}. Press Play to carry on with the week.[/color]\n");
        foreach (var a in W.Script.Where(a => a.Kind != ActionKind.Collect))
            AppendTicker(a.Tick, a.Kind == ActionKind.Raid ? a.DefenderGangId : a.GangId, a.Text, a.DefenderGangId == Me);
        SetLive(true);
        _map.ResumeReplay(_shell.Sim);
        _clock.Text = MapView.ClockText(_map.Clock);
        _pause.Text = "Play";
    }

    private void FillGameMenu()
    {
        var menu = _gameMenu.GetPopup();
        menu.Clear();
        menu.AddItem("New small city: one district, two small gangs", 2);
        menu.AddItem("New medium city: four wards, four small gangs", 3);
        menu.AddItem("New large city: six wards, six small gangs", 4);
        menu.AddItem("New huge city: twelve wards, eleven small gangs", 5);
        menu.AddItem("New metropolis: a river city of twenty wards, twenty small gangs", 6);
        menu.AddItem("New greater city: thirty wards, thirty-four small gangs", 7);
        menu.AddSeparator();
        menu.AddCheckItem("Angled view: buildings stand up off the map", AngledMenuId);
        menu.SetItemChecked(menu.ItemCount - 1, _map.Angled);
        menu.AddSeparator();
        FillScenarioMenu(menu);
        menu.AddSeparator();
        for (int i = 1; i <= Slots; i++) menu.AddItem($"Save to slot {i}" + (SlotLabel(i) is string l ? $": {l}" : ""), 10 + i);
        menu.AddSeparator();
        for (int i = 0; i <= Slots; i++)
        {
            string? label = SlotLabel(i);
            menu.AddItem((i == 0 ? "Load autosave" : $"Load slot {i}") + (label != null ? $": {label}" : ": empty"), 20 + i);
            menu.SetItemDisabled(menu.ItemCount - 1, label == null);
        }
    }

    private void OnGameMenu(long id)
    {
        if (id is >= 2 and <= 7) { _citySize = (CitySize)(id - 2); _scenario = ScenarioKind.None; NewGame(GD.Randi()); }
        else if (id >= ScenarioMenuId && id < ScenarioMenuId + Scenarios.All.Length) StartScenario((int)id - ScenarioMenuId);
        else if (id is CampaignMenuId or NextChapterMenuId or RetryChapterMenuId) OnCampaignMenu(id);
        else if (id is > 10 and <= 10 + Slots) SaveGameTo((int)id - 10);
        else if (id is >= 20 and <= 20 + Slots) LoadGame((int)id - 20);
        else if (id == AngledMenuId) SetAngled(!_map.Angled);
    }

    private void ShowSoundPanel()
    {
        var audio = Audio.Instance;
        if (audio == null) return;
        var popup = new PopupPanel();
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
        box.AddThemeConstantOverride("separation", 6);
        HSlider Slider(string label, float value, Action<float> set)
        {
            AddLabel(box, label, Palette.InkQuiet);
            var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value };
            slider.ValueChanged += v => set((float)v);
            box.AddChild(slider);
            return slider;
        }
        Slider("Music", audio.MusicVolume, v => audio.MusicVolume = v);
        Slider("Street and effects", audio.EffectsVolume, v => { audio.EffectsVolume = v; audio.Play("click"); });
        var mute = new CheckBox { Text = "Mute everything", ButtonPressed = audio.Muted };
        mute.Toggled += on => audio.Muted = on;
        box.AddChild(mute);
        popup.AddChild(box);
        popup.PopupHide += popup.QueueFree;
        AddChild(popup);
        popup.Popup(new Rect2I((Vector2I)(_soundButton.GlobalPosition + new Vector2(-150, _soundButton.Size.Y + 4)), new Vector2I(260, 0)));
    }

    // ---- Layout ---------------------------------------------------------------

    private void BuildLayout()
    {
        var bg = new ColorRect { Color = Palette.Background };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 10);
        AddChild(margin);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        margin.AddChild(rows);

        // Top bar: who you are and how you stand.
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 18);
        _gangName = new Label();
        _gangName.AddThemeFontSizeOverride("font_size", 20);
        top.AddChild(_gangName);
        _date = new Label();
        top.AddChild(_date);
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        top.AddChild(_cash = new Label());
        top.AddChild(_heat = new Label());
        top.AddChild(_turf = new Label());
        top.AddChild(_men = new Label());
        _endWeek = new Button { Text = "End week", CustomMinimumSize = new Vector2(190, 0) };
        _endWeek.Pressed += EndWeek;
        top.AddChild(_endWeek);
        _gameMenu = new MenuButton { Text = "Game", Flat = false, TooltipText = "New city, save and load" };
        _gameMenu.GetPopup().AboutToPopup += FillGameMenu;
        _gameMenu.GetPopup().AboutToPopup += PauseForMenu;
        _gameMenu.GetPopup().IdPressed += OnGameMenu;
        top.AddChild(_gameMenu);
        _soundButton = new Button { Text = "Sound", TooltipText = "Music and effects volume" };
        _soundButton.Pressed += ShowSoundPanel;
        top.AddChild(_soundButton);
        rows.AddChild(top);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        rows.AddChild(body);

        // Far left: the crew on a rolodex.
        _rolodex = new Rolodex { SizeFlagsVertical = SizeFlags.ExpandFill };
        _rolodex.HoodPicked += id => { _map.Select(hood: id); OnMapSelection(); };
        body.AddChild(_rolodex);

        // Left: the map, the live controls and the street ticker.
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 6);
        _map = new MapView { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2.2f, CustomMinimumSize = new Vector2(540, 400) };
        LoadView();
        _map.BusinessClicked += _ => OnMapSelection();
        _map.PersonClicked += _ => OnMapSelection();
        _map.CitizenClicked += _ => OnMapSelection();
        _map.HoodClicked += _ => OnMapSelection();
        _map.BankClicked += _ => OnMapSelection();
        _map.ShopClicked += _ => OnMapSelection();
        _map.LockupClicked += _ => OnMapSelection();
        _map.ActionHappened += OnAction;
        left.AddChild(_map);

        _liveBar = new HBoxContainer { Visible = false };
        _liveBar.AddThemeConstantOverride("separation", 8);
        _clock = new Label { CustomMinimumSize = new Vector2(200, 0) };
        _clock.AddThemeFontSizeOverride("font_size", 17);
        _liveBar.AddChild(_clock);
        _pause = AddButton(_liveBar, "Pause", () => { _playing = !_playing; _pause.Text = _playing ? "Pause" : "Play"; });
        foreach (var s in Speeds)
        {
            var speed = s;
            var b = AddButton(_liveBar, $"{s}×", () => SetSpeed(speed));
            b.ToggleMode = true;
            _speedButtons.Add(b);
        }
        _liveBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        AddButton(_liveBar, "Skip to Sunday", () => _map.Advance(Content.HoursPerWeek));
        left.AddChild(_liveBar);

        var tickerPanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _ticker = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, FitContent = false };
        tickerPanel.AddChild(_ticker);
        left.AddChild(tickerPanel);
        body.AddChild(left);

        // Right: tabs for planning.
        _tabs = new TabContainer { CustomMinimumSize = new Vector2(420, 0), SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
        _businessPanel = ScrollTab("Selected");
        // The Men tab: the roster on top, crews underneath.
        var menTab = new VSplitContainer { Name = "Men" };
        _tabs.AddChild(menTab);
        _menTree = TreeTab("Men", "Name", "Age", "Int", "Mus", "Brn", "Stl", "Loy", "Crew", "Now");
        _menTree.GetParent().RemoveChild(_menTree);
        _menTree.Name = "Roster";
        _menTree.CustomMinimumSize = new Vector2(0, 200);
        _menTree.SizeFlagsVertical = SizeFlags.ExpandFill;
        _menTree.Visible = true; // the tab container hid it while it was a tab of its own
        menTab.AddChild(_menTree);
        _crewsPanel = ScrollTab("Crews");
        var crewsScroll = (Control)_crewsPanel.GetParent().GetParent();
        crewsScroll.GetParent().RemoveChild(crewsScroll);
        crewsScroll.CustomMinimumSize = new Vector2(0, 160);
        crewsScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        crewsScroll.SizeFlagsStretchRatio = 1.4f;
        crewsScroll.Visible = true;
        menTab.AddChild(crewsScroll);
        _ordersPanel = ScrollTab("Orders");
        _hallPanel = ScrollTab("City Hall");
        BuildGangsTab();
        _news = new RichTextLabel { Name = "Report", SelectionEnabled = true, BbcodeEnabled = true };
        _tabs.AddChild(_news);
        _tabs.AddChild(BuildConsole());
        body.AddChild(_tabs);
        // The map shows the wards while City Hall is open.
        _tabs.TabChanged += _ => { _map.ShowWards = _tabs.GetCurrentTabControl()?.Name == "City Hall"; };
        _map.WardClicked += ward => { _selectedWard = ward; RefreshCityHall(); };
        _map.CornerClicked += ShowCorner;

        SetSpeed(1);
    }

    private void SetSpeed(float speed)
    {
        _speed = speed;
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].ButtonPressed = Math.Abs(Speeds[i] - speed) < 0.01f;
    }

    private void SetLive(bool live)
    {
        _liveBar.Visible = live;
        _endWeek.Disabled = live;
        if (live) _endWeek.Text = "Week in progress";
        _pause.Text = "Pause";
    }

    /// <summary>The week stops while the Game menu is open, so a save catches it where you left it.</summary>
    private void PauseForMenu()
    {
        if (!_playing) return;
        _playing = false;
        _pause.Text = "Play";
    }

    private VBoxContainer ScrollTab(string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" }) pad.AddThemeConstantOverride($"margin_{side}", 8);
        pad.AddChild(box);
        scroll.AddChild(pad);
        _tabs.AddChild(scroll);
        return box;
    }

    private Tree TreeTab(string name, params string[] columns)
    {
        var tree = new Tree { Name = name, Columns = columns.Length, ColumnTitlesVisible = true, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row };
        for (int i = 0; i < columns.Length; i++)
        {
            tree.SetColumnTitle(i, columns[i]);
            tree.SetColumnExpand(i, i == 0);
            if (i > 0) tree.SetColumnCustomMinimumWidth(i, columns[i] is "Cash" ? 80 : columns[i] is "Now" ? 50 : columns[i] is "Crew" or "Wards" ? 50 : columns[i] is "You" ? 74 : 34);
            if (i > 0) tree.SetColumnTitleAlignment(i, HorizontalAlignment.Center);
        }
        _tabs.AddChild(tree);
        return tree;
    }

    private Control BuildConsole()
    {
        var box = new VBoxContainer { Name = "Console" };
        _console = new RichTextLabel { SizeFlagsVertical = SizeFlags.ExpandFill, ScrollFollowing = true, SelectionEnabled = true };
        _console.AddThemeFontOverride("normal_font", Mono());
        _console.AddText(CommandShell.Help);
        box.AddChild(_console);
        _consoleInput = new LineEdit { PlaceholderText = "Type an order, e.g. extort 2 14" };
        _consoleInput.TextSubmitted += text =>
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _consoleInput.Clear();
            _console.AddText($"\n> {text}\n");
            if (text.Trim() == "end") { EndWeek(); return; }
            _console.AddText(_shell.Execute(text) ?? "");
            if (_shell.NextCity is { } next) { _nextCity = next.World.Settings; NewGame(next.World.Settings.Seed); }
            RefreshAll();
        };
        box.AddChild(_consoleInput);
        return box;
    }

    private int TabIndex(string name)
    {
        for (int i = 0; i < _tabs.GetTabCount(); i++) if (_tabs.GetTabControl(i).Name == name) return i;
        return 0;
    }

    private static Font Mono() => new SystemFont { FontNames = new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "monospace" } };

    private static Label AddLabel(Container parent, string text, Color colour, int size = 0, bool wrap = false)
    {
        // Everything in the side panel wraps so a long shop name never widens the panel.
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(120, 0) };
        label.AddThemeColorOverride("font_color", colour);
        if (size > 0) label.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(label);
        return label;
    }

    private static Button AddButton(Container parent, string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += () => Audio.Instance?.Play("click", 0.5f, 0.1f);
        b.Pressed += onPressed;
        parent.AddChild(b);
        return b;
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); }
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 15 };
        StyleBoxFlat Box(Color c, int radius = 4, int pad = 6) => new()
        {
            BgColor = c,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad - 2, ContentMarginBottom = pad - 2,
        };
        theme.SetStylebox("panel", "PanelContainer", Box(Palette.Panel, 4, 8));
        theme.SetStylebox("panel", "TabContainer", Box(Palette.Panel, 4, 4));
        theme.SetStylebox("normal", "Button", Box(Palette.PanelRaised));
        theme.SetStylebox("hover", "Button", Box(Palette.PanelRaised.Lightened(0.12f)));
        theme.SetStylebox("pressed", "Button", Box(Palette.Player.Darkened(0.45f)));
        theme.SetStylebox("disabled", "Button", Box(Palette.Panel));
        theme.SetStylebox("normal", "OptionButton", Box(Palette.PanelRaised));
        theme.SetStylebox("hover", "OptionButton", Box(Palette.PanelRaised.Lightened(0.12f)));
        theme.SetStylebox("panel", "Tree", Box(Palette.Panel, 0, 4));
        theme.SetColor("font_color", "Label", Palette.Ink);
        theme.SetColor("font_color", "Button", Palette.Ink);
        theme.SetColor("font_color", "Tree", Palette.Ink);
        theme.SetColor("title_button_color", "Tree", Palette.InkQuiet);
        theme.SetColor("default_color", "RichTextLabel", Palette.Ink);
        theme.SetColor("font_selected_color", "TabContainer", Palette.Player);
        theme.SetFontSize("font_size", "TabContainer", 13);
        theme.SetColor("font_unselected_color", "TabContainer", Palette.InkQuiet);
        return theme;
    }

    // ---- Screenshots for development -----------------------------------------
    // godot --path godot -- --seed 7 --weeks 6 --plan --select 12 --tab Selected --live 40 --shot out.png

    private async void RunCommandLine()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool Flag(string name) => Array.IndexOf(args, name) >= 0;
        if (args.Length == 0) return;

        if (Arg("--size") is string size) _citySize = Enum.Parse<CitySize>(size, true);
        if (Arg("--scenario") is string scenario) _scenario = Enum.Parse<ScenarioKind>(scenario, true);
        if (Arg("--campaign") is string chapter)
        {
            // A campaign at this chapter, every chapter before it won.
            var settings = Campaign.Start(ulong.Parse(Arg("--seed") ?? "1"));
            for (int i = 0; i < int.Parse(chapter); i++)
            {
                var before = Simulation.New(settings).World;
                before.Goal.Outcome = ScenarioOutcome.Won;
                settings = Campaign.Next(before, settings.Seed + 1);
            }
            _nextCity = settings;
        }
        if (Arg("--seed") is string seed) NewGame(ulong.Parse(seed));
        if (Arg("--load") is string slot) GD.Print(LoadGame(int.Parse(slot)) ? $"Loaded {SlotPath(int.Parse(slot))}" : "Load failed");
        int weeks = int.Parse(Arg("--weeks") ?? "0");
        if (weeks > 0 && IsInstanceValid(_briefing)) _briefing!.Hide();
        for (int i = 0; i < weeks; i++) { _replayWeek = W.Week; _shell.Execute("auto"); _shell.EndWeek(); _news.Text = WeekReport(_replayWeek); }
        if (Arg("--repeal") is string repeal) SetUpRepeal(repeal);
        if (Flag("--untouchables")) SetUpUntouchables();
        if (Flag("--rumrow")) SetUpRumRow();
        if (Flag("--brewery")) SetUpBrewery();
        if (Flag("--fixing")) SetUpFixing();
        if (Flag("--kidnapping")) SetUpKidnapping();
        if (Flag("--nightclub")) SetUpNightclub();
        if (Flag("--contracts")) SetUpContracts();
        if (Flag("--funeral")) SetUpFuneral();
        if (Flag("--reputation"))
        {
            // A boss the papers have started writing about, with a soup kitchen.
            W.Player.Fear = 46;
            W.Player.Respect = 22;
            W.Player.Cash = Math.Max(W.Player.Cash, 5000);
            W.Player.SoupKitchen = true;
            RefreshAll();
        }
        if (Arg("--midweek") is string hour)
        {
            // Play the week to this hour, save it to slot 3 and load it back, to show a week picked up partway.
            _shell.Execute("auto");
            EndWeek();
            _map.Advance(float.Parse(hour));
            SaveGameTo(3);
            GD.Print(LoadGame(3) ? "Saved and loaded mid-week" : "Mid-week load failed");
        }
        if (Flag("--lam"))
        {
            // One man out on bail who then skipped town, one lying low, and one home with a warrant out.
            W.Player.Cash = Math.Max(W.Player.Cash, 8000);
            var men = W.AvailableHoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId).ToList();
            _shell.Sim.Jail(men[0], 60, "with assault");
            W.PostBail(W.Player, men[0]);
            W.SkipTown(W.Player, men[0]);
            W.ComeHome(W.Player, men[0]);
            _shell.Sim.Jail(men[1], 80, "with the hold-up at the Union Trust", strength: 0.8);
            W.PostBail(W.Player, men[1]);
            W.SkipTown(W.Player, men[1]);
            men[1].AwaySinceWeek -= 6;
            men[1].Case!.Strength -= 0.06;
            W.SkipTown(W.Player, men[2]);
            _map.Select(hood: men[1].Id);
            RefreshAll();
        }
        if (Flag("--verdict"))
        {
            if (IsInstanceValid(_briefing)) _briefing!.Hide();
            W.Goal.Outcome = ScenarioOutcome.Won;
            W.Goal.Week = _replayWeek = W.Week;
            AnnounceVerdict();
        }
        if (Flag("--inside"))
        {
            // The boss goes away for a year and a half, and the week runs so he takes up the chair from his cell.
            var boss = W.HoodById(W.Player.BossHoodId);
            W.Player.Cash = Math.Max(W.Player.Cash, 5000);
            _shell.Sim.Jail(boss, 78, "for running a disorderly house");
            boss.Case = null;
            boss.JailWeeks = 78;
            _shell.Execute("auto");
            _shell.EndWeek();
            _news.Text = WeekReport(W.Week - 1);
        }
        if (Arg("--fronts") is string fronts)
        {
            // Buy the richest places on your turf as fronts, then let a few weeks wash money through them.
            W.Player.Cash += 30_000;
            foreach (var b in W.TurfOf(Me).Where(b => b.IsOpen).OrderByDescending(b => b.Takings).Take(int.Parse(fronts)).ToList()) W.BuyFront(W.Player, b);
            int then = int.Parse(Arg("--then") ?? "0");
            for (int i = 0; i < then; i++) { _replayWeek = W.Week; _shell.Execute("auto"); _shell.EndWeek(); _news.Text = WeekReport(_replayWeek); }
        }
        if (Flag("--trial"))
        {
            // Two of your men in the cells waiting on juries: one with a lawyer and a paid judge, one with neither yet.
            W.Player.Cash += 10_000;
            var men = W.AvailableHoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId).OrderByDescending(h => h.Strength).ToList();
            W.Retain(W.Player, Lawyer.Storefront);
            _shell.Sim.Jail(men[0], 52, $"with the hold-up at {W.Banks[0].Name}");
            W.Retain(W.Player, Lawyer.None);
            _shell.Sim.Jail(men[1], 20, "with attempted arson");
            W.Retain(W.Player, Lawyer.Storefront);
            W.PayJudge(W.Player, men[0]);
        }
        if (Flag("--talk"))
        {
            // A truce with the smallest outfit, a paid truce offered by the second biggest, and the biggest picked to talk to.
            W.Pacts.RemoveAll(p => p.Involves(Me));
            W.Offers.Clear();
            var rivals = W.Outfits.Where(g => !g.IsPlayer).OrderByDescending(W.Power).ToList();
            W.Sign(W.Player, rivals[^1], PactKind.Truce);
            W.Put(rivals[1], OfferKind.Truce, Content.TributeAsk(W.Income(rivals[1])));
            _talkTo = rivals[0].Id;
            W.AddGrudge(rivals[0].Id, Me, 30 - W.GrudgeOf(rivals[0].Id, Me));
        }
        if (Flag("--marriage"))
        {
            // Your nephew married into the second biggest outfit, picked to talk to.
            W.Pacts.RemoveAll(p => p.Involves(Me));
            W.Offers.Clear();
            var family = W.Outfits.Where(g => !g.IsPlayer).OrderByDescending(W.Power).ElementAt(1);
            W.Player.Cash = Math.Max(W.Player.Cash, 8000);
            var groom = W.Groom(W.Player) ?? W.NewRelative(W.Player);
            W.Sign(W.Player, family, PactKind.Truce).SignedWeek = W.Week - Content.MatchPactWeeks;
            W.ProposeMatch(W.Player, family);
            _talkTo = family.Id;
            RefreshAll();
        }
        if (Flag("--union"))
        {
            // You run the local of the trade with the most shops near home, and its members are out at the best one a rival protects.
            W.Pacts.RemoveAll(p => p.Involves(Me));
            var shop = W.Businesses.Where(b => b.IsOpen && b.IsProtected && b.ProtectorGangId != Me && W.LocalOf(b) is { IsDocks: false })
                .OrderBy(b => W.BlocksFromHq(W.Player, b)).ThenByDescending(b => b.Takings).First();
            var local = W.LocalOf(shop)!;
            local.OwnerGangId = Me;
            local.PresidentHoodId = W.AvailableHoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId).OrderByDescending(h => h.Intimidation).First().Id;
            W.CallStrike(W.Player, local, shop);
            local.StrikeWeek = W.Week - 2;
            _map.SelectedBusiness = shop.Id;
        }
        if (Flag("--war"))
        {
            // At war with the biggest rival for a few weeks, talking to them.
            W.Pacts.RemoveAll(p => p.Involves(Me));
            var enemy = W.Outfits.Where(g => !g.IsPlayer && W.WarOf(g.Id) == null).OrderByDescending(W.Power).First();
            W.Declare(W.Player, enemy);
            W.Wars.Single(x => x.Involves(Me)).StartWeek = W.Week - 5;
            _talkTo = enemy.Id;
        }
        if (Flag("--commission"))
        {
            // At the table, talking to the biggest rival there, with nothing between you yet.
            if (!W.Seated(Me)) W.Commission.Seats.Add(Me);
            var rival = W.Members.Where(g => !g.IsPlayer && W.WarOf(g.Id) == null).OrderByDescending(W.Power).First();
            W.Pacts.RemoveAll(p => p.Involves(Me));
            W.Commission.LastAsked.Remove(Me);
            _talkTo = rival.Id;
        }
        if (Flag("--gang-crews"))
        {
            // Talk to the outfit with the most crews, with a man of yours inside it.
            var rival = W.Outfits.Where(g => !g.IsPlayer).OrderByDescending(g => W.CrewsOf(g.Id).Count()).First();
            _talkTo = rival.Id;
            var spy = W.AvailableHoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId).OrderBy(h => h.Strength).FirstOrDefault();
            if (spy != null) { spy.State = HoodState.Inside; spy.InsideGangId = rival.Id; spy.InsideSinceWeek = W.Week; }
        }
        if (Arg("--event") is string news)
        {
            // Break the news on Sunday night, as the game would, and show the front page.
            _shell.Sim.Begin(Enum.Parse<CityEventKind>(news, true));
            foreach (var e in W.CityEvents.Where(e => e.StartWeek == W.Week + 1).ToList())
                W.CityEvents[W.CityEvents.IndexOf(e)] = new CityEvent { Kind = e.Kind, StartWeek = W.Week, UntilWeek = e.UntilWeek - 1, WardId = e.WardId };
            _news.Text = WeekReport(Math.Max(0, W.Week - 1));
            AnnounceNews();
        }
        if (Flag("--ballot"))
        {
            // Put your brightest man on the ballot where you run the most shops, and look at that ward.
            var home = W.TurfOf(Me).GroupBy(b => W.LotOf(b).WardId).OrderByDescending(x => x.Count()).First().Key;
            var man = W.AvailableHoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId && h.Id != W.Player.HeirHoodId).OrderByDescending(h => h.Brains).First();
            man.Brains = Math.Max(man.Brains, Content.CandidateBrains);
            man.Record.RemoveAll(r => r.Kind == RapKind.Convicted);
            Politics.Stand(W, W.Player, man, W.Wards[home]);
            _selectedWard = home;
        }
        if (Flag("--plan")) _shell.Execute("auto");
        if (Arg("--select-front") is string which)
            _map.SelectedBusiness = (which == "offer"
                ? W.TurfOf(Me).Where(b => !b.IsFront && b.IsOpen).OrderByDescending(b => b.Takings).First()
                : W.FrontsOf(Me).First()).Id;
        if (Arg("--select") is string sel) { _map.SelectedBusiness = int.Parse(sel); }
        if (Flag("--select-mine")) _map.SelectedBusiness = W.TurfOf(Me).First().Id;
        if (Flag("--select-row") && ShopRowNearHq() is { } row)
        {
            // The middle shop of a row picked out, yours to the west of it and a burnt-out one to the east.
            W.BusinessById(row.Shops[0]).ProtectorGangId = Me;
            var burnt = W.BusinessById(row.Shops[2]);
            burnt.ShutWeeks = 4;
            burnt.BurnedWeek = W.Week - 1;
            _map.SelectedBusiness = row.Shops[1];
            RefreshBusiness();
        }
        // A small gang to deal with: the nearest of the kind asked for, or the nearest of any.
        if (Arg("--select-corner") is string corner
            && W.SmallGangs.Where(g => corner == "any" || g.Kind.ToString().Equals(corner, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => W.Map.Distance(W.HqOf(W.Player), W.HqOf(g))).FirstOrDefault() is { } small)
            ShowCorner(small.Id);
        if (Flag("--select-court")) _map.SelectedBusiness = W.Businesses.Where(b => b.Kind == BusinessKind.Court).OrderBy(b => W.BlocksFromHq(W.Player, b)).First().Id;
        if (Flag("--select-rival"))
            _map.SelectedBusiness = W.Businesses.Where(b => b.IsProtected && b.ProtectorGangId != Me && b.IsOpen)
                .OrderByDescending(b => Simulation.CanHijack(W, W.Player, b)).ThenBy(b => W.BlocksFromHq(W.Player, b)).First().Id;
        if (Arg("--select-person") is string kind
            && W.People.Where(p => p.Kind == Enum.Parse<PersonKind>(kind, true) && (p.GangId < 0 || p.GangId == Me))
                .OrderBy(p => W.Map.Distance(W.HqOf(W.Player), W.Map.LotAt(p.LotId))).FirstOrDefault() is { } person)
            _map.Select(person: person.Id);
        // A resident near headquarters: a shopkeeper, a landlord, or anyone.
        if (Arg("--select-citizen") is string who && _map.Directory is { } directory)
        {
            var hq = W.HqOf(W.Player);
            int pick = who switch
            {
                "owner" => W.Businesses.Where(b => b.IsOpen).OrderBy(b => b.ProtectorGangId == Me ? 0 : 1).ThenBy(b => W.BlocksFromHq(W.Player, b)).Select(b => directory.OwnerOf(b.Id)).First(i => i >= 0),
                "landlord" => W.Map.Lots.OrderBy(l => W.Map.Distance(hq, l)).Select(l => directory.LandlordOf(l.Id)).First(i => i >= 0),
                "walking" => Enumerable.Range(0, directory.CountOn((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY))
                    .Select(n => directory.IdOf((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY, n)).First(i => _map.WhereIs(i) != null),
                "secret" => Enumerable.Range(0, directory.CountOn((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY))
                    .Select(n => directory.IdOf((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY, n))
                    .OrderByDescending(i => directory.SecretsOf(i).Count(s => s.Vice == Vice.Lover) * 2 + directory.SecretsOf(i).Count).First(),
                "torpedo" => directory.Population > 0 ? Enumerable.Range(0, directory.CountOn((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY))
                    .Select(n => directory.IdOf((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY, n))
                    .OrderByDescending(i => directory.CharacterOf(i).Traits.Contains(Trait.WannabeTorpedo) ? 2 : directory.CharacterOf(i).Traits.Count(t => t >= Trait.StoolPigeon)).First() : 0,
                "feud" => Enumerable.Range(0, directory.CountOn((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY))
                    .Select(n => directory.IdOf((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY, n))
                    .First(i => directory.Get(i).Age >= 25 && directory.Get(i).Ties.Count(t => t.Kin != Kin.Employer) >= 2),
                _ => directory.IdOf((hq.X - 1) / CityMap.StrideX, (hq.Y - 1) / CityMap.StrideY, 0),
            };
            _map.Select(citizen: pick);
            if (who == "feud") StageFeud(pick);
        }
        _revealCitizens = Flag("--reveal");
        if (Flag("--character-tab")) _cardTab = CardTab.Character;
        if (Flag("--grudge-tab")) _cardTab = CardTab.Grudges;
        _pinGrudgeHelp = Flag("--grudge-help");
        if (Arg("--select-bank") is string bankArg)
            _map.Select(bank: bankArg == "near" ? W.Banks.OrderBy(b => W.Map.Distance(W.HqOf(W.Player), W.Map.LotAt(b.LotId))).First().Id : int.Parse(bankArg));
        if (Flag("--select-hood")) _map.Select(hood: W.Hoods.First(h => h.IsAvailable && h.GangId != Me && W.GangById(h.GangId).Alive).Id);
        // A man of yours or another outfit's, whoever has the most bad blood about him, for the gangster's card.
        if (Arg("--select-man") is string side)
        {
            var men = W.Hoods.Where(h => h.IsActive && (side == "mine" ? h.GangId == Me : h.GangId != Me && W.GangById(h.GangId).Alive)).ToList();
            var man = men.OrderByDescending(h => W.HeldBy(Party.Hood, h.Id).Count() + W.HeldAgainst(Party.Hood, h.Id).Count()).ThenBy(h => h.Id).First();
            _map.Select(hood: man.Id);
            if (side == "mine") { RefreshAll(); _rolodex.Show(man.Id); }
        }
        if (Arg("--card") is string card) { RefreshAll(); _rolodex.Show(W.HoodsOf(Me).OrderBy(h => h.Id).ElementAt(int.Parse(card)).Id); }
        if (Flag("--sheet"))
        {
            // The man of yours with the longest record, his card turned over.
            RefreshAll();
            _rolodex.Show(W.HoodsOf(Me).OrderByDescending(h => h.Record.Count + (h.Deeds.Any ? 1 : 0)).ThenBy(h => h.Id).First().Id);
            _rolodex.ShowSheets = true;
        }
        if (Flag("--clear")) _shell.Pending.Clear();
        if (Flag("--crimes"))
        {
            // Queue a bank job and a fire, for screenshots of the new crimes.
            var men = FreeHoods().Where(h => h.Id != W.Player.BossHoodId).ToList();
            var hq = W.HqOf(W.Player);
            var bank = W.Banks.OrderBy(b => W.Map.Distance(hq, W.Map.LotAt(b.LotId))).First();
            if (men.Count >= 3) _shell.Pending.Add(new RobberyOrder(Me, men[0].Id, bank.Id, new[] { men[1].Id, men[2].Id }));
            var mark = W.Businesses.Where(b => b.IsProtected && b.ProtectorGangId != Me && b.IsOpen).OrderBy(b => W.BlocksFromHq(W.Player, b)).First();
            if (men.Count >= 4) _shell.Pending.Add(new ArsonOrder(Me, men[3].Id, mark.Id));
            if (Flag("--sure")) { foreach (var h in men) h.Brains = h.Muscle = h.Stealth = 10; }
        }
        if (Flag("--arm"))
        {
            // Kit the gang out: guns all round, cars for two of them, and armour for the boss.
            W.Player.Cash += 20_000;
            var men = W.AvailableHoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId).OrderByDescending(h => h.Strength).ToList();
            for (int i = 0; i < men.Count; i++) W.Arm(W.Player, men[i], i == 0 ? Weapon.TommyGun : i < 3 ? Weapon.Shotgun : Weapon.Pistol);
            foreach (var h in men.Take(2)) W.Drive(W.Player, h, h == men[0] ? Car.Packard : Car.ModelT);
            W.Drive(W.Player, W.HoodById(W.Player.BossHoodId), Car.Cadillac);
        }
        if (Flag("--lockup"))
        {
            // A lockup near headquarters, and a Packard for the first man out on a job, to watch it go in.
            W.Player.Cash += 2_000;
            var near = W.Lockups.Where(l => l.GangId < 0).OrderBy(l => W.FromHq(W.Player, l)).ThenBy(l => l.Id).ToList();
            foreach (var l in near.Take(3)) W.RentLockup(W.Player, l);
            if (Flag("--stocked"))
            {
                near[1].Holds = Stashed.Truck;
                near[1].Value = 480;
                near[1].OutWeek = W.Week + 2;
                near[1].Job = "the hijacking on the road to Kelly's";
            }
            if (Flag("--hijack"))
            {
                // A rival's speakeasy close by, and a sure crew to stop its beer truck.
                var hq = W.HqOf(W.Player);
                var bar = W.Businesses.Where(b => b.IsOpen && b.IsProtected && b.ProtectorGangId != Me && !W.GangById(b.ProtectorGangId).IsSmall)
                    .OrderBy(b => W.Map.Distance(hq, W.LotOf(b))).First();
                bar.Racket = RacketKind.Speakeasy;
                var crew = FreeHoods().Where(h => h.Id != W.Player.BossHoodId).Take(3).ToList();
                foreach (var h in crew) h.Muscle = h.Intimidation = h.Stealth = 10;
                _shell.Pending.Clear();
                _shell.Pending.Add(new HijackOrder(Me, crew[0].Id, bar.Id, crew.Skip(1).Select(h => h.Id).ToArray()));
                crew[0].Car = Car.Packard;
            }
            var lead = _shell.Pending.OfType<RobberyOrder>().Select(o => W.HoodById(o.HoodId)).FirstOrDefault() ?? FreeHoods().FirstOrDefault(h => h.Id != W.Player.BossHoodId);
            if (lead != null) lead.Car = Car.Packard;
            if (Flag("--select-lockup")) _map.Select(lockup: near[0].Id);
        }
        if (Arg("--select-shop") is string shopKind)
        {
            var shopUse = shopKind == "car" ? LotUse.CarDealer : LotUse.GunShop;
            var shop = W.Shops(shopUse).OrderBy(l => W.Map.Distance(W.HqOf(W.Player), l)).First();
            _map.Select(shop: shop.Id);
            _shopFor = W.AvailableHoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId).OrderByDescending(h => h.Strength).Skip(int.Parse(Arg("--for") ?? "0")).First().Id;
        }
        if (Arg("--booze") is string booze)
        {
            // Your best spot as a speakeasy on a supply line, watered; or a rival's speakeasy on one, to stop its shipment.
            W.Player.Cash += 10_000;
            if (booze == "rival")
            {
                var line = W.SupplyLines.FirstOrDefault(l => l.OwnerGangId != Me && l.OwnerGangId >= 0 && W.DropOf(l) != null);
                if (line == null)
                {
                    // Nobody has one yet: set the biggest rival up with a speakeasy and a line into it.
                    var rival = W.Outfits.Where(g => !g.IsPlayer).OrderByDescending(g => W.TurfOf(g.Id).Count()).First();
                    var bar = W.TurfOf(rival.Id).Where(b => b.IsOpen).OrderByDescending(b => Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy)).First();
                    bar.Racket = RacketKind.Speakeasy;
                    line = W.SupplyLines.OrderBy(l => l.OwnerGangId >= 0).First();
                    line.OwnerGangId = rival.Id;
                }
                _map.SelectedBusiness = W.DropOf(line)!.Id;
            }
            else
            {
                var spot = W.TurfOf(Me).Where(b => b.IsOpen && Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy)).OrderByDescending(b => b.Takings).First();
                spot.Racket = RacketKind.Speakeasy;
                var line = W.SupplyLines.FirstOrDefault(l => W.CannotBuyLine(W.Player, l) == null);
                if (line != null) W.BuyLine(W.Player, line);
                W.SetCut(W.Player, Cut.Watered);
                _map.SelectedBusiness = spot.Id;
            }
        }
        if (Flag("--rats"))
        {
            // A man in a cell talking to the DA, an informer found out, and a man inside a rival.
            var men = W.AvailableHoodsOf(Me).Where(h => h.Id != W.Player.BossHoodId).OrderBy(h => h.Strength).ToList();
            _shell.Sim.Jail(men[0], 60, "with attempted arson");
            men[0].Loyalty = 14;
            men[0].Talking = 2;
            men[1].Informer = true;
            men[1].Loyalty = 18;
            men[1].ExposedWeek = W.Week;
            var host = W.Outfits.First(g => !g.IsPlayer);
            men[2].State = HoodState.Inside;
            men[2].InsideGangId = host.Id;
            men[2].InsideSinceWeek = W.Week;
        }
        if (Arg("--exec") is string commands)
            foreach (var c in commands.Split(';')) GD.Print(_shell.Execute(c.Trim()));
        RefreshAll();
        if (Arg("--ward") is string wardArg) _selectedWard = int.Parse(wardArg);
        if (Flag("--whole-map")) _map.ResetView(null);
        RefreshCityHall();
        if (Arg("--tab") is string tab) _tabs.CurrentTab = TabIndex(tab);
        if (Arg("--save") is string saveSlot) SaveGameTo(int.Parse(saveSlot));
        if (Arg("--live") is string live)
        {
            EndWeek();
            if (Flag("--kill-boss")) W.HoodById(W.Player.BossHoodId).State = HoodState.Dead;
            _playing = false;
            _map.Advance(float.Parse(live));
            if (Flag("--send-now"))
            {
                var hood = FreeHoods().First();
                var biz = W.Businesses.Where(b => !b.IsProtected && b.IsOpen).OrderBy(b => W.BlocksFromHq(W.Player, b)).First();
                _map.SelectedBusiness = biz.Id;
                OrderNow(new ExtortOrder(Me, hood.Id, biz.Id));
            }
            if (Arg("--then") is string then) _map.Advance(float.Parse(then));
            _clock.Text = MapView.ClockText(_map.Clock);
            RefreshAll();
        }
        if (Arg("--playtest") is string playtest) { await Playtest(int.Parse(playtest), Arg("--zoom")); return; }
        if (Arg("--bench") is string frames)
        {
            // Time how long the map takes to draw, for checking big cities stay smooth.
            if (Arg("--zoom") is string z) _map.ZoomCentre(float.Parse(z));
            for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong start = Time.GetTicksUsec();
            MapView.CityDraws = 0;
            int n = int.Parse(frames);
            for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"BENCH {W.Map.BlocksX}x{W.Map.BlocksY}, {_map.Population} people: draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}, primitives {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)}, items {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)}; {(Time.GetTicksUsec() - start) / 1000.0 / n:F1} ms per frame; city recorded in {MapView.CityMillis:F1} ms, moving things {MapView.OverlayMillis:F1} ms, rows re-recorded {MapView.CityDraws} times");
            GetTree().Quit();
            return;
        }
        if (Arg("--shot") is string shot)
        {
            if (Arg("--anim") is string anim) _map.AnimFrom = float.Parse(anim);
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Flag("--ballot") && _wardHeader != null && _hallPanel.GetParent()?.GetParent() is ScrollContainer hallScroll)
            {
                hallScroll.ScrollVertical = (int)_wardHeader.Position.Y;
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (Arg("--scroll-hall") is string hallDown && _hallPanel.GetParent()?.GetParent() is ScrollContainer hallScroll2)
            {
                hallScroll2.ScrollVertical = int.Parse(hallDown);
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (Arg("--scroll-talk") is string talkDown && _talkPanel.GetParent()?.GetParent() is ScrollContainer talkScroll)
            {
                talkScroll.ScrollVertical = int.Parse(talkDown);
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (Arg("--scroll-selected") is string selDown && _businessPanel.GetParent()?.GetParent() is ScrollContainer selScroll)
            {
                selScroll.ScrollVertical = int.Parse(selDown);
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (Arg("--scroll-men") is string down && _crewsPanel.GetParent()?.GetParent() is ScrollContainer menScroll)
            {
                menScroll.ScrollVertical = int.Parse(down);
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (Arg("--focus") is string focus)
            {
                // Close in on your headquarters, the bank nearest it, or a business burned out this week.
                var hq = W.HqOf(W.Player);
                var lot = focus switch
                {
                    "bank" => W.Map.LotAt(W.Banks.OrderBy(b => W.Map.Distance(hq, W.Map.LotAt(b.LotId))).First().LotId),
                    "fire" => W.Businesses.Where(b => b.BurnedWeek >= W.Week - 1).Select(W.LotOf).FirstOrDefault() ?? hq,
                    "person" => W.PersonById(_map.SelectedPerson) is { } p ? W.Map.LotAt(p.LotId) : hq,
                    "citizen" => _map.Directory is { } d && _map.SelectedCitizen >= 0 ? W.Map.LotAt(d.Get(_map.SelectedCitizen).HomeLotId) : hq,
                    "shop" => _map.SelectedShop >= 0 ? W.Map.LotAt(_map.SelectedShop) : hq,
                    "court" => W.Businesses.Where(b => b.Kind == BusinessKind.Court).Select(W.LotOf).OrderBy(l => W.Map.Distance(hq, l)).First(),
                    "corner" => W.SmallGangs.Where(g => Arg("--select-corner") is not { } k || k == "any" || g.Kind.ToString().Equals(k, StringComparison.OrdinalIgnoreCase))
                        .Select(W.HqOf).OrderBy(l => W.Map.Distance(hq, l)).First(),
                    "speakeasy" => W.Businesses.Where(b => b.IsOpen && b.Racket == RacketKind.Speakeasy).Select(W.LotOf).OrderBy(l => W.Map.Distance(hq, l)).FirstOrDefault() ?? hq,
                    "church" => W.Map.Lots.Where(l => l.Terrain == Terrain.Church).OrderBy(l => W.Map.Distance(hq, l)).First(),
                    "raid" => W.Script.LastOrDefault(x => x.Kind == ActionKind.Raid) is { } raid ? W.Map.LotAt(raid.ToLot) : hq,
                    "row" => ShopRowNearHq() ?? hq,
                    "civic" => W.Map.Lots.Where(l => l.Terrain == Terrain.Civic).OrderBy(l => W.Map.Distance(hq, l)).FirstOrDefault() ?? hq,
                    _ when focus.StartsWith("zone:") && Enum.TryParse<Zone>(focus[5..], true, out var zone) =>
                        W.Map.Lots.Where(l => W.Map.ZoneOf(l) == zone && l.Terrain == Terrain.Land).OrderBy(l => W.Map.Distance(hq, l)).FirstOrDefault() ?? hq,
                    "garage" => W.Garages.Where(g => !g.Police).Select(g => W.Map.LotAt(g.LotId)).OrderBy(l => W.Map.Distance(hq, l)).FirstOrDefault() ?? hq,
                    _ when int.TryParse(focus, out int lotId) => W.Map.LotAt(lotId),
                    "precinct" => W.Map.Lots.Where(l => l.Use == LotUse.Precinct).OrderBy(l => W.Map.Distance(hq, l)).First(),
                    "lockup" => W.LockupsOf(Me).Select(W.LockupLot).FirstOrDefault() ?? hq,
                    "alley" => W.Map.Lots.Where(l => l.Terrain == Terrain.Land && W.Map.HasBackAlley((l.X - 1) / CityMap.StrideX, (l.Y - 1) / CityMap.StrideY))
                        .OrderBy(l => W.Map.Distance(hq, l)).ThenBy(l => l.Id).FirstOrDefault() ?? hq,
                    "river" => W.Map.Lots.Where(l => l.Terrain == Terrain.Water).OrderBy(l => W.Map.Distance(hq, l)).FirstOrDefault() ?? hq,
                    "cemetery" => W.Map.Lots.Where(l => l.Terrain == Terrain.Cemetery).OrderByDescending(l => W.Map.LotsOfBlock((l.X - 1) / CityMap.StrideX, (l.Y - 1) / CityMap.StrideY).Count(x => x.Terrain == Terrain.Cemetery)).ThenBy(l => l.Id).First(),
                    "biz" => _map.SelectedBusiness >= 0 ? W.LotOf(W.BusinessById(_map.SelectedBusiness)) : hq,
                    "job" => _shell.Sim.Upcoming.FirstOrDefault(j => j.GangId == Me && Simulation.TeamOf(j.Order).Any()) is { } job
                        ? W.Map.LotAt(Simulation.TargetLot(W, job.Order)) : hq,
                    _ => hq,
                };
                _map.ResetView(lot, float.Parse(Arg("--zoom") ?? "4"));
                // Ring the spot as a link on a card would, to see the mark.
                if (Flag("--mark")) _map.PanTo(lot);
                if (focus == "lockup" && W.LockupsOf(Me).FirstOrDefault() is { } shed)
                {
                    var (sx0, sx1) = W.Map.LockupSpots(shed.Bx, shed.By)[shed.Index];
                    _map.CentreOn(new Vector2((sx0 + sx1) / 2 + 0.6f, shed.By * CityMap.StrideY + 2));
                }
                if (Flag("--showroom")) { _map.Showroom = lot; _map.CentreOn(new Vector2(lot.X, lot.FrontY + 0.5f)); }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            else if (Arg("--zoom") is string zoom) { _map.ZoomCentre(float.Parse(zoom)); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            GetViewport().GetTexture().GetImage().SavePng(shot);
            GetTree().Quit();
        }
    }

    /// <summary>Who else keeps shop in the same building, for one of a row of shops.</summary>
    private void ShopRowLine(Business b)
    {
        var others = W.ShopsAt(W.LotOf(b)).Where(x => x.Id != b.Id).ToList();
        if (others.Count == 0) return;
        AddLabel(_businessPanel, "One of a row of shops, next to " + string.Join(" and ", others.Select(x => x.Name.Split(", ")[0])) + ".", Palette.InkQuiet, wrap: true);
    }

    /// <summary>For screenshots: the nearest row of three shops to HQ that faces the viewer, off the street the el runs over.</summary>
    private Lot? ShopRowNearHq()
    {
        var hq = W.HqOf(W.Player);
        return W.Map.Lots.Where(l => l.Shops.Count == 3 && l.FrontY > l.Y && l.FrontY != W.Map.BlocksY / 3 * CityMap.StrideY)
            .OrderBy(l => W.Map.Distance(hq, l)).ThenBy(l => l.Id).FirstOrDefault();
    }

    /// <summary>For screenshots: bad blood all round a resident, as if the city had been at it for a while.</summary>
    private void StageFeud(int id)
    {
        var d = _map.Directory!;
        var c = d.Get(id);
        int ward = W.Map.LotAt(c.HomeLotId).WardId;
        W.Introduce(id, ward, c.Ties, d.SecretsOf(id), d.CharacterOf(id), c.OwnsBusinessId);
        W.Learn(id, Fact.Name | Fact.Job | Fact.Home);
        var kin = c.Ties.Where(t => t.Kin != Kin.Employer).ToList();
        foreach (var t in kin)
        {
            W.LearnTie(id, t);
            W.Learn(t.Id, Fact.Name);
        }
        var man = W.HoodsOf(Me).First(h => h.Id != W.Player.BossHoodId);
        W.Bear(Party.Resident, id, Party.Gang, Me, 45, "shook them down", ward, known: true);
        W.Bear(Party.Resident, id, Party.Hood, man.Id, 100, "shook them down", ward, known: true);
        if (W.BeatCopOf(ward) is { } cop) W.Bear(Party.Resident, id, Party.Cop, cop.Id, 25, "took in", ward, known: true, aboutId: kin[0].Id, aboutKin: Content.Reverse(kin[0].Kin));
        W.Bear(Party.Resident, kin[1].Id, Party.Resident, id, 10, "fell out with them over their mother's will", ward, known: true);
    }
}
