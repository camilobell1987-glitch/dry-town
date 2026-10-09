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
    private const float HoursPerSecond = 8f;

    private CommandShell _shell = null!;
    private World W => _shell.Sim.World;
    private int Me => W.Player.Id;

    private MapView _map = null!;
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
    private CitySize _citySize = CitySize.Large;
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
        Palette.Reset();
        _shell = new CommandShell(Simulation.New(new WorldSettings { Seed = seed, Size = _citySize }));
        _map.World = W;
        _map.SelectedBusiness = -1;
        _selectedWard = W.HqOf(W.Player).WardId;
        _map.ResetView(W.HqOf(W.Player));
        _ticker.Clear();
        _ticker.AppendText($"[color=#{Palette.Hex(Palette.InkQuiet)}]You run {W.Player.Name} out of a back room on {W.Map.StreetOf(W.HqOf(W.Player))} St. " +
                           "Click a business on the map to give orders, or press Plan for me. Then End week.[/color]\n");
        _news.Text = "No reports yet. Your first week starts Monday.";
        RefreshAll();
    }

    private int _bossAtStartOfWeek = -1;

    private void EndWeek()
    {
        if (_map.Live || !W.Player.Alive) return;
        _bossAtStartOfWeek = W.Player.BossHoodId;
        var snapshot = W.Businesses.ToDictionary(b => b.Id, b => (b.ProtectorGangId, b.Racket));
        _replayWeek = W.Week;
        _ticker.Clear();
        _shell.StartWeek();
        SetLive(true);
        _map.BeginReplay(snapshot, _shell.Sim);
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
        foreach (var e in W.Events.Where(e => e.Week == _replayWeek && e.Tick >= Content.ReckoningTick && Reports.IsHeadline(e) && !shown.Contains(e.Text)))
            AppendTicker(e.Tick, e.GangId, e.Text);
        _news.Text = WeekReport(_replayWeek);
        _tabs.CurrentTab = TabIndex("Report");
        Audio.Instance?.Play("paper");
        SaveGameTo(0);
        AnnounceNewBoss();
        RefreshAll();
    }

    public override void _Process(double delta)
    {
        if (!_map.Live) return;
        if (_playing) _map.Advance((float)delta * HoursPerSecond * _speed);
        _clock.Text = MapView.ClockText(_map.Clock);
        if (_map.ReplayFinished) FinishReplay();
    }

    private void OnAction(ScriptAction a)
    {
        PlaySound(a);
        if (a.BusinessId >= 0 && a.BusinessId == _map.SelectedBusiness) RefreshBusiness();
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
            var net = l.Net >= 0 ? Palette.Good : Palette.Bad;
            sb.Append($"[b]Net [color={C(net)}]{(l.Net >= 0 ? "+" : "-")}${Math.Abs(l.Net):N0}[/color][/b]   Cash ${p.Cash:N0} · Heat {p.Heat}\n\n");
        }
        else if (!p.Alive) sb.Append($"[color={C(Palette.Bad)}]Your outfit is finished. The city goes on without you.[/color]\n\n");

        sb.Append($"[color={C(Palette.InkQuiet)}]HEADLINES[/color]\n");
        foreach (var e in W.Events.Where(e => e.Week == week && Reports.IsHeadline(e)).OrderBy(e => e.Tick))
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
            _date.Text += $" · {boss.Name}, {boss.Age(W.Week)}";
        }
        _cash.Text = $"${p.Cash:N0}";
        _heat.Text = $"Heat {p.Heat}";
        _heat.AddThemeColorOverride("font_color", p.Heat > 60 ? Palette.Bad : p.Heat > 35 ? Palette.Player : Palette.Ink);
        _turf.Text = $"Turf {W.TurfOf(Me).Count()}/{W.Businesses.Count}";
        _men.Text = $"Men {W.HoodsOf(Me).Count()}";
        if (_map.Live) _endWeek.Text = "Week in progress";
        else _endWeek.Text = _shell.Pending.Count > 0 ? $"End week  ({_shell.Pending.Count} order{(_shell.Pending.Count == 1 ? "" : "s")})" : "End week";
        _endWeek.Disabled = !p.Alive || _map.Live;

        RefreshBusiness();
        RefreshMen();
        RefreshOrders();
        RefreshCrews();
        RefreshGangs();
        RefreshCityHall();
        _map.QueueRedraw();
    }

    private void RefreshBusiness()
    {
        Clear(_businessPanel);
        int id = _map.SelectedBusiness;
        if (id < 0)
        {
            AddLabel(_businessPanel, "Click a business on the map.", Palette.InkQuiet);
            AddLabel(_businessPanel, "A shop's awning is striped in the colours of the gang it pays; yours are brass. Plain awnings pay nobody yet. A red badge on the roof means a racket in the back room.", Palette.InkQuiet, wrap: true);
            AddLabel(_businessPanel, "Scroll to zoom the map. Drag with the right mouse button to move it.", Palette.InkQuiet, wrap: true);
            return;
        }

        var b = W.BusinessById(id);
        var lot = W.LotOf(b);
        AddLabel(_businessPanel, b.Name, Palette.Ink, 18);
        AddLabel(_businessPanel, $"{Content.Label(b.Kind)} · takes ${b.Takings} a week · owner toughness {b.Toughness}/10", Palette.InkQuiet);
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
            AddLabel(_businessPanel, $"Pays {g.Name}: {b.ProtectionRate}% (${b.Takings * b.ProtectionRate / 100}/week)", Palette.Gang(W, g.Id));
            var handler = W.Hoods.FirstOrDefault(h => h.Id == b.HandlerHoodId);
            if (handler != null) AddLabel(_businessPanel, $"Handled by {handler.Name}", Palette.InkQuiet);
        }
        else AddLabel(_businessPanel, "Pays nobody.", Palette.InkQuiet);
        AddLabel(_businessPanel, $"Resentment {b.Resentment}/100" + (b.Resentment > 60 ? " · may talk to the police" : ""), b.Resentment > 60 ? Palette.Bad : Palette.InkQuiet);
        if (b.Racket != RacketKind.None) AddLabel(_businessPanel, $"Back room: {Content.Rackets[b.Racket].Label}", Palette.Bad);
        if (!b.IsOpen) AddLabel(_businessPanel, $"Closed by the police for {b.ShutWeeks} more weeks.", Palette.Bad);

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
        if (b.ProtectorGangId == Me) OwnBusinessActions(b);
        else if (b.IsOpen) TargetActions(b);
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
        string Odds(Hood h, IReadOnlyList<Hood> backup)
        {
            if (!rival) return $"{Simulation.ExtortChance(W, W.Player, h, b, backup.Count):P0} chance";
            double defence = Simulation.DefenceStrength(W, W.GangById(b.ProtectorGangId), b);
            return $"{Simulation.TakeoverChance(h.Strength + Simulation.BackupStrength(backup), defence):P0} to win if unguarded";
        }

        AddLabel(_businessPanel, rival ? $"Take it from {W.GangById(b.ProtectorGangId).Name}. Expect a fight; men can die." : "Lean on the owner for protection money.", Palette.Ink, wrap: true);
        string verb = _map.Live ? "now" : "";
        if (hoods.Count > 0)
        {
            var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            foreach (var h in hoods) pick.AddItem($"{h.Name} · {Odds(h, Array.Empty<Hood>())}", h.Id);
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

        AddLabel(_businessPanel, "Protection rate", Palette.Ink);
        var rateRow = new HBoxContainer();
        var rate = new SpinBox { MinValue = 5, MaxValue = 30, Value = b.ProtectionRate, Suffix = "%" };
        rateRow.AddChild(rate);
        AddButton(rateRow, "Set rate", () => Queue(new SetRateOrder(Me, b.Id, (int)rate.Value)));
        _businessPanel.AddChild(rateRow);
        AddLabel(_businessPanel, "Above 12% the owner grows resentful; below it he warms to you.", Palette.InkQuiet, wrap: true);

        if (hoods.Count == 0 && crews.Count == 0) { AddLabel(_businessPanel, "All your men have jobs this week. Cancel one on the Orders tab to free him.", Palette.InkQuiet, wrap: true); return; }

        var rackets = Content.RacketsFor(b.Kind)
            .Select(k => Content.Rackets[k])
            .Where(r => !r.NeedsProhibition || W.Prohibition)
            .ToList();
        if (b.Racket == RacketKind.None && rackets.Count > 0 && hoods.Count > 0)
        {
            _businessPanel.AddChild(new HSeparator());
            AddLabel(_businessPanel, "Open a racket in the back room", Palette.Ink);
            var racketPick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            foreach (var r in rackets) racketPick.AddItem($"{r.Label} · costs ${r.SetupCost}, pays ~${r.WeeklyIncome}/week, +{r.WeeklyHeat} heat", (int)r.Kind);
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

    private OptionButton HoodPicker(List<Hood> hoods, Func<Hood, string> note)
    {
        var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in hoods) pick.AddItem($"{h.Name} · {note(h)}", h.Id);
        _businessPanel.AddChild(pick);
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
            string state = h.Id == W.Player.BossHoodId ? "Boss" : h.State == HoodState.Jailed ? $"Jail {h.JailWeeks}w" : busy.Contains(h.Id) ? "Job" : "Free";
            if (h.Id == W.Player.HeirHoodId) state = h.State == HoodState.Jailed ? state : "Heir";
            var crew = W.CrewOfHood(h.Id);
            string crewText = crew == null ? "" : crew.LieutenantHoodId == h.Id ? "Lt" : World.Surname(W.HoodById(crew.LieutenantHoodId).Name);
            string[] cols = { h.Name, $"{h.Age(W.Week)}", $"{h.Intimidation}", $"{h.Muscle}", $"{h.Brains}", $"{h.Stealth}", $"{h.Loyalty}", crewText, state };
            for (int i = 0; i < cols.Length; i++) it.SetText(i, cols[i]);
            string tip = $"{h.Name}, {h.Age(W.Week)}" + (h.Family ? ", family" : "") + $"\nIntimidation {h.Intimidation}, Muscle {h.Muscle}, Brains {h.Brains}, Stealth {h.Stealth}\nLoyalty {h.Loyalty}/100, ambition {h.Ambition}/100, wage ${h.Wage}/week" + (job != null ? $"\nThis week: {job}" : "");
            for (int i = 0; i < cols.Length; i++) it.SetTooltipText(i, tip);
            if (h.Loyalty < 30) it.SetCustomColor(6, Palette.Bad);
            if (h.Age(W.Week) >= 65) it.SetCustomColor(1, Palette.Bad);
            if (h.Family) it.SetCustomColor(0, Palette.Player);
            if (h.State == HoodState.Jailed) it.SetCustomColor(8, Palette.Police);
            else if (busy.Contains(h.Id)) it.SetCustomColor(8, Palette.Player);
        }
    }

    private void RefreshOrders()
    {
        Clear(_ordersPanel);
        if (_map.Live)
        {
            AddLabel(_ordersPanel, "The week is under way. Your men still to do their jobs:", Palette.InkQuiet, wrap: true);
            foreach (var job in _shell.Sim.Upcoming.Where(j => j.GangId == Me))
                AddLabel(_ordersPanel, $"{MapView.ClockText(job.Tick)}: {_shell.Describe(job.Order)}", Palette.Ink, wrap: true);
            AddLabel(_ordersPanel, "To send more men, pause and click a business on the map.", Palette.InkQuiet, wrap: true);
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

        if (!_map.Live)
        {
            bool queued = _shell.Pending.Any(o => o is RecruitOrder { Family: true });
            var family = AddButton(_crewsPanel, queued ? "Family arrives this week" : $"Bring a son or nephew into the business (${Content.FamilyCost})",
                () => { _shell.Execute("family"); RefreshAll(); });
            family.Disabled = queued || !Simulation.CanBringInFamily(W, p);
            family.TooltipText = "Family starts young and green, but loyal, and grows into the job. Once a year.";
        }
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
            string[] cols = { g.IsPlayer ? $"{g.Name} (you)" : g.Name, $"{W.TurfOf(g.Id).Count()}", $"{W.HoodsOf(g.Id).Count()}", $"{wards}", $"${g.Cash:N0}", $"{g.Heat}" };
            string tip = $"{g.Name}, run by {boss.Name}" + (wards > 0 ? $"\nAldermen on the payroll: {string.Join(", ", W.Wards.Where(x => x.OwnerGangId == g.Id).Select(x => x.Name))}" : "")
                + (W.Hall.FriendGangId == g.Id ? $"\nMayor {W.Hall.Mayor} owes them" : "");
            for (int i = 0; i < cols.Length; i++) { it.SetText(i, cols[i]); it.SetTooltipText(i, tip); }
            it.SetCustomColor(0, Palette.Gang(W, g.Id));
        }
    }

    // ---- Saving ---------------------------------------------------------------

    public override void _Notification(int what)
    {
        // Quitting while planning keeps this week's orders for next time.
        if (what == NotificationWMCloseRequest && _shell != null && !_map.Live && W.Player.Alive && OS.GetCmdlineUserArgs().Length == 0) SaveGameTo(0);
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
        if (_map.Live) return;
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
        _map.EndReplay();
        _map.World = W;
        _map.SelectedBusiness = -1;
        _selectedWard = W.HqOf(W.Player).WardId;
        _map.ResetView(W.HqOf(W.Player));
        _ticker.Clear();
        _ticker.AppendText($"[color=#{Palette.Hex(Palette.InkQuiet)}]Back in {W.Map.StreetOf(W.HqOf(W.Player))} St. {Reports.Date(W)}. Click a business to give orders, then End week.[/color]\n");
        _news.Text = W.Week > 0 ? WeekReport(W.Week - 1) : "No reports yet.";
        RefreshAll();
        return true;
    }

    private void FillGameMenu()
    {
        var menu = _gameMenu.GetPopup();
        menu.Clear();
        menu.AddItem("New small city: one district, three outfits", 2);
        menu.AddItem("New medium city: four wards, four outfits", 3);
        menu.AddItem("New large city: six wards, five outfits", 4);
        menu.AddItem("New huge city: twelve wards, seven outfits", 5);
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
        if (id is >= 2 and <= 5) { _citySize = (CitySize)(id - 2); NewGame(GD.Randi()); }
        else if (id is > 10 and <= 10 + Slots) SaveGameTo((int)id - 10);
        else if (id is >= 20 and <= 20 + Slots) LoadGame((int)id - 20);
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
        _gameMenu.GetPopup().IdPressed += OnGameMenu;
        top.AddChild(_gameMenu);
        _soundButton = new Button { Text = "Sound", TooltipText = "Music and effects volume" };
        _soundButton.Pressed += ShowSoundPanel;
        top.AddChild(_soundButton);
        rows.AddChild(top);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        rows.AddChild(body);

        // Left: the map, the live controls and the street ticker.
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 6);
        _map = new MapView { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2.2f, CustomMinimumSize = new Vector2(640, 400) };
        _map.BusinessClicked += _ =>
        {
            // Clicking a shop mid-week stops the clock so you can decide what to do.
            if (_map.Live && _playing) { _playing = false; _pause.Text = "Play"; }
            _tabs.CurrentTab = TabIndex("Business");
            RefreshBusiness();
        };
        _map.ActionHappened += OnAction;
        left.AddChild(_map);

        _liveBar = new HBoxContainer { Visible = false };
        _liveBar.AddThemeConstantOverride("separation", 8);
        _clock = new Label { CustomMinimumSize = new Vector2(170, 0) };
        _clock.AddThemeFontSizeOverride("font_size", 17);
        _liveBar.AddChild(_clock);
        _pause = AddButton(_liveBar, "Pause", () => { _playing = !_playing; _pause.Text = _playing ? "Pause" : "Play"; });
        foreach (var s in new[] { 1f, 2f, 4f, 8f })
        {
            var speed = s;
            var b = AddButton(_liveBar, $"{s}×", () => SetSpeed(speed));
            b.ToggleMode = true;
            _speedButtons.Add(b);
        }
        _liveBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        AddButton(_liveBar, "Skip to Sunday report", () => _map.Advance(Content.HoursPerWeek));
        left.AddChild(_liveBar);

        var tickerPanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _ticker = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, FitContent = false };
        tickerPanel.AddChild(_ticker);
        left.AddChild(tickerPanel);
        body.AddChild(left);

        // Right: tabs for planning.
        _tabs = new TabContainer { CustomMinimumSize = new Vector2(420, 0), SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
        _businessPanel = ScrollTab("Business");
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
        _gangsTree = TreeTab("Gangs", "Gang", "Turf", "Men", "Wards", "Cash", "Heat");
        _news = new RichTextLabel { Name = "Report", SelectionEnabled = true, BbcodeEnabled = true };
        _tabs.AddChild(_news);
        _tabs.AddChild(BuildConsole());
        body.AddChild(_tabs);
        // The map shows the wards while City Hall is open.
        _tabs.TabChanged += _ => { _map.ShowWards = _tabs.GetCurrentTabControl()?.Name == "City Hall"; };
        _map.WardClicked += ward => { _selectedWard = ward; RefreshCityHall(); };

        SetSpeed(1);
    }

    private void SetSpeed(float speed)
    {
        _speed = speed;
        float[] speeds = { 1, 2, 4, 8 };
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].ButtonPressed = Math.Abs(speeds[i] - speed) < 0.01f;
    }

    private void SetLive(bool live)
    {
        _liveBar.Visible = live;
        _endWeek.Disabled = live;
        if (live) _endWeek.Text = "Week in progress";
        _pause.Text = "Pause";
        _gameMenu.Disabled = live;
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
            if (i > 0) tree.SetColumnCustomMinimumWidth(i, columns[i] is "Cash" ? 80 : columns[i] is "Now" ? 50 : columns[i] is "Crew" or "Wards" ? 50 : 34);
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
    // godot --path godot -- --seed 7 --weeks 6 --plan --select 12 --tab Business --live 40 --shot out.png

    private async void RunCommandLine()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool Flag(string name) => Array.IndexOf(args, name) >= 0;
        if (args.Length == 0) return;

        if (Arg("--size") is string size) _citySize = Enum.Parse<CitySize>(size, true);
        if (Arg("--seed") is string seed) NewGame(ulong.Parse(seed));
        if (Arg("--load") is string slot) GD.Print(LoadGame(int.Parse(slot)) ? $"Loaded {SlotPath(int.Parse(slot))}" : "Load failed");
        int weeks = int.Parse(Arg("--weeks") ?? "0");
        for (int i = 0; i < weeks; i++) { _replayWeek = W.Week; _shell.Execute("auto"); _shell.EndWeek(); _news.Text = WeekReport(_replayWeek); }
        if (Flag("--plan")) _shell.Execute("auto");
        if (Arg("--select") is string sel) { _map.SelectedBusiness = int.Parse(sel); }
        if (Flag("--select-mine")) _map.SelectedBusiness = W.TurfOf(Me).First().Id;
        if (Flag("--select-rival")) _map.SelectedBusiness = W.Businesses.First(b => b.IsProtected && b.ProtectorGangId != Me).Id;
        if (Flag("--clear")) _shell.Pending.Clear();
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
        if (Arg("--bench") is string frames)
        {
            // Time how long the map takes to draw, for checking big cities stay smooth.
            if (Arg("--zoom") is string z) _map.ZoomCentre(float.Parse(z));
            for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong start = Time.GetTicksUsec();
            MapView.CityDraws = 0;
            int n = int.Parse(frames);
            for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"BENCH {W.Map.BlocksX}x{W.Map.BlocksY}: {(Time.GetTicksUsec() - start) / 1000.0 / n:F1} ms per frame; city {MapView.CityMillis:F1} ms, overlay {MapView.OverlayMillis:F1} ms, city drawn {MapView.CityDraws} times");
            GetTree().Quit();
            return;
        }
        if (Arg("--shot") is string shot)
        {
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Arg("--zoom") is string zoom) { _map.ZoomCentre(float.Parse(zoom)); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            GetViewport().GetTexture().GetImage().SavePng(shot);
            GetTree().Quit();
        }
    }
}
