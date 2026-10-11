using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class UnionTests
{
    /// <summary>A quiet city: no news, no new outfits, and by default nobody else to get in the way.</summary>
    private static (Simulation Sim, World W, Gang Me) Start(int gangs = 1, CitySize size = CitySize.Small)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, Size = size, StartingGangs = gangs, CityEventsEnabled = false, DirectorEnabled = false });
        return (sim, sim.World, sim.World.Player);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    /// <summary>Hand the gang a local, with one of its men in the chair.</summary>
    private static UnionLocal Run(World w, Gang g, int localId = 1)
    {
        var local = w.LocalById(localId)!;
        local.OwnerGangId = g.Id;
        local.PresidentHoodId = w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId).Id;
        return local;
    }

    /// <summary>A member shop of the local that nobody protects.</summary>
    private static Business Shop(World w, UnionLocal local) => w.MembersOf(local).First(b => b.IsOpen && !b.IsProtected);

    [Fact]
    public void EveryCityHasItsLocalsAndItsDocks()
    {
        Assert.Equal(2, Start().W.Locals.Count);
        var w = Start(size: CitySize.Medium).W;
        Assert.Equal(3, w.Locals.Count);
        Assert.True(w.Locals[0].IsDocks);
        Assert.All(w.Locals, l => Assert.True(l.LotId >= 0 && l.OwnerGangId < 0));
        var shop = w.MembersOf(w.Locals[1]).First();
        Assert.Equal(w.Locals[1], w.LocalOf(shop));
    }

    [Fact]
    public void TakingOverALocalBringsInItsDues()
    {
        var (sim, w, me) = Start();
        var local = w.LocalById(1)!;
        var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).Take(2).ToList();
        foreach (var h in men) h.Intimidation = h.Muscle = 10;
        for (int i = 0; i < 10 && local.OwnerGangId < 0 && men.All(h => h.IsAvailable); i++)
            sim.AdvanceWeek(Mine(w, new UnionOrder(me.Id, men[0].Id, local.Id, new[] { men[1].Id })));
        Assert.Equal(me.Id, local.OwnerGangId);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Union && e.Text.StartsWith($"{men[0].Name} of {me.Name} took over {local.Name}"));

        int dues = w.DuesOf(local);
        Assert.True(dues > 0);
        sim.AdvanceWeek(Mine(w));
        Assert.Equal(dues, w.LastLedger[me.Id].Dues);
    }

    [Fact]
    public void AStrikeStopsTheTakingsUntilTheOwnerPays()
    {
        var (sim, w, me) = Start();
        var local = Run(w, me);
        var shop = Shop(w, local);
        Assert.Null(w.CallStrike(me, local, shop));
        Assert.Equal(local, w.StrikeAt(shop));
        Assert.Equal(0, w.TakingsFactor(shop));
        Assert.Equal(0, w.DuesOf(local));

        // The owner gives in, or the pickets give up and are sent out again.
        for (int i = 0; i < 60 && shop.ProtectorGangId != me.Id; i++)
        {
            if (!local.OnStrike) w.CallStrike(me, local, shop);
            sim.AdvanceWeek(Mine(w));
        }
        Assert.Equal(me.Id, shop.ProtectorGangId);
        Assert.False(local.OnStrike);
        Assert.Contains(w.Events, e => e.Text.StartsWith($"The owner of {shop.Name} gave in to the pickets."));
    }

    [Fact]
    public void ThePicketsGiveUpInTheEnd()
    {
        var (sim, w, me) = Start();
        var local = Run(w, me);
        var shop = Shop(w, local);
        w.CallStrike(me, local, shop);
        for (int i = 0; i < Content.StrikeWeeks; i++) sim.AdvanceWeek(Mine(w));
        Assert.False(local.OnStrike);
        Assert.Equal(1, w.TakingsFactor(shop));
    }

    [Fact]
    public void OnlyShopsInTheTradeThatDontPayYouCanBeStruck()
    {
        var (_, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var local = Run(w, me);
        Assert.Contains("don't run", w.CannotStrike(rival, local, Shop(w, local)));
        Assert.Contains("belongs to", w.CannotStrike(me, local, w.Businesses.First(b => !local.Trades.Contains(b.Kind))));
        var mine = Shop(w, local);
        mine.ProtectorGangId = me.Id;
        Assert.Equal("They already pay you.", w.CannotStrike(me, local, mine));

        var theirs = Shop(w, local);
        theirs.ProtectorGangId = rival.Id;
        w.Sign(me, rival, PactKind.Truce);
        Assert.Contains("pact", w.CannotStrike(me, local, theirs));
        w.Pacts.Clear();

        int grudge = w.GrudgeOf(rival.Id, me.Id);
        Assert.Null(w.CallStrike(me, local, theirs));
        Assert.Equal(grudge + Content.GrudgeStrike, w.GrudgeOf(rival.Id, me.Id));
        Assert.Contains("already out", w.CannotStrike(me, local, Shop(w, local)));
    }

    [Fact]
    public void TheDocksTakeACutOfEveryBoat()
    {
        var (sim, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var bar = w.Businesses.Where(b => b.IsOpen && !b.IsProtected && Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy)).OrderBy(b => b.Id).First();
        bar.ProtectorGangId = rival.Id;
        bar.HandlerHoodId = w.AvailableHoodsOf(rival.Id).First(h => h.Id != rival.BossHoodId).Id;
        bar.Racket = RacketKind.Speakeasy;
        var boat = w.SupplyLines.First(l => l.Kind == LineKind.Boat);
        boat.OwnerGangId = rival.Id;
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        double before = Simulation.WaylayChance(w, man, Array.Empty<Hood>(), boat);

        Run(w, me, localId: 0);
        Assert.Equal(me.Id, w.DocksGangId);
        Assert.True(Simulation.WaylayChance(w, man, Array.Empty<Hood>(), boat) > before);
        rival.Cash = 10_000;
        sim.AdvanceWeek(Mine(w));
        Assert.Equal(w.DuesOf(w.Locals[0]) + Content.Wharfage, w.LastLedger[me.Id].Dues);
    }

    [Fact]
    public void ALocalGoesBackToItsMembersWhenTheOutfitIsFinished()
    {
        var (sim, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var local = Run(w, rival);
        rival.Alive = false;
        sim.AdvanceWeek(Mine(w));
        Assert.Equal(-1, local.OwnerGangId);
        Assert.Contains(w.Events, e => e.Text == $"With {rival.Name} gone, {local.Name} elected its own officers again.");
    }

    [Fact]
    public void LocalsAndStrikesAreSaved()
    {
        var (sim, w, me) = Start();
        var local = Run(w, me);
        var shop = Shop(w, local);
        w.CallStrike(me, local, shop);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(w.Locals.Count, loaded.Locals.Count);
        var again = loaded.LocalById(local.Id)!;
        Assert.Equal(me.Id, again.OwnerGangId);
        Assert.Equal(local.PresidentHoodId, again.PresidentHoodId);
        Assert.Equal(shop.Id, again.StrikeBusinessId);
        Assert.Equal(local.Trades, again.Trades);
    }

    [Fact]
    public void TheConsoleRunsTheUnions()
    {
        var (sim, w, me) = Start();
        var shell = new CommandShell(sim);
        Assert.Contains("Teamsters Local 710", shell.Execute("unions"));
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        Assert.StartsWith("Queued:", shell.Execute($"local {man.Id} 1"));
        Assert.Contains("You don't run", shell.Execute($"strike 1 {Shop(w, w.Locals[1]).Id}"));
        var local = Run(w, me);
        var shop = Shop(w, local);
        Assert.Contains("is out at", shell.Execute($"strike 1 {shop.Id}"));
        Assert.Contains("going back to work", shell.Execute("strike 1 off"));
        Assert.False(local.OnStrike);
    }

    [Fact]
    public void RivalsRunTheLocals()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 1, Size = CitySize.Medium });
        for (int i = 0; i < 3 * Content.WeeksPerYear; i++) sim.AdvanceWeek(playerAutopilot: true);
        var w = sim.World;
        bool Rival(GameEvent e) => e.GangId >= 0 && !w.GangById(e.GangId).IsPlayer;
        Assert.Contains(w.Events, e => e.Kind == EventKind.Union && e.Text.Contains(" took over ") && Rival(e));
        Assert.Contains(w.Events, e => e.Kind == EventKind.Union && e.Text.Contains(" walked out of ") && Rival(e));
    }
}
