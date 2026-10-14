using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class LockupTests
{
    /// <summary>A two-gang city with the rival protecting the first business and nothing else going on.</summary>
    private static (Simulation sim, Gang me, Gang rival, Business biz) Duel(ulong seed)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 2, DirectorEnabled = false });
        var w = sim.World;
        var rival = w.Gangs[1];
        var biz = w.Businesses.First();
        biz.ProtectorGangId = rival.Id;
        biz.HandlerHoodId = w.HoodsOf(rival.Id).Last().Id;
        return (sim, w.Player, rival, biz);
    }

    private static Dictionary<int, List<Order>> Only(Gang me, Gang rival, params Order[] mine) =>
        new() { [me.Id] = mine.ToList(), [rival.Id] = new() };

    private static Lockup Nearest(World w, Gang g) => w.Lockups.Where(l => l.GangId < 0).OrderBy(l => w.FromHq(g, l)).ThenBy(l => l.Id).First();

    /// <summary>A crew good enough to rob a bank, the first of them driving a Packard.</summary>
    private static List<Hood> Crew(World w, Gang me)
    {
        var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).Take(3).ToList();
        foreach (var h in men) h.Brains = h.Muscle = h.Stealth = 10;
        men[0].Car = Car.Packard;
        return men;
    }

    private static RobberyOrder HoldUp(World w, Gang me, List<Hood> men) =>
        new(me.Id, men[0].Id, w.Banks[0].Id, men.Skip(1).Select(h => h.Id).ToArray());

    [Theory]
    [InlineData(CitySize.Small)]
    [InlineData(CitySize.Large)]
    [InlineData(CitySize.Metropolis)]
    public void LockupsLineTheBackAlleys(CitySize size)
    {
        var w = Simulation.New(new WorldSettings { Seed = 3, Size = size }).World;
        Assert.NotEmpty(w.Lockups);
        foreach (var l in w.Lockups)
        {
            Assert.True(w.Map.HasBackAlley(l.Bx, l.By));
            var (x0, x1) = w.Map.LockupSpots(l.Bx, l.By)[l.Index];
            float west = l.Bx * CityMap.StrideX + 1;
            Assert.InRange(x0, west, x1);
            Assert.InRange(x1, x0, west + CityMap.LotsPerBlockX);
            Assert.Equal(-1, l.GangId);
        }
    }

    [Fact]
    public void ALockupIsRentedByTheWeekAndOnlyGivenUpEmpty()
    {
        var (sim, me, rival, _) = Duel(4);
        var w = sim.World;
        me.Cash = 1000;
        var l = Nearest(w, me);
        Assert.True(w.RentLockup(me, l));
        Assert.Equal(1000 - Content.LockupRent, me.Cash);
        rival.Cash = 1000;
        Assert.NotNull(w.CannotRent(rival, l));
        Assert.False(w.RentLockup(rival, l));

        l.Holds = Stashed.Truck;
        Assert.False(w.GiveUpLockup(me, l));
        World.Clear(l);
        Assert.True(w.GiveUpLockup(me, l));
        Assert.Equal(-1, l.GangId);
    }

    [Fact]
    public void AGetawayCarGoesIntoTheLockupAndComesOutWhenThePoliceStopLooking()
    {
        int hidden = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var (sim, me, rival, _) = Duel(seed);
            var w = sim.World;
            var men = Crew(w, me);
            var l = Nearest(w, me);
            w.RentLockup(me, l);
            sim.AdvanceWeek(Only(me, rival, HoldUp(w, me, men)));
            if (!w.Events.Any(e => e.Kind == EventKind.Robbery && e.Text.Contains("got away with"))) continue;
            if (l.Holds != Stashed.Car) continue; // the police found it the first Sunday
            hidden++;
            Assert.Equal(Car.Packard, l.Car);
            Assert.Equal(men[0].Id, l.HoodId);
            Assert.Equal(Car.None, men[0].Car);
            Assert.Equal(0, men[0].CarWanted);
            Assert.Contains(w.Script, a => a.Kind == ActionKind.Robbery && a.LockupId == l.Id && a.Drove == Car.Packard);

            for (int i = 1; i < Content.CarWantedWeeks && l.Holds == Stashed.Car; i++) sim.AdvanceWeek(Only(me, rival));
            if (l.Holds == Stashed.Car) Assert.Fail("The car should be out by now");
            if (w.Events.Any(e => e.Text.Contains("broke open"))) continue;
            Assert.Equal(Car.Packard, men[0].Car);
            Assert.Equal(0, men[0].CarWanted);
        }
        Assert.True(hidden >= 4, $"only {hidden} cars were hidden");
    }

    [Fact]
    public void WithoutALockupTheGetawayCarIsWanted()
    {
        int wanted = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var (sim, me, rival, _) = Duel(seed);
            var w = sim.World;
            var men = Crew(w, me);
            sim.AdvanceWeek(Only(me, rival, HoldUp(w, me, men)));
            if (!w.Events.Any(e => e.Kind == EventKind.Robbery && e.Text.Contains("got away with"))) continue;
            wanted++;
            // Either the police are still looking for it, or they've already towed it.
            bool towed = w.Events.Any(e => e.Text.Contains("towed it away"));
            Assert.True(towed ? men[0].Car == Car.None : men[0].CarWanted == Content.CarWantedWeeks - 1);
            if (!towed) Assert.Contains(w.Banks[0].Name, men[0].CarWantedFor);
        }
        Assert.True(wanted >= 4);
    }

    [Fact]
    public void AHijackedTruckWaitsInTheLockupAndItsLoadFetchesMore()
    {
        int kept = 0;
        for (ulong seed = 1; seed <= 30 && kept < 3; seed++)
        {
            var (sim, me, rival, biz) = Duel(seed);
            var w = sim.World;
            biz.Racket = RacketKind.Speakeasy;
            var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).Take(3).ToList();
            foreach (var h in men) h.Muscle = h.Intimidation = h.Stealth = 10;
            var l = Nearest(w, me);
            w.RentLockup(me, l);
            sim.AdvanceWeek(Only(me, rival, new HijackOrder(me.Id, men[0].Id, biz.Id, men.Skip(1).Select(h => h.Id).ToArray())));
            if (!w.Events.Any(e => e.Kind == EventKind.Hijack && e.Text.Contains("hid it in"))) continue;
            if (l.Holds != Stashed.Truck) continue;
            kept++;
            int load = Content.Rackets[RacketKind.Speakeasy].WeeklyIncome * 2;
            Assert.Equal((int)(load * Content.TruckPatience), l.Value);
            Assert.Contains(w.Script, a => a.Kind == ActionKind.Hijack && a.Truck && a.LockupId == l.Id);
            for (int i = 1; i < Content.TruckCoolWeeks && l.Holds == Stashed.Truck; i++) sim.AdvanceWeek(Only(me, rival));
            Assert.True(l.IsEmpty);
            if (w.Events.Any(e => e.Text.Contains("broke open"))) continue;
            Assert.Contains(w.Events, e => e.Text.Contains($"sold the load from the beer truck in {w.LockupName(l)} for ${(int)(load * Content.TruckPatience)}"));
        }
        Assert.True(kept >= 1);
    }

    [Fact]
    public void OutfitsTakeALockupOnceTheyHaveCarsAndMoney()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 6, Size = CitySize.Large });
        var w = sim.World;
        foreach (var g in w.LivingGangs.Where(g => !g.IsSmall && g.Id != w.Player.Id))
        {
            g.Cash = 5000;
            w.HoodsOf(g.Id).First().Car = Car.ModelT;
        }
        sim.AdvanceWeek(playerAutopilot: true);
        Assert.All(w.LivingGangs.Where(g => !g.IsSmall && g.Id != w.Player.Id), g => Assert.NotEmpty(w.LockupsOf(g.Id)));
    }

    [Fact]
    public void LockupsAndWantedCarsAreSaved()
    {
        var (sim, me, _, _) = Duel(5);
        var w = sim.World;
        me.Cash = 1000;
        var l = Nearest(w, me);
        w.RentLockup(me, l);
        l.Holds = Stashed.Car;
        l.Car = Car.Packard;
        l.OutWeek = 9;
        l.Job = "the hold-up at the bank";
        var man = w.HoodsOf(me.Id).First();
        man.Car = Car.ModelT;
        man.CarWanted = 2;
        man.CarWantedFor = "a killing";

        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(w.Lockups.Count, loaded.Lockups.Count);
        var back = loaded.Lockups[l.Id];
        Assert.Equal((me.Id, Stashed.Car, Car.Packard, 9, "the hold-up at the bank"), (back.GangId, back.Holds, back.Car, back.OutWeek, back.Job));
        Assert.Equal((2, "a killing"), (loaded.HoodById(man.Id).CarWanted, loaded.HoodById(man.Id).CarWantedFor));
    }
}
