namespace DryTown.Core;

/// <summary>What's behind a lockup's doors.</summary>
public enum Stashed { Nothing, Car, Truck }

/// <summary>
/// A lockup in a back alley: a brick shed with a pair of doors, rented by the week. A car the police are looking for
/// waits in one until they stop looking, and a hijacked beer truck until its load can be sold at a proper price.
/// </summary>
public sealed class Lockup
{
    public int Id { get; init; }

    /// <summary>The block whose back alley it's in, and which lockup along the row it is, from the west.</summary>
    public int Bx { get; init; }
    public int By { get; init; }
    public int Index { get; init; }

    /// <summary>The gang renting it, or -1 when it's to let.</summary>
    public int GangId { get; set; } = -1;

    public Stashed Holds { get; set; }

    /// <summary>The car inside, and the man it belongs to.</summary>
    public Car Car { get; set; }
    public int HoodId { get; set; } = -1;

    /// <summary>The week the car comes out or the truck's load is sold.</summary>
    public int OutWeek { get; set; }

    /// <summary>What a truck's load will fetch.</summary>
    public int Value { get; set; }

    /// <summary>The job that put it there.</summary>
    public string Job { get; set; } = "";

    public bool IsEmpty => Holds == Stashed.Nothing;
}

public sealed partial class World
{
    public List<Lockup> Lockups { get; } = new();

    /// <summary>A lockup in every place along the back alleys. Laid out from the map, so an older saved city gets them when it's loaded.</summary>
    public void PlaceLockups()
    {
        if (Lockups.Count > 0) return;
        for (int by = 0; by < Map.BlocksY; by++)
        for (int bx = 0; bx < Map.BlocksX; bx++)
        {
            int n = Map.LockupSpots(bx, by).Count;
            for (int i = 0; i < n; i++) Lockups.Add(new Lockup { Id = Lockups.Count, Bx = bx, By = by, Index = i });
        }
    }

    public Lockup? LockupById(int id) => id >= 0 && id < Lockups.Count ? Lockups[id] : null;

    public IEnumerable<Lockup> LockupsOf(int gangId) => Lockups.Where(l => l.GangId == gangId);

    /// <summary>One of the gang's lockups with nothing in it, the nearest headquarters first.</summary>
    public Lockup? EmptyLockupOf(Gang gang) =>
        LockupsOf(gang.Id).Where(l => l.IsEmpty).OrderBy(l => FromHq(gang, l)).ThenBy(l => l.Id).FirstOrDefault();

    /// <summary>The lockup a man's car is waiting in, if it is.</summary>
    public Lockup? LockupWithCarOf(Hood hood) => Lockups.FirstOrDefault(l => l.Holds == Stashed.Car && l.HoodId == hood.Id);

    /// <summary>"the alley behind Mercer St": named for the street along the north side of its block.</summary>
    public string AlleyName(Lockup l) => $"the alley behind {Map.StreetNames[l.By]} St";

    public string LockupName(Lockup l) => $"Lockup {l.Index + 1}, {AlleyName(l)}";

    /// <summary>A lot at the mouth of the lockup's alley, on the street north of it: where a car turns in.</summary>
    public Lot LockupLot(Lockup l) =>
        Map.Lots.First(x => x.X == l.Bx * CityMap.StrideX + 1 + Map.AlleyOf(l.Bx, l.By) && x.Y == l.By * CityMap.StrideY + 1);

    /// <summary>How far a lockup is from the gang's headquarters, in tiles along the streets.</summary>
    public int FromHq(Gang gang, Lockup l) => gang.HqLotId < 0 ? 0 : Map.Distance(HqOf(gang), LockupLot(l));

    /// <summary>Why the gang can't rent this lockup, or null if it can.</summary>
    public string? CannotRent(Gang gang, Lockup l)
    {
        if (l.GangId == gang.Id) return "It's yours.";
        if (l.GangId >= 0) return $"{GangById(l.GangId).Name} rent it.";
        return gang.Cash < Content.LockupRent ? $"The first week is ${Content.LockupRent}." : null;
    }

    /// <summary>Take a lockup, paying the first week now.</summary>
    public bool RentLockup(Gang gang, Lockup l)
    {
        if (CannotRent(gang, l) != null) return false;
        gang.Cash -= Content.LockupRent;
        Ledger(gang.Id).Spending += Content.LockupRent;
        l.GangId = gang.Id;
        Log(EventKind.Gear, gang.Id, $"{gang.Name} took {LockupName(l)}.");
        return true;
    }

    /// <summary>Hand an empty lockup back.</summary>
    public bool GiveUpLockup(Gang gang, Lockup l)
    {
        if (l.GangId != gang.Id || !l.IsEmpty) return false;
        l.GangId = -1;
        return true;
    }

    /// <summary>Empty a lockup, leaving it rented.</summary>
    public static void Clear(Lockup l)
    {
        l.Holds = Stashed.Nothing;
        l.Car = Car.None;
        l.HoodId = -1;
        l.Value = 0;
        l.Job = "";
    }

    /// <summary>
    /// A car back out of a lockup: to the man it belongs to if he's still around and walking, else to someone else
    /// in the outfit who walks, else sold off. Returns who got it, or null if it was sold.
    /// </summary>
    public Hood? ReturnCar(Gang gang, Lockup l)
    {
        var car = l.Car;
        var owner = l.HoodId >= 0 && l.HoodId < Hoods.Count ? HoodById(l.HoodId) : null;
        var to = owner is { IsActive: true, Car: Car.None } && owner.GangId == gang.Id
            ? owner
            : HoodsOf(gang.Id).Where(h => h.Car == Car.None).OrderByDescending(h => h.Strength).ThenBy(h => h.Id).FirstOrDefault();
        Clear(l);
        if (to != null) { to.Car = car; return to; }
        int price = Content.TradeIn(Content.Cars[car].Price);
        gang.Cash += price;
        Ledger(gang.Id).Rackets += price;
        return null;
    }
}
