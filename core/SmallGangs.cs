namespace DryTown.Core;

public static partial class Content
{
    /// <summary>The most blocks a small gang can hold. Hoodlums keep to their corner; a street gang runs two blocks, and sharks lend money across four.</summary>
    public static int MaxBlocks(GangKind kind) => kind switch
    {
        GangKind.Hoodlums => 1,
        GangKind.StreetGang => 2,
        GangKind.Sharks => 4,
        _ => int.MaxValue,
    };

    /// <summary>The most men a small gang keeps, boss included.</summary>
    public static int MaxMen(GangKind kind) => kind switch
    {
        GangKind.Hoodlums => 3,
        GangKind.StreetGang => 5,
        GangKind.Sharks => 5,
        _ => int.MaxValue,
    };

    /// <summary>The men a small gang starts with, boss included.</summary>
    public static int StartingMen(GangKind kind) => kind == GangKind.Hoodlums ? 2 : 3;

    public static string Label(GangKind kind) => kind switch
    {
        GangKind.Hoodlums => "hoodlums",
        GangKind.StreetGang => "street gang",
        GangKind.Sharks => "loan sharks",
        _ => "outfit",
    };

    /// <summary>What each kind of small gang calls itself: {0} is the street its block faces, {1} the boss's surname.</summary>
    public static string[] SmallGangPatterns(GangKind kind) => kind switch
    {
        GangKind.Hoodlums => new[] { "The {0} Street Hoodlums", "The {0} Street Toughs", "The {0} Street Rats" },
        GangKind.StreetGang => new[] { "The {0} Street Gang", "The {0} Street Boys", "The {0} Street Dukes" },
        _ => new[] { "{1}'s Sharks", "The {0} Street Sharks", "{1}'s Loan Office" },
    };

    /// <summary>The racket a small gang runs behind one of its shops: sharks lend money, a street gang runs numbers, hoodlums nothing.</summary>
    public static RacketKind SmallRacket(GangKind kind) => kind switch
    {
        GangKind.StreetGang => RacketKind.Numbers,
        GangKind.Sharks => RacketKind.LoanShark,
        _ => RacketKind.None,
    };

    public const int SmallGangCash = 250;

    /// <summary>The share of a man's wage a small gang pays him: the rest of his living is his cut of the corner.</summary>
    public const double SmallGangWages = 0.3;

    /// <summary>Weeks of a man's wages it takes to get him to walk out on his gang.</summary>
    public const int PoachWeeks = 4;

    /// <summary>What a small gang wants to fold into an outfit: something for each man, and for each shop it hands over.</summary>
    public const int AbsorbPerMan = 250, AbsorbPerBusiness = 150;

    /// <summary>A small gang hears an offer only from an outfit at least this many times stronger.</summary>
    public const double AbsorbPower = 1.5;

    /// <summary>How often a new small gang sets up, each week, while there are fewer than three quarters of the city's share.</summary>
    public const double NewSmallGangChance = 0.15;

    /// <summary>A rival outfit looks at the small gangs this often, and only at those this many blocks from its headquarters.</summary>
    public const double AiSmallGangs = 0.03;
    public const double AiSmallGangReach = 7;
}

/// <summary>
/// The small gangs: hoodlums, street gangs and loan sharks, each holding a block or a few and
/// never more. An outfit can shake down their shops one at a time, hire their men away, buy the
/// whole gang into its own, or wipe it out.
/// </summary>
public sealed partial class World
{
    /// <summary>The four outfits that run the city, the player's among them, while they last.</summary>
    public IEnumerable<Gang> Outfits => Gangs.Where(g => g.Alive && !g.IsSmall);
    public IEnumerable<Gang> SmallGangs => Gangs.Where(g => g.Alive && g.IsSmall);

    /// <summary>The blocks a small gang holds: the one its men hang about in, and wherever its shops are.</summary>
    public HashSet<int> BlocksOf(Gang g)
    {
        var blocks = TurfOf(g.Id).Select(b => Map.BlockOf(LotOf(b))).ToHashSet();
        if (g.HqLotId >= 0) blocks.Add(Map.BlockOf(HqOf(g)));
        return blocks;
    }

    /// <summary>Whether a gang can move on a business: an outfit anywhere, a small gang only on its own blocks, or one next to them while it has room to grow.</summary>
    public bool CanReach(Gang g, Business biz)
    {
        if (!g.IsSmall) return true;
        var blocks = BlocksOf(g);
        int block = Map.BlockOf(LotOf(biz));
        return blocks.Contains(block) || blocks.Count < Content.MaxBlocks(g.Kind) && blocks.Any(b => Map.NextDoor(b, block));
    }

    /// <summary>A small gang's name, from the street its corner faces.</summary>
    private string SmallGangName(Gang gang, Lot hq, string surname, HashSet<string> taken)
    {
        var patterns = Content.SmallGangPatterns(gang.Kind);
        string street = Map.StreetOf(hq);
        return patterns.OrderBy(_ => Rng.NextDouble()).Select(p => string.Format(p, street, surname)).FirstOrDefault(n => !taken.Contains(n))
               ?? $"{string.Format(patterns[0], street, surname)} of {Map.AvenueNames[hq.X / CityMap.StrideX]}";
    }

    /// <summary>The small gangs a new city starts with, each on a corner of its own with the shops of its block.</summary>
    private void PlaceSmallGangs()
    {
        for (int i = 0; i < Settings.SmallGangCount; i++) FoundSmallGang();
    }

    /// <summary>A small gang sets up on a free corner, as far from everyone else as it can, and takes the shops of its block.</summary>
    public Gang FoundSmallGang()
    {
        double roll = Rng.NextDouble();
        var kind = roll < 0.45 ? GangKind.Hoodlums : roll < 0.8 ? GangKind.StreetGang : GangKind.Sharks;
        var gang = FoundGang(isPlayer: false, Content.SmallGangCash + Rng.Range(0, 150), Content.StartingMen(kind), aggression: 0.3 + Rng.NextDouble() * 0.5, kind: kind);
        int block = Map.BlockOf(HqOf(gang));
        var men = HoodsOf(gang.Id).ToList();
        int n = 0;
        foreach (var biz in Businesses.Where(b => !b.IsProtected && Map.BlockOf(LotOf(b)) == block).OrderBy(b => b.Id))
        {
            biz.ProtectorGangId = gang.Id;
            biz.HandlerHoodId = men[n++ % men.Count].Id;
            biz.ProtectionRate = Content.DefaultRatePercent;
        }
        return gang;
    }

    /// <summary>What a small gang asks to fold into an outfit.</summary>
    public int AbsorbPrice(Gang small) =>
        HoodsOf(small.Id).Count() * Content.AbsorbPerMan + TurfOf(small.Id).Count() * Content.AbsorbPerBusiness;

    /// <summary>Why an outfit can't put it to a small gang to join it, or null.</summary>
    public string? CannotAbsorb(Gang g, Gang small)
    {
        if (!small.Alive || !small.IsSmall) return "Only a small gang can be brought into the outfit.";
        if (g.IsSmall) return "A small gang doesn't take in another.";
        if (g.Cash < AbsorbPrice(small)) return $"{small.Name} want ${AbsorbPrice(small):N0} to come in with you.";
        if (Power(g) < Power(small) * Content.AbsorbPower) return $"{small.Name} don't think you're big enough to be worth joining.";
        return null;
    }

    /// <summary>The chance a small gang takes the offer: better the bigger and better known the outfit, and the harder the man who puts it to them.</summary>
    public double AbsorbChance(Gang g, Gang small, Hood envoy)
    {
        double ratio = Power(g) / Math.Max(1, Power(small));
        return Math.Clamp(0.25 + Math.Min(0.3, (ratio - Content.AbsorbPower) * 0.03) + envoy.Intimidation * 0.03 + (g.Fear + g.Respect) / 500 - GrudgeOf(small.Id, g.Id) / 150.0, 0.05, 0.95);
    }

    /// <summary>What it takes to get a small gang's man to walk out on it.</summary>
    public int PoachFee(Hood man) => man.Wage * Content.PoachWeeks;

    /// <summary>Why an outfit can't hire a man away from a small gang, or null.</summary>
    public string? CannotPoach(Gang g, Hood man)
    {
        var his = GangById(man.GangId);
        if (!his.IsSmall || !his.Alive) return "Only a small gang's men can be hired away.";
        if (g.IsSmall) return "A small gang can't hire men away from another.";
        if (man.Id == his.BossHoodId) return $"{man.Name} runs {his.Name}. Bring the whole gang in instead.";
        if (!man.IsAvailable) return $"{man.Name} is out of reach.";
        if (g.Cash < PoachFee(man)) return $"{man.Name} wants ${PoachFee(man)} to walk out on {his.Name}.";
        return null;
    }

    /// <summary>The chance a man walks out on his small gang: the less he thinks of it and the more he thinks of you, the likelier.</summary>
    public double PoachChance(Gang g, Hood man) =>
        Math.Clamp(0.3 + (70 - man.Loyalty) / 100.0 + g.Respect / 300, 0.05, 0.9);

    /// <summary>Why an outfit can't send men to wipe out a small gang, or null.</summary>
    public string? CannotWipeOut(Gang g, Gang small)
    {
        if (!small.Alive || !small.IsSmall) return "Only a small gang can be wiped out in one go. An outfit takes a war.";
        if (g.IsSmall) return "A small gang doesn't go after another's corner.";
        if (!AvailableHoodsOf(small.Id).Any()) return $"{small.Name} have nobody on the street to go after.";
        return null;
    }

    /// <summary>How hard a small gang is to wipe out: every man it has on its corner.</summary>
    public double CornerStrength(Gang small)
    {
        var men = AvailableHoodsOf(small.Id).ToList();
        return men.Count == 0 ? 0 : men.Max(h => h.Strength) + men.Sum(h => h.Strength) * 0.25 + 2;
    }

    /// <summary>A small gang is finished: brought in or wiped out. Its corner is free.</summary>
    public void Fold(Gang small)
    {
        small.Alive = false;
        small.DissolvedWeek = Week;
        foreach (var crew in CrewsOf(small.Id).ToList()) Crews.Remove(crew);
        ReleaseHeadquarters(small);
    }
}

public sealed partial class Simulation
{
    public static double WipeOutChance(World w, Hood lead, IEnumerable<Hood> backup, Gang small) =>
        TakeoverChance(StormingStrength(lead, backup), w.CornerStrength(small));

    /// <summary>A man is offered more to work for the outfit than his gang pays him. If he takes it, he walks away from them.</summary>
    private void Poach(Gang gang, PoachOrder o)
    {
        var w = World;
        var man = w.Hoods.FirstOrDefault(h => h.Id == o.TargetHoodId);
        if (man == null || w.CannotPoach(gang, man) != null) return;
        var his = w.GangById(man.GangId);
        int fee = w.PoachFee(man);
        int from = HauntOf(w, man);
        string text;
        if (w.Rng.Chance(w.PoachChance(gang, man)))
        {
            gang.Cash -= fee;
            w.Ledger(gang.Id).Spending += fee;
            w.LeaveCrew(man.Id);
            foreach (var biz in w.TurfOf(his.Id).Where(b => b.HandlerHoodId == man.Id)) biz.HandlerHoodId = his.BossHoodId;
            man.GangId = gang.Id;
            man.Loyalty = 55;
            man.JoinedWeek = w.Week;
            w.AddGrudge(his.Id, gang.Id, 15);
            text = $"{man.Name} walked out on {his.Name} for ${fee} and signed on with {gang.Name}.";
            w.Act(new ScriptAction(ActionKind.Hire, gang.Id, man.Id, from, gang.HqLotId, -1, ActionResult.Success, text));
        }
        else
        {
            w.AddGrudge(his.Id, gang.Id, 5);
            text = $"{man.Name} wouldn't walk out on {his.Name} for {gang.Name}'s money.";
            w.Act(new ScriptAction(ActionKind.Hire, gang.Id, -1, gang.HqLotId, from, -1, ActionResult.Failed, text));
        }
        w.Log(EventKind.Recruited, gang.Id, text);
    }

    /// <summary>A man goes round to a small gang's corner and puts it to them: come in with the outfit, men, shops and all.</summary>
    private void Absorb(Gang gang, AbsorbOrder o)
    {
        var w = World;
        var small = w.Gangs.FirstOrDefault(g => g.Id == o.TargetGangId);
        if (small == null || w.CannotAbsorb(gang, small) != null) return;
        if (!TryUseHood(gang, o.HoodId, out var envoy)) return;
        int price = w.AbsorbPrice(small);
        int corner = small.HqLotId;
        if (!w.Rng.Chance(w.AbsorbChance(gang, small, envoy)))
        {
            w.AddGrudge(small.Id, gang.Id, 10);
            string no = $"{small.Name} heard {envoy.Name} out and told him {gang.Name} could keep its money.";
            w.Log(EventKind.Diplomacy, gang.Id, no);
            w.Act(new ScriptAction(ActionKind.Hire, gang.Id, envoy.Id, gang.HqLotId, corner, -1, ActionResult.Failed, no));
            return;
        }

        gang.Cash -= price;
        w.Ledger(gang.Id).Spending += price;
        gang.Cash += Math.Max(0, small.Cash);
        int men = 0;
        foreach (var man in w.Hoods.Where(h => h.GangId == small.Id && h.IsActive).ToList())
        {
            w.LeaveCrew(man.Id);
            man.GangId = gang.Id;
            man.JoinedWeek = w.Week;
            // The man who ran his own corner isn't used to taking orders.
            man.Loyalty = man.Id == small.BossHoodId ? 40 : 55;
            if (man.Id == small.BossHoodId) man.Ambition = Math.Max(man.Ambition, 60);
            men++;
        }
        int shops = 0;
        foreach (var biz in w.TurfOf(small.Id).ToList()) { biz.ProtectorGangId = gang.Id; shops++; }
        w.Fold(small);
        string text = $"{small.Name} came in with {gang.Name} for ${price:N0}: {men} men and {shops} shop{(shops == 1 ? "" : "s")}.";
        w.Log(EventKind.Takeover, gang.Id, text);
        w.Renown(gang, respect: 1);
        w.Act(new ScriptAction(ActionKind.Hire, gang.Id, envoy.Id, gang.HqLotId, corner, -1, ActionResult.Success, text));
    }

    /// <summary>Men go round to a small gang's corner to finish it. Those who aren't shot run, and its shops are left for anyone.</summary>
    private void WipeOut(Gang gang, WipeOutOrder o)
    {
        var w = World;
        var small = w.Gangs.FirstOrDefault(g => g.Id == o.TargetGangId);
        if (small == null || w.CannotWipeOut(gang, small) != null) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        var backupIds = backup.Select(h => h.Id).ToList();
        var team = backup.Prepend(hood).ToList();
        int corner = small.HqLotId;
        w.Hostile(gang, small, Content.GrudgeHit);
        gang.Heat = Math.Min(100, gang.Heat + 8 + GunHeat(team));

        if (w.Rng.Chance(WipeOutChance(w, hood, backup, small)))
        {
            int dead = 0;
            foreach (var man in w.Hoods.Where(h => h.GangId == small.Id && h.IsActive).ToList())
            {
                if (man.IsAvailable && w.Rng.Chance(0.5)) { Kill(man, $"when {gang.Name} wiped out {small.Name}"); dead++; hood.Deeds.Kills++; }
                else { w.LeaveCrew(man.Id); man.State = HoodState.Gone; }
            }
            foreach (var biz in w.TurfOf(small.Id).ToList()) { biz.ProtectorGangId = -1; biz.HandlerHoodId = -1; }
            w.Fold(small);
            string text = $"{gang.Name} wiped out {small.Name}: {(dead == 0 ? "nobody was killed, but" : dead == 1 ? "one man was shot and" : $"{dead} men were shot and")} the rest ran. Their shops are anybody's.";
            w.Log(EventKind.Hit, gang.Id, text);
            w.Renown(gang, fear: Content.FearHit);
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hit, gang.Id, hood.Id, gang.HqLotId, corner, -1, ActionResult.Won, text, small.Id)));
            MaybeWitness(gang, hood, w.Map.LotAt(corner), $"{hood.Name} and his men shoot up {small.Name}'s corner", 0.3);
            return;
        }

        int casualty = -1;
        string fail = $"{small.Name} saw {hood.Name}'s men coming and fought them off.";
        if (w.Rng.Chance(0.3 * (1 - Getaway(team))))
        {
            var fallen = backup.Count > 0 && w.Rng.Chance(0.5) ? w.Rng.Pick(backup) : hood;
            Kill(fallen, $"trying to wipe out {small.Name}");
            casualty = fallen.Id;
            fail = $"{small.Name} fought off {gang.Name}: {fallen.Name} is dead.";
        }
        w.Log(EventKind.Hit, gang.Id, fail);
        w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hit, gang.Id, hood.Id, gang.HqLotId, corner, -1, ActionResult.Lost, fail, small.Id, -1, casualty)));
    }

    /// <summary>Sunday: a city whose small gangs have thinned out sees new ones set up on empty corners.</summary>
    private void NewSmallGangs()
    {
        var w = World;
        if (!w.Settings.DirectorEnabled || w.Settings.SmallGangCount == 0) return;
        if (w.SmallGangs.Count() * 4 >= w.Settings.SmallGangCount * 3 || !w.Rng.Chance(Content.NewSmallGangChance)) return;
        if (!w.Map.Lots.Any(l => l.IsFree)) return;
        var gang = w.FoundSmallGang();
        w.Log(EventKind.NewGang, gang.Id, $"{gang.Name} set up on a corner of {w.Map.StreetOf(w.HqOf(gang))} St.");
    }
}

public static partial class AiPlanner
{
    /// <summary>A small gang's week: keep its numbers up, run its one racket, and lean on the shops of its own blocks.</summary>
    public static List<Order> PlanSmall(World w, Gang g)
    {
        var orders = new List<Order>();
        var turf = w.TurfOf(g.Id).ToList();
        long cash = g.Cash;
        if (w.HoodsOf(g.Id).Count() < Content.MaxMen(g.Kind) && cash > Content.RecruitCost * 2)
        {
            orders.Add(new RecruitOrder(g.Id));
            cash -= Content.RecruitCost;
        }
        foreach (var biz in turf)
        {
            int rate = biz.Resentment > 55 ? 8 : biz.Resentment < 20 ? 15 : Content.DefaultRatePercent;
            if (rate != biz.ProtectionRate) orders.Add(new SetRateOrder(g.Id, biz.Id, rate));
        }

        var hoods = w.AvailableHoodsOf(g.Id).OrderByDescending(h => h.Strength).ThenBy(h => h.Id).ToList();
        var kind = Content.SmallRacket(g.Kind);
        if (kind != RacketKind.None && hoods.Count > 0 && cash > Content.Rackets[kind].SetupCost + 150 && !turf.Any(b => b.Racket == kind))
        {
            var site = turf.Where(b => w.FreeBackRoom(b) && b.IsOpen && w.CannotOpenRacket(g, b, kind) == null).OrderByDescending(b => b.Takings).ThenBy(b => b.Id).FirstOrDefault();
            if (site != null)
            {
                var brains = hoods.OrderByDescending(h => h.Brains).ThenBy(h => h.Id).First();
                orders.Add(new RacketOrder(g.Id, brains.Id, site.Id, kind));
                hoods.Remove(brains);
            }
        }

        // The boss minds the corner while there's anyone else to send.
        if (hoods.Count >= 2) hoods.RemoveAll(h => h.Id == g.BossHoodId);
        var taken = new HashSet<int>();
        foreach (var hood in hoods)
        {
            Business? best = null;
            double bestScore = 60;
            foreach (var biz in w.Businesses)
            {
                if (!biz.IsOpen || biz.ProtectorGangId == g.Id || taken.Contains(biz.Id) || !w.CanReach(g, biz)) continue;
                double value = (biz.Takings * 0.12 + (biz.Racket != RacketKind.None ? 60 : 0)) * 10;
                double score;
                if (!biz.IsProtected) score = value * Simulation.ExtortChance(w, g, hood, biz);
                else
                {
                    // A small gang fights another for a shop, never an outfit.
                    var rival = w.GangById(biz.ProtectorGangId);
                    if (!rival.IsSmall) continue;
                    double win = Simulation.TakeoverChance(Simulation.StormingStrength(hood, Array.Empty<Hood>()), Simulation.DefenceStrength(w, rival, biz));
                    score = value * win * (0.3 + g.Aggression) - (1 - win) * 200;
                }
                if (score > bestScore) { bestScore = score; best = biz; }
            }
            if (best == null) continue;
            taken.Add(best.Id);
            orders.Add(new ExtortOrder(g.Id, hood.Id, best.Id));
        }
        return orders;
    }

    /// <summary>
    /// A rival outfit now and then looks at the small gangs near it: one it can afford it buys in,
    /// and one that has crossed it it wipes out. The rest it picks at a shop at a time.
    /// </summary>
    private static long PlanSmallGangs(World w, Gang g, List<Hood> hoods, long cash, List<Order> orders)
    {
        if (g.IsPlayer && w.Rng.Chance(0.5) || hoods.Count < 3 || !w.Rng.Chance(Content.AiSmallGangs)) return 0;
        var near = w.SmallGangs.Where(s => w.Map.Distance(w.HqOf(g), w.HqOf(s)) / (double)CityMap.StrideX <= Content.AiSmallGangReach).OrderBy(s => s.Id).ToList();
        var buy = near.Where(s => w.CannotAbsorb(g, s) == null && w.TurfOf(s.Id).Any() && cash > w.AbsorbPrice(s) * 2 + 800)
            .OrderByDescending(s => w.TurfOf(s.Id).Sum(b => b.Takings) - w.AbsorbPrice(s) / 10).ThenBy(s => s.Id).FirstOrDefault();
        if (buy != null)
        {
            var envoy = hoods.OrderByDescending(h => h.Intimidation).ThenBy(h => h.Id).First();
            hoods.Remove(envoy);
            orders.Add(new AbsorbOrder(g.Id, envoy.Id, buy.Id));
            return w.AbsorbPrice(buy);
        }
        var enemy = near.Where(s => w.CannotWipeOut(g, s) == null && (w.GrudgeOf(g.Id, s.Id) >= 25 || g.Aggression > 0.65)).OrderByDescending(s => w.GrudgeOf(g.Id, s.Id)).ThenBy(s => s.Id).FirstOrDefault();
        if (enemy == null || hoods.Count < 4) return 0;
        var lead = hoods[0];
        var backup = hoods.Skip(1).Take(2).ToList();
        if (Simulation.WipeOutChance(w, lead, backup, enemy) < 0.55) return 0;
        hoods.Remove(lead);
        foreach (var b in backup) hoods.Remove(b);
        orders.Add(new WipeOutOrder(g.Id, lead.Id, enemy.Id, backup.Select(b => b.Id).ToArray()));
        return 0;
    }
}
