namespace DryTown.Core;

/// <summary>Kidnapping: snatching a rival's man off the street and holding him until his outfit pays to get him back.</summary>
public static partial class Content
{
    /// <summary>What an outfit asks for a soldier, and for the boss's heir or one of his family.</summary>
    public const int Ransom = 1500, FamilyRansom = 4000;

    /// <summary>Weeks his outfit has to pay before the kidnappers decide what to do with him, and the most men an outfit holds at once.</summary>
    public const int RansomWeeks = 4, MaxHostages = 2;

    /// <summary>Taking a man alive is harder than shooting him: this much off the chance of a hit.</summary>
    public const double KidnapHarder = 0.1;

    /// <summary>Weekly chance a man gets away from the men holding him, and what each point of his stealth adds.</summary>
    public const double HostageEscape = 0.03, EscapePerStealth = 0.01;

    /// <summary>The heat a snatch brings, the grudge it leaves, and the fear a man killed for an unpaid ransom earns.</summary>
    public const int KidnapHeat = 8, GrudgeKidnap = 20, GrudgeUnpaid = 30;
    public const double FearKidnap = 3;

    /// <summary>
    /// A rival snatches a man at this chance a week, when it is angry enough with an outfit that has money. It pays a ransom at this
    /// chance a week while it can spare the money, and an outfit this aggressive kills a man whose ransom never came.
    /// </summary>
    public const double AiKidnap = 0.04, AiPays = 0.6, AiKills = 0.5;
    public const int AiKidnapGrudge = 25, AiKidnapCash = 4000;
}

/// <summary>A man held for ransom: who has him, what they want, and the week it's due.</summary>
public sealed class Hostage
{
    public int HoodId { get; set; }
    public int HeldByGangId { get; set; }
    public int Ransom { get; set; }
    public int Week { get; set; }
    public int DueWeek { get; set; }
}

public sealed partial class World
{
    public List<Hostage> Hostages { get; } = new();

    public Hostage? HostageOf(Hood h) => Hostages.FirstOrDefault(x => x.HoodId == h.Id);
    public IEnumerable<Hostage> HeldBy(int gangId) => Hostages.Where(x => x.HeldByGangId == gangId).OrderBy(x => x.Week).ThenBy(x => x.HoodId);
    public IEnumerable<Hostage> TakenFrom(int gangId) => Hostages.Where(x => HoodById(x.HoodId).GangId == gangId).OrderBy(x => x.Week).ThenBy(x => x.HoodId);

    /// <summary>What his outfit would be asked for him.</summary>
    public int RansomFor(Hood h) => h.Family || GangById(h.GangId).HeirHoodId == h.Id ? Content.FamilyRansom : Content.Ransom;

    /// <summary>Why the gang can't snatch this man, or null if it can.</summary>
    public string? CannotKidnap(Gang gang, Hood target)
    {
        if (target.GangId == gang.Id) return "He's one of yours.";
        if (!target.IsAvailable) return "He's out of reach.";
        if (GangById(target.GangId).BossHoodId == target.Id) return "A boss never goes anywhere alone. Snatch one of his men.";
        return HeldBy(gang.Id).Count() >= Content.MaxHostages ? $"You're holding {Content.MaxHostages} men already." : null;
    }

    /// <summary>The man is taken. His outfit is told what it will cost, and the player is asked straight out.</summary>
    public Hostage Snatch(Gang gang, Hood target)
    {
        var victim = GangById(target.GangId);
        target.State = HoodState.Held;
        var h = new Hostage { HoodId = target.Id, HeldByGangId = gang.Id, Ransom = RansomFor(target), Week = Week, DueWeek = Week + Content.RansomWeeks };
        Hostages.Add(h);
        if (victim.IsPlayer)
        {
            int id = Offers.Count == 0 ? 1 : Offers.Max(o => o.Id) + 1;
            Offers.Add(new Offer { Id = id, Kind = OfferKind.Ransom, FromGangId = gang.Id, Tribute = h.Ransom, HoodId = target.Id, ExpiresWeek = h.DueWeek });
        }
        return h;
    }

    /// <summary>Why the gang can't pay this ransom now, or null.</summary>
    public string? CannotPayRansom(Gang payer, Hostage h) =>
        payer.Cash < h.Ransom ? $"You don't have ${h.Ransom:N0}." : null;

    /// <summary>The ransom is paid and the man comes home.</summary>
    public string PayRansom(Gang payer, Hostage h)
    {
        var holder = GangById(h.HeldByGangId);
        var man = HoodById(h.HoodId);
        payer.Cash -= h.Ransom;
        Ledger(payer.Id).Fines += h.Ransom;
        holder.Cash += h.Ransom;
        Ledger(holder.Id).Rackets += h.Ransom;
        Free(h);
        string text = $"{payer.Name} paid {holder.Name} ${h.Ransom:N0}, and {man.Name} came home.";
        Log(EventKind.Kidnap, payer.Id, text);
        return text;
    }

    /// <summary>The holder lets him go without the money.</summary>
    public string LetGo(Gang holder, Hostage h, bool unpaid = false)
    {
        var man = HoodById(h.HoodId);
        Free(h);
        string text = unpaid ? $"{GangById(man.GangId).Name} never paid for {man.Name}, but {holder.Name} let him go anyway."
            : $"{holder.Name} let {man.Name} of {GangById(man.GangId).Name} go.";
        Log(EventKind.Kidnap, holder.Id, text);
        return text;
    }

    internal void Free(Hostage h)
    {
        Hostages.Remove(h);
        Offers.RemoveAll(o => o.Kind == OfferKind.Ransom && o.HoodId == h.HoodId);
        var man = HoodById(h.HoodId);
        if (man.State == HoodState.Held) man.State = HoodState.Free;
    }
}

public sealed partial class Simulation
{
    /// <summary>Chance of taking him alive: a hit's chance, a little lower.</summary>
    public static double KidnapChance(World w, Hood hood, IEnumerable<Hood> backup, Hood target) =>
        Math.Max(0.05, HitChance(w, hood, backup, target) - Content.KidnapHarder);

    private void Kidnap(Gang gang, KidnapOrder o)
    {
        var w = World;
        var target = w.Hoods.FirstOrDefault(h => h.Id == o.TargetHoodId);
        if (target == null || w.CannotKidnap(gang, target) != null) return;
        if (KeepsTheirWord(gang, target.GangId)) return;
        if (!TryUseHood(gang, o.HoodId, out var hood)) return;
        var backup = TakeBackup(gang, o.Backup);
        var backupIds = backup.Select(h => h.Id).ToList();
        var rival = w.GangById(target.GangId);
        w.Hostile(gang, rival, Content.GrudgeKidnap);
        int where = HauntOf(w, target);

        if (w.Rng.Chance(KidnapChance(w, hood, backup, target)))
        {
            var h = w.Snatch(gang, target);
            gang.Heat = Math.Min(100, gang.Heat + Content.KidnapHeat + GunHeat(backup.Prepend(hood)));
            w.Renown(gang, fear: Content.FearKidnap);
            string text = $"{hood.Name} of {gang.Name} snatched {target.Name} of {rival.Name} off the street. {gang.Name} wants ${h.Ransom:N0} for him within {Content.RansomWeeks} weeks.";
            w.Log(EventKind.Kidnap, gang.Id, text);
            if (rival.IsPlayer) w.Log(EventKind.Kidnap, rival.Id, $"{gang.Name} has {target.Name}. Pay ${h.Ransom:N0} on the Gangs tab within {Content.RansomWeeks} weeks, or he's a dead man.");
            w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hit, gang.Id, hood.Id, gang.HqLotId, where, -1, ActionResult.Won, text, rival.Id, target.Id)));
            MaybeWitness(gang, hood, w.Map.LotAt(where), $"{hood.Name} bundle {target.Name} into a car", 0.3);
            return;
        }

        gang.Heat = Math.Min(100, gang.Heat + 4 + GunHeat(backup.Prepend(hood)));
        int casualty = -1;
        string fail = $"{target.Name} of {rival.Name} fought his way clear of {hood.Name}.";
        if (w.Rng.Chance(0.25 * (1 - Getaway(backup.Prepend(hood)))))
        {
            var fallen = backup.Count > 0 && w.Rng.Chance(0.5) ? w.Rng.Pick(backup) : hood;
            Kill(fallen, $"trying to snatch {target.Name}");
            casualty = fallen.Id;
            fail = $"{target.Name} of {rival.Name} pulled a gun when they came to snatch him: {fallen.Name} of {gang.Name} is dead.";
        }
        w.Log(EventKind.Kidnap, gang.Id, fail);
        w.Act(BackedBy(backupIds, new ScriptAction(ActionKind.Hit, gang.Id, hood.Id, gang.HqLotId, where, -1, ActionResult.Lost, fail, rival.Id, target.Id, casualty)));
    }

    /// <summary>
    /// Sunday for the men held for ransom: some get away, rivals pay for their own, and when the time's up
    /// the kidnappers let a man go or kill him. An outfit that's finished loses its claim on him either way.
    /// </summary>
    private void Hostages()
    {
        var w = World;
        foreach (var h in w.Hostages.OrderBy(x => x.Week).ThenBy(x => x.HoodId).ToList())
        {
            var man = w.HoodById(h.HoodId);
            var holder = w.GangById(h.HeldByGangId);
            var victim = w.GangById(man.GangId);
            if (man.State != HoodState.Held) { w.Free(h); continue; }
            if (!victim.Alive)
            {
                w.Hostages.Remove(h);
                man.State = HoodState.Gone;
                w.Log(EventKind.Kidnap, holder.Id, $"With {victim.Name} finished, nobody was going to pay for {man.Name}. {holder.Name} let him go, and he left town.");
                continue;
            }
            if (!holder.Alive)
            {
                w.Free(h);
                w.Log(EventKind.Kidnap, victim.Id, $"With {holder.Name} finished, {man.Name} walked out and came home to {victim.Name}.");
                continue;
            }
            if (w.Rng.Chance(Content.HostageEscape + man.Stealth * Content.EscapePerStealth))
            {
                w.Free(h);
                w.Log(EventKind.Kidnap, victim.Id, $"{man.Name} got away from the men {holder.Name} had watching him, and came home to {victim.Name}.");
                continue;
            }
            if (!victim.IsPlayer && w.CannotPayRansom(victim, h) == null && victim.Cash >= h.Ransom + Content.AiKidnapCash / 2 && w.Rng.Chance(Content.AiPays))
            {
                w.PayRansom(victim, h);
                continue;
            }
            if (h.DueWeek > w.Week + 1) continue;
            bool kill = holder.IsPlayer || holder.Aggression >= Content.AiKills;
            if (!kill) { w.LetGo(holder, h, unpaid: true); continue; }
            w.Hostages.Remove(h);
            man.State = HoodState.Free;
            Kill(man, $"by {holder.Name}, when his ransom wasn't paid");
            w.AddGrudge(victim.Id, holder.Id, Content.GrudgeUnpaid);
            w.Renown(holder, fear: Content.FearHit);
            holder.Heat = Math.Min(100, holder.Heat + 10);
            w.Log(EventKind.Kidnap, holder.Id, $"{victim.Name} never paid for {man.Name}, and he was found in the river. Everyone knows who had him.");
        }
    }
}
