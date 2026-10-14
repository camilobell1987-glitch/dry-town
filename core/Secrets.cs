namespace DryTown.Core;

/// <summary>Finding out what people are hiding, and making them pay for it.</summary>
public static partial class Content
{
    public static readonly string[] WantedFor = { "robbery", "manslaughter", "fraud", "arson", "bigamy", "forgery", "car theft", "burglary" };
    public static readonly string[] WantedIn = { "Chicago", "Detroit", "Boston", "Philadelphia", "Cleveland", "Pittsburgh" };

    /// <summary>The chance asking around turns up each secret, and the chance the week's gossip about someone is one of them.</summary>
    public const double AskSecret = 0.12, RumourSecret = 0.15;

    /// <summary>How much likelier a threat or a squeeze works for each degree of shame you know of, and the most it can add.</summary>
    public const double ShameThreat = 0.06, ShameThreatMost = 0.2;

    /// <summary>Weeks a blackmailed man pays before he's squeezed dry, and the weekly chance he goes to the police instead.</summary>
    public const int HushWeeks = 8;
    public const double HushSnaps = 0.04;

    /// <summary>The heat the police take off you for a tip that sticks, and how long a shop stays shut while the family sorts things out.</summary>
    public const int TipHeat = 4, ShutForOwner = 4;

    /// <summary>
    /// What a court of the day could give for each crime, in years, least to most: car theft under the Dyer Act and the
    /// state laws, manslaughter up to fifteen, armed robbery up to twenty, and so on.
    /// </summary>
    public static (double Least, double Most) Penalty(string crime) => crime switch
    {
        "robbery" => (5, 20),
        "manslaughter" => (5, 15),
        "fraud" => (1, 7),
        "arson" => (2, 20),
        "bigamy" => (1, 5),
        "forgery" => (2, 10),
        "car theft" => (2, 4),
        "burglary" => (2, 10),
        "receiving stolen goods" => (1, 3),
        _ => (1, 5),
    };

    /// <summary>How a trial can go: a plea for a light term, an ordinary sentence, or a hanging judge.</summary>
    public enum Verdict { Deal, Fair, Harsh }

    /// <summary>
    /// The chance of a deal and of a hanging judge. Money buys a lawyer who can strike a deal; a reform administration
    /// wants examples made.
    /// </summary>
    public static (double Deal, double Harsh) VerdictOdds(int worth, bool reform) =>
        (Math.Clamp(0.25 + worth / 200.0 - (reform ? 0.1 : 0), 0.1, 0.6), reform ? 0.3 : 0.15);

    /// <summary>A term in years, as a judge would read it out: "18 months", "7 years", "3 years and 6 months".</summary>
    public static string Term(int months) => (months / 12, months % 12) switch
    {
        (1, 0) => "a year",
        (< 2, _) => $"{months} months",
        (var y, 0) => $"{y} years",
        (var y, var m) => $"{y} years and {m} months",
    };

    /// <summary>How bad it would be if it came out, 1 to 3.</summary>
    public static int Shame(Vice vice) => vice switch
    {
        Vice.Wanted => 3,
        Vice.Lover or Vice.Thief => 2,
        _ => 1,
    };

    /// <summary>Whether the police would take him in for it.</summary>
    public static bool IsCrime(Vice vice) => vice is Vice.Thief or Vice.Wanted;

    public static string Label(Vice vice) => vice switch
    {
        Vice.Lover => "Carrying on",
        Vice.Drink => "Drinks",
        Vice.Gambling => "Gambles",
        Vice.Debt => "In debt",
        Vice.Thief => "Steals",
        _ => "Wanted",
    };

    /// <summary>What he's wanted for, in a few words.</summary>
    public static string Crime(Secret s) => s.Vice == Vice.Wanted
        ? $"{Offence(s)} in {WantedIn[s.Other % 16 % WantedIn.Length]}"
        : Offence(s);

    /// <summary>The crime itself, without where.</summary>
    public static string Offence(Secret s) => s.Vice == Vice.Wanted ? WantedFor[s.Other / 16 % WantedFor.Length]
        : s.Other >= 0 ? "theft" : "receiving stolen goods";
}

public sealed partial class Acquaintance
{
    /// <summary>What he's hiding, whether or not you know it yet, and the vices you've found out.</summary>
    public List<Secret> Secrets { get; set; } = new();
    public List<Vice> KnownVices { get; set; } = new();

    public IEnumerable<Secret> UnknownSecrets => Secrets.Where(s => !KnownVices.Contains(s.Vice));
}

public sealed partial class World
{
    /// <summary>Note what someone is hiding, from the directory, the first time you come across him.</summary>
    public Acquaintance Introduce(int citizen, int wardId, IReadOnlyList<Tie> ties, IReadOnlyList<Secret> secrets)
    {
        var a = Introduce(citizen, wardId, ties);
        if (a.Secrets.Count == 0 && secrets.Count > 0) a.Secrets = secrets.ToList();
        return a;
    }

    public IEnumerable<Secret> KnownSecretsOf(int citizen) =>
        Acquaintances.TryGetValue(citizen, out var a) ? a.Secrets.Where(s => a.KnownVices.Contains(s.Vice)) : Enumerable.Empty<Secret>();

    /// <summary>Find out one of someone's secrets. A lover goes in your directory as someone he's close to.</summary>
    public bool LearnSecret(int citizen, Secret secret)
    {
        var a = Meet(citizen, -1);
        if (a.KnownVices.Contains(secret.Vice)) return false;
        a.KnownVices.Add(secret.Vice);
        if (secret.Vice == Vice.Lover && secret.Other >= 0)
        {
            var tie = new Tie(secret.Other, Kin.Lover);
            if (!a.Ties.Contains(tie)) a.Ties.Add(tie);
            LearnTie(citizen, tie);
        }
        return true;
    }

    /// <summary>Find out each of someone's unknown secrets with this chance. Returns how many were new.</summary>
    public int LearnSecrets(int citizen, double chance)
    {
        if (!Acquaintances.TryGetValue(citizen, out var a)) return 0;
        int learned = 0;
        foreach (var secret in a.UnknownSecrets.ToList())
            if (Rng.Chance(chance) && LearnSecret(citizen, secret)) learned++;
        return learned;
    }

    /// <summary>How much what you know of his shame makes a threat or a squeeze likelier.</summary>
    public double Shame(int citizen) => Math.Min(Content.ShameThreatMost, KnownSecretsOf(citizen).Sum(s => Content.Shame(s.Vice)) * Content.ShameThreat);

    /// <summary>A crime you know he's committed, or null.</summary>
    public Secret? KnownCrime(int citizen) => KnownSecretsOf(citizen).Where(s => Content.IsCrime(s.Vice)).Cast<Secret?>().FirstOrDefault();

    /// <summary>What he pays each week to keep it quiet.</summary>
    public static int HushMoney(int worth) => Math.Max(3, worth / 2);

    /// <summary>"one of their secrets" words.</summary>
    public static string Secrets(int count) => count == 1 ? "something they'd rather nobody knew" : $"{count} things they'd rather nobody knew";
}
