using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The streets don't run ruler-straight. Everything on the map is drawn through a gentle bend, so the streets and
/// avenues curve and the blocks between them lean at odd angles: a little all over town, and a lot in the old town
/// by downtown, where the streets were laid out along cow paths before anyone drew a grid. The plan underneath
/// keeps its grid, so walking, driving and every block's lots are just as they were; only the drawing bends, and
/// picking on the map bends back the same way.
/// </summary>
public partial class MapView : IPlan
{
    /// <summary>How long the bends are, in drawn tiles: the streets' sway across town and the avenues' down it.</summary>
    private const float StreetSway = 7, AvenueSway = 6;

    /// <summary>How far, in tiles, the lines stray all over town, and how much further in the old town.</summary>
    private const float TownBend = 0.3f, OldTownBend = 1.1f;

    /// <summary>How far in from the edge of the city the bend fades in, so the city's outline stays square.</summary>
    private const float EdgeFade = 8;

    private CityMap? _bentFor;
    private Vector2 _oldTown, _drawnSize;
    private float _oldTownRadius;
    private Vector4 _sway;

    /// <summary>
    /// Where the old town is and which way the streets bend, worked out from the map alone so the city looks the
    /// same every time it's opened: the old town takes in downtown and the blocks just below it.
    /// </summary>
    private void LayBend()
    {
        if (_bentFor == Map) return;
        _bentFor = Map;
        uint salt = 2166136261;
        foreach (char ch in string.Concat(Map.StreetNames) + string.Concat(Map.AvenueNames)) salt = (salt ^ ch) * 16777619;
        int seed = (int)(salt & 0xFFFFFF);
        _sway = new Vector4(Hash(seed, 1600), Hash(seed, 1601), Hash(seed, 1602), Hash(seed, 1603)) * Mathf.Tau;
        var downtown = Enumerable.Range(0, Map.BlocksX * Map.BlocksY).Select(i => (X: i % Map.BlocksX, Y: i / Map.BlocksX))
            .Where(b => Map.ZoneOf(b.X, b.Y) == Zone.Downtown).ToList();
        var middle = downtown.Count == 0
            ? new Vector2(Map.BlocksX / 2f, Map.BlocksY / 2f)
            : new Vector2((float)downtown.Average(b => b.X) + 0.5f, (float)downtown.Average(b => b.Y) + 1.5f);
        _oldTown = Widen(new Vector2(middle.X * CityMap.StrideX, Mathf.Min(middle.Y, Map.BlocksY) * CityMap.StrideY));
        _drawnSize = DrawnSize;
        _oldTownRadius = Mathf.Max(10, 0.33f * Mathf.Max(_drawnSize.X, _drawnSize.Y));
    }

    /// <summary>How far the lines stray at a drawn point: more in the old town, fading out toward the edge of the city.</summary>
    private float BendAt(Vector2 d)
    {
        float old = 1 - Mathf.SmoothStep(_oldTownRadius * 0.35f, _oldTownRadius, d.DistanceTo(_oldTown));
        float edge = Mathf.Min(Mathf.Min(d.X, _drawnSize.X - d.X), Mathf.Min(d.Y, _drawnSize.Y - d.Y));
        return (TownBend + OldTownBend * old) * Mathf.SmoothStep(0, EdgeFade, edge);
    }

    /// <summary>How far a drawn point moves: across, by how far down the map it is, and down, by how far across.</summary>
    private Vector2 Bend(Vector2 d)
    {
        float a = BendAt(d);
        return new Vector2(
            a * (Mathf.Sin(d.Y / StreetSway + _sway.X) + 0.4f * Mathf.Sin(d.Y / (StreetSway * 0.47f) + _sway.Y)),
            a * 0.8f * (Mathf.Sin(d.X / AvenueSway + _sway.Z) + 0.4f * Mathf.Sin(d.X / (AvenueSway * 0.53f) + _sway.W)));
    }

    /// <summary>A point of the plan, in tiles, where it's drawn: widened for the roadway, then bent.</summary>
    private Vector2 Drawn(Vector2 plan)
    {
        LayBend();
        var d = Widen(plan);
        return d + Bend(d);
    }

    /// <summary>A drawn point back to the plan: the bend undone a step at a time, then the widening.</summary>
    private Vector2 Undrawn(Vector2 drawn)
    {
        LayBend();
        var d = drawn;
        for (int i = 0; i < 8; i++) d = drawn - Bend(d);
        return Narrow(d);
    }

    /// <summary>
    /// How the bend turns and stretches things at a point of the plan, so something drawn rigid round it, a car say,
    /// lines up with the street it's on.
    /// </summary>
    private (Vector2 X, Vector2 Y) BendAxes(Vector2 plan)
    {
        const float e = 0.05f;
        var d = Widen(plan);
        var at = d + Bend(d);
        var dx = d + new Vector2(e, 0);
        var dy = d + new Vector2(0, e);
        return ((dx + Bend(dx) - at) / e, (dy + Bend(dy) - at) / e);
    }

    /// <summary>The same bend, for the crowd shader. Its uniforms are set with <see cref="BendCrowd"/>.</summary>
    private const string BendShader = @"
uniform vec2 old_town;
uniform float old_town_radius;
uniform vec2 drawn_size;
uniform vec4 sway;
vec2 bend(vec2 d) {
    float old = 1.0 - smoothstep(old_town_radius * 0.35, old_town_radius, distance(d, old_town));
    float rim = min(min(d.x, drawn_size.x - d.x), min(d.y, drawn_size.y - d.y));
    float a = (0.3 + 1.1 * old) * smoothstep(0.0, 8.0, rim);
    return vec2(
        a * (sin(d.y / 7.0 + sway.x) + 0.4 * sin(d.y / (7.0 * 0.47) + sway.y)),
        a * 0.8 * (sin(d.x / 6.0 + sway.z) + 0.4 * sin(d.x / (6.0 * 0.53) + sway.w)));
}
vec2 drawn(vec2 p) { vec2 d = widen2(p); return d + bend(d); }
";

    private void BendCrowd(ShaderMaterial material)
    {
        LayBend();
        material.SetShaderParameter("old_town", _oldTown);
        material.SetShaderParameter("old_town_radius", _oldTownRadius);
        material.SetShaderParameter("drawn_size", _drawnSize);
        material.SetShaderParameter("sway", _sway);
    }

    Vector2 IPlan.At(float x, float y) => P(x, y);
    float IPlan.Scale => _t;
}

/// <summary>Whatever draws a plan: where a point of it lands on screen.</summary>
public interface IPlan
{
    Vector2 At(float x, float y);

    /// <summary>How big a tile of the plan is drawn.</summary>
    float Scale { get; }
}

/// <summary>
/// A rectangle of the plan, as drawn: its corners land wherever the bend puts them, so a big one is drawn as a
/// mesh of smaller ones to follow the curve. Where a plain rectangle is wanted it gives its bounding box.
/// </summary>
public readonly struct Area
{
    public readonly IPlan Plan;
    public readonly float X, Y, W, H;

    public Area(IPlan plan, float x, float y, float w, float h)
    {
        Plan = plan;
        X = x;
        Y = y;
        W = w;
        H = h;
    }

    /// <summary>The rectangle grown or shrunk all round by a distance on screen.</summary>
    public Area Grow(float by)
    {
        float t = by / Plan.Scale;
        return new(Plan, X - t, Y - t, W + 2 * t, H + 2 * t);
    }

    /// <summary>The rectangle grown out on one side by a distance on screen.</summary>
    public Area GrowSide(Side side, float by)
    {
        float t = by / Plan.Scale;
        return side switch
        {
            Side.Left => new(Plan, X - t, Y, W + t, H),
            Side.Top => new(Plan, X, Y - t, W, H + t),
            Side.Right => new(Plan, X, Y, W + t, H),
            _ => new(Plan, X, Y, W, H + t),
        };
    }

    public bool Same(Area o) => X == o.X && Y == o.Y && W == o.W && H == o.H;

    public Vector2 Corner(float u, float v) => Plan.At(X + W * u, Y + H * v);

    public static implicit operator Rect2(Area a)
    {
        Vector2 p = a.Corner(0, 0), q = a.Corner(1, 0), r = a.Corner(1, 1), s = a.Corner(0, 1);
        var min = new Vector2(Mathf.Min(Mathf.Min(p.X, q.X), Mathf.Min(r.X, s.X)), Mathf.Min(Mathf.Min(p.Y, q.Y), Mathf.Min(r.Y, s.Y)));
        var max = new Vector2(Mathf.Max(Mathf.Max(p.X, q.X), Mathf.Max(r.X, s.X)), Mathf.Max(Mathf.Max(p.Y, q.Y), Mathf.Max(r.Y, s.Y)));
        return new Rect2(min, max - min);
    }

    /// <summary>How many pieces across and down it's drawn in, so no piece is much more than a tile.</summary>
    public (int Across, int Down) Pieces => (Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(W) / 1.2f)), Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(H) / 1.2f)));
}
