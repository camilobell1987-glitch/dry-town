namespace DryTown.Core;

/// <summary>Guns and cars: the shops that sell them, and buying, trading in and handing them round.</summary>
public sealed partial class World
{
    /// <summary>
    /// A gun shop in every other ward, between the banks, and a car dealer in every fourth, on lots
    /// nobody has taken. Placed without the dice, after the first gangs have their headquarters,
    /// so the rest of a new city comes out as it did before there were shops, and an older
    /// saved city gets shops when it's loaded.
    /// </summary>
    public void PlaceShops()
    {
        if (Map.Lots.Any(l => l.Use is LotUse.GunShop or LotUse.CarDealer)) return;
        int wards = Map.WardCount;
        for (int ward = 0; ward < wards; ward++)
        {
            bool gunShop = wards == 1 || ward % 2 == 1;
            bool dealer = wards == 1 || ward % 4 == 1;
            if (!gunShop && !dealer) continue;
            var lots = Map.Lots.Where(l => l.WardId == ward && l.Terrain == Terrain.Land).ToList();
            if (lots.Count == 0) continue;
            double cx = lots.Average(l => l.X), cy = lots.Average(l => l.Y);
            // Not right in the middle: the gun shop a little up the street, the dealer out on a main road.
            var free = lots.Where(l => l.IsFree).OrderBy(l => Math.Abs(l.X - cx - 2) + Math.Abs(l.Y - cy)).ThenBy(l => l.Id).ToList();
            if (gunShop && free.Count > 0) { free[0].Use = LotUse.GunShop; free.RemoveAt(0); }
            if (dealer && free.Count > 0)
                free.OrderBy(l => Math.Abs(l.X - cx + 3) + Math.Abs(l.Y - cy - 1)).ThenBy(l => l.Id).First().Use = LotUse.CarDealer;
        }
    }

    public IEnumerable<Lot> Shops(LotUse use) => Map.Lots.Where(l => l.Use == use);

    public static string ShopName(CityMap map, Lot lot) => lot.Use switch
    {
        LotUse.GunShop => $"{map.StreetOf(lot)} St Sporting Goods",
        LotUse.CarDealer => $"{map.StreetOf(lot)} St Motors",
        _ => "",
    };

    /// <summary>Why a man can't have this gun, or null if he can. The old one is traded in.</summary>
    public string? CannotArm(Gang gang, Hood hood, Weapon gun)
    {
        if (hood.GangId != gang.Id || !hood.IsActive) return "He isn't one of yours.";
        if (hood.State == HoodState.Jailed) return "He's in jail.";
        if (gun == hood.Weapon) return "He already carries one.";
        if (gun == Weapon.None) return null;
        if (Content.Guns[gun].Price < Content.Guns[hood.Weapon].Price) return "He carries something heavier.";
        int cost = Content.Guns[gun].Price - Content.TradeIn(Content.Guns[hood.Weapon].Price);
        return gang.Cash < cost ? $"That costs ${cost}." : null;
    }

    public string? CannotDrive(Gang gang, Hood hood, Car car)
    {
        if (hood.GangId != gang.Id || !hood.IsActive) return "He isn't one of yours.";
        if (car == hood.Car) return "He already drives one.";
        if (car == Car.None) return null;
        if (Content.Cars[car].Price < Content.Cars[hood.Car].Price) return "He drives something better.";
        int cost = Content.Cars[car].Price - Content.TradeIn(Content.Cars[hood.Car].Price);
        return gang.Cash < cost ? $"That costs ${cost}." : null;
    }

    /// <summary>Buy a man a gun, trading in what he had. Returns what it cost, or -1 if he can't have it.</summary>
    public int Arm(Gang gang, Hood hood, Weapon gun)
    {
        if (gun == Weapon.None || CannotArm(gang, hood, gun) != null) return -1;
        int cost = Content.Guns[gun].Price - Content.TradeIn(Content.Guns[hood.Weapon].Price);
        gang.Cash -= cost;
        Ledger(gang.Id).Spending += cost;
        hood.Weapon = gun;
        Log(EventKind.Gear, gang.Id, $"{hood.Name} of {gang.Name} bought a {Content.Guns[gun].Label}.");
        return cost;
    }

    /// <summary>Buy a man a car, trading in what he had. Returns what it cost, or -1 if he can't have it.</summary>
    public int Drive(Gang gang, Hood hood, Car car)
    {
        if (car == Car.None || CannotDrive(gang, hood, car) != null) return -1;
        int cost = Content.Cars[car].Price - Content.TradeIn(Content.Cars[hood.Car].Price);
        gang.Cash -= cost;
        Ledger(gang.Id).Spending += cost;
        hood.Car = car;
        Log(EventKind.Gear, gang.Id, $"{hood.Name} of {gang.Name} drove a {Content.Cars[car].Label} off the lot.");
        return cost;
    }

    /// <summary>Swap a gun or a car between two of the gang's men. A man in jail can still hand over what he left behind.</summary>
    public bool HandOver(Hood from, Hood to, bool car)
    {
        if (from.GangId != to.GangId || from.Id == to.Id || !from.IsActive || !to.IsActive) return false;
        if (car)
        {
            if (from.Car == Car.None) return false;
            (from.Car, to.Car) = (to.Car, from.Car);
        }
        else
        {
            // A jailed man's gun is stashed at headquarters, so it can go to someone else; he can't take one in with him.
            if (from.Weapon == Weapon.None || to.State == HoodState.Jailed) return false;
            (from.Weapon, to.Weapon) = (to.Weapon, from.Weapon);
        }
        return true;
    }
}
