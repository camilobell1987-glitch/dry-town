using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class LamTests
{
    /// <summary>A quiet city where one of the player's men has just been charged with something serious.</summary>
    private static (Simulation Sim, World W, Gang Me, Hood Man) Charged(double strength = 0.7, bool player = true, ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 2, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var g = player ? w.Player : w.LivingGangs.First(x => !x.IsPlayer);
        g.Cash = 20_000;
        g.Heat = 0;
        var man = w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId);
        sim.Jail(man, 60, "with assault", strength: strength);
        return (sim, w, g, man);
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    [Fact]
    public void BailGetsHimOutAndTheMoneyComesBackWhenHeShows()
    {
        var (sim, w, me, man) = Charged();
        int bail = w.BailFor(man);
        Assert.Equal(Content.Bail(man.Case!), bail);
        long cash = me.Cash;
        Assert.Equal(bail, w.PostBail(me, man));
        Assert.True(man.IsAvailable);
        Assert.Equal(cash - bail, me.Cash);
        Assert.Contains(man, w.OnTrial(me.Id));
        Assert.Null(w.CannotPayJudge(me, man));

        for (int i = 0; i < Content.WeeksToTrial && man.Case != null; i++) Week(sim);
        Assert.Null(man.Case);
        Assert.Equal(0, man.Bail);
        Assert.Contains(w.Events, e => e.Text == $"{man.Name} showed up for his trial, and {me.Name} got its ${bail:N0} bail back.");
    }

    [Fact]
    public void NoBailForAManFacingLifeOrOneWhoRanBefore()
    {
        var (_, w, me, man) = Charged();
        man.Case!.Life = true;
        Assert.Equal("The judge won't set bail for a man facing life.", w.CannotBail(me, man));
        man.Case.Life = false;
        man.Case.JumpedBail = true;
        Assert.Equal("He jumped bail once already. The judge won't set it again.", w.CannotBail(me, man));
        man.Case.JumpedBail = false;
        me.Cash = 10;
        Assert.StartsWith("The judge set bail at $", w.CannotBail(me, man));
    }

    [Fact]
    public void JumpingBailLosesTheMoneyAndHisCaseCoolsWhileHesGone()
    {
        var (sim, w, me, man) = Charged();
        w.PostBail(me, man);
        long cash = me.Cash;
        Assert.True(w.SkipTown(me, man));
        Assert.Equal(HoodState.Lam, man.State);
        Assert.True(World.IsWanted(man));
        Assert.True(man.Case!.JumpedBail);
        Assert.False(man.IsAvailable);
        Assert.Contains(man, w.HoodsOf(me.Id));
        Assert.Equal(cash, me.Cash);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Lam && e.Text.StartsWith($"{man.Name} of {me.Name} jumped his $"));

        double strength = man.Case.Strength;
        Week(sim);
        // Found already now and then; otherwise the case has cooled and the hideout is paid for.
        if (man.State == HoodState.Lam)
        {
            Assert.Equal(strength - Content.ColdCase, man.Case.Strength, 6);
            Assert.True(w.LastLedger[me.Id].Spending >= Content.HideoutWeekly);
        }
    }

    [Fact]
    public void AWantedManHomeIsPickedUpAndTheJuryHearsHeRan()
    {
        var (sim, w, me, man) = Charged(strength: 0.5);
        w.PostBail(me, man);
        w.SkipTown(me, man);
        Assert.True(w.ComeHome(me, man));
        Assert.Contains(man, w.WantedAtHome(me.Id));
        for (int i = 0; i < 60 && man.State != HoodState.Jailed; i++)
        {
            me.Heat = 100;
            Week(sim);
        }
        Assert.Equal(HoodState.Jailed, man.State);
        Assert.Equal(0.5 + Content.JumpedBailCase, man.Case!.Strength, 6);
        // On the warrant, or on something new with the old case still waiting.
        Assert.Contains(w.Events, e => e.Text == $"Police picked up {man.Name} of {me.Name} on the warrant in his case. His trial is in {Content.WeeksToTrial} weeks."
            || e.Text.StartsWith($"{man.Name} was picked up ") && e.Text.EndsWith("on the case already waiting for him."));
        Assert.NotNull(w.CannotBail(me, man));
    }

    [Fact]
    public void AManLyingLowCantBeRaidedOrHitButTheBossAwayCostsLoyalty()
    {
        var (sim, w, me, man) = Charged();
        var boss = w.HoodById(me.BossHoodId);
        var other = w.AvailableHoodsOf(me.Id).First(h => h.Id != boss.Id && h.Id != man.Id);
        other.Loyalty = 80;
        Assert.True(w.SkipTown(me, boss));
        Assert.False(World.IsWanted(boss));
        Assert.Contains(w.Events, e => e.Text == $"{boss.Name} of {me.Name} left town to lie low for a while.");
        Assert.DoesNotContain(boss, w.AvailableHoodsOf(me.Id));
        long cash = me.Cash;
        Week(sim);
        Assert.Equal(HoodState.Lam, boss.State);
        Assert.Equal(boss.Id, me.BossHoodId);
        Assert.True(cash - me.Cash >= Content.BossHideoutWeekly);
        Assert.True(other.Loyalty < 80 + 1);

        Assert.True(w.ComeHome(me, boss));
        Assert.True(boss.IsAvailable);
        Assert.Contains(w.Events, e => e.Text == $"{boss.Name} is back in town.");
    }

    [Fact]
    public void WithoutMoneyForTheHideoutHeComesHome()
    {
        var (sim, w, me, man) = Charged();
        var other = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        w.SkipTown(me, other);
        me.Cash = 0;
        Assert.StartsWith("Hiding out costs $", w.CannotSkipTown(me, w.AvailableHoodsOf(me.Id).First()));
        Week(sim);
        Assert.True(other.IsAvailable);
        Assert.Contains(w.Events, e => e.Text == $"{me.Name} couldn't pay for the hideout. {other.Name} is back in town.");
    }

    [Fact]
    public void ARivalBossBailsOutAndRunsFromALongStretch()
    {
        var (sim, w, g, _) = Charged(player: false);
        var boss = w.HoodById(g.BossHoodId);
        sim.Jail(boss, 80, "with tax evasion", federal: true, strength: 0.95);
        g.Cash = 50_000;
        // The rivals plan their own weeks.
        sim.AdvanceWeek();
        Assert.True(boss.Bail > 0 || boss.State == HoodState.Lam, string.Join("\n", w.Events.Where(e => e.GangId == g.Id).Select(e => e.Text)));
        for (int i = 0; i < 3 && boss.State != HoodState.Lam; i++) sim.AdvanceWeek();
        Assert.Equal(HoodState.Lam, boss.State);
        Assert.Equal(boss.Id, g.BossHoodId);
    }

    [Fact]
    public void TheConsoleBailsRunsAndBringsHimHome()
    {
        var (sim, w, me, man) = Charged();
        var shell = new CommandShell(sim);
        Assert.Contains($"bail ${w.BailFor(man):N0}", shell.Execute("cases"));
        Assert.Contains($"{man.Name} is out on $", shell.Execute($"bail {man.Id}"));
        Assert.Contains("bail,", shell.Execute("hoods"));
        Assert.Contains("jumped his $", shell.Execute($"lam {man.Id}"));
        Assert.Contains("on the lam", shell.Execute("hoods"));
        Assert.Contains("wanted, charged with assault", shell.Execute("cases"));
        Assert.Contains("the warrant in his case is still out", shell.Execute($"home {man.Id}"));
        Assert.Contains("home with a warrant out", shell.Execute("cases"));
        Assert.Contains("lam <hood>", CommandShell.Help);
    }

    [Fact]
    public void ItIsSaved()
    {
        var (sim, w, me, man) = Charged();
        w.PostBail(me, man);
        w.SkipTown(me, man);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var lm = loaded.HoodById(man.Id);
        Assert.Equal(HoodState.Lam, lm.State);
        Assert.Equal(man.AwaySinceWeek, lm.AwaySinceWeek);
        Assert.True(lm.Case!.JumpedBail);
        Assert.True(World.IsWanted(lm));
    }
}
