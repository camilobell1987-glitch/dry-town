namespace DryTown.Core;

public sealed class WorldSettings
{
    public ulong Seed { get; init; } = 1;
    public int Businesses { get; init; } = 48;
    public int StartingGangs { get; init; } = 3;
    public int StartingHoods { get; init; } = 4;
    public long StartingCash { get; init; } = 1500;

    /// <summary>When true, the rival director seeds new gangs and splits when the district goes quiet.</summary>
    public bool DirectorEnabled { get; init; } = true;
}

/// <summary>The whole simulation state for one district. Pure data plus id lookups.</summary>
public sealed class World
{
    public WorldSettings Settings { get; }
    public Rng Rng { get; }
    public int Week { get; set; }

    public List<Gang> Gangs { get; } = new();
    public List<Hood> Hoods { get; } = new();
    public List<Business> Businesses { get; } = new();
    public List<GameEvent> Events { get; } = new();

    /// <summary>This week's books per gang, reset at the start of each week.</summary>
    public Dictionary<int, WeekLedger> LastLedger { get; } = new();

    public WeekLedger Ledger(int gangId)
    {
        if (!LastLedger.TryGetValue(gangId, out var ledger)) LastLedger[gangId] = ledger = new WeekLedger();
        return ledger;
    }

    private int _nextHoodId;
    private int _nextGangId;

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

    public void Log(EventKind kind, int gangId, string text) => Events.Add(new GameEvent(Week, kind, gangId, text));

    public static World Create(WorldSettings settings)
    {
        var world = new World(settings);
        world.GenerateBusinesses();
        for (int i = 0; i < settings.StartingGangs; i++)
            world.FoundGang(isPlayer: i == 0, settings.StartingCash, settings.StartingHoods);
        return world;
    }

    private void GenerateBusinesses()
    {
        var kinds = Enum.GetValues<BusinessKind>();
        for (int i = 0; i < Settings.Businesses; i++)
        {
            var kind = kinds[Rng.Range(0, kinds.Length - 1)];
            var (min, max) = Content.TakingsFor(kind);
            string owner = Rng.Pick(Content.LastNames);
            string street = Rng.Pick(Content.StreetNames);
            Businesses.Add(new Business
            {
                Id = i,
                Name = $"{owner}'s {Content.Label(kind)}, {street} St",
                Kind = kind,
                Takings = Rng.Range(min, max),
                Toughness = Rng.Range(2, 9),
            });
        }
    }

    public Gang FoundGang(bool isPlayer, long cash, int hoods, Hood? boss = null)
    {
        var gang = new Gang
        {
            Id = _nextGangId++,
            Name = "",
            IsPlayer = isPlayer,
            FoundedWeek = Week,
            Cash = cash,
            Aggression = isPlayer ? 0.5 : 0.25 + Rng.NextDouble() * 0.6,
        };
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

    public static string Surname(string fullName) => fullName.Split(' ').Last();
}
