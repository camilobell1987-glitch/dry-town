using System.Text;

namespace DryTown.Core;

/// <summary>The bosses' names, in words.</summary>
public static partial class Reports
{
    public static string Names(World w, int gangId)
    {
        var me = w.GangById(gangId);
        var sb = new StringBuilder();
        string boss = w.HoodById(me.BossHoodId).Name;
        sb.AppendLine($"{boss} is {World.NameOf(me)}: fear {me.Fear:0}, respect {me.Respect:0}.");
        sb.AppendLine($"Shakedowns go {me.Fear * Content.FearShakedown:P0} better, juries convict your men {me.Fear * Content.FearJury + me.Respect * Content.RespectJury:P0} less often, and new men come in {(int)(me.Respect * Content.RespectRecruitLoyalty)} points more loyal{(me.Fear >= Content.FearedRecruit ? ", and harder" : "")}.");
        if (World.Fame(me) >= Content.NameInPapers)
            sb.AppendLine($"A name that big draws the Bureau: the Untouchables and the G-men weigh you {World.NameDraw(me):0.0} times as heavily.");
        sb.AppendLine(me.SoupKitchen
            ? $"Your soup kitchen costs ${Content.SoupKitchenWeekly} a week and earns {w.SoupRespect:0.0} respect a week{(w.Depression ? ", twice as much in the Depression" : "")}."
            : $"A soup kitchen would cost ${Content.SoupKitchenWeekly} a week and earn {w.SoupRespect:0.0} respect a week{(w.Depression ? "" : ", twice as much once the Depression comes")}. Type soup on.");
        sb.AppendLine("Killings, fires and shops taken by force make a boss feared. Acquittals and looking after jailed men's families make him admired, and breaking his word costs him. Names fade slowly, and a new boss keeps half the old one's.");
        sb.AppendLine();
        foreach (var g in w.Outfits.Where(g => g.Id != gangId).OrderByDescending(World.Fame).ThenBy(g => g.Id))
            sb.AppendLine($"  {Trim(w.HoodById(g.BossHoodId).Name, 28),-28} {Trim(g.Name, 24),-24} {World.NameOf(g)}{(g.SoupKitchen ? ", runs a soup kitchen" : "")}");
        return sb.ToString();
    }
}
