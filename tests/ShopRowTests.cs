using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class ShopRowTests
{
    private static World City(ulong seed, CitySize size = CitySize.Large) =>
        Simulation.New(new WorldSettings { Seed = seed, Size = size, CityEventsEnabled = false, DirectorEnabled = false }).World;

    [Theory]
    [InlineData(CitySize.Small)]
    [InlineData(CitySize.Large)]
    [InlineData(CitySize.Metropolis)]
    public void SmallShopsShareABuildingTwoOrThreeToARow(CitySize size)
    {
        var w = City(3, size);
        var rows = w.Map.Lots.Where(l => l.Shops.Count > 0).ToList();
        Assert.NotEmpty(rows);
        foreach (var lot in rows)
        {
            Assert.InRange(lot.Shops.Count, 2, 3);
            Assert.Equal(lot.Shops[0], lot.BusinessId);
            var kinds = w.ShopsAt(lot).Select(b => b.Kind).ToList();
            Assert.All(kinds, k => Assert.Contains(k, Content.ShopRowKinds));
            Assert.Equal(kinds.Count, kinds.Distinct().Count());
            Assert.All(w.ShopsAt(lot), b => Assert.Equal(lot.Id, b.LotId));
        }
    }

    [Fact]
    public void EveryBusinessIsInExactlyOneBuildingAndKnowsItsPlaceInTheRow()
    {
        var w = City(5);
        var seen = w.Map.Lots.Where(l => l.Use == LotUse.Business).SelectMany(w.ShopsAt).Select(b => b.Id).ToList();
        Assert.Equal(w.Businesses.Select(b => b.Id).OrderBy(id => id), seen.OrderBy(id => id));
        foreach (var b in w.Businesses)
        {
            var lot = w.LotOf(b);
            var (unit, units) = w.UnitOf(b);
            Assert.Equal(Math.Max(1, lot.Shops.Count), units);
            Assert.Equal(b.Id, w.ShopsAt(lot)[unit].Id);
        }
    }

    [Fact]
    public void BigBusinessesHaveABuildingToThemselvesAndSomeSmallOnesDoToo()
    {
        var w = City(6, CitySize.Metropolis);
        foreach (var b in w.Businesses.Where(b => b.Kind is BusinessKind.Hotel or BusinessKind.Warehouse or BusinessKind.Garage or BusinessKind.Court))
            Assert.Single(w.ShopsAt(w.LotOf(b)));
        Assert.Contains(w.Businesses, b => b.Kind is BusinessKind.Grocer or BusinessKind.Hardware && w.ShopsAt(w.LotOf(b)).Count == 1);
        Assert.Contains(w.Businesses, b => Content.ShopRowKinds.Contains(b.Kind) && w.ShopsAt(w.LotOf(b)).Count > 1);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void EveryTradeIsInTown(ulong seed)
    {
        var w = City(seed, CitySize.Small);
        foreach (var kind in Enum.GetValues<BusinessKind>())
            Assert.Contains(w.Businesses, b => b.Kind == kind);
    }

    [Fact]
    public void EachShopInARowIsKeptByADifferentFamilyOverIt()
    {
        var w = City(4);
        var d = new CityDirectory(w, l => l.Terrain != Terrain.Land ? 0 : l.Use == LotUse.Business ? 20 : 10, l => l.Terrain == Terrain.Land && l.Use == LotUse.Empty);
        int rows = 0;
        foreach (var lot in w.Map.Lots.Where(l => l.Shops.Count > 1))
        {
            var owners = lot.Shops.Select(d.OwnerOf).ToList();
            Assert.All(owners, o => Assert.True(o >= 0));
            Assert.Equal(owners.Count, owners.Distinct().Count());
            Assert.All(owners, o => Assert.Equal(lot.Id, d.Get(o).HomeLotId));
            rows++;
        }
        Assert.True(rows > 0);
    }

    [Fact]
    public void RowsAreSaved()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 7, Size = CitySize.Large });
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(sim.World.Map.Lots.Select(l => string.Join(",", l.Shops)), loaded.Map.Lots.Select(l => string.Join(",", l.Shops)));
    }
}
