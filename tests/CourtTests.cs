using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class CourtTests
{
    private static (Simulation Sim, World W, Gang Me, Hood Man) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed });
        var w = sim.World;
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        return (sim, w, w.Player, man);
    }

    [Fact]
    public void APettyChargeIsSettledOnTheSpot()
    {
        var (sim, _, _, man) = Start();
        sim.Jail(man, Content.PettySentence, "for menacing a shopkeeper");
        Assert.Equal(HoodState.Jailed, man.State);
        Assert.Null(man.Case);
        Assert.Equal(Content.PettySentence, man.JailWeeks);
    }

    [Fact]
    public void ALongerChargeWaitsForAJury()
    {
        var (sim, w, _, man) = Start();
        sim.Jail(man, 40, "with attempted arson");
        Assert.NotNull(man.Case);
        Assert.Equal(Content.WeeksToTrial, man.JailWeeks);
        Assert.Equal(Content.CityCase, Simulation.ConvictionChance(w, man), 3);
        Assert.Contains(man, w.OnTrial(man.GangId));
    }

    [Fact]
    public void ALawyerAJudgeAndAJuryEachHelp()
    {
        var (sim, w, me, man) = Start();
        me.Cash = 50_000;
        w.Retain(me, Lawyer.Mouthpiece);
        sim.Jail(man, 40, "with attempted arson");
        double odds = Content.CityCase - Content.Lawyers[Lawyer.Mouthpiece].Cut;
        Assert.Equal(odds, Simulation.ConvictionChance(w, man), 3);

        int price = w.JudgePrice(man);
        Assert.Equal(price, w.PayJudge(me, man));
        Assert.Equal(50_000 - price, me.Cash);
        Assert.Equal(odds - Content.JudgeCut, Simulation.ConvictionChance(w, man), 3);
        Assert.NotNull(w.CannotPayJudge(me, man));

        man.Case!.JuryFixed = true;
        Assert.Equal(0.05, Simulation.ConvictionChance(w, man), 3);
    }

    [Fact]
    public void ALawyerHiredAfterTheArrestDoesHalfAsMuch()
    {
        var (sim, w, me, man) = Start();
        sim.Jail(man, 40, "with attempted arson");
        w.Retain(me, Lawyer.Mouthpiece);
        Assert.Equal(Content.CityCase - Content.Lawyers[Lawyer.Mouthpiece].Cut / 2, Simulation.ConvictionChance(w, man), 3);
    }

    [Fact]
    public void AFederalJudgeCantBeBought()
    {
        var (sim, w, me, man) = Start();
        me.Cash = 50_000;
        sim.Jail(man, 100, "with tax evasion", federal: true);
        Assert.Contains("federal", w.CannotPayJudge(me, man));
        Assert.Equal(-1, w.PayJudge(me, man));
    }

    [Fact]
    public void TheJuryComesBackAfterTheWaitAndTimeServedCounts()
    {
        var (sim, w, _, man) = Start();
        sim.Jail(man, 40, "with attempted arson");
        for (int i = 0; i < Content.WeeksToTrial; i++) sim.AdvanceWeek();
        Assert.Null(man.Case);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Trial && e.Text.Contains(man.Name));
        if (man.State == HoodState.Jailed) Assert.Equal(40 - Content.WeeksToTrial, man.JailWeeks);
        else Assert.Equal(HoodState.Free, man.State);
    }

    [Fact]
    public void AnAlmostCertainAcquittalUsuallyComes()
    {
        int free = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var (sim, _, _, man) = Start(seed);
            sim.Jail(man, 40, "with attempted arson", strength: 0);
            for (int i = 0; i < Content.WeeksToTrial; i++) sim.AdvanceWeek();
            if (man.State == HoodState.Free || man.Case == null && man.JailWeeks < 30) free++;
        }
        Assert.True(free >= 15, $"only {free} of 20 walked");
    }

    [Fact]
    public void ALawyerWhoIsntPaidQuits()
    {
        var (sim, w, me, _) = Start();
        w.Retain(me, Lawyer.Mouthpiece);
        me.Cash = 0;
        sim.AdvanceWeek();
        Assert.Equal(Lawyer.None, me.Lawyer);
    }

    [Fact]
    public void AManCanBeSentToLeanOnTheJury()
    {
        var (sim, w, me, man) = Start(1);
        var heavy = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId && h.Id != man.Id);
        heavy.Intimidation = 10;
        sim.Jail(man, 40, "with attempted arson");
        sim.BeginWeek(new Dictionary<int, List<Order>> { [me.Id] = new() { new JuryOrder(me.Id, heavy.Id, man.Id) } });
        sim.RunUntil(Content.HoursPerWeek - 30);
        Assert.True(man.Case!.JuryFixed || heavy.Case != null);
    }

    [Fact]
    public void TheShellHandlesTheCourts()
    {
        var shell = new CommandShell(Simulation.New(new WorldSettings { Seed = 4 }));
        var w = shell.Sim.World;
        w.Player.Cash = 50_000;
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        Assert.Contains("Nobody is waiting", shell.Execute("cases"));
        shell.Sim.Jail(man, 40, "with attempted arson");
        Assert.Contains("storefront", shell.Execute("lawyer storefront"));
        Assert.Contains("took", shell.Execute($"judge {man.Id}"));
        Assert.Contains("judge paid", shell.Execute("cases"));
        Assert.Contains("trial in", shell.Execute("hoods"));
    }

    [Fact]
    public void CasesAndLawyersSurviveASave()
    {
        var (sim, w, me, man) = Start();
        w.Retain(me, Lawyer.Storefront);
        sim.Jail(man, 40, "with attempted arson");
        man.Case!.JuryFixed = true;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, new List<Order>()))).World;
        Assert.Equal(Lawyer.Storefront, loaded.Player.Lawyer);
        var again = loaded.HoodById(man.Id);
        Assert.True(again.Case!.JuryFixed);
        Assert.Equal(40, again.Case.Sentence);
    }

    [Fact]
    public void RivalsHireLawyers()
    {
        var w = Simulation.New(new WorldSettings { Seed = 1, Size = CitySize.Medium }).World;
        var sim = Simulation.New(new WorldSettings { Seed = 1, Size = CitySize.Medium });
        for (int i = 0; i < 3 * Content.WeeksPerYear; i++) sim.AdvanceWeek(playerAutopilot: true);
        Assert.Contains(sim.World.Events, e => e.Text.Contains(" on retainer") && e.GangId != sim.World.Player.Id);
        Assert.Contains(sim.World.Events, e => e.Kind == EventKind.Trial && e.Text.StartsWith("A jury acquitted"));
    }
}
