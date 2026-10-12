namespace DryTown.Core;

/// <summary>A set start with a goal and a deadline, as an alternative to the open city.</summary>
public enum ScenarioKind { None, TakeTheWard, SurviveTheCrusade, OffTheBoat, Waterfront, LastCall }

public enum ScenarioOutcome { Playing, Won, Lost }

/// <summary>How a scenario stands: still being played, won or lost, and what it was measured against.</summary>
public sealed class ScenarioState
{
    public ScenarioOutcome Outcome { get; set; }

    /// <summary>The week it was won or lost.</summary>
    public int Week { get; set; }

    /// <summary>What the goal is measured against: the home ward's id, or the turf held at the start.</summary>
    public int Baseline { get; set; }
}

/// <summary>A scenario's card: what it's called, the briefing, the city it's played in, and the year it must be done by.</summary>
public sealed record ScenarioInfo(ScenarioKind Kind, string Title, string Briefing, string Goal, CitySize Size, Difficulty Difficulty, int StartYear, int DeadlineYear);

public static class Scenarios
{
    public const int CrusadeTurf = 18, OffTheBoatTurf = 20, LastCallClean = 20_000, WaterfrontLocals = 2;

    public static readonly ScenarioInfo[] All =
    {
        new(ScenarioKind.TakeTheWard, "Take the Ward",
            "Your outfit has a back room in a ward that nobody runs. The alderman is a party man who'll take anyone's envelope, and the shops pay whoever leans hardest.",
            "Run your home ward by 1923: its alderman on your payroll, or your own man in the seat, and at least half its open shops paying you.",
            CitySize.Medium, Difficulty.Normal, 1920, 1923),
        new(ScenarioKind.SurviveTheCrusade, "Survive the Crusade",
            $"You are the biggest outfit in the city, with {CrusadeTurf} shops and money in the safe. This week the Tribune puts your name on its front page, and the police chief has promised the mayor a clean-up.",
            $"Still be the biggest outfit in the city at the start of 1922, with at least as many shops as you started with.",
            CitySize.Large, Difficulty.Hard, 1920, 1922),
        new(ScenarioKind.OffTheBoat, "Off the Boat",
            "You came off the boat with three cousins, a boss's nerve and $500. The outfits that run this city don't know your name and won't go easy on you.",
            $"Be the biggest outfit in the city by 1924: more shops than anyone else, and at least {OffTheBoatTurf}.",
            CitySize.Medium, Difficulty.Hard, 1920, 1924),
        new(ScenarioKind.Waterfront, "The Waterfront",
            "Whoever runs the docks takes a cut of every boat that lands, and right now that's your biggest rival. The other locals are up for grabs.",
            $"By 1923, run the longshoremen and at least one other union local at the same time.",
            CitySize.Large, Difficulty.Normal, 1920, 1923),
        new(ScenarioKind.LastCall, "Last Call",
            "It's 1932. The banks are failing, and everyone knows repeal is coming. When it does, bootleg money dries up overnight, and only money that can show where it came from will be worth anything.",
            $"Have ${LastCallClean:N0} of clean money in the safe before Prohibition ends at the start of 1934.",
            CitySize.Large, Difficulty.Normal, 1932, 1934),
    };

    public static ScenarioInfo Info(ScenarioKind kind) => All.First(i => i.Kind == kind);

    public static int Deadline(ScenarioInfo info) => (info.DeadlineYear - Content.StartYear) * Content.WeeksPerYear;

    /// <summary>The year this city's goal must be met by: the scenario's own, or its chapter's in a campaign.</summary>
    public static int DeadlineYear(World w) =>
        w.Settings.Campaign != null ? Campaign.Current(w).DeadlineYear : Info(w.Settings.Scenario).DeadlineYear;

    public static int Deadline(World w) => (DeadlineYear(w) - Content.StartYear) * Content.WeeksPerYear;

    /// <summary>The goal as it reads in this city, with its own deadline.</summary>
    public static string Goal(World w)
    {
        var info = Info(w.Settings.Scenario);
        return info.Goal.Replace(info.DeadlineYear.ToString(), DeadlineYear(w).ToString());
    }

    /// <summary>The settings for a new game of this scenario.</summary>
    public static WorldSettings Settings(ScenarioKind kind, ulong seed)
    {
        var info = Info(kind);
        return new WorldSettings
        {
            Seed = seed, Size = info.Size, Difficulty = info.Difficulty, Scenario = kind,
            StartWeek = (info.StartYear - Content.StartYear) * Content.WeeksPerYear,
        };
    }

    /// <summary>Set the city up the way the scenario's briefing says it is.</summary>
    internal static void Setup(Simulation sim)
    {
        var w = sim.World;
        var me = w.Player;
        switch (w.Settings.Scenario)
        {
            case ScenarioKind.TakeTheWard:
            {
                var ward = w.Wards[w.HqOf(me).WardId];
                ward.Reformer = false;
                ward.OwnMan = false;
                ward.OwnerGangId = -1;
                w.Goal.Baseline = ward.Id;
                break;
            }
            case ScenarioKind.SurviveTheCrusade:
            {
                for (int i = 0; i < 4; i++) w.NewHood(me.Id, bossQuality: false);
                me.Cash += 4000;
                var men = w.AvailableHoodsOf(me.Id).Where(h => h.Id != me.BossHoodId).ToList();
                var near = w.Businesses.Where(b => b.IsOpen && !b.IsProtected).OrderBy(b => w.BlocksFromHq(me, b)).ThenBy(b => b.Id).Take(CrusadeTurf).ToList();
                for (int i = 0; i < near.Count; i++)
                {
                    near[i].ProtectorGangId = me.Id;
                    near[i].HandlerHoodId = men[i % men.Count].Id;
                    near[i].ProtectionRate = Content.DefaultRatePercent;
                }
                w.Goal.Baseline = w.TurfOf(me.Id).Count();
                // Every other outfit smells blood, and bears the city's biggest outfit a grudge.
                foreach (var rival in w.LivingGangs.Where(g => !g.IsPlayer).ToList()) w.Hostile(me, rival, 40);
                sim.Begin(CityEventKind.Crusade);
                sim.Begin(CityEventKind.Shakeup);
                break;
            }
            case ScenarioKind.OffTheBoat:
                me.Cash = 500;
                break;
            case ScenarioKind.Waterfront:
            {
                var docks = w.Locals.First(l => l.IsDocks);
                var rival = w.LivingGangs.Where(g => !g.IsPlayer).OrderByDescending(w.Power).ThenBy(g => g.Id).First();
                docks.OwnerGangId = rival.Id;
                docks.PresidentHoodId = w.AvailableHoodsOf(rival.Id).Where(h => h.Id != rival.BossHoodId).OrderByDescending(h => h.Intimidation).ThenBy(h => h.Id).First().Id;
                w.Log(EventKind.Union, rival.Id, $"{rival.Name} runs {docks.Name}.");
                break;
            }
            case ScenarioKind.LastCall:
            {
                me.Cash += 4500;
                me.CleanCash = 2000;
                // The Depression is two years in already, and lasts until repeal.
                var hard = sim.Begin(CityEventKind.Depression);
                w.CityEvents.Remove(hard);
                w.CityEvents.Add(new CityEvent { Kind = hard.Kind, StartWeek = hard.StartWeek, UntilWeek = Deadline(w), WardId = hard.WardId });
                break;
            }
        }
        if (w.Settings.Campaign is { } campaign)
        {
            // Word of the cities he won gets here before he does.
            me.Cash += Campaign.WordAhead * campaign.Won;
            if (Campaign.Bridge(campaign) is { Length: > 0 } bridge) w.Log(EventKind.Era, me.Id, bridge);
        }
        w.Log(EventKind.Era, me.Id, $"{Info(w.Settings.Scenario).Title}. {Goal(w)}");
    }

    /// <summary>Whether the goal is met right now.</summary>
    public static bool Met(World w)
    {
        var me = w.Player;
        if (!me.Alive) return false;
        int turf = w.TurfOf(me.Id).Count();
        switch (w.Settings.Scenario)
        {
            case ScenarioKind.TakeTheWard:
            {
                var (mine, open) = WardShops(w);
                return w.Wards[w.Goal.Baseline].OwnerGangId == me.Id && mine * 2 >= open;
            }
            case ScenarioKind.SurviveTheCrusade: return turf >= w.Goal.Baseline && Biggest(w, turf);
            case ScenarioKind.OffTheBoat: return turf >= OffTheBoatTurf && Biggest(w, turf);
            case ScenarioKind.Waterfront: return w.DocksGangId == me.Id && w.LocalsOf(me.Id).Count() >= WaterfrontLocals;
            case ScenarioKind.LastCall: return me.Clean >= LastCallClean;
            default: return false;
        }
    }

    /// <summary>Whether no other outfit holds as many shops.</summary>
    private static bool Biggest(World w, int turf) => w.LivingGangs.All(g => g.IsPlayer || w.TurfOf(g.Id).Count() < turf);

    /// <summary>The open shops in the home ward that pay the player, and all the open shops there.</summary>
    private static (int Mine, int Open) WardShops(World w)
    {
        var shops = w.Businesses.Where(b => b.IsOpen && w.LotOf(b).WardId == w.Goal.Baseline).ToList();
        return (shops.Count(b => b.ProtectorGangId == w.Player.Id), shops.Count);
    }

    /// <summary>
    /// Sunday night: the scenario is won once the goal is met, or lost when the outfit is finished or
    /// the deadline passes. Holding out through the crusade only counts at the end.
    /// </summary>
    internal static void Check(World w)
    {
        if (w.Settings.Scenario == ScenarioKind.None || w.Goal.Outcome != ScenarioOutcome.Playing) return;
        var info = Info(w.Settings.Scenario);
        bool due = w.Week + 1 >= Deadline(w);
        if (!w.Player.Alive) { End(w, ScenarioOutcome.Lost, $"{info.Title} is lost: your outfit is finished."); return; }
        bool met = Met(w);
        bool atTheEnd = w.Settings.Scenario == ScenarioKind.SurviveTheCrusade;
        if (met && (!atTheEnd || due)) End(w, ScenarioOutcome.Won, $"{info.Title} is won in {Reports.Date(w)}. The city is yours to carry on in for as long as you like.");
        else if (due) End(w, ScenarioOutcome.Lost, $"{info.Title} is lost: time ran out at the start of {DeadlineYear(w)}. You can carry on in the city anyway.");
        if (w.Goal.Outcome != ScenarioOutcome.Playing && Campaign.Verdict(w) is { } verdict) w.Log(EventKind.Era, w.Player.Id, verdict);
    }

    private static void End(World w, ScenarioOutcome outcome, string text)
    {
        w.Goal.Outcome = outcome;
        w.Goal.Week = w.Week;
        w.Log(EventKind.Era, w.Player.Id, text);
    }

    /// <summary>How the player stands against the goal, in a sentence or two.</summary>
    public static string Progress(World w)
    {
        var me = w.Player;
        int turf = w.TurfOf(me.Id).Count();
        switch (w.Settings.Scenario)
        {
            case ScenarioKind.TakeTheWard:
            {
                var ward = w.Wards[w.Goal.Baseline];
                var (mine, open) = WardShops(w);
                string alderman = ward.OwnerGangId == me.Id ? (ward.OwnMan ? "is your own man" : "is on your payroll")
                    : ward.Reformer ? "is a reformer, who has to be beaten at the polls"
                    : ward.OwnerGangId >= 0 ? $"is on {w.GangById(ward.OwnerGangId).Name}'s payroll" : "is still for sale";
                return $"Alderman {ward.Alderman} of {ward.Name} {alderman}. {mine} of its {open} open shops pay you, and you need {(open + 1) / 2}.";
            }
            case ScenarioKind.SurviveTheCrusade:
            {
                var top = w.LivingGangs.Where(g => !g.IsPlayer).OrderByDescending(g => w.TurfOf(g.Id).Count()).ThenBy(g => g.Id).FirstOrDefault();
                return $"You hold {turf} shops, and need {w.Goal.Baseline}." + (top != null ? $" The next biggest outfit, {top.Name}, holds {w.TurfOf(top.Id).Count()}." : "");
            }
            case ScenarioKind.OffTheBoat:
            {
                var top = w.LivingGangs.Where(g => !g.IsPlayer).OrderByDescending(g => w.TurfOf(g.Id).Count()).ThenBy(g => g.Id).FirstOrDefault();
                return $"You hold {turf} shops." + (top != null ? $" The biggest outfit, {top.Name}, holds {w.TurfOf(top.Id).Count()}." : "");
            }
            case ScenarioKind.Waterfront:
            {
                int docks = w.DocksGangId;
                string who = docks == me.Id ? "You run the docks" : docks >= 0 ? $"{w.GangById(docks).Name} runs the docks" : "The longshoremen run their own local";
                return $"{who}, and you run {w.LocalsOf(me.Id).Count()} of the city's {w.Locals.Count} locals.";
            }
            case ScenarioKind.LastCall:
                return $"You have ${me.Clean:N0} of clean money out of ${LastCallClean:N0}, and ${me.Cash - me.Clean:N0} that still needs washing.";
            default:
                return "";
        }
    }

    /// <summary>The goal, how it stands, and the time left or the result, for the report and the console.</summary>
    public static string Summary(World w)
    {
        if (w.Settings.Scenario == ScenarioKind.None) return "";
        var info = Info(w.Settings.Scenario);
        string state = w.Goal.Outcome switch
        {
            ScenarioOutcome.Won => $"Won in {Reports.Date(w, w.Goal.Week)}.",
            ScenarioOutcome.Lost => $"Lost in {Reports.Date(w, w.Goal.Week)}.",
            _ => $"{Deadline(w) - w.Week} weeks left.",
        };
        string campaign = w.Settings.Campaign is { } c ? $"Chapter {c.Chapter + 1} of {Campaign.Chapters.Length}. " : "";
        return $"{campaign}{info.Title}: {Goal(w)}\n{Progress(w)} {state}";
    }
}
