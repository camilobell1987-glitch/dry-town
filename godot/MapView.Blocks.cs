using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// What makes one block different from the next: alleys cut between the buildings, tenements built
/// round a court with washing strung across it, graveyards behind iron railings, and churches.
/// </summary>
public partial class MapView
{
    private static readonly Color Cobbles = new("3c3834");
    private static readonly Color Gravel = new("8a8070");
    private static readonly Color Headstone = new("9a958a");
    private static readonly Color[] Washing = { new("ece6d6"), new("f4f0e4"), new("9ab0c8"), new("c89a9a"), new("d8c88a"), new("8a9a7a") };

    private static readonly string[] Saints =
    {
        "Patrick", "Brigid", "Anthony", "Stanislaus", "Mary", "Joseph", "Michael", "Columba", "Rocco", "Casimir",
        "Paul", "Peter", "Agnes", "Lucy", "Bartholomew", "Malachy", "Hedwig", "Lucian",
    };

    private static readonly string[] Cemeteries =
    {
        "Calvary", "Holy Cross", "Mount Olivet", "Evergreens", "Woodlawn", "Mount Hope", "Oak Hill", "St. Adalbert's",
    };

    /// <summary>For each block, the gap between lot columns an alley runs north to south through, or 0 for none.</summary>
    private int[,] _alley = new int[0, 0];

    /// <summary>Blocks whose buildings stand back from an alley along the middle, east to west.</summary>
    private bool[,] _backAlley = new bool[0, 0];

    /// <summary>Blocks of tenements built round a court.</summary>
    private bool[,] _courtBlock = new bool[0, 0];

    private void IndexBlocks()
    {
        _alley = new int[Map.BlocksX, Map.BlocksY];
        _backAlley = new bool[Map.BlocksX, Map.BlocksY];
        _courtBlock = new bool[Map.BlocksX, Map.BlocksY];
        foreach (var lot in Map.Lots)
            if (lot.Court) _courtBlock[(lot.X - 1) / CityMap.StrideX, (lot.Y - 1) / CityMap.StrideY] = true;
        for (int by = 0; by < Map.BlocksY; by++)
        for (int bx = 0; bx < Map.BlocksX; bx++)
        {
            _alley[bx, by] = Map.AlleyOf(bx, by);
            _backAlley[bx, by] = Map.HasBackAlley(bx, by);
        }
        IndexGarages();
    }

    private static (int Bx, int By, int Lx, int Row) Place(Lot lot) =>
        ((lot.X - 1) / CityMap.StrideX, (lot.Y - 1) / CityMap.StrideY, (lot.X - 1) % CityMap.StrideX, (lot.Y - 1) % CityMap.StrideY);

    private int AlleyOf(int bx, int by) => bx < _alley.GetLength(0) && by < _alley.GetLength(1) ? _alley[bx, by] : 0;

    /// <summary>
    /// A building's footprint on its lot, in tiles: west, north, east, south. Buildings round a court stand
    /// back from the yard in the middle of the block, and those along an alley stand back from it.
    /// </summary>
    private (float X0, float Y0, float X1, float Y1) Footprint(Lot lot)
    {
        float x0 = lot.X + 0.07f, y0 = lot.Y + 0.1f, x1 = lot.X + 0.93f, y1 = lot.Y + 0.9f;
        var (bx, by, lx, row) = Place(lot);
        (x0, x1) = BesideRamp(lot, x0, x1);
        if (lot.Court)
        {
            if (row == 0) y1 = lot.Y + 0.55f;
            else y0 = lot.Y + 0.45f;
            return (x0, y0, x1, y1);
        }
        int a = AlleyOf(bx, by);
        if (a == 0) return (x0, y0, x1, y1);
        if (lx == a - 1) x1 = Mathf.Min(x1, lot.X + 0.8f);
        if (lx == a) x0 = Mathf.Max(x0, lot.X + 0.2f);
        // Along a back alley the buildings stand back to make room for the lockups and the lane in front of them.
        if (_backAlley[bx, by])
        {
            if (row == 0) y1 = lot.Y + CityMap.LockupFront;
            else y0 = lot.Y + CityMap.LaneEdge - 1;
        }
        return (x0, y0, x1, y1);
    }

    // ---- Ground -----------------------------------------------------------------

    /// <summary>Cobbles down the alleys and across the courts, with a drain and a puddle or two.</summary>
    private void DrawBackGround(int bx, int by)
    {
        float x0 = bx * CityMap.StrideX + 1, y0 = by * CityMap.StrideY + 1;
        int a = AlleyOf(bx, by);
        int k = bx * 131 + by;
        if (_courtBlock[bx, by])
        {
            var yard = R(x0 + 0.04f, y0 + 0.58f, CityMap.LotsPerBlockX - 0.08f, 0.84f);
            C.DrawRect(yard, Cobbles);
            if (Fine)
            {
                // Flagstones, a drain in the middle and the yard pump.
                for (float x = x0 + 0.2f; x < x0 + CityMap.LotsPerBlockX; x += 0.2f)
                    C.DrawLine(P(x, y0 + 0.58f), P(x, y0 + 1.42f), Cobbles.Darkened(0.3f) with { A = 0.5f }, 1);
                for (float y = y0 + 0.75f; y < y0 + 1.42f; y += 0.17f)
                    C.DrawLine(P(x0 + 0.04f, y), P(x0 + CityMap.LotsPerBlockX - 0.04f, y), Cobbles.Darkened(0.3f) with { A = 0.5f }, 1);
                Ellipse(P(x0 + 1.5f + Hash(k, 910), y0 + 1.1f), new Vector2(0.28f, 0.08f) * _t, Water with { A = 0.5f });
                C.DrawRect(R(x0 + 2.0f - 0.05f, y0 + 0.97f, 0.1f, 0.06f), Iron);
            }
            // The passage through to the street.
            if (Map.CourtGate(bx, by) is { } gate)
                C.DrawRect(R(gate.X + 0.36f, y0 + 1.3f, 0.28f, 0.72f), Cobbles.Darkened(0.15f));
            return;
        }
        if (a == 0) return;
        var lane = Cobbles.Lightened(0.04f);
        float ax = x0 + a - 0.2f;
        C.DrawRect(R(ax, y0 - 0.02f, 0.4f, CityMap.LotsPerBlockY + 0.04f), lane);
        if (_backAlley[bx, by]) C.DrawRect(R(x0, y0 + CityMap.LockupFront, CityMap.LotsPerBlockX, CityMap.LaneEdge - CityMap.LockupFront), lane);
        if (!Fine) return;
        // A gutter down the middle, and a puddle.
        C.DrawLine(P(ax + 0.2f, y0 + 0.05f), P(ax + 0.2f, y0 + CityMap.LotsPerBlockY - 0.05f), lane.Darkened(0.35f), Hair(0.03f));
        Ellipse(P(ax + 0.2f, y0 + 0.4f + Hash(k, 911) * 1.2f), new Vector2(0.12f, 0.2f) * _t, Water with { A = 0.45f });
    }

    /// <summary>The grass of a graveyard, mown between the rows, with a gravel walk down the middle.</summary>
    private void DrawCemeteryGround(Lot lot)
    {
        var r = R(lot.X, lot.Y, 1, 1);
        C.DrawRect(r.Grow(_t * 0.01f), GrassDark.Lerp(Grass, 0.4f));
        C.DrawRect(R(lot.X, lot.Y + 0.44f, 1, 0.12f), Gravel);
        if (!Fine) return;
        for (int i = 0; i < 6; i++)
            Ellipse(P(lot.X + 0.1f + Hash(lot.Id, 920 + i) * 0.8f, lot.Y + 0.1f + Hash(lot.Id, 930 + i) * 0.8f), new Vector2(0.14f, 0.06f) * _t, Grass with { A = 0.5f });
    }

    // ---- Courts and alleys ----------------------------------------------------------

    /// <summary>
    /// The yard behind a lot on the south side of a court: the pump, ash cans and handcarts, and washing
    /// strung across from the windows opposite. Drawn before the building that hides the back of it.
    /// </summary>
    private void DrawCourtYard(Lot lot)
    {
        var (_, by, lx, row) = Place(lot);
        if (row != 1) return;
        float north = lot.Y - 0.45f, south = lot.Y + 0.45f;
        if (lx == 1 + (Place(lot).Bx + by) % 2 && Fine)
        {
            // The pump the whole block draws its water from.
            float px = lot.X + 0.5f, py = lot.Y - 0.02f;
            Box(px - 0.04f, py - 0.03f, px + 0.04f, py + 0.03f, 0, 0.14f, Iron.Lightened(0.15f), Iron.Lightened(0.25f), Iron);
            C.DrawLine(Q(px + 0.04f, py, 0.12f), Q(px + 0.12f, py, 0.09f), Iron, Hair(0.02f));
        }
        if (Fine)
        {
            for (int i = 0; i < 2; i++)
            {
                if (Hash(lot.Id, 940 + i) > 0.6f) continue;
                float cx = lot.X + 0.12f + Hash(lot.Id, 945 + i) * 0.7f, cy = lot.Y - 0.25f + Hash(lot.Id, 948 + i) * 0.4f;
                var can = new Color("5a5850");
                Box(cx - 0.035f, cy - 0.025f, cx + 0.035f, cy + 0.025f, 0, 0.08f, can, can.Lightened(0.1f), can.Darkened(0.25f));
            }
            if (Hash(lot.Id, 950) < 0.4f)
            {
                // A handcart tipped up against the wall.
                var wood = new Color("6b5038");
                float cx = lot.X + 0.3f + Hash(lot.Id, 951) * 0.4f;
                Box(cx - 0.1f, north + 0.04f, cx + 0.1f, north + 0.12f, 0.04f, 0.08f, wood, wood.Lightened(0.1f), wood.Lightened(0.05f));
                C.DrawCircle(Q(cx - 0.08f, north + 0.12f, 0.04f), _t * 0.03f, Iron);
                C.DrawCircle(Q(cx + 0.08f, north + 0.12f, 0.04f), _t * 0.03f, Iron);
            }
        }
        // Washing strung across the yard on lines between poles, a line or two to each house.
        int lines = Fine ? 2 : 1;
        for (int i = 0; i < lines; i++)
        {
            float y = north + 0.2f + i * 0.32f + Hash(lot.Id, 960 + i) * 0.08f, z = 0.3f + 0.04f * ((lot.Id + i) % 3);
            float xa = lot.X + 0.05f, xb = lot.X + 0.95f;
            var pole = new Color("4a3a2a");
            if (Fine)
            {
                C.DrawLine(Q(xa, y, 0), Q(xa, y, z + 0.02f), pole, Hair(0.012f));
                C.DrawLine(Q(xb, y, 0), Q(xb, y, z + 0.02f), pole, Hair(0.012f));
            }
            var from = Q(xa, y, z);
            var to = Q(xb, y, z);
            C.DrawLine(from, to, new Color("d8d0bc") with { A = 0.7f }, Hair(0.006f));
            for (int j = 1; j < (Fine ? 8 : 4); j++)
            {
                if (Hash(lot.Id * 7 + i, 970 + j) < 0.3f) continue;
                float t = j / (Fine ? 8f : 4f);
                var at = from.Lerp(to, t) + new Vector2(0, _t * 0.015f * Mathf.Sin(t * Mathf.Pi));
                var cloth = Washing[(int)(Hash(lot.Id * 7 + i, 980 + j) * Washing.Length)];
                float w = _t * (0.06f + Hash(lot.Id, 990 + j) * 0.04f), drop = _t * (0.07f + Hash(lot.Id, 995 + j) * 0.06f);
                C.DrawColoredPolygon(new[] { at, at + new Vector2(w, 0), at + new Vector2(w, drop), at + new Vector2(0, drop) }, cloth);
                if (Fine) C.DrawColoredPolygon(new[] { at + new Vector2(0, drop * 0.75f), at + new Vector2(w, drop * 0.75f), at + new Vector2(w, drop), at + new Vector2(0, drop) }, cloth.Darkened(0.12f));
            }
        }
    }

    /// <summary>The way into a court: an arched passage through the middle of the house on the street.</summary>
    private void DrawCourtPassage(Lot lot, float h, Color wall, bool open)
    {
        var (_, _, _, y1) = Footprint(lot);
        float xc = lot.X + 0.5f, half = 0.13f, spring = 0.15f;
        var arch = new List<Vector2> { Q(xc - half, y1, 0), Q(xc + half, y1, 0) };
        for (int i = 0; i <= 8; i++)
        {
            float a = Mathf.Pi * i / 8;
            arch.Add(Q(xc + half * Mathf.Cos(a), y1, spring + half * 0.7f * Mathf.Sin(a)));
        }
        // Stone voussoirs round the arch, the dark way through, and the yard light at the far end.
        var stone = wall.Lerp(Dressing, 0.6f);
        var ring = new List<Vector2> { Q(xc - half - 0.03f, y1, 0), Q(xc + half + 0.03f, y1, 0) };
        for (int i = 0; i <= 8; i++)
        {
            float a = Mathf.Pi * i / 8;
            ring.Add(Q(xc + (half + 0.03f) * Mathf.Cos(a), y1, spring + (half * 0.7f + 0.03f) * Mathf.Sin(a)));
        }
        C.DrawColoredPolygon(ring.ToArray(), stone);
        C.DrawColoredPolygon(arch.ToArray(), new Color("141210"));
        Face(xc - half * 0.6f, xc + half * 0.6f, y1, 0, spring * 0.8f, Cobbles.Lightened(0.08f) with { A = 0.35f });
        Face(xc - 0.02f, xc + 0.02f, y1, spring + half * 0.7f - 0.01f, spring + half * 0.7f + 0.04f, stone.Lightened(0.15f));
        if (open)
        {
            var lamp = Q(xc, y1, spring + half * 0.45f);
            C.DrawCircle(lamp, _t * 0.018f, new Color("6a5a3a"));
            Lit(() => { C.DrawCircle(lamp, _t * 0.02f, Lamplight); Glow(lamp, _t * 0.22f, Lamplight, 0.8f); });
        }
        // An enamel plate with the court's name, beside the way in.
        Face(xc + half + 0.06f, xc + half + 0.2f, y1, 0.17f, 0.24f, new Color("2a3a5a"));
        Face(xc + half + 0.07f, xc + half + 0.19f, y1, 0.18f, 0.23f, new Color("2a3a5a").Lightened(0.15f));
    }

    /// <summary>Ash cans, crates and a washing line along an alley, drawn before the building to the west of it.</summary>
    private void DrawAlleyClutter(Lot lot)
    {
        var (bx, by, lx, row) = Place(lot);
        int a = AlleyOf(bx, by);
        if (a == 0 || lx != a - 1 || !Fine) return;
        float ax = lot.X + 0.8f, aw = 0.4f;
        for (int i = 0; i < 3; i++)
        {
            if (Hash(lot.Id, 1000 + i) < 0.4f) continue;
            float cx = ax + 0.08f + Hash(lot.Id, 1005 + i) * (aw - 0.16f), cy = lot.Y + 0.15f + i * 0.28f;
            if (Hash(lot.Id, 1010 + i) < 0.5f)
            {
                var can = new Color("5a5850");
                Box(cx - 0.035f, cy - 0.025f, cx + 0.035f, cy + 0.025f, 0, 0.08f, can, can.Lightened(0.1f), can.Darkened(0.25f));
            }
            else
            {
                var crate = new Color("7a5e3e");
                Box(cx - 0.05f, cy - 0.04f, cx + 0.05f, cy + 0.04f, 0, 0.07f, crate, crate.Lightened(0.1f), crate.Lightened(0.06f));
                C.DrawLine(Q(cx - 0.05f, cy + 0.04f, 0), Q(cx + 0.05f, cy + 0.04f, 0.07f), crate.Darkened(0.3f), 1);
            }
        }
        // A line across the alley, from window to window.
        if (Hash(lot.Id, 1020) < 0.6f)
        {
            float y = lot.Y + 0.3f + Hash(lot.Id, 1021) * 0.4f, z = 0.35f + Hash(lot.Id, 1022) * 0.3f;
            var from = Q(ax, y, z);
            var to = Q(ax + aw, y, z + 0.03f);
            C.DrawLine(from, to, new Color("d8d0bc") with { A = 0.7f }, Hair(0.006f));
            for (int j = 1; j < 4; j++)
            {
                var at = from.Lerp(to, j / 4f);
                var cloth = Washing[(int)(Hash(lot.Id, 1030 + j) * Washing.Length)];
                float w = _t * 0.06f;
                C.DrawColoredPolygon(new[] { at, at + new Vector2(w, 0), at + new Vector2(w, w * 1.3f), at + new Vector2(0, w * 1.3f) }, cloth);
            }
        }
    }

    // ---- Graveyards and churches -------------------------------------------------------

    /// <summary>
    /// Rows of headstones and crosses, a yew or two, an angel on a plinth now and then, and iron railings
    /// along the streets. A whole graveyard block has a chapel and gates.
    /// </summary>
    private void DrawCemeteryLot(Lot lot)
    {
        var (bx, by, lx, row) = Place(lot);
        bool whole = Map.LotsOfBlock(bx, by).All(l => l.Terrain == Terrain.Cemetery);
        bool chapel = whole && lx == (bx + by) % 2 + 1 && row == 0;
        bool northEdge = row == 0, southEdge = row == CityMap.LotsPerBlockY - 1;
        bool westEdge = lx == 0 || Map.Lots[lot.Id - 1].Terrain != Terrain.Cemetery;
        if (northEdge) DrawRailing(lot.X, lot.X + 1, lot.Y + 0.04f);
        if (westEdge) DrawWestRailing(lot.X + 0.03f, lot.Y, lot.Y + 1);

        if (chapel)
        {
            var stone = Stones[(int)(Hash(lot.Id, 2) * Stones.Length)];
            float x0 = lot.X + 0.25f, x1 = lot.X + 0.75f, y0 = lot.Y + 0.12f, y1 = lot.Y + 0.4f;
            Box(x0, y0, x1, y1, 0, 0.28f, stone, stone.Lightened(0.12f), new Color("4a4a52"));
            C.DrawColoredPolygon(new[] { Q(x0, y1, 0.28f), Q(x1, y1, 0.28f), Q((x0 + x1) / 2, y1, 0.42f) }, stone.Darkened(0.05f));
            Face((x0 + x1) / 2 - 0.05f, (x0 + x1) / 2 + 0.05f, y1, 0, 0.15f, new Color("2e2620"));
            var cross = Q((x0 + x1) / 2, y1, 0.42f);
            C.DrawLine(cross, cross - new Vector2(0, _t * 0.08f), Iron, Hair(0.015f));
            C.DrawLine(cross - new Vector2(_t * 0.025f, _t * 0.055f), cross - new Vector2(-_t * 0.025f, _t * 0.055f), Iron, Hair(0.015f));
        }

        if (Fine)
        {
            int i = 0;
            for (float y = lot.Y + 0.18f; y < lot.Y + 0.95f; y += 0.15f)
            {
                if (Mathf.Abs(y - (lot.Y + 0.5f)) < 0.08f) continue;
                if (chapel && y < lot.Y + 0.45f) continue;
                for (float x = lot.X + 0.1f + (i % 2) * 0.05f; x < lot.X + 0.92f; x += 0.13f, i++)
                {
                    float r = Hash(lot.Id * 53 + i, 1040);
                    if (r < 0.12f) continue;
                    float jx = x + (Hash(lot.Id * 53 + i, 1041) - 0.5f) * 0.03f;
                    var grey = Headstone.Darkened(Hash(lot.Id * 53 + i, 1042) * 0.35f);
                    if (r < 0.7f)
                    {
                        float tall = 0.05f + Hash(lot.Id * 53 + i, 1043) * 0.03f;
                        Box(jx - 0.025f, y - 0.008f, jx + 0.025f, y + 0.008f, 0, tall, grey, grey.Lightened(0.1f), grey.Lightened(0.15f));
                        C.DrawCircle(Q(jx, y + 0.008f, tall), _t * 0.025f, grey);
                    }
                    else if (r < 0.9f)
                    {
                        C.DrawLine(Q(jx, y, 0), Q(jx, y, 0.1f), grey.Lightened(0.1f), Hair(0.014f));
                        C.DrawLine(Q(jx - 0.025f, y, 0.075f), Q(jx + 0.025f, y, 0.075f), grey.Lightened(0.1f), Hair(0.014f));
                    }
                    else
                    {
                        // An obelisk, or an angel on her plinth.
                        Box(jx - 0.03f, y - 0.03f, jx + 0.03f, y + 0.03f, 0, 0.05f, grey, grey.Lightened(0.1f), grey.Lightened(0.15f));
                        C.DrawColoredPolygon(new[] { Q(jx - 0.02f, y + 0.03f, 0.05f), Q(jx + 0.02f, y + 0.03f, 0.05f), Q(jx, y + 0.03f, 0.2f) }, grey.Lightened(0.08f));
                    }
                }
            }
        }
        else
            for (int i = 0; i < 8; i++)
                C.DrawRect(R(lot.X + 0.12f + (i % 4) * 0.2f, lot.Y + 0.25f + (i / 4) * 0.45f, 0.08f, 0.04f), Headstone);

        // Dark yews, kept away from the walk.
        if (Hash(lot.Id, 1050) < 0.55f)
        {
            float tx = lot.X + 0.2f + Hash(lot.Id, 1051) * 0.6f, ty = lot.Y + (Hash(lot.Id, 1052) < 0.5f ? 0.3f : 0.8f);
            C.DrawLine(Q(tx, ty, 0), Q(tx, ty, 0.1f), new Color("3a2a1e"), Hair(0.03f));
            C.DrawColoredPolygon(new[] { Q(tx - 0.08f, ty, 0.06f), Q(tx + 0.08f, ty, 0.06f), Q(tx, ty, 0.42f) }, new Color("2a3e2a"));
            C.DrawColoredPolygon(new[] { Q(tx - 0.03f, ty, 0.12f), Q(tx + 0.02f, ty, 0.12f), Q(tx - 0.005f, ty, 0.36f) }, new Color("3a523a"));
        }

        if (southEdge)
        {
            bool gate = whole && lx == (bx + by) % 2 + 1;
            if (gate)
            {
                DrawRailing(lot.X, lot.X + 0.3f, lot.Y + 0.96f);
                DrawRailing(lot.X + 0.7f, lot.X + 1, lot.Y + 0.96f);
                var stone = new Color("8f887a");
                foreach (float px in new[] { lot.X + 0.3f, lot.X + 0.66f })
                {
                    Box(px, lot.Y + 0.93f, px + 0.05f, lot.Y + 0.99f, 0, 0.2f, stone, stone.Lightened(0.1f), stone.Lightened(0.15f));
                    C.DrawCircle(Q(px + 0.025f, lot.Y + 0.99f, 0.22f), _t * 0.022f, stone.Lightened(0.1f));
                }
                C.DrawArc(Q(lot.X + 0.5f, lot.Y + 0.96f, 0.15f), _t * 0.16f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 10, Iron, Hair(0.012f));
            }
            else DrawRailing(lot.X, lot.X + 1, lot.Y + 0.96f);
        }
    }

    /// <summary>Iron railings along a south or north edge: posts with spear tops and a rail.</summary>
    private void DrawRailing(float xa, float xb, float y)
    {
        if (!Fine)
        {
            C.DrawLine(Q(xa, y, 0.08f), Q(xb, y, 0.08f), Iron, Mathf.Max(1, _t * 0.02f));
            return;
        }
        for (float x = xa + 0.02f; x < xb; x += 0.04f)
            C.DrawLine(Q(x, y, 0), Q(x, y, 0.11f), Iron, Hair(0.007f));
        C.DrawLine(Q(xa, y, 0.09f), Q(xb, y, 0.09f), Iron, Hair(0.01f));
        C.DrawLine(Q(xa, y, 0.02f), Q(xb, y, 0.02f), Iron, Hair(0.008f));
    }

    private void DrawWestRailing(float x, float ya, float yb)
    {
        if (!Fine) return;
        for (float y = ya + 0.02f; y < yb; y += 0.05f)
            C.DrawLine(Q(x, y, 0), Q(x, y, 0.11f), Iron, Hair(0.007f));
        C.DrawLine(Q(x, ya, 0.09f), Q(x, yb, 0.09f), Iron, Hair(0.01f));
    }

    /// <summary>
    /// A stone church: a nave with buttresses and tall lancet windows down its side, a rose window in the
    /// gable, and the steeple at the west end with a gilt cross.
    /// </summary>
    private void DrawChurch(Lot lot)
    {
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        var stone = Stones[(int)(Hash(lot.Id, 2) * Stones.Length)];
        var slate = new Color("4a4a52");
        float nx0 = x0 + 0.27f;
        Box(nx0, y0 + 0.05f, x1, y1, 0, h, stone, stone.Lightened(0.12f), slate);
        // The pitched roof: its south slope, and the ridge.
        float ridge = (y0 + 0.05f + y1) / 2;
        C.DrawColoredPolygon(new[] { Q(nx0, y1, h), Q(x1, y1, h), Q(x1, ridge, h + 0.22f), Q(nx0, ridge, h + 0.22f) }, slate.Lightened(0.06f));
        C.DrawColoredPolygon(new[] { Q(nx0, y1, h), Q(nx0, ridge, h + 0.22f), Q(nx0, y0 + 0.05f, h) }, stone.Darkened(0.05f));
        if (Fine)
        {
            for (float z = h + 0.03f; z < h + 0.2f; z += 0.045f)
            {
                float t = (z - h) / 0.22f;
                float y = y1 + (ridge - y1) * t;
                C.DrawLine(Q(nx0, y, z), Q(x1, y, z), slate.Darkened(0.2f) with { A = 0.6f }, 1);
            }
            DressWall(lot, nx0, y0 + 0.05f, x1, y1, 0, h, stone, true);
            // Lancet windows between the buttresses, stained glass that glows after dark.
            int bays = 3;
            float bay = (x1 - nx0) / bays;
            for (int i = 0; i < bays; i++)
            {
                float wx = nx0 + bay * i + bay * 0.32f, ww = bay * 0.36f;
                var lancet = new[] { Q(wx, y1, 0.1f), Q(wx + ww, y1, 0.1f), Q(wx + ww, y1, h - 0.12f), Q(wx + ww / 2, y1, h - 0.05f), Q(wx, y1, h - 0.12f) };
                C.DrawColoredPolygon(lancet, new Color("2e3a52"));
                Lit(() => C.DrawColoredPolygon(lancet, new Color("e8a050") with { A = 0.85f }));
                C.DrawLine(Q(wx + ww / 2, y1, 0.1f), Q(wx + ww / 2, y1, h - 0.06f), stone.Darkened(0.2f), Hair(0.008f));
                if (i > 0)
                {
                    float bx = nx0 + bay * i;
                    Box(bx - 0.025f, y1, bx + 0.025f, y1 + 0.05f, 0, h * 0.8f, stone.Darkened(0.04f), stone.Lightened(0.08f), stone.Lightened(0.1f));
                    C.DrawColoredPolygon(new[] { Q(bx - 0.025f, y1 + 0.05f, h * 0.8f), Q(bx + 0.025f, y1 + 0.05f, h * 0.8f), Q(bx + 0.025f, y1, h * 0.92f), Q(bx - 0.025f, y1, h * 0.92f) }, stone.Lightened(0.1f));
                }
            }
        }
        // The tower and spire at the west end, with the doors at its foot.
        float tx0 = x0, tx1 = x0 + 0.27f, ty0 = y1 - 0.3f, tower = h + 0.55f;
        Box(tx0, ty0, tx1, y1, 0, tower, stone.Lightened(0.05f), stone.Lightened(0.17f), stone);
        if (Fine)
        {
            DressWall(lot, tx0, ty0, tx1, y1, 0, tower, stone.Lightened(0.05f), true);
            // The belfry's louvred openings.
            Face(tx0 + 0.08f, tx1 - 0.08f, y1, tower - 0.2f, tower - 0.06f, new Color("1e1a18"));
            for (float z = tower - 0.18f; z < tower - 0.07f; z += 0.03f)
                C.DrawLine(Q(tx0 + 0.08f, y1, z), Q(tx1 - 0.08f, y1, z), stone.Darkened(0.2f), 1);
            // A clock face.
            C.DrawCircle(Q((tx0 + tx1) / 2, y1, tower - 0.3f), _t * 0.045f, new Color("e8e0c8"));
            C.DrawLine(Q((tx0 + tx1) / 2, y1, tower - 0.3f), Q((tx0 + tx1) / 2, y1, tower - 0.27f), Iron, 1);
        }
        var door = new[] { Q(tx0 + 0.07f, y1, 0), Q(tx1 - 0.07f, y1, 0), Q(tx1 - 0.07f, y1, 0.13f), Q((tx0 + tx1) / 2, y1, 0.18f), Q(tx0 + 0.07f, y1, 0.13f) };
        C.DrawColoredPolygon(door, new Color("4a2e1e"));
        float apex = tower + 0.6f;
        var cx = (tx0 + tx1) / 2;
        var cy = (ty0 + y1) / 2;
        C.DrawColoredPolygon(new[] { Q(tx0, y1, tower), Q(tx1, y1, tower), Q(cx, cy, apex) }, slate);
        C.DrawColoredPolygon(new[] { Q(tx0, y1, tower), Q(tx0, ty0, tower), Q(cx, cy, apex) }, slate.Lightened(0.1f));
        var cross = Q(cx, cy, apex);
        var gilt = new Color("c9a24a");
        C.DrawLine(cross, cross - new Vector2(0, _t * 0.14f), gilt, Mathf.Max(1, _t * 0.02f));
        C.DrawLine(cross - new Vector2(_t * 0.045f, _t * 0.095f), cross - new Vector2(-_t * 0.045f, _t * 0.095f), gilt, Mathf.Max(1, _t * 0.02f));
        // The rose window in the gable end.
        var rose = Q((nx0 + x1) / 2, y1, h * 0.75f);
        if (!Fine) return;
        C.DrawCircle(rose, _t * 0.07f, stone.Darkened(0.15f));
        C.DrawCircle(rose, _t * 0.055f, new Color("3a4a6a"));
        Lit(() => C.DrawCircle(rose, _t * 0.05f, new Color("ffb85a")));
    }

    /// <summary>The name of a church, a graveyard or a churchyard.</summary>
    private string LandmarkName(Lot lot)
    {
        var (bx, by, _, _) = Place(lot);
        switch (lot.Terrain)
        {
            case Terrain.Church:
                return $"St. {Saints[(bx * 7 + by * 3) % Saints.Length]}'s Church";
            case Terrain.Cemetery:
                if (Map.LotsOfBlock(bx, by).All(l => l.Terrain == Terrain.Cemetery))
                    return $"{Cemeteries[(bx * 5 + by) % Cemeteries.Length]} Cemetery";
                var church = Map.LotsOfBlock(bx, by).FirstOrDefault(l => l.Terrain == Terrain.Church);
                return church == null ? "Churchyard" : $"Churchyard of {LandmarkName(church)}";
            case Terrain.Park:
                return "City park";
        }
        return "";
    }
}
