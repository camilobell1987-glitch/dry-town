using System;
using DryTown.Core;
using Godot;

/// <summary>
/// The streets are drawn wider than the one tile the city plan gives them, so a car can drive each way with a row of
/// parking bays at each kerb. The plan keeps its tiles; only the drawing stretches the roadway between the pavements,
/// and everything on the map is placed through the same stretch so it lands where it should.
/// </summary>
public partial class MapView
{
    /// <summary>The pavement along each side of a street, in tiles. It isn't stretched.</summary>
    private const float PaveWidth = 0.2f;

    /// <summary>The roadway between the pavements, in tiles of the plan.</summary>
    private const float Roadway = 1 - 2 * PaveWidth;

    /// <summary>How much wider the roadway is drawn than the plan has it: room for four cars abreast.</summary>
    private const float RoadWiden = 0.52f;

    /// <summary>Across the roadway, as shares of it: the parking bays at each kerb and the lane each way between them.</summary>
    private const float NearBay = 0.125f, NearLane = 0.375f, FarLane = 0.625f, FarBay = 0.875f;

    /// <summary>A coordinate of the plan along one axis, as drawn.</summary>
    private static float Widen(float v, int stride)
    {
        float k = Mathf.Floor(v / stride), u = v - k * stride, before = k * RoadWiden;
        if (u <= PaveWidth) return v + before;
        if (u >= 1 - PaveWidth) return v + before + RoadWiden;
        return v + before + (u - PaveWidth) * RoadWiden / Roadway;
    }

    /// <summary>A drawn coordinate back to the plan's.</summary>
    private static float Narrow(float d, int stride)
    {
        float span = stride + RoadWiden, k = Mathf.Floor(d / span), u = d - k * span;
        if (u <= PaveWidth) return k * stride + u;
        if (u >= 1 - PaveWidth + RoadWiden) return k * stride + u - RoadWiden;
        return k * stride + PaveWidth + (u - PaveWidth) * Roadway / (Roadway + RoadWiden);
    }

    /// <summary>A point of the plan, in tiles, where it's drawn.</summary>
    private static Vector2 Widen(Vector2 p) => new(Widen(p.X, CityMap.StrideX), Widen(p.Y, CityMap.StrideY));

    /// <summary>A drawn point, in tiles, back to the plan.</summary>
    private static Vector2 Narrow(Vector2 d) => new(Narrow(d.X, CityMap.StrideX), Narrow(d.Y, CityMap.StrideY));

    /// <summary>The city as drawn, in tiles.</summary>
    private Vector2 DrawnSize => Widen(new Vector2(Map.Width, Map.Height));

    /// <summary>Where across a street or avenue a share of its roadway is, in tiles of the plan.</summary>
    private static float Across(int line, int stride, float share) => line * stride + PaveWidth + Roadway * share;

    /// <summary>A screen point as a tile of the plan.</summary>
    private Vector2 TileUnder(Vector2 screen) => Narrow((screen - Origin) / Tile);

    /// <summary>A tile of the plan as a point on screen.</summary>
    private Vector2 OnScreen(Vector2 tile) => Origin + Widen(tile) * Tile;

    /// <summary>The same stretch, for the crowd shader.</summary>
    private const string RoadShader = @"
float widen(float v, float stride) {
    float k = floor(v / stride);
    float u = v - k * stride;
    float before = k * 0.52;
    if (u <= 0.2) return v + before;
    if (u >= 0.8) return v + before + 0.52;
    return v + before + (u - 0.2) * 0.52 / 0.6;
}
vec2 widen2(vec2 p) { return vec2(widen(p.x, 5.0), widen(p.y, 3.0)); }
";

    // ---- Parking ----------------------------------------------------------------

    /// <summary>The length of a parking bay along the kerb.</summary>
    private const float Bay = 0.62f;

    /// <summary>
    /// Cars parked in the bays along each side of a block: about half the bays are taken, more in the day than
    /// at night, and none outside a headquarters or a precinct house, where their own cars stand.
    /// </summary>
    private void DrawParkedCars(float night, Rect2 view)
    {
        var map = Map;
        int bx0 = Math.Max(0, (int)(view.Position.X / CityMap.StrideX) - 1), bx1 = Math.Min(map.BlocksX, (int)(view.End.X / CityMap.StrideX) + 1);
        int by0 = Math.Max(0, (int)(view.Position.Y / CityMap.StrideY) - 1), by1 = Math.Min(map.BlocksY, (int)(view.End.Y / CityMap.StrideY) + 1);
        float hour = HourOfDay, taken = hour is >= 7 and < 19 ? 0.55f : 0.35f;
        _reserved.Clear();
        foreach (var lot in map.Lots)
            if (lot.Use is LotUse.Headquarters or LotUse.Precinct && CornerOf(lot) == null
                || lot.Use == LotUse.Business && World!.BusinessById(lot.BusinessId).Kind == BusinessKind.GasStation)
                _reserved.Add((lot.X, lot.Y));
        for (int by = by0; by <= by1; by++)
        for (int bx = bx0; bx <= bx1; bx++)
        {
            float x0 = bx * CityMap.StrideX, y0 = by * CityMap.StrideY;
            // Along the street: bays on its north kerb belong to the block above, its south kerb to the block below.
            if (bx < map.BlocksX && !map.StreetClosed(bx, by))
                for (int side = 0; side < 2; side++)
                {
                    if (!KerbHasBays(by, side)) continue;
                    float y = Across(by, CityMap.StrideY, side == 0 ? NearBay : FarBay);
                    for (int i = 0; i < 6; i++)
                    {
                        float x = x0 + 1 + 0.35f + i * Bay;
                        if (x > x0 + CityMap.StrideX - 0.3f) break;
                        int key = ((by * 2 + side) * (map.BlocksX + 1) + bx) * 8 + i;
                        if (Hash(key, 760) >= taken || _reserved.Contains(((int)x, by * CityMap.StrideY + (side == 0 ? -1 : 1))) || BlocksRamp(x, by, side)) continue;
                        var at = new Vector2(x, y);
                        if (!view.HasPoint(at)) continue;
                        C = DynAt(y);
                        var ride = TrafficRide(Hash(key, 764), parked: true);
                        DrawVehicle(at, true, side == 0 ? -1 : 1, ride, RidePaint(ride, Hash(key, 761)), 0, 0.85f, seed: key);
                    }
                }
            // Along the avenue, the same down each kerb.
            if (by < map.BlocksY && by != map.RiverRow && !map.AvenueClosed(bx, by))
                for (int side = 0; side < 2; side++)
                {
                    if (side == 0 && bx == 0 || side == 1 && bx == map.BlocksX) continue;
                    float x = Across(bx, CityMap.StrideX, side == 0 ? NearBay : FarBay);
                    for (int i = 0; i < 3; i++)
                    {
                        float y = y0 + 1 + 0.4f + i * Bay;
                        if (y > y0 + CityMap.StrideY - 0.3f) break;
                        int key = ((bx * 2 + side) * (map.BlocksY + 1) + by) * 8 + i + 4096;
                        if (Hash(key, 762) >= taken) continue;
                        var at = new Vector2(x, y);
                        if (!view.HasPoint(at)) continue;
                        C = DynAt(y);
                        var ride = TrafficRide(Hash(key, 765), parked: true);
                        DrawVehicle(at, false, side == 0 ? 1 : -1, ride, RidePaint(ride, Hash(key, 763)), 0, 0.85f, seed: key);
                    }
                }
        }
    }

    /// <summary>The lots whose kerb is kept clear: for a headquarters' or a precinct's own cars, or a filling station's driveways.</summary>
    private readonly System.Collections.Generic.HashSet<(int X, int Y)> _reserved = new();

    /// <summary>Whether a street's north (0) or south (1) kerb has bays: not at the edge of the city or along the river.</summary>
    private bool KerbHasBays(int street, int side) =>
        side == 0 ? street > 0 && street - 1 != Map.RiverRow : street < Map.BlocksY && street != Map.RiverRow;

    /// <summary>The lines marking out the bays along each kerb of a ground cell's street and avenue.</summary>
    private void DrawBayLines(int bx, int by)
    {
        float x0 = bx * CityMap.StrideX, y0 = by * CityMap.StrideY;
        var paint = RoadPaint with { A = 0.3f };
        float lw = Mathf.Max(1, _t * 0.02f);
        if (bx < Map.BlocksX && !Map.StreetClosed(bx, by))
            for (int side = 0; side < 2; side++)
            {
                if (!KerbHasBays(by, side)) continue;
                float kerb = Across(by, CityMap.StrideY, side == 0 ? 0 : 1), edge = Across(by, CityMap.StrideY, 0.25f + side * 0.5f);
                for (int i = 0; i <= 6; i++)
                {
                    float x = x0 + 1 + 0.35f - Bay / 2 + i * Bay;
                    if (x > x0 + CityMap.StrideX - 0.3f + Bay / 2) break;
                    C.DrawLine(P(x, kerb), P(x, edge), paint, lw);
                }
                // The line between the bays and the traffic.
                C.DrawLine(P(x0 + 1 + 0.35f - Bay / 2, edge), P(x0 + 1 + 0.35f + 5.5f * Bay, edge), paint with { A = 0.18f }, lw);
            }
        if (by < Map.BlocksY && by != Map.RiverRow && !Map.AvenueClosed(bx, by))
            for (int side = 0; side < 2; side++)
            {
                if (side == 0 && bx == 0 || side == 1 && bx == Map.BlocksX) continue;
                float kerb = Across(bx, CityMap.StrideX, side == 0 ? 0 : 1), edge = Across(bx, CityMap.StrideX, 0.25f + side * 0.5f);
                for (int i = 0; i <= 3; i++)
                {
                    float y = y0 + 1 + 0.4f - Bay / 2 + i * Bay;
                    if (y > y0 + CityMap.StrideY - 0.3f + Bay / 2) break;
                    C.DrawLine(P(kerb, y), P(edge, y), paint, lw);
                }
            }
    }
}
