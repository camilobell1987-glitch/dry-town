namespace DryTown.Core;

/// <summary>Everything there is to know about one resident of the city, whether or not you know it yet.</summary>
public sealed record Citizen(
    int Id, string Name, bool Woman, int Age, Ethnicity Origin, string Job,
    int HomeLotId, int WorkBusinessId, int LandlordId, IReadOnlyList<int> Owns, int OwnsBusinessId, int Worth,
    IReadOnlyList<Tie> Ties);

/// <summary>How one resident is bound to another.</summary>
public enum Kin { Spouse, Child, Parent, Sibling, Employer, Lover }

/// <summary>What someone would rather nobody knew.</summary>
public enum Vice { Lover, Drink, Gambling, Debt, Thief, Wanted }

/// <summary>
/// One of someone's secrets. What it concerns depends on the vice: the lover, the place they drink or gamble, the
/// business they steal from (or -1 if they fence instead), or which crime and city they're wanted for.
/// </summary>
public readonly record struct Secret(Vice Vice, int Other);

/// <summary>Someone a resident is bound to, and how: his wife, his son, the man he works for.</summary>
public readonly record struct Tie(int Id, Kin Kin);

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
    private readonly Dictionary<int, IReadOnlyList<Secret>> _secrets = new();

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
                // Whoever it lands on, the building belongs to the head of his household.
                int n = (int)(Roll(ward.Key, 80 + k) * _blockPopulation[b]);
                int id = b * PerBlock + HouseholdAt(b, n).Start;
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
        var (start, size) = HouseholdAt(block, n);
        int place = n - start, headId = block * PerBlock + start;
        var head = place == 0 ? null : Get(headId);
        bool married = size >= 2 && Roll(headId, 301) < 0.8;
        // The head's family are his wife and children if he's old enough to have them, else his brothers and sisters.
        var kin = place == 0 ? Kin.Spouse : place == 1 && married ? Kin.Spouse : head!.Age >= 32 ? Kin.Child : Kin.Sibling;

        // Most are of the neighbourhood's people; the rest came from anywhere. A family are all one people.
        var local = _w.Neighbourhood(home.WardId);
        var origin = head?.Origin ?? (local != Ethnicity.Unknown && Roll(id, 1) < 0.75 ? local : Content.Peoples[(int)(Roll(id, 2) * Content.Peoples.Length)]);
        bool woman = place == 1 && married ? !head!.Woman : Roll(id, 3) < 0.5;
        int age = 16 + (int)(Math.Pow(Roll(id, 4), 1.4) * 60);
        if (head == null) age = Math.Max(age, size >= 3 ? 34 : size == 2 ? 20 : 16);
        else if (kin == Kin.Spouse) age = Math.Clamp(head.Age + (int)(Roll(id, 302) * 10) - (woman ? 6 : 2), 18, 90);
        else if (kin == Kin.Child) age = Math.Max(1, head.Age - 20 - (int)(Roll(id, 303) * 22));
        else age = Math.Clamp(head.Age + (int)(Roll(id, 304) * 12) - 6, 16, 90);
        var names = woman ? WomenOf(origin) : Content.FirstNamesOf.GetValueOrDefault(origin) ?? Content.FirstNames;
        string first = Pick(names, id, 5);
        // Nobody names a child after his brother, and few after his father.
        var taken = Enumerable.Range(0, place).Select(k => Get(block * PerBlock + start + k).Name.Split(' ')[0]).ToList();
        for (int salt = 14; salt < 24 && taken.Contains(first); salt++) first = Pick(names, id, salt);
        string last = head?.Name[(head.Name.IndexOf(' ') + 1)..] ?? Pick(Content.LastNamesOf.GetValueOrDefault(origin) ?? Content.LastNames, id, 6);

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
            if (size < 3) age = Math.Max(age, 30 + (int)(Roll(id, 7) * 30));
        }
        else if (buildings.Count > 0)
        {
            job = buildings.Count == 1 ? "Landlord of a tenement" : $"Landlord of {buildings.Count} tenements";
            if (size < 3) age = Math.Max(age, 35 + (int)(Roll(id, 7) * 30));
        }
        else if (age < 14) job = age < 6 ? "Too young for school" : "At school";
        else if (age >= 65 && Roll(id, 8) < 0.7) job = "Retired";
        else if (_businessesInWard.TryGetValue(home.WardId, out var nearby) && Roll(id, 9) < 0.3)
        {
            work = nearby[(int)(Roll(id, 10) * nearby.Count)];
            var biz = _w.BusinessById(work);
            job = $"{(woman ? WomanAt(biz.Kind, id) : ManAt(biz.Kind, id))} at {Short(biz.Name)}";
        }
        else job = age < 19 ? (woman ? "Shop girl" : "Newsboy") : Pick(woman ? WomensTrades : MensTrades, id, 11);

        // What he's good for in a week, which sets what a bribe costs and what he can be shaken down or ransomed for.
        int worth = owns >= 0 ? Math.Max(15, _w.BusinessById(owns).Takings / 5)
            : buildings.Count > 0 ? 25 * buildings.Count
            : age < 14 ? 1
            : job is "Out of work" or "Newsboy" ? 2
            : job is "Retired" or "Keeps house" ? 5
            : 8 + (int)(Roll(id, 13) * 11);
        var ties = new List<Tie>();
        for (int k = 0; k < size; k++)
        {
            if (k == place) continue;
            int other = block * PerBlock + start + k;
            if (KinBetween(place, k, married, (head?.Age ?? age) >= 32) is Kin between) ties.Add(new Tie(other, between));
        }
        if (work >= 0 && OwnerOf(work) is int boss and >= 0 && boss != id) ties.Add(new Tie(boss, Kin.Employer));
        var citizen = new Citizen(id, $"{first} {last}", woman, age, origin, job, homeLot, work, LandlordOf(homeLot), buildings, owns, worth, ties);
        _cache[id] = citizen;
        return citizen;
    }

    /// <summary>
    /// What someone is hiding. About a third of grown-ups have something: a lover, a thirst, a habit at the tables,
    /// a debt, a hand in the till, or a warrant out for them somewhere else.
    /// </summary>
    public IReadOnlyList<Secret> SecretsOf(int id)
    {
        if (_secrets.TryGetValue(id, out var known)) return known;
        var c = Get(id);
        var secrets = new List<Secret>();
        if (c.Age >= 18 && Roll(id, 400) < 0.35)
        {
            int count = Roll(id, 401) < 0.25 ? 2 : 1;
            var ward = _w.Map.LotAt(c.HomeLotId).WardId;
            var nearby = _businessesInWard.GetValueOrDefault(ward);
            for (int k = 0; k < count; k++)
            {
                double r = Roll(id, 402 + k);
                var vice = r < 0.25 ? Vice.Lover : r < 0.5 ? Vice.Drink : r < 0.7 ? Vice.Gambling : r < 0.8 ? Vice.Debt : r < 0.9 ? Vice.Thief : Vice.Wanted;
                int other = vice switch
                {
                    Vice.Lover => LoverFor(c, k),
                    Vice.Drink or Vice.Gambling => nearby is { Count: > 0 } ? nearby[(int)(Roll(id, 410 + k) * nearby.Count)] : -1,
                    Vice.Thief => c.WorkBusinessId,
                    Vice.Wanted => (int)(Roll(id, 412 + k) * Content.WantedFor.Length) * 16 + (int)(Roll(id, 414 + k) * Content.WantedIn.Length),
                    _ => -1,
                };
                if (vice == Vice.Lover && other < 0 || (vice is Vice.Drink or Vice.Gambling) && other < 0) vice = Vice.Debt;
                if (secrets.Any(x => x.Vice == vice)) continue;
                secrets.Add(new Secret(vice, other));
            }
        }
        _secrets[id] = secrets;
        return secrets;
    }

    /// <summary>A grown-up of the other sex from another household on the same block, or -1.</summary>
    private int LoverFor(Citizen c, int k)
    {
        int block = c.Id / PerBlock, people = _blockPopulation[block];
        for (int tries = 0; tries < 8; tries++)
        {
            int other = block * PerBlock + (int)(Roll(c.Id, 420 + k * 8 + tries) * people);
            if (other == c.Id || c.Ties.Any(t => t.Id == other)) continue;
            var them = Get(other);
            if (them.Woman != c.Woman && them.Age >= 18 && Math.Abs(them.Age - c.Age) < 20) return other;
        }
        return -1;
    }

    /// <summary>
    /// How the k-th of a household is related to the one in place: the head comes first, then his wife if he has
    /// one, then his children if he's old enough to have them, or else his brothers and sisters.
    /// </summary>
    private static Kin? KinBetween(int place, int k, bool married, bool children)
    {
        int Role(int i) => i == 0 ? 0 : i == 1 && married ? 1 : 2;
        return (Role(place), Role(k)) switch
        {
            (0, 1) or (1, 0) => Kin.Spouse,
            (0, 2) or (1, 2) when children => Kin.Child,
            (2, 0) or (2, 1) when children => Kin.Parent,
            (0, 2) or (2, 0) => Kin.Sibling,
            (2, 2) => Kin.Sibling,
            _ => null,
        };
    }

    /// <summary>Where in his block's numbering a resident's household starts, and how many are in it.</summary>
    private (int Start, int Size) HouseholdAt(int block, int n)
    {
        int lotStart = 0;
        foreach (var (lot, people) in _homes[block])
        {
            if (n < lotStart + people)
            {
                // Each building is let out to households of one to five, in order from its first resident.
                int at = lotStart;
                for (int k = 0; ; k++)
                {
                    double r = Roll(lot * 64 + k, 300);
                    int size = Math.Min(lotStart + people - at, r < 0.2 ? 1 : r < 0.45 ? 2 : r < 0.7 ? 3 : r < 0.88 ? 4 : 5);
                    if (n < at + size) return (at, size);
                    at += size;
                }
            }
            lotStart += people;
        }
        return (n, 1);
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
