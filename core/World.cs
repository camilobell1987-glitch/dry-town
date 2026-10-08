namespace DryTown.Core;

public enum Difficulty { Easy, Normal, Hard }

public sealed class WorldSettings
{
    public ulong Seed { get; init; } = 1;
    public int Businesses { get; init; } = 48;
    public int StartingGangs { get; init; } = 3;
    public int StartingHoods { get; init; } = 4;
    public long StartingCash { get; init; } = 1500;
    public Difficulty Difficulty { get; init; } = Difficulty.Normal;

    /// <summary>The player starts ahead of the rivals on Easy and Normal; on Hard, level with them.</summary>
    public int PlayerStartingHoods => Difficulty switch { Difficulty.Easy => StartingHoods + 3, Difficulty.Normal => StartingHoods + 2, _ => StartingHoods };
    public long PlayerStartingCash => Difficulty switch { Difficulty.Easy => StartingCash * 3, Difficulty.Normal => StartingCash * 2, _ => StartingCash };

    /// <summary>How hard rivals push into the player's turf while they size up a newcomer, by year of play.</summary>
    public double RivalWariness(int yearsPlayed) => Difficulty switch
    {
        Difficulty.Hard => 1.0,
        Difficulty.Normal => yearsPlayed < 1 ? 0.35 : yearsPlayed < 2 ? 0.7 : 1.0,
        _ => yearsPlayed < 2 ? 0.3 : 0.7,
    };

    /// <summary>When true, the rival director seeds new gangs and splits when the district goes quiet.</summary>
    public bool DirectorEnabled { get; init; } = true;
}

/// <summary>The whole simulation state for one district. Pure data plus id lookups.</summary>
public sealed class World
{
    public WorldSettings Settings { get; }
    public Rng Rng { get; private set; }
    public int Week { get; set; }

    /// <summary>Hour of the current week, 0 to 167. Everything logged is stamped with it.</summary>
    public int Tick { get; set; }

    public CityMap Map { get; private set; } = new();

    /// <summary>What happened on the streets this week, in time order, for the live view.</summary>
    public List<ScriptAction> Script { get; } = new();

    public List<Gang> Gangs { get; } = new();
    public List<Hood> Hoods { get; } = new();
    public List<Business> Businesses { get; } = new();
    public List<GameEvent> Events { get; } = new();
    public List<Crew> Crews { get; } = new();

    /// <summary>This week's books per gang, reset at the start of each week.</summary>
    public Dictionary<int, WeekLedger> LastLedger { get; } = new();

    public WeekLedger Ledger(int gangId)
    {
        if (!LastLedger.TryGetValue(gangId, out var ledger)) LastLedger[gangId] = ledger = new WeekLedger();
        return ledger;
    }

    private int _nextHoodId;
    private int _nextGangId;
    private int _nextCrewId;

    public World(WorldSettings settings)
    {
        Settings = settings;
        Rng = new Rng(settings.Seed);
    }

    public int Year => Content.StartYear + Week / Content.WeeksPerYear;
    public int WeekOfYear => Week % Content.WeeksPerYear + 1;
    public bool Prohibition => Year < Content.RepealYear;

    public Gang Player => Gangs.First(g => g.IsPlayer);

    public Gang GangById(int id) => Gangs.First(g => g.Id == id);
    public Hood HoodById(int id) => Hoods.First(h => h.Id == id);
    public Business BusinessById(int id) => Businesses.First(b => b.Id == id);

    public IEnumerable<Gang> LivingGangs => Gangs.Where(g => g.Alive);
    public IEnumerable<Hood> HoodsOf(int gangId) => Hoods.Where(h => h.GangId == gangId && h.IsActive);
    public IEnumerable<Hood> AvailableHoodsOf(int gangId) => Hoods.Where(h => h.GangId == gangId && h.IsAvailable);
    public IEnumerable<Business> TurfOf(int gangId) => Businesses.Where(b => b.ProtectorGangId == gangId);

    // ---- Crews ----------------------------------------------------------------

    public IEnumerable<Crew> CrewsOf(int gangId) => Crews.Where(c => c.GangId == gangId);
    public Crew? CrewById(int id) => Crews.FirstOrDefault(c => c.Id == id);
    public Crew? CrewOfHood(int hoodId) => Crews.FirstOrDefault(c => c.LieutenantHoodId == hoodId || c.MemberIds.Contains(hoodId));

    /// <summary>Make a hood the lieutenant of a new crew. He leaves any crew he was in.</summary>
    public Crew FormCrew(Hood lieutenant)
    {
        LeaveCrew(lieutenant.Id);
        var crew = new Crew { Id = _nextCrewId++, GangId = lieutenant.GangId, LieutenantHoodId = lieutenant.Id };
        Crews.Add(crew);
        return crew;
    }

    /// <summary>Put a hood under a lieutenant. Returns false if the crew is full or he's in another gang.</summary>
    public bool JoinCrew(Crew crew, Hood hood)
    {
        if (hood.GangId != crew.GangId || crew.MemberIds.Count >= Crew.MaxMembers || crew.LieutenantHoodId == hood.Id) return false;
        if (crew.MemberIds.Contains(hood.Id)) return true;
        LeaveCrew(hood.Id);
        crew.MemberIds.Add(hood.Id);
        return true;
    }

    /// <summary>Take a hood out of his crew. A lieutenant leaving breaks his crew up.</summary>
    public void LeaveCrew(int hoodId)
    {
        var crew = CrewOfHood(hoodId);
        if (crew == null) return;
        if (crew.LieutenantHoodId == hoodId) Crews.Remove(crew);
        else crew.MemberIds.Remove(hoodId);
    }

    /// <summary>Drop men who have died, gone or changed sides; a crew whose lieutenant is lost promotes its best man.</summary>
    public void TidyCrews()
    {
        foreach (var crew in Crews.ToList())
        {
            crew.MemberIds.RemoveAll(id => HoodById(id) is var h && (h.GangId != crew.GangId || !h.IsActive));
            var lt = HoodById(crew.LieutenantHoodId);
            if (lt.GangId == crew.GangId && lt.IsActive) continue;
            var next = crew.MemberIds.Select(HoodById).OrderByDescending(h => h.Brains + h.Strength).ThenBy(h => h.Id).FirstOrDefault();
            if (next == null) { Crews.Remove(crew); continue; }
            crew.MemberIds.Remove(next.Id);
            crew.LieutenantHoodId = next.Id;
        }
    }

    public void Log(EventKind kind, int gangId, string text) => Events.Add(new GameEvent(Week, kind, gangId, text, Tick));

    public void Act(ScriptAction action) => Script.Add(action with { Tick = Tick });

    public Lot LotOf(Business b) => Map.LotAt(b.LotId);
    public Lot HqOf(Gang g) => Map.LotAt(g.HqLotId);
    public Lot Precinct => Map.Lots.First(l => l.Use == LotUse.Precinct);

    /// <summary>Walking distance in blocks from a gang's headquarters to a business.</summary>
    public double BlocksFromHq(Gang g, Business b) => Map.Distance(HqOf(g), LotOf(b)) / (double)CityMap.StrideX;

    public static World Create(WorldSettings settings)
    {
        var world = new World(settings);
        world.GenerateBusinesses();
        for (int i = 0; i < settings.StartingGangs; i++)
        {
            bool player = i == 0;
            world.FoundGang(player,
                player ? settings.PlayerStartingCash : settings.StartingCash,
                player ? settings.PlayerStartingHoods : settings.StartingHoods);
        }
        return world;
    }

    private void GenerateBusinesses()
    {
        Map = CityMap.Generate(Rng);

        // The precinct house sits near the middle of the district.
        var precinct = Map.Lots.OrderBy(l => Math.Abs(l.X - CityMap.Width / 2) + Math.Abs(l.Y - CityMap.Height / 2)).ThenBy(l => l.Id).First();
        precinct.Use = LotUse.Precinct;

        var free = Map.Lots.Where(l => l.Use == LotUse.Empty).ToList();
        Rng.Shuffle(free);
        var kinds = Enum.GetValues<BusinessKind>();
        int count = Math.Min(Settings.Businesses, free.Count - 8);
        for (int i = 0; i < count; i++)
        {
            var lot = free[i];
            var kind = kinds[Rng.Range(0, kinds.Length - 1)];
            var (min, max) = Content.TakingsFor(kind);
            string owner = Rng.Pick(Content.LastNames);
            lot.Use = LotUse.Business;
            lot.BusinessId = i;
            Businesses.Add(new Business
            {
                Id = i,
                Name = $"{owner}'s {Content.Label(kind)}, {Map.StreetOf(lot)} St",
                Kind = kind,
                Takings = Rng.Range(min, max),
                Toughness = Rng.Range(2, 9),
                LotId = lot.Id,
            });
        }
    }

    /// <summary>A free lot as far as possible from every other gang's headquarters.</summary>
    private Lot PickHeadquarters()
    {
        var hqs = LivingGangs.Where(g => g.HqLotId >= 0).Select(g => Map.LotAt(g.HqLotId)).ToList();
        var free = Map.Lots.Where(l => l.Use == LotUse.Empty).ToList();
        if (hqs.Count == 0) return free[Rng.Range(0, free.Count - 1)];
        return free
            .OrderByDescending(l => hqs.Min(h => Map.Distance(h, l)))
            .ThenBy(l => l.Id)
            .First();
    }

    public void ReleaseHeadquarters(Gang gang)
    {
        if (gang.HqLotId < 0) return;
        var lot = Map.LotAt(gang.HqLotId);
        lot.Use = LotUse.Empty;
        lot.GangId = -1;
    }

    public Gang FoundGang(bool isPlayer, long cash, int hoods, Hood? boss = null, double? aggression = null)
    {
        var gang = new Gang
        {
            Id = _nextGangId++,
            Name = "",
            IsPlayer = isPlayer,
            FoundedWeek = Week,
            Cash = cash,
            Aggression = aggression ?? (isPlayer ? 0.5 : 0.25 + Rng.NextDouble() * 0.6),
        };
        var hq = PickHeadquarters();
        hq.Use = LotUse.Headquarters;
        hq.GangId = gang.Id;
        gang.HqLotId = hq.Id;
        Gangs.Add(gang);

        if (boss is null)
        {
            boss = NewHood(gang.Id, bossQuality: true);
        }
        else
        {
            boss.GangId = gang.Id;
            boss.Loyalty = 100;
        }
        gang.BossHoodId = boss.Id;
        gang.Name = string.Format(Rng.Pick(Content.GangPatterns), Surname(boss.Name));

        for (int i = 1; i < hoods; i++) NewHood(gang.Id, bossQuality: false);
        return gang;
    }

    public Hood NewHood(int gangId, bool bossQuality)
    {
        int lo = bossQuality ? 4 : 1, hi = bossQuality ? 9 : 8;
        string first = Rng.Pick(Content.FirstNames);
        string last = Rng.Pick(Content.LastNames);
        string name = Rng.Chance(0.3) ? $"{first} \"{Rng.Pick(Content.Nicknames)}\" {last}" : $"{first} {last}";
        var hood = new Hood
        {
            Id = _nextHoodId++,
            Name = name,
            GangId = gangId,
            Intimidation = Rng.Range(lo, hi),
            Muscle = Rng.Range(lo, hi),
            Brains = Rng.Range(lo, hi),
            Stealth = Rng.Range(lo, hi),
            Loyalty = bossQuality ? 100 : Rng.Range(40, 90),
            Ambition = Rng.Range(10, 90),
            JoinedWeek = Week,
        };
        hood.Wage = 14 + (hood.Strength + hood.Brains) * 2;
        Hoods.Add(hood);
        return hood;
    }

    // ---- Saving ---------------------------------------------------------------

    public SaveData ToSave() => new()
    {
        Settings = Settings,
        RngState = Rng.State,
        Week = Week,
        NextHoodId = _nextHoodId,
        NextGangId = _nextGangId,
        NextCrewId = _nextCrewId,
        StreetNames = Map.StreetNames,
        AvenueNames = Map.AvenueNames,
        Lots = Map.Lots,
        Gangs = Gangs,
        Hoods = Hoods,
        Businesses = Businesses,
        Crews = Crews,
        Events = Events,
        LastLedger = LastLedger,
    };

    public static World FromSave(SaveData d)
    {
        var w = new World(d.Settings) { Week = d.Week, _nextHoodId = d.NextHoodId, _nextGangId = d.NextGangId, _nextCrewId = d.NextCrewId };
        w.Rng = Rng.FromState(d.RngState);
        w.Map = CityMap.FromSave(d.Lots, d.StreetNames, d.AvenueNames);
        w.Gangs.AddRange(d.Gangs);
        w.Hoods.AddRange(d.Hoods);
        w.Businesses.AddRange(d.Businesses);
        w.Crews.AddRange(d.Crews);
        w.Events.AddRange(d.Events);
        foreach (var (id, ledger) in d.LastLedger) w.LastLedger[id] = ledger;
        return w;
    }

    public static string Surname(string fullName) => fullName.Split(' ').Last();
}
