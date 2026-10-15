namespace DryTown.Core;

/// <summary>
/// What a part of town is given over to, the way a city grew up by 1920: offices and the city's own buildings in
/// the middle, factories and yards on the waterfront and out along one edge, the immigrant tenements packed in
/// round them, brownstone rows further out, fine houses by the parks, and the suburbs at the edge of town.
/// </summary>
public enum Zone { Downtown, Industrial, Tenements, Rowhouses, GoldCoast, Suburbs, River, Park }

/// <summary>What a lot nobody does business on has on it.</summary>
public enum Filler { None, Tenement, Office, Factory, Vacant, Parking, PocketPark, Rowhouse, House, Mansion, Apartment, Warehouse, Gasworks, Yard }

/// <summary>One of the city's own buildings downtown, each taking two lots side by side.</summary>
public enum Civic { CityHall, Courthouse, Municipal, Records, Library, Museum, Opera, Theatre }

public sealed partial class CityMap
{
    private Zone[,]? _zones;
    private float[,]? _core;

    /// <summary>The zone a block is in.</summary>
    public Zone ZoneOf(int bx, int by)
    {
        if (_zones == null) LayZones();
        return _zones![Math.Clamp(bx, 0, BlocksX - 1), Math.Clamp(by, 0, BlocksY - 1)];
    }

    public Zone ZoneOf(Lot lot) => ZoneOf((lot.X - 1) / StrideX, (lot.Y - 1) / StrideY);

    /// <summary>How far into downtown a block is: 1 at the heart of it, falling to 0.4 at its edge, and 0 outside.</summary>
    public float CoreOf(int bx, int by)
    {
        if (_core == null) LayZones();
        return _core![Math.Clamp(bx, 0, BlocksX - 1), Math.Clamp(by, 0, BlocksY - 1)];
    }

    public float CoreOf(Lot lot) => CoreOf((lot.X - 1) / StrideX, (lot.Y - 1) / StrideY);

    /// <summary>Whether a block is in a zone, for a block that may be off the map.</summary>
    private bool IsZone(int bx, int by, Zone zone) => bx >= 0 && by >= 0 && bx < BlocksX && by < BlocksY && _zones![bx, by] == zone;

    /// <summary>
    /// Zone the city. Worked out from where the blocks lie, the river and the parks alone, never the dice, so a
    /// city loaded from an older save is zoned the same way.
    /// </summary>
    private void LayZones()
    {
        var zones = new Zone[BlocksX, BlocksY];
        var core = new float[BlocksX, BlocksY];
        _zones = zones;
        _core = core;
        var parks = Lots.Where(l => l.Terrain == Terrain.Park).Select(l => ((l.X - 1) / StrideX, (l.Y - 1) / StrideY)).ToHashSet();

        // Downtown is in the middle of town, or north of the water in a city with a river.
        float cx = BlocksX / 2f, cy = RiverRow >= 0 ? RiverRow * 0.55f : BlocksY / 2f;
        double Dist(int bx, int by)
        {
            double dx = (bx + 0.5 - cx) * StrideX, dy = (by + 0.5 - cy) * StrideY;
            // Across the river is further than it looks: there's a bridge to find.
            if (RiverRow >= 0 && by > RiverRow) dy += StrideY * 2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        var land = new List<(int X, int Y)>();
        for (int by = 0; by < BlocksY; by++)
        for (int bx = 0; bx < BlocksX; bx++)
        {
            if (by == RiverRow) zones[bx, by] = Zone.River;
            else if (parks.Contains((bx, by))) zones[bx, by] = Zone.Park;
            else land.Add((bx, by));
        }
        land = land.OrderBy(b => Dist(b.X, b.Y)).ThenBy(b => b.Y).ThenBy(b => b.X).ToList();
        int n = land.Count;
        if (n == 0) return;

        int downtown = Math.Max(1, (int)Math.Round(n * 0.09));
        for (int i = 0; i < downtown; i++)
        {
            var b = land[i];
            zones[b.X, b.Y] = Zone.Downtown;
            core[b.X, b.Y] = 1 - 0.6f * i / downtown;
        }
        var rest = land.Skip(downtown).ToList();

        // Industry: the waterfront first, then an estate out along one side of town, the side the street names pick.
        uint salt = 2166136261;
        foreach (char ch in string.Concat(StreetNames) + string.Concat(AvenueNames)) salt = (salt ^ ch) * 16777619;
        float edgeX = salt % 2 == 0 ? 0 : BlocksX - 1, edgeY = BlocksY * 0.8f;
        bool Waterfront((int X, int Y) b) => RiverRow >= 0 && Math.Abs(b.Y - RiverRow) == 1;
        int industrial = Math.Max(1, (int)Math.Round(n * 0.14));
        var wharves = rest.Where(Waterfront).OrderByDescending(b => Dist(b.X, b.Y)).Take((int)(industrial * 0.6)).ToList();
        var estate = rest.Except(wharves)
            .OrderBy(b => Math.Abs(b.X - edgeX) * StrideX + Math.Abs(b.Y - edgeY) * StrideY).ThenBy(b => b.Y).ThenBy(b => b.X)
            .Take(industrial - wharves.Count);
        foreach (var b in wharves.Concat(estate).ToList())
        {
            zones[b.X, b.Y] = Zone.Industrial;
            rest.Remove(b);
        }

        bool Near((int X, int Y) b, Zone zone, int reach)
        {
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
                if (IsZone(b.X + dx, b.Y + dy, zone)) return true;
            return false;
        }

        // The rich live by the parks, close to downtown and well away from the smoke.
        int gold = n >= 12 ? Math.Max(1, (int)Math.Round(n * 0.08)) : 0;
        var clean = rest.Where(b => !Near(b, Zone.Industrial, 2)).ToList();
        if (clean.Count < gold) clean = rest.Where(b => !Near(b, Zone.Industrial, 1)).ToList();
        foreach (var b in clean.OrderBy(b => (Near(b, Zone.Park, 1) ? 0 : 1000) + Dist(b.X, b.Y)).Take(gold).ToList())
        {
            zones[b.X, b.Y] = Zone.GoldCoast;
            rest.Remove(b);
        }

        // The tenements crowd in round the factories and downtown, where the work is; then come the brownstone rows,
        // and out at the edge of town the suburbs.
        var order = rest.OrderBy(b => Dist(b.X, b.Y) - (Near(b, Zone.Industrial, 1) || Near(b, Zone.Downtown, 1) ? StrideX * 1.5 : 0))
            .ThenBy(b => b.Y).ThenBy(b => b.X).ToList();
        int tenements = (int)Math.Round(order.Count * 0.45), rows = (int)Math.Round(order.Count * 0.32);
        for (int i = 0; i < order.Count; i++)
            zones[order[i].X, order[i].Y] = i < tenements ? Zone.Tenements : i < tenements + rows ? Zone.Rowhouses : Zone.Suburbs;
    }

    /// <summary>
    /// Give the heart of downtown to the city: a civic centre with City Hall and the courts, and in a bigger city a
    /// second block for the library, the museum, the opera and a theatre. Each takes a block that is still all
    /// ordinary land.
    /// </summary>
    private void LayCivic()
    {
        int want = BlocksX * BlocksY >= 40 ? 2 : 1;
        var blocks = new List<(int X, int Y)>();
        for (int by = 0; by < BlocksY; by++)
        for (int bx = 0; bx < BlocksX; bx++)
            if (ZoneOf(bx, by) == Zone.Downtown && LotsOfBlock(bx, by).All(l => l.Terrain == Terrain.Land && !l.Court)) blocks.Add((bx, by));
        foreach (var b in blocks.OrderByDescending(b => CoreOf(b.X, b.Y)).Take(want))
            foreach (var lot in LotsOfBlock(b.X, b.Y)) lot.Terrain = Terrain.Civic;
    }

    /// <summary>What stands on a lot nobody does business on, from its zone and a roll of its own.</summary>
    public Filler FillerOf(Lot lot)
    {
        if (lot.Use != LotUse.Empty || lot.Terrain != Terrain.Land) return Filler.None;
        if (lot.Court) return Filler.Tenement;
        float h = Hash(lot.Id, 11);
        return ZoneOf(lot) switch
        {
            Zone.Downtown => h < 0.88f ? Filler.Office : Filler.Parking,
            Zone.Industrial => h switch
            {
                < 0.4f => Filler.Factory,
                < 0.6f => Filler.Warehouse,
                < 0.68f => Filler.Gasworks,
                < 0.8f => Filler.Yard,
                < 0.9f => Filler.Vacant,
                _ => Filler.Parking,
            },
            Zone.Tenements => h switch { < 0.84f => Filler.Tenement, < 0.92f => Filler.Vacant, _ => Filler.PocketPark },
            Zone.Rowhouses => h switch { < 0.8f => Filler.Rowhouse, < 0.88f => Filler.Tenement, < 0.94f => Filler.PocketPark, _ => Filler.Vacant },
            Zone.GoldCoast => h switch { < 0.55f => Filler.Mansion, < 0.9f => Filler.Apartment, _ => Filler.PocketPark },
            Zone.Suburbs => h < 0.9f ? Filler.House : Filler.Vacant,
            _ => Filler.Tenement,
        };
    }

    /// <summary>Whether people live in a building of this sort, rather than work in it.</summary>
    public static bool IsHome(Filler f) => f is Filler.Tenement or Filler.Rowhouse or Filler.House or Filler.Mansion or Filler.Apartment;

    /// <summary>The blocks given to the city's own buildings, the heart of downtown first.</summary>
    public List<(int X, int Y)> CivicBlocks()
    {
        var blocks = Lots.Where(l => l.Terrain == Terrain.Civic).Select(l => ((l.X - 1) / StrideX, (l.Y - 1) / StrideY)).Distinct().ToList();
        return blocks.OrderByDescending(b => CoreOf(b.Item1, b.Item2)).ThenBy(b => b.Item2).ThenBy(b => b.Item1).ToList();
    }

    /// <summary>
    /// Which of the city's buildings stands on a civic lot, and whether this lot is the west half of it, where it's
    /// drawn from. In a city with one civic block it holds City Hall, the courthouse, the library and a theatre; with
    /// two, the government has the first and the arts the second.
    /// </summary>
    public (Civic Building, bool West) CivicOf(Lot lot)
    {
        int bx = (lot.X - 1) / StrideX, by = (lot.Y - 1) / StrideY, lx = (lot.X - 1) % StrideX;
        bool south = lot.FrontY > lot.Y, west = lx < LotsPerBlockX / 2;
        var blocks = CivicBlocks();
        int index = Math.Max(0, blocks.IndexOf((bx, by)));
        Civic[] plan = blocks.Count == 1
            ? new[] { Civic.CityHall, Civic.Courthouse, Civic.Library, Civic.Theatre }
            : index == 0
                ? new[] { Civic.CityHall, Civic.Courthouse, Civic.Municipal, Civic.Records }
                : new[] { Civic.Opera, Civic.Museum, Civic.Library, Civic.Theatre };
        return (plan[(south ? 0 : 2) + (west ? 0 : 1)], lx % 2 == 0);
    }

    /// <summary>
    /// Whether a business would set up in a zone. Hotels want downtown or the fashionable streets, warehouses the
    /// industrial district; the suburbs have shops along their main streets but no pool halls, and the fine houses
    /// only a few shops of the right sort.
    /// </summary>
    public static bool Suits(BusinessKind kind, Zone zone, int lotId) => zone switch
    {
        Zone.Downtown => kind is not BusinessKind.Warehouse,
        Zone.Industrial => kind is BusinessKind.Warehouse or BusinessKind.Garage or BusinessKind.Diner or BusinessKind.PoolHall
            or BusinessKind.Laundry or BusinessKind.Hardware,
        Zone.Tenements or Zone.Rowhouses => kind is not (BusinessKind.Hotel or BusinessKind.Warehouse),
        Zone.GoldCoast => kind is BusinessKind.Hotel or BusinessKind.Tailor or BusinessKind.Haberdasher or BusinessKind.Pharmacy
            && Hash(lotId, 1400) < 0.5f,
        Zone.Suburbs => kind is not (BusinessKind.Hotel or BusinessKind.Warehouse or BusinessKind.PoolHall) && Hash(lotId, 1400) < 0.55f,
        _ => false,
    };

    /// <summary>How much more or less a business takes for where it is: more downtown and among the rich, less in the slums.</summary>
    public static double TakingsIn(Zone zone) => zone switch
    {
        Zone.Downtown => 1.3,
        Zone.GoldCoast => 1.4,
        Zone.Suburbs => 1.1,
        Zone.Industrial => 0.9,
        Zone.Tenements => 0.85,
        _ => 1,
    };

    public static string Label(Zone zone) => zone switch
    {
        Zone.Downtown => "Downtown",
        Zone.Industrial => "Factories and yards",
        Zone.Tenements => "Tenements",
        Zone.Rowhouses => "Brownstone rows",
        Zone.GoldCoast => "Fashionable district",
        Zone.Suburbs => "Suburbs",
        Zone.River => "The river",
        _ => "Park",
    };

    public static string Label(Civic c) => c switch
    {
        Civic.CityHall => "City Hall",
        Civic.Courthouse => "County Courthouse",
        Civic.Municipal => "Municipal Building",
        Civic.Records => "Hall of Records",
        Civic.Library => "Public Library",
        Civic.Museum => "Museum of Art",
        Civic.Opera => "Opera House",
        _ => "Palace Theatre",
    };
}
