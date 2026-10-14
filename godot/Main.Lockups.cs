using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The side panel for a lockup in a back alley: renting it, giving it up, and what's inside.</summary>
public partial class Main
{
    private void LockupPanel(Lockup l)
    {
        var panel = _businessPanel;
        AddLabel(panel, W.LockupName(l), Palette.Ink, 18);
        AddLabel(panel,
            "A brick lockup in a back alley, rented by the week. A car seen at a bank job or a killing goes into an empty one of yours on the way home, " +
            "and waits there until the police stop looking for it; out on the street they may spot it and tow it away. A hijacked beer truck waits in one " +
            "until its load can be sold for half as much again. The hotter your outfit, the likelier the police are to break one open.",
            Palette.InkQuiet, wrap: true);
        AddLabel(panel, $"{W.FromHq(W.Player, l) / (double)CityMap.StrideX:0.#} blocks from your HQ", Palette.InkQuiet);
        panel.AddChild(new HSeparator());
        AddLabel(panel, l.GangId < 0 ? $"To let, ${Content.LockupRent} a week."
            : l.GangId == Me ? $"Yours, at ${Content.LockupRent} a week."
            : $"Rented by {W.GangById(l.GangId).Name}.", Palette.Ink);
        if (l.GangId == Me) AddLabel(panel, Contents(l), Palette.Ink, wrap: true);
        if (!W.Player.Alive) return;

        if (l.GangId < 0)
        {
            string? problem = W.CannotRent(W.Player, l);
            var rent = AddButton(panel, $"Rent it, ${Content.LockupRent} a week", () =>
            {
                if (!W.RentLockup(W.Player, l)) return;
                Audio.Instance?.Play("cash", 0.6f);
                RefreshAll();
            });
            rent.Disabled = problem != null;
            rent.TooltipText = problem ?? "";
        }
        else if (l.GangId == Me)
        {
            var leave = AddButton(panel, "Give it up", () => { if (W.GiveUpLockup(W.Player, l)) RefreshAll(); });
            leave.Disabled = !l.IsEmpty;
            leave.TooltipText = l.IsEmpty ? "" : "Not while there's something in it.";
        }

        var mine = W.LockupsOf(Me).Where(x => x.Id != l.Id).OrderBy(x => x.Id).ToList();
        if (mine.Count > 0)
        {
            panel.AddChild(new HSeparator());
            AddLabel(panel, "Your other lockups", Palette.Ink);
            foreach (var x in mine)
                AddButton(panel, $"Lockup {x.Index + 1}, {W.Map.StreetNames[x.By]} St: {Short(x)}", () => ShowLockup(x)).ClipText = true;
        }

        var wanted = W.HoodsOf(Me).Where(h => h.CarWanted > 0 && h.Car != Car.None).OrderBy(h => h.Id).ToList();
        if (wanted.Count > 0)
        {
            panel.AddChild(new HSeparator());
            foreach (var h in wanted)
                AddLabel(panel, $"The police are looking for {h.Name}'s {Content.Cars[h.Car].Label}, seen at {h.CarWantedFor}, for {MoreWeeks(h.CarWanted)}.", Palette.Bad, wrap: true);
        }
    }

    /// <summary>What's in one of your lockups, and when it comes out.</summary>
    private string Contents(Lockup l) => l.Holds switch
    {
        Stashed.Car => $"Hiding {(l.HoodId >= 0 ? W.HoodById(l.HoodId).Name + "'s" : "a")} {Content.Cars[l.Car].Label}, wanted for {l.Job}. It comes out {When(l)}.",
        Stashed.Truck => $"Hiding a beer truck from {l.Job}. Its load sells for ${l.Value} {When(l)}.",
        _ => "Empty, ready for the next job.",
    };

    private string Short(Lockup l) => l.Holds switch
    {
        Stashed.Car => $"{Content.Cars[l.Car].Label}, out {When(l)}",
        Stashed.Truck => $"beer truck, sold {When(l)}",
        _ => "empty",
    };

    /// <summary>The Sunday something in a lockup comes out.</summary>
    private string When(Lockup l) => (l.OutWeek - 1 - W.Week) switch
    {
        <= 0 => "this Sunday",
        1 => "next Sunday",
        var n => $"in {n} weeks",
    };

    private static string MoreWeeks(int n) => n == 1 ? "one more week" : $"{n} more weeks";

    /// <summary>Select a lockup and bring it into view.</summary>
    private void ShowLockup(Lockup l)
    {
        _map.Select(lockup: l.Id);
        _map.ResetView(W.LockupLot(l));
        _tabs.CurrentTab = TabIndex("Selected");
        RefreshBusiness();
    }

    /// <summary>One of your lockups, or the nearest one to let.</summary>
    private void FindLockup()
    {
        var l = W.LockupsOf(Me).OrderBy(x => x.Id).FirstOrDefault()
            ?? W.Lockups.Where(x => x.GangId < 0).OrderBy(x => W.FromHq(W.Player, x)).ThenBy(x => x.Id).FirstOrDefault();
        if (l != null) ShowLockup(l);
    }
}
