namespace DryTown.Core;

/// <summary>Running it from inside: a boss sent away keeps the chair from his cell, while a street boss minds the outfit.</summary>
public static partial class Content
{
    /// <summary>A stretch longer than this costs a boss the chair, unless he runs the outfit from inside.</summary>
    public const int LongStretch = 26;

    /// <summary>What the guards want each week to carry the boss's word out: a base, and a little more for every shop he runs.</summary>
    public static int CellFee(int turf) => 40 + 2 * turf;

    /// <summary>Nobody tries to run it from inside without money for this many weeks of the guards, and a rival boss only tries if he has the brains.</summary>
    public const int InsideReserveWeeks = 8, InsideBrains = 5;

    /// <summary>
    /// Weekly chance the street boss decides he doesn't need orders from a cell: more for an ambitious man and the
    /// longer it goes on, less for a loyal one and under a frightening boss.
    /// </summary>
    public static double CoupChance(Hood streetBoss, Hood boss, int weeksInside) =>
        Math.Clamp(0.004 + (streetBoss.Ambition - 50) * 0.0004 - (streetBoss.Loyalty - 50) * 0.0003
                   + weeksInside / (double)WeeksPerYear * 0.004 - boss.Intimidation * 0.0005, 0, 0.05);

    /// <summary>Weekly chance the warden moves him somewhere no word gets out of: more under a reform DA or mayor, and when the papers are angry.</summary>
    public static double TransferChance(World w) =>
        0.005 + (w.Hall.DAReform ? 0.01 : 0) + (w.Hall.Reform ? 0.005 : 0) + w.Hall.Outrage * 0.0001;
}

public sealed partial class World
{
    /// <summary>The boss is in a cell and still runs the outfit.</summary>
    public bool RunsFromInside(Gang g) => g.InsideSinceWeek >= 0;

    /// <summary>Who minds the street while the boss is inside: his heir.</summary>
    public Hood? StreetBoss(Gang g) => RunsFromInside(g) && g.HeirHoodId >= 0 ? HoodById(g.HeirHoodId) : null;

    /// <summary>What the guards cost the gang this week.</summary>
    public int CellFee(Gang g) => Content.CellFee(TurfOf(g.Id).Count());

    /// <summary>Why the boss couldn't run the outfit from his cell, or null if he could.</summary>
    public string? CannotRunFromInside(Gang g)
    {
        var boss = HoodById(g.BossHoodId);
        if (boss.FederalTime) return "He's in a federal pen. No word gets out of there.";
        if (g.Cash < CellFee(g) * Content.InsideReserveWeeks)
            return $"The guards want ${CellFee(g):N0} a week to carry his word out, and the outfit hasn't ${CellFee(g) * Content.InsideReserveWeeks:N0} to see it through the first {Content.InsideReserveWeeks} weeks.";
        return null;
    }
}
