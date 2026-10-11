using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class BoozeTests
{
    /// <summary>A quiet city: no news, no new outfits, and by default nobody else to take the player's places.</summary>
    private static (Simulation Sim, World W, Gang Me) Start(int gangs = 1)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = gangs, CityEventsEnabled = false, DirectorEnabled = false });
        return (sim, sim.World, sim.World.Player);
    }

    /// <summary>Put a business nobody protects on the gang's books, with a racket in the back.</summary>
    private static Business Give(World w, Gang g, RacketKind racket = RacketKind.Speakeasy)
    {
        var b = w.Businesses.Where(b => b.IsOpen && !b.IsProtected).OrderBy(b => w.BlocksFromHq(g, b)).ThenBy(b => b.Id).First();
        b.ProtectorGangId = g.Id;
        b.HandlerHoodId = w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId).Id;
        b.Racket = racket;
        return b;
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    [Fact]
    public void ALineAndAHeavyPourBothPay()
    {
        var (_, w, me) = Start();
        var bar = Give(w, me);
        Assert.Equal(1, w.BoozeFactor(bar, me));
        Assert.True(w.BadBatchChance(bar, me) > 0);

        var line = w.SupplyLines[0];
        me.Cash = 0;
        Assert.NotNull(w.CannotBuyLine(me, line));
        me.Cash = 10_000;
        Assert.Null(w.CannotBuyLine(me, line));
        Assert.Equal(line.Price, w.BuyLine(me, line));
        Assert.Equal(10_000 - line.Price, me.Cash);
        Assert.Contains(bar.Id, w.Supplied(me.Id));
        Assert.Equal(1 + Content.LineBonus, w.BoozeFactor(bar, me), 6);
        // Real liquor served straight never hurts anyone.
        Assert.Equal(0, w.BadBatchChance(bar, me));

        w.SetCut(me, Cut.Watered);
        Assert.Equal((1 + Content.LineBonus) * 1.2, w.BoozeFactor(bar, me), 6);
        Assert.True(w.BadBatchChance(bar, me) > 0);
        Assert.Contains("It's already yours", w.CannotBuyLine(me, line));
    }

    [Fact]
    public void RealLiquorShowsInTheTakings()
    {
        long Takings(bool line)
        {
            var (sim, w, me) = Start();
            Give(w, me);
            me.Cash = 10_000;
            if (line) { me.Cash += w.SupplyLines[0].Price; w.BuyLine(me, w.SupplyLines[0]); }
            sim.AdvanceWeek(Mine(w));
            return w.Ledger(me.Id).Rackets;
        }
        Assert.True(Takings(line: true) > Takings(line: false));
    }

    [Fact]
    public void AStoppedShipmentPaysAndLeavesTheOwnerDry()
    {
        var (sim, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var bar = Give(w, rival);
        rival.Cash = 10_000;
        var line = w.SupplyLines[0];
        w.BuyLine(rival, line);
        Assert.Equal(bar, w.DropOf(line));
        Assert.True(Simulation.CanWaylay(w, me, line));
        Assert.False(Simulation.CanWaylay(w, rival, line));

        var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).Take(3).ToList();
        foreach (var h in men) h.Muscle = h.Intimidation = 10;
        bool stopped = false;
        for (int i = 0; i < 10 && !stopped && men.All(h => h.IsAvailable) && line.OwnerGangId == rival.Id; i++)
        {
            long before = me.Cash;
            sim.AdvanceWeek(Mine(w, new WaylayOrder(me.Id, men[0].Id, line.Id, new[] { men[1].Id, men[2].Id })));
            stopped = line.WaylaidWeek == w.Week - 1;
        }
        Assert.True(stopped);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Hijack && e.Text.StartsWith($"{men[0].Name} stopped {rival.Name}'s shipment on {line.Name}"));
    }

    [Fact]
    public void ABadBatchClosesThePlace()
    {
        var (sim, w, me) = Start();
        me.Cash = 50_000;
        var bars = Enumerable.Range(0, 4).Select(_ => Give(w, me, RacketKind.Still)).ToList();
        w.SetCut(me, Cut.Hard);
        GameEvent? bad = null;
        for (int i = 0; i < 40 && bad == null; i++)
        {
            sim.AdvanceWeek(Mine(w));
            bad = w.Events.FirstOrDefault(e => e.Kind == EventKind.Booze && e.Text.Contains(" went blind "));
        }
        Assert.NotNull(bad);
        var place = bars.First(b => bad!.Text.Contains(b.Name));
        Assert.Equal(RacketKind.None, place.Racket);
        Assert.True(place.ShutWeeks > 0);
    }

    [Fact]
    public void RepealEndsTheRuns()
    {
        var (sim, w, me) = Start();
        me.Cash = 10_000;
        w.BuyLine(me, w.SupplyLines[0]);
        w.SetCut(me, Cut.Watered);
        w.Week = (Content.RepealYear - Content.StartYear) * Content.WeeksPerYear - 1;
        sim.AdvanceWeek(Mine(w));
        Assert.All(w.SupplyLines, l => Assert.Equal(-1, l.OwnerGangId));
        Assert.Equal(Cut.Straight, me.Cut);
        Assert.NotNull(w.CannotBuyLine(me, w.SupplyLines[0]));
    }

    [Fact]
    public void LinesAndThePourAreSaved()
    {
        var (sim, w, me) = Start();
        me.Cash = 10_000;
        w.BuyLine(me, w.SupplyLines[1]);
        w.SupplyLines[1].WaylaidWeek = 3;
        w.SetCut(me, Cut.Hard);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(w.SupplyLines.Count, loaded.SupplyLines.Count);
        Assert.Equal(me.Id, loaded.SupplyLines[1].OwnerGangId);
        Assert.Equal(3, loaded.SupplyLines[1].WaylaidWeek);
        Assert.Equal(Cut.Hard, loaded.Player.Cut);
    }

    [Fact]
    public void TheConsoleRunsTheBooze()
    {
        var (sim, w, me) = Start();
        Give(w, me);
        me.Cash = 10_000;
        var shell = new CommandShell(sim);
        string list = shell.Execute("booze")!;
        Assert.Contains(w.SupplyLines[0].Name, list);
        Assert.Contains("for sale", list);
        Assert.StartsWith("You hold ", shell.Execute("buyline 0"));
        Assert.Equal(me.Id, w.SupplyLines[0].OwnerGangId);
        Assert.Contains("watered down", shell.Execute("cut watered"));
        Assert.Equal(Cut.Watered, me.Cut);
        Assert.Contains("That's your own line", shell.Execute($"waylay {w.AvailableHoodsOf(me.Id).First().Id} 0"));
    }

    [Fact]
    public void RivalsTakeOverTheLines()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 1, Size = CitySize.Medium });
        for (int i = 0; i < 3 * Content.WeeksPerYear; i++) sim.AdvanceWeek(playerAutopilot: true);
        var w = sim.World;
        Assert.Contains(w.Events, e => e.Kind == EventKind.Booze && e.Text.Contains(" to take over ") && e.GangId >= 0 && !w.GangById(e.GangId).IsPlayer);
    }
}
