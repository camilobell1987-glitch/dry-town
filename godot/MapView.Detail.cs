using System;
using DryTown.Core;
using Godot;

/// <summary>
/// The fine detail that makes a building read as brick and stone rather than a painted box: courses
/// of brick, soot rising from the street, string courses between floors, a cornice with its dentils,
/// framed sash windows with sills and lintels, curtains and window boxes, iron fire escapes, and a roof
/// with a coping round its parapet, tar seams, skylights and chimneys. Only drawn close up.
/// </summary>
public partial class MapView
{
    private static readonly Color Iron = new("1e1c1a");
    private static readonly Color Sash = new("d6cdb6");
    private static readonly Color Dressing = new("bdb39c");
    private static readonly Color Soot = new(0.06f, 0.05f, 0.04f, 0.42f);
    private static readonly Color Clear = new(0, 0, 0, 0);
    private static readonly Color[] Curtains = { new("8a3a34"), new("d8cca6"), new("4f5f7e"), new("6a7a4a"), new("b88a4a") };
    private static readonly Color[] Blooms = { new("c8463c"), new("e8c84a"), new("f0ece0"), new("d86a9a") };

    /// <summary>A hairline at this many tiles wide.</summary>
    private float Hair(float tiles) => _t * tiles;

    /// <summary>
    /// Brick courses up the front and west walls, a few bricks a shade off, stone quoins on the corners of a
    /// stone building, a string course at every floor, and soot darkening the walls toward the street.
    /// </summary>
    private void DressWall(Lot lot, float x0, float y0, float x1, float y1, float zFrom, float top, Color wall, bool stone)
    {
        if (!Fine || top <= 0.05f) return;
        float course = stone ? 0.07f : 0.035f;
        var joint = wall.Darkened(stone ? 0.12f : 0.25f) with { A = 0.6f };
        var westJoint = joint with { A = 0.4f };
        for (float z = course; z < top - 0.02f; z += course)
        {
            C.DrawLine(Q(x0, y1, z), Q(x1, y1, z), joint, Hair(0.005f));
            C.DrawLine(Q(x0, y0, z), Q(x0, y1, z), westJoint, Hair(0.005f));
        }

        // A brick or a block here and there a shade off its neighbours.
        int patches = (int)((x1 - x0) * top * 30);
        for (int i = 0; i < patches; i++)
        {
            float px = x0 + Hash(lot.Id * 31 + i, 600) * (x1 - x0 - 0.06f);
            float pz = Mathf.Floor(Hash(lot.Id * 31 + i, 601) * (top - course) / course) * course;
            var tint = Hash(lot.Id * 31 + i, 602) < 0.5f ? wall.Lightened(0.1f) : wall.Darkened(0.14f);
            Face(px, px + (stone ? 0.09f : 0.055f), y1, pz + Hair(0.0002f), pz + course, tint with { A = 0.55f });
        }

        if (stone)
            for (int k = 0; k * 0.07f + 0.065f < top; k++)
            {
                float z = k * 0.07f, w = k % 2 == 0 ? 0.06f : 0.035f;
                Face(x0, x0 + w, y1, z + 0.004f, z + 0.066f, wall.Lightened(0.14f));
                Face(x1 - w, x1, y1, z + 0.004f, z + 0.066f, wall.Lightened(0.1f));
            }

        // A stone band where each floor meets the next.
        var band = wall.Lerp(Dressing, 0.55f);
        for (float z = zFrom + 0.19f; z < top - 0.12f; z += 0.2f)
        {
            Face(x0, x1, y1, z, z + 0.012f, band);
            Face(x0, x1, y1, z - 0.006f, z, new Color(0, 0, 0, 0.18f));
        }

        // Soot from the street, fading up the walls.
        float rise = Mathf.Min(top, 0.35f);
        C.DrawShadedQuad(Q(x0, y1, 0), Q(x1, y1, 0), Q(x1, y1, rise), Q(x0, y1, rise), Soot, Soot, Clear, Clear);
        C.DrawShadedQuad(Q(x0, y1, 0), Q(x0, y0, 0), Q(x0, y0, rise), Q(x0, y1, rise), Soot, Soot, Clear, Clear);
        // And a little shade under the eaves.
        C.DrawShadedQuad(Q(x0, y1, top - 0.08f), Q(x1, y1, top - 0.08f), Q(x1, y1, top), Q(x0, y1, top), Clear, Clear, Soot with { A = 0.25f }, Soot with { A = 0.25f });
    }

    /// <summary>A cornice across the top of the front wall, standing a little proud of it, with dentils under its lip.</summary>
    private void DrawCornice(float x0, float y1, float x1, float top, Color wall)
    {
        if (!Fine) return;
        var stone = wall.Lerp(Dressing, 0.7f);
        Face(x0, x1, y1, top - 0.075f, top - 0.05f, new Color(0, 0, 0, 0.28f));
        Box(x0 - 0.012f, y1 - 0.03f, x1 + 0.012f, y1 + 0.022f, top - 0.05f, top, stone, stone.Darkened(0.12f), stone.Lightened(0.12f));
        C.DrawLine(Q(x0 - 0.012f, y1 + 0.022f, top - 0.02f), Q(x1 + 0.012f, y1 + 0.022f, top - 0.02f), stone.Darkened(0.2f), Hair(0.006f));
        for (float x = x0 + 0.012f; x < x1 - 0.015f; x += 0.034f)
            C.DrawColoredPolygon(new[] { Q(x, y1 + 0.02f, top - 0.065f), Q(x + 0.016f, y1 + 0.02f, top - 0.065f), Q(x + 0.016f, y1 + 0.02f, top - 0.05f), Q(x, y1 + 0.02f, top - 0.05f) }, stone.Darkened(0.08f));
    }

    /// <summary>
    /// One sash window on a south wall: a stone lintel and sill, a painted frame, two panes with the sky in
    /// the upper one, and now and then curtains or a window box. Some are lit after dark.
    /// </summary>
    private void DrawSashWindow(Lot lot, float wx, float ww, float y, float z, int floor, int i)
    {
        float r = Hash(lot.Id * 13 + floor, i + 700);
        Face(wx - 0.016f, wx + ww + 0.016f, y, z + 0.145f, z + 0.163f, Dressing);
        Face(wx - 0.012f, wx + ww + 0.012f, y, z + 0.034f, z + 0.048f, Dressing);
        Face(wx - 0.01f, wx + ww + 0.01f, y, z + 0.024f, z + 0.034f, new Color(0, 0, 0, 0.3f));
        Face(wx - 0.006f, wx + ww + 0.006f, y, z + 0.048f, z + 0.145f, Sash);
        float ga = wx + 0.005f, gb = wx + ww - 0.005f;
        Face(ga, gb, y, z + 0.099f, z + 0.139f, Glass);
        Face(ga, gb, y, z + 0.054f, z + 0.093f, Glass.Darkened(0.18f));
        C.DrawColoredPolygon(new[] { Q(ga, y, z + 0.101f), Q(ga + ww * 0.42f, y, z + 0.137f), Q(ga + ww * 0.62f, y, z + 0.137f), Q(ga + ww * 0.18f, y, z + 0.101f) },
            Glass.Lightened(0.4f) with { A = 0.45f });
        Color? curtain = r < 0.4f ? Curtains[(int)(Hash(lot.Id, floor * 17 + i) * Curtains.Length)] : null;
        if (curtain is Color cc)
        {
            Face(ga, ga + ww * 0.28f, y, z + 0.054f, z + 0.139f, cc);
            Face(gb - ww * 0.28f, gb, y, z + 0.054f, z + 0.139f, cc.Darkened(0.12f));
        }
        if (Hash(lot.Id * 13 + floor, i + 500) < 0.42f)
            Lit(() =>
            {
                Face(ga, gb, y, z + 0.099f, z + 0.139f, WindowLit);
                Face(ga, gb, y, z + 0.054f, z + 0.093f, WindowLit.Darkened(0.12f));
                if (curtain is Color lc)
                {
                    Face(ga, ga + ww * 0.28f, y, z + 0.054f, z + 0.139f, lc.Lerp(WindowLit, 0.45f));
                    Face(gb - ww * 0.28f, gb, y, z + 0.054f, z + 0.139f, lc.Lerp(WindowLit, 0.35f));
                }
            });
        if (r > 0.86f)
        {
            Face(wx - 0.01f, wx + ww + 0.01f, y + 0.012f, z + 0.03f, z + 0.052f, new Color("5a3a24"));
            for (int k = 0; k < 4; k++)
            {
                var at = Q(wx + ww * (0.08f + 0.28f * k), y + 0.012f, z + 0.058f);
                C.DrawCircle(at, _t * 0.011f, new Color("4a6a32"));
                C.DrawCircle(at + new Vector2(_t * 0.004f, -_t * 0.006f), _t * 0.008f, Blooms[(int)(Hash(lot.Id + k, floor * 5 + i) * Blooms.Length)]);
            }
        }
    }

    /// <summary>An iron fire escape down the front: a railed landing at every floor and a stair zig-zagging between them.</summary>
    private void DrawFireEscape(float fx, float y, float zFrom, float zTo)
    {
        if (!Fine) return;
        const float w = 0.32f, d = 0.06f;
        int f = 0;
        for (float z = zFrom + 0.03f; z + 0.1f < zTo; z += 0.2f, f++)
        {
            C.DrawColoredPolygon(new[] { Q(fx, y, z), Q(fx + w, y, z), Q(fx + w, y + d, z), Q(fx, y + d, z) }, Iron with { A = 0.8f });
            C.DrawLine(Q(fx, y + d, z), Q(fx + w, y + d, z), Iron, Hair(0.012f));
            C.DrawLine(Q(fx, y + d, z + 0.07f), Q(fx + w, y + d, z + 0.07f), Iron, Hair(0.01f));
            C.DrawLine(Q(fx, y + d, z + 0.035f), Q(fx + w, y + d, z + 0.035f), Iron, Hair(0.005f));
            C.DrawLine(Q(fx, y, z + 0.07f), Q(fx, y + d, z + 0.07f), Iron, Hair(0.007f));
            C.DrawLine(Q(fx + w, y, z + 0.07f), Q(fx + w, y + d, z + 0.07f), Iron, Hair(0.007f));
            for (float x = fx; x <= fx + w + 0.001f; x += 0.04f)
                C.DrawLine(Q(x, y + d, z), Q(x, y + d, z + 0.07f), Iron, Hair(0.004f));
            if (z - 0.2f > 0.06f)
            {
                bool left = f % 2 == 0;
                float a = left ? fx + 0.03f : fx + w - 0.03f, b = left ? fx + w - 0.1f : fx + 0.1f;
                C.DrawLine(Q(a, y + d * 0.5f, z), Q(b, y + d * 0.5f, z - 0.2f), Iron, Hair(0.014f));
                for (int k = 1; k < 7; k++)
                {
                    var p = Q(a + (b - a) * k / 7f, y + d * 0.5f, z - 0.2f * k / 7f);
                    C.DrawLine(p - new Vector2(_t * 0.012f, 0), p + new Vector2(_t * 0.012f, 0), Iron, Hair(0.005f));
                }
            }
            else
            {
                // The drop ladder, hauled up out of reach of the street.
                C.DrawLine(Q(fx + 0.05f, y + d, z), Q(fx + 0.05f, y + d, z - 0.11f), Iron, Hair(0.006f));
                C.DrawLine(Q(fx + 0.1f, y + d, z), Q(fx + 0.1f, y + d, z - 0.11f), Iron, Hair(0.006f));
                for (float zz = z - 0.1f; zz < z; zz += 0.025f)
                    C.DrawLine(Q(fx + 0.05f, y + d, zz), Q(fx + 0.1f, y + d, zz), Iron, Hair(0.004f));
            }
        }
    }

    /// <summary>A roof: shade inside the parapet, tar seams and patches, a coping round the edge and a skylight on some.</summary>
    private void DressRoof(Lot lot, float x0, float y0, float x1, float y1, float h, Color roof)
    {
        if (!Fine) return;
        var ao = new Color(0, 0, 0, 0.26f);
        const float e = 0.06f;
        C.DrawShadedQuad(Q(x0, y0, h), Q(x1, y0, h), Q(x1, y0 + e, h), Q(x0, y0 + e, h), ao, ao, Clear, Clear);
        C.DrawShadedQuad(Q(x0, y0, h), Q(x0 + e, y0, h), Q(x0 + e, y1, h), Q(x0, y1, h), ao, Clear, Clear, ao);
        for (int i = 1; i < 4; i++)
        {
            float yy = y0 + (y1 - y0) * i / 4f;
            C.DrawLine(Q(x0 + 0.03f, yy, h), Q(x1 - 0.03f, yy, h), roof.Darkened(0.18f) with { A = 0.7f }, Hair(0.005f));
        }
        for (int i = 0; i < 3; i++)
        {
            float px = x0 + 0.08f + Hash(lot.Id, 620 + i) * (x1 - x0 - 0.3f), py = y0 + 0.08f + Hash(lot.Id, 630 + i) * (y1 - y0 - 0.25f);
            float pw = 0.08f + Hash(lot.Id, 640 + i) * 0.12f, ph = 0.05f + Hash(lot.Id, 650 + i) * 0.08f;
            C.DrawColoredPolygon(new[] { Q(px, py, h), Q(px + pw, py, h), Q(px + pw, py + ph, h), Q(px, py + ph, h) },
                (i % 2 == 0 ? roof.Lightened(0.05f) : roof.Darkened(0.06f)) with { A = 0.45f });
        }
        var coping = roof.Lightened(0.32f);
        C.DrawLine(Q(x0, y0, h), Q(x1, y0, h), coping, Hair(0.014f));
        C.DrawLine(Q(x0, y0, h), Q(x0, y1, h), coping, Hair(0.014f));
        C.DrawLine(Q(x1, y0, h), Q(x1, y1, h), coping.Darkened(0.15f), Hair(0.014f));
        C.DrawLine(Q(x0, y1, h), Q(x1, y1, h), coping.Lightened(0.08f), Hair(0.016f));
        if (Hash(lot.Id, 610) < 0.45f)
        {
            float sx = x0 + 0.35f + Hash(lot.Id, 611) * (x1 - x0 - 0.6f), sy = y0 + 0.4f;
            var frame = new Color("5a5650");
            Box(sx, sy, sx + 0.16f, sy + 0.12f, h, h + 0.03f, frame, frame.Darkened(0.15f), Glass.Lightened(0.28f));
            C.DrawLine(Q(sx + 0.08f, sy, h + 0.03f), Q(sx + 0.08f, sy + 0.12f, h + 0.03f), frame, Hair(0.008f));
            C.DrawLine(Q(sx, sy + 0.06f, h + 0.03f), Q(sx + 0.16f, sy + 0.06f, h + 0.03f), frame, Hair(0.006f));
        }
    }

    /// <summary>A brick chimney with a stone cap and a pot or two.</summary>
    private void DrawChimney(float cx, float cy, float h, Color brick)
    {
        Box(cx, cy, cx + 0.07f, cy + 0.06f, h, h + 0.13f, brick, brick.Darkened(0.15f), brick.Darkened(0.35f));
        C.DrawLine(Q(cx, cy + 0.06f, h + 0.05f), Q(cx + 0.07f, cy + 0.06f, h + 0.05f), brick.Darkened(0.3f), Hair(0.004f));
        C.DrawLine(Q(cx, cy + 0.06f, h + 0.09f), Q(cx + 0.07f, cy + 0.06f, h + 0.09f), brick.Darkened(0.3f), Hair(0.004f));
        Box(cx - 0.008f, cy - 0.006f, cx + 0.078f, cy + 0.066f, h + 0.13f, h + 0.145f, Dressing, Dressing.Darkened(0.15f), Dressing.Darkened(0.05f));
        var pot = new Color("8a5a3a");
        Box(cx + 0.015f, cy + 0.02f, cx + 0.035f, cy + 0.04f, h + 0.145f, h + 0.18f, pot, pot.Darkened(0.2f), new Color("1a1614"));
        Box(cx + 0.042f, cy + 0.02f, cx + 0.06f, cy + 0.04f, h + 0.145f, h + 0.17f, pot, pot.Darkened(0.2f), new Color("1a1614"));
    }

    /// <summary>The bulkhead over the stairs up to the roof, with its door.</summary>
    private void DrawBulkhead(float bx, float by, float h, Color roof)
    {
        var wall = roof.Lightened(0.22f);
        Box(bx, by, bx + 0.16f, by + 0.12f, h, h + 0.12f, wall, wall.Darkened(0.15f), roof.Darkened(0.2f));
        Face(bx + 0.05f, bx + 0.1f, by + 0.12f, h, h + 0.09f, new Color("3a2c22"));
        C.DrawLine(Q(bx - 0.01f, by + 0.12f, h + 0.12f), Q(bx + 0.17f, by + 0.12f, h + 0.12f), roof.Lightened(0.35f), Hair(0.01f));
    }
    private static readonly Color[] SignBoards = { new("2c3a2e"), new("3a2422"), new("22283a"), new("3a3022"), new("1e1e22") };
    private static readonly Color Gilt = new("d8b860");
    private static readonly Color[] WindowGoods = { new("c8463c"), new("e0c070"), new("7a9a52"), new("e8e2d4"), new("6a86a8"), new("a8703c") };

    /// <summary>
    /// A shopfront's trimmings: a painted sign board with gilt lettering over the awning, a mullion and a
    /// kick plate on the plate glass with goods set out behind it, and a glazed door with a brass knob.
    /// </summary>
    private void DressShopfront(Lot lot, float x0, float x1, float y1)
    {
        var board = SignBoards[(int)(Hash(lot.Id, 670) * SignBoards.Length)];
        Face(x0 + 0.02f, x1 - 0.02f, y1, 0.172f, 0.2f, board);
        C.DrawLine(Q(x0 + 0.03f, y1, 0.176f), Q(x1 - 0.03f, y1, 0.176f), Gilt with { A = 0.7f }, Hair(0.003f));
        C.DrawLine(Q(x0 + 0.03f, y1, 0.196f), Q(x1 - 0.03f, y1, 0.196f), Gilt with { A = 0.7f }, Hair(0.003f));
        // Gilt letters, suggested rather than spelled.
        float lx = x0 + 0.08f + Hash(lot.Id, 671) * 0.06f;
        for (int k = 0; lx < x1 - 0.1f; k++)
        {
            float lw = 0.012f + Hash(lot.Id * 7 + k, 672) * 0.016f;
            if (Hash(lot.Id * 7 + k, 673) < 0.15f) { lx += 0.025f; continue; }
            Face(lx, lx + lw, y1, 0.18f, 0.192f, Gilt);
            lx += lw + 0.007f;
        }
        float gx0 = x0 + 0.08f, gx1 = x1 - 0.3f;
        Face(gx0, gx1, y1, 0, 0.02f, board.Darkened(0.3f));
        for (int k = 0; k < (int)((gx1 - gx0) / 0.04f); k++)
        {
            float px = gx0 + 0.01f + k * 0.04f;
            Face(px, px + 0.026f, y1, 0.024f, 0.024f + 0.02f + Hash(lot.Id * 3 + k, 674) * 0.025f, WindowGoods[(int)(Hash(lot.Id * 3 + k, 675) * WindowGoods.Length)]);
        }
        C.DrawLine(Q((gx0 + gx1) / 2, y1, 0.02f), Q((gx0 + gx1) / 2, y1, 0.13f), Sash.Darkened(0.4f), Hair(0.006f));
        C.DrawColoredPolygon(new[] { Q(gx0 + 0.01f, y1, 0.03f), Q(gx0 + 0.09f, y1, 0.125f), Q(gx0 + 0.13f, y1, 0.125f), Q(gx0 + 0.05f, y1, 0.03f) }, Glass.Lightened(0.45f) with { A = 0.35f });
        Face(x1 - 0.22f, x1 - 0.12f, y1, 0.07f, 0.125f, Glass);
        C.DrawCircle(Q(x1 - 0.135f, y1, 0.065f), _t * 0.006f, Gilt);
        Face(x1 - 0.25f, x1 - 0.09f, y1, 0.14f, 0.152f, Dressing);
    }
    private static readonly Color LampIron = new("23272a");
    private static readonly Color HydrantRed = new("a8332c");
    private static readonly Color MailboxGreen = new("3c5a3a");

    /// <summary>A cast-iron lamp post: a fluted base, a tapering column, and a lantern whose glass lights at dusk.</summary>
    private void DrawLantern(Vector2 l)
    {
        Ellipse(P(l.X + 0.03f, l.Y + 0.01f), new Vector2(0.05f, 0.02f) * _t, Shadow);
        Box(l.X - 0.02f, l.Y - 0.015f, l.X + 0.02f, l.Y + 0.015f, 0, 0.05f, LampIron, LampIron.Lightened(0.1f), LampIron.Lightened(0.15f));
        C.DrawColoredPolygon(new[] { Q(l.X - 0.012f, l.Y, 0.05f), Q(l.X + 0.012f, l.Y, 0.05f), Q(l.X + 0.007f, l.Y, 0.4f), Q(l.X - 0.007f, l.Y, 0.4f) }, LampIron);
        C.DrawLine(Q(l.X - 0.004f, l.Y, 0.06f), Q(l.X - 0.003f, l.Y, 0.39f), LampIron.Lightened(0.25f), Hair(0.004f));
        C.DrawColoredPolygon(new[] { Q(l.X - 0.02f, l.Y, 0.39f), Q(l.X + 0.02f, l.Y, 0.39f), Q(l.X + 0.014f, l.Y, 0.405f), Q(l.X - 0.014f, l.Y, 0.405f) }, LampIron);
        // The lantern: glass widening upward under a peaked cap with a finial.
        var glass = new[] { Q(l.X - 0.02f, l.Y, 0.405f), Q(l.X + 0.02f, l.Y, 0.405f), Q(l.X + 0.032f, l.Y, 0.47f), Q(l.X - 0.032f, l.Y, 0.47f) };
        C.DrawColoredPolygon(glass, new Color("c8c2ae"));
        C.DrawLine(Q(l.X, l.Y, 0.405f), Q(l.X, l.Y, 0.47f), LampIron, Hair(0.004f));
        Lit(() => C.DrawColoredPolygon(glass, Lamplight));
        C.DrawColoredPolygon(new[] { Q(l.X - 0.04f, l.Y, 0.47f), Q(l.X + 0.04f, l.Y, 0.47f), Q(l.X, l.Y, 0.51f) }, LampIron);
        C.DrawCircle(Q(l.X, l.Y, 0.52f), _t * 0.007f, LampIron);
    }

    /// <summary>
    /// What stands on and around a block's pavement: a fire hydrant and a mailbox at the corners, an ash can
    /// at the kerb, a joint down the paving, drain grates at the kerb and a manhole out in the street.
    /// </summary>
    private void DressStreet(int bx, int by)
    {
        if (bx >= Map.BlocksX || by >= Map.BlocksY || by == Map.RiverRow) return;
        float bx0 = bx * CityMap.StrideX + 1, by0 = by * CityMap.StrideY + 1;
        float bx1 = bx0 + CityMap.LotsPerBlockX, by1 = by0 + CityMap.LotsPerBlockY;
        var joint = Kerb with { A = 0.3f };
        C.DrawLine(P(bx0 - 0.2f, by0 - 0.1f), P(bx1 + 0.2f, by0 - 0.1f), joint, Hair(0.006f));
        C.DrawLine(P(bx0 - 0.2f, by1 + 0.1f), P(bx1 + 0.2f, by1 + 0.1f), joint, Hair(0.006f));

        // Drain grates in the gutter at each corner.
        foreach (var (gx, gy) in new[] { (bx0 + 0.1f, by0 - 0.24f), (bx1 - 0.25f, by1 + 0.21f) })
        {
            C.DrawRect(R(gx, gy, 0.14f, 0.05f), new Color("1a1918"));
            for (int k = 1; k < 5; k++) C.DrawLine(P(gx + k * 0.028f, gy), P(gx + k * 0.028f, gy + 0.05f), new Color("4a4844"), Hair(0.006f));
        }

        // A manhole out in the street north of the block.
        float mx = bx0 + CityMap.LotsPerBlockX * (0.3f + Hash(bx, by + 900) * 0.4f), my = by0 - 0.7f;
        Ellipse(P(mx, my), new Vector2(0.08f, 0.055f) * _t, new Color("26252a"));
        Ellipse(P(mx, my), new Vector2(0.065f, 0.044f) * _t, new Color("38363a"));
        C.DrawLine(P(mx - 0.05f, my), P(mx + 0.05f, my), new Color("26252a"), Hair(0.006f));

        // The fire hydrant on the north-west corner.
        float hx = bx0 - 0.1f, hy = by0 - 0.12f;
        Ellipse(P(hx + 0.03f, hy + 0.01f), new Vector2(0.035f, 0.015f) * _t, Shadow);
        Box(hx - 0.016f, hy - 0.012f, hx + 0.016f, hy + 0.012f, 0, 0.065f, HydrantRed, HydrantRed.Lightened(0.12f), HydrantRed.Lightened(0.2f));
        Box(hx - 0.03f, hy - 0.006f, hx + 0.03f, hy + 0.006f, 0.035f, 0.05f, HydrantRed.Darkened(0.1f), HydrantRed, HydrantRed);
        C.DrawCircle(Q(hx, hy, 0.075f), _t * 0.018f, HydrantRed.Lightened(0.15f));

        // A mailbox on the south-east corner, and an ash can at the kerb.
        float px = bx1 + 0.02f, py = by1 + 0.12f;
        Ellipse(P(px + 0.04f, py + 0.01f), new Vector2(0.045f, 0.018f) * _t, Shadow);
        Box(px - 0.025f, py - 0.02f, px + 0.025f, py + 0.02f, 0, 0.03f, LampIron, LampIron, LampIron);
        Box(px - 0.03f, py - 0.025f, px + 0.03f, py + 0.025f, 0.03f, 0.1f, MailboxGreen, MailboxGreen.Lightened(0.1f), MailboxGreen.Lightened(0.15f));
        C.DrawCircle(Q(px, py, 0.105f), _t * 0.03f, MailboxGreen.Lightened(0.15f));
        Face(px - 0.018f, px + 0.018f, py + 0.025f, 0.075f, 0.085f, new Color("1a1a18"));
        float ax = bx0 + CityMap.LotsPerBlockX * 0.55f + Hash(bx, by + 901) * 0.6f, ay = by1 + 0.16f;
        var can = new Color("6a6660");
        Ellipse(P(ax + 0.03f, ay + 0.01f), new Vector2(0.04f, 0.015f) * _t, Shadow);
        Box(ax - 0.025f, ay - 0.02f, ax + 0.025f, ay + 0.02f, 0, 0.07f, can, can.Lightened(0.1f), can.Darkened(0.2f));
        C.DrawLine(Q(ax - 0.025f, ay + 0.02f, 0.025f), Q(ax + 0.025f, ay + 0.02f, 0.025f), can.Darkened(0.25f), Hair(0.005f));
        C.DrawLine(Q(ax - 0.025f, ay + 0.02f, 0.05f), Q(ax + 0.025f, ay + 0.02f, 0.05f), can.Darkened(0.25f), Hair(0.005f));
    }
}
