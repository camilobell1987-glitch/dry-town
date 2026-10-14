namespace DryTown.Core;

/// <summary>What sort of person someone is, in the words of the day.</summary>
public enum Trait { Pious, OnTheTake, HardBoiled, Yellow, Blabbermouth, Clam, SquareShooter, Hothead, Sap, Sharp, Tightwad, FamilyMan, Souse }

/// <summary>Where someone came from, which makes some traits likelier than others.</summary>
public enum Background { None, Doughboy, Orphan, Greenhorn, Jailbird, PreachersKid, OldMoney, Hayseed, UnionMan }

/// <summary>Someone's background and the two or three traits that sum them up.</summary>
public sealed record Character(Background Background, IReadOnlyList<Trait> Traits);

public static partial class Content
{
    public static string Label(Trait t) => t switch
    {
        Trait.Pious => "Pious",
        Trait.OnTheTake => "On the take",
        Trait.HardBoiled => "Hard-boiled",
        Trait.Yellow => "Yellow",
        Trait.Blabbermouth => "Blabbermouth",
        Trait.Clam => "Clam",
        Trait.SquareShooter => "Square shooter",
        Trait.Hothead => "Hothead",
        Trait.Sap => "Sap",
        Trait.Sharp => "Sharp as a tack",
        Trait.Tightwad => "Tightwad",
        Trait.FamilyMan => "Family first",
        _ => "Souse",
    };

    /// <summary>What a trait means for anyone dealing with them.</summary>
    public static string Meaning(Trait t) => t switch
    {
        Trait.Pious => "Goes to church every Sunday. Won't take a bribe or look the other way, and has little to hide.",
        Trait.OnTheTake => "Has a price, and everyone knows it. Takes a bribe gladly.",
        Trait.HardBoiled => "Tough as old boots. Threats and rough stuff don't move them much, and they fight back.",
        Trait.Yellow => "Scares easy. Folds the minute anyone leans on them.",
        Trait.Blabbermouth => "Can't keep a thing to themselves. Easy to find out about, and they spill what they hear about others.",
        Trait.Clam => "Keeps their trap shut. Hard to find out about, and no use as an informant.",
        Trait.SquareShooter => "Honest as the day is long. Runs to the cops if you try anything and fail.",
        Trait.Hothead => "Flies off the handle. Hard to lean on, and liable to start swinging.",
        Trait.Sap => "Believes anything. Easy to buy, easy to squeeze.",
        Trait.Sharp => "Nobody's fool. Spots a tail and calls a bluff.",
        Trait.Tightwad => "Pinches every penny. A bribe has to be worth their while.",
        Trait.FamilyMan => "Would do anything for their own. Threats to the family work wonders.",
        _ => "Never far from a bottle. Talks too much when they drink.",
    };

    public static string Label(Background b) => b switch
    {
        Background.Doughboy => "Doughboy",
        Background.Orphan => "Orphan",
        Background.Greenhorn => "Greenhorn",
        Background.Jailbird => "Jailbird",
        Background.PreachersKid => "Preacher's kid",
        Background.OldMoney => "Old money",
        Background.Hayseed => "Hayseed",
        Background.UnionMan => "Union man",
        _ => "Nothing out of the ordinary",
    };

    public static string Meaning(Background b) => b switch
    {
        Background.Doughboy => "Went over there with the AEF and came back changed.",
        Background.Orphan => "Grew up in an orphan asylum and learned to look out for number one.",
        Background.Greenhorn => "Fresh off the boat, and wary of the law and of strangers.",
        Background.Jailbird => "Did a stretch upstate. Knows how things work, and won't talk to cops.",
        Background.PreachersKid => "Raised in the church, with the Good Book never far away.",
        Background.OldMoney => "Born to money, and never had to fight for anything.",
        Background.Hayseed => "Came in from the farm not long ago and is still green to the city.",
        Background.UnionMan => "Carries a union card and stands with his brothers.",
        _ => "",
    };

    /// <summary>How common each trait is before background is counted.</summary>
    private static double TraitWeight(Trait t) => t switch
    {
        Trait.Pious or Trait.OnTheTake or Trait.Blabbermouth or Trait.Tightwad or Trait.FamilyMan => 1.2,
        Trait.HardBoiled or Trait.Yellow or Trait.Clam or Trait.SquareShooter or Trait.Hothead or Trait.Sap or Trait.Souse => 1,
        _ => 0.8,
    };

    /// <summary>How much a background makes a trait likelier (above 1) or rarer (below 1).</summary>
    public static double Leaning(Background b, Trait t) => (b, t) switch
    {
        (Background.Doughboy, Trait.HardBoiled) => 3,
        (Background.Doughboy, Trait.Hothead) => 2,
        (Background.Doughboy, Trait.Souse) => 1.5,
        (Background.Doughboy, Trait.Yellow) => 0.2,
        (Background.Orphan, Trait.Clam) => 2,
        (Background.Orphan, Trait.OnTheTake) => 1.8,
        (Background.Orphan, Trait.HardBoiled) => 1.5,
        (Background.Orphan, Trait.FamilyMan) => 0.5,
        (Background.Greenhorn, Trait.Sap) => 2,
        (Background.Greenhorn, Trait.Clam) => 1.5,
        (Background.Greenhorn, Trait.Pious) => 1.5,
        (Background.Greenhorn, Trait.FamilyMan) => 1.5,
        (Background.Jailbird, Trait.Clam) => 3,
        (Background.Jailbird, Trait.OnTheTake) => 2,
        (Background.Jailbird, Trait.HardBoiled) => 1.5,
        (Background.Jailbird, Trait.SquareShooter) => 0,
        (Background.Jailbird, Trait.Pious) => 0.3,
        (Background.PreachersKid, Trait.Pious) => 4,
        (Background.PreachersKid, Trait.SquareShooter) => 2,
        (Background.PreachersKid, Trait.Souse) => 0.5,
        (Background.PreachersKid, Trait.OnTheTake) => 0.3,
        (Background.OldMoney, Trait.Tightwad) => 2,
        (Background.OldMoney, Trait.Sap) => 1.5,
        (Background.OldMoney, Trait.Yellow) => 1.5,
        (Background.OldMoney, Trait.HardBoiled) => 0.5,
        (Background.Hayseed, Trait.Sap) => 2.5,
        (Background.Hayseed, Trait.SquareShooter) => 1.5,
        (Background.Hayseed, Trait.Pious) => 1.5,
        (Background.Hayseed, Trait.Sharp) => 0.3,
        (Background.UnionMan, Trait.HardBoiled) => 1.5,
        (Background.UnionMan, Trait.Hothead) => 1.5,
        (Background.UnionMan, Trait.Clam) => 1.5,
        (Background.UnionMan, Trait.Yellow) => 0.5,
        _ => 1,
    };

    /// <summary>Traits that can't both describe one person.</summary>
    public static bool Clash(Trait a, Trait b) => (a, b) switch
    {
        (Trait.HardBoiled, Trait.Yellow) or (Trait.Yellow, Trait.HardBoiled) => true,
        (Trait.Blabbermouth, Trait.Clam) or (Trait.Clam, Trait.Blabbermouth) => true,
        (Trait.Pious, Trait.OnTheTake) or (Trait.OnTheTake, Trait.Pious) => true,
        (Trait.Pious, Trait.Souse) or (Trait.Souse, Trait.Pious) => true,
        (Trait.SquareShooter, Trait.OnTheTake) or (Trait.OnTheTake, Trait.SquareShooter) => true,
        (Trait.Sap, Trait.Sharp) or (Trait.Sharp, Trait.Sap) => true,
        (Trait.Hothead, Trait.Yellow) or (Trait.Yellow, Trait.Hothead) => true,
        _ => false,
    };

    /// <summary>
    /// How much a trait changes the odds of each way of dealing with someone. The pious and the tightwad don't take
    /// money; the hard-boiled and the hothead don't scare; the yellow and the sap fold.
    /// </summary>
    public static double TraitOdds(Trait t, Lean lean) => (t, lean) switch
    {
        (Trait.Pious, Lean.Bribe) => -0.35,
        (Trait.Pious, Lean.Blackmail) => -0.1,
        (Trait.OnTheTake, Lean.Bribe) => 0.25,
        (Trait.Tightwad, Lean.Bribe) => -0.15,
        (Trait.Sap, Lean.Bribe or Lean.Blackmail) => 0.12,
        (Trait.Sharp, Lean.Blackmail) => -0.12,
        (Trait.HardBoiled, Lean.Intimidate or Lean.Extort) => -0.2,
        (Trait.HardBoiled, Lean.Kidnap or Lean.Kill) => -0.1,
        (Trait.Yellow, Lean.Intimidate or Lean.Extort) => 0.22,
        (Trait.Yellow, Lean.Blackmail) => 0.12,
        (Trait.Hothead, Lean.Intimidate or Lean.Extort) => -0.12,
        (Trait.Hothead, Lean.Kidnap) => -0.1,
        (Trait.SquareShooter, Lean.Bribe) => -0.15,
        (Trait.OnTheTake, Lean.Square) => 0.15,
        (Trait.Tightwad or Trait.Pious, Lean.Square) => 0.1,
        (Trait.Hothead or Trait.HardBoiled, Lean.Square) => -0.1,
        _ => 0,
    };

    /// <summary>How much easier it is to learn about someone with this trait, by asking, tailing or gossip.</summary>
    public static double Talk(Trait t) => t switch
    {
        Trait.Blabbermouth => 0.15,
        Trait.Souse => 0.06,
        Trait.Clam => -0.12,
        _ => 0,
    };

    /// <summary>How much someone in your pocket counts as an informant: a blabbermouth tells you twice as much, a clam half.</summary>
    public static double AsInformant(IEnumerable<Trait> traits) =>
        traits.Contains(Trait.Blabbermouth) ? 2 : traits.Contains(Trait.Clam) ? 0.5 : 1;

    /// <summary>Extra heat when an attempt on someone fails, because they go straight to the cops, or less if they never would.</summary>
    public static int FailHeat(IEnumerable<Trait> traits, Background b) =>
        (traits.Contains(Trait.SquareShooter) ? 2 : 0) + (traits.Contains(Trait.Pious) ? 1 : 0)
        - (traits.Contains(Trait.Clam) || b == Background.Jailbird ? 1 : 0);

    /// <summary>The weighted chance of each trait for someone of this background, for picking.</summary>
    public static double TraitChance(Background b, Trait t) => TraitWeight(t) * Leaning(b, t);
}

public sealed partial class CityDirectory
{
    private readonly Dictionary<int, Character> _characters = new();

    /// <summary>
    /// Someone's background and traits. About half of grown-ups have a background worth telling; everyone has two
    /// traits, some three, picked with their background's leanings. Children have one.
    /// </summary>
    public Character CharacterOf(int id)
    {
        if (_characters.TryGetValue(id, out var known)) return known;
        var c = Get(id);
        var background = Background.None;
        if (c.Age >= 16 && Roll(id, 500) < 0.5)
        {
            var options = new List<Background> { Background.Orphan, Background.Hayseed };
            // Doughboys went to France in 1917 and 1918; union men and jailbirds are grown men and women.
            if (!c.Woman && c.Age is >= 20 and <= 48) options.AddRange(new[] { Background.Doughboy, Background.Doughboy, Background.Doughboy });
            if (c.Age >= 20) options.AddRange(new[] { Background.Jailbird, Background.PreachersKid, Background.UnionMan });
            if (c.Worth >= 40 || c.Owns.Count > 0) options.AddRange(new[] { Background.OldMoney, Background.OldMoney });
            // Greenhorns are of the immigrant peoples.
            if (c.Origin is Ethnicity.Italian or Ethnicity.Jewish or Ethnicity.Polish or Ethnicity.Irish or Ethnicity.German) options.AddRange(new[] { Background.Greenhorn, Background.Greenhorn });
            background = options[(int)(Roll(id, 501) * options.Count)];
        }
        int count = c.Age < 16 ? 1 : Roll(id, 502) < 0.25 ? 3 : 2;
        var traits = new List<Trait>();
        var all = Enum.GetValues<Trait>();
        for (int k = 0; k < count; k++)
        {
            var open = all.Where(t => !traits.Contains(t) && !traits.Any(x => Content.Clash(x, t))).ToList();
            double total = open.Sum(t => Content.TraitChance(background, t)), roll = Roll(id, 503 + k) * total;
            foreach (var t in open)
            {
                roll -= Content.TraitChance(background, t);
                if (roll > 0) continue;
                traits.Add(t);
                break;
            }
        }
        var character = new Character(background, traits);
        _characters[id] = character;
        return character;
    }
}

public sealed partial class Acquaintance
{
    /// <summary>His background and traits, from the directory, and the traits you've seen in him.</summary>
    public Background Background { get; set; }
    public List<Trait> Traits { get; set; } = new();
    public List<Trait> KnownTraits { get; set; } = new();

    public IEnumerable<Trait> UnknownTraits => Traits.Where(t => !KnownTraits.Contains(t));
}

public sealed partial class World
{
    /// <summary>Note what sort of person someone is, from the directory, the first time you come across him.</summary>
    public Acquaintance Introduce(int citizen, int wardId, IReadOnlyList<Tie> ties, IReadOnlyList<Secret> secrets, Character character, int business = -1)
    {
        var a = Introduce(citizen, wardId, ties, secrets);
        // The shop they run, so what happens to it can be held against whoever did it.
        if (a.BusinessId < 0) a.BusinessId = business;
        if (a.Traits.Count == 0 && character.Traits.Count > 0)
        {
            a.Traits = character.Traits.ToList();
            a.Background = character.Background;
        }
        return a;
    }

    public IEnumerable<Trait> KnownTraitsOf(int citizen) =>
        Acquaintances.TryGetValue(citizen, out var a) ? a.KnownTraits : Enumerable.Empty<Trait>();

    public bool LearnTrait(int citizen, Trait trait)
    {
        var a = Meet(citizen, -1);
        if (!a.Traits.Contains(trait) || a.KnownTraits.Contains(trait)) return false;
        a.KnownTraits.Add(trait);
        return true;
    }

    /// <summary>Find out each of someone's unknown traits with this chance. Returns how many were new.</summary>
    public int LearnTraits(int citizen, double chance)
    {
        if (!Acquaintances.TryGetValue(citizen, out var a)) return 0;
        int learned = 0;
        foreach (var t in a.UnknownTraits.ToList())
            if (Rng.Chance(chance) && LearnTrait(citizen, t)) learned++;
        return learned;
    }

    /// <summary>How much someone's traits change the odds of a way of dealing with them: all of them, or only the ones you know.</summary>
    public double TraitOdds(int citizen, Lean lean, bool knownOnly)
    {
        if (!Acquaintances.TryGetValue(citizen, out var a)) return 0;
        return (knownOnly ? a.KnownTraits : a.Traits).Sum(t => Content.TraitOdds(t, lean));
    }

    /// <summary>How much easier someone is to find out about for their traits.</summary>
    public double TalkOf(int citizen) => Acquaintances.TryGetValue(citizen, out var a) ? a.Traits.Sum(Content.Talk) : 0;

    /// <summary>"what sort they are" words for traits just seen.</summary>
    public static string Traits(int count) => count == 1 ? "what sort they are" : "a good deal about what sort they are";
}
