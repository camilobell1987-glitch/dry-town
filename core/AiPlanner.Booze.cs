namespace DryTown.Core;

public static partial class AiPlanner
{
    /// <summary>
    /// A supply line once the outfit runs more speakeasies than its lines keep in good liquor and has
    /// the money to spare, boats for the rest, and a heavier pour when money is short and the police aren't looking.
    /// </summary>
    private static long PlanBooze(World w, Gang g, List<Business> turf, long cash, List<Order> orders)
    {
        if (!w.Prohibition) return 0;
        var want = cash < 600 && g.Heat < 40 && g.Aggression > 0.5 ? Cut.Watered : Cut.Straight;
        if (want != g.Cut) orders.Add(new CutOrder(g.Id, want));

        int speakeasies = turf.Count(b => b.Racket == RacketKind.Speakeasy && b.IsOpen);
        int room = w.LinesOf(g.Id).Sum(l => l.Capacity);
        long spent = 0;
        // Nobody signs on with a run in the last year before repeal.
        if (w.Year < Content.RepealYear - 1 && speakeasies >= 2 && speakeasies > room)
        {
            var line = w.SupplyLines.Where(l => w.CannotBuyLine(g, l) == null && cash - l.Price >= 3000)
                .OrderBy(l => Math.Abs(l.Capacity - (speakeasies - room))).ThenBy(l => l.Price).ThenBy(l => l.Id).FirstOrDefault();
            if (line != null)
            {
                orders.Add(new LineOrder(g.Id, line.Id));
                spent = line.Price;
                room += line.Capacity;
            }
        }
        return spent + PlanBoats(w, g, speakeasies - room, cash - spent, orders);
    }

    /// <summary>
    /// Boats for the speakeasies no line can supply: send the ones at the dock out while the warehouse
    /// is short of a month's pouring, on whichever run is out of its stormy season, and buy one when
    /// there's no line to be had.
    /// </summary>
    private static long PlanBoats(World w, Gang g, int dry, long cash, List<Order> orders)
    {
        if (dry <= 0) return 0;
        long spent = 0;
        var boats = w.BoatsOf(g.Id).ToList();
        int coming = g.Cellar + boats.Sum(b => b.Cases);
        foreach (var boat in boats.Where(b => !b.AtSea))
        {
            if (coming >= dry * 4) break;
            var run = new[] { Run.Islands, Run.Canada }.Where(r => w.CannotSail(g, boat, r) == null && cash - spent - Content.Runs[r].Cost >= Content.AiSailReserve)
                .OrderBy(r => w.StormChance(r)).Cast<Run?>().FirstOrDefault();
            if (run is not { } r2) break;
            orders.Add(new SailOrder(g.Id, boat.Id, r2));
            spent += Content.Runs[r2].Cost;
            coming += Content.Runs[r2].Cases;
        }
        bool lineToBeHad = w.SupplyLines.Any(l => l.OwnerGangId < 0);
        if (w.Year < Content.RepealYear - 1 && dry >= 2 && !lineToBeHad && boats.Count < (dry + 4) / 5 && cash - spent - Content.BoatPrice >= 3000 && w.CannotBuyBoat(g) == null)
        {
            orders.Add(new BoatOrder(g.Id));
            spent += Content.BoatPrice;
        }
        return spent;
    }
}
