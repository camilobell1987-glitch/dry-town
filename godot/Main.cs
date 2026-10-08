using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// Phase 2 screen. Plan the week on the map and the side panel, press End week, and watch
/// the week play out on the streets before the Sunday report.
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
    private VBoxContainer _businessPanel = null!, _ordersPanel = null!;
    private Tree _menTree = null!, _gangsTree = null!;
    private RichTextLabel _news = null!, _console = null!;
    private LineEdit _consoleInput = null!;

    private bool _playing;
    private float _speed = 1;
    private int _replayWeek;

    public override void _Ready()
    {
        Theme = BuildTheme();
        BuildLayout();
        NewGame(1920UL + (ulong)GD.Randi() % 100000);
        RunCommandLine();
    }

    // ---- Game flow ------------------------------------------------------------

    private void NewGame(ulong seed)
    {
        Palette.Reset();
        _shell = new CommandShell(Simulation.New(new WorldSettings { Seed = seed }));
        _map.World = W;
        _map.SelectedBusiness = -1;
        _ticker.Clear();
        _ticker.AppendText($"[color=#{Palette.Hex(Palette.InkQuiet)}]You run {W.Player.Name} out of a back room on {W.Map.StreetOf(W.HqOf(W.Player))} St. " +
                           "Click a business on the map to give orders, or press Plan for me. Then End week.[/color]\n");
        _news.Text = "No reports yet. Your first week starts Monday.";
        RefreshAll();
    }

    private void EndWeek()
    {
        if (_map.Live) return;
        var snapshot = W.Businesses.ToDictionary(b => b.Id, b => (b.ProtectorGangId, b.Racket));
        _replayWeek = W.Week;
        _shell.EndWeek();
        _ticker.Clear();
        _map.BeginReplay(snapshot);
        _playing = true;
        SetLive(true);
    }

    private void FinishReplay()
    {
        _map.EndReplay();
        _playing = false;
        SetLive(false);
        foreach (var e in W.Events.Where(e => e.Week == _replayWeek && e.Tick >= Content.ReckoningTick && Reports.IsHeadline(e)))
            AppendTicker(e.Tick, e.GangId, e.Text);
        _news.Text = WeekReport(_replayWeek);
        _tabs.CurrentTab = TabIndex("Report");
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

    private static int HoodOf(Order o) => o switch
    {
        ExtortOrder e => e.HoodId,
        RacketOrder r => r.HoodId,
        GuardOrder g => g.HoodId,
        _ => -1,
    };

    private static int BusinessOf(Order o) => o switch
    {
        ExtortOrder e => e.BusinessId,
        RacketOrder r => r.BusinessId,
        GuardOrder g => g.BusinessId,
        SetRateOrder s => s.BusinessId,
        _ => -1,
    };

    private List<Hood> FreeHoods()
    {
        var busy = _shell.Pending.Select(HoodOf).ToHashSet();
        return W.AvailableHoodsOf(Me).Where(h => !busy.Contains(h.Id)).OrderByDescending(h => h.Strength).ToList();
    }

    // ---- Refresh --------------------------------------------------------------

    private void RefreshAll()
    {
        var p = W.Player;
        _gangName.Text = p.Alive ? p.Name : $"{p.Name} (finished)";
        _gangName.AddThemeColorOverride("font_color", Palette.Player);
        _date.Text = Reports.Date(W) + (W.Prohibition ? "" : " · after repeal");
        _cash.Text = $"${p.Cash:N0}";
        _heat.Text = $"Heat {p.Heat}";
        _heat.AddThemeColorOverride("font_color", p.Heat > 60 ? Palette.Bad : p.Heat > 35 ? Palette.Player : Palette.Ink);
        _turf.Text = $"Turf {W.TurfOf(Me).Count()}/{W.Businesses.Count}";
        _men.Text = $"Men {W.HoodsOf(Me).Count()}";
        _endWeek.Text = _shell.Pending.Count > 0 ? $"End week  ({_shell.Pending.Count} orders)" : "End week";
        _endWeek.Disabled = !p.Alive;

        RefreshBusiness();
        RefreshMen();
        RefreshOrders();
        RefreshGangs();
        _map.QueueRedraw();
    }

    private void RefreshBusiness()
    {
        Clear(_businessPanel);
        int id = _map.SelectedBusiness;
        if (id < 0)
        {
            AddLabel(_businessPanel, "Click a business on the map.", Palette.InkQuiet);
            AddLabel(_businessPanel, "Your turf is outlined in brass. Rival gangs have their own colours. Grey shops pay nobody yet. A red dot means a racket in the back room.", Palette.InkQuiet, wrap: true);
            return;
        }

        var b = W.BusinessById(id);
        var lot = W.LotOf(b);
        AddLabel(_businessPanel, b.Name, Palette.Ink, 18);
        AddLabel(_businessPanel, $"{Content.Label(b.Kind)} · takes ${b.Takings} a week · owner toughness {b.Toughness}/10", Palette.InkQuiet);
        AddLabel(_businessPanel, $"{W.BlocksFromHq(W.Player, b):0.#} blocks from your HQ", Palette.InkQuiet);

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

        foreach (var o in _shell.Pending.Where(o => BusinessOf(o) == id))
            AddLabel(_businessPanel, "Queued: " + _shell.Describe(o), Palette.Player, wrap: true);

        _businessPanel.AddChild(new HSeparator());
        if (!W.Player.Alive) return;

        if (b.ProtectorGangId == Me) OwnBusinessActions(b);
        else if (b.IsOpen) TargetActions(b);
    }

    private void TargetActions(Business b)
    {
        var hoods = FreeHoods().Where(h => h.Id != W.Player.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3).ToList();
        if (hoods.Count == 0) { AddLabel(_businessPanel, "All your men have jobs this week. Cancel one on the Orders tab to free him.", Palette.InkQuiet, wrap: true); return; }

        bool rival = b.IsProtected;
        string Odds(Hood h)
        {
            if (!rival) return $"{Simulation.ExtortChance(W, W.Player, h, b):P0} chance";
            double defence = Simulation.DefenceStrength(W, W.GangById(b.ProtectorGangId), b);
            return $"{Simulation.TakeoverChance(h.Strength, defence):P0} to win if unguarded";
        }

        AddLabel(_businessPanel, rival ? $"Take it from {W.GangById(b.ProtectorGangId).Name}. Expect a fight; men can die." : "Lean on the owner for protection money.", Palette.Ink, wrap: true);
        var pick = new OptionButton();
        foreach (var h in hoods) pick.AddItem($"{h.Name} · {Odds(h)}", h.Id);
        _businessPanel.AddChild(pick);
        AddButton(_businessPanel, rival ? "Send him to take it" : "Send him", () => Queue(new ExtortOrder(Me, pick.GetSelectedId(), b.Id)));
    }

    private void OwnBusinessActions(Business b)
    {
        var hoods = FreeHoods();

        AddLabel(_businessPanel, "Protection rate", Palette.Ink);
        var rateRow = new HBoxContainer();
        var rate = new SpinBox { MinValue = 5, MaxValue = 30, Value = b.ProtectionRate, Suffix = "%" };
        rateRow.AddChild(rate);
        AddButton(rateRow, "Set rate", () => Queue(new SetRateOrder(Me, b.Id, (int)rate.Value)));
        _businessPanel.AddChild(rateRow);
        AddLabel(_businessPanel, "Above 12% the owner grows resentful; below it he warms to you.", Palette.InkQuiet, wrap: true);

        if (hoods.Count == 0) { AddLabel(_businessPanel, "All your men have jobs this week. Cancel one on the Orders tab to free him.", Palette.InkQuiet, wrap: true); return; }

        var rackets = Content.RacketsFor(b.Kind)
            .Select(k => Content.Rackets[k])
            .Where(r => !r.NeedsProhibition || W.Prohibition)
            .ToList();
        if (b.Racket == RacketKind.None && rackets.Count > 0)
        {
            _businessPanel.AddChild(new HSeparator());
            AddLabel(_businessPanel, "Open a racket in the back room", Palette.Ink);
            var racketPick = new OptionButton();
            foreach (var r in rackets) racketPick.AddItem($"{r.Label} · costs ${r.SetupCost}, pays ~${r.WeeklyIncome}/week, +{r.WeeklyHeat} heat", (int)r.Kind);
            _businessPanel.AddChild(racketPick);
            var who = HoodPicker(hoods, h => $"brains {h.Brains}");
            AddButton(_businessPanel, "Open it", () => Queue(new RacketOrder(Me, who.GetSelectedId(), b.Id, (RacketKind)racketPick.GetSelectedId())));
        }

        _businessPanel.AddChild(new HSeparator());
        AddLabel(_businessPanel, "Post a guard for the week", Palette.Ink);
        var guard = HoodPicker(hoods, h => $"strength {h.Strength}");
        AddButton(_businessPanel, "Post him", () => Queue(new GuardOrder(Me, guard.GetSelectedId(), b.Id)));
    }

    private OptionButton HoodPicker(List<Hood> hoods, Func<Hood, string> note)
    {
        var pick = new OptionButton();
        foreach (var h in hoods) pick.AddItem($"{h.Name} · {note(h)}", h.Id);
        _businessPanel.AddChild(pick);
        return pick;
    }

    private void RefreshMen()
    {
        _menTree.Clear();
        var root = _menTree.CreateItem();
        var jobs = _shell.Pending.Where(o => HoodOf(o) >= 0).ToDictionary(HoodOf, o => _shell.Describe(o));
        foreach (var h in W.HoodsOf(Me).OrderBy(h => h.Id))
        {
            var it = _menTree.CreateItem(root);
            jobs.TryGetValue(h.Id, out var job);
            string state = h.Id == W.Player.BossHoodId ? "Boss" : h.State == HoodState.Jailed ? $"Jail {h.JailWeeks}w" : job != null ? "Job" : "Free";
            string[] cols = { h.Name, $"{h.Intimidation}", $"{h.Muscle}", $"{h.Brains}", $"{h.Stealth}", $"{h.Loyalty}", $"${h.Wage}", state };
            for (int i = 0; i < cols.Length; i++) it.SetText(i, cols[i]);
            string tip = $"{h.Name}\nIntimidation {h.Intimidation}, Muscle {h.Muscle}, Brains {h.Brains}, Stealth {h.Stealth}\nLoyalty {h.Loyalty}/100, ambition {h.Ambition}/100, wage ${h.Wage}/week" + (job != null ? $"\nThis week: {job}" : "");
            for (int i = 0; i < cols.Length; i++) it.SetTooltipText(i, tip);
            if (h.Loyalty < 30) it.SetCustomColor(5, Palette.Bad);
            if (h.State == HoodState.Jailed) it.SetCustomColor(7, Palette.Police);
            else if (jobs.ContainsKey(h.Id)) it.SetCustomColor(7, Palette.Player);
        }
    }

    private void RefreshOrders()
    {
        Clear(_ordersPanel);
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

    private void RefreshGangs()
    {
        _gangsTree.Clear();
        var root = _gangsTree.CreateItem();
        foreach (var g in W.LivingGangs.OrderByDescending(g => W.TurfOf(g.Id).Count()))
        {
            var it = _gangsTree.CreateItem(root);
            var boss = W.HoodById(g.BossHoodId);
            string[] cols = { g.IsPlayer ? $"{g.Name} (you)" : g.Name, $"{W.TurfOf(g.Id).Count()}", $"{W.HoodsOf(g.Id).Count()}", $"${g.Cash:N0}", $"{g.Heat}" };
            for (int i = 0; i < cols.Length; i++) { it.SetText(i, cols[i]); it.SetTooltipText(i, $"{g.Name}, run by {boss.Name}"); }
            it.SetCustomColor(0, Palette.Gang(W, g.Id));
        }
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
        var newCity = new Button { Text = "New city", TooltipText = "Start again in a new district" };
        newCity.Pressed += () => NewGame(GD.Randi());
        top.AddChild(newCity);
        rows.AddChild(top);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        rows.AddChild(body);

        // Left: the map, the live controls and the street ticker.
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 6);
        _map = new MapView { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2.2f, CustomMinimumSize = new Vector2(780, 400) };
        _map.BusinessClicked += _ => { _tabs.CurrentTab = TabIndex("Business"); RefreshBusiness(); };
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
        _menTree = TreeTab("Men", "Name", "Int", "Mus", "Brn", "Stl", "Loy", "Wage", "Now");
        _ordersPanel = ScrollTab("Orders");
        _gangsTree = TreeTab("Gangs", "Gang", "Turf", "Men", "Cash", "Heat");
        _news = new RichTextLabel { Name = "Report", SelectionEnabled = true, BbcodeEnabled = true };
        _tabs.AddChild(_news);
        _tabs.AddChild(BuildConsole());
        body.AddChild(_tabs);

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
        _tabs.Modulate = live ? new Color(1, 1, 1, 0.45f) : Colors.White;
        _tabs.MouseFilter = live ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        SetProcessInput(!live);
        foreach (var child in _tabs.GetChildren().OfType<Control>()) child.ProcessMode = live ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
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
            if (i > 0) tree.SetColumnCustomMinimumWidth(i, columns[i] is "Cash" ? 80 : columns[i] is "Now" ? 64 : columns[i] is "Wage" ? 50 : 36);
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

        if (Arg("--seed") is string seed) NewGame(ulong.Parse(seed));
        int weeks = int.Parse(Arg("--weeks") ?? "0");
        for (int i = 0; i < weeks; i++) { _replayWeek = W.Week; _shell.Execute("auto"); _shell.EndWeek(); _news.Text = WeekReport(_replayWeek); }
        if (Flag("--plan")) _shell.Execute("auto");
        if (Arg("--select") is string sel) { _map.SelectedBusiness = int.Parse(sel); }
        if (Flag("--select-mine")) _map.SelectedBusiness = W.TurfOf(Me).First().Id;
        if (Flag("--select-rival")) _map.SelectedBusiness = W.Businesses.First(b => b.IsProtected && b.ProtectorGangId != Me).Id;
        if (Flag("--clear")) _shell.Pending.Clear();
        RefreshAll();
        if (Arg("--tab") is string tab) _tabs.CurrentTab = TabIndex(tab);
        if (Arg("--live") is string live)
        {
            EndWeek();
            _playing = false;
            _map.Advance(float.Parse(live));
            _clock.Text = MapView.ClockText(_map.Clock);
        }
        if (Arg("--shot") is string shot)
        {
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(shot);
            GetTree().Quit();
        }
    }
}
