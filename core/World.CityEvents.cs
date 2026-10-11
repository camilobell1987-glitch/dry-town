namespace DryTown.Core;

/// <summary>The city events in progress, and what they do to the takings, the rackets, the police and the Treasury.</summary>
public sealed partial class World
{
    public List<CityEvent> CityEvents { get; } = new();

    /// <summary>When each random event last started, so the same one doesn't come round too soon.</summary>
    public Dictionary<CityEventKind, int> CityEventLast { get; } = new();

    public bool Happening(CityEventKind kind) => CityEvents.Any(e => e.Kind == kind);

    /// <summary>What a shop takes in this week, as a share of its usual takings.</summary>
    public double TakingsFactor(Business b)
    {
        double f = 1;
        foreach (var e in CityEvents)
            f *= e.Kind switch
            {
                CityEventKind.Crash => 0.9,
                CityEventKind.Depression => 0.8,
                CityEventKind.Convention => 1.2,
                CityEventKind.Strike when Map.LotAt(b.LotId).WardId == e.WardId => 0.5,
                _ => 1,
            };
        return f;
    }

    /// <summary>What a racket brings in this week, as a share of its usual income.</summary>
    public double RacketFactor(RacketKind kind)
    {
        double f = 1;
        foreach (var e in CityEvents)
            f *= (e.Kind, kind) switch
            {
                (CityEventKind.HeatWave, RacketKind.Speakeasy or RacketKind.Still) => 1.5,
                (CityEventKind.Convention, RacketKind.GamblingDen or RacketKind.Bookie) => 1.5,
                (CityEventKind.TitleFight, RacketKind.GamblingDen or RacketKind.Bookie) => 3,
                (CityEventKind.Depression, RacketKind.LoanShark) => 2,
                (CityEventKind.Depression, RacketKind.Numbers) => 1.25,
                _ => 1,
            };
        return f;
    }

    /// <summary>How much heat a racket draws this week, as a multiple of the usual.</summary>
    public int RacketHeatFactor(RacketKind kind) => Happening(CityEventKind.DrySquad) && Content.Rackets[kind].NeedsProhibition ? 2 : 1;

    /// <summary>How much sooner the police raid: points off the heat a gang can carry before they come.</summary>
    public int RaidSooner => Happening(CityEventKind.Crusade) ? 10 : 0;

    /// <summary>Under a new commissioner, heat doesn't cool off by itself.</summary>
    public bool HeatStays => Happening(CityEventKind.Shakeup);

    public double TaxCaseFactor => Happening(CityEventKind.TaxExample) ? 2 : 1;

    /// <summary>The event's headline or effect, with its ward filled in.</summary>
    public string Describe(CityEvent e, string text) => text.Replace("{ward}", e.WardId >= 0 && e.WardId < Wards.Count ? Wards[e.WardId].Name : "the city");
}
