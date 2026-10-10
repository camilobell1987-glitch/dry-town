namespace DryTown.Core;

/// <summary>How guns and cars change a job: the getaway, the heat, and the drive there.</summary>
public sealed partial class Simulation
{
    /// <summary>The best car anyone on the job drives. The whole team piles into it.</summary>
    public static Car BestCar(IEnumerable<Hood> team) =>
        team.Select(h => h.Car).DefaultIfEmpty(Car.None).MaxBy(c => Content.Cars[c].Getaway);

    /// <summary>How much less likely the team is to be killed or caught when a job goes wrong.</summary>
    public static double Getaway(IEnumerable<Hood> team) => Content.Cars[BestCar(team)].Getaway;

    /// <summary>The extra heat a gunfight brings: the heaviest gun on the job sets it.</summary>
    public static int GunHeat(IEnumerable<Hood> team) => team.Select(h => Content.Guns[h.Weapon].Heat).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Strength going in through someone else's door. The men inside have cover and know the room,
    /// so guns count for half as much on the way in as they do holding the place.
    /// </summary>
    public static double StormingStrength(Hood lead, IEnumerable<Hood> backup) =>
        lead.Strength + BackupStrength(backup) - (Content.Guns[lead.Weapon].Firepower + backup.Sum(h => Content.Guns[h.Weapon].Firepower * 0.35)) * 0.5;

    /// <summary>Hours to get from one lot to another: walking, or driving if anyone on the job has a car.</summary>
    public static float TravelHours(World w, int fromLot, int toLot, IEnumerable<Hood> team) =>
        WalkHours(w, fromLot, toLot) / Content.Cars[BestCar(team)].Speed;
}
