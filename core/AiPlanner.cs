namespace DryTown.Core;

/// <summary>
/// Heuristic planner used by rival gangs, and by the player's gang on autopilot
/// (soak tests, or "plan my week for me").
/// </summary>
public static class AiPlanner
{
    public static List<Order> Plan(World w, Gang g)
    {
        var orders = new List<Order>();
        var turf = w.TurfOf(g.Id).ToList();
        int active = w.HoodsOf(g.Id).Count();
        long cash = g.Cash;

        if (g.Heat > 45 && cash > 600)
        {
            int units = Math.Min(5, (g.Heat - 30) / Content.HeatPerBribeUnit);
            int amount = (int)Math.Min(units * Content.BribeUnit, cash / 3 / Content.BribeUnit * Content.BribeUnit);
            if (amount > 0) { orders.Add(new BribeOrder(g.Id, amount)); cash -= amount; }
        }

        int wanted = Math.Max(5, 3 + turf.Count / 3);
        // A good man on the street is worth his asking price over a stranger off the boat.
        var hires = w.People.Where(p => p.Kind == PersonKind.Drifter && p.Intimidation + p.Muscle + p.Brains + p.Stealth >= 24)
            .OrderByDescending(p => p.Intimidation + p.Muscle + p.Brains + p.Stealth).ThenBy(p => p.Id).ToList();
        for (int i = 0; i < 2 && active + i < wanted && cash > Content.RecruitCost * 4; i++)
        {
            var drifter = hires.FirstOrDefault(p => cash > p.Fee * 4);
            if (drifter != null)
            {
                orders.Add(new HireOrder(g.Id, drifter.Id));
                hires.Remove(drifter);
                cash -= drifter.Fee;
                continue;
            }
            orders.Add(new RecruitOrder(g.Id));
            cash -= Content.RecruitCost;
        }

        cash -= PlanStreet(w, g, cash, orders);
        cash -= PlanGear(w, g, cash, orders);
        cash -= PlanFronts(w, g, turf, cash, orders);
        cash -= PlanCourts(w, g, turf, cash, orders);

        // An aging boss with money to spare brings his family into the business.
        if (w.HoodById(g.BossHoodId).Age(w.Week) >= 45 && cash > Content.FamilyCost * 5 && Simulation.CanBringInFamily(w, g))
        {
            orders.Add(new RecruitOrder(g.Id, Family: true));
            cash -= Content.FamilyCost;
        }

        cash -= PlanPolitics(w, g, turf, cash, orders);

        foreach (var biz in turf)
        {
            int rate = biz.Resentment > 55 ? 8 : biz.Resentment < 20 ? 15 : Content.DefaultRatePercent;
            if (rate != biz.ProtectionRate) orders.Add(new SetRateOrder(g.Id, biz.Id, rate));
        }

        var hoods = w.AvailableHoodsOf(g.Id).ToList();
        // The boss stays home once there are soldiers to send.
        if (hoods.Count >= 3) hoods.RemoveAll(h => h.Id == g.BossHoodId);
        hoods = hoods.OrderByDescending(h => h.Strength).ThenBy(h => h.Id).ToList();

        if (g.Heat < 50 && hoods.Count > 0)
        {
            var site = turf
                .Where(b => b.Racket == RacketKind.None && b.IsOpen)
                .OrderByDescending(b => b.Takings).ThenBy(b => b.Id)
                .FirstOrDefault();
            if (site != null)
            {
                var racket = Content.RacketsFor(site.Kind)
                    .Select(k => Content.Rackets[k])
                    .Where(r => !r.NeedsProhibition || w.Prohibition)
                    // The dearer setups are for outfits with money behind them, and a press only for the well off.
                    .Where(r => cash > (r.SetupCost >= 900 ? r.SetupCost * 2 : r.SetupCost) + 400)
                    .Where(r => r.Kind != RacketKind.Counterfeit || cash > 5000)
                    .OrderByDescending(r => r.WeeklyIncome - r.WeeklyHeat * 15 - (r.Kind == RacketKind.Counterfeit ? 60 : 0))
                    .FirstOrDefault();
                if (racket != null)
                {
                    var brains = hoods.OrderByDescending(h => h.Brains).First();
                    orders.Add(new RacketOrder(g.Id, brains.Id, site.Id, racket.Kind));
                    hoods.Remove(brains);
                    cash -= racket.SetupCost;
                }
            }
        }

        // Post the best man on the most valuable racket once there are hands to spare.
        if (hoods.Count >= 5)
        {
            var prize = turf.Where(b => b.Racket != RacketKind.None && b.IsOpen).OrderByDescending(b => b.Takings).ThenBy(b => b.Id).FirstOrDefault();
            if (prize != null)
            {
                var guard = hoods[0];
                orders.Add(new GuardOrder(g.Id, guard.Id, prize.Id));
                hoods.Remove(guard);
            }
        }

        PlanCrimes(w, g, turf, hoods, cash, orders);

        // Score each target as about ten weeks of income, less the expected cost of losing the man.
        // Small gangs value their men more and so pick fewer fights.
        double manCost = (active <= 4 ? 900 : 450) * (1.3 - g.Aggression);
        var taken = new HashSet<int>();
        var assigned = new HashSet<int>();
        foreach (var hood in hoods)
        {
            if (assigned.Contains(hood.Id)) continue;
            // Spare hands, weakest first, who could go along as backup on a hard job.
            var spare = hoods.Where(h => h.Id != hood.Id && !assigned.Contains(h.Id)).Reverse().ToList();
            Business? best = null;
            List<Hood> bestBackup = new();
            double bestScore = 0;
            foreach (var biz in w.Businesses)
            {
                if (!biz.IsOpen || biz.ProtectorGangId == g.Id || taken.Contains(biz.Id)) continue;
                double value = (biz.Takings * 0.12 + (biz.Racket != RacketKind.None ? 80 : 0)) * 10;
                if (!biz.IsProtected)
                {
                    double score = value * Simulation.ExtortChance(w, g, hood, biz);
                    if (score > bestScore) { bestScore = score; best = biz; bestBackup = new(); }
                    continue;
                }
                if (hood.Id == g.BossHoodId) continue;
                var rival = w.GangById(biz.ProtectorGangId);
                double defence = Simulation.DefenceStrength(w, rival, biz);
                // Alone, or with up to two men behind him if the gang has hands to spare. Gangs that
                // already run much of the district are spread too thin to send men in pairs.
                bool canBackUp = spare.Count >= 4 && turf.Count < w.Businesses.Count * 0.35;
                for (int extra = 0; extra <= (canBackUp ? 2 : 0); extra++)
                {
                    var backup = spare.Take(extra).ToList();
                    double edge = (Simulation.StormingStrength(hood, backup) - defence) / 10.0;
                    double win = Math.Clamp(0.5 + edge, 0.05, 0.95);
                    double score = value * win * (0.4 + g.Aggression) - (1 - win) * 0.25 * manCost - extra * 150;
                    if (g.Heat > 60) score *= 0.3;
                    if (rival.IsPlayer && !g.IsPlayer) score *= w.Settings.RivalWariness(w.Week / Content.WeeksPerYear);
                    if (score > bestScore) { bestScore = score; best = biz; bestBackup = backup; }
                }
            }
            if (best == null || bestScore < 80) continue;
            taken.Add(best.Id);
            assigned.Add(hood.Id);
            foreach (var b in bestBackup) assigned.Add(b.Id);
            orders.Add(new ExtortOrder(g.Id, hood.Id, best.Id, bestBackup.Count > 0 ? bestBackup.Select(b => b.Id).ToArray() : null));
        }

        return orders;
    }

    /// <summary>Kill a story before it runs, and pay the beat cop when the precinct is breathing down the gang's neck.</summary>
    /// <summary>
    /// Arm the men, best first, out of money the gang can spare: revolvers all round, then a car
    /// to get away in, then heavier guns for the hard men, and armour for a rich boss.
    /// </summary>
    private static bool TopDog(World w, Gang g) => w.Businesses.Count(b => b.ProtectorGangId == g.Id) * 3 > w.Businesses.Count;

    /// <summary>
    /// Once the safe holds more dirty money than is comfortable, buy the best place on the books
    /// to wash it through. The biggest outfit in town is the one the Treasury wants, and it knows it
    /// can't hide that much, so it doesn't try.
    /// </summary>
    private static long PlanFronts(World w, Gang g, List<Business> turf, long cash, List<Order> orders)
    {
        if (TopDog(w, g) || g.Dirty < Content.TaxCaseDirty) return 0;
        if (w.FrontsOf(g.Id).Count() >= 1 + turf.Count / 8) return 0;
        var buy = turf.Where(b => w.CannotBuyFront(g, b) == null && Content.FrontPrice(b) * 2 <= cash - 6000)
            .OrderByDescending(b => b.Takings).ThenBy(b => b.Id).FirstOrDefault();
        if (buy == null) return 0;
        orders.Add(new FrontOrder(g.Id, buy.Id));
        return Content.FrontPrice(buy);
    }

    /// <summary>
    /// A lawyer while the boss or his heir is waiting on a trial, and the judge paid in the boss's case
    /// if it isn't a federal one. Rival outfits don't lean on juries.
    /// </summary>
    private static long PlanCourts(World w, Gang g, List<Business> turf, long cash, List<Order> orders)
    {
        long spent = 0;
        // Rivals only go to the trouble when the man in the dock is the boss or the one who'd take over.
        // Your own autopilot plays the same way, so it's no better than they are.
        bool trial = w.OnTrial(g.Id).Any(h => h.Id == g.BossHoodId || h.Id == g.HeirHoodId);
        var want = trial && cash > 1500 ? (cash > 20_000 ? Lawyer.Mouthpiece : Lawyer.Storefront) : Lawyer.None;
        if (want != g.Lawyer && !(trial && want < g.Lawyer)) orders.Add(new LawyerOrder(g.Id, want));

        foreach (var man in w.OnTrial(g.Id))
        {
            if (man.Id != g.BossHoodId) continue;
            int price = w.JudgePrice(man);
            if (w.CannotPayJudge(g, man) == null && cash - spent > price * 3)
            {
                orders.Add(new JudgeOrder(g.Id, man.Id));
                spent += price;
            }
        }
        return spent;
    }

    private static long PlanGear(World w, Gang g, long cash, List<Order> orders)
    {
        long budget = Math.Max(0, (cash - 10000) / 12);
        long spent = 0;
        var men = w.AvailableHoodsOf(g.Id).OrderByDescending(h => h.Strength).ThenBy(h => h.Id).ToList();
        bool Buy(int cost) { if (spent + cost > budget) return false; spent += cost; return true; }
        int Cost(Weapon from, Weapon to) => Content.Guns[to].Price - Content.TradeIn(Content.Guns[from].Price);
        int CarCost(Car from, Car to) => Content.Cars[to].Price - Content.TradeIn(Content.Cars[from].Price);

        // An outfit that already runs a third of the city spends on holding it: no more iron, and a few cars.
        bool topDog = TopDog(w, g);
        // Half the men armed is plenty; the rest are collectors and lookouts.
        int armed = men.Count(h => h.Weapon != Weapon.None);
        if (!topDog && armed * 2 < men.Count && men.FirstOrDefault(h => h.Weapon == Weapon.None) is { } bare && Buy(Cost(bare.Weapon, Weapon.Pistol)))
            orders.Add(new ArmOrder(g.Id, bare.Id, Weapon.Pistol));
        int cars = w.HoodsOf(g.Id).Count(h => h.Car != Car.None);
        var driver = men.FirstOrDefault(h => h.Car == Car.None && h.Id != g.BossHoodId);
        if (driver != null && cars < (topDog ? 3 : 1 + men.Count / 6))
        {
            var car = cash > 9000 ? Car.Packard : Car.ModelT;
            if (Buy(CarCost(Car.None, car))) orders.Add(new CarOrder(g.Id, driver.Id, car));
        }
        if (!topDog && cash > 8000 && men.FirstOrDefault(h => h.Weapon == Weapon.Pistol) is { } hard && Buy(Cost(hard.Weapon, Weapon.Shotgun)))
            orders.Add(new ArmOrder(g.Id, hard.Id, Weapon.Shotgun));
        if (!topDog && cash > 15000 && g.Heat < 60 && men.FirstOrDefault(h => h.Weapon == Weapon.Shotgun) is { } heavy && Buy(Cost(heavy.Weapon, Weapon.TommyGun)))
            orders.Add(new ArmOrder(g.Id, heavy.Id, Weapon.TommyGun));
        var boss = w.HoodById(g.BossHoodId);
        if (cash > 25000 && boss.Car != Car.Cadillac && Buy(CarCost(boss.Car, Car.Cadillac)))
            orders.Add(new CarOrder(g.Id, boss.Id, Car.Cadillac));
        return spent;
    }

    private static long PlanStreet(World w, Gang g, long cash, List<Order> orders)
    {
        long spent = 0;
        var reporter = w.People.FirstOrDefault(p => p.Kind == PersonKind.Reporter && p.GangId == g.Id);
        if (reporter != null && cash > Math.Max(2500, Content.StoryPrice(g.Heat) * 5))
        {
            orders.Add(new BuyStoryOrder(g.Id, reporter.Id));
            spent += Content.StoryPrice(g.Heat);
        }
        if (g.Heat > 40 && cash - spent > 1500)
        {
            // The cop in the ward with the most rackets to lose.
            var ward = w.TurfOf(g.Id).Where(b => b.Racket != RacketKind.None)
                .GroupBy(b => w.LotOf(b).WardId).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).FirstOrDefault()?.Key;
            var cop = ward == null ? null : w.People.FirstOrDefault(p => p.Kind == PersonKind.BeatCop && p.WardId == ward && p.PaidByGangId < 0 && p.Nerve < 8);
            if (cop != null)
            {
                orders.Add(new PayCopOrder(g.Id, cop.Id));
                spent += Content.CopPrice;
            }
        }
        return spent;
    }

    /// <summary>
    /// The heavier work, taken on now and then: silence a witness, rob a bank when money is short,
    /// hijack a rival's beer, burn out his best earner, or go after his best man. Men sent on these
    /// come out of the pool for shakedowns.
    /// </summary>
    private static void PlanCrimes(World w, Gang g, List<Business> turf, List<Hood> hoods, long cash, List<Order> orders)
    {
        Hood? Take(Func<Hood, int> by)
        {
            var h = hoods.OrderByDescending(by).ThenBy(x => x.Id).FirstOrDefault();
            if (h != null) hoods.Remove(h);
            return h;
        }

        foreach (var witness in w.People.Where(p => p.Kind == PersonKind.Witness && p.GangId == g.Id).ToList())
        {
            if (hoods.Count == 0) return;
            var man = Take(h => h.Intimidation);
            orders.Add(new SilenceOrder(g.Id, man!.Id, witness.Id));
        }

        // A gang only tries one big job a week, and only every so often.
        int beat = (w.Week + g.Id * 7) % 4;

        if (beat == 0 && cash < 1500 && hoods.Count >= 4 && g.Heat < 30 && g.Aggression > 0.35 && w.Banks.Count > 0)
        {
            var lead = hoods.OrderByDescending(h => h.Brains * 2 + h.Stealth).ThenBy(h => h.Id).First();
            var backup = hoods.Where(h => h.Id != lead.Id).OrderByDescending(h => h.Muscle).ThenBy(h => h.Id).Take(2).ToList();
            var bank = w.Banks.OrderByDescending(b => b.Vault * Simulation.RobberyChance(w, lead, backup, b)).ThenBy(b => b.Id).First();
            if (Simulation.RobberyChance(w, lead, backup, bank) >= 0.45 && bank.Vault >= 2000)
            {
                hoods.Remove(lead);
                foreach (var b in backup) hoods.Remove(b);
                orders.Add(new RobberyOrder(g.Id, lead.Id, bank.Id, backup.Select(b => b.Id).ToArray()));
                return;
            }
        }

        bool Near(Business b) => w.BlocksFromHq(g, b) <= 4;
        bool Wary(Business b) => w.GangById(b.ProtectorGangId).IsPlayer && w.Settings.RivalWariness(w.Week / Content.WeeksPerYear) < 1;

        if (beat == 1 && w.Prohibition && g.Heat < 40 && hoods.Count >= 3)
        {
            var lead = hoods[0];
            var backup = hoods.Skip(1).Take(1).ToList();
            var truck = w.Businesses.Where(b => Simulation.CanHijack(w, g, b) && Near(b) && !Wary(b))
                .OrderByDescending(b => Simulation.HijackChance(w, lead, backup, b)).ThenBy(b => b.Id).FirstOrDefault();
            if (truck != null && Simulation.HijackChance(w, lead, backup, truck) >= 0.55)
            {
                hoods.Remove(lead);
                foreach (var b in backup) hoods.Remove(b);
                orders.Add(new HijackOrder(g.Id, lead.Id, truck.Id, backup.Select(b => b.Id).ToArray()));
                return;
            }
        }

        if (beat == 2 && g.Aggression > 0.5 && g.Heat < 35 && hoods.Count >= 3)
        {
            var torch = hoods.OrderByDescending(h => h.Stealth).ThenBy(h => h.Id).First();
            var target = w.Businesses.Where(b => b.IsOpen && b.IsProtected && b.ProtectorGangId != g.Id && b.Racket != RacketKind.None && Near(b) && !Wary(b))
                .OrderByDescending(b => b.Takings).ThenBy(b => b.Id).FirstOrDefault();
            if (target != null && Simulation.ArsonChance(torch, guarded: false) >= 0.6)
            {
                hoods.Remove(torch);
                orders.Add(new ArsonOrder(g.Id, torch.Id, target.Id));
                return;
            }
        }

        if (beat == 3 && (w.Week / 4 + g.Id) % 3 == 0 && g.Aggression > 0.6 && g.Heat < 30 && hoods.Count >= 4)
        {
            // The rival's best man who works close to home.
            var lead = hoods[0];
            var backup = hoods.Skip(1).Take(1).ToList();
            var target = w.Hoods.Where(h => h.IsAvailable && h.GangId != g.Id && w.GangById(h.GangId).Alive && w.GangById(h.GangId).BossHoodId != h.Id
                    && !(w.GangById(h.GangId).IsPlayer && w.Settings.RivalWariness(w.Week / Content.WeeksPerYear) < 1))
                .Where(h => w.Map.Distance(w.HqOf(g), w.Map.LotAt(Simulation.HauntOf(w, h))) <= CityMap.StrideX * 4)
                .OrderByDescending(h => w.Businesses.Count(b => b.HandlerHoodId == h.Id)).ThenByDescending(h => h.Strength).ThenBy(h => h.Id)
                .FirstOrDefault();
            if (target != null && Simulation.HitChance(w, lead, backup, target) >= 0.55)
            {
                hoods.Remove(lead);
                foreach (var b in backup) hoods.Remove(b);
                orders.Add(new HitOrder(g.Id, lead.Id, target.Id, backup.Select(b => b.Id).ToArray()));
            }
        }
    }

    /// <summary>
    /// Buy the alderman where the gang does most of its business, and put money into elections
    /// once it can afford to. Returns what it plans to spend.
    /// </summary>
    private static long PlanPolitics(World w, Gang g, List<Business> turf, long cash, List<Order> orders)
    {
        if (w.Wards.Count == 0 || !w.Settings.PoliticsEnabled || turf.Count < 4) return 0;
        var home = turf.GroupBy(b => w.LotOf(b).WardId)
            .Select(x => (Ward: w.Wards[x.Key], Count: x.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Ward.Id).ToList();

        var due = Politics.Campaigning(w);
        // Each race gets one budget, spent over a couple of weeks.
        if (due is { Kind: ElectionKind.Mayor } && cash > 5000 && w.Hall.Campaign.GetValueOrDefault(g.Id) < 3000)
        {
            int amount = (int)Math.Min(1500, cash / 10);
            orders.Add(new CampaignOrder(g.Id, -1, amount));
            return amount;
        }
        if (due is { Kind: ElectionKind.Alderman } && cash > 4000)
        {
            // Defend a bought alderman first, then try for the home ward.
            var target = home.FirstOrDefault(x => x.Ward.OwnerGangId == g.Id && x.Count >= 3).Ward ?? home[0].Ward;
            if (target.Campaign.GetValueOrDefault(g.Id) < 1500)
            {
                int amount = (int)Math.Min(750, cash / 12);
                orders.Add(new CampaignOrder(g.Id, target.Id, amount));
                return amount;
            }
        }

        foreach (var (ward, count) in home)
        {
            if (count < 3 || ward.OwnerGangId == g.Id || ward.Reformer || Politics.StaysBought(w, ward, g)) continue;
            long cost = Politics.PayoffCost(w, ward, g);
            if (cost > cash / 4 || cash - cost < 2500) continue;
            // Rivals' aldermen are only worth stealing where the gang clearly runs the ward.
            if (ward.OwnerGangId >= 0 && count < Politics.BusinessesIn(w, ward).Count(b => b.ProtectorGangId == ward.OwnerGangId) + 2) continue;
            orders.Add(new PayoffOrder(g.Id, ward.Id));
            return cost;
        }
        return 0;
    }
}
