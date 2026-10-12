using System.Text;

namespace DryTown.Core;

/// <summary>The Commission in words: who sits at the table, what it has allowed, and who is out.</summary>
public static partial class Reports
{
    public static string Commission(World w, int gangId)
    {
        var c = w.Commission;
        if (!c.Sitting) return $"The bosses haven't sat down together yet. The Commission forms in {Content.CommissionYear}.\n";
        var sb = new StringBuilder();
        var chair = w.Chair;
        sb.AppendLine($"The Commission has sat since {Date(w, c.FormedWeek)}. No outfit at the table goes to war with another, or touches another's boss, without its say-so.");
        foreach (var g in w.Members.OrderByDescending(g => w.TurfOf(g.Id).Count()))
            sb.AppendLine($"  {g.Id,3} {Trim(g.Name, 28),-28} {w.TurfOf(g.Id).Count(),3} shops" + (g.Id == chair?.Id ? ", in the chair" : "") + (g.Id == gangId ? " (you)" : ""));
        foreach (var (id, until) in c.ShelvedUntil.Where(x => w.Shelved(x.Key)))
            sb.AppendLine($"  {w.GangById(id).Name} broke the rules and is out until {Date(w, until)}.");
        foreach (var s in c.Sanctions.Where(s => s.UntilWeek > w.Week))
            sb.AppendLine($"  {w.GangById(s.By).Name} may {(s.Kind == SanctionKind.War ? "go to war with" : "hit the boss of")} {w.GangById(s.Against).Name} for {s.UntilWeek - w.Week} more weeks.");
        if (!w.Seated(gangId))
            sb.AppendLine(w.Shelved(gangId) ? "You broke the rules and have no seat. Nobody needs the table's say-so to go after you." :
                c.PlayerWalkedOut ? "You walked out. 'commission join' asks for your seat back." : $"You have no seat. The table seats outfits with {Content.CommissionTurf} shops or more.");
        return sb.ToString();
    }

    /// <summary>A warning when a hit on this man would break the Commission's rules, or null.</summary>
    public static string? BreachWarning(World w, int gangId, Hood target)
    {
        var rival = w.GangById(target.GangId);
        if (rival.BossHoodId != target.Id || !w.BreaksRules(gangId, rival.Id, SanctionKind.Hit)) return null;
        return $"{target.Name} sits at the Commission. Going after him without its say-so breaks the rules: the table will throw you out for a year, and every outfit at it will hold it against you.";
    }

    /// <summary>The same, for going to war.</summary>
    public static string? WarWarning(World w, int gangId, Gang enemy) =>
        w.BreaksRules(gangId, enemy.Id, SanctionKind.War)
            ? $"{enemy.Name} sits at the Commission. Going to war without its say-so breaks the rules: the table will throw you out for a year, and every outfit at it will hold it against you."
            : null;
}
