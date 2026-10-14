using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class PayoffTests
{
    private static (Simulation Sim, World W, Gang Me) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 1, CityEventsEnabled = false, DirectorEnabled = false });
        return (sim, sim.World, sim.World.Player);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    [Fact]
    public void KnowingWhereSomeoneLivesMakesASnatchLikelier()
    {
        var man = new Hood { Name = "a", Intimidation = 5, Muscle = 5, Stealth = 5, Brains = 5 };
        Assert.True(Simulation.LeanChance(man, Lean.Kidnap, 5, Fact.Home) > Simulation.LeanChance(man, Lean.Kidnap, 5));
        Assert.True(Simulation.LeanChance(man, Lean.Bribe, 8, Fact.Work | Fact.Origin) > Simulation.LeanChance(man, Lean.Bribe, 8));
        foreach (var lean in Enum.GetValues<Lean>())
            Assert.True(Simulation.LeanChance(man, lean, 5, Fact.All) >= Simulation.LeanChance(man, lean, 5));
    }

    [Fact]
    public void NerveCanOnlyBeJudgedOnceYouKnowThem()
    {
        var (_, w, me) = Start();
        Assert.False(w.ReadsNerve(me, 77));
        w.Learn(77, Fact.Name | Fact.Age | Fact.Job);
        Assert.False(w.ReadsNerve(me, 77));
        w.Learn(77, Fact.Home);
        Assert.True(w.ReadsNerve(me, 77));
        var a = w.Meet(78, 0);
        a.Standing = Standing.Scared;
        a.GangId = me.Id;
        Assert.True(w.ReadsNerve(me, 78));
    }

    [Fact]
    public void AShopkeeperInYourPocketTakesYourProtectionMoreReadily()
    {
        var (_, w, me) = Start();
        var biz = w.Businesses.First(b => !b.IsProtected);
        var hood = w.AvailableHoodsOf(me.Id).First();
        hood.Intimidation = biz.Toughness;
        double before = Simulation.ExtortChance(w, me, hood, biz);
        var a = w.Meet(99, w.LotOf(biz).WardId);
        a.BusinessId = biz.Id;
        a.Standing = Standing.Bought;
        a.GangId = me.Id;
        Assert.True(w.OwnerInPocket(me, biz));
        Assert.True(Simulation.ExtortChance(w, me, hood, biz) > before || before >= 0.95);
        a.Standing = Standing.None;
        Assert.False(w.OwnerInPocket(me, biz));
    }

    [Fact]
    public void ALandlordInYourPocketCountsForEveryBuildingHeOwns()
    {
        var (_, w, me) = Start();
        var a = w.Meet(500, 2);
        a.Standing = Standing.Bought;
        a.GangId = me.Id;
        a.Tenements = 3;
        Assert.Equal(4, w.InPocket(me, 2));
        Assert.Equal(0, w.InPocket(me, 3));
    }

    [Fact]
    public void ThePayrollIsPaidEveryWeekOrThePeopleOnItWalk()
    {
        var (sim, w, me) = Start();
        var a = w.Meet(500, 2);
        a.Standing = Standing.Bought;
        a.GangId = me.Id;
        a.Worth = 50;
        me.Cash = 1000;
        sim.AdvanceWeek(Mine(w));
        Assert.Equal(Standing.Bought, w.StandingOf(500));
        Assert.Contains(w.Events, e => e.Text.Contains($"Paid ${World.Retainer(50)} to the one man on your payroll"));
        me.Cash = 0;
        // Whatever comes in during the week, it isn't enough if he's owed more than the gang has.
        a.Worth = 1_000_000;
        sim.AdvanceWeek(Mine(w));
        Assert.Equal(Standing.None, w.StandingOf(500));
        Assert.Contains(w.Events, e => e.Text.Contains("went off your payroll"));
    }
}
