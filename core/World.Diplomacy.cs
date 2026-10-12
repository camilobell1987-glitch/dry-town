namespace DryTown.Core;

/// <summary>Grudges, truces, alliances and tribute: how the gangs feel about each other, and what they have agreed.</summary>
public sealed partial class World
{
    public List<Pact> Pacts { get; } = new();
    public List<Grudge> Grudges { get; } = new();

    /// <summary>Proposals the rivals have put to the player, waiting on an answer.</summary>
    public List<Offer> Offers { get; } = new();

    public Pact? PactBetween(int a, int b) => a == b ? null : Pacts.FirstOrDefault(p => p.Involves(a) && p.Involves(b));

    /// <summary>The two gangs have agreed to leave each other alone.</summary>
    public bool AtPeace(int a, int b) => PactBetween(a, b) != null;

    public IEnumerable<int> AlliesOf(int gangId) => Pacts.Where(p => p.Kind == PactKind.Alliance && p.Involves(gangId)).Select(p => p.Other(gangId));

    public int GrudgeOf(int from, int to) => Grudges.FirstOrDefault(x => x.From == from && x.To == to)?.Amount ?? 0;

    public void AddGrudge(int from, int to, int amount)
    {
        if (from == to) return;
        var g = Grudges.FirstOrDefault(x => x.From == from && x.To == to);
        if (g == null) Grudges.Add(g = new Grudge { From = from, To = to });
        g.Amount = Math.Clamp(g.Amount + amount, 0, 100);
        if (g.Amount == 0) Grudges.Remove(g);
    }

    /// <summary>What a gang can put on the street: its free men's strength, and the turf behind them.</summary>
    public double Power(Gang g) => AvailableHoodsOf(g.Id).Sum(h => h.Strength) + TurfOf(g.Id).Count() * 4;

    /// <summary>What a gang's turf brings in a week, before wages.</summary>
    public long Income(Gang g) => TurfOf(g.Id).Where(b => b.IsOpen)
        .Sum(b => (long)b.Takings * b.ProtectionRate / 100 + (b.Racket != RacketKind.None ? Content.Rackets[b.Racket].WeeklyIncome : 0));

    /// <summary>The outfit that already runs a third of the city.</summary>
    /// <summary>The other side will only agree to a truce if it's paid for.</summary>
    public bool WantsPaying(Gang from, Gang to) => GrudgeOf(to.Id, from.Id) > Content.GrudgeTooDeep || Power(to) > Power(from) * Content.TributeRatio;

    public bool TopDog(Gang g) => Businesses.Count(b => b.ProtectorGangId == g.Id) * 3 > Businesses.Count;

    /// <summary>The gang broke its word within the last year, and nobody will take it again until then.</summary>
    public bool Untrusted(Gang g) => Week - g.BrokeWordWeek < Content.WeeksPerYear;

    public static string Name(PactKind kind) => kind == PactKind.Truce ? "truce" : "alliance";

    /// <summary>
    /// One gang moved on another. The victim and its allies hold it against the attacker, and if the
    /// two had a pact, the attacker has just broken it.
    /// </summary>
    public void Hostile(Gang attacker, Gang victim, int grudge)
    {
        if (attacker.Id == victim.Id) return;
        if (PactBetween(attacker.Id, victim.Id) is { } pact) Betray(attacker, victim, pact);
        AddGrudge(victim.Id, attacker.Id, grudge);
        foreach (var ally in AlliesOf(victim.Id).ToList())
            if (ally != attacker.Id) AddGrudge(ally, attacker.Id, grudge / 2);
    }

    /// <summary>Tear up a pact. The partner won't forget it, and nobody else will trust the breaker for a year.</summary>
    public void Betray(Gang breaker, Gang partner, Pact pact)
    {
        Pacts.Remove(pact);
        var marriage = MarriageBetween(breaker.Id, partner.Id);
        if (marriage != null) Marriages.Remove(marriage);
        int times = marriage != null ? Content.MarriedBreak : 1;
        breaker.BrokeWordWeek = Week;
        Renown(breaker, respect: -Content.RespectBrokenWord * times);
        AddGrudge(partner.Id, breaker.Id, Content.GrudgeBetrayed * times);
        foreach (var other in LivingGangs)
            if (other.Id != breaker.Id && other.Id != partner.Id) AddGrudge(other.Id, breaker.Id, Content.GrudgeWitnessed * times);
        Log(EventKind.Diplomacy, breaker.Id, marriage != null
            ? $"{breaker.Name} turned on {partner.Name}, their own family by marriage, and {marriage.Bride} went home to her father. Nobody will take their word for a year."
            : $"{breaker.Name} broke the {Name(pact.Kind)} with {partner.Name}. Nobody will take their word for a year.");
    }

    /// <summary>
    /// Why <paramref name="to"/> won't agree to a pact <paramref name="from"/> proposes, or null if it will.
    /// Tribute is what the proposer offers to pay each week.
    /// </summary>
    public string? Refusal(Gang from, Gang to, PactKind kind, int tribute = 0)
    {
        if (from.Id == to.Id || !to.Alive) return "There's nobody to talk to.";
        var pact = PactBetween(from.Id, to.Id);
        if (pact != null && (pact.Kind == kind || pact.Kind == PactKind.Alliance)) return $"You already have {(pact.Kind == PactKind.Truce ? "a truce" : "an alliance")} with {to.Name}.";
        if (Untrusted(from)) return $"{to.Name} won't take the word of an outfit that broke its last one.";
        int grudge = GrudgeOf(to.Id, from.Id);
        double mine = Power(from), theirs = Power(to);
        if (kind == PactKind.Truce)
        {
            int ask = Content.TributeAsk(Income(from));
            if (tribute >= ask) return null;
            if (grudge > Content.GrudgeTooDeep) return $"There's too much bad blood. {to.Name} would want ${ask} a week to call it off.";
            if (theirs > mine * Content.TributeRatio) return $"{to.Name} doesn't need a truce with {from.Name}. They'd take ${ask} a week to leave them alone.";
            return null;
        }
        if (TopDog(to)) return $"{to.Name} runs half the city already. They don't need allies.";
        if (grudge > 20) return $"{to.Name} doesn't trust {from.Name} that far.";
        bool threat = LivingGangs.Any(g => g.Id != from.Id && g.Id != to.Id && !AtPeace(g.Id, to.Id) && Power(g) > Math.Max(mine, theirs) * 1.2);
        return threat ? null : $"{to.Name} sees nobody worth ganging up on.";
    }

    /// <summary>Why <paramref name="to"/> won't pay the tribute <paramref name="from"/> demands, or null if it will.</summary>
    public string? DemandRefusal(Gang from, Gang to, int amount)
    {
        if (from.Id == to.Id || !to.Alive) return "There's nobody to lean on.";
        if (AtPeace(from.Id, to.Id)) return $"You already have an understanding with {to.Name}.";
        if (Power(from) < Power(to) * Content.TributeRatio) return $"{to.Name} laughed. They aren't scared of {from.Name}.";
        if (amount > Math.Max(Content.TributeAsk(Income(to)) * 3 / 2, 150)) return $"{to.Name} can't find ${amount} a week. Ask for less.";
        if (GrudgeOf(to.Id, from.Id) > 70) return $"{to.Name} would sooner fight.";
        return null;
    }

    /// <summary>Seal a pact, replacing any the two already had.</summary>
    public Pact Sign(Gang a, Gang b, PactKind kind, int payerId = -1, int tribute = 0, string? text = null)
    {
        Pacts.RemoveAll(p => p.Involves(a.Id) && p.Involves(b.Id));
        Offers.RemoveAll(o => (a.IsPlayer && o.FromGangId == b.Id) || (b.IsPlayer && o.FromGangId == a.Id));
        var pact = new Pact
        {
            Kind = kind, A = a.Id, B = b.Id, SignedWeek = Week, PayerId = payerId, Tribute = tribute,
            UntilWeek = Week + (kind == PactKind.Truce ? Content.TruceWeeks : Content.AllianceWeeks),
        };
        Pacts.Add(pact);
        if (MarriageBetween(a.Id, b.Id) != null) Bind(pact);
        // Signing clears the air.
        AddGrudge(a.Id, b.Id, -GrudgeOf(a.Id, b.Id));
        AddGrudge(b.Id, a.Id, -GrudgeOf(b.Id, a.Id));
        text ??= kind == PactKind.Alliance
            ? $"{a.Name} and {b.Name} are allies now: an attack on one is an attack on both."
            : payerId >= 0
                ? $"{GangById(payerId).Name} will pay {GangById(pact.Other(payerId)).Name} ${tribute} a week to be left alone."
                : $"{a.Name} and {b.Name} agreed a truce.";
        if (text.Length > 0) Log(EventKind.Diplomacy, a.Id, text);
        if (Wars.FirstOrDefault(x => x.Involves(a.Id) && x.Involves(b.Id)) is { } war)
            EndWar(war, $"The war between {a.Name} and {b.Name} is over, and the men are home from the mattresses.");
        return pact;
    }

    // ---- The player's side ---------------------------------------------------------

    /// <summary>Put a truce or an alliance to a rival. They answer at once. Returns what they said.</summary>
    public string Propose(Gang from, Gang to, PactKind kind, int tribute = 0)
    {
        var no = Refusal(from, to, kind, tribute);
        if (no != null) return no;
        Sign(from, to, kind, tribute > 0 ? from.Id : -1, tribute);
        return kind == PactKind.Alliance ? $"{to.Name} shook on it. You're allies for a year."
            : tribute > 0 ? $"{to.Name} took the deal: ${tribute} a week and they leave you alone for {Content.TruceWeeks} weeks."
            : $"{to.Name} agreed. Neither of you moves on the other for {Content.TruceWeeks} weeks.";
    }

    /// <summary>Lean on a weaker gang for tribute. A refusal is taken as an insult on both sides.</summary>
    public string Demand(Gang from, Gang to, int amount)
    {
        var no = DemandRefusal(from, to, amount);
        if (no != null)
        {
            if (!AtPeace(from.Id, to.Id)) AddGrudge(to.Id, from.Id, Content.GrudgeRefused / 2);
            return no;
        }
        Sign(from, to, PactKind.Truce, to.Id, amount);
        return $"{to.Name} will pay you ${amount} every Sunday. Leave them alone, or the money stops.";
    }

    /// <summary>Why the gang can't break off with this one, or null.</summary>
    public string? CannotBreak(Gang breaker, Gang partner) => PactBetween(breaker.Id, partner.Id) == null ? $"You have no pact with {partner.Name}." : null;

    public string Break(Gang breaker, Gang partner)
    {
        if (CannotBreak(breaker, partner) is { } no) return no;
        Betray(breaker, partner, PactBetween(breaker.Id, partner.Id)!);
        return $"It's off with {partner.Name}. They won't forget it, and nobody will take your word for a year.";
    }

    /// <summary>Accept or turn down a rival's proposal.</summary>
    public string Answer(Offer offer, bool accept, bool lapsed = false)
    {
        Offers.Remove(offer);
        var from = GangById(offer.FromGangId);
        if (!from.Alive) return $"{from.Name} is finished.";
        if (offer.Kind == OfferKind.Ransom)
        {
            var held = Hostages.FirstOrDefault(h => h.HoodId == offer.HoodId);
            if (held == null) return "He isn't being held any more.";
            if (!accept)
            {
                if (!lapsed) Offers.Add(offer);
                return lapsed ? "" : $"You haven't paid. {from.Name} has until week {held.DueWeek % Content.WeeksPerYear + 1} to decide what to do with {HoodById(held.HoodId).Name}.";
            }
            if (CannotPayRansom(Player, held) is { } broke) { Offers.Add(offer); return broke; }
            return PayRansom(Player, held);
        }
        if (!accept)
        {
            if (offer.Kind == OfferKind.Marriage)
            {
                AddGrudge(from.Id, Player.Id, Content.GrudgeRefused / 2);
                return $"You turned down {from.Name}'s match. They took it as an insult.";
            }
            if (offer.Kind == OfferKind.Demand)
            {
                AddGrudge(from.Id, Player.Id, Content.GrudgeRefused);
                Log(EventKind.Diplomacy, Player.Id, lapsed ? $"{Player.Name} never answered {from.Name}'s demand, and {from.Name} took it badly."
                    : $"{Player.Name} wouldn't pay {from.Name}, and {from.Name} took it badly.");
                return $"You told {from.Name} where to go. Expect them to come for you.";
            }
            return $"You turned {from.Name} down.";
        }
        switch (offer.Kind)
        {
            case OfferKind.Marriage:
                if (MatchRefusal(from, Player) is { } off) return $"The match is off: {off}";
                var m = Wed(from, Player);
                return $"{HoodById(m.GroomHoodId).Name} married {m.Bride}. You're family with {from.Name} now: allies for as long as the marriage lasts.";
            case OfferKind.Demand:
                Sign(from, Player, PactKind.Truce, Player.Id, offer.Tribute);
                return $"You'll pay {from.Name} ${offer.Tribute} a week, and they'll leave you alone.";
            case OfferKind.Alliance:
                Sign(from, Player, PactKind.Alliance);
                return $"You and {from.Name} are allies for a year.";
            default:
                Sign(from, Player, PactKind.Truce, offer.Tribute > 0 ? from.Id : -1, offer.Tribute);
                return offer.Tribute > 0 ? $"{from.Name} will pay you ${offer.Tribute} a week for the truce." : $"You have a truce with {from.Name}.";
        }
    }

    /// <summary>A rival puts something to the player, unless something like it is already waiting.</summary>
    public void Put(Gang from, OfferKind kind, int tribute = 0)
    {
        if (Offers.Any(o => o.FromGangId == from.Id)) return;
        int id = Offers.Count == 0 ? 1 : Offers.Max(o => o.Id) + 1;
        Offers.Add(new Offer { Id = id, Kind = kind, FromGangId = from.Id, Tribute = tribute, ExpiresWeek = Week + Content.OfferWeeks });
        Log(EventKind.Diplomacy, Player.Id, Describe(Offers[^1]));
    }

    public string Describe(Offer o)
    {
        var from = GangById(o.FromGangId);
        return o.Kind switch
        {
            OfferKind.Demand => $"{from.Name} wants ${o.Tribute} a week from you to leave you alone.",
            OfferKind.Alliance => $"{from.Name} proposes an alliance against the bigger outfits.",
            OfferKind.Ransom => $"{from.Name} has {HoodById(o.HoodId).Name} and wants ${o.Tribute:N0} to give him back. If it isn't paid by week {o.ExpiresWeek % Content.WeeksPerYear + 1}, he's a dead man.",
            OfferKind.Marriage => $"{HoodById(from.BossHoodId).Name} of {from.Name} proposes that {Groom(from)?.Name ?? "his son"} marry your boss's daughter. Family is an alliance that doesn't run out.",
            _ => o.Tribute > 0 ? $"{from.Name} offers you ${o.Tribute} a week for a truce." : $"{from.Name} proposes a truce.",
        };
    }

    /// <summary>A gang is finished: nothing it agreed or held against anyone matters any more.</summary>
    public void ForgetDiplomacy(Gang gang)
    {
        Pacts.RemoveAll(p => p.Involves(gang.Id));
        Marriages.RemoveAll(m => m.Involves(gang.Id));
        Grudges.RemoveAll(g => g.From == gang.Id || g.To == gang.Id);
        Offers.RemoveAll(o => o.FromGangId == gang.Id);
        foreach (var h in Hostages.Where(h => h.HeldByGangId == gang.Id).ToList()) Free(h);
    }
}
