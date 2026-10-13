using DryTown.Core;

// Headless runner.
//   dotnet run --project sim -- soak --seeds 5 --years 10
//   dotnet run --project sim -- play --seed 42 [--scenario LastCall | --campaign]
//   dotnet run --project sim -- scenarios --seeds 40
//   dotnet run --project sim -- campaigns --seeds 40

string mode = args.Length > 0 ? args[0] : "play";
ulong seed = ulong.Parse(Arg("--seed", "1"));
int years = int.Parse(Arg("--years", "10"));
int seeds = int.Parse(Arg("--seeds", "1"));
var difficulty = Enum.Parse<Difficulty>(Arg("--difficulty", "Normal"), true);
var size = Enum.Parse<CitySize>(Arg("--size", "Small"), true);
var scenario = Enum.Parse<ScenarioKind>(Arg("--scenario", "None"), true);

return mode switch
{
    "soak" => Soak(),
    "play" => Play(),
    "scenarios" => ScenarioRuns(),
    "campaigns" => CampaignRuns(),
    _ => Usage(),
};

string Arg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

int Usage()
{
    Console.WriteLine("usage: soak [--seed N] [--seeds N] [--years N] | play [--seed N] [--scenario name] | scenarios [--seed N] [--seeds N] [--scenario name] | campaigns [--seed N] [--seeds N]");
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
    long localsTaken = 0, localsSeized = 0, strikes = 0, strikesWon = 0, strikesWonFromRivals = 0, localsHeld = 0, localsTotal = 0, docksHeld = 0;
    long warsDeclared = 0, warsOnPlayer = 0, warsCalledOff = 0, warsMadePeace = 0, warsFinished = 0, warsAtEnd = 0;
    long weddings = 0, playerWeddings = 0, marriagesBroken = 0, takenIn = 0, stoodBehind = 0, marriagesAtEnd = 0;
    long funerals = 0, wreaths = 0, playerWreaths = 0, funeralBreaches = 0, playerFuneralBreaches = 0;
    long contractWeeks = 0, playerContractWeeks = 0, contractsWon = 0, playerContractsWon = 0, graftStories = 0;
    long clubWeeks = 0, playerClubWeeks = 0, allWeeks = 0, clubsAtEnd = 0;
    long breweryWeeks = 0, playerBreweryWeeks = 0, prohibitionWeeks = 0, breweriesSmashed = 0, breweriesLegal = 0, breweriesAtEnd = 0;
    long snatches = 0, playerSnatches = 0, playerMenTaken = 0, ransomsPaid = 0, ransomsKilled = 0, ransomsLetGo = 0, ransomsEscaped = 0;
    long fixesTried = 0, fixesWon = 0, playerFixes = 0, fixesCalledOff = 0, fixersTurnedIn = 0, fixWinnings = 0;
    long boatsBought = 0, playerBoats = 0, landings = 0, sunk = 0, dumped = 0, seized = 0, outran = 0, boatsAtEnd = 0;
    long linesBought = 0, shipmentsStopped = 0, linesSwitched = 0, badBatches = 0, linesHeld = 0, linesTotal = 0;
    long gearBought = 0, armedAtEnd = 0, menAtEnd = 0;
    long frontsBought = 0, taxCases = 0, cleanAtEnd = 0, cashAtEnd = 0;
    long trials = 0, acquittals = 0, judgesPaid = 0, juries = 0, lawyered = 0, gangsAtEnd = 0;
    long smallAtEnd = 0, smallStart = 0, absorbed = 0, wipedOut = 0, poached = 0, smallTakeovers = 0, outfitsAtEnd = 0;
    long truces = 0, alliances = 0, tributes = 0, broken = 0, pactsAtEnd = 0;
    long lifers = 0, repeat = 0, convictedAtEnd = 0;
    long asks = 0, asksPassed = 0, breaches = 0, playerBreaches = 0, seatsAtEnd = 0, warsAfter = 0, bossHitsAfter = 0, aliveAtSitdown = 0, aliveAfterSitdown = 0;
    long insideStarts = 0, walkedOut = 0, coups = 0, guardsQuit = 0, moved = 0, successions = 0, playerInside = 0;
    long bails = 0, jumps = 0, foundAway = 0, pickedUp = 0, cameHome = 0, showedUp = 0;
    long namesMade = 0, householdNames = 0, soupAtEnd = 0, bossesAtEnd = 0; double fearAtEnd = 0, respectAtEnd = 0, playerFear = 0, playerRespect = 0; int playerAliveAtEnd = 0;
    long squadWeeks = 0, squadNamed = 0, squadNamedPlayer = 0, squadSmashed = 0, squadEmpty = 0, squadArrests = 0, indictments = 0, indictedAway = 0, indictedPlayer = 0;
    long playerNamed = 0, playerArrests = 0, wireMoves = 0, enemiesNamed = 0, gmenArrests = 0, bossesTaken = 0, casinosAtEnd = 0, slotsAtEnd = 0, wireHeld = 0, aliveAtRepeal = 0, aliveAfterRepeal = 0;
    long cityEvents = 0, running = 0, ownMenWon = 0, pollsWorked = 0, pollsCaught = 0, reformDAs = 0, ownSeatsAtEnd = 0;
    for (ulong s = seed; s < seed + (ulong)seeds; s++)
    {
        var sim = Simulation.New(new WorldSettings { Seed = s, Difficulty = difficulty, Size = size, PoliticsEnabled = !args.Contains("--no-politics") });
        for (int week = 0; week < years * Content.WeeksPerYear; week++)
        {
            sim.AdvanceWeek(playerAutopilot: true);
            allWeeks++;
            contractWeeks += sim.World.Contracts.Count(c => c.GangId >= 0);
            playerContractWeeks += sim.World.Contracts.Count(c => c.GangId == sim.World.Player.Id);
            clubWeeks += sim.World.Outfits.Count(g => sim.World.ClubOf(g.Id) != null);
            if (sim.World.ClubOf(sim.World.Player.Id) != null) playerClubWeeks++;
            if (!sim.World.Prohibition) continue;
            prohibitionWeeks++;
            breweryWeeks += sim.World.Businesses.Count(b => b.Racket == RacketKind.Brewery);
            if (sim.World.BreweryOf(sim.World.Player.Id) != null) playerBreweryWeeks++;
        }

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
        cleanAtEnd += sim.World.Outfits.Sum(g => g.Clean);
        cashAtEnd += sim.World.Outfits.Sum(g => Math.Max(0, g.Cash));
        trials += ev.Count(e => e.Kind == EventKind.Trial && (e.Text.Contains(" was convicted ") || e.Text.StartsWith("A jury acquitted")));
        acquittals += ev.Count(e => e.Kind == EventKind.Trial && e.Text.StartsWith("A jury acquitted"));
        judgesPaid += ev.Count(e => e.Kind == EventKind.Bribe && e.Text.Contains(" paid the judge "));
        juries += ev.Count(e => e.Kind == EventKind.Trial && e.Text.Contains("paid visits to the jurors"));
        lawyered += sim.World.Outfits.Count(g => g.Lawyer != Lawyer.None);
        gangsAtEnd += sim.World.Outfits.Count();
        outfitsAtEnd += sim.World.Outfits.Count();
        smallAtEnd += sim.World.SmallGangs.Count();
        smallStart += sim.World.Settings.SmallGangCount;
        absorbed += ev.Count(e => e.Kind == EventKind.Takeover && e.Text.Contains(" came in with "));
        wipedOut += ev.Count(e => e.Kind == EventKind.Hit && e.Text.Contains(" wiped out "));
        poached += ev.Count(e => e.Kind == EventKind.Recruited && e.Text.Contains(" walked out on "));
        smallTakeovers += ev.Count(e => e.Kind == EventKind.Takeover && sim.World.GangById(e.GangId).IsSmall);
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
        foreach (var g in sim.World.Outfits.Where(g => !g.IsPlayer))
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
        localsTaken += ev.Count(e => e.Kind == EventKind.Union && e.Text.Contains(" took over ") && e.Text.Contains("Its dues go through"));
        localsSeized += ev.Count(e => e.Kind == EventKind.Union && e.Text.Contains(" pushed ") && e.Text.Contains(" out of "));
        strikes += ev.Count(e => e.Kind == EventKind.Union && e.Text.Contains(" walked out of "));
        strikesWon += ev.Count(e => e.Kind == EventKind.Union && e.Text.Contains(" gave in to the pickets"));
        strikesWonFromRivals += ev.Count(e => e.Kind == EventKind.Union && e.Text.Contains(" gave in to the pickets and dropped "));
        localsHeld += sim.World.Locals.Count(l => l.OwnerGangId >= 0);
        localsTotal += sim.World.Locals.Count;
        if (sim.World.DocksGangId >= 0) docksHeld++;
        linesBought += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" to take over "));
        shipmentsStopped += ev.Count(e => e.Kind == EventKind.Hijack && e.Text.Contains(" and sold the load for ") && (e.Text.Contains("'s shipment on ") || e.Text.Contains("'s landing on ")));
        linesSwitched += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" would rather deal with "));
        badBatches += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" went blind "));
        linesHeld += sim.World.SupplyLines.Count(l => l.OwnerGangId >= 0);
        boatsBought += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" bought a fast boat, "));
        playerBoats += ev.Count(e => e.Kind == EventKind.Booze && e.GangId == sim.World.Player.Id && e.Text.Contains(" bought a fast boat, "));
        landings += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" cases of ") && e.Text.Contains(" landed "));
        sunk += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" went down in "));
        dumped += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(" put the cargo over the side"));
        seized += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains(", and seized her."));
        outran += ev.Count(e => e.Kind == EventKind.Booze && e.Text.Contains("A Coast Guard cutter ran "));
        boatsAtEnd += sim.World.Boats.Count;
        var kidnaps = ev.Where(e => e.Kind == EventKind.Kidnap).ToList();
        string playerName = sim.World.Player.Name;
        snatches += kidnaps.Count(e => e.Text.Contains(" off the street. "));
        playerSnatches += kidnaps.Count(e => e.Text.Contains(" off the street. ") && e.GangId == sim.World.Player.Id);
        playerMenTaken += kidnaps.Count(e => e.Text.Contains(" off the street. ") && e.Text.Contains($" of {playerName} off the street."));
        ransomsPaid += kidnaps.Count(e => e.Text.EndsWith(" came home.") && e.Text.Contains(" paid "));
        ransomsKilled += kidnaps.Count(e => e.Text.Contains(" found in the river."));
        ransomsLetGo += kidnaps.Count(e => e.Text.Contains(" let him go anyway."));
        ransomsEscaped += kidnaps.Count(e => e.Text.Contains(" got away from the men "));
        var talks = ev.Where(e => e.Kind == EventKind.Diplomacy).ToList();
        funerals += talks.Count(e => e.Text.Contains(" next week. Until then, nobody who wants"));
        wreaths += talks.Count(e => e.Text.Contains(" sent a wreath to "));
        playerWreaths += talks.Count(e => e.Text.Contains(" sent a wreath to ") && e.GangId == sim.World.Player.Id);
        funeralBreaches += talks.Count(e => e.Text.Contains(" while they were burying "));
        playerFuneralBreaches += talks.Count(e => e.Text.Contains(" while they were burying ") && e.GangId == sim.World.Player.Id);
        var cityHall = ev.Where(e => e.Kind == EventKind.Politics).ToList();
        contractsWon += cityHall.Count(e => e.Text.StartsWith("The aldermen gave "));
        playerContractsWon += cityHall.Count(e => e.Text.StartsWith("The aldermen gave ") && e.GangId == sim.World.Player.Id);
        graftStories += cityHall.Count(e => e.Text.Contains(" The city tore up the contract."));
        var fixes = ev.Where(e => e.Kind == EventKind.Fix).ToList();
        fixesTried += fixes.Count;
        fixesWon += fixes.Count(e => e.Text.Contains(" cleared $"));
        playerFixes += fixes.Count(e => e.GangId == sim.World.Player.Id && e.Text.Contains(" cleared $"));
        fixesCalledOff += fixes.Count(e => e.Text.EndsWith(" called all bets off."));
        fixersTurnedIn += fixes.Count(e => e.Text.EndsWith(" went to the police."));
        foreach (var e in fixes.Where(e => e.Text.Contains(" cleared $")))
            fixWinnings += long.Parse(e.Text.Split(" cleared $")[1].Split(' ')[0].Replace(",", ""));
        breweriesSmashed += ev.Count(e => e.Kind == EventKind.Untouchables && e.Text.StartsWith("The Untouchables smashed ") && e.Text.Contains("'s brewery at "));
        breweriesLegal += ev.Count(e => e.Kind == EventKind.Era && e.Text.Contains(" took out a licence."));
        breweriesAtEnd += sim.World.Businesses.Count(b => b.Racket == RacketKind.Brewery);
        clubsAtEnd += sim.World.Businesses.Count(b => b.Racket == RacketKind.Nightclub);
        weddings += ev.Count(e => e.Kind == EventKind.Diplomacy && e.Text.EndsWith(" The two outfits are family now."));
        playerWeddings += ev.Count(e => e.Kind == EventKind.Diplomacy && e.Text.EndsWith(" The two outfits are family now.") && e.Text.Contains(sim.World.Player.Name));
        marriagesBroken += ev.Count(e => e.Kind == EventKind.Diplomacy && e.Text.Contains(" went home to her father."));
        takenIn += ev.Count(e => e.Kind == EventKind.GangDissolved && e.Text.EndsWith(", its family by marriage."));
        stoodBehind += ev.Count(e => e.Kind == EventKind.Succession && e.Text.Contains(" stood behind "));
        marriagesAtEnd += sim.World.Marriages.Count;
        linesTotal += sim.World.SupplyLines.Count;
        foreach (var (label, phrase) in RatLines)
            ratCounts[label] = ratCounts.GetValueOrDefault(label) + ev.Count(e => e.Kind is EventKind.Rat or EventKind.Whisper && e.Text.Contains(phrase));
        wireMoves += ev.Count(e => e.Kind == EventKind.Gmen && e.Text.Contains(" runs the racing wire now"));
        enemiesNamed += ev.Count(e => e.Kind == EventKind.Gmen && e.Text.Contains(" Public Enemy Number One. "));
        gmenArrests += ev.Count(e => e.Kind == EventKind.Gmen && e.Text.StartsWith("G-men picked up "));
        playerNamed += ev.Count(e => e.Kind == EventKind.Gmen && e.GangId == sim.World.Player.Id && e.Text.Contains(" Public Enemy Number One. "));
        playerArrests += ev.Count(e => e.Kind == EventKind.Gmen && e.GangId == sim.World.Player.Id && e.Text.StartsWith("G-men picked up "));
        bossesTaken += ev.Count(e => e.Kind == EventKind.Gmen && e.Text.Contains("Public Enemy Number One himself"));
        casinosAtEnd += sim.World.Businesses.Count(b => b.Racket == RacketKind.Casino);
        slotsAtEnd += sim.World.Businesses.Count(b => b.Racket == RacketKind.Slots);
        if (sim.World.WireGangId >= 0) wireHeld++;
        insideStarts += ev.Count(e => e.Kind == EventKind.Inside && e.Text.Contains(" from his cell. ") && e.Text.Contains(" minds the street for him"));
        playerInside += ev.Count(e => e.Kind == EventKind.Inside && e.GangId == sim.World.Player.Id && e.Text.Contains(" from his cell. ") && e.Text.Contains(" minds the street for him"));
        walkedOut += ev.Count(e => e.Kind == EventKind.Inside && e.Text.Contains(" walked out and took back the chair"));
        coups += ev.Count(e => e.Kind == EventKind.Inside && e.Text.Contains(" stopped taking orders from a cell."));
        guardsQuit += ev.Count(e => e.Kind == EventKind.Inside && e.Text.StartsWith("The guards stopped carrying "));
        moved += ev.Count(e => e.Kind == EventKind.Inside && e.Text.Contains(" where no word gets out."));
        successions += ev.Count(e => e.Kind == EventKind.Succession);
        bails += ev.Count(e => e.Kind == EventKind.Trial && e.Text.Contains(" bail for ") && e.Text.Contains(" put up $"));
        showedUp += ev.Count(e => e.Kind == EventKind.Trial && e.Text.Contains(" showed up for his trial"));
        jumps += ev.Count(e => e.Kind == EventKind.Lam && e.Text.Contains(" jumped his $"));
        cameHome += ev.Count(e => e.Kind == EventKind.Lam && e.Text.Contains(" is back in town"));
        foundAway += ev.Count(e => e.Kind == EventKind.HoodJailed && e.Text.Contains(" was found out of town and brought back"));
        pickedUp += ev.Count(e => e.Kind == EventKind.HoodJailed && e.Text.StartsWith("Police picked up ") && e.Text.Contains(" on the warrant in his case."));
        namesMade += ev.Count(e => e.Kind == EventKind.Name && e.Text.StartsWith("The papers have started writing about "));
        householdNames += ev.Count(e => e.Kind == EventKind.Name && e.Text.StartsWith("Everybody in the city knows the name "));
        foreach (var g in sim.World.Outfits)
        {
            bossesAtEnd++;
            fearAtEnd += g.Fear;
            respectAtEnd += g.Respect;
            if (g.SoupKitchen) soupAtEnd++;
        }
        if (sim.World.Player.Alive) { playerAliveAtEnd++; playerFear += sim.World.Player.Fear; playerRespect += sim.World.Player.Respect; }
        int squadFrom = (Content.UntouchablesYear - Content.StartYear) * Content.WeeksPerYear;
        squadWeeks += Math.Max(0, Math.Min(sim.World.Week, (Content.RepealYear - Content.StartYear) * Content.WeeksPerYear) - squadFrom);
        squadNamed += ev.Count(e => e.Kind == EventKind.Untouchables && e.Text.StartsWith("The Untouchables are going after "));
        squadNamedPlayer += ev.Count(e => e.Kind == EventKind.Untouchables && e.GangId == sim.World.Player.Id && e.Text.StartsWith("The Untouchables are going after "));
        squadSmashed += ev.Count(e => e.Kind == EventKind.Untouchables && e.Text.StartsWith("The Untouchables smashed "));
        squadEmpty += ev.Count(e => e.Kind == EventKind.Untouchables && e.Text.Contains(" got word in time"));
        squadArrests += ev.Count(e => e.Kind == EventKind.HoodJailed && e.Text.Contains(" with running liquor"));
        indictments += ev.Count(e => e.Kind == EventKind.Untouchables && e.Text.StartsWith("A federal grand jury indicted "));
        indictedPlayer += ev.Count(e => e.Kind == EventKind.Untouchables && e.GangId == sim.World.Player.Id && e.Text.StartsWith("A federal grand jury indicted "));
        indictedAway += ev.Count(e => e.Kind == EventKind.Lam && e.Text.Contains("there's a federal warrant out for him now"));
        int sitdown = sim.World.CommissionStartWeek;
        asks += ev.Count(e => e.Kind == EventKind.Commission && e.Text.StartsWith("The Commission voted "));
        asksPassed += ev.Count(e => e.Kind == EventKind.Commission && e.Text.StartsWith("The Commission voted ") && e.Text.Contains(" to let "));
        breaches += ev.Count(e => e.Kind == EventKind.Commission && e.Text.Contains(" without the Commission's say-so."));
        playerBreaches += ev.Count(e => e.Kind == EventKind.Commission && e.GangId == sim.World.Player.Id && e.Text.Contains(" without the Commission's say-so."));
        seatsAtEnd += sim.World.Members.Count();
        warsAfter += ev.Count(e => e.Week >= sitdown && e.Text.Contains(" went to the mattresses against "));
        bossHitsAfter += ev.Count(e => e.Week >= sitdown && e.Kind == EventKind.Hit && e.Text.Contains(", boss of "));
        var samples = sim.Metrics.Samples;
        if (samples.Count > sitdown && samples[sitdown - 1].PlayerAlive)
        {
            aliveAtSitdown++;
            if (samples[^1].PlayerAlive) aliveAfterSitdown++;
        }
        int repealWeek = (Content.RepealYear - Content.StartYear) * Content.WeeksPerYear;
        if (samples.Count > repealWeek && samples[repealWeek - 1].PlayerAlive)
        {
            aliveAtRepeal++;
            if (samples[^1].PlayerAlive) aliveAfterRepeal++;
        }
        if (samples[Math.Min(samples.Count, 3 * Content.WeeksPerYear) - 1].PlayerAlive) aliveAt3++;
        if (samples[^1].PlayerAlive) aliveAtEnd++;
    }
    Console.WriteLine($"{seeds - stalled}/{seeds} runs stayed contested.");
    Console.WriteLine("Politics:\n  " + string.Join("\n  ", politics));
    Console.WriteLine($"Street crime per city-year: {string.Join(", ", crimes.OrderBy(c => c.Key).Select(c => $"{c.Key} {c.Value / (double)(seeds * years):0.0}"))}.");
    Console.WriteLine($"Guns and cars bought per city-year: {gearBought / (double)(seeds * years):0.0}; men carrying a gun at the end: {armedAtEnd * 100 / Math.Max(1, menAtEnd)}%.");
    Console.WriteLine($"Fronts bought per city-year: {frontsBought / (double)(seeds * years):0.0}; tax cases per city-year: {taxCases / (double)(seeds * years):0.00}; gangs' cash clean at the end: {cleanAtEnd * 100 / Math.Max(1, cashAtEnd)}%.");
    Console.WriteLine($"Small gangs: {smallStart / (double)seeds:0} to start, {smallAtEnd / (double)seeds:0.0} at the end; outfits at the end {outfitsAtEnd / (double)seeds:0.0}; per city-year {absorbed / (double)(seeds * years):0.00} brought in, {wipedOut / (double)(seeds * years):0.00} wiped out, {poached / (double)(seeds * years):0.00} men hired away, {smallTakeovers / (double)(seeds * years):0.0} takeovers by small gangs.");
    Console.WriteLine($"Trials per city-year: {trials / (double)(seeds * years):0.0}, {acquittals * 100 / Math.Max(1, trials)}% acquitted; judges paid {judgesPaid / (double)(seeds * years):0.0}, juries leaned on {juries / (double)(seeds * years):0.00} per city-year; gangs with a lawyer at the end: {lawyered * 100 / Math.Max(1, gangsAtEnd)}%.");
    double cy = seeds * years;
    Console.WriteLine($"Pacts per city-year: truces {truces / cy:0.0}, tribute deals {tributes / cy:0.0}, alliances {alliances / cy:0.0}, broken {broken / cy:0.0}; pacts standing at the end: {pactsAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Life sentences for a fourth conviction per city-year: {lifers / cy:0.00}; men with a conviction at the end: {convictedAtEnd * 100 / Math.Max(1, menAtEnd)}%, with two or more: {repeat * 100 / Math.Max(1, menAtEnd)}%.");
    Console.WriteLine($"Elections per city-year: men run for alderman {running / cy:0.00}, own men elected {ownMenWon / cy:0.00}, weeks worked at the polls {pollsWorked / cy:0.0} with {pollsCaught * 100 / Math.Max(1, pollsWorked + pollsCaught)}% caught; reform DAs elected {reformDAs / cy:0.00}; seats held by an outfit's own man at the end: {ownSeatsAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Random city events per city-year: {cityEvents / cy:0.0}.");
    Console.WriteLine($"Rival crews at the end: {rivalCrews / (double)seeds:0.0} a city, with {crewedMen * 100 / Math.Max(1, rivalMen)}% of rival men in one.");
    Console.WriteLine($"Unions per city-year: locals taken from the members {localsTaken / cy:0.00}, from another outfit {localsSeized / cy:0.00}, strikes {strikes / cy:0.00}, owners who gave in {strikesWon / cy:0.00} ({strikesWonFromRivals / cy:0.00} dropping another outfit); locals run by an outfit at the end: {localsHeld}/{localsTotal}, docks {docksHeld}/{seeds}.");
    Console.WriteLine($"Wars per city-year: declared {warsDeclared / cy:0.00} ({warsOnPlayer / cy:0.00} on your gang), called off {warsCalledOff / cy:0.00}, ended in a pact {warsMadePeace / cy:0.00}, ended with one side finished {warsFinished / cy:0.00}; still going at the end: {warsAtEnd}/{seeds}.");
    Console.WriteLine($"Rum-running per city-year: lines bought {linesBought / cy:0.00}, shipments stopped {shipmentsStopped / cy:0.00}, lines changing hands after one {linesSwitched / cy:0.00}, bad batches {badBatches / cy:0.00}; lines held at the end: {linesHeld}/{linesTotal}.");
    Console.WriteLine($"Rats per city-year: {string.Join(", ", RatLines.Select(r => $"{r.Label} {ratCounts.GetValueOrDefault(r.Label) / cy:0.00}"))}.");
    Console.WriteLine($"On the lam, per city-year: bails posted {bails / cy:0.00} (showed up for trial {showedUp / cy:0.00}), bail jumped {jumps / cy:0.00}, found out of town {foundAway / cy:0.00}, came home {cameHome / cy:0.00}, picked up at home on the warrant {pickedUp / cy:0.00}.");
    double squadYears = Math.Max(1, squadWeeks) / (double)Content.WeeksPerYear;
    if (squadWeeks > 0)
        Console.WriteLine($"The Untouchables, per city-year with the squad in town ({squadYears / seeds:0.0} a city): outfits gone after {squadNamed / squadYears:0.00} ({squadNamedPlayer} times the player's), places smashed {squadSmashed / squadYears:0.00}, raids that came up empty {squadEmpty / squadYears:0.00}, men taken {squadArrests / squadYears:0.00}, bosses indicted {indictments / squadYears:0.00} ({indictedPlayer} times the player's; out of town {indictedAway}).");
    Console.WriteLine($"Marriages per city-year: weddings {weddings / cy:0.00} (the player's {playerWeddings / cy:0.00}), broken {marriagesBroken / cy:0.00}, outfits taken in by family {takenIn / cy:0.00}, splits stopped by in-laws {stoodBehind / cy:0.00}; marriages at the end {marriagesAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Funerals per city-year: {funerals / cy:0.00}, with {wreaths / (double)Math.Max(1, funerals):0.0} wreaths each (the player's {playerWreaths / cy:0.00} a year); peace broken {funeralBreaches / cy:0.00} (by the player {playerFuneralBreaches / cy:0.00}).");
    Console.WriteLine($"City contracts: held by an outfit in an average week {contractWeeks / (double)Math.Max(1, allWeeks):0.00} of 4 (the player's {playerContractWeeks / (double)Math.Max(1, allWeeks):0.00}); won per city-year {contractsWon / cy:0.00} (the player's {playerContractsWon / cy:0.00}); torn up by the papers {graftStories / cy:0.00}.");
    Console.WriteLine($"Nightclubs: running in an average week {clubWeeks / (double)Math.Max(1, allWeeks):0.00} a city (the player has one {playerClubWeeks / (double)Math.Max(1, allWeeks):0%} of the time); clubs at the end {clubsAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Breweries: running in an average Prohibition week {breweryWeeks / (double)Math.Max(1, prohibitionWeeks):0.00} a city (the player has one {playerBreweryWeeks / (double)Math.Max(1, prohibitionWeeks):0%} of the time); smashed by the Untouchables {breweriesSmashed / cy:0.00} per city-year, gone legal at repeal {breweriesLegal / (double)seeds:0.00} a city; breweries at the end {breweriesAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Kidnapping per city-year: men snatched {snatches / cy:0.00} (by the player {playerSnatches / cy:0.00}, of the player's {playerMenTaken / cy:0.00}), ransoms paid {ransomsPaid / cy:0.00}, killed when nobody paid {ransomsKilled / cy:0.00}, let go unpaid {ransomsLetGo / cy:0.00}, got away {ransomsEscaped / cy:0.00}.");
    Console.WriteLine($"Fixed fights and races per city-year: tried {fixesTried / cy:0.00}, paid off {fixesWon / cy:0.00} (the player's {playerFixes / cy:0.00}), bets called off {fixesCalledOff / cy:0.00}, fixer turned in {fixersTurnedIn / cy:0.00}; ${fixWinnings / (double)Math.Max(1, fixesWon):N0} cleared on a fix that paid.");
    Console.WriteLine($"Rum row per city-year: boats bought {boatsBought / cy:0.00} (the player's {playerBoats / cy:0.00}), loads landed {landings / cy:0.00}, sunk {sunk / cy:0.00}, cargo dumped in a storm {dumped / cy:0.00}, seized {seized / cy:0.00}, run off by a cutter {outran / cy:0.00}; boats at the end {boatsAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Reputation: names in the papers {namesMade / cy:0.00} per city-year, household names {householdNames / cy:0.00}; at the end, bosses' fear {fearAtEnd / Math.Max(1, bossesAtEnd):0} and respect {respectAtEnd / Math.Max(1, bossesAtEnd):0} on average (the player's {playerFear / Math.Max(1, playerAliveAtEnd):0} and {playerRespect / Math.Max(1, playerAliveAtEnd):0}), soup kitchens {soupAtEnd / (double)seeds:0.0} a city.");
    Console.WriteLine($"Running it from inside, per city-year: bosses who kept the chair from a cell {insideStarts / cy:0.00} ({playerInside} times the player's), walked back into it {walkedOut / cy:0.00}, lost it to the street boss {coups / cy:0.00}, to the guards' fee {guardsQuit / cy:0.00}, to a transfer {moved / cy:0.00}; ordinary successions {successions / cy:0.00}.");
    double sy = seeds * Math.Max(0, years - (Content.CommissionYear - Content.StartYear) - (Content.CommissionWeek - 1) / (double)Content.WeeksPerYear);
    if (sy > 0)
        Console.WriteLine($"After the Commission sits, per city-year: questions put to the table {asks / sy:0.00} ({asksPassed / sy:0.00} passed), rules broken {breaches / sy:0.00} ({playerBreaches} times by the player's gang), wars declared {warsAfter / sy:0.00}, bosses gunned down {bossHitsAfter / sy:0.00}; " +
            $"seats at the end {seatsAtEnd / (double)seeds:0.0} a city; the player's gang, alive when it sat, was still alive at the end in {aliveAfterSitdown}/{aliveAtSitdown}.");
    double ry = seeds * Math.Max(0, years - (Content.RepealYear - Content.StartYear));
    if (ry > 0)
        Console.WriteLine($"After repeal, per city-year: the wire changing hands {wireMoves / ry:0.00}, public enemies named {enemiesNamed / ry:0.00}, G-men arrests {gmenArrests / ry:0.00} ({bossesTaken / ry:0.00} of the boss); the player's gang named {playerNamed} times, with {playerArrests} arrests; " +
            $"at the end: casinos {casinosAtEnd / (double)seeds:0.0} and slot machines {slotsAtEnd / (double)seeds:0.0} a city, the wire run by an outfit in {wireHeld}/{seeds}; the player's gang, alive at repeal in {aliveAtRepeal}, was still alive at the end in {aliveAfterRepeal}.");
    Console.WriteLine($"Rackets opened: {string.Join(", ", rackets.OrderBy(c => c.Key).Select(c => $"{Content.Rackets[c.Key].Label} {c.Value}"))}.");
    Console.WriteLine($"Your gang on autopilot ({difficulty}): alive after 3 years in {aliveAt3}/{seeds}, after {years} years in {aliveAtEnd}/{seeds}.");
    return stalled == 0 ? 0 : 1;
}

// Each scenario played on autopilot until it's won or lost: how often the planner manages it, and how soon.
int ScenarioRuns()
{
    foreach (var info in Scenarios.All.Where(i => scenario == ScenarioKind.None || i.Kind == scenario))
    {
        int won = 0, finished = 0;
        var weeks = new List<int>();
        for (ulong s = seed; s < seed + (ulong)seeds; s++)
        {
            var sim = Simulation.New(Scenarios.Settings(info.Kind, s));
            var w = sim.World;
            while (w.Goal.Outcome == ScenarioOutcome.Playing) sim.AdvanceWeek(playerAutopilot: true);
            if (w.Goal.Outcome == ScenarioOutcome.Won) { won++; weeks.Add(w.Goal.Week - w.Settings.StartWeek + 1); }
            else if (!w.Player.Alive) finished++;
        }
        weeks.Sort();
        string when = weeks.Count > 0 ? $", in a median {weeks[weeks.Count / 2]} weeks" : "";
        Console.WriteLine($"{info.Title}: won {won}/{seeds}{when}; outfit finished in {finished}.");
    }
    return 0;
}

// Whole campaigns played on autopilot: each chapter until it's won or lost, then on to the next while the outfit stands.
int CampaignRuns()
{
    var wonBy = new int[Campaign.Chapters.Length];
    var reached = new int[Campaign.Chapters.Length];
    var totals = new int[Campaign.Chapters.Length + 1];
    int finishedAll = 0;
    for (ulong s = seed; s < seed + (ulong)seeds; s++)
    {
        var sim = Simulation.New(Campaign.Start(s));
        while (true)
        {
            var w = sim.World;
            int chapter = w.Settings.Campaign!.Chapter;
            reached[chapter]++;
            while (w.Goal.Outcome == ScenarioOutcome.Playing) sim.AdvanceWeek(playerAutopilot: true);
            if (w.Goal.Outcome == ScenarioOutcome.Won) wonBy[chapter]++;
            if (Campaign.CannotGoOn(w) != null)
            {
                int won = w.Settings.Campaign.Won + (w.Goal.Outcome == ScenarioOutcome.Won ? 1 : 0);
                totals[won]++;
                if (chapter + 1 == Campaign.Chapters.Length) finishedAll++;
                break;
            }
            sim = Simulation.New(Campaign.Next(w, s * 31 + (ulong)chapter));
        }
    }
    for (int i = 0; i < Campaign.Chapters.Length; i++)
        Console.WriteLine($"{i + 1}. {Scenarios.Info(Campaign.Chapters[i].Kind).Title}: reached in {reached[i]}/{seeds}, won {wonBy[i]}.");
    Console.WriteLine($"Played to repeal in {finishedAll}/{seeds}. Chapters won: {string.Join(", ", totals.Select((n, i) => $"{i}: {n}"))}.");
    return 0;
}

int Play()
{
    var settings = args.Contains("--campaign") ? Campaign.Start(seed)
        : scenario != ScenarioKind.None ? Scenarios.Settings(scenario, seed) : new WorldSettings { Seed = seed, Size = size };
    var shell = new CommandShell(Simulation.New(settings));
    Console.Write(shell.Welcome);
    while (true)
    {
        Console.Write(shell.Prompt + " > ");
        var line = Console.ReadLine();
        if (line == null) return 0;
        var output = shell.Execute(line);
        if (output == null) return 0;
        Console.Write(output);
        if (shell.NextCity is { } next)
        {
            shell = new CommandShell(next);
            Console.Write(shell.Welcome);
        }
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
