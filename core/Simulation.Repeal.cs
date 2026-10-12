namespace DryTown.Core;

/// <summary>After repeal: the stills go cold, the wire takes its cut, and the G-men hunt Public Enemy Number One.</summary>
public sealed partial class Simulation
{
    /// <summary>The week repeal comes in.</summary>
    private void Repealed()
    {
        var w = World;
        foreach (var b in w.Businesses.Where(b => b.Racket == RacketKind.Still)) b.Racket = RacketKind.None;
        foreach (var b in w.Businesses.Where(b => b.Racket == RacketKind.Brewery && b.IsProtected).OrderBy(b => b.Id))
            w.Log(EventKind.Era, b.ProtectorGangId, $"{w.GangById(b.ProtectorGangId).Name}'s brewery at {b.Name} took out a licence. Its beer is legal now, and so is the money.");
        string room = w.WireBusinessId >= 0 ? $" Whoever runs {w.BusinessById(w.WireBusinessId).Name} will take a cut of every bookie in the city." : "";
        w.Log(EventKind.Era, -1, $"The back-room stills go cold. Slot machines are the new money, and the racing wire has come to town.{room}");
    }

    /// <summary>A bookie's week, after the wire: its own outfit's bookies do better, and everyone else's pay the fee.</summary>
    private int Wire(Gang gang, int income)
    {
        var w = World;
        int wire = w.WireGangId;
        if (wire < 0) return income;
        if (wire == gang.Id) return income + (int)(income * Content.WireBonus);
        int fee = Math.Min(Content.WireFee, income);
        var boss = w.GangById(wire);
        boss.Cash += fee;
        w.Ledger(boss.Id).Wire += fee;
        return income - fee;
    }

    /// <summary>Sunday after repeal: news of the wire changing hands, the Bureau's public enemy, and its raids.</summary>
    private void Gmen()
    {
        var w = World;
        if (w.Prohibition) return;

        int wire = w.WireGangId;
        if (wire != w.Repeal.WireGangId)
        {
            var room = w.BusinessById(w.WireBusinessId);
            w.Log(EventKind.Gmen, wire, wire >= 0
                ? $"{w.GangById(wire).Name} runs the racing wire now, from the back of {room.Name}. Every bookie in the city pays them."
                : $"Nobody runs the racing wire at {room.Name} now.");
            w.Repeal.WireGangId = wire;
        }

        bool newYear = w.Week % Content.WeeksPerYear == 0;
        if (w.PublicEnemy == null && w.Repeal.PublicEnemyGangId >= 0) w.Repeal.PublicEnemyGangId = -1;
        if (newYear || w.PublicEnemy == null && w.Week - w.Repeal.NamedWeek >= Content.WeeksPerYear / 2) Name();

        var enemy = w.PublicEnemy;
        if (enemy == null || !w.Rng.Chance(w.GmenChance(enemy))) return;
        var boss = w.HoodById(enemy.BossHoodId);
        var men = w.AvailableHoodsOf(enemy.Id).Where(h => h.Id != boss.Id).ToList();
        bool takeBoss = boss.IsAvailable && (men.Count == 0 || w.Rng.Chance(Content.GmenTakeBoss));
        if (!takeBoss && men.Count == 0) return;
        var man = takeBoss ? boss : w.Rng.Pick(men);
        w.Log(EventKind.Gmen, enemy.Id, $"G-men picked up {man.Name} of {enemy.Name}{(takeBoss ? ", the Bureau's Public Enemy Number One himself," : "")} on a federal warrant.");
        Jail(man, w.Rng.Range(26, 104), "on a federal warrant", federal: true);
        if (!takeBoss) return;
        w.Repeal.PublicEnemyGangId = -1;
        w.Repeal.NamedWeek = w.Week;
    }

    /// <summary>The Bureau names the biggest and loudest outfit in the city, if it has one worth naming.</summary>
    private void Name()
    {
        var w = World;
        var pick = w.LivingGangs.Select(g => (g, turf: w.TurfOf(g.Id).Count()))
            .Where(x => x.turf >= Content.PublicEnemyTurf)
            .OrderByDescending(x => x.turf * (x.g.Heat + 20) * World.NameDraw(x.g)).ThenBy(x => x.g.Id).Select(x => x.g).FirstOrDefault();
        int was = w.Repeal.PublicEnemyGangId;
        w.Repeal.NamedWeek = w.Week;
        w.Repeal.PublicEnemyGangId = pick?.Id ?? -1;
        if (pick == null || pick.Id == was) return;
        w.Log(EventKind.Gmen, pick.Id, $"The Bureau of Investigation has named {w.HoodById(pick.BossHoodId).Name} of {pick.Name} Public Enemy Number One. G-men can't be bought, and they don't need the precinct's say-so.");
    }
}
