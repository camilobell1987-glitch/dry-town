using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The docks and the unions in the side panel: whose local a shop's workers belong to, strikes, and taking the local over.</summary>
public partial class Main
{
    /// <summary>A line under a shop's details: its workers' local, who runs it, and any pickets outside.</summary>
    private void UnionLine(Business b)
    {
        if (W.LocalOf(b) is not { } local) return;
        string who = local.OwnerGangId == Me ? "you run it" : local.OwnerGangId >= 0 ? $"{W.GangById(local.OwnerGangId).Name} runs it" : "its members run it";
        AddLabel(_businessPanel, $"Its workers belong to {local.Name}, and {who}.", local.OwnerGangId >= 0 ? Palette.Gang(W, local.OwnerGangId) : Palette.InkQuiet, 13, wrap: true);
        if (W.StrikeAt(b) is { } strike)
            AddLabel(_businessPanel, $"On strike for {W.Week - strike.StrikeWeek + 1} weeks: nothing comes in until the owner pays {W.GangById(strike.OwnerGangId).Name}.", Palette.Bad, 13, wrap: true);
    }

    /// <summary>The local behind a shop: call a strike there or call it off, or send men to take the local over.</summary>
    private void UnionActions(Business b)
    {
        if (W.LocalOf(b) is not { } local) return;
        var p = W.Player;
        _businessPanel.AddChild(new HSeparator());
        AddLabel(_businessPanel, World.Capital(local.Name), Palette.Ink);
        if (local.IsDocks && W.Prohibition)
            AddLabel(_businessPanel, $"The longshoremen unload every boat that lands. Whoever runs them takes ${Content.Wharfage} a week from every other outfit with a boat coming in, and hears when a rival's is due.", Palette.InkQuiet, 13, wrap: true);

        if (local.OwnerGangId == Me)
        {
            var president = W.Hoods.FirstOrDefault(h => h.Id == local.PresidentHoodId);
            AddLabel(_businessPanel, $"Your man in the hall is {president?.Name ?? "nobody"}. The dues of its {W.MembersOf(local).Count()} shops bring in ${W.DuesOf(local, evenOnStrike: true)} a week" +
                (local.OnStrike ? ", but nothing while the members are on strike pay." : "."), Palette.Player, 13, wrap: true);
            if (local.StrikeBusinessId == b.Id)
            {
                AddButton(_businessPanel, "Call off the strike", () =>
                {
                    W.EndStrike(local, $"{W.Player.Name} sent the pickets of {local.Name} back to work at {b.Name}.");
                    RefreshAll();
                });
                return;
            }
            if (b.ProtectorGangId == Me) return;
            var problem = W.CannotStrike(p, local, b);
            var strike = AddButton(_businessPanel, problem ?? "Call a strike until the owner pays you", () =>
            {
                if (W.CallStrike(W.Player, local, b) != null) return;
                Audio.Instance?.Play("whistle", 0.5f);
                RefreshAll();
            });
            strike.Disabled = problem != null;
            strike.TooltipText = $"Nothing comes in from the shop while the pickets are out. Each week the owner may give in and pay you: about {(b.IsProtected ? Content.GiveInProtected : Content.GiveIn):P0} a week{(b.IsProtected ? $" with {W.GangById(b.ProtectorGangId).Name} behind him" : "")}. After {Content.StrikeWeeks} weeks the strike falls apart, and while it lasts the local pays strike money instead of dues.";
            return;
        }

        if (!Simulation.CanTakeLocal(W, p, local) || AlreadyGiven(o => o is UnionOrder u && u.LocalId == local.Id)) return;
        var hoods = FreeHoods().Where(h => h.Id != p.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3).ToList();
        bool rival = local.OwnerGangId >= 0;
        AddLabel(_businessPanel, rival
                ? $"Push {W.GangById(local.OwnerGangId).Name} out of the hall: a fight with their man in the chair, and men can die. The dues bring in ${W.DuesOf(local, evenOnStrike: true)} a week."
                : $"Lean on the members until they put your man in the chair. The dues bring in ${W.DuesOf(local, evenOnStrike: true)} a week, and you can call the members out of any shop in the trade.",
            Palette.Ink, 13, wrap: true);
        if (hoods.Count > 0)
        {
            var who = HoodPicker(hoods, h => $"{Simulation.LocalChance(W, p, h, Array.Empty<Hood>(), local):P0} chance");
            AddButton(_businessPanel, "Send him to the hall", () => Give(new UnionOrder(Me, who.GetSelectedId(), local.Id)));
        }
        CrewPicker(_businessPanel, "Send the crew to the hall", team => $"{Simulation.LocalChance(W, W.Player, team[0], team.Skip(1), local):P0} chance",
            (lead, backup) => new UnionOrder(Me, lead, local.Id, backup));
    }
}
