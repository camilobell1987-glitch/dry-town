namespace DryTown.Core;

/// <summary>
/// Rats and planted men. A disloyal man in a cell may take the district attorney's deal and
/// name his friends; a disloyal man on the street may tell the police what his outfit is up to;
/// and a man planted in a rival outfit tells his own side what it's planning.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>Chance a man asking around finds out a rat, for each one there is.</summary>
    public static double HuntChance(Hood man) => Math.Clamp(0.25 + man.Brains * 0.05, 0.3, 0.75);

    /// <summary>Chance a man gets a rival outfit to take him on without anyone knowing whose he is.</summary>
    public static double PlantChance(Hood man)
    {
        var d = man.Deeds;
        int fame = d.Takeovers + d.Kills * 2 + d.Fires + d.Heists;
        return Math.Clamp(0.35 + man.Stealth * 0.05 - Math.Min(0.25, fame * 0.02), 0.1, 0.85);
    }

    /// <summary>Chance a man gets to a rat: in a cell, through the guards; outside, wherever he's hiding.</summary>
    public static double SeenToChance(Hood man, Hood target) => target.State == HoodState.Jailed
        ? Math.Clamp(0.35 + man.Intimidation * 0.03 + man.Stealth * 0.03, 0.2, 0.85)
        : Math.Clamp(0.5 + man.Stealth * 0.03 + (man.Strength - target.Strength) * 0.02, 0.2, 0.9);

    /// <summary>Weekly chance a planted man gives himself away.</summary>
    public static double CaughtInsideChance(Hood man) => Math.Clamp(0.06 - man.Stealth * 0.004, 0.02, 0.05);

    private void Hunt(Gang gang, HuntOrder o)
    {
        var w = World;
        if (!TryUseHood(gang, o.HoodId, out var man)) return;
        var suspects = w.Hoods.Where(h => h.Id != man.Id && !h.Exposed && (h.GangId == gang.Id && h.Informer && h.State == HoodState.Free
            || h.State == HoodState.Inside && h.InsideGangId == gang.Id)).ToList();
        var found = suspects.Where(_ => w.Rng.Chance(HuntChance(man))).ToList();
        // He may have missed one, but the outfit takes his word for it.
        gang.TalkWeek = -1000;
        if (found.Count == 0)
        {
            w.Log(EventKind.Whisper, gang.Id, $"{man.Name} asked around {gang.Name} and found nobody talking.");
            return;
        }
        foreach (var rat in found)
        {
            rat.ExposedWeek = w.Week;
            if (rat.State == HoodState.Inside)
            {
                var owner = w.GangById(rat.GangId);
                w.Log(EventKind.Whisper, gang.Id, $"{man.Name} found out {rat.Name} is really one of {owner.Name}'s men. He'll run if he's left alone.");
                w.Log(EventKind.Whisper, owner.Id, $"{gang.Name} has found out {rat.Name} is ours. He'll have to get out.");
                w.AddGrudge(gang.Id, owner.Id, 15);
            }
            else
                w.Log(EventKind.Whisper, gang.Id, $"{man.Name} found out {rat.Name} has been talking to the police. He'll run if he's left alone.");
        }
    }

    private void SeeTo(Gang gang, SeenToOrder o)
    {
        var w = World;
        var target = w.Hoods.FirstOrDefault(h => h.Id == o.TargetHoodId);
        if (target == null || target.Id == o.HoodId || w.CannotSeeTo(gang, target) != null) return;
        if (!TryUseHood(gang, o.HoodId, out var man)) return;
        bool cell = target.State == HoodState.Jailed;
        int where = cell ? w.PrecinctNear(w.Map.LotAt(gang.HqLotId)).Id : HauntOf(w, target);
        bool ok = w.Rng.Chance(SeenToChance(man, target));
        string text;
        if (ok)
        {
            text = cell
                ? $"{target.Name} was found hanged in his cell before he could talk to the DA."
                : $"{target.Name} was pulled out of the river. {gang.Name} had found out about him.";
            Kill(target, cell ? "in his cell" : "for talking");
            target.Talking = 0;
            man.Deeds.Kills++;
            gang.Heat = Math.Min(100, gang.Heat + (cell ? 4 : 6));
            w.Log(EventKind.Rat, gang.Id, text);
            if (!cell) MaybeWitness(gang, man, w.Map.LotAt(where), $"{man.Name} with {target.Name} the night he went in the river", 0.25);
        }
        else if (cell)
        {
            text = $"The guards caught {man.Name} with a knife meant for {target.Name}.";
            w.Log(EventKind.Rat, gang.Id, text);
            Jail(man, w.Rng.Range(26, 52), "with conspiring to murder a witness");
            // Nothing makes a man sign faster than knowing his friends want him dead.
            Turn(target);
        }
        else
        {
            text = $"{target.Name} saw {man.Name} coming and got away.";
            w.Log(EventKind.Rat, gang.Id, text);
            Flee(target);
        }
        w.Act(new ScriptAction(cell ? ActionKind.Silence : ActionKind.Hit, gang.Id, man.Id, gang.HqLotId, where, -1,
            ok ? ActionResult.Won : cell ? ActionResult.Arrested : ActionResult.Lost, text,
            target.State == HoodState.Inside ? target.GangId : gang.Id, target.Id, ok ? target.Id : cell ? man.Id : -1));
    }

    private void Plant(Gang gang, PlantOrder o)
    {
        var w = World;
        var man = w.Hoods.FirstOrDefault(h => h.Id == o.HoodId);
        var host = w.Gangs.FirstOrDefault(g => g.Id == o.TargetGangId);
        if (man == null || host == null || w.CannotPlant(gang, man, host) != null) return;
        if (!TryUseHood(gang, o.HoodId, out man)) return;
        if (!w.Rng.Chance(PlantChance(man)))
        {
            w.Log(EventKind.Whisper, gang.Id, $"{host.Name} wouldn't take {man.Name} on. Somebody knew whose man he was.");
            w.AddGrudge(host.Id, gang.Id, 5);
            return;
        }
        w.LeaveCrew(man.Id);
        man.State = HoodState.Inside;
        man.InsideGangId = host.Id;
        man.InsideSinceWeek = w.Week;
        man.ExposedWeek = -1;
        w.Log(EventKind.Whisper, gang.Id, $"{man.Name} has signed on with {host.Name}. He'll tell us what they're planning.");
    }

    /// <summary>
    /// A rat signs the district attorney's deal. His own charge goes away and so does he, and the
    /// men he names are charged on his word. A lieutenant or an old hand can name the boss.
    /// </summary>
    public void Turn(Hood rat)
    {
        var w = World;
        var gang = w.GangById(rat.GangId);
        var crew = w.CrewOfHood(rat.Id);
        bool knowsBoss = crew?.LieutenantHoodId == rat.Id || rat.Id == gang.HeirHoodId || w.Week - rat.JoinedWeek >= 2 * Content.WeeksPerYear;
        rat.Talking = 0;
        rat.Case = null;
        rat.Informer = false;
        rat.ExposedWeek = -1;
        rat.State = HoodState.Gone;
        w.LeaveCrew(rat.Id);
        Book(rat, RapKind.Turned, $"Turned state's evidence against {gang.Name}.");
        gang.Heat = Math.Min(100, gang.Heat + 15);

        // He knows his own crew best, then whoever has done the most.
        int names = Content.RatNames + (crew?.LieutenantHoodId == rat.Id ? 1 : 0);
        var named = w.AvailableHoodsOf(gang.Id).Where(h => h.Id != gang.BossHoodId)
            .OrderByDescending(h => crew != null && crew.Everyone.Contains(h.Id))
            .ThenByDescending(h => h.Deeds.Shakedowns + h.Deeds.Takeovers * 2 + h.Deeds.Kills * 3 + h.Deeds.Fires * 2 + h.Deeds.Heists * 2)
            .ThenBy(h => h.Id).Take(names).ToList();
        var boss = w.HoodById(gang.BossHoodId);
        bool bossToo = knowsBoss && boss.IsAvailable && w.Rng.Chance(0.5);
        var shut = w.TurfOf(gang.Id).Where(b => b.HandlerHoodId == rat.Id && b.Racket != RacketKind.None).ToList();

        var told = named.Select(h => h.Name).ToList();
        if (bossToo) told.Add($"the boss, {boss.Name}");
        string what = told.Count == 0 ? "but there was nobody left for him to name" : $"and named {Join(told)}";
        string rackets = shut.Count == 0 ? "" : $" The police closed the {Join(shut.Select(b => $"{Content.Rackets[b.Racket].Label} at {b.Name}").ToList())}.";
        w.Log(EventKind.Rat, gang.Id, $"{rat.Name} turned state's evidence against {gang.Name}, {what}.{rackets}");

        foreach (var b in shut) { b.Racket = RacketKind.None; b.ShutWeeks = Math.Max(b.ShutWeeks, 4); }
        foreach (var h in named) Jail(h, w.Rng.Range(26, 78), $"on {rat.Name}'s testimony", strength: Content.RatTestimony);
        if (bossToo) Jail(boss, w.Rng.Range(52, 156), $"on {rat.Name}'s testimony", strength: Content.RatBossTestimony);
    }

    private static string Join(List<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    /// <summary>A rat who has been found out gets away: an informer to the DA, a planted man home.</summary>
    private void Flee(Hood rat)
    {
        var w = World;
        if (rat.State == HoodState.Inside)
        {
            var host = w.GangById(rat.InsideGangId);
            rat.State = HoodState.Free;
            rat.InsideGangId = -1;
            rat.ExposedWeek = -1;
            w.Log(EventKind.Whisper, rat.GangId, $"{rat.Name} got out of {host.Name} with his skin and came home.");
            w.Log(EventKind.Whisper, host.Id, $"{rat.Name} got away and went back to {w.GangById(rat.GangId).Name}.");
            return;
        }
        Turn(rat);
    }

    /// <summary>Sunday: men in cells weigh the DA's offer, informers talk, and planted men risk being found out.</summary>
    private void Rats()
    {
        var w = World;

        // Men already talking sign, unless something has changed their minds.
        foreach (var man in w.Hoods.Where(h => h.Talking > 0).ToList())
        {
            if (man.State != HoodState.Jailed || !w.GangById(man.GangId).Alive) { man.Talking = 0; continue; }
            if (man.Loyalty >= Content.RatLine(man))
            {
                man.Talking = 0;
                w.Log(EventKind.Whisper, man.GangId, $"{man.Name} sent the DA's men away. He's keeping his mouth shut.");
                continue;
            }
            if (--man.Talking == 0) Turn(man);
        }

        foreach (var gang in w.LivingGangs.ToList())
        {
            // The outfit that runs a third of the city is the one the DA wants, and the one its own men can sell.
            double wanted = w.TopDog(gang) ? 2 : 1;

            // The DA visits the cells. A man facing a long stretch who owes his outfit nothing listens.
            foreach (var man in w.HoodsOf(gang.Id).Where(h => h.State == HoodState.Jailed && h.Talking == 0 && h.Id != gang.BossHoodId).ToList())
            {
                int weeks = man.Case?.Sentence ?? man.JailWeeks;
                int line = Content.RatLine(man);
                if (man.Loyalty >= line || weeks < 20) continue;
                double odds = (line - man.Loyalty) / (double)line * 0.2
                    * (man.Case?.Life == true ? 2 : weeks >= 52 ? 1.4 : 1)
                    * (man.Case is { Federal: false } && w.Hall.DAReform ? 1.3 : 1) * wanted;
                if (!w.Rng.Chance(odds)) continue;
                man.Talking = Content.TalkingWeeks;
                w.Log(EventKind.Whisper, gang.Id, $"Word from the jail: {man.Name} has had visits from the DA's office. He'll sign a deal in {Content.TalkingWeeks} weeks unless something changes his mind.");
            }

            // Men on the street who hate the outfit may start talking to the police, and stop when things look up.
            foreach (var man in w.AvailableHoodsOf(gang.Id).Where(h => h.Id != gang.BossHoodId && !h.Family).ToList())
            {
                if (man.Informer && man.Loyalty >= Content.InformerQuits && !man.Exposed) { man.Informer = false; continue; }
                if (!man.Informer && man.Loyalty < Content.InformerLoyalty && w.Rng.Chance(0.02 * (0.5 + gang.Heat / 100.0) * wanted))
                    man.Informer = true;
            }

            var informers = w.AvailableHoodsOf(gang.Id).Where(h => h.Informer).ToList();
            if (informers.Count > 0)
            {
                gang.Heat = Math.Min(100, gang.Heat + 2 * informers.Count);
                // Now and then the DA has enough from him to bring a case.
                if (w.Rng.Chance(0.05 * informers.Count))
                {
                    var target = w.AvailableHoodsOf(gang.Id).Where(h => !h.Informer && h.Id != gang.BossHoodId && h.Deeds.Any)
                        .OrderByDescending(h => h.Deeds.Takeovers + h.Deeds.Kills * 2 + h.Deeds.Heists + h.Deeds.Fires).ThenBy(h => h.Id).FirstOrDefault();
                    if (target != null)
                    {
                        gang.TalkWeek = w.Week;
                        w.Log(EventKind.Whisper, gang.Id, $"The police seem to know a lot about {gang.Name}. Somebody is talking.");
                        Jail(target, w.Rng.Range(26, 52), "on an informer's word", strength: 0.7);
                    }
                }
            }
        }

        // Rats who were found out and left alone get away.
        foreach (var rat in w.Hoods.Where(h => h.Exposed && h.State is HoodState.Free or HoodState.Inside && w.Week - h.ExposedWeek >= Content.RunWeeks).ToList())
            Flee(rat);

        // Planted men: home when either outfit is finished, and sometimes found out.
        foreach (var man in w.Hoods.Where(h => h.State == HoodState.Inside && !h.Exposed).ToList())
        {
            var owner = w.GangById(man.GangId);
            var host = w.GangById(man.InsideGangId);
            if (!owner.Alive) { man.State = HoodState.Gone; man.InsideGangId = -1; continue; }
            if (!host.Alive)
            {
                man.State = HoodState.Free;
                man.InsideGangId = -1;
                w.Log(EventKind.Whisper, owner.Id, $"{host.Name} is finished, and {man.Name} has come home.");
                continue;
            }
            if (!w.Rng.Chance(CaughtInsideChance(man))) continue;
            w.AddGrudge(host.Id, owner.Id, 15);
            if (host.IsPlayer)
            {
                // The player decides what's done with him.
                man.ExposedWeek = w.Week;
                w.Log(EventKind.Whisper, host.Id, $"{man.Name} has been asking too many questions. He's one of {owner.Name}'s men, planted on us. He'll run if he's left alone.");
                w.Log(EventKind.Whisper, owner.Id, $"{host.Name} has found out {man.Name} is ours. He'll have to get out.");
                continue;
            }
            Kill(man, $"after {host.Name} found out he was working for {owner.Name}");
            w.Log(EventKind.Whisper, owner.Id, $"{man.Name} was pulled out of the river. {host.Name} had found out he was ours.");
        }
    }

    /// <summary>
    /// Monday morning, a planted man passes on what the outfit he's inside means to do to his own:
    /// which businesses it will move on, what it will burn, and whom it will go after.
    /// </summary>
    private void WordFromInside(List<(Gang gang, List<Order> orders)> plans)
    {
        var w = World;
        foreach (var (host, list) in plans)
        {
            foreach (var man in w.Hoods.Where(h => h.State == HoodState.Inside && h.InsideGangId == host.Id && !h.Exposed).ToList())
            {
                int me = man.GangId;
                foreach (var order in list)
                {
                    string? what = order switch
                    {
                        ExtortOrder e when w.BusinessById(e.BusinessId).ProtectorGangId == me => $"move on {w.BusinessById(e.BusinessId).Name}",
                        ArsonOrder a when w.BusinessById(a.BusinessId).ProtectorGangId == me => $"burn {w.BusinessById(a.BusinessId).Name}",
                        HijackOrder h when w.BusinessById(h.BusinessId).ProtectorGangId == me => $"stop the beer truck to {w.BusinessById(h.BusinessId).Name}",
                        HitOrder h when w.HoodById(h.TargetHoodId).GangId == me => $"go after {w.HoodById(h.TargetHoodId).Name}",
                        PlantOrder p when p.TargetGangId == me => $"plant {w.HoodById(p.HoodId).Name} on us",
                        _ => null,
                    };
                    if (what == null) continue;
                    var team = TeamOf(order).ToList();
                    string who = team.Count == 0 ? "" : w.CrewOfHood(team[0]) is { } crew && team.Count > 1 && team.All(crew.Everyone.Contains)
                        ? $", sending {w.HoodById(crew.LieutenantHoodId).Name}'s crew" : $", sending {w.HoodById(team[0]).Name}";
                    w.Log(EventKind.Whisper, me, $"Word from {man.Name}: {host.Name} will {what} this week{who}.");
                }
            }
        }
    }
}
