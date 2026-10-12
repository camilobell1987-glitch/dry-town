using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class BreweryTests
{
    private const int Y1927 = (Content.UntouchablesYear - Content.StartYear) * Content.WeeksPerYear;

    /// <summary>A quiet city in the given week, where the player has money and nobody else to bother him.</summary>
    private static (Simulation Sim, World W, Gang Me) Start(int week = 0, int gangs = 1)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = gangs, CityEventsEnabled = false, DirectorEnabled = false, StartWeek = week });
        var me = sim.World.Player;
        me.Cash = 20_000;
        me.Heat = 0;
        return (sim, sim.World, me);
    }

    /// <summary>Put businesses nobody protects that can hide this racket on the gang's books, with it in the back.</summary>
    private static List<Business> Give(World w, Gang g, RacketKind racket, int count)
    {
        var men = w.AvailableHoodsOf(g.Id).Where(h => h.Id != g.BossHoodId).ToList();
        var places = w.Businesses.Where(b => b.IsOpen && !b.IsProtected && Content.RacketsFor(b.Kind).Contains(racket))
            .OrderBy(b => w.BlocksFromHq(g, b)).ThenBy(b => b.Id).Take(count).ToList();
        int i = 0;
        foreach (var b in places)
        {
            b.ProtectorGangId = g.Id;
            b.HandlerHoodId = men[i++ % men.Count].Id;
            b.Racket = racket;
        }
        return places;
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    [Fact]
    public void TheBreweryPutsBeerWhereTheLinesDontReach()
    {
        var (_, w, me) = Start();
        var bars = Give(w, me, RacketKind.Speakeasy, 10);
        w.BuyLine(me, w.SupplyLines[0]);
        Assert.Empty(w.OnTap(me.Id));
        Assert.Equal(1, w.BoozeFactor(bars.First(b => !w.Supplied(me.Id).Contains(b.Id)), me));

        var brewery = Give(w, me, RacketKind.Brewery, 1)[0];
        Assert.Equal(BusinessKind.Warehouse, brewery.Kind);
        var supplied = w.Supplied(me.Id);
        var tap = w.OnTap(me.Id);
        Assert.Equal(Content.BreweryRoom, tap.Count);
        Assert.Empty(tap.Intersect(supplied));
        var beer = bars.First(b => tap.Contains(b.Id));
        var gin = bars.Single(b => !tap.Contains(b.Id) && !supplied.Contains(b.Id));
        Assert.Equal(1 + Content.BeerBonus, w.BoozeFactor(beer, me), 6);
        Assert.Equal(1, w.BoozeFactor(gin, me));
        Assert.Equal(0, w.BadBatchChance(beer, me));
        Assert.True(w.BadBatchChance(gin, me) > 0);
        Assert.Contains($", {Content.BreweryRoom} pour beer from your brewery (+25%); the rest sell bathtub gin.", Reports.Booze(w, me.Id));
    }

    [Fact]
    public void OneBreweryToAnOutfitAndOnlyInAWarehouse()
    {
        var (_, w, me) = Start();
        var houses = Give(w, me, RacketKind.LoanShark, 2);
        foreach (var b in houses) b.Racket = RacketKind.None;
        var diner = Give(w, me, RacketKind.Speakeasy, 1)[0];
        diner.Racket = RacketKind.None;
        Assert.Equal($"A {Content.Label(diner.Kind).ToLowerInvariant()} can't hide that.", w.CannotOpenRacket(me, diner, RacketKind.Brewery));
        var warehouse = w.Businesses.First(b => b.Kind == BusinessKind.Warehouse && !b.IsProtected && b.IsOpen);
        var other = w.Businesses.First(b => b.Kind == BusinessKind.Warehouse && !b.IsProtected && b.IsOpen && b.Id != warehouse.Id);
        warehouse.ProtectorGangId = other.ProtectorGangId = me.Id;
        Assert.Null(w.CannotOpenRacket(me, warehouse, RacketKind.Brewery));
        warehouse.Racket = RacketKind.Brewery;
        Assert.Equal($"{me.Name} brews at {warehouse.Name} already.", w.CannotOpenRacket(me, other, RacketKind.Brewery));
        me.Cash = 100;
        warehouse.Racket = RacketKind.None;
        Assert.Equal("That costs $3,000.", w.CannotOpenRacket(me, warehouse, RacketKind.Brewery));
    }

    [Fact]
    public void TheSquadGoesForTheBreweryFirst()
    {
        var (sim, w, me) = Start(Y1927 + 10, gangs: 2);
        Give(w, me, RacketKind.Speakeasy, 6);
        var brewery = Give(w, me, RacketKind.Brewery, 1)[0];
        foreach (var h in w.HoodsOf(me.Id)) h.Stealth = 1;
        Assert.Equal(6 + Content.BreweryBootlegging, w.Bootlegging(me));
        w.Untouchables.Formed = true;
        w.Untouchables.TargetGangId = me.Id;
        w.Untouchables.NamedWeek = w.Untouchables.LastRaidWeek = w.Week;
        for (int i = 0; i < 300 && brewery.Racket == RacketKind.Brewery; i++)
        {
            w.Untouchables.Evidence = 0;
            me.Heat = 100;
            Week(sim);
        }
        Assert.Equal(RacketKind.None, brewery.Racket);
        Assert.Equal(Content.BreweryEvidence, w.Untouchables.Evidence);
        Assert.Contains(w.Events, e => e.Text == $"The Untouchables smashed {me.Name}'s brewery at {brewery.Name}, with the papers watching.");
    }

    [Fact]
    public void RepealMakesTheBreweryHonest()
    {
        var (sim, w, me) = Start((Content.RepealYear - Content.StartYear) * Content.WeeksPerYear - 1);
        var brewery = Give(w, me, RacketKind.Brewery, 1)[0];
        Week(sim);
        Assert.False(w.Prohibition);
        Assert.Equal(RacketKind.Brewery, brewery.Racket);
        Assert.Contains(w.Events, e => e.Text == $"{me.Name}'s brewery at {brewery.Name} took out a licence. Its beer is legal now, and so is the money.");
        long legit = w.Ledger(me.Id).Legit;
        me.Heat = 0;
        Week(sim);
        Assert.True(w.Ledger(me.Id).Legit - legit >= Content.Rackets[RacketKind.Brewery].WeeklyIncome / 2);
        Assert.Equal("Since repeal, anyone can sell a drink.", w.CannotOpenRacket(me, brewery, RacketKind.Brewery));
    }

    [Fact]
    public void ARivalWithThirstySpeakeasiesBuildsOne()
    {
        var (_, w, _) = Start(gangs: 2);
        var g = w.LivingGangs.First(x => !x.IsPlayer);
        g.Cash = 20_000;
        g.Heat = 0;
        foreach (var b in w.TurfOf(g.Id)) b.Racket = RacketKind.Numbers;
        Give(w, g, RacketKind.Speakeasy, 5);
        var warehouse = w.Businesses.First(b => b.Kind == BusinessKind.Warehouse && !b.IsProtected && b.IsOpen);
        warehouse.ProtectorGangId = g.Id;
        Assert.Contains(AiPlanner.Plan(w, g), o => o is RacketOrder r && r.BusinessId == warehouse.Id && r.Racket == RacketKind.Brewery);
    }

    [Fact]
    public void TheConsoleOpensABrewery()
    {
        var (sim, w, me) = Start();
        var warehouse = w.Businesses.First(b => b.Kind == BusinessKind.Warehouse && !b.IsProtected && b.IsOpen);
        warehouse.ProtectorGangId = me.Id;
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        var shell = new CommandShell(sim);
        Assert.Contains("opens a brewery behind", shell.Execute($"racket {man.Id} {warehouse.Id} brewery"));
        shell.Execute("end");
        Assert.Equal(RacketKind.Brewery, warehouse.Racket);
        Assert.Contains($"Your brewery is at {warehouse.Name} ({warehouse.Id}).", Reports.Booze(w, me.Id));
    }
}
