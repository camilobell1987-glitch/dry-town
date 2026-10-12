using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class MidWeekSaveTests
{
    /// <summary>The whole city as a save writes it, without the time it was written.</summary>
    private static string Snapshot(Simulation sim)
    {
        var data = SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()));
        data.SavedAt = default;
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    private static Simulation City(ulong seed, int weeks)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, Size = CitySize.Medium });
        for (int i = 0; i < weeks; i++) sim.AdvanceWeek(playerAutopilot: true);
        return sim;
    }

    private static Simulation Reload(Simulation sim) => SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>())));

    [Theory]
    [InlineData(3UL, 20, 0)]
    [InlineData(3UL, 20, 50)]
    [InlineData(8UL, 40, 100)]
    [InlineData(11UL, 60, 130)]
    public void AWeekSavedPartwayFinishesTheSameAsOneThatWasnt(ulong seed, int weeks, int hour)
    {
        var straight = City(seed, weeks);
        var saved = City(seed, weeks);
        foreach (var sim in new[] { straight, saved })
        {
            sim.BeginWeek(playerAutopilot: true);
            sim.RunUntil(hour);
        }
        var resumed = Reload(saved);
        Assert.True(resumed.WeekRunning);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(saved.World.Script), System.Text.Json.JsonSerializer.Serialize(resumed.World.Script));
        Assert.Equal(saved.StartOfWeek, resumed.StartOfWeek);
        Assert.Equal(saved.Upcoming.Select(j => j.Tick), resumed.Upcoming.Select(j => j.Tick));

        // A few more weeks, so anything the save lost would show.
        foreach (var sim in new[] { straight, resumed })
        {
            sim.FinishWeek();
            for (int i = 0; i < 3; i++) sim.AdvanceWeek(playerAutopilot: true);
        }
        Assert.Equal(Snapshot(straight), Snapshot(resumed));
    }

    [Fact]
    public void AnOrderGivenMidWeekSurvivesTheSave()
    {
        var sim = City(5, 10);
        sim.BeginWeek(new Dictionary<int, List<Order>> { [sim.World.Player.Id] = new() });
        sim.RunUntil(30);
        var w = sim.World;
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        var shop = w.Businesses.Where(b => b.IsOpen && b.ProtectorGangId != w.Player.Id).OrderBy(b => w.BlocksFromHq(w.Player, b)).First();
        int? at = sim.OrderNow(new ExtortOrder(w.Player.Id, man.Id, shop.Id), 30);
        Assert.NotNull(at);

        var resumed = Reload(sim);
        Assert.Contains(resumed.Upcoming, j => j.Order is ExtortOrder e && e.HoodId == man.Id && j.Tick == at);
        resumed.FinishWeek();
        Assert.Contains(resumed.World.Script, a => a.HoodId == man.Id && a.BusinessId == shop.Id);
    }

    [Fact]
    public void ASaveBetweenWeeksHasNoWeekInIt()
    {
        var sim = City(2, 3);
        var data = SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()));
        Assert.Null(data.InWeek);
        Assert.False(SaveGame.Restore(data).WeekRunning);

        sim.BeginWeek(playerAutopilot: true);
        sim.RunUntil(40);
        data = SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()));
        Assert.NotNull(data.InWeek);
        Assert.EndsWith(", Tuesday, 4 PM", data.Label);
    }
}
