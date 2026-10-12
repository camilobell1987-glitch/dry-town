namespace DryTown.Core;

/// <summary>Election work on the street: men leaning on voters outside the polling places.</summary>
public sealed partial class Simulation
{
    private void WorkPolls(Gang gang, PollsOrder o)
    {
        var w = World;
        if (o.WardId < 0 || o.WardId >= w.Wards.Count || Politics.PollsOpen(w) is not { } race) return;
        if (!TryUseHood(gang, o.HoodId, out var man)) return;
        var ward = w.Wards[o.WardId];
        int lot = Politics.PollingPlace(w, ward, gang.HqLotId);
        string text;
        if (w.Rng.Chance(Politics.PollsRisk(w, ward, man)))
        {
            gang.Heat = Math.Min(100, gang.Heat + 6);
            w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + 2);
            text = $"Police picked up {man.Name} leaning on voters in {ward.Name}.";
            Jail(man, w.Rng.Range(12, 26), "with intimidating voters");
            w.Act(new ScriptAction(ActionKind.Polls, gang.Id, man.Id, gang.HqLotId, lot, -1, ActionResult.Arrested, text, CasualtyHoodId: man.Id));
            return;
        }
        int votes = Politics.PollsVotes(w, ward, man);
        var pot = race.Kind == ElectionKind.Mayor ? w.Hall.Polls : ward.Polls;
        pot[gang.Id] = pot.GetValueOrDefault(gang.Id) + votes;
        gang.Heat = Math.Min(100, gang.Heat + 3);
        w.Hall.Outrage = Math.Min(100, w.Hall.Outrage + 1);
        string who = race.Kind == ElectionKind.Mayor ? "the machine's ticket" : ward.Candidates.TryGetValue(gang.Id, out var c) ? w.HoodById(c).Name : $"{gang.Name}'s man";
        text = $"{man.Name} worked the polls in {ward.Name}. A few hundred voters know to pick {who} now.";
        w.Log(EventKind.Politics, gang.Id, text);
        w.Act(new ScriptAction(ActionKind.Polls, gang.Id, man.Id, gang.HqLotId, lot, -1, ActionResult.Success, text));
    }
}
