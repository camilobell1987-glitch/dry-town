namespace DryTown.Core;

/// <summary>The boss a campaign carries from one city to the next: who he is, and how good he is.</summary>
public sealed class CarriedBoss
{
    public string Name { get; set; } = "";
    public Ethnicity Origin { get; set; }
    public int Intimidation { get; set; }
    public int Muscle { get; set; }
    public int Brains { get; set; }
    public int Stealth { get; set; }
    public int Ambition { get; set; }
    public int BornWeek { get; set; }
}

/// <summary>Where a campaign stands: which chapter this city is, how the ones before it went, and the boss and outfit it carries.</summary>
public sealed class CampaignState
{
    /// <summary>The chapter being played, from 0.</summary>
    public int Chapter { get; set; }

    /// <summary>How each chapter before this one came out.</summary>
    public List<ScenarioOutcome> Results { get; set; } = new();

    /// <summary>The boss who comes to this city, or null for a new man (the first chapter).</summary>
    public CarriedBoss? Boss { get; set; }

    /// <summary>The outfit's name, kept from city to city.</summary>
    public string? Outfit { get; set; }

    public int Won => Results.Count(r => r == ScenarioOutcome.Won);
}

/// <summary>One step of the campaign: the scenario, the years it's played in, and how the story gets there.</summary>
public sealed record Chapter(ScenarioKind Kind, int StartYear, int DeadlineYear, string Bridge);

/// <summary>
/// The campaign: the five scenarios played one after another by the same boss, from 1920 to repeal. Each chapter
/// is a new city that starts the year the last one's deadline fell. Win or lose, an outfit still standing goes on to
/// the next; a chapter can always be tried again. Every chapter won sends word ahead, worth money at the start of the next.
/// </summary>
public static class Campaign
{
    /// <summary>What each chapter won before this one adds to the outfit's money at the start.</summary>
    public const int WordAhead = 1000;

    /// <summary>{0} is the boss, {1} the outfit.</summary>
    public static readonly Chapter[] Chapters =
    {
        new(ScenarioKind.OffTheBoat, 1920, 1924, ""),
        new(ScenarioKind.TakeTheWard, 1924, 1927, "In 1924 a party man in another city sends for {0}. Over there, he says, is a ward that nobody runs, and he'd rather it was run by somebody he knows."),
        new(ScenarioKind.Waterfront, 1927, 1930, "By 1927 {0} has his eye on a port city, where the boats come in from Canada and the Islands. {1} sets up shop near the docks."),
        new(ScenarioKind.SurviveTheCrusade, 1930, 1932, "It's 1930, and {1} has grown into the biggest outfit in a city of its own. The Crash has made the papers hungry for someone to blame."),
        new(ScenarioKind.LastCall, 1932, 1934, "{0} sees the end of Prohibition coming, and moves {1} to one more city to get the money clean before it does."),
    };

    public static Chapter Current(World w) => Chapters[w.Settings.Campaign!.Chapter];

    public static string Bridge(CampaignState c) =>
        c.Boss == null ? "" : string.Format(Chapters[c.Chapter].Bridge, c.Boss.Name, c.Outfit);

    /// <summary>The settings for a new campaign's first city.</summary>
    public static WorldSettings Start(ulong seed) => SettingsFor(new CampaignState(), seed);

    /// <summary>The same chapter again in a new city, with the boss and record it started with.</summary>
    public static WorldSettings Retry(World w, ulong seed)
    {
        var c = w.Settings.Campaign!;
        return SettingsFor(new CampaignState { Chapter = c.Chapter, Results = c.Results.ToList(), Boss = c.Boss, Outfit = c.Outfit }, seed);
    }

    /// <summary>Why the outfit can't go on to the next chapter yet, or null if it can.</summary>
    public static string? CannotGoOn(World w)
    {
        var c = w.Settings.Campaign;
        if (c == null) return "This city isn't part of a campaign.";
        if (w.Goal.Outcome == ScenarioOutcome.Playing) return "This chapter isn't over yet.";
        if (c.Chapter + 1 >= Chapters.Length) return "That was the last chapter.";
        if (!w.Player.Alive) return $"{w.Player.Name} is finished, so the campaign can't go on. Try the chapter again.";
        return null;
    }

    /// <summary>The next chapter's city, carrying the outfit's boss, name and record.</summary>
    public static WorldSettings Next(World w, ulong seed)
    {
        if (CannotGoOn(w) is { } why) throw new InvalidOperationException(why);
        var c = w.Settings.Campaign!;
        var boss = w.HoodById(w.Player.BossHoodId);
        return SettingsFor(new CampaignState
        {
            Chapter = c.Chapter + 1,
            Results = c.Results.Append(w.Goal.Outcome).ToList(),
            Boss = new CarriedBoss
            {
                Name = boss.Name, Origin = boss.Origin, Ambition = boss.Ambition, BornWeek = boss.BornWeek,
                Intimidation = boss.Intimidation, Muscle = boss.Muscle, Brains = boss.Brains, Stealth = boss.Stealth,
            },
            Outfit = w.Player.Name,
        }, seed);
    }

    private static WorldSettings SettingsFor(CampaignState c, ulong seed)
    {
        var chapter = Chapters[c.Chapter];
        var info = Scenarios.Info(chapter.Kind);
        return new WorldSettings
        {
            Seed = seed, Size = info.Size, Difficulty = info.Difficulty, Scenario = chapter.Kind, Campaign = c,
            StartWeek = (chapter.StartYear - Content.StartYear) * Content.WeeksPerYear,
        };
    }

    /// <summary>The campaign's record so far, chapter by chapter, for the console and the report.</summary>
    public static string Record(World w)
    {
        var c = w.Settings.Campaign;
        if (c == null) return "";
        var lines = new List<string> { $"Campaign, chapter {c.Chapter + 1} of {Chapters.Length}:" };
        for (int i = 0; i < Chapters.Length; i++)
        {
            var ch = Chapters[i];
            string state = i < c.Results.Count ? (c.Results[i] == ScenarioOutcome.Won ? "won" : "lost")
                : i == c.Chapter ? (w.Goal.Outcome switch { ScenarioOutcome.Won => "won", ScenarioOutcome.Lost => "lost", _ => "playing" })
                : "to come";
            lines.Add($"  {i + 1}. {Scenarios.Info(ch.Kind).Title}, {ch.StartYear} to {ch.DeadlineYear}: {state}");
        }
        if (Verdict(w) is { } verdict) lines.Add(verdict);
        else if (w.Goal.Outcome != ScenarioOutcome.Playing)
            lines.Add(CannotGoOn(w) is { } why ? why : "The next chapter is ready when you are.");
        return string.Join("\n", lines);
    }

    /// <summary>When the last chapter is decided, how the whole campaign went; else null.</summary>
    public static string? Verdict(World w)
    {
        var c = w.Settings.Campaign;
        if (c == null || c.Chapter + 1 < Chapters.Length || w.Goal.Outcome == ScenarioOutcome.Playing) return null;
        int won = c.Won + (w.Goal.Outcome == ScenarioOutcome.Won ? 1 : 0);
        string how = won == Chapters.Length ? "Every one of them. Nobody in the business has a record like it."
            : won >= 3 ? "A career the papers will write about for years."
            : won >= 1 ? "Not every city went his way, but he lasted to repeal."
            : "None of them, but he lasted to repeal, and plenty of men didn't.";
        return $"The campaign is over. {w.HoodById(w.Player.BossHoodId).Name} won {won} of {Chapters.Length} chapters. {how}";
    }
}

public sealed partial class World
{
    /// <summary>The boss a campaign brings to this city, as a new man in it.</summary>
    private Hood CarryBoss(CarriedBoss b)
    {
        var hood = new Hood
        {
            Id = _nextHoodId++, Name = b.Name, GangId = -1, Origin = b.Origin,
            Intimidation = b.Intimidation, Muscle = b.Muscle, Brains = b.Brains, Stealth = b.Stealth,
            Loyalty = 100, Ambition = b.Ambition, JoinedWeek = Week, BornWeek = b.BornWeek,
        };
        hood.Wage = 14 + (hood.Strength + hood.Brains) * 2;
        Hoods.Add(hood);
        Characterise(hood);
        return hood;
    }
}
