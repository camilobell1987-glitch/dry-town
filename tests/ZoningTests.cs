using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class ZoningTests
{
    private static World City(ulong seed, CitySize size = CitySize.Large) =>
        Simulation.New(new WorldSettings { Seed = seed, Size = size, CityEventsEnabled = false, DirectorEnabled = false }).World;

    private static IEnumerable<(int X, int Y)> Blocks(CityMap m) =>
        Enumerable.Range(0, m.BlocksX * m.BlocksY).Select(i => (i % m.BlocksX, i / m.BlocksX));

    [Theory]
    [InlineData(CitySize.Large)]
    [InlineData(CitySize.Huge)]
    [InlineData(CitySize.Metropolis)]
    public void ABigCityHasEveryKindOfNeighbourhood(CitySize size)
    {
        var m = City(3, size).Map;
        var zones = Blocks(m).Select(b => m.ZoneOf(b.X, b.Y)).ToList();
        foreach (var zone in new[] { Zone.Downtown, Zone.Industrial, Zone.Tenements, Zone.Rowhouses, Zone.GoldCoast, Zone.Suburbs })
            Assert.Contains(zone, zones);
        // The tenements are the biggest part of town, and downtown one of the smallest.
        Assert.True(zones.Count(z => z == Zone.Tenements) > zones.Count(z => z == Zone.Suburbs));
        Assert.True(zones.Count(z => z == Zone.Downtown) < zones.Count(z => z == Zone.Industrial));
    }

    [Fact]
    public void DowntownIsInTheMiddleAndTheSuburbsAtTheEdge()
    {
        var m = City(4, CitySize.Huge).Map;
        double FromMiddle((int X, int Y) b) => Math.Abs(b.X + 0.5 - m.BlocksX / 2.0) / m.BlocksX + Math.Abs(b.Y + 0.5 - m.BlocksY / 2.0) / m.BlocksY;
        double Mean(Zone z) => Blocks(m).Where(b => m.ZoneOf(b.X, b.Y) == z).Average(FromMiddle);
        Assert.True(Mean(Zone.Downtown) < Mean(Zone.Tenements));
        Assert.True(Mean(Zone.Tenements) < Mean(Zone.Suburbs));
        Assert.All(Blocks(m).Where(b => m.CoreOf(b.X, b.Y) > 0), b => Assert.Equal(Zone.Downtown, m.ZoneOf(b.X, b.Y)));
    }

    [Theory]
    [InlineData(CitySize.Small, 1)]
    [InlineData(CitySize.Large, 2)]
    public void TheCitysOwnBuildingsStandDowntown(CitySize size, int blocks)
    {
        var m = City(5, size).Map;
        Assert.Equal(blocks, m.CivicBlocks().Count);
        Assert.All(m.CivicBlocks(), b => Assert.Equal(Zone.Downtown, m.ZoneOf(b.X, b.Y)));
        var buildings = m.Lots.Where(l => l.Terrain == Terrain.Civic).Select(m.CivicOf).Where(c => c.West).Select(c => c.Building).ToList();
        Assert.Equal(blocks * 4, buildings.Count);
        Assert.Contains(Civic.CityHall, buildings);
        Assert.Contains(Civic.Library, buildings);
        Assert.Equal(buildings.Count, buildings.Distinct().Count());
    }

    [Fact]
    public void TenementCourtsAreInTheTenements()
    {
        var m = City(6, CitySize.Huge).Map;
        var courts = m.Lots.Where(l => l.Court).ToList();
        Assert.NotEmpty(courts);
        Assert.All(courts, l => Assert.Equal(Zone.Tenements, m.ZoneOf(l)));
    }

    [Fact]
    public void BusinessesSetUpWhereTheyBelong()
    {
        var w = City(7, CitySize.Metropolis);
        Zone ZoneOf(Business b) => w.Map.ZoneOf(w.LotOf(b));
        var warehouses = w.Businesses.Where(b => b.Kind == BusinessKind.Warehouse).ToList();
        Assert.True(warehouses.Count(b => ZoneOf(b) == Zone.Industrial) > warehouses.Count / 2);
        Assert.All(w.Businesses.Where(b => b.Kind == BusinessKind.Hotel), b => Assert.Contains(ZoneOf(b), new[] { Zone.Downtown, Zone.GoldCoast }));
        // The suburbs have a few shops, far fewer to the block than the tenements.
        double PerBlock(Zone z) => (double)w.Businesses.Count(b => ZoneOf(b) == z) / Blocks(w.Map).Count(b => w.Map.ZoneOf(b.X, b.Y) == z);
        Assert.True(PerBlock(Zone.Suburbs) > 0);
        Assert.True(PerBlock(Zone.Suburbs) < PerBlock(Zone.Tenements));
        // And a shop takes more downtown than in the slums.
        double Takes(Zone z) => w.Businesses.Where(b => b.Kind != BusinessKind.Court && ZoneOf(b) == z).Average(b => (double)b.Takings);
        Assert.True(Takes(Zone.Downtown) > Takes(Zone.Tenements));
    }

    [Fact]
    public void ALoadedCityIsZonedTheSameWay()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 8, Size = CitySize.Large });
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World.Map;
        var m = sim.World.Map;
        Assert.Equal(Blocks(m).Select(b => m.ZoneOf(b.X, b.Y)), Blocks(loaded).Select(b => loaded.ZoneOf(b.X, b.Y)));
        Assert.Equal(m.CivicBlocks(), loaded.CivicBlocks());
    }
}
