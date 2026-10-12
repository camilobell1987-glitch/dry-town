namespace DryTown.Core;

/// <summary>Marrying into a family: a son or nephew of one boss weds the daughter of another, and the two outfits are family.</summary>
public static partial class Content
{
    /// <summary>What the groom's side pays for the wedding.</summary>
    public const int WeddingCost = 2000;

    /// <summary>
    /// Weeks two outfits must have kept a truce or an alliance before the bride's father will listen,
    /// the grudge he'll overlook, and how much stronger than the groom's outfit his own can be.
    /// </summary>
    public const int MatchPactWeeks = 8, MatchGrudge = 10;
    public const double MatchPower = 2;

    /// <summary>A big wedding puts both bosses in the papers.</summary>
    public const double WeddingRespect = 5;

    /// <summary>How many times worse it is to break faith with family than with a partner.</summary>
    public const int MarriedBreak = 2;

    /// <summary>The loyalty men bring when they come over from a fallen outfit to its family by marriage.</summary>
    public const int InLawLoyalty = 50;

    /// <summary>A rival seeks a match once it has kept a pact this long and has this much money, at this chance when it talks.</summary>
    public const int AiMatchPactWeeks = 8, AiMatchCash = 8000;
    public const double AiMatch = 0.25;

    public static readonly IReadOnlyDictionary<Ethnicity, string[]> Daughters = new Dictionary<Ethnicity, string[]>
    {
        [Ethnicity.Irish] = new[] { "Bridget", "Maureen", "Kathleen", "Nora", "Siobhan", "Margaret" },
        [Ethnicity.Italian] = new[] { "Rosa", "Concetta", "Lucia", "Filomena", "Teresa", "Angela" },
        [Ethnicity.Jewish] = new[] { "Esther", "Miriam", "Sadie", "Rachel", "Ida", "Rose" },
        [Ethnicity.Polish] = new[] { "Jadwiga", "Stella", "Helena", "Agnieszka", "Sophie", "Irena" },
        [Ethnicity.German] = new[] { "Greta", "Liesl", "Hilda", "Frieda", "Anna", "Elsa" },
        [Ethnicity.Unknown] = new[] { "Mary", "Helen", "Ruth", "Dorothy", "Alice", "Grace" },
    };
}

/// <summary>A wedding between two outfits: the groom from one, the bride the daughter of the other's boss.</summary>
public sealed class Marriage
{
    public int GroomGangId { get; set; }
    public int BrideGangId { get; set; }
    public int GroomHoodId { get; set; }
    public string Bride { get; set; } = "";
    public int Week { get; set; }

    public bool Involves(int gangId) => GroomGangId == gangId || BrideGangId == gangId;
    public int Other(int gangId) => GroomGangId == gangId ? BrideGangId : GroomGangId;
}

public sealed partial class World
{
    public List<Marriage> Marriages { get; } = new();

    public Marriage? MarriageBetween(int a, int b) => a == b ? null : Marriages.FirstOrDefault(m => m.Involves(a) && m.Involves(b));

    /// <summary>The outfit the gang is family with by marriage, while it's still standing. An outfit marries into one family at a time.</summary>
    public IEnumerable<Gang> InLawsOf(int gangId) =>
        Marriages.Where(m => m.Involves(gangId)).Select(m => GangById(m.Other(gangId))).Where(g => g.Alive).Distinct();

    public Marriage? MarriageOf(Hood h) => Marriages.FirstOrDefault(m => m.GroomHoodId == h.Id);

    /// <summary>The son or nephew the gang would send to the altar: the heir first if he's family, then the eldest.</summary>
    public Hood? Groom(Gang g) => HoodsOf(g.Id)
        .Where(h => h.Family && h.IsActive && h.Id != g.BossHoodId && MarriageOf(h) == null)
        .OrderByDescending(h => h.Id == g.HeirHoodId).ThenBy(h => h.BornWeek).ThenBy(h => h.Id).FirstOrDefault();

    /// <summary>Why the gang can't put a match to anyone, or null.</summary>
    public string? CannotMatch(Gang g)
    {
        if (InLawsOf(g.Id).FirstOrDefault() is { } family) return $"{g.Name} is already family with {family.Name}.";
        if (Groom(g) == null) return $"{g.Name} has no son or nephew free to marry. Bring family into the business first.";
        return g.Cash < Content.WeddingCost ? $"The wedding costs ${Content.WeddingCost:N0}." : null;
    }

    /// <summary>Why <paramref name="to"/>'s boss won't give his daughter to <paramref name="from"/>'s son, or null if he will.</summary>
    public string? MatchRefusal(Gang from, Gang to)
    {
        if (from.Id == to.Id || !to.Alive) return "There's nobody to talk to.";
        if (MarriageBetween(from.Id, to.Id) != null) return $"You're already family with {to.Name}.";
        if (CannotMatch(from) is { } no) return no;
        if (InLawsOf(to.Id).FirstOrDefault() is { } theirs) return $"{to.Name} is already family with {theirs.Name}.";
        var pact = PactBetween(from.Id, to.Id);
        if (pact == null || Week - pact.SignedWeek < Content.MatchPactWeeks)
            return $"{HoodById(to.BossHoodId).Name} doesn't know {from.Name} well enough. Keep the peace with them for {Content.MatchPactWeeks} weeks first.";
        if (Untrusted(from)) return $"{to.Name} won't give a daughter to an outfit that broke its word this year.";
        if (GrudgeOf(to.Id, from.Id) > Content.MatchGrudge) return $"There's still bad blood. {to.Name} won't hear of it.";
        if (Power(to) > Power(from) * Content.MatchPower) return $"{HoodById(to.BossHoodId).Name} won't marry his daughter beneath him. {from.Name} is too small.";
        return null;
    }

    /// <summary>The wedding. The two outfits are allies for as long as the marriage lasts.</summary>
    public Marriage Wed(Gang from, Gang to)
    {
        var groom = Groom(from)!;
        var father = HoodById(to.BossHoodId);
        var names = Content.Daughters[Content.Daughters.ContainsKey(father.Origin) ? father.Origin : Ethnicity.Unknown];
        string bride = $"{names[Rng.Range(0, names.Length - 1)]} {Surname(father.Name)}";
        from.Cash -= Content.WeddingCost;
        Ledger(from.Id).Spending += Content.WeddingCost;
        var m = new Marriage { GroomGangId = from.Id, BrideGangId = to.Id, GroomHoodId = groom.Id, Bride = bride, Week = Week };
        Marriages.Add(m);
        Sign(from, to, PactKind.Alliance, text: $"{groom.Name} of {from.Name} married {bride}, the daughter of {father.Name} of {to.Name}. The two outfits are family now.");
        Renown(from, respect: Content.WeddingRespect);
        Renown(to, respect: Content.WeddingRespect);
        return m;
    }

    /// <summary>Put a match to a rival. They answer at once. Returns what they said.</summary>
    public string ProposeMatch(Gang from, Gang to)
    {
        if (MatchRefusal(from, to) is { } no) return no;
        var m = Wed(from, to);
        return $"{HoodById(to.BossHoodId).Name} said yes. {HoodById(m.GroomHoodId).Name} married {m.Bride}, and you're family with {to.Name} now: allies for as long as the marriage lasts.";
    }

    /// <summary>A married pact never runs out.</summary>
    private void Bind(Pact pact) => pact.UntilWeek = int.MaxValue / 2;

    /// <summary>
    /// Family stays allied: a pact torn up by anything but a betrayal, such as the Commission throwing
    /// one side out, is made good again on Sunday.
    /// </summary>
    public void KeepFamily()
    {
        Marriages.RemoveAll(m => !GangById(m.GroomGangId).Alive || !GangById(m.BrideGangId).Alive);
        foreach (var m in Marriages)
            if (PactBetween(m.GroomGangId, m.BrideGangId) is not { Kind: PactKind.Alliance } pact || pact.UntilWeek < int.MaxValue / 4)
                Sign(GangById(m.GroomGangId), GangById(m.BrideGangId), PactKind.Alliance, text: "");
    }

    /// <summary>
    /// A finished outfit's men and turf go to its family by marriage, unless that family already runs a
    /// third of the city. Returns the outfit that took them in, or null.
    /// </summary>
    public Gang? TakeIn(Gang gang, List<Hood> men)
    {
        var family = InLawsOf(gang.Id).Where(g => !TopDog(g)).OrderBy(g => g.Id).FirstOrDefault();
        if (family == null) return null;
        foreach (var h in men)
        {
            h.GangId = family.Id;
            h.Loyalty = Math.Min(h.Loyalty, Content.InLawLoyalty);
            LeaveCrew(h.Id);
        }
        foreach (var biz in TurfOf(gang.Id).ToList())
        {
            biz.ProtectorGangId = family.Id;
            if (!men.Any(h => h.Id == biz.HandlerHoodId)) biz.HandlerHoodId = -1;
        }
        foreach (var biz in FrontsOf(gang.Id).ToList()) biz.OwnerGangId = family.Id;
        if (gang.Cash > 0) family.Cash += gang.Cash;
        gang.Cash = 0;
        return family;
    }
}
