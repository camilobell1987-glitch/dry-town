using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>Colours for the whole UI. Prohibition-era: soot, brick and newsprint, with the player in brass.</summary>
public static class Palette
{
    public static readonly Color Background = new("17161a");
    public static readonly Color Panel = new("211f23");
    public static readonly Color PanelRaised = new("2b282d");
    public static readonly Color Street = new("2e2c2b");
    public static readonly Color StreetLine = new("3d3a37");
    public static readonly Color Block = new("3a352f");
    public static readonly Color Lot = new("5a5246");
    public static readonly Color LotEmpty = new("44403a");
    public static readonly Color Ink = new("ece3cf");
    public static readonly Color InkQuiet = new("a89f8c");
    public static readonly Color Police = new("5b8def");
    public static readonly Color Good = new("6fbf73");
    public static readonly Color Bad = new("e05a4f");
    public static readonly Color Neutral = new("9a9286");

    public static readonly Color Player = new("e8b84a");

    private static readonly Color[] Rivals =
    {
        new("c8463c"), // brick
        new("3fa39a"), // teal
        new("9a76d1"), // violet
        new("e0803a"), // orange
        new("5d8fd6"), // steel
        new("8fae4a"), // olive
        new("d6609e"), // rose
        new("b4b8bf"), // silver
        new("a8693a"), // sienna
        new("6fcf9f"), // mint
        new("8a62b0"), // plum
        new("3f8a5a"), // forest
        new("6ab0c8"), // sky
        new("e3a3a8"), // blush
    };

    private static readonly Dictionary<int, int> Assigned = new();

    public static void Reset() => Assigned.Clear();

    /// <summary>A gang keeps its colour for life; a new gang takes the first colour no living rival is using.</summary>
    public static Color Gang(World w, int gangId)
    {
        if (gangId < 0) return Neutral;
        var gang = w.Gangs.FirstOrDefault(g => g.Id == gangId);
        if (gang == null) return Neutral;
        if (gang.IsPlayer) return Player;
        if (!Assigned.TryGetValue(gangId, out int index))
        {
            var inUse = w.LivingGangs.Where(g => g.Id != gangId && Assigned.ContainsKey(g.Id)).Select(g => Assigned[g.Id]).ToHashSet();
            index = Enumerable.Range(0, Rivals.Length).FirstOrDefault(i => !inUse.Contains(i), gangId % Rivals.Length);
            Assigned[gangId] = index;
        }
        return Rivals[index];
    }

    public static string Hex(Color c) => c.ToHtml(false);
}
