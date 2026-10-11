using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class ScenarioTests
{
    private static Simulation Start(ScenarioKind kind, ulong seed = 1) => Simulation.New(Scenarios.Settings(kind, seed));

    private static Dictionary<int, List<Order>> Nothing(World w) => new() { [w.Player.Id] = new List<Order>() };

    [Fact]
    public void EveryScenarioStartsWithItsGoal()
    {
        foreach (var info in Scenarios.All)
        {
            var w = Start(info.Kind).World;
            Assert.Equal(info.Size, w.Settings.Size);
            Assert.Equal(info.StartYear, w.Year);
            Assert.Equal(ScenarioOutcome.Playing, w.Goal.Outcome);
            Assert.Contains(w.Events, e => e.Kind == EventKind.Era && e.Text.StartsWith(info.Title));
            Assert.StartsWith(info.Title, Scenarios.Summary(w));
        }
    }

    [Fact]
    public void TheOpenCityHasNoGoal()
    {
        var w = Simulation.New(new WorldSettings { Seed = 1 }).World;
        Assert.Equal(ScenarioKind.None, w.Settings.Scenario);
        Assert.Equal("", Scenarios.Summary(w));
        Assert.Contains("open city", new CommandShell(new Simulation(w)).Execute("goal"));
    }

    [Fact]
    public void LastCallStartsIn1932InTheDepression()
    {
        var w = Start(ScenarioKind.LastCall).World;
        Assert.Equal(1932, w.Year);
        Assert.True(w.Prohibition);
        Assert.InRange(w.HoodById(w.Player.BossHoodId).Age(w.Week), 32, 50);
        Assert.True(w.Happening(CityEventKind.Depression));
        Assert.Equal(Scenarios.Deadline(Scenarios.Info(ScenarioKind.LastCall)), w.CityEvents.Single(e => e.Kind == CityEventKind.Depression).UntilWeek);
        // Rivals size up a newcomer from the year the city starts, not from 1920.
        Assert.Equal(w.Settings.RivalWariness(0), w.RivalWariness);
    }

    [Fact]
    public void TheCrusadeStartsWithYouOnTop()
    {
        var w = Start(ScenarioKind.SurviveTheCrusade).World;
        Assert.Equal(Scenarios.CrusadeTurf, w.TurfOf(w.Player.Id).Count());
        Assert.Equal(Scenarios.CrusadeTurf, w.Goal.Baseline);
        Assert.True(w.Happening(CityEventKind.Crusade));
        Assert.Contains(w.Events, e => e.Text == $"The Tribune's front page names {w.Player.Name} as the worst outfit in the city.");
        Assert.True(Scenarios.Met(w));
    }

    [Fact]
    public void TheCrusadeOnlyCountsAtTheEnd()
    {
        var sim = Start(ScenarioKind.SurviveTheCrusade);
        var w = sim.World;
        sim.AdvanceWeek(Nothing(w));
        Assert.Equal(ScenarioOutcome.Playing, w.Goal.Outcome);

        w.Week = Scenarios.Deadline(Scenarios.Info(ScenarioKind.SurviveTheCrusade)) - 1;
        // Every shop in the city pays the player, so a raid or two in the last week can't spoil it.
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        foreach (var b in w.Businesses.Where(b => b.IsOpen))
        {
            b.ProtectorGangId = w.Player.Id;
            b.HandlerHoodId = man.Id;
        }
        w.Player.Cash = 50_000;
        sim.AdvanceWeek(Nothing(w));
        Assert.Equal(ScenarioOutcome.Won, w.Goal.Outcome);
    }

    [Fact]
    public void TheWardIsWonOnceItIsYours()
    {
        var sim = Start(ScenarioKind.TakeTheWard);
        var w = sim.World;
        var me = w.Player;
        var ward = w.Wards[w.Goal.Baseline];
        Assert.Equal(w.HqOf(me).WardId, ward.Id);
        Assert.False(Scenarios.Met(w));

        ward.OwnerGangId = me.Id;
        ward.BoughtWeek = w.Week;
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        foreach (var b in w.Businesses.Where(b => w.LotOf(b).WardId == ward.Id))
        {
            b.ProtectorGangId = me.Id;
            b.HandlerHoodId = man.Id;
        }
        me.Cash = 50_000;
        sim.AdvanceWeek(Nothing(w));
        Assert.Equal(ScenarioOutcome.Won, w.Goal.Outcome);
        Assert.Contains(w.Events, e => e.Text.StartsWith("Take the Ward is won"));
    }

    [Fact]
    public void AScenarioIsLostWhenTimeRunsOut()
    {
        var sim = Start(ScenarioKind.OffTheBoat);
        var w = sim.World;
        w.Week = Scenarios.Deadline(Scenarios.Info(ScenarioKind.OffTheBoat)) - 1;
        sim.AdvanceWeek(Nothing(w));
        Assert.Equal(ScenarioOutcome.Lost, w.Goal.Outcome);
        Assert.Contains("Lost in", Scenarios.Summary(w));

        // Once it's decided, it stays decided, and the city goes on.
        sim.AdvanceWeek(Nothing(w));
        Assert.Equal(ScenarioOutcome.Lost, w.Goal.Outcome);
    }

    [Fact]
    public void AScenarioIsLostWhenTheOutfitIsFinished()
    {
        var sim = Start(ScenarioKind.Waterfront);
        var w = sim.World;
        w.Player.Alive = false;
        sim.AdvanceWeek();
        Assert.Equal(ScenarioOutcome.Lost, w.Goal.Outcome);
    }

    [Fact]
    public void TheWaterfrontStartsWithTheDocksInARivalsHands()
    {
        var w = Start(ScenarioKind.Waterfront).World;
        int docks = w.DocksGangId;
        Assert.True(docks >= 0 && docks != w.Player.Id);
        foreach (var l in w.Locals.Take(2)) l.OwnerGangId = w.Player.Id;
        Assert.True(Scenarios.Met(w));
    }

    [Fact]
    public void ScenariosAreSaved()
    {
        var sim = Start(ScenarioKind.LastCall);
        sim.World.Goal.Outcome = ScenarioOutcome.Won;
        sim.World.Goal.Week = 640;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(ScenarioKind.LastCall, loaded.Settings.Scenario);
        Assert.Equal(sim.World.Settings.StartWeek, loaded.Settings.StartWeek);
        Assert.Equal(ScenarioOutcome.Won, loaded.Goal.Outcome);
        Assert.Equal(640, loaded.Goal.Week);
    }

    [Fact]
    public void TheConsoleShowsTheGoal()
    {
        var shell = new CommandShell(Start(ScenarioKind.LastCall));
        Assert.Contains("Last Call", shell.Welcome);
        Assert.Contains("clean money", shell.Execute("goal"));
        Assert.Contains("104 weeks left", shell.Execute("goal"));
    }
}
