namespace DryTown.Core;

/// <summary>Sunday in a war: the safehouses' bill comes in with the wages, and the city grows angrier.</summary>
public sealed partial class Simulation
{
    private void Wars()
    {
        var w = World;
        foreach (var war in w.Wars.ToList())
        {
            var a = w.GangById(war.A);
            var b = w.GangById(war.B);
            if (!a.Alive || !b.Alive)
            {
                var left = a.Alive ? a : b;
                w.EndWar(war, $"With {(a.Alive ? b : a).Name} finished, {left.Name}'s men came home from the mattresses.");
                continue;
            }
            int weeks = w.Week - war.StartWeek;
            foreach (var g in new[] { a, b }) g.Heat = Math.Min(100, g.Heat + Content.WarHeat(weeks));
            w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + Content.WarOutrage(weeks));
            if (weeks > 0 && weeks % 8 == 0)
                w.Log(EventKind.News, -1, $"The war between {a.Name} and {b.Name} has gone on for {weeks} weeks. The papers want to know where the police are.");
        }
    }

    /// <summary>What a gang pays this week to keep its men on the mattresses.</summary>
    public static long MattressBill(World w, Gang g) =>
        w.AtWar(g.Id) ? w.HoodsOf(g.Id).Count(h => h.IsAvailable) * (long)Content.MattressCost : 0;
}
