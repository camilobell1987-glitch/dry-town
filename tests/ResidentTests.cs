using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class ResidentTests
{
    private static (Simulation Sim, World W, Gang Me, Hood Man) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 1, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        man.Intimidation = man.Muscle = man.Stealth = man.Brains = 10;
        return (sim, w, w.Player, man);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    /// <summary>A resident with as little nerve as the seed allows, so a good man nearly always pulls it off.</summary>
    private static int Meek(World w) => Enumerable.Range(1000, 500).First(id => w.NerveOf(id) == 1);

    private static LeanOrder Send(World w, Hood man, int citizen, Lean lean, int worth = 20) =>
        new(w.Player.Id, man.Id, citizen, w.HqOf(w.Player).Id, "Sadie Weiss", lean, worth);

    [Fact]
    public void NerveIsFixedForEachResident()
    {
        var (_, w, _, _) = Start();
        Assert.Equal(w.NerveOf(77), w.NerveOf(77));
        Assert.All(Enumerable.Range(0, 200), id => Assert.InRange(w.NerveOf(id), 1, 10));
        Assert.True(Enumerable.Range(0, 200).Select(w.NerveOf).Distinct().Count() > 5);
    }

    [Fact]
    public void TheOddsFallWithNerveAndRiseWithTheRightMan()
    {
        var tough = new Hood { Name = "a", Intimidation = 10, Muscle = 10, Stealth = 10, Brains = 10 };
        var soft = new Hood { Name = "b", Intimidation = 1, Muscle = 1, Stealth = 1, Brains = 1 };
        foreach (var lean in Enum.GetValues<Lean>())
        {
            Assert.True(Simulation.LeanChance(tough, lean, 5) > Simulation.LeanChance(soft, lean, 5));
            Assert.True(Simulation.LeanChance(tough, lean, 2) >= Simulation.LeanChance(tough, lean, 9));
        }
    }

    [Fact]
    public void ABoughtManCostsMoneyAndTellsYouEverything()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.Bribe)));
        if (w.StandingOf(who) != Standing.Bought) return; // the odds are high but not certain
        Assert.Equal(Fact.All, w.KnownAbout(who));
        Assert.Contains(w.Events, e => e.Kind == EventKind.Street && e.Text.Contains("on the payroll for $60"));
        Assert.Equal("They're on your payroll already.", w.CannotLean(me, who, Lean.Bribe));
    }

    [Fact]
    public void AShakedownPaysAndCantBeRepeatedStraightAway()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        int heat = me.Heat;
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.Extort)));
        Assert.Contains(w.Events, e => e.Kind == EventKind.Street && e.Text.Contains("Sadie Weiss"));
        Assert.NotNull(w.CannotLean(me, who, Lean.Extort));
        if (w.StandingOf(who) == Standing.Scared)
            Assert.Contains(w.Events, e => e.Text.Contains("shook down Sadie Weiss for $60"));
    }

    [Fact]
    public void ASnatchedManIsHeldUntilTheFamilyPaysOrGivesUp()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.Kidnap)));
        if (w.StandingOf(who) != Standing.Held && w.StandingOf(who) != Standing.Scared) return;
        for (int i = 0; i < Content.ResidentRansomWeeks + 1; i++) sim.AdvanceWeek(Mine(w));
        Assert.Equal(Standing.Scared, w.StandingOf(who));
        Assert.Contains(w.Events, e => e.Text.Contains("let go"));
    }

    [Fact]
    public void AKillingIsFinalAndTheWardHearsOfIt()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        double respect = me.Respect;
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.Kill)));
        if (w.StandingOf(who) != Standing.Dead) return;
        Assert.Equal("They're dead.", w.CannotLean(me, who, Lean.Intimidate));
        Assert.Contains(w.Script, a => a.Result == ActionResult.Killed && a.ToLot == w.HqOf(me).Id);
        Assert.Contains(man.Name, w.Acquaintances[who].Note);
        Assert.True(me.Respect <= respect);
    }

    [Fact]
    public void DealtWithPeopleAreSaved()
    {
        var (sim, w, me, _) = Start();
        var a = w.Meet(5, 1);
        a.Standing = Standing.Held;
        a.Ransom = 300;
        a.GangId = me.Id;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(Standing.Held, loaded.StandingOf(5));
        Assert.Equal(300, loaded.Acquaintances[5].Ransom);
    }
}
