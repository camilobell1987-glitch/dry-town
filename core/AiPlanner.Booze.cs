namespace DryTown.Core;

public static partial class AiPlanner
{
    /// <summary>
    /// A supply line once the outfit runs more speakeasies than its lines keep in good liquor and has
    /// the money to spare, and a heavier pour when money is short and the police aren't looking.
    /// </summary>
    private static long PlanBooze(World w, Gang g, List<Business> turf, long cash, List<Order> orders)
    {
        if (!w.Prohibition) return 0;
        var want = cash < 600 && g.Heat < 40 && g.Aggression > 0.5 ? Cut.Watered : Cut.Straight;
        if (want != g.Cut) orders.Add(new CutOrder(g.Id, want));

        // Nobody signs on with a run in the last year before repeal.
        if (w.Year >= Content.RepealYear - 1) return 0;
        int speakeasies = turf.Count(b => b.Racket == RacketKind.Speakeasy && b.IsOpen);
        int room = w.LinesOf(g.Id).Sum(l => l.Capacity);
        if (speakeasies < 2 || speakeasies <= room) return 0;
        var line = w.SupplyLines.Where(l => w.CannotBuyLine(g, l) == null && cash - l.Price >= 3000)
            .OrderBy(l => Math.Abs(l.Capacity - (speakeasies - room))).ThenBy(l => l.Price).ThenBy(l => l.Id).FirstOrDefault();
        if (line == null) return 0;
        orders.Add(new LineOrder(g.Id, line.Id));
        return line.Price;
    }
}
