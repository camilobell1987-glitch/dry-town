using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class GrievanceTests
{
    private static (Simulation Sim, World W, Gang Me, Hood Man) Start(ulong seed = 4)
    {
        var sim = Simulation.New(new WorldSettings { Seed = seed, StartingGangs = 1, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        var man = w.AvailableHoodsOf(w.Player.Id).First(h => h.Id != w.Player.BossHoodId);
        man.Intimidation = man.Muscle = man.Stealth = man.Brains = 10;
        return (sim, w, w.Player, man);
    }

    private static Dictionary<int, List<Order>> Mine(World w, params Order[] orders) => new() { [w.Player.Id] = orders.ToList() };

    private static int Meek(World w) => Enumerable.Range(1000, 500).First(id => w.NerveOf(id) == 1);

    private static LeanOrder Send(World w, Hood man, int citizen, Lean lean, int worth = 20) =>
        new(w.Player.Id, man.Id, citizen, w.HqOf(w.Player).Id, "Sadie Weiss", lean, worth);

    private static Character Plain => new(Background.None, new[] { Trait.Sharp });

    [Fact]
    public void AShakedownLeavesTheVictimAndTheirSpouseSoreAtYou()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        w.Introduce(who, w.HqOf(me).WardId, new[] { new Tie(9000, Kin.Spouse) }, Array.Empty<Secret>(), Plain);
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.Extort)));
        if (w.StandingOf(who) != Standing.Scared) return;
        var own = w.GrievanceOf(Party.Resident, who, Party.Gang, me.Id);
        Assert.NotNull(own);
        Assert.True(own!.Known);
        Assert.Equal("shook them down", own.Why);
        Assert.NotNull(w.GrievanceOf(Party.Resident, who, Party.Hood, man.Id));
        var spouse = w.GrievanceOf(Party.Resident, 9000, Party.Gang, me.Id);
        Assert.NotNull(spouse);
        Assert.Equal("shook down their husband or wife", Content.Over(spouse!));
        Assert.Equal("shook down their wife", Content.Over(spouse!, aboutWoman: true));
        Assert.True(w.BadBlood(me, who) > 0);
    }

    [Fact]
    public void AKillingSetsTheFamilyAndTheCopAgainstYouForGood()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        int ward = w.HqOf(me).WardId;
        // The beat cops come on duty at the end of the first week.
        sim.AdvanceWeek(Mine(w));
        w.Introduce(who, ward, new[] { new Tie(9001, Kin.Child), new Tie(9002, Kin.Sibling) }, Array.Empty<Secret>(), Plain);
        w.LearnTie(who, new Tie(9001, Kin.Child));
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.Kill)));
        if (w.StandingOf(who) != Standing.Dead) return;
        var son = w.GrievanceOf(Party.Resident, 9001, Party.Gang, me.Id)!;
        Assert.True(son.Known);
        Assert.Equal(Kin.Parent, son.AboutKin);
        Assert.Equal("killed their father", Content.Over(son, aboutWoman: false));
        Assert.True(son.Floor >= 40);
        Assert.False(w.GrievanceOf(Party.Resident, 9002, Party.Gang, me.Id)!.Known);
        Assert.NotNull(w.GrievanceOf(Party.Resident, 9001, Party.Hood, man.Id));
        // Nobody quite forgets.
        for (int i = 0; i < 80; i++) w.Ease(son, 1);
        Assert.Equal(son.Floor, son.Amount);
        if (w.BeatCopOf(ward) is { } cop) Assert.True(w.CopGrudge(me, cop.Id) > 0);
    }

    [Fact]
    public void AGrudgeMakesSomeoneHarderToBuyAndScare()
    {
        var (_, w, me, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), Plain);
        Assert.Equal("They've no quarrel with you.", w.CannotLean(me, 7000, Lean.Square));
        w.Bear(Party.Resident, 7000, Party.Gang, me.Id, 80, "shook them down");
        Assert.Null(w.CannotLean(me, 7000, Lean.Square));
        Assert.True(w.GrudgeOdds(me, 7000, Lean.Bribe, knownOnly: false) < 0);
        Assert.True(w.GrudgeOdds(me, 7000, Lean.Intimidate, knownOnly: false) < 0);
        Assert.Equal(0, w.GrudgeOdds(me, 7000, Lean.Kill, knownOnly: false));
        // The card only counts what you know of.
        Assert.Equal(0, w.GrudgeOdds(me, 7000, Lean.Bribe, knownOnly: true));
    }

    [Fact]
    public void HowHardSomeoneTakesItDependsOnTheirCharacter()
    {
        var (_, w, me, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), new Character(Background.None, new[] { Trait.Hothead, Trait.FamilyMan }));
        w.Introduce(7001, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), new Character(Background.None, new[] { Trait.Pious, Trait.Yellow }));
        int hot = w.Bear(Party.Resident, 7000, Party.Gang, me.Id, 30, "snatched", family: true)!.Amount;
        int meek = w.Bear(Party.Resident, 7001, Party.Gang, me.Id, 30, "snatched", family: true)!.Amount;
        Assert.True(hot > 60);
        Assert.True(meek < 20);
        Assert.True(Content.GrievanceFade(new[] { Trait.Pious }) > Content.GrievanceFade(new[] { Trait.HardBoiled }));
        Assert.True(Content.RevengeChance(90, new[] { Trait.Hothead }) > Content.RevengeChance(90, new[] { Trait.Yellow }));
        Assert.Equal(0, Content.RevengeChance(40, new[] { Trait.Hothead }));
    }

    [Fact]
    public void SquaringThingsEasesTheirGrudgeAndTheirFamilys()
    {
        var (sim, w, me, man) = Start();
        int who = Meek(w);
        w.Introduce(who, 1, new[] { new Tie(9000, Kin.Spouse) }, Array.Empty<Secret>(), new Character(Background.None, new[] { Trait.OnTheTake }));
        w.Bear(Party.Resident, who, Party.Gang, me.Id, 60, "shook them down", known: true);
        w.Bear(Party.Resident, 9000, Party.Gang, me.Id, 30, "shook down", aboutId: who);
        me.Cash = 5000;
        sim.AdvanceWeek(Mine(w, Send(w, man, who, Lean.Square)));
        if (!w.Events.Any(e => e.Text.Contains("squared things"))) return;
        Assert.True(w.BadBlood(me, who) <= 10);
        Assert.True((w.GrievanceOf(Party.Resident, 9000, Party.Gang, me.Id)?.Amount ?? 0) <= 10);
    }

    [Fact]
    public void ACopWithItInForYouWontTakeYourMoney()
    {
        var (sim, w, me, _) = Start();
        sim.AdvanceWeek(Mine(w));
        var cop = w.People.First(p => p.Kind == PersonKind.BeatCop);
        w.Bear(Party.Cop, cop.Id, Party.Gang, me.Id, 60, "a killing on his beat");
        me.Cash = 5000;
        sim.AdvanceWeek(Mine(w, new PayCopOrder(me.Id, cop.Id)));
        Assert.Contains(w.Events, e => e.Text.Contains("wouldn't touch the money"));
    }

    [Fact]
    public void ABadEnoughGrudgeBoilsOver()
    {
        var (sim, w, me, man) = Start();
        int ward = w.HqOf(me).WardId;
        w.Introduce(7000, ward, Array.Empty<Tie>(), Array.Empty<Secret>(), new Character(Background.None, new[] { Trait.Hothead, Trait.HardBoiled }));
        var g = w.Bear(Party.Resident, 7000, Party.Hood, man.Id, 100, "killed", ward, known: true)!;
        for (int i = 0; i < 20 && w.Grievances.Contains(g) && g.Amount > 70; i++) sim.AdvanceWeek(Mine(w));
        Assert.Contains(w.Events, e => e.Text.Contains($"has it in for {man.Name} went to the police about him"));
        Assert.True(g.Amount <= 70);
    }

    [Fact]
    public void AskingAroundTurnsUpBadBloodAndEnemiesTalk()
    {
        var (sim, w, me, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), Plain);
        w.Introduce(7001, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), Plain);
        w.Bear(Party.Resident, 7001, Party.Resident, 7000, 70, "fell out with them over money");
        Assert.True(w.HasEnemies(7000));
        Assert.False(w.HasEnemies(7001));
        Assert.Equal(1, w.LearnGrievances(7000, 1));
        Assert.True(w.GrievanceOf(Party.Resident, 7001, Party.Resident, 7000)!.Known);
        sim.AdvanceWeek(Mine(w, new AskOrder(me.Id, 7000, w.HqOf(me).Id, "Sadie Weiss")));
        Assert.Contains(w.Events, e => e.Text.Contains("Somebody who can't stand them was glad to talk"));
    }

    [Fact]
    public void GrudgesFadeAndTheDeadHoldNone()
    {
        var (sim, w, me, _) = Start();
        w.Introduce(7000, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), new Character(Background.None, new[] { Trait.Pious }));
        w.Introduce(7001, 1, Array.Empty<Tie>(), Array.Empty<Secret>(), Plain);
        var forgiving = w.Bear(Party.Resident, 7000, Party.Gang, me.Id, 20, "leaned on them")!;
        w.Bear(Party.Resident, 7001, Party.Gang, me.Id, 20, "leaned on them");
        w.Acquaintances[7001].Standing = Standing.Dead;
        int before = forgiving.Amount;
        sim.AdvanceWeek(Mine(w));
        Assert.Equal(before - 3, forgiving.Amount);
        Assert.Null(w.GrievanceOf(Party.Resident, 7001, Party.Gang, me.Id));
    }

    [Fact]
    public void GrudgesAreSaved()
    {
        var (sim, w, me, man) = Start();
        w.Bear(Party.Resident, 7000, Party.Hood, man.Id, 45, "killed", 2, known: true, floor: 30, aboutId: 7001, aboutKin: Kin.Child);
        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        var g = loaded.GrievanceOf(Party.Resident, 7000, Party.Hood, man.Id)!;
        Assert.Equal(45, g.Amount);
        Assert.Equal(30, g.Floor);
        Assert.Equal(7001, g.AboutId);
        Assert.Equal(Kin.Child, g.AboutKin);
        Assert.True(g.Known);
        Assert.Equal(2, g.WardId);
    }
}
