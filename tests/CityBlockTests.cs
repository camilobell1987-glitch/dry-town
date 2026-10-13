using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class CityBlockTests
{
    private static Simulation New(ulong seed = 3, CitySize size = CitySize.Small) =>
        Simulation.New(new WorldSettings { Seed = seed, Size = size, CityEventsEnabled = false, DirectorEnabled = false });

    private static (int Bx, int By) Block(Lot l) => ((l.X - 1) / CityMap.StrideX, (l.Y - 1) / CityMap.StrideY);

    [Theory]
    [InlineData(CitySize.Small)]
    [InlineData(CitySize.Medium)]
    [InlineData(CitySize.Metropolis)]
    public void EveryCityHasAParkAGraveyardChurchesAndCourts(CitySize size)
    {
        var w = New(size: size).World;
        Assert.True(w.Map.Lots.Count(l => l.Terrain == Terrain.Park) >= CityMap.LotsPerBlockX * CityMap.LotsPerBlockY);
        // A whole block of graves somewhere out past the houses.
        Assert.Contains(w.Map.Lots.Where(l => l.Terrain == Terrain.Cemetery).GroupBy(Block), g => g.Count() == CityMap.LotsPerBlockX * CityMap.LotsPerBlockY);

        // Every church has its churchyard beside it, and faces the street to the south.
        var churches = w.Map.Lots.Where(l => l.Terrain == Terrain.Church).ToList();
        Assert.True(churches.Count >= 2);
        foreach (var church in churches)
        {
            Assert.True(church.FrontY > church.Y);
            Assert.Contains(w.Map.Lots, l => l.Y == church.Y && Math.Abs(l.X - church.X) == 1 && l.Terrain == Terrain.Cemetery);
        }

        // Each court has its way in, and that's where the court does its business.
        var gates = w.Map.CourtGates().ToList();
        Assert.True(gates.Count >= 2);
        foreach (var gate in gates)
        {
            Assert.Equal(LotUse.Business, gate.Use);
            var court = w.BusinessById(gate.BusinessId);
            Assert.Equal(BusinessKind.Court, court.Kind);
            Assert.EndsWith($"'s Court, {w.Map.StreetOf(gate)} St", court.Name);
            Assert.All(w.Map.LotsOfBlock(Block(gate).Bx, Block(gate).By), l => Assert.True(l.Court));
        }
        Assert.All(w.Map.Lots.Where(l => l.Terrain != Terrain.Land), l => Assert.Equal(LotUse.Empty, l.Use));
    }

    [Fact]
    public void ARacketInACourtDrawsLessHeat()
    {
        var w = New().World;
        var court = w.Businesses.First(b => b.Kind == BusinessKind.Court);
        var hall = w.Businesses.First(b => b.Kind == BusinessKind.PoolHall);
        Assert.Contains(RacketKind.GamblingDen, Content.RacketsFor(BusinessKind.Court));
        Assert.DoesNotContain(RacketKind.Casino, Content.RacketsFor(BusinessKind.Court));
        court.Racket = hall.Racket = RacketKind.GamblingDen;
        Assert.Equal(w.RacketHeat(hall) - Content.CourtHeatOff, w.RacketHeat(court));
        court.Racket = RacketKind.Numbers;
        Assert.Equal(0, w.RacketHeat(court));
    }

    [Fact]
    public void ACourtCanBeShakenDownButNotBought()
    {
        var sim = New();
        var w = sim.World;
        var me = w.Player;
        var court = w.Businesses.First(b => b.Kind == BusinessKind.Court && !b.IsProtected);
        court.ProtectorGangId = me.Id;
        me.Cash = 100_000;
        Assert.Equal("Nobody owns a court to sell it.", w.CannotBuyFront(me, court));
        Assert.Equal(-1, w.BuyFront(me, court));
    }

    [Fact]
    public void ASaveKeepsTheChurchesGraveyardsAndCourts()
    {
        var sim = New(5);
        var w = sim.World;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(w.Map.Lots.Select(l => (l.Terrain, l.Court)), loaded.Map.Lots.Select(l => (l.Terrain, l.Court)));
        Assert.Equal(w.Map.CourtGates().Select(l => l.Id), loaded.Map.CourtGates().Select(l => l.Id));
    }
}
