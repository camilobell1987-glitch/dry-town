using System.Collections.Generic;
using DryTown.Core;
using Godot;

/// <summary>
/// Underground garages: a concrete ramp beside the building, running down from the pavement into the dark under it.
/// Cars turn off the street and roll down out of sight, and others come up; at a precinct house it's patrol cars and
/// the paddy wagon, and a raid sets out up the ramp.
/// </summary>
public partial class MapView
{
    private static readonly Color Concrete = new("706b62");

    /// <summary>How deep into its lot a ramp runs from the pavement, and how wide it is.</summary>
    private const float RampDeep = 0.56f, RampWide = 0.3f;

    /// <summary>Lots with a garage under them, and whether the ramp is on the east side of the building.</summary>
    private readonly Dictionary<int, bool> _ramps = new();

    private void IndexGarages()
    {
        _ramps.Clear();
        foreach (var g in World!.Garages)
        {
            var lot = Map.LotAt(g.LotId);
            var (bx, by, lx, _) = Place(lot);
            // Beside the building, on whichever side an alley doesn't take.
            _ramps[lot.Id] = AlleyOf(bx, by) != lx + 1;
        }
    }

    /// <summary>A ramp's footprint: west, east, the end at the pavement and the end under the building.</summary>
    private static (float X0, float X1, float Mouth, float Foot) Ramp(Lot lot, bool east)
    {
        float x0 = east ? lot.X + 0.96f - RampWide : lot.X + 0.04f;
        bool north = lot.FrontY < lot.Y;
        return (x0, x0 + RampWide, north ? lot.Y : lot.Y + 1, north ? lot.Y + RampDeep : lot.Y + 1 - RampDeep);
    }

    /// <summary>The building on a garage lot stands to one side of its ramp.</summary>
    private (float X0, float X1) BesideRamp(Lot lot, float x0, float x1)
    {
        if (!_ramps.TryGetValue(lot.Id, out bool east)) return (x0, x1);
        return east ? (x0, Mathf.Min(x1, lot.X + 0.92f - RampWide)) : (Mathf.Max(x0, lot.X + 0.08f + RampWide), x1);
    }

    /// <summary>The ramp down, drawn before the building beside it.</summary>
    private void DrawRamp(Lot lot)
    {
        if (!_ramps.TryGetValue(lot.Id, out bool east)) return;
        var (x0, x1, mouth, foot) = Ramp(lot, east);
        float top = Mathf.Min(mouth, foot), deep = Mathf.Abs(foot - mouth);
        C.DrawRect(R(x0, top, x1 - x0, deep), Concrete);
        // Darker the further down it goes, to black where it runs in under the building.
        const int bands = 8;
        for (int i = 0; i < bands; i++)
        {
            float a = (i + 0.5f) / bands, y = mouth + (foot - mouth) * i / bands;
            C.DrawRect(R(x0, Mathf.Min(y, y + (foot - mouth) / bands), x1 - x0, deep / bands), new Color(0, 0, 0, a * 0.8f));
        }
        float inner = foot - (foot - mouth) * 0.1f;
        C.DrawRect(R(x0, Mathf.Min(inner, foot), x1 - x0, deep * 0.1f), new Color("0c0b0a"));
        // The retaining walls down each side, and grooves across for grip.
        var wall = Concrete.Lightened(0.18f);
        C.DrawLine(P(x0, mouth), P(x0, foot), wall, Hair(0.025f));
        C.DrawLine(P(x1, mouth), P(x1, foot), wall, Hair(0.025f));
        if (Fine)
            for (int i = 1; i < 6; i++)
            {
                float y = mouth + (foot - mouth) * i / 7f;
                C.DrawLine(P(x0 + 0.02f, y), P(x1 - 0.02f, y), new Color(0, 0, 0, 0.25f), Hair(0.006f));
            }
        // A sign at the mouth: an enamel GARAGE plate, or the precinct's blue lamp.
        float sx = east ? x1 - 0.05f : x0 + 0.05f, sy = mouth + (foot > mouth ? 0.04f : -0.04f);
        if (lot.Use == LotUse.Precinct)
        {
            C.DrawLine(Q(sx, sy, 0), Q(sx, sy, 0.16f), Iron, Hair(0.012f));
            C.DrawCircle(Q(sx, sy, 0.16f), _t * 0.025f, new Color("4a7ad8"));
            Lit(() => Glow(Q(sx, sy, 0.16f), _t * 0.18f, new Color("6a9aff"), 0.7f));
        }
        else
        {
            var plate = new Color("2a4a7a");
            C.DrawLine(Q(sx, sy, 0), Q(sx, sy, 0.12f), Iron, Hair(0.012f));
            C.DrawRect(new Rect2(Q(sx - 0.06f, sy, 0.17f), new Vector2(0.12f, 0.05f) * _t), plate);
            C.DrawRect(new Rect2(Q(sx - 0.05f, sy, 0.16f), new Vector2(0.1f, 0.012f) * _t), new Color("e8e0c8"));
        }
    }

    /// <summary>
    /// Cars going down into each garage in view and coming back up, one at a time: off the street, down the ramp
    /// and out of sight, and a while later another the other way.
    /// </summary>
    private void DrawGarageTraffic(float night, Rect2 view)
    {
        foreach (var (lotId, east) in _ramps)
        {
            var lot = Map.LotAt(lotId);
            if (!view.HasPoint(new Vector2(lot.X + 0.5f, lot.Y + 0.5f))) continue;
            bool police = lot.Use == LotUse.Precinct;
            float period = police ? 40 : 26, t = (_anim / period + Hash(lotId, 1200)) % 1f;
            bool down = t < 0.3f;
            if (!down && t is < 0.55f or >= 0.85f) continue;
            float f = down ? t / 0.3f : 1 - (t - 0.55f) / 0.3f;
            int trip = (int)(_anim / period + Hash(lotId, 1200)) * 2 + (down ? 0 : 1);
            var (x0, x1, mouth, foot) = Ramp(lot, east);
            bool north = lot.FrontY < lot.Y;
            // From the near lane of the street to well down the ramp, where the dark swallows it.
            float lane = Across(lot.FrontY / CityMap.StrideY, CityMap.StrideY, north ? FarLane : NearLane);
            float end = foot - (foot - mouth) * 0.15f;
            var at = new Vector2((x0 + x1) / 2, Mathf.Lerp(lane, end, f));
            int heading = (north ? 1 : -1) * (down ? 1 : -1);
            var ride = police ? (trip % 3 == 0 ? Ride.PaddyWagon : Ride.PoliceCar) : TrafficRide(Hash(lotId * 31 + trip, 1201), parked: true);
            var paint = police ? (ride == Ride.PaddyWagon ? WagonBlue : PoliceBlack) : RidePaint(ride, Hash(lotId * 31 + trip, 1202));
            var keep = C;
            C = DynAt(at.Y);
            DrawVehicle(at, false, heading, ride, paint, night, 0.85f);
            ShadeOnRamp(lot, east, at, Length(ride) * 0.85f, Width(ride) * 0.85f);
            C = keep;
        }
    }

    /// <summary>The dark of the ramp over the part of a vehicle that has gone down into it.</summary>
    private void ShadeOnRamp(Lot lot, bool east, Vector2 at, float halfLength, float halfWidth)
    {
        var (x0, x1, mouth, foot) = Ramp(lot, east);
        float top = Mathf.Min(mouth, foot), bottom = Mathf.Max(mouth, foot);
        float y0 = Mathf.Max(top, at.Y - halfLength), y1 = Mathf.Min(bottom, at.Y + halfLength);
        if (y1 <= y0) return;
        float cx0 = Mathf.Max(x0, at.X - halfWidth), cx1 = Mathf.Min(x1, at.X + halfWidth);
        const int bands = 8;
        for (int i = 0; i < bands; i++)
        {
            float a = mouth + (foot - mouth) * i / bands, b = mouth + (foot - mouth) * (i + 1) / bands;
            float s0 = Mathf.Max(y0, Mathf.Min(a, b)), s1 = Mathf.Min(y1, Mathf.Max(a, b));
            if (s1 <= s0) continue;
            C.DrawRect(R(cx0, s0, cx1 - cx0, s1 - s0), new Color(0, 0, 0, (i + 0.5f) / bands * 0.9f));
        }
    }

    /// <summary>A vehicle on its way somewhere, shaded if it's still on a garage ramp.</summary>
    private void ShadeIfOnRamp(Vector2 at, bool horizontal, Ride ride)
    {
        if (horizontal) return;
        var lot = World!.Map.Lots.Count > 0 ? LotAtTile((int)at.X, (int)at.Y) : null;
        if (lot == null || !_ramps.TryGetValue(lot.Id, out bool east)) return;
        ShadeOnRamp(lot, east, at, Length(ride), Width(ride));
    }

    /// <summary>The lot covering a tile, or null on a street.</summary>
    private Lot? LotAtTile(int x, int y)
    {
        if (CityMap.IsRoad(x, y) || x < 0 || y < 0) return null;
        int bx = x / CityMap.StrideX, by = y / CityMap.StrideY;
        if (bx >= Map.BlocksX || by >= Map.BlocksY) return null;
        int id = (by * Map.BlocksX + bx) * CityMap.LotsPerBlockX * CityMap.LotsPerBlockY + (y - by * CityMap.StrideY - 1) * CityMap.LotsPerBlockX + (x - bx * CityMap.StrideX - 1);
        return id < Map.Lots.Count ? Map.LotAt(id) : null;
    }

    /// <summary>The way out of a precinct house's garage: from down the ramp up to the pavement.</summary>
    private List<Vector2>? RampOut(Lot lot)
    {
        // The week can start replaying before the city is first drawn.
        if (_ramps.Count == 0 && World!.Garages.Count > 0) IndexBlocks();
        if (!_ramps.TryGetValue(lot.Id, out bool east)) return null;
        var (x0, x1, mouth, foot) = Ramp(lot, east);
        float x = (x0 + x1) / 2;
        return new List<Vector2> { new(x, foot - (foot - mouth) * 0.2f), new(x, mouth - (foot - mouth) * 0.5f) };
    }

    /// <summary>A tooltip line for a building with a garage under it.</summary>
    private string GarageLine(Lot lot) => World!.GarageAt(lot.Id) is { } g
        ? $"\nUnderground garage, {g.Spaces} spaces{(g.Police ? " for patrol cars and the paddy wagon" : "")}"
        : "";

    /// <summary>Whether a street bay is kept clear in front of a garage ramp.</summary>
    private bool BlocksRamp(float x, int street, int side)
    {
        // The north kerb of a street fronts lots on the block above it; the south kerb lots on the block below.
        int y = street * CityMap.StrideY + (side == 0 ? -1 : 1);
        foreach (int lx in new[] { (int)x - 1, (int)x, (int)x + 1 })
        {
            var lot = LotAtTile(lx, y);
            if (lot == null || !_ramps.TryGetValue(lot.Id, out bool east)) continue;
            var (x0, x1, _, _) = Ramp(lot, east);
            if (x > x0 - Bay * 0.45f && x < x1 + Bay * 0.45f) return true;
        }
        return false;
    }
}
