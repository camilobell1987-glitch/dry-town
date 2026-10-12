using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class NeighbourhoodTests
{
    private static (Simulation Sim, World W, Gang Me) Start(int gangs = 1, CitySize size = CitySize.Medium)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, Size = size, StartingGangs = gangs, CityEventsEnabled = false, DirectorEnabled = false });
        return (sim, sim.World, sim.World.Player);
    }

    private static Dictionary<int, List<Order>> Quiet(World w) => w.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>());

    /// <summary>A business in a ward of this people, and one in a ward of another.</summary>
    private static (Business Home, Business Away) Shops(World w, Ethnicity people)
    {
        var open = w.Businesses.Where(b => b.IsOpen && !b.IsProtected && !b.IsFront).ToList();
        return (open.First(b => w.Neighbourhood(b) == people), open.First(b => w.Neighbourhood(b) != people));
    }

    [Fact]
    public void EveryWardIsSomePeoplesNeighbourhood()
    {
        var (sim, w, _) = Start(size: CitySize.Large);
        var peoples = w.Wards.Select(w.Neighbourhood).ToList();
        Assert.DoesNotContain(Ethnicity.Unknown, peoples);
        Assert.Equal(Content.PeoplesFor(w.Wards.Count), peoples.Distinct().Count());
        // A ward named for one people's neighbourhood is theirs.
        Assert.All(w.Wards.Where(x => Content.WardNameOf.ContainsKey(x.Name)), x => Assert.Equal(Content.WardNameOf[x.Name], w.Neighbourhood(x)));
        // The shopkeepers are mostly the ward's own people.
        Assert.All(w.Businesses, b => Assert.Contains(b.Name[..b.Name.IndexOf("'s ")], Content.LastNamesOf[w.Neighbourhood(b)]));
        // And a saved city comes back with the same neighbourhoods.
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(peoples, loaded.Wards.Select(loaded.Neighbourhood).ToList());
    }

    [Fact]
    public void AnOutfitComesFromWhereItStarted()
    {
        var (_, w, _) = Start(gangs: 4);
        foreach (var g in w.LivingGangs)
        {
            var boss = w.HoodById(g.BossHoodId);
            Assert.Equal(w.Neighbourhood(w.HqOf(g).WardId), boss.Origin);
            Assert.Equal(boss.Origin, w.PeopleOf(g));
            Assert.Contains(World.Surname(boss.Name), Content.LastNamesOf[boss.Origin]);
            Assert.All(w.HoodsOf(g.Id), h => Assert.Contains(World.Surname(h.Name), Content.LastNamesOf[h.Origin]));
        }
        var son = w.NewRelative(w.Player);
        Assert.Equal(w.PeopleOf(w.Player), son.Origin);
    }

    [Fact]
    public void MostNewMenAreTheBosssPeopleAndStrangersStartLessLoyal()
    {
        var (_, w, me) = Start();
        var own = w.PeopleOf(me);
        // An outfit with no shops yet takes on only its own people.
        Assert.All(Enumerable.Range(0, 20), _ => Assert.Equal(own, w.NewHood(me.Id, bossQuality: false).Origin));
        // Once it works other neighbourhoods, some of its men come from there.
        foreach (var b in w.Businesses.Where(b => w.Neighbourhood(b) != own).Take(10)) b.ProtectorGangId = me.Id;
        var men = Enumerable.Range(0, 200).Select(_ => w.NewHood(me.Id, bossQuality: false)).ToList();
        Assert.InRange(men.Count(h => h.Origin == own), 115, 165);

        double Loyalty(Func<Ethnicity, bool> which) =>
            Enumerable.Range(0, 100).Select(_ => w.NewHood(me.Id, bossQuality: false, Content.Peoples.First(which))).Average(h => h.Loyalty);
        double gap = Loyalty(e => e == own) - Loyalty(e => e != own);
        Assert.InRange(gap, Content.StrangerLoyalty - 5, Content.StrangerLoyalty + 5);
    }

    [Fact]
    public void AMansOwnPeoplePayHimSooner()
    {
        var (_, w, me) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        var home = w.Businesses.First(b => b.IsOpen && !b.IsProtected && w.Neighbourhood(b) == man.Origin && Simulation.ExtortChance(w, me, man, b) is > 0.2 and < 0.8);
        var away = w.Businesses.First(b => w.Neighbourhood(b) != man.Origin);
        // The same shop, as if he were of the other ward's people.
        double here = Simulation.ExtortChance(w, me, man, home);
        man.Origin = w.Neighbourhood(away);
        double stranger = Simulation.ExtortChance(w, me, man, home);
        Assert.Equal(Content.HomeShakedown + Content.StrangerShakedown, here - stranger, 6);
        Assert.False(w.AtHome(man, home));
    }

    [Fact]
    public void ShopkeepersMindPayingTheirOwnPeopleLess()
    {
        var (sim, w, me) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        var (home, away) = Shops(w, w.PeopleOf(me));
        foreach (var b in new[] { home, away })
        {
            b.ProtectorGangId = me.Id;
            b.HandlerHoodId = man.Id;
            b.ProtectionRate = 20;
            b.Resentment = 50;
        }
        sim.AdvanceWeek(Quiet(w));
        Assert.Equal(Content.HomeResentment, away.Resentment - home.Resentment);
    }

    [Fact]
    public void AWardVotesForItsOwn()
    {
        var (_, w, me) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        var home = w.Wards.First(x => w.Neighbourhood(x) == man.Origin);
        var away = w.Wards.First(x => w.Neighbourhood(x) != man.Origin);
        Assert.Equal(Content.HomeCandidate, Politics.CandidateWeight(w, home, man) - Politics.CandidateWeight(w, away, man));
        Assert.Equal((int)(Content.PollsVotes(man) * Content.HomePolls), Politics.PollsVotes(w, home, man));
        Assert.Equal(Content.PollsVotes(man), Politics.PollsVotes(w, away, man));
    }

    [Fact]
    public void ADrifterIsFromTheWardHesFoundIn()
    {
        var (sim, w, me) = Start();
        var lot = w.Map.LotAt(w.Businesses[5].LotId);
        var drifter = w.NewPerson(PersonKind.Drifter, lot, 5);
        Assert.Equal(w.Neighbourhood(lot.WardId), drifter.Origin);
        Assert.Contains(World.Surname(drifter.Name), Content.LastNamesOf[drifter.Origin]);
        Assert.Contains($"{Content.Label(drifter.Origin)} drifter", new CommandShell(sim).Execute("street"));
    }

    [Fact]
    public void OldSavesWorkOutEachMansPeople()
    {
        var (sim, w, me) = Start(gangs: 3);
        var data = SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()));
        var origins = data.Hoods.ToDictionary(h => h.Id, h => h.Origin);
        foreach (var h in data.Hoods) h.Origin = Ethnicity.Unknown;
        foreach (var p in data.People) p.Origin = Ethnicity.Unknown;
        var loaded = SaveGame.Restore(data).World;
        Assert.All(loaded.Hoods, h => Assert.Equal(origins[h.Id], h.Origin));
        Assert.DoesNotContain(loaded.People, p => p.Origin == Ethnicity.Unknown);
    }

    [Fact]
    public void TheConsoleShowsWhoLivesWhere()
    {
        var (sim, w, me) = Start(gangs: 3);
        var shell = new CommandShell(sim);
        string text = shell.Execute("neighbourhoods");
        Assert.StartsWith($"Your outfit is {Content.Label(w.PeopleOf(me))}.", text);
        foreach (var ward in w.Wards) Assert.Contains(ward.Name, text);
        Assert.Contains(Content.Label(w.HoodById(me.BossHoodId).Origin), shell.Execute("hoods"));
        Assert.Contains("neighbourhoods", CommandShell.Help);
    }
}
