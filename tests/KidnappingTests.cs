using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class KidnappingTests
{
    /// <summary>A quiet city with the player and one rival, both with money.</summary>
    private static (Simulation Sim, World W, Gang Me, Gang Them) Start()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = 2, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var me = w.Player;
        var them = w.LivingGangs.First(g => !g.IsPlayer);
        me.Cash = them.Cash = 20_000;
        return (sim, w, me, them);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };
    private static void Week(Simulation sim) => sim.AdvanceWeek(sim.World.LivingGangs.ToDictionary(g => g.Id, _ => new List<Order>()));

    private static Hood ManOf(World w, Gang g) => w.AvailableHoodsOf(g.Id).Where(h => h.Id != g.BossHoodId && h.Id != g.HeirHoodId && !h.Family).OrderBy(h => h.Id).First();

    [Fact]
    public void WhoCanBeSnatched()
    {
        var (_, w, me, them) = Start();
        var man = ManOf(w, them);
        Assert.Null(w.CannotKidnap(me, man));
        Assert.Equal(Content.Ransom, w.RansomFor(man));
        Assert.Equal("A boss never goes anywhere alone. Snatch one of his men.", w.CannotKidnap(me, w.HoodById(them.BossHoodId)));
        Assert.Equal("He's one of yours.", w.CannotKidnap(me, ManOf(w, me)));
        man.Family = true;
        Assert.Equal(Content.FamilyRansom, w.RansomFor(man));
    }

    [Fact]
    public void ASnatchedManIsHeldAndHisOutfitPays()
    {
        var (sim, w, me, them) = Start();
        var man = ManOf(w, them);
        var mine = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).OrderByDescending(h => h.Strength).First();
        string snatched = $"{mine.Name} of {me.Name} snatched {man.Name} of {them.Name} off the street.";
        for (int i = 0; i < 40 && !w.Events.Any(e => e.Text.StartsWith(snatched)); i++)
        {
            if (!mine.IsAvailable || !man.IsAvailable) return; // one of them was hurt or jailed trying
            sim.AdvanceWeek(Mine(w, new KidnapOrder(me.Id, mine.Id, man.Id)));
        }
        Assert.Contains(w.Events, e => e.Kind == EventKind.Kidnap && e.Text.StartsWith(snatched));
        // Paid on Sunday, got away, or still held.
        if (man.State == HoodState.Held)
        {
            Assert.False(man.IsAvailable);
            Assert.True(man.IsActive);
            long before = me.Cash;
            Assert.Equal($"{them.Name} paid {me.Name} ${Content.Ransom:N0}, and {man.Name} came home.", w.PayRansom(them, w.HostageOf(man)!));
            Assert.Equal(before + Content.Ransom, me.Cash);
        }
        Assert.Equal(HoodState.Free, man.State);
        Assert.Empty(w.Hostages);
        Assert.Contains(w.Events, e => e.Text == $"{them.Name} paid {me.Name} ${Content.Ransom:N0}, and {man.Name} came home." || e.Text.StartsWith($"{man.Name} got away"));
    }

    [Fact]
    public void TheMoneyComesOrHeDies()
    {
        var (sim, w, me, them) = Start();
        var man = ManOf(w, them);
        man.Stealth = 0;
        them.Cash = 0;
        var h = w.Snatch(me, man);
        for (int i = 0; i < Content.RansomWeeks + 1 && man.State == HoodState.Held; i++) { them.Cash = 0; Week(sim); }
        if (man.State == HoodState.Free) return; // he got away
        Assert.Equal(HoodState.Dead, man.State);
        Assert.Empty(w.Hostages);
        Assert.Contains(w.Events, e => e.Text == $"{them.Name} never paid for {man.Name}, and he was found in the river. Everyone knows who had him.");
    }

    [Fact]
    public void ARivalDecidesWhenThePlayerDoesntPay()
    {
        var (sim, w, me, them) = Start();
        var man = ManOf(w, me);
        man.Stealth = 0;
        me.Cash = 0;
        w.Snatch(them, man);
        var offer = Assert.Single(w.Offers, o => o.Kind == OfferKind.Ransom);
        Assert.Equal(man.Id, offer.HoodId);
        Assert.Contains($"wants ${Content.Ransom:N0} to give him back", w.Describe(offer));
        Assert.Equal($"You don't have ${Content.Ransom:N0}.", w.Answer(offer, true));
        Assert.Single(w.Offers, o => o.Kind == OfferKind.Ransom);
        for (int i = 0; i < Content.RansomWeeks + 1 && man.State == HoodState.Held; i++) { me.Cash = 0; Week(sim); }
        Assert.DoesNotContain(w.Offers, o => o.Kind == OfferKind.Ransom);
        if (them.Aggression >= Content.AiKills) Assert.Contains(man.State, new[] { HoodState.Dead, HoodState.Free });
        else Assert.Equal(HoodState.Free, man.State);
    }

    [Fact]
    public void ThePlayerPaysToGetHisManBack()
    {
        var (_, w, me, them) = Start();
        var man = ManOf(w, me);
        w.Snatch(them, man);
        var offer = w.Offers.Single(o => o.Kind == OfferKind.Ransom);
        Assert.Equal($"{me.Name} paid {them.Name} ${Content.Ransom:N0}, and {man.Name} came home.", w.Answer(offer, true));
        Assert.Equal(20_000 - Content.Ransom, me.Cash);
        Assert.Equal(20_000 + Content.Ransom, them.Cash);
        Assert.True(man.IsAvailable);
    }

    [Fact]
    public void AnAngryRivalSnatchesARichOutfitsMan()
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, StartingGangs = 5, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var me = w.Player;
        me.Cash = 20_000;
        var them = w.LivingGangs.Where(g => !g.IsPlayer).OrderByDescending(g => g.Aggression).First();
        Assert.True(them.Aggression >= 0.4);
        w.AddGrudge(them.Id, me.Id, 60);
        bool snatches = false;
        for (int i = 0; i < 400 && !snatches; i++) snatches = AiPlanner.Plan(w, them).Any(o => o is KidnapOrder k && w.HoodById(k.TargetHoodId).GangId == me.Id);
        Assert.True(snatches);
    }

    [Fact]
    public void TheConsoleSnatchesAndItIsSaved()
    {
        var (sim, w, me, them) = Start();
        var man = ManOf(w, them);
        var mine = ManOf(w, me);
        var shell = new CommandShell(sim);
        Assert.StartsWith("Nobody is holding anyone", shell.Execute("hostages"));
        Assert.Contains($"snatches {man.Name}", shell.Execute($"kidnap {mine.Id} {man.Id}"));
        Assert.Contains("kidnap <hood> <target hood>", CommandShell.Help);
        shell.Pending.Clear();
        w.Snatch(me, man);
        Assert.Contains($"{man.Name} of {them.Name}: you want ${Content.Ransom:N0}", shell.Execute("hostages"));

        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var h = loaded.HostageOf(loaded.HoodById(man.Id))!;
        Assert.Equal(me.Id, h.HeldByGangId);
        Assert.Equal(HoodState.Held, loaded.HoodById(man.Id).State);

        Assert.Equal($"{me.Name} let {man.Name} of {them.Name} go.\n", shell.Execute($"release {man.Id}"));
        Assert.True(man.IsAvailable);
    }
}
