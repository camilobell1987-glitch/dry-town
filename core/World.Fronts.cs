namespace DryTown.Core;

/// <summary>Businesses a gang owns outright, to wash its money through.</summary>
public sealed partial class World
{
    public IEnumerable<Business> FrontsOf(int gangId) => Businesses.Where(b => b.OwnerGangId == gangId);

    /// <summary>A front only does its job while it's open and the gang's own men are still the ones watching it.</summary>
    public static bool Working(Business b) => b.IsFront && b.ProtectorGangId == b.OwnerGangId && b.IsOpen;

    /// <summary>How much dirty money the gang's working fronts can wash in a week.</summary>
    public long LaunderCapacity(int gangId) => FrontsOf(gangId).Where(Working).Sum(b => (long)Content.LaunderCapacity(b));

    /// <summary>Why the gang can't buy this business, or null if it can.</summary>
    public string? CannotBuyFront(Gang gang, Business b)
    {
        if (b.Kind == BusinessKind.Court) return "Nobody owns a court to sell it.";
        if (b.OwnerGangId == gang.Id) return "It's already yours.";
        if (b.IsFront) return $"It's a front for {GangById(b.OwnerGangId).Name}.";
        if (b.ProtectorGangId != gang.Id) return "The owner only sells to the outfit he already pays.";
        if (!b.IsOpen) return "It's shut. Nobody buys a shut business.";
        int price = Content.FrontPrice(b);
        return gang.Cash < price ? $"That costs ${price:N0}." : null;
    }

    /// <summary>Buy a business the gang protects. Returns what it cost, or -1 if it can't be bought.</summary>
    public int BuyFront(Gang gang, Business b)
    {
        if (CannotBuyFront(gang, b) != null) return -1;
        int price = Content.FrontPrice(b);
        gang.Cash -= price;
        Ledger(gang.Id).Spending += price;
        b.OwnerGangId = gang.Id;
        b.Resentment = 0;
        Log(EventKind.Front, gang.Id, $"{gang.Name} bought {b.Name} outright for ${price:N0}.");
        return price;
    }
}
