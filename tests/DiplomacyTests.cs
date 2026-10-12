using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class DiplomacyTests
{
    private static (Simulation Sim, World W, Gang Me, Gang Them, Gang Third) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed });
        var w = sim.World;
        var rivals = w.LivingGangs.Where(g => !g.IsPlayer).OrderBy(g => g.Id).ToList();
        return (sim, w, w.Player, rivals[0], rivals[1]);
    }

    /// <summary>Make a gang about this strong, by changing the weight of its men.</summary>
    private static void Muscle(World w, Gang g, int power)
    {
        foreach (var h in w.AvailableHoodsOf(g.Id)) { h.Intimidation = 1; h.Muscle = 1; h.Weapon = Weapon.None; }
        var man = w.AvailableHoodsOf(g.Id).First();
        double others = w.Power(g) - man.Strength;
        man.Muscle = (int)Math.Max(1, power - others - man.Intimidation);
    }

    [Fact]
    public void AnAttackLeavesAGrudgeThatAlliesShare()
    {
        var (_, w, me, them, third) = Start();
        w.Sign(them, third, PactKind.Alliance);
        w.Hostile(me, them, Content.GrudgeArson);
        Assert.Equal(Content.GrudgeArson, w.GrudgeOf(them.Id, me.Id));
        Assert.Equal(Content.GrudgeArson / 2, w.GrudgeOf(third.Id, me.Id));
        Assert.Equal(0, w.GrudgeOf(me.Id, them.Id));
    }

    [Fact]
    public void MovingOnAPartnerBreaksYourWord()
    {
        var (_, w, me, them, third) = Start();
        Assert.Null(w.Refusal(me, them, PactKind.Truce));
        w.Propose(me, them, PactKind.Truce);
        Assert.True(w.AtPeace(me.Id, them.Id));

        w.Hostile(me, them, Content.GrudgeTakeover);
        Assert.False(w.AtPeace(me.Id, them.Id));
        Assert.True(w.Untrusted(me));
        Assert.Equal(Content.GrudgeBetrayed + Content.GrudgeTakeover, w.GrudgeOf(them.Id, me.Id));
        Assert.Equal(Content.GrudgeWitnessed, w.GrudgeOf(third.Id, me.Id));
        Assert.Contains("broke its last one", w.Refusal(me, third, PactKind.Truce));
    }

    [Fact]
    public void AMuchStrongerGangWantsPayingForATruce()
    {
        var (_, w, me, them, _) = Start();
        Muscle(w, me, 40);
        Muscle(w, them, 100);
        var no = w.Refusal(me, them, PactKind.Truce);
        Assert.Contains("a week to leave them alone", no);
        int ask = Content.TributeAsk(w.Income(me));
        Assert.Null(w.Refusal(me, them, PactKind.Truce, ask));
        w.Propose(me, them, PactKind.Truce, ask);
        Assert.Equal(me.Id, w.PactBetween(me.Id, them.Id)!.PayerId);
    }

    [Fact]
    public void BadBloodCanBeBoughtOff()
    {
        var (_, w, me, them, _) = Start();
        w.AddGrudge(them.Id, me.Id, 80);
        Assert.Contains("too much bad blood", w.Refusal(me, them, PactKind.Truce));
        Assert.True(w.WantsPaying(me, them));
        Assert.Null(w.Refusal(me, them, PactKind.Truce, Content.TributeAsk(w.Income(me))));
        Assert.NotNull(w.Refusal(me, them, PactKind.Alliance));
    }

    [Fact]
    public void TributeIsPaidOnSundayUntilTheMoneyRunsOut()
    {
        var (sim, w, me, them, _) = Start();
        w.Sign(me, them, PactKind.Truce, me.Id, 300);
        me.Cash = 1000;
        long theirs = them.Cash;
        sim.AdvanceWeek();
        Assert.Equal(300, w.LastLedger[me.Id].TributePaid);
        Assert.Equal(300, w.LastLedger[them.Id].TributeIn);

        me.Cash = -5000;
        sim.AdvanceWeek();
        Assert.False(w.AtPeace(me.Id, them.Id));
        Assert.Contains(w.Events, e => e.Kind == EventKind.Diplomacy && e.Text.Contains("couldn't pay"));
    }

    [Fact]
    public void RivalsKeepTheirWord()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var (sim, w, me, _, _) = Start(seed);
            foreach (var g in w.LivingGangs.Where(g => !g.IsPlayer).ToList()) w.Sign(me, g, PactKind.Truce);
            int turf = w.TurfOf(me.Id).Count();
            for (int i = 0; i < 8; i++) sim.AdvanceWeek();
            var moved = w.Events.Where(e => e.GangId != me.Id && e.Kind is EventKind.Takeover or EventKind.Arson or EventKind.Hit or EventKind.Hijack && e.Text.Contains(me.Name))
                .Where(e => w.PactBetween(me.Id, e.GangId) != null);
            Assert.Empty(moved);
        }
    }

    [Fact]
    public void ATruceRunsOut()
    {
        var (sim, w, me, them, _) = Start(5);
        w.Sign(me, them, PactKind.Truce);
        for (int i = 0; i < Content.TruceWeeks; i++) sim.AdvanceWeek();
        Assert.False(w.AtPeace(me.Id, them.Id));
        Assert.Contains(w.Events, e => e.Text.StartsWith("The truce between") && e.Text.EndsWith("ran out."));
    }

    [Fact]
    public void GrudgesFade()
    {
        var (sim, w, me, them, _) = Start();
        w.AddGrudge(them.Id, me.Id, 10);
        for (int i = 0; i < 5; i++) sim.AdvanceWeek();
        Assert.Equal(0, w.GrudgeOf(them.Id, me.Id));
    }

    [Fact]
    public void TurningDownADemandMakesAnEnemy()
    {
        var (_, w, me, them, _) = Start();
        w.Put(them, OfferKind.Demand, 200);
        var offer = Assert.Single(w.Offers);
        Assert.Contains("$200 a week", w.Describe(offer));
        w.Answer(offer, accept: false);
        Assert.Empty(w.Offers);
        Assert.Equal(Content.GrudgeRefused, w.GrudgeOf(them.Id, me.Id));

        w.Put(them, OfferKind.Demand, 200);
        w.Answer(w.Offers[0], accept: true);
        var pact = w.PactBetween(me.Id, them.Id)!;
        Assert.Equal(me.Id, pact.PayerId);
        Assert.Equal(200, pact.Tribute);
    }

    [Fact]
    public void AnUnansweredOfferLapses()
    {
        var (sim, w, _, them, _) = Start();
        w.Put(them, OfferKind.Truce);
        var offer = w.Offers.Single();
        for (int i = 0; i < Content.OfferWeeks; i++) sim.AdvanceWeek();
        Assert.DoesNotContain(offer, w.Offers);
    }

    [Fact]
    public void AnAllianceNeedsSomeoneToBeAfraidOf()
    {
        var (_, w, me, them, third) = Start();
        foreach (var g in w.LivingGangs) Muscle(w, g, 50);
        Assert.Contains("nobody worth ganging up on", w.Refusal(me, them, PactKind.Alliance));
        Muscle(w, third, 120);
        Assert.Null(w.Refusal(me, them, PactKind.Alliance));
    }

    [Fact]
    public void RivalsMakeAndBreakPactsOnTheirOwn()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 2, Size = CitySize.Medium });
        for (int i = 0; i < 3 * Content.WeeksPerYear; i++) sim.AdvanceWeek(playerAutopilot: true);
        var ev = sim.World.Events.Where(e => e.Kind == EventKind.Diplomacy).ToList();
        Assert.Contains(ev, e => e.Text.Contains(" broke the "));
        Assert.Contains(ev, e => e.Text.EndsWith("agreed a truce.") || e.Text.Contains(" a week to be left alone"));
        Assert.Contains(ev, e => e.Text.Contains(" are allies now"));
    }

    [Fact]
    public void TheShellTalksToTheOtherGangs()
    {
        var shell = new CommandShell(Simulation.New(new WorldSettings { Seed = 4 }));
        var w = shell.Sim.World;
        var them = w.LivingGangs.First(g => !g.IsPlayer);
        Assert.Contains("no pact", shell.Execute("relations"));
        Assert.Contains("agreed", shell.Execute($"truce {them.Id}"));
        Assert.Contains($"truce, {Content.TruceWeeks} weeks left", shell.Execute("relations"));
        var biz = w.Businesses.First(b => !b.IsProtected);
        biz.ProtectorGangId = them.Id;
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        Assert.Contains("That breaks your truce", shell.Execute($"extort {man.Id} {biz.Id}"));
        Assert.Contains("It's off", shell.Execute($"break {them.Id}"));
        Assert.Contains("broke your word", shell.Execute("relations"));

        w.Put(w.LivingGangs.First(g => !g.IsPlayer && g.Id != them.Id), OfferKind.Truce);
        Assert.Contains("accept 1", shell.Execute("relations"));
        Assert.Contains("You have a truce", shell.Execute("accept 1"));
    }

    [Fact]
    public void PactsGrudgesAndOffersSurviveASave()
    {
        var (sim, w, me, them, third) = Start();
        w.Sign(me, them, PactKind.Truce, me.Id, 150);
        w.AddGrudge(third.Id, me.Id, 33);
        w.Put(third, OfferKind.Demand, 250);
        me.BrokeWordWeek = 3;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, new List<Order>()))).World;
        Assert.Equal(150, loaded.PactBetween(me.Id, them.Id)!.Tribute);
        Assert.Equal(33, loaded.GrudgeOf(third.Id, me.Id));
        Assert.Equal(250, Assert.Single(loaded.Offers).Tribute);
        Assert.Equal(3, loaded.Player.BrokeWordWeek);
    }
}
