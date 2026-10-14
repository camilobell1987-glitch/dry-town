using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// A row of shops in one building: two or three businesses side by side along the ground floor, each with its own
/// door, window, awning and sign, under one roof with flats above. Each can be protected, burnt or shut on its own,
/// and clicking picks whichever one is under the cursor.
/// </summary>
public partial class MapView
{
    /// <summary>The stretch of its building's front a shop in a row takes, west to east.</summary>
    private (float X0, float X1) UnitSpan(Lot lot, int unit, int units)
    {
        var (x0, _, x1, _) = Footprint(lot);
        return (x0 + (x1 - x0) * unit / units, x0 + (x1 - x0) * (unit + 1) / units);
    }

    /// <summary>The outline on screen, in tiles, of the slice of a building one shop in a row takes.</summary>
    private Vector2[] UnitSilhouette(Lot lot, int unit, int units)
    {
        var (_, y0, _, y1) = Footprint(lot);
        var (x0, x1) = UnitSpan(lot, unit, units);
        float h = HeightCached(lot);
        Vector2 a = Drawn(new Vector2(x0, y0)), b = Drawn(new Vector2(x1, y0)), c = Drawn(new Vector2(x1, y1)), d = Drawn(new Vector2(x0, y1));
        if (h <= 0 || !Angled) return new[] { a, b, c, d };
        var up = new Vector2(S * h, -h);
        return new[] { a + up, b + up, c + up, c, d, d + up };
    }

    /// <summary>The business in a building under a point on screen: the shop under the cursor in a row, or the only one.</summary>
    private int BusinessUnder(Lot lot, Vector2 pos)
    {
        if (lot.Shops.Count <= 1) return lot.BusinessId;
        var tile = (pos - Origin) / Tile;
        for (int i = 0; i < lot.Shops.Count; i++)
            if (Geometry2D.IsPointInPolygon(tile, UnitSilhouette(lot, i, lot.Shops.Count))) return lot.Shops[i];
        return lot.BusinessId;
    }

    /// <summary>A row of shops: one building, a shopfront and a roof sign for each business in it.</summary>
    private void DrawShopRow(Lot lot, IReadOnlyList<Business> shops)
    {
        var w = World!;
        if (shops.All(Burnt)) { DrawShell(lot, shops[0]); return; }
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        var wall = WallFor(lot);
        var roof = RoofFor(lot);
        if (!shops.Any(b => b.IsOpen || Live)) { wall = wall.Darkened(0.3f); roof = roof.Darkened(0.3f); }
        DrawBlock(lot, h, wall, roof, FacesSouth(lot) ? 0 : 1);
        if (Fine) DrawRoofClutter(lot, h, roof);
        if (h >= 0.55f && Hash(lot.Id, 660) < 0.4f) DrawFireEscape(Hash(lot.Id, 4) < 0.5f ? x0 + 0.08f : x1 - 0.4f, y1, 0.2f, h);

        int n = shops.Count;
        for (int i = 0; i < n; i++)
        {
            var b = shops[i];
            var (ux0, ux1) = UnitSpan(lot, i, n);
            float mid = (ux0 + ux1) / 2;
            var (protector, racket) = ShownState(b);
            Color? gang = protector >= 0 ? Palette.Gang(w, protector) : null;
            bool open = b.IsOpen || Live, burnt = Burnt(b);
            if (FacesSouth(lot))
            {
                if (burnt) DrawBurntFront(lot, ux0, ux1, h);
                else DrawShopfront(lot, b, gang, open, ux0, ux1, i + 1);
                // A brick pier between one shop and the next, up to the first-floor sills.
                if (i > 0) Face(ux0 - 0.012f, ux0 + 0.012f, y1, 0, 0.21f, wall.Darkened(0.28f));
            }
            var trade = b.Kind;
            float icon = _t * 0.11f * Mathf.Min(1, (ux1 - ux0) / 0.42f);
            DrawBillboard(lot, h, burnt ? new Color("2e2824") : gang ?? new Color("3a332b"), gang.HasValue ? 2.5f : 1, open && !burnt,
                () => { if (Fine && !burnt) DrawTradeIcon(trade, SignCentre(lot, h, mid), icon); }, ux0 + 0.04f, ux1 - 0.04f);
            if (racket != RacketKind.None && !burnt) DrawRacketBadge(Q(mid, (y0 + y1) / 2 - 0.1f, h), racket);
            if (b.IsFront) DrawFrontBadge(Q(mid, (y0 + y1) / 2 + 0.16f, h), Palette.Gang(w, b.OwnerGangId));
            if (!open && !burnt) DrawBoardedUnit(lot, h, ux0, ux1);
        }
        if (shops.Any(b => w.StrikeAt(b) != null)) DrawPickets(lot, h);
        if (shops.Any(w.IsWireRoom)) DrawWireMast(lot, h);
    }

    /// <summary>One burnt-out shop in a row: a black hole where the window was, and soot up the wall above it.</summary>
    private void DrawBurntFront(Lot lot, float x0, float x1, float h)
    {
        float y1 = Footprint(lot).Y1;
        Face(x0 + 0.02f, x1 - 0.02f, y1, 0, 0.17f, new Color("141110"));
        Face(x0 + 0.02f, x1 - 0.02f, y1, 0.17f, Mathf.Min(h, 0.45f), new Color("1a1614") with { A = 0.7f });
        if (Fine)
            for (int i = 0; i < 3; i++)
            {
                float x = x0 + 0.04f + (x1 - x0 - 0.08f) * Hash(lot.Id * 5 + i, 690);
                C.DrawLine(Q(x, y1, 0.17f), Q(x + 0.04f, y1, 0.0f), new Color("3a2a1e"), Mathf.Max(1, _t * 0.02f));
            }
    }

    /// <summary>A shop in a row the police have shut: its sign crossed out and planks over its front.</summary>
    private void DrawBoardedUnit(Lot lot, float h, float x0, float x1)
    {
        var plank = new Color("8a7050");
        float lw = Mathf.Max(2, _t * 0.04f);
        float y1 = Footprint(lot).Y1, y = y1 - 0.12f, a = x0 + 0.06f, b = x1 - 0.06f;
        C.DrawLine(Q(a, y, h + 0.08f), Q(b, y, h + 0.3f), plank, lw);
        C.DrawLine(Q(b, y, h + 0.08f), Q(a, y, h + 0.3f), plank, lw);
        if (FacesSouth(lot))
            for (int i = 0; i < 3; i++)
                Face(x0 + 0.04f, x1 - 0.04f, y1, 0.03f + i * 0.04f, 0.055f + i * 0.04f, plank);
    }

    /// <summary>Gang rims round each protected shop in a row, rather than round the whole building.</summary>
    private void DrawUnitRims(Lot lot)
    {
        var w = World!;
        for (int i = 0; i < lot.Shops.Count; i++)
        {
            var (protector, _) = ShownState(w.BusinessById(lot.Shops[i]));
            if (protector < 0) continue;
            var outline = UnitOutline(lot, i);
            C.DrawPolyline(outline.Append(outline[0]).ToArray(), Palette.Gang(w, protector) with { A = 0.9f }, Px(2));
        }
    }

    /// <summary>One shop's slice of a row, on screen, drawn just inside the slices beside it so rims don't overlap.</summary>
    private Vector2[] UnitOutline(Lot lot, int unit)
    {
        var poly = UnitSilhouette(lot, unit, lot.Shops.Count).Select(p => _o + p * _t).ToArray();
        var mid = poly.Aggregate(Vector2.Zero, (a, p) => a + p) / poly.Length;
        return poly.Select(p => p + (mid - p).Normalized() * Px(1.5f)).ToArray();
    }

    /// <summary>A shop's line in a row's tooltip: who else is in the building.</summary>
    private string RowLine(Lot lot, Business b) => lot.Shops.Count <= 1 ? ""
        : "\nShares the building with " + string.Join(" and ", lot.Shops.Where(id => id != b.Id).Select(id => "the " + Content.Label(World!.BusinessById(id).Kind).ToLowerInvariant()));
}
