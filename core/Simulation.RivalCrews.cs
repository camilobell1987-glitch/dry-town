namespace DryTown.Core;

public static partial class Content
{
    /// <summary>The most standing crews a rival outfit keeps, however many men it has.</summary>
    public const int MaxRivalCrews = 4;
}

/// <summary>Rival outfits organise their men the way the player can: lieutenants with standing crews under them.</summary>
public sealed partial class Simulation
{
    /// <summary>How many crews an outfit of this many men keeps: one for every four men besides the boss.</summary>
    public static int CrewsFor(int men) => Math.Min(Content.MaxRivalCrews, men / (Crew.MaxMembers + 1));

    /// <summary>
    /// Sunday: each rival outfit fills the gaps in its crews with loose men, and once it has hands
    /// enough, makes its best loose man a lieutenant of a new one. The player's crews are left alone.
    /// </summary>
    private void RivalCrews()
    {
        var w = World;
        foreach (var gang in w.LivingGangs.Where(g => !g.IsPlayer).ToList())
        {
            var men = w.HoodsOf(gang.Id).Where(h => h.Id != gang.BossHoodId).ToList();
            var loose = men.Where(h => h.IsAvailable && w.CrewOfHood(h.Id) == null)
                .OrderByDescending(h => h.Strength).ThenBy(h => h.Id).ToList();

            if (w.CrewsOf(gang.Id).Count() < CrewsFor(men.Count) && loose.Count >= 2)
            {
                // The man the others would follow: a head on his shoulders, some weight, and no grudge against the boss.
                var lt = loose.Where(h => h.Loyalty >= 40)
                    .OrderByDescending(h => h.Brains * 2 + h.Strength + h.Loyalty / 10).ThenBy(h => h.Id).FirstOrDefault();
                if (lt != null)
                {
                    w.FormCrew(lt);
                    loose.Remove(lt);
                    w.Log(EventKind.Whisper, gang.Id, $"{gang.Name} made {lt.Name} a lieutenant.");
                }
            }

            foreach (var crew in w.CrewsOf(gang.Id).ToList())
                while (crew.MemberIds.Count < Crew.MaxMembers && loose.Count > 0)
                {
                    w.JoinCrew(crew, loose[0]);
                    loose.RemoveAt(0);
                }
        }
    }
}
