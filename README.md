# Dry Town: Phase 1 rules prototype

An open-ended Prohibition-era crime strategy game, inspired by how *Gangsters: Organized Crime* (1998) plays. It is a new game. It uses none of the original's art, audio, text, code, data or name.

This is Phase 1 of the plan: a headless rules core covering one district, protection, rackets, police heat and rival gangs, with text-only reports. It has no win screen; the city keeps running.

## Layout

| Folder | What it is |
| --- | --- |
| `core/` | The simulation, in plain C# with no Godot dependency. All game rules live here. |
| `sim/` | Console runner: play in the terminal, or soak-test long games. |
| `tests/` | xUnit tests, including the Phase 1 gate (ten-year cities stay contested). |
| `godot/` | Godot 4 (.NET) project: a text planning screen on top of the same core. |

## Run it

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). For the Godot screen, also install the **.NET edition** of Godot 4.

```sh
dotnet test tests                                          # 18 tests
dotnet run --project sim -- play --seed 42                 # play in the terminal
dotnet run --project sim -- soak --seeds 40 --years 10     # balance check across many cities
```

In Godot, open `godot/project.godot` and press Play. The C# solution is `godot/DryTown.sln`. The project targets Godot 4.7; if your editor is a different 4.x version, it updates the version in `DryTown.csproj` when it opens the project.

## How a week works

1. **Plan.** Queue orders: `extort <hood> <business>`, `racket <hood> <business> <kind>`, `recruit`, `bribe <dollars>`, `rate <business> <percent>`. Use `auto` to have the AI plan for you, then edit.
2. **Run.** `end` resolves the week for every gang at once, in a seeded random order.
3. **Read.** The ledger and headlines show protection, racket income, wages, fines, takeovers, raids, arrests and killings.

## Systems

- **Protection.** A hood leans on a shop. Success depends on his Intimidation against the owner's toughness. Higher rates pay more but build resentment, and resentful owners talk to the police.
- **Rackets.** These hide behind protected businesses: speakeasies, stills, numbers games and loan books. Liquor rackets only pay well until repeal in 1934.
- **Turf wars.** A hood sent at a rival's business fights that business's handler. The rest of the rival gang only backs him up if it isn't spread thin, so sprawling gangs fray at the edges. Losers can die.
- **Heat.** Rackets, violence and sheer size draw police attention. High heat brings raids, fines, closed rackets and arrests. Bribes cool it.
- **Feds.** Treasury tax cases hit gangs holding large amounts of cash. They can't be bribed, they seize money, and they can send the boss away.
- **Loyalty.** Unpaid wages and big gangs breed restless lieutenants. An ambitious one can break away with his businesses and friends. A boss who dies or gets a long sentence is succeeded, and a rival heir may split the gang.
- **The rival director.** If one gang holds most of the district, the director sows dissent in that gang and brings in an outside syndicate sized to challenge it. It also steps in if the district goes quiet or if fewer than two gangs are left.

## Phase 1 gate results

The gate is that a ten-year city never stalls. A city counts as stalled if, in any year, it has fewer than two gangs at some point, one gang holds over 75% of the district all year, or fewer than 3 businesses change hands. All gangs, including the player's, were run by the AI planner.

- 10-year runs, seeds 1 to 40: **40 of 40 stayed contested.**
- 50-year runs, seeds 1 to 10: **10 of 10 stayed contested.**

**Known issue for Phase 2:** on autopilot, the player's starting gang is usually wiped out within its first three years. The city carries on, but for a human player that is too harsh. Phase 2 should give the player a sturdier start, and Phase 4's dynasty rules should let a fallen family rebuild.
