using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class UntouchablesTests
{
    private const int Y1927 = (Content.UntouchablesYear - Content.StartYear) * Content.WeeksPerYear;

    /// <summary>A quiet city in the given week, where the gang runs six speakeasies.</summary>
    private static (Simulation Sim, World W, Gang G) Bootlegger(int week = Y1927 + 10, bool player = true, ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 2, CityEventsEnabled = false, DirectorEnabled = false, StartWeek = week });
        var w = sim.World;
        var g = player ? w.Player : w.LivingGangs.First(x => !x.IsPlayer);
        g.Cash = 20_000;
        g.Heat = 0;
        var men = w.AvailableHoodsOf(g.Id).Where(h => h.Id != g.BossHoodId).ToList();
        int i = 0;
        foreach (var b in w.Businesses.Where(b => b.IsOpen && (b.ProtectorGangId == g.Id || b.ProtectorGangId < 0) && Content.RacketsFor(b.Kind).Contains(RacketKind.Speakeasy)).Take(6))
        {
            b.ProtectorGangId = g.Id;
            b.Racket = RacketKind.Speakeasy;
            b.HandlerHoodId = men[i++ % men.Count].Id;
        }
        foreach (var man in men) man.Stealth = 1;
        return (sim, w, g);
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    private static void After(World w, Gang g, int evidence = 0)
    {
        w.Untouchables.Formed = true;
        w.Untouchables.TargetGangId = g.Id;
        w.Untouchables.NamedWeek = w.Week;
        w.Untouchables.Evidence = evidence;
        w.Untouchables.LastRaidWeek = w.Week;
    }

    [Fact]
    public void TheSquadComesIn1927AfterTheBiggestBootlegger()
    {
        var (sim, w, me) = Bootlegger(Y1927 - 1);
        Assert.Contains("until 1927", Reports.Squad(w, me.Id));
        Week(sim);
        Assert.Null(w.SquadTarget);
        Week(sim);
        Assert.Equal(me, w.SquadTarget);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Untouchables && e.Text.EndsWith("The papers call them the Untouchables."));
        Assert.Contains(w.Events, e => e.Text == $"The Untouchables are going after {w.HoodById(me.BossHoodId).Name} of {me.Name}, the biggest bootlegger in the city.");
    }

    [Fact]
    public void ARaidSmashesAPlaceAndTakesItsMan()
    {
        var (sim, w, me) = Bootlegger(seed: 4);
        After(w, me);
        for (int i = 0; i < 80 && w.Untouchables.Evidence == 0; i++) { me.Heat = 100; Week(sim); }
        Assert.Equal(1, w.Untouchables.Evidence);
        var smash = w.Events.Single(e => e.Text.StartsWith($"The Untouchables smashed {me.Name}'s speakeasy at "));
        var place = w.Businesses.First(b => smash.Text.Contains(b.Name));
        Assert.Equal(RacketKind.None, place.Racket);
    }

    [Fact]
    public void AStealthyManGetsWordInTime()
    {
        var (sim, w, me) = Bootlegger(seed: 8);
        After(w, me);
        foreach (var h in w.HoodsOf(me.Id)) h.Stealth = 10;
        Assert.Equal(10 * Content.SquadWarning, w.Warning(w.LiquorOf(me.Id).First()), 6);
        for (int i = 0; i < 200 && !w.Events.Any(e => e.Text.Contains(" got word in time")); i++) { me.Heat = 100; Week(sim); }
        Assert.Contains(w.Events, e => e.Kind == EventKind.Untouchables && e.Text.Contains(" got word in time, and there was nothing in the back room when they came."));
    }

    [Fact]
    public void EnoughEvidenceIndictsTheBoss()
    {
        var (sim, w, me) = Bootlegger();
        After(w, me, Content.SquadIndictment);
        var boss = w.HoodById(me.BossHoodId);
        Week(sim);
        Assert.Equal(HoodState.Jailed, boss.State);
        Assert.True(boss.Case!.Federal);
        Assert.Equal("with conspiracy to break the Prohibition laws", boss.Case.Charge);
        Assert.Equal(Content.SquadCase, boss.Case.Strength, 6);
        Assert.Null(w.SquadTarget);
        Assert.Equal(0, w.Untouchables.Evidence);
        Assert.Contains(w.Events, e => e.Text.StartsWith($"A federal grand jury indicted {boss.Name} of {me.Name}"));
    }

    [Fact]
    public void ABossOutOfTownIsOnlyWanted()
    {
        var (sim, w, me) = Bootlegger();
        var boss = w.HoodById(me.BossHoodId);
        Assert.True(w.SkipTown(me, boss));
        After(w, me, Content.SquadIndictment);
        Week(sim);
        // Found out of town now and then, the week the warrant goes out.
        if (boss.State == HoodState.Lam)
        {
            Assert.True(World.IsWanted(boss));
            Assert.Contains(w.Events, e => e.Text == $"{boss.Name} is out of town, and there's a federal warrant out for him now.");
        }
        Assert.True(boss.Case!.Federal);
    }

    [Fact]
    public void EvidenceGoesStaleWithoutRaids()
    {
        var (sim, w, me) = Bootlegger();
        After(w, me, 3);
        foreach (var b in w.LiquorOf(me.Id).ToList()) b.Racket = RacketKind.None;
        w.Untouchables.LastRaidWeek = w.Week - Content.SquadStaleWeeks;
        Week(sim);
        Assert.Equal(2, w.Untouchables.Evidence);
        Assert.Equal(0, w.SquadChance(me));
    }

    [Fact]
    public void RepealSendsThemHome()
    {
        var (sim, w, me) = Bootlegger((Content.RepealYear - Content.StartYear) * Content.WeeksPerYear - 1);
        After(w, me);
        Week(sim);
        Week(sim);
        Assert.True(w.Untouchables.Disbanded);
        Assert.Null(w.SquadTarget);
        Assert.Contains(w.Events, e => e.Text.StartsWith("With repeal, the Untouchables are sent home."));
        Assert.Equal("The Untouchables went home with repeal.\n", Reports.Squad(w, me.Id));
    }

    [Fact]
    public void ARivalBossLeavesTownBeforeTheGrandJury()
    {
        var (sim, w, g) = Bootlegger(player: false);
        After(w, g, Content.SquadIndictment - 1);
        var boss = w.HoodById(g.BossHoodId);
        g.Cash = 50_000;
        sim.AdvanceWeek();
        Assert.True(boss.State == HoodState.Lam || boss.Case is { Federal: true }, string.Join("\n", w.Events.Where(e => e.GangId == g.Id).Select(e => e.Text)));
        Assert.NotEqual(HoodState.Jailed, boss.State);
    }

    [Fact]
    public void TheConsoleShowsTheSquadAndItIsSaved()
    {
        var (sim, w, me) = Bootlegger();
        After(w, me, 2);
        var shell = new CommandShell(sim);
        string squad = shell.Execute("squad");
        Assert.Contains("The Untouchables are after you.", squad);
        Assert.Contains("2 of the 5 smashed places", squad);
        Assert.Contains("he gets word in time", squad);
        Assert.Contains("squad (the Untouchables", CommandShell.Help);

        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(me.Id, loaded.SquadTarget!.Id);
        Assert.Equal(2, loaded.Untouchables.Evidence);
        Assert.True(loaded.Untouchables.Formed);
    }
}
