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
                // An ordinary man on the door, not the boss.
                var handler = rivalHoods[2];
                handler.Intimidation = handler.Muscle = 4;
                biz.HandlerHoodId = handler.Id;
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
        var crew = w.FormCrew(w.HoodsOf(w.Player.Id).First());
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
        var done = w.Script.Single(s => s.HoodId == hood.Id && s.BusinessId == target.Id && s.Kind != ActionKind.Collect);
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
        var action = w.Script.Single(s => s.HoodId == order.HoodId && s.Kind == ActionKind.Extort);
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

public class Phase4Tests
{
    private static Simulation Run(ulong seed, int weeks)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed });
        for (int i = 0; i < weeks; i++) sim.AdvanceWeek(playerAutopilot: true);
        return sim;
    }

    [Fact]
    public void OldMenAreLikelierToDie()
    {
        Assert.True(Content.YearlyDeathChance(30) < 0.01);
        Assert.True(Content.YearlyDeathChance(50) < Content.YearlyDeathChance(65));
        Assert.True(Content.YearlyDeathChance(80) > 0.2);
    }

    [Fact]
    public void OverDecadesMenDieOfOldAgeAndGangsCarryOn()
    {
        var sim = Run(2, 40 * Content.WeeksPerYear);
        Assert.Contains(sim.World.Events, e => e.Kind == EventKind.DiedNaturally);
        Assert.Contains(sim.World.Events, e => e.Kind == EventKind.Succession);
        Assert.False(sim.Metrics.Check().Stalled);
    }

    [Fact]
    public void TheNamedHeirTakesOverWhenTheBossDies()
    {
        var sim = Run(8, 10);
        var w = sim.World;
        var gang = w.Player;
        var heir = w.AvailableHoodsOf(gang.Id).Where(h => h.Id != gang.BossHoodId).OrderBy(h => h.Brains).First();
        var shell = new CommandShell(sim);
        shell.Execute($"heir {heir.Id}");
        Assert.Equal(heir.Id, gang.HeirHoodId);
        w.HoodById(gang.BossHoodId).State = HoodState.Dead;
        sim.AdvanceWeek();
        if (heir.IsAvailable) Assert.Equal(heir.Id, gang.BossHoodId);
        else Assert.NotEqual(heir.Id, gang.HeirHoodId);
    }

    [Fact]
    public void FamilyComesInYoungAndLoyalOnceAYear()
    {
        var sim = Run(12, 4);
        var w = sim.World;
        w.Player.Cash = 5000;
        var shell = new CommandShell(sim);
        shell.Execute("family");
        Assert.Single(shell.Pending);
        shell.EndWeek();
        var relative = w.HoodsOf(w.Player.Id).Single(h => h.Family);
        Assert.Equal(World.Surname(w.HoodById(w.Player.BossHoodId).Name), World.Surname(relative.Name));
        Assert.InRange(relative.Age(w.Week), 17, 22);
        Assert.Contains("once a year", shell.Execute("family"));
    }

    [Fact]
    public void SavesFromBeforeAgesGetPlausibleAges()
    {
        var sim = Run(5, 20);
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveGame.Write(sim, new List<Order>()))!;
        json["Version"] = 1;
        foreach (var h in json["Hoods"]!.AsArray()) h!.AsObject().Remove("BornWeek");
        var loaded = SaveGame.Restore(SaveGame.Read(json.ToJsonString()));
        foreach (var h in loaded.World.Hoods) Assert.InRange(h.Age(loaded.World.Week), 18, 60);
    }
}

public class Phase5Tests
{
    private static Simulation Run(ulong seed, int weeks, CitySize size = CitySize.Small)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, Size = size });
        for (int i = 0; i < weeks; i++) sim.AdvanceWeek(playerAutopilot: true);
        return sim;
    }

    [Theory]
    [InlineData(CitySize.Small, 5, 4, 2, 1, 3)]
    [InlineData(CitySize.Medium, 7, 5, 4, 2, 4)]
    [InlineData(CitySize.Large, 9, 7, 6, 3, 5)]
    public void CitiesComeInThreeSizes(CitySize size, int blocksX, int blocksY, int wards, int precincts, int gangs)
    {
        var w = World.Create(new WorldSettings { Seed = 3, Size = size });
        Assert.Equal(blocksX, w.Map.BlocksX);
        Assert.Equal(blocksY, w.Map.BlocksY);
        Assert.Equal(wards, w.Wards.Count);
        Assert.Equal(precincts, w.Map.Lots.Count(l => l.Use == LotUse.Precinct));
        Assert.Equal(gangs, w.LivingGangs.Count());
        Assert.Equal(Content.Shape(size).Businesses, w.Businesses.Count);
        // Every ward has shops in it, and every walk stays on the streets.
        foreach (var ward in w.Wards) Assert.True(Politics.BusinessesIn(w, ward).Count() >= 10);
        var a = w.Map.Lots.First();
        var b = w.Map.Lots.Last();
        foreach (var (x, y) in w.Map.Path(a, b).Skip(1).SkipLast(1)) Assert.True(CityMap.IsRoad(x, y));
    }

    [Fact]
    public void BigCitiesStayContestedAndRunFast()
    {
        var sim = Run(4, 3 * Content.WeeksPerYear, CitySize.Large);
        Assert.True(sim.World.LivingGangs.Count() >= 3);
        Assert.False(sim.Metrics.Check().Stalled);
    }

    [Fact]
    public void AnAldermanOnThePayrollTipsOffRacketsInHisWard()
    {
        int raids = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 2, DirectorEnabled = false });
            var w = sim.World;
            var me = w.Player;
            var ward = w.Wards[0];
            ward.OwnerGangId = me.Id;
            var site = Politics.BusinessesIn(w, ward).First(b => b.IsOpen);
            site.ProtectorGangId = me.Id;
            site.HandlerHoodId = w.HoodsOf(me.Id).First().Id;
            site.ProtectionRate = 12;
            site.Racket = RacketKind.Numbers;
            me.Heat = 100;
            me.Cash = 50_000;
            sim.AdvanceWeek(new Dictionary<int, List<Order>> { [me.Id] = new() });
            if (w.Events.Any(e => e.Kind == EventKind.Raid && e.GangId == me.Id && e.Text.Contains("tipped off"))) raids++;
            Assert.Equal(RacketKind.Numbers, site.Racket);
        }
        Assert.True(raids > 0);
    }

    [Fact]
    public void ABoughtAldermanStaysBoughtForAWhile()
    {
        var sim = Run(6, 2);
        var w = sim.World;
        var me = w.Player;
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var ward = w.Wards[0];
        me.Cash = rival.Cash = 20_000;
        var shell = new CommandShell(sim);
        long cost = Politics.PayoffCost(w, ward, me);
        Assert.StartsWith("Queued", shell.Execute($"payoff {ward.Id}"));
        shell.EndWeek();
        Assert.Equal(me.Id, ward.OwnerGangId);
        Assert.True(Politics.StaysBought(w, ward, rival));
        Assert.False(Politics.Payoff(w, rival, ward));
        Assert.Equal(cost * 2, Politics.PayoffCost(w, ward, rival));
        w.Week += Content.AldermanLoyalWeeks;
        Assert.True(Politics.Payoff(w, rival, ward));
        Assert.Equal(rival.Id, ward.OwnerGangId);
    }

    [Fact]
    public void ReformersWontTakeTheMoney()
    {
        var sim = Run(7, 1);
        var w = sim.World;
        w.Wards[1].Reformer = true;
        w.Player.Cash = 20_000;
        int heat = w.Player.Heat;
        Assert.False(Politics.Payoff(w, w.Player, w.Wards[1]));
        Assert.Equal(-1, w.Wards[1].OwnerGangId);
        Assert.Equal(20_000, w.Player.Cash);
        Assert.True(w.Player.Heat > heat);
    }

    [Fact]
    public void ElectionsComeOnScheduleAndCampaignsOnlyBeforeThem()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 8 });
        var w = sim.World;
        Assert.Equal(Content.AldermanElectionWeek - 1, Politics.NextElection(w, ElectionKind.Alderman));
        Assert.Equal(3 * Content.WeeksPerYear + Content.MayorElectionWeek - 1, Politics.NextElection(w, ElectionKind.Mayor));
        w.Player.Cash = 5000;
        Assert.Equal(0, Politics.Campaign(w, w.Player, 0, 500));
        Assert.Contains("no election", new CommandShell(sim).Execute("campaign 0 500"));

        while (w.Week < Content.AldermanElectionWeek - Content.CampaignWeeks) sim.AdvanceWeek(playerAutopilot: true);
        Assert.Equal(ElectionKind.Alderman, Politics.Campaigning(w)!.Value.Kind);
        w.Player.Cash = 5000;
        Assert.Equal(0, Politics.Campaign(w, w.Player, -1, 500)); // not a mayor's year
        Assert.Equal(500, Politics.Campaign(w, w.Player, 0, 500));
        while (w.Week < Content.AldermanElectionWeek) sim.AdvanceWeek(playerAutopilot: true);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Politics && e.Week == Content.AldermanElectionWeek - 1 && (e.Text.Contains(" won ") || e.Text.Contains(" held ")));
        Assert.All(w.Wards, x => Assert.Empty(x.Campaign));

        int mayorWeek = Politics.NextElection(w, ElectionKind.Mayor);
        while (w.Week <= mayorWeek) sim.AdvanceWeek(playerAutopilot: true);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Politics && e.Text.Contains("mayor", StringComparison.OrdinalIgnoreCase) && e.Year(w) == 1923);
    }

    [Fact]
    public void SavesFromBeforePoliticsGetWards()
    {
        var sim = Run(5, 20);
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveGame.Write(sim, new List<Order>()))!;
        json["Version"] = 2;
        json.AsObject().Remove("Wards");
        json.AsObject().Remove("Hall");
        json.AsObject().Remove("WardsX");
        json.AsObject().Remove("WardsY");
        foreach (var l in json["Lots"]!.AsArray()) l!.AsObject().Remove("WardId");
        var loaded = SaveGame.Restore(SaveGame.Read(json.ToJsonString())).World;
        Assert.Equal(2, loaded.Wards.Count);
        Assert.False(string.IsNullOrEmpty(loaded.Hall.Mayor));
        Assert.Contains(loaded.Map.Lots, l => l.WardId == 1);
        Assert.Contains(loaded.Map.Lots, l => l.WardId == 0);
    }

    [Fact]
    public void PoliticsSurviveASave()
    {
        var sim = Run(9, 30);
        var w = sim.World;
        w.Wards[0].OwnerGangId = w.Player.Id;
        w.Hall.Outrage = 42;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, new List<Order>()))).World;
        Assert.Equal(w.Player.Id, loaded.Wards[0].OwnerGangId);
        Assert.Equal(42, loaded.Hall.Outrage);
        Assert.Equal(w.Wards.Select(x => x.Alderman), loaded.Wards.Select(x => x.Alderman));
    }
}

internal static class EventYear
{
    public static int Year(this GameEvent e, World w) => Content.StartYear + e.Week / Content.WeeksPerYear;
}
