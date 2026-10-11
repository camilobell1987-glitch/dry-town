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
    long gearBought = 0, armedAtEnd = 0, menAtEnd = 0;
    long frontsBought = 0, taxCases = 0, cleanAtEnd = 0, cashAtEnd = 0;
    long trials = 0, acquittals = 0, judgesPaid = 0, juries = 0, lawyered = 0, gangsAtEnd = 0;
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
        foreach (var e in ev.Where(e => e.Kind == EventKind.RacketOpened && e.Text.Contains(" opened a ")))
            foreach (var info in Content.Rackets.Values.Where(r => e.Text.Contains($" opened a {r.Label} ")))
                rackets[info.Kind] = rackets.GetValueOrDefault(info.Kind) + 1;
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
