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

    /// <summary>
    /// A 1920s sedan seen from above and to the south: a body, a cabin with glass, and wheels.
    /// <paramref name="horizontal"/> lays it along a street rather than an avenue.
    /// </summary>
    private void DrawCar(Vector2 centre, bool horizontal, Color paint, float night, float scale = 1, bool police = false, int heading = 1)
    {
        float len = 0.5f * scale, wid = 0.22f * scale, z0 = 0.03f * scale, z1 = 0.1f * scale, z2 = 0.19f * scale;
        float cx = centre.X, cy = centre.Y;
        var light = paint.Lightened(0.14f);
        var cabinTop = police ? new Color("e9e6df") : paint.Lightened(0.08f);
        var wheel = new Color("141414");
        if (horizontal)
        {
            Ellipse(P(cx + 0.06f, cy + 0.04f), new Vector2(len * 0.55f, wid * 0.6f) * _t, Shadow);
            Box(cx - len / 2, cy - wid / 2, cx + len / 2, cy + wid / 2, z0, z1, paint, light, light);
            float cab = cx - heading * len * 0.08f;
            Box(cab - len * 0.22f, cy - wid * 0.4f, cab + len * 0.22f, cy + wid * 0.4f, z1, z2, paint, light, cabinTop);
            if (Fine)
            {
                Face(cab - len * 0.17f, cab + len * 0.17f, cy + wid * 0.4f, z1 + 0.015f, z2 - 0.02f, Glass.Lightened(0.25f));
                C.DrawLine(Q(cab, cy + wid * 0.4f, z1 + 0.015f), Q(cab, cy + wid * 0.4f, z2 - 0.02f), paint, _t * 0.012f * scale);
                // A chrome strip down the side, the running board, and a bumper at each end.
                C.DrawLine(Q(cx - len / 2, cy + wid / 2, z1 - 0.012f), Q(cx + len / 2, cy + wid / 2, z1 - 0.012f), paint.Lightened(0.35f), _t * 0.006f);
                Face(cx - len * 0.36f, cx + len * 0.36f, cy + wid / 2 + 0.015f, z0 - 0.01f, z0 + 0.005f, new Color("1a1a1a"));
                Face(cx - len / 2 - 0.02f, cx - len / 2 + 0.04f, cy + wid / 2, z0, z0 + 0.018f, new Color("b8b4a8"));
                Face(cx + len / 2 - 0.04f, cx + len / 2 + 0.02f, cy + wid / 2, z0, z0 + 0.018f, new Color("b8b4a8"));
            }
            foreach (float wx in new[] { cx - len * 0.3f, cx + len * 0.3f })
            {
                var hub = Q(wx, cy + wid / 2, z0 + 0.01f);
                if (Fine) C.DrawColoredPolygon(new[] { Q(wx - len * 0.14f, cy + wid / 2 + 0.005f, z0), Q(wx + len * 0.14f, cy + wid / 2 + 0.005f, z0), Q(wx + len * 0.1f, cy + wid / 2 + 0.005f, z0 + 0.07f * scale), Q(wx - len * 0.1f, cy + wid / 2 + 0.005f, z0 + 0.07f * scale) }, paint.Darkened(0.35f));
                C.DrawCircle(hub, 0.045f * scale * _t, wheel);
                if (Fine)
                {
                    C.DrawCircle(hub, 0.026f * scale * _t, new Color("8a8478"));
                    C.DrawCircle(hub, 0.01f * scale * _t, new Color("2a2826"));
                }
            }
            float front = cx + heading * len / 2;
            C.DrawCircle(Q(front, cy + wid * 0.3f, z1 - 0.02f), 0.025f * scale * _t, new Color("f3e6b0"));
            if (police) C.DrawCircle(Q(cab, cy, z2 + 0.02f), _t * 0.035f, ((int)(_anim * 3) % 2 == 0 && Live) ? Palette.Bad : Palette.Police);
            if (night > 0.3f) _headlights.Add((P(front + heading * 0.28f, cy), night));
        }
        else
        {
            Ellipse(P(cx + 0.05f, cy + 0.05f), new Vector2(wid * 0.6f, len * 0.55f) * _t, Shadow);
            Box(cx - wid / 2, cy - len / 2, cx + wid / 2, cy + len / 2, z0, z1, paint, light, light);
            float cab = cy - heading * len * 0.06f;
            Box(cx - wid * 0.4f, cab - len * 0.2f, cx + wid * 0.4f, cab + len * 0.2f, z1, z2, paint, light, cabinTop);
            float lamps = cy + len / 2;
            if (heading > 0)
            {
                if (Fine)
                {
                    Face(cx - wid * 0.34f, cx + wid * 0.34f, cab + len * 0.2f, z1 + 0.015f, z2 - 0.02f, Glass.Lightened(0.25f));
                    // The radiator grille between the lamps, and the bumper under it.
                    Face(cx - wid * 0.16f, cx + wid * 0.16f, lamps, z0 + 0.015f, z1 - 0.005f, new Color("a8a498"));
                    for (float gx = cx - wid * 0.12f; gx < cx + wid * 0.14f; gx += wid * 0.06f)
                        C.DrawLine(Q(gx, lamps, z0 + 0.02f), Q(gx, lamps, z1 - 0.01f), new Color("4a4844"), _t * 0.004f);
                    Face(cx - wid * 0.5f, cx + wid * 0.5f, lamps + 0.012f, z0 - 0.005f, z0 + 0.012f, new Color("b8b4a8"));
                }
                C.DrawCircle(Q(cx - wid * 0.3f, lamps, z1 - 0.025f), 0.025f * scale * _t, new Color("f3e6b0"));
                C.DrawCircle(Q(cx + wid * 0.3f, lamps, z1 - 0.025f), 0.025f * scale * _t, new Color("f3e6b0"));
            }
            else
            {
                C.DrawCircle(Q(cx - wid * 0.3f, lamps, z1 - 0.025f), 0.02f * scale * _t, new Color("c03028"));
                C.DrawCircle(Q(cx + wid * 0.3f, lamps, z1 - 0.025f), 0.02f * scale * _t, new Color("c03028"));
            }
            if (police) C.DrawCircle(Q(cx, cab, z2 + 0.02f), _t * 0.035f, ((int)(_anim * 3) % 2 == 0 && Live) ? Palette.Bad : Palette.Police);
            if (night > 0.3f) _headlights.Add((P(cx, cy + heading * (len / 2 + 0.28f)), night));
        }
    }

    private readonly List<(Vector2 At, float Strength)> _headlights = new();

    private sealed record Traffic(bool Horizontal, int Line, int Heading, float Speed, float Phase, Color Paint);

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
            _traffic.Add(new Traffic(horizontal, line, heading, 0.6f + Hash(i, 203) * 0.7f, Hash(i, 204) * 40, CarPaint[(int)(Hash(i, 205) * CarPaint.Length)]));
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
            // Keep to the right: eastbound and southbound in the lower or left lane.
            float lane = car.Horizontal ? car.Line * CityMap.StrideY + 0.5f + 0.2f * car.Heading
                                        : car.Line * CityMap.StrideX + 0.5f - 0.2f * car.Heading;
            var centre = car.Horizontal ? new Vector2(pos, lane) : new Vector2(lane, pos);
            if (centre.X < 0.2f || centre.X > Map.Width - 0.2f || centre.Y < 0.2f || centre.Y > Map.Height - 0.2f) continue;
            if (!view.HasPoint(centre)) continue;
            C = DynAt(centre.Y);
            DrawCar(centre, car.Horizontal, car.Paint, night, 0.9f, heading: car.Heading);
        }
    }

    /// <summary>The boss's car outside each headquarters, and patrol cars outside the precinct houses.</summary>
    private void DrawParkedOfficials(float night, Rect2 view)
    {
        var w = World!;
        foreach (var lot in w.Map.Lots)
        {
            if (lot.Use is not (LotUse.Headquarters or LotUse.Precinct) || !view.HasPoint(new Vector2(lot.X, lot.Y))) continue;
            bool frontTop = lot.FrontY < lot.Y;
            float carY = lot.FrontY + 0.5f + (frontTop ? 0.24f : -0.24f);
            C = DynAt(carY);
            if (lot.Use == LotUse.Headquarters) DrawCar(new Vector2(lot.X + 0.5f, carY), true, Palette.Gang(w, lot.GangId).Darkened(0.35f), night);
            else
            {
                DrawCar(new Vector2(lot.X + 0.25f, carY), true, new Color("1a1a1e"), night, police: true);
                DrawCar(new Vector2(lot.X + 0.8f, carY), true, new Color("1a1a1e"), night, police: true);
            }
        }
    }

    /// <summary>People on the pavements, walking round the blocks. They're scenery, so they're small and muted.</summary>
    private static readonly Color[] Dresses = { new("8a3a44"), new("4a6680"), new("a88a4a"), new("6a4a7a"), new("3a5a4a"), new("b8a888") };
    private static readonly Color[] Skins = { new("e0b896"), new("c89870"), new("9a6a48"), new("ecc8a8"), new("b07c58") };

    private void DrawPassersBy(Rect2 view)
    {
        var coats = new[] { new Color("6a6258"), new Color("4e4a46"), new Color("7a6a58"), new Color("5a5f66"), new Color("8a7a66") };
        var hats = new[] { new Color("2c2926"), new Color("2c2926"), new Color("8a6a4a"), new Color("4a3a2a") };
        float r = Mathf.Max(Px(2.6f), _t * 0.1f);
        if (Tile < 9) return;
        for (int i = 0; i < 26 * Map.BlocksX * Map.BlocksY / 20; i++)
        {
            int bx = (int)(Hash(i, 300) * Map.BlocksX), by = (int)(Hash(i, 301) * Map.BlocksY);
            if (by == Map.RiverRow) continue;
            float x0 = bx * CityMap.StrideX + 1 - 0.11f, y0 = by * CityMap.StrideY + 1 - 0.11f;
            float w = CityMap.LotsPerBlockX + 0.22f, h = CityMap.LotsPerBlockY + 0.22f;
            float perimeter = 2 * (w + h);
            int dir = Hash(i, 304) < 0.5f ? 1 : -1;
            float d = (Hash(i, 302) * perimeter + _anim * (0.15f + Hash(i, 303) * 0.12f) * dir) % perimeter;
            if (d < 0) d += perimeter;
            Vector2 at, facing;
            if (d < w) { at = new Vector2(x0 + d, y0); facing = Vector2.Right; }
            else if (d < w + h) { at = new Vector2(x0 + w, y0 + d - w); facing = Vector2.Down; }
            else if (d < 2 * w + h) { at = new Vector2(x0 + w - (d - w - h), y0 + h); facing = Vector2.Left; }
            else { at = new Vector2(x0, y0 + h - (d - 2 * w - h)); facing = Vector2.Up; }
            if (!view.HasPoint(at)) continue;
            bool woman = i % 3 == 0;
            var coat = woman ? Dresses[i % Dresses.Length] : coats[i % coats.Length];
            var hat = woman ? Dresses[(i + 2) % Dresses.Length].Darkened(0.2f) : hats[i % hats.Length];
            DrawFigure(at * _t, facing * dir, r, coat, hat, hat.Lightened(0.2f), true, Skins[i % Skins.Length], woman);
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
