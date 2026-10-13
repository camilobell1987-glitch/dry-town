using System.Text;

namespace DryTown.Core;

/// <summary>Plain-text reports shared by the console runner and the Godot screen.</summary>
public static partial class Reports
{
    public static string Date(World w) => $"Week {w.WeekOfYear}, {w.Year}";

    /// <summary>The day and hour of the week, as in "Tuesday, 3 PM".</summary>
    public static string Hour(int tick)
    {
        string[] days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        int day = Math.Clamp(tick / 24, 0, 6);
        int hour = Math.Max(0, tick) % 24;
        string h = hour == 0 ? "midnight" : hour == 12 ? "noon" : hour < 12 ? $"{hour} AM" : $"{hour - 12} PM";
        return $"{days[day]}, {h}";
    }

    public static string WeekSummary(World w, int week)
    {
        var sb = new StringBuilder();
        var player = w.Player;
        int year = Content.StartYear + week / Content.WeeksPerYear;
        sb.AppendLine($"=== Week {week % Content.WeeksPerYear + 1}, {year} ===");
        if (w.Settings.Scenario != ScenarioKind.None) sb.AppendLine(Scenarios.Summary(w));
        if (player.Alive && w.LastLedger.TryGetValue(player.Id, out var l))
        {
            sb.AppendLine($"Protection ${l.Protection}  Rackets ${l.Rackets}  Wages -${l.Wages}  Spending -${l.Spending}  Fines -${l.Fines}" +
                (l.Legit + l.LaunderFee > 0 ? $"  Fronts ${l.Legit}  Bookkeeper -${l.LaunderFee}" : "") + $"  Net ${l.Net}");
            if (l.Laundered > 0) sb.AppendLine($"Washed ${l.Laundered} through the fronts.");
            if (l.TributePaid + l.TributeIn > 0) sb.AppendLine($"Tribute: paid ${l.TributePaid}, received ${l.TributeIn}.");
            if (l.Dues > 0) sb.AppendLine($"Union dues: ${l.Dues}.");
            sb.AppendLine($"Cash ${player.Cash} (${player.Clean} clean)  Heat {player.Heat}  Turf {w.TurfOf(player.Id).Count()}/{w.Businesses.Count}  Men {w.HoodsOf(player.Id).Count()}");
        }
        else if (!player.Alive)
        {
            sb.AppendLine("Your outfit is finished. The city goes on without you.");
        }
        sb.AppendLine();
        sb.AppendLine("Headlines:");
        var news = w.Events.Where(e => e.Week == week && (IsHeadline(e) || e.Kind == EventKind.Whisper && e.GangId == player.Id)).ToList();
        if (news.Count == 0) sb.AppendLine("  A quiet week in the district.");
        foreach (var e in news) sb.AppendLine($"  {(e.GangId == player.Id ? "*" : "-")} {e.Text}");
        return sb.ToString();
    }

    public static bool IsHeadline(GameEvent e) => e.Kind is not (EventKind.Recruited or EventKind.Bribe or EventKind.HoodReleased or EventKind.Street or EventKind.Gear or EventKind.Front or EventKind.Whisper);

    /// <summary>Who is about on the street, and what each of them can be for the gang.</summary>
    public static string Street(World w, int gangId)
    {
        var sb = new StringBuilder();
        var gang = w.GangById(gangId);
        foreach (var p in w.People.OrderBy(p => p.Kind).ThenBy(p => p.Id))
        {
            string ward = w.Wards.ElementAtOrDefault(p.WardId)?.Name ?? "";
            string line = p.Kind switch
            {
                PersonKind.Drifter => $"{Content.Label(p.Origin)} drifter, Int {p.Intimidation} Mus {p.Muscle} Brn {p.Brains} Stl {p.Stealth}, asks ${p.Fee}, moves on in {p.WeeksLeft}w",
                PersonKind.BeatCop => $"beat cop in {ward}" + (p.PaidByGangId >= 0 ? $", paid by {w.GangById(p.PaidByGangId).Name} this week" : ""),
                PersonKind.Reporter => $"writing about {w.GangById(p.GangId).Name}, goes to print in {p.WeeksLeft}w" + (p.GangId == gangId ? $", would drop it for ${Content.StoryPrice(gang.Heat)}" : ""),
                _ => $"saw {p.About}, testifies in {p.WeeksLeft}w, nerve {p.Nerve}",
            };
            sb.AppendLine($"{p.Id,4} {Trim(p.Name, 40),-40} {line}");
        }
        return sb.Length == 0 ? "Nobody about.\n" : sb.ToString();
    }

    /// <summary>What a man carries and drives, in a few words.</summary>
    public static string Gear(Hood h) =>
        h.Weapon == Weapon.None && h.Car == Car.None ? "-"
        : string.Join(", ", new[] { h.Weapon != Weapon.None ? Content.Guns[h.Weapon].Label : null, h.Car != Car.None ? Content.Cars[h.Car].Label : null }.Where(x => x != null));

    /// <summary>Where to buy guns and cars, and what they cost.</summary>
    public static string Shops(World w, int gangId)
    {
        var sb = new StringBuilder();
        foreach (var lot in w.Shops(LotUse.GunShop).Concat(w.Shops(LotUse.CarDealer)))
            sb.AppendLine($"  {World.ShopName(w.Map, lot)}");
        sb.AppendLine("Guns (arm <hood> <gun>):");
        foreach (var (kind, g) in Content.Guns.Where(x => x.Key != Weapon.None))
            sb.AppendLine($"  {kind,-9} {g.Label,-18} ${g.Price,-5} +{g.Firepower} strength, +{g.Extort * 100:0}% to lean on a shop" + (g.Heat > 0 ? $", +{g.Heat} heat in a gunfight" : ""));
        sb.AppendLine("Cars (drive <hood> <car>):");
        foreach (var (kind, c) in Content.Cars.Where(x => x.Key != Car.None))
            sb.AppendLine($"  {kind,-9} {c.Label,-18} ${c.Price,-5} {c.Speed:0.#}x as fast, {c.Getaway * 100:0}% better getaway" + (kind == Car.Cadillac ? ", bulletproof" : ""));
        sb.AppendLine($"Trading in what a man has already gets back half its price.");
        return sb.ToString();
    }

    /// <summary>The gang's lawyer and every man of it held for trial, with the odds against him.</summary>
    public static string Cases(World w, int gangId)
    {
        var g = w.GangById(gangId);
        var sb = new StringBuilder();
        var lawyer = Content.Lawyers[g.Lawyer];
        sb.AppendLine(g.Lawyer == Lawyer.None ? "You have no lawyer. Your men go to court with whoever the court gives them."
            : $"You keep {lawyer.Label} for ${lawyer.Weekly} a week: every case is {lawyer.Cut * 100:0} points likelier to end in an acquittal.");
        var cases = w.OnTrial(gangId).ToList();
        if (cases.Count == 0) sb.AppendLine("Nobody is waiting on a trial.");
        foreach (var h in cases)
        {
            var c = h.Case!;
            string help = (c.JudgePaid ? ", judge paid" : "") + (c.JuryFixed ? ", jury leaned on" : "");
            sb.AppendLine($"  {h.Id,4} {h.Name,-28} charged {c.Charge}; trial in {h.JailWeeks}w, faces {(c.Life ? "life" : $"{c.Sentence}w")}{(c.Federal ? " (federal)" : "")}; " +
                $"{Simulation.ConvictionChance(w, h) * 100:0}% to convict{help}" + (c.Federal || c.JudgePaid ? "" : $"; the judge wants ${w.JudgePrice(h):N0}") +
                (h.Bail > 0 ? $"; out on ${h.Bail:N0} bail" : w.CannotBail(g, h) is { } no && !no.StartsWith("The judge set") ? "" : $"; bail ${w.BailFor(h):N0}"));
        }
        foreach (var h in w.OutOfTown(gangId))
            sb.AppendLine($"  {h.Id,4} {h.Name,-28} out of town {w.Week - h.AwaySinceWeek}w, ${Content.HideoutFee(g, h)} a week" +
                (h.Case is { } c ? $"; wanted, charged {c.Charge}; {Simulation.ConvictionChance(w, h) * 100:0}% to convict if he's caught" : "; lying low"));
        foreach (var h in w.WantedAtHome(gangId))
            sb.AppendLine($"  {h.Id,4} {h.Name,-28} home with a warrant out, charged {h.Case!.Charge}; {Content.WantedPickup(g) * 100:0}% a week the police pick him up");
        return sb.ToString();
    }

    /// <summary>Men in cells talking to the DA, rats found out, and the gang's men planted in other outfits.</summary>
    public static string Rats(World w, int gangId)
    {
        var sb = new StringBuilder();
        var talking = w.TalkingOf(gangId).ToList();
        foreach (var h in talking)
            sb.AppendLine($"  {h.Id,4} {h.Name,-28} talking to the DA; signs in {h.Talking}w unless his loyalty ({h.Loyalty}) reaches {Content.RatLine(h)}" +
                (h.LookedAfter ? "" : $"; his family needs ${Content.FamilyCare(h):N0}"));
        var exposed = w.ExposedIn(gangId).ToList();
        foreach (var h in exposed)
            sb.AppendLine($"  {h.Id,4} {h.Name,-28} " + (h.State == HoodState.Inside ? $"one of {w.GangById(h.GangId).Name}'s men, planted on you" : "talking to the police")
                + $"; runs in {Math.Max(0, Content.RunWeeks - (w.Week - h.ExposedWeek))}w");
        if (talking.Count + exposed.Count == 0) sb.AppendLine("Nobody you know of is talking.");
        var plants = w.PlantsOf(gangId).ToList();
        if (plants.Count == 0) sb.AppendLine("You have nobody inside another outfit.");
        foreach (var h in plants)
        {
            var host = w.GangById(h.InsideGangId);
            sb.AppendLine($"  {h.Id,4} {h.Name,-28} inside {host.Name} since {Date(w, h.InsideSinceWeek)}" + (h.Exposed ? ", found out" : "")
                + $": ${host.Cash:N0} in the safe, heat {host.Heat}, {w.HoodsOf(host.Id).Count()} men");
        }
        return sb.ToString();
    }

    /// <summary>A man's rap sheet: what he has done for the outfit, and every time the police had him.</summary>
    public static string Sheet(World w, Hood h)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{h.Name} of {w.GangById(h.GangId).Name}, {h.Age(w.Week)}. {Known(h)}");
        sb.AppendLine(Priors(h));
        foreach (var r in h.Record) sb.AppendLine($"  {Date(w, r.Week),-16} {r.Text}");
        return sb.ToString();
    }

    /// <summary>His work for the outfit in one line.</summary>
    public static string Known(Hood h)
    {
        var d = h.Deeds;
        var parts = new List<string>();
        void Add(int n, string one, string many) { if (n > 0) parts.Add(n == 1 ? one : $"{n} {many}"); }
        Add(d.Shakedowns, "one shop shaken down", "shops shaken down");
        Add(d.Takeovers, "one business taken from a rival", "businesses taken from rivals");
        Add(d.Kills, "one man killed", "men killed");
        Add(d.Fires, "one fire", "fires");
        Add(d.Heists, "one heist", "heists");
        return parts.Count == 0 ? "No work to his name yet." : "Known for " + string.Join(", ", parts) + ".";
    }

    /// <summary>How the police see him: arrests and convictions, and what the next one means.</summary>
    public static string Priors(Hood h)
    {
        if (h.Record.Count == 0) return "No police record.";
        int arrests = h.Record.Count(r => r.Kind is RapKind.Petty or RapKind.Charged);
        int convictions = h.Convictions;
        string next = convictions >= Content.HabitualOffender ? " One more conviction and he goes away for life."
            : convictions > 0 ? $" His next sentence will be {Content.PriorSentence * convictions * 100:0}% longer." : "";
        return $"Arrested {arrests} time{(arrests == 1 ? "" : "s")}, convicted {convictions} time{(convictions == 1 ? "" : "s")} by a jury.{next}";
    }

    public static string Date(World w, int week) => $"Week {week % Content.WeeksPerYear + 1}, {Content.StartYear + week / Content.WeeksPerYear}";

    /// <summary>The gang's fronts, what they wash, and how much of the safe is still dirty.</summary>
    public static string Fronts(World w, int gangId)
    {
        var g = w.GangById(gangId);
        var sb = new StringBuilder();
        sb.AppendLine($"Cash ${g.Cash:N0}: ${g.Clean:N0} clean, ${g.Dirty:N0} dirty." +
            (g.Dirty >= Content.TaxCaseDirty ? " The Treasury could bring a tax case." : ""));
        var fronts = w.FrontsOf(gangId).ToList();
        if (fronts.Count == 0) sb.AppendLine("No fronts yet. Buy a business you protect with 'buy <biz>'.");
        foreach (var b in fronts)
            sb.AppendLine($"  {b.Id,3} {b.Name,-40} washes ${Content.LaunderCapacity(b)}/week, profit ${b.Takings * Content.FrontProfitPercent / 100}" +
                (b.Racket == RacketKind.Nightclub ? ", your nightclub" : "") + (World.Working(b) ? "" : b.IsOpen ? " (not yours to watch now: idle)" : " (shut: idle)"));
        sb.AppendLine($"For sale to you (price = 25 weeks' takings; the bookkeeper keeps {Content.LaunderFeePercent}% of what he washes):");
        foreach (var b in w.TurfOf(gangId).Where(b => !b.IsFront && b.IsOpen).OrderByDescending(b => b.Takings).Take(8))
            sb.AppendLine($"  {b.Id,3} {b.Name,-40} ${Content.FrontPrice(b):N0}");
        return sb.ToString();
    }

    /// <summary>The supply lines into the city, who holds them, and how the gang pours its liquor.</summary>
    public static string Booze(World w, int gangId)
    {
        var g = w.GangById(gangId);
        var sb = new StringBuilder();
        if (!w.Prohibition) return "Since repeal, liquor comes in by the front door like anything else.\n";
        foreach (var l in w.SupplyLines)
        {
            string who = l.OwnerGangId == gangId ? "yours" : l.OwnerGangId >= 0 ? $"held by {w.GangById(l.OwnerGangId).Name}" : $"for sale, ${l.Price:N0}";
            sb.AppendLine($"{l.Id,3} {Trim(l.Name, 20),-20} {l.Kind.ToString().ToLowerInvariant(),-5} {l.Capacity} speakeasies, {who}" +
                (l.WaylaidWeek == w.Week ? ", stopped this week" : "") + $"\n      {l.Cargo}");
        }
        var mine = w.TurfOf(gangId).Where(b => World.Liquor(b.Racket) && b.IsOpen).ToList();
        var real = w.Supplied(gangId, thisWeek: false);
        int speakeasies = mine.Count(b => b.Racket == RacketKind.Speakeasy);
        sb.AppendLine($"Your liquor is {Content.Label(g.Cut)}: {Content.CutIncome(g.Cut):+0%;-0%;+0%} on every speakeasy and still.");
        int beer = w.OnTap(gangId, real).Count;
        sb.AppendLine(speakeasies == 0 ? "You run no speakeasies." : $"{real.Count} of your {speakeasies} speakeasies pour real liquor ({Content.LineBonus:+0%} takings)" +
            (beer > 0 ? $", {beer} pour beer from your brewery ({Content.BeerBonus:+0%})" : "") + "; the rest sell bathtub gin.");
        if (w.BreweryOf(gangId) is { } brewery) sb.AppendLine($"Your brewery is at {brewery.Name} ({brewery.Id}). It keeps up to {Content.BreweryRoom} speakeasies in beer.");
        if (mine.Count > 0)
            sb.AppendLine($"Chance of a bad batch somewhere this week: {1 - mine.Aggregate(1.0, (p, b) => p * (1 - w.BadBatchChance(b, g, real))):P1}.");
        return sb.ToString();
    }

    /// <summary>The city's union locals: whose trades, who runs them, what the dues bring in, and any strike.</summary>
    public static string Unions(World w, int gangId)
    {
        var sb = new StringBuilder();
        foreach (var l in w.Locals)
        {
            string who = l.OwnerGangId == gangId ? "yours" : l.OwnerGangId >= 0 ? $"run by {w.GangById(l.OwnerGangId).Name}" : "run by its members";
            string trades = string.Join(", ", l.Trades.Select(Content.Plural));
            sb.AppendLine($"{l.Id,3} {Trim(World.Capital(l.Name), 40),-40} {who}, dues ${w.DuesOf(l)} a week" +
                (l.OnStrike ? $", out at {w.BusinessById(l.StrikeBusinessId).Name}" : "") + $"\n      {w.MembersOf(l).Count()} shops: {trades}");
        }
        int docks = w.DocksGangId;
        if (w.Prohibition && docks >= 0)
            sb.AppendLine(docks == gangId
                ? $"You run the docks: other outfits pay you ${Content.Wharfage} for every boat that lands, and your men hear when a rival's is due."
                : $"{w.GangById(docks).Name} runs the docks: every boat that lands pays them ${Content.Wharfage}.");
        return sb.ToString();
    }

    public static string Banks(World w, int gangId)
    {
        var sb = new StringBuilder();
        foreach (var b in w.Banks)
            sb.AppendLine($"{b.Id,3} {Trim(b.Name, 40),-40} vault ${b.Vault,-6} guards {b.Guards}" + (w.Week - b.RobbedWeek < 12 ? ", on its guard after a hold-up" : ""));
        return sb.Length == 0 ? "No banks in this city.\n" : sb.ToString();
    }

    /// <summary>Where the player stands with every other outfit, and what they've put to him.</summary>
    public static string Relations(World w, int gangId)
    {
        var me = w.GangById(gangId);
        var sb = new StringBuilder();
        sb.AppendLine($"{"Id",3} {"Gang",-28} {"Power",5} {"Hate you",8} {"You hate",8}  Standing");
        foreach (var g in w.LivingGangs.Where(g => g.Id != gangId).OrderByDescending(w.Power))
            sb.AppendLine($"{g.Id,3} {Trim(g.Name, 28),-28} {w.Power(g),5:0} {w.GrudgeOf(g.Id, gangId),8} {w.GrudgeOf(gangId, g.Id),8}  {Standing(w, me, g)}");
        sb.AppendLine($"Your power: {w.Power(me):0}." + (w.Untrusted(me) ? " You broke your word this year: nobody will sign with you." : ""));
        foreach (var m in w.Marriages.Where(m => !m.Involves(gangId)))
            sb.AppendLine($"  {w.GangById(m.GroomGangId).Name} and {w.GangById(m.BrideGangId).Name} are family: {w.HoodById(m.GroomHoodId).Name} married {m.Bride}.");
        if (w.Groom(me) is { } groom) sb.AppendLine($"{groom.Name} is free to marry: 'marry <gang>' puts a match to an outfit you've kept the peace with for {Content.MatchPactWeeks} weeks (${Content.WeddingCost:N0} for the wedding).");
        foreach (var war in w.Wars.Where(x => !x.Involves(gangId)))
            sb.AppendLine($"  {w.GangById(war.A).Name} has been at war with {w.GangById(war.B).Name} for {w.Week - war.StartWeek} weeks.");
        if (w.WarOf(gangId) is { } mine)
            sb.AppendLine($"Your men are on the mattresses: ${Simulation.MattressBill(w, me)} a week for the safehouses, hits on {w.GangById(mine.Other(gangId)).Name} {Content.WarHit:+0%} likelier and drawing half the heat." + (mine.A == gangId ? " Type 'calloff' to end it." : " They started it: only a truce ends it from your side."));
        foreach (var f in w.Funerals.Where(f => w.Week < f.UntilWeek))
            sb.AppendLine(f.GangId == gangId
                ? $"You're burying {f.Boss}. Wreaths so far from {f.Flowers.Count} outfit{(f.Flowers.Count == 1 ? "" : "s")}."
                : $"{w.GangById(f.GangId).Name} are burying {f.Boss}. Moving on them before the funeral breaks its peace." + (f.Flowers.Contains(gangId) ? " Your wreath is there." : $" 'flowers {f.GangId}' sends a wreath (${Content.FlowersCost})."));
        foreach (var o in w.Offers)
            sb.AppendLine($"  Offer {o.Id}: {w.Describe(o)} Answer by week {o.ExpiresWeek % Content.WeeksPerYear + 1} with 'accept {o.Id}' or 'decline {o.Id}'.");
        return sb.ToString();
    }

    /// <summary>A gang's pact with another, in a few words.</summary>
    public static string Standing(World w, Gang me, Gang other)
    {
        if (w.Wars.FirstOrDefault(x => x.Involves(me.Id) && x.Involves(other.Id)) is { } war)
            return $"at war for {w.Week - war.StartWeek} weeks, since {(war.A == me.Id ? "you" : "they")} went to the mattresses";
        if (w.MarriageBetween(me.Id, other.Id) is { } m)
            return $"family by marriage: {w.HoodById(m.GroomHoodId).Name} married {m.Bride}";
        var p = w.PactBetween(me.Id, other.Id);
        if (p == null) return w.GrudgeOf(other.Id, me.Id) > Content.GrudgeTooDeep ? "bad blood" : "no pact";
        int left = p.UntilWeek - w.Week;
        string tribute = p.PayerId == me.Id ? $", you pay ${p.Tribute}/week" : p.PayerId >= 0 ? $", they pay ${p.Tribute}/week" : "";
        return $"{World.Name(p.Kind)}{tribute}, {left} weeks left";
    }

    public static string Gangs(World w)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Gang",-28} {"Boss",-28} {"People",-7} {"Turf",4} {"Men",4} {"Cash",8} {"Heat",4}");
        foreach (var g in w.LivingGangs.OrderByDescending(g => w.TurfOf(g.Id).Count()))
        {
            var boss = w.HoodById(g.BossHoodId);
            string name = g.IsPlayer ? $"{g.Name} (you)" : g.Name;
            sb.AppendLine($"{Trim(name, 28),-28} {Trim(boss.Name, 28),-28} {Content.Label(boss.Origin),-7} {w.TurfOf(g.Id).Count(),4} {w.HoodsOf(g.Id).Count(),4} {g.Cash,8} {g.Heat,4}");
        }
        return sb.ToString();
    }

    public static string Hoods(World w, int gangId)
    {
        var sb = new StringBuilder();
        var gang = w.GangById(gangId);
        sb.AppendLine($"{"Id",4} {"Name",-30} {"People",-7} {"Age",3} {"Int",3} {"Mus",3} {"Brn",3} {"Stl",3} {"Loy",3} {"Wage",4}  {"State",-10} Carries / drives");
        foreach (var h in w.HoodsOf(gangId).OrderBy(h => h.Id))
        {
            string state = h.State == HoodState.Jailed ? (h.Case != null ? $"trial in {h.JailWeeks}w" : $"jailed {h.JailWeeks}w")
                : h.State == HoodState.Lam ? (World.IsWanted(h) ? "on the lam" : "lying low")
                : h.State == HoodState.Held ? "kidnapped"
                : h.Bail > 0 ? $"bail, {h.JailWeeks}w" : World.IsWanted(h) ? "wanted"
                : h.Id == gang.BossHoodId ? "boss" : h.Id == gang.HeirHoodId ? "heir" : "free";
            sb.AppendLine($"{h.Id,4} {Trim(h.Name, 30),-30} {Content.Label(h.Origin),-7} {h.Age(w.Week),3} {h.Intimidation,3} {h.Muscle,3} {h.Brains,3} {h.Stealth,3} {h.Loyalty,3} {h.Wage,4}  {state,-10} {Gear(h)}");
        }
        return sb.ToString();
    }

    public static string Businesses(World w, Func<Business, bool>? filter = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Id",3} {"Business",-36} {"Take",5} {"Tgh",3} {"Rate",4} {"Res",3}  Protected by / racket");
        foreach (var b in w.Businesses.Where(filter ?? (_ => true)))
        {
            string owner = b.IsProtected ? w.GangById(b.ProtectorGangId).Name : "nobody";
            string racket = b.Racket != RacketKind.None ? $", {Content.Rackets[b.Racket].Label}" : "";
            string shut = b.IsOpen ? "" : $" (shut {b.ShutWeeks}w)";
            if (b.IsFront) shut += $", front for {w.GangById(b.OwnerGangId).Name}";
            string rate = b.IsProtected ? $"{b.ProtectionRate}%" : "";
            sb.AppendLine($"{b.Id,3} {Trim(b.Name, 36),-36} {b.Takings,5} {b.Toughness,3} {rate,4} {b.Resentment,3}  {owner}{racket}{shut}");
        }
        return sb.ToString();
    }

    public static string YearLine(Simulation sim, int yearIndex)
    {
        var samples = sim.Metrics.Samples.Where(s => s.Week / Content.WeeksPerYear == yearIndex).ToList();
        if (samples.Count == 0) return "";
        var last = samples[^1];
        return $"{Content.StartYear + yearIndex}: gangs {samples.Min(s => s.LivingGangs)}-{samples.Max(s => s.LivingGangs)}, " +
               $"top share {samples.Max(s => s.TopShare):P0}, takeovers {samples.Sum(s => s.Takeovers)}, " +
               $"your cash ${last.PlayerCash}{(last.PlayerAlive ? "" : " (out)")}";
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + ".";

    public static string Crews(World w, int gangId)
    {
        var crews = w.CrewsOf(gangId).ToList();
        if (crews.Count == 0) return "No crews. 'crew new <hood>' makes a hood a lieutenant.\n";
        var sb = new System.Text.StringBuilder();
        foreach (var c in crews)
        {
            var lt = w.HoodById(c.LieutenantHoodId);
            var men = c.MemberIds.Select(w.HoodById).Select(h => $"{h.Id} {h.Name}");
            sb.AppendLine($"  crew {c.Id}: {lt.Id} {lt.Name} (lieutenant, loyalty {lt.Loyalty})" + (c.MemberIds.Count > 0 ? $" with {string.Join(", ", men)}" : ""));
        }
        return sb.ToString();
    }

    /// <summary>A rival's crews, as far as anyone on the street can tell. A man inside knows how the lieutenants feel about their boss.</summary>
    public static string RivalCrews(World w, Gang gang, bool inside)
    {
        var crews = w.CrewsOf(gang.Id).ToList();
        if (crews.Count == 0) return $"{gang.Name} keeps no standing crews.\n";
        var sb = new StringBuilder();
        foreach (var c in crews)
        {
            var lt = w.HoodById(c.LieutenantHoodId);
            var men = c.MemberIds.Select(w.HoodById).Select(h => $"{h.Id} {h.Name}");
            sb.AppendLine($"  {lt.Id} {lt.Name} (lieutenant, strength {lt.Strength}{(inside ? $", loyalty {lt.Loyalty}" : "")})"
                + (c.MemberIds.Count > 0 ? $" with {string.Join(", ", men)}" : "") + $"; crew strength {CrewStrength(w, c):0}");
        }
        return sb.ToString();
    }

    /// <summary>What a crew brings to a fight when it goes in together: its hardest man in front and the rest behind him.</summary>
    public static double CrewStrength(World w, Crew c)
    {
        var men = c.Everyone.Select(w.HoodById).Where(h => h.IsAvailable).OrderByDescending(h => h.Strength).ToList();
        return men.Count == 0 ? 0 : Simulation.StormingStrength(men[0], men.Skip(1));
    }

    public static string Wards(World w, int gangId)
    {
        var sb = new StringBuilder();
        var hall = w.Hall;
        string friend = hall.FriendGangId >= 0 ? $", a friend of {w.GangById(hall.FriendGangId).Name}" : "";
        sb.AppendLine($"Mayor {hall.Mayor} ({(hall.Reform ? "reform" : "machine")}{friend}). Outrage {hall.Outrage}.");
        string daFriend = hall.DAFriendGangId >= 0 ? $", a friend of {w.GangById(hall.DAFriendGangId).Name}" : "";
        sb.AppendLine($"District Attorney {hall.DA} ({(hall.DAReform ? "reform" : "machine")}{daFriend}).");
        var due = Politics.Campaigning(w);
        if (due is { } d) sb.AppendLine($"Election in {d.Week - w.Week + 1} weeks: {(d.Kind == ElectionKind.Mayor ? "mayor" : "aldermen")}.");
        else sb.AppendLine($"Next elections: aldermen week {Politics.NextElection(w, ElectionKind.Alderman) % Content.WeeksPerYear + 1} of {Content.StartYear + Politics.NextElection(w, ElectionKind.Alderman) / Content.WeeksPerYear}, " +
                           $"mayor {Content.StartYear + Politics.NextElection(w, ElectionKind.Mayor) / Content.WeeksPerYear}.");
        if (w.ClubOf(gangId) is { } club) sb.AppendLine($"Your nightclub is at {club.Name} ({club.Id}). Aldermen come {Content.ClubPayoff:0%} cheaper to you, and your campaign money counts for {Content.ClubCampaign:0%} more.");
        sb.AppendLine($"{"Id",3} {"Ward",-16} {"People",-7} {"Alderman",-30} {"Owned by",-24} {"Yours",5} {"Price",6}");
        foreach (var ward in w.Wards)
        {
            var biz = Politics.BusinessesIn(w, ward).ToList();
            string owner = ward.Reformer ? "nobody (reformer)" : ward.OwnerGangId >= 0 ? w.GangById(ward.OwnerGangId).Name + (ward.OwnMan ? " (own man)" : "") : "the party";
            string price = ward.Reformer || ward.OwnMan || ward.OwnerGangId == gangId ? "" : Politics.PayoffCost(w, ward, w.GangById(gangId)).ToString();
            sb.AppendLine($"{ward.Id,3} {Trim(ward.Name, 16),-16} {Content.Label(w.Neighbourhood(ward)),-7} {Trim(ward.Alderman, 30),-30} {Trim(owner, 24),-24} {biz.Count(b => b.ProtectorGangId == gangId),2}/{biz.Count,-2} {price,6}");
        }
        return sb.ToString();
    }

    /// <summary>The last count in every race: each candidate, his ticket and his votes.</summary>
    public static string Results(World w)
    {
        if (w.Hall.Results.Count == 0) return "No votes have been counted yet.\n";
        var sb = new StringBuilder();
        foreach (var race in w.Hall.Results)
        {
            sb.AppendLine($"{race.Race}, {Date(w, race.Week)}:");
            foreach (var t in race.Field.OrderByDescending(t => t.Votes))
                sb.AppendLine($"  {(t.Won ? "*" : " ")} {Trim(t.Name, 30),-30} {Trim(t.Ticket, 28),-28} {t.Votes,6:N0} {race.Share(t),3}%");
        }
        return sb.ToString();
    }

    /// <summary>The city events in progress: the headline, what it does, and how long it has left.</summary>
    public static string News(World w)
    {
        if (w.CityEvents.Count == 0) return "Nothing out of the ordinary is going on in the city.\n";
        var sb = new StringBuilder();
        foreach (var e in w.CityEvents.OrderBy(e => e.StartWeek))
        {
            var info = Content.CityEvents[e.Kind];
            int left = e.UntilWeek - w.Week;
            sb.AppendLine($"  {w.Describe(e, info.Headline)} {w.Describe(e, info.Effect)} ({left} week{(left == 1 ? "" : "s")} left)");
        }
        return sb.ToString();
    }
}
