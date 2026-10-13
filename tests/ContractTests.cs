using DryTown.Core;
using Xunit;

namespace DryTown.Core.Tests;

public class ContractTests
{
    /// <summary>A quiet medium city at the turn of a quarter, where the player has money and nobody else to bother him.</summary>
    private static (Simulation Sim, World W, Gang Me) Start(int gangs = 1)
    {
        var sim = Simulation.New(new WorldSettings { Seed = 4, Size = CitySize.Medium, StartingGangs = gangs, CityEventsEnabled = false, DirectorEnabled = false });
        var w = sim.World;
        w.Week = Content.ContractLetting;
        w.Player.Cash = 50_000;
        w.Hall.Outrage = 0;
        w.Hall.Reform = false;
        return (sim, w, w.Player);
    }

    /// <summary>Put enough aldermen on the gang's payroll to win a contract.</summary>
    private static void Aldermen(World w, Gang g)
    {
        foreach (var ward in w.Wards) { ward.Reformer = false; ward.OwnMan = false; ward.OwnerGangId = -1; }
        foreach (var ward in w.Wards.Take(Contracts.VotesNeeded(w))) ward.OwnerGangId = g.Id;
    }

    /// <summary>A business of this kind the gang owns outright and watches.</summary>
    private static Business Front(World w, Gang g, BusinessKind kind)
    {
        var b = w.Businesses.First(x => x.Kind == kind && x.IsOpen && !x.IsFront && !x.IsProtected);
        b.ProtectorGangId = g.Id;
        b.HandlerHoodId = w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId).Id;
        Assert.True(w.BuyFront(g, b) > 0);
        return b;
    }

    private static Contract Of(World w, ContractKind k) => w.Contracts.First(c => c.Kind == k);

    [Fact]
    public void TheAldermenLetAContractToTheOutfitThatOwnsThem()
    {
        var (_, w, me) = Start();
        Assert.Equal(4, w.Contracts.Count);
        var garage = Front(w, me, BusinessKind.Garage);
        foreach (var ward in w.Wards) ward.OwnerGangId = -1;
        w.Wards[0].OwnerGangId = me.Id;
        Contracts.Step(w);
        Assert.All(w.Contracts, c => Assert.Equal(-1, c.GangId));

        Aldermen(w, me);
        Contracts.Step(w);
        Assert.Equal(me.Id, Of(w, ContractKind.Garbage).GangId);
        // A garage can pave the streets too, but there's no warehouse for the coal or laundry for the linens.
        Assert.Equal(me.Id, Of(w, ContractKind.Paving).GangId);
        Assert.Equal(-1, Of(w, ContractKind.Coal).GangId);
        Assert.Equal(-1, Of(w, ContractKind.Linens).GangId);
        Assert.Equal(w.Week + Content.ContractWeeks, Of(w, ContractKind.Garbage).UntilWeek);
        Assert.Contains(w.Events, e => e.Text == $"The aldermen gave {me.Name}'s {garage.Name} the contract for hauling the city's garbage: ${Contracts.Pay(w)} a week, clean, for a year.");
    }

    [Fact]
    public void NobodyHandsTheCitysMoneyToTheOutfitThatRunsAThirdOfIt()
    {
        var (_, w, me) = Start();
        Front(w, me, BusinessKind.Garage);
        Aldermen(w, me);
        var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).ToList();
        foreach (var b in w.Businesses.Where(b => !b.IsProtected).Take(w.Businesses.Count / 3 + 1))
        {
            b.ProtectorGangId = me.Id;
            b.HandlerHoodId = men[0].Id;
        }
        Assert.True(w.TopDog(me));
        Assert.EndsWith("handing it the city's money too.", Contracts.CannotWin(w, me));
        Contracts.Step(w);
        Assert.Equal(-1, Of(w, ContractKind.Garbage).GangId);
    }

    [Fact]
    public void TheMayorsFriendHasTheMostVotes()
    {
        var (_, w, me) = Start(gangs: 2);
        var rival = w.LivingGangs.First(g => !g.IsPlayer);
        Front(w, me, BusinessKind.Warehouse);
        rival.Cash = 50_000;
        Front(w, rival, BusinessKind.Warehouse);
        Aldermen(w, rival);
        foreach (var ward in w.Wards.Where(x => x.OwnerGangId < 0).Take(Contracts.VotesNeeded(w))) ward.OwnerGangId = me.Id;
        Assert.Equal(Contracts.Votes(w, me), Contracts.Votes(w, rival));
        w.Hall.FriendGangId = me.Id;
        Assert.Equal(Contracts.Votes(w, rival) + Content.MayorVotes, Contracts.Votes(w, me));
        Contracts.Step(w);
        Assert.Equal(me.Id, Of(w, ContractKind.Coal).GangId);
    }

    [Fact]
    public void TheContractPaysCleanUntilTheFrontIsGone()
    {
        var (_, w, me) = Start();
        var garage = Front(w, me, BusinessKind.Garage);
        Aldermen(w, me);
        Contracts.Step(w);
        w.Week++;
        long cash = me.Cash, clean = me.CleanCash;
        Contracts.Step(w);
        Assert.Equal(cash + 2 * Contracts.Pay(w), me.Cash);
        Assert.Equal(clean + 2 * Contracts.Pay(w), me.CleanCash);

        garage.ProtectorGangId = -1;
        Contracts.Step(w);
        Assert.Equal(-1, Of(w, ContractKind.Garbage).GangId);
        Assert.Contains(w.Events, e => e.Text == $"With no garage of its own to do the work, {me.Name} lost the contract for hauling the city's garbage.");
    }

    [Fact]
    public void AContractRunsOutAfterAYear()
    {
        var (_, w, me) = Start();
        Front(w, me, BusinessKind.Laundry);
        Aldermen(w, me);
        Contracts.Step(w);
        var linens = Of(w, ContractKind.Linens);
        Assert.Equal(me.Id, linens.GangId);
        foreach (var ward in w.Wards) ward.OwnerGangId = -1;
        w.Week = linens.UntilWeek;
        Contracts.Step(w);
        Assert.Equal(-1, linens.GangId);
        Assert.Contains(w.Events, e => e.Text == $"{me.Name}'s contract for linens for the city hospital ran out.");
    }

    [Fact]
    public void ThePapersTearUpAContract()
    {
        var (_, w, me) = Start();
        Front(w, me, BusinessKind.Garage);
        Aldermen(w, me);
        Contracts.Step(w);
        w.Hall.Reform = true;
        w.Hall.Outrage = 100;
        me.Heat = 0;
        for (int i = 0; i < 500 && Of(w, ContractKind.Garbage).GangId == me.Id; i++)
        {
            w.Week++;
            if (w.Week % Content.ContractLetting == 0) w.Week++;
            Contracts.Step(w);
        }
        Assert.Contains(w.Events, e => e.Text.EndsWith($"and {me.Name} behind it. The city tore up the contract."));
        Assert.True(me.Heat >= Content.GraftHeat);
    }

    [Fact]
    public void ARivalWithTheAldermenBuysAPlaceToDoTheWork()
    {
        var (_, w, _) = Start(gangs: 2);
        var g = w.LivingGangs.First(x => !x.IsPlayer);
        g.Cash = 200_000;
        g.Heat = 0;
        g.CleanCash = g.Cash;
        Aldermen(w, g);
        var laundry = w.Businesses.First(x => x.Kind == BusinessKind.Laundry && x.IsOpen && !x.IsProtected && !x.IsFront);
        laundry.ProtectorGangId = g.Id;
        laundry.HandlerHoodId = w.AvailableHoodsOf(g.Id).First(h => h.Id != g.BossHoodId).Id;
        var orders = AiPlanner.Plan(w, g);
        var kinds = Enum.GetValues<ContractKind>().SelectMany(Contracts.Needs).ToHashSet();
        Assert.Contains(orders, o => o is FrontOrder f && kinds.Contains(w.BusinessById(f.BusinessId).Kind));
    }

    [Fact]
    public void TheConsoleShowsTheContractsAndTheyAreSaved()
    {
        var (sim, w, me) = Start();
        Front(w, me, BusinessKind.Garage);
        Aldermen(w, me);
        Contracts.Step(w);
        var shell = new CommandShell(sim);
        string text = shell.Execute("contracts");
        Assert.Contains($"you have {Contracts.VotesNeeded(w)}.", text);
        Assert.Contains("hauling the city's garbage", text);
        Assert.Contains("needs a laundry you own", text);
        Assert.Contains("contracts", CommandShell.Help);

        var loaded = SaveGame.Restore(SaveGame.Read(SaveGame.Write(sim, Array.Empty<Order>()))).World;
        Assert.Equal(me.Id, loaded.Contracts.First(c => c.Kind == ContractKind.Garbage).GangId);
        Assert.Equal(Of(w, ContractKind.Garbage).UntilWeek, loaded.Contracts.First(c => c.Kind == ContractKind.Garbage).UntilWeek);
    }
}
