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

    /// <summary>Raised once per script action as the live clock passes the moment it happens.</summary>
    public event Action<ScriptAction>? ActionHappened;

    public World? World { get; set; }
    public int SelectedBusiness { get; set; } = -1;

    public bool Live { get; private set; }
    public float Clock { get; private set; }

    private int _hoverLot = -1;

    /// <summary>During replay, who each business answers to as of the live clock.</summary>
    private Dictionary<int, (int Gang, RacketKind Racket)> _shown = new();
    private int _nextAction;
    private List<Actor> _actors = new();

    /// <summary>Seconds of real time, for scenery that moves whether or not the week is running.</summary>
    private float _anim;

    private float _zoom = 1;
    private Vector2 _pan;
    private bool _dragging;

    private float FitTile => Mathf.Floor(Mathf.Min(Size.X / (CityMap.Width + 0.6f), Size.Y / (CityMap.Height + 0.6f)));
    private float Tile => FitTile * _zoom;
    private Vector2 Origin => (Size - new Vector2(CityMap.Width, CityMap.Height) * Tile) / 2 + _pan;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TooltipText = " "; // enables _GetTooltip
        Resized += QueueRedraw;
        BuildTraffic();
    }

    public override void _Process(double delta)
    {
        _anim += (float)delta;
        QueueRedraw();
    }

    /// <summary>Zoom about a point on screen, keeping whatever is under it in place.</summary>
    private void ZoomAt(Vector2 screen, float factor)
    {
        var tileUnder = (screen - Origin) / Tile;
        _zoom = Mathf.Clamp(_zoom * factor, 1, 4);
        if (_zoom <= 1.001f) { _pan = Vector2.Zero; return; }
        _pan += screen - (Origin + tileUnder * Tile);
        ClampPan();
    }

    /// <summary>Zoom about the middle of the map, for screenshots.</summary>
    public void ZoomCentre(float zoom) => ZoomAt(Size / 2, zoom / _zoom);

    private void ClampPan()
    {
        var excess = (new Vector2(CityMap.Width, CityMap.Height) * Tile - Size) / 2 + Vector2.One * Tile;
        excess = excess.Max(Vector2.Zero);
        _pan = _pan.Clamp(-excess, excess);
    }

    // ---- Live replay ----------------------------------------------------------

    private sealed record Actor(ScriptAction Action, List<Vector2> Path, float Depart, float Arrive, float Leave, float Home, Color Colour, int Order);

    /// <summary>Start replaying the week that just ran. The map shows the state at the start of that week until each change happens.</summary>
    public void BeginReplay(Dictionary<int, (int Gang, RacketKind Racket)> startOfWeek)
    {
        if (World == null) return;
        _shown = new Dictionary<int, (int, RacketKind)>(startOfWeek);
        Live = true;
        Clock = 0;
        _nextAction = 0;
        _actors = new List<Actor>();
        int collectIndex = 0;
        for (int i = 0; i < World.Script.Count; i++)
        {
            var a = World.Script[i];
            var from = World.Map.LotAt(a.FromLot);
            var to = World.Map.LotAt(a.ToLot);
            var path = World.Map.Path(from, to).Select(p => new Vector2(p.X + 0.5f, p.Y + 0.5f)).ToList();
            // Men leave from one front door and stand at another, out on the pavement.
            path[0] = Doorstep(from);
            path[^1] = Doorstep(to);
            float hours = Mathf.Clamp(World.Map.Distance(from, to) / 7f, 0.75f, 3f);
            float arrive = a.Tick;
            // Sunday collectors set out in waves rather than all at once.
            if (a.Kind == ActionKind.Collect) arrive += (collectIndex++ % 10) * 0.6f;
            float linger = a.Kind switch { ActionKind.Takeover => 1.5f, ActionKind.Raid => 2f, ActionKind.Collect => 0.4f, _ => 1f };
            float leave = a.Kind == ActionKind.Guard ? Content.HoursPerWeek : arrive + linger;
            bool comesHome = a.Result is not (ActionResult.Killed or ActionResult.Arrested) && a.CasualtyHoodId != a.HoodId;
            float home = comesHome ? leave + hours : leave;
            var colour = a.Kind == ActionKind.Raid ? Palette.Police : Palette.Gang(World, a.GangId);
            _actors.Add(new Actor(a, path, arrive - hours, arrive, leave, home, colour, i));
        }
        QueueRedraw();
    }

    private static Vector2 Doorstep(Lot lot) => new(lot.X + 0.5f, lot.FrontY < lot.Y ? lot.Y - 0.1f : lot.Y + 1.1f);

    public void EndReplay()
    {
        Live = false;
        _actors.Clear();
        QueueRedraw();
    }

    /// <summary>Advance the live clock by some hours of game time.</summary>
    public void Advance(float hours)
    {
        if (!Live) return;
        Clock = Mathf.Min(Clock + hours, Content.HoursPerWeek);
        while (_nextAction < _actors.Count && ArrivalOf(_nextAction) <= Clock)
        {
            var a = _actors[_nextAction].Action;
            if (a.BusinessId >= 0 && _shown.TryGetValue(a.BusinessId, out var was))
            {
                _shown[a.BusinessId] = a switch
                {
                    { Kind: ActionKind.Extort, Result: ActionResult.Success } => (a.GangId, was.Racket),
                    { Kind: ActionKind.Takeover, Result: ActionResult.Won } => (a.GangId, was.Racket),
                    { Kind: ActionKind.Racket, Result: ActionResult.Success } => (was.Gang, World.BusinessById(a.BusinessId).Racket is var k and not RacketKind.None ? k : RacketKind.Numbers),
                    { Kind: ActionKind.Raid } => (was.Gang, RacketKind.None),
                    _ => was,
                };
            }
            ActionHappened?.Invoke(a);
            _nextAction++;
        }
        QueueRedraw();
    }

    private float ArrivalOf(int index) => _actors[index].Arrive;

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
            if (lot != _hoverLot) { _hoverLot = lot; QueueRedraw(); }
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
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
                return $"{b.Name}\nTakes ${b.Takings} a week · owner toughness {b.Toughness}/10\n{owner}{racket}{shut}";
            case LotUse.Headquarters:
                var g = World.Gangs.First(x => x.Id == lot.GangId);
                return $"Headquarters of {g.Name}";
            case LotUse.Precinct:
                return "Precinct house";
            default:
                return "";
        }
    }

    // ---- Drawing --------------------------------------------------------------

    public override void _Draw()
    {
        if (World == null) return;
        var w = World;
        _t = Tile;
        _o = Origin;
        _headlights.Clear();
        var font = ThemeDB.FallbackFont;
        float night = Night;

        DrawGround();
        DrawShadows();
        foreach (var lot in w.Map.Lots) DrawLot(lot, night);
        DrawLampPosts(night);
        DrawStreetNames(font);
        DrawTraffic(night);
        DrawPassersBy();
        DrawNight(night);
        foreach (var lot in w.Map.Lots) DrawLotOutline(lot);

        if (Live) DrawActors(font);
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
                        DrawLine(new Vector2(x, f.Position.Y + _t * 0.08f), new Vector2(x, f.End.Y - _t * 0.08f), roof.Darkened(0.15f), 1);
                }
                if (b.Kind == BusinessKind.Hotel)
                {
                    // A water tower on its stilts.
                    var c = P(lot.X + 0.78f, lot.Y + (lot.FrontY < lot.Y ? 0.78f : 0.24f));
                    DrawCircle(c + Vector2.One * _t * 0.08f, _t * 0.12f, Shadow);
                    DrawCircle(c, _t * 0.12f, new Color("6b5038"));
                    DrawArc(c, _t * 0.12f, 0, Mathf.Tau, 14, new Color("3a2a1e"), Mathf.Max(1, _t * 0.025f));
                    DrawCircle(c, _t * 0.04f, new Color("3a2a1e"));
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
            if (protector >= 0) DrawRect(r, Palette.Gang(w, protector), false, Mathf.Max(2, _t * 0.06f));
        }
        else if (lot.Use == LotUse.Headquarters)
            DrawRect(r, Palette.Gang(w, lot.GangId), false, Mathf.Max(2, _t * 0.09f));

        bool selected = lot.Use == LotUse.Business && lot.BusinessId == SelectedBusiness;
        if (selected)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(_anim * 4);
            DrawRect(r.Grow(Mathf.Max(3, _t * 0.08f)), Palette.Ink with { A = pulse }, false, Mathf.Max(2, _t * 0.06f));
        }
        else if (lot.Id == _hoverLot && lot.Use != LotUse.Empty) DrawRect(r.Grow(2), Palette.Ink with { A = 0.6f }, false, 1.5f);
    }

    /// <summary>A man seen from above: shoulders in his coat, a hat on top, a shadow under him.</summary>
    private void DrawFigure(Vector2 pos, Vector2 facing, float r, Color coat, Color hat, Color band, bool walking)
    {
        float angle = facing.LengthSquared() > 0.0001f ? facing.Angle() : 0;
        float sway = walking ? Mathf.Sin(_anim * 10 + pos.X * 0.1f) * 0.15f : 0;
        Ellipse(pos + new Vector2(r * 0.35f, r * 0.45f), new Vector2(r * 0.75f, r * 1.05f), Shadow, angle);
        Ellipse(pos, new Vector2(r * 0.62f, r * 1.0f), coat.Darkened(0.55f), angle + sway, 16);
        Ellipse(pos, new Vector2(r * 0.52f, r * 0.9f), coat, angle + sway, 16);
        DrawCircle(pos + facing.Normalized() * r * 0.08f, r * 0.62f, hat);
        DrawArc(pos + facing.Normalized() * r * 0.08f, r * 0.42f, 0, Mathf.Tau, 14, band, Mathf.Max(1, r * 0.16f));
        DrawCircle(pos + facing.Normalized() * r * 0.08f, r * 0.3f, hat.Lightened(0.12f));
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
                DrawArc(o + target * t, t * 0.4f * pulse, 0, Mathf.Tau, 24, ring, 2.5f);
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

            bool fallen = a.CasualtyHoodId >= 0 && a.CasualtyHoodId == a.HoodId && now >= actor.Arrive;
            if (fallen) continue; // drawn below as a cross

            if (a.Kind == ActionKind.Raid)
                DrawFigure(pos, ahead, radius, Palette.Police, new Color("1b2a4a"), Palette.Police.Lightened(0.3f), walking);
            else
                DrawFigure(pos, ahead, radius, actor.Colour, new Color("1c1a19"), actor.Colour.Lightened(0.25f), walking);
            if (a.Kind == ActionKind.Guard && atScene) DrawArc(pos, radius + 3, 0, Mathf.Tau, 16, actor.Colour with { A = 0.6f }, 1.5f);
        }

        // Where someone died this week, a cross stays on the pavement.
        foreach (var actor in _actors)
        {
            var a = actor.Action;
            if (a.CasualtyHoodId < 0 || a.Result == ActionResult.Arrested || now < actor.Arrive) continue;
            if (a.Kind == ActionKind.Raid) continue;
            var c = o + (actor.Path[^1] + new Vector2(-0.3f, 0.3f)) * t;
            float s = t * 0.14f;
            DrawLine(c - new Vector2(s, s), c + new Vector2(s, s), Palette.Bad, 2.5f);
            DrawLine(c - new Vector2(s, -s), c + new Vector2(s, -s), Palette.Bad, 2.5f);
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
