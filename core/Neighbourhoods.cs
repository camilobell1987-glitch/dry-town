namespace DryTown.Core;

/// <summary>Where a man's people came from. The city's wards each belong to one of them.</summary>
public enum Ethnicity { Unknown, Irish, Italian, Jewish, Polish, German }

/// <summary>The neighbourhoods: each ward is home to one people, and its shopkeepers and voters know their own.</summary>
public static partial class Content
{
    /// <summary>What a man of the ward's own people adds to the chance a shopkeeper pays, and what a stranger loses.</summary>
    public const double HomeShakedown = 0.08, StrangerShakedown = 0.03;

    /// <summary>A shopkeeper paying an outfit of his own people resents it this much less each week.</summary>
    public const int HomeResentment = 1;

    /// <summary>What a candidate of the ward's own people is worth on the ballot, and how much further a man at the polls goes there.</summary>
    public const int HomeCandidate = 150;
    public const double HomePolls = 1.5;

    /// <summary>How likely a new man is to be of the boss's own people, rather than from wherever the outfit works.</summary>
    public const double RecruitOwnPeople = 0.7;

    /// <summary>How much less loyal a man starts out when he isn't of the boss's people.</summary>
    public const int StrangerLoyalty = 10;

    public static readonly Ethnicity[] Peoples = { Ethnicity.Irish, Ethnicity.Italian, Ethnicity.Jewish, Ethnicity.Polish, Ethnicity.German };

    /// <summary>How many peoples a city of this many wards has room for.</summary>
    public static int PeoplesFor(int wards) => wards <= 2 ? 2 : wards <= 4 ? 3 : wards <= 6 ? 4 : 5;

    public static string Label(Ethnicity e) => e == Ethnicity.Unknown ? "nobody's" : e.ToString();

    public static readonly Dictionary<Ethnicity, string[]> FirstNamesOf = new()
    {
        [Ethnicity.Irish] = new[] { "Mickey", "Jimmy", "Pat", "Dinny", "Frankie", "Tommy", "Eddie", "Jack", "Hughie", "Danny", "Terry", "Owney" },
        [Ethnicity.Italian] = new[] { "Sal", "Carmine", "Vito", "Dom", "Augie", "Tony", "Joe", "Nick", "Frankie", "Gus", "Lou", "Vic" },
        [Ethnicity.Jewish] = new[] { "Abe", "Izzy", "Moe", "Benny", "Sid", "Lou", "Harry", "Nate", "Max", "Meyer", "Hymie", "Sammy" },
        [Ethnicity.Polish] = new[] { "Stan", "Walt", "Kaz", "Eddie", "Joe", "Frank", "Tad", "Marty", "Steve", "Johnny", "Ziggy", "Hank" },
        [Ethnicity.German] = new[] { "Dutch", "Fritz", "Gus", "Hank", "Otto", "Walt", "Ollie", "Herm", "Kip", "Karl", "Ray", "Pete" },
    };

    public static readonly Dictionary<Ethnicity, string[]> LastNamesOf = new()
    {
        [Ethnicity.Irish] = new[] { "Brennan", "Doyle", "Fitzgerald", "Hanrahan", "Malloy", "O'Hare", "Quigley", "Tierney", "Sullivan", "Moran", "Duffy", "Kearney", "Madden" },
        [Ethnicity.Italian] = new[] { "Abbate", "Castellano", "Esposito", "Greco", "Iannucci", "Lombardo", "Russo", "Vitale", "Morello", "Barone", "Aiello", "Rinaldi", "Moretti" },
        [Ethnicity.Jewish] = new[] { "Weiss", "Kaplan", "Rosen", "Shapiro", "Gold", "Feldman", "Berman", "Levitt", "Gurwitz", "Abramowitz", "Silver", "Rubin" },
        [Ethnicity.Polish] = new[] { "Janowski", "Kowalczyk", "Novak", "Zielinski", "Dombrowski", "Wojcik", "Kaminski", "Lewandowski", "Mazur", "Sobczak", "Grabowski" },
        [Ethnicity.German] = new[] { "Schultz", "Ulrich", "Bauer", "Kessler", "Hoffman", "Krause", "Becker", "Vogel", "Engel", "Richter", "Weber", "Lindqvist" },
    };

    /// <summary>Ward names that belong to one people's neighbourhood, and only go to a ward of theirs.</summary>
    public static readonly Dictionary<string, Ethnicity> WardNameOf = new()
    {
        ["Little Palermo"] = Ethnicity.Italian,
        ["Kerry Patch"] = Ethnicity.Irish,
        ["Bridgeport"] = Ethnicity.Irish,
        ["Dutch Hollow"] = Ethnicity.German,
        ["Brewery Hill"] = Ethnicity.German,
        ["Little Warsaw"] = Ethnicity.Polish,
        ["Maxwell Street"] = Ethnicity.Jewish,
        ["Hester Street"] = Ethnicity.Jewish,
    };

    /// <summary>The people a surname belongs to, for saves from before the neighbourhoods.</summary>
    public static Ethnicity PeopleOfName(string name)
    {
        string last = World.Surname(name);
        foreach (var (people, names) in LastNamesOf)
            if (names.Contains(last)) return people;
        return Ethnicity.Unknown;
    }
}

public sealed partial class World
{
    private Ethnicity[]? _neighbourhoods;

    /// <summary>
    /// Each ward's people. A few wards are each the heart of one people's neighbourhood, and every other
    /// ward belongs to the nearest of them, so a neighbourhood runs on across the ward lines. Placed with
    /// dice of its own from the city's seed, so a saved city comes back with the same neighbourhoods.
    /// </summary>
    public Ethnicity Neighbourhood(int wardId)
    {
        if (_neighbourhoods == null || _neighbourhoods.Length != Map.WardCount)
        {
            var rng = new Rng(Settings.Seed ^ 0xF01C5);
            var peoples = Content.Peoples.ToList();
            rng.Shuffle(peoples);
            var wards = Enumerable.Range(0, Map.WardCount).ToList();
            rng.Shuffle(wards);
            int k = Math.Min(Content.PeoplesFor(Map.WardCount), Map.WardCount);
            var hearts = wards.Take(k).ToList();
            int Apart(int a, int b) => Math.Abs(a % Map.WardsX - b % Map.WardsX) + Math.Abs(a / Map.WardsX - b / Map.WardsX);
            _neighbourhoods = Enumerable.Range(0, Map.WardCount)
                .Select(ward => peoples[Enumerable.Range(0, k).OrderBy(i => Apart(ward, hearts[i])).ThenBy(i => i).First()])
                .ToArray();
        }
        return wardId >= 0 && wardId < _neighbourhoods.Length ? _neighbourhoods[wardId] : Ethnicity.Unknown;
    }

    public Ethnicity Neighbourhood(Ward ward) => Neighbourhood(ward.Id);

    public Ethnicity Neighbourhood(Business b) => Neighbourhood(LotOf(b).WardId);

    /// <summary>An outfit's people are its boss's.</summary>
    public Ethnicity PeopleOf(Gang g) => g.BossHoodId >= 0 ? HoodById(g.BossHoodId).Origin : Ethnicity.Unknown;

    /// <summary>Whether this man is of the people whose neighbourhood the business is in.</summary>
    public bool AtHome(Hood h, Business b) => h.Origin != Ethnicity.Unknown && h.Origin == Neighbourhood(b);

    /// <summary>Where a new man comes from: most often the boss's own people, else the neighbourhood of one of the outfit's shops.</summary>
    private Ethnicity RecruitOrigin(int gangId)
    {
        var gang = Gangs.FirstOrDefault(g => g.Id == gangId);
        if (gang == null) return Rng.Pick(Content.Peoples);
        var own = PeopleOf(gang);
        if (own != Ethnicity.Unknown && Rng.Chance(Content.RecruitOwnPeople)) return own;
        var turf = TurfOf(gang.Id).ToList();
        int lot = turf.Count > 0 ? Rng.Pick(turf).LotId : gang.HqLotId;
        return lot >= 0 ? Neighbourhood(Map.LotAt(lot).WardId) : own;
    }

    /// <summary>A man's loyalty to start with, a little less if he isn't of the boss's people.</summary>
    private int StartingLoyalty(int gangId, Ethnicity origin, int loyalty)
    {
        var gang = Gangs.FirstOrDefault(g => g.Id == gangId);
        return gang != null && PeopleOf(gang) is var own && own != Ethnicity.Unknown && own != origin ? loyalty - Content.StrangerLoyalty : loyalty;
    }

    private string FirstName(Ethnicity e) => Rng.Pick(Content.FirstNamesOf.GetValueOrDefault(e) ?? Content.FirstNames);

    private string LastName(Ethnicity e) => Rng.Pick(Content.LastNamesOf.GetValueOrDefault(e) ?? Content.LastNames);

    /// <summary>Saves from before the neighbourhoods: tell each man's people by his name, else by where his outfit is.</summary>
    private void GiveOrigins()
    {
        Ethnicity Near(int lotId) => lotId >= 0 ? Neighbourhood(Map.LotAt(lotId).WardId) : Neighbourhood(0);
        foreach (var h in Hoods.Where(h => h.Origin == Ethnicity.Unknown))
        {
            h.Origin = Content.PeopleOfName(h.Name);
            if (h.Origin == Ethnicity.Unknown) h.Origin = Near(Gangs.FirstOrDefault(g => g.Id == h.GangId)?.HqLotId ?? -1);
        }
        foreach (var p in People.Where(p => p.Origin == Ethnicity.Unknown))
        {
            p.Origin = Content.PeopleOfName(p.Name);
            if (p.Origin == Ethnicity.Unknown) p.Origin = Neighbourhood(p.WardId);
        }
    }
}
