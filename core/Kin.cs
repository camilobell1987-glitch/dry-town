namespace DryTown.Core;

/// <summary>Finding out who people belong to, and using it against them.</summary>
public static partial class Content
{
    /// <summary>The chance asking around turns up each of someone's ties, and the chance the street mentions one in a week instead of some other fact.</summary>
    public const double AskTie = 0.3, RumourTie = 0.35;

    /// <summary>How much likelier a threat works for each of his family you know of, and the most it can add.</summary>
    public const double FamilyThreat = 0.07, FamilyThreatMost = 0.2;

    /// <summary>How much likelier he does what he's told while you're holding one of his family.</summary>
    public const double HostageThreat = 0.35;

    public static string Label(Kin kin, bool woman) => kin switch
    {
        Kin.Spouse => woman ? "Wife" : "Husband",
        Kin.Child => woman ? "Daughter" : "Son",
        Kin.Parent => woman ? "Mother" : "Father",
        Kin.Sibling => woman ? "Sister" : "Brother",
        Kin.Lover => "Sweetheart",
        _ => "Works for",
    };
}

public sealed partial class Acquaintance
{
    /// <summary>Everyone he's bound to, whether or not you know of them yet, and the ones you do.</summary>
    public List<Tie> Ties { get; set; } = new();
    public List<int> KnownTies { get; set; } = new();

    public IEnumerable<Tie> UnknownTies => Ties.Where(t => !KnownTies.Contains(t.Id));
}

public sealed partial class World
{
    /// <summary>Note who someone is bound to, from the directory, the first time you come across him.</summary>
    public Acquaintance Introduce(int citizen, int wardId, IReadOnlyList<Tie> ties)
    {
        var a = Meet(citizen, wardId);
        if (a.Ties.Count == 0 && ties.Count > 0) a.Ties = ties.ToList();
        return a;
    }

    /// <summary>The ties you know of, with how each is related.</summary>
    public IEnumerable<Tie> KnownTiesOf(int citizen) =>
        Acquaintances.TryGetValue(citizen, out var a) ? a.Ties.Where(t => a.KnownTies.Contains(t.Id)) : Enumerable.Empty<Tie>();

    /// <summary>
    /// Find out one of someone's ties. The relative goes in your directory too, knowing he's bound the other way, and
    /// family living under one roof share an address.
    /// </summary>
    public bool LearnTie(int citizen, Tie tie)
    {
        var a = Meet(citizen, -1);
        if (a.KnownTies.Contains(tie.Id)) return false;
        a.KnownTies.Add(tie.Id);
        var other = Meet(tie.Id, a.WardId);
        if (!other.KnownTies.Contains(citizen)) other.KnownTies.Add(citizen);
        if (tie.Kin != Kin.Employer && a.Known.HasFlag(Fact.Home)) other.Known |= Fact.Home;
        return true;
    }

    /// <summary>Find out each of someone's unknown ties with this chance. Returns how many were new.</summary>
    public int LearnTies(int citizen, double chance, bool family = true, bool work = true)
    {
        if (!Acquaintances.TryGetValue(citizen, out var a)) return 0;
        int learned = 0;
        foreach (var tie in a.UnknownTies.ToList())
            if ((tie.Kin == Kin.Employer ? work : family) && Rng.Chance(chance) && LearnTie(citizen, tie)) learned++;
        return learned;
    }

    /// <summary>The family you know of and are holding, which make a threat against someone likelier to work.</summary>
    public double Leverage(Gang g, int citizen)
    {
        var family = KnownTiesOf(citizen).Where(t => t.Kin != Kin.Employer).ToList();
        double threat = Math.Min(Content.FamilyThreatMost, family.Count * Content.FamilyThreat) + Shame(citizen);
        if (family.Any(t => Acquaintances.TryGetValue(t.Id, out var r) && r.Standing == Standing.Held && r.GangId == g.Id)) threat += Content.HostageThreat;
        return threat;
    }

    /// <summary>One of his family you're holding, or -1.</summary>
    public int HostageFrom(Gang g, int citizen) =>
        KnownTiesOf(citizen).Where(t => t.Kin != Kin.Employer && Acquaintances.TryGetValue(t.Id, out var r) && r.Standing == Standing.Held && r.GangId == g.Id)
            .Select(t => t.Id).DefaultIfEmpty(-1).First();

    /// <summary>Words for ties just found out.</summary>
    public static string Ties(int ties) => ties == 1 ? "someone close to them" : $"{ties} people close to them";
}
