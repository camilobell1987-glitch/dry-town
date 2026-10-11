using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class GearTests
{
    private static Simulation Run(ulong seed, int weeks, CitySize size = CitySize.Small)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, Size = size });
        for (int i = 0; i < weeks; i++) sim.AdvanceWeek(playerAutopilot: true);
        return sim;
    }

    private static Hood Soldier(World w, Gang g) => w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId);

    [Theory]
    [InlineData(CitySize.Small)]
    [InlineData(CitySize.Large)]
    [InlineData(CitySize.Metropolis)]
    public void EveryCityHasAGunShopAndACarDealer(CitySize size)
    {
        var w = Simulation.New(new WorldSettings { Seed = 3, Size = size }).World;
        Assert.NotEmpty(w.Shops(LotUse.GunShop));
        Assert.NotEmpty(w.Shops(LotUse.CarDealer));
        Assert.All(w.Shops(LotUse.GunShop).Concat(w.Shops(LotUse.CarDealer)), l => Assert.Equal(-1, l.BusinessId));
    }

    [Fact]
    public void AGunAddsToAMansStrengthAndTheOldOneIsTradedIn()
    {
        var w = Simulation.New(new WorldSettings { Seed = 4 }).World;
        var me = w.Player;
        me.Cash = 5000;
        var man = Soldier(w, me);
        int bare = man.Strength;

        Assert.Equal(Content.Guns[Weapon.Pistol].Price, w.Arm(me, man, Weapon.Pistol));
        Assert.Equal(bare + Content.Guns[Weapon.Pistol].Firepower, man.Strength);
        int upgrade = w.Arm(me, man, Weapon.TommyGun);
        Assert.Equal(Content.Guns[Weapon.TommyGun].Price - Content.TradeIn(Content.Guns[Weapon.Pistol].Price), upgrade);
        Assert.Equal(5000 - Content.Guns[Weapon.Pistol].Price - upgrade, me.Cash);
        Assert.Equal(bare + Content.Guns[Weapon.TommyGun].Firepower, man.Strength);
    }

    [Fact]
    public void YouCantBuyWhatYouCantAfford()
    {
        var w = Simulation.New(new WorldSettings { Seed = 4 }).World;
        var me = w.Player;
        me.Cash = 100;
        var man = Soldier(w, me);
        Assert.NotNull(w.CannotDrive(me, man, Car.Packard));
        Assert.Equal(-1, w.Drive(me, man, Car.Packard));
        Assert.Equal(Car.None, man.Car);
        Assert.Equal(100, me.Cash);
    }

    [Fact]
    public void AJailedMansGunAndCarCanBeHandedOn()
    {
        var w = Simulation.New(new WorldSettings { Seed = 4 }).World;
        var me = w.Player;
        me.Cash = 10_000;
        var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).Take(2).ToList();
        w.Arm(me, men[0], Weapon.Shotgun);
        w.Drive(me, men[0], Car.ModelT);
        men[0].State = HoodState.Jailed;

        Assert.True(w.HandOver(men[0], men[1], car: false));
        Assert.True(w.HandOver(men[0], men[1], car: true));
        Assert.Equal(Weapon.Shotgun, men[1].Weapon);
        Assert.Equal(Car.ModelT, men[1].Car);
        // Nobody can pass a gun into a cell.
        Assert.False(w.HandOver(men[1], men[0], car: false));
    }

    [Fact]
    public void AGetawayCarSetsTheTravelTimeAndTheOdds()
    {
        var w = Simulation.New(new WorldSettings { Seed = 4 }).World;
        var me = w.Player;
        me.Cash = 10_000;
        var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).Take(2).ToList();
        var far = w.Businesses.OrderByDescending(b => w.BlocksFromHq(me, b)).First();
        float walk = Simulation.TravelHours(w, me.HqLotId, far.LotId, men);
        double before = Simulation.RobberyChance(w, men[0], men.Skip(1), w.Banks[0]);

        w.Drive(me, men[1], Car.Packard);
        Assert.Equal(Car.Packard, Simulation.BestCar(men));
        Assert.True(Simulation.TravelHours(w, me.HqLotId, far.LotId, men) < walk);
        Assert.True(Simulation.RobberyChance(w, men[0], men.Skip(1), w.Banks[0]) >= before);
        Assert.Equal(Content.Cars[Car.Packard].Getaway, Simulation.Getaway(men));
    }

    [Fact]
    public void AnArmouredCarMakesAMenHarderToHit()
    {
        var w = Simulation.New(new WorldSettings { Seed = 4 }).World;
        var hitman = Soldier(w, w.Player);
        var target = w.Hoods.First(h => h.GangId != w.Player.Id && h.IsAvailable && w.GangById(h.GangId).BossHoodId != h.Id);
        double open = Simulation.HitChance(w, hitman, Array.Empty<Hood>(), target);
        target.Car = Car.Cadillac;
        Assert.True(Simulation.HitChance(w, hitman, Array.Empty<Hood>(), target) < open);
    }

    [Fact]
    public void OrdersToBuyAreCarriedOutMondayMorning()
    {
        var shell = new CommandShell(Run(2, 2));
        var w = shell.Sim.World;
        w.Player.Cash = 5000;
        var man = Soldier(w, w.Player);
        shell.Pending.Clear();
        shell.Pending.Add(new ArmOrder(w.Player.Id, man.Id, Weapon.Pistol));
        shell.Pending.Add(new CarOrder(w.Player.Id, man.Id, Car.ModelT));
        Assert.Contains("revolver", shell.Describe(shell.Pending[0]));
        shell.Sim.BeginWeek(new Dictionary<int, List<Order>> { [w.Player.Id] = shell.Pending.ToList() });
        shell.Sim.RunUntil(1);
        Assert.Equal(Weapon.Pistol, man.Weapon);
        Assert.Equal(Car.ModelT, man.Car);
    }

    [Fact]
    public void TheShellSellsGunsAndCarsOnTheSpot()
    {
        var shell = new CommandShell(Run(2, 2));
        var w = shell.Sim.World;
        w.Player.Cash = 5000;
        var men = w.AvailableHoodsOf(w.Player.Id).Where(h => h.Id != w.Player.BossHoodId).Take(2).ToList();
        Assert.Contains("Thompson", shell.Execute("shops"));
        Assert.Contains("now carries", shell.Execute($"arm {men[0].Id} pistol"));
        Assert.Contains("now drives", shell.Execute($"drive {men[0].Id} modelt"));
        Assert.Contains("handed", shell.Execute($"hand {men[0].Id} {men[1].Id} car"));
        Assert.Equal(Car.ModelT, men[1].Car);
        Assert.Contains("revolver", shell.Execute("hoods"));
    }

    [Fact]
    public void RivalsArmThemselvesAsTheyGetRich()
    {
        var w = Run(3, 3 * Content.WeeksPerYear, CitySize.Medium).World;
        Assert.Contains(w.Events, e => e.Kind == EventKind.Gear && e.GangId != w.Player.Id);
        Assert.Contains(w.Hoods, h => h.IsActive && h.GangId != w.Player.Id && h.Weapon != Weapon.None);
    }

    [Fact]
    public void GearSurvivesASaveAndOldSavesGetShops()
    {
        var sim = Run(5, 10, CitySize.Large);
        var w = sim.World;
        w.Player.Cash = 10_000;
        var man = Soldier(w, w.Player);
        w.Arm(w.Player, man, Weapon.Shotgun);
        w.Drive(w.Player, man, Car.Packard);

        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveGame.Write(sim, new List<Order>()))!;
        var loaded = SaveGame.Restore(SaveGame.Read(json.ToJsonString())).World;
        Assert.Equal(Weapon.Shotgun, loaded.HoodById(man.Id).Weapon);
        Assert.Equal(Car.Packard, loaded.HoodById(man.Id).Car);

        json["Version"] = 4;
        foreach (var l in json["Lots"]!.AsArray())
            if ((int)l!["Use"]! is (int)LotUse.GunShop or (int)LotUse.CarDealer) l["Use"] = (int)LotUse.Empty;
        var old = SaveGame.Restore(SaveGame.Read(json.ToJsonString())).World;
        Assert.Equal(w.Shops(LotUse.GunShop).Count(), old.Shops(LotUse.GunShop).Count());
        Assert.Equal(w.Shops(LotUse.CarDealer).Count(), old.Shops(LotUse.CarDealer).Count());
    }
}
