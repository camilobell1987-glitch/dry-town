using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class RepealTests
{
    private const int RepealWeek = (Content.RepealYear - Content.StartYear) * Content.WeeksPerYear;

    /// <summary>A quiet city on the eve of repeal, or after it.</summary>
    private static (Simulation Sim, World W, Gang Me) Start(int gangs = 1, int week = RepealWeek)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = gangs, CityEventsEnabled = false, DirectorEnabled = false });
        sim.World.Week = week;
        sim.World.Player.Cash = 50_000;
        return (sim, sim.World, sim.World.Player);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    /// <summary>Have the gang protect a business, with one of its men handling it.</summary>
    private static Business Protect(World w, Gang g, Business b)
    {
        b.ProtectorGangId = g.Id;
        b.HandlerHoodId = w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId).Id;
        b.ShutWeeks = 0;
        return b;
    }

    private static Business Hotel(World w) => w.Businesses.First(b => b.Kind == BusinessKind.Hotel && b.Id != w.WireBusinessId);

    [Fact]
    public void RepealPutsOutTheStills()
    {
        var (sim, w, me) = Start(week: RepealWeek - 1);
        var still = Protect(w, me, w.Businesses.First(b => Content.RacketsFor(b.Kind).Contains(RacketKind.Still)));
        still.Racket = RacketKind.Still;
        sim.AdvanceWeek(Mine(w));
        Assert.False(w.Prohibition);
        Assert.Equal(RacketKind.None, still.Racket);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Era && e.Text.Contains("racing wire has come to town"));
    }

    [Fact]
    public void SlotMachinesComeInWithRepeal()
    {
        var (_, w, me) = Start(week: RepealWeek - 1);
        var diner = Protect(w, me, w.Businesses.First(b => b.Kind == BusinessKind.Diner));
        diner.Racket = RacketKind.None;
        Assert.Equal("Not while Prohibition lasts.", w.CannotOpenRacket(me, diner, RacketKind.Slots));
        w.Week = RepealWeek;
        Assert.Null(w.CannotOpenRacket(me, diner, RacketKind.Slots));
        // Since repeal a speakeasy is only a bar, and can make way for something better.
        diner.Racket = RacketKind.Speakeasy;
        Assert.Null(w.CannotOpenRacket(me, diner, RacketKind.Slots));
        diner.Racket = RacketKind.Numbers;
        Assert.Equal("The back room is taken.", w.CannotOpenRacket(me, diner, RacketKind.Slots));
    }

    [Fact]
    public void ACasinoNeedsAHotelYouOwnAndPaysClean()
    {
        var (sim, w, me) = Start();
        var hotel = Protect(w, me, Hotel(w));
        hotel.Racket = RacketKind.Numbers;
        Assert.Equal("A casino needs a hotel you own outright.", w.CannotOpenRacket(me, hotel, RacketKind.Casino));
        Assert.True(w.BuyFront(me, hotel) > 0);
        // A casino takes over whatever was in the back room.
        Assert.Null(w.CannotOpenRacket(me, hotel, RacketKind.Casino));

        hotel.Racket = RacketKind.Casino;
        me.CleanCash = 0;
        sim.AdvanceWeek(Mine(w));
        var ledger = w.LastLedger[me.Id];
        Assert.True(ledger.Legit >= Content.Rackets[RacketKind.Casino].WeeklyIncome / 2);
        Assert.True(me.Clean >= ledger.Legit);
    }

    [Fact]
    public void ACasinoShutsWhenItsOwnersLoseTheHotel()
    {
        var (sim, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var hotel = Protect(w, me, Hotel(w));
        w.BuyFront(me, hotel);
        hotel.Racket = RacketKind.Casino;
        Protect(w, rival, hotel);
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [me.Id] = new(), [rival.Id] = new() });
        Assert.Equal(RacketKind.None, hotel.Racket);
        Assert.Contains(w.Events, e => e.Text == $"With {me.Name} gone from it, the casino at {hotel.Name} shut its doors.");
    }

    [Fact]
    public void ACasinoRunsQuietlyWithTheWardsAlderman()
    {
        var (sim, w, me) = Start();
        var hotel = Protect(w, me, Hotel(w));
        w.BuyFront(me, hotel);
        hotel.Racket = RacketKind.Casino;
        int HeatAfterAWeek()
        {
            me.Heat = 20;
            sim.AdvanceWeek(Mine(w));
            return me.Heat;
        }
        Politics.WardOf(w, hotel).OwnerGangId = -1;
        int uncovered = HeatAfterAWeek();
        Politics.WardOf(w, hotel).OwnerGangId = me.Id;
        int covered = HeatAfterAWeek();
        Assert.True(uncovered - covered >= Content.CasinoHeatUncovered - 1, $"{uncovered} against {covered}");
    }

    [Fact]
    public void TheWireTakesACutOfEveryOtherBookie()
    {
        var (sim, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var room = Protect(w, rival, w.BusinessById(w.WireBusinessId));
        Assert.Equal(rival.Id, w.WireGangId);
        var bookie = Protect(w, me, w.Businesses.First(b => b.Id != room.Id && b.Kind == BusinessKind.Barber));
        bookie.Racket = RacketKind.Bookie;
        rival.Cash = 0;

        // The rival sits the week out, so all that changes is the wire.
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [me.Id] = new(), [rival.Id] = new() });
        Assert.True(w.LastLedger[rival.Id].Wire >= Content.WireFee);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Gmen && e.Text.StartsWith($"{rival.Name} runs the racing wire now"));
        Assert.Contains($"{rival.Name} runs the racing wire", new CommandShell(sim).Execute("wire"));
    }

    [Fact]
    public void TheWireIsWorthMoreToItsOwnBookies()
    {
        var (_, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        foreach (var b in w.Businesses) b.Racket = RacketKind.None;
        var mine = Protect(w, me, w.Businesses.First(b => b.Kind == BusinessKind.Barber));
        var theirs = Protect(w, rival, w.Businesses.Last(b => b.Kind == BusinessKind.Diner));
        mine.Racket = theirs.Racket = RacketKind.Bookie;
        Assert.Equal((int)(Content.Rackets[RacketKind.Bookie].WeeklyIncome * Content.WireBonus) + Content.WireFee, w.WireWorth(me.Id));
        w.Week = RepealWeek - 1;
        Assert.Equal(0, w.WireWorth(me.Id));
        Assert.Equal(-1, w.WireGangId);
    }

    [Fact]
    public void TheBureauNamesTheBiggestOutfitEachJanuary()
    {
        var (sim, w, me) = Start(gangs: 2, week: RepealWeek + Content.WeeksPerYear - 1);
        foreach (var b in w.Businesses.Where(b => b.IsOpen).Take(12)) Protect(w, me, b);
        sim.AdvanceWeek(Mine(w));
        Assert.Equal(me, w.PublicEnemy);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Gmen && e.Text.Contains("Public Enemy Number One"));
        Assert.Contains("named you", new CommandShell(sim).Execute("gmen"));
        Assert.Equal(0, w.GmenChance(w.LivingGangs.First(g => !g.IsPlayer)));
    }

    [Fact]
    public void TheGmenPickUpThePublicEnemysMenOnFederalWarrants()
    {
        var (sim, w, me) = Start(week: RepealWeek + 5);
        w.Repeal.PublicEnemyGangId = me.Id;
        w.Repeal.NamedWeek = w.Week;
        for (int i = 0; i < 300 && !w.Events.Any(e => e.Kind == EventKind.Gmen && e.Text.StartsWith("G-men picked up ")); i++)
        {
            me.Heat = 100;
            me.Cash = 50_000;
            if (w.AvailableHoodsOf(me.Id).Count() < 3) w.NewHood(me.Id, bossQuality: false);
            sim.AdvanceWeek(Mine(w));
        }
        var raid = w.Events.First(e => e.Kind == EventKind.Gmen && e.Text.StartsWith("G-men picked up "));
        var man = w.Hoods.First(h => h.GangId == me.Id && raid.Text.StartsWith($"G-men picked up {h.Name} "));
        Assert.True(man.Case is { Federal: true } || man.Record.Any(r => r.Text.Contains("federal")));
    }

    [Fact]
    public void TakingTheBossClearsTheBureausList()
    {
        var (sim, w, me) = Start(week: RepealWeek + 5);
        foreach (var h in w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).ToList()) h.State = HoodState.Jailed;
        w.Repeal.PublicEnemyGangId = me.Id;
        for (int i = 0; i < 300 && w.PublicEnemy == me; i++)
        {
            me.Heat = 100;
            sim.AdvanceWeek(Mine(w));
        }
        Assert.Null(w.PublicEnemy);
        Assert.Equal(HoodState.Jailed, w.HoodById(me.BossHoodId).State);
        Assert.Contains(w.Events, e => e.Text.Contains("Public Enemy Number One himself"));
    }

    [Fact]
    public void NoneOfItBeforeRepeal()
    {
        var (sim, w, me) = Start(week: 0);
        w.Repeal.PublicEnemyGangId = me.Id;
        Assert.Equal(-1, w.WireGangId);
        Assert.Contains("comes to town with repeal", new CommandShell(sim).Execute("wire"));
        Assert.Contains("until after repeal", new CommandShell(sim).Execute("gmen"));
        for (int i = 0; i < 20; i++) sim.AdvanceWeek(Mine(w));
        Assert.DoesNotContain(w.Events, e => e.Kind == EventKind.Gmen);
    }

    [Fact]
    public void TheWireAndThePublicEnemyAreSaved()
    {
        var (sim, w, me) = Start();
        w.Repeal.PublicEnemyGangId = me.Id;
        w.Repeal.NamedWeek = 900;
        w.Repeal.WireGangId = me.Id;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(me.Id, loaded.Repeal.PublicEnemyGangId);
        Assert.Equal(900, loaded.Repeal.NamedWeek);
        Assert.Equal(me.Id, loaded.Repeal.WireGangId);
    }
}
