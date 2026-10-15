using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class InsideTests
{
    /// <summary>A quiet city whose boss has just been sent away for a long stretch, with a free heir to mind the street.</summary>
    private static (Simulation Sim, World W, Gang G, Hood Boss, Hood Heir) SentAway(bool player = true, int weeks = 60, ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 2, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var g = player ? w.Player : w.LivingGangs.First(x => !x.IsPlayer);
        g.Cash = 20_000;
        var boss = w.HoodById(g.BossHoodId);
        var heir = w.AvailableHoodsOf(g.Id).First(h => h.Id != boss.Id);
        w.NameHeir(g, heir, announce: false);
        heir.Ambition = 20;
        boss.State = HoodState.Jailed;
        boss.JailWeeks = weeks;
        return (sim, w, g, boss, heir);
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    [Fact]
    public void ABossSentAwayRunsItFromHisCell()
    {
        var (sim, w, g, boss, heir) = SentAway();
        int fee = w.CellFee(g);
        long cash = g.Cash;
        Week(sim);
        Assert.Equal(boss.Id, g.BossHoodId);
        Assert.True(w.RunsFromInside(g));
        Assert.Equal(heir, w.StreetBoss(g));
        Assert.True(cash - g.Cash >= fee);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Inside && e.Text == $"{boss.Name} will run {g.Name} from his cell. {heir.Name} minds the street for him, and the guards get ${fee:N0} a week to carry his word out.");
        Assert.Contains($"{heir.Name} minds the street for him", new CommandShell(sim).Execute("inside"));
    }

    [Fact]
    public void WithoutItTheHeirTakesOverAndTheOldBossComesOutSore()
    {
        var (sim, w, g, boss, heir) = SentAway();
        g.RunFromInside = false;
        Week(sim);
        Assert.Equal(heir.Id, g.BossHoodId);
        Assert.False(w.RunsFromInside(g));
        Assert.Equal(30, boss.Loyalty);
    }

    [Fact]
    public void HeWalksOutAndTakesTheChairBack()
    {
        var (sim, w, g, boss, heir) = SentAway();
        Week(sim);
        boss.JailWeeks = 1;
        for (int i = 0; i < 5 && boss.State == HoodState.Jailed; i++) Week(sim);
        Assert.Equal(HoodState.Free, boss.State);
        Week(sim);
        Assert.Equal(boss.Id, g.BossHoodId);
        Assert.False(w.RunsFromInside(g));
        Assert.Contains(w.Events, e => e.Text == $"{boss.Name} walked out and took back the chair at {g.Name}. {heir.Name} goes back to minding the street.");
    }

    [Fact]
    public void NoMoneyForTheGuardsNoWordGetsOut()
    {
        var (sim, w, g, boss, heir) = SentAway();
        Week(sim);
        g.Cash = 0;
        Week(sim);
        Assert.Equal(heir.Id, g.BossHoodId);
        Assert.Contains(w.Events, e => e.Text == $"The guards stopped carrying {boss.Name}'s word when {g.Name}'s money ran short.");
    }

    [Fact]
    public void NoWordGetsOutOfAFederalPen()
    {
        var (sim, w, g, boss, heir) = SentAway();
        boss.FederalTime = true;
        Week(sim);
        Assert.Equal(heir.Id, g.BossHoodId);
        Assert.Contains(w.Events, e => e.Text == $"{boss.Name} can't run {g.Name} from his cell. He's in a federal pen. No word gets out of there.");
    }

    [Fact]
    public void AnAmbitiousStreetBossTakesTheChair()
    {
        // The warden can still move the boss first now and then, so try a few cities.
        int coups = 0;
        var why = new List<string>();
        for (ulong seed = 8; seed <= 12; seed++)
        {
            var (sim, w, g, boss, _) = SentAway(weeks: 1000, seed: seed);
            Week(sim);
            boss.Intimidation = 1;
            Hood? street = null;
            for (int i = 0; i < 300 && w.RunsFromInside(g); i++)
            {
                // Whoever minds the street wants the chair, though not so badly he would walk out with a faction, and the warden has no push to move the boss.
                if (w.StreetBoss(g) is { } next)
                {
                    street = next;
                    street.Ambition = 100;
                    street.Loyalty = 50;
                }
                g.Cash = 20_000;
                w.Hall.DAReform = w.Hall.Reform = false;
                w.Hall.Outrage = 0;
                Week(sim);
            }
            // He may also have died in his cell, or been moved first.
            if (street == null || !w.Events.Any(e => e.Text.StartsWith($"{street.Name} stopped taking orders from a cell. He runs {g.Name} now")))
            {
                why.Add(string.Join(" / ", w.Events.Where(e => e.Kind is EventKind.Inside or EventKind.Succession).Select(e => e.Text)));
                continue;
            }
            coups++;
            Assert.Equal(street.Id, g.BossHoodId);
            Assert.False(w.RunsFromInside(g));
            Assert.Equal(20, boss.Loyalty);
        }
        Assert.True(coups >= 3, $"only {coups} of 5 street bosses took the chair: " + string.Join("\n", why));
    }

    [Fact]
    public void YouCanHandItOverFromTheCell()
    {
        var (sim, w, g, boss, heir) = SentAway();
        Week(sim);
        var shell = new CommandShell(sim);
        Assert.Contains($"hands {g.Name} to {heir.Name} for good", shell.Execute("inside off"));
        Week(sim);
        Assert.Equal(heir.Id, g.BossHoodId);
        Assert.True(boss.Loyalty >= 70);
        Assert.Contains(w.Events, e => e.Text == $"{boss.Name} handed {g.Name} to {heir.Name} from his cell.");
    }

    [Fact]
    public void ASmartRivalBossRunsItFromInsideAndADullOneDoesnt()
    {
        var (sim, w, g, boss, heir) = SentAway(player: false);
        boss.Brains = 8;
        Week(sim);
        Assert.True(w.RunsFromInside(g));

        (sim, w, g, boss, heir) = SentAway(player: false);
        boss.Brains = Content.InsideBrains - 1;
        Week(sim);
        Assert.False(w.RunsFromInside(g));
        Assert.Equal(heir.Id, g.BossHoodId);
    }

    [Fact]
    public void ItIsSaved()
    {
        var (sim, w, g, boss, _) = SentAway();
        Week(sim);
        g.RunFromInside = false;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var lg = loaded.GangById(g.Id);
        Assert.Equal(g.InsideSinceWeek, lg.InsideSinceWeek);
        Assert.False(lg.RunFromInside);
        Assert.True(loaded.RunsFromInside(lg));
    }
}
