using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class WarTests
{
    /// <summary>A quiet city with the player and two rivals: no news and no new outfits.</summary>
    private static (Simulation Sim, World W, Gang Me, Gang Them) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 3, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        return (sim, w, w.Player, w.LivingGangs.First(g => !g.IsPlayer));
    }

    private static Dictionary<int, List<Order>> Mine(World w) => new() { [w.Player.Id] = new List<Order>() };

    [Fact]
    public void GoingToTheMattressesStartsAWar()
    {
        var (_, w, me, them) = Start();
        int grudge = w.GrudgeOf(them.Id, me.Id);
        Assert.Null(w.CannotDeclare(me, them));
        w.Declare(me, them);
        Assert.True(w.AtWar(me.Id, them.Id));
        Assert.Equal(grudge + Content.GrudgeWar, w.GrudgeOf(them.Id, me.Id));
        Assert.Contains("already at war", w.CannotDeclare(me, them));
        var third = w.LivingGangs.First(g => g.Id != me.Id && g.Id != them.Id);
        Assert.Contains("already at war", w.CannotDeclare(me, third));
        Assert.Contains("already has its men on the mattresses", w.CannotDeclare(third, them));
        Assert.Contains(w.Events, e => e.Text.StartsWith($"{me.Name} went to the mattresses against {them.Name}"));
    }

    [Fact]
    public void APactHasToBeBrokenFirst()
    {
        var (_, w, me, them) = Start();
        w.Sign(me, them, PactKind.Truce);
        Assert.Contains("Break it first", w.CannotDeclare(me, them));
    }

    [Fact]
    public void HitsComeEasierAndBossesHoleUp()
    {
        var (_, w, me, them) = Start();
        var gunman = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        var target = w.AvailableHoodsOf(them.Id).First(h => h.Id != them.BossHoodId);
        var boss = w.HoodById(them.BossHoodId);
        foreach (var h in w.AvailableHoodsOf(them.Id).Where(h => h.Id != boss.Id).Skip(1).ToList()) h.State = HoodState.Jailed;
        Assert.False(Simulation.IsGuardedBoss(w, boss));
        double before = Simulation.HitChance(w, gunman, Array.Empty<Hood>(), target);
        w.Declare(me, them);
        Assert.True(Simulation.HitChance(w, gunman, Array.Empty<Hood>(), target) > before);
        Assert.True(Simulation.IsGuardedBoss(w, boss));
    }

    [Fact]
    public void TheMattressesCostMoneyAndTheCityNotices()
    {
        (long Wages, int Outrage) Week(bool war)
        {
            var (sim, w, me, them) = Start();
            me.Cash = 50_000;
            if (war) w.Declare(me, them);
            sim.AdvanceWeek(Mine(w));
            return (w.Ledger(me.Id).Wages, w.Hall.Outrage);
        }
        var peace = Week(false);
        var war = Week(true);
        Assert.True(war.Wages > peace.Wages);
        Assert.True(war.Outrage > peace.Outrage);
    }

    [Fact]
    public void OnlyTheSideThatStartedItCanCallItOff()
    {
        var (_, w, me, them) = Start();
        w.Declare(them, me);
        Assert.NotNull(w.CannotCallOff(me));
        Assert.Null(w.CannotCallOff(them));
        w.CallOff(them);
        Assert.False(w.AtWar(me.Id));
        Assert.Contains(w.Events, e => e.Text.StartsWith($"{them.Name} called off the war with {me.Name}"));
    }

    [Fact]
    public void ATruceEndsTheWar()
    {
        var (_, w, me, them) = Start();
        w.Declare(me, them);
        w.Sign(them, me, PactKind.Truce);
        Assert.Empty(w.Wars);
    }

    [Fact]
    public void AWarEndsWhenOneSideIsFinished()
    {
        var (sim, w, me, them) = Start();
        w.Declare(me, them);
        them.Alive = false;
        sim.AdvanceWeek(Mine(w));
        Assert.Empty(w.Wars);
        Assert.Contains(w.Events, e => e.Text == $"With {them.Name} finished, {me.Name}'s men came home from the mattresses.");
    }

    [Fact]
    public void WarsAreSaved()
    {
        var (sim, w, me, them) = Start();
        w.Declare(them, me);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var war = Assert.Single(loaded.Wars);
        Assert.Equal(them.Id, war.A);
        Assert.Equal(me.Id, war.B);
        Assert.Equal(w.Week, war.StartWeek);
    }

    [Fact]
    public void TheConsoleGoesToWar()
    {
        var (sim, w, me, them) = Start();
        var shell = new CommandShell(sim);
        Assert.StartsWith("Your men are on the mattresses", shell.Execute($"war {them.Id}"));
        Assert.Contains("at war for 0 weeks", shell.Execute("relations"));
        Assert.StartsWith("You called it off", shell.Execute("calloff"));
        Assert.Empty(w.Wars);
    }

    [Fact]
    public void RivalsGoToWarOnTheirOwn()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 1, Size = CitySize.Medium });
        for (int i = 0; i < 3 * Content.WeeksPerYear; i++) sim.AdvanceWeek(playerAutopilot: true);
        var w = sim.World;
        Assert.Contains(w.Events, e => e.Text.Contains(" went to the mattresses against ") && !w.GangById(e.GangId).IsPlayer);
        Assert.DoesNotContain(w.Events, e => e.Text.StartsWith($"{w.Player.Name} went to the mattresses"));
    }
}
