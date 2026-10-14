using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class SmallGangTests
{
    private static Simulation New(ulong seed = 3, CitySize size = CitySize.Medium) =>
        Simulation.New(new WorldSettings { Seed = seed, Size = size, CityEventsEnabled = false, DirectorEnabled = false });

    private static Dictionary<int, List<Order>> Only(World w, params Order[] orders) =>
        w.LivingGangs.ToDictionary(g => g.Id, g => g.Id == w.Player.Id ? orders.ToList() : new List<Order>());

    /// <summary>The player, made strong enough and rich enough to deal with any small gang.</summary>
    private static Gang Strong(World w)
    {
        var me = w.Player;
        me.Cash = 50_000;
        foreach (var h in w.HoodsOf(me.Id)) { h.Intimidation = 10; h.Muscle = 10; h.Weapon = Weapon.TommyGun; }
        for (int i = 0; i < 6; i++) { var h = w.NewHood(me.Id, bossQuality: true); h.Intimidation = 10; h.Muscle = 10; h.Weapon = Weapon.TommyGun; }
        return me;
    }

    [Fact]
    public void EachSmallGangStartsOnItsOwnCornerWithTheShopsOfItsBlock()
    {
        var w = New(size: CitySize.Metropolis).World;
        Assert.Equal(4, w.Outfits.Count());
        var small = w.SmallGangs.ToList();
        Assert.Equal(20, small.Count);
        Assert.Contains(small, g => g.Kind == GangKind.Hoodlums);
        Assert.Contains(small, g => g.Kind == GangKind.StreetGang);
        Assert.Contains(small, g => g.Kind == GangKind.Sharks);
        foreach (var g in small)
        {
            Assert.Single(w.BlocksOf(g));
            Assert.InRange(w.HoodsOf(g.Id).Count(), 2, 3);
            int block = w.Map.BlockOf(w.HqOf(g));
            Assert.All(w.TurfOf(g.Id), b => Assert.Equal(block, w.Map.BlockOf(w.LotOf(b))));
            // Hoodlums and street gangs take the name of the street their corner faces.
            if (g.Kind != GangKind.Sharks) Assert.Contains(w.Map.StreetOf(w.HqOf(g)), g.Name);
        }
        // Their corners are spread about the city.
        Assert.Equal(small.Count, small.Select(g => w.Map.BlockOf(w.HqOf(g))).Distinct().Count());
    }

    [Theory]
    [InlineData(GangKind.Hoodlums, 1)]
    [InlineData(GangKind.StreetGang, 2)]
    [InlineData(GangKind.Sharks, 4)]
    public void ASmallGangNeverHoldsMoreBlocksThanItsKindAllows(GangKind kind, int most)
    {
        var w = New(size: CitySize.Metropolis).World;
        var g = w.SmallGangs.First(x => x.Kind == kind);
        Assert.Equal(most, Content.MaxBlocks(kind));
        // Give it shops on block after block, each next to one it holds, while it can still reach them.
        var held = w.BlocksOf(g);
        for (int round = 0; round < 8; round++)
        {
            var next = w.Businesses.Where(b => b.ProtectorGangId != g.Id && !held.Contains(w.Map.BlockOf(w.LotOf(b))))
                .Where(b => w.CanReach(g, b)).OrderBy(b => b.Id).FirstOrDefault();
            if (next == null) break;
            next.ProtectorGangId = g.Id;
            held = w.BlocksOf(g);
        }
        Assert.Equal(most, held.Count);
        // Every shop off its blocks is out of reach, and an outfit can reach them all.
        Assert.All(w.Businesses.Where(b => !held.Contains(w.Map.BlockOf(w.LotOf(b)))), b => Assert.False(w.CanReach(g, b)));
        Assert.All(w.Businesses, b => Assert.True(w.CanReach(w.Player, b)));
    }

    [Fact]
    public void ASmallGangLeftAloneKeepsToItsBlocks()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 6, Size = CitySize.Metropolis, CityEventsEnabled = false });
        for (int i = 0; i < 52; i++) sim.AdvanceWeek(playerAutopilot: true);
        var w = sim.World;
        Assert.All(w.SmallGangs, g => Assert.InRange(w.BlocksOf(g).Count, 1, Content.MaxBlocks(g.Kind)));
        Assert.True(w.Outfits.Count() <= Content.Outfits);
    }

    [Fact]
    public void AnOutfitCanBuyASmallGangIn()
    {
        var sim = New();
        var w = sim.World;
        var me = Strong(w);
        var small = w.SmallGangs.OrderBy(g => g.Id).First();
        var men = w.HoodsOf(small.Id).Select(h => h.Id).ToList();
        var shops = w.TurfOf(small.Id).Select(b => b.Id).ToList();
        int price = w.AbsorbPrice(small);
        Assert.Null(w.CannotAbsorb(me, small));
        var envoy = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        for (int i = 0; i < 10 && small.Alive; i++) sim.AdvanceWeek(Only(w, new AbsorbOrder(me.Id, envoy.Id, small.Id)));

        Assert.False(small.Alive);
        Assert.All(men, id => Assert.Equal(me.Id, w.HoodById(id).GangId));
        Assert.All(shops, id => Assert.Equal(me.Id, w.BusinessById(id).ProtectorGangId));
        Assert.Contains(w.Events, e => e.Text.StartsWith($"{small.Name} came in with {me.Name} for ${price:N0}"));
        Assert.Equal(LotUse.Empty, w.Map.LotAt(small.HqLotId).Use);
    }

    [Fact]
    public void ASmallGangWontJoinAnOutfitNoBiggerThanItself()
    {
        var w = New().World;
        var me = w.Player;
        var small = w.SmallGangs.First();
        foreach (var h in w.HoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).ToList()) h.State = HoodState.Gone;
        foreach (var h in w.HoodsOf(small.Id)) { h.Intimidation = 10; h.Muscle = 10; }
        me.Cash = 50_000;
        Assert.Contains("big enough", w.CannotAbsorb(me, small));
        me.Cash = 0;
        Assert.Contains("want $", w.CannotAbsorb(me, small));
    }

    [Fact]
    public void AnOutfitCanHireASmallGangsManAway()
    {
        var sim = New();
        var w = sim.World;
        var me = Strong(w);
        var small = w.SmallGangs.First(g => w.HoodsOf(g.Id).Count() >= 2);
        var man = w.HoodsOf(small.Id).First(h => h.Id != small.BossHoodId);
        Assert.Contains("runs", w.CannotPoach(me, w.HoodById(small.BossHoodId)));
        man.Loyalty = 0;
        // Leave aside the sort he is: a two-timer is easier to buy, a stand-up guy harder.
        man.Traits.Clear();
        Assert.Equal(0.9, w.PoachChance(me, man), 2);
        for (int i = 0; i < 10 && man.GangId == small.Id; i++)
        {
            man.Loyalty = 0;
            sim.AdvanceWeek(Only(w, new PoachOrder(me.Id, man.Id)));
        }
        Assert.Equal(me.Id, man.GangId);
        Assert.Contains(w.Events, e => e.Text.StartsWith($"{man.Name} walked out on {small.Name} for $"));
        Assert.DoesNotContain(w.TurfOf(small.Id), b => b.HandlerHoodId == man.Id);
    }

    [Fact]
    public void AnOutfitCanWipeOutASmallGang()
    {
        var sim = New();
        var w = sim.World;
        var me = Strong(w);
        var small = w.SmallGangs.First();
        var shops = w.TurfOf(small.Id).Select(b => b.Id).ToList();
        var team = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).OrderByDescending(h => h.Strength).Take(3).ToList();
        Assert.True(Simulation.WipeOutChance(w, team[0], team.Skip(1), small) > 0.8);
        for (int i = 0; i < 10 && small.Alive; i++)
        {
            var free = team.Where(h => h.IsAvailable).ToList();
            sim.AdvanceWeek(Only(w, new WipeOutOrder(me.Id, free[0].Id, small.Id, free.Skip(1).Select(h => h.Id).ToArray())));
        }
        Assert.False(small.Alive);
        Assert.Empty(w.HoodsOf(small.Id));
        Assert.All(shops, id => Assert.NotEqual(small.Id, w.BusinessById(id).ProtectorGangId));
        Assert.Contains(w.Events, e => e.Text.StartsWith($"{me.Name} wiped out {small.Name}"));
    }

    [Fact]
    public void NobodySignsPactsWithASmallGangOrGoesToWarWithOne()
    {
        var w = New().World;
        var small = w.SmallGangs.First();
        Assert.Contains("small gang", w.Propose(w.Player, small, PactKind.Truce));
        Assert.Contains("small gang", w.CannotDeclare(w.Player, small));
        Assert.Contains("small gang", w.Demand(w.Player, small, 100));
    }

    [Fact]
    public void ACityHasNoMoreThanFourOutfits()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 2, Size = CitySize.Medium });
        for (int i = 0; i < 3 * Content.WeeksPerYear; i++)
        {
            sim.AdvanceWeek(playerAutopilot: true);
            Assert.True(sim.World.Outfits.Count() <= Content.Outfits);
        }
        Assert.NotEmpty(sim.World.SmallGangs);
    }

    [Fact]
    public void SmallGangsAreKeptWhenTheCityIsSaved()
    {
        var sim = New();
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(sim.World.SmallGangs.Select(g => (g.Id, g.Kind)), loaded.SmallGangs.Select(g => (g.Id, g.Kind)));
    }
}
