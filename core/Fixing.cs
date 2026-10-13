namespace DryTown.Core;

/// <summary>Fixed fights and races: a fighter paid to go down, a jockey paid to pull his horse, and the outfit's bookies on the right side of it.</summary>
public static partial class Content
{
    /// <summary>The fights at the arena come every fourth week. Races run every week of the season at the track, from mid-April to October.</summary>
    public const int FightEvery = 4, RaceSeasonFrom = 15, RaceSeasonTo = 44;

    /// <summary>What it costs to buy the fighter or the jockey, and the title fight, which costs far more and pays three times as much.</summary>
    public const int FightFix = 800, RaceFix = 400, TitleFix = 2500, TitlePays = 3;

    /// <summary>What a fixed fight or race pays: money laid off around town, and more for each of the outfit's own bookies taking bets on it.</summary>
    public const int FightLayoff = 1200, FightPerBookie = 300, RaceLayoff = 600, RacePerBookie = 150;

    /// <summary>What each other outfit's bookie pays out on a fixed fight or race. It comes out of their pockets.</summary>
    public const int FightLoss = 100, RaceLoss = 50;

    /// <summary>The wire tells its own outfit's bookies the result first: a fixed race pays them this much more.</summary>
    public const double WireFix = 0.5;

    /// <summary>
    /// Chance the boxing commission or the stewards smell it and call the bets off, and what each point of heat adds.
    /// When they do, the man who arranged it may be charged, and the outfits that would have paid hold it against the fixer.
    /// </summary>
    public const double FixFound = 0.1, FixFoundPerHeat = 0.002, FixCharged = 0.5;
    public const int FixHeat = 3, FixFoundHeat = 10, GrudgeFix = 8;

    /// <summary>Chance a fighter or jockey who won't take the money goes to the police about the man who offered it.</summary>
    public const double FixTalks = 0.25;

    /// <summary>A rival fixes the card at this chance a week, when it runs a bookie and has three times the price to spare.</summary>
    public const double AiFix = 0.15;

    public static readonly string[] Fighters =
    {
        "Kid Gavilan", "Irish Mickey Walsh", "Battling Rocco", "Young Stribling", "Sailor Tom Kearns", "Benny 'the Ghetto Wizard' Levine",
        "Jack 'Kid' Berg", "Tiger Jack Fox", "Kingfish Levinsky", "Dynamite Joe Kowalski", "Two-Ton Tony Galento", "Gentleman Jim Doyle",
    };

    public static readonly string[] Horses =
    {
        "Gallant Fox", "Blue Larkspur", "Twenty Grand", "Sun Beau", "Clyde Van Dusen", "Reigh Count",
        "Black Gold", "Zev", "Morvich", "Bubbling Over", "Whiskery", "Equipoise",
    };
}

public enum Bout { None, Fight, Title, Race }

public sealed partial class World
{
    /// <summary>The outfit that fixed this week's card, or -1, and the week it did. Over by Sunday, so never saved.</summary>
    private int _fixedBy = -1, _fixedWeek = -1;

    /// <summary>What's on this week: the title fight when it comes to town, the fights at the arena, the races in season, or nothing.</summary>
    public Bout Card =>
        Happening(CityEventKind.TitleFight) ? Bout.Title
        : WeekOfYear % Content.FightEvery == 0 ? Bout.Fight
        : WeekOfYear >= Content.RaceSeasonFrom && WeekOfYear <= Content.RaceSeasonTo ? Bout.Race
        : Bout.None;

    /// <summary>Who's on the card this week: the fighter a fix would put on the canvas, or the favourite a fix would pull up.</summary>
    public string CardName(Bout bout = Bout.None)
    {
        bout = bout == Bout.None ? Card : bout;
        return bout == Bout.Race ? Content.Horses[Week % Content.Horses.Length] : Content.Fighters[Week % Content.Fighters.Length];
    }

    /// <summary>What's on the card, in a few words.</summary>
    public string CardLabel(Bout bout = Bout.None)
    {
        bout = bout == Bout.None ? Card : bout;
        return bout switch
        {
            Bout.Title => $"the heavyweight title fight, with {CardName(bout)} the champion",
            Bout.Fight => $"Friday night's fights at the arena, with {CardName(bout)} the favourite in the main event",
            Bout.Race => $"Saturday's races at the track, with {CardName(bout)} the favourite in the feature",
            _ => "nothing",
        };
    }

    public static int FixPrice(Bout bout) => bout switch { Bout.Title => Content.TitleFix, Bout.Fight => Content.FightFix, Bout.Race => Content.RaceFix, _ => 0 };

    /// <summary>What a fix on this week's card would pay the gang, if the bets aren't called off.</summary>
    public int FixPays(Gang g, Bout bout = Bout.None)
    {
        bout = bout == Bout.None ? Card : bout;
        int bookies = Bookies(g.Id);
        double pay = bout switch
        {
            Bout.Title => (Content.FightLayoff + Content.FightPerBookie * bookies) * Content.TitlePays,
            Bout.Fight => Content.FightLayoff + Content.FightPerBookie * bookies,
            Bout.Race => (Content.RaceLayoff + Content.RacePerBookie * bookies) * (WireGangId == g.Id ? 1 + Content.WireFix : 1),
            _ => 0,
        };
        return (int)pay;
    }

    /// <summary>What each other outfit's bookie pays out on a fixed card.</summary>
    public static int FixLoss(Bout bout) => bout switch { Bout.Title => Content.FightLoss * Content.TitlePays, Bout.Fight => Content.FightLoss, Bout.Race => Content.RaceLoss, _ => 0 };

    public int Bookies(int gangId) => TurfOf(gangId).Count(b => b.Racket == RacketKind.Bookie && b.IsOpen);

    /// <summary>Chance the commission or the stewards smell a fix the gang put in.</summary>
    public double FixFoundChance(Gang g) => Math.Min(0.5, Content.FixFound + g.Heat * Content.FixFoundPerHeat);

    /// <summary>Chance a man gets to the fighter or the jockey and he takes the money: brains to find the way in, and a little menace.</summary>
    public static double FixChance(Hood man) => Math.Clamp(0.4 + man.Brains * 0.04 + man.Intimidation * 0.02, 0.4, 0.9);

    /// <summary>Why the gang can't fix this week's card, or null if it can.</summary>
    public string? CannotFix(Gang g)
    {
        var bout = Card;
        if (bout == Bout.None) return "There's nothing on this week: the track is closed for the winter and the next fights are at the end of the month.";
        if (_fixedWeek == Week && _fixedBy == g.Id) return "You've already got to him.";
        if (_fixedWeek == Week) return "Somebody else got to him first.";
        return g.Cash < FixPrice(bout) ? $"He wants ${FixPrice(bout):N0}." : null;
    }

    /// <summary>
    /// The fix, once the man gets to the fighter or jockey. Returns what happened. The bets are settled at once:
    /// the gang collects unless the commission or the stewards call them off, and every other outfit's bookies pay.
    /// </summary>
    public string Fix(Gang g, Hood man, out bool talked)
    {
        talked = false;
        var bout = Card;
        int price = FixPrice(bout);
        string who = CardName(bout);
        g.Heat = Math.Min(100, g.Heat + Content.FixHeat);
        if (!Rng.Chance(FixChance(man)))
        {
            talked = Rng.Chance(Content.FixTalks);
            string no = bout == Bout.Race ? $"{who}'s jockey wouldn't hear of it" : $"{who} wouldn't hear of it";
            string text = talked ? $"{man.Name} of {g.Name} offered to fix {CardLabel(bout)}, but {no} and went to the police."
                : $"{man.Name} of {g.Name} tried to fix {CardLabel(bout)}, but {no}.";
            Log(EventKind.Fix, g.Id, text);
            return text;
        }

        g.Cash -= price;
        Ledger(g.Id).Spending += price;
        _fixedBy = g.Id;
        _fixedWeek = Week;
        string how = bout == Bout.Race ? $"{who}'s jockey pulled him up in the stretch" : $"{who} went down in the fourth";
        if (Rng.Chance(FixFoundChance(g)))
        {
            g.Heat = Math.Min(100, g.Heat + Content.FixFoundHeat);
            string body = bout == Bout.Race ? "the stewards" : "the boxing commission";
            foreach (var rival in Outfits.Where(r => r.Id != g.Id && Bookies(r.Id) > 0)) AddGrudge(rival.Id, g.Id, Content.GrudgeFix);
            string found = $"{man.Name} of {g.Name} fixed {CardLabel(bout)}, and {how}, but {body} smelled it and called all bets off.";
            Log(EventKind.Fix, g.Id, found);
            return found;
        }

        int pays = FixPays(g, bout);
        g.Cash += pays;
        Ledger(g.Id).Rackets += pays;
        int loss = FixLoss(bout);
        foreach (var rival in Outfits.Where(r => r.Id != g.Id))
        {
            long lost = Math.Min(Math.Max(0, rival.Cash), (long)Bookies(rival.Id) * loss);
            if (lost <= 0) continue;
            rival.Cash -= lost;
            Ledger(rival.Id).Fines += lost;
            if (rival.IsPlayer) Log(EventKind.Whisper, rival.Id, $"Your bookies paid out ${lost:N0} on {CardLabel(bout)}. Word is somebody got to {who}.");
        }
        string done = $"{man.Name} of {g.Name} fixed {CardLabel(bout)}. {Capital(how)}, and {g.Name} cleared ${pays:N0} on it.";
        Log(EventKind.Fix, g.Id, done);
        return done;
    }
}

public sealed partial class Simulation
{
    private void FixCard(Gang gang, FixOrder o)
    {
        var w = World;
        if (w.CannotFix(gang) != null) return;
        if (!TryUseHood(gang, o.HoodId, out var man)) return;
        w.Fix(gang, man, out bool talked);
        if (talked && w.Rng.Chance(Content.FixCharged))
            Jail(man, w.Rng.Range(13, 26), w.Card == Bout.Race ? "with trying to fix a race" : "with trying to fix a fight");
    }
}
