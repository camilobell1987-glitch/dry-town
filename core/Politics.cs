namespace DryTown.Core;

/// <summary>
/// A ward of the city and the alderman who runs it. An alderman on a gang's payroll keeps the
/// precinct away from its rackets in his ward and makes complaints from his shopkeepers go away.
/// A reformer can't be bought, and the only way to be rid of him is to beat him at the polls.
/// </summary>
public sealed class Ward
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Alderman { get; set; }

    /// <summary>The gang whose money he takes, or -1.</summary>
    public int OwnerGangId { get; set; } = -1;

    /// <summary>Elected on a clean-government ticket: he won't take an envelope.</summary>
    public bool Reformer { get; set; }

    /// <summary>When his current owner bought him. A bought man stays bought for a while.</summary>
    public int BoughtWeek { get; set; } = -1000;

    /// <summary>Money put behind candidates this campaign, by gang.</summary>
    public Dictionary<int, long> Campaign { get; init; } = new();

    /// <summary>The alderman is one of the owner's own men: he takes no envelope and nobody can buy him away.</summary>
    public bool OwnMan { get; set; }

    /// <summary>Men the outfits have put on the ballot this campaign, by gang.</summary>
    public Dictionary<int, int> Candidates { get; init; } = new();

    /// <summary>Votes leaned on at the polls this campaign, by gang.</summary>
    public Dictionary<int, int> Polls { get; init; } = new();
}

/// <summary>The mayor's office and the mood of the city.</summary>
public sealed class CityHall
{
    public required string Mayor { get; set; }

    /// <summary>A reform mayor leans on the police: raids come sooner and aldermen cost more.</summary>
    public bool Reform { get; set; }

    /// <summary>The gang that bankrolled the mayor's win: its heat cools faster. -1 for nobody.</summary>
    public int FriendGangId { get; set; } = -1;

    /// <summary>0 to 100. Killings and raids in the papers; the higher it runs, the better reformers do at the polls.</summary>
    public int Outrage { get; set; }

    /// <summary>Money put behind the machine candidate this campaign, by gang.</summary>
    public Dictionary<int, long> Campaign { get; init; } = new();

    /// <summary>Votes leaned on at the polls for the machine ticket this campaign, by gang.</summary>
    public Dictionary<int, int> Polls { get; init; } = new();

    /// <summary>The district attorney, elected on the mayor's ballot. He decides how hard the city's cases are pushed.</summary>
    public string DA { get; set; } = "";

    /// <summary>A reform DA pushes every case harder.</summary>
    public bool DAReform { get; set; }

    /// <summary>The gang that bankrolled the DA's win: its men's cases go soft. -1 for nobody.</summary>
    public int DAFriendGangId { get; set; } = -1;

    /// <summary>How the last vote in every race came out.</summary>
    public List<RaceResult> Results { get; set; } = new();
}

public enum ElectionKind { Alderman, Mayor }

/// <summary>One race on election night: who ran and how many votes each of them got.</summary>
public sealed class RaceResult
{
    public ElectionKind Kind { get; init; }
    public required string Race { get; init; }

    /// <summary>The ward, or -1 for the mayor's and the DA's races.</summary>
    public int WardId { get; init; } = -1;
    public int Week { get; init; }
    public List<Tally> Field { get; init; } = new();

    public Tally Winner => Field.First(t => t.Won);
    public int Share(Tally t) => (int)Math.Round(100.0 * t.Votes / Math.Max(1, Field.Sum(x => x.Votes)));
}

/// <summary>One candidate's count. Ticket says whose he is: reform, the party, or an outfit's.</summary>
public sealed class Tally
{
    public required string Name { get; init; }
    public required string Ticket { get; init; }
    public int GangId { get; init; } = -1;
    public int Votes { get; init; }
    public bool Won { get; init; }
}

/// <summary>Elections, payoffs and the weekly envelopes. Called by the simulation; holds no state of its own.</summary>
public static class Politics
{
    public static string NewPolitician(Rng rng, bool reformer)
    {
        string first = rng.Pick(Content.PoliticianFirstNames);
        string last = rng.Pick(Content.LastNames);
        return !reformer && rng.Chance(0.35) ? $"{first} \"{rng.Pick(Content.PoliticianNicknames)}\" {last}" : $"{first} {last}";
    }

    /// <summary>Seat the first aldermen and mayor of a new city: party men, none of them bought yet.</summary>
    public static void Found(World w)
    {
        var names = Content.WardNames.ToList();
        w.Rng.Shuffle(names);
        w.Wards.Clear();
        // A name that belongs to one people's neighbourhood only goes to a ward of theirs.
        var left = names.ToList();
        for (int i = 0; i < w.Map.WardCount; i++)
        {
            var people = w.Neighbourhood(i);
            string name = left.FirstOrDefault(n => !Content.WardNameOf.TryGetValue(n, out var e) || e == people) ?? names[i % names.Count];
            left.Remove(name);
            w.Wards.Add(new Ward { Id = i, Name = name, Alderman = NewPolitician(w.Rng, false) });
        }
        w.Hall = new CityHall { Mayor = NewPolitician(w.Rng, false), DA = NewPolitician(w.Rng, false) };
    }

    public static IEnumerable<Business> BusinessesIn(World w, Ward ward) => w.Businesses.Where(b => w.LotOf(b).WardId == ward.Id);

    public static Ward WardOf(World w, Business b) => w.Wards[w.LotOf(b).WardId];

    /// <summary>True when the alderman of this business's ward is on the gang's payroll.</summary>
    public static bool Covered(World w, Gang g, Business b) => w.Wards.Count > 0 && WardOf(w, b).OwnerGangId == g.Id;

    public static int Retainer(World w, Ward ward) => Content.AldermanRetainer(BusinessesIn(w, ward).Count());

    /// <summary>What it costs to put an alderman on the payroll now: double if he's another outfit's man, double again under a reform mayor.</summary>
    public static long PayoffCost(World w, Ward ward, Gang g)
    {
        long cost = Retainer(w, ward) * Content.PayoffWeeks;
        if (ward.OwnerGangId >= 0 && ward.OwnerGangId != g.Id) cost *= 2;
        if (w.Hall.Reform) cost *= 2;
        return cost;
    }

    /// <summary>True while an alderman who has just taken one outfit's money won't hear offers from another.</summary>
    public static bool StaysBought(World w, Ward ward, Gang g) =>
        ward.OwnerGangId >= 0 && ward.OwnerGangId != g.Id && (ward.OwnMan || w.Week - ward.BoughtWeek < Content.AldermanLoyalWeeks);

    /// <summary>The race whose polls are open: the last weeks of a campaign, when men on the street can turn out the vote.</summary>
    public static (ElectionKind Kind, int Week)? PollsOpen(World w) =>
        Campaigning(w) is { } d && d.Week - w.Week < Content.PollsWeeks ? d : null;

    /// <summary>The ward a man is on the ballot in, or null.</summary>
    public static Ward? StandingIn(World w, Hood man) => w.Wards.FirstOrDefault(x => x.Candidates.TryGetValue(man.GangId, out var id) && id == man.Id);

    /// <summary>Why this man can't run for alderman in this ward now, or null if he can.</summary>
    public static string? CannotStand(World w, Gang g, Hood man, Ward ward)
    {
        if (Campaigning(w) is not { Kind: ElectionKind.Alderman })
            return "A man can only get on the ballot while the aldermen's campaign is on.";
        if (man.GangId != g.Id || !man.IsAvailable) return $"{man.Name} has to be free to run.";
        if (man.Id == g.BossHoodId) return "The boss runs the outfit, not a ward.";
        if (man.Id == g.HeirHoodId) return $"{man.Name} is your heir. He can't sit on the council and take over the outfit too.";
        if (man.Convictions > 0) return $"{man.Name} has a conviction on his record. No ward would elect him.";
        if (man.Brains < Content.CandidateBrains) return $"{man.Name} hasn't the brains for City Hall. A candidate needs {Content.CandidateBrains}.";
        if (ward.OwnMan && ward.OwnerGangId == g.Id) return $"Your own man already sits for {ward.Name}.";
        if (ward.Candidates.TryGetValue(g.Id, out var other)) return $"{w.HoodById(other).Name} is already on the ballot there for you.";
        if (StandingIn(w, man) is { } there) return $"{man.Name} is already running in {there.Name}.";
        return null;
    }

    /// <summary>Put one of the gang's men on the ballot. If he wins, the seat is the gang's for as long as he holds it.</summary>
    public static bool Stand(World w, Gang g, Hood man, Ward ward)
    {
        if (CannotStand(w, g, man, ward) != null) return false;
        ward.Candidates[g.Id] = man.Id;
        w.Log(EventKind.Politics, g.Id, $"{man.Name} of {g.Name} is running for alderman in {ward.Name}.");
        return true;
    }

    /// <summary>Where the ward votes: outside its busiest business.</summary>
    public static int PollingPlace(World w, Ward ward, int fallback) =>
        BusinessesIn(w, ward).OrderByDescending(b => b.Takings).ThenBy(b => b.Id).Select(b => b.LotId).DefaultIfEmpty(fallback).First();

    /// <summary>What a man on the ballot is worth: more in a ward of his own people.</summary>
    public static int CandidateWeight(World w, Ward ward, Hood man) =>
        Content.CandidateWeight(man) + (man.Origin == w.Neighbourhood(ward) ? Content.HomeCandidate : 0);

    /// <summary>The votes a man at the polls turns out: more among his own people, who know him.</summary>
    public static int PollsVotes(World w, Ward ward, Hood man) =>
        (int)(Content.PollsVotes(man) * (man.Origin == w.Neighbourhood(ward) ? Content.HomePolls : 1));

    /// <summary>Chance a man leaning on voters gets picked up. Reformers watch their own polls, and a reform mayor's police watch everyone's.</summary>
    public static double PollsRisk(World w, Ward ward, Hood man) =>
        Math.Clamp(0.1 + (ward.Reformer ? 0.08 : 0) + (w.Hall.Reform ? 0.07 : 0) - man.Stealth * 0.01, 0.03, 0.4);

    /// <summary>The election coming up, if campaigning has started: which, and the week it falls on.</summary>
    public static (ElectionKind Kind, int Week)? Campaigning(World w)
    {
        foreach (var kind in new[] { ElectionKind.Mayor, ElectionKind.Alderman })
        {
            var week = NextElection(w, kind);
            if (week - w.Week is >= 0 and < Content.CampaignWeeks) return (kind, week);
        }
        return null;
    }

    /// <summary>The game week of the next election of this kind, this week included.</summary>
    public static int NextElection(World w, ElectionKind kind)
    {
        for (int year = w.Year; ; year++)
        {
            bool due = kind == ElectionKind.Mayor ? Content.IsMayorYear(year) : Content.IsAldermanYear(year);
            int weekOfYear = kind == ElectionKind.Mayor ? Content.MayorElectionWeek : Content.AldermanElectionWeek;
            int week = (year - Content.StartYear) * Content.WeeksPerYear + weekOfYear - 1;
            if (due && week >= w.Week) return week;
        }
    }

    /// <summary>Put an alderman on the payroll. Returns what happened, for the log.</summary>
    public static bool Payoff(World w, Gang g, Ward ward)
    {
        if (ward.OwnerGangId == g.Id || ward.OwnMan) return false;
        if (ward.Reformer)
        {
            g.Heat = Math.Min(100, g.Heat + 4);
            w.Log(EventKind.Politics, g.Id, $"Alderman {ward.Alderman} of {ward.Name} threw {g.Name}'s envelope back and told the papers.");
            return false;
        }
        long cost = PayoffCost(w, ward, g);
        if (g.Cash < cost || StaysBought(w, ward, g)) return false;
        g.Cash -= cost;
        w.Ledger(g.Id).Spending += cost;
        string was = ward.OwnerGangId >= 0 ? $", away from {w.GangById(ward.OwnerGangId).Name}" : "";
        ward.OwnerGangId = g.Id;
        ward.BoughtWeek = w.Week;
        w.Log(EventKind.Politics, g.Id, $"{g.Name} put Alderman {ward.Alderman} of {ward.Name} on the payroll{was} (${cost}).");
        return true;
    }

    /// <summary>Put money behind a candidate. ward is -1 for the mayor's race. Strong-arming voters draws a little heat.</summary>
    public static long Campaign(World w, Gang g, int wardId, long amount)
    {
        var due = Campaigning(w);
        if (due == null) return 0;
        bool mayor = wardId < 0;
        if (mayor != (due.Value.Kind == ElectionKind.Mayor)) return 0;
        if (!mayor && (wardId >= w.Wards.Count)) return 0;
        amount = Math.Min(amount, g.Cash);
        if (amount <= 0) return 0;
        g.Cash -= amount;
        w.Ledger(g.Id).Spending += amount;
        g.Heat = Math.Min(100, g.Heat + (int)(amount / 400));
        var pot = mayor ? w.Hall.Campaign : w.Wards[wardId].Campaign;
        pot[g.Id] = pot.GetValueOrDefault(g.Id) + amount;
        string race = mayor ? "the machine's man for mayor" : $"its man in {w.Wards[wardId].Name}";
        w.Log(EventKind.Politics, g.Id, $"{g.Name} put ${amount} behind {race}.");
        return amount;
    }

    /// <summary>Sunday: pay the aldermen, let the papers cool off, and hold any election that's due.</summary>
    public static void Step(World w, int killingsThisWeek)
    {
        if (w.Wards.Count == 0 || !w.Settings.PoliticsEnabled) return;
        PayRetainers(w);

        var hall = w.Hall;
        // Each killing makes the papers; the story fades over a few months.
        hall.Outrage = Math.Clamp(hall.Outrage * 93 / 100 + killingsThisWeek * 2, 0, 100);

        var due = Campaigning(w);
        if (due is { } d && d.Week - w.Week == Content.CampaignWeeks - 1)
        {
            string what = d.Kind == ElectionKind.Mayor ? $"Mayor {hall.Mayor} is up for re-election" : "Every ward elects its alderman";
            w.Log(EventKind.Politics, -1, $"{what} in {Content.CampaignWeeks} weeks. Money talks at City Hall.");
        }
        if (NextElection(w, ElectionKind.Alderman) == w.Week) AldermanElections(w);
        if (NextElection(w, ElectionKind.Mayor) == w.Week) MayorElection(w);
    }

    private static void PayRetainers(World w)
    {
        foreach (var ward in w.Wards.Where(x => x.OwnerGangId >= 0 && !x.OwnMan))
        {
            var g = w.GangById(ward.OwnerGangId);
            int fee = Retainer(w, ward);
            if (g.Alive && g.Cash >= fee)
            {
                g.Cash -= fee;
                w.Ledger(g.Id).Spending += fee;
                continue;
            }
            ward.OwnerGangId = -1;
            if (g.Alive) w.Log(EventKind.Politics, g.Id, $"Alderman {ward.Alderman} of {ward.Name} stopped taking {g.Name}'s calls when the envelope came up short.");
        }
    }

    /// <summary>
    /// Each ward chooses between a reformer, a party regular and whichever outfits are in the race.
    /// An outfit runs one of its own men if it put one up, else a party hack its money bought.
    /// Money, men at the polls, the shops it runs in the ward and a sitting alderman's machine all count.
    /// </summary>
    private static void AldermanElections(World w)
    {
        var hall = w.Hall;
        var results = new List<RaceResult>();
        foreach (var ward in w.Wards)
        {
            bool partyHeld = !ward.Reformer && ward.OwnerGangId < 0;
            var field = new List<Runner>
            {
                new(-2, ward.Reformer ? ward.Alderman : NewPolitician(w.Rng, true), "reform", 250 + hall.Outrage * 8 + (hall.Reform ? 150 : 0)),
                new(-1, partyHeld ? ward.Alderman : NewPolitician(w.Rng, false), "party", 350),
            };
            foreach (var g in w.LivingGangs)
            {
                long money = ward.Campaign.GetValueOrDefault(g.Id);
                int polls = ward.Polls.GetValueOrDefault(g.Id);
                bool incumbent = ward.OwnerGangId == g.Id;
                var man = OnTheBallot(w, g, ward);
                if (money <= 0 && polls <= 0 && !incumbent && man == null) continue;
                int turf = BusinessesIn(w, ward).Count(b => b.ProtectorGangId == g.Id);
                double weight = money * 0.6 + polls + turf * 25
                    + (incumbent ? ward.OwnMan ? Content.OwnManIncumbency : 250 : 0)
                    + (man != null ? CandidateWeight(w, ward, man) : 0);
                string name = man?.Name ?? (incumbent ? ward.Alderman : NewPolitician(w.Rng, false));
                string ticket = man != null || incumbent && ward.OwnMan ? $"{g.Name}'s own man" : $"{g.Name}'s money";
                field.Add(new(g.Id, name, ticket, weight, man));
            }
            var (result, at) = Vote(w, ElectionKind.Alderman, ward.Name, ward.Id, field, Content.WardTurnout(BusinessesIn(w, ward).Count()));
            results.Add(result);
            Seat(w, ward, field[at], result.Share(result.Winner), partyHeld);
            foreach (var loser in field.Where(r => r.Man != null && r != field[at]))
                w.Log(EventKind.Politics, loser.Side, $"{loser.Man!.Name} lost in {ward.Name}, and went back to work for {w.GangById(loser.Side).Name}.");
            ward.Campaign.Clear();
            ward.Candidates.Clear();
            ward.Polls.Clear();
        }
        hall.Results.RemoveAll(r => r.Kind == ElectionKind.Alderman);
        hall.Results.AddRange(results);
    }

    /// <summary>Put the winner in the seat, or keep the man who held it.</summary>
    private static void Seat(World w, Ward ward, Runner win, int pct, bool partyHeld)
    {
        string before = ward.Alderman;
        int ownerBefore = ward.OwnerGangId;
        string share = $"{pct}% of the vote";
        if (win.Side >= 0 && win.Side == ownerBefore && win.Man == null)
        {
            ward.BoughtWeek = w.Week;
            string gang = w.GangById(win.Side).Name;
            w.Log(EventKind.Politics, win.Side, ward.OwnMan
                ? $"Alderman {before}, {gang}'s own man, held {ward.Name} with {share}."
                : $"Alderman {before} held {ward.Name} with {share}, and stays on {gang}'s payroll.");
            return;
        }
        if (win.Side == -1 && partyHeld)
        {
            w.Log(EventKind.Politics, -1, $"Alderman {before} held {ward.Name} for the party with {share}.");
            return;
        }
        if (win.Side == -2 && ward.Reformer)
        {
            w.Log(EventKind.Politics, -1, $"Reformer {before} held {ward.Name} with {share}. He still won't take an envelope.");
            return;
        }
        ward.Alderman = win.Name;
        ward.Reformer = win.Side == -2;
        ward.OwnerGangId = Math.Max(-1, win.Side);
        ward.OwnMan = win.Man != null;
        ward.BoughtWeek = w.Week;
        if (win.Man != null)
        {
            // He gives up the street for a seat on the council.
            win.Man.State = HoodState.Gone;
            w.LeaveCrew(win.Man.Id);
        }
        string text = win.Side switch
        {
            -2 => $"Reformer {ward.Alderman} won {ward.Name} with {share}. He won't take an envelope.",
            -1 => $"Party man {ward.Alderman} won {ward.Name} with {share}.",
            _ when win.Man != null => $"{ward.Alderman} of {w.GangById(win.Side).Name} won {ward.Name} with {share}. He leaves the street for a seat on the council, and he'll never need an envelope.",
            _ => $"{ward.Alderman} won {ward.Name} with {share} and {w.GangById(win.Side).Name}'s money behind him.",
        };
        w.Log(EventKind.Politics, Math.Max(-1, win.Side), text);
        if (ownerBefore >= 0 && ownerBefore != win.Side)
            w.Log(EventKind.Politics, ownerBefore, $"{w.GangById(ownerBefore).Name} lost its alderman in {ward.Name}.");
    }

    /// <summary>The gang's man on the ballot in this ward, if he's still free to run on election day.</summary>
    private static Hood? OnTheBallot(World w, Gang g, Ward ward)
    {
        if (!ward.Candidates.TryGetValue(g.Id, out var id)) return null;
        var man = w.HoodById(id);
        if (man.GangId == g.Id && man.IsAvailable && man.Convictions == 0) return man;
        w.Log(EventKind.Politics, g.Id, $"{man.Name} is off the ballot in {ward.Name}.");
        return null;
    }

    /// <summary>
    /// The machine against the reformers, for mayor and for district attorney on the same ballot.
    /// The outfit that put in the most money has the new mayor's ear, and the DA's, if their men win.
    /// </summary>
    private static void MayorElection(World w)
    {
        var hall = w.Hall;
        long machineMoney = hall.Campaign.Values.Sum();
        int polls = hall.Polls.Values.Sum();
        int turnout = w.Wards.Sum(x => Content.WardTurnout(BusinessesIn(w, x).Count()));
        bool newMachineMan = hall.Reform || w.Rng.Chance(0.4);
        var field = new List<Runner>
        {
            new(-2, hall.Reform ? hall.Mayor : NewPolitician(w.Rng, true), "reform", 600 + hall.Outrage * 15),
            new(-1, newMachineMan ? NewPolitician(w.Rng, false) : hall.Mayor, "machine", 600 + machineMoney * 0.5 + polls + (hall.Reform ? 0 : 150)),
        };
        var (mayor, at) = Vote(w, ElectionKind.Mayor, "Mayor", -1, field, turnout);
        bool reformWins = at == 0;
        var backer = hall.Campaign.Where(kv => kv.Value >= 500).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).DefaultIfEmpty(-1).First();
        string old = hall.Mayor;
        bool wasReform = hall.Reform;
        string share = $"{mayor.Share(mayor.Winner)}% of the vote";
        hall.Mayor = field[at].Name;

        if (reformWins)
        {
            hall.Reform = true;
            hall.FriendGangId = -1;
            // A reform mayor's new police chief cleans out the precincts: every alderman's deal is suddenly worth less.
            w.Log(EventKind.Politics, -1, wasReform
                ? $"Mayor {hall.Mayor} was re-elected on a clean-up ticket with {share}. The raids go on."
                : $"Reformer {hall.Mayor} beat Mayor {old} with {share}. The new police chief promises to clean up the city.");
        }
        else
        {
            hall.Reform = false;
            hall.FriendGangId = backer;
            string who = hall.Mayor == old ? $"Mayor {hall.Mayor} was re-elected with {share}" : $"{hall.Mayor} was elected mayor with {share}";
            string friend = backer >= 0 ? $" {w.GangById(backer).Name} paid for it, and the police will go easy on them." : " The town stays wide open.";
            w.Log(EventKind.Politics, backer, who + "." + friend);
        }

        // The district attorney runs on the same ballot, and the new mayor's coat-tails carry his side's man.
        var daField = new List<Runner>
        {
            new(-2, hall.DAReform ? hall.DA : NewPolitician(w.Rng, true), "reform", 500 + hall.Outrage * 12 + (reformWins ? Content.CoatTails : 0)),
            new(-1, !hall.DAReform && hall.DA != "" ? hall.DA : NewPolitician(w.Rng, false), "machine", 500 + machineMoney * 0.35 + polls * 0.5 + (reformWins ? 0 : Content.CoatTails)),
        };
        var (da, dat) = Vote(w, ElectionKind.Mayor, "District attorney", -1, daField, turnout);
        string oldDa = hall.DA;
        hall.DA = daField[dat].Name;
        hall.DAReform = dat == 0;
        hall.DAFriendGangId = hall.DAReform ? -1 : backer;
        string daShare = $"{da.Share(da.Winner)}% of the vote";
        string daWho = hall.DA == oldDa ? $"District Attorney {hall.DA} was re-elected with {daShare}." : $"{hall.DA} was elected district attorney with {daShare}.";
        if (hall.DAReform) w.Log(EventKind.Politics, -1, $"{daWho} He's a reformer, and every case in the city courts will be pushed harder.");
        else w.Log(EventKind.Politics, backer, daWho + (backer >= 0 ? $" {w.GangById(backer).Name}'s men will find the city's cases against them go soft." : ""));

        hall.Results.RemoveAll(r => r.Kind == ElectionKind.Mayor);
        hall.Results.Add(mayor);
        hall.Results.Add(da);
        hall.Campaign.Clear();
        hall.Polls.Clear();
        hall.Outrage = hall.Outrage / 2;
    }

    /// <summary>A name on the ballot. Side is -2 for the reformer, -1 for the party or machine, else the gang behind him.</summary>
    private sealed record Runner(int Side, string Name, string Ticket, double Weight, Hood? Man = null);

    /// <summary>
    /// Count the vote. Each runner wins with odds in proportion to his weight: the one with the highest
    /// weight over an exponential draw takes it. The counts come from the same draw, so the winner
    /// always has the most votes.
    /// </summary>
    private static (RaceResult Result, int Winner) Vote(World w, ElectionKind kind, string race, int wardId, List<Runner> field, int turnout)
    {
        var score = field.Select(r => Math.Max(1, r.Weight) / Math.Max(1e-9, -Math.Log(1 - w.Rng.NextDouble()))).ToList();
        int winner = score.IndexOf(score.Max());
        var pull = score.Select(x => Math.Pow(x, Content.VoteSpread)).ToList();
        double total = pull.Sum();
        var result = new RaceResult
        {
            Kind = kind, Race = race, WardId = wardId, Week = w.Week,
            Field = field.Select((r, i) => new Tally
            {
                Name = r.Name, Ticket = r.Ticket, GangId = Math.Max(-1, r.Side), Votes = (int)Math.Round(turnout * pull[i] / total), Won = i == winner,
            }).ToList(),
        };
        return (result, winner);
    }

    /// <summary>A gang's dissolution frees its aldermen and the mayor's favour.</summary>
    public static void Forget(World w, Gang g)
    {
        foreach (var ward in w.Wards)
        {
            if (ward.OwnerGangId == g.Id) { ward.OwnerGangId = -1; ward.OwnMan = false; }
            ward.Candidates.Remove(g.Id);
            ward.Polls.Remove(g.Id);
        }
        if (w.Hall.FriendGangId == g.Id) w.Hall.FriendGangId = -1;
        if (w.Hall.DAFriendGangId == g.Id) w.Hall.DAFriendGangId = -1;
    }
}
