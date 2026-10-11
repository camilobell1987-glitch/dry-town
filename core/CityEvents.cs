namespace DryTown.Core;

/// <summary>Things that happen to the whole city, or one ward of it, for a few weeks or a few years.</summary>
public enum CityEventKind
{
    // On the calendar.
    DrySquad, Crash, Depression, TaxExample,
    // Any year, at random.
    HeatWave, Convention, TitleFight, Crusade, Shakeup, Strike, Fire,
}

/// <summary>A city event in progress. WardId is -1 when it covers the whole city.</summary>
public sealed class CityEvent
{
    public CityEventKind Kind { get; init; }
    public int StartWeek { get; init; }
    public int UntilWeek { get; init; }
    public int WardId { get; init; } = -1;
}

/// <summary>
/// What each city event is called, what it does, how long it lasts and how often it comes.
/// Headlines and endings may name the ward with {ward}.
/// </summary>
public sealed record CityEventInfo(string Headline, string Effect, string Ended, int Weeks, int Weight = 0, int FromWeek = 0, int ToWeek = 51);

public static partial class Content
{
    public static readonly IReadOnlyDictionary<CityEventKind, CityEventInfo> CityEvents = new Dictionary<CityEventKind, CityEventInfo>
    {
        [CityEventKind.DrySquad] = new("A federal dry squad has come to town to dry the city out.",
            "Speakeasies and stills draw twice the heat.", "The federal dry squad has moved on to another city.", 26),
        [CityEventKind.Crash] = new("Stocks have crashed on Wall Street. Fortunes were wiped out overnight.",
            "Shops take in a tenth less.", "", 9),
        [CityEventKind.Depression] = new("The banks are failing and the breadlines are growing. Hard times have come to the city.",
            "Shops take in a fifth less. Loan books bring in twice as much and numbers games a quarter more.",
            "The worst of the hard times is over.", 4 * WeeksPerYear),
        [CityEventKind.TaxExample] = new("A big-city boss has been sent away for tax evasion, and the Treasury means to make more examples.",
            "Tax cases come twice as often.", "The Treasury's drive against the rackets has wound down.", WeeksPerYear),
        [CityEventKind.HeatWave] = new("A heat wave has settled on the city, and everybody wants a cold beer.",
            "Speakeasies and stills bring in half as much again.", "The heat wave has broken.", 4, 3, 24, 34),
        [CityEventKind.Convention] = new("A big convention is in town, and the delegates have money to spend.",
            "Shops take in a fifth more. Gambling dens and bookies bring in half as much again.", "The convention has left town.", 3, 3),
        [CityEventKind.TitleFight] = new("The heavyweight title fight is on the radio this week, and the whole city has a bet on it.",
            "Bookies and gambling dens bring in three times as much this week.", "", 1, 2),
        [CityEventKind.Crusade] = new("The Tribune has declared war on the rackets, and every other paper in town has joined in.",
            "Public outrage jumps, the outfit that runs the most of the city takes the heat, and the police raid sooner.", "The papers have dropped their crusade.", 8, 3),
        [CityEventKind.Shakeup] = new("A new police commissioner has cleaned house at headquarters.",
            "Every outfit's heat jumps, and it doesn't cool off on its own.", "The new commissioner has settled in, and the precincts are back to normal.", 6, 2),
        [CityEventKind.Strike] = new("A strike has shut down {ward}.",
            "Shops in {ward} take in half as much.", "The strike in {ward} is over.", 4, 2),
        [CityEventKind.Fire] = new("A fire swept through a block of {ward}.",
            "Several shops there are shut while they rebuild.", "", 1, 1),
    };

    /// <summary>The calendar: which event starts in which year and week of the year (1 to 52).</summary>
    public static readonly (CityEventKind Kind, int Year, int Week)[] CityCalendar =
    {
        (CityEventKind.DrySquad, 1924, 20),
        (CityEventKind.Crash, 1929, 44),
        (CityEventKind.Depression, 1930, 1),
        (CityEventKind.TaxExample, 1931, 42),
    };

    /// <summary>News on the calendar that changes nothing by itself.</summary>
    public static readonly (int Year, int Week, string Text)[] CityNotices =
    {
        (1920, 3, "The Volstead Act took effect at midnight. The city is dry, and thirsty."),
        (1933, 49, "Repeal has been ratified. Prohibition ends with the year."),
    };

    /// <summary>The heat a newspaper crusade puts on the outfit that runs the most of the city.</summary>
    public const int CrusadeHeat = 20;

    /// <summary>Weekly chance one of the random events starts, while fewer than two are on.</summary>
    public const double CityEventChance = 0.05;

    /// <summary>The same random event won't come round again for this long after it starts.</summary>
    public const int CityEventRest = 26;
}
