using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class ElectionTests
{
    private static (Simulation Sim, World W) ToCampaign(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed });
        while (sim.World.Week < Content.AldermanElectionWeek - Content.CampaignWeeks) sim.AdvanceWeek(playerAutopilot: true);
        return (sim, sim.World);
    }

    private static void ToVote(Simulation sim)
    {
        // Lying low, so no raid picks up a candidate before the vote.
        while (sim.World.Week < Content.AldermanElectionWeek)
        {
            sim.World.Player.Heat = 0;
            sim.AdvanceWeek();
        }
    }

    /// <summary>A free man of the player's who could run: bright, with a clean record.</summary>
    private static Hood Candidate(World w)
    {
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId && h.Id != w.Player.HeirHoodId);
        man.Brains = 8;
        man.Record.Clear();
        return man;
    }

    [Fact]
    public void EveryVoteIsCountedAndTheWinnerHasTheMost()
    {
        var (sim, w) = ToCampaign();
        ToVote(sim);
        Assert.Equal(w.Wards.Count, w.Hall.Results.Count(r => r.Kind == ElectionKind.Alderman));
        foreach (var race in w.Hall.Results)
        {
            Assert.Single(race.Field, t => t.Won);
            Assert.Equal(race.Field.Max(t => t.Votes), race.Winner.Votes);
            Assert.Equal(w.Wards[race.WardId].Alderman, race.Winner.Name);
            Assert.Contains(race.Field, t => t.Ticket == "reform");
        }
        Assert.Contains(w.Events, e => e.Kind == EventKind.Politics && e.Text.Contains("% of the vote"));
        Assert.Contains("* ", new CommandShell(sim).Execute("results"));
    }

    [Fact]
    public void YourOwnManTakesTheSeatForGood()
    {
        var (sim, w) = ToCampaign(3);
        var me = w.Player;
        var man = Candidate(w);
        var ward = w.Wards[0];
        Assert.True(Politics.Stand(w, me, man, ward));
        me.Cash += 250_000;
        Politics.Campaign(w, me, ward.Id, 200_000);
        // Straight to election night, so nothing happens to him on the street first.
        w.Week = Content.AldermanElectionWeek - 1;
        Politics.Step(w, 0);
        w.Week++;

        Assert.True(ward.OwnMan);
        Assert.Equal(me.Id, ward.OwnerGangId);
        Assert.Equal(man.Name, ward.Alderman);
        Assert.Equal(HoodState.Gone, man.State);
        Assert.Contains(w.Events, e => e.Text.Contains("leaves the street for a seat on the council"));

        // Nobody can buy him, and he takes no envelope.
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        rival.Cash = 100_000;
        Assert.True(Politics.StaysBought(w, ward, rival));
        Assert.False(Politics.Payoff(w, rival, ward));
        long cash = me.Cash;
        Politics.Step(w, 0);
        Assert.Equal(cash, me.Cash);
    }

    [Fact]
    public void OnlyACleanBrightManCanRun()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 10 });
        var w = sim.World;
        var man = Candidate(w);
        Assert.Contains("campaign is on", Politics.CannotStand(w, w.Player, man, w.Wards[0]));
        while (w.Week < Content.AldermanElectionWeek - Content.CampaignWeeks) sim.AdvanceWeek(playerAutopilot: true);
        if (!man.IsAvailable || man.GangId != w.Player.Id) man = Candidate(w);
        Assert.Null(Politics.CannotStand(w, w.Player, man, w.Wards[0]));
        var boss = w.HoodById(w.Player.BossHoodId);
        boss.State = HoodState.Free;
        Assert.Contains("boss", Politics.CannotStand(w, w.Player, boss, w.Wards[0]));
        man.Brains = Content.CandidateBrains - 1;
        Assert.Contains("brains", Politics.CannotStand(w, w.Player, man, w.Wards[0]));
        man.Brains = 8;
        man.Record.Add(new RapEntry { Week = 1, Kind = RapKind.Convicted, Text = "Convicted with arson." });
        Assert.Contains("conviction", Politics.CannotStand(w, w.Player, man, w.Wards[0]));
        man.Record.Clear();

        var shell = new CommandShell(sim);
        Assert.Contains("runs for alderman", shell.Execute($"stand {man.Id} 0"));
        Assert.Contains("already putting a man up", shell.Execute($"stand {man.Id} 0"));
        Assert.Contains("last 2 weeks", shell.Execute($"polls {man.Id} 0"));
    }

    [Fact]
    public void AJailedCandidateIsOffTheBallot()
    {
        var (sim, w) = ToCampaign();
        var man = Candidate(w);
        Politics.Stand(w, w.Player, man, w.Wards[0]);
        man.State = HoodState.Jailed;
        man.JailWeeks = 30;
        ToVote(sim);
        Assert.Contains(w.Events, e => e.Text == $"{man.Name} is off the ballot in {w.Wards[0].Name}.");
        Assert.NotEqual(man.Name, w.Wards[0].Alderman);
        Assert.Empty(w.Wards[0].Candidates);
    }

    [Fact]
    public void MenAtThePollsTurnOutVotesOrGetCharged()
    {
        var (sim, w) = ToCampaign(2);
        var ward = w.Wards[0];
        var shell = new CommandShell(sim);
        while (Politics.PollsOpen(w) == null) sim.AdvanceWeek();
        var men = w.AvailableHoodsOf(w.Player.Id).Where(h => h.Id != w.Player.BossHoodId).Take(2).ToList();
        foreach (var m in men) shell.Execute($"polls {m.Id} {ward.Id}");
        int heat = w.Player.Heat;
        sim.AdvanceWeek(new Dictionary<int, List<Order>> { [w.Player.Id] = shell.Pending.ToList() });
        int charged = men.Count(m => m.Case?.Charge == "with intimidating voters");
        Assert.Equal(men.Where(m => m.Case == null).Sum(m => Politics.PollsVotes(w, ward, m)), ward.Polls.GetValueOrDefault(w.Player.Id));
        Assert.True(charged < men.Count || ward.Polls.Count == 0);

        // Reformers keep a closer eye on their polls.
        ward.Reformer = false;
        double party = Politics.PollsRisk(w, ward, men[0]);
        ward.Reformer = true;
        Assert.True(Politics.PollsRisk(w, ward, men[0]) > party);
    }

    [Fact]
    public void TheDistrictAttorneyRunsWithTheMayorAndBendsTheCases()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 5 });
        var w = sim.World;
        Assert.NotEqual("", w.Hall.DA);
        int mayorWeek = Politics.NextElection(w, ElectionKind.Mayor);
        while (w.Week <= mayorWeek) sim.AdvanceWeek(playerAutopilot: true);
        Assert.Contains(w.Hall.Results, r => r.Race == "District attorney");
        Assert.Contains(w.Hall.Results, r => r.Race == "Mayor");
        Assert.Contains(w.Events, e => e.Text.Contains("district attorney", StringComparison.OrdinalIgnoreCase) && e.Year(w) == 1923);

        var men = w.Hoods.Where(h => h.IsAvailable && w.GangById(h.GangId).Alive).Take(3).ToList();
        w.Hall.DAReform = true;
        w.Hall.DAFriendGangId = -1;
        sim.Jail(men[0], 30, "with arson");
        Assert.Equal(Content.CityCase + Content.DaReformCase, men[0].Case!.Strength, 6);
        w.Hall.DAReform = false;
        w.Hall.DAFriendGangId = men[1].GangId;
        sim.Jail(men[1], 30, "with arson");
        Assert.Equal(Content.CityCase - Content.DaFriendCase, men[1].Case!.Strength, 6);
        sim.Jail(men[^1], 30, "with bootlegging", federal: true);
        Assert.Equal(Content.FederalCase, men[^1].Case!.Strength, 6);
    }

    [Fact]
    public void BallotsResultsAndTheDASurviveASave()
    {
        var (sim, w) = ToCampaign();
        var man = Candidate(w);
        Politics.Stand(w, w.Player, man, w.Wards[1]);
        w.Wards[0].OwnMan = true;
        w.Wards[0].OwnerGangId = w.Player.Id;
        w.Wards[1].Polls[w.Player.Id] = 321;
        w.Hall.DAFriendGangId = w.Player.Id;
        w.Hall.Results.Add(new RaceResult { Race = "Mayor", Week = 3, Field = { new Tally { Name = "Otis Kane", Ticket = "machine", Votes = 900, Won = true } } });
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, new List<Order>()))).World;
        Assert.True(loaded.Wards[0].OwnMan);
        Assert.Equal(man.Id, loaded.Wards[1].Candidates[w.Player.Id]);
        Assert.Equal(321, loaded.Wards[1].Polls[w.Player.Id]);
        Assert.Equal(w.Hall.DA, loaded.Hall.DA);
        Assert.Equal(w.Player.Id, loaded.Hall.DAFriendGangId);
        Assert.Equal("Otis Kane", Assert.Single(loaded.Hall.Results).Winner.Name);
    }

    [Fact]
    public void RivalsRunTheirOwnMenAndWorkThePolls()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 2, Size = CitySize.Medium });
        for (int i = 0; i < 3 * Content.WeeksPerYear; i++) sim.AdvanceWeek(playerAutopilot: true);
        var ev = sim.World.Events.Where(e => e.Kind == EventKind.Politics && e.GangId != sim.World.Player.Id).Select(e => e.Text).ToList();
        Assert.Contains(ev, t => t.Contains(" is running for alderman in "));
        Assert.Contains(ev, t => t.Contains(" worked the polls in "));
    }
}
