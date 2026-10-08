# Dry Town

An open-ended Prohibition-era crime strategy game, inspired by how *Gangsters: Organized Crime* (1998) plays. It is a new game. It uses none of the original's art, audio, text, code, data or name.

There is no win screen; the city keeps running. Phase 1 built the rules. Phase 2, the current state, makes a week playable: you plan it on a district map, then watch it happen on the streets.

![Planning a week](docs/screenshots/planning.png)

## Layout

| Folder | What it is |
| --- | --- |
| `core/` | The simulation, in plain C# with no Godot dependency. All game rules live here. |
| `sim/` | Console runner: play in the terminal, or soak-test long games. |
| `tests/` | xUnit tests, including the Phase 1 gate (ten-year cities stay contested) and player survival. |
| `godot/` | Godot 4 (.NET) game: district map, planning panels, live week and Sunday report. |

## Run it

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). To play the game, install the **.NET edition** of Godot 4.7, open `godot/project.godot` and press Play. If your editor is a different 4.x version, it updates the version in `DryTown.csproj` when it opens the project.

```sh
dotnet test tests                                                    # 23 tests
dotnet run --project sim -- play --seed 42                           # play in the terminal
dotnet run --project sim -- soak --seeds 40 --years 10               # balance check across many cities
dotnet run --project sim -- soak --seeds 40 --years 10 --difficulty Hard
```

## How a week works

1. **Plan (Monday morning).** Click a business on the map. If it pays nobody, send a man to lean on the owner; the panel shows his chance. If it pays a rival, send a man to take it, and the panel shows his odds if it is unguarded. On your own turf you can set the protection rate, open a racket in the back room, or post a guard for the week. The **Men** tab shows your roster and who is busy. On the **Orders** tab you can cancel orders, or press **Plan for me** to have the AI fill your week.
2. **Watch (Monday to Saturday).** Press **End week**. Each order happens at its own hour. Your men walk the streets from your headquarters to the job, and rival gangs and the police do the same. Rings show the outcome: green for success, red for a lost fight, blue for an arrest. A cross marks where someone died. Shops change colour as they change hands. The ticker logs each event as it happens. You can pause, play at 1×, 2×, 4× or 8×, or skip.
3. **Report (Sunday).** Collectors make their rounds, then the police, the Treasury and the gangs settle up. The **Report** tab shows the books and the week's headlines, with your own in bold.

![The live week on Sunday morning](docs/screenshots/live-week.png)

The **Console** tab and `sim play` take the same text orders: `extort <hood> <business>`, `racket <hood> <business> <kind>`, `guard <hood> <business>`, `recruit`, `bribe <dollars>`, `rate <business> <percent>`, `auto`, `end`.

## Systems

- **The district.** It is a grid of 20 blocks and 160 lots. 48 of them are businesses, and the rest are empty lots, gang headquarters and the precinct house. Owners take a gang less seriously the further its headquarters is: each block beyond the second costs a few points of extortion chance.
- **Protection.** A hood leans on a shop. Success depends on his Intimidation against the owner's toughness. Higher rates pay more but build resentment, and resentful owners talk to the police.
- **Rackets.** These hide behind protected businesses: speakeasies, stills, numbers games and loan books. Liquor rackets only pay well until repeal in 1934.
- **Turf wars and guards.** A hood sent at a rival's business fights whoever answers the door. That is the guard posted there, otherwise the business's handler. A guard fights harder than a handler who gets called in. The rest of the rival gang only backs him up if it isn't spread thin. Losers can die.
- **Heat.** Rackets, violence and sheer size draw police attention. High heat brings raids, fines, closed rackets and arrests. Bribes cool it.
- **Feds.** Treasury tax cases hit gangs holding large amounts of cash. They can't be bribed, they seize money, and they can send the boss away.
- **Loyalty.** Unpaid wages and big gangs breed restless lieutenants. An ambitious one can break away with a faction and the businesses its members handle. A boss who dies or gets a long sentence is succeeded, and a rival heir may split the gang.
- **The rival director.** If one gang holds most of the district, the director sows dissent in that gang and brings in an aggressive outside syndicate sized to challenge it. It also steps in if the district goes quiet or if fewer than two gangs are left. Rival gangs with no men, turf or money fold to make room.
- **Difficulty.** On Easy and Normal the player starts with more men and money than the rivals, and rivals hold back from the player's turf for the first year or two. Hard starts everyone level.

## Balance checks

All gangs, including the player's, were run by the AI planner. A city counts as stalled if, in any year, it has fewer than two gangs at some point, one gang holds over 75% of the district all year, or fewer than 3 businesses change hands.

| Check | Phase 1 | Phase 2 |
| --- | --- | --- |
| 10-year cities contested, seeds 1–40 (Normal) | 40/40 | 40/40 |
| 10-year cities contested, seeds 1–40 (Hard) | n/a | 40/40 |
| 50-year cities contested, seeds 1–10 | 10/10 | 10/10 |
| Player's gang alive after 3 years (Normal) | 8/40 | 35/40 |
| Player's gang alive after 10 years (Normal) | 0/40 | 28/40 |
| Player's gang alive after 3 years (Hard) | n/a | 19/40 |

The AI is a cautious but simple player; a person paying attention should do better.

## Not in Phase 2 yet

- **Crews led by lieutenants.** Orders still go to individual hoods.
- **Mid-week orders.** You can pause the live week, but you can't intervene in it.
- **Art and sound.** The map is drawn with flat shapes and letters: G grocer, D diner, B barber, T tailor, A garage, L laundry, H hotel, Rx pharmacy, 8 pool hall, W warehouse.
