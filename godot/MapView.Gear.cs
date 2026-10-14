using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// Guns and cars on the map: the sporting-goods stores and the motor dealers that sell them,
/// and the gang cars that carry men to a job when one of them drives.
/// </summary>
public partial class MapView
{
    public event Action<int>? ShopClicked;

    /// <summary>The lot of the gun shop or car dealer that's selected, or -1.</summary>
    public int SelectedShop { get; private set; } = -1;

    // ---- The gun shop -----------------------------------------------------------

    /// <summary>A narrow brick store with rifles racked in the window and a striped awning.</summary>
    private void DrawGunShop(Lot lot)
    {
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        var brick = new Color("7a3e2c");
        var roof = new Color("3e3430");
        DrawBlock(lot, h, brick, roof, FacesSouth(lot) ? 0 : 1);
        if (FacesSouth(lot))
        {
            // A dark window with rifles stood up in it, a heavy door, and a green-and-white awning.
            var window = new Color("2a2c2a");
            Face(x0 + 0.08f, x1 - 0.32f, y1, 0.02f, 0.16f, window);
            if (Fine)
                for (float x = x0 + 0.12f; x < x1 - 0.36f; x += 0.07f)
                    C.DrawLine(Q(x, y1, 0.03f), Q(x + 0.02f, y1, 0.15f), new Color("8a6a44"), Mathf.Max(1, _t * 0.015f));
            Face(x1 - 0.26f, x1 - 0.1f, y1, 0, 0.16f, new Color("2a1e16"));
            Lit(() => Face(x0 + 0.08f, x1 - 0.32f, y1, 0.02f, 0.16f, WindowLit with { A = 0.5f }));
            int stripes = Fine ? 6 : 1;
            for (int i = 0; i < stripes; i++)
            {
                float a = x0 + 0.04f + (x1 - x0 - 0.08f) * i / stripes, z = x0 + 0.04f + (x1 - x0 - 0.08f) * (i + 1) / stripes;
                var c = i % 2 == 0 || !Fine ? new Color("2f5a3a") : new Color("e9e0c8");
                C.DrawColoredPolygon(new[] { Q(a, y1, 0.2f), Q(z, y1, 0.2f), Q(z, y1 + 0.12f, 0.15f), Q(a, y1 + 0.12f, 0.15f) }, c);
            }
        }
        DrawBillboard(lot, h, new Color("2f5a3a"), 3, true, () => { if (Fine) DrawRifles(SignCentre(lot, h), _t * 0.11f); });
    }

    /// <summary>Two rifles crossed, for the gun shop's sign and the side panel.</summary>
    private void DrawRifles(Vector2 c, float s)
    {
        var steel = new Color("2a2622");
        var stock = new Color("7a4a26");
        float lw = Mathf.Max(1, s * 0.22f);
        foreach (float dir in new[] { 1f, -1f })
        {
            var muzzle = c + new Vector2(dir * s * 0.95f, -s * 0.75f);
            var grip = c + new Vector2(-dir * s * 0.55f, s * 0.45f);
            C.DrawLine(muzzle, grip, steel, lw);
            C.DrawLine(grip, grip + new Vector2(-dir * s * 0.4f, s * 0.35f), stock, lw * 1.8f);
        }
    }

    // ---- The car dealer -----------------------------------------------------------

    /// <summary>
    /// A low glass showroom, new cars lined up on the forecourt, and pennants strung over them. On a
    /// lot facing the viewer the forecourt is in front; on one facing away it runs down the east side,
    /// where the cars can still be seen.
    /// </summary>
    private void DrawCarDealer(Lot lot)
    {
        float h = HeightCached(lot);
        bool south = FacesSouth(lot);
        float sx0 = lot.X + 0.07f, sx1 = south ? lot.X + 0.93f : lot.X + 0.5f;
        float sy0 = lot.Y + 0.1f, sy1 = south ? lot.Y + 0.52f : lot.Y + 0.9f;
        var wall = new Color("d8ceb4");
        var roof = new Color("7a7266");
        // The forecourt the cars stand on.
        var (fx0, fy0, fx1, fy1) = south ? (sx0, sy1, sx1, lot.Y + 0.95f) : (sx1 + 0.03f, lot.Y + 0.05f, lot.X + 0.95f, lot.Y + 0.92f);
        C.DrawColoredPolygon(new[] { Q(fx0, fy0, 0), Q(fx1, fy0, 0), Q(fx1, fy1, 0), Q(fx0, fy1, 0) }, new Color("8a8478"));
        Box(sx0, sy0, sx1, sy1, 0, h, wall, wall.Lightened(0.08f), roof);
        // Plate glass right across the front, lit at night.
        Face(sx0 + 0.04f, sx1 - 0.04f, sy1, 0.03f, h - 0.06f, Glass.Lightened(0.15f));
        Lit(() => Face(sx0 + 0.04f, sx1 - 0.04f, sy1, 0.03f, h - 0.06f, WindowLit with { A = 0.6f }));
        if (Fine)
            for (float x = sx0 + 0.2f; x < sx1 - 0.1f; x += 0.2f)
                C.DrawLine(Q(x, sy1, 0.03f), Q(x, sy1, h - 0.06f), wall.Darkened(0.25f), 1);
        Face(sx0, sx1, sy1, h - 0.06f, h, new Color("8c2f2a"));

        // Cars for sale, noses to the street.
        if (south)
            for (int i = 0; i < 3; i++)
                DrawVehicle(new Vector2(lot.X + 0.22f + i * 0.28f, (fy0 + fy1) / 2), false, 1, ForSale(lot, i), CarPaint[(lot.Id + i * 3) % CarPaint.Length], 0, 0.75f);
        else
            for (int i = 0; i < 2; i++)
                DrawVehicle(new Vector2((fx0 + fx1) / 2, lot.Y + 0.3f + i * 0.4f), false, -1, ForSale(lot, i), CarPaint[(lot.Id + i * 3) % CarPaint.Length], 0, 0.75f);

        if (Fine)
        {
            // Pennants strung from poles along the street edge of the forecourt.
            float py = south ? fy1 - 0.04f : fy0 + 0.04f;
            var pole = new Color("3a3632");
            C.DrawLine(Q(fx0 + 0.02f, py, 0), Q(fx0 + 0.02f, py, 0.32f), pole, Mathf.Max(1, _t * 0.015f));
            C.DrawLine(Q(fx1 - 0.02f, py, 0), Q(fx1 - 0.02f, py, 0.32f), pole, Mathf.Max(1, _t * 0.015f));
            Color[] flags = { new("c8463c"), new("e8b84a"), new("3a5fb0"), new("e9e0c8") };
            int n = south ? 8 : 4;
            for (int i = 0; i < n; i++)
            {
                float a = fx0 + 0.02f + (fx1 - fx0 - 0.04f) * i / n, b = fx0 + 0.02f + (fx1 - fx0 - 0.04f) * (i + 1) / n;
                float sag = 0.06f * Mathf.Sin(Mathf.Pi * (i + 0.5f) / n);
                C.DrawColoredPolygon(new[] { Q(a, py, 0.32f - sag), Q(b, py, 0.32f - sag), Q((a + b) / 2, py, 0.26f - sag) }, flags[i % flags.Length]);
            }
        }

        // A sign on the showroom roof with a car on it.
        float mid = (sx0 + sx1) / 2, sy = sy1 - 0.1f, half = Mathf.Min(0.26f, (sx1 - sx0) / 2 - 0.02f);
        Face(mid - half, mid + half, sy, h + 0.02f, h + 0.3f, new Color("8c2f2a"));
        Face(mid - half + 0.03f, mid + half - 0.03f, sy, h + 0.05f, h + 0.27f, new Color("e6dcc2"));
        if (Fine) DrawCarIcon(Q(mid, sy, h + 0.16f), _t * 0.09f, new Color("2a2622"));
    }

    /// <summary>A car side-on: the dealer's sign, and the rolodex card of a man who drives.</summary>
    private void DrawCarIcon(Vector2 c, float s, Color ink)
    {
        C.DrawRect(new Rect2(c.X - s * 1.0f, c.Y - s * 0.05f, s * 2.0f, s * 0.45f), ink);
        C.DrawColoredPolygon(new[] { c + new Vector2(-s * 0.55f, -s * 0.05f), c + new Vector2(-s * 0.35f, -s * 0.5f), c + new Vector2(s * 0.35f, -s * 0.5f), c + new Vector2(s * 0.55f, -s * 0.05f) }, ink);
        C.DrawCircle(c + new Vector2(-s * 0.55f, s * 0.42f), s * 0.22f, ink);
        C.DrawCircle(c + new Vector2(s * 0.55f, s * 0.42f), s * 0.22f, ink);
    }

    private string ShopTooltip(Lot lot) => lot.Use switch
    {
        LotUse.GunShop => $"{World.ShopName(World!.Map, lot)}\nRevolvers, shotguns, and a Thompson under the counter.{WardLine(lot)}",
        LotUse.CarDealer => $"{World.ShopName(World!.Map, lot)}\nFords, Packards, and an armoured Cadillac for the right customer.{WardLine(lot)}",
        _ => "",
    };

    // ---- Gang cars on the road --------------------------------------------------

    /// <summary>The car the men on a job ride in, if any of them drives.</summary>
    private Car CarFor(IEnumerable<int> team) => Simulation.BestCar(Men(team));

    /// <summary>The men behind a list of ids, skipping the empty slot a street action has where a man would be.</summary>
    private IEnumerable<Hood> Men(IEnumerable<int> ids) => ids.Where(id => id >= 0 && id < World!.Hoods.Count).Select(World!.HoodById);

    /// <summary>A gang's car on its way to or from a job, keeping to the right-hand lane.</summary>
    private void DrawGangCar(Vector2 at, Vector2 ahead, Color gang, Car car)
    {
        var paint = car == Car.Cadillac ? new Color("1c1a1c") : gang.Darkened(0.35f);
        var ride = car switch { Car.ModelT => Ride.ModelT, Car.Packard => Ride.Packard, _ => Ride.Armoured };
        var spot = DrawOnTheRoad(at, ahead, ride, paint, false);
        // A band of the gang's colour on the roof, so you can tell whose it is.
        if (car == Car.Cadillac)
        {
            var keep = C;
            C = DynAt(spot.Y);
            C.DrawCircle(Q(spot.X, spot.Y, 0.225f), _t * 0.04f, gang);
            C = keep;
        }
    }

    /// <summary>A vehicle driving along a path, in the right-hand lane. Returns where it was drawn.</summary>
    private Vector2 DrawOnTheRoad(Vector2 at, Vector2 ahead, Ride ride, Color paint, bool siren)
    {
        bool horizontal = Mathf.Abs(ahead.X) >= Mathf.Abs(ahead.Y);
        int heading = horizontal ? (ahead.X >= 0 ? 1 : -1) : (ahead.Y >= 0 ? 1 : -1);
        float off = Roadway * (FarLane - 0.5f);
        var lane = horizontal ? new Vector2(0, off * heading) : new Vector2(-off * heading, 0);
        var keep = C;
        C = DynAt(at.Y);
        DrawVehicle(at + lane, horizontal, heading, ride, paint, Night, siren: siren);
        C = keep;
        return at + lane;
    }

    /// <summary>A vehicle pulled up in the bay at the kerb outside a lot.</summary>
    private void DrawAtKerb(Lot lot, Ride ride, Color paint, bool siren)
    {
        float y = Across(lot.FrontY / CityMap.StrideY, CityMap.StrideY, lot.FrontY < lot.Y ? FarBay : NearBay);
        var keep = C;
        C = DynAt(y);
        DrawVehicle(new Vector2(lot.X + 0.5f, y), true, lot.FrontY < lot.Y ? 1 : -1, ride, paint, Night, siren: siren);
        C = keep;
    }

    /// <summary>One of the motor dealer's cars for sale.</summary>
    private static Ride ForSale(Lot lot, int i) => ((lot.Id + i) % 3) switch { 0 => Ride.ModelT, 1 => Ride.Packard, _ => Ride.Roadster };
}
