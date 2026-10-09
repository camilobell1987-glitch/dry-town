using System.Text.Json;

namespace DryTown.Core;

/// <summary>Everything needed to carry on a city exactly where it was left: the world, the dice, and the orders on the desk.</summary>
public sealed class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTime SavedAt { get; set; }
    public string Label { get; set; } = "";

    public required WorldSettings Settings { get; set; }
    public ulong RngState { get; set; }
    public int Week { get; set; }
    public int NextHoodId { get; set; }
    public int NextGangId { get; set; }
    public int NextCrewId { get; set; }

    public string[] StreetNames { get; set; } = Array.Empty<string>();
    public string[] AvenueNames { get; set; } = Array.Empty<string>();
    public List<Lot> Lots { get; set; } = new();
    public List<Gang> Gangs { get; set; } = new();
    public List<Hood> Hoods { get; set; } = new();
    public List<Business> Businesses { get; set; } = new();
    public List<Crew> Crews { get; set; } = new();
    public List<GameEvent> Events { get; set; } = new();
    public Dictionary<int, WeekLedger> LastLedger { get; set; } = new();

    public int DirectorDominantWeeks { get; set; }
    public int DirectorQuietWeeks { get; set; }

    public List<Order> Pending { get; set; } = new();
}

/// <summary>Writes and reads saved games as JSON. File handling is left to the front end.</summary>
public static class SaveGame
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Write(Simulation sim, IEnumerable<Order> pending)
    {
        if (sim.WeekRunning) throw new InvalidOperationException("Can't save in the middle of a week.");
        var data = sim.World.ToSave();
        data.SavedAt = DateTime.Now;
        data.Label = $"{sim.World.Player.Name}, {Reports.Date(sim.World)}";
        (data.DirectorDominantWeeks, data.DirectorQuietWeeks) = sim.Director.State;
        data.Pending = pending.ToList();
        return JsonSerializer.Serialize(data, Options);
    }

    public static SaveData Read(string json)
    {
        var data = JsonSerializer.Deserialize<SaveData>(json, Options) ?? throw new InvalidDataException("Empty save file.");
        if (data.Version > SaveData.CurrentVersion) throw new InvalidDataException("This save is from a newer version of the game.");
        return data;
    }

    /// <summary>Rebuild a running simulation from saved data.</summary>
    public static Simulation Restore(SaveData data)
    {
        var sim = new Simulation(World.FromSave(data));
        sim.Director.State = (data.DirectorDominantWeeks, data.DirectorQuietWeeks);
        return sim;
    }
}
