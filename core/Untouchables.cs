namespace DryTown.Core;

/// <summary>The Untouchables: a squad of Prohibition agents nobody can buy, sent after the city's biggest bootlegger.</summary>
public static partial class Content
{
    /// <summary>The year the Prohibition Bureau sends its squad to the city. It stays until repeal.</summary>
    public const int UntouchablesYear = 1927;

    /// <summary>The squad doesn't bother with an outfit running fewer stills and speakeasies than this, counting each supply line as two and each boat as one.</summary>
    public const int SquadRackets = 4;

    /// <summary>Weekly chance the squad raids one of its bootlegger's stills or speakeasies, and what each point of heat adds.</summary>
    public const double SquadRaid = 0.08, SquadRaidPerHeat = 0.001;

    /// <summary>For each point of the handler's stealth, the chance he gets word in time and the place is clean when they come.</summary>
    public const double SquadWarning = 0.04;

    /// <summary>Chance the squad takes the man who handles a place it smashes.</summary>
    public const double SquadArrest = 0.5;

    /// <summary>Smashed places it takes to put the boss before a grand jury, and the weeks without one before the oldest goes stale.</summary>
    public const int SquadIndictment = 5, SquadStaleWeeks = 13;

    /// <summary>How strong the conspiracy case is. Weaker than most federal cases: the squad's evidence is about the stills, not the boss.</summary>
    public const double SquadCase = 0.75;

    public static bool IsLiquor(RacketKind r) => r is RacketKind.Still or RacketKind.Speakeasy;
}

/// <summary>Where the squad stands: who it's after and how much it has on him.</summary>
public sealed class UntouchablesState
{
    public bool Formed { get; set; }
    public bool Disbanded { get; set; }

    /// <summary>The outfit the squad is after, or -1, and the week it chose.</summary>
    public int TargetGangId { get; set; } = -1;
    public int NamedWeek { get; set; } = -1;

    /// <summary>Places smashed that the squad can put before a grand jury, and the week of the last one.</summary>
    public int Evidence { get; set; }
    public int LastRaidWeek { get; set; } = -1;
}

public sealed partial class World
{
    public UntouchablesState Untouchables { get; set; } = new();

    /// <summary>The squad is in the city.</summary>
    public bool SquadInTown => Prohibition && Year >= Content.UntouchablesYear;

    /// <summary>The outfit the squad is after, if it's still standing.</summary>
    public Gang? SquadTarget => SquadInTown && Untouchables.TargetGangId >= 0 && GangById(Untouchables.TargetGangId) is { Alive: true } g ? g : null;

    /// <summary>The gang's stills and speakeasies.</summary>
    public IEnumerable<Business> LiquorOf(int gangId) => TurfOf(gangId).Where(b => Content.IsLiquor(b.Racket));

    /// <summary>How big a bootlegger the squad thinks the gang is.</summary>
    public int Bootlegging(Gang g) => LiquorOf(g.Id).Count() + LinesOf(g.Id).Count() * 2 + BoatsOf(g.Id).Count();

    public double SquadChance(Gang g) => SquadTarget?.Id != g.Id || !LiquorOf(g.Id).Any() ? 0 : Content.SquadRaid + g.Heat * Content.SquadRaidPerHeat;

    /// <summary>The chance the man handling this place gets word in time.</summary>
    public double Warning(Business b) =>
        b.HandlerHoodId >= 0 && Hoods.FirstOrDefault(h => h.Id == b.HandlerHoodId) is { IsAvailable: true } man && man.GangId == b.ProtectorGangId ? man.Stealth * Content.SquadWarning : 0;
}

public sealed partial class Simulation
{
    /// <summary>Sunday during Prohibition, from 1927: the squad picks its bootlegger, raids him, and takes the boss to a grand jury.</summary>
    private void Untouchables()
    {
        var w = World;
        var s = w.Untouchables;
        if (!w.Prohibition)
        {
            if (s.Formed && !s.Disbanded)
            {
                s.Disbanded = true;
                s.TargetGangId = -1;
                w.Log(EventKind.Untouchables, -1, "With repeal, the Untouchables are sent home. There's no law left for them to enforce.");
            }
            return;
        }
        if (!w.SquadInTown) return;
        if (!s.Formed)
        {
            s.Formed = true;
            w.Log(EventKind.Untouchables, -1, "The Prohibition Bureau has sent a squad of its own agents to the city, young men picked because nobody could buy them. The papers call them the Untouchables.");
            Target();
        }

        if (w.SquadTarget == null && s.TargetGangId >= 0) { s.TargetGangId = -1; s.Evidence = 0; }
        bool newYear = w.Week % Content.WeeksPerYear == 0;
        if (newYear || w.SquadTarget == null && w.Week - s.NamedWeek >= Content.SquadStaleWeeks) Target();

        var g = w.SquadTarget;
        if (g == null) return;
        if (s.Evidence > 0 && w.Week - s.LastRaidWeek >= Content.SquadStaleWeeks)
        {
            s.Evidence--;
            s.LastRaidWeek = w.Week;
        }
        if (w.Rng.Chance(w.SquadChance(g))) SquadRaid(g);

        var boss = w.HoodById(g.BossHoodId);
        if (s.Evidence >= Content.SquadIndictment && boss.State != HoodState.Jailed) Indict(g, boss);
    }

    /// <summary>The squad goes after the city's biggest, loudest bootlegger, if it has one worth the trouble.</summary>
    private void Target()
    {
        var w = World;
        var s = w.Untouchables;
        var pick = w.LivingGangs.Select(g => (g, size: w.Bootlegging(g)))
            .Where(x => x.size >= Content.SquadRackets)
            .OrderByDescending(x => x.size * (x.g.Heat + 20) * World.NameDraw(x.g)).ThenBy(x => x.g.Id).Select(x => x.g).FirstOrDefault();
        int was = s.TargetGangId;
        s.NamedWeek = w.Week;
        s.TargetGangId = pick?.Id ?? -1;
        if (pick?.Id == was) return;
        s.Evidence = 0;
        s.LastRaidWeek = w.Week;
        if (pick != null)
            w.Log(EventKind.Untouchables, pick.Id, $"The Untouchables are going after {w.HoodById(pick.BossHoodId).Name} of {pick.Name}, the biggest bootlegger in the city.");
    }

    /// <summary>A raid on one of the bootlegger's stills or speakeasies.</summary>
    private void SquadRaid(Gang g)
    {
        var w = World;
        var s = w.Untouchables;
        var places = w.LiquorOf(g.Id).OrderBy(b => b.Id).ToList();
        var b = w.Rng.Pick(places);
        string what = b.Racket == RacketKind.Still ? "still" : "speakeasy";
        var man = b.HandlerHoodId >= 0 ? w.Hoods.FirstOrDefault(h => h.Id == b.HandlerHoodId && h.GangId == g.Id) : null;
        if (w.Rng.Chance(w.Warning(b)))
        {
            w.Log(EventKind.Untouchables, g.Id, $"The Untouchables raided {b.Name}, but {man!.Name} of {g.Name} got word in time, and there was nothing in the back room when they came.");
            return;
        }
        b.Racket = RacketKind.None;
        s.Evidence++;
        s.LastRaidWeek = w.Week;
        w.Log(EventKind.Untouchables, g.Id, $"The Untouchables smashed {g.Name}'s {what} at {b.Name}, with the papers watching.");
        if (man is { IsAvailable: true } && w.Rng.Chance(Content.SquadArrest)) Jail(man, w.Rng.Range(26, 52), "with running liquor", federal: true);
        if (s.Evidence == Content.SquadIndictment - 1)
            w.Log(EventKind.Whisper, g.Id, $"Word is the Untouchables are one raid away from taking {w.HoodById(g.BossHoodId).Name} before a grand jury.");
    }

    /// <summary>The boss is indicted on what the squad has found. Out of town, he's a wanted man.</summary>
    private void Indict(Gang g, Hood boss)
    {
        var w = World;
        var s = w.Untouchables;
        const string why = "with conspiracy to break the Prohibition laws";
        w.Log(EventKind.Untouchables, g.Id, $"A federal grand jury indicted {boss.Name} of {g.Name} on what the Untouchables found. The squad moves on.");
        int weeks = w.Rng.Range(104, 208);
        if (boss.State == HoodState.Lam)
        {
            int priors = boss.Convictions;
            int sentence = Content.Sentence(weeks, priors);
            if (boss.Case == null || boss.Case.Sentence < sentence)
                boss.Case = new Case
                {
                    Charge = why, Sentence = sentence, Federal = true, Strength = Content.SquadCase,
                    Prepared = g.Lawyer != Lawyer.None, Life = priors >= Content.HabitualOffender, JumpedBail = boss.Case?.JumpedBail ?? false,
                };
            Book(boss, RapKind.Charged, $"Charged {why} in federal court while out of town.");
            w.Log(EventKind.Lam, g.Id, $"{boss.Name} is out of town, and there's a federal warrant out for him now.");
        }
        else Jail(boss, weeks, why, federal: true, strength: Content.SquadCase);
        s.Evidence = 0;
        s.TargetGangId = -1;
        s.NamedWeek = w.Week;
    }
}
