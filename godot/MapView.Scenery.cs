using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The river and its bridges, boats, the elevated railway, and everything on wheels or legs that
/// moves through the city: traffic, the boss's car outside headquarters, passers-by and smoke.
/// </summary>
public partial class MapView
{
    // ---- The river --------------------------------------------------------------

    /// <summary>A block of river: stone quays along both streets and water between, with piers sticking out.</summary>
    private void DrawRiver(int bx, int by)
    {
        float x0 = bx * CityMap.StrideX, y0 = by * CityMap.StrideY;
        float top = y0 + 1, bottom = y0 + CityMap.StrideY;
        var quay = new Color("7a7468");
        C.DrawRect(R(x0 + 0.95f, top, CityMap.StrideX - 0.9f, bottom - top), Water);
        C.DrawRect(R(x0 + 0.95f, top - 0.12f, CityMap.StrideX - 0.9f, 0.3f), quay);
        C.DrawRect(R(x0 + 0.95f, bottom - 0.18f, CityMap.StrideX - 0.9f, 0.3f), quay);
        C.DrawRect(R(x0 + 0.95f, top + 0.18f, CityMap.StrideX - 0.9f, 0.06f), Water.Darkened(0.35f));
        if (Fine)
        {
            // Ripples on the water.
            for (int i = 0; i < 10; i++)
            {
                float rx = x0 + 1.1f + Hash(bx * 13 + i, 600) * 3.6f, ry = top + 0.4f + Hash(bx * 13 + i, 601) * 1.3f;
                C.DrawLine(P(rx, ry), P(rx + 0.25f, ry), Water.Lightened(0.18f), 1);
            }
        }
        // Wooden piers off both quays.
        var wood = new Color("6b5038");
        for (int i = 0; i < 4; i++)
        {
            if (Hash(bx * 7 + i, 610) > 0.5f) continue;
            float px = x0 + 1.2f + i * 0.95f;
            bool north = i % 2 == 0;
            float py = north ? top + 0.18f : bottom - 0.78f;
            C.DrawRect(R(px, py, 0.32f, 0.6f), wood);
            for (float y = py + 0.08f; Fine && y < py + 0.6f; y += 0.1f) C.DrawLine(P(px, y), P(px + 0.32f, y), wood.Darkened(0.25f), 1);
        }
    }

    /// <summary>Where an avenue crosses the river: the road carries on between stone parapets.</summary>
    private void DrawBridge(float x0, float y0)
    {
        float top = y0 + 1, len = CityMap.StrideY - 1;
        var stone = new Color("9a9284");
        C.DrawRect(R(x0 + 0.02f, top, 0.1f, len), stone);
        C.DrawRect(R(x0 + 0.88f, top, 0.1f, len), stone);
        C.DrawRect(R(x0 + 0.98f, top + 0.18f, 0.12f, len - 0.3f), Water.Darkened(0.4f) with { A = 0.6f });
        for (float y = top + 0.3f; Fine && y < top + len; y += 0.5f)
        {
            C.DrawRect(R(x0 + 0.0f, y, 0.14f, 0.1f), stone.Lightened(0.1f));
            C.DrawRect(R(x0 + 0.86f, y, 0.14f, 0.1f), stone.Lightened(0.1f));
        }
    }

    /// <summary>Boats tied up along the quays, on some stretches of river.</summary>
    private void DrawMooring(Lot lot)
    {
        if (Hash(lot.Id, 620) > 0.22f) return;
        bool north = (lot.Y - 1) % CityMap.StrideY == 0;
        float y = north ? lot.Y + 0.42f : lot.Y + 0.55f;
        DrawBoat(lot.X + 0.5f, y, 0.75f, Hash(lot.Id, 621) < 0.5f ? 1 : -1, (int)(Hash(lot.Id, 622) * 3), 0);
    }

    /// <summary>A barge with crates, a tug with its funnel, or a launch.</summary>
    private void DrawBoat(float x, float y, float len, int heading, int kind, float night)
    {
        float w = kind == 0 ? 0.26f : 0.2f;
        var hull = kind switch { 0 => new Color("3a2e26"), 1 => new Color("2a2a30"), _ => new Color("e9e6df") };
        Ellipse(P(x + 0.05f, y + 0.08f), new Vector2(len * 0.55f, w * 0.6f) * _t, Water.Darkened(0.4f) with { A = 0.6f });
        Box(x - len / 2, y - w / 2, x + len / 2, y + w / 2, 0, 0.06f, hull, hull.Lightened(0.12f), kind == 2 ? new Color("a08060") : hull.Lightened(0.2f));
        float bow = x + heading * len / 2;
        C.DrawColoredPolygon(new[] { Q(bow, y - w / 2, 0.06f), Q(bow + heading * 0.12f, y, 0.06f), Q(bow, y + w / 2, 0.06f) }, kind == 2 ? new Color("a08060") : hull.Lightened(0.2f));
        float stern = x - heading * len * 0.3f;
        switch (kind)
        {
            case 0:
                for (int i = 0; i < 3; i++)
                {
                    float cx = x - len * 0.35f + i * len * 0.25f;
                    Box(cx, y - 0.08f, cx + len * 0.2f, y + 0.08f, 0.06f, 0.16f, new Color("9a7448"), new Color("aa8458"), new Color("b08a5a"));
                }
                break;
            case 1:
                Box(stern - 0.1f, y - 0.07f, stern + 0.1f, y + 0.07f, 0.06f, 0.2f, new Color("c9c2b2"), new Color("d9d2c2"), new Color("8a3a3a"));
                Box(stern - 0.03f, y - 0.03f, stern + 0.03f, y + 0.03f, 0.2f, 0.32f, new Color("2a2626"), new Color("3a3636"), new Color("1a1616"));
                break;
            default:
                Box(stern - 0.08f, y - 0.06f, stern + 0.08f, y + 0.06f, 0.06f, 0.14f, new Color("6b5038"), new Color("7b6048"), new Color("e9e6df"));
                break;
        }
    }

    // ---- The elevated railway ----------------------------------------------------

    /// <summary>One column's stretch of the El: iron columns, a girder deck and the rails on top.</summary>
    private void DrawElevated(int col)
    {
        float y = RailRow;
        float xa = col == 0 ? 0 : col * CityMap.StrideX, xb = col == Map.BlocksX - 1 ? Map.Width : (col + 1) * CityMap.StrideX;
        var iron = new Color("3a3a3e");
        float lw = Mathf.Max(1, _t * 0.04f);
        for (float x = xa + 0.5f; x < xb; x += 1)
        {
            C.DrawLine(Q(x, y + 0.25f, 0), Q(x, y + 0.25f, 0.62f), iron.Lightened(0.1f), lw);
            C.DrawLine(Q(x, y + 0.75f, 0), Q(x, y + 0.75f, 0.62f), iron, lw);
        }
        Box(xa, y + 0.2f, xb, y + 0.8f, 0.6f, 0.68f, iron, iron.Lightened(0.15f), new Color("4a443e"));
        if (!Fine) return;
        for (float x = xa; x < xb; x += 0.25f)
        {
            C.DrawLine(Q(x, y + 0.8f, 0.6f), Q(x + 0.125f, y + 0.8f, 0.68f), iron.Lightened(0.25f), 1);
            C.DrawLine(Q(x + 0.125f, y + 0.8f, 0.68f), Q(x + 0.25f, y + 0.8f, 0.6f), iron.Lightened(0.25f), 1);
            C.DrawLine(Q(x, y + 0.3f, 0.68f), Q(x, y + 0.7f, 0.68f), new Color("5a4a3a"), 1);
        }
        C.DrawLine(Q(xa, y + 0.4f, 0.69f), Q(xb, y + 0.4f, 0.69f), new Color("9a9a9e"), 1);
        C.DrawLine(Q(xa, y + 0.6f, 0.69f), Q(xb, y + 0.6f, 0.69f), new Color("9a9a9e"), 1);
    }

    /// <summary>A train of four cars running along the El, its windows lit after dark.</summary>
    private void DrawTrain(float night, Rect2 view)
    {
        if (RailRow < 0) return;
        float y = RailRow;
        if (y < view.Position.Y || y > view.End.Y + 2) return;
        C = DynAt(y);
        float run = Map.Width + 10;
        for (int train = 0; train < 2; train++)
        {
            int heading = train == 0 ? 1 : -1;
            float head = ((_anim * 1.6f + train * run / 2) % run) - 5;
            if (heading < 0) head = Map.Width - head;
            var paint = new Color("4a5a3a");
            var window = Glass.Lerp(new Color(2.4f, 2.1f, 1.2f), night);
            for (int car = 0; car < 4; car++)
            {
                float x0 = head - heading * car * 0.95f - (heading > 0 ? 0.9f : 0);
                if (x0 + 1 < view.Position.X || x0 > view.End.X) continue;
                Box(x0, y + 0.28f, x0 + 0.9f, y + 0.72f, 0.69f, 0.92f, paint, paint.Lightened(0.12f), new Color("3a3632"));
                if (!Fine) continue;
                for (int i = 0; i < 5; i++)
                    Face(x0 + 0.06f + i * 0.17f, x0 + 0.17f + i * 0.17f, y + 0.72f, 0.78f, 0.87f, window);
            }
        }
    }

    // ---- Vehicles and passers-by ---------------------------------------------

    private readonly List<(Vector2 At, float Strength)> _headlights = new();

    private sealed record Traffic(bool Horizontal, int Line, int Heading, float Speed, float Phase, Ride Ride, Color Paint);

    private List<Traffic> _traffic = new();

    private CityMap? _trafficFor;

    private void BuildTraffic()
    {
        _trafficFor = Map;
        _traffic = new List<Traffic>();
        int cars = 14 * (Map.BlocksX * Map.BlocksY) / 20;
        for (int i = 0; i < cars; i++)
        {
            bool horizontal = Hash(i, 200) < 0.6f;
            int line = (int)(Hash(i, 201) * ((horizontal ? Map.BlocksY : Map.BlocksX) + 1));
            int heading = Hash(i, 202) < 0.5f ? 1 : -1;
            var ride = TrafficRide(Hash(i, 206), parked: false);
            // Trucks and vans go slower than the cars.
            float speed = (0.6f + Hash(i, 203) * 0.7f) * (ride is Ride.StakeTruck or Ride.BeerTruck or Ride.Van or Ride.BankCar or Ride.PaddyWagon ? 0.7f : 1);
            _traffic.Add(new Traffic(horizontal, line, heading, speed, Hash(i, 204) * 40, ride, RidePaint(ride, Hash(i, 205))));
        }
    }

    private void DrawTraffic(float night, Rect2 view)
    {
        if (_trafficFor != Map) BuildTraffic();
        foreach (var car in _traffic)
        {
            float length = car.Horizontal ? Map.Width : Map.Height;
            float pos = ((car.Phase + _anim * car.Speed) % (length + 2)) - 1;
            if (car.Heading < 0) pos = length - pos;
            // Keep to the right: eastbound in the lower lane, southbound in the left one, clear of the parked cars.
            float lane = car.Horizontal ? Across(car.Line, CityMap.StrideY, car.Heading > 0 ? FarLane : NearLane)
                                        : Across(car.Line, CityMap.StrideX, car.Heading > 0 ? NearLane : FarLane);
            var centre = car.Horizontal ? new Vector2(pos, lane) : new Vector2(lane, pos);
            if (centre.X < 0.2f || centre.X > Map.Width - 0.2f || centre.Y < 0.2f || centre.Y > Map.Height - 0.2f) continue;
            if (!view.HasPoint(centre)) continue;
            C = DynAt(centre.Y);
            DrawVehicle(centre, car.Horizontal, car.Heading, car.Ride, car.Paint, night, 0.9f);
        }
    }

    /// <summary>The boss's car outside each headquarters, and patrol cars outside the precinct houses.</summary>
    private void DrawParkedOfficials(float night, Rect2 view)
    {
        var w = World!;
        foreach (var lot in w.Map.Lots)
        {
            if (lot.Use is not (LotUse.Headquarters or LotUse.Precinct) || !view.HasPoint(new Vector2(lot.X, lot.Y)) || CornerOf(lot) != null) continue;
            bool frontTop = lot.FrontY < lot.Y;
            // In the bay at the kerb outside.
            float carY = Across(lot.FrontY / CityMap.StrideY, CityMap.StrideY, frontTop ? FarBay : NearBay);
            C = DynAt(carY);
            if (lot.Use == LotUse.Headquarters) DrawVehicle(new Vector2(lot.X + 0.5f, carY), true, 1, Ride.Packard, Palette.Gang(w, lot.GangId).Darkened(0.35f), night);
            else
            {
                // A patrol car at the kerb, clear of the garage ramp, where the paddy wagon is kept.
                bool rampEast = !_ramps.TryGetValue(lot.Id, out bool east) || east;
                DrawVehicle(new Vector2(lot.X + (rampEast ? 0.3f : 0.7f), carY), true, rampEast ? 1 : -1, Ride.PoliceCar, PoliceBlack, night, 0.9f);
            }
        }
    }

    /// <summary>Barges and tugs working up and down the river.</summary>
    private void DrawRiverTraffic(Rect2 view)
    {
        if (Map.RiverRow < 0) return;
        float top = Map.RiverRow * CityMap.StrideY + 1;
        for (int i = 0; i < 2 + Map.BlocksX / 6; i++)
        {
            int heading = i % 2 == 0 ? 1 : -1;
            float y = top + (heading > 0 ? 1.25f : 0.75f);
            float run = Map.Width + 4;
            float x = ((_anim * (0.25f + Hash(i, 640) * 0.2f) + Hash(i, 641) * run) % run) - 2;
            if (heading < 0) x = Map.Width - x;
            if (!view.HasPoint(new Vector2(x, y))) continue;
            C = DynAt(y);
            // A wake trailing behind.
            for (int k = 1; k <= 3; k++)
                C.DrawLine(P(x - heading * (0.4f + k * 0.2f), y - 0.05f * k), P(x - heading * (0.4f + k * 0.2f), y + 0.05f * k), new Color("cfe0ea") with { A = 0.5f / k }, 1);
            DrawBoat(x, y, i % 3 == 0 ? 1.1f : 0.7f, heading, i % 3 == 0 ? 0 : 1, 0);
        }
    }

    /// <summary>Smoke rising from the factory chimneys and drifting east.</summary>
    private void DrawSmoke(Rect2 view)
    {
        if (!Fine) return;
        foreach (var lot in _factories)
        {
            if (!view.HasPoint(new Vector2(lot.X, lot.Y))) continue;
            var (_, y0, x1, _) = Footprint(lot);
            var chimney = Q(x1 - 0.15f, y0 + 0.12f, 1.27f);
            C = DynAt(lot.Y);
            for (int k = 0; k < 5; k++)
            {
                float phase = (_anim * 0.22f + k / 5f + Hash(lot.Id, 650)) % 1;
                var at = chimney + new Vector2(phase * 0.6f, -phase * 0.9f) * _t;
                C.DrawCircle(at, (0.05f + 0.13f * phase) * _t, new Color("8a8680") with { A = 0.45f * (1 - phase) });
            }
        }
    }
}
