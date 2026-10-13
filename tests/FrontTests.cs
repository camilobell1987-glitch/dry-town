using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class FrontTests
{
    private static Simulation Run(ulong seed, int weeks, CitySize size = CitySize.Small)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, Size = size });
        for (int i = 0; i < weeks; i++) sim.AdvanceWeek(playerAutopilot: true);
        return sim;
    }

    /// <summary>A business the gang protects, handing it one if it has none yet.</summary>
    private static Business Turf(World w, Gang g)
    {
        var b = w.TurfOf(g.Id).Where(b => b.IsOpen && !b.IsFront).OrderBy(b => b.Id).FirstOrDefault()
            ?? w.Businesses.Where(b => !b.IsProtected && b.IsOpen).OrderBy(b => b.Id).First();
        b.ProtectorGangId = g.Id;
        b.ProtectionRate = Content.DefaultRatePercent;
        b.HandlerHoodId = w.AvailableHoodsOf(g.Id).First().Id;
        return b;
    }

    [Fact]
    public void OnlyTheOutfitAShopPaysCanBuyIt()
    {
        var w = Simulation.New(new WorldSettings { Seed = 4 }).World;
        var me = w.Player;
        me.Cash = 50_000;
        var rivals = Turf(w, w.LivingGangs.First(g => g.Id != me.Id));
        Assert.NotNull(w.CannotBuyFront(me, rivals));
        Assert.Equal(-1, w.BuyFront(me, rivals));

        var mine = Turf(w, me);
        int price = Content.FrontPrice(mine);
        Assert.Equal(price, w.BuyFront(me, mine));
        Assert.Equal(me.Id, mine.OwnerGangId);
        Assert.Equal(50_000 - price, me.Cash);
        Assert.Equal("It's already yours.", w.CannotBuyFront(me, mine));
    }

    [Fact]
    public void AFrontWashesDirtyMoneyForAFee()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4 });
        var w = sim.World;
        var me = w.Player;
        me.Cash = 20_000;
        var front = Turf(w, me);
        w.BuyFront(me, front);
        Assert.Equal(0, me.Clean);

        sim.AdvanceWeek();
        var ledger = w.LastLedger[me.Id];
        Assert.Equal(Content.LaunderCapacity(front), ledger.Laundered);
        Assert.Equal(ledger.Laundered * Content.LaunderFeePercent / 100, ledger.LaunderFee);
        Assert.Equal(front.Takings * Content.FrontProfitPercent / 100, ledger.Legit);
        Assert.Equal(ledger.Laundered - ledger.LaunderFee + ledger.Legit, me.Clean);
        Assert.Equal(0, ledger.Protection - w.TurfOf(me.Id).Where(b => b != front).Sum(b => (long)b.Takings * b.ProtectionRate / 100));
    }

    [Fact]
    public void CleanMoneyIsSpentLast()
    {
        var w = Simulation.New(new WorldSettings { Seed = 4 }).World;
        var me = w.Player;
        me.Cash = 5000;
        me.CleanCash = 1000;
        Assert.Equal(4000, me.Dirty);
        me.Cash -= 4500;
        Assert.Equal(500, me.Clean);
        Assert.Equal(0, me.Dirty);
    }

    [Fact]
    public void AFrontARivalHasTakenWashesNothing()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4 });
        var w = sim.World;
        var me = w.Player;
        me.Cash = 20_000;
        var front = Turf(w, me);
        w.BuyFront(me, front);
        front.ProtectorGangId = w.LivingGangs.First(g => g.Id != me.Id).Id;
        Assert.False(World.Working(front));
        Assert.Equal(0, w.LaunderCapacity(me.Id));
        sim.AdvanceWeek();
        Assert.Equal(0, w.LastLedger[me.Id].Laundered);
    }

    [Fact]
    public void TheTaxMenCantTouchCleanMoney()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 6 });
        var me = sim.World.Player;
        me.Cash = me.CleanCash = 1_000_000;
        for (int i = 0; i < 30; i++) sim.AdvanceWeek();
        Assert.DoesNotContain(sim.World.Events, e => e.GangId == me.Id && e.Text.StartsWith("Treasury agents"));
        // Wages, and any split in the outfit, come out of it; the Treasury takes none of it.
        Assert.Equal(me.Cash, me.Clean);
        Assert.True(me.Clean > 500_000);
    }

    [Fact]
    public void RivalsBuyFrontsAsTheyGetRich()
    {
        var w = Run(1, 3 * Content.WeeksPerYear, CitySize.Medium).World;
        Assert.Contains(w.Events, e => e.Kind == EventKind.Front && e.GangId != w.Player.Id);
        Assert.Contains(w.LivingGangs, g => !g.IsPlayer && g.Clean > 0);
    }

    [Fact]
    public void TheShellBuysAndListsFronts()
    {
        var shell = new CommandShell(Run(2, 2));
        var w = shell.Sim.World;
        w.Player.Cash = 50_000;
        var b = Turf(w, w.Player);
        Assert.Contains("No fronts yet", shell.Execute("fronts"));
        Assert.Contains("is yours", shell.Execute($"buy {b.Id}"));
        Assert.Contains("washes", shell.Execute("fronts"));
        Assert.Contains("front for", shell.Execute("turf"));
    }

    [Fact]
    public void FrontsAndCleanMoneySurviveASave()
    {
        var sim = Run(5, 10);
        var w = sim.World;
        w.Player.Cash = 50_000;
        var b = Turf(w, w.Player);
        w.BuyFront(w.Player, b);
        w.Player.CleanCash = 7000;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, new List<Order>()))).World;
        Assert.Equal(w.Player.Id, loaded.BusinessById(b.Id).OwnerGangId);
        Assert.Equal(7000, loaded.Player.Clean);
    }
}
