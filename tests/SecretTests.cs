using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class SecretTests
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

    private static int Meek(World w) => Enumerable.Range(1000, 500).First(id => w.NerveOf(id) == 1);

    private static LeanOrder Send(World w, Hood man, int citizen, Lean lean, int worth = 20, int business = -1) =>
        new(w.Player.Id, man.Id, citizen, w.HqOf(w.Player).Id, "Sadie Weiss", lean, worth, business);

    [Fact]
    public void ADirectoryGivesSomeGrownUpsSecretsAndNoChildren()
    {
        var w = Simulation.New(new WorldSettings { Seed = 4, CityEventsEnabled = false, DirectorEnabled = false }).World;
        bool Tenement(Lot l) => l.Terrain == Terrain.Land && l.Use == LotUse.Empty;
        int People(Lot l) => l.Terrain != Terrain.Land ? 0 : l.Use == LotUse.Business ? 20 : Tenement(l) ? 10 : 0;
        var d = new CityDirectory(w, People, Tenement);
        var people = Enumerable.Range(0, d.CountOn(1, 1)).Select(n => d.Get(d.IdOf(1, 1, n))).ToList();
        int hiding = people.Count(p => d.SecretsOf(p.Id).Count > 0);
        Assert.InRange(hiding, people.Count / 10, people.Count / 2);
        Assert.All(people.Where(p => p.Age < 18), p => Assert.Empty(d.SecretsOf(p.Id)));
        Assert.All(people.SelectMany(p => d.SecretsOf(p.Id).Where(s => s.Vice == Vice.Lover).Select(s => (p, s))),
            x => Assert.NotEqual(x.p.Woman, d.Get(x.s.Other).Woman));
    }

    [Fact]
    public void FindingOutALoverPutsThemInYourDirectory()
    {
        var (_, w, me, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), new[] { new Secret(Vice.Lover, 7100) });
        Assert.Equal(0, w.Shame(7000));
        Assert.True(w.LearnSecret(7000, new Secret(Vice.Lover, 7100)));
        Assert.Contains(new Tie(7100, Kin.Lover), w.KnownTiesOf(7000));
        Assert.True(w.Shame(7000) > 0);
        Assert.True(w.Leverage(me, 7000) > 0);
    }

    [Fact]
    public void YouCantBlackmailSomeoneYouHaveNothingOn()
    {
        var (_, w, me, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), new[] { new Secret(Vice.Drink, -1), new Secret(Vice.Wanted, 17) });
        Assert.Equal("You've nothing on them yet.", w.CannotLean(me, 7000, Lean.Blackmail));
        w.LearnSecret(7000, new Secret(Vice.Drink, -1));
        Assert.Null(w.CannotLean(me, 7000, Lean.Blackmail));
        Assert.NotNull(w.CannotLean(me, 7000, Lean.TurnIn));
        w.LearnSecret(7000, new Secret(Vice.Wanted, 17));
        Assert.Null(w.CannotLean(me, 7000, Lean.TurnIn));
        Assert.Equal("manslaughter in Detroit", Content.Crime(w.KnownCrime(7000)!.Value));
    }

    [Fact]
    public void ABlackmailedManPaysEveryWeekUntilHesSqueezedDry()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        w.Introduce(who, 1, Array.Empty<Tie>(), new[] { new Secret(Vice.Thief, -1) });
        w.LearnSecret(who, new Secret(Vice.Thief, -1));
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.Blackmail, worth: 40)));
        if (w.StandingOf(who) != Standing.Blackmailed) return;
        Assert.Contains(w.Events, e => e.Text.Contains("$20 a week to keep it quiet"));
        Assert.Equal("They're paying you already.", w.CannotLean(me, who, Lean.Blackmail));
        for (int i = 0; i < Content.HushWeeks + 1; i++) sim.AdvanceWeek(Mine(w));
        Assert.Equal(Standing.None, w.StandingOf(who));
        Assert.Contains(w.Events, e => e.Text.Contains("hush money") || e.Text.Contains("stopped paying"));
    }

    [Fact]
    public void TurningSomeoneInCoolsThePoliceAndShutsTheirShop()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        var shop = w.Businesses.First(b => b.IsOpen);
        w.Introduce(who, 1, Array.Empty<Tie>(), new[] { new Secret(Vice.Wanted, 0) });
        w.LearnSecret(who, new Secret(Vice.Wanted, 0));
        me.Heat = 40;
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.TurnIn, business: shop.Id)));
        if (w.StandingOf(who) != Standing.Jailed) return;
        Assert.True(shop.ShutWeeks > 0);
        Assert.Equal("They're in jail.", w.CannotLean(me, who, Lean.Kill));
        Assert.Contains(w.Events, e => e.Text.Contains("taken in for robbery in Chicago"));
    }

    [Fact]
    public void SecretsAreSaved()
    {
        var (sim, w, _, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), new[] { new Secret(Vice.Gambling, 3), new Secret(Vice.Debt, -1) });
        w.LearnSecret(7000, new Secret(Vice.Debt, -1));
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(2, loaded.Acquaintances[7000].Secrets.Count);
        Assert.Equal(new[] { new Secret(Vice.Debt, -1) }, loaded.KnownSecretsOf(7000));
    }
}
