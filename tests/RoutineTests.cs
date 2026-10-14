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
    public void ALandlordGoesRoundHisBuildingsAndIsHomeAfterDark()
    {
        var (w, d) = Start();
        var landlord = Everyone(w, d).First(c => c.Owns.Count(l => l != c.HomeLotId) >= 2 && c.OwnsBusinessId < 0);
        var r = d.RoutineOf(landlord.Id);
        Assert.Equal(RoutineKind.Landlord, r.Kind);
        Assert.All(r.Stops, s => Assert.Contains(s.LotId, landlord.Owns));
        Assert.InRange(r.LeaveHome, 8.5f, 10);
        float home = r.Stops[^1].Leave + w.Map.Distance(w.Map.LotAt(r.Stops[^1].LotId), w.Map.LotAt(landlord.HomeLotId)) / Content.ResidentPace;
        Assert.InRange(home, 19, 21.5f);
        // Each call comes after the last.
        Assert.True(r.Stops.Zip(r.Stops.Skip(1)).All(p => p.First.Leave < p.Second.Leave));
    }

    [Fact]
    public void AWomanKeepingHouseGoesToTheShopsAndIsHomeForSupper()
    {
        var (w, d) = Start();
        var wife = Everyone(w, d).First(c => c.Job == "Keeps house" && d.RoutineOf(c.Id).Kind == RoutineKind.Errands);
        var r = d.RoutineOf(wife.Id);
        Assert.InRange(r.Stops.Count, 1, 2);
        Assert.All(r.Stops, s => Assert.Contains(w.Businesses, b => b.LotId == s.LotId));
        Assert.InRange(r.LeaveHome, 9.5f, 11.5f);
        Assert.True(r.Stops[^1].Leave < 18);
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
            if (c.WorkBusinessId >= 0) Assert.Equal(RoutineKind.Worker, r.Kind);
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
}
