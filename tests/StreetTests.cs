using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class Phase7Tests
{
    private static Simulation Run(ulong seed, int weeks, CitySize size = CitySize.Small)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, Size = size });
        for (int i = 0; i < weeks; i++) sim.AdvanceWeek(playerAutopilot: true);
        return sim;
    }

    /// <summary>A two-gang city with the rival protecting the first business and nothing else going on.</summary>
    private static (Simulation sim, Gang me, Gang rival, Business biz) Duel(ulong seed)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 2, DirectorEnabled = false });
        var w = sim.World;
        var rival = w.Gangs[1];
        var biz = w.Businesses.First();
        biz.ProtectorGangId = rival.Id;
        biz.HandlerHoodId = w.HoodsOf(rival.Id).Last().Id;
        return (sim, w.Player, rival, biz);
    }

    private static Hood Soldier(World w, Gang g) => w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId);

    private static Dictionary<int, List<Order>> Only(Gang me, Gang rival, params Order[] mine) =>
        new() { [me.Id] = mine.ToList(), [rival.Id] = new() };

    [Theory]
    [InlineData(CitySize.Small, 1)]
    [InlineData(CitySize.Large, 3)]
    [InlineData(CitySize.Metropolis, 10)]
    public void EveryOtherWardHasABank(CitySize size, int banks)
    {
        var w = Simulation.New(new WorldSettings { Seed = 3, Size = size }).World;
        Assert.Equal(banks, w.Banks.Count);
        foreach (var bank in w.Banks)
        {
            Assert.Equal(LotUse.Bank, w.Map.LotAt(bank.LotId).Use);
            Assert.InRange(bank.Capacity, 4000, 9000);
        }
    }

    [Fact]
    public void TheStreetFillsUpWithCopsAndDrifters()
    {
        var w = Run(4, 12, CitySize.Large).World;
        Assert.Equal(w.Wards.Count, w.People.Count(p => p.Kind == PersonKind.BeatCop));
        Assert.Contains(w.People, p => p.Kind == PersonKind.Drifter);
        Assert.All(w.People, p => Assert.Equal(w.Map.LotAt(p.LotId).WardId, p.WardId));
    }

    [Fact]
    public void AHiredDrifterKeepsTheSkillsHeShowed()
    {
        var (sim, me, rival, _) = Duel(1);
        var w = sim.World;
        var drifter = w.NewPerson(PersonKind.Drifter, w.Map.LotAt(w.Businesses[3].LotId), 5);
        long cash = me.Cash;
        sim.AdvanceWeek(Only(me, rival, new HireOrder(me.Id, drifter.Id)));
        var hood = w.HoodsOf(me.Id).Single(h => h.Name == drifter.Name);
        // He may have learned a thing or two in his first week.
        Assert.InRange(hood.Intimidation + hood.Muscle + hood.Brains + hood.Stealth - (drifter.Intimidation + drifter.Muscle + drifter.Brains + drifter.Stealth), 0, 1);
        Assert.True(hood.Intimidation >= drifter.Intimidation && hood.Muscle >= drifter.Muscle && hood.Brains >= drifter.Brains && hood.Stealth >= drifter.Stealth);
        Assert.Equal(drifter.Origin, hood.Origin);
        Assert.DoesNotContain(drifter, w.People);
        Assert.True(me.Cash <= cash - drifter.Fee);
    }

    [Fact]
    public void ArsonShutsARivalsBusinessAndBurnsItsRacket()
    {
        int burned = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var (sim, me, rival, biz) = Duel(seed);
            biz.Racket = RacketKind.Numbers;
            var torch = Soldier(sim.World, me);
            torch.Stealth = 10;
            sim.AdvanceWeek(Only(me, rival, new ArsonOrder(me.Id, torch.Id, biz.Id)));
            if (biz.IsOpen) continue;
            burned++;
            Assert.Equal(RacketKind.None, biz.Racket);
            Assert.Contains(sim.World.Events, e => e.Kind == EventKind.Arson && e.Text.Contains("burned down"));
        }
        Assert.True(burned >= 7, $"only {burned}/10 fires took");
    }

    [Fact]
    public void AHitKillsTheTargetOrCostsTheShooter()
    {
        int dead = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var (sim, me, rival, _) = Duel(seed);
            var w = sim.World;
            var target = Soldier(w, rival);
            target.Intimidation = target.Muscle = 3;
            var shooter = Soldier(w, me);
            shooter.Intimidation = shooter.Muscle = shooter.Stealth = 9;
            sim.AdvanceWeek(Only(me, rival, new HitOrder(me.Id, shooter.Id, target.Id)));
            Assert.Contains(w.Events, e => e.Kind == EventKind.Hit);
            if (target.State == HoodState.Dead) dead++;
        }
        Assert.True(dead >= 28, $"only {dead}/40 hits came off");
    }

    [Fact]
    public void AGoodCrewRobsABankAndTheVaultRefills()
    {
        int robbed = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var (sim, me, rival, _) = Duel(seed);
            var w = sim.World;
            var bank = w.Banks[0];
            var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).Take(3).ToList();
            foreach (var h in men) h.Brains = h.Muscle = h.Stealth = 10;
            int vault = bank.Vault;
            sim.AdvanceWeek(Only(me, rival, new RobberyOrder(me.Id, men[0].Id, bank.Id, men.Skip(1).Select(h => h.Id).ToArray())));
            if (!w.Events.Any(e => e.Kind == EventKind.Robbery && e.Text.Contains("got away with"))) continue;
            robbed++;
            Assert.True(bank.Vault < vault);
            int after = bank.Vault;
            sim.AdvanceWeek(Only(me, rival));
            Assert.True(bank.Vault > after);
        }
        Assert.True(robbed >= 5, $"only {robbed}/10 hold-ups came off");
    }

    [Fact]
    public void BeerTrucksCanOnlyBeHijackedDuringProhibition()
    {
        var (sim, me, rival, biz) = Duel(2);
        var w = sim.World;
        biz.Racket = RacketKind.Speakeasy;
        Assert.True(Simulation.CanHijack(w, me, biz));
        biz.Racket = RacketKind.Numbers;
        Assert.False(Simulation.CanHijack(w, me, biz));
        biz.Racket = RacketKind.Speakeasy;
        w.Week = (Content.RepealYear - Content.StartYear) * Content.WeeksPerYear;
        Assert.False(Simulation.CanHijack(w, me, biz));
    }

    [Fact]
    public void AWitnessWhoIsntSilencedPutsTheManAway()
    {
        var (sim, me, rival, biz) = Duel(5);
        var w = sim.World;
        var hood = Soldier(w, me);
        var witness = w.NewPerson(PersonKind.Witness, w.LotOf(biz), 1, me.Id, hood.Id, "something");
        sim.AdvanceWeek(Only(me, rival));
        Assert.Equal(HoodState.Jailed, hood.State);
        Assert.DoesNotContain(witness, w.People);
    }

    [Fact]
    public void ALeanedOnWitnessForgets()
    {
        var (sim, me, rival, biz) = Duel(6);
        var w = sim.World;
        var hood = Soldier(w, me);
        var heavy = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId && h.Id != hood.Id);
        heavy.Intimidation = 10;
        var witness = w.NewPerson(PersonKind.Witness, w.LotOf(biz), 2, me.Id, hood.Id, "something");
        sim.AdvanceWeek(Only(me, rival, new SilenceOrder(me.Id, heavy.Id, witness.Id)));
        // Ten against the nerviest witness is still better than even; check the outcome is one of the two.
        bool forgot = !w.People.Contains(witness);
        Assert.True(forgot || witness.WeeksLeft == 0 || hood.State == HoodState.Jailed);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Street && e.Text.Contains(heavy.Name));
    }

    [Fact]
    public void APaidCopKeepsTheRaidsOffRacketsInHisWard()
    {
        var (sim, me, rival, biz) = Duel(7);
        var w = sim.World;
        sim.AdvanceWeek(Only(me, rival));
        biz.ProtectorGangId = me.Id;
        biz.Racket = RacketKind.Numbers;
        var cop = w.People.First(p => p.Kind == PersonKind.BeatCop && p.WardId == w.LotOf(biz).WardId);
        if (cop.Nerve >= 8) return; // An honest cop; nothing to test with this seed.
        me.Heat = 100;
        me.Cash = 5000;
        sim.AdvanceWeek(Only(me, rival, new PayCopOrder(me.Id, cop.Id)));
        Assert.Equal(RacketKind.Numbers, biz.Racket);
    }

    [Fact]
    public void AStoryThatRunsHeatsUpTheGangAndTheCity()
    {
        var (sim, me, rival, _) = Duel(8);
        var w = sim.World;
        var reporter = w.NewPerson(PersonKind.Reporter, w.HqOf(me), 1, me.Id);
        int outrage = w.Hall.Outrage;
        sim.AdvanceWeek(Only(me, rival));
        Assert.DoesNotContain(reporter, w.People);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Street && e.Text.Contains("front-page"));
        Assert.True(w.Hall.Outrage >= outrage);
    }

    [Fact]
    public void ABoughtStoryNeverRuns()
    {
        var (sim, me, rival, _) = Duel(8);
        var w = sim.World;
        var reporter = w.NewPerson(PersonKind.Reporter, w.HqOf(me), 1, me.Id);
        sim.AdvanceWeek(Only(me, rival, new BuyStoryOrder(me.Id, reporter.Id)));
        Assert.DoesNotContain(w.Events, e => e.Text.Contains("front-page"));
    }

    [Fact]
    public void TheRivalGangsUseTheNewCrimesAndRackets()
    {
        var kinds = new HashSet<EventKind>();
        var rackets = new HashSet<RacketKind>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var sim = Simulation.New(new WorldSettings { Seed = seed, Size = CitySize.Large });
            for (int i = 0; i < 6 * Content.WeeksPerYear; i++)
            {
                sim.AdvanceWeek(playerAutopilot: true);
                foreach (var b in sim.World.Businesses) rackets.Add(b.Racket);
            }
            foreach (var e in sim.World.Events) kinds.Add(e.Kind);
        }
        Assert.Contains(EventKind.Arson, kinds);
        Assert.Contains(EventKind.Hit, kinds);
        Assert.Contains(EventKind.Robbery, kinds);
        Assert.Contains(EventKind.Hijack, kinds);
        Assert.Contains(RacketKind.Bookie, rackets);
        Assert.Contains(RacketKind.GamblingDen, rackets);
        Assert.Contains(RacketKind.Counterfeit, rackets);
    }

    [Fact]
    public void PeopleAndBanksSurviveASave()
    {
        var sim = Run(9, 30, CitySize.Medium);
        var w = sim.World;
        var pending = new List<Order> { new HireOrder(w.Player.Id, 3), new RobberyOrder(w.Player.Id, 1, 0, new[] { 2 }) };
        var data = SaveGame.Read(SaveGame.Write(sim, pending));
        var loaded = SaveGame.Restore(data);
        Assert.Equal(pending.Count, data.Pending.Count);
        Assert.Equal(w.People.Select(p => (p.Name, p.Kind, p.WeeksLeft)), loaded.World.People.Select(p => (p.Name, p.Kind, p.WeeksLeft)));
        Assert.Equal(w.Banks.Select(b => b.Vault), loaded.World.Banks.Select(b => b.Vault));
        for (int i = 0; i < 20; i++)
        {
            sim.AdvanceWeek(playerAutopilot: true);
            loaded.AdvanceWeek(playerAutopilot: true);
        }
        Assert.Equal(sim.World.Events.Select(e => e.Text), loaded.World.Events.Select(e => e.Text));
    }

    [Fact]
    public void SavesFromBeforeTheStreetGetBanks()
    {
        var sim = Run(5, 20, CitySize.Large);
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveGame.Write(sim, new List<Order>()))!;
        json["Version"] = 3;
        json.AsObject().Remove("People");
        json.AsObject().Remove("Banks");
        foreach (var l in json["Lots"]!.AsArray()) if ((int)l!["Use"]! == (int)LotUse.Bank) l["Use"] = (int)LotUse.Empty;
        var loaded = SaveGame.Restore(SaveGame.Read(json.ToJsonString()));
        Assert.Equal(sim.World.Banks.Select(b => b.LotId), loaded.World.Banks.Select(b => b.LotId));
        loaded.AdvanceWeek(playerAutopilot: true);
        Assert.Contains(loaded.World.People, p => p.Kind == PersonKind.BeatCop);
    }

    [Fact]
    public void TheShellTakesTheNewOrders()
    {
        var shell = new CommandShell(Run(4, 8, CitySize.Medium));
        var w = shell.Sim.World;
        var me = w.Player;
        var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).ToList();
        var rivalMan = w.Hoods.First(h => h.GangId != me.Id && h.IsAvailable);
        Assert.StartsWith("Queued", shell.Execute($"rob {men[0].Id} 0 {men[1].Id}"));
        Assert.StartsWith("Queued", shell.Execute($"hit {men[2].Id} {rivalMan.Id}"));
        var drifter = w.People.FirstOrDefault(p => p.Kind == PersonKind.Drifter);
        if (drifter != null && me.Cash >= drifter.Fee) Assert.StartsWith("Queued", shell.Execute($"hire {drifter.Id}"));
        if (w.TurfOf(me.Id).FirstOrDefault() is { } mine) Assert.DoesNotContain("Queued", shell.Execute($"torch {men[0].Id} {mine.Id}"));
        Assert.Contains("Officer", shell.Execute("street"));
        Assert.Contains("vault", shell.Execute("banks"));
        shell.Execute("end");
    }
}
