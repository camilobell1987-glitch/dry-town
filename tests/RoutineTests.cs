using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class RoutineTests
{
    private static (World W, CityDirectory D) Start(ulong seed = 4)
    {
        var w = Simulation.New(new WorldSettings { Seed = seed, CityEventsEnabled = false, DirectorEnabled = false }).World;
        bool Tenement(Lot l) => l.Terrain == Terrain.Land && l.Use == LotUse.Empty;
        int People(Lot l) => l.Terrain != Terrain.Land ? 0 : l.Use == LotUse.Business ? 20 : Tenement(l) ? 10 : 0;
        return (w, new CityDirectory(w, People, Tenement));
    }

    private static IEnumerable<Citizen> Everyone(World w, CityDirectory d)
    {
        for (int by = 0; by < w.Map.BlocksY; by++)
        for (int bx = 0; bx < w.Map.BlocksX; bx++)
        for (int n = 0; n < d.CountOn(bx, by); n++)
            yield return d.Get(d.IdOf(bx, by, n));
    }

    [Fact]
    public void AShopkeeperOpensUpInTheMorningAndShutsAtNight()
    {
        var (w, d) = Start();
        var shop = w.Businesses.First(b => b.Kind == BusinessKind.Grocer && d.OwnerOf(b.Id) >= 0);
        var r = d.RoutineOf(d.OwnerOf(shop.Id));
        Assert.Equal(RoutineKind.Shopkeeper, r.Kind);
        Assert.All(r.Stops, s => Assert.Equal(shop.LotId, s.LotId));
        Assert.InRange(r.LeaveHome, 6, 8);
        Assert.InRange(r.Stops[^1].Leave, 18, 20);
    }

    [Fact]
    public void ALandlordCollectsTheRentOnSaturdayAndLooksInDuringTheWeek()
    {
        var (w, d) = Start();
        var landlord = Everyone(w, d).First(c => c.Owns.Count(l => l != c.HomeLotId) >= 2 && c.OwnsBusinessId < 0 && d.RestDayOf(c) == 6);
        var rent = d.RoutineOf(landlord.Id, 5);
        Assert.Equal(RoutineKind.Landlord, rent.Kind);
        Assert.All(rent.Stops, s => Assert.Contains(s.LotId, landlord.Owns));
        Assert.True(rent.Stops.Count >= 2);
        Assert.InRange(rent.LeaveHome, 8.5f, 10);
        float home = rent.Stops[^1].Leave + w.Map.Distance(w.Map.LotAt(rent.Stops[^1].LotId), w.Map.LotAt(landlord.HomeLotId)) / Content.ResidentPace;
        Assert.InRange(home, 19, 21.5f);
        // Each call comes after the last.
        Assert.True(rent.Stops.Zip(rent.Stops.Skip(1)).All(p => p.First.Leave < p.Second.Leave));
        // In the week he looks in on a building or two and is home in the afternoon, unless he goes out again.
        var tuesday = d.RoutineOf(landlord.Id, 1);
        var calls = tuesday.Stops.Where(s => s.Why == Errand.Rent).ToList();
        Assert.InRange(calls.Count, 1, 2);
        Assert.True(calls[^1].Leave < 17.5f);
    }

    [Fact]
    public void AWomanKeepingHouseWashesOnMondayShopsInTheWeekAndGoesToMarketOnSaturday()
    {
        var (w, d) = Start();
        var wives = Everyone(w, d).Where(c => c.Job == "Keeps house" && d.RoutineOf(c.Id, 1).Kind == RoutineKind.Errands).ToList();
        Assert.NotEmpty(wives);
        foreach (var wife in wives)
        {
            Assert.Equal(RoutineKind.Loafer, d.RoutineOf(wife.Id, 0).Kind);
            var r = d.RoutineOf(wife.Id, 1);
            Assert.InRange(r.Stops.Count, 1, 2);
            Assert.All(r.Stops, s => Assert.Contains(w.Businesses, b => b.LotId == s.LotId));
            Assert.InRange(r.LeaveHome, 9.5f, 11.5f);
            Assert.True(r.Stops[^1].Leave < 18);
        }
        Assert.Contains(wives, c => d.RoutineOf(c.Id, 5).Stops.Count(s => s.Why == Errand.Shop) >= 2);
    }

    [Fact]
    public void EveryoneWithAJobGoesToItAndComesHomeAndNobodyHasTooManyStops()
    {
        var (w, d) = Start();
        var people = Everyone(w, d).ToList();
        foreach (var c in people)
        {
            var r = d.RoutineOf(c.Id);
            Assert.True(r.Stops.Count <= Content.MostStops);
            if (r.Kind == RoutineKind.Loafer) continue;
            Assert.True(r.LeaveHome < r.Stops[0].Leave, $"{c.Job}: leaves at {r.LeaveHome} but done at the first stop at {r.Stops[0].Leave}");
            if (c.WorkBusinessId >= 0 && c.Age >= 14 && d.RestDayOf(c) == 6) Assert.Equal(RoutineKind.Worker, r.Kind);
        }
        // Most grown-ups have somewhere to be.
        var grown = people.Where(c => c.Age >= 18).ToList();
        Assert.True(grown.Count(c => d.RoutineOf(c.Id).Kind != RoutineKind.Loafer) > grown.Count / 2);
        Assert.Contains(people, c => d.RoutineOf(c.Id).Kind == RoutineKind.Park);
        Assert.Contains(people, c => d.RoutineOf(c.Id).Kind == RoutineKind.NightShift && d.RoutineOf(c.Id).Stops[0].Leave > 24);
    }

    [Fact]
    public void ADayIsTheSameEveryTime()
    {
        var (w, d) = Start();
        var (_, again) = Start();
        int id = d.IdOf(1, 1, 3);
        Assert.Equal(d.RoutineOf(id).Stops, again.RoutineOf(id).Stops);
        Assert.Equal(d.RoutineOf(id).LeaveHome, again.RoutineOf(id).LeaveHome);
    }

    /// <summary>A city peopled the way the map peoples it: homes on the residential lots, a few over the shops.</summary>
    private static (World W, CityDirectory D) Zoned(ulong seed, CitySize size)
    {
        var w = Simulation.New(new WorldSettings { Seed = seed, Size = size, CityEventsEnabled = false, DirectorEnabled = false }).World;
        bool Home(Lot l) => CityMap.IsHome(w.Map.FillerOf(l));
        int People(Lot l) => l.Terrain != Terrain.Land ? 0 : l.Use == LotUse.Business ? 20 : Home(l) ? 30 : 0;
        return (w, new CityDirectory(w, People, Home));
    }

    [Fact]
    public void FactoryHandsGoToTheFactoriesAndClerksDowntown()
    {
        var (w, d) = Zoned(7, CitySize.Huge);
        var workers = Everyone(w, d).Where(c => c.WorkBusinessId < 0 && d.RoutineOf(c.Id).Kind == RoutineKind.Worker).ToList();
        Filler At(Citizen c) => w.Map.FillerOf(w.Map.LotAt(d.RoutineOf(c.Id).Stops[0].LotId));
        var hands = workers.Where(c => c.Job is "Factory hand" or "Garment worker").ToList();
        Assert.True(hands.Count(c => At(c) is Filler.Factory or Filler.Gasworks) > hands.Count / 2);
        var clerks = workers.Where(c => c.Job is "Typist" or "Insurance man").ToList();
        Assert.True(clerks.Count(c => At(c) == Filler.Office) > clerks.Count / 3);
        Assert.Contains(clerks, c => w.Map.LotAt(d.RoutineOf(c.Id).Stops[0].LotId).Terrain == Terrain.Civic);
        Assert.Contains(workers, c => At(c) is Filler.Warehouse or Filler.Yard);
    }

    [Fact]
    public void NobodyWorksInSomebodysHouseOrAnEmptyLot()
    {
        var (w, d) = Zoned(7, CitySize.Huge);
        foreach (var c in Everyone(w, d).Where(c => d.RoutineOf(c.Id).Kind is RoutineKind.Worker or RoutineKind.NightShift))
        {
            var f = w.Map.FillerOf(w.Map.LotAt(d.RoutineOf(c.Id).Stops[0].LotId));
            Assert.False(CityMap.IsHome(f) || f is Filler.Vacant or Filler.Parking or Filler.PocketPark, $"{c.Job} works at a {f}");
        }
    }

    [Fact]
    public void EveryDayOfTheWeekHoldsTogether()
    {
        var (w, d) = Start();
        foreach (var c in Everyone(w, d))
        for (int day = 0; day < 7; day++)
        {
            var r = d.RoutineOf(c.Id, day);
            Assert.True(r.Stops.Count <= Content.MostStops);
            if (r.Kind == RoutineKind.Loafer) continue;
            Assert.True(r.LeaveHome < r.Stops[0].Leave, $"{c.Job} on {Content.DayNames[day]}: leaves at {r.LeaveHome}, done at {r.Stops[0].Leave}");
            Assert.True(r.Stops.Zip(r.Stops.Skip(1)).All(p => p.First.Leave <= p.Second.Leave), $"{c.Job} on {Content.DayNames[day]}");
            Assert.All(r.Stops, s => Assert.True(s.Outside >= 0));
        }
    }

    [Fact]
    public void AWorkingManWorksSixDaysAHalfDayOnSaturdayAndRestsOnSunday()
    {
        var (w, d) = Start();
        var man = Everyone(w, d).First(c => c.WorkBusinessId >= 0 && c.Age >= 18 && d.RestDayOf(c) == 6 && d.RoutineOf(c.Id).Kind == RoutineKind.Worker);
        for (int day = 0; day < 5; day++) Assert.Contains(d.RoutineOf(man.Id, day).Stops, s => s.Why == Errand.Work && s.Leave >= 16);
        Assert.Contains(d.RoutineOf(man.Id, 5).Stops, s => s.Why == Errand.Work && s.Leave < 14);
        Assert.DoesNotContain(d.RoutineOf(man.Id, 6).Stops, s => s.Why == Errand.Work);
    }

    [Fact]
    public void ACatholicFamilyGoesToMassTogetherOnSunday()
    {
        var (w, d) = Start(7);
        var families = Everyone(w, d).Where(c => c.Origin is Ethnicity.Irish or Ethnicity.Italian or Ethnicity.Polish && c.Ties.Any(t => t.Kin == Kin.Spouse))
            .Where(c => d.RoutineOf(c.Id, 6).Stops.FirstOrDefault().Why == Errand.Church).ToList();
        Assert.NotEmpty(families);
        foreach (var c in families.Take(40))
        {
            var spouse = c.Ties.First(t => t.Kin == Kin.Spouse).Id;
            var mine = d.RoutineOf(c.Id, 6).Stops[0];
            var theirs = d.RoutineOf(spouse, 6).Stops.FirstOrDefault();
            // The same church at the same Mass, unless one of them would rather not go.
            if (theirs.Why == Errand.Church) Assert.Equal((mine.LotId, mine.Leave), (theirs.LotId, theirs.Leave));
            Assert.Equal(Terrain.Church, w.Map.LotAt(mine.LotId).Terrain);
        }
        // Hardly anyone goes to church on a working day.
        Assert.DoesNotContain(Everyone(w, d), c => d.RoutineOf(c.Id, 1).Stops.Any(s => s.Why == Errand.Church && s.Leave < 17));
    }

    [Fact]
    public void ASouseDrinksMostNightsAndAFamilyManStaysIn()
    {
        var (w, d) = Start(7);
        var people = Everyone(w, d).Where(c => c.Age >= 18).ToList();
        var souse = people.First(c => d.CharacterOf(c.Id).Traits.Contains(Trait.Souse) && d.SecretsOf(c.Id).Any(s => s.Vice == Vice.Drink)
            && d.RoutineOf(c.Id).Kind == RoutineKind.Worker);
        int nights = Enumerable.Range(0, 7).Count(day => d.RoutineOf(souse.Id, day).Stops.Any(s => s.Why == Errand.Drink));
        Assert.True(nights >= 5, $"drinks {nights} nights");
        foreach (var c in people.Where(c => d.CharacterOf(c.Id).Traits.Contains(Trait.FamilyMan)).Take(60))
            for (int day = 0; day < 7; day++)
                Assert.DoesNotContain(d.RoutineOf(c.Id, day).Stops, s => s.Why is Errand.Drink or Errand.Gamble or Errand.NightOut or Errand.Lover);
    }

    [Fact]
    public void SaturdayNightIsWhenTheYoungGoOut()
    {
        var (w, d) = Start(7);
        var people = Everyone(w, d).Where(c => c.Age >= 18).ToList();
        int Out(int day) => people.Count(c => d.RoutineOf(c.Id, day).Stops.Any(s => s.Why == Errand.NightOut));
        Assert.True(Out(5) > 3 * Math.Max(1, Out(1)), $"Saturday {Out(5)}, Tuesday {Out(1)}");
        // A man with a woman on the side calls on her at home.
        var visits = people.SelectMany(c => d.RoutineOf(c.Id, 2).Stops.Where(s => s.Why == Errand.Lover).Select(s => (c, s))).ToList();
        Assert.NotEmpty(visits);
        Assert.All(visits, v => Assert.Equal(d.Get(d.SecretsOf(v.c.Id).First(x => x.Vice == Vice.Lover).Other).HomeLotId, v.s.LotId));
    }

    [Fact]
    public void AJewWhoKeepsTheSabbathRestsOnSaturdayAndAJewishShopShutsThen()
    {
        var (w, d) = Start(7);
        var people = Everyone(w, d).ToList();
        var observant = people.Where(c => c.Origin == Ethnicity.Jewish && c.Age >= 18 && d.RestDayOf(c) == 5 && d.RoutineOf(c.Id).Kind == RoutineKind.Worker).ToList();
        Assert.NotEmpty(observant);
        foreach (var c in observant)
        {
            Assert.DoesNotContain(d.RoutineOf(c.Id, 5).Stops, s => s.Why == Errand.Work);
            Assert.Contains(d.RoutineOf(c.Id, 6).Stops, s => s.Why == Errand.Work);
            // Home before sundown on Friday.
            Assert.True(d.RoutineOf(c.Id, 4).Stops.First(s => s.Why == Errand.Work).Leave <= 15.5f);
        }
        var shops = people.Where(c => c.Origin == Ethnicity.Jewish && c.OwnsBusinessId >= 0
            && w.BusinessById(c.OwnsBusinessId).Kind is not (BusinessKind.Diner or BusinessKind.Hotel or BusinessKind.PoolHall)).ToList();
        Assert.All(shops, c => Assert.NotEqual(RoutineKind.Shopkeeper, d.RoutineOf(c.Id, 5).Kind));
        Assert.All(shops, c => Assert.Equal(RoutineKind.Shopkeeper, d.RoutineOf(c.Id, 6).Kind));
    }
}
