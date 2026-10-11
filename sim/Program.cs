using DryTown.Core;

// Headless runner.
//   dotnet run --project sim -- soak --seeds 5 --years 10
//   dotnet run --project sim -- play --seed 42

string mode = args.Length > 0 ? args[0] : "play";
ulong seed = ulong.Parse(Arg("--seed", "1"));
int years = int.Parse(Arg("--years", "10"));
int seeds = int.Parse(Arg("--seeds", "1"));
var difficulty = Enum.Parse<Difficulty>(Arg("--difficulty", "Normal"), true);
var size = Enum.Parse<CitySize>(Arg("--size", "Small"), true);

return mode switch
{
    "soak" => Soak(),
    "play" => Play(),
    _ => Usage(),
};

string Arg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

int Usage()
{
    Console.WriteLine("usage: soak [--seed N] [--seeds N] [--years N] | play [--seed N]");
    return 2;
}

int Soak()
{
    int stalled = 0, aliveAt3 = 0, aliveAtEnd = 0;
    var politics = new List<string>();
    var crimes = new Dictionary<EventKind, int>();
    var rackets = new Dictionary<RacketKind, int>();
    var ratCounts = new Dictionary<string, int>();
    long rivalCrews = 0, crewedMen = 0, rivalMen = 0;
    long warsDeclared = 0, warsOnPlayer = 0, warsCalledOff = 0, warsMadePeace = 0, warsFinished = 0, warsAtEnd = 0;
    long linesBought = 0, shipmentsStopped = 0, linesSwitched = 0, badBatches = 0, linesHeld = 0, linesTotal = 0;
    long gearBought = 0, armedAtEnd = 0, menAtEnd = 0;
    long frontsBought = 0, taxCases = 0, cleanAtEnd = 0, cashAtEnd = 0;
    long trials = 0, acquittals = 0, judgesPaid = 0, juries = 0, lawyered = 0, gangsAtEnd = 0;
    long truces = 0, alliances = 0, tributes = 0, broken = 0, pactsAtEnd = 0;
    long lifers = 0, repeat = 0, convictedAtEnd = 0;
    long cityEvents = 0, running = 0, ownMenWon = 0, pollsWorked = 0, pollsCaught = 0, reformDAs = 0, ownSeatsAtEnd = 0;
    for (ulong s = seed; s < seed + (ulong)seeds; s++)
    {
        var sim = Simulation.New(new WorldSettings { Seed = s, Difficulty = difficulty, Size = size, PoliticsEnabled = !args.Contains("--no-politics") });
        for (int week = 0; week < years * Content.WeeksPerYear; week++) sim.AdvanceWeek(playerAutopilot: true);

        Console.WriteLine($"--- seed {s}, {years} years ---");
        for (int y = 0; y < years; y++) Console.WriteLine(Reports.YearLine(sim, y));
        var report = sim.Metrics.Check();
        Console.WriteLine(report.Stalled ? $"STALLED: {string.Join("; ", report.Problems)}" : "OK: stayed contested every year");
        Console.WriteLine(Reports.Gangs(sim.World));
        if (report.Stalled) stalled++;
        var ev = sim.World.Events;
        politics.Add($"seed {s}: payoffs {ev.Count(e => e.Kind == EventKind.Politics && e.Text.Contains("on the payroll"))}, " +
            $"reformers {ev.Count(e => e.Text.StartsWith("Reformer"))}, gang-won wards {ev.Count(e => e.Text.Contains("money behind him"))}, " +
            $"mayor friends {ev.Count(e => e.Text.Contains("paid for it"))}, wards owned at end {sim.World.Wards.Count(x => x.OwnerGangId >= 0)}/{sim.World.Wards.Count}");
        foreach (var e in ev.Where(e => e.Kind is EventKind.Arson or EventKind.Hit or EventKind.Robbery or EventKind.Hijack))
            crimes[e.Kind] = crimes.GetValueOrDefault(e.Kind) + 1;
        gearBought += ev.Count(e => e.Kind == EventKind.Gear);
        armedAtEnd += sim.World.Hoods.Count(h => h.IsActive && h.Weapon != Weapon.None);
        menAtEnd += sim.World.Hoods.Count(h => h.IsActive);
        frontsBought += ev.Count(e => e.Kind == EventKind.Front);
        taxCases += ev.Count(e => e.Text.StartsWith("Treasury agents brought a tax case") || e.Text.StartsWith("Treasury agents seized"));
        cleanAtEnd += sim.World.LivingGangs.Sum(g => g.Clean);
        cashAtEnd += sim.World.LivingGangs.Sum(g => Math.Max(0, g.Cash));
        trials += ev.Count(e => e.Kind == EventKind.Trial && (e.Text.Contains(" was convicted ") || e.Text.StartsWith("A jury acquitted")));
        acquittals += ev.Count(e => e.Kind == EventKind.Trial && e.Text.StartsWith("A jury acquitted"));
        judgesPaid += ev.Count(e => e.Kind == EventKind.Bribe && e.Text.Contains(" paid the judge "));
        juries += ev.Count(e => e.Kind == EventKind.Trial && e.Text.Contains("paid visits to the jurors"));
        lawyered += sim.World.LivingGangs.Count(g => g.Lawyer != Lawyer.None);
        gangsAtEnd += sim.World.LivingGangs.Count();
        truces += ev.Count(e => e.Kind == EventKind.Diplomacy && e.Text.EndsWith("agreed a truce."));
        alliances += ev.Count(e => e.Kind == EventKind.Diplomacy && e.Text.Contains(" are allies now"));
        tributes += ev.Count(e => e.Kind == EventKind.Diplomacy && e.Text.Contains(" a week to be left alone"));
        broken += ev.Count(e => e.Kind == EventKind.Diplomacy && e.Text.Contains(" broke the "));
        pactsAtEnd += sim.World.Pacts.Count;
        lifers += ev.Count(e => e.Text.Contains("habitual offender law"));
        running += ev.Count(e => e.Text.Contains(" is running for alderman in "));
        var randomHeadlines = Content.CityEvents.Values.Where(i => i.Weight > 0).Select(i => i.Headline.Split("{ward}")[0]).ToList();
        cityEvents += ev.Count(e => e.Kind == EventKind.News && e.GangId < 0 && randomHeadlines.Any(h => e.Text.StartsWith(h)));
        ownMenWon += ev.Count(e => e.Text.Contains("leaves the street for a seat on the council"));
        pollsWorked += ev.Count(e => e.Text.Contains(" worked the polls in "));
        pollsCaught += ev.Count(e => e.Text.Contains("charged with intimidating voters"));
        reformDAs += ev.Count(e => e.Text.Contains("every case in the city courts will be pushed harder"));
        ownSeatsAtEnd += sim.World.Wards.Count(x => x.OwnMan);
        repeat += sim.World.Hoods.Count(h => h.IsActive && h.Convictions >= 2);
        convictedAtEnd += sim.World.Hoods.Count(h => h.IsActive && h.Convictions >= 1);
        foreach (var e in ev.Where(e => e.Kind == EventKind.RacketOpened && e.Text.Contains(" opened a ")))
            foreach (var info in Content.Rackets.Values.Where(r => e.Text.Contains($" opened a {r.Label} ")))
                rackets[info.Kind] = rackets.GetValueOrDefault(info.Kind) + 1;
        foreach (var g in sim.World.LivingGangs.Where(g => !g.IsPlayer))
        {
            rivalCrews += sim.World.CrewsOf(g.Id).Count();
            crewedMen += sim.World.CrewsOf(g.Id).Sum(c => c.Everyone.Count());
            rivalMen += sim.World.HoodsOf(g.Id).Count();
        }
        warsDeclared += ev.Count(e => e.Text.Contains(" went to the mattresses against "));
        warsOnPlayer += ev.Count(e => e.Text.Contains($" went to the mattresses against {sim.World.Player.Name}."));
        warsCalledOff += ev.Count(e => e.Text.Contains(" called off the war with "));
        warsMadePeace += ev.Count(e => e.Text.Contains(" is over, and the men are home from the mattresses."));
        warsFinished += ev.Count(e => e.Text.Contains("'s men came home from the mattresses."));
        warsAtEnd += sim.World.Wars.Count;
        linesBought += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" to take over "));
        shipmentsStopped += ev.Count(e => e.Kind == EventKind.Hijack && e.Text.Contains(" and sold the load for ") && (e.Text.Contains("'s shipment on ") || e.Text.Contains("'s landing on ")));
        linesSwitched += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" would rather deal with "));
        badBatches += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" went blind "));
        linesHeld += sim.World.SupplyLines.Count(l => l.OwnerGangId >= 0);
        linesTotal += sim.World.SupplyLines.Count;
        foreach (var (label, phrase) in RatLines)
            ratCounts[label] = ratCounts.GetValueOrDefault(label) + ev.Count(e => e.Kind is EventKind.Rat or EventKind.Whisper && e.Text.Contains(phrase));
        var samples = sim.Metrics.Samples;
        if (samples[Math.Min(samples.Count, 3 * Content.WeeksPerYear) - 1].PlayerAlive) aliveAt3++;
        if (samples[^1].PlayerAlive) aliveAtEnd++;
    }
    Console.WriteLine($"{seeds - stalled}/{seeds} runs stayed contested.");
    Console.WriteLine("Politics:\n  " + string.Join("\n  ", politics));
    Console.WriteLine($"Street crime per city-year: {string.Join(", ", crimes.OrderBy(c => c.Key).Select(c => $"{c.Key} {c.Value / (double)(seeds * years):0.0}"))}.");
    Console.WriteLine($"Guns and cars bought per city-year: {gearBought / (double)(seeds * years):0.0}; men carrying a gun at the end: {armedAtEnd * 100 / Math.Max(1, menAtEnd)}%.");
    Console.WriteLine($"Fronts bought per city-year: {frontsBought / (double)(seeds * years):0.0}; tax cases per city-year: {taxCases / (double)(seeds * years):0.00}; gangs' cash clean at the end: {cleanAtEnd * 100 / Math.Max(1, cashAtEnd)}%.");
    Console.WriteLine($"Trials per city-year: {trials / (double)(seeds * years):0.0}, {acquittals * 100 / Math.Max(1, trials)}% acquitted; judges paid {judgesPaid / (double)(seeds * years):0.0}, juries leaned on {juries / (double)(seeds * years):0.00} per city-year; gangs with a lawyer at the end: {lawyered * 100 / Math.Max(1, gangsAtEnd)}%.");
    double cy = seeds * years;
    Console.WriteLine($"Pacts per city-year: truces {truces / cy:0.0}, tribute deals {tributes / cy:0.0}, alliances {alliances / cy:0.0}, broken {broken / cy:0.0}; pacts standing at the end: {pactsAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Life sentences for a fourth conviction per city-year: {lifers / cy:0.00}; men with a conviction at the end: {convictedAtEnd * 100 / Math.Max(1, menAtEnd)}%, with two or more: {repeat * 100 / Math.Max(1, menAtEnd)}%.");
    Console.WriteLine($"Elections per city-year: men run for alderman {running / cy:0.00}, own men elected {ownMenWon / cy:0.00}, weeks worked at the polls {pollsWorked / cy:0.0} with {pollsCaught * 100 / Math.Max(1, pollsWorked + pollsCaught)}% caught; reform DAs elected {reformDAs / cy:0.00}; seats held by an outfit's own man at the end: {ownSeatsAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Random city events per city-year: {cityEvents / cy:0.0}.");
    Console.WriteLine($"Rival crews at the end: {rivalCrews / (double)seeds:0.0} a city, with {crewedMen * 100 / Math.Max(1, rivalMen)}% of rival men in one.");
    Console.WriteLine($"Wars per city-year: declared {warsDeclared / cy:0.00} ({warsOnPlayer / cy:0.00} on your gang), called off {warsCalledOff / cy:0.00}, ended in a pact {warsMadePeace / cy:0.00}, ended with one side finished {warsFinished / cy:0.00}; still going at the end: {warsAtEnd}/{seeds}.");
    Console.WriteLine($"Rum-running per city-year: lines bought {linesBought / cy:0.00}, shipments stopped {shipmentsStopped / cy:0.00}, lines changing hands after one {linesSwitched / cy:0.00}, bad batches {badBatches / cy:0.00}; lines held at the end: {linesHeld}/{linesTotal}.");
    Console.WriteLine($"Rats per city-year: {string.Join(", ", RatLines.Select(r => $"{r.Label} {ratCounts.GetValueOrDefault(r.Label) / cy:0.00}"))}.");
    Console.WriteLine($"Rackets opened: {string.Join(", ", rackets.OrderBy(c => c.Key).Select(c => $"{Content.Rackets[c.Key].Label} {c.Value}"))}.");
    Console.WriteLine($"Your gang on autopilot ({difficulty}): alive after 3 years in {aliveAt3}/{seeds}, after {years} years in {aliveAtEnd}/{seeds}.");
    return stalled == 0 ? 0 : 1;
}

int Play()
{
    var shell = new CommandShell(Simulation.New(new WorldSettings { Seed = seed, Size = size }));
    Console.Write(shell.Welcome);
    while (true)
    {
        Console.Write(shell.Prompt + " > ");
        var line = Console.ReadLine();
        if (line == null) return 0;
        var output = shell.Execute(line);
        if (output == null) return 0;
        Console.Write(output);
    }
}

partial class Program
{
    /// <summary>What the soak counts about rats, by a phrase from the log line.</summary>
    static readonly (string Label, string Phrase)[] RatLines =
    {
        ("DA visits", "has had visits from the DA's office"),
        ("turned", "turned state's evidence against"),
        ("talked out of it", "sent the DA's men away"),
        ("families paid", "'s family while he's inside"),
        ("silenced in a cell", "hanged in his cell"),
        ("guards caught the knife", "with a knife meant for"),
        ("informer cases", "Somebody is talking."),
        ("informers found", "has been talking to the police"),
        ("plants", "has signed on with"),
        ("plants found", "is ours. He'll have to get out"),
        ("plants killed", "had found out he was ours"),
        ("tip-offs", "this week"),
    };
}
