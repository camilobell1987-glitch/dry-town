using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class RatTests
{
    private static (Simulation Sim, World W, Gang Me, Hood Man) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed });
        var w = sim.World;
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        return (sim, w, w.Player, man);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    [Fact]
    public void AManWhoSignsTheDealNamesHisFriends()
    {
        var (sim, w, me, man) = Start();
        sim.Jail(man, 60, "with attempted arson");
        man.Loyalty = 0;
        man.Talking = 1;
        sim.AdvanceWeek(Mine(w));

        Assert.Equal(HoodState.Gone, man.State);
        Assert.Equal(RapKind.Turned, man.Record[^1].Kind);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Rat && e.Text.StartsWith($"{man.Name} turned state's evidence"));
        Assert.Contains(w.Hoods, h => h.GangId == me.Id && h.Case?.Charge == $"on {man.Name}'s testimony");
    }

    [Fact]
    public void LookingAfterHisFamilyKeepsHimQuiet()
    {
        var (sim, w, me, man) = Start();
        me.Cash = 10_000;
        sim.Jail(man, 60, "with attempted arson");
        // A long stretch takes more loyalty to sit out.
        Assert.True(Content.RatLine(man) > Content.RatLoyalty);
        man.Loyalty = Content.RatLine(man) - Content.FamilyLoyalty;
        man.Talking = 1;
        Assert.Null(w.CannotLookAfter(me, man));
        int price = Content.FamilyCare(man);
        Assert.Equal(price, w.LookAfter(me, man));
        Assert.Equal(Content.RatLine(man), man.Loyalty);
        Assert.NotNull(w.CannotLookAfter(me, man));

        sim.AdvanceWeek(Mine(w));
        Assert.Equal(0, man.Talking);
        Assert.DoesNotContain(man.Record, r => r.Kind == RapKind.Turned);
        Assert.Contains(w.Events, e => e.Text == $"{man.Name} sent the DA's men away. He's keeping his mouth shut.");
    }

    [Fact]
    public void AManInACellCanBeSeenTo()
    {
        var (sim, w, me, man) = Start();
        sim.Jail(man, 60, "with attempted arson");
        man.Loyalty = 0;
        man.Talking = 2;
        var hand = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        Assert.Null(w.CannotSeeTo(me, man));
        sim.AdvanceWeek(Mine(w, new SeenToOrder(me.Id, hand.Id, man.Id)));

        // Either he never talked, or the guards caught the knife and he signed at once.
        if (man.State == HoodState.Dead) Assert.Contains(w.Events, e => e.Text.Contains("hanged in his cell"));
        else
        {
            Assert.Equal(HoodState.Gone, man.State);
            Assert.Equal(HoodState.Jailed, hand.State);
        }
    }

    [Fact]
    public void ALoyalManIsNotARat()
    {
        var (_, w, me, man) = Start();
        Assert.NotNull(w.CannotSeeTo(me, man));
    }

    [Fact]
    public void AskingAroundFindsAnInformer()
    {
        var (sim, w, me, man) = Start();
        man.Informer = true;
        man.Loyalty = 0;
        var asker = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId && h.Id != man.Id);
        asker.Brains = 10;
        for (int i = 0; i < 10 && !man.Exposed && man.IsAvailable && asker.IsAvailable; i++)
            sim.AdvanceWeek(Mine(w, new HuntOrder(me.Id, asker.Id)));
        Assert.True(man.Exposed);
        Assert.Contains(man, w.ExposedIn(me.Id));
    }

    [Fact]
    public void AnInformerLeftAloneRunsToTheDistrictAttorney()
    {
        var (sim, w, me, man) = Start();
        man.Informer = true;
        man.Loyalty = 0;
        w.Week = 10;
        man.ExposedWeek = w.Week - Content.RunWeeks;
        sim.AdvanceWeek(Mine(w));
        Assert.Equal(HoodState.Gone, man.State);
        Assert.Equal(RapKind.Turned, man.Record[^1].Kind);
    }

    [Fact]
    public void AManInsideARivalPassesOnItsPlansAndHelpsHisOwnSide()
    {
        var (sim, w, me, man) = Start();
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        var gunman = w.AvailableHoodsOf(rival.Id).First(h => h.Id != rival.BossHoodId);
        var target = w.AvailableHoodsOf(me.Id).First(h => h.Id != man.Id);
        var rivalTarget = w.AvailableHoodsOf(rival.Id).First(h => h.Id != rival.BossHoodId);
        double before = Simulation.HitChance(w, target, Array.Empty<Hood>(), rivalTarget);
        double against = Simulation.HitChance(w, gunman, Array.Empty<Hood>(), target);

        man.State = HoodState.Inside;
        man.InsideGangId = rival.Id;
        Assert.True(w.HasManInside(me.Id, rival.Id));
        Assert.DoesNotContain(man, w.HoodsOf(me.Id));
        Assert.Contains(man, w.PlantsOf(me.Id));
        Assert.True(Simulation.HitChance(w, target, Array.Empty<Hood>(), rivalTarget) > before);
        Assert.True(Simulation.HitChance(w, gunman, Array.Empty<Hood>(), target) < against);

        var orders = Mine(w);
        orders[rival.Id] = new List<Order> { new HitOrder(rival.Id, gunman.Id, target.Id) };
        sim.AdvanceWeek(orders);
        Assert.Contains(w.Events, e => e.GangId == me.Id && e.Text.StartsWith($"Word from {man.Name}: {rival.Name} will go after {target.Name} this week"));
        // Only his own side hears it.
        Assert.DoesNotContain(w.Events, e => e.Text.StartsWith("Word from ") && Reports.IsHeadline(e));
    }

    [Fact]
    public void APlantedManComesHomeWhenCalled()
    {
        var (sim, w, me, man) = Start();
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        man.Stealth = 10;
        Assert.Null(w.CannotPlant(me, man, rival));
        Assert.NotNull(w.CannotPlant(me, w.HoodById(me.BossHoodId), rival));
        sim.AdvanceWeek(Mine(w, new PlantOrder(me.Id, man.Id, rival.Id)));
        if (man.State != HoodState.Inside)
        {
            Assert.Contains(w.Events, e => e.Text.StartsWith($"{rival.Name} wouldn't take {man.Name} on"));
            return;
        }
        Assert.Equal(rival.Id, man.InsideGangId);
        w.Recall(me, man);
        Assert.Equal(HoodState.Free, man.State);
        Assert.Equal(-1, man.InsideGangId);
    }

    [Fact]
    public void RatsAndPlantsAreSaved()
    {
        var (sim, w, me, man) = Start();
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        man.State = HoodState.Inside;
        man.InsideGangId = rival.Id;
        man.InsideSinceWeek = 3;
        var other = w.AvailableHoodsOf(me.Id).First(h => h.Id != me.BossHoodId);
        other.Informer = true;
        other.ExposedWeek = 2;
        me.TalkWeek = 5;
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var m = loaded.HoodById(man.Id);
        Assert.Equal(HoodState.Inside, m.State);
        Assert.Equal(rival.Id, m.InsideGangId);
        Assert.Equal(3, m.InsideSinceWeek);
        Assert.True(loaded.HoodById(other.Id).Informer);
        Assert.Equal(2, loaded.HoodById(other.Id).ExposedWeek);
        Assert.Equal(5, loaded.Player.TalkWeek);
    }
}
