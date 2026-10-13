namespace DryTown.Core;

/// <summary>Sunday's news: events on the calendar, a few at random, and the ones that have run their course.</summary>
public sealed partial class Simulation
{
    private void CityNews()
    {
        var w = World;
        if (!w.Settings.CityEventsEnabled) return;
        // Next week's events start tonight, so they cover all of it.
        int next = w.Week + 1;
        int year = Content.StartYear + next / Content.WeeksPerYear, weekOfYear = next % Content.WeeksPerYear + 1;

        foreach (var e in w.CityEvents.Where(e => e.UntilWeek <= next).ToList())
        {
            w.CityEvents.Remove(e);
            string ended = Content.CityEvents[e.Kind].Ended;
            if (ended != "") w.Log(EventKind.News, -1, w.Describe(e, ended));
        }

        foreach (var (_, _, text) in Content.CityNotices.Where(n => n.Year == year && n.Week == weekOfYear))
            w.Log(EventKind.News, -1, text);
        foreach (var (kind, _, _) in Content.CityCalendar.Where(c => c.Year == year && c.Week == weekOfYear))
            Begin(kind);

        int random = w.CityEvents.Count(e => Content.CityEvents[e.Kind].Weight > 0);
        if (random >= 2 || !w.Rng.Chance(Content.CityEventChance)) return;
        var choices = Content.CityEvents
            .Where(kv => kv.Value.Weight > 0 && weekOfYear - 1 >= kv.Value.FromWeek && weekOfYear - 1 <= kv.Value.ToWeek)
            .Where(kv => !w.Happening(kv.Key) && next - w.CityEventLast.GetValueOrDefault(kv.Key, -1000) >= Content.CityEventRest)
            .Where(kv => w.Wards.Count > 0 || kv.Key is not (CityEventKind.Strike or CityEventKind.Fire))
            .OrderBy(kv => kv.Key).ToList();
        if (choices.Count == 0) return;
        double roll = w.Rng.NextDouble() * choices.Sum(kv => kv.Value.Weight);
        foreach (var (kind, info) in choices)
        {
            if ((roll -= info.Weight) >= 0) continue;
            Begin(kind);
            break;
        }
    }

    /// <summary>Start an event now, and do whatever it does on the day it breaks.</summary>
    public CityEvent Begin(CityEventKind kind, int wardId = -1)
    {
        var w = World;
        var info = Content.CityEvents[kind];
        if (wardId < 0 && kind is CityEventKind.Strike or CityEventKind.Fire && w.Wards.Count > 0) wardId = w.Rng.Range(0, w.Wards.Count - 1);
        var e = new CityEvent { Kind = kind, StartWeek = w.Week + 1, UntilWeek = w.Week + 1 + info.Weeks, WardId = wardId };
        w.CityEvents.Add(e);
        w.CityEventLast[kind] = e.StartWeek;
        w.Log(EventKind.News, -1, w.Describe(e, info.Headline));

        switch (kind)
        {
            case CityEventKind.Crusade:
                w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + 15);
                // The papers go after whoever runs the most of the city.
                var biggest = w.Outfits.OrderByDescending(g => w.TurfOf(g.Id).Count()).ThenBy(g => g.Id).FirstOrDefault();
                if (biggest != null)
                {
                    biggest.Heat = Math.Min(100, biggest.Heat + Content.CrusadeHeat);
                    w.Log(EventKind.News, biggest.Id, $"The Tribune's front page names {biggest.Name} as the worst outfit in the city.");
                }
                break;
            case CityEventKind.Shakeup:
                foreach (var g in w.LivingGangs) g.Heat = Math.Min(100, g.Heat + 10);
                break;
            case CityEventKind.Fire:
                // It starts in one shop and takes the ones nearest it.
                var open = w.Businesses.Where(b => b.IsOpen && w.LotOf(b).WardId == wardId).ToList();
                if (open.Count == 0) break;
                var origin = w.Map.LotAt(w.Rng.Pick(open).LotId);
                foreach (var b in open.OrderBy(b => w.Map.Distance(origin, w.LotOf(b))).ThenBy(b => b.Id).Take(3))
                {
                    b.ShutWeeks = w.Rng.Range(6, 10);
                    b.BurnedWeek = w.Week;
                    if (b.IsProtected) w.Log(EventKind.News, b.ProtectorGangId, $"{b.Name} burned in the fire, and will be shut for {b.ShutWeeks} weeks.");
                }
                break;
        }
        return e;
    }
}
