using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class RivalCrewTests
{
    [Fact]
    public void RivalsKeepCrewsAndTheAutopilotLeavesYoursAlone()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 3, Size = CitySize.Medium });
        var w = sim.World;
        for (int i = 0; i < 30; i++) sim.AdvanceWeek(playerAutopilot: true);

        Assert.Empty(w.CrewsOf(w.Player.Id));
        var rivals = w.LivingGangs.Where(g => !g.IsPlayer).ToList();
        Assert.Contains(rivals, g => w.CrewsOf(g.Id).Any());
        foreach (var g in rivals)
        {
            Assert.True(w.CrewsOf(g.Id).Count() <= Content.MaxRivalCrews);
            foreach (var crew in w.CrewsOf(g.Id))
            {
                Assert.True(crew.MemberIds.Count <= Crew.MaxMembers);
                Assert.All(crew.Everyone, id => Assert.Equal(g.Id, w.HoodById(id).GangId));
                Assert.NotEqual(g.BossHoodId, crew.LieutenantHoodId);
            }
        }
    }

    [Fact]
    public void ARivalCrewGoesInTogether()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 3, Size = CitySize.Medium });
        var w = sim.World;
        bool together = false;
        for (int i = 0; i < 80 && !together; i++)
        {
            sim.AdvanceWeek(playerAutopilot: true);
            together = w.Script.Any(a => a.Kind == ActionKind.Takeover && a.GangId != w.Player.Id && a.Backup.Count > 0
                && w.CrewOfHood(a.HoodId) is { } crew && a.Backup.All(crew.Everyone.Contains));
        }
        Assert.True(together);
    }

    [Fact]
    public void TheConsoleListsARivalsCrews()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 3, Size = CitySize.Medium });
        var w = sim.World;
        for (int i = 0; i < 30; i++) sim.AdvanceWeek(playerAutopilot: true);
        var rival = w.LivingGangs.First(g => !g.IsPlayer && w.CrewsOf(g.Id).Any());
        var lt = w.HoodById(w.CrewsOf(rival.Id).First().LieutenantHoodId);
        string text = new CommandShell(sim).Execute($"crews {rival.Id}")!;
        Assert.Contains($"{lt.Name} (lieutenant", text);
        // Without a man inside, nobody knows how the lieutenants feel.
        Assert.DoesNotContain("loyalty", text);
    }
}
