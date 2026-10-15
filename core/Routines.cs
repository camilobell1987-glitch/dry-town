namespace DryTown.Core;

/// <summary>What sort of day someone has, which decides where they go.</summary>
public enum RoutineKind { Loafer, Shopkeeper, Worker, Landlord, Errands, NightShift, Park, DayOff, JobHunt }

/// <summary>Why someone goes somewhere, so their week can be told on their card.</summary>
public enum Errand { Work, Home, Shop, Church, Park, Drink, Gamble, Lover, Precinct, LoanShark, Rent, JobHunt, NightOut, Cafe }

/// <summary>
/// A place someone goes in their day: the lot, the hour they set out for the next place (or home), how long they
/// stand about outside on arriving before going in, and why they go. Hours run past 24 for a night out that ends after
/// midnight.
/// </summary>
public readonly record struct Stop(int LotId, float Leave, float Outside, Errand Why = Errand.Work);

/// <summary>
/// Someone's day: when they leave home and the places they go before they come back. A loafer has no places to be,
/// and walks round his own block now and then as before.
/// </summary>
public sealed record Routine(RoutineKind Kind, float LeaveHome, IReadOnlyList<Stop> Stops)
{
    public static readonly Routine Loafing = new(RoutineKind.Loafer, 0, Array.Empty<Stop>());
}

public static partial class Content
{
    /// <summary>How fast residents walk, in tiles an hour.</summary>
    public const float ResidentPace = 10;

    /// <summary>The most stops in anyone's day, so the map can keep them in a fixed space.</summary>
    public const int MostStops = 3;

    /// <summary>
    /// The hour one day's comings and goings give way to the next. A night out on Saturday runs on into the small hours
    /// of Sunday, and the city is quietest at five in the morning.
    /// </summary>
    public const float DayTurns = 5;

    public static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
}

public sealed partial class CityDirectory
{
    /// <summary>Monday's routine, the day the week's planning is done on.</summary>
    public Routine RoutineOf(int id) => RoutineOf(id, 0);

    /// <summary>
    /// Where someone goes on a day of the week, 0 for Monday to 6 for Sunday. Most work six days and rest on the
    /// seventh: Sunday, or Saturday for a Jew who keeps the Sabbath. Saturday is a half day for working men and market
    /// day for the women keeping house; Monday is wash day. Then who they are fills in the rest: a souse is at his bar
    /// most nights, a sport at the tables, a man with a woman on the side goes calling twice a week, a stool pigeon drops
    /// in at the precinct house on Thursdays, the young and single go out on Saturday night, a family man goes nowhere but
    /// home, and the Catholic families go to Mass together on Sunday and the park after.
    /// </summary>
    public Routine RoutineOf(int id, int day)
    {
        day = (day % 7 + 7) % 7;
        var c = Get(id);
        var home = _w.Map.LotAt(c.HomeLotId);
        var character = CharacterOf(id);
        bool Is(Trait t) => character.Traits.Contains(t);
        // What varies from day to day, and what's the same every day: the hour a man starts work doesn't change.
        float Today(float from, float spread, int salt) => from + (float)Roll(id, salt + 7919 * (day + 1)) * spread;
        float Daily(float from, float spread, int salt) => from + (float)Roll(id, salt) * spread;
        bool restDay = day == RestDayOf(c);
        var plan = new DayPlan(this, c.HomeLotId);

        if (c.OwnsBusinessId >= 0)
        {
            // He lives over the shop: he's out front to open up in the morning, and again to shut up for the night.
            var shop = _w.BusinessById(c.OwnsBusinessId);
            var (open, shut) = Hours(shop.Kind);
            // A diner, a hotel, a pool hall, a picture house and a dance hall open every day; anything else shuts on its owner's day of rest.
            bool everyDay = shop.Kind is BusinessKind.Diner or BusinessKind.Hotel or BusinessKind.PoolHall or BusinessKind.PictureHouse or BusinessKind.DanceHall;
            if (restDay && !everyDay) return RestDay(c, character, day, plan);
            if (day == 5) shut += 1;
            if (day == 6 && everyDay) open += 2;
            float opens = open + (float)Roll(id, 601) * 0.5f, shuts = shut + (float)Roll(id, 602) * 0.5f;
            plan.SetOut(opens - 0.4f);
            plan.Until(shop.LotId, shuts - 0.4f, 0.4f, Errand.Work);
            plan.Until(shop.LotId, shuts, 0.4f, Errand.Work);
            Evening(c, character, day, plan, shuts);
            return plan.Done(RoutineKind.Shopkeeper);
        }
        if (restDay) return RestDay(c, character, day, plan);
        if (c.Owns.Count > 0)
        {
            // Saturday is rent day, round every building for it, home after dark. In the week he looks in on one or two
            // of them for the repairs and the complaints, and is home by the afternoon.
            var buildings = c.Owns.Where(l => l != c.HomeLotId).OrderBy(l => Roll(id, 610 + l % 97)).ToList();
            if (buildings.Count == 0) buildings.Add(c.HomeLotId);
            bool rent = day == 5;
            int calls = rent ? Math.Min(Content.MostStops, buildings.Count) : Math.Min(1 + (int)(Roll(id, 614 + day) * 2), buildings.Count);
            var round = Enumerable.Range(0, calls).Select(k => buildings[(k + (rent ? 0 : day)) % buildings.Count]).ToList();
            float back = rent ? Today(19.5f, 1.5f, 612) : Today(15, 1.5f, 612);
            plan.SetOut(Today(8.5f, 1.5f, 611));
            for (int k = 0; k < round.Count; k++)
            {
                // He spends the day going door to door; the last call lasts until it's time to go home.
                bool last = k == round.Count - 1;
                float stay = last ? Math.Max(0.5f, back - WalkHours(round[k], c.HomeLotId) - plan.Arriving(round[k])) : Today(1.5f, 2, 613 + k);
                plan.For(round[k], stay, Math.Min(stay, 0.6f), Errand.Rent);
            }
            if (!rent) Evening(c, character, day, plan, plan.At);
            return plan.Done(RoutineKind.Landlord);
        }
        if (c.Age < 14) return Routine.Loafing;
        if (c.Age >= 65 && c.Job == "Retired")
        {
            // The old go and sit in the park if there's one near, most days.
            if (Today(0, 1, 623) < 0.25 || ParkNear(home, id) is not int bench) return Routine.Loafing;
            plan.SetOut(Today(10, 2, 620));
            plan.Until(bench, Today(14.5f, 2, 621), 2.5f, Errand.Park);
            return plan.Done(RoutineKind.Park);
        }
        if (c.Job == "Night watchman")
        {
            // Out as the others come home, six nights a week, and home before first light.
            if (Workplace(c, home) is not { } post) return Routine.Loafing;
            plan.SetOut(Daily(18.5f, 1, 630));
            plan.Until(post.Id, 24 + Daily(3.2f, 0.6f, 631), 0, Errand.Work);
            return plan.Done(RoutineKind.NightShift);
        }
        if (c.Job is "Keeps house" or "Takes in washing")
        {
            // Monday is wash day, and nobody goes out. In the week she runs to a shop or a diner; Saturday is market day,
            // round two or three of them. A nosy parker stops for longer at each.
            if (day == 0) return Routine.Loafing;
            var shops = Errands(c, home, day == 5 ? 3 : Is(Trait.NosyParker) ? 2 : 1, day);
            if (shops.Count == 0) return Routine.Loafing;
            plan.SetOut(Today(9.5f, 2, 640));
            foreach (int lot in shops) plan.For(lot, Today(0.4f, 1.1f, 641 + plan.Stops.Count), Is(Trait.NosyParker) ? 0.6f : 0.15f, Errand.Shop);
            return plan.Done(RoutineKind.Errands);
        }
        if (c.Job == "Out of work")
        {
            // Round the factory gates and the shops in the morning asking for work, and whatever he does after.
            var gates = JobHunt(c, home, day);
            if (gates.Count == 0) return Routine.Loafing;
            plan.SetOut(Today(6.5f, 1, 645));
            foreach (int lot in gates.Take(2)) plan.For(lot, Today(0.3f, 0.5f, 646 + plan.Stops.Count), 0.4f, Errand.JobHunt);
            Evening(c, character, day, plan, plan.At + 1);
            return plan.Done(RoutineKind.JobHunt);
        }
        {
            // Anyone else has a job to get to, six days a week: a half day on Saturday, and a Jew who keeps the Sabbath
            // is home before sundown on Friday.
            if (Workplace(c, home) is not { } work) return Routine.Loafing;
            float off = day == 5 ? Daily(12.5f, 1, 652) : Daily(17, 1.5f, 651);
            if (day == 4 && KeepsSabbath(c)) off = Math.Min(off, Daily(15, 0.5f, 653));
            plan.SetOut(Daily(6.5f, 1.5f, 650));
            plan.Until(work.Id, off, 0, Errand.Work);
            Evening(c, character, day, plan, off);
            return plan.Done(RoutineKind.Worker);
        }
    }

    /// <summary>Whether someone keeps the Jewish Sabbath: the devout, the old, and those fresh off the boat.</summary>
    private bool KeepsSabbath(Citizen c) =>
        c.Origin == Ethnicity.Jewish && (CharacterOf(HeadOfHousehold(c.Id)).Traits.Contains(Trait.Pious) || c.Age >= 50
            || CharacterOf(HeadOfHousehold(c.Id)).Background == Background.Greenhorn || Roll(HeadOfHousehold(c.Id), 660) < 0.4);

    /// <summary>The day someone rests: Sunday, or Saturday for a Jew who keeps the Sabbath. A Jewish shop shuts on Saturday.</summary>
    public int RestDayOf(Citizen c) => KeepsSabbath(c) || c.OwnsBusinessId >= 0 && c.Origin == Ethnicity.Jewish ? 5 : 6;

    /// <summary>The first of someone's household, whose say decides where the family goes together.</summary>
    private int HeadOfHousehold(int id)
    {
        var (_, _, n) = Where(id);
        return id - n + HouseholdAt(id / PerBlock, n).Start;
    }

    /// <summary>
    /// A day of rest. The Catholic families go to Mass together in the morning; a family man takes his family to the
    /// park in the afternoon, and so do the Germans; a Jewish family keeping the Sabbath takes its walk in the park. The
    /// Italians sit down to Sunday dinner at home, and walk out to a café at dusk.
    /// </summary>
    private Routine RestDay(Citizen c, Character character, int day, DayPlan plan)
    {
        int head = HeadOfHousehold(c.Id);
        var home = _w.Map.LotAt(c.HomeLotId);
        var headCharacter = CharacterOf(head);
        bool Is(Trait t) => character.Traits.Contains(t);
        // The family walk out together, so their hours come from the head of the house.
        float Together(float from, float spread, int salt) => from + (float)Roll(head, salt) * spread;
        bool sabbath = day == 5;
        double faith = sabbath ? 0 : c.Origin switch
        {
            Ethnicity.Polish => 0.9,
            Ethnicity.Irish => 0.85,
            Ethnicity.Italian => 0.75,
            Ethnicity.German => 0.6,
            Ethnicity.Jewish => 0,
            _ => 0.5,
        };
        if (headCharacter.Traits.Contains(Trait.Pious)) faith = Math.Min(1, faith * 1.5 + 0.2);
        if (Is(Trait.Souse) || Is(Trait.Sport) || Is(Trait.Sheik)) faith *= 0.5;
        bool family = c.Ties.Any(t => t.Kin is Kin.Spouse or Kin.Child or Kin.Parent);
        // Mass at eight, half past nine or eleven.
        if (Roll(head, 670) < faith && Nearest(home, "church", 30) is { } church)
        {
            float mass = new[] { 8f, 9.5f, 11f }[(int)(Roll(head, 671) * 3)];
            plan.SetOut(mass - WalkHours(c.HomeLotId, church.Id) - 0.2f);
            plan.Until(church.Id, mass + 1.1f, 0.25f, Errand.Church);
        }
        bool park = sabbath || headCharacter.Traits.Contains(Trait.FamilyMan) || c.Origin == Ethnicity.German
            || family && Roll(head, 672) < 0.35 || c.Age >= 65;
        if (c.Origin == Ethnicity.Italian && family && !sabbath)
        {
            // Sunday dinner at home, and a walk to the café in the evening.
            if (Cafe(home) is int cafe)
            {
                if (plan.Stops.Count == 0) plan.SetOut(Together(17.5f, 1, 673));
                else plan.Until(c.HomeLotId, Together(17.5f, 1, 673), 0, Errand.Home);
                plan.For(cafe, Together(1.2f, 1, 674), 0.5f, Errand.Cafe);
            }
        }
        else if (park && ParkNear(home, head) is int bench)
        {
            float leave = Together(14, 1.5f, 675);
            if (plan.Stops.Count == 0) plan.SetOut(leave);
            else if (plan.At < leave - 0.5f) plan.Until(c.HomeLotId, leave, 0, Errand.Home);
            plan.Until(bench, Together(16.5f, 1.5f, 676), 1.5f, Errand.Park);
        }
        if (c.Age >= 18) Evening(c, character, day, plan, Math.Max(plan.At, 18));
        return plan.Done(RoutineKind.DayOff);
    }

    /// <summary>
    /// Where someone goes of an evening, after work or their day: home, mostly. A souse goes to his bar most nights and a
    /// sport to his game three nights a week; a man with a lover goes calling on Wednesdays and Saturdays; a man in debt
    /// pays the loan shark on Friday; a stool pigeon with something to tell has a word at the precinct on Thursday; the devout go to church on
    /// Wednesday evening; and the young and single, and the hotheads, go out on Saturday night. A family man and a
    /// tightwad stay in.
    /// </summary>
    private void Evening(Citizen c, Character character, int day, DayPlan plan, float free)
    {
        if (c.Age < 18 || plan.Stops.Count >= Content.MostStops) return;
        bool Is(Trait t) => character.Traits.Contains(t);
        if (Is(Trait.FamilyMan)) return;
        var home = _w.Map.LotAt(c.HomeLotId);
        var secrets = SecretsOf(c.Id);
        int? Secret(Vice v) => secrets.Where(s => s.Vice == v).Select(s => (int?)s.Other).FirstOrDefault();
        float Today(float from, float spread, int salt) => from + (float)Roll(c.Id, salt + 7919 * (day + 1)) * spread;
        bool married = c.Ties.Any(t => t.Kin == Kin.Spouse);
        bool saturday = day == 5;
        // Supper at home first, unless he goes straight from work.
        void Go(int lot, float from, float until, float outside, Errand why)
        {
            if (plan.Stops.Count == 0) plan.SetOut(from - WalkHours(c.HomeLotId, lot));
            else if (plan.Stops.Count < Content.MostStops - 1 && plan.At + WalkHours(plan.Last, c.HomeLotId) + 0.75f < from - WalkHours(c.HomeLotId, lot))
                plan.Until(c.HomeLotId, from - WalkHours(c.HomeLotId, lot), 0, Errand.Home);
            plan.Until(lot, until, outside, why);
        }
        free = Math.Max(free, 17);

        if (Secret(Vice.Drink) is int bar and >= 0 && (Is(Trait.Souse) ? day != RestDayOf(c) || Today(0, 1, 680) < 0.5 : saturday || day == 2))
        {
            Go(_w.LotOf(_w.BusinessById(bar)).Id, Math.Max(free + 0.5f, Today(19, 1.5f, 681)), saturday ? Today(24, 1.5f, 682) : Today(21.5f, 1.5f, 682), 0.2f, Errand.Drink);
            return;
        }
        if (Secret(Vice.Gambling) is int game and >= 0 && (day is 1 or 3 or 4 || saturday && Is(Trait.Sport)))
        {
            Go(_w.LotOf(_w.BusinessById(game)).Id, Math.Max(free + 0.5f, Today(19.5f, 1, 683)), Today(22.5f, 2, 684), 0.2f, Errand.Gamble);
            return;
        }
        if (Secret(Vice.Lover) is int lover and >= 0 && Exists(lover) && day is 2 or 5)
        {
            Go(Get(lover).HomeLotId, Math.Max(free + 0.5f, Today(20, 1, 685)), Today(22.5f, 1, 686), 0, Errand.Lover);
            return;
        }
        if (Secret(Vice.Debt) is not null && day == 4 && LoanShark(home) is int shark)
        {
            Go(shark, Math.Max(free + 0.3f, Today(18, 1, 687)), Today(18.8f, 0.5f, 688), 0.3f, Errand.LoanShark);
            return;
        }
        if (Is(Trait.StoolPigeon) && day == 3 && Roll(c.Id, 696) < 0.4 && Nearest(home, "precinct", 30) is { } precinct)
        {
            Go(precinct.Id, Math.Max(free + 0.3f, Today(19, 1.5f, 689)), Today(20.8f, 0.5f, 690), 0.1f, Errand.Precinct);
            return;
        }
        if (Is(Trait.Pious) && day == 2 && c.Origin != Ethnicity.Jewish && Nearest(home, "church", 30) is { } church)
        {
            Go(church.Id, Math.Max(free + 0.3f, 19), Today(20.2f, 0.4f, 691), 0.2f, Errand.Church);
            return;
        }
        if (Is(Trait.Tightwad) || Is(Trait.Pious)) return;
        // Saturday night: the young and single, a sheik or a flapper on Friday too, and a hothead or an Irishman even if he's married.
        bool young = !married && c.Age <= 35;
        bool goesOut = saturday && (young || Is(Trait.Sheik) || (!c.Woman && (Is(Trait.Hothead) || c.Origin == Ethnicity.Irish) && Today(0, 1, 692) < 0.5))
            || day == 4 && Is(Trait.Sheik);
        if (goesOut && NightSpot(home, c.Id) is int spot)
            Go(spot, Math.Max(free + 1, Today(20, 1.5f, 693)), Today(24, 1.5f, 694), 0.4f, Errand.NightOut);
    }

    /// <summary>
    /// A day planned out one place after another: it keeps count of the hour, so each stop leaves after the walk there,
    /// and never takes more places than the map has room for.
    /// </summary>
    private sealed class DayPlan
    {
        private readonly CityDirectory _d;
        private readonly int _home;
        private float _leave = -1;

        public readonly List<Stop> Stops = new();

        /// <summary>The hour the day has got to, and the place it got to.</summary>
        public float At { get; private set; }
        public int Last { get; private set; }

        public DayPlan(CityDirectory d, int home)
        {
            _d = d;
            _home = home;
            Last = home;
        }

        public void SetOut(float hour)
        {
            if (_leave >= 0) return;
            _leave = Math.Max(0, hour);
            At = _leave;
        }

        /// <summary>When someone setting out for a lot now would get there.</summary>
        public float Arriving(int lot) => At + _d.WalkHours(Last, lot);

        /// <summary>Walk to a lot and stay until an hour, or a little while if the walk takes past it.</summary>
        public void Until(int lot, float leave, float outside, Errand why)
        {
            if (Stops.Count >= Content.MostStops || _leave < 0) return;
            float arrive = Arriving(lot);
            float until = Math.Max(leave, arrive + 0.2f);
            Stops.Add(new Stop(lot, until, Math.Min(outside, until - arrive), why));
            At = until;
            Last = lot;
        }

        /// <summary>Walk to a lot and stay there a while.</summary>
        public void For(int lot, float stay, float outside, Errand why) => Until(lot, Arriving(lot) + stay, outside, why);

        public Routine Done(RoutineKind kind)
        {
            // A last stop at home is the same as going home.
            while (Stops.Count > 0 && Stops[^1].LotId == _home && Stops[^1].Why == Errand.Home) Stops.RemoveAt(Stops.Count - 1);
            return Stops.Count == 0 ? Routine.Loafing : new Routine(kind, _leave, Stops.ToArray());
        }
    }

    /// <summary>A spot in the nearest park, somewhere on the grass and not all on the one bench.</summary>
    private int? ParkNear(Lot home, int who)
    {
        if (Nearest(home, "park", 18) is not { } near) return null;
        var park = _w.Map.LotsOfBlock((near.X - 1) / CityMap.StrideX, (near.Y - 1) / CityMap.StrideY).Where(l => l.Terrain == Terrain.Park).ToList();
        return park.Count == 0 ? null : park[(int)(Roll(who, 622) * park.Count)].Id;
    }

    /// <summary>Businesses in a home's ward that answer to something, nearest first.</summary>
    private IEnumerable<Business> WardBusinesses(Lot home, Func<Business, bool> fits) =>
        (_businessesInWard.GetValueOrDefault(home.WardId) ?? new List<int>()).Select(_w.BusinessById).Where(fits)
            .OrderBy(b => _w.Map.Distance(home, _w.LotOf(b))).ThenBy(b => b.Id);

    /// <summary>Where the ward goes on a Saturday night: a speakeasy, a club or a dance hall, or a picture house, a pool hall, a hotel bar or a diner.</summary>
    private int? NightSpot(Lot home, int who)
    {
        var spots = WardBusinesses(home, b => b.Racket is RacketKind.Speakeasy or RacketKind.Nightclub || b.Kind == BusinessKind.DanceHall).Take(3).ToList();
        if (spots.Count == 0) spots = WardBusinesses(home, b => b.Kind is BusinessKind.PictureHouse or BusinessKind.PoolHall or BusinessKind.Hotel or BusinessKind.Diner).Take(3).ToList();
        return spots.Count == 0 ? null : spots[(int)(Roll(who, 695) * spots.Count)].LotId;
    }

    private int? Cafe(Lot home) => WardBusinesses(home, b => b.Kind == BusinessKind.Diner).Select(b => (int?)b.LotId).FirstOrDefault();

    /// <summary>Whoever lends money in the ward: a loan shark's back room, or failing that a bookie's.</summary>
    private int? LoanShark(Lot home) =>
        WardBusinesses(home, b => b.Racket == RacketKind.LoanShark).Concat(WardBusinesses(home, b => b.Racket == RacketKind.Bookie))
            .Select(b => (int?)b.LotId).FirstOrDefault();

    /// <summary>Two places a man out of work asks at on a morning: the factories and yards near home, or the ward's shops.</summary>
    private List<int> JobHunt(Citizen c, Lot home, int day)
    {
        var gates = new List<int>();
        for (int k = 0; k < 2; k++)
        {
            var places = Roll(c.Id, 700 + day * 3 + k) < 0.5 ? new[] { Filler.Factory, Filler.Warehouse, Filler.Yard } : null;
            int? lot = places != null ? Commute(home, places, c.Id * 7 + day * 3 + k)?.Id : null;
            lot ??= _businessesInWard.GetValueOrDefault(home.WardId) is { Count: > 0 } shops
                ? _w.LotOf(_w.BusinessById(shops[(int)(Roll(c.Id, 710 + day * 3 + k) * shops.Count)])).Id : null;
            if (lot is int l && !gates.Contains(l)) gates.Add(l);
        }
        return gates;
    }

    private static (float Open, float Shut) Hours(BusinessKind kind) => kind switch
    {
        BusinessKind.Diner => (6, 20),
        BusinessKind.Grocer => (7, 19),
        BusinessKind.PoolHall => (12, 25),
        BusinessKind.Hotel => (7, 22),
        BusinessKind.Warehouse => (6.5f, 17.5f),
        BusinessKind.Garage => (7.5f, 18),
        BusinessKind.Bakery => (6, 18),
        BusinessKind.Butcher => (7, 18),
        BusinessKind.CigarStore => (7, 21),
        BusinessKind.PictureHouse => (12, 23.5f),
        BusinessKind.DanceHall => (19, 26),
        _ => (8.5f, 18),
    };

    private float WalkHours(int from, int to) =>
        _w.Map.Distance(_w.Map.LotAt(from), _w.Map.LotAt(to)) / Content.ResidentPace;

    private readonly Dictionary<(int Lot, string What), Lot?> _nearest = new();

    /// <summary>The nearest lot of a sort ("park", "dock", "church", "precinct", "bank") to a home, within a walk of so many tiles, or null.</summary>
    private Lot? Nearest(Lot from, string what, int within)
    {
        if (_nearest.TryGetValue((from.Id, what), out var known)) return known;
        Func<Lot, bool> fits = what switch
        {
            "park" => l => l.Terrain == Terrain.Park,
            "dock" => l => l.Terrain == Terrain.Water,
            "church" => l => l.Terrain == Terrain.Church,
            "precinct" => l => l.Use == LotUse.Precinct,
            _ => l => l.Use == LotUse.Bank,
        };
        Lot? best = null;
        int bestDistance = within + 1;
        foreach (var lot in _w.Map.Lots)
        {
            if (!fits(lot)) continue;
            int d = Math.Abs(lot.X - from.X) + Math.Abs(lot.Y - from.Y);
            if (d >= bestDistance) continue;
            best = lot;
            bestDistance = d;
        }
        _nearest[(from.Id, what)] = best;
        return best;
    }

    /// <summary>
    /// Where someone works: the business he's on the books of, or a place for his trade. Factory hands and garment
    /// workers go to the factories, teamsters and railroad men to the warehouses and yards, clerks and typists to the
    /// office towers and the city's own buildings downtown, and the longshoremen to the docks. Anyone whose trade has
    /// no such place near enough, a carpenter or a schoolteacher, works at some business in his own ward.
    /// </summary>
    private Lot? Workplace(Citizen c, Lot home)
    {
        if (c.WorkBusinessId >= 0) return _w.LotOf(_w.BusinessById(c.WorkBusinessId));
        if (c.Job is "Longshoreman" && Nearest(home, "dock", 40) is { } dock) return dock;
        if (c.Job is "Bank clerk" && Nearest(home, "bank", 40) is { } bank) return bank;
        // A quarter of the clerks work for the city.
        var places = c.Job switch
        {
            "Factory hand" => new[] { Filler.Factory, Filler.Gasworks },
            "Ironworker" or "Garment worker" or "Cigar roller" or "Seamstress" => new[] { Filler.Factory },
            "Teamster" or "Longshoreman" => new[] { Filler.Warehouse, Filler.Yard },
            "Railroad man" => new[] { Filler.Yard, Filler.Warehouse },
            "Night watchman" => new[] { Filler.Factory, Filler.Warehouse, Filler.Office },
            "Typist" or "Telephone operator" or "Insurance man" or "Bank clerk" => Roll(c.Id, 656) < 0.25 ? new[] { Filler.None } : new[] { Filler.Office },
            _ => null,
        };
        if (places != null && Commute(home, places, c.Id) is { } place) return place;
        if (c.Job is "Railroad man" && Nearest(home, "dock", 40) is { } wharf) return wharf;
        var nearby = _businessesInWard.GetValueOrDefault(home.WardId);
        if (nearby is not { Count: > 0 }) return null;
        return _w.LotOf(_w.BusinessById(nearby[(int)(Roll(c.Id, 655) * nearby.Count)]));
    }

    /// <summary>How far anyone walks to work, in tiles along the streets; further than that he finds work nearer home.</summary>
    private const int Commutable = 36;

    private readonly Dictionary<(int Home, string Places), List<int>> _commutes = new();

    /// <summary>
    /// One of the few nearest buildings of these sorts to a home, so a street's men go to the same few factories and
    /// not all to the one nearest. Filler.None stands for the city's own buildings.
    /// </summary>
    private Lot? Commute(Lot home, Filler[] places, int id)
    {
        var key = (home.Id, string.Join(",", places));
        if (!_commutes.TryGetValue(key, out var near))
        {
            near = _w.Map.Lots
                .Where(l => l.Terrain == Terrain.Civic ? places.Contains(Filler.None) : _w.Map.FillerOf(l) is var f && f != Filler.None && places.Contains(f))
                .Select(l => (l.Id, Distance: Math.Abs(l.X - home.X) + Math.Abs(l.Y - home.Y)))
                .Where(l => l.Distance <= Commutable)
                .OrderBy(l => l.Distance).ThenBy(l => l.Id)
                .Take(6).Select(l => l.Id).ToList();
            _commutes[key] = near;
        }
        return near.Count == 0 ? null : _w.Map.LotAt(near[(int)(Roll(id, 657) * near.Count)]);
    }

    /// <summary>A few shops or a diner in the ward to go to on a day, in the order she'll go round them.</summary>
    private List<int> Errands(Citizen c, Lot home, int count, int day)
    {
        var shops = (_businessesInWard.GetValueOrDefault(home.WardId) ?? new List<int>())
            .Select(_w.BusinessById)
            .Where(b => b.Kind is BusinessKind.Grocer or BusinessKind.Diner or BusinessKind.Pharmacy or BusinessKind.Tailor or BusinessKind.Laundry or BusinessKind.Hardware or BusinessKind.Haberdasher
                or BusinessKind.Bakery or BusinessKind.Butcher or BusinessKind.Florist)
            .Select(b => b.LotId).Distinct().ToList();
        if (shops.Count == 0) return shops;
        var picked = new List<int>();
        for (int k = 0; k < count * 2 && picked.Count < count; k++)
        {
            int lot = shops[(int)(Roll(c.Id, 661 + k + 11 * day) * shops.Count)];
            if (!picked.Contains(lot)) picked.Add(lot);
        }
        return picked;
    }
}
