namespace DryTown.Core;

/// <summary>What stands on a lot. <see cref="Yard"/> is the back lot of a big scrapyard, its <see cref="Lot.BusinessId"/> the yard's.</summary>
public enum LotUse { Empty, Business, Headquarters, Precinct, Bank, GunShop, CarDealer, Yard }

/// <summary>
/// What the ground under a lot is. Only land can be built on; the river, the parks, the graveyards
/// the churches and the city's own buildings downtown stay as they are.
/// </summary>
public enum Terrain { Land, Water, Park, Cemetery, Church, Civic }

/// <summary>A building plot. Every lot faces one street row, above or below its block.</summary>
public sealed class Lot
{
    public int Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>The y of the street row this lot's door opens onto.</summary>
    public int FrontY { get; init; }

    public LotUse Use { get; set; }
    public int BusinessId { get; set; } = -1;

    /// <summary>
    /// A row of shops sharing the ground floor, west to east, when there's more than one; empty when one business
    /// has the whole building. <see cref="BusinessId"/> is the first of them.
    /// </summary>
    public List<int> Shops { get; set; } = new();
    public int GangId { get; set; } = -1;

    /// <summary>The ward the lot votes in.</summary>
    public int WardId { get; set; }

    public Terrain Terrain { get; set; }

    /// <summary>
    /// Part of a tenement block built round a court: the buildings stand back from a yard in the middle,
    /// reached through a passage from the street.
    /// </summary>
    public bool Court { get; set; }

    /// <summary>Empty land, where a business, a headquarters or a precinct house can go.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFree => Use == LotUse.Empty && Terrain == Terrain.Land;
}

/// <summary>
/// The district as a tile grid: rectangular blocks of lots separated by streets.
/// Horizontal street rows run the full width and vertical avenues the full height,
/// so any two doors are joined by an L or Z shaped walk along the streets.
/// </summary>
public sealed partial class CityMap
{
    public const int LotsPerBlockX = 4, LotsPerBlockY = 2;
    public const int StrideX = LotsPerBlockX + 1, StrideY = LotsPerBlockY + 1;

    /// <summary>Blocks across and down. The first district was five by four.</summary>
    public int BlocksX { get; }
    public int BlocksY { get; }
    public int Width => BlocksX * StrideX + 1;
    public int Height => BlocksY * StrideY + 1;

    /// <summary>How the blocks are split into wards: this many across and down.</summary>
    public int WardsX { get; private set; } = 1;
    public int WardsY { get; private set; } = 1;
    public int WardCount => WardsX * WardsY;

    public List<Lot> Lots { get; } = new();

    /// <summary>The row of blocks the river runs through, or -1 for a city without one.</summary>
    public int RiverRow { get; private set; } = -1;
    public string[] StreetNames { get; }
    public string[] AvenueNames { get; }

    public CityMap(int blocksX = 5, int blocksY = 4)
    {
        BlocksX = blocksX;
        BlocksY = blocksY;
        StreetNames = new string[BlocksY + 1];
        AvenueNames = new string[BlocksX + 1];
    }

    /// <summary>The ward a block belongs to.</summary>
    public int WardOfBlock(int bx, int by) => by * WardsY / BlocksY * WardsX + bx * WardsX / BlocksX;

    /// <summary>Assign every lot to a ward, splitting the blocks into an even grid.</summary>
    public void DrawWards(int wardsX, int wardsY)
    {
        WardsX = Math.Clamp(wardsX, 1, BlocksX);
        WardsY = Math.Clamp(wardsY, 1, BlocksY);
        foreach (var lot in Lots) lot.WardId = WardOfBlock((lot.X - 1) / StrideX, (lot.Y - 1) / StrideY);
    }

    /// <summary>The block a lot stands in, as a number: its row times the blocks across, plus its column.</summary>
    public int BlockOf(Lot lot) => (lot.Y - 1) / StrideY * BlocksX + (lot.X - 1) / StrideX;

    /// <summary>Two blocks side by side or one above the other, across a street or an avenue.</summary>
    public bool NextDoor(int a, int b) => Math.Abs(a % BlocksX - b % BlocksX) + Math.Abs(a / BlocksX - b / BlocksX) == 1;

    // ---- Alleys ----------------------------------------------------------------

    /// <summary>
    /// The gap between lot columns an alley runs north to south through a block, or 0 for none. Only a block of
    /// plain land has one, about half of them, chosen by position so the city's dice come out the same.
    /// </summary>
    public int AlleyOf(int bx, int by)
    {
        if (bx < 0 || by < 0 || bx >= BlocksX || by >= BlocksY) return 0;
        if (LotsOfBlock(bx, by).Any(l => l.Terrain != Terrain.Land || l.Court)) return 0;
        int k = bx * 131 + by;
        return Hash(k, 900) >= 0.55f ? 0 : 1 + (int)(Hash(k, 901) * (LotsPerBlockX - 1));
    }

    /// <summary>Whether a block with an alley also has one along the middle, east to west, behind the buildings.</summary>
    public bool HasBackAlley(int bx, int by) => AlleyOf(bx, by) > 0 && Hash(bx * 131 + by, 902) < 0.5f;

    /// <summary>
    /// How a block with a back alley is laid out from its north edge, in tiles: the buildings on the north side end at
    /// <see cref="LockupFront"/>, a row of lockups runs along their backs to <see cref="LockupDoor"/>, and a cobbled lane
    /// runs beside the lockups to <see cref="LaneEdge"/>, where the buildings on the south side begin.
    /// </summary>
    public const float LockupFront = 0.62f, LockupDoor = 1.1f, LaneEdge = 1.42f, LockupWidth = 0.34f;

    /// <summary>The lockups along a block's back alley, west to east, each as its west and east edge in tiles.</summary>
    public IReadOnlyList<(float X0, float X1)> LockupSpots(int bx, int by)
    {
        var spots = new List<(float, float)>();
        if (!HasBackAlley(bx, by)) return spots;
        float x0 = bx * StrideX + 1, alley = x0 + AlleyOf(bx, by);
        foreach (var (from, to) in new[] { (x0 + 0.06f, alley - 0.22f), (alley + 0.22f, x0 + LotsPerBlockX - 0.06f) })
        {
            int n = (int)((to - from) / LockupWidth);
            float start = from + (to - from - n * LockupWidth) / 2;
            for (int i = 0; i < n; i++) spots.Add((start + i * LockupWidth, start + (i + 1) * LockupWidth));
        }
        return spots;
    }

    // ---- Squares ---------------------------------------------------------------

    private HashSet<(int, int, bool)>? _squares;

    /// <summary>
    /// Stretches of road closed to traffic and paved over: a square with a fountain where a street ran between two
    /// avenues, or a little plaza where an avenue ran between two streets. Each is given as the avenue and street
    /// it starts from, and whether it was an avenue. People walk across them; cars go round.
    /// </summary>
    public IReadOnlyCollection<(int Bx, int By, bool Avenue)> Squares => _squares ??= LaySquares();

    /// <summary>Whether the stretch of a street row between avenue <paramref name="bx"/> and the next is a square.</summary>
    public bool StreetClosed(int bx, int street) => (_squares ??= LaySquares()).Contains((bx, street, false));

    /// <summary>Whether the stretch of an avenue between street row <paramref name="by"/> and the next is a plaza.</summary>
    public bool AvenueClosed(int avenue, int by) => (_squares ??= LaySquares()).Contains((avenue, by, true));

    /// <summary>
    /// Whether a closed stretch of avenue has been built over, joining the blocks either side into one double block.
    /// Nobody walks through one: the walk goes round by the next avenue along.
    /// </summary>
    public bool BuiltOver(int avenue, int by) => AvenueClosed(avenue, by) && Hash(avenue * 977 + by, 1302) < 0.4f;

    /// <summary>Whether an avenue can be walked between two street rows: none of the stretch between them is built over.</summary>
    public bool Walkable(int avenue, int fromStreet, int toStreet)
    {
        for (int by = Math.Min(fromStreet, toStreet); by < Math.Max(fromStreet, toStreet); by++)
            if (BuiltOver(avenue, by)) return false;
        return true;
    }

    /// <summary>
    /// The avenue a walk between two doors on different streets goes up or down: the one with the shortest way
    /// round, never one built over between the two streets.
    /// </summary>
    public int WalkAvenue(float x1, float x2, int fromStreet, int toStreet)
    {
        int best = 0;
        float bestCost = float.MaxValue;
        for (int a = 0; a <= BlocksX; a++)
        {
            if (!Walkable(a, fromStreet, toStreet)) continue;
            int ax = a * StrideX;
            float cost = Math.Abs(ax - x1) + Math.Abs(ax - x2);
            if (cost < bestCost) { bestCost = cost; best = ax; }
        }
        return best;
    }

    /// <summary>Whether a lot's door opens onto a square rather than a street cars can drive down.</summary>
    public bool FrontsSquare(Lot lot) => StreetClosed((lot.X - 1) / StrideX, lot.FrontY / StrideY);

    /// <summary>The street row the elevated railway runs over, counted in streets, or -1 in a city too small for one.</summary>
    public int RailStreet => BlocksY >= 5 ? BlocksY / 3 : -1;

    /// <summary>
    /// Pick the squares: about one for every five blocks, never on the edge of the city, the waterfront, the street
    /// under the El or the street a back alley opens onto, and never two at one crossing, so traffic can always
    /// turn off before one. Chosen by position and the street names rather than by dice, so the city's other draws
    /// come out the same, and a city loaded from an older save gets the same squares.
    /// </summary>
    private HashSet<(int, int, bool)> LaySquares()
    {
        uint salt = 2166136261;
        foreach (char ch in string.Concat(StreetNames) + string.Concat(AvenueNames)) salt = (salt ^ ch) * 16777619;
        int seed = (int)(salt & 0xFFFFFF);
        var candidates = new List<(int Bx, int By, bool Avenue)>();
        for (int s = 1; s < BlocksY; s++)
        {
            if (s == RailStreet || s == RiverRow || s == RiverRow + 1) continue;
            for (int bx = 0; bx < BlocksX; bx++)
                if (!HasBackAlley(bx, s)) candidates.Add((bx, s, false));
        }
        for (int a = 1; a < BlocksX; a++)
        for (int by = 0; by < BlocksY; by++)
            if (by != RiverRow) candidates.Add((a, by, true));

        var chosen = new HashSet<(int, int, bool)>();
        var crossings = new HashSet<(int, int)>();
        int count = Math.Max(1, BlocksX * BlocksY / 5);
        foreach (var c in candidates.OrderBy(c => Hash(c.Bx * 977 + c.By * 31 + (c.Avenue ? 7 : 0), seed)))
        {
            if (chosen.Count >= count) break;
            var ends = c.Avenue ? new[] { (c.Bx, c.By), (c.Bx, c.By + 1) } : new[] { (c.Bx, c.By), (c.Bx + 1, c.By) };
            if (ends.Any(crossings.Contains)) continue;
            chosen.Add(c);
            foreach (var e in ends) crossings.Add(e);
        }
        return chosen;
    }

    /// <summary>A number from 0 to 1 that depends only on its arguments, the same one the map is drawn with.</summary>
    public static float Hash(int a, int b = 0)
    {
        uint h = (uint)(a * 374761393 + b * 668265263) ^ 0x9E3779B9u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    public static bool IsStreetRow(int y) => y % StrideY == 0;
    public static bool IsAvenue(int x) => x % StrideX == 0;
    public static bool IsRoad(int x, int y) => IsStreetRow(y) || IsAvenue(x);

    public static CityMap Generate(Rng rng, int blocksX = 5, int blocksY = 4, bool river = false, int parks = 0)
    {
        var map = new CityMap(blocksX, blocksY);
        var streets = Content.StreetNames.ToList();
        rng.Shuffle(streets);
        for (int i = 0; i < map.StreetNames.Length; i++) map.StreetNames[i] = streets[i % streets.Count];
        var avenues = Content.AvenueNames.ToList();
        rng.Shuffle(avenues);
        for (int i = 0; i < map.AvenueNames.Length; i++) map.AvenueNames[i] = avenues[i % avenues.Count];

        for (int by = 0; by < map.BlocksY; by++)
        for (int bx = 0; bx < map.BlocksX; bx++)
        for (int ly = 0; ly < LotsPerBlockY; ly++)
        for (int lx = 0; lx < LotsPerBlockX; lx++)
        {
            int y = by * StrideY + 1 + ly;
            map.Lots.Add(new Lot
            {
                Id = map.Lots.Count,
                X = bx * StrideX + 1 + lx,
                Y = y,
                FrontY = ly == 0 ? by * StrideY : (by + 1) * StrideY,
            });
        }
        if (river) map.LayRiver(blocksY / 2);
        int blocks = blocksX * blocksY;
        map.LayParks(Math.Max(parks, Math.Max(1, blocks / 30)));
        map.LayLandmarks(rng);
        map.LayCivic();
        return map;
    }

    /// <summary>Turn a row of blocks into river. The streets either side become the waterfront and the avenues cross on bridges.</summary>
    private void LayRiver(int row)
    {
        RiverRow = row;
        foreach (var lot in Lots)
            if ((lot.Y - 1) / StrideY == row) lot.Terrain = Terrain.Water;
    }

    /// <summary>
    /// Lay out whole blocks as parks, spread through the city and away from the river. Chosen by
    /// position rather than by dice, so the city's other draws come out the same.
    /// </summary>
    private void LayParks(int count)
    {
        for (int i = 0; i < count; i++)
        {
            int bx = (int)(BlocksX * (i + 0.5) / count + (i % 2 == 0 ? 0.2 : -0.2));
            int by = (int)(BlocksY * (i % 2 == 0 ? 0.25 : 0.72));
            if (by == RiverRow) by++;
            bx = Math.Clamp(bx, 0, BlocksX - 1);
            by = Math.Clamp(by, 0, BlocksY - 1);
            foreach (var lot in Lots)
                if ((lot.X - 1) / StrideX == bx && (lot.Y - 1) / StrideY == by && lot.Terrain == Terrain.Land) lot.Terrain = Terrain.Park;
        }
    }

    /// <summary>The lots of one block, west to east along the north row and then the south.</summary>
    public IEnumerable<Lot> LotsOfBlock(int bx, int by) =>
        Lots.Where(l => (l.X - 1) / StrideX == bx && (l.Y - 1) / StrideY == by).OrderBy(l => l.Y).ThenBy(l => l.X);

    /// <summary>How far a block is from the middle of the city: 0 in the middle, about 1 at the edges.</summary>
    private double FromMiddle(int bx, int by)
    {
        double dx = (bx + 0.5) / BlocksX - 0.5, dy = (by + 0.5) / BlocksY - 0.5;
        return Math.Sqrt(dx * dx + dy * dy) * 2;
    }

    /// <summary>
    /// Graveyards on the edge of town, churches with their churchyards, and tenement blocks built
    /// round a court. Each takes a block that is still all ordinary land.
    /// </summary>
    private void LayLandmarks(Rng rng)
    {
        int blocks = BlocksX * BlocksY;
        var open = new List<(int X, int Y)>();
        for (int by = 0; by < BlocksY; by++)
        for (int bx = 0; bx < BlocksX; bx++)
            if (LotsOfBlock(bx, by).All(l => l.Terrain == Terrain.Land)) open.Add((bx, by));
        rng.Shuffle(open);

        // The dead are buried out past the houses.
        int cemeteries = Math.Max(1, blocks / 40);
        var outskirts = open.OrderByDescending(b => FromMiddle(b.X, b.Y)).Take(Math.Max(cemeteries, open.Count / 4)).ToList();
        foreach (var b in open.Where(outskirts.Contains).Take(cemeteries).ToList())
        {
            foreach (var lot in LotsOfBlock(b.X, b.Y)) lot.Terrain = Terrain.Cemetery;
            open.Remove(b);
        }

        // Tenement courts are where the poor live, in the tenement district.
        int courts = Math.Max(2, blocks / 7);
        foreach (var b in open.Where(b => ZoneOf(b.X, b.Y) == Zone.Tenements).Take(courts).ToList())
        {
            foreach (var lot in LotsOfBlock(b.X, b.Y)) lot.Court = true;
            open.Remove(b);
        }

        // A church every few blocks, with its churchyard beside it.
        int churches = Math.Max(2, blocks / 8);
        foreach (var b in open.Take(churches))
        {
            // On the south side of the block, so its doors face the way the city is seen from.
            int y = b.Y * StrideY + LotsPerBlockY;
            var row = LotsOfBlock(b.X, b.Y).Where(l => l.Y == y).ToList();
            int at = rng.Range(0, row.Count - 2);
            bool yardWest = rng.Chance(0.5);
            row[yardWest ? at + 1 : at].Terrain = Terrain.Church;
            row[yardWest ? at : at + 1].Terrain = Terrain.Cemetery;
        }
    }

    /// <summary>The way into a tenement court: a passage through the building on the south side of the block.</summary>
    public Lot? CourtGate(int bx, int by)
    {
        var south = LotsOfBlock(bx, by).Where(l => l.Court && l.FrontY > l.Y).ToList();
        return south.Count == 0 ? null : south[Math.Min(south.Count - 1, 1 + (bx + by) % 2)];
    }

    /// <summary>The ways into all the tenement courts in the city.</summary>
    public IEnumerable<Lot> CourtGates()
    {
        for (int by = 0; by < BlocksY; by++)
        for (int bx = 0; bx < BlocksX; bx++)
            if (CourtGate(bx, by) is { } gate) yield return gate;
    }

    public static CityMap FromSave(List<Lot> lots, string[] streets, string[] avenues, int blocksX, int blocksY, int wardsX, int wardsY)
    {
        var map = new CityMap(blocksX, blocksY) { WardsX = Math.Max(1, wardsX), WardsY = Math.Max(1, wardsY) };
        map.Lots.AddRange(lots.OrderBy(l => l.Id));
        var water = map.Lots.FirstOrDefault(l => l.Terrain == Terrain.Water);
        if (water != null) map.RiverRow = (water.Y - 1) / StrideY;
        Array.Copy(streets, map.StreetNames, Math.Min(streets.Length, map.StreetNames.Length));
        Array.Copy(avenues, map.AvenueNames, Math.Min(avenues.Length, map.AvenueNames.Length));
        return map;
    }

    public Lot LotAt(int id) => Lots[id];

    public string StreetOf(Lot lot) => StreetNames[lot.FrontY / StrideY];

    /// <summary>The walk between two doors along the streets, as tile corners including both ends.</summary>
    public List<(int X, int Y)> Path(Lot from, Lot to)
    {
        var path = new List<(int, int)> { (from.X, from.Y), (from.X, from.FrontY) };
        if (from.FrontY != to.FrontY)
        {
            int avenue = WalkAvenue(from.X, to.X, from.FrontY / StrideY, to.FrontY / StrideY);
            path.Add((avenue, from.FrontY));
            path.Add((avenue, to.FrontY));
        }
        path.Add((to.X, to.FrontY));
        path.Add((to.X, to.Y));
        return path;
    }

    /// <summary>Walking distance between two doors, in tiles.</summary>
    public int Distance(Lot from, Lot to)
    {
        var p = Path(from, to);
        int d = 0;
        for (int i = 1; i < p.Count; i++) d += Math.Abs(p[i].X - p[i - 1].X) + Math.Abs(p[i].Y - p[i - 1].Y);
        return d;
    }
}
