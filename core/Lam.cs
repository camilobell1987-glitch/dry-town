namespace DryTown.Core;

/// <summary>On the lam: bail before a trial, skipping town ahead of it or to lie low, and the warrant waiting at home.</summary>
public static partial class Content
{
    /// <summary>What the court wants to let a man out until his trial: more for a longer sentence, twice as much in federal court.</summary>
    public static int Bail(Case c) => (300 + c.Sentence * 15) * (c.Federal ? 2 : 1);

    /// <summary>A week out of town: a room, a car and someone to bring the papers. The boss lives better, and needs more men around him.</summary>
    public const int HideoutWeekly = 40, BossHideoutWeekly = 150;

    /// <summary>Nobody leaves town without money for this many weeks of hiding out.</summary>
    public const int HideoutReserveWeeks = 4;

    /// <summary>How much a case weakens each week the man is gone, as witnesses move away and forget, and how weak it can get.</summary>
    public const double ColdCase = 0.02, ColdestCase = 0.2;

    /// <summary>What jumping bail adds to the case when he's caught.</summary>
    public const double JumpedBailCase = 0.15;

    /// <summary>Weekly chance a wanted man is found out of town. Federal agents look harder.</summary>
    public static double LamCatch(Case c) => c.Federal ? 0.03 : 0.015;

    /// <summary>Weekly chance a wanted man back in town is picked up, more as his outfit runs hotter.</summary>
    public static double WantedPickup(Gang g) => 0.08 + g.Heat * 0.002;

    /// <summary>Each week the boss is out of town, his men's loyalty slips this much, though not below the floor: they miss him, but they don't forget him.</summary>
    public const int BossAwayLoyalty = 1, BossAwayLoyaltyFloor = 50;

    /// <summary>
    /// How rivals use it: bail for the boss or heir when the jury is at least this likely to convict, a run when it's
    /// likelier still and the sentence is at least a year, and home once the case has cooled to this.
    /// </summary>
    public const double AiBailOdds = 0.4, AiRunOdds = 0.6, AiHomeCase = 0.3;
    public const int AiRunSentence = 52;

    public static int HideoutFee(Gang g, Hood h) => h.Id == g.BossHoodId ? BossHideoutWeekly : HideoutWeekly;
}

public sealed partial class World
{
    /// <summary>A charge is waiting for him and nobody is holding him for it: out of town, or home and dodging the police.</summary>
    public static bool IsWanted(Hood h) => h.Case != null && h.Bail == 0 && h.State is HoodState.Free or HoodState.Lam;

    /// <summary>The gang's men out on bail, soonest trial first.</summary>
    public IEnumerable<Hood> OnBail(int gangId) =>
        Hoods.Where(h => h.GangId == gangId && h.Bail > 0 && h.State == HoodState.Free).OrderBy(h => h.JailWeeks).ThenBy(h => h.Id);

    /// <summary>The gang's men out of town, longest gone first.</summary>
    public IEnumerable<Hood> OutOfTown(int gangId) =>
        Hoods.Where(h => h.GangId == gangId && h.State == HoodState.Lam).OrderBy(h => h.AwaySinceWeek).ThenBy(h => h.Id);

    /// <summary>The gang's men home with a warrant out for them.</summary>
    public IEnumerable<Hood> WantedAtHome(int gangId) =>
        Hoods.Where(h => h.GangId == gangId && h.State == HoodState.Free && IsWanted(h)).OrderBy(h => h.Id);

    public int BailFor(Hood h) => h.Case == null ? 0 : Content.Bail(h.Case);

    /// <summary>Why the gang can't bail this man out, or null if it can.</summary>
    public string? CannotBail(Gang gang, Hood hood)
    {
        if (hood.GangId != gang.Id || hood.State != HoodState.Jailed || hood.Case == null) return "He isn't held for trial.";
        if (hood.Case.Life) return "The judge won't set bail for a man facing life.";
        if (hood.Case.JumpedBail) return "He jumped bail once already. The judge won't set it again.";
        int bail = BailFor(hood);
        return gang.Cash < bail ? $"The judge set bail at ${bail:N0}." : null;
    }

    /// <summary>Put up a man's bail. He's free until his trial, and the money comes back if he shows up for it. Returns the bail, or -1.</summary>
    public int PostBail(Gang gang, Hood hood)
    {
        if (CannotBail(gang, hood) != null) return -1;
        int bail = BailFor(hood);
        gang.Cash -= bail;
        hood.Bail = bail;
        hood.State = HoodState.Free;
        Log(EventKind.Trial, gang.Id, $"{gang.Name} put up ${bail:N0} bail for {hood.Name}. He's out until his trial in {hood.JailWeeks} week{(hood.JailWeeks == 1 ? "" : "s")}.");
        return bail;
    }

    /// <summary>Why this man can't leave town, or null if he can.</summary>
    public string? CannotSkipTown(Gang gang, Hood hood)
    {
        if (hood.GangId != gang.Id || !hood.IsAvailable) return "He isn't free to go anywhere.";
        int fee = Content.HideoutFee(gang, hood);
        return gang.Cash < fee * Content.HideoutReserveWeeks
            ? $"Hiding out costs ${fee:N0} a week, and the outfit hasn't ${fee * Content.HideoutReserveWeeks:N0} for the first {Content.HideoutReserveWeeks} weeks."
            : null;
    }

    /// <summary>
    /// Send a man out of town. A man on bail jumps it, and the outfit loses the money; anyone else just lies low.
    /// Out of town nobody can arrest him, hit him or raid him, but he does no work.
    /// </summary>
    public bool SkipTown(Gang gang, Hood hood)
    {
        if (CannotSkipTown(gang, hood) != null) return false;
        string text;
        if (hood.Bail > 0)
        {
            text = $"{hood.Name} of {gang.Name} jumped his ${hood.Bail:N0} bail and skipped town ahead of his trial.";
            hood.Bail = 0;
            hood.Case!.JumpedBail = true;
            hood.Record.Add(new RapEntry { Week = Week, Kind = RapKind.Charged, Text = "Jumped bail and skipped town." });
        }
        else if (hood.Case != null) text = $"{hood.Name} of {gang.Name} skipped town, with a warrant out for him.";
        else text = $"{hood.Name} of {gang.Name} left town to lie low for a while.";
        hood.State = HoodState.Lam;
        hood.AwaySinceWeek = Week;
        Log(EventKind.Lam, gang.Id, text);
        return true;
    }

    /// <summary>Bring a man home. If there's a warrant out for him, the police can pick him up on it.</summary>
    public bool ComeHome(Gang gang, Hood hood, string? why = null)
    {
        if (hood.GangId != gang.Id || hood.State != HoodState.Lam) return false;
        hood.State = HoodState.Free;
        hood.AwaySinceWeek = -1;
        Log(EventKind.Lam, gang.Id, (why ?? "") + $"{hood.Name} is back in town" + (hood.Case != null ? ", and the warrant in his case is still out." : "."));
        return true;
    }
}

public sealed partial class Simulation
{
    /// <summary>
    /// Sunday, after the police: men on bail go to trial when it comes, men out of town cost their hideouts and may be
    /// found, their cases cool, a boss away loses his men's loyalty, and a wanted man at home may be picked up.
    /// </summary>
    private void OnTheLam()
    {
        var w = World;
        foreach (var hood in w.Hoods.Where(h => h.Bail > 0 && h.State == HoodState.Free).ToList())
        {
            if (--hood.JailWeeks > 0) continue;
            var gang = w.GangById(hood.GangId);
            gang.Cash += hood.Bail;
            w.Log(EventKind.Trial, gang.Id, $"{hood.Name} showed up for his trial, and {gang.Name} got its ${hood.Bail:N0} bail back.");
            hood.Bail = 0;
            hood.State = HoodState.Jailed;
            Trial(hood);
        }

        foreach (var hood in w.Hoods.Where(h => h.State == HoodState.Lam).ToList())
        {
            var gang = w.GangById(hood.GangId);
            if (!gang.Alive) { hood.State = HoodState.Gone; continue; }
            int fee = Content.HideoutFee(gang, hood);
            if (gang.Cash < fee) { w.ComeHome(gang, hood, $"{gang.Name} couldn't pay for the hideout. "); continue; }
            gang.Cash -= fee;
            w.Ledger(gang.Id).Spending += fee;
            if (hood.Id == gang.BossHoodId)
                foreach (var man in w.HoodsOf(gang.Id).Where(h => h.Id != hood.Id && h.Loyalty > Content.BossAwayLoyaltyFloor))
                    man.Loyalty = Math.Max(Content.BossAwayLoyaltyFloor, man.Loyalty - Content.BossAwayLoyalty);
            if (hood.Case is not { } c) continue;
            c.Strength = Math.Max(Content.ColdestCase, c.Strength - Content.ColdCase);
            if (w.Rng.Chance(Content.LamCatch(c))) Caught(hood, $"{hood.Name} of {gang.Name} was found out of town and brought back on the warrant in his case.");
        }

        foreach (var hood in w.Hoods.Where(h => h.State == HoodState.Free && World.IsWanted(h)).ToList())
        {
            var gang = w.GangById(hood.GangId);
            if (w.Rng.Chance(Content.WantedPickup(gang))) Caught(hood, $"Police picked up {hood.Name} of {gang.Name} on the warrant in his case.");
        }
    }

    /// <summary>A wanted man is caught: held for trial again, with no bail, and a jury that knows he ran if he did.</summary>
    private void Caught(Hood hood, string text)
    {
        var c = hood.Case!;
        hood.State = HoodState.Jailed;
        hood.AwaySinceWeek = -1;
        hood.JailWeeks = Content.WeeksToTrial;
        if (c.JumpedBail) c.Strength = Math.Min(0.95, c.Strength + Content.JumpedBailCase);
        World.Log(EventKind.HoodJailed, hood.GangId, $"{text} His trial is in {Content.WeeksToTrial} weeks.");
    }
}
