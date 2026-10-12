namespace DryTown.Core;

/// <summary>Reputation: the boss's name on the street and in the papers, feared or admired.</summary>
public static partial class Content
{
    /// <summary>What a killing, a boss killed, a fire set and a shop taken by force add to the boss's fear.</summary>
    public const double FearHit = 6, FearBossHit = 15, FearArson = 2, FearTakeover = 0.5;

    /// <summary>What an acquittal, the boss's own acquittal and a jailed man's family looked after add to his respect, and what breaking his word costs.</summary>
    public const double RespectAcquittal = 3, RespectBossAcquittal = 8, RespectCare = 2, RespectBrokenWord = 10;

    /// <summary>The share of a name that's left each week: people forget, slowly.</summary>
    public const double NameFade = 0.99;

    /// <summary>A soup kitchen's week, and the respect it earns: twice as much once the Depression comes.</summary>
    public const int SoupKitchenWeekly = 100;
    public const double SoupRespect = 0.5;

    /// <summary>For each point of fear, what a shakedown gains; for each point of fear and of respect, what comes off a jury's odds.</summary>
    public const double FearShakedown = 0.001, FearJury = 0.001, RespectJury = 0.0005;

    /// <summary>For each point of respect, the loyalty a new man brings with him. A feared boss draws harder men.</summary>
    public const double RespectRecruitLoyalty = 0.2;
    public const int FearedRecruit = 50;

    /// <summary>How big a name has to be to be a name in the papers, and a household name.</summary>
    public const int NameInPapers = 40, HouseholdName = 70;

    /// <summary>A gang's new boss keeps this much of the old one's name.</summary>
    public const double InheritedName = 0.5;

    /// <summary>The planner opens a soup kitchen once the Depression comes, if the outfit has this much cash, and closes it below the second figure.</summary>
    public const int AiSoupCash = 15_000, AiSoupClose = 3_000;
}

public sealed partial class World
{
    /// <summary>How big the boss's name is, 0 to 100, feared and admired together.</summary>
    public static double Fame(Gang g) => Math.Min(100, g.Fear + g.Respect);

    /// <summary>How much more the Bureau and the Untouchables want an outfit with a big name.</summary>
    public static double NameDraw(Gang g) => 1 + Fame(g) / 100;

    /// <summary>The breadlines, from 1930 on.</summary>
    public bool Depression => Year >= 1930;

    /// <summary>Add to the boss's name. Either part stays between 0 and 100.</summary>
    public void Renown(Gang g, double fear = 0, double respect = 0)
    {
        g.Fear = Math.Clamp(g.Fear + fear, 0, 100);
        g.Respect = Math.Clamp(g.Respect + respect, 0, 100);
    }

    public static int Tier(Gang g) => Fame(g) >= Content.HouseholdName ? 2 : Fame(g) >= Content.NameInPapers ? 1 : 0;

    /// <summary>The boss's name in a few words: "feared on the street", "an admired name in the papers".</summary>
    public static string NameOf(Gang g)
    {
        if (Fame(g) < 15) return "hardly known";
        string how = g.Fear >= g.Respect * 2 ? "feared" : g.Respect >= g.Fear * 2 ? "admired" : "feared and admired";
        return Tier(g) switch
        {
            2 => $"a household name, {how}",
            1 => $"a name in the papers, {how}",
            _ => $"{how} on the street",
        };
    }

    public string? CannotRunSoupKitchen(Gang g) =>
        g.SoupKitchen ? null : g.Cash < Content.SoupKitchenWeekly * 4 ? $"A soup kitchen costs ${Content.SoupKitchenWeekly} a week, and the outfit hasn't ${Content.SoupKitchenWeekly * 4} for the first four." : null;

    public double SoupRespect => Content.SoupRespect * (Depression ? 2 : 1);
}

public sealed partial class Simulation
{
    /// <summary>Sunday: a new boss starts with half the old one's name, soup kitchens are paid for, names fade, and a big one makes the papers.</summary>
    private void Reputation()
    {
        var w = World;
        foreach (var g in w.LivingGangs)
        {
            var boss = w.HoodById(g.BossHoodId);
            if (g.NameBossId != g.BossHoodId)
            {
                if (g.NameBossId >= 0)
                {
                    g.Fear *= Content.InheritedName;
                    g.Respect *= Content.InheritedName;
                }
                g.NameBossId = g.BossHoodId;
                g.NameTier = World.Tier(g);
            }

            if (g.SoupKitchen)
            {
                if (g.Cash < Content.SoupKitchenWeekly)
                {
                    g.SoupKitchen = false;
                    w.Log(EventKind.Name, g.Id, $"{g.Name} couldn't pay for the soup kitchen any more, and it closed.");
                }
                else
                {
                    g.Cash -= Content.SoupKitchenWeekly;
                    w.Ledger(g.Id).Spending += Content.SoupKitchenWeekly;
                    w.Renown(g, respect: w.SoupRespect);
                }
            }

            g.Fear *= Content.NameFade;
            g.Respect *= Content.NameFade;
            int tier = World.Tier(g);
            if (tier > g.NameTier)
            {
                string how = g.Fear >= g.Respect ? "the most feared man in the city" : "a friend to the working man";
                w.Log(EventKind.Name, g.Id, tier == 2
                    ? $"Everybody in the city knows the name {boss.Name} now. The papers call him {how}."
                    : $"The papers have started writing about {boss.Name} of {g.Name}. To some of them he's {how}.");
            }
            g.NameTier = Math.Max(g.NameTier, tier);
        }
    }
}
