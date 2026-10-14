using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class GarageTests
{
    [Theory]
    [InlineData(CitySize.Small)]
    [InlineData(CitySize.Large)]
    [InlineData(CitySize.Metropolis)]
    public void EveryPrecinctHouseHasAGarageUnderIt(CitySize size)
    {
        var w = Simulation.New(new WorldSettings { Seed = 3, Size = size }).World;
        foreach (var precinct in w.Map.Lots.Where(l => l.Use == LotUse.Precinct))
            Assert.Equal(new Garage(precinct.Id, true, Content.PrecinctGarage), w.GarageAt(precinct.Id));
    }

    [Theory]
    [InlineData(CitySize.Small)]
    [InlineData(CitySize.Large)]
    public void MotorGaragesAndHotelsHavePublicGarages(CitySize size)
    {
        var w = Simulation.New(new WorldSettings { Seed = 3, Size = size }).World;
        var under = w.Garages.Where(g => !g.Police).ToList();
        Assert.NotEmpty(under);
        foreach (var g in under)
        {
            var kind = w.BusinessById(w.Map.LotAt(g.LotId).BusinessId).Kind;
            Assert.Equal(kind == BusinessKind.Garage ? Content.MotorGarage : Content.HotelGarage, g.Spaces);
        }
        Assert.All(w.Businesses.Where(b => b.Kind is BusinessKind.Garage or BusinessKind.Hotel && !IsGate(w, w.LotOf(b)) && !w.Map.FrontsSquare(w.LotOf(b))),
            b => Assert.NotNull(w.GarageAt(b.LotId)));
        Assert.Equal(under.Sum(g => g.Spaces), w.GarageSpaces);
    }

    [Fact]
    public void TheWayIntoACourtHasNoGarage()
    {
        var w = Simulation.New(new WorldSettings { Seed = 3, Size = CitySize.Large }).World;
        foreach (var gate in w.Map.CourtGates()) Assert.Null(w.GarageAt(gate.Id));
    }

    [Fact]
    public void GaragesComeBackTheSameFromASave()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 5 });
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(sim.World.Garages, loaded.Garages);
    }

    private static bool IsGate(World w, Lot lot) => w.Map.CourtGates().Any(g => g.Id == lot.Id);
}
