using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// Draws the district: streets, blocks, every business tinted by whoever protects it, gang
/// headquarters and the precinct house. In live mode it also replays the week's script,
/// with hoods walking the streets to their jobs and coming home (or not).
/// </summary>
public partial class MapView : Control
{
    public event Action<int>? BusinessClicked;

    /// <summary>Raised when a ward is clicked while wards are shown.</summary>
    public event Action<int>? WardClicked;

    /// <summary>Raised once per script action as the live clock passes the moment it happens.</summary>
    public event Action<ScriptAction>? ActionHappened;

    public World? World { get; set; }

    private static readonly CityMap NoMap = new();

    /// <summary>The city being drawn; its size sets the grid.</summary>
    private CityMap Map => World?.Map ?? NoMap;
    public int SelectedBusiness { get; set; } = -1;

    public bool Live { get; private set; }
    public float Clock { get; private set; }

    private int _hoverLot = -1;

    /// <summary>During replay, who each business answers to as of the live clock.</summary>
    private Dictionary<int, (int Gang, RacketKind Racket)> _shown = new();
    private List<Actor> _actors = new();

    /// <summary>Seconds of real time, for scenery that moves whether or not the week is running.</summary>
    private float _anim;

    private float _zoom = 1;
    private Vector2 _pan;
    private bool _dragging;

    private float FitTile => Mathf.Floor(Mathf.Min(Size.X / (Map.Width + 0.6f), Size.Y / (Map.Height + 0.6f)));
    private float Tile => FitTile * _zoom;
    private Vector2 Origin => (Size - new Vector2(Map.Width, Map.Height) * Tile) / 2 + _pan;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TooltipText = " "; // enables _GetTooltip
        Resized += QueueRedraw;
        C = this;
        _overlay = new Overlay { Map = this, MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_overlay);
    }

    private (float Zoom, Vector2 Pan, Vector2 Size) _cityView;

    public override void _Process(double delta)
    {
        _anim += (float)delta;
        _overlay.QueueRedraw();
        // The city only needs drawing again when the view moves or the light changes.
        var view = (_zoom, _pan, Size);
        if (view != _cityView || Mathf.Round(Night * 40) / 40 != _cityNight) { _cityView = view; QueueRedraw(); }
    }

    /// <summary>Big cities draw small when fitted, so they can be zoomed in further.</summary>
    private float MaxZoom => Mathf.Max(4, Map.Width / 6f);

    /// <summary>Zoom about a point on screen, keeping whatever is under it in place.</summary>
    private void ZoomAt(Vector2 screen, float factor)
    {
        var tileUnder = (screen - Origin) / Tile;
        _zoom = Mathf.Clamp(_zoom * factor, 1, MaxZoom);
        if (_zoom <= 1.001f) { _pan = Vector2.Zero; return; }
        _pan += screen - (Origin + tileUnder * Tile);
        ClampPan();
    }

    /// <summary>Fit the whole city, or on a big one, start zoomed in on a lot (usually your headquarters).</summary>
    public void ResetView(Lot? focus)
    {
        _zoom = 1;
        _pan = Vector2.Zero;
        if (focus == null || Map.BlocksX <= 5 || Size.X <= 0) return;
        ZoomAt(Origin + new Vector2(focus.X + 0.5f, focus.Y + 0.5f) * Tile, 2);
        // Bring the lot towards the middle of the view.
        _pan += Size / 2 - (Origin + new Vector2(focus.X + 0.5f, focus.Y + 0.5f) * Tile);
        ClampPan();
    }

    /// <summary>Zoom about the middle of the map, for screenshots.</summary>
    public void ZoomCentre(float zoom) => ZoomAt(Size / 2, zoom / _zoom);

    private void ClampPan()
    {
        var excess = (new Vector2(Map.Width, Map.Height) * Tile - Size) / 2 + Vector2.One * Tile;
        excess = excess.Max(Vector2.Zero);
        _pan = _pan.Clamp(-excess, excess);
    }

    // ---- Live replay ----------------------------------------------------------

    private sealed record Actor(ScriptAction Action, List<Vector2> Path, float Depart, float Arrive, float Leave, float Home, Color Colour, int Order);

    /// <summary>The simulation running the week, which the map steps forward as the clock moves.</summary>
    private Simulation? _sim;

    private readonly List<bool> _fired = new();
    private int _collectIndex;

    /// <summary>The live clock hands the week over to Sunday's reckoning a little before collection, so collectors set out on time.</summary>
    public const int FinishTick = Content.CollectionTick - 3;

    /// <summary>
    /// Start the live week. The map shows the state at the start of the week until each change
    /// happens, and runs the simulation forward hour by hour as the clock moves.
    /// </summary>
    public void BeginReplay(Dictionary<int, (int Gang, RacketKind Racket)> startOfWeek, Simulation sim)
    {
        if (World == null) return;
        _sim = sim;
        _shown = new Dictionary<int, (int, RacketKind)>(startOfWeek);
        Live = true;
        Clock = 0;
        _actors = new List<Actor>();
        _fired.Clear();
        _collectIndex = 0;
        Step();
        QueueRedraw();
    }

    private List<Vector2> StreetPath(int fromLot, int toLot)
    {
        var from = World!.Map.LotAt(fromLot);
        var to = World.Map.LotAt(toLot);
        var path = World.Map.Path(from, to).Select(p => new Vector2(p.X + 0.5f, p.Y + 0.5f)).ToList();
        // Men leave from one front door and stand at another, out on the pavement.
        path[0] = Doorstep(from);
        path[^1] = Doorstep(to);
        return path;
    }

    /// <summary>Turn any new lines of the week's script into people on the street.</summary>
    private void SyncScript()
    {
        for (int i = _actors.Count; i < World!.Script.Count; i++)
        {
            var a = World.Script[i];
            var path = StreetPath(a.FromLot, a.ToLot);
            float hours = Simulation.WalkHours(World, a.FromLot, a.ToLot);
            float arrive = a.Tick;
            // Sunday collectors set out in waves rather than all at once.
            if (a.Kind == ActionKind.Collect) arrive += (_collectIndex++ % 10) * 0.6f;
            float linger = a.Kind switch { ActionKind.Takeover => 1.5f, ActionKind.Raid => 2f, ActionKind.Collect => 0.4f, _ => 1f };
            float leave = a.Kind == ActionKind.Guard ? Content.HoursPerWeek : arrive + linger;
            bool comesHome = (a.Result is not (ActionResult.Killed or ActionResult.Arrested) && a.CasualtyHoodId != a.HoodId) || a.Backup.Count > 0;
            float home = comesHome ? leave + hours : leave;
            var colour = a.Kind == ActionKind.Raid ? Palette.Police : Palette.Gang(World, a.GangId);
            _actors.Add(new Actor(a, path, arrive - hours, arrive, leave, home, colour, i));
            _fired.Add(false);
        }
    }

    private static Vector2 Doorstep(Lot lot) => new(lot.X + 0.5f, lot.FrontY < lot.Y ? lot.Y - 0.1f : lot.Y + 1.1f);

    public void EndReplay()
    {
        Live = false;
        _sim = null;
        _actors.Clear();
        _fired.Clear();
        QueueRedraw();
    }

    /// <summary>Advance the live clock by some hours of game time.</summary>
    public void Advance(float hours)
    {
        if (!Live) return;
        Clock = Mathf.Min(Clock + hours, Content.HoursPerWeek);
        Step();
        QueueRedraw();
    }

    /// <summary>Run the simulation up to the clock, then show whatever has happened by now.</summary>
    private void Step()
    {
        if (_sim is { WeekRunning: true })
        {
            if (Clock >= FinishTick) _sim.FinishWeek();
            else _sim.RunUntil((int)Clock);
        }
        SyncScript();
        for (int i = 0; i < _actors.Count; i++)
        {
            if (_fired[i] || _actors[i].Arrive > Clock) continue;
            _fired[i] = true;
            var a = _actors[i].Action;
            if (a.BusinessId >= 0 && _shown.TryGetValue(a.BusinessId, out var was))
            {
                _shown[a.BusinessId] = a switch
                {
                    { Kind: ActionKind.Extort, Result: ActionResult.Success } => (a.GangId, was.Racket),
                    { Kind: ActionKind.Takeover, Result: ActionResult.Won } => (a.GangId, was.Racket),
                    { Kind: ActionKind.Racket, Result: ActionResult.Success } => (was.Gang, World!.BusinessById(a.BusinessId).Racket is var k and not RacketKind.None ? k : RacketKind.Numbers),
                    { Kind: ActionKind.Raid } => (was.Gang, RacketKind.None),
                    _ => was,
                };
            }
            if (a.BusinessId >= 0) QueueRedraw();
            ActionHappened?.Invoke(a);
        }
    }

    /// <summary>Who answers to whom as the live map shows it, which may run behind the simulation.</summary>
    public int ShownProtector(Business b) => ShownState(b).Gang;

    public bool ReplayFinished => Live && Clock >= Content.HoursPerWeek;

    // ---- Input ----------------------------------------------------------------

    public override void _GuiInput(InputEvent e)
    {
        if (World == null) return;
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } up) { ZoomAt(up.Position, 1.15f); return; }
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } down) { ZoomAt(down.Position, 1 / 1.15f); return; }
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag) { _dragging = drag.Pressed; return; }
        if (e is InputEventMagnifyGesture pinch) { ZoomAt(pinch.Position, pinch.Factor); return; }
        if (e is InputEventPanGesture panGesture) { _pan -= panGesture.Delta * 8; ClampPan(); return; }
        if (e is InputEventMouseMotion motion)
        {
            if (_dragging) { _pan += motion.Relative; ClampPan(); }
            int lot = LotUnder(motion.Position);
            _hoverLot = lot;
            HoverWard = ShowWards ? WardAtTile((motion.Position - Origin) / Tile) : -1;
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            if (ShowWards && WardAtTile((click.Position - Origin) / Tile) is int ward and >= 0) WardClicked?.Invoke(ward);
            int lotId = LotUnder(click.Position);
            if (lotId >= 0 && World.Map.LotAt(lotId).BusinessId is int biz and >= 0)
            {
                SelectedBusiness = biz;
                BusinessClicked?.Invoke(biz);
                QueueRedraw();
            }
        }
    }

    private int LotUnder(Vector2 pos)
    {
        if (World == null) return -1;
        var tile = ((pos - Origin) / Tile).Floor();
        var lot = World.Map.Lots.FirstOrDefault(l => l.X == (int)tile.X && l.Y == (int)tile.Y);
        return lot?.Id ?? -1;
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (World == null) return "";
        int lotId = LotUnder(atPosition);
        if (lotId < 0) return "";
        var lot = World.Map.LotAt(lotId);
        switch (lot.Use)
        {
            case LotUse.Business:
                var b = World.BusinessById(lot.BusinessId);
                string owner = b.IsProtected ? World.GangById(b.ProtectorGangId).Name : "Nobody's paying anyone";
                string racket = b.Racket != RacketKind.None ? $"\nBack room: {Content.Rackets[b.Racket].Label}" : "";
                string shut = b.IsOpen ? "" : $"\nClosed by police for {b.ShutWeeks} more weeks";
                return $"{b.Name}\nTakes ${b.Takings} a week · owner toughness {b.Toughness}/10\n{owner}{racket}{shut}{WardLine(lot)}";
            case LotUse.Headquarters:
                var g = World.Gangs.First(x => x.Id == lot.GangId);
                return $"Headquarters of {g.Name}";
            case LotUse.Precinct:
                return "Precinct house" + WardLine(lot);
            default:
                return "";
        }
    }

    private string WardLine(Lot lot)
    {
        if (World == null || lot.WardId >= World.Wards.Count) return "";
        var ward = World.Wards[lot.WardId];
        string whose = ward.Reformer ? "a reformer" : ward.OwnerGangId >= 0 ? $"on {World.GangById(ward.OwnerGangId).Name}'s payroll" : "a party man";
        return $"\n{ward.Name}: Alderman {ward.Alderman}, {whose}";
    }

    // ---- Drawing --------------------------------------------------------------

    /// <summary>
    /// The canvas the drawing helpers paint on. The city itself (ground, buildings, street names)
    /// is drawn on the map and only redrawn when something changes; what moves every frame
    /// (traffic, people, the night, outlines, wards and men on the street) goes on an overlay.
    /// </summary>
    private CanvasItem C = null!;

    private Overlay _overlay = null!;

    /// <summary>Milliseconds the city and the overlay last took to draw, for the --bench flag.</summary>
    public static double CityMillis, OverlayMillis;
    public static int CityDraws;

    private sealed partial class Overlay : Control
    {
        public MapView Map = null!;
        public override void _Draw() => Map.DrawOverlay(this);
    }

    /// <summary>Night, quantised, as last drawn on the city; the city is redrawn when it moves on.</summary>
    private float _cityNight = -1;

    public override void _Draw()
    {
        if (World == null) return;
        ulong started = Time.GetTicksUsec();
        C = this;
        _t = Tile;
        _o = Origin;
        _cityNight = Mathf.Round(Night * 40) / 40;
        var font = ThemeDB.FallbackFont;
        DrawGround();
        DrawShadows();
        var view = new Rect2(Vector2.Zero, Size).Grow(_t);
        foreach (var lot in World.Map.Lots)
            if (view.Intersects(Footprint(lot))) DrawLot(lot, _cityNight);
        DrawLampPosts(_cityNight);
        DrawStreetNames(font);
        CityMillis = (Time.GetTicksUsec() - started) / 1000.0;
        CityDraws++;
    }

    private void DrawOverlay(CanvasItem canvas)
    {
        if (World == null) return;
        ulong started = Time.GetTicksUsec();
        C = canvas;
        _t = Tile;
        _o = Origin;
        _headlights.Clear();
        var font = ThemeDB.FallbackFont;
        float night = Night;
        DrawTraffic(night);
        DrawPassersBy();
        DrawNight(night);
        var view = new Rect2(Vector2.Zero, Size).Grow(_t);
        foreach (var lot in World.Map.Lots)
            if (lot.Use != LotUse.Empty && view.Intersects(Footprint(lot))) DrawLotOutline(lot);
        DrawWards(font);
        if (Live) DrawActors(font);
        C = this;
        OverlayMillis = (Time.GetTicksUsec() - started) / 1000.0;
    }

    /// <summary>Who protects a business, and what's in its back room, as of the live clock.</summary>
    private (int Gang, RacketKind Racket) ShownState(Business b) =>
        Live && _shown.TryGetValue(b.Id, out var shown) ? shown : (b.ProtectorGangId, b.Racket);

    private void DrawLot(Lot lot, float night)
    {
        var w = World!;
        switch (lot.Use)
        {
            case LotUse.Business:
            {
                var b = w.BusinessById(lot.BusinessId);
                var (protector, racket) = ShownState(b);
                bool open = b.IsOpen || Live;
                var roof = Roofs[(int)(Hash(lot.Id, 1) * Roofs.Length)];
                if (b.Kind == BusinessKind.Warehouse) roof = new Color("5d5a55");
                DrawBuilding(lot, open ? roof : roof.Darkened(0.35f), night);
                if (b.Kind == BusinessKind.Warehouse)
                {
                    var f = Footprint(lot);
                    for (float x = f.Position.X + _t * 0.1f; x < f.End.X - _t * 0.05f; x += _t * 0.1f)
                        C.DrawLine(new Vector2(x, f.Position.Y + _t * 0.08f), new Vector2(x, f.End.Y - _t * 0.08f), roof.Darkened(0.15f), 1);
                }
                if (b.Kind == BusinessKind.Hotel)
                {
                    // A water tower on its stilts.
                    var c = P(lot.X + 0.78f, lot.Y + (lot.FrontY < lot.Y ? 0.78f : 0.24f));
                    C.DrawCircle(c + Vector2.One * _t * 0.08f, _t * 0.12f, Shadow);
                    C.DrawCircle(c, _t * 0.12f, new Color("6b5038"));
                    C.DrawArc(c, _t * 0.12f, 0, Mathf.Tau, 14, new Color("3a2a1e"), Mathf.Max(1, _t * 0.025f));
                    C.DrawCircle(c, _t * 0.04f, new Color("3a2a1e"));
                }
                DrawAwning(lot, protector >= 0 ? Palette.Gang(w, protector) : null, open);
                DrawSign(lot, b);
                if (racket != RacketKind.None) DrawRacketBadge(lot, racket);
                if (!open) DrawBoardedUp(lot);
                break;
            }
            case LotUse.Headquarters:
                DrawHeadquarters(lot, Palette.Gang(w, lot.GangId), night);
                break;
            case LotUse.Precinct:
                DrawPrecinct(lot, night);
                break;
            default:
                DrawEmptyLot(lot);
                break;
        }
    }

    /// <summary>Ownership rims, selection and hover, drawn over the lighting so they read at night.</summary>
    private void DrawLotOutline(Lot lot)
    {
        var w = World!;
        var r = Footprint(lot);
        if (lot.Use == LotUse.Business)
        {
            var (protector, _) = ShownState(w.BusinessById(lot.BusinessId));
            if (protector >= 0) C.DrawRect(r, Palette.Gang(w, protector), false, Mathf.Max(2, _t * 0.06f));
        }
        else if (lot.Use == LotUse.Headquarters)
            C.DrawRect(r, Palette.Gang(w, lot.GangId), false, Mathf.Max(2, _t * 0.09f));

        bool selected = lot.Use == LotUse.Business && lot.BusinessId == SelectedBusiness;
        if (selected)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(_anim * 4);
            C.DrawRect(r.Grow(Mathf.Max(3, _t * 0.08f)), Palette.Ink with { A = pulse }, false, Mathf.Max(2, _t * 0.06f));
        }
        else if (lot.Id == _hoverLot && lot.Use != LotUse.Empty) C.DrawRect(r.Grow(2), Palette.Ink with { A = 0.6f }, false, 1.5f);
    }

    /// <summary>A man seen from above: shoulders in his coat, a hat on top, a shadow under him.</summary>
    private void DrawFigure(Vector2 pos, Vector2 facing, float r, Color coat, Color hat, Color band, bool walking)
    {
        float angle = facing.LengthSquared() > 0.0001f ? facing.Angle() : 0;
        float sway = walking ? Mathf.Sin(_anim * 10 + pos.X * 0.1f) * 0.15f : 0;
        Ellipse(pos + new Vector2(r * 0.35f, r * 0.45f), new Vector2(r * 0.75f, r * 1.05f), Shadow, angle);
        Ellipse(pos, new Vector2(r * 0.62f, r * 1.0f), coat.Darkened(0.55f), angle + sway, 16);
        Ellipse(pos, new Vector2(r * 0.52f, r * 0.9f), coat, angle + sway, 16);
        C.DrawCircle(pos + facing.Normalized() * r * 0.08f, r * 0.62f, hat);
        C.DrawArc(pos + facing.Normalized() * r * 0.08f, r * 0.42f, 0, Mathf.Tau, 14, band, Mathf.Max(1, r * 0.16f));
        C.DrawCircle(pos + facing.Normalized() * r * 0.08f, r * 0.3f, hat.Lightened(0.12f));
    }

    private void DrawActors(Font font)
    {
        float now = Clock;
        var o = _o;
        float t = _t;
        foreach (var actor in _actors)
        {
            if (now < actor.Depart || now > actor.Home + 0.01f) continue;
            var a = actor.Action;
            var target = actor.Path[^1];
            Vector2 at, ahead;
            bool walking = true;
            if (now < actor.Arrive)
            {
                float f = (now - actor.Depart) / (actor.Arrive - actor.Depart);
                at = Along(actor.Path, f);
                ahead = Along(actor.Path, f + 0.02f) - at;
            }
            else if (now <= actor.Leave)
            {
                at = target;
                ahead = actor.Path[^2] - target;
                walking = false;
            }
            else
            {
                float f = 1 - (now - actor.Leave) / Mathf.Max(0.01f, actor.Home - actor.Leave);
                at = Along(actor.Path, f);
                ahead = Along(actor.Path, f - 0.02f) - at;
            }

            var pos = o + at * t;
            float radius = Mathf.Max(5.5f, t * 0.24f);
            bool atScene = now >= actor.Arrive && now <= actor.Leave;

            // Outcome rings while the man is on the job.
            if (atScene && a.Kind != ActionKind.Guard && a.Kind != ActionKind.Collect)
            {
                var ring = a.Result switch
                {
                    ActionResult.Success or ActionResult.Won => Palette.Good,
                    ActionResult.Lost or ActionResult.Killed => Palette.Bad,
                    ActionResult.Arrested => Palette.Police,
                    _ => Palette.Neutral,
                };
                float pulse = 1 + 0.35f * Mathf.Sin((now - actor.Arrive) * 9);
                C.DrawArc(o + target * t, t * 0.4f * pulse, 0, Mathf.Tau, 24, ring, 2.5f);
            }

            // In a takeover the rival's man stands in the doorway, and shots are traded.
            if (a.Kind == ActionKind.Takeover && a.DefenderHoodId >= 0 && now >= actor.Arrive - 0.5f && now <= actor.Leave)
            {
                var defPos = o + (target + new Vector2(0.3f, -0.2f)) * t;
                var dc = Palette.Gang(World!, a.DefenderGangId);
                DrawFigure(defPos, pos - defPos, radius, dc, new Color("1c1a19"), dc, false);
                if (atScene && now - actor.Arrive < 0.8f && Mathf.Sin(_anim * 23 + a.HoodId) > 0.55f)
                    Star(pos.Lerp(defPos, Hash((int)(_anim * 8), a.HoodId) < 0.5f ? 0.3f : 0.7f), radius * 0.7f, new Color("ffe28a"), 6, 0.4f);
            }

            // Backup walks a step behind the man in front, and fans out round him on the job.
            int backup = a.Backup.Count - (now >= actor.Arrive && a.CasualtyHoodId >= 0 && a.Backup.Contains(a.CasualtyHoodId) ? 1 : 0);
            DrawBackup(pos, ahead, radius, actor.Colour, backup, walking);

            bool fallen = a.CasualtyHoodId >= 0 && a.CasualtyHoodId == a.HoodId && now >= actor.Arrive;
            if (fallen || (now > actor.Leave && a.Result is ActionResult.Killed or ActionResult.Arrested && a.CasualtyHoodId == a.HoodId)) continue; // drawn below as a cross

            if (a.Kind == ActionKind.Raid)
                DrawFigure(pos, ahead, radius, Palette.Police, new Color("1b2a4a"), Palette.Police.Lightened(0.3f), walking);
            else
                DrawFigure(pos, ahead, radius, actor.Colour, new Color("1c1a19"), actor.Colour.Lightened(0.25f), walking);
            if (a.Kind == ActionKind.Guard && atScene) C.DrawArc(pos, radius + 3, 0, Mathf.Tau, 16, actor.Colour with { A = 0.6f }, 1.5f);
        }

        DrawOnTheirWay(now, o, t);

        // Where someone died this week, a cross stays on the pavement.
        foreach (var actor in _actors)
        {
            var a = actor.Action;
            if (a.CasualtyHoodId < 0 || a.Result == ActionResult.Arrested || now < actor.Arrive) continue;
            if (a.Kind == ActionKind.Raid) continue;
            var c = o + (actor.Path[^1] + new Vector2(-0.3f, 0.3f)) * t;
            float s = t * 0.14f;
            C.DrawLine(c - new Vector2(s, s), c + new Vector2(s, s), Palette.Bad, 2.5f);
            C.DrawLine(c - new Vector2(s, -s), c + new Vector2(s, -s), Palette.Bad, 2.5f);
        }
    }

    private void DrawBackup(Vector2 pos, Vector2 ahead, float radius, Color colour, int count, bool walking)
    {
        var back = ahead.LengthSquared() > 0.0001f ? -ahead.Normalized() : Vector2.Down;
        for (int i = 0; i < count; i++)
        {
            var at = walking
                ? pos + back * radius * 1.7f * (i + 1) + back.Orthogonal() * radius * 0.5f * (i % 2 == 0 ? 1 : -1)
                : pos + back.Rotated(Mathf.Pi / 2 + i * 1.1f - 1.1f) * radius * 1.9f;
            DrawFigure(at, ahead, radius * 0.92f, colour, new Color("1c1a19"), colour.Lightened(0.25f), walking);
        }
    }

    /// <summary>Men already on their way to a job that hasn't happened yet: rivals' as well as yours.</summary>
    private void DrawOnTheirWay(float now, Vector2 o, float t)
    {
        if (_sim is not { WeekRunning: true }) return;
        foreach (var job in _sim.Upcoming)
        {
            if (job.Order is not (ExtortOrder or GuardOrder or RacketOrder)) continue;
            var gang = World!.GangById(job.GangId);
            int bizId = job.Order switch { ExtortOrder e => e.BusinessId, GuardOrder g => g.BusinessId, RacketOrder r => r.BusinessId, _ => -1 };
            int toLot = World.BusinessById(bizId).LotId;
            float hours = Simulation.WalkHours(World, gang.HqLotId, toLot);
            if (now < job.Tick - hours || now >= job.Tick) continue;
            var path = StreetPath(gang.HqLotId, toLot);
            float f = (now - (job.Tick - hours)) / hours;
            var at = Along(path, f);
            var pos = o + at * t;
            var ahead = Along(path, f + 0.02f) - at;
            float radius = Mathf.Max(5.5f, t * 0.24f);
            var colour = Palette.Gang(World, gang.Id);
            DrawBackup(pos, ahead, radius, colour, Simulation.TeamOf(job.Order).Count() - 1, true);
            DrawFigure(pos, ahead, radius, colour, new Color("1c1a19"), colour.Lightened(0.25f), true);
        }
    }

    private static Vector2 Along(List<Vector2> path, float fraction)
    {
        fraction = Mathf.Clamp(fraction, 0, 1);
        float total = 0;
        for (int i = 1; i < path.Count; i++) total += path[i].DistanceTo(path[i - 1]);
        float want = total * fraction;
        for (int i = 1; i < path.Count; i++)
        {
            float seg = path[i].DistanceTo(path[i - 1]);
            if (want <= seg) return path[i - 1].Lerp(path[i], seg <= 0 ? 1 : want / seg);
            want -= seg;
        }
        return path[^1];
    }

    public static string ClockText(float tick)
    {
        string[] days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        int day = Mathf.Clamp((int)(tick / 24), 0, 6);
        int hour = (int)tick % 24;
        string h = hour == 0 ? "midnight" : hour == 12 ? "noon" : hour < 12 ? $"{hour} AM" : $"{hour - 12} PM";
        return $"{days[day]}, {h}";
    }
}
