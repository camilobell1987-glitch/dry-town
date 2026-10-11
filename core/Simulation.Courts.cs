namespace DryTown.Core;

/// <summary>Arrests, trials and verdicts. Anything worse than a few weeks goes to a jury, and a jury can be helped.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// Lock a man up. A petty charge is settled on the spot; anything longer holds him for trial,
    /// where his gang's lawyer, a paid judge or a frightened jury can still get him off.
    /// </summary>
    public void Jail(Hood hood, int weeks, string why, bool federal = false, double? strength = null)
    {
        hood.State = HoodState.Jailed;
        if (weeks <= Content.PettySentence)
        {
            hood.JailWeeks = weeks;
            World.Log(EventKind.HoodJailed, hood.GangId, $"{hood.Name} got {weeks} weeks {why}.");
            return;
        }
        hood.JailWeeks = Content.WeeksToTrial;
        hood.Case = new Case
        {
            Charge = why, Sentence = weeks, Federal = federal, Strength = strength ?? (federal ? Content.FederalCase : Content.CityCase),
            Prepared = World.GangById(hood.GangId).Lawyer != Lawyer.None,
        };
        World.Log(EventKind.HoodJailed, hood.GangId, $"{hood.Name} was charged {why} and is held for trial. He faces {weeks} weeks.");
    }

    /// <summary>How likely the jury is to convict him as things stand.</summary>
    public static double ConvictionChance(World w, Hood hood)
    {
        var c = hood.Case;
        if (c == null) return 0;
        // A lawyer brought in after the arrest has only weeks to prepare, and does half as much.
        double odds = c.Strength - Content.Lawyers[w.GangById(hood.GangId).Lawyer].Cut * (c.Prepared ? 1 : 0.5);
        if (c.JudgePaid) odds -= Content.JudgeCut;
        if (c.JuryFixed) odds -= Content.JuryCut;
        return Math.Clamp(odds, 0.05, 0.95);
    }

    private void Trial(Hood hood)
    {
        var w = World;
        var c = hood.Case!;
        var gang = w.GangById(hood.GangId);
        double odds = ConvictionChance(w, hood);
        hood.Case = null;
        if (!w.Rng.Chance(odds))
        {
            hood.State = HoodState.Free;
            hood.Loyalty = Math.Min(100, hood.Loyalty + 5);
            w.Log(EventKind.Trial, gang.Id, $"A jury acquitted {hood.Name} of {gang.Name}, charged {c.Charge}. He walked out of court a free man.");
            return;
        }
        // The weeks he was held for trial count against the sentence.
        hood.JailWeeks = Math.Max(1, c.Sentence - Content.WeeksToTrial);
        w.Log(EventKind.Trial, gang.Id, $"{hood.Name} of {gang.Name} was convicted {c.Charge} and sent away for {c.Sentence} weeks.");
        // A man going away for a long time who owes his outfit nothing may make a deal.
        if (hood.Loyalty < 30 && c.Sentence >= 26 && w.Rng.Chance(0.3))
        {
            gang.Heat = Math.Min(100, gang.Heat + 15);
            hood.Loyalty = 0;
            w.Log(EventKind.Squeal, gang.Id, $"{hood.Name} turned state's evidence against {gang.Name} for a shorter stretch.");
        }
    }

    /// <summary>Chance a man can get to the jurors in a case and change their minds without being caught.</summary>
    public static double JuryChance(Hood man) => Math.Clamp(0.3 + man.Intimidation * 0.045, 0.3, 0.75);

    private void Jury(Gang gang, JuryOrder o)
    {
        var w = World;
        var defendant = w.Hoods.FirstOrDefault(h => h.Id == o.DefendantId);
        if (defendant is not { State: HoodState.Jailed, Case: { JuryFixed: false } } || defendant.GangId != gang.Id) return;
        if (!TryUseHood(gang, o.HoodId, out var man)) return;
        int court = w.PrecinctNear(w.Map.LotAt(gang.HqLotId)).Id;
        bool ok = w.Rng.Chance(JuryChance(man));
        string text;
        if (ok)
        {
            defendant.Case!.JuryFixed = true;
            gang.Heat = Math.Min(100, gang.Heat + 6);
            text = $"{man.Name} paid visits to the jurors in {defendant.Name}'s case. Several of them have doubts now.";
            w.Log(EventKind.Trial, gang.Id, text);
        }
        else
        {
            gang.Heat = Math.Min(100, gang.Heat + 8);
            text = $"A juror in {defendant.Name}'s case went to the police about {man.Name}.";
            Jail(man, w.Rng.Range(26, 52), "with jury tampering");
        }
        w.Act(new ScriptAction(ActionKind.Silence, gang.Id, man.Id, gang.HqLotId, court, -1, ok ? ActionResult.Success : ActionResult.Arrested, text,
            CasualtyHoodId: ok ? -1 : man.Id));
    }

    /// <summary>The lawyers' fees, on Sunday with the wages. A lawyer who isn't paid stops taking the gang's calls.</summary>
    private void PayLawyers()
    {
        var w = World;
        foreach (var gang in w.LivingGangs.Where(g => g.Lawyer != Lawyer.None))
        {
            int fee = Content.Lawyers[gang.Lawyer].Weekly;
            if (gang.Cash < fee)
            {
                w.Log(EventKind.Trial, gang.Id, $"{gang.Name} couldn't pay {Content.Lawyers[gang.Lawyer].Label}, and he dropped them.");
                gang.Lawyer = Lawyer.None;
                continue;
            }
            gang.Cash -= fee;
            w.Ledger(gang.Id).Spending += fee;
        }
    }
}
