namespace DryTown.Core;

/// <summary>Sunday at the fronts: their honest profit, then the dirty money run through their books.</summary>
public sealed partial class Simulation
{
    private void Launder()
    {
        var w = World;
        foreach (var gang in w.LivingGangs)
        {
            // Clean money is spent last, so what's left of it is whatever the week's bills didn't reach.
            gang.CleanCash = gang.Clean;
            var fronts = w.FrontsOf(gang.Id).Where(World.Working).ToList();
            if (fronts.Count == 0) continue;
            var ledger = w.Ledger(gang.Id);

            long profit = fronts.Sum(b => (long)b.Takings * Content.FrontProfitPercent / 100);
            gang.Cash += profit;
            gang.CleanCash += profit;
            ledger.Legit += profit;

            long washed = Math.Min(gang.Dirty, fronts.Sum(b => (long)Content.LaunderCapacity(b)));
            if (washed <= 0) continue;
            long fee = washed * Content.LaunderFeePercent / 100;
            gang.Cash -= fee;
            gang.CleanCash += washed - fee;
            ledger.Laundered += washed;
            ledger.LaunderFee += fee;
        }
    }
}
