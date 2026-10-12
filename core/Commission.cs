namespace DryTown.Core;

/// <summary>The Commission: when it sits, who gets a seat, and what its say-so lasts.</summary>
public static partial class Content
{
    /// <summary>The bosses sit down together for the first time in this week of this year.</summary>
    public const int CommissionYear = 1931, CommissionWeek = 40;

    /// <summary>An outfit needs this many shops for a seat.</summary>
    public const int CommissionTurf = 5;

    /// <summary>What the Commission's say-so is good for: a war, or one boss.</summary>
    public const int SanctionWarWeeks = 12, SanctionHitWeeks = 8;

    /// <summary>How long an outfit must wait to put another question to the table.</summary>
    public const int AskEvery = 8;

    /// <summary>What a member's grudge against the outfit that broke the rules grows by, and how long the breaker is shut out.</summary>
    public const int GrudgeBreach = 25, ShelvedWeeks = 52;

    /// <summary>The grudge it takes for a member to vote for a war or a hit on someone.</summary>
    public const int VoteGrudge = 30;

    /// <summary>A rival at the table only goes to war without its say-so if it's this hot-headed and this angry, and then only now and then.</summary>
    public const double BreachAggression = 0.75, BreachChance = 0.2;
    public const int BreachGrudge = 85;
}

public enum SanctionKind { War, Hit }

/// <summary>The Commission's say-so: one outfit may go to war with another, or hit its boss.</summary>
public sealed class Sanction
{
    public SanctionKind Kind { get; init; }
    public int By { get; init; } = -1;
    public int Against { get; init; } = -1;
    public int UntilWeek { get; init; }
}

public sealed class CommissionState
{
    public bool Sitting { get; set; }
    public int FormedWeek { get; set; } = -1;
    public List<int> Seats { get; set; } = new();

    /// <summary>Outfits put out for breaking the rules, and the week they can sit again.</summary>
    public Dictionary<int, int> ShelvedUntil { get; set; } = new();

    public List<Sanction> Sanctions { get; set; } = new();

    /// <summary>The last week each outfit put a question to the table.</summary>
    public Dictionary<int, int> LastAsked { get; set; } = new();

    /// <summary>The player walked out, and won't be given a seat until they ask for one.</summary>
    public bool PlayerWalkedOut { get; set; }
}

/// <summary>The rules: no war on another member and no hit on its boss without the table's say-so.</summary>
public sealed partial class World
{
    public CommissionState Commission { get; set; } = new();

    public int CommissionStartWeek => (Content.CommissionYear - Content.StartYear) * Content.WeeksPerYear + Content.CommissionWeek - 1;

    public bool Seated(int gangId) => Commission.Sitting && Commission.Seats.Contains(gangId);

    public IEnumerable<Gang> Members => Commission.Seats.Select(GangById).Where(g => g.Alive);

    /// <summary>The biggest outfit at the table chairs it, and breaks a tied vote.</summary>
    public Gang? Chair => Members.OrderByDescending(g => TurfOf(g.Id).Count()).ThenBy(g => g.Id).FirstOrDefault();

    public bool Shelved(int gangId) => Commission.ShelvedUntil.TryGetValue(gangId, out int until) && Week < until;

    /// <summary>The table's say-so for this outfit to go after that one, of this kind (a war covers its boss too).</summary>
    public bool Sanctioned(int by, int against, SanctionKind kind) =>
        Commission.Sanctions.Any(s => s.Against == against && s.By == by && s.UntilWeek > Week && (s.Kind == kind || s.Kind == SanctionKind.War));

    /// <summary>Whether going to war with, or hitting the boss of, the other outfit breaks the Commission's rules.</summary>
    public bool BreaksRules(int by, int against, SanctionKind kind) =>
        Seated(by) && Seated(against) && !AtWar(by, against) && !Sanctioned(by, against, kind);

    /// <summary>Why the gang can't put this question to the table, or null if it can.</summary>
    public string? CannotAsk(Gang asker, Gang target)
    {
        if (!Commission.Sitting) return "There's no Commission yet.";
        if (!Seated(asker.Id)) return "You don't have a seat at the table.";
        if (asker.Id == target.Id || !target.Alive) return "There's nobody to ask about.";
        if (!Seated(target.Id)) return $"{target.Name} has no seat. The rules don't protect them.";
        if (Commission.LastAsked.TryGetValue(asker.Id, out int last) && Week - last < Content.AskEvery)
            return $"You put a question to the table {Week - last} weeks ago. Wait {Content.AskEvery - (Week - last)} more.";
        return null;
    }

    /// <summary>How each other member would vote on letting the asker go after the target: yes, or no and why.</summary>
    public List<(Gang Voter, bool Yes)> Votes(Gang asker, Gang target) =>
        Members.Where(m => m.Id != asker.Id && m.Id != target.Id).OrderBy(m => m.Id).Select(m => (m, VotesYes(m, asker, target))).ToList();

    private bool VotesYes(Gang voter, Gang asker, Gang target)
    {
        if (AtPeace(voter.Id, target.Id) && PactBetween(voter.Id, target.Id)!.Kind == PactKind.Alliance) return false;
        if (GrudgeOf(voter.Id, asker.Id) >= Content.VoteGrudge) return false;
        if (AlliesOf(voter.Id).Contains(asker.Id)) return true;
        // Nobody at the table likes an outfit that's getting too big.
        return GrudgeOf(voter.Id, target.Id) >= Content.VoteGrudge || TopDog(target);
    }

    /// <summary>Whether the table would say yes, as things stand.</summary>
    public bool WouldPass(Gang asker, Gang target)
    {
        var votes = Votes(asker, target);
        int yes = votes.Count(v => v.Yes), no = votes.Count - yes;
        if (yes != no) return yes > no;
        // A tie goes the chair's way, and a chair with a stake in it says no.
        var chair = Chair;
        return chair != null && chair.Id != target.Id && (chair.Id == asker.Id || votes.Any(v => v.Voter.Id == chair.Id && v.Yes));
    }

    /// <summary>Put it to the table: may the asker go to war with the target, or hit its boss? Returns what the table said.</summary>
    public string Ask(Gang asker, Gang target, SanctionKind kind)
    {
        if (CannotAsk(asker, target) is { } no) return no;
        Commission.LastAsked[asker.Id] = Week;
        var votes = Votes(asker, target);
        bool pass = WouldPass(asker, target);
        string what = kind == SanctionKind.War ? $"go to war with {target.Name}" : $"have {HoodById(target.BossHoodId).Name} killed";
        string tally = votes.Count == 0 ? "with nobody else at the table to vote" : $"{votes.Count(v => v.Yes)} to {votes.Count(v => !v.Yes)}";
        if (pass)
        {
            Commission.Sanctions.Add(new Sanction { Kind = kind, By = asker.Id, Against = target.Id, UntilWeek = Week + (kind == SanctionKind.War ? Content.SanctionWarWeeks : Content.SanctionHitWeeks) });
            AddGrudge(target.Id, asker.Id, 10);
        }
        Log(EventKind.Commission, asker.Id, pass
            ? $"The Commission voted {tally} to let {asker.Name} {what}."
            : $"The Commission voted {tally} against letting {asker.Name} {what}.");
        string who = votes.Count == 0 ? "" : " " + string.Join("; ", votes.Select(v => $"{v.Voter.Name}: {(v.Yes ? "yes" : "no")}")) + ".";
        return (pass ? $"The table said yes, {tally}. You have {(kind == SanctionKind.War ? Content.SanctionWarWeeks : Content.SanctionHitWeeks)} weeks." : $"The table said no, {tally}.") + who;
    }

    /// <summary>An outfit at the table went to war, or after a boss, without its say-so. Everyone at the table turns on it.</summary>
    public void Breach(Gang breaker, Gang victim, string what)
    {
        Commission.Seats.Remove(breaker.Id);
        Commission.ShelvedUntil[breaker.Id] = Week + Content.ShelvedWeeks;
        foreach (var m in Members.ToList())
        {
            AddGrudge(m.Id, breaker.Id, Content.GrudgeBreach);
            if (m.Id != victim.Id && MarriageBetween(m.Id, breaker.Id) == null && PactBetween(m.Id, breaker.Id) is { } pact) Pacts.Remove(pact);
        }
        Log(EventKind.Commission, breaker.Id, $"{breaker.Name} {what} without the Commission's say-so. The table has thrown them out for a year, and every outfit at it is free to go after them.");
    }

    /// <summary>Leave the table, or ask for the seat back. Only the player does either.</summary>
    public string Walk(Gang gang, bool leave)
    {
        if (!Commission.Sitting) return "There's no Commission yet.";
        if (leave)
        {
            if (!Seated(gang.Id)) return "You don't have a seat.";
            Commission.Seats.Remove(gang.Id);
            Commission.PlayerWalkedOut = true;
            Log(EventKind.Commission, gang.Id, $"{gang.Name} walked out of the Commission. The rules don't bind them now, and don't protect them either.");
            return "You walked out. Nobody needs the table's say-so to go after you, and you don't need it either.";
        }
        if (Seated(gang.Id)) return "You already have a seat.";
        if (Shelved(gang.Id)) return $"The table won't have you back until week {Commission.ShelvedUntil[gang.Id] % Content.WeeksPerYear + 1}, {Content.StartYear + Commission.ShelvedUntil[gang.Id] / Content.WeeksPerYear}.";
        if (TurfOf(gang.Id).Count() < Content.CommissionTurf) return $"The table only seats outfits with {Content.CommissionTurf} shops or more.";
        Commission.PlayerWalkedOut = false;
        Commission.Seats.Add(gang.Id);
        Log(EventKind.Commission, gang.Id, $"{gang.Name} took a seat at the Commission.");
        return "You have your seat back. The rules bind you again, and protect you.";
    }
}
