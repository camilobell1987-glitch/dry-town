using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class MarriageTests
{
    /// <summary>A quiet city with one rival, the two at peace long enough to talk of a match.</summary>
    private static (Simulation Sim, World W, Gang Me, Gang Them) Start(bool peace = true)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = 2, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var me = w.Player;
        var them = w.LivingGangs.First(g => !g.IsPlayer);
        me.Cash = them.Cash = 20_000;
        if (peace) w.Sign(me, them, PactKind.Truce).SignedWeek = w.Week - Content.MatchPactWeeks;
        return (sim, w, me, them);
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    [Fact]
    public void AMatchNeedsASonPeaceAndMoney()
    {
        var (_, w, me, them) = Start(peace: false);
        foreach (var h in w.HoodsOf(me.Id)) h.Family = false;
        Assert.StartsWith($"{me.Name} has no son or nephew free to marry.", w.MatchRefusal(me, them));
        var son = w.NewRelative(me);
        Assert.Equal(son, w.Groom(me));
        Assert.EndsWith($"Keep the peace with them for {Content.MatchPactWeeks} weeks first.", w.MatchRefusal(me, them));
        w.Sign(me, them, PactKind.Truce);
        Assert.NotNull(w.MatchRefusal(me, them));
        w.PactBetween(me.Id, them.Id)!.SignedWeek = w.Week - Content.MatchPactWeeks;
        me.Cash = 100;
        Assert.Equal($"The wedding costs ${Content.WeddingCost:N0}.", w.MatchRefusal(me, them));
        me.Cash = 20_000;
        Assert.Null(w.MatchRefusal(me, them));

        double respect = them.Respect;
        string said = w.ProposeMatch(me, them);
        Assert.Contains($"{son.Name} married ", said);
        Assert.Equal(20_000 - Content.WeddingCost, me.Cash);
        Assert.Equal(respect + Content.WeddingRespect, them.Respect, 6);
        var m = w.MarriageBetween(me.Id, them.Id)!;
        Assert.Equal(son.Id, m.GroomHoodId);
        Assert.EndsWith(World.Surname(w.HoodById(them.BossHoodId).Name), m.Bride);
        Assert.Equal(PactKind.Alliance, w.PactBetween(me.Id, them.Id)!.Kind);
        Assert.Contains(w.Events, e => e.Text == $"{son.Name} of {me.Name} married {m.Bride}, the daughter of {w.HoodById(them.BossHoodId).Name} of {them.Name}. The two outfits are family now.");
        Assert.Equal($"You're already family with {them.Name}.", w.MatchRefusal(me, them));
        w.NewRelative(me);
        Assert.Equal($"{me.Name} is already family with {them.Name}.", w.CannotMatch(me));
        Assert.StartsWith("family by marriage", Reports.Standing(w, me, them));
    }

    [Fact]
    public void ABigOutfitWontMarryBeneathIt()
    {
        var (_, w, me, them) = Start();
        w.NewRelative(me);
        foreach (var b in w.Businesses.Where(b => b.ProtectorGangId != me.Id).Take(40)) b.ProtectorGangId = them.Id;
        Assert.EndsWith($"{me.Name} is too small.", w.MatchRefusal(me, them));
    }

    [Fact]
    public void FamilyNeverRunsOut()
    {
        var (sim, w, me, them) = Start();
        w.NewRelative(me);
        w.ProposeMatch(me, them);
        for (int i = 0; i < Content.AllianceWeeks + 5; i++) Week(sim);
        if (!them.Alive) return;
        Assert.Equal(PactKind.Alliance, w.PactBetween(me.Id, them.Id)!.Kind);
        Assert.DoesNotContain(w.Events, e => e.Text.StartsWith("The alliance between ") && e.Text.EndsWith(" ran out."));

        // Torn up by anything but a betrayal, it's made good again on Sunday.
        w.Pacts.Remove(w.PactBetween(me.Id, them.Id)!);
        w.KeepFamily();
        Assert.Equal(PactKind.Alliance, w.PactBetween(me.Id, them.Id)!.Kind);
        Assert.NotNull(w.MarriageBetween(me.Id, them.Id));
    }

    [Fact]
    public void TurningOnFamilyCostsTwice()
    {
        var (_, w, me, them) = Start();
        w.NewRelative(me);
        w.ProposeMatch(me, them);
        me.Respect = 50;
        string bride = w.MarriageBetween(me.Id, them.Id)!.Bride;
        w.Break(me, them);
        Assert.Null(w.MarriageBetween(me.Id, them.Id));
        Assert.Null(w.PactBetween(me.Id, them.Id));
        Assert.Equal(50 - Content.RespectBrokenWord * Content.MarriedBreak, me.Respect, 6);
        Assert.Equal(Content.GrudgeBetrayed * Content.MarriedBreak, w.GrudgeOf(them.Id, me.Id));
        Assert.Contains(w.Events, e => e.Text == $"{me.Name} turned on {them.Name}, their own family by marriage, and {bride} went home to her father. Nobody will take their word for a year.");
    }

    [Fact]
    public void AFinishedOutfitGoesToItsFamily()
    {
        var (sim, w, me, them) = Start();
        w.NewRelative(me);
        w.ProposeMatch(me, them);
        var bars = w.Businesses.Where(b => b.IsOpen && !b.IsProtected).Take(2).ToList();
        foreach (var b in bars) b.ProtectorGangId = them.Id;
        foreach (var h in w.HoodsOf(them.Id).ToList()) h.State = HoodState.Dead;
        Week(sim);
        Assert.False(them.Alive);
        Assert.All(bars, b => Assert.Equal(me.Id, b.ProtectorGangId));
        Assert.Contains(w.Events, e => e.Text == $"{them.Name} is finished. What was left of it went to {me.Name}, its family by marriage.");
        Assert.Empty(w.Marriages);
    }

    [Fact]
    public void FamilyStandsBehindANewBoss()
    {
        var (sim, w, me, them) = Start();
        w.NewRelative(me);
        w.ProposeMatch(me, them);
        var men = w.AvailableHoodsOf(them.Id).Where(h => h.Id != them.BossHoodId).OrderBy(h => h.Id).ToList();
        Assert.True(men.Count >= 2);
        w.NameHeir(them, men[0], announce: false);
        men[1].Ambition = 95;
        men[1].Loyalty = 30;
        w.HoodById(them.BossHoodId).State = HoodState.Dead;
        int gangs = w.Gangs.Count;
        Week(sim);
        Assert.Equal(men[0].Id, them.BossHoodId);
        Assert.Equal(gangs, w.Gangs.Count);
        Assert.Contains(w.Events, e => e.Text == $"{men[1].Name} thought about striking out on his own, but {them.Name}'s family in {me.Name} stood behind {men[0].Name}.");
    }

    [Fact]
    public void ARivalAsksForAMatch()
    {
        var (_, w, me, them) = Start();
        var son = w.NewRelative(them);
        them.Cash = 50_000;
        for (int i = 0; i < 60 && !w.Offers.Any(); i++) AiPlanner.Negotiate(w, them);
        var offer = Assert.Single(w.Offers);
        Assert.Equal(OfferKind.Marriage, offer.Kind);
        Assert.Contains($"proposes that {son.Name} marry your boss's daughter.", w.Describe(offer));
        Assert.StartsWith($"{son.Name} married ", w.Answer(offer, true));
        Assert.Equal(them.Id, w.MarriageBetween(me.Id, them.Id)!.GroomGangId);
        Assert.Equal(50_000 - Content.WeddingCost, them.Cash);
    }

    [Fact]
    public void TurningDownAMatchIsAnInsult()
    {
        var (_, w, me, them) = Start();
        w.NewRelative(them);
        w.Put(them, OfferKind.Marriage);
        Assert.Equal($"You turned down {them.Name}'s match. They took it as an insult.", w.Answer(w.Offers[0], false));
        Assert.Equal(Content.GrudgeRefused / 2, w.GrudgeOf(them.Id, me.Id));
    }

    [Fact]
    public void TheConsoleMarriesAndItIsSaved()
    {
        var (sim, w, me, them) = Start();
        var son = w.NewRelative(me);
        var shell = new CommandShell(sim);
        Assert.Contains($"{son.Name} is free to marry", shell.Execute("relations"));
        Assert.Contains($"{son.Name} married ", shell.Execute($"marry {them.Id}"));
        Assert.Contains("family by marriage", shell.Execute("relations"));
        Assert.Contains("marry <gang>", CommandShell.Help);

        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var m = loaded.MarriageBetween(me.Id, them.Id)!;
        Assert.Equal(son.Id, m.GroomHoodId);
        Assert.Equal(w.MarriageBetween(me.Id, them.Id)!.Bride, m.Bride);
    }
}
