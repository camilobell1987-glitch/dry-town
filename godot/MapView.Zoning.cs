using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The city zoned the way it grew up: brownstone rows with their stoops, wooden houses behind picket fences out in
/// the suburbs, mansions and grand apartment houses by the parks, warehouses, gasholders and coal yards on the
/// waterfront, and downtown the city's own buildings in stone: City Hall under its dome, the courts, the library,
/// the museum, the opera and the theatres.
/// </summary>
public partial class MapView
{
    private static readonly Color Cinders = new("3e3a37");
    private static readonly Color Lawn = new("51703c");
    private static readonly Color Flagstone = new("8e877a");
    private static readonly Color[] Brownstones = { new("6e4a3a"), new("7a5242"), new("5e4034"), new("84604a") };
    private static readonly Color[] Limestones = { new("cfc6b2"), new("c2b8a2"), new("d8d0bc"), new("b8ae98") };
    private static readonly Color[] Clapboard = { new("e8e2d2"), new("d8d0a8"), new("b8c4b0"), new("a8b4c4"), new("d8b8a0"), new("c8c0b4") };
    private static readonly Color[] Shingles = { new("5a3a30"), new("4a4a52"), new("3e4a3a"), new("6a4a3a") };
    private static readonly Color RoofSlate = new("3e3e46");
    private static readonly Color Copper = new("6a9a88");

    /// <summary>The part of town a lot is in, for its tooltip: whose quarter it is when it's the tenements.</summary>
    private string ZoneLine(Lot lot)
    {
        var zone = Map.ZoneOf(lot);
        if (zone == Zone.Tenements && World is { } w)
        {
            var people = w.Neighbourhood(lot.WardId);
            if (people != Ethnicity.Unknown) return $"\n{Content.Label(people)} tenements";
        }
        return $"\n{CityMap.Label(zone)}";
    }

    // ---- Houses -------------------------------------------------------------------

    /// <summary>A house or a mansion stands back from the street in its garden, its front door toward the street.</summary>
    private (float X0, float Y0, float X1, float Y1) HouseFootprint(Lot lot)
    {
        bool mansion = FillerOf(lot) == Filler.Mansion, south = FacesSouth(lot);
        float half = mansion ? 0.36f : 0.25f, depth = mansion ? 0.44f : 0.36f;
        float cx = lot.X + 0.5f + (mansion ? 0 : (Hash(lot.Id, 1500) - 0.5f) * 0.14f);
        float front = south ? lot.Y + 0.72f : lot.Y + 0.28f;
        return south ? (cx - half, front - depth, cx + half, front) : (cx - half, front, cx + half, front + depth);
    }

    /// <summary>A lawn round a house, a path from its door to the gate and, before a mansion, a gravel drive.</summary>
    private void DrawGardenGround(Lot lot)
    {
        C.DrawRect(R(lot.X, lot.Y, 1, 1).Grow(-_t * 0.02f), Lawn);
        var (x0, y0, x1, y1) = Footprint(lot);
        bool south = FacesSouth(lot), mansion = FillerOf(lot) == Filler.Mansion;
        float cx = (x0 + x1) / 2;
        float from = south ? y1 : lot.Y, to = south ? lot.Y + 1 : y0;
        C.DrawRect(R(cx - (mansion ? 0.09f : 0.04f), from, mansion ? 0.18f : 0.08f, to - from), mansion ? Gravel : Flagstone);
        if (!Fine) return;
        if (mansion)
        {
            // Flower beds either side of the drive.
            foreach (float bx in new[] { lot.X + 0.12f, lot.X + 0.66f })
                C.DrawRect(R(bx, south ? y1 + 0.06f : lot.Y + 0.06f, 0.22f, 0.12f), new Color("6a4a3a"));
            for (int i = 0; i < 8; i++)
                C.DrawCircle(P(lot.X + 0.14f + (i % 4) * 0.06f + (i >= 4 ? 0.54f : 0), (south ? y1 + 0.12f : lot.Y + 0.12f)),
                    _t * 0.018f, i % 2 == 0 ? new Color("c84a4a") : new Color("e8d07a"));
        }
        // A tree in the back garden throws its shadow on the lawn.
        var (tx, ty) = GardenTree(lot);
        TreeShadow(tx, ty, 1);
    }

    private (float X, float Y) GardenTree(Lot lot)
    {
        bool south = FacesSouth(lot);
        float x = lot.X + (Hash(lot.Id, 1502) < 0.5f ? 0.14f : 0.86f);
        return (x, south ? lot.Y + 0.2f : lot.Y + 0.8f);
    }

    /// <summary>A pitched roof with its ridge running east to west, and the gable end showing on the west.</summary>
    private void Gable(float x0, float y0, float x1, float y1, float h, float rise, Color roof, Color end)
    {
        float ridge = (y0 + y1) / 2;
        C.DrawColoredPolygon(new[] { Q(x0, y0, h), Q(x1, y0, h), Q(x1, ridge, h + rise), Q(x0, ridge, h + rise) }, roof.Darkened(0.2f));
        C.DrawColoredPolygon(new[] { Q(x0, y1, h), Q(x0, ridge, h + rise), Q(x0, y0, h) }, end.Darkened(0.08f));
        C.DrawColoredPolygon(new[] { Q(x0, y1, h), Q(x1, y1, h), Q(x1, ridge, h + rise), Q(x0, ridge, h + rise) }, roof);
        if (!Fine) return;
        for (float t = 0.25f; t < 1; t += 0.25f)
        {
            float y = y1 + (ridge - y1) * t, z = h + rise * t;
            C.DrawLine(Q(x0, y, z), Q(x1, y, z), roof.Darkened(0.25f) with { A = 0.6f }, 1);
        }
    }

    /// <summary>A hipped roof: four slopes up to a short ridge, the way the grand houses have them.</summary>
    private void Hip(float x0, float y0, float x1, float y1, float h, float rise, Color roof)
    {
        float mid = (y0 + y1) / 2, inset = Mathf.Min((y1 - y0) / 2, (x1 - x0) / 2 - 0.02f);
        Vector2 ra = Q(x0 + inset, mid, h + rise), rb = Q(x1 - inset, mid, h + rise);
        C.DrawColoredPolygon(new[] { Q(x0, y0, h), Q(x1, y0, h), rb, ra }, roof.Darkened(0.22f));
        C.DrawColoredPolygon(new[] { Q(x1, y0, h), Q(x1, y1, h), rb }, roof.Darkened(0.1f));
        C.DrawColoredPolygon(new[] { Q(x0, y1, h), Q(x0, y0, h), ra }, roof.Lightened(0.06f));
        C.DrawColoredPolygon(new[] { Q(x0, y1, h), Q(x1, y1, h), rb, ra }, roof);
    }

    /// <summary>A wooden house in the suburbs: clapboard, a shingled roof, a porch out front and a picket fence.</summary>
    private void DrawHouse(Lot lot, float h)
    {
        var (x0, y0, x1, y1) = Footprint(lot);
        var wall = Clapboard[(int)(Hash(lot.Id, 2) * Clapboard.Length)];
        var roof = Shingles[(int)(Hash(lot.Id, 1) * Shingles.Length)];
        bool south = FacesSouth(lot);
        var (tx, ty) = GardenTree(lot);
        if (ty < y0) DrawTree(tx, ty, 1);
        float ridge = (y0 + y1) / 2;
        // The chimney, behind the ridge.
        var brick = new Color("8a4a34");
        Box(x1 - 0.1f, ridge - 0.08f, x1 - 0.04f, ridge - 0.02f, h, h + 0.22f, brick, brick.Lightened(0.1f), new Color("1a1716"));
        Box(x0, y0, x1, y1, 0, h, wall, wall.Darkened(0.08f), roof);
        if (Fine)
        {
            for (float z = 0.03f; z < h; z += 0.035f) C.DrawLine(Q(x0, y1, z), Q(x1, y1, z), wall.Darkened(0.14f) with { A = 0.5f }, 1);
            foreach (float z in new[] { 0.04f, 0.17f })
                foreach (float x in new[] { x0 + 0.07f, x1 - 0.13f })
                {
                    Face(x, x + 0.06f, y1, z, z + 0.08f, Glass);
                    Lit(() => Face(x + 0.01f, x + 0.05f, y1, z + 0.01f, z + 0.07f, new Color("ffd890") with { A = 0.85f }));
                    Face(x - 0.015f, x, y1, z, z + 0.08f, new Color("3e5a3a"));
                    Face(x + 0.06f, x + 0.075f, y1, z, z + 0.08f, new Color("3e5a3a"));
                }
        }
        Gable(x0, y0, x1, y1, h, 0.16f, roof, wall);
        float cx = (x0 + x1) / 2;
        if (south)
        {
            Face(cx - 0.03f, cx + 0.03f, y1, 0, 0.1f, new Color("4a2e22"));
            // The porch: a roof on two posts over the steps.
            Box(cx - 0.11f, y1, cx + 0.11f, y1 + 0.07f, 0.12f, 0.135f, wall.Darkened(0.1f), wall, roof.Lightened(0.1f));
            if (Fine)
                foreach (float x in new[] { cx - 0.1f, cx + 0.1f })
                    C.DrawLine(Q(x, y1 + 0.065f, 0), Q(x, y1 + 0.065f, 0.12f), wall.Lightened(0.2f), Mathf.Max(1, _t * 0.012f));
        }
        if (ty >= y0) DrawTree(tx, ty, 1);
        DrawPicketFence(lot, cx);
        // Now and then the family car out front.
        if (Hash(lot.Id, 1503) < 0.3f)
        {
            var ride = Hash(lot.Id, 1504) < 0.7f ? Ride.ModelT : Ride.Packard;
            float carX = cx < lot.X + 0.5f ? lot.X + 0.84f : lot.X + 0.16f;
            DrawVehicle(new Vector2(carX, south ? lot.Y + 0.82f : lot.Y + 0.18f), true, 1, ride, RidePaint(ride, Hash(lot.Id, 1505)), 0, 0.7f);
        }
    }

    /// <summary>A white picket fence along the street, with a gap for the gate.</summary>
    private void DrawPicketFence(Lot lot, float gate)
    {
        if (!Fine) return;
        float y = FacesSouth(lot) ? lot.Y + 0.97f : lot.Y + 0.03f;
        var white = new Color("eeeae0");
        for (float x = lot.X + 0.03f; x < lot.X + 0.98f; x += 0.045f)
            if (Mathf.Abs(x - gate) > 0.06f) C.DrawLine(Q(x, y, 0), Q(x, y, 0.055f), white, Mathf.Max(1, _t * 0.008f));
        C.DrawLine(Q(lot.X + 0.03f, y, 0.04f), Q(gate - 0.06f, y, 0.04f), white, 1);
        C.DrawLine(Q(gate + 0.06f, y, 0.04f), Q(lot.X + 0.97f, y, 0.04f), white, 1);
    }

    /// <summary>A mansion: dressed stone, a hipped slate roof with dormers, a columned porch and a clipped hedge.</summary>
    private void DrawMansion(Lot lot, float h)
    {
        var (x0, y0, x1, y1) = Footprint(lot);
        var wall = Hash(lot.Id, 2) < 0.6f ? Limestones[(int)(Hash(lot.Id, 3) * Limestones.Length)] : new Color("8a4a3a");
        bool south = FacesSouth(lot);
        var (tx, ty) = GardenTree(lot);
        if (ty < y0) DrawTree(tx, ty, 1.2f);
        foreach (float x in new[] { x0 + 0.08f, x1 - 0.14f })
            Box(x, y0 + 0.12f, x + 0.06f, y0 + 0.18f, h, h + 0.26f, wall.Darkened(0.1f), wall, new Color("1a1716"));
        Box(x0, y0, x1, y1, 0, h, wall, wall.Lightened(0.08f), RoofSlate);
        DressWall(lot, x0, y0, x1, y1, 0, h, wall, true);
        DrawWindows(lot, x0 + 0.04f, x1 - 0.04f, y1, 0.04f, h);
        Face(x0, x1, y1, h - 0.035f, h, wall.Lightened(0.15f));
        Hip(x0, y0, x1, y1, h, 0.18f, RoofSlate);
        float cx = (x0 + x1) / 2;
        if (Fine)
            foreach (float x in new[] { x0 + 0.16f, cx - 0.03f, x1 - 0.22f })
            {
                // Dormers in the south slope.
                float dy = y1 - 0.08f;
                Box(x, dy - 0.04f, x + 0.06f, dy, h + 0.03f, h + 0.1f, wall, wall.Lightened(0.08f), RoofSlate);
                Face(x + 0.015f, x + 0.045f, dy, h + 0.045f, h + 0.085f, Glass);
            }
        if (south)
        {
            // The portico: four columns under a pediment.
            float px0 = cx - 0.13f, px1 = cx + 0.13f;
            Box(px0 - 0.03f, y1, px1 + 0.03f, y1 + 0.1f, 0, 0.025f, wall.Darkened(0.05f), wall, wall.Lightened(0.1f));
            Face(cx - 0.035f, cx + 0.035f, y1, 0.025f, 0.15f, new Color("3a2418"));
            for (int i = 0; i < 4; i++)
            {
                float x = px0 + i * (px1 - px0) / 3;
                Box(x - 0.012f, y1 + 0.07f, x + 0.012f, y1 + 0.09f, 0.025f, h * 0.72f, wall.Lightened(0.12f), wall, wall);
            }
            Box(px0 - 0.02f, y1, px1 + 0.02f, y1 + 0.1f, h * 0.72f, h * 0.8f, wall.Lightened(0.08f), wall, wall.Lightened(0.12f));
            C.DrawColoredPolygon(new[] { Q(px0 - 0.02f, y1 + 0.1f, h * 0.8f), Q(px1 + 0.02f, y1 + 0.1f, h * 0.8f), Q(cx, y1 + 0.1f, h * 0.8f + 0.09f) }, wall.Lightened(0.14f));
        }
        if (ty >= y0) DrawTree(tx, ty, 1.2f);
        // A clipped hedge along the street, open where the drive goes through.
        float hy = south ? lot.Y + 0.95f : lot.Y + 0.02f;
        var hedge = new Color("2e4a26");
        Box(lot.X + 0.03f, hy - 0.03f, cx - 0.11f, hy, 0, 0.07f, hedge, hedge.Lightened(0.08f), hedge.Lightened(0.15f));
        Box(cx + 0.11f, hy - 0.03f, lot.X + 0.97f, hy, 0, 0.07f, hedge, hedge.Lightened(0.08f), hedge.Lightened(0.15f));
        if (Hash(lot.Id, 1503) < 0.45f)
            DrawVehicle(new Vector2(cx, south ? lot.Y + 0.85f : lot.Y + 0.15f), false, 1, Ride.Packard, RidePaint(Ride.Packard, Hash(lot.Id, 1505)), 0, 0.7f);
    }

    /// <summary>A grand apartment house on the fashionable streets: pale stone, and a canopy from the door to the kerb.</summary>
    private void DrawApartment(Lot lot, float h)
    {
        DrawBlock(lot, h, WallFor(lot), RoofFor(lot), 0);
        var (x0, _, x1, y1) = Footprint(lot);
        if (!FacesSouth(lot)) return;
        float cx = (x0 + x1) / 2;
        var awning = new Color("2e4a3a");
        Face(cx - 0.05f, cx + 0.05f, y1, 0, 0.16f, new Color("2a2018"));
        Lit(() => Face(cx - 0.04f, cx + 0.04f, y1, 0.01f, 0.15f, new Color("ffd890") with { A = 0.8f }));
        Box(cx - 0.06f, y1, cx + 0.06f, Footprint(lot).Y1 + 0.2f, 0.15f, 0.17f, awning, awning.Lightened(0.08f), awning.Lightened(0.15f));
        if (Fine)
            foreach (float x in new[] { cx - 0.055f, cx + 0.055f })
                C.DrawLine(Q(x, y1 + 0.19f, 0), Q(x, y1 + 0.19f, 0.15f), Gilt, 1);
    }

    /// <summary>A brownstone in a row: the parlour floor up a high stoop, with iron railings either side of the steps.</summary>
    private void DrawRowhouse(Lot lot, float h)
    {
        var wall = WallFor(lot);
        DrawBlock(lot, h, wall, RoofFor(lot), 1);
        var (_, _, _, y1) = Footprint(lot);
        if (!FacesSouth(lot)) return;
        foreach (float dx in new[] { 0.27f, 0.73f })
        {
            float x = lot.X + dx;
            var step = wall.Darkened(0.06f);
            for (int i = 0; i < 3; i++)
                Box(x - 0.055f, y1, x + 0.055f, y1 + 0.13f - i * 0.04f, i * 0.035f, (i + 1) * 0.035f, step, step.Darkened(0.06f), step.Lightened(0.08f));
            Face(x - 0.035f, x + 0.035f, y1, 0.1f, 0.22f, new Color("2a1c14"));
            if (Fine)
                foreach (float rx in new[] { x - 0.055f, x + 0.055f })
                    C.DrawLine(Q(rx, y1 + 0.13f, 0.06f), Q(rx, y1, 0.16f), Iron, 1);
        }
    }

    // ---- Industry -------------------------------------------------------------------

    /// <summary>A warehouse: blank brick, a row of loading doors over a dock, the owner's name painted high up.</summary>
    private void DrawWarehouse(Lot lot, float h)
    {
        var (x0, y0, x1, y1) = Footprint(lot);
        var wall = new Color("7a4a38");
        Box(x0, y0, x1, y1, 0, h, wall, wall.Lightened(0.1f), new Color("4a4442"));
        if (Fine)
        {
            for (float z = 0.06f; z < h - 0.05f; z += 0.04f) C.DrawLine(Q(x0, y1, z), Q(x1, y1, z), wall.Darkened(0.12f) with { A = 0.35f }, 1);
            // The name painted along the top of the wall.
            Face(x0 + 0.08f, x1 - 0.08f, y1, h - 0.13f, h - 0.05f, new Color("d8cfb6") with { A = 0.55f });
            for (float x = x0 + 0.12f; x < x1 - 0.12f; x += 0.06f)
                Face(x, x + 0.035f, y1, h - 0.11f, h - 0.07f, wall.Darkened(0.2f) with { A = 0.7f });
            for (float z = 0.24f; z + 0.08f < h - 0.14f; z += 0.14f)
                for (float x = x0 + 0.1f; x < x1 - 0.1f; x += 0.2f) Face(x, x + 0.06f, y1, z, z + 0.07f, Glass);
        }
        if (!FacesSouth(lot)) return;
        Box(x0 + 0.04f, y1, x1 - 0.04f, y1 + 0.06f, 0, 0.05f, new Color("5a5048"), new Color("6a6058"), new Color("6a6058"));
        for (float x = x0 + 0.08f; x + 0.14f < x1; x += 0.22f)
            Face(x, x + 0.14f, y1, 0.05f, 0.19f, Hash(lot.Id, (int)(x * 50)) < 0.4f ? new Color("141210") : new Color("5a4030"));
        if (Hash(lot.Id, 5) < 0.4f) DrawWaterTower(x0 + 0.15f, y0 + 0.2f, h);
    }

    /// <summary>
    /// A gasholder: an iron tank that rises and falls in its frame of girders as the town burns its gas, with the
    /// retort house beside it.
    /// </summary>
    private void DrawGasworks(Lot lot, float h)
    {
        float cx = lot.X + 0.52f, cy = lot.Y + 0.5f, r = 0.36f;
        var tank = new Color("5e6a64");
        var frame = new Color("3a2e2a");
        float top = h * (0.6f + 0.4f * Hash(lot.Id, 1510));
        // Guide columns behind the tank.
        if (Fine)
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.Pi + i * Mathf.Pi / 8;
                C.DrawLine(Q(cx + r * 1.08f * Mathf.Cos(a), cy + r * 1.08f * Mathf.Sin(a), 0), Q(cx + r * 1.08f * Mathf.Cos(a), cy + r * 1.08f * Mathf.Sin(a), h), frame, Mathf.Max(1, _t * 0.012f));
            }
        Cylinder(cx, cy, r, 0, top, tank, tank.Lightened(0.12f));
        if (Fine)
        {
            for (float z = 0.1f; z < top; z += 0.1f) Ring(cx, cy, r, z, tank.Darkened(0.2f));
            // The guide columns in front, tied together at the top by a ring girder.
            for (int i = 0; i <= 8; i++)
            {
                float a = i * Mathf.Pi / 8;
                C.DrawLine(Q(cx + r * 1.08f * Mathf.Cos(a), cy + r * 1.08f * Mathf.Sin(a), 0), Q(cx + r * 1.08f * Mathf.Cos(a), cy + r * 1.08f * Mathf.Sin(a), h), frame, Mathf.Max(1, _t * 0.014f));
            }
            Ring(cx, cy, r * 1.08f, h, frame);
            Ring(cx, cy, r * 1.08f, h * 0.5f, frame);
        }
    }

    /// <summary>An upright cylinder: its front half from the ground to <paramref name="z1"/>, and its top.</summary>
    private void Cylinder(float cx, float cy, float r, float z0, float z1, Color side, Color top, int points = 18)
    {
        var body = new Vector2[points * 2 + 2];
        for (int i = 0; i <= points; i++)
        {
            float a = i * Mathf.Pi / points;
            body[i] = Q(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), z0);
            body[points * 2 + 1 - i] = Q(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), z1);
        }
        C.DrawColoredPolygon(body, side);
        var lid = new Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float a = i * Mathf.Pi / points;
            lid[i] = Q(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), z1);
        }
        C.DrawColoredPolygon(lid, top);
    }

    /// <summary>The front half of a ring round an upright cylinder, at one height.</summary>
    private void Ring(float cx, float cy, float r, float z, Color colour, int points = 18)
    {
        var line = new Vector2[points + 1];
        for (int i = 0; i <= points; i++)
        {
            float a = i * Mathf.Pi / points;
            line[i] = Q(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), z);
        }
        C.DrawPolyline(line, colour, Mathf.Max(1, _t * 0.01f));
    }

    /// <summary>The coal heaps' and timber stacks' shadows in a yard.</summary>
    private void DrawYardGround(Lot lot)
    {
        if (!Fine) return;
        for (int i = 0; i < 6; i++)
            C.DrawCircle(P(lot.X + 0.1f + Hash(lot.Id, 1520 + i) * 0.8f, lot.Y + 0.1f + Hash(lot.Id, 1530 + i) * 0.8f), _t * 0.03f, new Color("1a1816"));
    }

    /// <summary>A coal and lumber yard: black heaps of coal, stacks of sawn timber and the yard office.</summary>
    private void DrawYard(Lot lot)
    {
        var coal = new Color("1e1c1c");
        for (int i = 0; i < 2; i++)
        {
            float x = lot.X + 0.28f + i * 0.4f, y = lot.Y + 0.35f + i * 0.08f;
            C.DrawColoredPolygon(new[] { Q(x - 0.17f, y + 0.12f, 0), Q(x + 0.17f, y + 0.12f, 0), Q(x + 0.04f, y, 0.16f), Q(x - 0.06f, y, 0.15f) }, coal);
            C.DrawColoredPolygon(new[] { Q(x - 0.06f, y, 0.15f), Q(x + 0.04f, y, 0.16f), Q(x + 0.12f, y - 0.12f, 0), Q(x - 0.14f, y - 0.12f, 0) }, coal.Lightened(0.08f));
        }
        var wood = new Color("a07a50");
        for (int i = 0; i < 3; i++)
        {
            float x = lot.X + 0.12f + i * 0.26f, y = lot.Y + 0.72f;
            float top = 0.06f + 0.05f * Hash(lot.Id, 1540 + i);
            Box(x, y - 0.1f, x + 0.2f, y + 0.06f, 0, top, wood, wood.Darkened(0.1f), wood.Lightened(0.12f));
            if (Fine) for (float z = 0.02f; z < top; z += 0.02f) C.DrawLine(Q(x, y + 0.06f, z), Q(x + 0.2f, y + 0.06f, z), wood.Darkened(0.3f), 1);
        }
        // The office, with the scales out front.
        var shed = new Color("6a5a4a");
        float sx = FacesSouth(lot) ? lot.Y + 0.86f : lot.Y + 0.24f;
        Box(lot.X + 0.72f, sx - 0.14f, lot.X + 0.92f, sx, 0, 0.14f, shed, shed.Lightened(0.1f), new Color("3a3432"));
    }

    // ---- The city's own buildings --------------------------------------------------

    /// <summary>
    /// The footprint of a civic building: two lots wide, drawn from its west lot, and set back behind its steps.
    /// The east lot is left with nothing standing on it.
    /// </summary>
    private (float X0, float Y0, float X1, float Y1) CivicFootprint(Lot lot)
    {
        bool south = FacesSouth(lot);
        var (_, west) = Map.CivicOf(lot);
        float x0 = west ? lot.X + 0.08f : lot.X, x1 = west ? lot.X + 1.92f : lot.X;
        return south ? (x0, lot.Y + 0.08f, x1, lot.Y + 0.74f) : (x0, lot.Y + 0.26f, x1, lot.Y + 0.92f);
    }

    private float CivicHeight(Lot lot)
    {
        var (building, west) = Map.CivicOf(lot);
        if (!west) return 0;
        return building switch
        {
            Civic.Municipal => 1.0f,
            Civic.Courthouse or Civic.Records => 0.9f,
            Civic.Opera => 0.95f,
            Civic.Theatre => 0.8f,
            Civic.Library => 0.72f,
            _ => 0.85f,
        };
    }

    /// <summary>Stone paving in front of the city's buildings, with a strip of lawn either side of the steps.</summary>
    private void DrawCivicGround(Lot lot)
    {
        var r = R(lot.X, lot.Y, 1, 1).Grow(-_t * 0.02f);
        C.DrawRect(r, Flagstone);
        if (!Fine) return;
        for (float x = lot.X + 0.2f; x < lot.X + 1; x += 0.2f) C.DrawLine(P(x, lot.Y), P(x, lot.Y + 1), Flagstone.Darkened(0.15f), 1);
        for (float y = lot.Y + 0.2f; y < lot.Y + 1; y += 0.2f) C.DrawLine(P(lot.X, y), P(lot.X + 1, y), Flagstone.Darkened(0.15f), 1);
        bool south = FacesSouth(lot);
        var (_, west) = Map.CivicOf(lot);
        float ly = south ? lot.Y + 0.8f : lot.Y + 0.04f;
        C.DrawRect(R(west ? lot.X + 0.06f : lot.X + 0.6f, ly, 0.34f, 0.14f), Lawn);
    }

    private void DrawCivic(Lot lot)
    {
        var (building, west) = Map.CivicOf(lot);
        if (!west) return;
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        bool south = FacesSouth(lot);
        var stone = building is Civic.Theatre ? new Color("c89a7a") : Limestones[(int)building % Limestones.Length];
        if (!Angled) CastShadow(x0, y0, x1, y1, h);
        switch (building)
        {
            case Civic.CityHall:
                Classical(lot, x0, y0, x1, y1, h, stone, 6, true, south);
                Cylinder(cx, cy, 0.24f, h, h + 0.22f, stone.Darkened(0.04f), stone.Lightened(0.1f));
                Dome(cx, cy, h + 0.22f, 0.24f, Copper);
                Flag(cx, cy, h + 0.56f);
                break;
            case Civic.Courthouse:
                Classical(lot, x0, y0, x1, y1, h, stone, 8, true, south);
                Box(x0 + 0.1f, y0 + 0.06f, x1 - 0.1f, y1 - 0.12f, h, h + 0.02f, stone, stone, stone.Lightened(0.08f));
                break;
            case Civic.Municipal:
                Classical(lot, x0, y0, x1, y1, h, stone, 10, false, south);
                // The tower over the middle, stepping back as it climbs, with a gilt figure on top.
                Box(cx - 0.32f, y0 + 0.1f, cx + 0.32f, y1 - 0.12f, h, h + 1.0f, stone, stone.Lightened(0.08f), stone.Lightened(0.12f));
                DrawWindows(lot, cx - 0.3f, cx + 0.3f, y1 - 0.12f, h, h + 1.0f);
                Box(cx - 0.2f, y0 + 0.16f, cx + 0.2f, y1 - 0.18f, h + 1.0f, h + 1.4f, stone.Lightened(0.03f), stone.Lightened(0.1f), stone.Lightened(0.12f));
                DrawWindows(lot, cx - 0.18f, cx + 0.18f, y1 - 0.18f, h + 1.0f, h + 1.4f);
                Cylinder(cx, cy - 0.03f, 0.1f, h + 1.4f, h + 1.62f, stone.Lightened(0.05f), stone.Lightened(0.12f));
                Dome(cx, cy - 0.03f, h + 1.62f, 0.1f, Gilt);
                C.DrawLine(Q(cx, cy - 0.03f, h + 1.72f), Q(cx, cy - 0.03f, h + 1.86f), Gilt, Mathf.Max(1, _t * 0.02f));
                break;
            case Civic.Records:
                Classical(lot, x0, y0, x1, y1, h, stone, 4, false, south);
                Hip(x0, y0, x1, y1, h, 0.2f, RoofSlate);
                break;
            case Civic.Library:
                Classical(lot, x0, y0, x1, y1, h, stone, 6, false, south);
                if (south)
                    foreach (float x in new[] { cx - 0.38f, cx + 0.38f })
                    {
                        // The lions either side of the steps.
                        Box(x - 0.05f, y1 + 0.1f, x + 0.05f, y1 + 0.16f, 0, 0.05f, stone.Darkened(0.05f), stone, stone.Lightened(0.08f));
                        Box(x - 0.04f, y1 + 0.11f, x + 0.04f, y1 + 0.15f, 0.05f, 0.1f, stone.Lightened(0.05f), stone, stone.Lightened(0.1f));
                    }
                break;
            case Civic.Museum:
                Classical(lot, x0, y0, x1, y1, h, stone, 4, true, south);
                if (south && Fine)
                    foreach (float x in new[] { x0 + 0.2f, x0 + 0.45f, x1 - 0.5f, x1 - 0.25f })
                        Face(x, x + 0.07f, y1 + 0.005f, h * 0.35f, h * 0.85f, new Color("8a2a2a"));
                break;
            case Civic.Opera:
                Box(x0, y0, x1, y1, 0, h, stone, stone.Lightened(0.08f), Copper);
                DressWall(lot, x0, y0, x1, y1, 0, h, stone, true);
                Hip(x0, y0, x1, y1, h, 0.22f, Copper);
                if (Fine)
                    for (float x = x0 + 0.12f; x + 0.12f < x1; x += 0.2f)
                    {
                        // Tall arched windows along the grand foyer, bright on an opera night.
                        var arch = new[] { Q(x, y1, h * 0.3f), Q(x + 0.1f, y1, h * 0.3f), Q(x + 0.1f, y1, h * 0.75f), Q(x + 0.05f, y1, h * 0.82f), Q(x, y1, h * 0.75f) };
                        C.DrawColoredPolygon(arch, new Color("5a4a3a"));
                        Lit(() => C.DrawColoredPolygon(arch, new Color("ffcf80") with { A = 0.9f }));
                    }
                if (south) Canopy(cx, y1, 0.36f);
                break;
            case Civic.Theatre:
                Box(x0, y0, x1, y1, 0, h, stone, stone.Lightened(0.08f), new Color("4a4442"));
                DressWall(lot, x0, y0, x1, y1, 0, h, stone, true);
                if (south)
                {
                    Canopy(cx, y1, 0.6f);
                    // The upright sign over the marquee, ringed with bulbs.
                    float sx = cx - 0.04f;
                    Box(sx, y1 + 0.02f, sx + 0.08f, y1 + 0.06f, 0.2f, h + 0.25f, new Color("8a1e1e"), new Color("6a1414"), new Color("6a1414"));
                    if (Fine)
                        for (float z = 0.24f; z < h + 0.22f; z += 0.045f)
                        {
                            float zz = z;
                            C.DrawCircle(Q(sx + 0.04f, y1 + 0.06f, zz), _t * 0.012f, new Color("e8d8a0"));
                            Lit(() => C.DrawCircle(Q(sx + 0.04f, y1 + 0.06f, zz), _t * 0.016f, new Color("ffe8a0")));
                        }
                }
                break;
        }
    }

    /// <summary>
    /// A classical front: rows of tall windows, a cornice, and in the middle a portico of columns on a flight of
    /// steps, with a pediment over it on the grandest.
    /// </summary>
    private void Classical(Lot lot, float x0, float y0, float x1, float y1, float h, Color stone, int columns, bool pediment, bool south)
    {
        Box(x0, y0, x1, y1, 0, h, stone, stone.Lightened(0.08f), stone.Darkened(0.08f));
        DressWall(lot, x0, y0, x1, y1, 0, h, stone, true);
        if (Fine)
            for (float x = x0 + 0.06f; x + 0.06f < x1; x += 0.13f)
                for (float z = 0.08f; z + 0.14f < h - 0.08f; z += 0.2f)
                {
                    float xx = x, zz = z;
                    Face(xx, xx + 0.06f, y1, zz, zz + 0.13f, Glass);
                    Lit(() => Face(xx + 0.008f, xx + 0.052f, y1, zz + 0.01f, zz + 0.12f, new Color("ffd890") with { A = Hash(lot.Id + (int)(xx * 40), (int)(zz * 50)) < 0.4f ? 0.85f : 0 }));
                }
        Face(x0, x1, y1, h - 0.06f, h, stone.Lightened(0.14f));
        if (!south) return;
        float cx = (x0 + x1) / 2, span = columns * 0.085f;
        float px0 = cx - span / 2, px1 = cx + span / 2;
        // The steps, then the columns, then the entablature across their tops.
        for (int i = 0; i < 3; i++)
            Box(px0 - 0.08f + i * 0.02f, y1, px1 + 0.08f - i * 0.02f, y1 + 0.16f - i * 0.04f, i * 0.02f, (i + 1) * 0.02f, stone.Darkened(0.04f), stone.Darkened(0.08f), stone.Lightened(0.06f));
        Face(px0 + 0.02f, px1 - 0.02f, y1, 0.06f, h * 0.78f, stone.Darkened(0.25f));
        for (int i = 0; i < columns; i++)
        {
            float x = px0 + 0.03f + i * (span - 0.06f) / Mathf.Max(1, columns - 1);
            Box(x - 0.015f, y1 + 0.06f, x + 0.015f, y1 + 0.085f, 0.06f, h * 0.8f, stone.Lightened(0.14f), stone.Lightened(0.02f), stone);
        }
        Box(px0, y1, px1, y1 + 0.1f, h * 0.8f, h * 0.92f, stone.Lightened(0.08f), stone, stone.Lightened(0.12f));
        if (pediment)
            C.DrawColoredPolygon(new[] { Q(px0, y1 + 0.1f, h * 0.92f), Q(px1, y1 + 0.1f, h * 0.92f), Q(cx, y1 + 0.1f, h * 0.92f + 0.12f) }, stone.Lightened(0.12f));
    }

    /// <summary>A dome over a drum, drawn as its outline against the sky, with the lantern on top.</summary>
    private void Dome(float cx, float cy, float z, float r, Color colour)
    {
        const int points = 14;
        var shape = new Vector2[points + 1];
        for (int i = 0; i <= points; i++)
        {
            float a = i * Mathf.Pi / points;
            shape[i] = Q(cx + r * Mathf.Cos(a), cy, z + r * 1.1f * Mathf.Sin(a));
        }
        C.DrawColoredPolygon(shape, colour);
        if (Fine)
            for (int i = 1; i < 4; i++)
            {
                float a = i * Mathf.Pi / 4;
                C.DrawLine(Q(cx + r * Mathf.Cos(a) * 0.98f, cy, z), Q(cx, cy, z + r * 1.1f), colour.Darkened(0.2f), 1);
            }
        Box(cx - 0.025f, cy - 0.025f, cx + 0.025f, cy + 0.025f, z + r * 1.08f, z + r * 1.08f + 0.07f, colour.Lightened(0.15f), colour, colour.Lightened(0.2f));
    }

    private void Flag(float x, float y, float z)
    {
        var top = Q(x, y, z + 0.25f);
        C.DrawLine(Q(x, y, z), top, Iron, Mathf.Max(1, _t * 0.012f));
        C.DrawRect(new Rect2(top, new Vector2(0.16f, 0.09f) * _t), new Color("b02a2a"));
        C.DrawRect(new Rect2(top, new Vector2(0.07f, 0.05f) * _t), new Color("2a3a6a"));
    }

    /// <summary>A canopy out over the pavement from a theatre's doors, its underside ringed with bulbs that light at dusk.</summary>
    private void Canopy(float cx, float y1, float width)
    {
        var dark = new Color("2a2420");
        Face(cx - width / 2 + 0.04f, cx + width / 2 - 0.04f, y1, 0, 0.16f, new Color("3a2418"));
        Lit(() => Face(cx - width / 2 + 0.05f, cx + width / 2 - 0.05f, y1, 0.01f, 0.15f, new Color("ffcf80") with { A = 0.8f }));
        Box(cx - width / 2, y1, cx + width / 2, y1 + 0.2f, 0.17f, 0.22f, new Color("d8c8a0"), dark, dark.Lightened(0.1f));
        if (!Fine) return;
        for (float x = cx - width / 2 + 0.02f; x < cx + width / 2; x += 0.035f)
        {
            float xx = x;
            Lit(() => C.DrawCircle(Q(xx, y1 + 0.2f, 0.17f), _t * 0.01f, new Color("fff0b0")));
        }
    }
}
