using System;
using System.Collections.Generic;
using DryTown.Core;
using Godot;

/// <summary>
/// The motor traffic of the 1920s, each kind built from boxes the way the buildings are: Ford's Model T and the long
/// Packard sedan, roadsters with the hood folded, stake trucks loaded with crates or beer barrels, panel vans with the
/// trade on the side, the banks' armoured cars, the outfits' armoured Cadillacs, the police department's black
/// patrol cars and paddy wagons, and the Untouchables' raid truck with a steel ram on the front.
/// </summary>
public partial class MapView
{
    /// <summary>What's on the road.</summary>
    private enum Ride { ModelT, Packard, Roadster, Armoured, StakeTruck, BeerTruck, Van, BankCar, PoliceCar, PaddyWagon, RaidTruck }

    private static readonly Color Tyre = new("141414"), Chrome = new("c8c4b8"), Brass = new("b8962e"), Black = new("1a1a1c");
    private static readonly Color Canvas = new("8a8466"), Wood = new("6b5a42"), PoliceBlack = new("16181c"), WagonBlue = new("1c2333");
    private static readonly Color Olive = new("3d4433"), Steel = new("6a6e70"), Signwriting = new("d8ccaa");

    // The vehicle being drawn: its middle, whether it lies along a street, which way it faces, and its size.
    private Vector2 _vc;
    private bool _vh;
    private int _vd;
    private float _vs;

    private readonly record struct Part(float A0, float A1, float B0, float B1, float Z0, float Z1, Color Body, Color Top);
    private readonly List<Part> _parts = new();

    /// <summary>
    /// A vehicle on the ground at a point, along a street (<paramref name="horizontal"/>) or an avenue, its nose
    /// towards <paramref name="heading"/>. Its siren light flashes while the week runs if <paramref name="siren"/>.
    /// </summary>
    private void DrawVehicle(Vector2 centre, bool horizontal, int heading, Ride kind, Color paint, float night, float scale = 1, bool siren = false, int seed = 0)
    {
        // Out in the widened street it keeps its shape: placed by its middle, and drawn round that.
        var keep = _pin;
        _pin = centre;
        (_vc, _vh, _vd, _vs) = (centre, horizontal, heading >= 0 ? 1 : -1, scale);
        _parts.Clear();
        float len = Length(kind);
        float wide = Width(kind);
        // A shadow on the road, to the south-east.
        var (sx0, sy0, sx1, sy1) = Rect(-len, len, -wide, wide);
        Ellipse(P((sx0 + sx1) / 2 + 0.04f, (sy0 + sy1) / 2 + 0.03f), new Vector2((sx1 - sx0) * 0.56f, (sy1 - sy0) * 0.62f) * _t, Shadow with { A = 0.22f });
        if (!Angled) Wheels(kind, both: true);
        switch (kind)
        {
            case Ride.ModelT: ModelT(paint); break;
            case Ride.Packard: Packard(paint); break;
            case Ride.Roadster: Roadster(paint); break;
            case Ride.Armoured: Armoured(paint); break;
            case Ride.StakeTruck: case Ride.BeerTruck: StakeTruck(paint, kind == Ride.BeerTruck, seed); break;
            case Ride.Van: Van(paint, seed); break;
            case Ride.BankCar: BankCar(); break;
            case Ride.PoliceCar: PoliceCar(siren); break;
            case Ride.PaddyWagon: PaddyWagon(siren); break;
            case Ride.RaidTruck: RaidTruck(siren); break;
        }
        if (night > 0.3f) _headlights.Add((P(Ground(len + 0.28f, 0)), night));
        _pin = keep;
    }

    private static float Length(Ride kind) => kind switch
    {
        Ride.ModelT or Ride.Roadster => 0.235f,
        Ride.Packard => 0.29f,
        Ride.Armoured => 0.3f,
        Ride.StakeTruck or Ride.BeerTruck or Ride.PaddyWagon or Ride.BankCar => 0.32f,
        Ride.RaidTruck => 0.36f,
        Ride.PoliceCar => 0.27f,
        _ => 0.3f,
    };

    private static float Width(Ride kind) => kind switch
    {
        Ride.ModelT or Ride.Roadster => 0.1f,
        Ride.Armoured or Ride.RaidTruck or Ride.BankCar => 0.115f,
        _ => 0.107f,
    };

    /// <summary>For screenshots: every kind of vehicle lined up in the street outside a lot, both ways along and down the avenue.</summary>
    public Lot? Showroom { get; set; }

    private void DrawShowroom()
    {
        if (Showroom is not { } lot) return;
        var kinds = Enum.GetValues<Ride>();
        int street = lot.FrontY / CityMap.StrideY;
        float x0 = lot.X - 1.4f;
        for (int i = 0; i < kinds.Length; i++)
        {
            var kind = kinds[i];
            var paint = kind switch { Ride.PoliceCar => PoliceBlack, Ride.PaddyWagon => WagonBlue, Ride.RaidTruck => Olive, _ => RidePaint(kind, (i * 0.37f) % 1) };
            float x = x0 + (i % 6) * 0.7f;
            float y = Across(street, CityMap.StrideY, i < 6 ? NearLane : FarLane);
            C = DynAt(y);
            DrawVehicle(new Vector2(x, y), true, i < 6 ? -1 : 1, kind, paint, Night, 1, siren: true, seed: i);
        }
    }

    // ---- The vehicle's own frame ----------------------------------------------
    // a runs from the tail (negative) to the nose (positive); b across, positive on the side the viewer sees (south
    // on a street, west on an avenue); z up. All before scaling.

    private (float X0, float Y0, float X1, float Y1) Rect(float a0, float a1, float b0, float b1)
    {
        a0 *= _vs; a1 *= _vs; b0 *= _vs; b1 *= _vs;
        if (_vh)
        {
            float xa = _vc.X + _vd * a0, xb = _vc.X + _vd * a1;
            return (Math.Min(xa, xb), _vc.Y + b0, Math.Max(xa, xb), _vc.Y + b1);
        }
        float ya = _vc.Y + _vd * a0, yb = _vc.Y + _vd * a1;
        return (_vc.X - b1, Math.Min(ya, yb), _vc.X - b0, Math.Max(ya, yb));
    }

    /// <summary>A point of the vehicle on the ground, in tiles.</summary>
    private Vector2 Ground(float a, float b) =>
        _vh ? new Vector2(_vc.X + _vd * a * _vs, _vc.Y + b * _vs) : new Vector2(_vc.X - b * _vs, _vc.Y + _vd * a * _vs);

    /// <summary>A point of the vehicle as drawn.</summary>
    private Vector2 At(float a, float b, float z)
    {
        var g = Ground(a, b);
        return Q(g.X, g.Y, z * _vs);
    }

    /// <summary>Whether the nose is the end the viewer sees: the west end on a street, the south end on an avenue.</summary>
    private bool NoseShows => _vh ? _vd < 0 : _vd > 0;

    private void Add(float a0, float a1, float b0, float b1, float z0, float z1, Color body, Color? top = null) =>
        _parts.Add(new Part(a0, a1, b0, b1, z0, z1, body, top ?? body.Lightened(0.14f)));

    /// <summary>Draw the parts added, the far ones first so the near ones stand in front.</summary>
    private void Build()
    {
        _parts.Sort((p, q) => Depth(p).CompareTo(Depth(q)));
        foreach (var p in _parts)
        {
            var (x0, y0, x1, y1) = Rect(p.A0, p.A1, p.B0, p.B1);
            Box(x0, y0, x1, y1, p.Z0 * _vs, p.Z1 * _vs, p.Body, p.Body.Lightened(0.07f), p.Top);
        }
        _parts.Clear();
    }

    private float Depth(Part p)
    {
        var mid = Ground((p.A0 + p.A1) / 2, (p.B0 + p.B1) / 2);
        return mid.Y - mid.X + p.Z0 * 0.01f;
    }

    /// <summary>A patch on the side the viewer sees, between two points along the vehicle.</summary>
    private void Flank(float a0, float a1, float b, float z0, float z1, Color colour) =>
        C.DrawColoredPolygon(new[] { At(a0, b, z0), At(a1, b, z0), At(a1, b, z1), At(a0, b, z1) }, colour);

    /// <summary>A patch across one end, drawn only if that end faces the viewer.</summary>
    private void End(bool nose, float a, float b0, float b1, float z0, float z1, Color colour)
    {
        if (nose != NoseShows) return;
        C.DrawColoredPolygon(new[] { At(a, b0, z0), At(a, b1, z0), At(a, b1, z1), At(a, b0, z1) }, colour);
    }

    /// <summary>A patch lying flat on a top.</summary>
    private void Top(float a0, float a1, float b0, float b1, float z, Color colour) =>
        C.DrawColoredPolygon(new[] { At(a0, b0, z), At(a1, b0, z), At(a1, b1, z), At(a0, b1, z) }, colour);

    private void Lamp(float a, float b, float z, float r, Color colour) => C.DrawCircle(At(a, b, z), r * _vs * _t, colour);

    /// <summary>
    /// A wheel on the near side: a tyre, and spokes or a disc. Seen straight down, the tyres show as dark ends
    /// sticking out from under the body on both sides.
    /// </summary>
    private void Wheel(float a, float b, float r, bool spokes, bool whitewall = false)
    {
        if (!Angled)
        {
            var (x0, y0, x1, y1) = Rect(a - r, a + r, b - 0.02f, b + 0.012f);
            C.DrawRect(new Rect2(P(x0, y0), P(x1, y1) - P(x0, y0)), Tyre);
            return;
        }
        var hub = At(a, b + 0.004f, r);
        float px = r * _vs * _t;
        C.DrawCircle(hub, px, Tyre);
        if (whitewall && Fine) C.DrawCircle(hub, px * 0.78f, new Color("e6e1d4"));
        C.DrawCircle(hub, px * (whitewall ? 0.6f : 0.62f), spokes ? new Color("6a5a40") : Chrome.Darkened(0.2f));
        if (Fine && spokes)
            for (int k = 0; k < 6; k++)
            {
                var d = Vector2.Right.Rotated(Mathf.Pi * k / 6) * px * 0.6f;
                C.DrawLine(hub - d, hub + d, Tyre with { A = 0.7f }, Mathf.Max(1, px * 0.08f));
            }
        C.DrawCircle(hub, px * 0.2f, Chrome);
    }

    private void Wheels(Ride kind, bool both)
    {
        float w = Width(kind);
        foreach (float b in both ? new[] { -w + 0.01f, w - 0.01f } : new[] { w })
            switch (kind)
            {
                case Ride.ModelT: case Ride.Roadster: Wheel(-0.15f, b, 0.052f, true); Wheel(0.14f, b, 0.052f, true); break;
                case Ride.Packard: case Ride.PoliceCar: Wheel(-0.17f, b, 0.05f, true, kind == Ride.Packard); Wheel(0.17f, b, 0.05f, true, kind == Ride.Packard); break;
                case Ride.Armoured: Wheel(-0.18f, b, 0.052f, false, true); Wheel(0.18f, b, 0.052f, false, true); break;
                case Ride.RaidTruck: Wheel(-0.24f, b, 0.058f, false); Wheel(-0.12f, b, 0.058f, false); Wheel(0.22f, b, 0.058f, false); break;
                default: Wheel(-0.2f, b, 0.054f, false); Wheel(0.2f, b, 0.054f, false); break;
            }
    }

    /// <summary>The near wheels, drawn over the body's side in the angled view.</summary>
    private void NearWheels(Ride kind) { if (Angled) Wheels(kind, both: false); }

    /// <summary>A pair of headlamps on stalks either side of the radiator. The far one is hidden unless the nose faces the viewer.</summary>
    private void Headlamps(float a, float z, float b = 0.06f)
    {
        foreach (float side in Angled && !NoseShows ? new[] { b } : new[] { -b, b })
        {
            Lamp(a, side, z, 0.017f, Chrome);
            Lamp(a, side, z, 0.011f, new Color("f3e6b0"));
        }
    }

    /// <summary>
    /// What the roof shows from above: the windscreen at the front, the back window, and the side windows along its
    /// edges, so a car reads as a car seen straight down.
    /// </summary>
    private void Roof(float a0, float a1, float b, float z, Color glass)
    {
        if (!Fine) return;
        Top(a1 - 0.035f, a1 - 0.004f, -b + 0.012f, b - 0.012f, z, glass.Lightened(0.15f));
        Top(a0 + 0.004f, a0 + 0.022f, -b + 0.02f, b - 0.02f, z, glass);
        Top(a0 + 0.03f, a1 - 0.045f, b - 0.014f, b - 0.004f, z, glass.Darkened(0.15f));
        Top(a0 + 0.03f, a1 - 0.045f, -b + 0.004f, -b + 0.014f, z, glass.Darkened(0.15f));
    }

    /// <summary>The hood from above: the crease down its middle and the cap on the radiator.</summary>
    private void Hood(float a0, float a1, float z, Color paint)
    {
        if (!Fine) return;
        Top(a0 + 0.01f, a1, -0.003f, 0.003f, z, paint.Lightened(0.25f));
        Lamp(a1 + 0.012f, 0, z + 0.012f, 0.008f, Chrome);
    }

    /// <summary>Mudguards curving over the wheels, seen from above as dark arcs out at the sides.</summary>
    private void Mudguards(Ride kind, float z)
    {
        if (!Fine || Angled) return;
        float w = Width(kind);
        foreach (float a in WheelsAt(kind))
            foreach (float b in new[] { -w + 0.012f, w - 0.012f })
            {
                var c = At(a, b, z);
                var radii = new Vector2(0.075f, 0.02f) * _vs * _t;
                if (!_vh) radii = new Vector2(radii.Y, radii.X);
                Ellipse(c, radii, Black.Lightened(0.06f));
            }
    }

    private static float[] WheelsAt(Ride kind) => kind switch
    {
        Ride.ModelT or Ride.Roadster => new[] { -0.15f, 0.14f },
        Ride.Packard or Ride.PoliceCar => new[] { -0.17f, 0.17f },
        Ride.Armoured => new[] { -0.18f, 0.18f },
        Ride.RaidTruck => new[] { -0.24f, -0.12f, 0.22f },
        _ => new[] { -0.2f, 0.2f },
    };

    private void TailLamp(float a, float z) => Lamp(a, 0.07f, z, 0.01f, new Color("c03028"));

    /// <summary>A flashing red lamp on the roof while the week runs; a dull one otherwise.</summary>
    private void Beacon(float a, float z, bool siren)
    {
        bool on = siren && Live && (int)(_anim * 4) % 2 == 0;
        Lamp(a, 0, z, 0.026f, Chrome.Darkened(0.3f));
        Lamp(a, 0, z + 0.008f, 0.02f, on ? new Color("ff3a2a") : new Color("7a2420"));
        if (on && Fine) Glow(At(a, 0, z + 0.01f), _t * 0.12f * _vs, new Color("ff4a30"), 0.5f);
    }

    // ---- The models -------------------------------------------------------------

    /// <summary>Ford's Model T: short and tall, a square cabin, a little hood and big spoked wheels.</summary>
    private void ModelT(Color paint)
    {
        Add(-0.235f, 0.22f, -0.1f, 0.1f, 0.035f, 0.05f, Black);
        Add(-0.22f, 0.04f, -0.085f, 0.085f, 0.05f, 0.125f, paint);
        Add(-0.2f, 0.035f, -0.083f, 0.083f, 0.125f, 0.225f, paint, Black.Lightened(0.08f));
        Add(0.04f, 0.2f, -0.058f, 0.058f, 0.05f, 0.115f, paint);
        Add(0.2f, 0.225f, -0.062f, 0.062f, 0.045f, 0.13f, new Color("2a2a2a"), Brass);
        Build();
        Mudguards(Ride.ModelT, 0.05f);
        Roof(-0.2f, 0.035f, 0.083f, 0.225f, Glass);
        Hood(0.04f, 0.2f, 0.115f, paint);
        if (Fine)
        {
            Flank(-0.18f, -0.085f, 0.083f, 0.14f, 0.205f, Glass.Lightened(0.25f));
            Flank(-0.07f, 0.02f, 0.083f, 0.14f, 0.205f, Glass.Lightened(0.25f));
            End(true, 0.035f, -0.07f, 0.07f, 0.135f, 0.21f, Glass.Lightened(0.3f));
            End(false, -0.2f, -0.05f, 0.05f, 0.15f, 0.2f, Glass.Lightened(0.15f));
            Flank(-0.22f, 0.22f, 0.1f, 0.035f, 0.045f, Black.Lightened(0.15f));
        }
        Headlamps(0.21f, 0.11f, 0.065f);
        TailLamp(-0.225f, 0.09f);
        NearWheels(Ride.ModelT);
    }

    /// <summary>A Packard: a long hood behind a tall shield of a radiator, sidemount spare, chrome and whitewalls.</summary>
    private void Packard(Color paint)
    {
        Add(-0.28f, 0.27f, -0.105f, 0.105f, 0.035f, 0.058f, Black);
        Add(-0.27f, 0.04f, -0.09f, 0.09f, 0.058f, 0.125f, paint);
        Add(-0.23f, 0.03f, -0.088f, 0.088f, 0.125f, 0.21f, paint, paint.Darkened(0.35f));
        Add(0.04f, 0.255f, -0.066f, 0.066f, 0.058f, 0.13f, paint);
        Add(0.255f, 0.275f, -0.068f, 0.068f, 0.05f, 0.145f, Chrome.Darkened(0.1f), Chrome);
        Add(-0.29f, -0.27f, -0.07f, 0.07f, 0.065f, 0.115f, Black);
        Build();
        Mudguards(Ride.Packard, 0.058f);
        Roof(-0.23f, 0.03f, 0.088f, 0.21f, Glass);
        Hood(0.04f, 0.255f, 0.13f, paint);
        if (Fine)
        {
            for (int k = 0; k < 3; k++) Flank(-0.21f + k * 0.08f, -0.15f + k * 0.08f, 0.088f, 0.14f, 0.195f, Glass.Lightened(0.25f));
            End(true, 0.03f, -0.075f, 0.075f, 0.135f, 0.2f, Glass.Lightened(0.3f));
            // Louvres down the side of the hood, and a chrome belt line.
            for (float a = 0.1f; a < 0.23f; a += 0.025f) Flank(a, a + 0.008f, 0.066f, 0.075f, 0.115f, paint.Darkened(0.4f));
            Flank(-0.27f, 0.255f, 0.09f, 0.12f, 0.126f, Chrome);
            // The spare on the near front fender.
            if (Angled)
            {
                var spare = At(0.06f, 0.1f, 0.1f);
                C.DrawCircle(spare, 0.045f * _vs * _t, Tyre);
                C.DrawCircle(spare, 0.025f * _vs * _t, paint.Lightened(0.1f));
            }
            End(true, 0.28f, -0.105f, 0.105f, 0.04f, 0.055f, Chrome);
            End(false, -0.295f, -0.105f, 0.105f, 0.045f, 0.06f, Chrome);
        }
        Headlamps(0.26f, 0.12f, 0.075f);
        TailLamp(-0.285f, 0.1f);
        NearWheels(Ride.Packard);
    }

    /// <summary>A roadster with the top folded down behind the seat and a rumble seat lid.</summary>
    private void Roadster(Color paint)
    {
        Add(-0.235f, 0.22f, -0.1f, 0.1f, 0.035f, 0.05f, Black);
        Add(-0.22f, 0.04f, -0.085f, 0.085f, 0.05f, 0.125f, paint, new Color("3a2a20"));
        Add(0.04f, 0.21f, -0.06f, 0.06f, 0.05f, 0.12f, paint);
        Add(0.21f, 0.23f, -0.062f, 0.062f, 0.045f, 0.13f, Chrome.Darkened(0.15f), Chrome);
        Add(-0.215f, -0.16f, -0.08f, 0.08f, 0.125f, 0.15f, new Color("8a7a5a"));
        Add(0.025f, 0.035f, -0.075f, 0.075f, 0.125f, 0.18f, Glass.Lightened(0.3f), Chrome);
        Build();
        Mudguards(Ride.Roadster, 0.05f);
        Hood(0.04f, 0.21f, 0.12f, paint);
        if (Fine)
        {
            // The seat, and the lid of the rumble seat behind it.
            Top(-0.06f, 0.02f, -0.075f, 0.075f, 0.126f, new Color("5a3a2a"));
            Top(-0.155f, -0.07f, -0.075f, 0.075f, 0.126f, paint.Darkened(0.15f));
        }
        Headlamps(0.22f, 0.11f, 0.065f);
        TailLamp(-0.225f, 0.09f);
        NearWheels(Ride.Roadster);
    }

    /// <summary>An outfit's armoured Cadillac: long and heavy, steel plate, slits for windows, a heavy bumper.</summary>
    private void Armoured(Color paint)
    {
        Add(-0.3f, 0.29f, -0.115f, 0.115f, 0.035f, 0.062f, Black);
        Add(-0.29f, 0.05f, -0.1f, 0.1f, 0.062f, 0.135f, paint);
        Add(-0.25f, 0.04f, -0.1f, 0.1f, 0.135f, 0.22f, paint.Darkened(0.08f), paint.Lightened(0.05f));
        Add(0.05f, 0.27f, -0.075f, 0.075f, 0.062f, 0.14f, paint);
        Add(0.27f, 0.29f, -0.078f, 0.078f, 0.055f, 0.15f, Chrome.Darkened(0.1f), Chrome);
        Add(0.29f, 0.305f, -0.11f, 0.11f, 0.04f, 0.07f, Steel);
        Build();
        Mudguards(Ride.Armoured, 0.062f);
        Roof(-0.25f, 0.04f, 0.1f, 0.22f, Glass.Darkened(0.3f));
        Hood(0.05f, 0.27f, 0.14f, paint);
        if (Fine)
        {
            for (int k = 0; k < 3; k++) Flank(-0.22f + k * 0.085f, -0.165f + k * 0.085f, 0.1f, 0.18f, 0.198f, Glass.Darkened(0.2f));
            End(true, 0.04f, -0.08f, 0.08f, 0.18f, 0.2f, Glass);
            // Rivets along the plating.
            if (Angled) for (float a = -0.27f; a < 0.04f; a += 0.04f) C.DrawCircle(At(a, 0.1f, 0.1f), Mathf.Max(0.6f, 0.006f * _vs * _t), paint.Lightened(0.35f));
            for (float a = 0.29f; a > 0.06f; a -= 0.012f) End(true, a, -0.07f, 0.07f, 0.06f, 0.14f, Chrome.Darkened(0.4f));
        }
        Headlamps(0.28f, 0.125f, 0.08f);
        TailLamp(-0.295f, 0.1f);
        NearWheels(Ride.Armoured);
    }

    /// <summary>A stake-side truck, a Model TT or a Mack, carrying crates, or beer barrels with a tarpaulin pulled back.</summary>
    private void StakeTruck(Color paint, bool beer, int seed)
    {
        Add(-0.32f, 0.31f, -0.1f, 0.1f, 0.035f, 0.065f, Black);
        Add(-0.32f, 0.05f, -0.105f, 0.105f, 0.065f, 0.085f, Wood);
        Add(0.05f, 0.17f, -0.095f, 0.095f, 0.065f, 0.235f, paint, Black.Lightened(0.08f));
        Add(0.17f, 0.29f, -0.065f, 0.065f, 0.065f, 0.14f, paint);
        Add(0.29f, 0.31f, -0.068f, 0.068f, 0.06f, 0.15f, new Color("2a2a2a"), Brass);
        // The stake sides and the tailgate.
        Add(-0.32f, 0.05f, -0.105f, -0.093f, 0.085f, 0.16f, Wood.Lightened(0.08f));
        Add(-0.32f, -0.305f, -0.105f, 0.105f, 0.085f, 0.15f, Wood.Lightened(0.05f));
        Build();
        Mudguards(Ride.StakeTruck, 0.065f);
        Roof(0.05f, 0.17f, 0.095f, 0.235f, Glass);
        Hood(0.17f, 0.29f, 0.14f, paint);
        // The load.
        if (beer)
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 2; j++)
                {
                    float a = -0.27f + i * 0.08f, b = -0.045f + j * 0.09f;
                    C.DrawCircle(At(a, b, 0.15f), 0.04f * _vs * _t, new Color("5a3e24"));
                    C.DrawCircle(At(a, b, 0.152f), 0.032f * _vs * _t, new Color("7a5634"));
                    if (Fine) C.DrawArc(At(a, b, 0.152f), 0.04f * _vs * _t, 0, Mathf.Tau, 12, new Color("2a2a2a"), Mathf.Max(1, 0.006f * _vs * _t));
                }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                float a = -0.29f + i * 0.11f, h = 0.13f + Hash(seed, 800 + i) * 0.07f;
                Add(a, a + 0.1f, -0.085f, 0.085f, 0.085f, h, new Color("8a6a40"), new Color("a07c4c"));
            }
            Build();
        }
        Add(-0.32f, 0.05f, 0.093f, 0.105f, 0.085f, 0.16f, Wood.Lightened(0.08f));
        Build();
        if (Fine)
        {
            Flank(0.07f, 0.15f, 0.095f, 0.15f, 0.215f, Glass.Lightened(0.25f));
            End(true, 0.17f, -0.08f, 0.08f, 0.15f, 0.22f, Glass.Lightened(0.3f));
            for (float a = -0.3f; a < 0.05f; a += 0.06f) Flank(a, a + 0.012f, 0.105f, 0.085f, 0.16f, Wood.Darkened(0.3f));
        }
        Headlamps(0.3f, 0.12f, 0.07f);
        TailLamp(-0.32f, 0.08f);
        NearWheels(Ride.StakeTruck);
    }

    /// <summary>A panel delivery van, the trade painted down the side.</summary>
    private void Van(Color paint, int seed)
    {
        Add(-0.32f, 0.31f, -0.1f, 0.1f, 0.035f, 0.065f, Black);
        Add(-0.31f, 0.17f, -0.1f, 0.1f, 0.065f, 0.255f, paint, paint.Lightened(0.22f));
        Add(0.17f, 0.29f, -0.065f, 0.065f, 0.065f, 0.14f, paint);
        Add(0.29f, 0.31f, -0.068f, 0.068f, 0.06f, 0.15f, new Color("2a2a2a"), Brass);
        Build();
        Mudguards(Ride.Van, 0.065f);
        Hood(0.17f, 0.29f, 0.14f, paint);
        if (Fine) Top(0.13f, 0.165f, -0.085f, 0.085f, 0.255f, Glass.Lightened(0.15f));
        if (Fine)
        {
            var sign = seed % 3 == 0 ? Signwriting : seed % 3 == 1 ? new Color("c8b070") : new Color("e8e0cc");
            Flank(-0.27f, 0.05f, 0.1f, 0.13f, 0.215f, sign);
            Flank(-0.25f, 0.03f, 0.1f, 0.165f, 0.18f, paint.Darkened(0.2f));
            Flank(0.08f, 0.16f, 0.1f, 0.16f, 0.235f, Glass.Lightened(0.25f));
            End(true, 0.17f, -0.085f, 0.085f, 0.16f, 0.24f, Glass.Lightened(0.3f));
            End(false, -0.31f, -0.003f, 0.003f, 0.08f, 0.25f, paint.Darkened(0.4f));
        }
        Headlamps(0.3f, 0.12f, 0.07f);
        TailLamp(-0.315f, 0.1f);
        NearWheels(Ride.Van);
    }

    /// <summary>A bank's armoured car: a riveted steel box with gun ports and a vent on the roof.</summary>
    private void BankCar()
    {
        Add(-0.32f, 0.31f, -0.11f, 0.11f, 0.035f, 0.065f, Black);
        Add(-0.31f, 0.15f, -0.11f, 0.11f, 0.065f, 0.25f, Steel, Steel.Lightened(0.12f));
        Add(0.15f, 0.29f, -0.075f, 0.075f, 0.065f, 0.15f, Steel.Darkened(0.1f));
        Add(0.29f, 0.31f, -0.075f, 0.075f, 0.06f, 0.155f, new Color("2a2a2a"), Chrome);
        Add(-0.12f, -0.04f, -0.04f, 0.04f, 0.25f, 0.27f, Steel.Darkened(0.15f));
        Build();
        Mudguards(Ride.BankCar, 0.065f);
        Hood(0.15f, 0.29f, 0.15f, Steel);
        if (Fine)
        {
            if (Angled) for (float a = -0.28f; a < 0.13f; a += 0.05f) C.DrawCircle(At(a, 0.11f, 0.23f), Mathf.Max(0.6f, 0.006f * _vs * _t), Steel.Lightened(0.3f));
            if (Angled) for (float a = -0.28f; a < 0.13f; a += 0.05f) C.DrawCircle(At(a, 0.11f, 0.085f), Mathf.Max(0.6f, 0.006f * _vs * _t), Steel.Lightened(0.3f));
            Flank(-0.2f, -0.17f, 0.11f, 0.15f, 0.17f, Black);
            Flank(-0.05f, -0.02f, 0.11f, 0.15f, 0.17f, Black);
            Flank(0.06f, 0.14f, 0.11f, 0.19f, 0.225f, Glass.Darkened(0.25f));
            Flank(-0.3f, 0.14f, 0.11f, 0.105f, 0.12f, new Color("8a6a2a"));
        }
        Headlamps(0.3f, 0.12f, 0.075f);
        TailLamp(-0.315f, 0.1f);
        NearWheels(Ride.BankCar);
    }

    /// <summary>A police patrol car: a black touring sedan, white doors, a siren on the fender and a red lamp on the roof.</summary>
    private void PoliceCar(bool siren)
    {
        Add(-0.27f, 0.26f, -0.105f, 0.105f, 0.035f, 0.058f, Black);
        Add(-0.26f, 0.04f, -0.09f, 0.09f, 0.058f, 0.125f, PoliceBlack);
        Add(-0.22f, 0.03f, -0.088f, 0.088f, 0.125f, 0.205f, PoliceBlack, PoliceBlack.Lightened(0.1f));
        Add(0.04f, 0.245f, -0.064f, 0.064f, 0.058f, 0.128f, PoliceBlack);
        Add(0.245f, 0.265f, -0.066f, 0.066f, 0.05f, 0.14f, Chrome.Darkened(0.1f), Chrome);
        // The siren on the near fender.
        Add(0.12f, 0.17f, 0.07f, 0.1f, 0.058f, 0.095f, Chrome.Darkened(0.15f), Chrome);
        Build();
        Mudguards(Ride.PoliceCar, 0.058f);
        Roof(-0.22f, 0.03f, 0.088f, 0.205f, Glass);
        Hood(0.04f, 0.245f, 0.128f, PoliceBlack);
        if (Fine)
        {
            Flank(-0.17f, -0.02f, 0.09f, 0.062f, 0.122f, new Color("e9e6df"));
            if (Angled) C.DrawCircle(At(-0.095f, 0.09f, 0.092f), 0.018f * _vs * _t, Brass);
            for (int k = 0; k < 3; k++) Flank(-0.2f + k * 0.075f, -0.14f + k * 0.075f, 0.088f, 0.14f, 0.19f, Glass.Lightened(0.25f));
            End(true, 0.03f, -0.075f, 0.075f, 0.135f, 0.195f, Glass.Lightened(0.3f));
        }
        Beacon(-0.03f, 0.205f, siren);
        Headlamps(0.255f, 0.12f, 0.072f);
        TailLamp(-0.27f, 0.1f);
        NearWheels(Ride.PoliceCar);
    }

    /// <summary>A police paddy wagon: a tall closed van, barred windows at the back, a bell and a red lamp on the roof.</summary>
    private void PaddyWagon(bool siren)
    {
        Add(-0.33f, 0.31f, -0.105f, 0.105f, 0.035f, 0.065f, Black);
        Add(-0.32f, 0.14f, -0.105f, 0.105f, 0.065f, 0.27f, WagonBlue, WagonBlue.Lightened(0.15f));
        Add(0.14f, 0.29f, -0.066f, 0.066f, 0.065f, 0.145f, WagonBlue);
        Add(0.29f, 0.31f, -0.068f, 0.068f, 0.06f, 0.155f, new Color("2a2a2a"), Chrome);
        Add(-0.345f, -0.32f, -0.08f, 0.08f, 0.04f, 0.06f, Chrome.Darkened(0.3f));
        Build();
        Mudguards(Ride.PaddyWagon, 0.065f);
        Hood(0.14f, 0.29f, 0.145f, WagonBlue);
        if (Fine) Top(0.105f, 0.135f, -0.085f, 0.085f, 0.27f, Glass.Lightened(0.15f));
        if (Fine)
        {
            Flank(-0.3f, 0.12f, 0.105f, 0.15f, 0.165f, new Color("e9e6df"));
            // Barred windows down the back, the cab window at the front.
            for (int k = 0; k < 3; k++)
            {
                float a = -0.27f + k * 0.11f;
                Flank(a, a + 0.07f, 0.105f, 0.185f, 0.24f, Black);
                for (float x = a + 0.012f; x < a + 0.07f; x += 0.016f) Flank(x, x + 0.004f, 0.105f, 0.185f, 0.24f, Chrome.Darkened(0.2f));
            }
            Flank(0.06f, 0.13f, 0.105f, 0.18f, 0.25f, Glass.Lightened(0.25f));
            End(true, 0.14f, -0.085f, 0.085f, 0.18f, 0.255f, Glass.Lightened(0.3f));
            C.DrawCircle(At(0.06f, 0, 0.275f), 0.022f * _vs * _t, Brass);
        }
        Beacon(0.1f, 0.27f, siren);
        Headlamps(0.3f, 0.12f, 0.07f);
        TailLamp(-0.33f, 0.1f);
        NearWheels(Ride.PaddyWagon);
    }

    /// <summary>The Untouchables' raid truck: a heavy flatbed with a canvas tilt over the agents and a steel ram for doors.</summary>
    private void RaidTruck(bool siren)
    {
        Add(-0.36f, 0.33f, -0.11f, 0.11f, 0.035f, 0.07f, Black);
        Add(-0.36f, 0.04f, -0.115f, 0.115f, 0.07f, 0.24f, Canvas, Canvas.Lightened(0.12f));
        Add(0.04f, 0.17f, -0.105f, 0.105f, 0.07f, 0.25f, Olive, Olive.Darkened(0.2f));
        Add(0.17f, 0.31f, -0.075f, 0.075f, 0.07f, 0.15f, Olive);
        Add(0.31f, 0.33f, -0.076f, 0.076f, 0.065f, 0.16f, new Color("2a2a2a"), Steel);
        // The ram: a steel beam on two struts, out ahead of the radiator.
        Add(0.33f, 0.37f, -0.07f, -0.04f, 0.06f, 0.09f, Steel.Darkened(0.2f));
        Add(0.33f, 0.37f, 0.04f, 0.07f, 0.06f, 0.09f, Steel.Darkened(0.2f));
        Add(0.37f, 0.4f, -0.115f, 0.115f, 0.04f, 0.13f, Steel, Steel.Lightened(0.2f));
        Build();
        Mudguards(Ride.RaidTruck, 0.07f);
        Roof(0.04f, 0.17f, 0.105f, 0.25f, Glass);
        Hood(0.17f, 0.31f, 0.15f, Olive);
        if (Fine)
        {
            // The hoops under the canvas, and the Bureau's star on the side.
            for (float a = -0.33f; a < 0.04f; a += 0.07f) Top(a, a + 0.008f, -0.115f, 0.115f, 0.241f, Canvas.Darkened(0.25f));
            for (float a = -0.33f; a < 0.04f; a += 0.07f) Flank(a, a + 0.008f, 0.115f, 0.07f, 0.24f, Canvas.Darkened(0.25f));
            // Seen straight down, the star is painted on the canvas roof instead.
            var star = Angled ? At(-0.16f, 0.115f, 0.155f) : At(-0.16f, 0, 0.24f);
            float r = 0.04f * _vs * _t;
            var pts = new Vector2[10];
            for (int k = 0; k < 10; k++) pts[k] = star + Vector2.Up.Rotated(Mathf.Pi * k / 5) * (k % 2 == 0 ? r : r * 0.42f);
            C.DrawColoredPolygon(pts, new Color("ece6d6"));
            Flank(0.06f, 0.15f, 0.105f, 0.17f, 0.235f, Glass.Darkened(0.1f));
            End(true, 0.17f, -0.09f, 0.09f, 0.17f, 0.24f, Glass.Lightened(0.2f));
        }
        Beacon(0.1f, 0.25f, siren);
        Headlamps(0.32f, 0.125f, 0.075f);
        TailLamp(-0.36f, 0.1f);
        NearWheels(Ride.RaidTruck);
    }

    /// <summary>Something ordinary for the traffic and the kerbs: mostly cars, some trucks and vans, the odd patrol.</summary>
    private static Ride TrafficRide(float roll, bool parked) => roll switch
    {
        < 0.26f => Ride.ModelT,
        < 0.44f => Ride.Packard,
        < 0.54f => Ride.Roadster,
        < 0.66f => Ride.StakeTruck,
        < 0.72f => Ride.BeerTruck,
        < 0.86f => Ride.Van,
        < 0.89f => Ride.BankCar,
        < 0.96f => parked ? Ride.ModelT : Ride.PoliceCar,
        _ => parked ? Ride.Packard : Ride.PaddyWagon,
    };

    /// <summary>A paint for a car, truck or van: Ford black more often than not.</summary>
    private static Color RidePaint(Ride kind, float roll) => kind switch
    {
        Ride.ModelT => roll < 0.75f ? Black : CarPaint[(int)(roll * CarPaint.Length)],
        Ride.Van => new[] { new Color("2c3b2f"), new Color("4a2427"), new Color("2a3346"), new Color("54493a"), new Color("6a5a2a") }[(int)(roll * 5)],
        Ride.StakeTruck or Ride.BeerTruck => new[] { new Color("2c3b2f"), new Color("3a3a3c"), new Color("4a2427"), new Color("2a3346") }[(int)(roll * 4)],
        _ => CarPaint[(int)(roll * CarPaint.Length)],
    };
}
