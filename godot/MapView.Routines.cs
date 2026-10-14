using System;
using DryTown.Core;
using Godot;

/// <summary>
/// Everyone's day, from the city directory: where each resident goes and when. The map writes each day into a texture
/// once, and the crowd shader reads it to walk people from their door to their work, the shops or their buildings and
/// back along the streets. The processor works out the same thing for anyone near a click, so they can be picked out.
/// </summary>
public partial class MapView
{
    /// <summary>Floats each resident's day takes in the texture: six texels of four.</summary>
    private const int PlanFloats = 24;

    /// <summary>Texels across the texture of days.</summary>
    private const int PlansWide = 4096;

    /// <summary>Everyone's day, by their place in the crowd, laid out as the shader reads it.</summary>
    private float[] _plans = Array.Empty<float>();
    private ImageTexture? _plansTexture;

    /// <summary>Where each block's residents start in the crowd's order.</summary>
    private int[,] _crowdStart = new int[0, 0];

    /// <summary>
    /// The hour the crowd keeps. While a week runs it's the clock; between weeks the city carries on through a working
    /// day at a minute a second, so people are seen coming and going while you give orders.
    /// </summary>
    private float CrowdHour => Live ? Clock % 24 : 9.5f + Mathf.PosMod(_anim / 60f, 8f);

    /// <summary>A spot on the pavement in front of a lot, a little to one side for each person so they don't stand on each other.</summary>
    private static Vector2 FrontPavement(Lot lot, float jitter) =>
        new(lot.X + 0.5f + jitter, lot.FrontY < lot.Y ? lot.Y - 0.1f : lot.Y + 1.1f);

    /// <summary>
    /// The pavement x where someone walking between two doors on different streets goes up or down the avenue, or -1
    /// when they're on the same pavement and walk straight there. It's the avenue the map's own paths use.
    /// </summary>
    private float AvenueBetween(Vector2 from, Vector2 to)
    {
        if (Mathf.Abs(from.Y - to.Y) < 0.01f) return -1;
        var map = World!.Map;
        int best = map.WalkAvenue(from.X, to.X, Mathf.RoundToInt(from.Y / CityMap.StrideY), Mathf.RoundToInt(to.Y / CityMap.StrideY));
        // Down the near side of the avenue.
        return best + (from.X > best + 0.5f ? 0.9f : 0.1f);
    }

    /// <summary>Write everyone's day into the texture the crowd shader reads.</summary>
    private void BuildPlans()
    {
        var map = World!.Map;
        var d = Directory!;
        _plans = new float[Population * PlanFloats];
        _crowdStart = new int[map.BlocksX, map.BlocksY];
        int i = 0;
        for (int by = 0; by < map.BlocksY; by++)
        for (int bx = 0; bx < map.BlocksX; bx++)
        {
            _crowdStart[bx, by] = i;
            for (int n = 0; n < _crowdBlocks[bx, by]; n++, i++)
            {
                int id = d.IdOf(bx, by, n);
                if (!d.Exists(id)) continue;
                var routine = d.RoutineOf(id);
                int k = i * PlanFloats;
                _plans[k + 20] = (Hash(id, 970) - 0.5f) * 0.14f;
                if (routine.Kind == RoutineKind.Loafer) continue;
                float jitter = (Hash(id, 971) - 0.5f) * 0.6f;
                var home = FrontPavement(map.LotAt(d.Get(id).HomeLotId), jitter);
                int stops = routine.Stops.Count;
                // The places in order: home, each stop, and home again.
                var points = new Vector2[stops + 2];
                points[0] = home;
                for (int s = 0; s < stops; s++) points[s + 1] = FrontPavement(map.LotAt(routine.Stops[s].LotId), jitter);
                // In the park they sit about on the grass, not on the pavement.
                if (routine.Kind == RoutineKind.Park)
                {
                    var park = map.LotAt(routine.Stops[0].LotId);
                    points[1] = new Vector2(park.X + 0.15f + Hash(id, 972) * 0.7f, park.Y + 0.15f + Hash(id, 973) * 0.7f);
                }
                points[stops + 1] = home;
                // Room for three stops; a shorter day fills the rest with home.
                for (int s = 0; s < 3; s++)
                {
                    var p = points[Math.Min(s + 1, stops + 1)];
                    _plans[k + s * 2 + 2] = p.X;
                    _plans[k + s * 2 + 3] = p.Y;
                }
                _plans[k + 0] = home.X;
                _plans[k + 1] = home.Y;
                // When they set out from each place.
                _plans[k + 8] = routine.LeaveHome;
                for (int s = 0; s < 3; s++) _plans[k + 9 + s] = s < stops ? routine.Stops[s].Leave : 0;
                for (int s = 0; s < 3; s++) _plans[k + 12 + s] = s < stops ? routine.Stops[s].Outside : 0;
                _plans[k + 15] = stops;
                for (int s = 0; s < 4; s++) _plans[k + 16 + s] = s <= stops ? AvenueBetween(points[s], points[s + 1]) : -1;
            }
        }
        int rows = Math.Max(1, (Population * PlanFloats / 4 + PlansWide - 1) / PlansWide);
        var bytes = new byte[PlansWide * rows * 16];
        Buffer.BlockCopy(_plans, 0, bytes, 0, _plans.Length * 4);
        var image = Image.CreateFromData(PlansWide, rows, false, Image.Format.Rgbaf, bytes);
        _plansTexture = ImageTexture.CreateFromImage(image);
        _crowdMaterial!.SetShaderParameter("plans", _plansTexture);
        _crowdMaterial.SetShaderParameter("plans_wide", PlansWide);
        _crowdMaterial.SetShaderParameter("pace", Content.ResidentPace);
    }

    /// <summary>
    /// Where someone with somewhere to be is at an hour, the same way the shader works it out: walking between places
    /// along the streets, or standing at a door for a while on arriving. Null while they're indoors, or for a loafer.
    /// </summary>
    private Vector2? PlanSpot(int index, float hour)
    {
        int k = index * PlanFloats;
        if (k + PlanFloats > _plans.Length) return null;
        int stops = (int)_plans[k + 15];
        if (stops == 0) return null;
        // Places 0 to 3 are written in order; the fifth is home again.
        Vector2 Point(int s) => s >= 4 ? new(_plans[k], _plans[k + 1]) : new(_plans[k + s * 2], _plans[k + s * 2 + 1]);
        float Leave(int s) => _plans[k + 8 + s];
        // Each walk: from place s to s + 1, setting out at Leave(s).
        float end = 0;
        var arrive = new float[stops + 1];
        for (int s = 0; s <= stops; s++)
        {
            arrive[s] = Leave(s) + PathLength(Point(s), Point(s + 1), _plans[k + 16 + s]) / Content.ResidentPace;
            end = arrive[s];
        }
        // A day that runs past midnight is still going in the small hours.
        if (hour < Leave(0) && hour + 24 < end) hour += 24;
        if (hour < Leave(0) || hour >= end) return null;
        for (int s = 0; s <= stops; s++)
        {
            if (hour < arrive[s])
            {
                float walk = arrive[s] - Leave(s);
                float f = walk <= 0 ? 1 : (hour - Leave(s)) / walk;
                return AlongStreets(Point(s), Point(s + 1), _plans[k + 16 + s], f) + new Vector2(0, _plans[k + 20]);
            }
            if (s < stops && hour < Leave(s + 1))
                return hour < arrive[s] + _plans[k + 12 + s] ? Point(s + 1) + new Vector2(0, _plans[k + 20]) : null;
        }
        return null;
    }

    private static float PathLength(Vector2 from, Vector2 to, float avenue) =>
        avenue < 0 ? Mathf.Abs(to.X - from.X) : Mathf.Abs(avenue - from.X) + Mathf.Abs(to.Y - from.Y) + Mathf.Abs(to.X - avenue);

    /// <summary>A share of the way along the walk between two doors: along the street, up or down the avenue, along the next street.</summary>
    private static Vector2 AlongStreets(Vector2 from, Vector2 to, float avenue, float f)
    {
        float total = PathLength(from, to, avenue);
        if (total <= 0) return to;
        float d = Mathf.Clamp(f, 0, 1) * total;
        if (avenue < 0) return new Vector2(from.X + Mathf.Sign(to.X - from.X) * d, from.Y);
        float first = Mathf.Abs(avenue - from.X), second = Mathf.Abs(to.Y - from.Y);
        if (d < first) return new Vector2(from.X + Mathf.Sign(avenue - from.X) * d, from.Y);
        d -= first;
        if (d < second) return new Vector2(avenue, from.Y + Mathf.Sign(to.Y - from.Y) * d);
        d -= second;
        return new Vector2(avenue + Mathf.Sign(to.X - avenue) * d, to.Y);
    }

    /// <summary>The shader's half of the same: where someone is, from their day in the texture.</summary>
    private const string PlanShader = @"
uniform sampler2D plans : filter_nearest;
uniform int plans_wide;
uniform float hour;
uniform float pace;
vec4 plan(int i) { return texelFetch(plans, ivec2(i % plans_wide, i / plans_wide), 0); }
float path_length(vec2 a, vec2 b, float av) {
    return av < 0.0 ? abs(b.x - a.x) : abs(av - a.x) + abs(b.y - a.y) + abs(b.x - av);
}
vec2 along_streets(vec2 a, vec2 b, float av, float f) {
    float total = path_length(a, b, av);
    if (total <= 0.0) return b;
    float d = clamp(f, 0.0, 1.0) * total;
    if (av < 0.0) return vec2(a.x + sign(b.x - a.x) * d, a.y);
    float first = abs(av - a.x);
    float second = abs(b.y - a.y);
    if (d < first) return vec2(a.x + sign(av - a.x) * d, a.y);
    d -= first;
    if (d < second) return vec2(av, a.y + sign(b.y - a.y) * d);
    d -= second;
    return vec2(av + sign(b.x - av) * d, b.y);
}
// Where someone with a day planned is now; z is 1 when they're out, 0 when they're indoors. w is -1 for a loafer.
vec4 planned(int who) {
    int base = who * 6;
    vec4 t3 = plan(base + 3);
    int stops = int(t3.w + 0.5);
    if (stops == 0) return vec4(0.0, 0.0, 0.0, -1.0);
    vec4 t0 = plan(base);
    vec4 t1 = plan(base + 1);
    vec4 t2 = plan(base + 2);
    vec4 t4 = plan(base + 4);
    float across = plan(base + 5).x;
    vec2 pts[5];
    pts[0] = t0.xy; pts[1] = t0.zw; pts[2] = t1.xy; pts[3] = t1.zw; pts[4] = t0.xy;
    float leave[4];
    leave[0] = t2.x; leave[1] = t2.y; leave[2] = t2.z; leave[3] = t2.w;
    float linger[3];
    linger[0] = t3.x; linger[1] = t3.y; linger[2] = t3.z;
    float avs[4];
    avs[0] = t4.x; avs[1] = t4.y; avs[2] = t4.z; avs[3] = t4.w;
    float arrive[4];
    float end = 0.0;
    for (int s = 0; s < 4; s++) {
        if (s > stops) break;
        arrive[s] = leave[s] + path_length(pts[s], pts[s + 1], avs[s]) / pace;
        end = arrive[s];
    }
    float h = hour;
    if (h < leave[0] && h + 24.0 < end) h += 24.0;
    if (h < leave[0] || h >= end) return vec4(0.0);
    for (int s = 0; s < 4; s++) {
        if (s > stops) break;
        if (h < arrive[s]) {
            float walk = arrive[s] - leave[s];
            float f = walk <= 0.0 ? 1.0 : (h - leave[s]) / walk;
            return vec4(along_streets(pts[s], pts[s + 1], avs[s], f) + vec2(0.0, across), 1.0, 0.0);
        }
        if (s < stops && h < leave[s + 1]) {
            if (h < arrive[s] + linger[s]) return vec4(pts[s + 1] + vec2(0.0, across), 1.0, 0.0);
            return vec4(0.0);
        }
    }
    return vec4(0.0);
}
";
}
