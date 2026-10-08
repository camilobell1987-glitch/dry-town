namespace DryTown.Core;

public sealed record WeekSample(int Week, int LivingGangs, double TopShare, int Takeovers, long PlayerCash, bool PlayerAlive);

/// <summary>Weekly samples used by soak tests to decide whether a long game stays contested.</summary>
public sealed class Metrics
{
    public List<WeekSample> Samples { get; } = new();

    public void Record(World w, int takeovers)
    {
        var shares = w.LivingGangs.Select(g => (double)w.TurfOf(g.Id).Count() / w.Businesses.Count).DefaultIfEmpty(0);
        var player = w.Player;
        Samples.Add(new WeekSample(w.Week, w.LivingGangs.Count(), shares.Max(), takeovers, player.Cash, player.Alive));
    }

    /// <summary>
    /// The Phase 1 gate. A run "stalls" if any year has fewer than two gangs at some point,
    /// one gang holding most of the district all year, or no turf changing hands.
    /// </summary>
    public StallReport Check(int minTakeoversPerYear = 3, double maxYearLongShare = 0.75)
    {
        var problems = new List<string>();
        foreach (var year in Samples.GroupBy(s => s.Week / Content.WeeksPerYear))
        {
            int y = Content.StartYear + year.Key;
            if (year.Any(s => s.LivingGangs < 2)) problems.Add($"{y}: fewer than two gangs");
            if (year.All(s => s.TopShare > maxYearLongShare)) problems.Add($"{y}: one gang held over {maxYearLongShare:P0} all year");
            int takeovers = year.Sum(s => s.Takeovers);
            if (takeovers < minTakeoversPerYear) problems.Add($"{y}: only {takeovers} takeovers");
        }
        return new StallReport(problems);
    }
}

public sealed record StallReport(IReadOnlyList<string> Problems)
{
    public bool Stalled => Problems.Count > 0;
}
