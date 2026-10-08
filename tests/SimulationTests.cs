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

public class Phase2Tests
{
    [Fact]
    public void PlayerUsuallySurvivesTheFirstThreeYearsOnNormal()
    {
        int alive = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var sim = Simulation.New(new WorldSettings { Seed = seed });
            for (int i = 0; i < 3 * Content.WeeksPerYear && sim.World.Player.Alive; i++) sim.AdvanceWeek(playerAutopilot: true);
            if (sim.World.Player.Alive) alive++;
        }
        Assert.True(alive >= 15, $"only {alive}/20 player gangs survived three years");
    }

    [Fact]
    public void WeekScriptIsInTimeOrderAndOnTheMap()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4 });
        var w = sim.World;
        for (int i = 0; i < 10; i++) sim.AdvanceWeek(playerAutopilot: true);
        Assert.NotEmpty(w.Script);
        Assert.Equal(w.Script.Select(a => a.Tick).OrderBy(t => t), w.Script.Select(a => a.Tick));
        Assert.All(w.Script, a =>
        {
            Assert.InRange(a.Tick, 0, Content.HoursPerWeek - 1);
            Assert.InRange(a.FromLot, 0, w.Map.Lots.Count - 1);
            Assert.InRange(a.ToLot, 0, w.Map.Lots.Count - 1);
        });
        Assert.Contains(w.Script, a => a.Kind == ActionKind.Collect);
    }

    [Fact]
    public void PathsFollowTheStreets()
    {
        var map = World.Create(new WorldSettings { Seed = 2 }).Map;
        var a = map.Lots.First();
        var b = map.Lots.Last();
        var path = map.Path(a, b);
        Assert.Equal((a.X, a.Y), path[0]);
        Assert.Equal((b.X, b.Y), path[^1]);
        foreach (var (x, y) in path.Skip(1).SkipLast(1)) Assert.True(CityMap.IsRoad(x, y), $"({x},{y}) is not a road");
        for (int i = 1; i < path.Count; i++) Assert.True(path[i].X == path[i - 1].X || path[i].Y == path[i - 1].Y, "diagonal step");
    }

    [Fact]
    public void EveryGangHasItsOwnHeadquarters()
    {
        var w = World.Create(new WorldSettings { Seed = 8 });
        var hqs = w.LivingGangs.Select(g => g.HqLotId).ToList();
        Assert.Equal(hqs.Count, hqs.Distinct().Count());
        Assert.All(hqs, id => Assert.Equal(LotUse.Headquarters, w.Map.LotAt(id).Use));
        Assert.All(w.Businesses, b => Assert.Equal(b.Id, w.Map.LotAt(b.LotId).BusinessId));
    }

    [Fact]
    public void GuardDefendsHisPost()
    {
        // A strong guard should repel a weak attacker more often than an unguarded handler does.
        int RepelledOutOf(bool guarded)
        {
            int repelled = 0;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 2, DirectorEnabled = false });
                var w = sim.World;
                var me = w.Player;
                var rival = w.Gangs[1];
                var biz = w.Businesses.First();
                biz.ProtectorGangId = rival.Id;
                var rivalHoods = w.HoodsOf(rival.Id).ToList();
                biz.HandlerHoodId = rivalHoods[0].Id;
                var guard = rivalHoods[1];
                guard.Intimidation = guard.Muscle = 9;
                var attacker = w.HoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
                attacker.Intimidation = attacker.Muscle = 6;
                var orders = new Dictionary<int, List<Order>>
                {
                    [me.Id] = new() { new ExtortOrder(me.Id, attacker.Id, biz.Id) },
                    [rival.Id] = guarded ? new() { new GuardOrder(rival.Id, guard.Id, biz.Id) } : new(),
                };
                sim.AdvanceWeek(orders);
                if (biz.ProtectorGangId == rival.Id) repelled++;
            }
            return repelled;
        }
        Assert.True(RepelledOutOf(guarded: true) > RepelledOutOf(guarded: false));
    }
}

public class Phase3Tests
{
    private static Simulation Run(ulong seed, int weeks)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed });
        for (int i = 0; i < weeks; i++) sim.AdvanceWeek(playerAutopilot: true);
        return sim;
    }

    [Fact]
    public void SavedGameCarriesOnExactlyAsIfNeverStopped()
    {
        var original = Run(11, 40);
        var w = original.World;
        var crew = w.FormCrew(w.AvailableHoodsOf(w.Player.Id).First());
        var pending = new List<Order> { new RecruitOrder(w.Player.Id), new BribeOrder(w.Player.Id, 200) };

        string json = SaveGame.Write(original, pending);
        var data = SaveGame.Read(json);
        var loaded = SaveGame.Restore(data);

        Assert.Equal(pending, data.Pending);
        Assert.Equal(crew.LieutenantHoodId, loaded.World.CrewsOf(w.Player.Id).Single().LieutenantHoodId);
        for (int i = 0; i < 40; i++)
        {
            original.AdvanceWeek(playerAutopilot: true);
            loaded.AdvanceWeek(playerAutopilot: true);
        }
        Assert.Equal(original.World.Rng.State, loaded.World.Rng.State);
        Assert.Equal(original.World.Events.Select(e => e.Text), loaded.World.Events.Select(e => e.Text));
        Assert.Equal(original.World.Businesses.Select(b => b.ProtectorGangId), loaded.World.Businesses.Select(b => b.ProtectorGangId));
    }

    [Fact]
    public void CantSaveMidWeek()
    {
        var sim = Run(3, 2);
        sim.BeginWeek();
        Assert.Throws<InvalidOperationException>(() => SaveGame.Write(sim, new List<Order>()));
    }

    [Fact]
    public void RunningAWeekHourByHourMatchesRunningItAtOnce()
    {
        var a = Run(5, 20);
        var b = Run(5, 20);
        a.AdvanceWeek(playerAutopilot: true);
        b.BeginWeek(playerAutopilot: true);
        for (int tick = 0; tick < Content.CollectionTick; tick += 7) b.RunUntil(tick);
        b.FinishWeek();
        Assert.Equal(a.World.Rng.State, b.World.Rng.State);
        Assert.Equal(a.World.Script.Select(s => s.Text), b.World.Script.Select(s => s.Text));
    }

    [Fact]
    public void OrdersGivenMidWeekHappenWhenTheMenArrive()
    {
        var sim = Run(9, 4);
        var w = sim.World;
        sim.BeginWeek();
        sim.RunUntil(40);
        var hood = w.AvailableHoodsOf(w.Player.Id).First();
        var target = w.Businesses.Where(b => !b.IsProtected && b.IsOpen).OrderBy(b => w.BlocksFromHq(w.Player, b)).First();
        int? tick = sim.OrderNow(new ExtortOrder(w.Player.Id, hood.Id, target.Id), 40.5f);
        Assert.NotNull(tick);
        Assert.True(tick > 40);
        Assert.Contains(hood.Id, sim.CommittedHoods(w.Player.Id));
        sim.FinishWeek();
        var done = w.Script.Single(s => s.HoodId == hood.Id && s.BusinessId == target.Id);
        Assert.Equal(tick, done.Tick);
        Assert.Null(NewOrderAfterHours(Run(9, 4)));
    }

    private static int? NewOrderAfterHours(Simulation sim)
    {
        var w = sim.World;
        sim.BeginWeek();
        sim.RunUntil(Simulation.LastOrderTick);
        var hood = w.AvailableHoodsOf(w.Player.Id).First();
        return sim.OrderNow(new ExtortOrder(w.Player.Id, hood.Id, w.Businesses.First(b => !b.IsProtected).Id), Simulation.LastOrderTick);
    }

    [Fact]
    public void ACrewHitsHarderAndTiesUpEveryMan()
    {
        var sim = Run(4, 3);
        var w = sim.World;
        var shell = new CommandShell(sim);
        var men = w.AvailableHoodsOf(w.Player.Id).Where(h => h.Id != w.Player.BossHoodId).Take(3).ToList();
        var crew = w.FormCrew(men[0]);
        Assert.True(w.JoinCrew(crew, men[1]));
        Assert.True(w.JoinCrew(crew, men[2]));

        var biz = w.Businesses.First(b => !b.IsProtected && b.IsOpen && Simulation.ExtortChance(w, w.Player, men[0], b) is > 0.1 and < 0.8);
        Assert.True(Simulation.ExtortChance(w, w.Player, men[0], biz, 2) > Simulation.ExtortChance(w, w.Player, men[0], biz));
        Assert.True(Simulation.BackupStrength(men.Skip(1)) > 0);

        shell.Execute($"send {crew.Id} {biz.Id}");
        var order = Assert.IsType<ExtortOrder>(shell.Pending.Single());
        Assert.Equal(men.Select(h => h.Id).ToHashSet(), order.Team.ToHashSet());
        shell.EndWeek();
        var action = w.Script.Single(s => s.HoodId == men[0].Id && s.Kind == ActionKind.Extort);
        Assert.Equal(2, action.Backup.Count);
    }

    [Fact]
    public void ALieutenantWhoBreaksAwayTakesHisCrew()
    {
        var sim = Run(6, 10);
        var w = sim.World;
        var men = w.AvailableHoodsOf(w.Player.Id).Where(h => h.Id != w.Player.BossHoodId).Take(3).ToList();
        var crew = w.FormCrew(men[0]);
        w.JoinCrew(crew, men[1]);
        w.JoinCrew(crew, men[2]);
        men[1].Loyalty = men[2].Loyalty = 90;
        var gang = sim.Breakaway(w.Player, men[0]);
        Assert.Equal(gang.Id, men[1].GangId);
        Assert.Equal(gang.Id, men[2].GangId);
        Assert.Empty(w.CrewsOf(w.Player.Id));
    }
}
