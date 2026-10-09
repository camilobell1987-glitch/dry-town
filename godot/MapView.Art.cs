using System;
using System.Collections.Generic;
using DryTown.Core;
using Godot;

/// <summary>
/// The district's artwork, drawn from shapes at runtime: asphalt and pavements, lamps,
/// rooftops with awnings and trade signs, parks and parking lots, traffic, passers-by,
/// and the light changing through the day. Everything is in tile units so it scales with zoom.
/// </summary>
public partial class MapView
{
    // ---- Small helpers ---------------------------------------------------------

    private Vector2 _o;
    private float _t;

    private Vector2 P(float x, float y) => _o + new Vector2(x, y) * _t;
    private Rect2 R(float x, float y, float w, float h) => new(P(x, y), new Vector2(w, h) * _t);

    /// <summary>A stable pseudo-random number in [0, 1) for a pair of integers, so the city looks the same every frame.</summary>
    private static float Hash(int a, int b = 0)
    {
        uint h = (uint)(a * 374761393 + b * 668265263) ^ 0x9E3779B9u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    private void Ellipse(Vector2 centre, Vector2 radii, Color colour, float angle = 0, int points = 18)
    {
        var poly = new Vector2[points];
        for (int i = 0; i < points; i++)
        {
            float a = Mathf.Tau * i / points;
            poly[i] = centre + new Vector2(Mathf.Cos(a) * radii.X, Mathf.Sin(a) * radii.Y).Rotated(angle);
        }
        DrawColoredPolygon(poly, colour);
    }

    /// <summary>A soft pool of light: stacked translucent circles.</summary>
    private void Glow(Vector2 centre, float radius, Color colour, float strength)
    {
        if (strength <= 0.01f) return;
        for (int i = 6; i >= 1; i--)
            DrawCircle(centre, radius * i / 6f, colour with { A = colour.A * strength * 0.06f });
    }

    private void Star(Vector2 c, float r, Color colour, int points = 5, float inner = 0.45f)
    {
        var poly = new Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float a = -Mathf.Pi / 2 + Mathf.Pi * i / points;
            float rr = i % 2 == 0 ? r : r * inner;
            poly[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        }
        DrawColoredPolygon(poly, colour);
    }

    // ---- Light ----------------------------------------------------------------

    /// <summary>The hour shown on the map: the live clock during a week, Monday morning while planning.</summary>
    private float HourOfDay => Live ? Clock % 24 : 9.5f;

    /// <summary>0 in daylight, 1 at full night, with dusk and dawn in between.</summary>
    private float Night
    {
        get
        {
            float h = HourOfDay;
            if (h >= 7 && h <= 17) return 0;
            if (h > 17 && h < 21) return (h - 17) / 4;
            if (h >= 5 && h < 7) return 1 - (h - 5) / 2;
            return 1;
        }
    }

    // ---- Colours --------------------------------------------------------------

    private static readonly Color Asphalt = new("2b2a2c");
    private static readonly Color AsphaltWorn = new("323033");
    private static readonly Color Pavement = new("5e5850");
    private static readonly Color Kerb = new("777065");
    private static readonly Color RoadPaint = new("cfc6a8");
    private static readonly Color Grass = new("3f5a34");
    private static readonly Color GrassDark = new("33492b");
    private static readonly Color TreeLeaf = new("4f7a3d");
    private static readonly Color Dirt = new("5a4a38");
    private static readonly Color Shadow = new(0, 0, 0, 0.38f);
    private static readonly Color Lamplight = new("ffd88a");
    private static readonly Color NightTint = new(0.02f, 0.04f, 0.13f);

    private static readonly Color[] Roofs =
    {
        new("6b4a3a"), // brick
        new("5b5552"), // tar
        new("74685a"), // gravel
        new("4f5560"), // slate
        new("7a5a44"), // terracotta
        new("625b4c"), // weathered
    };

    private static readonly Color[] Awnings =
    {
        new("7b6a55"), new("5f6b5a"), new("6e5a63"), new("5a6573"),
    };

    private static readonly Color[] CarPaint =
    {
        new("1d1d1f"), new("1d1d1f"), new("2c3b2f"), new("4a2427"), new("cbbf9f"), new("2a3346"), new("54493a"),
    };

    // ---- Ground ---------------------------------------------------------------

    private void DrawGround()
    {
        var full = R(0, 0, CityMap.Width, CityMap.Height);
        DrawRect(full.Grow(_t * 0.25f), Pavement.Darkened(0.3f));
        DrawRect(full, Asphalt);

        // Patches of worn tarmac so the streets aren't one flat colour.
        for (int i = 0; i < 70; i++)
        {
            float x = Hash(i, 1) * CityMap.Width, y = Hash(i, 2) * CityMap.Height;
            if (!CityMap.IsRoad((int)x, (int)y)) continue;
            Ellipse(P(x, y), new Vector2(0.3f + Hash(i, 3) * 0.3f, 0.12f + Hash(i, 4) * 0.12f) * _t, AsphaltWorn with { A = 0.6f });
        }

        // Centre lines, broken at the crossings.
        float dash = 0.32f, gap = 0.28f, lw = Mathf.Max(1, _t * 0.035f);
        for (int row = 0; row <= CityMap.BlocksY; row++)
        {
            float y = row * CityMap.StrideY + 0.5f;
            for (int bx = 0; bx < CityMap.BlocksX; bx++)
                for (float x = bx * CityMap.StrideX + 1.25f; x < bx * CityMap.StrideX + CityMap.StrideX - 0.25f; x += dash + gap)
                    DrawLine(P(x, y), P(Mathf.Min(x + dash, bx * CityMap.StrideX + CityMap.StrideX - 0.25f), y), RoadPaint with { A = 0.45f }, lw);
        }
        for (int col = 0; col <= CityMap.BlocksX; col++)
        {
            float x = col * CityMap.StrideX + 0.5f;
            for (int by = 0; by < CityMap.BlocksY; by++)
                for (float y = by * CityMap.StrideY + 1.25f; y < by * CityMap.StrideY + CityMap.StrideY - 0.25f; y += dash + gap)
                    DrawLine(P(x, y), P(x, Mathf.Min(y + dash, by * CityMap.StrideY + CityMap.StrideY - 0.25f)), RoadPaint with { A = 0.45f }, lw);
        }

        // Zebra crossings on each side of every junction.
        for (int row = 0; row <= CityMap.BlocksY; row++)
        for (int col = 0; col <= CityMap.BlocksX; col++)
        {
            float cx = col * CityMap.StrideX, cy = row * CityMap.StrideY;
            for (int s = 0; s < 5; s++)
            {
                float k = 0.12f + s * 0.17f;
                if (col > 0) DrawRect(R(cx - 0.16f, cy + k, 0.12f, 0.08f), RoadPaint with { A = 0.35f });
                if (col < CityMap.BlocksX) DrawRect(R(cx + 1.04f, cy + k, 0.12f, 0.08f), RoadPaint with { A = 0.35f });
                if (row > 0) DrawRect(R(cx + k, cy - 0.16f, 0.08f, 0.12f), RoadPaint with { A = 0.35f });
                if (row < CityMap.BlocksY) DrawRect(R(cx + k, cy + 1.04f, 0.08f, 0.12f), RoadPaint with { A = 0.35f });
            }
        }

        // Pavements round each block, with a kerb edge and paving joints.
        for (int by = 0; by < CityMap.BlocksY; by++)
        for (int bx = 0; bx < CityMap.BlocksX; bx++)
        {
            var block = BlockRect(bx, by);
            var pave = block.Grow(_t * 0.2f);
            DrawRect(pave, Pavement);
            DrawRect(pave, Kerb, false, Mathf.Max(1, _t * 0.04f));
            for (float x = pave.Position.X + _t * 0.25f; x < pave.End.X; x += _t * 0.25f)
            {
                DrawLine(new Vector2(x, pave.Position.Y), new Vector2(x, block.Position.Y), Kerb with { A = 0.35f }, 1);
                DrawLine(new Vector2(x, block.End.Y), new Vector2(x, pave.End.Y), Kerb with { A = 0.35f }, 1);
            }
            DrawRect(block, Pavement.Darkened(0.25f));
        }
    }

    private Rect2 BlockRect(int bx, int by) =>
        R(bx * CityMap.StrideX + 1, by * CityMap.StrideY + 1, CityMap.LotsPerBlockX, CityMap.LotsPerBlockY);

    private IEnumerable<Vector2> LampPosts()
    {
        for (int by = 0; by < CityMap.BlocksY; by++)
        for (int bx = 0; bx < CityMap.BlocksX; bx++)
        {
            float x0 = bx * CityMap.StrideX + 1 - 0.1f, x1 = x0 + CityMap.LotsPerBlockX + 0.2f;
            float y0 = by * CityMap.StrideY + 1 - 0.1f, y1 = y0 + CityMap.LotsPerBlockY + 0.2f;
            yield return new Vector2(x0, y0);
            yield return new Vector2(x1, y1);
            yield return new Vector2(x0 + CityMap.LotsPerBlockX / 2f + 0.1f, y0);
            yield return new Vector2(x0 + CityMap.LotsPerBlockX / 2f + 0.1f, y1);
        }
    }

    private void DrawLampPosts(float night)
    {
        foreach (var l in LampPosts())
        {
            DrawCircle(P(l.X + 0.03f, l.Y + 0.03f), _t * 0.06f, Shadow);
            DrawCircle(P(l.X, l.Y), _t * 0.055f, new Color("1e1d1c"));
            DrawCircle(P(l.X, l.Y), _t * 0.03f, night > 0.2f ? Lamplight : new Color("8a8478"));
        }
    }

    private void DrawStreetNames(Font font)
    {
        var w = World!;
        int size = Mathf.Max(9, (int)(_t * 0.26f));
        var ink = Palette.Ink with { A = 0.5f };
        for (int i = 0; i < w.Map.StreetNames.Length; i++)
        {
            float y = i * CityMap.StrideY + 0.5f;
            for (int bx = 0; bx < CityMap.BlocksX; bx += 2)
                DrawString(font, P(bx * CityMap.StrideX + 1.3f, y) + new Vector2(0, size * 0.35f),
                    $"{w.Map.StreetNames[i].ToUpperInvariant()} ST", HorizontalAlignment.Left, -1, size, ink);
        }
        for (int i = 0; i < w.Map.AvenueNames.Length; i++)
        {
            float x = i * CityMap.StrideX + 0.5f;
            for (int by = 1; by < CityMap.BlocksY; by += 2)
            {
                DrawSetTransform(P(x, by * CityMap.StrideY + CityMap.StrideY - 0.3f) + new Vector2(size * 0.35f, 0), -Mathf.Pi / 2);
                DrawString(font, Vector2.Zero, $"{w.Map.AvenueNames[i].ToUpperInvariant()} AVE", HorizontalAlignment.Left, -1, size, ink);
                DrawSetTransform(Vector2.Zero, 0);
            }
        }
    }

    // ---- Buildings ------------------------------------------------------------

    /// <summary>How tall a lot's building reads, which sets the length of its shadow.</summary>
    private float HeightOf(Lot lot)
    {
        if (lot.Use == LotUse.Empty) return 0;
        if (lot.Use == LotUse.Precinct) return 1.3f;
        if (lot.Use == LotUse.Headquarters) return 1.2f;
        var b = World!.BusinessById(lot.BusinessId);
        return b.Kind switch
        {
            BusinessKind.Hotel => 1.8f,
            BusinessKind.Warehouse => 1.1f,
            BusinessKind.Garage => 0.6f,
            _ => 0.7f + Hash(lot.Id, 7) * 0.6f,
        };
    }

    private Rect2 Footprint(Lot lot) => R(lot.X + 0.05f, lot.Y + 0.05f, 0.9f, 0.9f);

    private void DrawShadows()
    {
        foreach (var lot in World!.Map.Lots)
        {
            float h = HeightOf(lot);
            if (h <= 0) continue;
            var r = Footprint(lot);
            r.Position += new Vector2(h, h) * _t * 0.09f;
            DrawRect(r, Shadow);
        }
    }

    private void DrawEmptyLot(Lot lot)
    {
        var r = R(lot.X, lot.Y, 1, 1).Grow(-_t * 0.03f);
        float kind = Hash(lot.Id, 11);
        if (kind < 0.38f)
        {
            // A pocket park: grass, a path, a bench and a couple of trees.
            DrawRect(r, Grass);
            DrawRect(R(lot.X + 0.45f, lot.Y, 0.1f, 1), Pavement with { A = 0.7f });
            DrawRect(R(lot.X + 0.12f, lot.Y + 0.44f, 0.22f, 0.07f), new Color("5a4632"));
            for (int i = 0; i < 2 + (int)(Hash(lot.Id, 12) * 2); i++)
            {
                var c = new Vector2(lot.X + 0.2f + Hash(lot.Id, 20 + i) * 0.6f, lot.Y + 0.2f + Hash(lot.Id, 30 + i) * 0.6f);
                float rad = 0.13f + Hash(lot.Id, 40 + i) * 0.08f;
                DrawCircle(P(c.X + 0.06f, c.Y + 0.07f), rad * _t, Shadow);
                DrawCircle(P(c.X, c.Y), rad * _t, GrassDark.Lerp(TreeLeaf, 0.5f));
                DrawCircle(P(c.X - rad * 0.25f, c.Y - rad * 0.25f), rad * 0.65f * _t, TreeLeaf);
            }
        }
        else if (kind < 0.7f)
        {
            // A vacant lot: dirt, rubble and a broken fence.
            DrawRect(r, Dirt);
            for (int i = 0; i < 9; i++)
            {
                var c = P(lot.X + 0.1f + Hash(lot.Id, 50 + i) * 0.8f, lot.Y + 0.1f + Hash(lot.Id, 60 + i) * 0.8f);
                DrawCircle(c, _t * (0.025f + Hash(lot.Id, 70 + i) * 0.04f), i % 3 == 0 ? GrassDark : Dirt.Lightened(0.15f));
            }
            var fence = new Color("4a3c2e");
            float fy = lot.FrontY < lot.Y ? lot.Y + 0.06f : lot.Y + 0.94f;
            for (float x = lot.X + 0.05f; x < lot.X + 0.95f; x += 0.11f)
                if (Hash(lot.Id, (int)(x * 100)) > 0.25f) DrawRect(R(x, fy - 0.04f, 0.07f, 0.08f), fence);
        }
        else
        {
            // A parking lot with a car or two.
            DrawRect(r, Asphalt.Lightened(0.06f));
            for (int i = 1; i < 4; i++) DrawLine(P(lot.X + i * 0.25f, lot.Y + 0.1f), P(lot.X + i * 0.25f, lot.Y + 0.42f), RoadPaint with { A = 0.4f }, 1);
            for (int i = 0; i < 3; i++)
                if (Hash(lot.Id, 80 + i) > 0.45f)
                    DrawCar(new Vector2(lot.X + 0.125f + i * 0.25f + 0.0f, lot.Y + 0.27f), false, CarPaint[(int)(Hash(lot.Id, 90 + i) * CarPaint.Length)], 0, 0.8f);
        }
    }

    private void DrawBuilding(Lot lot, Color roof, float night)
    {
        var r = Footprint(lot);
        DrawRect(r, roof);
        // Parapet: lighter rim on the sunny sides, darker on the shaded ones.
        float rim = Mathf.Max(1, _t * 0.05f);
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, rim)), roof.Lightened(0.18f));
        DrawRect(new Rect2(r.Position, new Vector2(rim, r.Size.Y)), roof.Lightened(0.12f));
        DrawRect(new Rect2(r.Position.X, r.End.Y - rim, r.Size.X, rim), roof.Darkened(0.25f));
        DrawRect(new Rect2(r.End.X - rim, r.Position.Y, rim, r.Size.Y), roof.Darkened(0.2f));

        // Rooftop clutter: a vent or two and a chimney, placed away from the sign.
        int clutter = 1 + (int)(Hash(lot.Id, 3) * 3);
        for (int i = 0; i < clutter; i++)
        {
            float cx = lot.X + 0.15f + Hash(lot.Id, 100 + i) * 0.2f + (i % 2) * 0.5f;
            float cy = lot.Y + 0.15f + Hash(lot.Id, 110 + i) * 0.1f + (lot.FrontY < lot.Y ? 0.5f : 0f);
            var box = R(cx, cy, 0.1f, 0.1f);
            DrawRect(new Rect2(box.Position + Vector2.One * _t * 0.03f, box.Size), Shadow);
            DrawRect(box, roof.Darkened(0.35f));
            if (i == 0 && night > 0.3f && Hash(lot.Id, 5) > 0.4f)
                DrawRect(box.Grow(-_t * 0.02f), Lamplight with { A = 0.7f * night });
        }
    }

    /// <summary>The awning over the shop door: striped in the protecting gang's colour, plain canvas otherwise.</summary>
    private void DrawAwning(Lot lot, Color? gang, bool open)
    {
        bool frontTop = lot.FrontY < lot.Y;
        float depth = 0.2f;
        float y = frontTop ? lot.Y + 0.05f : lot.Y + 0.95f - depth;
        var canvas = Awnings[(int)(Hash(lot.Id, 9) * Awnings.Length)];
        int stripes = 6;
        for (int i = 0; i < stripes; i++)
        {
            var c = gang is Color g ? (i % 2 == 0 ? g : new Color("e9e0c8")) : (i % 2 == 0 ? canvas : canvas.Lightened(0.2f));
            if (!open) c = c.Darkened(0.45f);
            DrawRect(R(lot.X + 0.05f + i * 0.9f / stripes, y, 0.9f / stripes, depth), c);
        }
        // Scalloped edge over the pavement.
        float edgeY = frontTop ? y : y + depth;
        for (int i = 0; i < stripes; i++)
            DrawCircle(P(lot.X + 0.05f + (i + 0.5f) * 0.9f / stripes, edgeY), _t * 0.045f, gang is Color g2 && i % 2 == 0 ? g2.Darkened(open ? 0.15f : 0.5f) : (gang.HasValue ? new Color("d6ccb2") : canvas).Darkened(open ? 0.1f : 0.5f));
    }

    private void DrawSign(Lot lot, Business b)
    {
        bool frontTop = lot.FrontY < lot.Y;
        var c = new Vector2(lot.X + 0.5f, lot.Y + (frontTop ? 0.58f : 0.42f));
        float s = 0.42f;
        var plate = R(c.X - s / 2, c.Y - s / 2, s, s);
        DrawRect(new Rect2(plate.Position + Vector2.One * _t * 0.025f, plate.Size), Shadow);
        DrawRect(plate, new Color("e6dcc2"));
        DrawRect(plate, new Color("3a332b"), false, Mathf.Max(1, _t * 0.025f));
        DrawTradeIcon(b.Kind, P(c.X, c.Y), s * _t * 0.42f);
    }

    /// <summary>A pictogram for each trade, drawn inside a box of half-size <paramref name="h"/>.</summary>
    private void DrawTradeIcon(BusinessKind kind, Vector2 c, float h)
    {
        var ink = new Color("2a241e");
        float lw = Mathf.Max(1, h * 0.16f);
        switch (kind)
        {
            case BusinessKind.Grocer:
                DrawRect(new Rect2(c.X - h * 0.8f, c.Y, h * 1.6f, h * 0.7f), new Color("8a5e34"));
                DrawLine(new Vector2(c.X - h * 0.8f, c.Y + h * 0.35f), new Vector2(c.X + h * 0.8f, c.Y + h * 0.35f), ink with { A = 0.6f }, 1);
                DrawCircle(c + new Vector2(-h * 0.45f, -h * 0.15f), h * 0.32f, new Color("c8463c"));
                DrawCircle(c + new Vector2(h * 0.05f, -h * 0.22f), h * 0.32f, new Color("e0903a"));
                DrawCircle(c + new Vector2(h * 0.5f, -h * 0.12f), h * 0.3f, new Color("8fae4a"));
                break;
            case BusinessKind.Diner:
                DrawRect(new Rect2(c.X - h * 0.6f, c.Y - h * 0.3f, h * 1.0f, h * 0.9f), ink);
                DrawArc(c + new Vector2(h * 0.45f, h * 0.15f), h * 0.28f, -Mathf.Pi / 2, Mathf.Pi / 2, 10, ink, lw);
                DrawRect(new Rect2(c.X - h * 0.8f, c.Y + h * 0.62f, h * 1.6f, h * 0.14f), ink);
                for (int i = 0; i < 2; i++)
                    DrawLine(c + new Vector2(-h * 0.3f + i * h * 0.4f, -h * 0.45f), c + new Vector2(-h * 0.2f + i * h * 0.4f, -h * 0.85f), ink with { A = 0.6f }, lw * 0.7f);
                break;
            case BusinessKind.Barber:
            {
                var pole = new Rect2(c.X - h * 0.28f, c.Y - h * 0.8f, h * 0.56f, h * 1.6f);
                DrawRect(pole, Colors.White);
                for (int i = 0; i < 4; i++)
                {
                    float y = pole.Position.Y + h * 0.1f + i * h * 0.4f;
                    DrawLine(new Vector2(pole.Position.X, y + h * 0.25f), new Vector2(pole.End.X, y), i % 2 == 0 ? new Color("c8463c") : new Color("3a5fb0"), lw * 1.2f);
                }
                DrawRect(new Rect2(c.X - h * 0.38f, c.Y - h * 0.95f, h * 0.76f, h * 0.18f), ink);
                DrawRect(new Rect2(c.X - h * 0.38f, c.Y + h * 0.78f, h * 0.76f, h * 0.18f), ink);
                break;
            }
            case BusinessKind.Tailor:
                DrawArc(c + new Vector2(-h * 0.45f, h * 0.5f), h * 0.25f, 0, Mathf.Tau, 12, ink, lw);
                DrawArc(c + new Vector2(h * 0.45f, h * 0.5f), h * 0.25f, 0, Mathf.Tau, 12, ink, lw);
                DrawLine(c + new Vector2(-h * 0.3f, h * 0.3f), c + new Vector2(h * 0.45f, -h * 0.85f), ink, lw);
                DrawLine(c + new Vector2(h * 0.3f, h * 0.3f), c + new Vector2(-h * 0.45f, -h * 0.85f), ink, lw);
                break;
            case BusinessKind.Garage:
                DrawCircle(c, h * 0.85f, ink);
                DrawCircle(c, h * 0.45f, new Color("9a9286"));
                DrawCircle(c, h * 0.15f, ink);
                for (int i = 0; i < 8; i++)
                {
                    var d = Vector2.Right.Rotated(Mathf.Tau * i / 8);
                    DrawLine(c + d * h * 0.62f, c + d * h * 0.85f, new Color("e6dcc2") with { A = 0.35f }, 1);
                }
                break;
            case BusinessKind.Laundry:
            {
                var shirt = new[]
                {
                    c + new Vector2(-h * 0.3f, -h * 0.8f), c + new Vector2(h * 0.3f, -h * 0.8f), c + new Vector2(h * 0.9f, -h * 0.35f),
                    c + new Vector2(h * 0.6f, 0), c + new Vector2(h * 0.45f, -h * 0.15f), c + new Vector2(h * 0.45f, h * 0.85f),
                    c + new Vector2(-h * 0.45f, h * 0.85f), c + new Vector2(-h * 0.45f, -h * 0.15f), c + new Vector2(-h * 0.6f, 0),
                    c + new Vector2(-h * 0.9f, -h * 0.35f),
                };
                DrawColoredPolygon(shirt, new Color("5d8fd6"));
                DrawLine(c + new Vector2(0, -h * 0.75f), c + new Vector2(0, h * 0.8f), ink with { A = 0.5f }, 1);
                break;
            }
            case BusinessKind.Hotel:
                DrawRect(new Rect2(c.X - h * 0.85f, c.Y - h * 0.1f, h * 1.7f, h * 0.55f), new Color("8a3a3a"));
                DrawRect(new Rect2(c.X - h * 0.85f, c.Y - h * 0.45f, h * 0.55f, h * 0.35f), Colors.White);
                DrawRect(new Rect2(c.X - h * 0.9f, c.Y - h * 0.7f, h * 0.15f, h * 1.5f), ink);
                DrawRect(new Rect2(c.X + h * 0.75f, c.Y - h * 0.1f, h * 0.15f, h * 0.9f), ink);
                break;
            case BusinessKind.Pharmacy:
                DrawRect(new Rect2(c.X - h * 0.25f, c.Y - h * 0.8f, h * 0.5f, h * 1.6f), new Color("3f8f4f"));
                DrawRect(new Rect2(c.X - h * 0.8f, c.Y - h * 0.25f, h * 1.6f, h * 0.5f), new Color("3f8f4f"));
                break;
            case BusinessKind.PoolHall:
                DrawCircle(c, h * 0.85f, ink);
                DrawCircle(c + new Vector2(-h * 0.12f, -h * 0.12f), h * 0.4f, Colors.White);
                DrawString(ThemeDB.FallbackFont, c + new Vector2(-h * 0.52f, h * 0.12f), "8", HorizontalAlignment.Center, h * 0.8f, Mathf.Max(6, (int)(h * 0.65f)), ink);
                break;
            case BusinessKind.Warehouse:
                foreach (var (dx, dy) in new[] { (-0.85f, 0.0f), (0.0f, 0.0f), (-0.42f, -0.8f) })
                {
                    var box = new Rect2(c.X + dx * h, c.Y + dy * h, h * 0.8f, h * 0.8f);
                    DrawRect(box, new Color("9a7448"));
                    DrawRect(box, ink with { A = 0.7f }, false, 1);
                    DrawLine(box.Position, box.End, ink with { A = 0.4f }, 1);
                }
                break;
        }
    }

    /// <summary>A badge on the roof for the racket in the back room.</summary>
    private void DrawRacketBadge(Lot lot, RacketKind racket)
    {
        var c = P(lot.X + 0.82f, lot.Y + (lot.FrontY < lot.Y ? 0.82f : 0.18f));
        float r = _t * 0.15f;
        DrawCircle(c + Vector2.One * _t * 0.02f, r, Shadow);
        DrawCircle(c, r, new Color("7a1f1a"));
        DrawArc(c, r, 0, Mathf.Tau, 16, new Color("e05a4f"), Mathf.Max(1, _t * 0.03f));
        float h = r * 0.55f;
        switch (racket)
        {
            case RacketKind.Speakeasy: // a bottle
                DrawRect(new Rect2(c.X - h * 0.4f, c.Y - h * 0.2f, h * 0.8f, h * 1.1f), new Color("c9a24a"));
                DrawRect(new Rect2(c.X - h * 0.15f, c.Y - h * 0.9f, h * 0.3f, h * 0.7f), new Color("c9a24a"));
                break;
            case RacketKind.Still: // a copper pot
                DrawCircle(c + new Vector2(0, h * 0.2f), h * 0.7f, new Color("c87a3a"));
                DrawRect(new Rect2(c.X - h * 0.12f, c.Y - h * 0.9f, h * 0.24f, h * 0.6f), new Color("c87a3a"));
                break;
            case RacketKind.Numbers: // a die
                DrawRect(new Rect2(c.X - h * 0.75f, c.Y - h * 0.75f, h * 1.5f, h * 1.5f), Colors.White);
                DrawCircle(c, h * 0.18f, Colors.Black);
                DrawCircle(c + new Vector2(-h * 0.4f, -h * 0.4f), h * 0.16f, Colors.Black);
                DrawCircle(c + new Vector2(h * 0.4f, h * 0.4f), h * 0.16f, Colors.Black);
                break;
            case RacketKind.LoanShark: // a coin
                DrawCircle(c, h * 0.85f, new Color("e8b84a"));
                DrawString(ThemeDB.FallbackFont, c + new Vector2(-h, h * 0.4f), "$", HorizontalAlignment.Center, h * 2, Mathf.Max(6, (int)(h * 1.3f)), new Color("5a3f10"));
                break;
        }
    }

    private void DrawBoardedUp(Lot lot)
    {
        var plank = new Color("7a6448");
        float lw = Mathf.Max(2, _t * 0.08f);
        DrawLine(P(lot.X + 0.12f, lot.Y + 0.15f), P(lot.X + 0.88f, lot.Y + 0.85f), plank, lw);
        DrawLine(P(lot.X + 0.88f, lot.Y + 0.15f), P(lot.X + 0.12f, lot.Y + 0.85f), plank, lw);
    }

    private void DrawHeadquarters(Lot lot, Color gang, float night)
    {
        DrawBuilding(lot, new Color("3a3534"), night);
        bool frontTop = lot.FrontY < lot.Y;
        // A neon sign in the gang's colour, brighter after dark.
        var sign = R(lot.X + 0.18f, lot.Y + (frontTop ? 0.12f : 0.6f), 0.64f, 0.28f);
        DrawRect(sign, new Color("1a1716"));
        DrawRect(sign, gang, false, Mathf.Max(1, _t * 0.035f));
        int size = Mathf.Max(7, (int)(_t * 0.22f));
        DrawString(ThemeDB.FallbackFont, new Vector2(sign.Position.X, sign.GetCenter().Y + size * 0.36f), "HQ", HorizontalAlignment.Center, sign.Size.X, size, gang.Lightened(0.2f));
        // A strong-room door on the roof and a pennant.
        DrawRect(R(lot.X + 0.2f, lot.Y + (frontTop ? 0.55f : 0.18f), 0.22f, 0.22f), new Color("2a2626"));
        var pole = P(lot.X + 0.72f, lot.Y + (frontTop ? 0.58f : 0.2f));
        DrawLine(pole, pole + new Vector2(0, _t * 0.25f), new Color("1a1716"), Mathf.Max(1, _t * 0.03f));
        DrawColoredPolygon(new[] { pole, pole + new Vector2(_t * 0.22f, _t * 0.06f), pole + new Vector2(0, _t * 0.12f) }, gang);
        // The boss's car out front.
        float carY = lot.FrontY + 0.5f + (frontTop ? 0.22f : -0.22f);
        DrawCar(new Vector2(lot.X + 0.5f, carY), true, gang.Darkened(0.35f), night, 1);
    }

    private void DrawPrecinct(Lot lot, float night)
    {
        DrawBuilding(lot, new Color("6d6a66"), night);
        var r = Footprint(lot);
        DrawRect(r.Grow(-_t * 0.08f), Palette.Police.Darkened(0.55f), false, Mathf.Max(1, _t * 0.05f));
        Star(r.GetCenter(), _t * 0.24f, new Color("d9b44a"), 6, 0.55f);
        DrawCircle(r.GetCenter(), _t * 0.07f, Palette.Police.Darkened(0.4f));
        // Patrol cars at the kerb, black and white.
        bool frontTop = lot.FrontY < lot.Y;
        float carY = lot.FrontY + 0.5f + (frontTop ? 0.22f : -0.22f);
        DrawCar(new Vector2(lot.X + 0.25f, carY), true, new Color("1a1a1e"), night, 1, police: true);
        DrawCar(new Vector2(lot.X + 0.8f, carY), true, new Color("1a1a1e"), night, 1, police: true);
    }

    // ---- Vehicles and passers-by ---------------------------------------------

    /// <summary>A 1920s sedan seen from above. <paramref name="horizontal"/> lays it along a street rather than an avenue.</summary>
    private void DrawCar(Vector2 centre, bool horizontal, Color paint, float night, float scale = 1, bool police = false, int heading = 1)
    {
        float len = 0.5f * scale, wid = 0.24f * scale;
        var size = horizontal ? new Vector2(len, wid) : new Vector2(wid, len);
        var body = R(centre.X - size.X / 2, centre.Y - size.Y / 2, size.X, size.Y);
        DrawRect(new Rect2(body.Position + Vector2.One * _t * 0.04f, body.Size), Shadow);
        DrawRect(body, paint);
        // Cabin roof and windscreen towards the front.
        var axis = horizontal ? new Vector2(heading, 0) : new Vector2(0, heading);
        var cabinCentre = P(centre.X, centre.Y) - axis * _t * len * 0.05f;
        var cabinSize = (horizontal ? new Vector2(len * 0.45f, wid * 0.8f) : new Vector2(wid * 0.8f, len * 0.45f)) * _t;
        var cabin = new Rect2(cabinCentre - cabinSize / 2, cabinSize);
        DrawRect(cabin, police ? new Color("e9e6df") : paint.Lightened(0.15f));
        var glassCentre = cabinCentre + axis * (_t * len * 0.27f);
        var glassSize = (horizontal ? new Vector2(len * 0.08f, wid * 0.7f) : new Vector2(wid * 0.7f, len * 0.08f)) * _t;
        DrawRect(new Rect2(glassCentre - glassSize / 2, glassSize), new Color("8fa3b0"));
        if (police) DrawCircle(cabinCentre, _t * 0.04f, ((int)(_anim * 3) % 2 == 0 && Live) ? Palette.Bad : Palette.Police);
        // Headlamps.
        var front = P(centre.X, centre.Y) + axis * _t * len * 0.5f;
        var side = (horizontal ? Vector2.Down : Vector2.Right) * _t * wid * 0.3f;
        DrawCircle(front + side, _t * 0.03f, new Color("f3e6b0"));
        DrawCircle(front - side, _t * 0.03f, new Color("f3e6b0"));
        if (night > 0.3f)
        {
            _headlights.Add((front + side + axis * _t * 0.25f, night));
            _headlights.Add((front - side + axis * _t * 0.25f, night));
        }
    }

    private readonly List<(Vector2 At, float Strength)> _headlights = new();

    private sealed record Traffic(bool Horizontal, int Line, int Heading, float Speed, float Phase, Color Paint);

    private List<Traffic> _traffic = new();

    private void BuildTraffic()
    {
        _traffic = new List<Traffic>();
        for (int i = 0; i < 14; i++)
        {
            bool horizontal = Hash(i, 200) < 0.6f;
            int line = (int)(Hash(i, 201) * ((horizontal ? CityMap.BlocksY : CityMap.BlocksX) + 1));
            int heading = Hash(i, 202) < 0.5f ? 1 : -1;
            _traffic.Add(new Traffic(horizontal, line, heading, 0.6f + Hash(i, 203) * 0.7f, Hash(i, 204) * 40, CarPaint[(int)(Hash(i, 205) * CarPaint.Length)]));
        }
    }

    private void DrawTraffic(float night)
    {
        foreach (var car in _traffic)
        {
            float length = car.Horizontal ? CityMap.Width : CityMap.Height;
            float pos = ((car.Phase + _anim * car.Speed) % (length + 2)) - 1;
            if (car.Heading < 0) pos = length - pos;
            // Keep to the right: eastbound and southbound in the lower or left lane.
            float lane = car.Horizontal ? car.Line * CityMap.StrideY + 0.5f + 0.2f * car.Heading
                                        : car.Line * CityMap.StrideX + 0.5f - 0.2f * car.Heading;
            var centre = car.Horizontal ? new Vector2(pos, lane) : new Vector2(lane, pos);
            if (centre.X < 0.2f || centre.X > CityMap.Width - 0.2f || centre.Y < 0.2f || centre.Y > CityMap.Height - 0.2f) continue;
            DrawCar(centre, car.Horizontal, car.Paint, night, 0.9f, heading: car.Heading);
        }
    }

    /// <summary>People on the pavements, walking round the blocks. They're scenery, so they're small and muted.</summary>
    private void DrawPassersBy()
    {
        var coats = new[] { new Color("6a6258"), new Color("4e4a46"), new Color("7a6a58"), new Color("5a5f66"), new Color("8a7a66") };
        for (int i = 0; i < 26; i++)
        {
            int bx = (int)(Hash(i, 300) * CityMap.BlocksX), by = (int)(Hash(i, 301) * CityMap.BlocksY);
            float x0 = bx * CityMap.StrideX + 1 - 0.11f, y0 = by * CityMap.StrideY + 1 - 0.11f;
            float w = CityMap.LotsPerBlockX + 0.22f, h = CityMap.LotsPerBlockY + 0.22f;
            float perimeter = 2 * (w + h);
            float d = (Hash(i, 302) * perimeter + _anim * (0.15f + Hash(i, 303) * 0.12f) * (Hash(i, 304) < 0.5f ? 1 : -1)) % perimeter;
            if (d < 0) d += perimeter;
            Vector2 at;
            if (d < w) at = new Vector2(x0 + d, y0);
            else if (d < w + h) at = new Vector2(x0 + w, y0 + d - w);
            else if (d < 2 * w + h) at = new Vector2(x0 + w - (d - w - h), y0 + h);
            else at = new Vector2(x0, y0 + h - (d - 2 * w - h));
            var p = P(at.X, at.Y);
            float r = Mathf.Max(2, _t * 0.07f);
            DrawCircle(p + Vector2.One * r * 0.4f, r, Shadow);
            DrawCircle(p, r, coats[i % coats.Length]);
            DrawCircle(p, r * 0.55f, Hash(i, 305) < 0.6f ? new Color("2c2926") : new Color("8a6a4a"));
        }
    }

    // ---- Night ----------------------------------------------------------------

    private void DrawNight(float night)
    {
        if (night <= 0.01f) return;
        DrawRect(R(0, 0, CityMap.Width, CityMap.Height).Grow(_t * 0.25f), NightTint with { A = 0.55f * night });

        foreach (var l in LampPosts()) Glow(P(l.X, l.Y), _t * 0.5f, Lamplight, night * 0.9f);

        // Open shops spill light onto the pavement; speakeasies stay lively all night.
        foreach (var lot in World!.Map.Lots)
        {
            if (lot.Use is not (LotUse.Business or LotUse.Headquarters or LotUse.Precinct)) continue;
            bool frontTop = lot.FrontY < lot.Y;
            var door = P(lot.X + 0.5f, frontTop ? lot.Y - 0.05f : lot.Y + 1.05f);
            float strength = night * 0.6f;
            var colour = Lamplight;
            if (lot.Use == LotUse.Business)
            {
                var b = World.BusinessById(lot.BusinessId);
                var racket = Live && _shown.TryGetValue(b.Id, out var s) ? s.Racket : b.Racket;
                if (!b.IsOpen && !Live) continue;
                if (racket is RacketKind.Speakeasy) { strength = night; colour = new Color("ffb070"); }
                else if (HourOfDay is > 22 or < 6 && Hash(lot.Id, 13) < 0.6f) continue;
            }
            else if (lot.Use == LotUse.Headquarters) colour = Palette.Gang(World, lot.GangId).Lightened(0.3f);
            else colour = Palette.Police.Lightened(0.3f);
            Glow(door, _t * 0.45f, colour, strength);
        }
        foreach (var (at, strength) in _headlights) Glow(at, _t * 0.2f, new Color("fff2c0"), strength * 0.8f);
    }
}
