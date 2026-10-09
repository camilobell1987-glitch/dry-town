namespace DryTown.Core;

public enum Difficulty { Easy, Normal, Hard }

public enum CitySize { Small, Medium, Large }

public sealed class WorldSettings
{
    public ulong Seed { get; init; } = 1;

    /// <summary>Small is the first district of five blocks by four; Large is nine by seven with six wards.</summary>
    public CitySize Size { get; init; } = CitySize.Small;

    /// <summary>Businesses and starting gangs, or 0 to fit the city's size.</summary>
    public int Businesses { get; init; }
    public int StartingGangs { get; init; }

    public Content.CityShape Shape => Content.Shape(Size);
    public int BusinessCount => Businesses > 0 ? Businesses : Shape.Businesses;
    public int StartingGangCount => StartingGangs > 0 ? StartingGangs : Shape.StartingGangs;
    public int MaxGangs => Shape.MaxGangs;

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

    /// <summary>When false, nobody buys aldermen or backs candidates, and the mayor never changes. For comparison runs.</summary>
    public bool PoliticsEnabled { get; init; } = true;

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

    /// <summary>The city's wards and their aldermen, and the mayor's office.</summary>
    public List<Ward> Wards { get; } = new();
    public CityHall Hall { get; set; } = new() { Mayor = "" };

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

    /// <summary>The precinct house closest to a lot: the one whose men answer a call there.</summary>
    public Lot PrecinctNear(Lot lot) =>
        Map.Lots.Where(l => l.Use == LotUse.Precinct).OrderBy(l => Map.Distance(l, lot)).ThenBy(l => l.Id).First();

    /// <summary>Walking distance in blocks from a gang's headquarters to a business.</summary>
    public double BlocksFromHq(Gang g, Business b) => Map.Distance(HqOf(g), LotOf(b)) / (double)CityMap.StrideX;

    public static World Create(WorldSettings settings)
    {
        var world = new World(settings);
        world.GenerateBusinesses();
        Politics.Found(world);
        for (int i = 0; i < settings.StartingGangCount; i++)
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
        var shape = Settings.Shape;
        Map = CityMap.Generate(Rng, shape.BlocksX, shape.BlocksY);
        Map.DrawWards(shape.WardsX, shape.WardsY);

        // Precinct houses sit evenly across the city, the first near the middle.
        for (int p = 0; p < shape.Precincts; p++)
        {
            float fx = shape.Precincts == 1 ? 0.5f : (p + 0.5f) / shape.Precincts;
            float fy = shape.Precincts == 1 ? 0.5f : p % 2 == 0 ? 0.35f : 0.65f;
            int cx = (int)(Map.Width * fx), cy = (int)(Map.Height * fy);
            var precinct = Map.Lots.Where(l => l.Use == LotUse.Empty)
                .OrderBy(l => Math.Abs(l.X - cx) + Math.Abs(l.Y - cy)).ThenBy(l => l.Id).First();
            precinct.Use = LotUse.Precinct;
        }

        var free = Map.Lots.Where(l => l.Use == LotUse.Empty).ToList();
        Rng.Shuffle(free);
        var kinds = Enum.GetValues<BusinessKind>();
        int count = Math.Min(Settings.BusinessCount, free.Count - 8 - 2 * shape.MaxGangs);
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
        // Two outfits with one name would confuse the papers: try the other patterns, then the boss's first name.
        string surname = Surname(boss.Name);
        var taken = LivingGangs.Where(g => g.Id != gang.Id).Select(g => g.Name).ToHashSet();
        var patterns = Content.GangPatterns.OrderBy(_ => Rng.NextDouble()).ToList();
        gang.Name = patterns.Select(p => string.Format(p, surname)).FirstOrDefault(n => !taken.Contains(n))
                    ?? string.Format(patterns[0], boss.Name.Split(' ')[0] + " " + surname);

        for (int i = 1; i < hoods; i++) NewHood(gang.Id, bossQuality: false);
        return gang;
    }

    public Hood NewHood(int gangId, bool bossQuality)
    {
        int lo = bossQuality ? 4 : 1, hi = bossQuality ? 9 : 8;
        int age = bossQuality ? Rng.Range(32, 50) : Rng.Range(18, 40);
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
            BornWeek = Week - age * Content.WeeksPerYear - Rng.Range(0, Content.WeeksPerYear - 1),
        };
        hood.Wage = 14 + (hood.Strength + hood.Brains) * 2;
        Hoods.Add(hood);
        return hood;
    }

    /// <summary>A son or nephew of the boss: young, green, and loyal to the family.</summary>
    public Hood NewRelative(Gang gang)
    {
        var boss = HoodById(gang.BossHoodId);
        string first = Rng.Pick(Content.FirstNames);
        string relation = Rng.Chance(0.5) ? "son" : "nephew";
        var hood = new Hood
        {
            Id = _nextHoodId++,
            Name = $"{first} {Surname(boss.Name)}",
            GangId = gang.Id,
            Intimidation = Rng.Range(1, 5),
            Muscle = Rng.Range(2, 6),
            Brains = Rng.Range(3, 7),
            Stealth = Rng.Range(2, 6),
            Loyalty = 95,
            Ambition = Rng.Range(20, 60),
            JoinedWeek = Week,
            BornWeek = Week - Rng.Range(17, 22) * Content.WeeksPerYear - Rng.Range(0, Content.WeeksPerYear - 1),
            Family = true,
        };
        hood.Wage = 14 + (hood.Strength + hood.Brains) * 2;
        Hoods.Add(hood);
        Log(EventKind.Family, gang.Id, $"{boss.Name} brought his {relation} {first} into {gang.Name}.");
        return hood;
    }

    /// <summary>
    /// Name the man who takes over. The rest of the outfit notices: the most ambitious man
    /// passed over takes it badly.
    /// </summary>
    public void NameHeir(Gang gang, Hood heir, bool announce = true)
    {
        if (heir.GangId != gang.Id || !heir.IsActive || heir.Id == gang.BossHoodId || gang.HeirHoodId == heir.Id) return;
        gang.HeirHoodId = heir.Id;
        heir.Loyalty = Math.Max(heir.Loyalty, 70);
        var passedOver = HoodsOf(gang.Id)
            .Where(h => h.Id != heir.Id && h.Id != gang.BossHoodId && !h.Family && h.Ambition > 60)
            .OrderByDescending(h => h.Ambition).ThenBy(h => h.Id).FirstOrDefault();
        if (passedOver != null) passedOver.Loyalty = Math.Max(0, passedOver.Loyalty - 10);
        if (announce) Log(EventKind.Heir, gang.Id, $"{HoodById(gang.BossHoodId).Name} named {heir.Name} to take over {gang.Name} after him.");
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
        BlocksX = Map.BlocksX,
        BlocksY = Map.BlocksY,
        WardsX = Map.WardsX,
        WardsY = Map.WardsY,
        Wards = Wards,
        Hall = Hall,
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
        w.Map = CityMap.FromSave(d.Lots, d.StreetNames, d.AvenueNames, d.BlocksX, d.BlocksY, d.WardsX, d.WardsY);
        w.Wards.AddRange(d.Wards);
        if (d.Hall != null) w.Hall = d.Hall;
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
