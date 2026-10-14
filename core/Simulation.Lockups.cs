namespace DryTown.Core;

/// <summary>
/// Cars the police are looking for, and the lockups that hide them: a car seen at a bank job or a killing goes into
/// one of the gang's lockups on the way home if there's room, and waits there until the police stop looking; out on
/// the street it may be spotted and towed. A hijacked truck waits in one until its load can be sold for its worth.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>
    /// The team gets away from a job in the best car among them. The police take its description; if the gang has an
    /// empty lockup, the car goes into it on the way home and the man walks until the heat's off.
    /// </summary>
    private ScriptAction DriveOff(Gang gang, IReadOnlyList<Hood> team, string job, ScriptAction a)
    {
        var w = World;
        var car = BestCar(team);
        if (car == Car.None) return a;
        var driver = team.Where(h => h.Car == car && h.IsActive).OrderBy(h => h.Id).FirstOrDefault();
        if (driver == null) return a with { Drove = car };
        if (w.EmptyLockupOf(gang) is { } lockup)
        {
            lockup.Holds = Stashed.Car;
            lockup.Car = car;
            lockup.HoodId = driver.Id;
            lockup.OutWeek = w.Week + Content.CarWantedWeeks;
            lockup.Job = job;
            driver.Car = Car.None;
            driver.CarWanted = 0;
            driver.CarWantedFor = "";
            w.Log(EventKind.Gear, gang.Id, $"{driver.Name} put his {Content.Cars[car].Label} away in {w.LockupName(lockup)} until the police stop looking for it.");
            return a with { Drove = car, LockupId = lockup.Id };
        }
        driver.CarWanted = Content.CarWantedWeeks;
        driver.CarWantedFor = job;
        return a with { Drove = car };
    }

    /// <summary>
    /// A hijacked truck into one of the gang's empty lockups, to sell its load once the heat's off. Returns the lockup,
    /// or null if the gang has none free and has to sell the load tonight.
    /// </summary>
    private Lockup? HideTruck(Gang gang, Hood hood, int load, string job)
    {
        var w = World;
        if (w.EmptyLockupOf(gang) is not { } lockup) return null;
        lockup.Holds = Stashed.Truck;
        lockup.HoodId = hood.Id;
        lockup.OutWeek = w.Week + Content.TruckCoolWeeks;
        lockup.Value = (int)(load * Content.TruckPatience);
        lockup.Job = job;
        return lockup;
    }

    /// <summary>Sunday: the rent, the police out looking for cars and in the alleys, and whatever is ready to come out.</summary>
    private void Lockups()
    {
        var w = World;
        // Outfits that rent nothing yet take a lockup near home once they have cars worth hiding and money to spare.
        foreach (var gang in w.LivingGangs.Where(g => !g.IsSmall && g.Id != w.Player.Id && g.HqLotId >= 0).OrderBy(g => g.Id))
        {
            if (w.LockupsOf(gang.Id).Any() || gang.Cash < 600 || !w.HoodsOf(gang.Id).Any(h => h.Car != Car.None)) continue;
            var near = w.Lockups.Where(l => l.GangId < 0).OrderBy(l => w.FromHq(gang, l)).ThenBy(l => l.Id).FirstOrDefault();
            if (near != null) w.RentLockup(gang, near);
        }

        foreach (var l in w.Lockups.Where(l => l.GangId >= 0).OrderBy(l => l.Id))
        {
            var gang = w.GangById(l.GangId);
            if (!gang.Alive)
            {
                World.Clear(l);
                l.GangId = -1;
                continue;
            }
            // Rent, or the doors are chained and whatever's inside is turned out into the alley.
            if (gang.Cash < Content.LockupRent)
            {
                if (l.Holds == Stashed.Car && w.ReturnCar(gang, l) is { } to)
                {
                    to.CarWanted = Math.Max(1, l.OutWeek - w.Week);
                    to.CarWantedFor = l.Job;
                }
                if (l.Holds == Stashed.Truck) World.Clear(l);
                l.GangId = -1;
                w.Log(EventKind.Gear, gang.Id, $"{gang.Name} couldn't pay the rent on {w.LockupName(l)} and lost it.");
                continue;
            }
            gang.Cash -= Content.LockupRent;
            w.Ledger(gang.Id).Spending += Content.LockupRent;
            if (l.IsEmpty) continue;

            // The hotter the gang, the harder the police look down its alleys.
            if (w.Rng.Chance(Content.LockupSearched * gang.Heat / 100.0))
            {
                string what = l.Holds == Stashed.Car ? $"the {Content.Cars[l.Car].Label} wanted for {l.Job}" : $"the beer truck from {l.Job}";
                gang.Heat = Math.Min(100, gang.Heat + 6);
                w.Log(EventKind.Gear, gang.Id, $"Police broke open {w.LockupName(l)} and found {what}. {gang.Name} won't see it again.");
                World.Clear(l);
                continue;
            }
            if (w.Week + 1 < l.OutWeek) continue;
            if (l.Holds == Stashed.Car)
            {
                var car = l.Car;
                var to = w.ReturnCar(gang, l);
                w.Log(EventKind.Gear, gang.Id, to != null
                    ? $"The police have stopped looking, and {to.Name} took the {Content.Cars[car].Label} out of {w.LockupName(l)}."
                    : $"{gang.Name} sold the {Content.Cars[car].Label} out of {w.LockupName(l)}; nobody was left to drive it.");
            }
            else
            {
                int value = l.Value;
                gang.Cash += value;
                w.Ledger(gang.Id).Rackets += value;
                w.Log(EventKind.Hijack, gang.Id, $"{gang.Name} sold the load from the beer truck in {w.LockupName(l)} for ${value}.");
                World.Clear(l);
            }
        }

        // Out on the street, a wanted car may be spotted and towed to the police garage.
        foreach (var hood in w.Hoods.Where(h => h.CarWanted > 0).OrderBy(h => h.Id))
        {
            if (hood.Car == Car.None || !hood.IsActive) { hood.CarWanted = 0; hood.CarWantedFor = ""; continue; }
            if (w.Rng.Chance(Content.SpotWantedCar))
            {
                var gang = w.GangById(hood.GangId);
                gang.Heat = Math.Min(100, gang.Heat + 5);
                w.Log(EventKind.Gear, gang.Id, $"Police spotted {hood.Name}'s {Content.Cars[hood.Car].Label}, wanted for {hood.CarWantedFor}, and towed it away.");
                hood.Car = Car.None;
                hood.CarWanted = 0;
                hood.CarWantedFor = "";
                continue;
            }
            if (--hood.CarWanted == 0) hood.CarWantedFor = "";
        }
    }
}
