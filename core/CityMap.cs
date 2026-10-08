namespace DryTown.Core;

public enum LotUse { Empty, Business, Headquarters, Precinct }

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
    public int GangId { get; set; } = -1;
}

/// <summary>
/// The district as a tile grid: rectangular blocks of lots separated by streets.
/// Horizontal street rows run the full width and vertical avenues the full height,
/// so any two doors are joined by an L or Z shaped walk along the streets.
/// </summary>
public sealed class CityMap
{
    public const int BlocksX = 5, BlocksY = 4;
    public const int LotsPerBlockX = 4, LotsPerBlockY = 2;
    public const int StrideX = LotsPerBlockX + 1, StrideY = LotsPerBlockY + 1;
    public const int Width = BlocksX * StrideX + 1, Height = BlocksY * StrideY + 1;

    public List<Lot> Lots { get; } = new();
    public string[] StreetNames { get; } = new string[BlocksY + 1];
    public string[] AvenueNames { get; } = new string[BlocksX + 1];

    public static bool IsStreetRow(int y) => y % StrideY == 0;
    public static bool IsAvenue(int x) => x % StrideX == 0;
    public static bool IsRoad(int x, int y) => IsStreetRow(y) || IsAvenue(x);

    public static CityMap Generate(Rng rng)
    {
        var map = new CityMap();
        var streets = Content.StreetNames.ToList();
        rng.Shuffle(streets);
        for (int i = 0; i < map.StreetNames.Length; i++) map.StreetNames[i] = streets[i % streets.Count];
        var avenues = Content.AvenueNames.ToList();
        rng.Shuffle(avenues);
        for (int i = 0; i < map.AvenueNames.Length; i++) map.AvenueNames[i] = avenues[i % avenues.Count];

        for (int by = 0; by < BlocksY; by++)
        for (int bx = 0; bx < BlocksX; bx++)
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
        return map;
    }

    public static CityMap FromSave(List<Lot> lots, string[] streets, string[] avenues)
    {
        var map = new CityMap();
        map.Lots.AddRange(lots.OrderBy(l => l.Id));
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
            int avenue = NearestAvenue(from.X, to.X);
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

    private static int NearestAvenue(int x1, int x2)
    {
        int best = 0, bestCost = int.MaxValue;
        for (int a = 0; a <= BlocksX; a++)
        {
            int ax = a * StrideX;
            int cost = Math.Abs(ax - x1) + Math.Abs(ax - x2);
            if (cost < bestCost) { bestCost = cost; best = ax; }
        }
        return best;
    }
}
