using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class ReputationTests
{
    private static (Simulation Sim, World W, Gang Me) City(int week = 0, ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 2, CityEventsEnabled = false, DirectorEnabled = false, StartWeek = week });
        sim.World.Player.Cash = 20_000;
        return (sim, sim.World, sim.World.Player);
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    [Fact]
    public void ANameHelpsShakedownsAndJuries()
    {
        var (sim, w, me) = City();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        var shop = w.Businesses.First(b => b.IsOpen && b.ProtectorGangId < 0 && Simulation.ExtortChance(w, me, man, b) is > 0.1 and < 0.5);
        double shake = Simulation.ExtortChance(w, me, man, shop);
        sim.Jail(man, 60, "with assault", strength: 0.7);
        double jury = Simulation.ConvictionChance(w, man);
        me.Fear = 40;
        me.Respect = 40;
        Assert.Equal(Math.Min(0.95, shake + 40 * Content.FearShakedown), Simulation.ExtortChance(w, me, man, shop), 6);
        Assert.Equal(jury - 40 * Content.FearJury - 40 * Content.RespectJury, Simulation.ConvictionChance(w, man), 6);
    }

    [Fact]
    public void AKillingMakesHimFearedAndAnAcquittalAdmired()
    {
        var (_, w, me) = City();
        w.Renown(me, fear: Content.FearHit);
        w.Renown(me, respect: Content.RespectBossAcquittal);
        Assert.Equal(Content.FearHit, me.Fear);
        Assert.Equal(Content.RespectBossAcquittal, me.Respect);
        w.Renown(me, fear: 500, respect: -500);
        Assert.Equal(100, me.Fear);
        Assert.Equal(0, me.Respect);
    }

    [Fact]
    public void BreakingHisWordCostsRespect()
    {
        var (_, w, me) = City();
        var them = w.LivingGangs.First(g => !g.IsPlayer);
        me.Respect = 30;
        w.Betray(me, them, new Pact { Kind = PactKind.Truce, A = me.Id, B = them.Id });
        Assert.Equal(30 - Content.RespectBrokenWord, me.Respect);
    }

    [Fact]
    public void NamesFadeAndMakeThePapers()
    {
        var (sim, w, me) = City();
        Week(sim);
        me.Fear = 45;
        Week(sim);
        Assert.Equal(45 * Content.NameFade, me.Fear, 6);
        var boss = w.HoodById(me.BossHoodId);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Name && e.Text == $"The papers have started writing about {boss.Name} of {me.Name}. To some of them he's the most feared man in the city.");
        Assert.Equal("a name in the papers, feared", World.NameOf(me));
        int count = w.Events.Count(e => e.Kind == EventKind.Name);
        Week(sim);
        Assert.Equal(count, w.Events.Count(e => e.Kind == EventKind.Name));
    }

    [Fact]
    public void ANewBossKeepsHalfTheName()
    {
        var (sim, w, me) = City();
        me.Fear = 60;
        Week(sim);
        var heir = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        me.BossHoodId = heir.Id;
        double before = me.Fear;
        Week(sim);
        Assert.Equal(before * Content.InheritedName * Content.NameFade, me.Fear, 6);
        Assert.Equal(heir.Id, me.NameBossId);
    }

    [Fact]
    public void ASoupKitchenEarnsRespectAndMoreInTheDepression()
    {
        var (sim, w, me) = City((1930 - Content.StartYear) * Content.WeeksPerYear + 5);
        Assert.Null(w.CannotRunSoupKitchen(me));
        me.SoupKitchen = true;
        long cash = me.Cash;
        Week(sim);
        Assert.Equal(Content.SoupRespect * 2 * Content.NameFade, me.Respect, 6);
        Assert.True(cash - me.Cash >= Content.SoupKitchenWeekly);
        me.Cash = 0;
        Week(sim);
        Assert.False(me.SoupKitchen);
        Assert.Contains(w.Events, e => e.Text == $"{me.Name} couldn't pay for the soup kitchen any more, and it closed.");
    }

    [Fact]
    public void AKnownNameBringsLoyalMen()
    {
        var (sim, w, me) = City();
        me.Respect = 50;
        me.Fear = 60;
        int before = w.HoodsOf(me.Id).Count();
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [me.Id] = new() { new RecruitOrder(me.Id) } });
        var man = w.HoodsOf(me.Id).OrderByDescending(h => h.Id).First();
        Assert.Equal(before + 1, w.HoodsOf(me.Id).Count());
        Assert.True(man.Loyalty >= (int)(50 * Content.RespectRecruitLoyalty));
    }

    [Fact]
    public void TheConsoleShowsTheNamesAndItIsSaved()
    {
        var (sim, w, me) = City();
        me.Fear = 30;
        var shell = new CommandShell(sim);
        Assert.Contains("feared on the street: fear 30", shell.Execute("name"));
        Assert.Contains("Your soup kitchen costs", shell.Execute("soup on"));
        Assert.True(me.SoupKitchen);
        Assert.Contains("soup [on|off]", CommandShell.Help);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World.Player;
        Assert.Equal(30, loaded.Fear);
        Assert.True(loaded.SoupKitchen);
    }
}
