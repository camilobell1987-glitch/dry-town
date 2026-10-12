using System.Text;

namespace DryTown.Core;

public static partial class Reports
{
    /// <summary>The city's neighbourhoods: whose people live in each ward, and which outfits run shops there.</summary>
    public static string Neighbourhoods(World w, int gangId)
    {
        var sb = new StringBuilder();
        var me = w.GangById(gangId);
        var mine = w.PeopleOf(me);
        sb.AppendLine($"Your outfit is {Content.Label(mine)}. Your men of a ward's own people do better shaking down its shops and getting out its vote; " +
                      "the shopkeepers there resent you less, and men of other peoples start out a little less loyal to you.");
        foreach (var people in w.Wards.Select(w.Neighbourhood).Distinct().OrderBy(e => e))
        {
            var wards = w.Wards.Where(x => w.Neighbourhood(x) == people).ToList();
            var outfits = w.LivingGangs.Where(g => w.PeopleOf(g) == people).Select(g => g.Id == gangId ? "you" : g.Name).ToList();
            sb.AppendLine($"{Content.Label(people)}: {string.Join(", ", wards.Select(x => x.Name))}. " +
                          (outfits.Count > 0 ? $"Their own outfits: {string.Join(", ", outfits)}." : "No outfit of their own."));
            var shops = wards.SelectMany(x => Politics.BusinessesIn(w, x)).Where(b => b.IsProtected)
                .GroupBy(b => b.ProtectorGangId).OrderByDescending(x => x.Count()).ThenBy(x => x.Key)
                .Select(x => $"{(x.Key == gangId ? "you" : w.GangById(x.Key).Name)} {x.Count()}");
            sb.AppendLine($"    Shops paying: {string.Join(", ", shops.DefaultIfEmpty("none"))}.");
        }
        var men = w.HoodsOf(gangId).Where(h => h.IsActive).GroupBy(h => h.Origin).OrderByDescending(x => x.Count()).ThenBy(x => x.Key);
        sb.AppendLine($"Your men: {string.Join(", ", men.Select(x => $"{x.Count()} {Content.Label(x.Key)}"))}.");
        return sb.ToString();
    }
}
