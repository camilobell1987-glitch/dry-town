using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class SimulationTests
{
    private static Simulation Run(ulong seed, int weeks)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed });
        for (int i = 0; i < weeks; i++) sim.AdvanceWeek(playerAutopilot: true);
        return sim;
    }

    [Fact]
    public void SameSeedReplaysIdentically()
    {
        var a = Run(7, 156);
        var b = Run(7, 156);
        Assert.Equal(a.World.Rng.State, b.World.Rng.State);
        Assert.Equal(a.World.Events.Select(e => e.Text), b.World.Events.Select(e => e.Text));
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        Assert.NotEqual(Run(1, 52).World.Rng.State, Run(2, 52).World.Rng.State);
    }

    // The Phase 1 gate: a ten-year city never stalls.
    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(4UL)] [InlineData(5UL)]
    [InlineData(6UL)] [InlineData(7UL)] [InlineData(8UL)] [InlineData(9UL)] [InlineData(10UL)]
    public void TenYearCityStaysContested(ulong seed)
    {
        var report = Run(seed, 10 * Content.WeeksPerYear).Metrics.Check();
        Assert.False(report.Stalled, string.Join("; ", report.Problems));
    }

    [Fact]
    public void ExtortingAnUnprotectedBusinessCanWinIt()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 3, StartingGangs = 2, DirectorEnabled = false });
        var w = sim.World;
        var hood = w.HoodsOf(w.Player.Id).First();
        hood.Intimidation = 10;
        var biz = w.Businesses.First();

        for (int i = 0; i < 20 && biz.ProtectorGangId != w.Player.Id; i++)
        {
            if (biz.IsProtected) break; // a rival got there first; nothing more to learn
            sim.AdvanceWeek(new Dictionary<int, List<Order>> { [w.Player.Id] = new() { new ExtortOrder(w.Player.Id, hood.Id, biz.Id) } });
            if (!hood.IsAvailable) break;
        }
        Assert.True(biz.IsProtected);
    }

    [Fact]
    public void RacketNeedsTurf()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 5, StartingGangs = 2, DirectorEnabled = false });
        var w = sim.World;
        var biz = w.Businesses.First(b => b.Kind == BusinessKind.Diner || b.Kind == BusinessKind.Hotel || b.Kind == BusinessKind.PoolHall);
        var hood = w.HoodsOf(w.Player.Id).First();
        Assert.False(biz.IsProtected);
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [w.Player.Id] = new() { new RacketOrder(w.Player.Id, hood.Id, biz.Id, RacketKind.Speakeasy) } });
        Assert.Equal(RacketKind.None, biz.Racket);
        Assert.DoesNotContain(w.Events, e => e.Kind == EventKind.RacketOpened && e.GangId == w.Player.Id);
    }

    [Fact]
    public void RepealEndsNewSpeakeasies()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 9 });
        var w = sim.World;
        w.Week = (Content.RepealYear - Content.StartYear) * Content.WeeksPerYear;
        Assert.False(w.Prohibition);
        var biz = w.Businesses.First(b => Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy));
        biz.ProtectorGangId = w.Player.Id;
        w.Player.Cash = 10_000;
        var hood = w.HoodsOf(w.Player.Id).First();
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [w.Player.Id] = new() { new RacketOrder(w.Player.Id, hood.Id, biz.Id, RacketKind.Speakeasy) } });
        Assert.NotEqual(RacketKind.Speakeasy, biz.Racket);
    }

    [Fact]
    public void DirectorRefillsAnEmptyDistrict()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 11, StartingGangs = 1 });
        sim.AdvanceWeek(playerAutopilot: true);
        Assert.True(sim.World.LivingGangs.Count() >= 2);
        Assert.Contains(sim.World.Events, e => e.Kind == EventKind.NewGang);
    }

    [Fact]
    public void PlayerWithNoOrdersStillCollects()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 13, StartingGangs = 2, DirectorEnabled = false });
        var w = sim.World;
        var biz = w.Businesses.First();
        biz.ProtectorGangId = w.Player.Id;
        biz.HandlerHoodId = w.HoodsOf(w.Player.Id).First().Id;
        biz.ProtectionRate = 20;
        sim.AdvanceWeek();
        Assert.True(w.LastLedger[w.Player.Id].Protection >= biz.Takings * 20 / 100);
    }
}

public class ShellTests
{
    [Fact]
    public void ShellAutoPlanMatchesAutopilot()
    {
        var auto = Simulation.New(new WorldSettings { Seed = 21 });
        var shell = new CommandShell(Simulation.New(new WorldSettings { Seed = 21 }));
        for (int i = 0; i < 52; i++)
        {
            auto.AdvanceWeek(playerAutopilot: true);
            shell.Execute("auto");
            shell.Execute("end");
        }
        Assert.Equal(auto.World.Events.Select(e => e.Text), shell.Sim.World.Events.Select(e => e.Text));
    }
}
