using System;
using DryTown.Core;
using Godot;

/// <summary>
/// Businesses that are mostly open ground: a filling station's forecourt and pumps under a canopy, and a scrapyard's
/// fenced heaps of junk round a tin office, the big one spread over the lot next door with a crane over it.
/// </summary>
public partial class MapView
{
    private static readonly Color Cinder = new("4a443c");
    private static readonly Color Rust = new("8a5434");

    /// <summary>
    /// How a filling station is laid out: the pump island across the middle of the forecourt, the row a car stops in on
    /// the street side of it, the two driveways at either end, and the kerb lane cars come along. Cars keep to the
    /// lane on the station's side of the street, so they come in at one end and leave by the other.
    /// </summary>
    private (bool South, float Pump, float Row, float In, float Out, float Lane, int Dir) StationPlan(Lot lot)
    {
        bool south = FacesSouth(lot);
        int dir = south ? -1 : 1;
        float lane = Across(lot.FrontY / CityMap.StrideY, CityMap.StrideY, south ? NearLane : FarLane);
        float mid = lot.X + 0.5f;
        return (south, south ? lot.Y + 0.6f : lot.Y + 0.4f, south ? lot.Y + 0.82f : lot.Y + 0.18f, mid - dir * 0.36f, mid + dir * 0.36f, lane, dir);
    }

    /// <summary>Where the sign stands on a filling station's or a scrapyard's office: its ends along the roof and the roof's front edge.</summary>
    private (float Xa, float Xb, float? Front)? OfficeSign(Lot lot, BusinessKind kind)
    {
        bool south = FacesSouth(lot);
        return kind switch
        {
            BusinessKind.GasStation => (lot.X + 0.17f, lot.X + 0.58f, south ? lot.Y + 0.38f : lot.Y + 0.9f),
            BusinessKind.Scrapyard => (lot.X + 0.13f, lot.X + 0.45f, south ? lot.Y + 0.87f : lot.Y + 0.43f),
            _ => null,
        };
    }

    /// <summary>A filling station: a little office at the back, a canopy over the pump island, and driveways in and out.</summary>
    private void DrawFillingStation(Lot lot, float h, bool open)
    {
        var plan = StationPlan(lot);
        bool south = plan.South;
        float x0 = lot.X + 0.05f, x1 = lot.X + 0.95f;
        // The office stands back from the street, the forecourt in front of it running out to the pavement.
        float oy0 = south ? lot.Y + 0.1f : lot.Y + 0.62f, oy1 = south ? lot.Y + 0.38f : lot.Y + 0.9f;
        float fy0 = south ? oy1 : lot.Y, fy1 = south ? lot.Y + 1 : oy0;
        var apron = new Color("7c776c");
        C.DrawColoredPolygon(new[] { Q(x0, fy0, 0), Q(x1, fy0, 0), Q(x1, fy1, 0), Q(x0, fy1, 0) }, apron);
        // The kerb dropped for a driveway at each end, across the pavement to the road.
        float kerb = south ? lot.Y + 1 + PaveWidth : lot.Y - PaveWidth, edge = south ? lot.Y + 1 : lot.Y;
        foreach (float x in new[] { plan.In, plan.Out })
            C.DrawColoredPolygon(new[] { Q(x - 0.13f, edge, 0), Q(x + 0.13f, edge, 0), Q(x + 0.13f, kerb, 0), Q(x - 0.13f, kerb, 0) }, apron.Lightened(0.06f));
        var wall = open ? new Color("e6dcc2") : new Color("e6dcc2").Darkened(0.3f);
        var trim = new Color("2f6a3a");
        Box(x0 + 0.1f, oy0, x0 + 0.55f, oy1, 0, h, wall, wall.Darkened(0.08f), new Color("8a3a2e"));
        if (south)
        {
            Face(x0 + 0.15f, x0 + 0.3f, oy1, 0.02f, h * 0.8f, Glass.Lightened(0.1f));
            Face(x0 + 0.36f, x0 + 0.48f, oy1, 0.02f, h * 0.7f, new Color("4a3a2a"));
            Face(x0 + 0.1f, x0 + 0.55f, oy1, h - 0.04f, h, trim);
            Lit(() => Face(x0 + 0.15f, x0 + 0.3f, oy1, 0.02f, h * 0.8f, WindowLit with { A = 0.6f }));
        }
        if (Fine)
        {
            // Oil stains where the cars stand, and a painted arrow at each driveway.
            C.DrawCircle(Q(lot.X + 0.5f, plan.Row, 0), Mathf.Max(1, _t * 0.06f), apron.Darkened(0.18f));
            var paint = new Color("e8e0c8") with { A = 0.7f };
            float ay = (plan.Row + edge) / 2;
            foreach (var (x, up) in new[] { (plan.In, true), (plan.Out, false) })
            {
                float tip = up ? -0.06f : 0.06f, sign = south ? 1 : -1;
                C.DrawLine(Q(x, ay - 0.06f, 0), Q(x, ay + 0.06f, 0), paint, Mathf.Max(1, _t * 0.012f));
                C.DrawLine(Q(x, ay + tip * sign, 0), Q(x - 0.03f, ay + tip * sign * 0.4f, 0), paint, Mathf.Max(1, _t * 0.012f));
                C.DrawLine(Q(x, ay + tip * sign, 0), Q(x + 0.03f, ay + tip * sign * 0.4f, 0), paint, Mathf.Max(1, _t * 0.012f));
            }
        }

        // The pump island, two pumps on it, and a canopy on two posts over it.
        float ix0 = lot.X + 0.32f, ix1 = lot.X + 0.68f, py = plan.Pump, top = 0.3f;
        Box(ix0, py - 0.04f, ix1, py + 0.04f, 0, 0.03f, new Color("b8b0a0"), new Color("a8a090"), new Color("c8c0b0"));
        var post = new Color("d8d0bc");
        float lw = Mathf.Max(1, _t * 0.02f);
        foreach (float px in new[] { ix0 + 0.03f, ix1 - 0.03f })
            C.DrawLine(Q(px, py, 0.03f), Q(px, py, top), post, lw);
        foreach (float px in new[] { lot.X + 0.42f, lot.X + 0.58f })
        {
            Box(px - 0.035f, py - 0.025f, px + 0.035f, py + 0.025f, 0.03f, 0.18f, new Color("c8463c"), new Color("a83a30"), new Color("d85a4c"));
            C.DrawCircle(Q(px, py, 0.21f), Mathf.Max(1.5f, _t * 0.028f), open ? new Color("f4ecd8") : new Color("8a8478"));
            Lit(() => { if (open) C.DrawCircle(Q(px, py, 0.21f), Mathf.Max(2, _t * 0.05f), WindowLit with { A = 0.5f }); });
        }
        float cy0 = py - 0.12f, cy1 = py + 0.12f;
        C.DrawColoredPolygon(new[] { Q(ix0 - 0.04f, cy0, top), Q(ix1 + 0.04f, cy0, top), Q(ix1 + 0.04f, cy1, top), Q(ix0 - 0.04f, cy1, top) }, open ? trim : trim.Darkened(0.3f));
        Face(ix0 - 0.04f, ix1 + 0.04f, cy1, top - 0.04f, top, new Color("e8d8a8"));
    }

    /// <summary>
    /// Cars and trucks calling at each filling station in view, one at a time: along the kerb lane, in at one driveway,
    /// a stop at the pumps with the hose in, and out by the other driveway and away up the street.
    /// </summary>
    private void DrawFillingStationTraffic(float night, Rect2 view)
    {
        var w = World!;
        foreach (var b in w.Businesses)
        {
            if (b.Kind != BusinessKind.GasStation || !b.IsOpen || Burnt(b)) continue;
            var lot = w.LotOf(b);
            if (!view.Grow(3).HasPoint(new Vector2(lot.X + 0.5f, lot.Y + 0.5f))) continue;
            var plan = StationPlan(lot);
            float mid = lot.X + 0.5f, run = 2.4f, speed = 0.7f, stop = 5;
            var points = new[]
            {
                new Vector2(plan.In - plan.Dir * run, plan.Lane), new Vector2(plan.In, plan.Lane), new Vector2(plan.In, plan.Row),
                new Vector2(mid, plan.Row), new Vector2(plan.Out, plan.Row), new Vector2(plan.Out, plan.Lane),
                new Vector2(plan.Out + plan.Dir * run, plan.Lane),
            };
            const int pumps = 3;
            float before = 0, after = 0;
            for (int i = 1; i < points.Length; i++)
                if (i <= pumps) before += points[i].DistanceTo(points[i - 1]);
                else after += points[i].DistanceTo(points[i - 1]);
            float visit = (before + after) / speed + stop, period = visit + 6 + Hash(lot.Id, 1780) * 14;
            float t = (_anim + Hash(lot.Id, 1781) * period) % period;
            if (t >= visit) continue;
            int trip = (int)((_anim + Hash(lot.Id, 1781) * period) / period);
            // How far along the way it has come: up to the pumps, waiting there, then on and out.
            float s = t < before / speed ? t * speed : t < before / speed + stop ? before : before + (t - before / speed - stop) * speed;
            bool filling = t >= before / speed && t < before / speed + stop;
            var (at, horizontal, heading) = Along(points, s);
            var ride = TrafficRide(Hash(lot.Id * 37 + trip, 1782), parked: false);
            if (ride is Ride.PoliceCar or Ride.PaddyWagon or Ride.RaidTruck or Ride.BankCar or Ride.Armoured) ride = Ride.ModelT;
            var keep = C;
            C = DynAt(at.Y);
            DrawVehicle(at, horizontal, heading, ride, RidePaint(ride, Hash(lot.Id * 37 + trip, 1783)), night, 0.75f);
            if (filling && Fine)
            {
                // The hose from the nearer pump to the tank.
                float px = plan.Dir < 0 ? lot.X + 0.42f : lot.X + 0.58f;
                C.DrawLine(Q(px, plan.Pump, 0.12f), Q(mid - plan.Dir * 0.05f, plan.Row, 0.08f), new Color("1e1c1a"), Mathf.Max(1, _t * 0.015f));
            }
            C = keep;
        }
    }

    /// <summary>A point a distance along a path of straight runs, which way the run there goes, and which way along it.</summary>
    private static (Vector2 At, bool Horizontal, int Heading) Along(Vector2[] points, float s)
    {
        for (int i = 1; i < points.Length; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            float len = a.DistanceTo(b);
            if (len <= 0) continue;
            bool horizontal = Mathf.Abs(b.X - a.X) >= Mathf.Abs(b.Y - a.Y);
            int heading = horizontal ? Math.Sign(b.X - a.X) : Math.Sign(b.Y - a.Y);
            if (s <= len || i == points.Length - 1) return (a.Lerp(b, Mathf.Clamp(s / len, 0, 1)), horizontal, heading);
            s -= len;
        }
        return (points[^1], true, 1);
    }

    /// <summary>A scrapyard: a board fence round heaps of junk and wrecked cars, a tin office by the gate.</summary>
    private void DrawScrapyard(Lot lot, float h, bool open)
    {
        float x0 = lot.X + 0.05f, y0 = lot.Y + 0.08f, x1 = lot.X + 0.95f, y1 = lot.Y + 0.92f;
        DrawJunk(lot, x0, y0, x1, y1);
        bool south = FacesSouth(lot);
        float oy1 = south ? y1 - 0.05f : y0 + 0.35f;
        var tin = open ? new Color("8e8a80") : new Color("8e8a80").Darkened(0.3f);
        Box(x0 + 0.06f, oy1 - 0.3f, x0 + 0.42f, oy1, 0, h, tin, tin.Darkened(0.1f), new Color("6a5444"));
        if (Fine)
            for (float x = x0 + 0.1f; x < x0 + 0.4f; x += 0.06f)
                C.DrawLine(Q(x, oy1 - 0.3f, h), Q(x, oy1, h), new Color("5a4838"), 1);
        if (south) Face(x0 + 0.16f, x0 + 0.28f, oy1, 0.02f, h * 0.75f, new Color("3a2e24"));
        DrawYardFence(x0, y0, x1, y1, south, gate: true);
    }

    /// <summary>The big scrapyard's back lot: more junk behind the fence, and the crane that sorts it.</summary>
    private void DrawYardLot(Lot lot)
    {
        float x0 = lot.X + 0.02f, y0 = lot.Y + 0.08f, x1 = lot.X + 0.98f, y1 = lot.Y + 0.92f;
        DrawJunk(lot, x0, y0, x1, y1);
        DrawYardFence(x0, y0, x1, y1, FacesSouth(lot), gate: false);
        // A derrick crane: a mast, a jib reaching over the heaps and a magnet on its cable.
        var steel = new Color("b0702c");
        float mx = lot.X + 0.3f, my = lot.Y + 0.35f, lw = Mathf.Max(2, _t * 0.04f);
        C.DrawLine(Q(mx, my, 0), Q(mx, my, 1.1f), steel, lw);
        C.DrawLine(Q(mx, my, 1.05f), Q(mx + 0.55f, my + 0.3f, 0.85f), steel, lw * 0.8f);
        C.DrawLine(Q(mx, my, 1.1f), Q(mx + 0.55f, my + 0.3f, 0.85f), steel.Darkened(0.2f), Mathf.Max(1, lw * 0.4f));
        C.DrawLine(Q(mx + 0.55f, my + 0.3f, 0.85f), Q(mx + 0.55f, my + 0.3f, 0.35f), new Color("2a241e"), Mathf.Max(1, _t * 0.012f));
        C.DrawCircle(Q(mx + 0.55f, my + 0.3f, 0.32f), Mathf.Max(2, _t * 0.05f), new Color("3a3632"));
        Box(mx - 0.08f, my - 0.06f, mx + 0.08f, my + 0.06f, 0, 0.12f, new Color("6a5444"), new Color("5a4838"), new Color("7a6454"));
    }

    /// <summary>Heaps of scrap, rusted car bodies and old tyres on cinder ground, laid out the same way every time.</summary>
    private void DrawJunk(Lot lot, float x0, float y0, float x1, float y1)
    {
        C.DrawColoredPolygon(new[] { Q(x0, y0, 0), Q(x1, y0, 0), Q(x1, y1, 0), Q(x0, y1, 0) }, Cinder);
        Color[] scrap = { Rust, new("5c5850"), new("7a6a50"), new("3e3a36") };
        for (int i = 0; i < 9; i++)
        {
            float cx = x0 + 0.12f + (x1 - x0 - 0.24f) * Hash(lot.Id, 1710 + i), cy = y0 + 0.12f + (y1 - y0 - 0.24f) * Hash(lot.Id, 1720 + i);
            float r = 0.08f + 0.08f * Hash(lot.Id, 1730 + i), tall = 0.1f + 0.12f * Hash(lot.Id, 1740 + i);
            var heap = scrap[i % scrap.Length];
            C.DrawColoredPolygon(new[] { Q(cx - r, cy, 0), Q(cx, cy - r * 0.6f, tall * 0.6f), Q(cx + r * 0.2f, cy, tall), Q(cx + r, cy, 0), Q(cx, cy + r * 0.6f, 0) }, heap);
            if (Fine) C.DrawLine(Q(cx - r * 0.5f, cy, tall * 0.4f), Q(cx + r * 0.4f, cy - r * 0.2f, tall * 0.7f), heap.Lightened(0.25f), 1);
        }
        if (!Fine) return;
        // A couple of wrecks with their wheels gone, and a stack of tyres.
        for (int i = 0; i < 3; i++)
        {
            float cx = x0 + 0.2f + (x1 - x0 - 0.4f) * Hash(lot.Id, 1750 + i), cy = y0 + 0.2f + (y1 - y0 - 0.4f) * Hash(lot.Id, 1760 + i);
            var body = CarPaint[(lot.Id + i) % CarPaint.Length].Darkened(0.45f).Lerp(Rust, 0.4f);
            Box(cx - 0.11f, cy - 0.05f, cx + 0.11f, cy + 0.05f, 0, 0.07f, body, body.Darkened(0.15f), body.Lightened(0.08f));
        }
        float tx = x1 - 0.14f, ty = y0 + 0.16f;
        for (int k = 0; k < 3; k++) C.DrawCircle(Q(tx, ty, 0.03f + k * 0.035f), Mathf.Max(1.5f, _t * 0.04f), new Color("1e1c1a"));
    }

    /// <summary>A board fence round the yard, with a gap for the gate on the street side.</summary>
    private void DrawYardFence(float x0, float y0, float x1, float y1, bool south, bool gate)
    {
        var board = new Color("6a5a44");
        float tall = 0.12f, lw = Mathf.Max(1, _t * 0.02f);
        float street = south ? y1 : y0, back = south ? y0 : y1;
        C.DrawLine(Q(x0, back, tall), Q(x1, back, tall), board, lw);
        C.DrawLine(Q(x0, y0, tall), Q(x0, y1, tall), board, lw);
        C.DrawLine(Q(x1, y0, tall), Q(x1, y1, tall), board, lw);
        float gapA = x0 + (x1 - x0) * 0.5f, gapB = gapA + 0.22f;
        if (gate)
        {
            Face(x0, gapA, street, 0, tall, board);
            Face(gapB, x1, street, 0, tall, board);
        }
        else Face(x0, x1, street, 0, tall, board);
    }
}
