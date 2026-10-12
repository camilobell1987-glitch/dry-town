namespace DryTown.Core;

/// <summary>Breweries: a warehouse turned over to making beer, which an outfit's speakeasies pour when its lines can't keep them in liquor.</summary>
public static partial class Content
{
    /// <summary>A speakeasy pouring the outfit's own beer takes this much more than one selling bathtub gin.</summary>
    public const double BeerBonus = 0.25;

    /// <summary>The speakeasies one brewery keeps in beer.</summary>
    public const int BreweryRoom = 6;

    /// <summary>A smashed brewery is worth this many smashed stills to the Untouchables, and this many places to the squad sizing up a bootlegger.</summary>
    public const int BreweryEvidence = 1, BreweryBootlegging = 2;

    /// <summary>The speakeasies on bathtub gin it takes for a rival to put a brewery in before anything else.</summary>
    public const int AiBreweryThirst = 2;

    /// <summary>Chance the squad goes for an outfit's brewery before its other places.</summary>
    public const double SquadBrewery = 0.5;
}

public sealed partial class World
{
    /// <summary>The gang's working brewery, if it has one. An outfit runs one at a time.</summary>
    public Business? BreweryOf(int gangId) => TurfOf(gangId).Where(b => b.Racket == RacketKind.Brewery && b.IsOpen).OrderBy(b => b.Id).FirstOrDefault();

    /// <summary>
    /// The gang's speakeasies its brewery keeps in beer: the oldest of those the lines and the warehouse
    /// leave on bathtub gin, as many as the brewery makes for.
    /// </summary>
    public HashSet<int> OnTap(int gangId, HashSet<int>? supplied = null)
    {
        if (!Prohibition || BreweryOf(gangId) == null) return new();
        supplied ??= Supplied(gangId);
        return TurfOf(gangId).Where(b => b.Racket == RacketKind.Speakeasy && b.IsOpen && !supplied.Contains(b.Id)).OrderBy(b => b.Id)
            .Take(Content.BreweryRoom).Select(b => b.Id).ToHashSet();
    }

    /// <summary>Whether this speakeasy pours the outfit's own beer.</summary>
    public bool Beer(Business b, Gang gang, HashSet<int>? supplied = null) =>
        b.Racket == RacketKind.Speakeasy && Prohibition && BreweryOf(gang.Id) != null && OnTap(gang.Id, supplied).Contains(b.Id);
}
