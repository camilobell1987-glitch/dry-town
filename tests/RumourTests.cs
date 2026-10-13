using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class RumourTests
{
    private static (Simulation Sim, World W, Gang Me) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 1, CityEventsEnabled = false, DirectorEnabled = false });
        return (sim, sim.World, sim.World.Player);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    private const int Someone = 12345;

    [Fact]
    public void AskingAroundAlwaysTurnsUpSomething()
    {
        var (sim, w, me) = Start();
        int lot = w.HqOf(me).Id;
        sim.AdvanceWeek(Mine(w, new AskOrder(me.Id, Someone, lot, "someone on the block")));
        Assert.NotEqual(Fact.None, w.KnownAbout(Someone));
        Assert.Contains(w.Events, e => e.Kind == EventKind.Street && e.Text.Contains("asked around about someone on the block"));
    }

    [Fact]
    public void AManTailingSomeoneLearnsAtLeastTheirAddress()
    {
        var (sim, w, me) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        int lot = w.HqOf(me).Id;
        sim.AdvanceWeek(Mine(w, new TailOrder(me.Id, man.Id, Someone, lot, "Mary Doyle")));
        Assert.True(w.KnownAbout(Someone).HasFlag(Fact.Home));
        Assert.Contains(w.Events, e => e.Kind == EventKind.Street && e.Text.Contains(man.Name) && e.Text.Contains("Mary Doyle"));
    }

    [Fact]
    public void AStealthyManIsSeldomSpottedAndAClumsyOneOften()
    {
        var sneak = new Hood { Name = "a", Stealth = 10 };
        var lump = new Hood { Name = "b", Stealth = 1 };
        Assert.True(Simulation.SpottedChance(sneak) < Simulation.SpottedChance(lump));
        Assert.True(Simulation.TailOdds(sneak) > Simulation.TailOdds(lump));
    }

    [Fact]
    public void TheStreetFillsInCardsOnPeopleYouveMetAndNobodyElse()
    {
        var (sim, w, me) = Start();
        int ward = w.HqOf(me).WardId;
        w.Meet(Someone, ward);
        for (int i = 0; i < 30 && w.KnownAbout(Someone) == Fact.None; i++) sim.AdvanceWeek(Mine(w));
        Assert.NotEqual(Fact.None, w.KnownAbout(Someone));
        Assert.Equal(Fact.None, w.KnownAbout(Someone + 1));
        Assert.Single(w.Acquaintances);
    }

    [Fact]
    public void WhatYouKnowIsSaved()
    {
        var (sim, w, _) = Start();
        Assert.Equal(Fact.Name | Fact.Job, w.Learn(Someone, Fact.Name | Fact.Job, 2));
        Assert.Equal(Fact.None, w.Learn(Someone, Fact.Name));
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(Fact.Name | Fact.Job, loaded.KnownAbout(Someone));
        Assert.Equal(2, loaded.Acquaintances[Someone].WardId);
    }

    [Fact]
    public void LearnedThingsReadAsAList() =>
        Assert.Equal("name, line of work and address", World.Describe(Fact.Name | Fact.Job | Fact.Home));
}
