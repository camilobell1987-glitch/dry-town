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
}

public enum ElectionKind { Alderman, Mayor }

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
        for (int i = 0; i < w.Map.WardCount; i++)
            w.Wards.Add(new Ward { Id = i, Name = names[i % names.Count], Alderman = NewPolitician(w.Rng, false) });
        w.Hall = new CityHall { Mayor = NewPolitician(w.Rng, false) };
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
        ward.OwnerGangId >= 0 && ward.OwnerGangId != g.Id && w.Week - ward.BoughtWeek < Content.AldermanLoyalWeeks;

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
        if (ward.OwnerGangId == g.Id) return false;
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
        foreach (var ward in w.Wards.Where(x => x.OwnerGangId >= 0))
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
    /// Each ward chooses between a reformer, a party regular and whichever outfits put money in.
    /// The vote is a weighted draw: money, muscle on the street and the incumbent's machine all count.
    /// </summary>
    private static void AldermanElections(World w)
    {
        var hall = w.Hall;
        foreach (var ward in w.Wards)
        {
            var field = new List<(int Gang, double Weight)>
            {
                (-2, 250 + hall.Outrage * 8 + (hall.Reform ? 150 : 0)), // reformer
                (-1, 350), // party regular
            };
            foreach (var g in w.LivingGangs)
            {
                long money = ward.Campaign.GetValueOrDefault(g.Id);
                bool incumbent = ward.OwnerGangId == g.Id;
                if (money <= 0 && !incumbent) continue;
                int turf = BusinessesIn(w, ward).Count(b => b.ProtectorGangId == g.Id);
                field.Add((g.Id, money * 0.6 + turf * 25 + (incumbent ? 250 : 0)));
            }
            int winner = Draw(w.Rng, field);
            string before = ward.Alderman;
            bool reformerBefore = ward.Reformer;
            int ownerBefore = ward.OwnerGangId;

            if (winner == ownerBefore && ownerBefore >= 0)
            {
                ward.BoughtWeek = w.Week;
                w.Log(EventKind.Politics, winner, $"Alderman {before} held {ward.Name}, and stays on {w.GangById(winner).Name}'s payroll.");
            }
            else if (winner == -1 && !reformerBefore && ownerBefore < 0)
            {
                w.Log(EventKind.Politics, -1, $"Alderman {before} held {ward.Name} for the party.");
            }
            else
            {
                ward.Alderman = NewPolitician(w.Rng, winner == -2);
                ward.Reformer = winner == -2;
                ward.OwnerGangId = Math.Max(-1, winner);
                ward.BoughtWeek = w.Week;
                string text = winner switch
                {
                    -2 => $"Reformer {ward.Alderman} won {ward.Name}. He won't take an envelope.",
                    -1 => $"Party man {ward.Alderman} won {ward.Name}.",
                    _ => $"{ward.Alderman} won {ward.Name} with {w.GangById(winner).Name}'s money behind him.",
                };
                w.Log(EventKind.Politics, Math.Max(-1, winner), text);
                if (ownerBefore >= 0 && ownerBefore != winner)
                    w.Log(EventKind.Politics, ownerBefore, $"{w.GangById(ownerBefore).Name} lost its alderman in {ward.Name}.");
            }
            ward.Campaign.Clear();
        }
    }

    /// <summary>The machine against the reformers. The outfit that put in the most money has the new mayor's ear.</summary>
    private static void MayorElection(World w)
    {
        var hall = w.Hall;
        long machineMoney = hall.Campaign.Values.Sum();
        double reform = 600 + hall.Outrage * 15;
        double machine = 600 + machineMoney * 0.5 + (hall.Reform ? 0 : 150);
        bool reformWins = w.Rng.NextDouble() * (reform + machine) < reform;
        var backer = hall.Campaign.Where(kv => kv.Value >= 500).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).DefaultIfEmpty(-1).First();
        string old = hall.Mayor;
        bool wasReform = hall.Reform;

        if (reformWins)
        {
            if (!wasReform) hall.Mayor = NewPolitician(w.Rng, true);
            hall.Reform = true;
            hall.FriendGangId = -1;
            // A reform mayor's new police chief cleans out the precincts: every alderman's deal is suddenly worth less.
            w.Log(EventKind.Politics, -1, wasReform
                ? $"Mayor {hall.Mayor} was re-elected on a clean-up ticket. The raids go on."
                : $"Reformer {hall.Mayor} beat Mayor {old}. The new police chief promises to clean up the city.");
        }
        else
        {
            if (wasReform || w.Rng.Chance(0.4)) hall.Mayor = NewPolitician(w.Rng, false);
            hall.Reform = false;
            hall.FriendGangId = backer;
            string who = hall.Mayor == old ? $"Mayor {hall.Mayor} was re-elected" : $"{hall.Mayor} was elected mayor";
            string friend = backer >= 0 ? $" {w.GangById(backer).Name} paid for it, and the police will go easy on them." : " The town stays wide open.";
            w.Log(EventKind.Politics, backer, who + "." + friend);
        }
        hall.Campaign.Clear();
        hall.Outrage = hall.Outrage / 2;
    }

    private static int Draw(Rng rng, List<(int Id, double Weight)> field)
    {
        double total = field.Sum(f => f.Weight), roll = rng.NextDouble() * total;
        foreach (var (id, weight) in field)
        {
            if (roll < weight) return id;
            roll -= weight;
        }
        return field[^1].Id;
    }

    /// <summary>A gang's dissolution frees its aldermen and the mayor's favour.</summary>
    public static void Forget(World w, Gang g)
    {
        foreach (var ward in w.Wards.Where(x => x.OwnerGangId == g.Id)) ward.OwnerGangId = -1;
        if (w.Hall.FriendGangId == g.Id) w.Hall.FriendGangId = -1;
    }
}
