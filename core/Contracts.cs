namespace DryTown.Core;

/// <summary>City contracts: the garbage, the paving, the coal and the hospital linens, let by the aldermen to whoever they owe.</summary>
public static partial class Content
{
    /// <summary>A contract runs this many weeks, and the aldermen let any free one at the turn of each quarter.</summary>
    public const int ContractWeeks = 52, ContractLetting = 13;

    /// <summary>What a contract pays a week, clean: more in a bigger city.</summary>
    public static int ContractPay(int businesses) => 40 + businesses / 2;

    /// <summary>An outfit needs this share of the aldermen voting for it to win a contract. The mayor's friend gets this many votes more.</summary>
    public const double ContractShare = 0.34;
    public const int MayorVotes = 2;

    /// <summary>Weekly chance the papers dig into a contract an outfit holds, and what each point of outrage and a reform mayor add.</summary>
    public const double GraftScandal = 0.004, GraftPerOutrage = 0.0002, GraftReform = 0.006;

    /// <summary>The heat and outrage a graft story brings.</summary>
    public const int GraftHeat = 10, GraftOutrage = 10;
}

public enum ContractKind { Garbage, Paving, Coal, Linens }

/// <summary>One of the city's contracts: who holds it, and until when.</summary>
public sealed class Contract
{
    public ContractKind Kind { get; init; }
    public int GangId { get; set; } = -1;
    public int UntilWeek { get; set; } = -1;
}

public static class Contracts
{
    public static string Label(ContractKind k) => k switch
    {
        ContractKind.Garbage => "hauling the city's garbage",
        ContractKind.Paving => "paving the city's streets",
        ContractKind.Coal => "coal for the schools",
        _ => "linens for the city hospital",
    };

    /// <summary>The kinds of front an outfit needs to take a contract on: a business that can plausibly do the work.</summary>
    public static BusinessKind[] Needs(ContractKind k) => k switch
    {
        ContractKind.Garbage => new[] { BusinessKind.Garage },
        ContractKind.Paving => new[] { BusinessKind.Warehouse, BusinessKind.Garage },
        ContractKind.Coal => new[] { BusinessKind.Warehouse },
        _ => new[] { BusinessKind.Laundry },
    };

    public static string NeedsLabel(ContractKind k) => string.Join(" or ", Needs(k).Select(b => Content.Label(b).ToLowerInvariant()));

    public static int Pay(World w) => Content.ContractPay(w.Businesses.Count);

    /// <summary>The front an outfit would do the work through, if it has one working.</summary>
    public static Business? FrontFor(World w, Gang g, ContractKind k) =>
        w.FrontsOf(g.Id).Where(b => World.Working(b) && Needs(k).Contains(b.Kind)).OrderBy(b => b.Id).FirstOrDefault();

    /// <summary>The aldermen who vote the way an outfit says: the ones it pays and its own men, and two more for the mayor's friend.</summary>
    public static int Votes(World w, Gang g) =>
        w.Wards.Count(x => x.OwnerGangId == g.Id) + (w.Hall.FriendGangId == g.Id && !w.Hall.Reform ? Content.MayorVotes : 0);

    /// <summary>The votes an outfit needs to win a contract.</summary>
    public static int VotesNeeded(World w) => Math.Max(1, (int)Math.Ceiling(w.Wards.Count * Content.ContractShare));

    public static Contract? HeldBy(World w, Gang g, ContractKind k) => w.Contracts.FirstOrDefault(c => c.Kind == k && c.GangId == g.Id);

    /// <summary>Why the aldermen won't let this outfit a contract whatever it has on them, or null. They won't be seen handing the city's money to the outfit that runs a third of it.</summary>
    public static string? CannotWin(World w, Gang g) =>
        w.TopDog(g) ? $"{g.Name} runs a third of the city, and no alderman will be seen handing it the city's money too." : null;

    /// <summary>The week the next letting comes.</summary>
    public static int NextLetting(World w) => (w.Week / Content.ContractLetting + 1) * Content.ContractLetting;

    /// <summary>
    /// Sunday: each contract an outfit holds pays clean money through its front, or lapses if the front or the outfit is gone.
    /// The papers may dig into one. At the turn of the quarter the aldermen let any contract that's free.
    /// </summary>
    public static void Step(World w)
    {
        if (w.Contracts.Count == 0)
            foreach (var k in Enum.GetValues<ContractKind>()) w.Contracts.Add(new Contract { Kind = k });
        int pay = Pay(w);
        foreach (var c in w.Contracts.Where(c => c.GangId >= 0))
        {
            var g = w.GangById(c.GangId);
            if (!g.Alive || FrontFor(w, g, c.Kind) is not { } front)
            {
                if (g.Alive) w.Log(EventKind.Politics, g.Id, $"With no {NeedsLabel(c.Kind)} of its own to do the work, {g.Name} lost the contract for {Label(c.Kind)}.");
                c.GangId = -1;
                continue;
            }
            if (w.Rng.Chance(Content.GraftScandal + w.Hall.Outrage * Content.GraftPerOutrage + (w.Hall.Reform ? Content.GraftReform : 0)))
            {
                g.Heat = Math.Min(100, g.Heat + Content.GraftHeat);
                w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + Content.GraftOutrage);
                w.Log(EventKind.Politics, g.Id, $"The papers found out who really does the work for {Label(c.Kind)}: {front.Name}, and {g.Name} behind it. The city tore up the contract.");
                c.GangId = -1;
                continue;
            }
            g.Cash += pay;
            g.CleanCash += pay;
            w.Ledger(g.Id).Legit += pay;
            if (w.Week >= c.UntilWeek)
            {
                w.Log(EventKind.Politics, g.Id, $"{g.Name}'s contract for {Label(c.Kind)} ran out.");
                c.GangId = -1;
            }
        }
        if (w.Week % Content.ContractLetting == 0) Let(w);
    }

    /// <summary>The aldermen let every free contract to the outfit with the most of them in its pocket and a front to do the work, unless it already runs a third of the city. Nobody with enough votes, and an honest firm gets it.</summary>
    private static void Let(World w)
    {
        int needed = VotesNeeded(w);
        foreach (var c in w.Contracts.Where(c => c.GangId < 0))
        {
            var winner = w.Outfits.Where(g => CannotWin(w, g) == null && FrontFor(w, g, c.Kind) != null)
                .Select(g => (g, votes: Votes(w, g)))
                .Where(x => x.votes >= needed)
                .OrderByDescending(x => x.votes).ThenBy(x => x.g.Id)
                .Select(x => x.g).FirstOrDefault();
            if (winner == null) continue;
            c.GangId = winner.Id;
            c.UntilWeek = w.Week + Content.ContractWeeks;
            w.Log(EventKind.Politics, winner.Id, $"The aldermen gave {winner.Name}'s {FrontFor(w, winner, c.Kind)!.Name} the contract for {Label(c.Kind)}: ${Pay(w)} a week, clean, for a year.");
        }
    }

    /// <summary>A dissolved outfit's contracts go back to the city.</summary>
    public static void Forget(World w, Gang g)
    {
        foreach (var c in w.Contracts.Where(c => c.GangId == g.Id)) c.GangId = -1;
    }
}

public sealed partial class World
{
    public List<Contract> Contracts { get; } = new();
}
