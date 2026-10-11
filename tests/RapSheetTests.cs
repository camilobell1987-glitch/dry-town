using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class RapSheetTests
{
    private static (Simulation Sim, World W, Hood Man) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed });
        var w = sim.World;
        return (sim, w, w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId));
    }

    private static void Convict(Hood man, int times)
    {
        for (int i = 0; i < times; i++) man.Record.Add(new RapEntry { Kind = RapKind.Convicted, Text = "Convicted of something." });
    }

    [Fact]
    public void EveryArrestGoesOnTheSheet()
    {
        var (sim, _, man) = Start();
        Assert.Equal("No police record.", Reports.Priors(man));
        sim.Jail(man, 4, "for menacing a shopkeeper");
        Assert.Equal(RapKind.Petty, man.Record[^1].Kind);
        man.State = HoodState.Free;
        sim.Jail(man, 30, "with attempted arson");
        Assert.Equal(RapKind.Charged, man.Record[^1].Kind);
        for (int i = 0; i < Content.WeeksToTrial; i++) sim.AdvanceWeek();
        Assert.Contains(man.Record[^1].Kind, new[] { RapKind.Convicted, RapKind.Acquitted });
        Assert.StartsWith("Arrested 2 times", Reports.Priors(man));
    }

    [Fact]
    public void ARecordMakesTheNextSentenceLonger()
    {
        var (sim, _, man) = Start();
        Convict(man, 2);
        sim.Jail(man, 20, "with attempted arson");
        Assert.Equal(Content.Sentence(20, 2), man.Case!.Sentence);
        Assert.Equal(40, man.Case.Sentence);
        Assert.False(man.Case.Life);
        Assert.Contains("100% longer", Reports.Priors(man));
    }

    [Fact]
    public void AFourthConvictionIsLife()
    {
        int gone = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var (sim, w, man) = Start(seed);
            Convict(man, Content.HabitualOffender);
            // A loyal man, so he doesn't take the DA's deal instead.
            man.Loyalty = 100;
            sim.Jail(man, 20, "with attempted arson", strength: 1);
            Assert.True(man.Case!.Life);
            for (int i = 0; i < Content.WeeksToTrial; i++) sim.AdvanceWeek();
            if (man.State != HoodState.Gone) continue;
            gone++;
            Assert.Contains("for life", man.Record[^1].Text);
            Assert.Contains(w.Events, e => e.Text.Contains("habitual offender law") && e.Text.Contains(man.Name));
        }
        Assert.True(gone >= 7, $"only {gone} of 10 went away for life");
    }

    [Fact]
    public void MenAreKnownForTheirWork()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 2, Size = CitySize.Medium });
        for (int i = 0; i < Content.WeeksPerYear; i++) sim.AdvanceWeek(playerAutopilot: true);
        var men = sim.World.Hoods;
        Assert.Contains(men, h => h.Deeds.Shakedowns > 0);
        Assert.Contains(men, h => h.Deeds.Takeovers > 0);
        Assert.Contains(men, h => h.Deeds.Kills > 0);
        var hard = men.First(h => h.Deeds.Shakedowns > 0);
        Assert.Contains("shop", Reports.Known(hard));
    }

    [Fact]
    public void TheShellReadsASheet()
    {
        var shell = new CommandShell(Simulation.New(new WorldSettings { Seed = 4 }));
        var man = shell.Sim.World.AvailableHoodsOf(shell.Sim.World.Player.Id).First();
        shell.Sim.Jail(man, 5, "for menacing a shopkeeper");
        var sheet = shell.Execute($"sheet {man.Id}");
        Assert.Contains(man.Name, sheet);
        Assert.Contains("Got 5 weeks for menacing a shopkeeper.", sheet);
        Assert.Contains("No such man", shell.Execute("sheet 9999"));
    }

    [Fact]
    public void TheSheetSurvivesASave()
    {
        var (sim, _, man) = Start();
        sim.Jail(man, 5, "for menacing a shopkeeper");
        man.Deeds.Kills = 3;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, new List<Order>()))).World.HoodById(man.Id);
        Assert.Single(loaded.Record);
        Assert.Equal(3, loaded.Deeds.Kills);
    }
}
