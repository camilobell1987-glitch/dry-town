namespace DryTown.Core;

/// <summary>Everything there is to know about one resident of the city, whether or not you know it yet.</summary>
public sealed record Citizen(
    int Id, string Name, bool Woman, int Age, Ethnicity Origin, string Job,
    int HomeLotId, int WorkBusinessId, int LandlordId, IReadOnlyList<int> Owns, int OwnsBusinessId);

/// <summary>
/// The city directory: every man, woman and child living in the city, each with a name, an age, a
/// job and a home. Nobody is stored until he's asked for; a resident is worked out the same way every
/// time from the city's seed, the block he lives on and his number there, so a hundred thousand of them
/// cost nothing until you take an interest in one.
/// </summary>
/// <remarks>
/// How many live in each building is the map's business, since only the map knows which empty lots
/// have tenements on them; it hands the directory a count per lot and which lots are tenements.
/// </remarks>
public sealed class CityDirectory
{
    /// <summary>Room for this many residents on one block, so (block, number) packs into one id.</summary>
    public const int PerBlock = 4096;

    private readonly World _w;
    private readonly ulong _seed;
    private readonly int[] _blockPopulation;
    // Each block's lots in order, with how many live in each, so a resident's number finds his building.
    private readonly (int LotId, int People)[][] _homes;
    private readonly Dictionary<int, int> _landlordOf = new();
    private readonly Dictionary<int, List<int>> _ownedBy = new();
    // A business's owner lives over the shop: the first resident of its lot.
    private readonly Dictionary<int, int> _ownerOf = new();
    private readonly Dictionary<int, int> _businessOfOwner = new();
    private readonly Dictionary<int, List<int>> _businessesInWard = new();
    private readonly Dictionary<int, Citizen> _cache = new();

    public CityDirectory(World w, Func<Lot, int> population, Func<Lot, bool> tenement)
    {
        _w = w;
        _seed = w.Settings.Seed;
        var map = w.Map;
        int blocks = map.BlocksX * map.BlocksY;
        _blockPopulation = new int[blocks];
        var homes = new List<(int, int)>[blocks];
        for (int i = 0; i < blocks; i++) homes[i] = new();
        foreach (var lot in map.Lots)
        {
            int b = BlockOf(lot), people = Math.Min(population(lot), PerBlock - _blockPopulation[b]);
            if (people <= 0) continue;
            homes[b].Add((lot.Id, people));
            _blockPopulation[b] += people;
        }
        _homes = homes.Select(h => h.ToArray()).ToArray();

        foreach (var biz in w.Businesses)
        {
            var lot = w.LotOf(biz);
            if (!_businessesInWard.TryGetValue(lot.WardId, out var list)) _businessesInWard[lot.WardId] = list = new();
            list.Add(biz.Id);
            if (FirstResidentOf(lot) is int owner and >= 0 && !_businessOfOwner.ContainsKey(owner))
            {
                _ownerOf[biz.Id] = owner;
                _businessOfOwner[owner] = biz.Id;
            }
        }

        // The tenements in each ward belong to a few landlords, who live in the ward and own several buildings each.
        foreach (var ward in map.Lots.Where(tenement).GroupBy(l => l.WardId))
        {
            var buildings = ward.OrderBy(l => l.Id).ToList();
            int owners = Math.Max(1, buildings.Count / 3);
            var pool = new List<int>();
            for (int k = 0; pool.Count < owners && k < owners * 8; k++)
            {
                var home = buildings[(int)(Roll(ward.Key, 50 + k) * buildings.Count)];
                int b = BlockOf(home);
                if (_blockPopulation[b] == 0) continue;
                int id = b * PerBlock + (int)(Roll(ward.Key, 80 + k) * _blockPopulation[b]);
                if (!pool.Contains(id) && !_businessOfOwner.ContainsKey(id)) pool.Add(id);
            }
            if (pool.Count == 0) continue;
            foreach (var building in buildings)
            {
                int landlord = pool[(int)(Roll(building.Id, 60) * pool.Count)];
                _landlordOf[building.Id] = landlord;
                if (!_ownedBy.TryGetValue(landlord, out var owned)) _ownedBy[landlord] = owned = new();
                owned.Add(building.Id);
            }
        }
    }

    /// <summary>How many live in the city.</summary>
    public int Population => _blockPopulation.Sum();

    /// <summary>How many live on a block.</summary>
    public int CountOn(int bx, int by) => _blockPopulation[by * _w.Map.BlocksX + bx];

    public int IdOf(int bx, int by, int n) => (by * _w.Map.BlocksX + bx) * PerBlock + n;

    public (int Bx, int By, int N) Where(int id)
    {
        int b = id / PerBlock;
        return (b % _w.Map.BlocksX, b / _w.Map.BlocksX, id % PerBlock);
    }

    /// <summary>Whether an id is someone who lives in the city.</summary>
    public bool Exists(int id) => id >= 0 && id / PerBlock < _blockPopulation.Length && id % PerBlock < _blockPopulation[id / PerBlock];

    /// <summary>The landlord of a tenement, if it's let.</summary>
    public int LandlordOf(int lotId) => _landlordOf.GetValueOrDefault(lotId, -1);

    /// <summary>The man who owns a business and lives over it.</summary>
    public int OwnerOf(int businessId) => _ownerOf.GetValueOrDefault(businessId, -1);

    /// <summary>Look someone up.</summary>
    public Citizen Get(int id)
    {
        if (_cache.TryGetValue(id, out var known)) return known;
        var (bx, by, n) = Where(id);
        int block = id / PerBlock;
        int homeLot = HomeOf(block, n);
        var home = _w.Map.LotAt(homeLot);

        // Most are of the neighbourhood's people; the rest came from anywhere.
        var local = _w.Neighbourhood(home.WardId);
        var origin = local != Ethnicity.Unknown && Roll(id, 1) < 0.75 ? local : Content.Peoples[(int)(Roll(id, 2) * Content.Peoples.Length)];
        bool woman = Roll(id, 3) < 0.5;
        int age = 16 + (int)(Math.Pow(Roll(id, 4), 1.4) * 60);
        string first = Pick(woman ? WomenOf(origin) : Content.FirstNamesOf.GetValueOrDefault(origin) ?? Content.FirstNames, id, 5);
        string last = Pick(Content.LastNamesOf.GetValueOrDefault(origin) ?? Content.LastNames, id, 6);

        int owns = _businessOfOwner.GetValueOrDefault(id, -1);
        var buildings = _ownedBy.GetValueOrDefault(id) ?? (IReadOnlyList<int>)Array.Empty<int>();
        int work = -1;
        string job;
        if (owns >= 0)
        {
            var biz = _w.BusinessById(owns);
            // Whoever owns "Weiss's Hotel" is a Weiss.
            int mark = biz.Name.IndexOf("'s ", StringComparison.Ordinal);
            if (mark > 0 && !biz.Name[..mark].Contains(' ')) last = biz.Name[..mark];
            job = $"Owns {Short(biz.Name)}";
            age = Math.Max(age, 30 + (int)(Roll(id, 7) * 30));
        }
        else if (buildings.Count > 0)
        {
            job = buildings.Count == 1 ? "Landlord of a tenement" : $"Landlord of {buildings.Count} tenements";
            age = Math.Max(age, 35 + (int)(Roll(id, 7) * 30));
        }
        else if (age >= 65 && Roll(id, 8) < 0.7) job = "Retired";
        else if (_businessesInWard.TryGetValue(home.WardId, out var nearby) && Roll(id, 9) < 0.3)
        {
            work = nearby[(int)(Roll(id, 10) * nearby.Count)];
            var biz = _w.BusinessById(work);
            job = $"{(woman ? WomanAt(biz.Kind, id) : ManAt(biz.Kind, id))} at {Short(biz.Name)}";
        }
        else job = age < 19 ? (woman ? "Shop girl" : "Newsboy") : Pick(woman ? WomensTrades : MensTrades, id, 11);

        var citizen = new Citizen(id, $"{first} {last}", woman, age, origin, job, homeLot, work, LandlordOf(homeLot), buildings, owns);
        _cache[id] = citizen;
        return citizen;
    }

    /// <summary>A business's name without the street it's on.</summary>
    private static string Short(string name) => name.IndexOf(", ", StringComparison.Ordinal) is int comma and > 0 ? name[..comma] : name;

    private int BlockOf(Lot lot) => (lot.Y - 1) / CityMap.StrideY * _w.Map.BlocksX + (lot.X - 1) / CityMap.StrideX;

    private int HomeOf(int block, int n)
    {
        foreach (var (lot, people) in _homes[block])
        {
            if (n < people) return lot;
            n -= people;
        }
        return _homes[block].Length > 0 ? _homes[block][^1].LotId : -1;
    }

    private int FirstResidentOf(Lot lot)
    {
        int b = BlockOf(lot), n = 0;
        foreach (var (id, people) in _homes[b])
        {
            if (id == lot.Id) return b * PerBlock + n;
            n += people;
        }
        return -1;
    }

    /// <summary>A stable number in [0, 1) for a resident and a question about him, the same in every game on this map.</summary>
    private double Roll(int id, int salt)
    {
        ulong h = _seed * 0x9E3779B97F4A7C15ul ^ (ulong)(uint)id * 0xBF58476D1CE4E5B9ul ^ (ulong)(uint)salt * 0x94D049BB133111EBul;
        h ^= h >> 31;
        h *= 0xD6E8FEB86659FD93ul;
        h ^= h >> 32;
        return (h >> 11) * (1.0 / (1ul << 53));
    }

    private string Pick(string[] from, int id, int salt) => from[(int)(Roll(id, salt) * from.Length)];

    private string ManAt(BusinessKind kind, int id) => kind switch
    {
        BusinessKind.Diner => Roll(id, 12) < 0.5 ? "Cook" : "Counterman",
        BusinessKind.Barber => "Barber",
        BusinessKind.Tailor => "Cutter",
        BusinessKind.Garage => "Mechanic",
        BusinessKind.Hotel => "Bellhop",
        BusinessKind.Pharmacy => "Soda jerk",
        BusinessKind.PoolHall => "Rack boy",
        BusinessKind.Warehouse => "Loader",
        BusinessKind.Laundry => "Presser",
        _ => "Clerk",
    };

    private string WomanAt(BusinessKind kind, int id) => kind switch
    {
        BusinessKind.Diner => "Waitress",
        BusinessKind.Tailor => "Seamstress",
        BusinessKind.Hotel => Roll(id, 12) < 0.5 ? "Chambermaid" : "Switchboard girl",
        BusinessKind.Laundry => "Laundress",
        BusinessKind.Warehouse or BusinessKind.Garage => "Bookkeeper",
        _ => "Shop girl",
    };

    private static readonly string[] MensTrades =
    {
        "Longshoreman", "Teamster", "Bricklayer", "Factory hand", "Streetcar motorman", "Iceman", "Milkman",
        "Peddler", "Carpenter", "Butcher's boy", "Ironworker", "Bank clerk", "Insurance man", "Schoolteacher",
        "Fireman", "Railroad man", "Out of work", "Out of work", "Night watchman", "Plumber",
    };

    private static readonly string[] WomensTrades =
    {
        "Keeps house", "Keeps house", "Keeps house", "Seamstress", "Garment worker", "Typist", "Telephone operator",
        "Schoolteacher", "Nurse", "Takes in washing", "Cigar roller", "Shop girl",
    };

    private static string[] WomenOf(Ethnicity e) => e switch
    {
        Ethnicity.Irish => new[] { "Mary", "Bridget", "Nora", "Kathleen", "Peggy", "Maureen", "Annie", "Rose", "Sheila", "Molly" },
        Ethnicity.Italian => new[] { "Maria", "Rosa", "Concetta", "Angela", "Lucia", "Carmela", "Teresa", "Josephine", "Filomena", "Anna" },
        Ethnicity.Jewish => new[] { "Sadie", "Rose", "Esther", "Bessie", "Ida", "Fanny", "Sarah", "Minnie", "Goldie", "Rebecca" },
        Ethnicity.Polish => new[] { "Helen", "Stella", "Sophie", "Agnes", "Wanda", "Jadwiga", "Mary", "Irene", "Josephine", "Celia" },
        Ethnicity.German => new[] { "Gertrude", "Hilda", "Elsie", "Frieda", "Martha", "Emma", "Bertha", "Clara", "Louise", "Greta" },
        _ => new[] { "Mary", "Helen", "Ruth", "Dorothy", "Margaret", "Alice" },
    };
}

/// <summary>The things you can find out about a resident. Until you do, his card shows question marks.</summary>
[Flags]
public enum Fact
{
    None = 0,
    Name = 1,
    Age = 2,
    Origin = 4,
    Job = 8,
    Work = 16,
    Home = 32,
    Landlord = 64,
    Owns = 128,
    All = Name | Age | Origin | Job | Work | Home | Landlord | Owns,
}
