using DryTown.Core;

// Headless runner.
//   dotnet run --project sim -- soak --seeds 5 --years 10
//   dotnet run --project sim -- play --seed 42

string mode = args.Length > 0 ? args[0] : "play";
ulong seed = ulong.Parse(Arg("--seed", "1"));
int years = int.Parse(Arg("--years", "10"));
int seeds = int.Parse(Arg("--seeds", "1"));

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
    int stalled = 0;
    for (ulong s = seed; s < seed + (ulong)seeds; s++)
    {
        var sim = Simulation.New(new WorldSettings { Seed = s });
        for (int week = 0; week < years * Content.WeeksPerYear; week++) sim.AdvanceWeek(playerAutopilot: true);

        Console.WriteLine($"--- seed {s}, {years} years ---");
        for (int y = 0; y < years; y++) Console.WriteLine(Reports.YearLine(sim, y));
        var report = sim.Metrics.Check();
        Console.WriteLine(report.Stalled ? $"STALLED: {string.Join("; ", report.Problems)}" : "OK: stayed contested every year");
        Console.WriteLine(Reports.Gangs(sim.World));
        if (report.Stalled) stalled++;
    }
    Console.WriteLine($"{seeds - stalled}/{seeds} runs stayed contested.");
    return stalled == 0 ? 0 : 1;
}

int Play()
{
    var shell = new CommandShell(Simulation.New(new WorldSettings { Seed = seed }));
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
