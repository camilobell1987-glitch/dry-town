namespace DryTown.Core;

/// <summary>What sort of day someone has, which decides where they go.</summary>
public enum RoutineKind { Loafer, Shopkeeper, Worker, Landlord, Errands, NightShift, Park }

/// <summary>
/// A place someone goes in their day: the lot, the hour they set out for the next place (or home), and how long they
/// stand about outside on arriving before going in. Hours run past 24 for a night out that ends after midnight.
/// </summary>
public readonly record struct Stop(int LotId, float Leave, float Outside);

/// <summary>
/// Someone's day: when they leave home and the places they go before they come back. A loafer has no places to be,
/// and walks round his own block now and then as before.
/// </summary>
public sealed record Routine(RoutineKind Kind, float LeaveHome, IReadOnlyList<Stop> Stops)
{
    public static readonly Routine Loafing = new(RoutineKind.Loafer, 0, Array.Empty<Stop>());
}

public static partial class Content
{
    /// <summary>How fast residents walk, in tiles an hour.</summary>
    public const float ResidentPace = 10;

    /// <summary>The most stops in anyone's day, so the map can keep them in a fixed space.</summary>
    public const int MostStops = 3;
}

public sealed partial class CityDirectory
{
    private readonly Dictionary<int, Routine> _routines = new();

    /// <summary>
    /// Where someone goes each day. A shopkeeper opens up in the morning and shuts at night; a man with a job walks to
    /// it and back; a landlord goes round his buildings for the rent and is home after dark; a woman keeping house
    /// does the shopping or sits in a diner; the old sit in the park; a night watchman goes out as the others come home.
    /// Children and anyone with nowhere to be wander their own block.
    /// </summary>
    public Routine RoutineOf(int id)
    {
        if (_routines.TryGetValue(id, out var known)) return known;
        var c = Get(id);
        var home = _w.Map.LotAt(c.HomeLotId);
        float Hour(float from, float spread, int salt) => from + (float)Roll(id, salt) * spread;
        Routine routine;
        if (c.OwnsBusinessId >= 0)
        {
            // He lives over the shop: he's out front to open up in the morning, and again to shut up for the night.
            var shop = _w.BusinessById(c.OwnsBusinessId);
            var (open, shut) = Hours(shop.Kind);
            float opens = open + (float)Roll(id, 601) * 0.5f, shuts = shut + (float)Roll(id, 602) * 0.5f;
            routine = new(RoutineKind.Shopkeeper, opens - 0.4f, new[]
            {
                new Stop(shop.LotId, shuts - 0.4f, 0.4f),
                new Stop(shop.LotId, shuts, 0.4f),
            });
        }
        else if (c.Owns.Count > 0)
        {
            // Round his buildings for the rent, knocking on doors at each, and home when it's dark.
            var buildings = c.Owns.Where(l => l != c.HomeLotId).OrderBy(l => Roll(id, 610 + l % 97)).Take(Content.MostStops).ToList();
            if (buildings.Count == 0) buildings.Add(c.HomeLotId);
            float leave = Hour(8.5f, 1.5f, 611);
            float back = Hour(19.5f, 1.5f, 612);
            var stops = new List<Stop>();
            float at = leave;
            for (int k = 0; k < buildings.Count; k++)
            {
                float walk = WalkHours(k == 0 ? c.HomeLotId : buildings[k - 1], buildings[k]);
                at += walk;
                // He spends the day going door to door; the last call lasts until it's time to go home.
                float stay = k == buildings.Count - 1 ? Math.Max(0.5f, back - WalkHours(buildings[k], c.HomeLotId) - at) : Hour(1.5f, 2, 613 + k);
                at += stay;
                stops.Add(new Stop(buildings[k], at, Math.Min(stay, 0.6f)));
            }
            routine = new(RoutineKind.Landlord, leave, stops);
        }
        else if (c.Age < 14 || c.Age >= 65 && c.Job == "Retired")
        {
            // The old go and sit in the park if there's one near; children play round the block.
            var near = c.Age >= 65 ? Nearest(home, "park", 18) : null;
            // Somewhere in the park, not all on the one bench.
            var park = near == null ? null : _w.Map.LotsOfBlock((near.X - 1) / CityMap.StrideX, (near.Y - 1) / CityMap.StrideY).Where(l => l.Terrain == Terrain.Park).ToList();
            routine = park is not { Count: > 0 } ? Routine.Loafing
                : new(RoutineKind.Park, Hour(10, 2, 620), new[] { new Stop(park[(int)(Roll(id, 622) * park.Count)].Id, Hour(14.5f, 2, 621), 2.5f) });
        }
        else if (c.Job == "Night watchman")
        {
            var post = Workplace(c, home);
            routine = post == null ? Routine.Loafing
                : new(RoutineKind.NightShift, Hour(18.5f, 1, 630), new[] { new Stop(post.Id, 24 + Hour(5.5f, 1, 631), 0) });
        }
        else if (c.Job == "Keeps house" || c.Job == "Out of work")
        {
            // Out to the shops, or a diner, once or twice in the day, and home to get supper on.
            var shops = Errands(c, home);
            if (shops.Count == 0) routine = Routine.Loafing;
            else
            {
                float leave = Hour(9.5f, 2, 640);
                var stops = new List<Stop>();
                float at = leave;
                int prev = c.HomeLotId;
                foreach (var lot in shops)
                {
                    at += WalkHours(prev, lot) + Hour(0.4f, 1.1f, 641 + stops.Count);
                    stops.Add(new Stop(lot, at, 0.15f));
                    prev = lot;
                }
                routine = new(RoutineKind.Errands, leave, stops);
            }
        }
        else
        {
            // Anyone else has a job to get to: the shop he works at, or the docks, a warehouse or wherever his trade is.
            var work = Workplace(c, home);
            routine = work == null ? Routine.Loafing
                : new(RoutineKind.Worker, Hour(6.5f, 1.5f, 650), new[] { new Stop(work.Id, Hour(17, 1.5f, 651), 0) });
        }
        _routines[id] = routine;
        return routine;
    }

    /// <summary>The hours a kind of business opens and shuts. A diner serves breakfast; a pool hall keeps late hours.</summary>
    private static (float Open, float Shut) Hours(BusinessKind kind) => kind switch
    {
        BusinessKind.Diner => (6, 20),
        BusinessKind.Grocer => (7, 19),
        BusinessKind.PoolHall => (12, 25),
        BusinessKind.Hotel => (7, 22),
        BusinessKind.Warehouse => (6.5f, 17.5f),
        BusinessKind.Garage => (7.5f, 18),
        _ => (8.5f, 18),
    };

    private float WalkHours(int from, int to) =>
        _w.Map.Distance(_w.Map.LotAt(from), _w.Map.LotAt(to)) / Content.ResidentPace;

    private readonly Dictionary<(int Lot, string What), Lot?> _nearest = new();

    /// <summary>The nearest lot of a sort ("park", "dock", "bank") to a home, within a walk of so many tiles, or null.</summary>
    private Lot? Nearest(Lot from, string what, int within)
    {
        if (_nearest.TryGetValue((from.Id, what), out var known)) return known;
        Func<Lot, bool> fits = what switch
        {
            "park" => l => l.Terrain == Terrain.Park,
            "dock" => l => l.Terrain == Terrain.Water,
            _ => l => l.Use == LotUse.Bank,
        };
        Lot? best = null;
        int bestDistance = within + 1;
        foreach (var lot in _w.Map.Lots)
        {
            if (!fits(lot)) continue;
            int d = Math.Abs(lot.X - from.X) + Math.Abs(lot.Y - from.Y);
            if (d >= bestDistance) continue;
            best = lot;
            bestDistance = d;
        }
        _nearest[(from.Id, what)] = best;
        return best;
    }

    /// <summary>Where someone works: the business he's on the books of, or a place for his trade near home.</summary>
    private Lot? Workplace(Citizen c, Lot home)
    {
        if (c.WorkBusinessId >= 0) return _w.LotOf(_w.BusinessById(c.WorkBusinessId));
        // The docks for a longshoreman, the river for a railroad man or teamster; a warehouse, a bank or any business otherwise.
        if (c.Job is "Longshoreman" or "Teamster" or "Railroad man" && Nearest(home, "dock", 40) is { } dock) return dock;
        if (c.Job is "Bank clerk" && Nearest(home, "bank", 40) is { } bank) return bank;
        var nearby = _businessesInWard.GetValueOrDefault(home.WardId);
        if (nearby is not { Count: > 0 }) return null;
        return _w.LotOf(_w.BusinessById(nearby[(int)(Roll(c.Id, 655) * nearby.Count)]));
    }

    /// <summary>One or two shops or a diner in the ward to go to, in the order she'll go round them.</summary>
    private List<int> Errands(Citizen c, Lot home)
    {
        var shops = (_businessesInWard.GetValueOrDefault(home.WardId) ?? new List<int>())
            .Select(_w.BusinessById)
            .Where(b => b.Kind is BusinessKind.Grocer or BusinessKind.Diner or BusinessKind.Pharmacy or BusinessKind.Tailor or BusinessKind.Laundry or BusinessKind.Hardware or BusinessKind.Haberdasher)
            .Select(b => b.LotId).Distinct().ToList();
        if (shops.Count == 0) return shops;
        int count = Roll(c.Id, 660) < 0.4 ? 2 : 1;
        var picked = new List<int>();
        for (int k = 0; k < 4 && picked.Count < count; k++)
        {
            int lot = shops[(int)(Roll(c.Id, 661 + k) * shops.Count)];
            if (!picked.Contains(lot)) picked.Add(lot);
        }
        return picked;
    }
}
