namespace DryTown.Core;

/// <summary>Going to the mattresses: what an open war costs, and what it changes.</summary>
public static partial class Content
{
    /// <summary>What it costs a week to keep each man in a safehouse: the rent, the cots and the meals.</summary>
    public const int MattressCost = 15;

    /// <summary>How much likelier a hit on the enemy is during a war, with the whole outfit looking for the chance.</summary>
    public const double WarHit = 0.15;

    /// <summary>Grudge the outfit on the receiving end takes against the one that declared.</summary>
    public const int GrudgeWar = 30;

    /// <summary>Heat each side draws, and outrage the city feels, in a week of war. Both grow the longer it lasts.</summary>
    public static int WarHeat(int weeks) => Math.Min(6, 1 + weeks / 6);
    public static int WarOutrage(int weeks) => Math.Min(4, 1 + weeks / 8);
}

/// <summary>Which outfits are at war, and declaring and ending one.</summary>
public sealed partial class World
{
    public List<War> Wars { get; } = new();

    public War? WarOf(int gangId) => Wars.FirstOrDefault(x => x.Involves(gangId));
    public bool AtWar(int gangId) => WarOf(gangId) != null;
    public bool AtWar(int a, int b) => a != b && Wars.Any(x => x.Involves(a) && x.Involves(b));

    /// <summary>Why the gang can't go to war with the other, or null if it can.</summary>
    public string? CannotDeclare(Gang gang, Gang enemy)
    {
        if (gang.Id == enemy.Id || !enemy.Alive) return "There's nobody to fight.";
        if (AtWar(gang.Id, enemy.Id)) return $"You're already at war with {enemy.Name}.";
        if (WarOf(gang.Id) is { } war) return $"You're already at war with {GangById(war.Other(gang.Id)).Name}.";
        if (WarOf(enemy.Id) is { } theirs) return $"{enemy.Name} already has its men on the mattresses against {GangById(theirs.Other(enemy.Id)).Name}.";
        if (PactBetween(gang.Id, enemy.Id) is { } pact) return $"You have {(pact.Kind == PactKind.Truce ? "a truce" : "an alliance")} with {enemy.Name}. Break it first.";
        return null;
    }

    /// <summary>Go to the mattresses against another outfit.</summary>
    public string Declare(Gang gang, Gang enemy)
    {
        if (CannotDeclare(gang, enemy) is { } no) return no;
        Wars.Add(new War { A = gang.Id, B = enemy.Id, StartWeek = Week });
        AddGrudge(enemy.Id, gang.Id, Content.GrudgeWar);
        Offers.RemoveAll(o => o.FromGangId == enemy.Id && gang.IsPlayer || o.FromGangId == gang.Id && enemy.IsPlayer);
        Log(EventKind.Diplomacy, gang.Id, $"{gang.Name} went to the mattresses against {enemy.Name}. Both outfits have their men sleeping in safehouses, and the shooting has started.");
        return $"Your men are on the mattresses. It's war with {enemy.Name} until one of you calls it off or makes peace.";
    }

    /// <summary>Why the gang can't call off its war, or null if it can. Only the side that started it can.</summary>
    public string? CannotCallOff(Gang gang)
    {
        if (WarOf(gang.Id) is not { } war) return "You aren't at war.";
        return war.A == gang.Id ? null : $"{GangById(war.A).Name} started it, and only they can call it off. Ask for a truce.";
    }

    public string CallOff(Gang gang)
    {
        if (CannotCallOff(gang) is { } no) return no;
        var war = WarOf(gang.Id)!;
        var enemy = GangById(war.Other(gang.Id));
        EndWar(war, $"{gang.Name} called off the war with {enemy.Name}, and the men came home from the mattresses.");
        return $"You called it off. Your men are home, and {enemy.Name} can stand down.";
    }

    public void EndWar(War war, string why)
    {
        Wars.Remove(war);
        Log(EventKind.Diplomacy, war.A, why);
    }
}
