namespace DryTown.Core;

/// <summary>Who can hold a grudge, and who one can be held against.</summary>
public enum Party { Gang, Hood, Cop, Resident }

/// <summary>
/// Bad blood: someone in the city who has it in for a gang, one of its men, a beat cop or another resident, how badly,
/// and why. Grudges fade with time, except that nobody quite forgets a killing.
/// </summary>
public sealed class Grievance
{
    public Party Holder { get; set; }
    public int HolderId { get; set; }
    public Party Target { get; set; }
    public int TargetId { get; set; }

    /// <summary>How badly, 1 to 100, and the least it can fade to.</summary>
    public int Amount { get; set; }
    public int Floor { get; set; }

    /// <summary>What it's over, in a few words: "killed", or "shook them down".</summary>
    public string Why { get; set; } = "";

    /// <summary>Whose harm it's over when it isn't the holder's own, and what they are to the holder: "killed" their son.</summary>
    public int AboutId { get; set; } = -1;
    public Kin AboutKin { get; set; }
    public int Week { get; set; }

    /// <summary>The ward the holder lives in, where they'll make trouble, or -1.</summary>
    public int WardId { get; set; } = -1;

    /// <summary>Whether you know about it.</summary>
    public bool Known { get; set; }

    /// <summary>Sworn for good: it never fades and nothing squares it.</summary>
    public bool Vendetta { get; set; }
}

/// <summary>How bad a grudge has got, from a little prickly to a vendetta there's no coming back from.</summary>
public enum Tier { Prickly, Sore, Scowling, Beefing, Vendetta }

public static partial class Content
{
    /// <summary>Where each tier starts. A grudge that reaches a vendetta stays there.</summary>
    public const int SoreAt = 20, ScowlingAt = 40, BeefingAt = 60, VendettaAt = 85;

    /// <summary>How bad a grudge has to be before someone does something about it, or quits your payroll over it.</summary>
    public const int RevengeGrudge = BeefingAt, QuitGrudge = BeefingAt;

    /// <summary>
    /// How much a grudge eases when the holder gets it off their chest, when you pay them off (and their family with
    /// them), when a bribe lands, when you do them a good turn, and when you put in a good word for their business.
    /// </summary>
    public const int Vented = 35, Squared = 50, KinSquared = 20, Bribed = 25, Favoured = 15, GoodWord = 25;

    /// <summary>How much a soup kitchen in a gang's name eases every grudge against it each week, doubled in the Depression.</summary>
    public const int SoupEases = 2;

    /// <summary>How much a resident's grudge against a beat cop makes the block clam up, and the most it can.</summary>
    public const double CopHated = 0.02, CopHatedMost = 0.1;

    /// <summary>How much likelier a witness comes forward for each grudge against the gang on the block, and the most.</summary>
    public const double AxeToGrind = 0.03, AxeToGrindMost = 0.12;

    public static Tier TierOf(int amount) => amount switch
    {
        >= VendettaAt => Tier.Vendetta,
        >= BeefingAt => Tier.Beefing,
        >= ScowlingAt => Tier.Scowling,
        >= SoreAt => Tier.Sore,
        _ => Tier.Prickly,
    };

    public static string Label(Tier tier) => tier switch
    {
        Tier.Prickly => "Prickly",
        Tier.Sore => "Sore",
        Tier.Scowling => "Scowling",
        Tier.Beefing => "Beefing",
        _ => "Vendetta",
    };

    /// <summary>A grudge in words: "Beefing with".</summary>
    public static string GrudgeWords(int amount) => TierOf(amount) switch
    {
        Tier.Prickly => "Prickly with",
        Tier.Sore => "Sore at",
        Tier.Scowling => "Scowling at",
        Tier.Beefing => "Beefing with",
        _ => "Sworn a vendetta against",
    };

    /// <summary>How much harder someone with a grudge against you is to deal with. A killing they'll leave to you.</summary>
    public static double GrudgeOdds(int amount, Lean lean) => lean switch
    {
        Lean.Bribe or Lean.Intimidate or Lean.Extort or Lean.Blackmail or Lean.Favour or Lean.GoodWord => -amount / 400.0,
        Lean.Square => -amount / 300.0,
        _ => 0,
    };

    /// <summary>
    /// How hard someone takes being wronged. A hothead flares up, the hard-boiled don't let go, the yellow and the sap
    /// swallow it, and the pious turn the other cheek; anyone who'd do anything for their family takes harm to them hardest.
    /// </summary>
    public static double GrudgeTaken(IEnumerable<Trait> traits, bool family)
    {
        double scale = 1;
        foreach (var t in traits)
            scale *= t switch
            {
                Trait.Hothead => 1.5,
                Trait.HardBoiled => 1.2,
                Trait.Yellow or Trait.Sap => 0.7,
                Trait.Pious => 0.7,
                Trait.FamilyMan when family => 1.5,
                Trait.HoldsAGrudge => 1.5,
                _ => 1,
            };
        return scale;
    }

    /// <summary>How fast someone's grudges fade each week. The pious forgive; a hothead cools off as fast as he flares.</summary>
    public static int GrievanceFade(IEnumerable<Trait> traits) =>
        traits.Contains(Trait.HoldsAGrudge) ? 0 : traits.Contains(Trait.Pious) ? 3 : traits.Contains(Trait.Hothead) ? 2 : 1;

    /// <summary>The weekly chance someone does something about a grudge this bad.</summary>
    public static double RevengeChance(int amount, IEnumerable<Trait> traits)
    {
        if (amount < RevengeGrudge) return 0;
        // A vendetta is never far from their mind.
        double chance = (amount - 40) / 400.0 * (amount >= VendettaAt ? 2 : 1);
        foreach (var t in traits)
            chance *= t switch
            {
                Trait.Hothead => 2,
                Trait.HardBoiled or Trait.HoldsAGrudge => 1.3,
                Trait.Yellow => 0.3,
                Trait.Pious => 0.5,
                _ => 1,
            };
        return chance;
    }

    /// <summary>
    /// How badly someone takes what a gang did to them, and how badly their family does. A killing they never get over.
    /// </summary>
    public static int Wronged(Lean lean, bool ok) => (lean, ok) switch
    {
        (Lean.Intimidate, true) => 15,
        (Lean.Intimidate, false) => 10,
        (Lean.Extort, true) => 30,
        (Lean.Extort, false) => 15,
        (Lean.Blackmail, true) => 35,
        (Lean.Blackmail, false) => 15,
        (Lean.Kidnap, true) => 50,
        (Lean.Kidnap, false) => 30,
        (Lean.Kill, false) => 50,
        _ => 0,
    };

    public static int KinWronged(Lean lean, bool ok, Kin kin) => (lean, ok, kin) switch
    {
        (Lean.Kill, true, Kin.Spouse or Kin.Parent or Kin.Child) => 80,
        (Lean.Kill, true, Kin.Sibling) => 60,
        (Lean.Kill, true, Kin.Lover) => 50,
        (Lean.Kill, true, _) => 15,
        (Lean.Kill, false, Kin.Employer) => 0,
        (Lean.Kill, false, _) => 25,
        (Lean.Kidnap, true, Kin.Spouse or Kin.Parent or Kin.Child) => 40,
        (Lean.Kidnap, true, Kin.Sibling) => 25,
        (Lean.Extort or Lean.Blackmail, true, Kin.Spouse) => 10,
        _ => 0,
    };

    public static string WhyWronged(Lean lean, bool ok) => (lean, ok) switch
    {
        (Lean.Intimidate, _) => "leaned on them",
        (Lean.Extort, true) => "shook them down",
        (Lean.Extort, false) => "tried to shake them down",
        (Lean.Blackmail, true) => "is blackmailing them",
        (Lean.Blackmail, false) => "tried to blackmail them",
        (Lean.Kidnap, true) => "snatched them",
        (Lean.Kidnap, false) => "tried to snatch them",
        _ => "tried to kill them",
    };

    public static string WhyKinWronged(Lean lean, bool ok) => (lean, ok) switch
    {
        (Lean.Kill, true) => "killed",
        (Lean.Kill, false) => "tried to kill",
        (Lean.Kidnap, _) => "snatched",
        (Lean.Blackmail, _) => "is blackmailing",
        _ => "shook down",
    };

    /// <summary>What a grudge is over, in words: "killed their son", with whoever it's about named by what they are.</summary>
    public static string Over(Grievance g, bool? aboutWoman = null) => g.AboutId < 0 ? g.Why
        : g.AboutKin == Kin.Employer ? $"{g.Why} someone they work with"
        : aboutWoman is { } woman ? $"{g.Why} their {Label(g.AboutKin, woman).ToLowerInvariant()}"
        : $"{g.Why} their {g.AboutKin switch { Kin.Spouse => "husband or wife", Kin.Child => "child", Kin.Parent => "father or mother", Kin.Sibling => "brother or sister", _ => "sweetheart" }}";

    /// <summary>What someone is to the person they're tied to: a parent's child, a child's parent.</summary>
    public static Kin Reverse(Kin kin) => kin switch
    {
        Kin.Parent => Kin.Child,
        Kin.Child => Kin.Parent,
        _ => kin,
    };

    /// <summary>What it costs to square things with someone: more for someone who's worth more, or angrier.</summary>
    public static int SquarePrice(int worth, int amount) => 10 + worth + amount / 2;
}

public sealed partial class World
{
    /// <summary>All the bad blood in the city.</summary>
    public List<Grievance> Grievances { get; } = new();

    public Grievance? GrievanceOf(Party holder, int holderId, Party target, int targetId) =>
        Grievances.FirstOrDefault(g => g.Holder == holder && g.HolderId == holderId && g.Target == target && g.TargetId == targetId);

    public IEnumerable<Grievance> HeldBy(Party holder, int id) => Grievances.Where(g => g.Holder == holder && g.HolderId == id);
    public IEnumerable<Grievance> HeldAgainst(Party target, int id) => Grievances.Where(g => g.Target == target && g.TargetId == id);

    /// <summary>A resident's traits, as far as the city knows them.</summary>
    private IEnumerable<Trait> TraitsOf(Party who, int id) =>
        who == Party.Resident && Acquaintances.TryGetValue(id, out var a) ? a.Traits : Enumerable.Empty<Trait>();

    /// <summary>
    /// Someone takes against someone else, or takes against them more. How hard depends on the sort they are. Returns the
    /// grudge, or null if they shrugged it off.
    /// </summary>
    public Grievance? Bear(Party holder, int holderId, Party target, int targetId, int amount, string why,
        int wardId = -1, bool known = false, bool family = false, int floor = 0, int aboutId = -1, Kin aboutKin = Kin.Spouse)
    {
        if (amount <= 0 || holder == target && holderId == targetId) return null;
        if (holder == Party.Resident && Acquaintances.TryGetValue(holderId, out var a) && a.Standing == Standing.Dead) return null;
        int scaled = (int)Math.Round(amount * Content.GrudgeTaken(TraitsOf(holder, holderId), family));
        if (scaled <= 0) return null;
        var g = GrievanceOf(holder, holderId, target, targetId);
        if (g == null) Grievances.Add(g = new Grievance { Holder = holder, HolderId = holderId, Target = target, TargetId = targetId });
        if (g.Vendetta) return g;
        g.Amount = Math.Min(100, g.Amount + scaled);
        g.Floor = Math.Min(g.Amount, Math.Max(g.Floor, floor));
        if (g.Amount >= Content.VendettaAt)
        {
            // There's no coming back from this.
            g.Vendetta = true;
            g.Amount = g.Floor = 100;
            if (known && (target == Party.Gang && targetId == Player.Id || target == Party.Hood && Hoods.FirstOrDefault(h => h.Id == targetId)?.GangId == Player.Id))
                Log(EventKind.Street, Player.Id, $"Someone has sworn a vendetta against {(target == Party.Gang ? "you" : Hoods.First(h => h.Id == targetId).Name)}. Nothing will square it now.");
        }
        g.Why = why;
        g.AboutId = aboutId;
        g.AboutKin = aboutKin;
        g.Week = Week;
        if (wardId >= 0) g.WardId = wardId;
        g.Known |= known;
        return g;
    }

    /// <summary>A grudge eases, though never below what can't be forgiven. Returns how much it eased.</summary>
    public int Ease(Grievance g, int amount)
    {
        if (g.Vendetta) return 0;
        int before = g.Amount;
        g.Amount = Math.Max(g.Floor, g.Amount - amount);
        if (g.Amount <= 0) Grievances.Remove(g);
        return before - g.Amount;
    }

    /// <summary>The grudges someone holds against a gang and the men in it.</summary>
    public IEnumerable<Grievance> AgainstGang(Gang gang, Party holder, int holderId) =>
        HeldBy(holder, holderId).Where(g => g.Target == Party.Gang && g.TargetId == gang.Id
            || g.Target == Party.Hood && Hoods.FirstOrDefault(h => h.Id == g.TargetId)?.GangId == gang.Id);

    /// <summary>How badly a resident has it in for a gang or any of its men: all of it, or only what you know of.</summary>
    public int BadBlood(Gang gang, int citizen, bool knownOnly = false) =>
        AgainstGang(gang, Party.Resident, citizen).Where(g => !knownOnly || g.Known).Select(g => g.Amount).DefaultIfEmpty(0).Max();

    /// <summary>Whether a resident has sworn a vendetta against a gang or one of its men.</summary>
    public bool HasVendetta(Gang gang, int citizen, bool knownOnly = false) =>
        AgainstGang(gang, Party.Resident, citizen).Any(g => g.Vendetta && (!knownOnly || g.Known));

    /// <summary>How much harder a grudge makes dealing with someone. A good word goes further from an outfit people respect.</summary>
    public double GrudgeOdds(Gang gang, int citizen, Lean lean, bool knownOnly) =>
        Content.GrudgeOdds(BadBlood(gang, citizen, knownOnly), lean) + (lean == Lean.GoodWord ? gang.Respect / 200 : 0);

    /// <summary>How badly a beat cop has it in for a gang.</summary>
    public int CopGrudge(Gang gang, int cop) => GrievanceOf(Party.Cop, cop, Party.Gang, gang.Id)?.Amount ?? 0;

    /// <summary>The beat cop who walks a ward, if there is one.</summary>
    public StreetPerson? BeatCopOf(int wardId) => People.FirstOrDefault(p => p.Kind == PersonKind.BeatCop && p.WardId == wardId);

    /// <summary>Find out about each grudge someone holds, or is held against them, with this chance. Returns how many were new.</summary>
    public int LearnGrievances(int citizen, double chance)
    {
        int learned = 0;
        foreach (var g in Grievances.Where(g => !g.Known && (g.Holder == Party.Resident && g.HolderId == citizen || g.Target == Party.Resident && g.TargetId == citizen)))
            if (Rng.Chance(chance))
            {
                g.Known = true;
                learned++;
            }
        return learned;
    }

    /// <summary>Whether anyone has a real grudge against a resident, so would gladly talk about them.</summary>
    public bool HasEnemies(int citizen) => HeldAgainst(Party.Resident, citizen).Any(g => g.Amount >= 30);

    /// <summary>"a grudge" words for grudges just heard of.</summary>
    public static string BadBloodWords(int count) => count == 1 ? "of some bad blood" : "of bad blood all round";
}

public sealed partial class Simulation
{
    /// <summary>
    /// When a gang deals with a resident, they and their family take it to heart, and a killing or a snatch on his beat
    /// sets the ward's cop against the gang. Turning someone in sets them against the cop who took them in.
    /// </summary>
    private void Wronged(Gang gang, Hood man, Acquaintance a, int citizen, Lean lean, bool ok, int wardId)
    {
        var w = World;
        int ward = a.WardId >= 0 ? a.WardId : wardId;
        if (lean == Lean.Bribe && ok)
        {
            // Money soothes most things.
            foreach (var g in w.AgainstGang(gang, Party.Resident, citizen).ToList()) w.Ease(g, Content.Bribed);
            return;
        }
        if (lean == Lean.TurnIn)
        {
            if (!ok) return;
            var cop = w.BeatCopOf(ward);
            if (cop != null) w.Bear(Party.Resident, citizen, Party.Cop, cop.Id, 40, "took them in", ward, known: true);
            // Most never work out who tipped off the precinct.
            bool found = w.Rng.Chance(0.35);
            if (found) w.Bear(Party.Resident, citizen, Party.Gang, gang.Id, 35, "turned them in", ward);
            foreach (var tie in a.Ties.Where(t => t.Kin is not (Kin.Employer or Kin.Lover)))
            {
                var them = Content.Reverse(tie.Kin);
                if (cop != null) w.Bear(Party.Resident, tie.Id, Party.Cop, cop.Id, 20, "took in", ward, known: a.KnownTies.Contains(tie.Id), family: true, aboutId: citizen, aboutKin: them);
                if (found) w.Bear(Party.Resident, tie.Id, Party.Gang, gang.Id, 20, "turned in", ward, family: true, aboutId: citizen, aboutKin: them);
            }
            return;
        }
        int hurt = Content.Wronged(lean, ok);
        if (hurt > 0)
        {
            w.Bear(Party.Resident, citizen, Party.Gang, gang.Id, hurt, Content.WhyWronged(lean, ok), ward, known: true);
            // And they won't forget the face of the man who came.
            w.Bear(Party.Resident, citizen, Party.Hood, man.Id, hurt * 3 / 5, Content.WhyWronged(lean, ok), ward, known: true);
        }
        foreach (var tie in a.Ties)
        {
            int kin = Content.KinWronged(lean, ok, tie.Kin);
            if (kin <= 0) continue;
            bool known = a.KnownTies.Contains(tie.Id);
            bool killed = lean == Lean.Kill && ok;
            string why = Content.WhyKinWronged(lean, ok);
            var them = Content.Reverse(tie.Kin);
            w.Bear(Party.Resident, tie.Id, Party.Gang, gang.Id, kin, why, ward, known, family: true, floor: killed && kin >= 60 ? 40 : 0, aboutId: citizen, aboutKin: them);
            // Word gets round who pulled the trigger.
            if (killed) w.Bear(Party.Resident, tie.Id, Party.Hood, man.Id, kin * 7 / 10, why, ward, known, family: true, floor: kin >= 60 ? 30 : 0, aboutId: citizen, aboutKin: them);
        }
        if (lean is Lean.Kill or Lean.Kidnap && w.BeatCopOf(ward) is { } beat)
            w.Bear(Party.Cop, beat.Id, Party.Gang, gang.Id, lean == Lean.Kill ? (ok ? 20 : 8) : (ok ? 10 : 5),
                lean == Lean.Kill ? "a killing on his beat" : "a snatching on his beat", ward, known: true);
    }

    /// <summary>
    /// Sunday for the city's grudges: they fade a little; husbands and wives find out about each other, and brothers fall
    /// out; shopkeepers sick of paying take against the gang that bleeds them; and the worst of it boils over.
    /// </summary>
    private void Grievances()
    {
        var w = World;
        Feuds();
        foreach (var biz in w.Businesses.Where(b => b.IsProtected && b.IsOpen && b.Resentment > 60).OrderBy(b => b.Id))
            foreach (var (id, a) in w.Acquaintances.Where(kv => kv.Value.BusinessId == biz.Id).OrderBy(kv => kv.Key))
                w.Bear(Party.Resident, id, Party.Gang, biz.ProtectorGangId, 2, "bleeds their shop dry", a.WardId, known: biz.ProtectorGangId == w.Player.Id);
        // A soup kitchen in the boss's name softens the neighbourhood towards the whole outfit.
        foreach (var gang in w.Gangs.Where(x => x.Alive && x.SoupKitchen).OrderBy(x => x.Id))
            foreach (var g in w.Grievances.Where(g => g.Holder == Party.Resident && (g.Target == Party.Gang && g.TargetId == gang.Id
                || g.Target == Party.Hood && w.Hoods.FirstOrDefault(h => h.Id == g.TargetId)?.GangId == gang.Id)).ToList())
                w.Ease(g, Content.SoupEases * (w.Depression ? 2 : 1));
        foreach (var g in w.Grievances.OrderBy(g => g.Holder).ThenBy(g => g.HolderId).ThenBy(g => g.Target).ThenBy(g => g.TargetId).ToList())
        {
            if (!w.Grievances.Contains(g)) continue;
            if (Revenge(g)) continue;
            w.Ease(g, g.Holder == Party.Cop ? 2 : Content.GrievanceFade(TraitsOfResident(g.HolderId)));
        }
        // The dead hold no grudges, and nobody holds one against a man who's gone.
        w.Grievances.RemoveAll(g => g.Holder == Party.Resident && w.StandingOf(g.HolderId) == Standing.Dead
            || g.Target == Party.Hood && w.Hoods.FirstOrDefault(h => h.Id == g.TargetId) is not { IsActive: true }
            || g.Target == Party.Gang && w.GangById(g.TargetId) is not { Alive: true }
            || g.Holder == Party.Cop && w.PersonById(g.HolderId) == null);
    }

    private IEnumerable<Trait> TraitsOfResident(int id) => World.Acquaintances.TryGetValue(id, out var a) ? a.Traits : Enumerable.Empty<Trait>();

    /// <summary>Affairs come out and families fall out, among the people you know of.</summary>
    private void Feuds()
    {
        var w = World;
        foreach (var (id, a) in w.Acquaintances.Where(kv => kv.Value.Standing != Standing.Dead).OrderBy(kv => kv.Key).ToList())
        {
            var spouse = a.Ties.Where(t => t.Kin == Kin.Spouse).Cast<Tie?>().FirstOrDefault();
            var affair = a.Secrets.Where(s => s.Vice == Vice.Lover && s.Other >= 0).Cast<Secret?>().FirstOrDefault();
            if (spouse is { } sp && affair is { } af && w.GrievanceOf(Party.Resident, sp.Id, Party.Resident, af.Other) == null && w.Rng.Chance(0.03))
            {
                bool known = a.KnownVices.Contains(Vice.Lover);
                w.Bear(Party.Resident, sp.Id, Party.Resident, af.Other, 60, "is carrying on with", a.WardId, known, family: true, floor: 20, aboutId: id, aboutKin: Kin.Spouse);
                w.Bear(Party.Resident, sp.Id, Party.Resident, id, 40, "cheated on them", a.WardId, known, family: true);
                if (known) w.Log(EventKind.Street, w.Player.Id, "Someone's husband or wife found out about the affair you knew of. There's bad blood now.");
            }
            var sibling = a.Ties.Where(t => t.Kin == Kin.Sibling).Cast<Tie?>().FirstOrDefault();
            if (sibling is { } sib && w.GrievanceOf(Party.Resident, id, Party.Resident, sib.Id) == null && w.Rng.Chance(0.005))
            {
                string why = w.Rng.Chance(0.5) ? "fell out with them over money" : "fell out with them over their mother's will";
                w.Bear(Party.Resident, id, Party.Resident, sib.Id, 25, why, a.WardId);
                w.Bear(Party.Resident, sib.Id, Party.Resident, id, 25, why, a.WardId);
            }
        }
    }

    /// <summary>
    /// Someone does something about a grudge: goes to the police about the man who hurt them, to the papers about his
    /// gang, puts a brick through a window, tells the papers about a crooked cop, or shops a neighbour. Returns whether
    /// they did, which gets it off their chest.
    /// </summary>
    private bool Revenge(Grievance g)
    {
        var w = World;
        if (g.Holder != Party.Resident) return false;
        var traits = TraitsOfResident(g.HolderId).ToList();
        if (!w.Rng.Chance(Content.RevengeChance(g.Amount, traits))) return false;
        var lot = WardLot(g.WardId);
        switch (g.Target)
        {
            case Party.Hood:
            {
                var hood = w.Hoods.FirstOrDefault(h => h.Id == g.TargetId);
                var gang = hood == null ? null : w.GangById(hood.GangId);
                if (hood is not { IsAvailable: true } || gang is not { Alive: true } || lot == null) return false;
                // A vendetta and a temper: they come for him with a gun.
                if (g.Vendetta && hood.Id != gang.BossHoodId && (traits.Contains(Trait.Hothead) || traits.Contains(Trait.HardBoiled)))
                {
                    if (w.Rng.Chance(0.3)) Kill(hood, "in the street by someone who'd sworn a vendetta against him");
                    else w.Log(EventKind.Street, gang.Id, $"Someone with a vendetta against {hood.Name} took a shot at him in the street, and missed.");
                    return true;
                }
                w.NewPerson(PersonKind.Witness, lot, 3, gang.Id, hood.Id, $"what {hood.Name} did");
                w.Log(EventKind.Street, gang.Id, $"Someone who has it in for {hood.Name} went to the police about him. A witness is ready to testify.");
                break;
            }
            case Party.Gang:
            {
                var gang = w.GangById(g.TargetId);
                if (gang is not { Alive: true }) return false;
                var window = g.WardId < 0 ? null : w.Businesses.Where(b => b.ProtectorGangId == gang.Id && b.IsOpen && w.LotOf(b).WardId == g.WardId).OrderBy(b => b.Id).FirstOrDefault();
                if (window != null && (traits.Contains(Trait.Hothead) || traits.Contains(Trait.HardBoiled)))
                {
                    window.ShutWeeks = Math.Max(window.ShutWeeks, 1);
                    w.Log(EventKind.Street, gang.Id, $"Someone with a grudge against {gang.Name} put a brick through the window of {window.Name}. It's shut for the week.");
                }
                else if (lot != null && w.Rng.Chance(0.5))
                {
                    w.NewPerson(PersonKind.Reporter, lot, 4, gang.Id, -1, $"what {gang.Name} {Content.Over(g)}");
                    w.Log(EventKind.Street, gang.Id, $"Someone who says {gang.Name} {Content.Over(g)} has been talking to a reporter.");
                }
                else
                {
                    gang.Heat = Math.Min(100, gang.Heat + 5);
                    w.Log(EventKind.Street, gang.Id, $"Someone with a grudge against {gang.Name} has been talking to the police.");
                }
                break;
            }
            case Party.Cop:
            {
                var cop = w.PersonById(g.TargetId);
                var payer = cop is { PaidByGangId: >= 0 } ? w.GangById(cop.PaidByGangId) : null;
                if (cop == null || payer is not { Alive: true }) return false;
                // He's on the take, and they can prove it.
                payer.Heat = Math.Min(100, payer.Heat + 6);
                w.Log(EventKind.Street, payer.Id, $"Someone who hates {cop.Name} told the papers he takes {payer.Name}'s money.");
                break;
            }
            default:
            {
                if (!w.Acquaintances.TryGetValue(g.TargetId, out var target) || target.Standing is Standing.Dead or Standing.Jailed or Standing.Held) return false;
                var crimes = target.Secrets.Where(s => Content.IsCrime(s.Vice)).ToList();
                if (crimes.Count > 0 && w.Rng.Chance(0.5))
                {
                    var crime = crimes[0];
                    // They go to the police with what they know.
                    var (months, _) = Sentence(crime, target.Worth);
                    target.Standing = Standing.Jailed;
                    target.GangId = -1;
                    target.UntilWeek = w.Week + Math.Max(1, months * Content.WeeksPerYear / 12);
                    target.Note = $"Serving {Content.Term(months)} for {Content.Crime(crime)}, turned in by someone with a grudge. Due out in {Content.StartYear + target.UntilWeek / Content.WeeksPerYear}.";
                    w.LearnSecret(g.TargetId, crime);
                    if (g.Known || target.Known != Fact.None) w.Log(EventKind.Street, w.Player.Id, "Someone you know of was turned in to the police by a neighbour with a grudge.");
                }
                else
                {
                    // They tell the whole block what they know.
                    var unknown = target.UnknownSecrets.ToList();
                    if (unknown.Count == 0) return false;
                    w.LearnSecret(g.TargetId, unknown[0]);
                    g.Known = true;
                    w.Log(EventKind.Street, w.Player.Id, "Someone with a grudge has been telling the whole block what a neighbour is hiding. Word reached you.");
                }
                break;
            }
        }
        w.Ease(g, Content.Vented);
        return true;
    }

    /// <summary>A lot somewhere in a ward, for a witness or reporter to turn up on.</summary>
    private Lot? WardLot(int wardId) =>
        wardId < 0 ? null : World.Map.Lots.Where(l => l.WardId == wardId && l.Terrain == Terrain.Land).OrderBy(l => l.Id).FirstOrDefault();
}
