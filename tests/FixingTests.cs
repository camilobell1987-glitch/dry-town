using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class FixingTests
{
    /// <summary>The week of the year these land on: a fight night, a race day, and a week in the winter with nothing on.</summary>
    private const int FightWeek = 3, RaceWeek = 21, Winter = 5;

    /// <summary>A quiet city in the given week, where the player and a rival each run two bookies.</summary>
    private static (Simulation Sim, World W, Gang Me, Gang Them) Start(int week = FightWeek)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = 2, CityEventsEnabled = false, DirectorEnabled = false, StartWeek = week });
        var w = sim.World;
        var me = w.Player;
        var them = w.LivingGangs.First(g => !g.IsPlayer);
        me.Cash = them.Cash = 20_000;
        me.Heat = them.Heat = 0;
        foreach (var g in new[] { me, them })
            foreach (var b in w.Businesses.Where(b => b.IsOpen && !b.IsProtected && Content.RacketsFor(b.Kind).Contains(RacketKind.Bookie)).Take(2))
            {
                b.ProtectorGangId = g.Id;
                b.Racket = RacketKind.Bookie;
            }
        return (sim, w, me, them);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    [Fact]
    public void TheCardFollowsTheCalendar()
    {
        Assert.Equal(Bout.Fight, Start(FightWeek).W.Card);
        Assert.Equal(Bout.Race, Start(RaceWeek).W.Card);
        var (_, w, me, _) = Start(Winter);
        Assert.Equal(Bout.None, w.Card);
        Assert.StartsWith("There's nothing on this week", w.CannotFix(me));
        Assert.StartsWith("There's nothing on this week", Reports.Card(w, me.Id));
    }

    [Fact]
    public void AFixPaysAndTheOtherBookiesPayOut()
    {
        var (sim, w, me, them) = Start();
        Assert.Equal(Content.FightLayoff + 2 * Content.FightPerBookie, w.FixPays(me));
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        man.Brains = man.Intimidation = 10;
        Assert.Equal(0.9, World.FixChance(man), 6);
        string? said = null;
        for (int i = 0; i < 40 && said?.Contains(" cleared $") != true; i++)
        {
            w.Week += Content.WeeksPerYear;
            me.Cash = them.Cash = 20_000;
            me.Heat = 0;
            said = w.Fix(me, man, out _);
        }
        Assert.Equal($"{man.Name} of {me.Name} fixed {w.CardLabel()}. {w.CardName()} went down in the fourth, and {me.Name} cleared ${w.FixPays(me):N0} on it.", said);
        Assert.Equal(20_000 - Content.FightFix + w.FixPays(me), me.Cash);
        Assert.Equal(20_000 - 2 * Content.FightLoss, them.Cash);
        Assert.Equal("You've already got to him.", w.CannotFix(me));
        Assert.Equal("Somebody else got to him first.", w.CannotFix(them));
    }

    [Fact]
    public void TheCommissionCanCallTheBetsOff()
    {
        var (_, w, me, them) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        man.Brains = man.Intimidation = 10;
        me.Heat = 100;
        Assert.Equal(Content.FixFound + 100 * Content.FixFoundPerHeat, w.FixFoundChance(me), 6);
        string? said = null;
        for (int i = 0; i < 60 && said?.Contains("called all bets off") != true; i++)
        {
            w.Week += Content.WeeksPerYear;
            me.Cash = 20_000;
            me.Heat = 100;
            said = w.Fix(me, man, out _);
        }
        Assert.EndsWith("but the boxing commission smelled it and called all bets off.", said);
        Assert.Equal(20_000 - Content.FightFix, me.Cash);
        Assert.Equal(Content.GrudgeFix, w.GrudgeOf(them.Id, me.Id));
    }

    [Fact]
    public void AManWhoCantBuyHimMayBeTurnedIn()
    {
        var (sim, w, me, _) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        man.Brains = man.Intimidation = 0;
        for (int i = 0; i < 200 && man.IsAvailable; i++)
        {
            sim.AdvanceWeek(Mine(w, new FixOrder(me.Id, man.Id)));
            w.Week = FightWeek;
            me.Cash = 20_000;
            me.Heat = 0;
        }
        Assert.False(man.IsAvailable);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Fix && e.Text.StartsWith($"{man.Name} of {me.Name} offered to fix ") && e.Text.EndsWith(" wouldn't hear of it and went to the police."));
    }

    [Fact]
    public void TheWireTellsItsBookiesFirst()
    {
        var (_, w, me, them) = Start((Content.RepealYear - Content.StartYear) * Content.WeeksPerYear + RaceWeek - 1);
        Assert.Equal(Bout.Race, w.Card);
        var room = w.BusinessById(w.WireBusinessId);
        room.ProtectorGangId = them.Id;
        Assert.Equal(Content.RaceLayoff + w.Bookies(me.Id) * Content.RacePerBookie, w.FixPays(me));
        room.ProtectorGangId = me.Id;
        room.Racket = RacketKind.Bookie;
        Assert.Equal(me.Id, w.WireGangId);
        Assert.Equal((int)((Content.RaceLayoff + w.Bookies(me.Id) * Content.RacePerBookie) * (1 + Content.WireFix)), w.FixPays(me));
        Assert.Equal(Content.RaceFix, World.FixPrice(w.Card));
    }

    [Fact]
    public void ARivalWithABookieFixesTheCard()
    {
        var (_, w, _, them) = Start();
        bool fixes = false;
        for (int i = 0; i < 100 && !fixes; i++) fixes = AiPlanner.Plan(w, them).Any(o => o is FixOrder);
        Assert.True(fixes);
    }

    [Fact]
    public void TheConsoleFixesTheCard()
    {
        var (sim, w, me, _) = Start();
        var man = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        var shell = new CommandShell(sim);
        Assert.Contains($"This week: {w.CardLabel()}.", shell.Execute("card"));
        Assert.Contains("fixes Friday night's fights", shell.Execute($"fix {man.Id}"));
        Assert.Equal("Someone is already on his way to see him.\n", shell.Execute($"fix {man.Id}"));
        Assert.Contains("card (this week's fights or races)", CommandShell.Help);
    }
}
