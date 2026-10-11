using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class CommissionTests
{
    /// <summary>A quiet city with three outfits, the Commission sitting and all three at the table.</summary>
    private static (Simulation Sim, World W, Gang Me, Gang A, Gang B) Start(bool sitting = true)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = 3, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var rivals = w.LivingGangs.Where(g => !g.IsPlayer).OrderBy(g => g.Id).ToList();
        if (sitting)
        {
            w.Week = w.CommissionStartWeek + 5;
            w.Commission.Sitting = true;
            w.Commission.FormedWeek = w.CommissionStartWeek;
            w.Commission.Seats.AddRange(w.LivingGangs.Select(g => g.Id));
        }
        return (sim, w, w.Player, rivals[0], rivals[1]);
    }

    [Fact]
    public void TheBossesSitDownIn1931()
    {
        var (sim, w, me, _, _) = Start(sitting: false);
        w.Week = w.CommissionStartWeek - 1;
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [me.Id] = new() });
        Assert.False(w.Commission.Sitting);
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [me.Id] = new() });
        Assert.True(w.Commission.Sitting);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Era && e.Text.Contains("formed a Commission"));
        Assert.All(w.LivingGangs, g => Assert.Equal(w.TurfOf(g.Id).Count() >= Content.CommissionTurf, w.Seated(g.Id)));
        Assert.Contains("has sat since", new CommandShell(sim).Execute("commission"));
    }

    [Fact]
    public void GoingToWarWithoutTheTablesSayGetsYouThrownOut()
    {
        var (_, w, me, a, b) = Start();
        Assert.NotNull(Reports.WarWarning(w, me.Id, a));
        int before = w.GrudgeOf(b.Id, me.Id);
        w.Declare(me, a);
        Assert.True(w.AtWar(me.Id, a.Id));
        Assert.False(w.Seated(me.Id));
        Assert.True(w.Shelved(me.Id));
        Assert.Equal(before + Content.GrudgeBreach, w.GrudgeOf(b.Id, me.Id));
        Assert.Contains(w.Events, e => e.Kind == EventKind.Commission && e.Text.StartsWith($"{me.Name} went to war with {a.Name} without the Commission's say-so."));
        // Out of the Commission, nobody needs its say-so to go after you.
        Assert.False(w.BreaksRules(b.Id, me.Id, SanctionKind.War));
        Assert.Contains("won't have you back", w.Walk(me, leave: false));
    }

    [Fact]
    public void TheTableLetsYouGoAfterAnOutfitItHates()
    {
        var (_, w, me, a, b) = Start();
        w.AddGrudge(b.Id, a.Id, 40);
        Assert.True(w.WouldPass(me, a));
        string answer = w.Ask(me, a, SanctionKind.War);
        Assert.StartsWith("The table said yes, 1 to 0.", answer);
        Assert.True(w.Sanctioned(me.Id, a.Id, SanctionKind.Hit));
        Assert.Null(Reports.WarWarning(w, me.Id, a));
        w.Declare(me, a);
        Assert.True(w.Seated(me.Id));
        Assert.Contains(w.Events, e => e.Text == $"The Commission voted 1 to 0 to let {me.Name} go to war with {a.Name}.");
    }

    [Fact]
    public void TheTableSaysNoWhenItIsSoreAtYou()
    {
        var (_, w, me, a, b) = Start();
        w.AddGrudge(b.Id, a.Id, 40);
        w.AddGrudge(b.Id, me.Id, 40);
        Assert.StartsWith("The table said no, 0 to 1.", w.Ask(me, a, SanctionKind.Hit));
        Assert.False(w.Sanctioned(me.Id, a.Id, SanctionKind.Hit));
        // And it won't hear another question for a while.
        Assert.StartsWith("You put a question to the table 0 weeks ago.", w.CannotAsk(me, a));
    }

    [Fact]
    public void ATiedVoteGoesTheChairsWay()
    {
        var (_, w, me, a, b) = Start();
        // With only the target to vote, there's nobody to say yes: a tie, and the chair decides.
        w.Commission.Seats.Remove(b.Id);
        foreach (var biz in w.Businesses.Where(x => x.IsOpen).Take(40)) { biz.ProtectorGangId = me.Id; }
        Assert.Equal(me, w.Chair);
        Assert.True(w.WouldPass(me, a));
    }

    [Fact]
    public void GoingAfterABossWithoutTheTablesSayIsAlsoABreach()
    {
        var (sim, w, me, a, _) = Start();
        var boss = w.HoodById(a.BossHoodId);
        Assert.NotNull(Reports.BreachWarning(w, me.Id, boss));
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        var shell = new CommandShell(sim);
        Assert.Contains("breaks the rules", shell.Execute($"hit {man.Id} {boss.Id}"));
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [me.Id] = shell.Pending.ToList() });
        Assert.Contains(w.Events, e => e.Kind == EventKind.Commission && e.Text.StartsWith($"{me.Name} sent men after {boss.Name}, boss of {a.Name}, without the Commission's say-so."));
        Assert.False(w.Seated(me.Id));
    }

    [Fact]
    public void YouCanWalkOutAndAskBackIn()
    {
        var (sim, w, me, a, _) = Start();
        foreach (var biz in w.Businesses.Where(x => x.IsOpen).Take(8)) biz.ProtectorGangId = me.Id;
        Assert.StartsWith("You walked out.", w.Walk(me, leave: true));
        Assert.False(w.BreaksRules(me.Id, a.Id, SanctionKind.War));
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [me.Id] = new() });
        Assert.False(w.Seated(me.Id));
        Assert.StartsWith("You have your seat back.", w.Walk(me, leave: false));
        Assert.True(w.Seated(me.Id));
    }

    [Fact]
    public void TheCommissionIsSaved()
    {
        var (sim, w, me, a, _) = Start();
        w.Commission.Sanctions.Add(new Sanction { Kind = SanctionKind.War, By = me.Id, Against = a.Id, UntilWeek = w.Week + 5 });
        w.Commission.ShelvedUntil[a.Id] = w.Week + 30;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.True(loaded.Commission.Sitting);
        Assert.Equal(w.Commission.Seats, loaded.Commission.Seats);
        Assert.True(loaded.Sanctioned(me.Id, a.Id, SanctionKind.War));
        Assert.True(loaded.Shelved(a.Id));
    }
}
