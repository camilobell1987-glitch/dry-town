namespace DryTown.Core;

/// <summary>
/// An underground garage: a ramp down beside a building to a floor of parking under it. Motor garages and hotels
/// have them for the public, and every precinct house has one for its patrol cars and paddy wagons.
/// </summary>
public sealed record Garage(int LotId, bool Police, int Spaces);

public sealed partial class World
{
    private List<Garage>? _garages;

    /// <summary>
    /// Every underground garage in the city. They come with the buildings, so they're worked out from the map rather
    /// than saved: under each motor garage and hotel, and under each precinct house.
    /// </summary>
    public IReadOnlyList<Garage> Garages => _garages ??= Map.Lots.Select(GarageUnder).OfType<Garage>().ToList();

    public Garage? GarageAt(int lotId) => Garages.FirstOrDefault(g => g.LotId == lotId);

    /// <summary>Spaces in the city's public garages, for cars that would otherwise take a bay in the street.</summary>
    public int GarageSpaces => Garages.Where(g => !g.Police).Sum(g => g.Spaces);

    private Garage? GarageUnder(Lot lot)
    {
        if (lot.Terrain != Terrain.Land) return null;
        // The passage into a tenement court takes the ground floor of its house.
        if (lot.Court && Map.CourtGate((lot.X - 1) / CityMap.StrideX, (lot.Y - 1) / CityMap.StrideY)?.Id == lot.Id) return null;
        // No ramp comes up into a square.
        if (Map.FrontsSquare(lot)) return null;
        if (lot.Use == LotUse.Precinct) return new Garage(lot.Id, true, Content.PrecinctGarage);
        if (lot.Use != LotUse.Business || lot.BusinessId < 0) return null;
        return BusinessById(lot.BusinessId).Kind switch
        {
            BusinessKind.Garage => new Garage(lot.Id, false, Content.MotorGarage),
            BusinessKind.Hotel => new Garage(lot.Id, false, Content.HotelGarage),
            _ => null,
        };
    }
}
