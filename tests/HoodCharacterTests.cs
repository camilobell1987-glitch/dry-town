using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class HoodCharacterTests
{
    private static (Simulation Sim, World W) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 3, CityEventsEnabled = false, DirectorEnabled = false });
        return (sim, sim.World);
    }

    [Fact]
    public void EveryGangsterHasABackgroundAndTwoOrThreeTraits()
    {
        var (_, w) = Start();
        Assert.All(w.Hoods, h =>
        {
            Assert.InRange(h.Traits.Count, 2, 3);
            Assert.All(h.Traits, t => Assert.Contains(t, Content.HoodTraits));
            Assert.Equal(h.Traits.Count, h.Traits.Distinct().Count());
        });
        Assert.Contains(w.Hoods, h => h.Background != Background.None);
        // The same men in the same game are the same sort.
        var (_, again) = Start();
        Assert.Equal(w.Hoods.Select(h => (h.Background, string.Join(",", h.Traits))), again.Hoods.Select(h => (h.Background, string.Join(",", h.Traits))));
    }

    [Fact]
    public void MenFromAnOldSaveAreGivenTheirCharactersOnLoading()
    {
        var (sim, w) = Start();
        var before = w.Hoods.ToDictionary(h => h.Id, h => string.Join(",", h.Traits));
        foreach (var h in w.Hoods) { h.Traits.Clear(); h.Background = Background.None; }
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, new List<Order>()))).World;
        Assert.All(loaded.Hoods, h => Assert.Equal(before[h.Id], string.Join(",", h.Traits)));
    }

    [Fact]
    public void AStandUpGuyHoldsOutAgainstTheDaAndAStoolPigeonFolds()
    {
        var (_, w) = Start();
        var man = w.HoodsOf(w.Player.Id).First();
        man.JailWeeks = 40;
        man.Traits = new() { Trait.Sharp };
        man.Background = Background.None;
        int plain = Content.RatLine(man);
        man.Traits = new() { Trait.StandUpGuy };
        Assert.True(Content.RatLine(man) < plain);
        Assert.Equal(0, Content.Leaky(man));
        man.Traits = new() { Trait.StoolPigeon };
        Assert.True(Content.RatLine(man) > plain);
        Assert.Equal(2, Content.Leaky(man));
        // Looking after his family keeps a family man from the DA.
        man.Traits = new() { Trait.FamilyMan };
        man.LookedAfter = true;
        Assert.True(Content.RatLine(man) < plain);
    }

    [Fact]
    public void AKillingSetsTheDeadMansCrewAgainstTheOutfitAndTheManWhoDidIt()
    {
        var (_, w) = Start();
        var mine = w.Player;
        var rival = w.LivingGangs.First(g => g.Id != mine.Id && !g.IsSmall && w.HoodsOf(g.Id).Count() >= 3);
        var men = w.HoodsOf(rival.Id).Where(h => h.Id != rival.BossHoodId).Take(2).ToList();
        var crew = w.FormCrew(men[0]);
        Assert.True(w.JoinCrew(crew, men[1]));
        foreach (var h in men) h.Traits = new() { Trait.Sharp };
        var killer = w.HoodsOf(mine.Id).First();
        w.Mourn(men[0], mine, killer);
        var g = w.GrievanceOf(Party.Hood, men[1].Id, Party.Gang, mine.Id);
        Assert.NotNull(g);
        Assert.Equal(Content.MournCrew, g!.Amount);
        Assert.True(g.Known);
        Assert.Equal($"killed {men[0].Name}", g.Why);
        Assert.NotNull(w.GrievanceOf(Party.Hood, men[1].Id, Party.Hood, killer.Id));
        // Nobody holds it against their own outfit.
        w.Mourn(men[0], rival, null);
        Assert.Null(w.GrievanceOf(Party.Hood, men[1].Id, Party.Gang, rival.Id));

        // Once it's bad enough he wants the job, and a hit on the man who did it is likelier to come off.
        double cold = Simulation.HitChance(w, men[1], Array.Empty<Hood>(), killer);
        w.Mourn(men[0], mine, killer);
        Assert.True(w.Wants(men[1], killer));
        Assert.True(Simulation.HitChance(w, men[1], Array.Empty<Hood>(), killer) > cold || cold >= 0.9);
    }

    [Fact]
    public void ADeadMansGrudgesGoWithHim()
    {
        var (sim, w) = Start();
        var rival = w.LivingGangs.First(g => g.Id != w.Player.Id && w.HoodsOf(g.Id).Count() >= 2);
        var man = w.HoodsOf(rival.Id).First(h => h.Id != rival.BossHoodId);
        w.Bear(Party.Hood, man.Id, Party.Gang, w.Player.Id, 50, "killed someone");
        man.State = HoodState.Dead;
        sim.AdvanceWeek();
        Assert.Empty(w.HeldBy(Party.Hood, man.Id));
    }

    [Fact]
    public void AHardBoiledManIsHarderToPutDown()
    {
        var (_, w) = Start();
        var rival = w.LivingGangs.First(g => g.Id != w.Player.Id && w.HoodsOf(g.Id).Count() >= 2);
        var target = w.HoodsOf(rival.Id).First(h => h.Id != rival.BossHoodId);
        var hitman = w.HoodsOf(w.Player.Id).First();
        hitman.Stealth = 3;
        target.Traits = new() { Trait.Clam };
        double soft = Simulation.HitChance(w, hitman, Array.Empty<Hood>(), target);
        target.Traits = new() { Trait.HardBoiled, Trait.Sharp };
        Assert.True(Simulation.HitChance(w, hitman, Array.Empty<Hood>(), target) < soft);
    }
}
