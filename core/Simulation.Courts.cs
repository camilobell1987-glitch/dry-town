namespace DryTown.Core;

/// <summary>Arrests, trials and verdicts. Anything worse than a few weeks goes to a jury, and a jury can be helped.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// Lock a man up. A petty charge is settled on the spot; anything longer holds him for trial,
    /// where his gang's lawyer, a paid judge or a frightened jury can still get him off. Every
    /// conviction already on his record makes the sentence longer, and a man with three faces life.
    /// </summary>
    public void Jail(Hood hood, int weeks, string why, bool federal = false, double? strength = null)
    {
        var w = World;
        hood.State = HoodState.Jailed;
        hood.FederalTime = federal;
        if (weeks <= Content.PettySentence)
        {
            hood.JailWeeks = weeks;
            Book(hood, RapKind.Petty, $"Got {weeks} weeks {why}.");
            w.Log(EventKind.HoodJailed, hood.GangId, $"{hood.Name} got {weeks} weeks {why}.");
            return;
        }
        int priors = hood.Convictions;
        bool life = priors >= Content.HabitualOffender;
        weeks = Content.Sentence(weeks, priors);
        hood.JailWeeks = Content.WeeksToTrial;
        double odds = strength ?? (federal ? Content.FederalCase : Content.CityCase);
        // The city's cases are the district attorney's to push or let slide.
        if (!federal) odds += (w.Hall.DAReform ? Content.DaReformCase : 0) - (w.Hall.DAFriendGangId == hood.GangId ? Content.DaFriendCase : 0);
        hood.Case = new Case
        {
            Charge = why, Sentence = weeks, Federal = federal, Strength = odds,
            Prepared = w.GangById(hood.GangId).Lawyer != Lawyer.None, Life = life,
        };
        string faces = life ? "life, with three convictions behind him" : $"{weeks} weeks" + (priors > 0 ? $", more with {priors} conviction{(priors == 1 ? "" : "s")} behind him" : "");
        Book(hood, RapKind.Charged, $"Charged {why}{(federal ? " in federal court" : "")}. Faced {faces}.");
        w.Log(EventKind.HoodJailed, hood.GangId, $"{hood.Name} was charged {why} and is held for trial. He faces {faces}.");
    }

    /// <summary>Put a line on a man's record.</summary>
    private void Book(Hood hood, RapKind kind, string text) => hood.Record.Add(new RapEntry { Week = World.Week, Kind = kind, Text = text });

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
        // A man talking to the DA signs before the jury sits.
        if (hood.Talking > 0 && hood.Loyalty < Content.RatLine(hood)) { Turn(hood); return; }
        hood.Talking = 0;
        var c = hood.Case!;
        var gang = w.GangById(hood.GangId);
        double odds = ConvictionChance(w, hood);
        hood.Case = null;
        if (!w.Rng.Chance(odds))
        {
            hood.State = HoodState.Free;
            hood.Loyalty = Math.Min(100, hood.Loyalty + 5);
            Book(hood, RapKind.Acquitted, $"Acquitted {c.Charge}.");
            w.Log(EventKind.Trial, gang.Id, $"A jury acquitted {hood.Name} of {gang.Name}, charged {c.Charge}. He walked out of court a free man.");
            return;
        }
        if (c.Life)
        {
            Book(hood, RapKind.Convicted, $"Convicted {c.Charge}. Sent away for life as a habitual offender.");
            hood.State = HoodState.Gone;
            hood.Case = null;
            w.LeaveCrew(hood.Id);
            w.Log(EventKind.Trial, gang.Id, $"{hood.Name} of {gang.Name} was convicted {c.Charge}. It was his fourth conviction, and under the habitual offender law he goes away for life.");
            return;
        }
        // The weeks he was held for trial count against the sentence.
        hood.JailWeeks = Math.Max(1, c.Sentence - Content.WeeksToTrial);
        Book(hood, RapKind.Convicted, $"Convicted {c.Charge}. Sent away for {c.Sentence} weeks.");
        w.Log(EventKind.Trial, gang.Id, $"{hood.Name} of {gang.Name} was convicted {c.Charge} and sent away for {c.Sentence} weeks.");
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
