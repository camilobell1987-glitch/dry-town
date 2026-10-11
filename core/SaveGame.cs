using System.Text.Json;

namespace DryTown.Core;

/// <summary>Everything needed to carry on a city exactly where it was left: the world, the dice, and the orders on the desk.</summary>
public sealed class SaveData
{
    /// <summary>2 added ages, heirs and family. 3 added city sizes, wards and City Hall. 4 added people on the street and banks. 5 added guns, cars and the shops that sell them. 6 added fronts and clean money. 7 added trials and lawyers. 8 added grudges, pacts and proposals. 9 added rap sheets. 10 added vote counts, candidates and the district attorney. 11 added city events. 12 added rats, informers and planted men. 13 added supply lines and cut liquor. 14 added wars.</summary>
    public const int CurrentVersion = 14;

    public int Version { get; set; } = CurrentVersion;
    public DateTime SavedAt { get; set; }
    public string Label { get; set; } = "";

    public required WorldSettings Settings { get; set; }
    public ulong RngState { get; set; }
    public int Week { get; set; }
    public int NextHoodId { get; set; }
    public int NextGangId { get; set; }
    public int NextCrewId { get; set; }
    public int NextPersonId { get; set; }

    public string[] StreetNames { get; set; } = Array.Empty<string>();
    public string[] AvenueNames { get; set; } = Array.Empty<string>();
    public int BlocksX { get; set; } = 5;
    public int BlocksY { get; set; } = 4;
    public int WardsX { get; set; } = 1;
    public int WardsY { get; set; } = 1;
    public List<Lot> Lots { get; set; } = new();
    public List<Ward> Wards { get; set; } = new();
    public CityHall? Hall { get; set; }
    public List<Gang> Gangs { get; set; } = new();
    public List<Hood> Hoods { get; set; } = new();
    public List<Business> Businesses { get; set; } = new();
    public List<Crew> Crews { get; set; } = new();
    public List<StreetPerson> People { get; set; } = new();
    public List<Bank> Banks { get; set; } = new();
    public List<SupplyLine> SupplyLines { get; set; } = new();
    public List<GameEvent> Events { get; set; } = new();
    public Dictionary<int, WeekLedger> LastLedger { get; set; } = new();
    public List<Pact> Pacts { get; set; } = new();
    public List<War> Wars { get; set; } = new();
    public List<Grudge> Grudges { get; set; } = new();
    public List<Offer> Offers { get; set; } = new();
    public List<CityEvent> CityEvents { get; set; } = new();
    public Dictionary<CityEventKind, int> CityEventLast { get; set; } = new();

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
        if (data.Version < 2) GiveAges(data);
        if (data.Version < 3) GiveWards(data);
        if (data.Hall is { DA: "" } hall) hall.DA = Politics.NewPolitician(new Rng(data.Settings.Seed ^ 0xDA), false);
        data.Version = SaveData.CurrentVersion;
        return data;
    }

    /// <summary>Saves from before men had ages: give everyone a plausible one, bosses older.</summary>
    private static void GiveAges(SaveData data)
    {
        var bosses = data.Gangs.Select(g => g.BossHoodId).ToHashSet();
        foreach (var h in data.Hoods)
        {
            int age = bosses.Contains(h.Id) ? 38 + h.Id % 12 : 20 + h.Id * 7 % 20;
            h.BornWeek = data.Week - age * Content.WeeksPerYear - h.Id * 11 % Content.WeeksPerYear;
        }
    }

    /// <summary>Saves from before politics: split the old district into two wards and seat party men in them.</summary>
    private static void GiveWards(SaveData data)
    {
        data.WardsX = 2;
        data.WardsY = 1;
        var map = CityMap.FromSave(data.Lots, data.StreetNames, data.AvenueNames, data.BlocksX, data.BlocksY, 1, 1);
        map.DrawWards(data.WardsX, data.WardsY);
        var rng = new Rng(data.Settings.Seed ^ 0x5157);
        var names = Content.WardNames.ToList();
        rng.Shuffle(names);
        data.Wards = Enumerable.Range(0, map.WardCount)
            .Select(i => new Ward { Id = i, Name = names[i], Alderman = Politics.NewPolitician(rng, false) }).ToList();
        data.Hall = new CityHall { Mayor = Politics.NewPolitician(rng, false) };
    }

    /// <summary>Rebuild a running simulation from saved data.</summary>
    public static Simulation Restore(SaveData data)
    {
        var sim = new Simulation(World.FromSave(data));
        sim.Director.State = (data.DirectorDominantWeeks, data.DirectorQuietWeeks);
        return sim;
    }
}
