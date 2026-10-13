namespace DryTown.Core;

/// <summary>After repeal: casinos for the outfits that can afford a hotel.</summary>
public static partial class AiPlanner
{
    /// <summary>Casinos an outfit wants: one, and another for every twenty shops.</summary>
    private static int CasinosWanted(World w, Gang g, int turf) => w.Prohibition ? 0 : 1 + turf / 20;

    private static int Casinos(World w, Gang g) => w.FrontsOf(g.Id).Count(b => b.Racket == RacketKind.Casino);

    /// <summary>A hotel to buy outright for a casino, once there's the money for both and some to spare.</summary>
    private static Business? BuyCasino(World w, Gang g, List<Business> turf, long cash)
    {
        if (Casinos(w, g) >= CasinosWanted(w, g, turf.Count)) return null;
        // One hotel at a time: a hotel already bought waits for its casino.
        if (w.FrontsOf(g.Id).Any(b => b.Kind == BusinessKind.Hotel && World.Working(b) && b.Racket is not (RacketKind.Casino or RacketKind.Nightclub) && Politics.Covered(w, g, b))) return null;
        int casino = Content.Rackets[RacketKind.Casino].SetupCost;
        // Only where the ward's alderman will see that the police leave it alone: a raid can close it, and $5,000 with it.
        return turf.Where(b => b.Kind == BusinessKind.Hotel && Politics.Covered(w, g, b) && w.CannotBuyFront(g, b) == null && Content.FrontPrice(b) + casino * 2 + 4000 <= cash)
            .OrderByDescending(b => b.Takings).ThenBy(b => b.Id).FirstOrDefault();
    }

    /// <summary>Open a casino in a hotel the outfit owns. Returns what it cost, or 0.</summary>
    private static long PlanCasino(World w, Gang g, List<Hood> hoods, long cash, List<Order> orders)
    {
        if (Casinos(w, g) >= CasinosWanted(w, g, w.TurfOf(g.Id).Count())) return 0;
        var info = Content.Rackets[RacketKind.Casino];
        if (cash < info.SetupCost * 2) return 0;
        // The club stays where it is.
        var hotel = w.FrontsOf(g.Id).Where(b => b.Racket != RacketKind.Nightclub && Politics.Covered(w, g, b) && w.CannotOpenRacket(g, b, RacketKind.Casino) == null).OrderByDescending(b => b.Takings).ThenBy(b => b.Id).FirstOrDefault();
        if (hotel == null) return 0;
        var brains = hoods.OrderByDescending(h => h.Brains).First();
        orders.Add(new RacketOrder(g.Id, brains.Id, hotel.Id, RacketKind.Casino));
        hoods.Remove(brains);
        return info.SetupCost;
    }
}
