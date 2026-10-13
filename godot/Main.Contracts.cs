using System.Linq;
using DryTown.Core;

/// <summary>The city's contracts, on the City Hall tab, and set up for the screenshot.</summary>
public partial class Main
{
    private void ContractsSection()
    {
        if (W.Contracts.Count == 0) return;
        var p = W.Player;
        _hallPanel.AddChild(new Godot.HSeparator());
        AddLabel(_hallPanel, "City contracts", Palette.Player, 17);
        int votes = Contracts.Votes(W, p), needed = Contracts.VotesNeeded(W);
        int next = Contracts.NextLetting(W);
        AddLabel(_hallPanel, $"Each pays ${Contracts.Pay(W)} a week, clean, for a year, through a front of yours that can do the work. " +
            $"The aldermen let the free ones in week {next % Content.WeeksPerYear + 1}, to the outfit with the most of them in its pocket, if that's at least {needed}. " +
            $"You have {votes}{(W.Hall.FriendGangId == Me && !W.Hall.Reform ? $", counting the mayor's {Content.MayorVotes}" : "")}. If the papers find out who does the work, the city tears the contract up.",
            Palette.InkQuiet, 13, wrap: true);
        if (Contracts.CannotWin(W, p) is { } why) AddLabel(_hallPanel, why, Palette.Bad, 13, wrap: true);
        foreach (var c in W.Contracts)
        {
            var front = Contracts.FrontFor(W, p, c.Kind);
            string who = c.GangId == Me ? $"Yours, through {front?.Name ?? "nobody"}, until week {c.UntilWeek % Content.WeeksPerYear + 1} of {Content.StartYear + c.UntilWeek / Content.WeeksPerYear}."
                : c.GangId >= 0 ? $"{W.GangById(c.GangId).Name} has it."
                : "An honest firm has it.";
            string need = c.GangId == Me ? "" : front != null ? $" You'd do it through {front.Name}." : $" You'd need a {Contracts.NeedsLabel(c.Kind)} of your own.";
            AddLabel(_hallPanel, $"{Capital(Contracts.Label(c.Kind))}. {who}{need}", c.GangId >= 0 ? Palette.Gang(W, c.GangId) : Palette.Ink, 13, wrap: true);
        }
    }

    /// <summary>A line under a front's details when it does a city contract.</summary>
    private void ContractLine(Business b)
    {
        if (!b.IsFront || !World.Working(b)) return;
        var owner = W.GangById(b.OwnerGangId);
        var held = W.Contracts.Where(c => c.GangId == owner.Id && Contracts.FrontFor(W, owner, c.Kind)?.Id == b.Id).Select(c => Contracts.Label(c.Kind)).ToList();
        if (held.Count == 0) return;
        AddLabel(_businessPanel, $"{(owner.Id == Me ? "It does" : $"{owner.Name} does")} {string.Join(" and ", held)} for the city, at ${Contracts.Pay(W)} a week each, clean.",
            owner.Id == Me ? Palette.Player : Palette.Gang(W, owner.Id), 13, wrap: true);
    }

    /// <summary>For the screenshot: aldermen enough in your pocket, a garage and a warehouse of your own, and the contracts let.</summary>
    private void SetUpContracts()
    {
        var p = W.Player;
        p.Cash += 40_000;
        foreach (var ward in W.Wards.Where(x => !x.Reformer && !x.OwnMan).Take(Contracts.VotesNeeded(W))) ward.OwnerGangId = Me;
        foreach (var kind in new[] { BusinessKind.Garage, BusinessKind.Warehouse })
        {
            var b = W.Businesses.Where(x => x.Kind == kind && x.IsOpen && !x.IsFront && x.Racket == RacketKind.None && !W.IsWireRoom(x)).OrderBy(x => W.BlocksFromHq(p, x)).First();
            b.ProtectorGangId = Me;
            b.HandlerHoodId = W.AvailableHoodsOf(Me).Where(h => h.Id != p.BossHoodId).OrderBy(h => h.Id).First().Id;
            W.BuyFront(p, b);
        }
        foreach (var c in W.Contracts.Where(c => c.Kind is ContractKind.Garbage or ContractKind.Coal))
        {
            c.GangId = Me;
            c.UntilWeek = W.Week + Content.ContractWeeks;
        }
        RefreshAll();
    }
}
