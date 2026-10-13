using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class FuneralTests
{
    /// <summary>A quiet city with three outfits, everyone with money.</summary>
    private static (Simulation Sim, World W, Gang Me, Gang A, Gang B) Start()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = 3, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        foreach (var g in w.LivingGangs) { g.Cash = 20_000; g.Heat = 0; }
        var rivals = w.LivingGangs.Where(g => !g.IsPlayer).OrderBy(g => g.Id).ToList();
        return (sim, w, w.Player, rivals[0], rivals[1]);
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    /// <summary>Kill the gang's boss and let Sunday find him, so his outfit starts burying him.</summary>
    private static Hood Bury(Simulation sim, Gang g)
    {
        var boss = sim.World.HoodById(g.BossHoodId);
        boss.State = HoodState.Dead;
        Week(sim);
        return boss;
    }

    [Fact]
    public void ADeadBossIsBuriedAndTheCityHoldsOff()
    {
        var (sim, w, _, a, _) = Start();
        var boss = Bury(sim, a);
        Assert.NotEqual(boss.Id, a.BossHoodId);
        Assert.True(w.Mourning(a.Id));
        Assert.Contains(w.Events, e => e.Text == $"{a.Name} will bury {boss.Name} next week. Until then, nobody who wants to be thought well of will lay a finger on them.");
        Week(sim);
        Assert.False(w.Mourning(a.Id));
        Assert.Contains(w.Events, e => e.Text.StartsWith($"{boss.Name} of {a.Name} was buried"));
    }

    [Fact]
    public void ABossLockedUpIsNotBuried()
    {
        var (sim, w, _, a, _) = Start();
        var boss = w.HoodById(a.BossHoodId);
        boss.State = HoodState.Jailed;
        boss.JailWeeks = Content.LongStretch + 10;
        a.InsideSinceWeek = -1;
        Week(sim);
        Assert.False(w.Mourning(a.Id));
    }

    [Fact]
    public void RivalsLeaveAnOutfitAloneWhileItBuriesItsBoss()
    {
        var (sim, w, me, a, b) = Start();
        Bury(sim, a);
        foreach (var g in new[] { me, b })
            Assert.DoesNotContain(AiPlanner.Plan(w, g), o => w.VictimOf(o) == a.Id);
    }

    [Fact]
    public void AWreathEarnsRespectAndTheyRememberWhoSentNone()
    {
        var (sim, w, me, a, b) = Start();
        b.Cash = 0;
        var boss = Bury(sim, a);
        me.Respect = 0;
        w.AddGrudge(a.Id, me.Id, 20);
        int grudge = w.GrudgeOf(a.Id, me.Id);
        long cash = me.Cash;
        Assert.Equal($"{me.Name} sent a wreath to {boss.Name}'s funeral.", w.SendFlowers(me, a));
        Assert.Equal(cash - Content.FlowersCost, me.Cash);
        Assert.Equal(Content.FlowersRespect, me.Respect);
        // A wreath doesn't settle old scores.
        Assert.Equal(grudge, w.GrudgeOf(a.Id, me.Id));
        Assert.Equal("Your wreath is already there.", w.CannotSendFlowers(me, a));
        Assert.Equal("It's your own funeral.", w.CannotSendFlowers(a, a));

        int fromB = w.GrudgeOf(a.Id, b.Id);
        Week(sim);
        // b had no money for flowers, so it sent none.
        Assert.Equal($"{boss.Name} of {a.Name} was buried. Wreaths came from {me.Name}.", w.Events.Last(e => e.Text.Contains(" was buried")).Text);
        Assert.True(w.GrudgeOf(a.Id, b.Id) > fromB - Content.GrudgeFade);
        Assert.Equal(grudge - Content.GrudgeFade, w.GrudgeOf(a.Id, me.Id));
        Assert.Equal($"Nobody in {a.Name} has died.", w.CannotSendFlowers(me, a));
    }

    [Fact]
    public void BreakingThePeaceOfAFuneralCostsDearly()
    {
        var (sim, w, me, a, b) = Start();
        w.Week = w.CommissionStartWeek + 5;
        w.Commission.Sitting = true;
        w.Commission.FormedWeek = w.CommissionStartWeek;
        w.Commission.Seats.AddRange(w.LivingGangs.Select(g => g.Id));
        var boss = Bury(sim, a);
        me.Respect = 30;
        int fromA = w.GrudgeOf(a.Id, me.Id), fromB = w.GrudgeOf(b.Id, me.Id);

        // A strike isn't a move on them.
        w.Hostile(me, a, Content.GrudgeStrike, violent: false);
        Assert.True(w.Seated(me.Id));

        w.Hostile(me, a, Content.GrudgeHit);
        Assert.Equal(30 - Content.RespectFuneralBreach, me.Respect);
        Assert.Equal(fromA + Content.GrudgeStrike + Content.GrudgeHit + Content.GrudgeFuneralBreach + Content.GrudgeBreach, w.GrudgeOf(a.Id, me.Id));
        Assert.Equal(fromB + Content.GrudgeFuneralWitnessed + Content.GrudgeBreach, w.GrudgeOf(b.Id, me.Id));
        // The table throws them out.
        Assert.False(w.Seated(me.Id));
        Assert.Contains(w.Events, e => e.Text == $"{me.Name} moved against {a.Name} while they were burying {boss.Name}. The whole city is talking about it.");
        // Only once a funeral.
        w.Hostile(me, a, Content.GrudgeHit);
        Assert.Equal(30 - Content.RespectFuneralBreach, me.Respect);
    }

    [Fact]
    public void EveryManIsAtTheChurch()
    {
        var (sim, w, me, a, _) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        man.Stealth = 1;
        var target = w.AvailableHoodsOf(a.Id).First(h => h.Id != a.BossHoodId);
        double before = Simulation.HitChance(w, man, Array.Empty<Hood>(), target);
        Bury(sim, a);
        Assert.True(target.IsAvailable);
        Assert.Equal(before + Content.FuneralHit, Simulation.HitChance(w, man, Array.Empty<Hood>(), target), 6);
    }

    [Fact]
    public void TheConsoleSendsAWreathAndTheFuneralIsSaved()
    {
        var (sim, w, me, a, _) = Start();
        var boss = Bury(sim, a);
        var shell = new CommandShell(sim);
        Assert.Contains($"{a.Name} are burying {boss.Name}.", shell.Execute("relations"));
        Assert.Contains("flowers <gang>", CommandShell.Help);
        var target = w.AvailableHoodsOf(a.Id).First();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        Assert.Contains("breaks the peace of the funeral", shell.Execute($"hit {man.Id} {target.Id}"));
        Assert.Equal($"{me.Name} sent a wreath to {boss.Name}'s funeral.\n", shell.Execute($"flowers {a.Id}"));

        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.True(loaded.Mourning(a.Id));
        Assert.Contains(me.Id, loaded.FuneralOf(a.Id)!.Flowers);
    }
}
