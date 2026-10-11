using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class CityEventTests
{
    private static int WeekOf(int year, int weekOfYear) => (year - Content.StartYear) * Content.WeeksPerYear + weekOfYear - 1;

    [Fact]
    public void TheCrashAndTheDepressionComeOnTheCalendar()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4 });
        var w = sim.World;
        w.Week = WeekOf(1929, 44) - 1;
        sim.AdvanceWeek(playerAutopilot: true);
        Assert.True(w.Happening(CityEventKind.Crash));
        Assert.Contains(w.Events, e => e.Kind == EventKind.News && e.Text.StartsWith("Stocks have crashed"));

        while (w.Week < WeekOf(1930, 1)) sim.AdvanceWeek(playerAutopilot: true);
        Assert.False(w.Happening(CityEventKind.Crash));
        Assert.True(w.Happening(CityEventKind.Depression));
        var biz = w.Businesses[0];
        Assert.Equal(0.8, w.TakingsFactor(biz), 6);
        Assert.Equal(2, w.RacketFactor(RacketKind.LoanShark), 6);
        Assert.Equal(1, w.RacketFactor(RacketKind.Speakeasy), 6);
        Assert.Equal(WeekOf(1934, 1), w.CityEvents.Single().UntilWeek);
    }

    [Fact]
    public void HardTimesCutTheProtectionMoney()
    {
        long Protection(bool hard)
        {
            var sim = Simulation.New(new WorldSettings { Seed = 4, CityEventsEnabled = false });
            if (hard) sim.World.CityEvents.Add(new CityEvent { Kind = CityEventKind.Depression, StartWeek = 0, UntilWeek = 100 });
            sim.AdvanceWeek(playerAutopilot: true);
            return sim.World.LastLedger[sim.World.Player.Id].Protection;
        }
        long good = Protection(false), bad = Protection(true);
        Assert.True(bad < good);
        Assert.InRange(bad, good * 7 / 10, good * 9 / 10);
    }

    [Fact]
    public void ACrusadeAndAShakeupPutThePoliceOnEdge()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4 });
        var w = sim.World;
        int outrage = w.Hall.Outrage;
        sim.Begin(CityEventKind.Crusade);
        Assert.Equal(outrage + 15, w.Hall.Outrage);
        Assert.Equal(10, w.RaidSooner);

        var heat = w.LivingGangs.ToDictionary(g => g.Id, g => g.Heat);
        sim.Begin(CityEventKind.Shakeup);
        Assert.All(w.LivingGangs, g => Assert.Equal(Math.Min(100, heat[g.Id] + 10), g.Heat));
        Assert.True(w.HeatStays);

        sim.Begin(CityEventKind.DrySquad);
        Assert.Equal(2, w.RacketHeatFactor(RacketKind.Speakeasy));
        Assert.Equal(1, w.RacketHeatFactor(RacketKind.Numbers));
        sim.Begin(CityEventKind.TaxExample);
        Assert.Equal(2, w.TaxCaseFactor, 6);
    }

    [Fact]
    public void AFireShutsABlockAndAStrikeOneWard()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, Size = CitySize.Medium });
        var w = sim.World;
        var fire = sim.Begin(CityEventKind.Fire);
        var burned = w.Businesses.Where(b => b.BurnedWeek == w.Week).ToList();
        Assert.Equal(3, burned.Count);
        Assert.All(burned, b => { Assert.Equal(fire.WardId, w.LotOf(b).WardId); Assert.InRange(b.ShutWeeks, 6, 10); });
        Assert.Contains("A fire swept through a block of " + w.Wards[fire.WardId].Name + ".", w.Events.Select(e => e.Text));

        var strike = sim.Begin(CityEventKind.Strike, wardId: 1);
        Assert.Equal(0.5, w.TakingsFactor(w.Businesses.First(b => w.LotOf(b).WardId == 1)), 6);
        Assert.Equal(1, w.TakingsFactor(w.Businesses.First(b => w.LotOf(b).WardId == 0)), 6);
        Assert.Contains(w.Wards[1].Name, new CommandShell(sim).Execute("news"));
        Assert.Equal(1, strike.WardId);
    }

    [Fact]
    public void EventsComeAndGoOnTheirOwn()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 3, Size = CitySize.Medium });
        var w = sim.World;
        int mostAtOnce = 0;
        for (int i = 0; i < 3 * Content.WeeksPerYear; i++)
        {
            sim.AdvanceWeek(playerAutopilot: true);
            mostAtOnce = Math.Max(mostAtOnce, w.CityEvents.Count(e => Content.CityEvents[e.Kind].Weight > 0));
            Assert.All(w.CityEvents, e => Assert.True(e.UntilWeek > w.Week));
        }
        var news = w.Events.Where(e => e.Kind == EventKind.News).Select(e => e.Text).ToList();
        Assert.Contains(news, t => t.StartsWith("The Volstead Act"));
        int kinds = Content.CityEvents.Count(kv => kv.Value.Weight > 0 && news.Any(t => t == w.Describe(new CityEvent { WardId = -1 }, kv.Value.Headline) || kv.Value.Headline.Contains("{ward}") && t.StartsWith(kv.Value.Headline[..kv.Value.Headline.IndexOf("{ward}")])));
        Assert.True(kinds >= 3, $"only {kinds} kinds of event in three years");
        Assert.InRange(mostAtOnce, 1, 2);
    }

    [Fact]
    public void AQuietCityHasNoNews()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 3, CityEventsEnabled = false });
        for (int i = 0; i < Content.WeeksPerYear; i++) sim.AdvanceWeek(playerAutopilot: true);
        Assert.DoesNotContain(sim.World.Events, e => e.Kind == EventKind.News);
        Assert.Contains("Nothing out of the ordinary", new CommandShell(sim).Execute("news"));
    }

    [Fact]
    public void EventsSurviveASave()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4 });
        sim.Begin(CityEventKind.Strike, wardId: 1);
        sim.Begin(CityEventKind.Depression);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, new List<Order>()))).World;
        Assert.Equal(2, loaded.CityEvents.Count);
        Assert.Equal(1, loaded.CityEvents.Single(e => e.Kind == CityEventKind.Strike).WardId);
        Assert.Equal(sim.World.Week + 1, loaded.CityEventLast[CityEventKind.Depression]);
    }
}
