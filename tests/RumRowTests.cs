using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class RumRowTests
{
    private const int Spring1923 = 3 * Content.WeeksPerYear + 15;

    /// <summary>A quiet city in the given week, where the player has money and nobody else to bother him.</summary>
    private static (Simulation Sim, World W, Gang Me) Start(int week = Spring1923, int gangs = 1)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = gangs, CityEventsEnabled = false, DirectorEnabled = false, StartWeek = week });
        var me = sim.World.Player;
        me.Cash = 20_000;
        me.Heat = 0;
        return (sim, sim.World, me);
    }

    /// <summary>Put businesses nobody protects on the gang's books as speakeasies.</summary>
    private static List<Business> Speakeasies(World w, Gang g, int count)
    {
        var bars = w.Businesses.Where(b => b.IsOpen && !b.IsProtected && Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy))
            .OrderBy(b => w.BlocksFromHq(g, b)).ThenBy(b => b.Id).Take(count).ToList();
        foreach (var b in bars)
        {
            b.ProtectorGangId = g.Id;
            b.HandlerHoodId = w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId).Id;
            b.Racket = RacketKind.Speakeasy;
        }
        return bars;
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    [Fact]
    public void ABoatGoesForALoadAndLandsIt()
    {
        var (sim, w, me) = Start();
        Assert.Null(w.CannotBuyBoat(me));
        var boat = w.BuyBoat(me)!;
        Assert.Equal(20_000 - Content.BoatPrice, me.Cash);
        Assert.Contains(w.Events, e => e.Text == $"{me.Name} bought a fast boat, {boat.Name}, to run liquor in from outside.");

        // The Coast Guard catches some landings and storms take others; one of a dozen runs comes in.
        for (int run = 0; run < 12 && w.Boats.Contains(boat) && me.Cellar == 0; run++)
        {
            me.Cash = 20_000;
            Assert.True(w.Sail(me, boat, Run.Canada));
            Assert.True(boat.AtSea);
            Assert.Contains("She's away to St. Pierre.", w.CannotSail(me, boat, Run.Islands));
            Week(sim);
            Week(sim);
            Assert.False(boat.AtSea);
        }
        Assert.Equal(Content.Runs[Run.Canada].Cases, me.Cellar);
        Assert.Contains(w.Events, e => e.Text == $"{World.Capital(boat.Name)} landed 12 cases of Canadian whisky from St. Pierre for {me.Name}.");
    }

    [Fact]
    public void TheWarehouseKeepsSpeakeasiesInRealLiquor()
    {
        var (sim, w, me) = Start();
        var bars = Speakeasies(w, me, 3);
        Assert.Empty(w.Supplied(me.Id));
        me.Cellar = 5;
        Assert.Equal(3, w.Supplied(me.Id).Count);
        Assert.Equal(1 + Content.LineBonus, w.BoozeFactor(bars[0], me), 6);
        Assert.Equal(0, w.BadBatchChance(bars[0], me));
        Week(sim);
        Assert.Equal(2, me.Cellar);
        Week(sim);
        Assert.Equal(0, me.Cellar);

        // A supply line pours first, and the warehouse only makes up the rest.
        me.Cellar = 5;
        w.BuyLine(me, w.SupplyLines.First(l => l.Kind == LineKind.Truck));
        Week(sim);
        Assert.Equal(5, me.Cellar);
    }

    [Fact]
    public void WhatWontFitIsSold()
    {
        var (sim, w, me) = Start();
        var boat = w.BuyBoat(me)!;
        me.Cellar = Content.CellarCap - 2;
        for (int run = 0; run < 12 && w.Boats.Contains(boat) && me.Cellar < Content.CellarCap; run++)
        {
            me.Cash = 20_000;
            w.Sail(me, boat, Run.Canada);
            Week(sim);
            Week(sim);
        }
        Assert.Equal(Content.CellarCap, me.Cellar);
        Assert.Contains(w.Events, e => e.Text.EndsWith($", and the 10 cases there was no room for sold for ${10 * Content.SurplusCase:N0}."));
    }

    [Fact]
    public void StormsComeInTheirSeasons()
    {
        Assert.True(Content.InSeason(Run.Canada, 1));
        Assert.True(Content.InSeason(Run.Canada, 50));
        Assert.False(Content.InSeason(Run.Canada, 30));
        Assert.True(Content.InSeason(Run.Islands, 36));
        Assert.False(Content.InSeason(Run.Islands, 5));

        var (_, w, _) = Start(3 * Content.WeeksPerYear + 35);
        Assert.Equal(Content.StormSeason, w.StormChance(Run.Islands));
        Assert.Equal(Content.Storm, w.StormChance(Run.Canada));
    }

    [Fact]
    public void ABoatCanBeLostAtSea()
    {
        var (sim, w, me) = Start(3 * Content.WeeksPerYear + 33);
        var boats = new List<Boat>();
        for (int i = 0; i < 300 && !w.Events.Any(e => e.Text.Contains(" went down in the hurricanes ")); i++)
        {
            w.Boats.Clear();
            me.Cash = 20_000;
            var boat = w.BuyBoat(me)!;
            w.Sail(me, boat, Run.Islands);
            Week(sim);
            w.Week = 3 * Content.WeeksPerYear + 33;
        }
        Assert.Contains(w.Events, e => e.Text.StartsWith($"{me.Name}'s boat ") && e.Text.EndsWith(" on the run from Nassau, with her crew and 30 cases of Bahamas rum."));
        Assert.Contains(w.Events, e => e.Text.EndsWith(" ran into the hurricanes on the Nassau run, and the crew put the cargo over the side to keep her afloat."));
    }

    [Fact]
    public void TheRumPatrolAndTheDocks()
    {
        var (_, w, me) = Start();
        Assert.Equal(Content.CoastGuard, w.CoastGuardChance(me), 6);
        me.Heat = 50;
        Assert.Equal(Content.CoastGuard + 50 * Content.CoastGuardPerHeat, w.CoastGuardChance(me), 6);
        me.Heat = 0;
        w.Locals.First(l => l.IsDocks).OwnerGangId = me.Id;
        Assert.Equal(Content.CoastGuard * Content.DocksLanding, w.CoastGuardChance(me), 6);

        var (_, later, him) = Start((Content.RumPatrolYear - Content.StartYear) * Content.WeeksPerYear);
        Assert.Equal(Content.RumPatrol, later.CoastGuardChance(him), 6);
    }

    [Fact]
    public void WhatStopsABoat()
    {
        var (_, w, me) = Start();
        var boat = w.BuyBoat(me)!;
        me.Cash = 100;
        Assert.Equal("The load costs $250.", w.CannotSail(me, boat, Run.Canada));
        Assert.Equal($"A fast boat and her crew cost ${Content.BoatPrice:N0}.", w.CannotBuyBoat(me));
        me.Cash = 20_000;
        w.BuyBoat(me);
        w.BuyBoat(me);
        Assert.Equal($"You run {Content.MaxBoats} boats already.", w.CannotBuyBoat(me));

        var (_, late, him) = Start((Content.RepealYear - Content.StartYear) * Content.WeeksPerYear - 3);
        var last = late.BuyBoat(him)!;
        Assert.Null(late.CannotSail(him, last, Run.Canada));
        Assert.Equal("She wouldn't be back before repeal.", late.CannotSail(him, last, Run.Islands));
    }

    [Fact]
    public void RepealSellsTheBoats()
    {
        var (sim, w, me) = Start((Content.RepealYear - Content.StartYear) * Content.WeeksPerYear - 1);
        w.BuyBoat(me);
        w.BuyBoat(me);
        me.Cellar = 10;
        Week(sim);
        Assert.Empty(w.Boats);
        Assert.Equal(0, me.Cellar);
        Assert.Contains(w.Events, e => e.Text == $"With repeal, {me.Name} sold its 2 boats to fishermen for ${Content.BoatPrice / 2:N0}.");
        Assert.Equal("Since repeal, nobody needs a rum-runner.", w.CannotBuyBoat(me));
    }

    [Fact]
    public void ARivalWithNoLineToBeHadBuysABoatAndSendsItOut()
    {
        var (_, w, _) = Start(gangs: 2);
        var g = w.LivingGangs.First(x => !x.IsPlayer);
        g.Cash = 50_000;
        Speakeasies(w, g, 4);
        foreach (var line in w.SupplyLines) line.OwnerGangId = w.Player.Id;
        Assert.Contains(AiPlanner.Plan(w, g), o => o is BoatOrder);

        var boat = w.BuyBoat(g)!;
        Assert.Contains(AiPlanner.Plan(w, g), o => o is SailOrder s && s.BoatId == boat.Id && s.Run == Run.Islands);
        // With a month's liquor in the warehouse, she stays at the dock.
        g.Cellar = 16;
        Assert.DoesNotContain(AiPlanner.Plan(w, g), o => o is SailOrder or BoatOrder);
    }

    [Fact]
    public void TheConsoleShowsTheBoatsAndTheyAreSaved()
    {
        var (sim, w, me) = Start();
        var shell = new CommandShell(sim);
        Assert.Contains("You have no boats.", shell.Execute("boats"));
        Assert.StartsWith("You bought the Nellie B.", shell.Execute("buyboat"));
        Assert.Equal("The Nellie B. sails for Nassau. She's due back with 30 cases in 4 weeks.\n", shell.Execute("sail 0 islands"));
        string boats = shell.Execute("boats")!;
        Assert.Contains("The Nellie B., away to Nassau, due back in 4 weeks with 30 cases", boats);
        Assert.Contains("Chance the Coast Guard catches a landing: 8%.", boats);
        Assert.Contains("boats (rum row)", CommandShell.Help);

        me.Cellar = 7;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var again = loaded.BoatById(0)!;
        Assert.Equal(Run.Islands, again.Run);
        Assert.Equal(30, again.Cases);
        Assert.Equal(7, loaded.Player.Cellar);
    }
}
