namespace DryTown.Core;

/// <summary>Nightclubs: a jazz club in a hotel, diner or dance hall an outfit owns outright, where the city's names and its politicians come to be seen.</summary>
public static partial class Content
{
    /// <summary>The respect a club earns its outfit's boss each week, with the bandleaders, ballplayers and society people at its tables.</summary>
    public const double ClubRespect = 0.3;

    /// <summary>With aldermen drinking on the house, an outfit with a club pays this share less to put one on the payroll and to keep him there.</summary>
    public const double ClubPayoff = 0.25;

    /// <summary>And its money behind a candidate counts for this much more, with the ward heelers at its tables.</summary>
    public const double ClubCampaign = 0.25;

    /// <summary>What the planner reckons a club's name and pull are worth a week, on top of its takings.</summary>
    public const int AiClubWorth = 60;
}

public sealed partial class World
{
    /// <summary>The gang's working nightclub, if it has one. An outfit runs one at a time.</summary>
    public Business? ClubOf(int gangId) =>
        FrontsOf(gangId).Where(b => b.Racket == RacketKind.Nightclub && Working(b)).OrderBy(b => b.Id).FirstOrDefault();

    /// <summary>What a club takes off the price of an alderman for this outfit: nothing, or a quarter.</summary>
    public double PayoffFactor(Gang g) => ClubOf(g.Id) != null ? 1 - Content.ClubPayoff : 1;

    /// <summary>What this outfit's campaign money is worth in the vote.</summary>
    public double CampaignWeight(int gangId) => ClubOf(gangId) != null ? 1 + Content.ClubCampaign : 1;
}
