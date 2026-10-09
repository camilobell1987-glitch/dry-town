namespace DryTown.Core;

/// <summary>
/// Keeps the district contested so play never runs out. It watches for one gang
/// owning too much, or too few gangs, or too long without turf changing hands,
/// and answers with an outside syndicate or an internal split.
/// </summary>
public sealed class Director
{
    private readonly World _w;
    private int _dominantWeeks;
    private int _quietWeeks;

    public const double DominanceShare = 0.55;
    public const int DominanceWeeks = 12;
    public const int QuietWeeks = 26;

    public Director(World world) => _w = world;

    public void Step()
    {
        if (!_w.Settings.DirectorEnabled) return;

        int living = _w.LivingGangs.Count();
        if (living < 2)
        {
            Outsiders("moved in to fill the vacuum");
            return;
        }

        var top = _w.LivingGangs.OrderByDescending(g => _w.TurfOf(g.Id).Count()).First();
        double share = (double)_w.TurfOf(top.Id).Count() / _w.Businesses.Count;
        _dominantWeeks = share >= DominanceShare ? _dominantWeeks + 1 : 0;

        bool changedHands = _w.Events.Any(e => e.Week == _w.Week && e.Kind is EventKind.Takeover or EventKind.Breakaway);
        _quietWeeks = changedHands ? 0 : _quietWeeks + 1;

        if (_dominantWeeks >= DominanceWeeks)
        {
            _dominantWeeks = 0;
            StirDissent(top);
            // A gang that owns most of the district has more than one lieutenant eyeing the chair.
            if (share >= 0.7) StirDissent(top);
            if (living < Content.MaxGangs) Outsiders($"arrived to challenge {top.Name}");
        }
        else if (_quietWeeks >= QuietWeeks)
        {
            _quietWeeks = 0;
            if (living < Content.MaxGangs) Outsiders("smelled easy money in a quiet district");
            else StirDissent(top);
        }
        else if (living < 3 && _w.Rng.Chance(0.01))
        {
            Outsiders("came down from the North Side");
        }
    }

    /// <summary>The most ambitious lieutenant of a dominant gang turns sour.</summary>
    private bool StirDissent(Gang gang)
    {
        var candidate = _w.AvailableHoodsOf(gang.Id)
            .Where(h => h.Id != gang.BossHoodId)
            .OrderByDescending(h => h.Ambition).ThenBy(h => h.Id)
            .FirstOrDefault();
        if (candidate == null) return false;
        candidate.Ambition = Math.Max(candidate.Ambition, 70);
        candidate.Loyalty = Math.Min(candidate.Loyalty, 15);
        return true;
    }

    /// <summary>An outside syndicate arrives, sized to be a threat to whoever is on top.</summary>
    private void Outsiders(string why)
    {
        var top = _w.LivingGangs.OrderByDescending(g => _w.HoodsOf(g.Id).Count()).FirstOrDefault();
        long cash = Math.Max(2500, (top?.Cash ?? 0) / 8);
        int hoods = Math.Clamp((top == null ? 0 : _w.HoodsOf(top.Id).Count()) * 2 / 3, 5, 12);
        // Outsiders come looking for a fight, not to share.
        var gang = _w.FoundGang(isPlayer: false, cash: cash, hoods: hoods, aggression: 1.0);
        _w.Log(EventKind.NewGang, gang.Id, $"{gang.Name} {why}.");
    }
}
