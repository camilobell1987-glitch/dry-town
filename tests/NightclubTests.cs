using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class NightclubTests
{
    private const int RepealWeek = (Content.RepealYear - Content.StartYear) * Content.WeeksPerYear;

    /// <summary>A quiet city in the given week, where the player has money and nobody else to bother him.</summary>
    private static (Simulation Sim, World W, Gang Me) Start(int week = 0, int gangs = 1, ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = gangs, CityEventsEnabled = false, DirectorEnabled = false, StartWeek = week });
        var me = sim.World.Player;
        me.Cash = 20_000;
        me.Heat = 0;
        return (sim, sim.World, me);
    }

    /// <summary>Have the gang protect a business nobody else does, with one of its men handling it.</summary>
    private static Business Protect(World w, Gang g, Business b)
    {
        b.ProtectorGangId = g.Id;
        b.HandlerHoodId = w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId).Id;
        b.ShutWeeks = 0;
        b.Racket = RacketKind.None;
        return b;
    }

    private static Business Free(World w, BusinessKind kind, int skip = 0) =>
        w.Businesses.Where(b => b.Kind == kind && !b.IsProtected && !b.IsFront && b.Id != w.WireBusinessId).OrderBy(b => b.Id).Skip(skip).First();

    /// <summary>A club in a hotel the gang owns.</summary>
    private static Business Club(World w, Gang g)
    {
        var hotel = Protect(w, g, Free(w, BusinessKind.Hotel));
        Assert.True(w.BuyFront(g, hotel) > 0);
        hotel.Racket = RacketKind.Nightclub;
        return hotel;
    }

    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    [Fact]
    public void AClubNeedsAPlaceYouOwnAndThereIsOneToAnOutfit()
    {
        var (_, w, me) = Start();
        var hotel = Protect(w, me, Free(w, BusinessKind.Hotel));
        Assert.Equal("A nightclub needs a hotel you own outright.", w.CannotOpenRacket(me, hotel, RacketKind.Nightclub));
        Assert.True(w.BuyFront(me, hotel) > 0);
        // A club takes over whatever was in the back room.
        hotel.Racket = RacketKind.GamblingDen;
        Assert.Null(w.CannotOpenRacket(me, hotel, RacketKind.Nightclub));
        hotel.Racket = RacketKind.Nightclub;
        Assert.Equal(hotel, w.ClubOf(me.Id));
        Assert.Equal("It's a nightclub already.", w.CannotOpenRacket(me, hotel, RacketKind.Nightclub));

        var diner = Protect(w, me, Free(w, BusinessKind.Diner));
        w.BuyFront(me, diner);
        Assert.Equal($"{me.Name} has a club at {hotel.Name} already.", w.CannotOpenRacket(me, diner, RacketKind.Nightclub));
        var grocer = Protect(w, me, Free(w, BusinessKind.Grocer));
        w.BuyFront(me, grocer);
        Assert.EndsWith("can't hide that.", w.CannotOpenRacket(me, grocer, RacketKind.Nightclub));
    }

    [Fact]
    public void TheCityComesToBeSeenAndTheBossWithThem()
    {
        var (sim, w, me) = Start();
        me.Respect = 0;
        Week(sim);
        double without = me.Respect;
        Club(w, me);
        me.Respect = 0;
        Week(sim);
        Assert.True(me.Respect - without >= Content.ClubRespect * 0.9, $"{me.Respect} against {without}");
    }

    [Fact]
    public void AldermenComeCheaperAndTheMoneyGoesFurther()
    {
        var (_, w, me) = Start();
        var ward = w.Wards.First(x => !x.Reformer && x.OwnerGangId < 0);
        long price = Politics.PayoffCost(w, ward, me);
        int retainer = Politics.RetainerFor(w, ward, me);
        Assert.Equal(1, w.CampaignWeight(me.Id));
        Club(w, me);
        Assert.Equal((long)(price * (1 - Content.ClubPayoff)), Politics.PayoffCost(w, ward, me));
        Assert.Equal((int)(Politics.Retainer(w, ward) * (1 - Content.ClubPayoff)), Politics.RetainerFor(w, ward, me));
        Assert.True(Politics.RetainerFor(w, ward, me) < retainer);
        Assert.Equal(1 + Content.ClubCampaign, w.CampaignWeight(me.Id));
    }

    [Fact]
    public void TheClubShutsWhenItsOwnersLoseThePlace()
    {
        var (sim, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var hotel = Club(w, me);
        hotel.ProtectorGangId = rival.Id;
        hotel.HandlerHoodId = w.AvailableHoodsOf(rival.Id).First(h => h.Id != rival.BossHoodId).Id;
        Week(sim);
        Assert.Equal(RacketKind.None, hotel.Racket);
        Assert.Null(w.ClubOf(me.Id));
        Assert.Equal($"With {me.Name} gone from it, the nightclub at {hotel.Name} shut its doors.", w.Events.LastOrDefault(e => e.Kind == EventKind.Lapsed)?.Text);
    }

    [Fact]
    public void AfterRepealTheClubKeepsHonestBooks()
    {
        var (sim, w, me) = Start(week: RepealWeek + 1);
        Club(w, me);
        Week(sim);
        Assert.True(w.LastLedger[me.Id].Legit >= Content.Rackets[RacketKind.Nightclub].WeeklyIncome / 2);
    }

    [Fact]
    public void ARichRivalWithAHotelOfItsOwnOpensAClub()
    {
        var (_, w, _) = Start(gangs: 2, seed: 1);
        var g = w.LivingGangs.First(x => !x.IsPlayer);
        g.Cash = 20_000;
        g.Heat = 0;
        var hotel = Protect(w, g, Free(w, BusinessKind.Hotel));
        Assert.True(w.BuyFront(g, hotel) > 0);
        Assert.Contains(AiPlanner.Plan(w, g), o => o is RacketOrder r && r.BusinessId == hotel.Id && r.Racket == RacketKind.Nightclub);
    }

    [Fact]
    public void TheConsoleOpensAClub()
    {
        var (sim, w, me) = Start();
        var hotel = Protect(w, me, Free(w, BusinessKind.Hotel));
        w.BuyFront(me, hotel);
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        man.Brains = 10;
        var shell = new CommandShell(sim);
        Assert.Contains("nightclub", CommandShell.Help);
        shell.Execute($"racket {man.Id} {hotel.Id} nightclub");
        Assert.Contains(shell.Pending, o => o is RacketOrder { Racket: RacketKind.Nightclub });
        shell.EndWeek();
        Assert.Equal(RacketKind.Nightclub, hotel.Racket);
        Assert.Contains($"Your nightclub is at {hotel.Name}", shell.Execute("wards"));
        Assert.Contains("your nightclub", shell.Execute("fronts"));
    }
}
