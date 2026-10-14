namespace DryTown.Core;

/// <summary>
/// What sort of man each gangster is, as the residents have a sort: a background and two or three traits. In a
/// gangster they show in whether he talks to the DA or the police, whether another outfit can buy him away, how hard
/// he is to put down, and how he takes it when his friends are killed.
/// </summary>
public static partial class Content
{
    /// <summary>The traits a gangster can have. Nobody squares up to the law, gets fooled or wants in who is already in.</summary>
    public static readonly Trait[] HoodTraits =
    {
        Trait.HardBoiled, Trait.Yellow, Trait.Blabbermouth, Trait.Clam, Trait.Hothead, Trait.Sharp, Trait.FamilyMan, Trait.Souse,
        Trait.StoolPigeon, Trait.TwoTimer, Trait.HoldsAGrudge, Trait.StandUpGuy, Trait.Sport, Trait.Sheik, Trait.OnTheTake, Trait.Pious,
    };

    /// <summary>The backgrounds a gangster can have. Old money rarely ends up in the rackets.</summary>
    private static readonly Background[] HoodBackgrounds =
    {
        Background.Doughboy, Background.Doughboy, Background.Orphan, Background.Orphan, Background.Jailbird, Background.Jailbird,
        Background.Jailbird, Background.Greenhorn, Background.Hayseed, Background.UnionMan, Background.PreachersKid, Background.OldMoney,
    };

    /// <summary>What a trait means in one of the outfit's men.</summary>
    public static string HoodMeaning(Trait t) => t switch
    {
        Trait.HardBoiled => "Tough as old boots, and shoots back. Harder to put down in a hit.",
        Trait.Yellow => "Loses his nerve when it counts. Quicker to take the DA's deal when he's facing time.",
        Trait.Blabbermouth => "Can't keep a thing to himself. Likelier to take the DA's deal or start whispering to the police.",
        Trait.Clam => "Never says a word to anyone. The DA and the police get little out of him.",
        Trait.Hothead => "Flies off the handle. Takes a wrong to heart, and goes twice as hard after a man he has it in for.",
        Trait.Sharp => "Nobody's fool. Sees trouble coming, so he's harder to put down in a hit.",
        Trait.FamilyMan => "Would do anything for his own. Look after his family while he's inside and the DA won't get near him.",
        Trait.Souse => "Never far from a bottle, and talks in his cups. Likelier to start whispering to the police.",
        Trait.StoolPigeon => "Sings to the cops. Twice as likely to start informing, and quick to take the DA's deal.",
        Trait.TwoTimer => "Goes wherever the money is. Easier for another outfit to buy away.",
        Trait.HoldsAGrudge => "Never forgets a wrong. Takes things harder than most, and his grudges never fade on their own.",
        Trait.StandUpGuy => "Keeps his word and his mouth shut. Never informs, and the DA gets nowhere with him.",
        Trait.Sport => "Can't keep away from the card table or the track. Always short, so another outfit's money talks.",
        Trait.Sheik => "Fast living and sweet talk. Dresses sharp, and never short of an admirer.",
        Trait.OnTheTake => "Has a price, like everyone in this town. Easier for another outfit to buy away.",
        Trait.Pious => "Goes to Mass every Sunday and lights a candle for the men he's hurt. His grudges fade fast.",
        _ => Meaning(t),
    };

    /// <summary>How much longer a man holds out against the DA for the sort he is, or how much sooner he folds.</summary>
    public static int Steadfast(Hood man) =>
        (man.Traits.Contains(Trait.StandUpGuy) ? 25 : 0) + (man.Traits.Contains(Trait.Clam) ? 15 : 0) + (man.Background == Background.Jailbird ? 10 : 0)
        + (man.Traits.Contains(Trait.FamilyMan) && man.LookedAfter ? 15 : 0)
        - (man.Traits.Contains(Trait.StoolPigeon) ? 15 : 0) - (man.Traits.Contains(Trait.Yellow) ? 10 : 0) - (man.Traits.Contains(Trait.Blabbermouth) ? 5 : 0);

    /// <summary>How much likelier a man is to start whispering to the police for the sort he is. A stand-up guy never does.</summary>
    public static double Leaky(Hood man) =>
        man.Traits.Contains(Trait.StandUpGuy) ? 0
        : (man.Traits.Contains(Trait.StoolPigeon) ? 2 : 1) * (man.Traits.Contains(Trait.Blabbermouth) || man.Traits.Contains(Trait.Souse) ? 1.5 : 1)
          * (man.Traits.Contains(Trait.Clam) ? 0.5 : 1);

    /// <summary>How much easier another outfit finds it to buy a man away.</summary>
    public static double Buyable(Hood man) =>
        (man.Traits.Contains(Trait.TwoTimer) ? 0.15 : 0) + (man.Traits.Contains(Trait.OnTheTake) ? 0.1 : 0) + (man.Traits.Contains(Trait.Sport) ? 0.05 : 0)
        - (man.Traits.Contains(Trait.StandUpGuy) ? 0.2 : 0);

    /// <summary>How much harder a man is to put down in a hit.</summary>
    public static double HardToKill(Hood man) =>
        (man.Traits.Contains(Trait.HardBoiled) ? 0.04 : 0) + (man.Traits.Contains(Trait.Sharp) ? 0.04 : 0);

    /// <summary>How much likelier a hit comes off when the man sent has it in for the target or his outfit.</summary>
    public const double Wanted = 0.06;

    /// <summary>How badly the outfit takes it when one of its men is killed: his crew and his family worst.</summary>
    public const int MournGang = 15, MournCrew = 35, MournFamily = 50;

    /// <summary>
    /// A gangster's background and traits, the same every time for the same man in the same game, so giving them out
    /// doesn't change anything else that happens.
    /// </summary>
    public static (Background Background, List<Trait> Traits) HoodCharacter(ulong seed, int hoodId, int age)
    {
        double Roll(int salt)
        {
            ulong h = seed * 0x9E3779B97F4A7C15ul ^ (ulong)(uint)hoodId * 0xBF58476D1CE4E5B9ul ^ (ulong)(uint)(salt + 7000) * 0x94D049BB133111EBul;
            h ^= h >> 31;
            h *= 0xD6E8FEB86659FD93ul;
            h ^= h >> 32;
            return (h >> 11) * (1.0 / (1ul << 53));
        }
        var background = Background.None;
        if (Roll(0) < 0.6)
        {
            var options = HoodBackgrounds.Where(b => b != Background.Doughboy || age is >= 20 and <= 48).ToList();
            background = options[(int)(Roll(1) * options.Count)];
        }
        int count = Roll(2) < 0.3 ? 3 : 2;
        var traits = new List<Trait>();
        for (int k = 0; k < count; k++)
        {
            var open = HoodTraits.Where(t => !traits.Contains(t) && !traits.Any(x => Clash(x, t))).ToList();
            double total = open.Sum(t => TraitChance(background, t)), roll = Roll(3 + k) * total;
            foreach (var t in open)
            {
                roll -= TraitChance(background, t);
                if (roll > 0) continue;
                traits.Add(t);
                break;
            }
        }
        return (background, traits);
    }
}

public sealed partial class World
{
    /// <summary>Give a man his background and traits, if he hasn't got them yet.</summary>
    public void Characterise(Hood man)
    {
        if (man.Traits.Count > 0) return;
        (man.Background, man.Traits) = Content.HoodCharacter(Settings.Seed, man.Id, Math.Max(16, man.Age(Week)));
    }

    /// <summary>Whether a man has it in for another, or for the outfit he's with, badly enough to want the job.</summary>
    public bool Wants(Hood man, Hood target) =>
        HeldBy(Party.Hood, man.Id).Any(g => g.Amount >= Content.ScowlingAt
            && (g.Target == Party.Hood && g.TargetId == target.Id || g.Target == Party.Gang && g.TargetId == target.GangId));

    /// <summary>
    /// One of an outfit's men has been killed by another outfit: his friends take it to heart, against the outfit and the
    /// man who did it. His crew and his family take it worst, and never quite get over it.
    /// </summary>
    public void Mourn(Hood dead, Gang by, Hood? killer)
    {
        if (by.Id == dead.GangId) return;
        var crew = CrewOfHood(dead.Id);
        string surname = Surname(dead.Name);
        foreach (var man in HoodsOf(dead.GangId).Where(h => h.Id != dead.Id).OrderBy(h => h.Id).ToList())
        {
            bool family = (dead.Family || man.Family) && Surname(man.Name) == surname;
            bool crewmate = crew != null && crew.Everyone.Contains(man.Id);
            int amount = family ? Content.MournFamily : crewmate ? Content.MournCrew : Content.MournGang;
            // You know your own men's minds, and anyone who has it in for you.
            bool known = man.GangId == Player.Id || by.Id == Player.Id;
            string why = $"killed {dead.Name}";
            Bear(Party.Hood, man.Id, Party.Gang, by.Id, amount, why, known: known, family: family, floor: family || crewmate ? 20 : 0);
            if (killer != null && killer.State != HoodState.Dead)
                Bear(Party.Hood, man.Id, Party.Hood, killer.Id, amount, why, known: known, family: family, floor: family || crewmate ? 20 : 0);
        }
    }

    /// <summary>A resident who joins an outfit brings the sort he is and his grudges with him.</summary>
    public void BringAlong(Hood recruit, int citizen)
    {
        // Whatever he had against the outfit he's joined, he's let lie.
        Grievances.RemoveAll(g => g.Holder == Party.Resident && g.HolderId == citizen
            && (g.Target == Party.Gang && g.TargetId == recruit.GangId || g.Target == Party.Hood && Hoods.FirstOrDefault(h => h.Id == g.TargetId)?.GangId == recruit.GangId));
        if (Acquaintances.TryGetValue(citizen, out var a) && a.Traits.Count > 0)
        {
            recruit.Background = a.Background;
            // Wanting in is behind him; if that was all anyone knew of him, he keeps the sort the outfit finds he is.
            var kept = a.Traits.Where(t => t != Trait.WannabeTorpedo).ToList();
            if (kept.Count > 0) recruit.Traits = kept;
        }
        foreach (var g in HeldBy(Party.Resident, citizen).ToList()) { g.Holder = Party.Hood; g.HolderId = recruit.Id; g.Known = true; }
        foreach (var g in HeldAgainst(Party.Resident, citizen).ToList()) { g.Target = Party.Hood; g.TargetId = recruit.Id; }
    }
}
