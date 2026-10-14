using System.Collections.Generic;
using DryTown.Core;
using Godot;

/// <summary>
/// The squares: stretches of street closed to traffic and paved over, with bollards across each end. Where a street
/// ran there's a square with a fountain, benches and a tree at each end, or a market street lined with stalls. Where
/// an avenue ran there's a little plaza with a statue between two trees, or a glass-roofed arcade built across the gap
/// that joins the two blocks into one. People cross them on foot, and the traffic turns off at the crossing before.
/// </summary>
public partial class MapView
{
    private static readonly Color Flags = new("8a8478");
    private static readonly Color Bronze = new("5a4a32");

    /// <summary>What a closed street became: a market street with stalls, or a square with a fountain.</summary>
    internal static bool IsMarket(int bx, int by) => Hash(bx * 977 + by, 1300) < 0.4f;

    /// <summary>What a closed avenue became: an arcade built across it, or a plaza with a statue.</summary>
    internal static bool IsArcade(int bx, int by) => Hash(bx * 977 + by, 1301) < 0.5f;

    /// <summary>The paving, bollards, basin and benches of any square in a ground cell: the street east of its crossing, the avenue south.</summary>
    private void DrawSquareGround(int bx, int by)
    {
        float x0 = bx * CityMap.StrideX, y0 = by * CityMap.StrideY;
        if (bx < Map.BlocksX && Map.StreetClosed(bx, by))
        {
            float a = x0 + 1 - PaveWidth, b = x0 + CityMap.StrideX + PaveWidth;
            Pave(a, y0 + PaveWidth, b, y0 + 1 - PaveWidth, true);
            float cx = x0 + CityMap.StrideX / 2f + 0.5f, cy = y0 + 0.5f;
            if (IsMarket(bx, by))
            {
                // Litter from the stalls: cabbage leaves, straw and a crushed crate or two.
                for (int i = 0; Fine && i < 14; i++)
                    C.DrawCircle(P(a + 0.2f + Hash(bx * 31 + i, by + 1310) * (b - a - 0.4f), y0 + 0.3f + Hash(bx * 31 + i, by + 1311) * 0.4f),
                        _t * 0.018f, i % 3 == 0 ? new Color("6a8a3a") : new Color("b89a5a") with { A = 0.7f });
                for (int i = 0; i < StallCount; i++) Ellipse(P(StallX(a, b, i) + 0.08f, cy - 0.02f), new Vector2(0.18f, 0.08f) * _t, Shadow with { A = 0.2f });
            }
            // A round basin in the middle, with a ring of paving round it.
            else
            {
                Ellipse(P(cx, cy), new Vector2(0.5f, 0.4f) * _t, Flags.Lightened(0.12f), points: 24);
                Ellipse(P(cx, cy), new Vector2(0.38f, 0.3f) * _t, Flags.Darkened(0.25f), points: 24);
                Ellipse(P(cx, cy), new Vector2(0.33f, 0.25f) * _t, new Color("3d6a7a"), points: 24);
                if (Fine) Ellipse(P(cx - 0.06f, cy - 0.03f), new Vector2(0.16f, 0.08f) * _t, new Color("6a98a8") with { A = 0.6f }, points: 16);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                if (!IsMarket(bx, by))
                {
                    Bench(cx + side * 0.95f, cy - 0.14f);
                    Bench(cx + side * 0.95f, cy + 0.14f);
                }
                TreeShadow(cx + side * 1.6f, cy, 1);
                Ellipse(P(cx + side * 1.6f, cy), new Vector2(0.14f, 0.1f) * _t, Flags.Darkened(0.3f));
            }
        }
        if (by < Map.BlocksY && Map.AvenueClosed(bx, by))
        {
            float a = y0 + 1 - PaveWidth, b = y0 + CityMap.StrideY + PaveWidth;
            Pave(x0 + PaveWidth, a, x0 + 1 - PaveWidth, b, false);
            float cx = x0 + 0.5f, cy = y0 + 2;
            if (IsArcade(bx, by)) return;
            Ellipse(P(cx + 0.06f, cy + 0.03f), new Vector2(0.22f, 0.12f) * _t, Shadow);
            foreach (float side in new[] { -1f, 1f })
            {
                TreeShadow(cx, cy + side * 0.6f, 1);
                Ellipse(P(cx, cy + side * 0.6f), new Vector2(0.12f, 0.1f) * _t, Flags.Darkened(0.3f));
            }
        }
    }

    /// <summary>Stone flags across a closed stretch, and a row of bollards across each end to keep the cars out.</summary>
    private void Pave(float x0, float y0, float x1, float y1, bool alongX)
    {
        C.DrawRect(R(x0, y0, x1 - x0, y1 - y0), Flags);
        if (!Fine) return;
        var joint = Flags.Darkened(0.18f) with { A = 0.6f };
        float lw = Hair(0.006f);
        for (float x = x0 + 0.15f; x < x1 - 0.05f; x += 0.15f) C.DrawLine(P(x, y0), P(x, y1), joint, lw);
        for (float y = y0 + 0.1f; y < y1 - 0.03f; y += 0.1f) C.DrawLine(P(x0, y), P(x1, y), joint, lw);
        var post = new Color("2a2622");
        if (alongX)
            foreach (float x in new[] { x0 + 0.04f, x1 - 0.04f })
                for (int k = 0; k < 4; k++) C.DrawCircle(P(x, y0 + (y1 - y0) * (k + 0.5f) / 4), _t * 0.025f, post);
        else
            foreach (float y in new[] { y0 + 0.04f, y1 - 0.04f })
                for (int k = 0; k < 3; k++) C.DrawCircle(P(x0 + (x1 - x0) * (k + 0.5f) / 3, y), _t * 0.025f, post);
    }

    /// <summary>A slatted park bench, seen from above.</summary>
    private void Bench(float x, float y)
    {
        var wood = new Color("6a4a30");
        C.DrawRect(R(x - 0.12f, y - 0.025f, 0.24f, 0.05f), wood);
        if (Fine) C.DrawLine(P(x - 0.12f, y), P(x + 0.12f, y), wood.Darkened(0.3f), Hair(0.005f));
    }

    /// <summary>Whatever stands up in the squares in one row of one column: the trees, the fountain's jet and the statues.</summary>
    private void DrawSquareFurniture(int y, int col)
    {
        foreach (var (bx, by, avenue) in Map.Squares)
        {
            if (!avenue)
            {
                float x0 = bx * CityMap.StrideX, row = by * CityMap.StrideY;
                if (y != (int)row || Column(x0 + 1) != col) continue;
                float cx = x0 + CityMap.StrideX / 2f + 0.5f, cy = row + 0.5f;
                if (IsMarket(bx, by))
                    for (int i = 0; i < StallCount; i++) DrawStall(StallX(x0 + 1 - PaveWidth, x0 + CityMap.StrideX + PaveWidth, i), cy, bx * 7 + by * 3 + i);
                else
                {
                    C.DrawLine(Q(cx, cy, 0), Q(cx, cy, 0.14f), new Color("b8d0d8") with { A = 0.8f }, Hair(0.02f));
                    C.DrawCircle(Q(cx, cy, 0.15f), _t * 0.03f, new Color("d8e8ee") with { A = 0.7f });
                }
                DrawTree(cx + 1.6f, cy, 1);
                DrawTree(cx - 1.6f, cy, 1);
            }
            else
            {
                float cx = bx * CityMap.StrideX + 0.5f, cy = by * CityMap.StrideY + 2;
                if (Column(cx) != col) continue;
                if (IsArcade(bx, by))
                {
                    if (y == (int)cy) DrawArcade(bx * CityMap.StrideX, by * CityMap.StrideY);
                    continue;
                }
                if (y == (int)(cy - 0.6f)) DrawTree(cx, cy - 0.6f, 1);
                if (y == (int)cy)
                {
                    Box(cx - 0.09f, cy - 0.07f, cx + 0.09f, cy + 0.07f, 0, 0.12f, Flags.Lightened(0.1f), Flags, Flags.Lightened(0.2f));
                    C.DrawLine(Q(cx, cy, 0.12f), Q(cx, cy, 0.3f), Bronze, Hair(0.05f));
                    C.DrawCircle(Q(cx, cy, 0.33f), _t * 0.03f, Bronze);
                    C.DrawLine(Q(cx, cy, 0.25f), Q(cx + 0.07f, cy, 0.34f), Bronze, Hair(0.02f));
                }
                if (y == (int)(cy + 0.6f)) DrawTree(cx, cy + 0.6f, 1);
            }
        }
    }

    private const int StallCount = 5;

    /// <summary>Where the stalls on a market street stand, spaced between its trees.</summary>
    private static float StallX(float a, float b, int i) => a + 0.95f + i * (b - a - 1.9f) / (StallCount - 1);

    /// <summary>A market stall: a trestle of produce under a striped canvas on poles.</summary>
    private void DrawStall(float x, float y, int seed)
    {
        var canvas = Awnings[(int)(Hash(seed, 1320) * Awnings.Length)];
        var wood = new Color("6a4a30");
        float w = 0.13f, d = 0.13f;
        Box(x - w, y - d, x + w, y + d, 0, 0.07f, wood, wood.Darkened(0.15f), wood.Lightened(0.1f));
        if (Fine)
            for (int k = 0; k < 4; k++)
                C.DrawCircle(Q(x - w + 0.04f + k * 0.06f, y + d - 0.04f, 0.08f), _t * 0.022f, WindowGoods[(seed + k) % WindowGoods.Length]);
        foreach (float px in new[] { x - w, x + w }) C.DrawLine(Q(px, y + d, 0.07f), Q(px, y + d, 0.18f), wood.Darkened(0.3f), Hair(0.01f));
        for (int i = 0; i < 4; i++)
        {
            float s0 = x - w - 0.02f + (2 * w + 0.04f) * i / 4, s1 = x - w - 0.02f + (2 * w + 0.04f) * (i + 1) / 4;
            var c = i % 2 == 0 ? canvas : new Color("e9e0c8");
            C.DrawColoredPolygon(new[] { Q(s0, y - d, 0.2f), Q(s1, y - d, 0.2f), Q(s1, y + d + 0.04f, 0.16f), Q(s0, y + d + 0.04f, 0.16f) }, c);
        }
    }

    /// <summary>
    /// A shopping arcade built across a closed avenue: a long hall with a glass roof on iron ribs joining the two
    /// blocks, and an arched entrance at each end.
    /// </summary>
    private void DrawArcade(float x0, float y0)
    {
        float a = x0 - 0.02f, b = x0 + 1.02f, n = y0 + 1, s = y0 + CityMap.StrideY;
        const float wall = 0.34f, ridge = 0.5f;
        var stone = Stones[0];
        Box(a, n, b, s, 0, wall, stone, stone.Lightened(0.1f), stone.Darkened(0.1f));
        // The glass roof, pitched along the length of the hall.
        float mid = (a + b) / 2;
        var glass = Glass.Lightened(0.3f);
        C.DrawColoredPolygon(new[] { Q(a, n, wall), Q(mid, n, ridge), Q(mid, s, ridge), Q(a, s, wall) }, glass.Lightened(0.1f));
        C.DrawColoredPolygon(new[] { Q(mid, n, ridge), Q(b, n, wall), Q(b, s, wall), Q(mid, s, ridge) }, glass.Darkened(0.12f));
        if (Fine)
            for (float y = n + 0.15f; y < s; y += 0.15f)
            {
                C.DrawLine(Q(a, y, wall), Q(mid, y, ridge), Iron, Hair(0.006f));
                C.DrawLine(Q(mid, y, ridge), Q(b, y, wall), Iron, Hair(0.006f));
            }
        C.DrawLine(Q(mid, n, ridge), Q(mid, s, ridge), Iron, Hair(0.012f));
        // The south front: a gable with an arched way in, and a name board over it.
        C.DrawColoredPolygon(new[] { Q(a, s, wall), Q(mid, s, ridge), Q(b, s, wall) }, stone.Lightened(0.06f));
        Face(a + 0.22f, b - 0.22f, s, 0, 0.22f, new Color("2a2420"));
        C.DrawCircle(Q(mid, s, 0.22f), (b - a - 0.44f) / 2 * _t, new Color("2a2420"));
        Face(a + 0.18f, b - 0.18f, s, 0.27f, 0.31f, SignBoards[1]);
        if (Fine)
        {
            Face(a + 0.25f, b - 0.25f, s, 0.282f, 0.298f, Gilt with { A = 0.8f });
            Lit(() => Face(a + 0.22f, b - 0.22f, s, 0, 0.2f, WindowLit with { A = 0.6f }));
        }
    }

    // ---- Traffic round the squares ------------------------------------------------

    /// <summary>One straight run of a car's way across town: along a street or an avenue, from one point to another.</summary>
    private sealed record Leg(bool Horizontal, int Line, int Heading, float From, float To);

    /// <summary>
    /// A car's way across town from one edge to another: straight along its street or avenue, turning off at the
    /// crossing before any square, either way, until it leaves the city.
    /// </summary>
    private List<Leg> Route(bool horizontal, int line, int heading, int seed)
    {
        var legs = new List<Leg>();
        float pos = heading > 0 ? -1 : (horizontal ? Map.Width : Map.Height) + 1;
        for (int turn = 0; turn < 8; turn++)
        {
            int stride = horizontal ? CityMap.StrideX : CityMap.StrideY;
            int segments = horizontal ? Map.BlocksX : Map.BlocksY;
            float end = heading > 0 ? (horizontal ? Map.Width : Map.Height) + 1 : -1;
            int crossing = -1;
            for (int k = 0; k < segments; k++)
            {
                int seg = heading > 0 ? k : segments - 1 - k;
                bool closed = horizontal ? Map.StreetClosed(seg, line) : Map.AvenueClosed(line, seg);
                int at = heading > 0 ? seg : seg + 1;
                if (closed && (at * stride - pos) * heading > 0) { crossing = at; break; }
            }
            if (crossing < 0) { legs.Add(new Leg(horizontal, line, heading, pos, end)); break; }
            legs.Add(new Leg(horizontal, line, heading, pos, crossing * stride));
            pos = line * (horizontal ? CityMap.StrideY : CityMap.StrideX);
            horizontal = !horizontal;
            line = crossing;
            heading = Hash(seed * 13 + turn, 207) < 0.5f ? 1 : -1;
        }
        return legs;
    }

    /// <summary>Where along its route a car is, and which way it's facing there.</summary>
    private (Vector2 Centre, bool Horizontal, int Heading) AlongRoute(List<Leg> legs, float s)
    {
        foreach (var leg in legs)
        {
            float length = Mathf.Abs(leg.To - leg.From);
            if (s > length && leg != legs[^1]) { s -= length; continue; }
            float pos = leg.From + leg.Heading * s;
            // Keep to the right: eastbound in the lower lane, southbound in the left one, clear of the parked cars.
            float lane = leg.Horizontal ? Across(leg.Line, CityMap.StrideY, leg.Heading > 0 ? FarLane : NearLane)
                                        : Across(leg.Line, CityMap.StrideX, leg.Heading > 0 ? NearLane : FarLane);
            return (leg.Horizontal ? new Vector2(pos, lane) : new Vector2(lane, pos), leg.Horizontal, leg.Heading);
        }
        return (Vector2.Zero, true, 1);
    }

    private static float RouteLength(List<Leg> legs)
    {
        float total = 0;
        foreach (var leg in legs) total += Mathf.Abs(leg.To - leg.From);
        return total;
    }
}
