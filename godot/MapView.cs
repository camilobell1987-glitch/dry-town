using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// Draws the city at an angle, the way the old gangster games did: streets and pavements flat on
/// the ground, buildings standing up with their fronts and west walls showing, every business tinted
/// by whoever protects it. In live mode it also replays the week's script, with hoods walking the
/// streets to their jobs and coming home (or not).
/// </summary>
public partial class MapView : Control
{
    public event Action<int>? BusinessClicked;

    /// <summary>Raised when a ward is clicked while wards are shown.</summary>
    public event Action<int>? WardClicked;

    /// <summary>A small gang's corner was clicked: the gang's id.</summary>
    public event Action<int>? CornerClicked;

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

    /// <summary>Room above the top street for the tallest buildings, in tiles: they only rise up the screen when seen at an angle.</summary>
    private float Headroom => Angled ? 1.6f : 0.2f;

    /// <summary>
    /// Whether the city is seen at an angle, with buildings standing up off the map, or straight down
    /// onto the roofs. Changing it draws the city again.
    /// </summary>
    public bool Angled
    {
        get => _angled;
        set { if (_angled == value) return; _angled = value; FreeLayers(); ClampPan(); QueueRedraw(); }
    }
    private bool _angled;

    private float FitTile => Mathf.Min(Size.X / (Map.Width + 0.6f), Size.Y / (Map.Height + 0.6f + Headroom));
    private float Tile => FitTile * _zoom;
    private Vector2 Origin => (Size - new Vector2(Map.Width, Map.Height - Headroom) * Tile) / 2 + _pan;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TooltipText = " "; // enables _GetTooltip
    }

    public override void _ExitTree() => FreeLayers();

    public override void _Process(double delta)
    {
        _anim += (float)delta;
        if (World == null) return;
        if (_builtFor != World.Map) BuildCity();
        RefreshChangedLots();
        PlaceWorld();
        DrawFrame();
    }

    /// <summary>Close enough to see the brickwork: a lot can fill about a fifth of a wide screen, whatever the city's size.</summary>
    private float MaxZoom => Mathf.Max(4, 220f / Mathf.Max(1, FitTile));

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
    public void ResetView(Lot? focus, float? zoomTo = null)
    {
        _zoom = 1;
        _pan = Vector2.Zero;
        if (focus == null || (Map.BlocksX <= 5 && zoomTo == null) || Size.X <= 0) return;
        float zoom = zoomTo ?? (Map.BlocksX >= 15 ? 3 : 2);
        ZoomAt(Origin + new Vector2(focus.X + 0.5f, focus.Y + 0.5f) * Tile, zoom);
        // Bring the lot towards the middle of the view.
        _pan += Size / 2 - (Origin + new Vector2(focus.X + 0.5f, focus.Y + 0.5f) * Tile);
        ClampPan();
    }

    /// <summary>Zoom about the middle of the map, for screenshots.</summary>
    public void ZoomCentre(float zoom) => ZoomAt(Size / 2, zoom / _zoom);

    private void ClampPan()
    {
        var excess = (new Vector2(Map.Width, Map.Height + Headroom) * Tile - Size) / 2 + Vector2.One * Tile;
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
        _burntNow.Clear();
        _collectIndex = 0;
        Step();
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
            float hours = Simulation.TravelHours(World, a.FromLot, a.ToLot, Men(a.Backup.Prepend(a.HoodId)));
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

    /// <summary>Where men stand at a door. A door facing away from the viewer is hidden by its own building, so they wait out in the street.</summary>
    private static Vector2 Doorstep(Lot lot) => new(lot.X + 0.5f, lot.FrontY < lot.Y ? lot.Y - 0.72f : lot.Y + 1.1f);

    /// <summary>
    /// Pick up a week loaded partway through at the hour it got to. Whatever happened before then is
    /// shown as done, without the sounds and ticker lines it made at the time.
    /// </summary>
    public void ResumeReplay(Simulation sim)
    {
        if (World == null) return;
        _sim = sim;
        _shown = new Dictionary<int, (int, RacketKind)>(sim.StartOfWeek);
        Live = true;
        Clock = World.Tick;
        _actors = new List<Actor>();
        _fired.Clear();
        _burntNow.Clear();
        _collectIndex = 0;
        _quiet = true;
        Step();
        _quiet = false;
    }

    /// <summary>True while a resumed week catches up, so what already happened makes no noise.</summary>
    private bool _quiet;

    public void EndReplay()
    {
        Live = false;
        _sim = null;
        _actors.Clear();
        _fired.Clear();
        _burntNow.Clear();
    }

    /// <summary>Advance the live clock by some hours of game time.</summary>
    public void Advance(float hours)
    {
        if (!Live) return;
        Clock = Mathf.Min(Clock + hours, Content.HoursPerWeek);
        Step();
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
                    { Kind: ActionKind.Arson, Result: ActionResult.Success } => (was.Gang, RacketKind.None),
                    _ => was,
                };
            }
            if (a is { Kind: ActionKind.Arson, Result: ActionResult.Success }) _burntNow.Add(a.BusinessId);
            if (!_quiet) ActionHappened?.Invoke(a);
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
            _hoverLot = LotUnder(motion.Position);
            HoverWard = ShowWards ? WardAtTile((motion.Position - Origin) / Tile) : -1;
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            if (ShowWards && WardAtTile((click.Position - Origin) / Tile) is int ward and >= 0) WardClicked?.Invoke(ward);
            // People stand in front of the buildings, so they're found first.
            if (!ShowWards && PickAt(click.Position) is { } who)
            {
                if (who.Kind == Pickable.Person) { Select(person: who.Id); PersonClicked?.Invoke(who.Id); }
                else { Select(hood: who.Id); HoodClicked?.Invoke(who.Id); }
                return;
            }
            int lotId = LotUnder(click.Position);
            if (lotId < 0) return;
            var lot = World.Map.LotAt(lotId);
            if (lot.BusinessId is int biz and >= 0)
            {
                Select(business: biz);
                BusinessClicked?.Invoke(biz);
            }
            else if (lot.Use == LotUse.Bank && World.BankAt(lotId) is { } bank)
            {
                Select(bank: bank.Id);
                BankClicked?.Invoke(bank.Id);
            }
            else if (lot.Use is LotUse.GunShop or LotUse.CarDealer)
            {
                Select(shop: lot.Id);
                ShopClicked?.Invoke(lot.Id);
            }
            else if (CornerOf(lot) is { } corner) CornerClicked?.Invoke(corner.Id);
        }
    }

    /// <summary>
    /// The lot under a point on screen. Buildings stand up from their lots, so a tall one is found by
    /// its walls and roof too, and whichever stands in front wins.
    /// </summary>
    private int LotUnder(Vector2 pos)
    {
        if (World == null) return -1;
        var tile = (pos - Origin) / Tile;
        int best = -1;
        foreach (var lot in World.Map.Lots)
        {
            if (lot.X > tile.X + 0.2f || lot.X + 1 + S * 3 < tile.X || lot.Y + 1 < tile.Y || lot.Y - 3 > tile.Y) continue;
            if (!Geometry2D.IsPointInPolygon(tile, Silhouette(lot))) continue;
            if (best < 0 || InFront(lot, World.Map.LotAt(best))) best = lot.Id;
        }
        return best;
    }

    /// <summary>Whether one lot's building is drawn over another's: rows nearer the bottom, then lots further west.</summary>
    private static bool InFront(Lot a, Lot b) => a.Y != b.Y ? a.Y > b.Y : a.X < b.X;

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (World == null) return "";
        if (PickAt(atPosition) is { } who) return PersonTooltip(who);
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
                string front = b.IsFront ? $"\nA front for {World.GangById(b.OwnerGangId).Name}" : "";
                string takes = b.Kind == BusinessKind.Court ? $"A tenement court · the block pays ${b.Takings} a week" : $"Takes ${b.Takings} a week · owner toughness {b.Toughness}/10";
                return $"{b.Name}\n{takes}\n{owner}{front}{racket}{shut}{WardLine(lot)}";
            case LotUse.Headquarters:
                var g = World.Gangs.First(x => x.Id == lot.GangId);
                return g.IsSmall ? CornerTip(g) : $"Headquarters of {g.Name}";
            case LotUse.Precinct:
                return "Precinct house" + WardLine(lot);
            case LotUse.Bank:
                var bank = World.BankAt(lot.Id);
                return bank == null ? "" : $"{bank.Name}\nAbout ${bank.Vault / 500 * 500:N0} in the vault · {bank.Guards} guard{(bank.Guards == 1 ? "" : "s")}{WardLine(lot)}";
            case LotUse.GunShop or LotUse.CarDealer:
                return ShopTooltip(lot);
            default:
                return lot.Terrain == Terrain.Water ? "" : FillerName(lot);
        }
    }

    private string WardLine(Lot lot)
    {
        if (World == null || lot.WardId >= World.Wards.Count) return "";
        var ward = World.Wards[lot.WardId];
        string whose = ward.Reformer ? "a reformer" : ward.OwnerGangId >= 0 ? $"on {World.GangById(ward.OwnerGangId).Name}'s payroll" : "a party man";
        return $"\n{ward.Name}: Alderman {ward.Alderman}, {whose}";
    }

    // ---- Layers ---------------------------------------------------------------

    /// <summary>
    /// The pen the drawing helpers paint with. The city is recorded once into canvas items laid out
    /// row by row, back to front, so buildings hide what stands behind them; panning and zooming only
    /// move the lot. Whatever moves (traffic, people, the men on the job) is drawn each frame into the
    /// row it stands in, so it passes behind the buildings in front of it.
    /// </summary>
    private Pen C = null!;

    /// <summary>Tile units are drawn this many units across in the recorded city, and scaled to the zoom.</summary>
    private const float U = 32;

    private readonly List<Rid> _items = new();
    private Rid _world;
    private Pen[,] _groundFull = new Pen[0, 0], _groundCoarse = new Pen[0, 0];

    /// <summary>Per row and column of blocks: the buildings in full detail, in outline for a far-off view, and their lit windows.</summary>
    private Pen[,] _full = new Pen[0, 0], _coarse = new Pen[0, 0], _lit = new Pen[0, 0];

    /// <summary>Per row: whatever moves there this frame.</summary>
    private Pen[] _dyn = Array.Empty<Pen>();

    private Pen _lampGlow = null!, _frameGlow = null!, _marks = null!, _screen = null!;

    private CityMap? _builtFor;
    private long[] _signature = Array.Empty<long>();
    private float[] _height = Array.Empty<float>();
    private float _shownNight = -1;
    private bool? _shownFine;

    /// <summary>Milliseconds the city took to record, the last frame took to draw, and how many times a part of the city was recorded, for --bench.</summary>
    public static double CityMillis, OverlayMillis;
    public static int CityDraws;

    private Pen NewPen(Rid parent)
    {
        var rid = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(rid, parent);
        RenderingServer.CanvasItemSetDrawIndex(rid, _items.Count);
        _items.Add(rid);
        return new Pen(rid);
    }

    private void FreeLayers()
    {
        foreach (var rid in _items) RenderingServer.FreeRid(rid);
        _items.Clear();
        _builtFor = null;
    }

    /// <summary>Lay out the canvas items for a new city and record every part of it.</summary>
    private void BuildCity()
    {
        ulong started = Time.GetTicksUsec();
        FreeLayers();
        var map = World!.Map;
        _builtFor = map;
        _trafficFor = null;
        _world = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_world, GetCanvasItem());
        _items.Add(_world);

        int cols = map.BlocksX, rows = map.Height;
        _groundFull = new Pen[map.BlocksX + 1, map.BlocksY + 1];
        _groundCoarse = new Pen[map.BlocksX + 1, map.BlocksY + 1];
        for (int by = 0; by <= map.BlocksY; by++)
        for (int bx = 0; bx <= map.BlocksX; bx++)
        {
            _groundFull[bx, by] = NewPen(_world);
            _groundCoarse[bx, by] = NewPen(_world);
        }

        _full = new Pen[rows, cols];
        _coarse = new Pen[rows, cols];
        _lit = new Pen[rows, cols];
        _dyn = new Pen[rows];
        for (int y = 0; y < rows; y++)
        {
            // East to west: the camera looks from the south-west, so western buildings stand in front.
            for (int c = cols - 1; c >= 0; c--)
            {
                _full[y, c] = NewPen(_world);
                _coarse[y, c] = NewPen(_world);
                _lit[y, c] = NewPen(_world);
            }
            _dyn[y] = NewPen(_world);
        }
        _lampGlow = NewPen(_world);
        _frameGlow = NewPen(_world);
        _marks = NewPen(_world);
        _screen = NewPen(GetCanvasItem());

        _signature = new long[map.Lots.Count];
        _height = new float[map.Lots.Count];
        foreach (var lot in map.Lots)
        {
            _signature[lot.Id] = Signature(lot);
            _height[lot.Id] = HeightOf(lot);
        }
        WorldMode();
        IndexCity();
        for (int by = 0; by <= map.BlocksY; by++)
        for (int bx = 0; bx <= map.BlocksX; bx++)
            RecordGround(bx, by);
        for (int y = 0; y < rows; y++)
        for (int c = 0; c < cols; c++)
            RecordRow(y, c);
        RecordLampGlow();
        _shownNight = -1;
        _shownFine = null;
        CityMillis = (Time.GetTicksUsec() - started) / 1000.0;
    }

    /// <summary>A number that changes whenever anything shown on a lot's building does.</summary>
    private long Signature(Lot lot)
    {
        long s = (long)lot.Use * 1_000_003 + lot.GangId * 7919 + lot.BusinessId;
        if (lot.Use == LotUse.Business)
        {
            var b = World!.BusinessById(lot.BusinessId);
            var (gang, racket) = ShownState(b);
            s = s * 31 + gang;
            s = s * 31 + (int)racket;
            s = s * 31 + (b.IsOpen || Live ? 1 : 0);
            s = s * 31 + (Burnt(b) ? 1 : 0);
            s = s * 31 + (World.StrikeAt(b) != null ? 1 : 0);
            s = s * 31 + (World.IsWireRoom(b) ? 1 : 0);
        }
        return s;
    }

    /// <summary>Record again any part of the city where a business changed hands, opened a racket or was shut.</summary>
    private void RefreshChangedLots()
    {
        var map = World!.Map;
        HashSet<(int Row, int Col)>? rows = null;
        HashSet<(int, int)>? blocks = null;
        foreach (var lot in map.Lots)
        {
            long s = Signature(lot);
            if (s == _signature[lot.Id]) continue;
            _signature[lot.Id] = s;
            _height[lot.Id] = HeightOf(lot);
            int col = Column(lot.X);
            (rows ??= new()).Add((lot.Y, col));
            (blocks ??= new()).Add((col, (lot.Y - 1) / CityMap.StrideY));
        }
        if (rows == null) return;
        WorldMode();
        FindFactories();
        foreach (var (row, col) in rows) RecordRow(row, col);
        foreach (var (bx, by) in blocks!) RecordGround(bx, by);
    }

    /// <summary>The column of blocks a tile belongs to; the avenue on a block's west side goes with it.</summary>
    private int Column(float x) => Mathf.Clamp((int)(x / CityMap.StrideX), 0, Map.BlocksX - 1);

    private Pen DynAt(float y) => _dyn[Mathf.Clamp((int)y, 0, _dyn.Length - 1)];

    /// <summary>Draw in tile units of the recorded city.</summary>
    private void WorldMode()
    {
        _o = Vector2.Zero;
        _t = U;
    }

    /// <summary>Draw in pixels on screen.</summary>
    private void ScreenMode()
    {
        _o = Origin;
        _t = Tile;
    }

    /// <summary>A size in screen pixels, as recorded-city units at the current zoom.</summary>
    private float Px(float px) => px * U / Mathf.Max(0.01f, Tile);

    /// <summary>Move and scale the recorded city to the view, and switch detail and daylight as needed.</summary>
    private void PlaceWorld()
    {
        float k = Tile / U;
        RenderingServer.CanvasItemSetTransform(_world, new Transform2D(0, new Vector2(k, k), 0, Origin));

        bool fine = Tile >= 16;
        if (fine != _shownFine)
        {
            _shownFine = fine;
            for (int y = 0; y < _full.GetLength(0); y++)
            for (int c = 0; c < _full.GetLength(1); c++)
            {
                RenderingServer.CanvasItemSetVisible(_full[y, c].Item, fine);
                RenderingServer.CanvasItemSetVisible(_coarse[y, c].Item, !fine);
            }
            foreach (var pen in _groundFull) RenderingServer.CanvasItemSetVisible(pen.Item, fine);
            foreach (var pen in _groundCoarse) RenderingServer.CanvasItemSetVisible(pen.Item, !fine);
        }

        float night = Mathf.Round(Night * 40) / 40;
        if (night != _shownNight)
        {
            _shownNight = night;
            var shade = Colors.White.Lerp(NightShade, night);
            foreach (var pen in _groundFull) RenderingServer.CanvasItemSetSelfModulate(pen.Item, shade);
            foreach (var pen in _groundCoarse) RenderingServer.CanvasItemSetSelfModulate(pen.Item, shade);
            foreach (var pen in _full) RenderingServer.CanvasItemSetSelfModulate(pen.Item, shade);
            foreach (var pen in _coarse) RenderingServer.CanvasItemSetSelfModulate(pen.Item, shade);
            foreach (var pen in _dyn) RenderingServer.CanvasItemSetSelfModulate(pen.Item, shade);
            foreach (var pen in _lit) RenderingServer.CanvasItemSetSelfModulate(pen.Item, Colors.White with { A = night });
            RenderingServer.CanvasItemSetSelfModulate(_lampGlow.Item, Colors.White with { A = night });
        }
    }

    /// <summary>The tiles on screen, with a margin for tall buildings and people half in view.</summary>
    private Rect2 VisibleTiles()
    {
        var from = (-Origin) / Tile;
        return new Rect2(from, Size / Tile).Grow(1.5f);
    }

    /// <summary>Draw everything that moves or marks, once a frame.</summary>
    private void DrawFrame()
    {
        ulong started = Time.GetTicksUsec();
        foreach (var pen in _dyn) pen.Clear();
        _frameGlow.Clear();
        _marks.Clear();
        _screen.Clear();

        WorldMode();
        _fine = Tile >= 16;
        var view = VisibleTiles();
        float night = Night;
        DrawTraffic(night, view);
        DrawParkedOfficials(night, view);
        DrawPassersBy(view);
        _clickables.Clear();
        DrawStreetPeople(view);
        DrawLoiterers(view);
        DrawFires(view);
        DrawRiverTraffic(view);
        DrawTrain(night, view);
        DrawSmoke(view);
        C = _marks;
        foreach (var lot in World!.Map.Lots)
            if (lot.Use != LotUse.Empty && view.HasPoint(new Vector2(lot.X, lot.Y))) DrawLotOutline(lot);
        if (Live) DrawActors();
        C = _frameGlow;
        foreach (var (at, strength) in _headlights) Glow(at, _t * 0.2f, new Color("fff2c0"), strength * 0.8f);
        _headlights.Clear();

        ScreenMode();
        C = _screen;
        var font = ThemeDB.FallbackFont;
        DrawStreetNames(font, view);
        DrawHeadquartersSigns(font, view);
        DrawCornerSigns(font, view);
        DrawWards(font);
        foreach (var pen in _dyn) pen.Flush();
        _frameGlow.Flush();
        _marks.Flush();
        _screen.Flush();
        OverlayMillis = (Time.GetTicksUsec() - started) / 1000.0;
    }

    /// <summary>Who protects a business, and what's in its back room, as of the live clock.</summary>
    private (int Gang, RacketKind Racket) ShownState(Business b) =>
        Live && _shown.TryGetValue(b.Id, out var shown) ? shown : (b.ProtectorGangId, b.Racket);

    /// <summary>Ownership rims round the whole building, selection and hover, drawn over everything so they read at night.</summary>
    private void DrawLotOutline(Lot lot)
    {
        var w = World!;
        float width = Px(2);
        Color? rim = null;
        if (lot.Use == LotUse.Business)
        {
            var (protector, _) = ShownState(w.BusinessById(lot.BusinessId));
            if (protector >= 0) rim = Palette.Gang(w, protector);
        }
        else if (lot.Use == LotUse.Headquarters)
        {
            rim = Palette.Gang(w, lot.GangId);
            width = CornerOf(lot) != null ? Px(2) : Px(3);
        }

        var outline = Silhouette(lot).Select(p => p * _t).ToArray();
        var closed = outline.Append(outline[0]).ToArray();
        if (rim is Color r) C.DrawPolyline(closed, r with { A = 0.9f }, width);

        bool selected = (lot.Use == LotUse.Business && lot.BusinessId == SelectedBusiness)
            || (lot.Use == LotUse.Bank && SelectedBank >= 0 && w.BankById(SelectedBank)?.LotId == lot.Id)
            || lot.Id == SelectedShop;
        if (selected)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(_anim * 4);
            var grown = Grow(outline, Px(4));
            C.DrawPolyline(grown.Append(grown[0]).ToArray(), Palette.Ink with { A = pulse }, Px(2.5f));
        }
        else if (lot.Id == _hoverLot) C.DrawPolyline(Grow(outline, Px(2)).Append(Grow(outline, Px(2))[0]).ToArray(), Palette.Ink with { A = 0.6f }, Px(1.5f));
    }

    /// <summary>Push every corner of an outline away from its middle.</summary>
    private static Vector2[] Grow(Vector2[] poly, float by)
    {
        var mid = poly.Aggregate(Vector2.Zero, (a, p) => a + p) / poly.Length;
        return poly.Select(p => p + (p - mid).Normalized() * by).ToArray();
    }

    /// <summary>
    /// A standing man, three-quarters on: shadow at his feet, legs, coat, a face and a hat. He leans
    /// the way he walks, and his legs scissor while he does.
    /// </summary>
    private void DrawFigure(Vector2 ground, Vector2 facing, float r, Color coat, Color hat, Color band, bool walking, Color? skin = null, bool woman = false)
    {
        var pen = DynAt(ground.Y / _t);
        var keep = C;
        C = pen;
        float step = walking ? Mathf.Sin(_anim * 10 + ground.X * 0.1f) : 0;
        float side = facing.X >= 0 ? 1 : -1;
        var face = skin ?? new Color("d9b48f");
        var shade = coat.Darkened(0.28f);
        Ellipse(ground + new Vector2(r * 0.3f, 0), new Vector2(r * 0.85f, r * 0.32f), Shadow);
        if (woman)
        {
            // Stockinged legs under a dropped-waist dress, and a cloche hat pulled down to the brow.
            var stocking = face.Darkened(0.15f);
            C.DrawLine(ground + new Vector2(-r * 0.18f, -r * 0.75f), ground + new Vector2(-r * 0.18f + step * r * 0.25f, 0), stocking, r * 0.2f);
            C.DrawLine(ground + new Vector2(r * 0.18f, -r * 0.75f), ground + new Vector2(r * 0.18f - step * r * 0.25f, 0), stocking, r * 0.2f);
            var dress = new[]
            {
                ground + new Vector2(-r * 0.62f, -r * 0.72f), ground + new Vector2(r * 0.62f, -r * 0.72f),
                ground + new Vector2(r * 0.42f, -r * 2.0f), ground + new Vector2(-r * 0.42f, -r * 2.0f),
            };
            C.DrawColoredPolygon(dress, coat);
            C.DrawColoredPolygon(new[] { ground + new Vector2(r * 0.05f, -r * 0.72f), dress[1], dress[2], ground + new Vector2(r * 0.05f, -r * 2.0f) }, shade);
            C.DrawLine(ground + new Vector2(-r * 0.55f, -r * 1.2f), ground + new Vector2(r * 0.55f, -r * 1.2f), coat.Lightened(0.2f), r * 0.1f);
            C.DrawLine(ground + new Vector2(-r * 0.44f, -r * 1.95f), ground + new Vector2(-r * 0.55f - step * r * 0.15f, -r * 1.15f), shade, r * 0.18f);
            var head = ground + new Vector2(side * r * 0.04f, -r * 2.33f);
            C.DrawCircle(head, r * 0.34f, face);
            Ellipse(head + new Vector2(0, -r * 0.12f), new Vector2(r * 0.44f, r * 0.3f), hat);
            Ellipse(head + new Vector2(0, r * 0.02f), new Vector2(r * 0.5f, r * 0.09f), hat.Darkened(0.2f));
        }
        else
        {
            var legs = coat.Darkened(0.6f);
            C.DrawLine(ground + new Vector2(-r * 0.22f, -r * 1.0f), ground + new Vector2(-r * 0.22f + step * r * 0.35f, 0), legs, r * 0.32f);
            C.DrawLine(ground + new Vector2(r * 0.22f, -r * 1.0f), ground + new Vector2(r * 0.22f - step * r * 0.35f, 0), legs, r * 0.32f);
            var body = new[]
            {
                ground + new Vector2(-r * 0.55f, -r * 0.9f), ground + new Vector2(r * 0.55f, -r * 0.9f),
                ground + new Vector2(r * 0.5f, -r * 2.05f), ground + new Vector2(-r * 0.5f, -r * 2.05f),
            };
            C.DrawColoredPolygon(body, coat);
            // The side away from the light, the shirt and tie at the open collar, and an arm swinging.
            C.DrawColoredPolygon(new[] { ground + new Vector2(r * 0.08f, -r * 0.9f), body[1], body[2], ground + new Vector2(r * 0.08f, -r * 2.05f) }, shade);
            C.DrawColoredPolygon(new[] { ground + new Vector2(-r * 0.2f, -r * 2.05f), ground + new Vector2(r * 0.2f, -r * 2.05f), ground + new Vector2(0, -r * 1.6f) }, new Color("e8e2d4"));
            C.DrawLine(ground + new Vector2(0, -r * 2.0f), ground + new Vector2(0, -r * 1.68f), band.Darkened(0.2f), r * 0.1f);
            C.DrawLine(body[0], body[3], coat.Darkened(0.35f), r * 0.12f);
            var shoulder = ground + new Vector2(-r * 0.5f, -r * 1.95f);
            var hand = ground + new Vector2(-r * 0.62f - step * r * 0.25f, -r * 1.05f);
            C.DrawLine(shoulder, hand, shade, r * 0.22f);
            C.DrawCircle(hand, r * 0.12f, face);
            var head = ground + new Vector2(side * r * 0.04f, -r * 2.38f);
            C.DrawCircle(head, r * 0.36f, face);
            Ellipse(head + new Vector2(0, -r * 0.2f), new Vector2(r * 0.6f, r * 0.16f), hat);
            C.DrawRect(new Rect2(head + new Vector2(-r * 0.36f, -r * 0.6f), new Vector2(r * 0.72f, r * 0.4f)), hat);
            C.DrawRect(new Rect2(head + new Vector2(-r * 0.36f, -r * 0.3f), new Vector2(r * 0.72f, r * 0.1f)), band);
        }
        C = keep;
    }

    private float FigureRadius => Mathf.Max(Px(4.5f), _t * 0.15f);

    private void DrawActors()
    {
        float now = Clock;
        float t = _t;
        float radius = FigureRadius;
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

            var pos = at * t;
            bool atScene = now >= actor.Arrive && now <= actor.Leave;

            // Men with a car drive there and back, and get out at the door.
            if (walking && a.Kind is not (ActionKind.Raid or ActionKind.Collect) && CarFor(a.Backup.Prepend(a.HoodId)) is var car and not Car.None)
            {
                DrawGangCar(at, ahead, actor.Colour, car);
                continue;
            }

            // Outcome rings round his feet while the man is on the job.
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
                FlatRing(target * t, t * 0.4f * pulse, ring, Px(2.5f));
            }

            // In a takeover the rival's man stands in the doorway, and shots are traded.
            if (a.Kind is ActionKind.Takeover or ActionKind.Hit && a.DefenderHoodId >= 0 && now >= actor.Arrive - 0.5f && now <= actor.Leave)
            {
                var defPos = (target + new Vector2(0.3f, -0.15f)) * t;
                var dc = Palette.Gang(World!, a.DefenderGangId);
                DrawFigure(defPos, pos - defPos, radius, dc, new Color("1c1a19"), dc, false);
                if (atScene && now - actor.Arrive < 0.8f && Mathf.Sin(_anim * 23 + a.HoodId) > 0.55f)
                    Star(pos.Lerp(defPos, Hash((int)(_anim * 8), a.HoodId) < 0.5f ? 0.3f : 0.7f) - new Vector2(0, radius * 1.6f), radius * 0.7f, new Color("ffe28a"), 6, 0.4f);
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
            if (a.Kind == ActionKind.Guard && atScene) FlatRing(pos, radius * 1.3f, actor.Colour with { A = 0.7f }, Px(1.5f));
        }

        DrawOnTheirWay(now, t, radius);

        // Where someone died this week, a cross stays on the pavement.
        foreach (var actor in _actors)
        {
            var a = actor.Action;
            if (a.CasualtyHoodId < 0 || a.Result == ActionResult.Arrested || now < actor.Arrive) continue;
            if (a.Kind == ActionKind.Raid) continue;
            var c = (actor.Path[^1] + new Vector2(-0.3f, 0.15f)) * t;
            float s = t * 0.12f;
            C.DrawLine(c - new Vector2(s, s * 0.6f), c + new Vector2(s, s * 0.6f), Palette.Bad, Px(2.5f));
            C.DrawLine(c - new Vector2(s, -s * 0.6f), c + new Vector2(s, -s * 0.6f), Palette.Bad, Px(2.5f));
        }
    }

    /// <summary>A ring lying on the pavement, squashed by the angle of view.</summary>
    private void FlatRing(Vector2 centre, float radius, Color colour, float width)
    {
        var pts = new Vector2[25];
        for (int i = 0; i <= 24; i++)
        {
            float a = Mathf.Tau * i / 24;
            pts[i] = centre + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.55f);
        }
        C.DrawPolyline(pts, colour, width);
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
    private void DrawOnTheirWay(float now, float t, float radius)
    {
        if (_sim is not { WeekRunning: true }) return;
        foreach (var job in _sim.Upcoming)
        {
            if (!Simulation.TeamOf(job.Order).Any()) continue;
            var gang = World!.GangById(job.GangId);
            int toLot = Simulation.TargetLot(World, job.Order);
            var team = Simulation.TeamOf(job.Order).ToList();
            float hours = Simulation.TravelHours(World, gang.HqLotId, toLot, Men(team));
            if (now < job.Tick - hours || now >= job.Tick) continue;
            var path = StreetPath(gang.HqLotId, toLot);
            float f = (now - (job.Tick - hours)) / hours;
            var at = Along(path, f);
            var pos = at * t;
            var ahead = Along(path, f + 0.02f) - at;
            var colour = Palette.Gang(World, gang.Id);
            if (CarFor(team) is var car and not Car.None) { DrawGangCar(at, ahead, colour, car); continue; }
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

    public static string ClockText(float tick) => Reports.Hour((int)tick);
}
