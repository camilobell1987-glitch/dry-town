namespace DryTown.Core;

/// <summary>What a gang can do to one of the city's residents.</summary>
public enum Lean { Bribe, Intimidate, Extort, Kidnap, Kill, Blackmail, TurnIn, Square, Favour, GoodWord }

/// <summary>Where a resident stands with you after you've dealt with him.</summary>
public enum Standing { None, Bought, Scared, Held, Dead, Blackmailed, Jailed }

/// <summary>Dealing with residents: buying them, scaring them, shaking them down, snatching them, or killing them.</summary>
public static partial class Content
{
    /// <summary>Weeks a scared man stays scared, and weeks before the same man can be shaken down again.</summary>
    public const int ScaredWeeks = 8, ShakedownWeeks = 4;

    /// <summary>Weeks his family has to pay a ransom before he's let go anyway, and how many weeks' worth they're asked for.</summary>
    public const int ResidentRansomWeeks = 3, RansomOfWorth = 4;

    /// <summary>How many times what someone is worth a shakedown takes off him.</summary>
    public const int ShakedownOfWorth = 2;

    /// <summary>How much more a ward's rumours tell you for each man there on your payroll or afraid of you.</summary>
    public const double InformantRumour = 0.1;

    /// <summary>How much less likely a witness is to come forward in a ward for each man there in your pocket, and the most it can fall.</summary>
    public const double Hush = 0.03, HushMost = 0.15;

    /// <summary>
    /// A shopkeeper in your pocket: how much likelier he is to take your protection, how much it helps to have him open
    /// the back door when you take his shop from another gang, and how much less he minds paying you each week.
    /// </summary>
    public const double PocketShakedown = 0.25;
    public const int PocketStorm = 2, PocketResentment = 3;

    /// <summary>How many facts you need about someone before you can judge their nerve.</summary>
    public const int NerveFacts = 4;

    public static string Label(Lean lean) => lean switch
    {
        Lean.Bribe => "bribe",
        Lean.Intimidate => "lean on",
        Lean.Extort => "shake down",
        Lean.Kidnap => "snatch",
        Lean.Blackmail => "blackmail",
        Lean.TurnIn => "turn in",
        Lean.Square => "square things with",
        Lean.Favour => "do a good turn for",
        Lean.GoodWord => "put in a good word for",
        _ => "kill",
    };
}

public sealed partial class Acquaintance
{
    public Standing Standing { get; set; }

    /// <summary>When being scared wears off, a ransom falls due, or he can next be shaken down.</summary>
    public int UntilWeek { get; set; }
    public int ShakenWeek { get; set; } = -1000;

    /// <summary>What a ransom is set at while he's held, and the gang holding or paying him.</summary>
    public int Ransom { get; set; }
    public int GangId { get; set; } = -1;

    /// <summary>A line for the card: how he died, who has him.</summary>
    public string Note { get; set; } = "";

    /// <summary>What he's worth, the shop he runs and the tenements he owns, noted once you've dealt with him.</summary>
    public int Worth { get; set; }
    public int BusinessId { get; set; } = -1;
    public int Tenements { get; set; }

    /// <summary>Whether he's bought or frightened by this gang just now.</summary>
    public bool InPocketOf(int gangId) => GangId == gangId && Standing is Standing.Bought or Standing.Scared or Standing.Blackmailed;
}

public sealed partial class World
{
    /// <summary>
    /// How much nerve a resident has, 1 to 10: whether he takes money, scares, or fights back. Like everything else about
    /// him it's fixed by the city's seed and his number, so asking twice gets the same answer.
    /// </summary>
    public int NerveOf(int citizen)
    {
        ulong h = Settings.Seed * 0x9E3779B97F4A7C15ul ^ (ulong)(uint)citizen * 0xBF58476D1CE4E5B9ul ^ 0x5bd1e995ul;
        h ^= h >> 29;
        h *= 0x94D049BB133111EBul;
        h ^= h >> 32;
        return 1 + (int)(h % 10);
    }

    public Standing StandingOf(int citizen) => Acquaintances.TryGetValue(citizen, out var a) ? a.Standing : Standing.None;

    /// <summary>Whether someone can be dealt with at all now, and why not.</summary>
    public string? CannotLean(Gang g, int citizen, Lean lean)
    {
        var a = Acquaintances.GetValueOrDefault(citizen);
        return a?.Standing switch
        {
            Standing.Dead => "They're dead.",
            Standing.Jailed => "They're in jail.",
            Standing.Held => a.GangId == g.Id ? "You're holding them already." : "Somebody else is holding them.",
            Standing.Bought when lean == Lean.Bribe => "They're on your payroll already.",
            Standing.Blackmailed when lean == Lean.Blackmail => "They're paying you already.",
            _ when lean == Lean.Blackmail && !KnownSecretsOf(citizen).Any() => "You've nothing on them yet.",
            _ when lean == Lean.TurnIn && KnownCrime(citizen) == null => "You know of no crime to turn them in for.",
            _ when lean is Lean.Bribe or Lean.Square or Lean.Favour or Lean.GoodWord && HasVendetta(g, citizen) => "They've sworn a vendetta against you. Nothing will square it.",
            _ when lean is Lean.Square or Lean.Favour && BadBlood(g, citizen) == 0 => "They've no quarrel with you.",
            _ when lean == Lean.GoodWord && a?.BusinessId is null or < 0 => "They've no business to speak up for.",
            _ when lean == Lean.Extort && a != null && Week - a.ShakenWeek < Content.ShakedownWeeks => "They were shaken down only lately. Give it a few weeks.",
            _ => null,
        };
    }

    /// <summary>What a bribe costs for someone of this worth, and what keeping him sweet costs each week after.</summary>
    public static int BribePrice(int worth) => 20 + worth * 2;
    public static int Retainer(int worth) => Math.Max(2, worth / 5);

    /// <summary>Whether you know enough about someone, or have dealt with him, to judge how much nerve he has.</summary>
    public bool ReadsNerve(Gang g, int citizen) =>
        Acquaintances.TryGetValue(citizen, out var a) && (a.GangId == g.Id && a.Standing != Standing.None
            || System.Numerics.BitOperations.PopCount((uint)a.Known) >= Content.NerveFacts);

    /// <summary>Whether the man who runs this shop is bought or frightened by this gang.</summary>
    public bool OwnerInPocket(Gang g, Business biz) =>
        g.IsPlayer && Acquaintances.Values.Any(a => a.BusinessId == biz.Id && a.InPocketOf(g.Id));

    /// <summary>How many people in a ward are in a gang's pocket, a landlord counting once more for each building he owns.</summary>
    public double InPocket(Gang g, int wardId) =>
        !g.IsPlayer || wardId < 0 ? 0 : Acquaintances.Values.Where(a => a.WardId == wardId && a.InPocketOf(g.Id)).Sum(a => (1 + a.Tenements) * Content.AsInformant(a.Traits));
}

/// <summary>Send a man to deal with a resident.</summary>
public sealed record LeanOrder(int GangId, int HoodId, int CitizenId, int LotId, string Who, Lean Lean, int Worth, int BusinessId = -1, int Tenements = 0) : Order(GangId);

public sealed partial class Simulation
{
    /// <summary>The chance a man pulls it off against someone with this much nerve, knowing what you know about him.</summary>
    public static double LeanChance(Hood man, Lean lean, int nerve, Fact known = Fact.None, double leverage = 0, double traits = 0) => Math.Clamp(traits + lean switch
    {
        // Money talks, except to the honest few.
        Lean.Bribe => 1.05 - nerve * 0.07 + man.Brains * 0.02,
        Lean.Intimidate or Lean.Extort => 0.35 + man.Intimidation * 0.06 - nerve * 0.04,
        Lean.Kidnap => 0.3 + man.Muscle * 0.04 + man.Stealth * 0.03 - nerve * 0.02,
        Lean.Blackmail => 0.45 + man.Brains * 0.03 - nerve * 0.03,
        // The police are glad of a tip, whoever it comes from.
        Lean.TurnIn => 0.85 + man.Brains * 0.01,
        // Money and a few kind words, from a man who knows how to say them.
        Lean.Square => 0.55 + man.Brains * 0.03 - nerve * 0.02,
        // A good turn costs nothing but the man's week, and most people take one.
        Lean.Favour => 0.7 + man.Brains * 0.02 - nerve * 0.01,
        Lean.GoodWord => 0.45 + man.Brains * 0.03,
        _ => 0.5 + man.Muscle * 0.04 + man.Stealth * 0.01 - nerve * 0.01,
    } + KnowingHelps(lean, known) + (lean is Lean.Intimidate or Lean.Extort or Lean.Blackmail ? leverage : 0), 0.05, 0.95);

    /// <summary>
    /// What it's worth knowing about someone before you go to him: where he lives, for a threat or a snatch; where he
    /// works, to find him alone or know what he can spare; and his people, to talk to him in his own language.
    /// </summary>
    public static double KnowingHelps(Lean lean, Fact known)
    {
        double home = known.HasFlag(Fact.Home) ? 1 : 0, work = known.HasFlag(Fact.Work) ? 1 : 0, origin = known.HasFlag(Fact.Origin) ? 1 : 0;
        return lean switch
        {
            Lean.Bribe => work * 0.05 + origin * 0.05,
            Lean.Intimidate => home * 0.08,
            Lean.Extort => home * 0.05 + work * 0.05,
            Lean.Kidnap => home * 0.12,
            Lean.Blackmail or Lean.TurnIn or Lean.Square => 0,
            Lean.Favour => home * 0.05,
            Lean.GoodWord => work * 0.1,
            _ => home * 0.05 + work * 0.05,
        };
    }

    /// <summary>The chance someone sees it and thinks about going to the police.</summary>
    public static double WitnessChance(Lean lean, bool ok) => (lean, ok) switch
    {
        (Lean.Kill, true) => 0.35,
        (Lean.Kill, false) => 0.6,
        (Lean.Kidnap, _) => 0.3,
        (Lean.Bribe or Lean.Blackmail or Lean.TurnIn or Lean.Square or Lean.Favour or Lean.GoodWord, _) => 0,
        (_, false) => 0.15,
        _ => 0.05,
    };

    private void LeanOn(Gang gang, LeanOrder o)
    {
        var w = World;
        if (w.CannotLean(gang, o.CitizenId, o.Lean) != null) return;
        int price = o.Lean == Lean.Square ? Content.SquarePrice(o.Worth, w.BadBlood(gang, o.CitizenId)) : World.BribePrice(o.Worth);
        if (o.Lean is Lean.Bribe or Lean.Square && gang.Cash < price) return;
        if (!TryUseHood(gang, o.HoodId, out var man)) return;
        var lot = w.Map.LotAt(o.LotId);
        var a = w.Meet(o.CitizenId, lot.WardId);
        int nerve = w.NerveOf(o.CitizenId);
        bool ok = w.Rng.Chance(LeanChance(man, o.Lean, nerve, a.Known, w.Leverage(gang, o.CitizenId), w.TraitOdds(o.CitizenId, o.Lean, knownOnly: false) + w.GrudgeOdds(gang, o.CitizenId, o.Lean, knownOnly: false)));
        // However it goes, you see what they're made of where it counts.
        foreach (var t in a.Traits.Where(t => Content.TraitOdds(t, o.Lean) != 0).ToList()) w.LearnTrait(o.CitizenId, t);
        if (!ok && o.Lean != Lean.TurnIn) gang.Heat = Math.Clamp(gang.Heat + Content.FailHeat(a.Traits, a.Background), 0, 100);
        a.Worth = o.Worth;
        a.BusinessId = o.BusinessId;
        a.Tenements = o.Tenements;
        string text;
        var kind = ActionKind.Extort;
        var result = ok ? ActionResult.Success : ActionResult.Failed;
        switch (o.Lean)
        {
            case Lean.Bribe when ok:
                gang.Cash -= price;
                w.Ledger(gang.Id).Spending += price;
                a.Standing = Standing.Bought;
                a.GangId = gang.Id;
                a.Known = Fact.All;
                foreach (var tie in a.Ties.ToList()) w.LearnTie(o.CitizenId, tie);
                foreach (var t in a.Traits) w.LearnTrait(o.CitizenId, t);
                w.LearnGrievances(o.CitizenId, 1);
                kind = ActionKind.Hire;
                text = $"{man.Name} put {o.Who} on the payroll for ${price}, and ${World.Retainer(o.Worth)} a week after. They'll tell you what they hear.";
                break;
            case Lean.Bribe:
                gang.Heat = Math.Min(100, gang.Heat + 2);
                kind = ActionKind.Hire;
                text = $"{o.Who} wouldn't take {man.Name}'s money, and told the beat cop about it.";
                break;
            case Lean.Intimidate when ok:
                Scare(o.CitizenId, a, gang);
                gang.Heat = Math.Min(100, gang.Heat + 1);
                w.Renown(gang, fear: 0.5);
                text = $"{man.Name} leaned on {o.Who}, who told him everything and will be no trouble.";
                break;
            case Lean.Extort when ok:
                int take = Math.Max(5, o.Worth * Content.ShakedownOfWorth);
                gang.Cash += take;
                w.Ledger(gang.Id).Rackets += take;
                a.ShakenWeek = w.Week;
                Scare(o.CitizenId, a, gang);
                gang.Heat = Math.Min(100, gang.Heat + 2);
                w.Renown(gang, fear: 0.5, respect: -0.5);
                text = $"{man.Name} shook down {o.Who} for ${take}.";
                break;
            case Lean.Intimidate or Lean.Extort:
                a.ShakenWeek = w.Week;
                gang.Heat = Math.Min(100, gang.Heat + 3);
                text = $"{o.Who} stood up to {man.Name} and went to the police.";
                break;
            case Lean.Kidnap when ok:
                a.Standing = Standing.Held;
                a.GangId = gang.Id;
                a.Ransom = Math.Max(50, o.Worth * Content.RansomOfWorth);
                a.UntilWeek = w.Week + Content.ResidentRansomWeeks;
                a.Note = $"Held by {gang.Name} for ${a.Ransom}.";
                gang.Heat = Math.Min(100, gang.Heat + 6);
                w.Renown(gang, fear: 1);
                kind = ActionKind.Hit;
                result = ActionResult.Won;
                text = $"{man.Name} snatched {o.Who} off the street. The family has been asked for ${a.Ransom}.";
                break;
            case Lean.Kidnap:
                gang.Heat = Math.Min(100, gang.Heat + 4);
                kind = ActionKind.Hit;
                text = $"{o.Who} fought {man.Name} off in the street and got away.";
                break;
            case Lean.Kill when ok:
                a.Standing = Standing.Dead;
                a.GangId = gang.Id;
                a.Note = $"Killed by {man.Name}, week {w.Week}.";
                gang.Heat = Math.Min(100, gang.Heat + 12);
                // Killing a nobody makes you feared, and the papers and the neighbourhood think less of you.
                w.Renown(gang, fear: Content.FearHit / 2, respect: -3);
                kind = ActionKind.Hit;
                result = ActionResult.Killed;
                text = $"{man.Name} killed {o.Who} in the street.";
                break;
            case Lean.Blackmail when ok:
                a.Standing = Standing.Blackmailed;
                a.GangId = gang.Id;
                a.UntilWeek = w.Week + Content.HushWeeks;
                a.Known |= Fact.Name | Fact.Job | Fact.Work | Fact.Home;
                w.Renown(gang, fear: 0.2);
                text = $"{man.Name} let {o.Who} know what he knows. They'll pay ${World.HushMoney(o.Worth)} a week to keep it quiet.";
                break;
            case Lean.Blackmail:
                gang.Heat = Math.Min(100, gang.Heat + 3);
                text = $"{o.Who} called {man.Name}'s bluff and went to the police.";
                break;
            case Lean.TurnIn when ok:
                var crime = w.KnownCrime(o.CitizenId)!.Value;
                var (months, verdict) = Sentence(crime, o.Worth);
                a.Standing = Standing.Jailed;
                a.GangId = gang.Id;
                a.UntilWeek = w.Week + Math.Max(1, months * Content.WeeksPerYear / 12);
                int outYear = Content.StartYear + a.UntilWeek / Content.WeeksPerYear;
                a.Note = $"Serving {Content.Term(months)} for {Content.Crime(crime)}. Due out in {outYear}.";
                string trial = verdict switch
                {
                    Content.Verdict.Deal => "pleaded guilty for a lighter term and got",
                    Content.Verdict.Harsh => "drew a hanging judge and got",
                    _ => "got",
                };
                gang.Heat = Math.Max(0, gang.Heat - Content.TipHeat);
                var shop = o.BusinessId >= 0 ? w.Businesses.FirstOrDefault(b => b.Id == o.BusinessId) : null;
                if (shop != null) shop.ShutWeeks = Math.Max(shop.ShutWeeks, Content.ShutForOwner);
                kind = ActionKind.Hire;
                text = $"{man.Name} tipped off the precinct, and {o.Who} was taken in for {Content.Crime(crime)}. They {trial} {Content.Term(months)}.{(shop != null ? $" {shop.Name} is shut for a month while the family sorts things out." : "")} The police owe you one.";
                break;
            case Lean.TurnIn:
                kind = ActionKind.Hire;
                text = $"{man.Name} tipped off the precinct about {o.Who}, but the police couldn't make it stick.";
                break;
            case Lean.Square when ok:
                gang.Cash -= price;
                w.Ledger(gang.Id).Spending += price;
                // They let it lie, and their family takes the hint.
                foreach (var g in w.AgainstGang(gang, Party.Resident, o.CitizenId).ToList())
                {
                    g.Known = true;
                    w.Ease(g, Content.Squared);
                }
                foreach (var tie in a.Ties)
                    foreach (var g in w.AgainstGang(gang, Party.Resident, tie.Id).ToList()) w.Ease(g, Content.KinSquared);
                kind = ActionKind.Hire;
                text = $"{man.Name} squared things with {o.Who} for ${price}. They'll let it lie.";
                break;
            case Lean.Favour when ok:
                foreach (var g in w.AgainstGang(gang, Party.Resident, o.CitizenId).ToList())
                {
                    g.Known = true;
                    w.Ease(g, Content.Favoured);
                }
                w.Renown(gang, respect: 0.1);
                kind = ActionKind.Hire;
                text = $"{man.Name} did {o.Who} a good turn. They're warming to you.";
                break;
            case Lean.Favour:
                kind = ActionKind.Hire;
                text = $"{o.Who} wanted nothing from {man.Name}.";
                break;
            case Lean.GoodWord:
            {
                var theirs = w.Businesses.FirstOrDefault(b => b.Id == a.BusinessId);
                string where = theirs?.Name ?? "their business";
                kind = ActionKind.Hire;
                if (!ok)
                {
                    text = $"{man.Name} put in a good word around the ward for {where}, but nobody paid him any mind.";
                    break;
                }
                foreach (var g in w.AgainstGang(gang, Party.Resident, o.CitizenId).ToList())
                {
                    g.Known = true;
                    w.Ease(g, Content.GoodWord);
                }
                if (theirs != null) theirs.Resentment = Math.Max(0, theirs.Resentment - 10);
                w.Renown(gang, respect: 0.1);
                text = $"{man.Name} put in a good word around the ward for {where}, and sent trade their way. {o.Who} is grateful.";
                break;
            }
            case Lean.Square:
                foreach (var g in w.AgainstGang(gang, Party.Resident, o.CitizenId).ToList()) g.Known = true;
                w.Bear(Party.Resident, o.CitizenId, Party.Gang, gang.Id, 5, "threw your money back at them", lot.WardId, known: true);
                kind = ActionKind.Hire;
                text = $"{o.Who} threw {man.Name}'s money back in his face. They won't be bought off.";
                break;
            default:
                gang.Heat = Math.Min(100, gang.Heat + 6);
                Scare(o.CitizenId, a, gang);
                kind = ActionKind.Hit;
                text = $"{man.Name} went after {o.Who}, who got away hurt and terrified.";
                break;
        }
        if (o.Lean is not (Lean.Square or Lean.Favour or Lean.GoodWord)) Wronged(gang, man, a, o.CitizenId, o.Lean, ok, lot.WardId);
        w.Log(EventKind.Street, gang.Id, text);
        w.Act(new ScriptAction(kind, gang.Id, man.Id, gang.HqLotId, o.LotId, -1, result, text));
        MaybeWitness(gang, man, lot, $"{man.Name} {Content.Label(o.Lean)} {o.Who}", WitnessChance(o.Lean, ok));
    }

    /// <summary>What the court gives someone for a crime, in months, and how the trial went.</summary>
    private (int Months, Content.Verdict Verdict) Sentence(Secret crime, int worth)
    {
        var (least, most) = Content.Penalty(Content.Offence(crime));
        var (deal, harsh) = Content.VerdictOdds(worth, World.Hall.Reform);
        double roll = World.Rng.NextDouble();
        var verdict = roll < deal ? Content.Verdict.Deal : roll < deal + harsh ? Content.Verdict.Harsh : Content.Verdict.Fair;
        // A deal lands in the bottom third of the range, a hanging judge in the top third, anyone else in the middle.
        double third = (most - least) / 3, at = World.Rng.NextDouble();
        double years = verdict switch
        {
            Content.Verdict.Deal => least + at * third,
            Content.Verdict.Harsh => most - third + at * third,
            _ => least + third + at * third,
        };
        return ((int)Math.Round(years * 12), verdict);
    }

    private void Scare(int citizen, Acquaintance a, Gang gang)
    {
        if (a.Standing is Standing.Bought or Standing.Held or Standing.Dead) return;
        a.Standing = Standing.Scared;
        a.GangId = gang.Id;
        a.UntilWeek = World.Week + Content.ScaredWeeks;
        a.Known |= Fact.Name | Fact.Job | Fact.Work | Fact.Home;
        // A frightened man says who he'd want kept out of it.
        foreach (var tie in a.Ties.ToList()) World.LearnTie(citizen, tie);
    }

    /// <summary>Sunday for the people you've dealt with: ransoms paid or given up on, and fear wearing off.</summary>
    private void Residents()
    {
        var w = World;
        var payroll = new Dictionary<int, (int Paid, int People, int Quit)>();
        var hush = new Dictionary<int, (int Paid, int People)>();
        foreach (var (id, a) in w.Acquaintances.OrderBy(kv => kv.Key))
        {
            var gang = w.Gangs.FirstOrDefault(g => g.Id == a.GangId);
            if (a.Standing == Standing.Scared && w.Week >= a.UntilWeek) a.Standing = Standing.None;
            if (a.Standing == Standing.Jailed && w.Week >= a.UntilWeek)
            {
                a.Standing = Standing.None;
                a.Note = "";
            }
            if (a.Standing == Standing.Blackmailed && gang != null)
            {
                if (!gang.Alive || w.Week >= a.UntilWeek) a.Standing = Standing.None;
                else if (w.Rng.Chance(Content.HushSnaps * (a.Traits.Contains(Trait.HardBoiled) ? 2 : 1) * (a.Traits.Contains(Trait.Hothead) ? 1.5 : 1) * (a.Traits.Contains(Trait.Yellow) ? 0.5 : 1)))
                {
                    // He'd sooner face the shame than keep paying.
                    a.Standing = Standing.None;
                    gang.Heat = Math.Min(100, gang.Heat + 4);
                    w.Log(EventKind.Street, gang.Id, "Someone you were blackmailing stopped paying and went to the police.");
                }
                else
                {
                    int paid = World.HushMoney(a.Worth);
                    gang.Cash += paid;
                    w.Ledger(gang.Id).Rackets += paid;
                    var sum = hush.GetValueOrDefault(gang.Id);
                    hush[gang.Id] = (sum.Paid + paid, sum.People + 1);
                }
                continue;
            }
            if (a.Standing == Standing.Bought && gang != null)
            {
                // A man on the payroll stays bought as long as the envelope keeps coming.
                int due = World.Retainer(a.Worth);
                var sum = payroll.GetValueOrDefault(gang.Id);
                int grudge = w.BadBlood(gang, id);
                if (grudge >= Content.QuitGrudge && w.Rng.Chance(grudge / 800.0))
                {
                    // The money never made up for what was done to them.
                    a.Standing = Standing.None;
                    gang.Heat = Math.Min(100, gang.Heat + 4);
                    w.Log(EventKind.Street, gang.Id, "Someone on your payroll who never forgave you quit, and went to the police.");
                    continue;
                }
                if (gang.Alive && gang.Cash >= due)
                {
                    gang.Cash -= due;
                    w.Ledger(gang.Id).Spending += due;
                    payroll[gang.Id] = (sum.Paid + due, sum.People + 1, sum.Quit);
                }
                else
                {
                    a.Standing = Standing.None;
                    payroll[gang.Id] = (sum.Paid, sum.People, sum.Quit + 1);
                }
                continue;
            }
            if (a.Standing != Standing.Held) continue;
            if (gang is not { Alive: true })
            {
                a.Standing = Standing.None;
                a.Note = "";
                continue;
            }
            string who = a.Known.HasFlag(Fact.Name) ? "a hostage" : "someone you're holding";
            if (w.Rng.Chance(0.5))
            {
                gang.Cash += a.Ransom;
                w.Ledger(gang.Id).Rackets += a.Ransom;
                w.Log(EventKind.Street, gang.Id, $"The family paid ${a.Ransom} and {who} was let go.");
            }
            else if (w.Week < a.UntilWeek) continue;
            else w.Log(EventKind.Street, gang.Id, $"Nobody paid for {who}, and they were let go.");
            a.Standing = Standing.Scared;
            a.UntilWeek = w.Week + Content.ScaredWeeks;
            a.Note = "";
        }
        foreach (var (gangId, (paid, people)) in hush)
            w.Log(EventKind.Street, gangId, $"Collected ${paid} in hush money from {(people == 1 ? "the one person" : $"the {people} people")} you're blackmailing.");
        foreach (var (gangId, (paid, people, quit)) in payroll)
        {
            if (people > 0) w.Log(EventKind.Street, gangId, $"Paid ${paid} to the {(people == 1 ? "one man" : $"{people} people")} on your payroll.");
            if (quit > 0) w.Log(EventKind.Street, gangId, $"{(quit == 1 ? "Someone" : $"{quit} people")} went off your payroll when the money didn't come.");
        }
    }

    /// <summary>How much more the street tells you about a ward for the people there you've bought or scared.</summary>
    private double Informants(Gang g, int wardId) => Content.InformantRumour * World.InPocket(g, wardId);
}
