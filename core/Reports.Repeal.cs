using System.Text;

namespace DryTown.Core;

/// <summary>After repeal, in words: the racing wire and the Bureau's public enemy.</summary>
public static partial class Reports
{
    public static string Wire(World w, int gangId)
    {
        if (!w.WireUp) return "The racing wire comes to town with repeal, in 1934.\n";
        var room = w.BusinessById(w.WireBusinessId);
        int wire = w.WireGangId;
        int bookies = w.Businesses.Count(b => b.Racket == RacketKind.Bookie && b.IsOpen && b.IsProtected);
        string who = wire == gangId
            ? $"You run the racing wire from the back of {room.Name}. Your bookies take half as much again, and every other outfit's pays you ${Content.WireFee} a week: ${w.WireWorth(gangId)} a week in all."
            : wire >= 0
                ? $"{w.GangById(wire).Name} runs the racing wire from the back of {room.Name} (business {room.Id}). Every bookie that isn't theirs pays them ${Content.WireFee} a week. Taking the place would be worth ${w.WireWorth(gangId)} a week to you."
                : $"Nobody runs the racing wire at {room.Name} (business {room.Id}). Whoever protects it takes a cut of every bookie in the city: ${w.WireWorth(gangId)} a week to you.";
        return $"{who}\nThe city has {bookies} bookies running.\n";
    }

    public static string Gmen(World w, int gangId)
    {
        if (w.Prohibition) return "The Bureau of Investigation leaves the city's outfits to the precincts and the Treasury until after repeal.\n";
        var enemy = w.PublicEnemy;
        var sb = new StringBuilder();
        if (enemy == null) sb.AppendLine("The Bureau hasn't named a public enemy in this city just now.");
        else
        {
            string boss = w.HoodById(enemy.BossHoodId).Name;
            sb.AppendLine(enemy.Id == gangId
                ? $"The Bureau has named you, {boss}, Public Enemy Number One. Each week there's a {w.GmenChance(enemy):P0} chance the G-men pick up one of your men, or you, on a federal warrant. Less heat means fewer raids, and they can't be bribed."
                : $"The Bureau has named {boss} of {enemy.Name} Public Enemy Number One.");
        }
        sb.AppendLine($"Each January the Bureau names the biggest, loudest outfit with at least {Content.PublicEnemyTurf} shops. When the G-men take the boss himself, it looks for someone else.");
        return sb.ToString();
    }
}
