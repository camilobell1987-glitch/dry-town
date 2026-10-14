using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class KinTests
{
    private static (Simulation Sim, World W, Gang Me) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 1, CityEventsEnabled = false, DirectorEnabled = false });
        return (sim, sim.World, sim.World.Player);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    private const int Shopkeeper = 7000, Wife = 7001, Son = 7002;

    private static Tie[] Family => new[] { new Tie(Wife, Kin.Spouse), new Tie(Son, Kin.Child) };

    [Fact]
    public void LearningATieBringsTheRelativeIntoYourDirectory()
    {
        var (_, w, _) = Start();
        w.Introduce(Shopkeeper, 1, Family);
        w.Learn(Shopkeeper, Fact.Home);
        Assert.True(w.LearnTie(Shopkeeper, Family[1]));
        Assert.False(w.LearnTie(Shopkeeper, Family[1]));
        Assert.Equal(new[] { Family[1] }, w.KnownTiesOf(Shopkeeper));
        Assert.Contains(Shopkeeper, w.Acquaintances[Son].KnownTies);
        Assert.True(w.KnownAbout(Son).HasFlag(Fact.Home));
        Assert.Equal(1, w.Acquaintances[Son].WardId);
    }

    [Fact]
    public void TheStreetTurnsUpFamilyInTime()
    {
        var (sim, w, _) = Start();
        w.Introduce(Shopkeeper, w.HqOf(w.Player).WardId, Family);
        for (int i = 0; i < 60 && w.KnownTiesOf(Shopkeeper).Count() < 2; i++) sim.AdvanceWeek(Mine(w));
        Assert.Equal(2, w.KnownTiesOf(Shopkeeper).Count());
    }

    [Fact]
    public void KnowingTheFamilyMakesAThreatLikelierAndHoldingOneOfThemMore()
    {
        var (_, w, me) = Start();
        w.Introduce(Shopkeeper, 1, Family);
        Assert.Equal(0, w.Leverage(me, Shopkeeper));
        w.LearnTie(Shopkeeper, Family[0]);
        double known = w.Leverage(me, Shopkeeper);
        Assert.True(known > 0);
        var son = w.Meet(Son, 1);
        son.Standing = Standing.Held;
        son.GangId = me.Id;
        Assert.Equal(known, w.Leverage(me, Shopkeeper)); // you don't know he's the son yet
        w.LearnTie(Shopkeeper, Family[1]);
        Assert.True(w.Leverage(me, Shopkeeper) > known + 0.3);
        Assert.Equal(Son, w.HostageFrom(me, Shopkeeper));
        var man = new Hood { Name = "a", Intimidation = 4, Muscle = 4, Stealth = 4, Brains = 4 };
        Assert.True(Simulation.LeanChance(man, Lean.Intimidate, 7, Fact.None, w.Leverage(me, Shopkeeper)) > Simulation.LeanChance(man, Lean.Intimidate, 7));
        Assert.Equal(Simulation.LeanChance(man, Lean.Bribe, 7, Fact.None, 1), Simulation.LeanChance(man, Lean.Bribe, 7));
    }

    [Fact]
    public void ABoughtManTellsYouWhoHeHas()
    {
        var (sim, w, me) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        man.Brains = 10;
        int who = Enumerable.Range(1000, 500).First(id => w.NerveOf(id) == 1);
        w.Introduce(who, 1, new[] { new Tie(who + 1, Kin.Spouse) });
        me.Cash = 1000;
        sim.AdvanceWeek(Mine(w, new LeanOrder(me.Id, man.Id, who, w.HqOf(me).Id, "Sadie Weiss", Lean.Bribe, 10)));
        if (w.StandingOf(who) != Standing.Bought) return;
        Assert.Single(w.KnownTiesOf(who));
    }

    [Fact]
    public void TiesAreSaved()
    {
        var (sim, w, _) = Start();
        w.Introduce(Shopkeeper, 1, Family);
        w.LearnTie(Shopkeeper, Family[0]);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(Family, loaded.Acquaintances[Shopkeeper].Ties);
        Assert.Equal(new[] { Family[0] }, loaded.KnownTiesOf(Shopkeeper));
    }
}
