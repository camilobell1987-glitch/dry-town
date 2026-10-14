using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class CharacterTests
{
    private static (Simulation Sim, World W, Gang Me, Hood Man) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 1, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        man.Intimidation = man.Muscle = man.Stealth = man.Brains = 10;
        return (sim, w, w.Player, man);
    }

    private static (CityDirectory D, List<Citizen> People) Directory(ulong seed = 4)
    {
        var w = Simulation.New(new WorldSettings { Seed = seed, CityEventsEnabled = false, DirectorEnabled = false }).World;
        bool Tenement(Lot l) => l.Terrain == Terrain.Land && l.Use == LotUse.Empty;
        int People(Lot l) => l.Terrain != Terrain.Land ? 0 : l.Use == LotUse.Business ? 20 : Tenement(l) ? 10 : 0;
        var d = new CityDirectory(w, People, Tenement);
        var people = new List<Citizen>();
        for (int x = 0; x < Math.Min(4, w.Map.BlocksX); x++)
            for (int y = 0; y < Math.Min(4, w.Map.BlocksY); y++)
                people.AddRange(Enumerable.Range(0, d.CountOn(x, y)).Select(n => d.Get(d.IdOf(x, y, n))));
        return (d, people);
    }

    private static Character Of(Background b, params Trait[] traits) => new(b, traits);

    [Fact]
    public void EveryoneHasTwoOrThreeTraitsThatDontClashAndChildrenOne()
    {
        var (d, people) = Directory();
        Assert.NotEmpty(people);
        foreach (var p in people)
        {
            var c = d.CharacterOf(p.Id);
            if (p.Age < 16) Assert.Single(c.Traits);
            else Assert.InRange(c.Traits.Count, 2, 3);
            Assert.Equal(c.Traits.Count, c.Traits.Distinct().Count());
            Assert.DoesNotContain(c.Traits, a => c.Traits.Any(b => Content.Clash(a, b)));
        }
    }

    [Fact]
    public void ACharacterIsFixedByTheSeed()
    {
        var (a, people) = Directory();
        var (b, _) = Directory();
        foreach (var p in people.Take(80))
        {
            Assert.Equal(a.CharacterOf(p.Id).Background, b.CharacterOf(p.Id).Background);
            Assert.Equal(a.CharacterOf(p.Id).Traits, b.CharacterOf(p.Id).Traits);
        }
    }

    [Fact]
    public void BackgroundsFitAgeAndSex()
    {
        var (d, people) = Directory();
        Assert.Contains(people, p => d.CharacterOf(p.Id).Background != Background.None);
        foreach (var p in people)
        {
            var b = d.CharacterOf(p.Id).Background;
            if (p.Age < 16) Assert.Equal(Background.None, b);
            if (b == Background.Doughboy) { Assert.False(p.Woman); Assert.InRange(p.Age, 20, 48); }
            if (b is Background.Jailbird or Background.UnionMan or Background.PreachersKid) Assert.True(p.Age >= 20);
        }
    }

    [Fact]
    public void BackgroundsMakeSomeTraitsLikelier()
    {
        double Share(Background b, Trait t) => Content.TraitChance(b, t) / Enum.GetValues<Trait>().Sum(x => Content.TraitChance(b, x));
        Assert.True(Share(Background.Doughboy, Trait.HardBoiled) > 2 * Share(Background.None, Trait.HardBoiled));
        Assert.True(Share(Background.Doughboy, Trait.Yellow) < Share(Background.None, Trait.Yellow));
        Assert.True(Share(Background.PreachersKid, Trait.Pious) > 2 * Share(Background.None, Trait.Pious));
        Assert.Equal(0, Content.TraitChance(Background.Jailbird, Trait.SquareShooter));
    }

    [Fact]
    public void PiousMenDontTakeBribesAndYellowMenFold()
    {
        var (_, w, _, man) = Start();
        int nerve = 5;
        double plain = Simulation.LeanChance(man, Lean.Bribe, nerve);
        Assert.True(Simulation.LeanChance(man, Lean.Bribe, nerve, traits: Content.TraitOdds(Trait.Pious, Lean.Bribe)) < plain);
        Assert.True(Simulation.LeanChance(man, Lean.Bribe, nerve, traits: Content.TraitOdds(Trait.OnTheTake, Lean.Bribe)) > plain);
        double scare = Simulation.LeanChance(man, Lean.Intimidate, nerve);
        Assert.True(Simulation.LeanChance(man, Lean.Intimidate, nerve, traits: Content.TraitOdds(Trait.Yellow, Lean.Intimidate)) > scare);
        Assert.True(Simulation.LeanChance(man, Lean.Intimidate, nerve, traits: Content.TraitOdds(Trait.HardBoiled, Lean.Intimidate)) < scare);
    }

    [Fact]
    public void TheCardOnlyCountsTraitsYouKnow()
    {
        var (_, w, _, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), Of(Background.PreachersKid, Trait.Pious, Trait.Yellow));
        Assert.Equal(0, w.TraitOdds(7000, Lean.Bribe, knownOnly: true));
        Assert.True(w.TraitOdds(7000, Lean.Bribe, knownOnly: false) < 0);
        Assert.True(w.LearnTrait(7000, Trait.Pious));
        Assert.False(w.LearnTrait(7000, Trait.Pious));
        Assert.False(w.LearnTrait(7000, Trait.Clam));
        Assert.Equal(new[] { Trait.Pious }, w.KnownTraitsOf(7000));
        Assert.Equal(new[] { Trait.Yellow }, w.Acquaintances[7000].UnknownTraits);
        Assert.True(w.TraitOdds(7000, Lean.Bribe, knownOnly: true) < 0);
        Assert.Equal(1, w.LearnTraits(7000, 1));
        Assert.Empty(w.Acquaintances[7000].UnknownTraits);
    }

    [Fact]
    public void BlabbermouthsTalkAndClamsDont()
    {
        var (_, w, _, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), Of(Background.None, Trait.Blabbermouth, Trait.Souse));
        w.Introduce(7001, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), Of(Background.Jailbird, Trait.Clam, Trait.HardBoiled));
        Assert.True(w.TalkOf(7000) > 0);
        Assert.True(w.TalkOf(7001) < 0);
        Assert.True(Content.AsInformant(new[] { Trait.Blabbermouth }) > Content.AsInformant(new[] { Trait.Clam }));
        Assert.True(Content.FailHeat(new[] { Trait.SquareShooter }, Background.None) > Content.FailHeat(new[] { Trait.Clam }, Background.Jailbird));
    }

    [Fact]
    public void ABlabbermouthInYourPocketCountsDouble()
    {
        var (_, w, me, _) = Start();
        w.Introduce(500, 2, Array.Empty<Tie>(), Array.Empty<Secret>(), Of(Background.None, Trait.Blabbermouth, Trait.Sap));
        var a = w.Acquaintances[500];
        a.Standing = Standing.Bought;
        a.GangId = me.Id;
        Assert.Equal(2, w.InPocket(me, 2));
    }

    [Fact]
    public void PiousPeopleHideLessAndSousesDrink()
    {
        var (d, people) = Directory();
        var grown = people.Where(p => p.Age >= 18).ToList();
        double Hiding(Func<Character, bool> f)
        {
            var group = grown.Where(p => f(d.CharacterOf(p.Id))).ToList();
            return group.Count == 0 ? 0 : group.Count(p => d.SecretsOf(p.Id).Count > 0) / (double)group.Count;
        }
        Assert.True(Hiding(c => c.Traits.Contains(Trait.Pious)) < Hiding(c => !c.Traits.Contains(Trait.Pious)));
        Assert.All(grown.Where(p => d.CharacterOf(p.Id).Traits.Contains(Trait.Souse)),
            p => Assert.Contains(d.SecretsOf(p.Id), s => s.Vice == Vice.Drink));
    }

    [Fact]
    public void CharacterIsSaved()
    {
        var (sim, w, _, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), Of(Background.Doughboy, Trait.HardBoiled, Trait.Hothead));
        w.LearnTrait(7000, Trait.Hothead);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var a = loaded.Acquaintances[7000];
        Assert.Equal(Background.Doughboy, a.Background);
        Assert.Equal(new[] { Trait.HardBoiled, Trait.Hothead }, a.Traits);
        Assert.Equal(new[] { Trait.Hothead }, a.KnownTraits);
    }
}
