namespace DryTown.Core;

/// <summary>What rats and planted men cost, and how likely they are to do what they do.</summary>
public static partial class Content
{
    /// <summary>
    /// Below this loyalty a man in a cell listens when the district attorney offers him a deal.
    /// The longer the stretch he's looking at, the more loyalty it takes to sit it out.
    /// </summary>
    public const int RatLoyalty = 35;

    public static int RatLine(Hood man) =>
        RatLoyalty + (man.Case is { Life: true } ? 25 : Math.Min(25, (man.Case?.Sentence ?? man.JailWeeks) / 4));

    /// <summary>Below this loyalty a man on the street may start telling the police what his outfit is up to. Above <see cref="InformerQuits"/> he stops.</summary>
    public const int InformerLoyalty = 30, InformerQuits = 50;

    /// <summary>Weeks between a man starting to talk to the DA and signing his deal.</summary>
    public const int TalkingWeeks = 2;

    /// <summary>Weeks a man who has been found out waits for the knock before he runs.</summary>
    public const int RunWeeks = 2;

    /// <summary>How many of his friends a rat names, and how sure a jury is of a man named by one.</summary>
    public const int RatNames = 2;
    public const double RatTestimony = 0.8, RatBossTestimony = 0.65;

    /// <summary>Loyalty bought by looking after a jailed man's family.</summary>
    public const int FamilyLoyalty = 30;

    /// <summary>What his family needs: more for a man looking at a long stretch.</summary>
    public static int FamilyCare(Hood man) =>
        man.Case is { Life: true } ? 1500 : 150 + Math.Min(200, man.Case?.Sentence ?? man.JailWeeks) * 6;

    /// <summary>
    /// What a man inside a rival outfit is worth. His own side's jobs against it go better, and
    /// he warns them when it moves against them.
    /// </summary>
    public const double InsideHit = 0.15, InsideBossHit = 0.3, InsideJob = 0.15, TippedOff = 0.15;
    public const int InsideStorm = 2, TippedOffDefence = 2;
}

/// <summary>Who is talking and who is working for someone else.</summary>
public sealed partial class World
{
    /// <summary>The men a gang has planted in other outfits.</summary>
    public IEnumerable<Hood> PlantsOf(int gangId) =>
        Hoods.Where(h => h.GangId == gangId && h.State == HoodState.Inside).OrderBy(h => h.InsideSinceWeek).ThenBy(h => h.Id);

    /// <summary>Whether a gang has a man inside another who hasn't been found out.</summary>
    public bool HasManInside(int gangId, int hostGangId) =>
        gangId != hostGangId && Hoods.Any(h => h.State == HoodState.Inside && h.GangId == gangId && h.InsideGangId == hostGangId && !h.Exposed);

    /// <summary>Rats a gang has found out and not yet dealt with: its own informers, and rivals' men planted in it.</summary>
    public IEnumerable<Hood> ExposedIn(int gangId) =>
        Hoods.Where(h => h.Exposed && (h.State == HoodState.Free && h.GangId == gangId && h.Informer
            || h.State == HoodState.Inside && h.InsideGangId == gangId));

    /// <summary>The gang's men in a cell who are talking to the district attorney.</summary>
    public IEnumerable<Hood> TalkingOf(int gangId) =>
        Hoods.Where(h => h.GangId == gangId && h.State == HoodState.Jailed && h.Talking > 0);

    /// <summary>Why a man can't be planted in a rival outfit, or null if he can.</summary>
    public string? CannotPlant(Gang gang, Hood man, Gang host)
    {
        if (man.GangId != gang.Id || !man.IsAvailable) return "He isn't free.";
        if (man.Id == gang.BossHoodId || man.Id == gang.HeirHoodId) return "The boss and his heir are too well known.";
        if (!host.Alive || host.Id == gang.Id) return "There's no such outfit.";
        if (HasManInside(gang.Id, host.Id)) return "You already have a man inside.";
        return null;
    }

    /// <summary>Why the gang can't look after this man's family, or null if it can.</summary>
    public string? CannotLookAfter(Gang gang, Hood man)
    {
        if (man.GangId != gang.Id || man.State != HoodState.Jailed) return "He isn't in jail.";
        if (man.LookedAfter) return "His family is already looked after.";
        int price = Content.FamilyCare(man);
        return gang.Cash < price ? $"His family needs ${price:N0}." : null;
    }

    /// <summary>Why this man can't be seen to, or null if he can.</summary>
    public string? CannotSeeTo(Gang gang, Hood target)
    {
        if (target.State == HoodState.Jailed && target.GangId == gang.Id) return target.Talking > 0 ? null : "He isn't talking.";
        if (target.Exposed && target.State == HoodState.Free && target.GangId == gang.Id && target.Informer) return null;
        if (target.Exposed && target.State == HoodState.Inside && target.InsideGangId == gang.Id) return null;
        return "He isn't a rat.";
    }

    /// <summary>Bring a planted man home. He goes back to work for his own outfit at once.</summary>
    public void Recall(Gang gang, Hood man)
    {
        if (man.GangId != gang.Id || man.State != HoodState.Inside) return;
        var host = GangById(man.InsideGangId);
        man.State = HoodState.Free;
        man.InsideGangId = -1;
        man.ExposedWeek = -1;
        Log(EventKind.Whisper, gang.Id, $"{man.Name} walked away from {host.Name} and came home to {gang.Name}.");
    }

    /// <summary>Look after a jailed man's family. Returns what it cost, or -1.</summary>
    public int LookAfter(Gang gang, Hood man)
    {
        if (CannotLookAfter(gang, man) != null) return -1;
        int price = Content.FamilyCare(man);
        gang.Cash -= price;
        Ledger(gang.Id).Spending += price;
        man.LookedAfter = true;
        man.Loyalty = Math.Min(100, man.Loyalty + Content.FamilyLoyalty);
        Log(EventKind.Whisper, gang.Id, $"{gang.Name} sent ${price:N0} to {man.Name}'s family while he's inside.");
        return price;
    }
}
