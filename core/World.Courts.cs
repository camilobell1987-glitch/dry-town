namespace DryTown.Core;

/// <summary>The courts from the gang's side: a lawyer on retainer, and a judge who can be reached.</summary>
public sealed partial class World
{
    /// <summary>The gang's men waiting on a trial, in a cell or out on bail, soonest first.</summary>
    public IEnumerable<Hood> OnTrial(int gangId) =>
        Hoods.Where(h => h.GangId == gangId && AwaitingTrial(h)).OrderBy(h => h.JailWeeks).ThenBy(h => h.Id);

    /// <summary>He has a trial date: held for it, or out on bail until it.</summary>
    public static bool AwaitingTrial(Hood h) => h.Case != null && (h.State == HoodState.Jailed || h.State == HoodState.Free && h.Bail > 0);

    /// <summary>Put a lawyer on retainer, or let him go. His first week's fee is paid on Sunday with the wages.</summary>
    public void Retain(Gang gang, Lawyer lawyer)
    {
        if (gang.Lawyer == lawyer) return;
        gang.Lawyer = lawyer;
        Log(EventKind.Bribe, gang.Id, lawyer == Lawyer.None
            ? $"{gang.Name} let its lawyer go."
            : $"{gang.Name} put {Content.Lawyers[lawyer].Label} on retainer.");
    }

    public int JudgePrice(Hood hood) => hood.Case == null ? 0 : Content.JudgePrice(hood.Case, Hall.Reform);

    /// <summary>Why the gang can't pay off the judge in this man's case, or null if it can.</summary>
    public string? CannotPayJudge(Gang gang, Hood hood)
    {
        if (hood.GangId != gang.Id || !AwaitingTrial(hood)) return "He isn't waiting on a trial.";
        if (hood.Case.Federal) return "It's a federal case. The Treasury's judges can't be bought.";
        if (hood.Case.JudgePaid) return "The judge has already been taken care of.";
        int price = JudgePrice(hood);
        return gang.Cash < price ? $"The judge wants ${price:N0}." : null;
    }

    /// <summary>Pay off the judge in a man's case. Returns what it cost, or -1.</summary>
    public int PayJudge(Gang gang, Hood hood)
    {
        if (CannotPayJudge(gang, hood) != null) return -1;
        int price = JudgePrice(hood);
        gang.Cash -= price;
        Ledger(gang.Id).Spending += price;
        gang.Heat = Math.Min(100, gang.Heat + 3);
        hood.Case!.JudgePaid = true;
        Log(EventKind.Bribe, gang.Id, $"{gang.Name} paid the judge ${price:N0} in {hood.Name}'s case.");
        return price;
    }
}
