using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class CityDirectoryTests
{
    /// <summary>A directory with every land lot a tenement of ten, and twenty living over each shop.</summary>
    private static (World W, CityDirectory D) Start(ulong seed = 4)
    {
        var w = Simulation.New(new WorldSettings { Seed = seed, CityEventsEnabled = false, DirectorEnabled = false }).World;
        bool Tenement(Lot l) => l.Terrain == Terrain.Land && l.Use == LotUse.Empty;
        int People(Lot l) => l.Terrain != Terrain.Land ? 0 : l.Use == LotUse.Business ? 20 : Tenement(l) ? 10 : 0;
        return (w, new CityDirectory(w, People, Tenement));
    }

    [Fact]
    public void EveryoneIsTheSamePersonEveryTimeYouAsk()
    {
        var (w, d) = Start();
        var (_, again) = Start();
        int id = d.IdOf(1, 1, 3);
        Assert.Equal(d.Get(id) with { Ties = again.Get(id).Ties, Owns = again.Get(id).Owns }, again.Get(id));
        Assert.Equal(d.Get(id).Ties, again.Get(id).Ties);
        Assert.True(d.Exists(id));
        Assert.Equal((1, 1, 3), d.Where(id));
        Assert.False(d.Exists(d.IdOf(1, 1, d.CountOn(1, 1))));
    }

    [Fact]
    public void ShopkeepersLiveOverTheShopAndCarryItsName()
    {
        var (w, d) = Start();
        var shop = w.Businesses.First(b => b.Name.Contains("'s ") && b.Kind != BusinessKind.Court);
        var owner = d.Get(d.OwnerOf(shop.Id));
        Assert.Equal(shop.Id, owner.OwnsBusinessId);
        Assert.Equal(shop.LotId, owner.HomeLotId);
        Assert.EndsWith(shop.Name[..shop.Name.IndexOf("'s ")], owner.Name);
        Assert.StartsWith("Owns ", owner.Job);
    }

    [Fact]
    public void EveryTenementHasALandlordWhoLivesInTheWard()
    {
        var (w, d) = Start();
        var tenements = w.Map.Lots.Where(l => l.Terrain == Terrain.Land && l.Use == LotUse.Empty).ToList();
        Assert.All(tenements, l => Assert.True(d.LandlordOf(l.Id) >= 0));
        var landlord = d.Get(d.LandlordOf(tenements[0].Id));
        Assert.Contains(tenements[0].Id, landlord.Owns);
        Assert.Equal(tenements[0].WardId, w.Map.LotAt(landlord.HomeLotId).WardId);
        Assert.StartsWith("Landlord", landlord.Job);
        // A tenant knows who he pays.
        var tenant = d.Get(d.IdOf(0, 0, 0));
        Assert.Equal(d.LandlordOf(tenant.HomeLotId), tenant.LandlordId);
    }

    [Fact]
    public void ACityOfStrangersHasBothSexesAllAgesAndAMixOfPeoples()
    {
        var (w, d) = Start();
        var people = Enumerable.Range(0, Math.Min(400, d.CountOn(0, 0))).Select(n => d.Get(d.IdOf(0, 0, n))).ToList();
        Assert.InRange(people.Count(p => p.Woman), people.Count / 4, people.Count * 3 / 4);
        Assert.All(people, p => Assert.InRange(p.Age, 1, 90));
        Assert.Contains(people, p => p.Age < 14);
        Assert.All(people, p => Assert.False(string.IsNullOrEmpty(p.Job)));
        Assert.True(people.Select(p => p.Name).Distinct().Count() > people.Count / 2);
    }

    [Fact]
    public void FamiliesLiveTogetherAndShareANameAndPeople()
    {
        var (w, d) = Start();
        var families = Enumerable.Range(0, d.CountOn(2, 2)).Select(n => d.Get(d.IdOf(2, 2, n))).Where(c => c.Ties.Any(t => t.Kin == Kin.Spouse)).Take(20).ToList();
        Assert.NotEmpty(families);
        foreach (var c in families)
        {
            var spouse = d.Get(c.Ties.First(t => t.Kin == Kin.Spouse).Id);
            Assert.Equal(c.HomeLotId, spouse.HomeLotId);
            Assert.Equal(c.Origin, spouse.Origin);
            Assert.NotEqual(c.Woman, spouse.Woman);
            Assert.Equal(c.Name.Split(' ')[^1], spouse.Name.Split(' ')[^1]);
            Assert.Contains(spouse.Ties, t => t.Id == c.Id && t.Kin == Kin.Spouse);
        }
    }

    [Fact]
    public void ParentsAndChildrenPointAtEachOther()
    {
        var (w, d) = Start();
        var child = Enumerable.Range(0, d.Population).Select(n => d.IdOf(n % w.Map.BlocksX, n / w.Map.BlocksX % w.Map.BlocksY, n / (w.Map.BlocksX * w.Map.BlocksY)))
            .Where(d.Exists).Select(d.Get).First(c => c.Ties.Any(t => t.Kin == Kin.Parent));
        var parent = d.Get(child.Ties.First(t => t.Kin == Kin.Parent).Id);
        Assert.True(parent.Age >= child.Age + 18);
        Assert.Contains(parent.Ties, t => t.Id == child.Id && t.Kin == Kin.Child);
        if (child.Age < 14) Assert.Contains("school", child.Job);
    }

    [Fact]
    public void ShopkeepersFamiliesCarryTheShopsName()
    {
        var (w, d) = Start();
        var shop = w.Businesses.Where(b => b.Name.Contains("'s ") && b.Kind != BusinessKind.Court)
            .First(b => d.Get(d.OwnerOf(b.Id)).Ties.Any(t => t.Kin != Kin.Employer));
        var owner = d.Get(d.OwnerOf(shop.Id));
        var kin = d.Get(owner.Ties.First(t => t.Kin != Kin.Employer).Id);
        Assert.EndsWith(shop.Name[..shop.Name.IndexOf("'s ")], kin.Name);
    }
}
