namespace DryTown.Core;

/// <summary>A job still on the week's timeline, or already done: its hour, whose it is and the order.</summary>
public sealed class SavedJob
{
    public int Tick { get; set; }
    public int GangId { get; set; }
    public required Order Order { get; set; }
}

/// <summary>A man standing guard over a business this week, and what his backup adds.</summary>
public sealed class SavedGuard
{
    public int BusinessId { get; set; }
    public int HoodId { get; set; }
    public double Backup { get; set; }
}

/// <summary>Who a business paid, and what ran in its back room, when the week started.</summary>
public sealed class ShopAtStart
{
    public int BusinessId { get; set; }
    public int GangId { get; set; }
    public RacketKind Racket { get; set; }
}

/// <summary>A week saved partway through: every gang's jobs, the hour it got to, and what has happened on the street so far.</summary>
public sealed class WeekInProgress
{
    public int Tick { get; set; }
    public List<SavedJob> Timeline { get; set; } = new();

    /// <summary>How many jobs on the timeline are done.</summary>
    public int Cursor { get; set; }

    public List<int> UsedHoods { get; set; } = new();
    public Dictionary<int, int> Recruits { get; set; } = new();
    public Dictionary<int, int> ProtectorAtPlanning { get; set; } = new();

    /// <summary>For jobs ordered mid-week, by their place on the timeline: who protected the target when the order was given.</summary>
    public Dictionary<int, int> ProtectorWhenOrdered { get; set; } = new();

    public List<SavedGuard> Guards { get; set; } = new();
    public List<ScriptAction> Script { get; set; } = new();
    public List<ShopAtStart> StartOfWeek { get; set; } = new();
}

public sealed partial class Simulation
{
    /// <summary>Who each business paid and what ran in its back room when this week started, for the live view.</summary>
    public IReadOnlyDictionary<int, (int Gang, RacketKind Racket)> StartOfWeek => _startOfWeek;

    private Dictionary<int, (int Gang, RacketKind Racket)> _startOfWeek = new();

    /// <summary>Everything about the running week that isn't in the world itself.</summary>
    internal WeekInProgress SaveWeek()
    {
        var index = new Dictionary<Order, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < _timeline.Count; i++) index.TryAdd(_timeline[i].Order, i);
        return new WeekInProgress
        {
            Tick = World.Tick,
            Timeline = _timeline.Select(j => new SavedJob { Tick = j.Tick, GangId = j.GangId, Order = j.Order }).ToList(),
            Cursor = _cursor,
            UsedHoods = _usedHoods.ToList(),
            Recruits = new Dictionary<int, int>(_recruits),
            ProtectorAtPlanning = new Dictionary<int, int>(_protectorAtPlanning),
            ProtectorWhenOrdered = _protectorWhenOrdered.Where(p => index.ContainsKey(p.Key)).ToDictionary(p => index[p.Key], p => p.Value),
            Guards = _guards.Select(g => new SavedGuard { BusinessId = g.Key, HoodId = g.Value.Hood.Id, Backup = g.Value.Backup }).ToList(),
            Script = World.Script.ToList(),
            StartOfWeek = _startOfWeek.Select(s => new ShopAtStart { BusinessId = s.Key, GangId = s.Value.Gang, Racket = s.Value.Racket }).ToList(),
        };
    }

    /// <summary>Pick a saved week up at the hour it got to.</summary>
    internal void ResumeWeek(WeekInProgress week)
    {
        var w = World;
        w.Tick = week.Tick;
        w.Script.Clear();
        w.Script.AddRange(week.Script);
        _timeline = week.Timeline.Select(j => new Job(j.Tick, j.GangId, j.Order)).ToList();
        _cursor = week.Cursor;
        _usedHoods.Clear();
        _usedHoods.UnionWith(week.UsedHoods);
        _recruits.Clear();
        foreach (var (gang, n) in week.Recruits) _recruits[gang] = n;
        _protectorAtPlanning = new Dictionary<int, int>(week.ProtectorAtPlanning);
        _protectorWhenOrdered.Clear();
        foreach (var (i, gang) in week.ProtectorWhenOrdered)
            if (i >= 0 && i < _timeline.Count) _protectorWhenOrdered[_timeline[i].Order] = gang;
        _guards.Clear();
        foreach (var g in week.Guards)
            if (w.Hoods.FirstOrDefault(h => h.Id == g.HoodId) is { } hood) _guards[g.BusinessId] = (hood, g.Backup);
        _startOfWeek = week.StartOfWeek.ToDictionary(s => s.BusinessId, s => (s.GangId, s.Racket));
        WeekRunning = true;
    }

    /// <summary>Note who each business pays before anything happens this week.</summary>
    private void NoteStartOfWeek() =>
        _startOfWeek = World.Businesses.ToDictionary(b => b.Id, b => (b.ProtectorGangId, b.Racket));
}
