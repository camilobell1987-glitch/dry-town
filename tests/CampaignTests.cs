using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class CampaignTests
{
    private static Dictionary<int, List<Order>> Nothing(World w) => new() { [w.Player.Id] = new List<Order>() };

    /// <summary>Settle the chapter as if it were played out, and go on to the next.</summary>
    private static Simulation GoOn(World w, ScenarioOutcome outcome, ulong seed = 9)
    {
        w.Goal.Outcome = outcome;
        w.Goal.Week = w.Week;
        return Simulation.New(Campaign.Next(w, seed));
    }

    [Fact]
    public void ACampaignStartsOffTheBoatIn1920()
    {
        var w = Simulation.New(Campaign.Start(1)).World;
        Assert.Equal(ScenarioKind.OffTheBoat, w.Settings.Scenario);
        Assert.Equal(0, w.Settings.Campaign!.Chapter);
        Assert.Equal(1920, w.Year);
        Assert.Equal(500, w.Player.Cash);
        Assert.StartsWith("Chapter 1 of 5. Off the Boat: Be the biggest outfit in the city by 1924", Scenarios.Summary(w));
        Assert.Contains("1. Off the Boat, 1920 to 1924: playing", Campaign.Record(w));
    }

    [Fact]
    public void TheBossAndHisOutfitGoOnToTheNextCity()
    {
        var w = Simulation.New(Campaign.Start(1)).World;
        var boss = w.HoodById(w.Player.BossHoodId);
        Assert.NotNull(Campaign.CannotGoOn(w));
        var next = GoOn(w, ScenarioOutcome.Won).World;
        var carried = next.HoodById(next.Player.BossHoodId);

        Assert.Equal(ScenarioKind.TakeTheWard, next.Settings.Scenario);
        Assert.Equal(1924, next.Year);
        Assert.Equal(w.Player.Name, next.Player.Name);
        Assert.Equal((boss.Name, boss.Origin, boss.BornWeek, boss.Intimidation, boss.Muscle, boss.Brains, boss.Stealth),
            (carried.Name, carried.Origin, carried.BornWeek, carried.Intimidation, carried.Muscle, carried.Brains, carried.Stealth));
        Assert.Equal(boss.Age(w.Week) + 4, carried.Age(next.Week));
        // His men are mostly his own people, as in any outfit.
        Assert.Equal(boss.Origin, next.PeopleOf(next.Player));
        Assert.Equal(new[] { ScenarioOutcome.Won }, next.Settings.Campaign!.Results);
        // The goal and the deadline are the chapter's, not the scenario's own.
        Assert.Equal((1927 - Content.StartYear) * Content.WeeksPerYear, Scenarios.Deadline(next));
        Assert.Contains("by 1927", Scenarios.Goal(next));
        Assert.Contains(next.Events, e => e.Text.StartsWith($"In 1924 a party man in another city sends for {boss.Name}."));
    }

    [Fact]
    public void EveryChapterWonSendsWordAhead()
    {
        var w = Simulation.New(Campaign.Start(1)).World;
        var won = GoOn(w, ScenarioOutcome.Won).World;
        var lost = GoOn(w, ScenarioOutcome.Lost).World;
        Assert.Equal(Campaign.WordAhead, won.Player.Cash - lost.Player.Cash);
    }

    [Fact]
    public void AFinishedOutfitCanOnlyTryAgain()
    {
        var sim = GoOn(Simulation.New(Campaign.Start(1)).World, ScenarioOutcome.Won);
        var w = sim.World;
        w.Player.Alive = false;
        sim.AdvanceWeek(Nothing(w));
        Assert.Equal(ScenarioOutcome.Lost, w.Goal.Outcome);
        Assert.Contains("is finished", Campaign.CannotGoOn(w));

        var again = Simulation.New(Campaign.Retry(w, 5)).World;
        Assert.Equal(ScenarioKind.TakeTheWard, again.Settings.Scenario);
        Assert.Equal(w.Settings.Campaign!.Results, again.Settings.Campaign!.Results);
        Assert.Equal(w.Settings.Campaign.Boss!.Name, again.HoodById(again.Player.BossHoodId).Name);
        Assert.Equal(ScenarioOutcome.Playing, again.Goal.Outcome);
    }

    [Fact]
    public void TheCrusadeRunsToItsChaptersDeadline()
    {
        var sim = Simulation.New(Campaign.Start(1));
        for (int i = 0; i < 3; i++) sim = GoOn(sim.World, ScenarioOutcome.Won);
        var w = sim.World;
        Assert.Equal(ScenarioKind.SurviveTheCrusade, w.Settings.Scenario);
        Assert.Equal(1930, w.Year);
        Assert.Contains("at the start of 1932", Scenarios.Goal(w));
        w.Week = Scenarios.Deadline(w) - 1;
        w.Player.Cash = 50_000;
        foreach (var b in w.Businesses.Where(b => b.IsOpen)) b.ProtectorGangId = w.Player.Id;
        sim.AdvanceWeek(Nothing(w));
        Assert.Equal(ScenarioOutcome.Won, w.Goal.Outcome);
        Assert.Equal(1932, w.Year);
    }

    [Fact]
    public void TheLastChapterEndsTheCampaign()
    {
        var sim = Simulation.New(Campaign.Start(1));
        for (int i = 0; i < 4; i++) sim = GoOn(sim.World, i % 2 == 0 ? ScenarioOutcome.Won : ScenarioOutcome.Lost);
        var w = sim.World;
        Assert.Equal(ScenarioKind.LastCall, w.Settings.Scenario);
        Assert.Equal(1932, w.Year);
        w.Player.Cash = w.Player.CleanCash = Scenarios.LastCallClean * 3;
        sim.AdvanceWeek(Nothing(w));
        Assert.Equal(ScenarioOutcome.Won, w.Goal.Outcome);
        Assert.Equal("That was the last chapter.", Campaign.CannotGoOn(w));
        Assert.Contains(w.Events, e => e.Text.StartsWith("The campaign is over.") && e.Text.Contains("won 3 of 5 chapters"));
    }

    [Fact]
    public void TheConsoleTakesYouToTheNextCity()
    {
        var shell = new CommandShell(Simulation.New(Campaign.Start(1)));
        Assert.Contains("This chapter isn't over yet.", shell.Execute("chapter next"));
        Assert.Null(shell.NextCity);
        shell.Sim.World.Goal.Outcome = ScenarioOutcome.Lost;
        Assert.Contains("2. Take the Ward, 1924 to 1927: to come", shell.Execute("chapter"));
        shell.Execute("chapter next");
        Assert.Equal(1, shell.NextCity!.World.Settings.Campaign!.Chapter);
        Assert.Contains("chapter <next|again>", CommandShell.Help);
        Assert.Contains("isn't part of a campaign", new CommandShell(Simulation.New(new WorldSettings { Seed = 1 })).Execute("chapter"));
    }

    [Fact]
    public void ItIsSaved()
    {
        var sim = GoOn(Simulation.New(Campaign.Start(1)).World, ScenarioOutcome.Won);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var c = loaded.Settings.Campaign!;
        Assert.Equal(1, c.Chapter);
        Assert.Equal(new[] { ScenarioOutcome.Won }, c.Results);
        Assert.Equal(sim.World.Settings.Campaign!.Boss!.Name, c.Boss!.Name);
        Assert.Equal(Scenarios.Deadline(sim.World), Scenarios.Deadline(loaded));
    }
}
