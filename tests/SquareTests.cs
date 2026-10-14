using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class SquareTests
{
    private static World City(ulong seed, CitySize size = CitySize.Large) =>
        Simulation.New(new WorldSettings { Seed = seed, Size = size, CityEventsEnabled = false, DirectorEnabled = false }).World;

    [Theory]
    [InlineData(CitySize.Small)]
    [InlineData(CitySize.Large)]
    [InlineData(CitySize.Metropolis)]
    public void SquaresKeepOffTheEdgesTheWaterfrontAndTheEl(CitySize size)
    {
        var m = City(3, size).Map;
        Assert.NotEmpty(m.Squares);
        foreach (var (bx, by, avenue) in m.Squares)
        {
            if (avenue)
            {
                Assert.InRange(bx, 1, m.BlocksX - 1);
                Assert.NotEqual(m.RiverRow, by);
            }
            else
            {
                Assert.InRange(by, 1, m.BlocksY - 1);
                Assert.NotEqual(m.RailStreet, by);
                Assert.NotEqual(m.RiverRow, by);
                Assert.NotEqual(m.RiverRow + 1, by);
                Assert.False(m.HasBackAlley(bx, by));
            }
        }
    }

    [Fact]
    public void NoTwoSquaresMeetAtOneCrossing()
    {
        var m = City(4, CitySize.Metropolis).Map;
        var ends = m.Squares.SelectMany(s => s.Avenue ? new[] { (s.Bx, s.By), (s.Bx, s.By + 1) } : new[] { (s.Bx, s.By), (s.Bx + 1, s.By) }).ToList();
        Assert.Equal(ends.Count, ends.Distinct().Count());
    }

    [Fact]
    public void NoGarageOpensOntoASquare()
    {
        var w = City(5, CitySize.Metropolis);
        Assert.Contains(w.Map.Lots, w.Map.FrontsSquare);
        Assert.All(w.Garages, g => Assert.False(w.Map.FrontsSquare(w.Map.LotAt(g.LotId))));
    }

    [Fact]
    public void PeopleStillWalkAcrossSquares()
    {
        var m = City(6).Map;
        var land = m.Lots.Where(l => l.Terrain == Terrain.Land).ToList();
        var path = m.Path(land.First(), land.Last());
        Assert.NotEmpty(path);
        // Between leaving one door and reaching the other, every corner is on a street or an avenue, squares included.
        Assert.All(path.Skip(1).SkipLast(1), p => Assert.True(CityMap.IsRoad(p.X, p.Y)));
        Assert.Contains(m.Squares, s => !s.Avenue);
    }

    [Fact]
    public void ALoadedCityHasTheSameSquares()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 7, Size = CitySize.Large });
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(sim.World.Map.Squares.OrderBy(s => s), loaded.Map.Squares.OrderBy(s => s));
    }
}
