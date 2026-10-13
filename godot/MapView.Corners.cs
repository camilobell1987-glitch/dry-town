using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// A small gang's corner: an ordinary tenement with its colours hung out front, a lamp over the stoop,
/// and for the loan sharks a sign that says what they do.
/// </summary>
public partial class MapView
{
    /// <summary>The small gang whose corner a lot is, if it is one.</summary>
    private Gang? CornerOf(Lot lot) =>
        lot.Use == LotUse.Headquarters && World != null && lot.GangId >= 0 && World.GangById(lot.GangId) is { IsSmall: true } g ? g : null;

    private void DrawCorner(Lot lot, Gang gang, Color colour)
    {
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        var wall = WallFor(lot);
        var roof = RoofFor(lot);
        DrawBlock(lot, h, wall, roof, FacesSouth(lot) ? 0 : 1);
        if (Fine) DrawRoofClutter(lot, h, roof);

        if (FacesSouth(lot))
        {
            // A stoop up to a dark door, a lamp over it, and the gang's colours hung from the first floor.
            Box(lot.X + 0.36f, y1, lot.X + 0.64f, y1 + 0.07f, 0, 0.03f, new Color("6a625a"), new Color("5a534c"), new Color("7a726a"));
            Face(lot.X + 0.42f, lot.X + 0.58f, y1, 0.03f, 0.17f, new Color("1a1716"));
            var lamp = Q(lot.X + 0.5f, y1 + 0.01f, 0.2f);
            C.DrawCircle(lamp, Mathf.Max(1, _t * 0.025f), new Color("f0d890"));
            Lit(() => Glow(lamp, _t * 0.3f, new Color("f0c870"), 1));
            float top = Mathf.Min(h - 0.04f, 0.42f);
            foreach (float bx in new[] { lot.X + 0.18f, lot.X + 0.7f })
            {
                Face(bx, bx + 0.12f, y1, top - 0.2f, top, colour);
                // A notch cut out of the bottom, the way a pennant hangs.
                C.DrawColoredPolygon(new[] { Q(bx + 0.03f, y1, top - 0.2f), Q(bx + 0.06f, y1, top - 0.15f), Q(bx + 0.09f, y1, top - 0.2f) }, wall);
            }
        }
        else
        {
            // From the street behind, a flag on a pole at the back of the roof.
            var pole = Q(x0 + 0.2f, y1 - 0.15f, h);
            C.DrawLine(pole, pole - new Vector2(0, _t * 0.35f), new Color("1a1716"), Mathf.Max(1, _t * 0.025f));
            var tip = pole - new Vector2(0, _t * 0.35f);
            C.DrawColoredPolygon(new[] { tip, tip + new Vector2(_t * 0.2f, _t * 0.05f), tip + new Vector2(0, _t * 0.11f) }, colour);
        }

        // The sharks put up a sign: they want to be found by anyone short before payday.
        if (gang.Kind == GangKind.Sharks)
            DrawBillboard(lot, h, colour, 2, true, () => { });
    }

    /// <summary>The loan office's sign, lettered on screen once the map is close enough to read it.</summary>
    private void DrawCornerSigns(Font font, Rect2 view)
    {
        if (_t < 14) return;
        int size = Mathf.Max(7, (int)(_t * 0.15f));
        foreach (var lot in World!.Map.Lots)
        {
            if (lot.Use != LotUse.Headquarters || !view.HasPoint(new Vector2(lot.X, lot.Y)) || CornerOf(lot) is not { Kind: GangKind.Sharks } g) continue;
            var centre = SignCentre(lot, HeightCached(lot));
            C.DrawString(font, centre + new Vector2(-_t * 0.25f, size * 0.36f), "LOANS", HorizontalAlignment.Center, _t * 0.5f, size, Palette.Gang(World, g.Id).Darkened(0.35f));
        }
    }

    /// <summary>What the tooltip says about a small gang's corner.</summary>
    private string CornerTip(Gang g)
    {
        var w = World!;
        int men = w.HoodsOf(g.Id).Count(), shops = w.TurfOf(g.Id).Count();
        return $"{g.Name}'s corner\n{Capital(Content.Label(g.Kind))}, run by {w.HoodById(g.BossHoodId).Name}\n{men} men · {shops} shop{(shops == 1 ? "" : "s")} · {w.BlocksOf(g).Count} of the {Content.MaxBlocks(g.Kind)} block{(Content.MaxBlocks(g.Kind) == 1 ? "" : "s")} they can hold\nClick to deal with them";
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
