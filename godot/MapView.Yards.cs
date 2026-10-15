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

    /// <summary>A filling station: a little office at the back, a canopy on posts over two pumps, and the forecourt.</summary>
    private void DrawFillingStation(Lot lot, float h, bool open)
    {
        bool south = FacesSouth(lot);
        float x0 = lot.X + 0.07f, x1 = lot.X + 0.93f;
        // The office stands back from the street, the forecourt in front of it.
        float oy0 = south ? lot.Y + 0.1f : lot.Y + 0.55f, oy1 = south ? lot.Y + 0.42f : lot.Y + 0.9f;
        float fy0 = south ? oy1 : lot.Y + 0.05f, fy1 = south ? lot.Y + 0.95f : oy0;
        C.DrawColoredPolygon(new[] { Q(x0, fy0, 0), Q(x1, fy0, 0), Q(x1, fy1, 0), Q(x0, fy1, 0) }, new Color("7c776c"));
        var wall = open ? new Color("e6dcc2") : new Color("e6dcc2").Darkened(0.3f);
        var trim = new Color("2f6a3a");
        Box(x0 + 0.08f, oy0, x0 + 0.5f, oy1, 0, h, wall, wall.Darkened(0.08f), new Color("8a3a2e"));
        if (south)
        {
            Face(x0 + 0.13f, x0 + 0.27f, oy1, 0.02f, h * 0.8f, Glass.Lightened(0.1f));
            Face(x0 + 0.32f, x0 + 0.44f, oy1, 0.02f, h * 0.7f, new Color("4a3a2a"));
            Face(x0 + 0.08f, x0 + 0.5f, oy1, h - 0.04f, h, trim);
            Lit(() => Face(x0 + 0.13f, x0 + 0.27f, oy1, 0.02f, h * 0.8f, WindowLit with { A = 0.6f }));
        }

        // The canopy on four posts, and the pumps under it.
        float cx0 = x0 + 0.2f, cx1 = x1 - 0.06f, cy0 = fy0 + 0.08f, cy1 = fy1 - 0.12f, top = 0.3f;
        var post = new Color("d8d0bc");
        float lw = Mathf.Max(1, _t * 0.02f);
        foreach (float px in new[] { cx0 + 0.04f, cx1 - 0.04f })
            C.DrawLine(Q(px, cy1, 0), Q(px, cy1, top), post, lw);
        for (int i = 0; i < 2; i++)
        {
            float px = cx0 + (cx1 - cx0) * (0.3f + 0.4f * i), py = (cy0 + cy1) / 2;
            Box(px - 0.04f, py - 0.03f, px + 0.04f, py + 0.03f, 0, 0.17f, new Color("c8463c"), new Color("a83a30"), new Color("d85a4c"));
            C.DrawCircle(Q(px, py + 0.03f, 0.2f), Mathf.Max(1.5f, _t * 0.03f), open ? new Color("f4ecd8") : new Color("8a8478"));
            Lit(() => { if (open) C.DrawCircle(Q(px, py + 0.03f, 0.2f), Mathf.Max(2, _t * 0.05f), WindowLit with { A = 0.5f }); });
        }
        C.DrawColoredPolygon(new[] { Q(cx0, cy0, top), Q(cx1, cy0, top), Q(cx1, cy1, top), Q(cx0, cy1, top) }, open ? trim : trim.Darkened(0.3f));
        Face(cx0, cx1, cy1, top - 0.04f, top, new Color("e8d8a8"));
        // A customer filling up, now and then.
        if (open && Hash(lot.Id, 1700) < 0.6f)
            DrawVehicle(new Vector2((cx0 + cx1) / 2, south ? fy1 - 0.08f : fy0 + 0.08f), true, 1, Ride.ModelT, CarPaint[lot.Id % CarPaint.Length], 0, 0.6f);
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
