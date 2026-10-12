using System.Text;

namespace DryTown.Core;

/// <summary>The Untouchables, in words.</summary>
public static partial class Reports
{
    public static string Squad(World w, int gangId)
    {
        if (!w.Prohibition) return "The Untouchables went home with repeal.\n";
        if (!w.SquadInTown) return $"The Prohibition Bureau leaves the city to the precincts until {Content.UntouchablesYear}, when it sends a squad nobody can buy.\n";
        var s = w.Untouchables;
        var sb = new StringBuilder();
        var target = w.SquadTarget;
        if (target == null) sb.AppendLine("The Untouchables aren't after anyone just now.");
        else if (target.Id != gangId) sb.AppendLine($"The Untouchables are after {w.HoodById(target.BossHoodId).Name} of {target.Name}. They have {s.Evidence} of the {Content.SquadIndictment} smashed places a grand jury needs.");
        else
        {
            var me = w.GangById(gangId);
            sb.AppendLine($"The Untouchables are after you. Each week there's a {w.SquadChance(me):P0} chance they raid one of your stills or speakeasies, more as your heat rises, and they can't be bought.");
            sb.AppendLine($"They have {s.Evidence} of the {Content.SquadIndictment} smashed places a grand jury needs to indict {w.HoodById(me.BossHoodId).Name}. If they go {Content.SquadStaleWeeks} weeks without one, the oldest goes stale.");
            foreach (var b in w.LiquorOf(gangId).OrderBy(b => b.Id))
            {
                var man = b.HandlerHoodId >= 0 ? w.Hoods.FirstOrDefault(h => h.Id == b.HandlerHoodId) : null;
                sb.AppendLine($"  {b.Id,4} {Trim(b.Name, 28),-28} {(b.Racket == RacketKind.Still ? "still" : "speakeasy"),-9} {(man == null ? "nobody handling it" : $"{man.Name}, {w.Warning(b):P0} he gets word in time")}");
            }
        }
        sb.AppendLine($"Each January the squad goes after the biggest, loudest bootlegger with at least {Content.SquadRackets} stills and speakeasies, a supply line counting as two. A stealthy man handling a place may get word before a raid.");
        return sb.ToString();
    }
}
