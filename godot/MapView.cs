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

    private float Tile => Mathf.Floor(Mathf.Min(Size.X / CityMap.Width, Size.Y / CityMap.Height));
    private Vector2 Origin => (Size - new Vector2(CityMap.Width, CityMap.Height) * Tile) / 2;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = " "; // enables _GetTooltip
        Resized += QueueRedraw;
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
        if (e is InputEventMouseMotion motion)
        {
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
        float t = Tile;
        var o = Origin;
        var font = ThemeDB.FallbackFont;

        DrawRect(new Rect2(o, new Vector2(CityMap.Width, CityMap.Height) * t), Palette.Street);

        // Blocks, with a faint kerb line along the streets.
        for (int by = 0; by < CityMap.BlocksY; by++)
        for (int bx = 0; bx < CityMap.BlocksX; bx++)
        {
            var r = new Rect2(o + new Vector2(bx * CityMap.StrideX + 1, by * CityMap.StrideY + 1) * t,
                new Vector2(CityMap.LotsPerBlockX, CityMap.LotsPerBlockY) * t);
            DrawRect(r.Grow(2), Palette.StreetLine);
            DrawRect(r, Palette.Block);
        }

        // Street names along the rows, avenue names down the columns.
        int labelSize = Mathf.Max(9, (int)(t * 0.32f));
        for (int i = 0; i < w.Map.StreetNames.Length; i++)
        {
            float y = o.Y + (i * CityMap.StrideY + 0.5f) * t + labelSize * 0.35f;
            DrawString(font, new Vector2(o.X + t * 1.1f, y), $"{w.Map.StreetNames[i].ToUpperInvariant()} ST", HorizontalAlignment.Left, -1, labelSize, Palette.InkQuiet with { A = 0.55f });
        }

        foreach (var lot in w.Map.Lots) DrawLot(lot, o, t, font);

        if (Live) DrawActors(o, t, font);
    }

    private void DrawLot(Lot lot, Vector2 o, float t, Font font)
    {
        var w = World!;
        var r = new Rect2(o + new Vector2(lot.X, lot.Y) * t, new Vector2(t, t)).Grow(-2);
        switch (lot.Use)
        {
            case LotUse.Business:
            {
                var b = w.BusinessById(lot.BusinessId);
                var (protector, racket) = Live && _shown.TryGetValue(b.Id, out var shown) ? shown : (b.ProtectorGangId, b.Racket);
                var fill = protector >= 0 ? Palette.Gang(w, protector).Darkened(0.35f) : Palette.Lot;
                if (!b.IsOpen && !Live) fill = fill.Darkened(0.5f);
                DrawRect(r, fill);
                if (protector >= 0) DrawRect(r, Palette.Gang(w, protector), false, 2);
                // Door on the street side.
                float doorY = lot.FrontY < lot.Y ? r.Position.Y : r.End.Y - 3;
                DrawRect(new Rect2(r.Position.X + r.Size.X * 0.4f, doorY, r.Size.X * 0.2f, 3), Palette.Ink with { A = 0.5f });
                string glyph = Glyph(b);
                int size = (int)(t * 0.42f);
                DrawString(font, new Vector2(r.Position.X, r.GetCenter().Y + size * 0.36f), glyph, HorizontalAlignment.Center, r.Size.X, size, Palette.Ink with { A = 0.85f });
                if (racket != RacketKind.None)
                    DrawCircle(r.Position + new Vector2(r.Size.X - 5, 5), 3.5f, Palette.Bad);
                break;
            }
            case LotUse.Headquarters:
            {
                var c = Palette.Gang(w, lot.GangId);
                DrawRect(r, c.Darkened(0.55f));
                DrawRect(r, c, false, 3);
                int size = (int)(t * 0.34f);
                DrawString(font, new Vector2(r.Position.X, r.GetCenter().Y + size * 0.36f), "HQ", HorizontalAlignment.Center, r.Size.X, size, c);
                break;
            }
            case LotUse.Precinct:
            {
                DrawRect(r, Palette.Police.Darkened(0.6f));
                DrawRect(r, Palette.Police, false, 2);
                int size = (int)(t * 0.42f);
                DrawString(font, new Vector2(r.Position.X, r.GetCenter().Y + size * 0.36f), "P", HorizontalAlignment.Center, r.Size.X, size, Palette.Police);
                break;
            }
            default:
                DrawRect(r, Palette.LotEmpty);
                break;
        }

        bool selected = lot.Use == LotUse.Business && lot.BusinessId == SelectedBusiness;
        if (selected) DrawRect(r.Grow(3), Palette.Ink, false, 2);
        else if (lot.Id == _hoverLot && lot.Use != LotUse.Empty) DrawRect(r.Grow(2), Palette.Ink with { A = 0.5f }, false, 1);
    }

    /// <summary>One letter per trade so a glance tells a hotel from a barber.</summary>
    private static string Glyph(Business b) => b.Kind switch
    {
        BusinessKind.Grocer => "G",
        BusinessKind.Diner => "D",
        BusinessKind.Barber => "B",
        BusinessKind.Tailor => "T",
        BusinessKind.Garage => "A",
        BusinessKind.Laundry => "L",
        BusinessKind.Hotel => "H",
        BusinessKind.Pharmacy => "Rx",
        BusinessKind.PoolHall => "8",
        BusinessKind.Warehouse => "W",
        _ => "?",
    };

    private void DrawActors(Vector2 o, float t, Font font)
    {
        float now = Clock;
        foreach (var actor in _actors)
        {
            if (now < actor.Depart || now > actor.Home + 0.01f) continue;
            var a = actor.Action;
            var target = actor.Path[^1];
            Vector2 at;
            if (now < actor.Arrive) at = Along(actor.Path, (now - actor.Depart) / (actor.Arrive - actor.Depart));
            else if (now <= actor.Leave) at = target;
            else at = Along(actor.Path, 1 - (now - actor.Leave) / Mathf.Max(0.01f, actor.Home - actor.Leave));

            var pos = o + at * t;
            float radius = Mathf.Max(4.5f, t * 0.22f);
            bool atScene = now >= actor.Arrive && now <= actor.Leave;

            // Outcome rings while the hood is on the job.
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
                DrawArc(o + target * t, t * 0.55f * pulse, 0, Mathf.Tau, 24, ring, 2.5f);
            }

            // In a takeover the rival's man stands in the doorway.
            if (a.Kind == ActionKind.Takeover && a.DefenderHoodId >= 0 && now >= actor.Arrive - 0.5f && now <= actor.Leave)
            {
                var defPos = o + (target + new Vector2(0.28f, -0.22f)) * t;
                DrawCircle(defPos, radius, Palette.Gang(World!, a.DefenderGangId));
                DrawArc(defPos, radius, 0, Mathf.Tau, 16, Palette.Background, 1.5f);
            }

            bool fallen = a.CasualtyHoodId >= 0 && a.CasualtyHoodId == a.HoodId && now >= actor.Arrive;
            if (fallen) continue; // drawn below as a cross

            DrawCircle(pos, radius, actor.Colour);
            DrawArc(pos, radius, 0, Mathf.Tau, 16, Palette.Background, 1.5f);
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
