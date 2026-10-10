using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The people on the street who can be clicked: drifters, beat cops, reporters and witnesses,
/// and each gang's men hanging about outside headquarters. Also the banks, and businesses that
/// have been burned out.
/// </summary>
public partial class MapView
{
    public event Action<int>? PersonClicked;
    public event Action<int>? HoodClicked;
    public event Action<int>? BankClicked;

    public int SelectedPerson { get; private set; } = -1;
    public int SelectedHood { get; private set; } = -1;
    public int SelectedBank { get; private set; } = -1;

    /// <summary>Select one thing on the map, and nothing else.</summary>
    public void Select(int business = -1, int person = -1, int hood = -1, int bank = -1)
    {
        SelectedBusiness = business;
        SelectedPerson = person;
        SelectedHood = hood;
        SelectedBank = bank;
    }

    private enum Pickable { Person, Hood }

    /// <summary>Someone drawn this frame who can be clicked: where his feet are, in tiles, and how big he's drawn.</summary>
    private readonly record struct Clickable(Pickable Kind, int Id, Vector2 Feet, float Radius);

    private readonly List<Clickable> _clickables = new();

    /// <summary>Businesses that caught fire during the live week, as the replay shows it.</summary>
    private readonly HashSet<int> _burntNow = new();

    /// <summary>Whether a business stands as a burned-out shell as the map shows it now.</summary>
    private bool Burnt(Business b) =>
        _burntNow.Contains(b.Id) || (!b.IsOpen && b.BurnedWeek >= 0 && b.BurnedWeek < World!.Week && World.Week - b.BurnedWeek <= 8);

    private static Color CoatOf(PersonKind kind) => kind switch
    {
        PersonKind.BeatCop => new Color("24365e"),
        PersonKind.Reporter => new Color("a08c6a"),
        PersonKind.Witness => new Color("8a8276"),
        _ => new Color("6b5a45"),
    };

    /// <summary>The colour of the marker over someone's head, which says what he is from across the city.</summary>
    public static Color MarkerOf(PersonKind kind) => kind switch
    {
        PersonKind.BeatCop => new Color("5b8ad9"),
        PersonKind.Reporter => new Color("f0ead8"),
        PersonKind.Witness => new Color("e0584a"),
        _ => new Color("e0b04a"),
    };

    /// <summary>Where someone stands outside a lot: on the pavement, spread along it so two don't stand in one spot.</summary>
    private Vector2 StandingSpot(Lot lot, int salt, float pace)
    {
        float along = (Hash(salt, 710) - 0.5f) * 0.7f + pace;
        return Kerbside(lot) + new Vector2(along, 0);
    }

    /// <summary>
    /// Where someone waiting outside a lot can be seen. In front of a door facing the viewer that's
    /// the pavement; a door facing away is hidden by its own building, so he waits across the street.
    /// </summary>
    private static Vector2 Kerbside(Lot lot) =>
        lot.FrontY < lot.Y ? new Vector2(lot.X + 0.5f, lot.Y - 0.8f) : new Vector2(lot.X + 0.5f, lot.Y + 1.22f);

    private void DrawStreetPeople(Rect2 view)
    {
        var w = World!;
        float r = FigureRadius;
        foreach (var p in w.People)
        {
            var lot = w.Map.LotAt(p.LotId);
            // Drifters and reporters wander up and down; cops walk their beat further; witnesses keep still.
            float pace = p.Kind switch
            {
                PersonKind.Drifter => Mathf.Sin(_anim * 0.4f + p.Id) * 0.25f,
                PersonKind.BeatCop => Mathf.Sin(_anim * 0.18f + p.Id) * 0.9f,
                PersonKind.Reporter => Mathf.Sin(_anim * 0.3f + p.Id) * 0.35f,
                _ => 0,
            };
            var at = StandingSpot(lot, p.Id * 3 + 1, pace);
            if (!view.HasPoint(at)) continue;
            var coat = CoatOf(p.Kind);
            var hat = p.Kind == PersonKind.BeatCop ? new Color("1b2a4a") : coat.Darkened(0.5f);
            var band = p.Kind switch { PersonKind.BeatCop => new Color("d9b44a"), PersonKind.Reporter => new Color("f0ead8"), _ => hat.Lightened(0.2f) };
            float drift = Mathf.Cos(_anim * (p.Kind == PersonKind.BeatCop ? 0.18f : 0.4f) + p.Id);
            bool walking = p.Kind is PersonKind.BeatCop or PersonKind.Drifter or PersonKind.Reporter;
            DrawFigure(at * _t, new Vector2(drift, 0), r, coat, hat, band, walking);
            if (p.Id == SelectedPerson)
            {
                var keep = C;
                C = DynAt(at.Y);
                FlatRing(at * _t, r * 1.4f, Palette.Ink, Px(2));
                C = keep;
            }
            DrawMarker(at, r, p.Kind, p.Id == SelectedPerson);
            _clickables.Add(new Clickable(Pickable.Person, p.Id, at, r / _t));
        }
    }

    /// <summary>A little sign over someone's head, big enough to find from across the city.</summary>
    private void DrawMarker(Vector2 feet, float r, PersonKind kind, bool selected)
    {
        var pen = DynAt(feet.Y);
        var keep = C;
        C = pen;
        float m = Mathf.Max(r * 0.55f, Px(5));
        var c = feet * _t - new Vector2(0, r * 3.1f + m * (1.2f + 0.15f * Mathf.Sin(_anim * 3 + feet.X)));
        var ink = new Color("1a1716");
        if (selected) Glow(c, m * 3, MarkerOf(kind), 0.9f);
        switch (kind)
        {
            case PersonKind.Drifter: // a diamond: for hire
                C.DrawColoredPolygon(new[] { c + new Vector2(0, -m * 1.25f), c + new Vector2(m * 1.05f, 0), c + new Vector2(0, m * 1.25f), c + new Vector2(-m * 1.05f, 0) }, ink);
                C.DrawColoredPolygon(new[] { c + new Vector2(0, -m), c + new Vector2(m * 0.8f, 0), c + new Vector2(0, m), c + new Vector2(-m * 0.8f, 0) }, MarkerOf(kind));
                break;
            case PersonKind.BeatCop: // a badge
                C.DrawCircle(c, m * 1.1f, ink);
                C.DrawCircle(c, m * 0.9f, MarkerOf(kind));
                Star(c, m * 0.65f, new Color("e9d27a"), 6, 0.5f);
                break;
            case PersonKind.Reporter: // a press card
                C.DrawRect(new Rect2(c - new Vector2(m * 1.15f, m * 0.85f), new Vector2(m * 2.3f, m * 1.7f)), ink);
                C.DrawRect(new Rect2(c - new Vector2(m * 0.95f, m * 0.65f), new Vector2(m * 1.9f, m * 1.3f)), MarkerOf(kind));
                for (int i = 0; i < 3; i++)
                    C.DrawLine(c + new Vector2(-m * 0.6f, -m * 0.3f + i * m * 0.3f), c + new Vector2(m * 0.6f, -m * 0.3f + i * m * 0.3f), ink, Mathf.Max(1, m * 0.15f));
                break;
            default: // an exclamation mark: he saw something
                C.DrawCircle(c, m * 1.1f, ink);
                C.DrawCircle(c, m * 0.9f, MarkerOf(kind));
                C.DrawRect(new Rect2(c + new Vector2(-m * 0.14f, -m * 0.6f), new Vector2(m * 0.28f, m * 0.75f)), Colors.White);
                C.DrawCircle(c + new Vector2(0, m * 0.45f), m * 0.16f, Colors.White);
                break;
        }
        C = keep;
    }

    /// <summary>A few of each gang's men hang about outside headquarters when they aren't on a job.</summary>
    private void DrawLoiterers(Rect2 view)
    {
        var w = World!;
        if (Tile < 9) return;
        float r = FigureRadius;
        foreach (var gang in w.LivingGangs)
        {
            var hq = w.HqOf(gang);
            if (!view.HasPoint(new Vector2(hq.X, hq.Y))) continue;
            var busy = _sim is { WeekRunning: true } ? _sim.CommittedHoods(gang.Id) : new HashSet<int>();
            var men = w.AvailableHoodsOf(gang.Id).Where(h => !busy.Contains(h.Id)).OrderBy(h => h.Id == gang.BossHoodId ? 1 : 0).ThenBy(h => h.Id).Take(4).ToList();
            var colour = Palette.Gang(w, gang.Id);
            for (int i = 0; i < men.Count; i++)
            {
                var h = men[i];
                var door = Kerbside(hq);
                float side = i % 2 == 0 ? -1 : 1;
                // Either side of the door, clear of the boss's car at the kerb.
                var at = door + new Vector2(side * (0.3f + 0.2f * (i / 2)), -0.04f * (i / 2));
                bool boss = h.Id == gang.BossHoodId;
                var hat = boss ? new Color("e9e0c8") : new Color("1c1a19");
                DrawFigure(at * _t, new Vector2(-side, 0), r, colour, hat, colour.Lightened(0.25f), false);
                if (h.Id == SelectedHood) { C = DynAt(at.Y); FlatRing(at * _t, r * 1.4f, Palette.Ink, Px(2)); }
                _clickables.Add(new Clickable(Pickable.Hood, h.Id, at, r / _t));
            }
        }
    }

    /// <summary>Flames on a business set alight this week, and smoke over the shell for a while after.</summary>
    private void DrawFires(Rect2 view)
    {
        var w = World!;
        foreach (var b in w.Businesses)
        {
            if (!Burnt(b)) continue;
            var lot = w.LotOf(b);
            if (!view.HasPoint(new Vector2(lot.X, lot.Y))) continue;
            float h = HeightCached(lot);
            var (x0, y0, x1, y1) = Footprint(lot);
            C = DynAt(lot.Y + 0.95f);
            bool burning = _burntNow.Contains(b.Id) || b.BurnedWeek == w.Week - 1;
            if (burning)
            {
                for (int k = 0; k < 5; k++)
                {
                    float x = x0 + 0.12f + k * (x1 - x0 - 0.24f) / 4;
                    float flick = 0.7f + 0.3f * Mathf.Sin(_anim * (9 + k) + k * 1.7f);
                    var base0 = Q(x, (y0 + y1) / 2, h);
                    float fw = 0.09f * _t, fh = (0.22f + 0.12f * Hash(b.Id, k)) * flick * _t;
                    C.DrawColoredPolygon(new[] { base0 + new Vector2(-fw, 0), base0 + new Vector2(0, -fh), base0 + new Vector2(fw, 0) }, new Color("e8601c") with { A = 0.9f });
                    C.DrawColoredPolygon(new[] { base0 + new Vector2(-fw * 0.5f, 0), base0 + new Vector2(0, -fh * 0.6f), base0 + new Vector2(fw * 0.5f, 0) }, new Color("ffd25a"));
                }
                Glow(Q(lot.X + 0.5f, (y0 + y1) / 2, h), _t * 0.9f, new Color("ff8a3a"), 0.7f);
            }
            if (!Fine) continue;
            for (int k = 0; k < 4; k++)
            {
                float phase = (_anim * 0.25f + k / 4f + Hash(b.Id, 720)) % 1;
                var at = Q(lot.X + 0.5f, (y0 + y1) / 2, h + 0.1f) + new Vector2(phase * 0.4f, -phase * 1.1f) * _t;
                C.DrawCircle(at, (0.06f + 0.16f * phase) * _t, new Color(burning ? "4a4440" : "7a7670") with { A = 0.5f * (1 - phase) });
            }
        }
    }

    /// <summary>The person or hood under a point on screen, frontmost first.</summary>
    private Clickable? PickAt(Vector2 screen)
    {
        var tile = (screen - Origin) / Tile;
        Clickable? best = null;
        foreach (var c in _clickables)
        {
            // Small figures far off still get a fair-sized target.
            float r = Mathf.Max(c.Radius, 6 / Mathf.Max(1, Tile));
            var box = new Rect2(c.Feet.X - r * 1.1f, c.Feet.Y - r * 4.2f, r * 2.2f, r * 4.6f);
            if (!box.HasPoint(tile)) continue;
            if (best == null || c.Feet.Y > best.Value.Feet.Y) best = c;
        }
        return best;
    }

    private string PersonTooltip(Clickable c)
    {
        var w = World!;
        if (c.Kind == Pickable.Hood)
        {
            var h = w.HoodById(c.Id);
            var g = w.GangById(h.GangId);
            return $"{h.Name}\n{(g.BossHoodId == h.Id ? "Boss of " : "")}{g.Name} · strength {h.Strength}";
        }
        var p = w.PersonById(c.Id);
        if (p == null) return "";
        return p.Kind switch
        {
            PersonKind.Drifter => $"{p.Name}\nLooking for work · asks ${p.Fee}",
            PersonKind.BeatCop => $"{p.Name}\nWalks the beat in {w.Wards.ElementAtOrDefault(p.WardId)?.Name}",
            PersonKind.Reporter => $"{p.Name}\nAsking questions about {w.GangById(p.GangId).Name}",
            _ => $"{p.Name}\nSaw {p.About}",
        };
    }

    // ---- Banks ------------------------------------------------------------------

    private void DrawBank(Lot lot)
    {
        float h = HeightCached(lot);
        var (x0, y0, x1, y1) = Footprint(lot);
        var stone = new Color("b3ab9a");
        var roof = new Color("6e6a62");
        Box(x0, y0, x1, y1, 0, h, stone, stone.Lightened(0.08f), roof);
        // A heavy cornice round the top.
        Face(x0, x1, y1, h - 0.07f, h, stone.Darkened(0.15f));
        West(x0, y0, y1, h - 0.07f, h, stone.Darkened(0.1f));
        if (FacesSouth(lot))
        {
            // Steps up to a colonnade, bronze doors, a pediment over it all, and lamps either side.
            Box(x0 + 0.02f, y1, x1 - 0.02f, y1 + 0.08f, 0, 0.05f, stone.Lightened(0.05f), stone, stone.Lightened(0.15f));
            Face(x0 + 0.38f, x1 - 0.38f, y1, 0.05f, 0.32f, new Color("4a3a24"));
            for (int i = 0; i < 6; i++)
            {
                float cx = x0 + 0.06f + i * (x1 - x0 - 0.17f) / 5;
                Face(cx, cx + 0.05f, y1 + 0.01f, 0.05f, h - 0.07f, stone.Lightened(0.22f));
                if (Fine) Face(cx + 0.035f, cx + 0.05f, y1 + 0.01f, 0.05f, h - 0.07f, stone.Darkened(0.1f));
            }
            C.DrawColoredPolygon(new[] { Q(x0, y1, h), Q(x1, y1, h), Q((x0 + x1) / 2, y1, h + 0.18f) }, stone.Lightened(0.12f));
            C.DrawPolyline(new[] { Q(x0, y1, h), Q((x0 + x1) / 2, y1, h + 0.18f), Q(x1, y1, h) }, stone.Darkened(0.3f), Mathf.Max(1, _t * 0.02f));
            Lit(() => Glow(Q((x0 + x1) / 2, y1 + 0.05f, 0.2f), _t * 0.45f, Lamplight, 0.6f));
        }
        else
        {
            // The back: tall barred windows.
            for (float x = x0 + 0.1f; x < x1 - 0.12f; x += 0.2f)
            {
                Face(x, x + 0.08f, y1, 0.12f, h - 0.15f, Glass);
                if (Fine) for (float z = 0.16f; z < h - 0.15f; z += 0.08f) C.DrawLine(Q(x, y1, z), Q(x + 0.08f, y1, z), stone.Darkened(0.2f), 1);
            }
        }
        DrawBillboard(lot, h, new Color("2c5a3a"), 3, true, () =>
        {
            if (!Fine) return;
            var c = SignCentre(lot, h);
            C.DrawCircle(c, _t * 0.1f, new Color("e8b84a"));
            C.DrawArc(c, _t * 0.065f, 0, Mathf.Tau, 12, new Color("5a3f10"), Mathf.Max(1, _t * 0.02f));
        });
    }
}
