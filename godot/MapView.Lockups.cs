using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The lockups along the back alleys: a row of low brick sheds with tarred roofs and double doors onto a cobbled lane.
/// A rented one has its gang's mark chalked on the doors. A car on its way home from a job turns down the alley,
/// noses in, and the doors shut behind it; a hijacked truck goes the same way with the gang's car behind.
/// </summary>
public partial class MapView
{
    public event Action<int>? LockupClicked;

    public int SelectedLockup { get; private set; } = -1;

    private static readonly Color Tar = new("4a4743");
    private static readonly Color[] DoorPaint = { new("3d4a3a"), new("4e3a2a"), new("3a4250"), new("5c4c32"), new("4a302b"), new("55584a") };

    /// <summary>How tall a lockup stands.</summary>
    private const float LockupHigh = 0.2f;

    /// <summary>A trip home by way of a lockup: the drive there, the pull in, and the rest of the way home.</summary>
    private sealed record Trip(Lockup Lockup, List<Vector2> In, List<Vector2> Out, float Parked, float Shut, bool Truck, Car Car);

    /// <summary>Lockups whose doors stand open this frame, for something pulling in.</summary>
    private readonly HashSet<int> _openDoors = new();

    /// <summary>Each lockup's footprint, in tiles of the plan, by id.</summary>
    private Rect2[] _lockupRects = Array.Empty<Rect2>();

    private Rect2[] LockupRects()
    {
        var w = World!;
        if (_lockupRects.Length == w.Lockups.Count) return _lockupRects;
        _lockupRects = new Rect2[w.Lockups.Count];
        foreach (var l in w.Lockups)
        {
            var (x0, x1) = w.Map.LockupSpots(l.Bx, l.By)[l.Index];
            float y0 = l.By * CityMap.StrideY + 1;
            _lockupRects[l.Id] = new Rect2(x0, y0 + CityMap.LockupFront, x1 - x0, CityMap.LockupDoor - CityMap.LockupFront);
        }
        return _lockupRects;
    }

    /// <summary>The middle of the lane in front of a lockup's doors.</summary>
    private static float LaneOf(Lockup l) => l.By * CityMap.StrideY + 1 + (CityMap.LockupDoor + CityMap.LaneEdge) / 2;

    /// <summary>
    /// The pen a vehicle is drawn with: its own row's, except down a back lane, where it goes with the row north of it
    /// so the buildings south of the lane stand in front of it.
    /// </summary>
    private Pen PenFor(Vector2 at)
    {
        int y = (int)at.Y;
        bool lane = !CityMap.IsStreetRow(y) && !CityMap.IsAvenue((int)at.X) && y % CityMap.StrideY == CityMap.LotsPerBlockY;
        return DynAt(lane ? y - 1 : y);
    }

    /// <summary>The way home from a job by a lockup, or null if nothing goes into one.</summary>
    private Trip? TripVia(ScriptAction a, float leave, Car ride)
    {
        var w = World!;
        if (w.LockupById(a.LockupId) is not { } l) return null;
        var mouth = w.LockupLot(l);
        var rect = LockupRects()[l.Id];
        float x = mouth.X, top = l.By * CityMap.StrideY + 0.5f, lane = LaneOf(l), xc = rect.GetCenter().X;
        var toMouth = StreetPath(a.ToLot, mouth.Id);
        toMouth[^1] = new Vector2(x, top);
        var into = new List<Vector2>(toMouth) { new(x, lane), new(xc, lane) };
        var home = StreetPath(mouth.Id, a.FromLot);
        home[0] = new Vector2(x, top);
        var outOf = new List<Vector2> { new(xc, lane), new(x, lane) };
        outOf.AddRange(home);
        float speed = Content.Cars[ride].Speed;
        float parked = leave + Simulation.WalkHours(w, a.ToLot, mouth.Id) / speed + 0.15f;
        if (OS.GetCmdlineUserArgs().Contains("--trips")) GD.Print($"Trip home by {w.LockupName(l)}: leaves {leave:F2}, pulls in {parked:F2}");
        return new Trip(l, into, outOf, parked, parked + 0.5f, a.Truck, ride);
    }

    /// <summary>How long the walk or drive home from the lockup takes.</summary>
    private float HomeFrom(Trip via, ScriptAction a)
    {
        float walk = Simulation.WalkHours(World!, World!.LockupLot(via.Lockup).Id, a.FromLot) + 0.15f;
        return via.Truck && via.Car != Car.None ? walk / Content.Cars[via.Car].Speed : walk;
    }

    /// <summary>Where the man in front is on the way home by a lockup.</summary>
    private static Vector2 TripAt(Actor actor, Trip via, float now)
    {
        if (now <= via.Parked) return Along(via.In, (now - actor.Leave) / Mathf.Max(0.01f, via.Parked - actor.Leave));
        if (now <= via.Shut) return via.In[^1];
        return Along(via.Out, (now - via.Shut) / Mathf.Max(0.01f, actor.Home - via.Shut));
    }

    /// <summary>The drive home by a lockup: the car or the truck down the alley and in, then the men walking home.</summary>
    private void DrawTripHome(Actor actor, Trip via, float now, float radius)
    {
        var a = actor.Action;
        var (fill, rim) = ManOf(a.GangId);
        var truckPaint = RidePaint(Ride.BeerTruck, Hash(a.HoodId, 77));
        float length = PathLength(via.In);
        if (now <= via.Parked)
        {
            float f = (now - actor.Leave) / Mathf.Max(0.01f, via.Parked - actor.Leave);
            var at = Along(via.In, f);
            var ahead = Along(via.In, f + 0.02f) - at;
            if (!via.Truck) { DrawGangCar(at, ahead, actor.Colour, via.Car); return; }
            DrawRolling(at, ahead, Ride.BeerTruck, truckPaint);
            float behind = f - 0.75f / Mathf.Max(0.1f, length);
            if (via.Car != Car.None && behind > 0)
                DrawGangCar(Along(via.In, behind), Along(via.In, behind + 0.02f) - Along(via.In, behind), actor.Colour, via.Car);
            return;
        }
        var door = via.In[^1];
        if (now <= via.Shut)
        {
            // Nose in through the open doors.
            _openDoors.Add(via.Lockup.Id);
            float f = Mathf.SmoothStep(0, 1, Mathf.Min(1, (now - via.Parked) / (via.Shut - via.Parked) * 1.4f));
            var inside = new Vector2(door.X, via.Lockup.By * CityMap.StrideY + 1 + 0.9f);
            var at = door.Lerp(inside, f);
            var keep = C;
            C = PenFor(at);
            if (via.Truck)
            {
                DrawVehicle(at, false, -1, Ride.BeerTruck, truckPaint, Night);
                C = keep;
                if (via.Car != Car.None) DrawGangCar(Along(via.In, 1 - 0.75f / Mathf.Max(0.1f, length)), door - Along(via.In, 1 - 0.75f / Mathf.Max(0.1f, length)), actor.Colour, via.Car);
                return;
            }
            DrawVehicle(at, false, -1, CarRide(via.Car), CarPaintOf(actor.Colour, via.Car), Night);
            C = keep;
            // Out they get once it's in.
            if (f >= 1) DrawMen(P(door + new Vector2(0.12f, 0)), Vector2.Right, radius, fill, rim, a, false);
            return;
        }
        float g = (now - via.Shut) / Mathf.Max(0.01f, actor.Home - via.Shut);
        var on = Along(via.Out, g);
        var way = Along(via.Out, g + 0.02f) - on;
        if (via.Truck && via.Car != Car.None) { DrawGangCar(on, way, actor.Colour, via.Car); return; }
        DrawMen(P(on), way, radius, fill, rim, a, true);
    }

    private void DrawMen(Vector2 pos, Vector2 ahead, float radius, Color fill, Color? rim, ScriptAction a, bool walking)
    {
        int backup = a.Backup.Count - (a.CasualtyHoodId >= 0 && a.Backup.Contains(a.CasualtyHoodId) ? 1 : 0);
        DrawBackup(pos, ahead, radius, fill, rim, backup, walking);
        DrawDot(pos, radius, fill, rim);
    }

    /// <summary>A vehicle on the move that isn't a gang's car: a hijacked truck.</summary>
    private void DrawRolling(Vector2 at, Vector2 ahead, Ride ride, Color paint) => DrawOnTheRoad(at, ahead, ride, paint, false);

    private static float PathLength(List<Vector2> path)
    {
        float total = 0;
        for (int i = 1; i < path.Count; i++) total += path[i].DistanceTo(path[i - 1]);
        return total;
    }

    /// <summary>The row of lockups along each back alley in view, drawn over anything pulling in so the roof hides it.</summary>
    private void DrawLockups(Rect2 view)
    {
        var w = World!;
        var rects = LockupRects();
        var keep = C;
        foreach (var l in w.Lockups)
        {
            var r = rects[l.Id];
            if (!view.Intersects(r)) continue;
            C = DynAt(l.By * CityMap.StrideY + 1);
            float x0 = r.Position.X + 0.012f, x1 = r.End.X - 0.012f, yn = r.Position.Y, ys = r.End.Y, h = LockupHigh;
            var brick = Bricks[(int)(Hash(l.Bx * 131 + l.By, 1100) * Bricks.Length)].Darkened(0.1f);
            var door = DoorPaint[(int)(Hash(l.Id, 1101) * DoorPaint.Length)];
            bool open = _openDoors.Contains(l.Id);
            var dark = new Color("141210");
            Color? mark = l.GangId >= 0 ? Palette.Gang(w, l.GangId) : null;
            // The party walls between them, then the shed.
            C.DrawRect(R(r.Position.X, yn, r.Size.X, ys - yn), brick.Darkened(0.35f));
            if (Angled)
            {
                Box(x0, yn, x1, ys, 0, h, brick, brick.Darkened(0.2f), Tar);
                // A lip of tar paper over the front, and the doors under it.
                Face(x0, x1, ys, h - 0.025f, h, Tar.Lightened(0.08f));
                Face(x0 + 0.035f, x1 - 0.035f, ys, 0, h - 0.04f, open ? dark : door);
                if (!open)
                {
                    float mid = (x0 + x1) / 2;
                    C.DrawLine(Q(mid, ys, 0), Q(mid, ys, h - 0.04f), door.Darkened(0.45f), Hair(0.01f));
                    for (int i = 1; i < 3; i++)
                        C.DrawLine(Q(x0 + 0.035f, ys, i * (h - 0.04f) / 3), Q(x1 - 0.035f, ys, i * (h - 0.04f) / 3), door.Lightened(0.08f), Hair(0.006f));
                    if (mark is { } chalk) Face(mid + 0.025f, mid + 0.07f, ys, h - 0.1f, h - 0.065f, chalk);
                    if (l.GangId >= 0) Face(mid - 0.012f, mid + 0.012f, ys, 0.07f, 0.095f, new Color("8a8478"));
                }
            }
            else
            {
                // From above: the tarred roof with its seams, and the doors as a strip along the front.
                C.DrawRect(R(x0, yn, x1 - x0, ys - yn), Tar);
                if (Fine)
                    for (int i = 1; i < 4; i++)
                        C.DrawLine(P(x0 + 0.01f, yn + i * (ys - yn) / 4), P(x1 - 0.01f, yn + i * (ys - yn) / 4), Tar.Lightened(0.1f), Hair(0.006f));
                C.DrawRect(R(x0 + 0.03f, ys - 0.055f, x1 - x0 - 0.06f, 0.055f), open ? dark : door);
                if (!open && mark is { } chalk) C.DrawRect(R((x0 + x1) / 2 - 0.045f, ys - 0.045f, 0.09f, 0.035f), chalk);
            }
            if (l.Id == SelectedLockup)
            {
                C = _marks;
                float pulse = 0.75f + 0.25f * Mathf.Sin(_anim * 4);
                var a = Q(r.Position.X, yn, h);
                var b = Q(r.End.X, ys, 0);
                var box = new Rect2(new Vector2(Mathf.Min(a.X, b.X), a.Y), new Vector2(Mathf.Abs(b.X - a.X), b.Y - a.Y)).Grow(Px(3));
                C.DrawPolyline(new[] { box.Position, box.Position + new Vector2(box.Size.X, 0), box.End, box.Position + new Vector2(0, box.Size.Y), box.Position }, Palette.Ink with { A = pulse }, Px(2.5f));
            }
        }
        C = keep;
        _openDoors.Clear();
    }

    /// <summary>The lockup under a point on screen, or -1.</summary>
    private int LockupUnder(Vector2 screen)
    {
        if (World == null) return -1;
        var tile = TileUnder(screen);
        var raised = Angled ? tile + new Vector2(-S * LockupHigh, LockupHigh) : tile;
        var rects = LockupRects();
        for (int i = 0; i < rects.Length; i++)
            if (rects[i].HasPoint(tile) || rects[i].HasPoint(raised)) return i;
        return -1;
    }

    private string LockupTooltip(Lockup l)
    {
        var w = World!;
        string who = l.GangId < 0 ? $"To let, ${Content.LockupRent} a week" : l.GangId == w.Player.Id ? "Yours" : $"Rented by {w.GangById(l.GangId).Name}";
        string what = l.GangId != w.Player.Id ? "" : l.Holds switch
        {
            Stashed.Car => $"\nHiding a {Content.Cars[l.Car].Label}",
            Stashed.Truck => "\nHiding a hijacked beer truck",
            _ => "\nEmpty",
        };
        return $"{w.LockupName(l)}\n{who}{what}";
    }

    private static Ride CarRide(Car car) => car switch { Car.ModelT => Ride.ModelT, Car.Packard => Ride.Packard, _ => Ride.Armoured };

    private static Color CarPaintOf(Color gang, Car car) => car == Car.Cadillac ? new Color("1c1a1c") : gang.Darkened(0.35f);
}
