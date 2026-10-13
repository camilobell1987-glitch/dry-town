using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The city's artwork, drawn from shapes at runtime and seen at an angle: asphalt and pavements
/// on the ground, buildings standing up with a front wall, a west wall and a roof, lamps, signs on
/// the rooftops, traffic and passers-by, and the light changing through the day. Everything is in
/// tile units, with height going up the screen and leaning a little to the east.
/// </summary>
public partial class MapView
{
    // ---- Small helpers ---------------------------------------------------------

    private Vector2 _o;
    private float _t;

    /// <summary>Whether small details are being drawn: paving joints, windows, rooftop clutter, trade pictograms.</summary>
    private bool _fine = true;

    /// <summary>While the city is recorded in full, the pen for lit windows and doorways, which only show after dark.</summary>
    private Pen? _lights;

    private bool Fine => _fine;

    /// <summary>How far a point moves east on screen for each tile it rises.</summary>
    private const float S = 0.25f;

    private Vector2 P(float x, float y) => _o + new Vector2(x, y) * _t;
    private Rect2 R(float x, float y, float w, float h) => new(P(x, y), new Vector2(w, h) * _t);

    /// <summary>A point at a height above the ground, as drawn.</summary>
    private Vector2 Q(float x, float y, float z) => P(x + S * z, y - z);

    /// <summary>A stable pseudo-random number in [0, 1) for a pair of integers, so the city looks the same every frame.</summary>
    private static float Hash(int a, int b = 0)
    {
        uint h = (uint)(a * 374761393 + b * 668265263) ^ 0x9E3779B9u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    private void Ellipse(Vector2 centre, Vector2 radii, Color colour, float angle = 0, int points = 14)
    {
        var poly = new Vector2[points];
        for (int i = 0; i < points; i++)
        {
            float a = Mathf.Tau * i / points;
            poly[i] = centre + new Vector2(Mathf.Cos(a) * radii.X, Mathf.Sin(a) * radii.Y).Rotated(angle);
        }
        C.DrawColoredPolygon(poly, colour);
    }

    /// <summary>A soft pool of light: stacked translucent circles.</summary>
    private void Glow(Vector2 centre, float radius, Color colour, float strength)
    {
        if (strength <= 0.01f) return;
        // Brightest at the middle and fading smoothly out, with a little more in the core.
        C.DrawRadial(centre, radius, colour with { A = colour.A * strength * 0.3f }, colour with { A = 0 });
        C.DrawRadial(centre, radius * 0.4f, colour with { A = colour.A * strength * 0.12f }, colour with { A = 0 }, 16);
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
        C.DrawColoredPolygon(poly, colour);
    }

    /// <summary>A box standing on the ground or raised off it: west wall, front wall, then the top.</summary>
    private void Box(float x0, float y0, float x1, float y1, float z0, float z1, Color front, Color side, Color top)
    {
        C.DrawColoredPolygon(new[] { Q(x0, y1, z0), Q(x0, y0, z0), Q(x0, y0, z1), Q(x0, y1, z1) }, side);
        C.DrawColoredPolygon(new[] { Q(x0, y1, z0), Q(x1, y1, z0), Q(x1, y1, z1), Q(x0, y1, z1) }, front);
        C.DrawColoredPolygon(new[] { Q(x0, y0, z1), Q(x1, y0, z1), Q(x1, y1, z1), Q(x0, y1, z1) }, top);
    }

    /// <summary>A patch on a south-facing wall: a window, a door, a sign.</summary>
    private void Face(float x0, float x1, float y, float z0, float z1, Color colour) =>
        C.DrawColoredPolygon(new[] { Q(x0, y, z0), Q(x1, y, z0), Q(x1, y, z1), Q(x0, y, z1) }, colour);

    /// <summary>A patch on a west-facing wall.</summary>
    private void West(float x, float y0, float y1, float z0, float z1, Color colour) =>
        C.DrawColoredPolygon(new[] { Q(x, y1, z0), Q(x, y0, z0), Q(x, y0, z1), Q(x, y1, z1) }, colour);

    /// <summary>Draw with the lights pen for a moment, if the city is being recorded in full.</summary>
    private void Lit(Action draw)
    {
        if (_lights == null) return;
        var keep = C;
        C = _lights;
        draw();
        C = keep;
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

    /// <summary>What daylight colours are multiplied by at full night.</summary>
    private static readonly Color NightShade = new(0.28f, 0.33f, 0.52f);

    // ---- Colours --------------------------------------------------------------

    private static readonly Color Asphalt = new("2b2a2c");
    private static readonly Color AsphaltWorn = new("323033");
    private static readonly Color Pavement = new("6a6359");
    private static readonly Color Kerb = new("837b6e");
    private static readonly Color RoadPaint = new("cfc6a8");
    private static readonly Color Grass = new("4a6a3a");
    private static readonly Color GrassDark = new("3a5530");
    private static readonly Color TreeLeaf = new("4f7a3d");
    private static readonly Color Dirt = new("6a5640");
    private static readonly Color Water = new("2f4c63");
    private static readonly Color Shadow = new(0, 0, 0, 0.32f);
    private static readonly Color Lamplight = new("ffd88a");
    private static readonly Color Glass = new("2c3640");
    private static readonly Color WindowLit = new("ffd27a");

    private static readonly Color[] Bricks =
    {
        new("7a3f2e"), new("8a4a34"), new("6e4a3a"), new("925b3f"), new("6a3a2c"), new("7d5a45"), new("865040"),
    };

    private static readonly Color[] Stones =
    {
        new("8f887a"), new("9a9078"), new("7f7c76"), new("a09478"), new("867d70"), new("6f6a64"),
    };

    private static readonly Color[] Roofs =
    {
        new("4a4440"), new("55504a"), new("5c5248"), new("4f5560"), new("5e4a3e"),
    };

    private static readonly Color[] Awnings =
    {
        new("7b6a55"), new("5f6b5a"), new("6e5a63"), new("5a6573"),
    };

    private static readonly Color[] CarPaint =
    {
        new("1d1d1f"), new("1d1d1f"), new("2c3b2f"), new("4a2427"), new("cbbf9f"), new("2a3346"), new("54493a"),
    };

    // ---- What stands where -----------------------------------------------------

    /// <summary>What a lot nobody does business on has on it.</summary>
    private enum Filler { None, Tenement, Office, Factory, Vacant, Parking, PocketPark }

    /// <summary>
    /// How far a point is into downtown, from 0 at the edge of it to 1 in the middle. Big cities have one;
    /// a city split by a river has it north of the water.
    /// </summary>
    private float Downtown(float x, float y)
    {
        if (Map.BlocksX < 9) return 0;
        float cy = Map.RiverRow >= 0 ? Map.Height * 0.3f : Map.Height * 0.5f;
        float dx = (x - Map.Width * 0.5f) / (Map.Width * 0.5f), dy = (y - cy) / (Map.Height * 0.5f);
        return Mathf.Clamp((0.42f - Mathf.Sqrt(dx * dx + dy * dy)) / 0.3f, 0, 1);
    }

    /// <summary>Whether a lot's block is on the waterfront.</summary>
    private bool Waterfront(Lot lot) => Map.RiverRow >= 0 && Math.Abs((lot.Y - 1) / CityMap.StrideY - Map.RiverRow) == 1;

    private Filler FillerOf(Lot lot)
    {
        if (lot.Use != LotUse.Empty || lot.Terrain != Terrain.Land) return Filler.None;
        if (lot.Court) return Filler.Tenement;
        float h = Hash(lot.Id, 11);
        if (Downtown(lot.X + 0.5f, lot.Y + 0.5f) > 0.3f) return h < 0.88f ? Filler.Office : Filler.Parking;
        if (Waterfront(lot)) return h < 0.45f ? Filler.Factory : h < 0.65f ? Filler.Vacant : h < 0.8f ? Filler.Parking : Filler.Tenement;
        return h switch
        {
            < 0.5f => Filler.Tenement,
            < 0.62f => Filler.Vacant,
            < 0.72f => Filler.Parking,
            < 0.84f => Filler.PocketPark,
            < 0.9f => Filler.Factory,
            _ => Filler.Tenement,
        };
    }

    private string FillerName(Lot lot) => lot.Terrain != Terrain.Land ? LandmarkName(lot) : FillerOf(lot) switch
    {
        Filler.Tenement => lot.Court ? "Tenement round a court" : "Tenement",
        Filler.Office => "Office building",
        Filler.Factory => "Factory",
        Filler.Vacant => "Vacant lot",
        Filler.Parking => "Parking lot",
        Filler.PocketPark => "Little park",
        _ => "",
    };

    /// <summary>How tall a lot's building stands, in tiles.</summary>
    private float HeightOf(Lot lot)
    {
        if (lot.Terrain == Terrain.Church) return 0.55f;
        if (lot.Terrain != Terrain.Land || World == null) return 0;
        // Round a court the houses on the street are low, and the tall tenements stand at the back.
        if (lot.Court && FacesSouth(lot)) return 0.36f + 0.08f * Hash(lot.Id, 7);
        float d = Downtown(lot.X + 0.5f, lot.Y + 0.5f);
        float h = Hash(lot.Id, 7);
        switch (lot.Use)
        {
            case LotUse.Precinct: return 0.72f;
            case LotUse.Headquarters: return 0.62f;
            case LotUse.Bank: return 0.68f;
            case LotUse.GunShop: return 0.5f + 0.15f * h;
            case LotUse.CarDealer: return 0.24f;
            case LotUse.Business:
                return World.BusinessById(lot.BusinessId).Kind switch
                {
                    BusinessKind.Hotel => (1.0f + 0.3f * h) * (1 + 0.8f * d),
                    BusinessKind.Warehouse => 0.45f,
                    BusinessKind.Garage => 0.28f,
                    BusinessKind.Court => (0.6f + 0.25f * h) * (1 + 1.2f * d),
                    _ => (0.4f + 0.25f * h) * (1 + 1.3f * d),
                };
        }
        return FillerOf(lot) switch
        {
            Filler.Tenement => (0.55f + 0.3f * h) * (1 + 1.2f * d),
            Filler.Office => (0.8f + 0.6f * h) * (1 + 0.5f * d),
            Filler.Factory => 0.4f,
            _ => 0,
        };
    }

    private float HeightCached(Lot lot) => lot.Id < _height.Length ? _height[lot.Id] : HeightOf(lot);

    /// <summary>A building's footprint on its lot, in tiles: west, north, east, south.</summary>

    /// <summary>The outline of a lot's building as drawn, in tiles: its walls and roof together.</summary>
    private Vector2[] Silhouette(Lot lot)
    {
        var (x0, y0, x1, y1) = Footprint(lot);
        float h = HeightCached(lot);
        if (h <= 0) return new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
        float s = S * h;
        return new[]
        {
            new Vector2(x0, y0), new Vector2(x0 + s, y0 - h), new Vector2(x1 + s, y0 - h),
            new Vector2(x1 + s, y1 - h), new Vector2(x1, y1), new Vector2(x0, y1),
        };
    }

    private static bool FacesSouth(Lot lot) => lot.FrontY > lot.Y;

    // ---- Recording the city ----------------------------------------------------

    private Dictionary<(int Row, int Col), List<Lot>> _lotsAt = new();
    private Dictionary<(int Row, int Col), List<Vector2>> _lampsAt = new();
    private List<Lot> _factories = new();

    /// <summary>The street row the elevated railway runs over, or -1 in a city too small for one.</summary>
    private int RailRow => Map.BlocksY >= 5 ? Map.BlocksY / 3 * CityMap.StrideY : -1;

    private void IndexCity()
    {
        IndexBlocks();
        _lotsAt = Map.Lots.GroupBy(l => (l.Y, Column(l.X))).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.X).ToList());
        _lampsAt = LampPosts().GroupBy(l => ((int)l.Y, Column(l.X))).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.X).ToList());
        FindFactories();
    }

    private void FindFactories() => _factories = Map.Lots.Where(l => FillerOf(l) == Filler.Factory).ToList();

    /// <summary>Record one cell of ground: the block, the street to its north and the avenue to its west.</summary>
    private void RecordGround(int bx, int by)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            C = pass == 0 ? _groundFull[bx, by] : _groundCoarse[bx, by];
            C.Clear();
            _fine = pass == 0;
            DrawGroundCell(bx, by);
            C.Flush();
        }
    }

    /// <summary>Record one row of one column of blocks: the buildings and whatever stands up there, east to west.</summary>
    private void RecordRow(int y, int col)
    {
        _full[y, col].Clear();
        _coarse[y, col].Clear();
        _lit[y, col].Clear();
        _lotsAt.TryGetValue((y, col), out var lots);
        _lampsAt.TryGetValue((y, col), out var lamps);
        for (int pass = 0; pass < 2; pass++)
        {
            C = pass == 0 ? _full[y, col] : _coarse[y, col];
            _fine = pass == 0;
            _lights = pass == 0 ? _lit[y, col] : null;
            if (y == RailRow) DrawElevated(col);
            if (lamps != null) foreach (var l in lamps) DrawLampPost(l);
            if (lots != null) foreach (var lot in lots) DrawLot(lot);
            C.Flush();
        }
        _lit[y, col].Flush();
        _lights = null;
        CityDraws++;
    }

    private void RecordLampGlow()
    {
        C = _lampGlow;
        C.Clear();
        foreach (var l in LampPosts()) Glow(P(l.X, l.Y), _t * 0.55f, Lamplight, 0.9f);
        C.Flush();
    }

    // ---- Ground ---------------------------------------------------------------

    private void DrawGroundCell(int bx, int by)
    {
        float x0 = bx * CityMap.StrideX, y0 = by * CityMap.StrideY;
        float w = bx < Map.BlocksX ? CityMap.StrideX : 1, h = by < Map.BlocksY ? CityMap.StrideY : 1;
        // A margin of dark pavement round the edge of the city.
        var cell = R(x0, y0, w, h);
        var grown = cell;
        if (bx == 0) grown = grown.GrowSide(Side.Left, _t * 0.3f);
        if (by == 0) grown = grown.GrowSide(Side.Top, _t * (0.3f + Headroom));
        if (bx == Map.BlocksX) grown = grown.GrowSide(Side.Right, _t * 0.3f);
        if (by == Map.BlocksY) grown = grown.GrowSide(Side.Bottom, _t * 0.3f);
        if (grown != cell) C.DrawRect(grown, Pavement.Darkened(0.35f));
        C.DrawRect(cell, Asphalt);

        if (Fine)
        {
            // Patches of worn tarmac so the streets aren't one flat colour.
            for (int i = 0; i < 4; i++)
            {
                int k = (bx * 31 + by) * 7 + i;
                float px = x0 + (Hash(k, 1) < 0.5f ? Hash(k, 2) * w : Hash(k, 2) * 1), py = y0 + (Hash(k, 1) < 0.5f ? Hash(k, 3) * 1 : Hash(k, 3) * h);
                Ellipse(P(px, py), new Vector2(0.3f + Hash(k, 4) * 0.3f, 0.12f + Hash(k, 5) * 0.12f) * _t, AsphaltWorn with { A = 0.6f });
            }
        }

        // Centre lines, broken at the crossings.
        float dash = 0.32f, gap = 0.28f, lw = Mathf.Max(1, _t * 0.035f);
        var paint = RoadPaint with { A = 0.45f };
        if (bx < Map.BlocksX)
            for (float x = x0 + 1.25f; x < x0 + CityMap.StrideX - 0.25f; x += dash + gap)
                C.DrawLine(P(x, y0 + 0.5f), P(Mathf.Min(x + dash, x0 + CityMap.StrideX - 0.25f), y0 + 0.5f), paint, lw);
        if (by < Map.BlocksY)
            for (float y = y0 + 1.25f; y < y0 + CityMap.StrideY - 0.25f; y += dash + gap)
                C.DrawLine(P(x0 + 0.5f, y), P(x0 + 0.5f, Mathf.Min(y + dash, y0 + CityMap.StrideY - 0.25f)), paint, lw);

        // Zebra crossings on each side of the junction.
        if (Fine)
        {
            var zebra = RoadPaint with { A = 0.35f };
            for (int s = 0; s < 5; s++)
            {
                float k = 0.12f + s * 0.17f;
                if (bx > 0) C.DrawRect(R(x0 - 0.16f, y0 + k, 0.12f, 0.08f), zebra);
                if (bx < Map.BlocksX) C.DrawRect(R(x0 + 1.04f, y0 + k, 0.12f, 0.08f), zebra);
                if (by > 0) C.DrawRect(R(x0 + k, y0 - 0.16f, 0.08f, 0.12f), zebra);
                if (by < Map.BlocksY) C.DrawRect(R(x0 + k, y0 + 1.04f, 0.08f, 0.12f), zebra);
            }
        }

        if (by == Map.RiverRow) DrawBridge(x0, y0);
        if (bx >= Map.BlocksX || by >= Map.BlocksY) return;
        if (by == Map.RiverRow) { DrawRiver(bx, by); return; }

        // The pavement round the block, with a kerb edge and paving joints.
        var block = BlockRect(bx, by);
        var pave = block.Grow(_t * 0.2f);
        C.DrawRect(pave, Pavement);
        C.DrawRect(pave, Kerb, false, Mathf.Max(1, _t * 0.04f));
        for (float x = pave.Position.X + _t * 0.25f; Fine && x < pave.End.X; x += _t * 0.25f)
        {
            C.DrawLine(new Vector2(x, pave.Position.Y), new Vector2(x, block.Position.Y), Kerb with { A = 0.35f }, 1);
            C.DrawLine(new Vector2(x, block.End.Y), new Vector2(x, pave.End.Y), Kerb with { A = 0.35f }, 1);
        }
        C.DrawRect(block, Pavement.Darkened(0.3f));

        var lots = Map.Lots.Where(l => (l.X - 1) / CityMap.StrideX == bx && (l.Y - 1) / CityMap.StrideY == by).ToList();
        if (lots.Count > 0 && lots[0].Terrain == Terrain.Park) DrawParkGround(bx, by);
        else foreach (var lot in lots) DrawLotGround(lot);
        DrawBackGround(bx, by);

        // Shadows fall to the east, away from the afternoon sun.
        foreach (var lot in lots)
        {
            float bh = HeightCached(lot);
            if (bh <= 0) continue;
            var (fx0, fy0, fx1, fy1) = Footprint(lot);
            float dx = bh * 0.55f, dy = bh * 0.18f;
            // A soft edge round a darker core, so the shadow doesn't look cut out of card.
            if (Fine)
                C.DrawColoredPolygon(new[]
                {
                    P(fx0, fy0 - 0.03f), P(fx1 + 0.03f, fy0 - 0.03f), P(fx1 + dx * 1.12f + 0.04f, fy0 + dy * 1.1f), P(fx1 + dx * 1.12f + 0.04f, fy1 + dy * 1.1f + 0.04f),
                    P(fx0 + dx * 1.12f, fy1 + dy * 1.1f + 0.04f), P(fx0, fy1 + 0.03f),
                }, Shadow with { A = 0.12f });
            C.DrawColoredPolygon(new[]
            {
                P(fx0, fy0), P(fx1, fy0), P(fx1 + dx, fy0 + dy), P(fx1 + dx, fy1 + dy), P(fx0 + dx, fy1 + dy), P(fx0, fy1),
            }, Shadow with { A = Fine ? 0.24f : Shadow.A });
        }
        if (Fine) DressStreet(bx, by);
    }

    private Rect2 BlockRect(int bx, int by) =>
        R(bx * CityMap.StrideX + 1, by * CityMap.StrideY + 1, CityMap.LotsPerBlockX, CityMap.LotsPerBlockY);

    /// <summary>The ground on a lot: a yard behind a building, a vacant lot, a parking lot or a little park.</summary>
    private void DrawLotGround(Lot lot)
    {
        var r = R(lot.X, lot.Y, 1, 1).Grow(-_t * 0.02f);
        if (lot.Terrain == Terrain.Cemetery) { DrawCemeteryGround(lot); return; }
        switch (FillerOf(lot))
        {
            case Filler.Vacant:
                C.DrawRect(r, Dirt);
                for (int i = 0; i < (Fine ? 9 : 0); i++)
                {
                    var c = P(lot.X + 0.1f + Hash(lot.Id, 50 + i) * 0.8f, lot.Y + 0.1f + Hash(lot.Id, 60 + i) * 0.8f);
                    C.DrawCircle(c, _t * (0.025f + Hash(lot.Id, 70 + i) * 0.04f), i % 3 == 0 ? GrassDark : Dirt.Lightened(0.15f));
                }
                break;
            case Filler.Parking:
                C.DrawRect(r, Asphalt.Lightened(0.06f));
                for (int i = 1; i < (Fine ? 4 : 0); i++)
                    C.DrawLine(P(lot.X + i * 0.25f, lot.Y + 0.15f), P(lot.X + i * 0.25f, lot.Y + 0.85f), RoadPaint with { A = 0.4f }, 1);
                break;
            case Filler.PocketPark:
                C.DrawRect(r, Grass);
                C.DrawRect(R(lot.X + 0.45f, lot.Y, 0.1f, 1), Pavement with { A = 0.7f });
                foreach (var (tx, ty, size) in TreesOn(lot)) TreeShadow(tx, ty, size);
                break;
            default:
                C.DrawRect(r, Pavement.Darkened(0.4f));
                break;
        }
    }

    /// <summary>A whole block of park: lawns, crossing paths, a pond in some and the trees' shadows.</summary>
    private void DrawParkGround(int bx, int by)
    {
        var block = BlockRect(bx, by).Grow(-_t * 0.04f);
        C.DrawRect(block, Grass);
        float mx = bx * CityMap.StrideX + 1 + CityMap.LotsPerBlockX / 2f, my = by * CityMap.StrideY + 1 + CityMap.LotsPerBlockY / 2f;
        var path = new Color("8a7f6a");
        C.DrawRect(R(mx - 0.08f, by * CityMap.StrideY + 1, 0.16f, CityMap.LotsPerBlockY), path);
        C.DrawRect(R(bx * CityMap.StrideX + 1, my - 0.08f, CityMap.LotsPerBlockX, 0.16f), path);
        C.DrawLine(P(bx * CityMap.StrideX + 1.1f, by * CityMap.StrideY + 1.1f), P(mx, my), path, _t * 0.1f);
        // The fountain's pool where the paths meet.
        Ellipse(P(mx, my), new Vector2(0.42f, 0.3f) * _t, new Color("8a8478"));
        Ellipse(P(mx, my), new Vector2(0.34f, 0.23f) * _t, Water.Lightened(0.15f));
        if (Hash(bx, by + 400) < 0.6f)
        {
            float px = bx * CityMap.StrideX + 1.0f + 2.9f, py = by * CityMap.StrideY + 1.6f;
            Ellipse(P(px, py), new Vector2(0.55f, 0.3f) * _t, Water);
            Ellipse(P(px - 0.1f, py - 0.05f), new Vector2(0.3f, 0.12f) * _t, Water.Lightened(0.12f));
        }
        foreach (var lot in Map.Lots.Where(l => (l.X - 1) / CityMap.StrideX == bx && (l.Y - 1) / CityMap.StrideY == by))
            foreach (var (tx, ty, size) in TreesOn(lot)) TreeShadow(tx, ty, size);
    }

    /// <summary>Where the trees stand on a park or a pocket park, away from its paths.</summary>
    private IEnumerable<(float X, float Y, float Size)> TreesOn(Lot lot)
    {
        bool park = lot.Terrain == Terrain.Park;
        int n = park ? 3 : 2;
        for (int i = 0; i < n; i++)
        {
            float fx = Hash(lot.Id, 20 + i), fy = Hash(lot.Id, 30 + i);
            float x = lot.X + 0.15f + fx * 0.7f, y = lot.Y + 0.2f + fy * 0.65f;
            if (park)
            {
                // Keep off the paths through the middle of the block.
                int bx = (lot.X - 1) / CityMap.StrideX, by = (lot.Y - 1) / CityMap.StrideY;
                float mx = bx * CityMap.StrideX + 1 + CityMap.LotsPerBlockX / 2f, my = by * CityMap.StrideY + 1 + CityMap.LotsPerBlockY / 2f;
                if (Mathf.Abs(x - mx) < 0.5f && Mathf.Abs(y - my) < 0.4f) continue;
                if (Mathf.Abs(x - mx) < 0.2f) x += 0.3f;
                if (Mathf.Abs(y - my) < 0.2f) y += 0.25f;
            }
            else if (Mathf.Abs(x - (lot.X + 0.5f)) < 0.15f) x += 0.25f;
            yield return (x, y, 0.8f + Hash(lot.Id, 40 + i) * 0.5f);
        }
    }

    private void TreeShadow(float x, float y, float size) =>
        Ellipse(P(x + 0.18f * size, y + 0.04f), new Vector2(0.2f, 0.09f) * size * _t, Shadow);

    private void DrawTree(float x, float y, float size)
    {
        C.DrawLine(Q(x, y, 0), Q(x, y, 0.16f * size), new Color("4a3626"), Mathf.Max(1, _t * 0.04f));
        var c = Q(x, y, 0.3f * size);
        float r = 0.16f * size * _t;
        C.DrawCircle(c, r, GrassDark.Lerp(TreeLeaf, 0.3f));
        if (!Fine) return;
        C.DrawCircle(c + new Vector2(-r * 0.3f, -r * 0.25f), r * 0.68f, TreeLeaf);
        C.DrawCircle(c + new Vector2(-r * 0.45f, -r * 0.4f), r * 0.32f, TreeLeaf.Lightened(0.15f));
    }

    // ---- Buildings ------------------------------------------------------------

    private void DrawLot(Lot lot)
    {
        var w = World!;
        if (lot.Terrain == Terrain.Water) { DrawMooring(lot); return; }
        if (lot.Terrain == Terrain.Park) { DrawParkLot(lot); return; }
        if (lot.Terrain == Terrain.Cemetery) { DrawCemeteryLot(lot); return; }
        if (lot.Terrain == Terrain.Church) { DrawChurch(lot); return; }
        if (lot.Court) DrawCourtYard(lot);
        DrawAlleyClutter(lot);
        switch (lot.Use)
        {
            case LotUse.Business:
            {
                var b = w.BusinessById(lot.BusinessId);
                var (protector, racket) = ShownState(b);
                if (Burnt(b)) DrawShell(lot, b);
                else DrawShop(lot, b, protector >= 0 ? Palette.Gang(w, protector) : null, racket, b.IsOpen || Live);
                break;
            }
            case LotUse.Headquarters:
                DrawHeadquarters(lot, Palette.Gang(w, lot.GangId));
                break;
            case LotUse.Precinct:
                DrawPrecinct(lot);
                break;
            case LotUse.Bank:
                DrawBank(lot);
                break;
            case LotUse.GunShop:
                DrawGunShop(lot);
                break;
            case LotUse.CarDealer:
                DrawCarDealer(lot);
                break;
            default:
                DrawFiller(lot);
                break;
        }
    }

    /// <summary>
    /// The walls and roof of a building, with rows of windows on the front and the west wall. A tall
    /// one steps back near the top, the way the towers downtown do.
    /// </summary>
    private void DrawBlock(Lot lot, float h, Color wall, Color roof, int groundFloorWindows = 1)
    {
        var (x0, y0, x1, y1) = Footprint(lot);
        bool setback = h > 1.3f;
        float lower = setback ? h * 0.68f : h;
        bool stone = IsStone(lot);
        float zFrom = groundFloorWindows == 1 ? 0 : 0.2f;
        Box(x0, y0, x1, y1, 0, lower, wall, wall.Lightened(0.12f), roof);
        DressWall(lot, x0, y0, x1, y1, zFrom, lower, wall, stone);
        DressRoof(lot, x0, y0, x1, y1, lower, roof);
        if (setback)
        {
            var upper = wall.Lightened(0.04f);
            Box(x0 + 0.1f, y0 + 0.08f, x1 - 0.1f, y1 - 0.1f, lower, h, upper, wall.Lightened(0.16f), roof.Lightened(0.05f));
            DrawWindows(lot, x0 + 0.1f, x1 - 0.1f, y1 - 0.1f, lower, h);
            DressRoof(lot, x0 + 0.1f, y0 + 0.08f, x1 - 0.1f, y1 - 0.1f, h, roof.Lightened(0.05f));
            DrawCornice(x0 + 0.1f, y1 - 0.1f, x1 - 0.1f, h, upper);
        }
        DrawWindows(lot, x0, x1, y1, zFrom, lower);
        DrawCornice(x0, y1, x1, lower, wall);
        if (Fine)
        {
            // Windows down the west wall.
            for (float z = 0.06f; z + 0.12f <= lower; z += 0.2f)
                for (float y = y0 + 0.15f; y < y1 - 0.15f; y += 0.28f)
                    West(x0, y, y + 0.1f, z + 0.03f, z + 0.11f, Glass.Lightened(0.08f));
        }
    }

    /// <summary>Rows of windows on a south wall, a floor every fifth of a tile; some are lit after dark.</summary>
    private void DrawWindows(Lot lot, float x0, float x1, float y, float zFrom, float zTo)
    {
        if (!Fine) return;
        int n = Mathf.Max(1, (int)((x1 - x0) / 0.2f));
        float span = (x1 - x0) / n;
        int floor = 0;
        for (float z = zFrom; z + 0.13f <= zTo; z += 0.2f, floor++)
            for (int i = 0; i < n; i++)
                DrawSashWindow(lot, x0 + span * i + span * 0.28f, span * 0.44f, y, z, floor, i);
    }

    /// <summary>Whether a lot's building is faced in stone rather than brick: downtown, and the city's own buildings.</summary>
    private bool IsStone(Lot lot) => lot.Use is LotUse.Precinct or LotUse.Bank || Downtown(lot.X + 0.5f, lot.Y + 0.5f) > 0.3f;

    private Color WallFor(Lot lot)
    {
        bool stone = Downtown(lot.X + 0.5f, lot.Y + 0.5f) > 0.3f;
        var set = stone ? Stones : Bricks;
        return set[(int)(Hash(lot.Id, 2) * set.Length)];
    }

    private Color RoofFor(Lot lot) => Roofs[(int)(Hash(lot.Id, 1) * Roofs.Length)];

    private void DrawShop(Lot lot, Business b, Color? gang, RacketKind racket, bool open)
    {
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        var wall = b.Kind switch
        {
            BusinessKind.Warehouse => new Color("7a7064"),
            BusinessKind.Garage => new Color("8a8070"),
            _ => WallFor(lot),
        };
        var roof = RoofFor(lot);
        if (!open) { wall = wall.Darkened(0.3f); roof = roof.Darkened(0.3f); }
        DrawBlock(lot, h, wall, roof, FacesSouth(lot) ? 0 : 1);

        if (b.Kind == BusinessKind.Warehouse && Fine)
            for (float x = x0 + 0.1f; x < x1 - 0.05f; x += 0.12f)
                C.DrawLine(Q(x, y0, h), Q(x, y1, h), roof.Darkened(0.2f), 1);
        if (b.Kind == BusinessKind.Hotel) DrawWaterTower(x1 - 0.22f, y0 + 0.25f, h);
        else if (Fine) DrawRoofClutter(lot, h, roof);
        if (h >= 0.55f && b.Kind is not (BusinessKind.Warehouse or BusinessKind.Garage or BusinessKind.Court) && Hash(lot.Id, 660) < 0.4f)
            DrawFireEscape(Hash(lot.Id, 4) < 0.5f ? x0 + 0.08f : x1 - 0.4f, y1, 0.2f, h);

        if (b.Kind == BusinessKind.Court)
        {
            if (FacesSouth(lot)) DrawCourtPassage(lot, h, wall, open);
        }
        else if (FacesSouth(lot)) DrawShopfront(lot, b, gang, open);
        DrawBillboard(lot, h, gang ?? new Color("3a332b"), gang.HasValue ? 3 : 1, open, () => { if (Fine) DrawTradeIcon(b.Kind, SignCentre(lot, h), _t * 0.11f); });
        if (racket != RacketKind.None) DrawRacketBadge(Q(x0 + 0.2f, (y0 + y1) / 2, h), racket);
        if (b.IsFront) DrawFrontBadge(Q(x1 - 0.2f, (y0 + y1) / 2, h), Palette.Gang(World!, b.OwnerGangId));
        if (!open) DrawBoardedUp(lot, h);
        if (World!.StrikeAt(b) != null) DrawPickets(lot, h);
        if (World.IsWireRoom(b)) DrawWireMast(lot, h);
    }

    /// <summary>The wire room: a telegraph mast on the roof, its wires running off to the street.</summary>
    private void DrawWireMast(Lot lot, float h)
    {
        var (x0, y0, x1, y1) = Footprint(lot);
        var wood = new Color("5a4632");
        float x = x0 + 0.3f, y = y0 + 0.3f, lw = Mathf.Max(2, _t * 0.045f);
        C.DrawLine(Q(x, y, h), Q(x, y, h + 0.8f), wood, lw * 1.4f);
        for (int i = 0; i < 2; i++)
        {
            float z = h + 0.62f + i * 0.13f;
            C.DrawLine(Q(x - 0.16f, y, z), Q(x + 0.16f, y, z), wood, lw);
            foreach (float dx in new[] { -0.13f, 0.13f })
            {
                C.DrawCircle(Q(x + dx, y, z + 0.02f), Mathf.Max(1.5f, _t * 0.022f), new Color("7ab0c0"));
                C.DrawLine(Q(x + dx, y, z), Q(x1 + 0.15f + dx, y1 - 0.1f, h * 0.6f), new Color("1a1714"), Mathf.Max(1, _t * 0.015f));
            }
        }
    }

    /// <summary>A strike: a placard over the roof that the whole street can see, and men with placards outside a shop that faces it.</summary>
    private void DrawPickets(Lot lot, float h)
    {
        var (x0, y0, x1, y1) = Footprint(lot);
        var card = new Color("efe8d6");
        var pole = new Color("6a5440");
        void Placard(float x, float y, float z, float w, float tall)
        {
            C.DrawLine(Q(x + w / 2, y, z - tall * 0.9f), Q(x + w / 2, y, z), pole, Mathf.Max(1, _t * 0.022f));
            C.DrawColoredPolygon(new[] { Q(x, y, z), Q(x + w, y, z), Q(x + w, y, z + tall), Q(x, y, z + tall) }, card);
            C.DrawColoredPolygon(new[] { Q(x + w * 0.1f, y, z + tall * 0.35f), Q(x + w * 0.9f, y, z + tall * 0.35f), Q(x + w * 0.9f, y, z + tall * 0.65f), Q(x + w * 0.1f, y, z + tall * 0.65f) }, Palette.Bad);
        }
        Placard(x1 - 0.42f, (y0 + y1) / 2 + 0.1f, h + 0.22f, 0.3f, 0.16f);
        if (!FacesSouth(lot)) return;
        var coat = new Color("2b2a33");
        float y = y1 + 0.2f;
        for (int i = 0; i < 3; i++)
        {
            float x = x0 + 0.16f + (x1 - x0 - 0.32f) * i / 2f;
            C.DrawLine(Q(x, y, 0), Q(x, y, 0.11f), coat, Mathf.Max(1, _t * 0.045f));
            C.DrawCircle(Q(x, y, 0.15f), _t * 0.028f, new Color("d9b48f"));
            Placard(x - 0.03f, y, 0.2f, 0.16f, 0.1f);
        }
    }

    /// <summary>What's left after a fire: blackened walls open to the sky and empty window holes.</summary>
    private void DrawShell(Lot lot, Business b)
    {
        float h = HeightCached(lot) * 0.85f;
        var (x0, y0, x1, y1) = Footprint(lot);
        var soot = new Color("2e2824");
        Box(x0, y0, x1, y1, 0, h, soot, soot.Lightened(0.08f), new Color("141110"));
        // Broken roof beams across the open top, and scorch marks up the front.
        for (int i = 0; i < 4; i++)
        {
            float x = x0 + 0.12f + i * 0.2f;
            C.DrawLine(Q(x, y0 + 0.05f, h), Q(x + 0.08f * (i % 2 == 0 ? 1 : -1), y1 - 0.05f, h - 0.04f), new Color("3a2a1e"), Mathf.Max(1, _t * 0.03f));
        }
        if (Fine)
            for (float z = 0.08f; z + 0.12f <= h; z += 0.2f)
                for (float x = x0 + 0.1f; x < x1 - 0.12f; x += 0.22f)
                    Face(x, x + 0.1f, y1, z, z + 0.1f, new Color("0e0c0b"));
        Face(x0 + 0.05f, x1 - 0.05f, y1, h * 0.55f, h, new Color("1a1614") with { A = 0.6f });
    }

    /// <summary>The ground floor of a shop that faces the viewer: a big window, a door, and an awning in the protector's colours.</summary>
    private void DrawShopfront(Lot lot, Business b, Color? gang, bool open)
    {
        var (x0, _, x1, y1) = Footprint(lot);
        if (b.Kind == BusinessKind.Garage)
        {
            Face(x0 + 0.12f, x1 - 0.12f, y1, 0, 0.17f, new Color("2a2622"));
            for (float z = 0.03f; Fine && z < 0.17f; z += 0.035f) C.DrawLine(Q(x0 + 0.12f, y1, z), Q(x1 - 0.12f, y1, z), new Color("4a443c"), 1);
        }
        else if (b.Kind == BusinessKind.Warehouse)
        {
            Face(x0 + 0.1f, x0 + 0.45f, y1, 0, 0.2f, new Color("4a3a2a"));
            Face(x1 - 0.3f, x1 - 0.12f, y1, 0, 0.14f, new Color("3a2a20"));
        }
        else
        {
            Face(x0 + 0.08f, x1 - 0.3f, y1, 0.02f, 0.13f, new Color("56636e"));
            Face(x1 - 0.24f, x1 - 0.1f, y1, 0, 0.14f, new Color("3e2c20"));
            if (Fine) DressShopfront(lot, x0, x1, y1);
            // The shop window is under the awning; after dark its light shows through the canvas instead.
            if (open && Hash(lot.Id, 13) < 0.7f)
                Lit(() => C.DrawColoredPolygon(new[] { Q(x0 + 0.04f, y1 + 0.13f, 0.12f), Q(x1 - 0.04f, y1 + 0.13f, 0.12f), Q(x1 - 0.04f, y1 + 0.13f, 0.08f), Q(x0 + 0.04f, y1 + 0.13f, 0.08f) }, WindowLit with { A = 0.4f }));
            if (open) Lit(() => Glow(P(lot.X + 0.5f, lot.Y + 1.05f), _t * 0.4f, Lamplight, 0.55f));

            // The awning: striped in the protector's colours, plain canvas otherwise.
            var canvas = Awnings[(int)(Hash(lot.Id, 9) * Awnings.Length)];
            int stripes = Fine ? 6 : 1;
            float ax0 = x0 + 0.04f, ax1 = x1 - 0.04f;
            for (int i = 0; i < stripes; i++)
            {
                float a = ax0 + (ax1 - ax0) * i / stripes, z = ax0 + (ax1 - ax0) * (i + 1) / stripes;
                var c = gang is Color g ? (i % 2 == 0 || !Fine ? g : new Color("e9e0c8")) : (i % 2 == 0 ? canvas : canvas.Lightened(0.2f));
                if (!open) c = c.Darkened(0.45f);
                C.DrawColoredPolygon(new[] { Q(a, y1, 0.17f), Q(z, y1, 0.17f), Q(z, y1 + 0.13f, 0.12f), Q(a, y1 + 0.13f, 0.12f) }, c);
                C.DrawColoredPolygon(new[] { Q(a, y1 + 0.13f, 0.12f), Q(z, y1 + 0.13f, 0.12f), Q(z, y1 + 0.13f, 0.09f), Q(a, y1 + 0.13f, 0.09f) }, c.Darkened(0.2f));
                if (Fine) C.DrawColoredPolygon(new[] { Q(a, y1 + 0.13f, 0.09f), Q(z, y1 + 0.13f, 0.09f), Q((a + z) / 2, y1 + 0.13f, 0.072f) }, c.Darkened(0.2f));
            }
        }
    }

    /// <summary>Where the sign on a building's roof is centred, as drawn.</summary>
    private Vector2 SignCentre(Lot lot, float h)
    {
        var (_, _, _, y1) = Footprint(lot);
        return Q(lot.X + 0.5f, y1 - 0.12f, h + 0.19f);
    }

    /// <summary>
    /// A sign standing on the roof, facing the street the viewer looks from, framed in the protector's
    /// colours. The trade's pictogram, or whatever else belongs there, is drawn on its face.
    /// </summary>
    private void DrawBillboard(Lot lot, float h, Color frame, float frameWidth, bool open, Action face)
    {
        var (_, _, _, y1) = Footprint(lot);
        float y = y1 - 0.12f, xa = lot.X + 0.24f, xb = lot.X + 0.76f;
        var post = new Color("2a2622");
        C.DrawLine(Q(xa + 0.06f, y, h), Q(xa + 0.06f, y, h + 0.06f), post, Mathf.Max(1, _t * 0.025f));
        C.DrawLine(Q(xb - 0.06f, y, h), Q(xb - 0.06f, y, h + 0.06f), post, Mathf.Max(1, _t * 0.025f));
        Face(xa - 0.02f, xb + 0.02f, y, h + 0.04f, h + 0.34f, frame);
        float inset = 0.012f * frameWidth;
        Face(xa - 0.02f + inset, xb + 0.02f - inset, y, h + 0.04f + inset, h + 0.34f - inset, open ? new Color("e6dcc2") : new Color("8a826e"));
        face();
    }

    private void DrawRoofClutter(Lot lot, float h, Color roof)
    {
        var (x0, y0, x1, y1) = Footprint(lot);
        var brick = Bricks[(int)(Hash(lot.Id, 2) * Bricks.Length)];
        DrawChimney(x0 + 0.1f + Hash(lot.Id, 100) * (x1 - x0 - 0.3f), y0 + 0.06f + Hash(lot.Id, 110) * 0.12f, h, brick);
        if (Hash(lot.Id, 3) < 0.6f) DrawBulkhead(x1 - 0.32f, y0 + 0.28f + Hash(lot.Id, 111) * 0.12f, h, roof);
    }

    /// <summary>A wooden water tank on stilts, on the roofs of hotels and tenements.</summary>
    private void DrawWaterTower(float x, float y, float h)
    {
        var wood = new Color("6b5038");
        float lw = Mathf.Max(1, _t * 0.02f);
        C.DrawLine(Q(x, y + 0.12f, h), Q(x, y + 0.12f, h + 0.14f), new Color("2a2622"), lw);
        C.DrawLine(Q(x + 0.16f, y + 0.12f, h), Q(x + 0.16f, y + 0.12f, h + 0.14f), new Color("2a2622"), lw);
        Box(x - 0.02f, y, x + 0.18f, y + 0.14f, h + 0.14f, h + 0.36f, wood, wood.Lightened(0.12f), wood.Darkened(0.2f));
        if (Fine)
        {
            // Iron hoops round the staves.
            foreach (float z in new[] { 0.18f, 0.25f, 0.32f })
            {
                C.DrawLine(Q(x - 0.02f, y + 0.14f, h + z), Q(x + 0.18f, y + 0.14f, h + z), Iron, Hair(0.006f));
                C.DrawLine(Q(x - 0.02f, y, h + z), Q(x - 0.02f, y + 0.14f, h + z), Iron, Hair(0.006f));
            }
            for (float sx = x; sx < x + 0.17f; sx += 0.035f)
                C.DrawLine(Q(sx, y + 0.14f, h + 0.14f), Q(sx, y + 0.14f, h + 0.36f), wood.Darkened(0.25f) with { A = 0.6f }, Hair(0.003f));
        }
        C.DrawColoredPolygon(new[] { Q(x - 0.04f, y + 0.14f, h + 0.36f), Q(x + 0.2f, y + 0.14f, h + 0.36f), Q(x + 0.08f, y + 0.07f, h + 0.46f) }, new Color("3a2a1e"));
    }

    /// <summary>A pictogram for each trade, drawn inside a box of half-size <paramref name="h"/>.</summary>
    private void DrawTradeIcon(BusinessKind kind, Vector2 c, float h)
    {
        var ink = new Color("2a241e");
        float lw = Mathf.Max(1, h * 0.16f);
        switch (kind)
        {
            case BusinessKind.Grocer:
                C.DrawRect(new Rect2(c.X - h * 0.8f, c.Y, h * 1.6f, h * 0.7f), new Color("8a5e34"));
                C.DrawCircle(c + new Vector2(-h * 0.45f, -h * 0.15f), h * 0.32f, new Color("c8463c"));
                C.DrawCircle(c + new Vector2(h * 0.05f, -h * 0.22f), h * 0.32f, new Color("e0903a"));
                C.DrawCircle(c + new Vector2(h * 0.5f, -h * 0.12f), h * 0.3f, new Color("8fae4a"));
                break;
            case BusinessKind.Diner:
                C.DrawRect(new Rect2(c.X - h * 0.6f, c.Y - h * 0.3f, h * 1.0f, h * 0.9f), ink);
                C.DrawArc(c + new Vector2(h * 0.45f, h * 0.15f), h * 0.28f, -Mathf.Pi / 2, Mathf.Pi / 2, 8, ink, lw);
                C.DrawRect(new Rect2(c.X - h * 0.8f, c.Y + h * 0.62f, h * 1.6f, h * 0.14f), ink);
                break;
            case BusinessKind.Barber:
            {
                var pole = new Rect2(c.X - h * 0.28f, c.Y - h * 0.8f, h * 0.56f, h * 1.6f);
                C.DrawRect(pole, Colors.White);
                for (int i = 0; i < 4; i++)
                {
                    float y = pole.Position.Y + h * 0.1f + i * h * 0.4f;
                    C.DrawLine(new Vector2(pole.Position.X, y + h * 0.25f), new Vector2(pole.End.X, y), i % 2 == 0 ? new Color("c8463c") : new Color("3a5fb0"), lw * 1.2f);
                }
                break;
            }
            case BusinessKind.Tailor:
                C.DrawArc(c + new Vector2(-h * 0.45f, h * 0.5f), h * 0.25f, 0, Mathf.Tau, 10, ink, lw);
                C.DrawArc(c + new Vector2(h * 0.45f, h * 0.5f), h * 0.25f, 0, Mathf.Tau, 10, ink, lw);
                C.DrawLine(c + new Vector2(-h * 0.3f, h * 0.3f), c + new Vector2(h * 0.45f, -h * 0.85f), ink, lw);
                C.DrawLine(c + new Vector2(h * 0.3f, h * 0.3f), c + new Vector2(-h * 0.45f, -h * 0.85f), ink, lw);
                break;
            case BusinessKind.Garage:
                C.DrawCircle(c, h * 0.85f, ink);
                C.DrawCircle(c, h * 0.45f, new Color("9a9286"));
                C.DrawCircle(c, h * 0.15f, ink);
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
                C.DrawColoredPolygon(shirt, new Color("5d8fd6"));
                break;
            }
            case BusinessKind.Hotel:
                C.DrawRect(new Rect2(c.X - h * 0.85f, c.Y - h * 0.1f, h * 1.7f, h * 0.55f), new Color("8a3a3a"));
                C.DrawRect(new Rect2(c.X - h * 0.85f, c.Y - h * 0.45f, h * 0.55f, h * 0.35f), Colors.White);
                C.DrawRect(new Rect2(c.X - h * 0.9f, c.Y - h * 0.7f, h * 0.15f, h * 1.5f), ink);
                C.DrawRect(new Rect2(c.X + h * 0.75f, c.Y - h * 0.1f, h * 0.15f, h * 0.9f), ink);
                break;
            case BusinessKind.Pharmacy:
                C.DrawRect(new Rect2(c.X - h * 0.25f, c.Y - h * 0.8f, h * 0.5f, h * 1.6f), new Color("3f8f4f"));
                C.DrawRect(new Rect2(c.X - h * 0.8f, c.Y - h * 0.25f, h * 1.6f, h * 0.5f), new Color("3f8f4f"));
                break;
            case BusinessKind.PoolHall:
                C.DrawCircle(c, h * 0.85f, ink);
                C.DrawCircle(c + new Vector2(-h * 0.12f, -h * 0.12f), h * 0.4f, Colors.White);
                C.DrawCircle(c + new Vector2(-h * 0.12f, -h * 0.12f), h * 0.16f, ink);
                break;
            case BusinessKind.Court:
            {
                // A washing line with a shirt and a sheet on it.
                C.DrawLine(c + new Vector2(-h * 0.9f, -h * 0.6f), c + new Vector2(h * 0.9f, -h * 0.45f), ink, lw * 0.6f);
                C.DrawRect(new Rect2(c.X - h * 0.7f, c.Y - h * 0.58f, h * 0.6f, h * 1.1f), new Color("5d8fd6"));
                C.DrawRect(new Rect2(c.X + h * 0.05f, c.Y - h * 0.5f, h * 0.7f, h * 0.8f), new Color("e8e0cc"));
                C.DrawRect(new Rect2(c.X + h * 0.05f, c.Y - h * 0.5f, h * 0.7f, h * 0.8f), ink with { A = 0.5f }, false, 1);
                break;
            }
            case BusinessKind.Warehouse:
                foreach (var (dx, dy) in new[] { (-0.85f, 0.0f), (0.0f, 0.0f), (-0.42f, -0.8f) })
                {
                    var box = new Rect2(c.X + dx * h, c.Y + dy * h, h * 0.8f, h * 0.8f);
                    C.DrawRect(box, new Color("9a7448"));
                    C.DrawRect(box, ink with { A = 0.7f }, false, 1);
                }
                break;
        }
    }

    /// <summary>A badge on the roof for the racket in the back room.</summary>
    private void DrawRacketBadge(Vector2 c, RacketKind racket)
    {
        float r = _t * 0.13f;
        c += new Vector2(0, -r * 0.6f);
        C.DrawCircle(c + new Vector2(r * 0.3f, r * 0.5f), r, Shadow);
        C.DrawCircle(c, r, new Color("7a1f1a"));
        C.DrawArc(c, r, 0, Mathf.Tau, 14, new Color("e05a4f"), Mathf.Max(1, _t * 0.025f));
        float h = r * 0.55f;
        switch (racket)
        {
            case RacketKind.Speakeasy: // a bottle
                C.DrawRect(new Rect2(c.X - h * 0.4f, c.Y - h * 0.2f, h * 0.8f, h * 1.1f), new Color("c9a24a"));
                C.DrawRect(new Rect2(c.X - h * 0.15f, c.Y - h * 0.9f, h * 0.3f, h * 0.7f), new Color("c9a24a"));
                break;
            case RacketKind.Still: // a copper pot
                C.DrawCircle(c + new Vector2(0, h * 0.2f), h * 0.7f, new Color("c87a3a"));
                C.DrawRect(new Rect2(c.X - h * 0.12f, c.Y - h * 0.9f, h * 0.24f, h * 0.6f), new Color("c87a3a"));
                break;
            case RacketKind.Numbers: // a die
                C.DrawRect(new Rect2(c.X - h * 0.75f, c.Y - h * 0.75f, h * 1.5f, h * 1.5f), Colors.White);
                C.DrawCircle(c, h * 0.18f, Colors.Black);
                C.DrawCircle(c + new Vector2(-h * 0.4f, -h * 0.4f), h * 0.16f, Colors.Black);
                C.DrawCircle(c + new Vector2(h * 0.4f, h * 0.4f), h * 0.16f, Colors.Black);
                break;
            case RacketKind.LoanShark: // a gold coin
                C.DrawCircle(c, h * 0.85f, new Color("e8b84a"));
                C.DrawArc(c, h * 0.5f, 0, Mathf.Tau, 10, new Color("5a3f10"), Mathf.Max(1, h * 0.18f));
                break;
            case RacketKind.Bookie: // a horseshoe
                C.DrawArc(c + new Vector2(0, h * 0.1f), h * 0.6f, Mathf.Pi * 0.85f, Mathf.Pi * 2.15f, 12, new Color("d8d2c4"), Mathf.Max(1, h * 0.35f));
                break;
            case RacketKind.GamblingDen: // a playing card with a red diamond
                C.DrawRect(new Rect2(c.X - h * 0.6f, c.Y - h * 0.85f, h * 1.2f, h * 1.7f), Colors.White);
                C.DrawColoredPolygon(new[] { c + new Vector2(0, -h * 0.5f), c + new Vector2(h * 0.35f, 0), c + new Vector2(0, h * 0.5f), c + new Vector2(-h * 0.35f, 0) }, new Color("c0302a"));
                break;
            case RacketKind.Slots: // a one-armed bandit
                C.DrawRect(new Rect2(c.X - h * 0.65f, c.Y - h * 0.75f, h * 1.3f, h * 1.5f), new Color("b8b8c0"));
                for (int i = -1; i <= 1; i++) C.DrawRect(new Rect2(c.X + i * h * 0.38f - h * 0.15f, c.Y - h * 0.3f, h * 0.3f, h * 0.45f), i == 0 ? new Color("c0302a") : Colors.White);
                C.DrawLine(c + new Vector2(h * 0.65f, -h * 0.1f), c + new Vector2(h * 0.95f, -h * 0.7f), new Color("e8b84a"), Mathf.Max(1, h * 0.15f));
                break;
            case RacketKind.Casino: // a roulette wheel
                C.DrawCircle(c, h * 0.9f, new Color("6a3a1a"));
                for (int i = 0; i < 8; i++)
                {
                    float a0 = i * Mathf.Tau / 8, a1 = a0 + Mathf.Tau / 8;
                    C.DrawColoredPolygon(new[] { c, c + Vector2.FromAngle(a0) * h * 0.75f, c + Vector2.FromAngle(a1) * h * 0.75f }, i % 2 == 0 ? new Color("c0302a") : new Color("1a1a1a"));
                }
                C.DrawCircle(c, h * 0.25f, new Color("e8b84a"));
                break;
            case RacketKind.Brewery: // a barrel with its hoops
                C.DrawRect(new Rect2(c.X - h * 0.6f, c.Y - h * 0.8f, h * 1.2f, h * 1.6f), new Color("9a6a3a"));
                C.DrawRect(new Rect2(c.X - h * 0.7f, c.Y - h * 0.45f, h * 1.4f, h * 0.18f), new Color("3a3a3a"));
                C.DrawRect(new Rect2(c.X - h * 0.7f, c.Y + h * 0.27f, h * 1.4f, h * 0.18f), new Color("3a3a3a"));
                break;
            case RacketKind.Nightclub: // a gold note on a purple sign
                C.DrawRect(new Rect2(c.X - h * 0.8f, c.Y - h * 0.85f, h * 1.6f, h * 1.7f), new Color("5a2a7a"));
                C.DrawCircle(c + new Vector2(-h * 0.2f, h * 0.4f), h * 0.28f, new Color("e8b84a"));
                C.DrawLine(c + new Vector2(h * 0.06f, h * 0.4f), c + new Vector2(h * 0.06f, -h * 0.6f), new Color("e8b84a"), Mathf.Max(1, h * 0.14f));
                C.DrawLine(c + new Vector2(h * 0.06f, -h * 0.6f), c + new Vector2(h * 0.45f, -h * 0.3f), new Color("e8b84a"), Mathf.Max(1, h * 0.14f));
                break;
            case RacketKind.Counterfeit: // a green banknote
                C.DrawRect(new Rect2(c.X - h * 0.9f, c.Y - h * 0.5f, h * 1.8f, h * 1.0f), new Color("6aa46a"));
                C.DrawCircle(c, h * 0.3f, new Color("2a5a2a"));
                break;
        }
    }

    /// <summary>Planks nailed across a shut business's sign and shop window.</summary>
    private void DrawBoardedUp(Lot lot, float h)
    {
        var plank = new Color("8a7050");
        float lw = Mathf.Max(2, _t * 0.05f);
        var (_, _, _, y1) = Footprint(lot);
        float y = y1 - 0.12f;
        C.DrawLine(Q(lot.X + 0.26f, y, h + 0.08f), Q(lot.X + 0.74f, y, h + 0.3f), plank, lw);
        C.DrawLine(Q(lot.X + 0.74f, y, h + 0.08f), Q(lot.X + 0.26f, y, h + 0.3f), plank, lw);
        if (FacesSouth(lot))
            for (int i = 0; i < 3; i++)
                Face(lot.X + 0.12f, lot.X + 0.68f, y1, 0.03f + i * 0.04f, 0.055f + i * 0.04f, plank);
    }

    private void DrawHeadquarters(Lot lot, Color gang)
    {
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        DrawBlock(lot, h, new Color("3a3534"), new Color("2a2625"), 1);
        if (FacesSouth(lot))
        {
            Face(lot.X + 0.38f, lot.X + 0.62f, y1, 0, 0.15f, new Color("1a1716"));
            C.DrawLine(Q(lot.X + 0.38f, y1, 0.15f), Q(lot.X + 0.62f, y1, 0.15f), gang, Mathf.Max(1, _t * 0.03f));
        }
        // A neon sign in the gang's colour on the roof, brighter after dark; the letters go on screen.
        DrawBillboard(lot, h, gang, 4, true, () => { });
        var sign = SignCentre(lot, h);
        C.DrawRect(new Rect2(sign - new Vector2(0.22f, 0.11f) * _t, new Vector2(0.44f, 0.22f) * _t), new Color("1a1716"));
        Lit(() => Glow(sign, _t * 0.5f, gang.Lightened(0.3f), 1));
        // A pennant on a pole at the back of the roof.
        var pole = Q(x1 - 0.12f, y0 + 0.15f, h);
        C.DrawLine(pole, pole - new Vector2(0, _t * 0.45f), new Color("1a1716"), Mathf.Max(1, _t * 0.025f));
        var top = pole - new Vector2(0, _t * 0.45f);
        C.DrawColoredPolygon(new[] { top, top + new Vector2(_t * 0.24f, _t * 0.06f), top + new Vector2(0, _t * 0.13f) }, gang);
    }

    private void DrawPrecinct(Lot lot)
    {
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        var stone = new Color("8e8b86");
        DrawBlock(lot, h, stone, new Color("5a5854"), 1);
        if (FacesSouth(lot))
        {
            // Steps, columns and the blue lamps either side of the door.
            Box(lot.X + 0.25f, y1, lot.X + 0.75f, y1 + 0.06f, 0, 0.03f, stone.Lightened(0.1f), stone, stone.Lightened(0.2f));
            for (int i = 0; i < 4; i++)
            {
                float cx = lot.X + 0.22f + i * 0.18f;
                Face(cx, cx + 0.05f, y1, 0, 0.3f, stone.Lightened(0.25f));
            }
            Face(lot.X + 0.43f, lot.X + 0.57f, y1, 0, 0.15f, new Color("2a2622"));
            foreach (float lx in new[] { lot.X + 0.36f, lot.X + 0.64f })
            {
                C.DrawCircle(Q(lx, y1 + 0.02f, 0.2f), _t * 0.03f, Palette.Police);
                Lit(() => Glow(Q(lx, y1 + 0.02f, 0.2f), _t * 0.25f, Palette.Police.Lightened(0.3f), 1));
            }
        }
        DrawBillboard(lot, h, Palette.Police.Darkened(0.4f), 3, true, () => Star(SignCentre(lot, h), _t * 0.12f, new Color("d9b44a"), 6, 0.55f));
        var pole = Q(x0 + 0.15f, y0 + 0.15f, h);
        C.DrawLine(pole, pole - new Vector2(0, _t * 0.5f), new Color("1a1716"), Mathf.Max(1, _t * 0.025f));
        var top = pole - new Vector2(0, _t * 0.5f);
        C.DrawRect(new Rect2(top, new Vector2(0.26f, 0.15f) * _t), new Color("2a3a6a"));
        C.DrawRect(new Rect2(top + new Vector2(0, 0.075f * _t), new Vector2(0.26f, 0.025f) * _t), new Color("e9e6df"));
    }

    // ---- Lots nobody does business on ------------------------------------------

    private void DrawFiller(Lot lot)
    {
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        switch (FillerOf(lot))
        {
            case Filler.Tenement:
            {
                DrawBlock(lot, h, WallFor(lot), RoofFor(lot), 1);
                if (Fine)
                {
                    if (h >= 0.5f) DrawFireEscape(Hash(lot.Id, 4) < 0.5f ? x0 + 0.08f : x1 - 0.4f, y1, 0.2f, h);
                    if (Hash(lot.Id, 5) < 0.35f && h >= 0.5f) DrawWaterTower(x0 + 0.15f, y0 + 0.2f, h);
                    else DrawRoofClutter(lot, h, RoofFor(lot));
                }
                break;
            }
            case Filler.Office:
            {
                DrawBlock(lot, h, WallFor(lot), RoofFor(lot), 1);
                if (Fine && h > 1.6f)
                {
                    var top = Q(lot.X + 0.5f, (y0 + y1) / 2, h);
                    C.DrawLine(top, top - new Vector2(0, _t * 0.35f), new Color("3a3632"), Mathf.Max(1, _t * 0.02f));
                    Lit(() => C.DrawCircle(top - new Vector2(0, _t * 0.35f), _t * 0.03f, new Color("ff5040")));
                }
                break;
            }
            case Filler.Factory:
            {
                var wall = new Color("6a5a50");
                var roof = new Color("4a4442");
                Box(x0, y0, x1, y1, 0, h, wall, wall.Lightened(0.12f), roof);
                if (Fine)
                {
                    // Saw-tooth skylights along the roof.
                    for (float x = x0 + 0.04f; x < x1 - 0.15f; x += 0.2f)
                        C.DrawColoredPolygon(new[] { Q(x, y0, h), Q(x, y1, h), Q(x + 0.18f, y1, h + 0.1f), Q(x + 0.18f, y0, h + 0.1f) }, roof.Lightened(0.12f));
                    DrawWindows(lot, x0, x1, y1, 0.05f, h);
                }
                Face(lot.X + 0.35f, lot.X + 0.65f, y1, 0, 0.2f, new Color("3a2e26"));
                // The chimney stack, banded near the top.
                var brick = new Color("8a4a34");
                Box(x1 - 0.22f, y0 + 0.05f, x1 - 0.08f, y0 + 0.19f, 0, 1.25f, brick, brick.Lightened(0.12f), new Color("1a1716"));
                Face(x1 - 0.22f, x1 - 0.08f, y0 + 0.19f, 1.08f, 1.13f, new Color("d6ccb2"));
                break;
            }
            case Filler.Vacant:
                if (!Fine) break;
                // A broken fence along the street.
                var fence = new Color("5a4a38");
                float fy = FacesSouth(lot) ? lot.Y + 0.94f : lot.Y + 0.08f;
                for (float x = lot.X + 0.05f; x < lot.X + 0.95f; x += 0.11f)
                    if (Hash(lot.Id, (int)(x * 100)) > 0.25f) Face(x, x + 0.07f, fy, 0, 0.1f + Hash(lot.Id, (int)(x * 77)) * 0.05f, fence);
                break;
            case Filler.Parking:
                for (int i = 0; i < 3; i++)
                    if (Hash(lot.Id, 80 + i) > 0.45f)
                        DrawCar(new Vector2(lot.X + 0.25f + i * 0.25f, lot.Y + (FacesSouth(lot) ? 0.62f : 0.38f)), false, CarPaint[(int)(Hash(lot.Id, 90 + i) * CarPaint.Length)], 0, 0.8f);
                break;
            case Filler.PocketPark:
                foreach (var (tx, ty, size) in TreesOn(lot).OrderBy(t => t.Y)) DrawTree(tx, ty, size);
                if (Fine) Box(lot.X + 0.12f, lot.Y + 0.48f, lot.X + 0.36f, lot.Y + 0.53f, 0.04f, 0.07f, new Color("5a4632"), new Color("6a5642"), new Color("6a5642"));
                break;
        }
    }

    private void DrawParkLot(Lot lot)
    {
        foreach (var (tx, ty, size) in TreesOn(lot).OrderBy(t => t.Y)) DrawTree(tx, ty, size);
        // The fountain stands where the paths cross, in the middle of the block.
        int bx = (lot.X - 1) / CityMap.StrideX, by = (lot.Y - 1) / CityMap.StrideY;
        float mx = bx * CityMap.StrideX + 1 + CityMap.LotsPerBlockX / 2f, my = by * CityMap.StrideY + 1 + CityMap.LotsPerBlockY / 2f;
        if (lot.X == (int)mx && lot.Y == (int)my)
        {
            var stone = new Color("a39c8e");
            Box(mx - 0.12f, my - 0.08f, mx + 0.12f, my + 0.08f, 0, 0.12f, stone, stone.Lightened(0.1f), Water.Lightened(0.2f));
            C.DrawLine(Q(mx, my, 0.12f), Q(mx, my, 0.3f), new Color("cfe0ea"), Mathf.Max(1, _t * 0.03f));
            C.DrawCircle(Q(mx, my, 0.3f), _t * 0.05f, new Color("cfe0ea") with { A = 0.7f });
        }
    }

    // ---- Lamps ----------------------------------------------------------------

    private IEnumerable<Vector2> LampPosts()
    {
        for (int by = 0; by < Map.BlocksY; by++)
        for (int bx = 0; bx < Map.BlocksX; bx++)
        {
            if (by == Map.RiverRow) continue;
            float x0 = bx * CityMap.StrideX + 1 - 0.12f, x1 = x0 + CityMap.LotsPerBlockX + 0.24f;
            float y0 = by * CityMap.StrideY + 1 - 0.12f, y1 = y0 + CityMap.LotsPerBlockY + 0.24f;
            yield return new Vector2(x0, y0);
            yield return new Vector2(x1, y1);
            yield return new Vector2(x0 + CityMap.LotsPerBlockX / 2f + 0.12f, y0);
            yield return new Vector2(x0 + CityMap.LotsPerBlockX / 2f + 0.12f, y1);
        }
    }

    private void DrawLampPost(Vector2 l)
    {
        if (Fine) { DrawLantern(l); return; }
        C.DrawLine(Q(l.X, l.Y, 0), Q(l.X, l.Y, 0.42f), new Color("1e1d1c"), Mathf.Max(1, _t * 0.03f));
        C.DrawCircle(Q(l.X, l.Y, 0.44f), _t * 0.045f, new Color("8a8478"));
        Lit(() => C.DrawCircle(Q(l.X, l.Y, 0.44f), _t * 0.045f, Lamplight));
    }

    // ---- Street names and signs --------------------------------------------------

    private void DrawStreetNames(Font font, Rect2 view)
    {
        var w = World!;
        if (_t < 12) return;
        int size = Mathf.Max(9, (int)(_t * 0.26f));
        var ink = Palette.Ink with { A = 0.55f };
        for (int i = 0; i < w.Map.StreetNames.Length; i++)
        {
            float y = i * CityMap.StrideY + 0.5f;
            if (y < view.Position.Y || y > view.End.Y) continue;
            for (int bx = 0; bx < Map.BlocksX; bx += 2)
            {
                float x = bx * CityMap.StrideX + 1.3f;
                if (x < view.Position.X - 3 || x > view.End.X) continue;
                C.DrawString(font, P(x, y) + new Vector2(0, size * 0.35f), $"{w.Map.StreetNames[i].ToUpperInvariant()} ST", HorizontalAlignment.Left, -1, size, ink);
            }
        }
        for (int i = 0; i < w.Map.AvenueNames.Length; i++)
        {
            float x = i * CityMap.StrideX + 0.5f;
            if (x < view.Position.X || x > view.End.X) continue;
            for (int by = 1; by < Map.BlocksY; by += 2)
            {
                float y = by * CityMap.StrideY + CityMap.StrideY - 0.3f;
                if (y < view.Position.Y || y > view.End.Y + 3) continue;
                C.DrawSetTransform(P(x, y) + new Vector2(size * 0.35f, 0), -Mathf.Pi / 2);
                C.DrawString(font, Vector2.Zero, $"{w.Map.AvenueNames[i].ToUpperInvariant()} AVE", HorizontalAlignment.Left, -1, size, ink);
                C.DrawSetTransform(Vector2.Zero, 0);
            }
        }
    }

    /// <summary>The letters on each gang's neon sign, drawn on screen so they stay sharp at any zoom.</summary>
    private void DrawHeadquartersSigns(Font font, Rect2 view)
    {
        if (_t < 14) return;
        int size = Mathf.Max(8, (int)(_t * 0.2f));
        foreach (var lot in World!.Map.Lots)
        {
            if (lot.Use != LotUse.Headquarters || !view.HasPoint(new Vector2(lot.X, lot.Y))) continue;
            var centre = SignCentre(lot, HeightCached(lot));
            var colour = Palette.Gang(World, lot.GangId).Lightened(0.25f);
            C.DrawString(font, centre + new Vector2(-_t * 0.25f, size * 0.36f), "HQ", HorizontalAlignment.Center, _t * 0.5f, size, colour);
        }
    }
}
