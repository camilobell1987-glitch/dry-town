using System.Text.Json.Serialization;

namespace DryTown.Core;

/// <summary>Orders a gang gives during the planning phase, or during the week. They resolve at their hour.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ExtortOrder), "extort")]
[JsonDerivedType(typeof(RacketOrder), "racket")]
[JsonDerivedType(typeof(GuardOrder), "guard")]
[JsonDerivedType(typeof(RecruitOrder), "recruit")]
[JsonDerivedType(typeof(BribeOrder), "bribe")]
[JsonDerivedType(typeof(SetRateOrder), "rate")]
[JsonDerivedType(typeof(PayoffOrder), "payoff")]
[JsonDerivedType(typeof(CampaignOrder), "campaign")]
[JsonDerivedType(typeof(ArsonOrder), "arson")]
[JsonDerivedType(typeof(HitOrder), "hit")]
[JsonDerivedType(typeof(RobberyOrder), "robbery")]
[JsonDerivedType(typeof(HijackOrder), "hijack")]
[JsonDerivedType(typeof(HireOrder), "hire")]
[JsonDerivedType(typeof(PayCopOrder), "paycop")]
[JsonDerivedType(typeof(BuyStoryOrder), "buystory")]
[JsonDerivedType(typeof(SilenceOrder), "silence")]
[JsonDerivedType(typeof(AskOrder), "ask")]
[JsonDerivedType(typeof(TailOrder), "tail")]
[JsonDerivedType(typeof(LeanOrder), "lean")]
[JsonDerivedType(typeof(ArmOrder), "arm")]
[JsonDerivedType(typeof(CarOrder), "car")]
[JsonDerivedType(typeof(FrontOrder), "front")]
[JsonDerivedType(typeof(JuryOrder), "jury")]
[JsonDerivedType(typeof(LawyerOrder), "lawyer")]
[JsonDerivedType(typeof(JudgeOrder), "judge")]
[JsonDerivedType(typeof(BailOrder), "bail")]
[JsonDerivedType(typeof(SoupOrder), "soup")]
[JsonDerivedType(typeof(LamOrder), "lam")]
[JsonDerivedType(typeof(HomeOrder), "home")]
[JsonDerivedType(typeof(StandOrder), "stand")]
[JsonDerivedType(typeof(PollsOrder), "polls")]
[JsonDerivedType(typeof(HuntOrder), "hunt")]
[JsonDerivedType(typeof(SeenToOrder), "seento")]
[JsonDerivedType(typeof(PlantOrder), "plant")]
[JsonDerivedType(typeof(RecallOrder), "recall")]
[JsonDerivedType(typeof(FamilyOrder), "family")]
[JsonDerivedType(typeof(WaylayOrder), "waylay")]
[JsonDerivedType(typeof(LineOrder), "line")]
[JsonDerivedType(typeof(CutOrder), "cut")]
[JsonDerivedType(typeof(BoatOrder), "boat")]
[JsonDerivedType(typeof(SailOrder), "sail")]
[JsonDerivedType(typeof(UnionOrder), "union")]
[JsonDerivedType(typeof(StrikeOrder), "strike")]
[JsonDerivedType(typeof(PoachOrder), "poach")]
[JsonDerivedType(typeof(AbsorbOrder), "absorb")]
[JsonDerivedType(typeof(WipeOutOrder), "wipeout")]
public abstract record Order(int GangId);

/// <summary>
/// Send a hood to lean on a business. Works on unprotected or rival-protected businesses.
/// Backup men go with him and add their weight.
/// </summary>
public sealed record ExtortOrder(int GangId, int HoodId, int BusinessId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}

/// <summary>Open a racket behind a business the gang protects.</summary>
public sealed record RacketOrder(int GangId, int HoodId, int BusinessId, RacketKind Racket) : Order(GangId);

/// <summary>Station a hood at one of the gang's businesses for the week. He fights anyone who comes for it.</summary>
public sealed record GuardOrder(int GangId, int HoodId, int BusinessId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}

/// <summary>Hire a new hood, or bring a young relative of the boss into the business.</summary>
public sealed record RecruitOrder(int GangId, bool Family = false) : Order(GangId);

/// <summary>Pay off police. Amount is spent in units of Content.BribeUnit.</summary>
public sealed record BribeOrder(int GangId, int Amount) : Order(GangId);

/// <summary>Change the weekly protection rate (percent of takings) on one business.</summary>
public sealed record SetRateOrder(int GangId, int BusinessId, int RatePercent) : Order(GangId);

/// <summary>Put a ward's alderman on the payroll: a one-off sweetener, then his envelope every Sunday.</summary>
public sealed record PayoffOrder(int GangId, int WardId) : Order(GangId);

/// <summary>Put money behind a candidate while an election is coming. WardId -1 is the mayor's race.</summary>
public sealed record CampaignOrder(int GangId, int WardId, int Amount) : Order(GangId);

/// <summary>Put one of the gang's men on the ballot for alderman in a ward.</summary>
public sealed record StandOrder(int GangId, int HoodId, int WardId) : Order(GangId);

/// <summary>Send a man to lean on voters in a ward in the last weeks of a campaign, for whichever race is on.</summary>
public sealed record PollsOrder(int GangId, int HoodId, int WardId) : Order(GangId);

/// <summary>Burn down a business a rival protects. It stays shut for weeks and its racket is lost.</summary>
public sealed record ArsonOrder(int GangId, int HoodId, int BusinessId) : Order(GangId);

/// <summary>Send a hood after a named man in another gang, with backup if he's well protected.</summary>
public sealed record HitOrder(int GangId, int HoodId, int TargetHoodId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}

/// <summary>Hold up a bank. The man in front plans it; the backup carries the guns.</summary>
public sealed record RobberyOrder(int GangId, int HoodId, int BankId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}

/// <summary>Stop the beer truck on its way to a rival's speakeasy or still and drive off with the load.</summary>
public sealed record HijackOrder(int GangId, int HoodId, int BusinessId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}

/// <summary>Sign on a drifter, at his asking price.</summary>
public sealed record HireOrder(int GangId, int PersonId) : Order(GangId);

/// <summary>Pay the beat cop to look the other way this week.</summary>
public sealed record PayCopOrder(int GangId, int PersonId) : Order(GangId);

/// <summary>Buy a reporter's story about the gang before it runs.</summary>
public sealed record BuyStoryOrder(int GangId, int PersonId) : Order(GangId);

/// <summary>Send a hood to persuade a witness to forget what he saw.</summary>
public sealed record SilenceOrder(int GangId, int HoodId, int PersonId) : Order(GangId);

/// <summary>Buy a man a gun first thing Monday, trading in the one he has.</summary>
public sealed record ArmOrder(int GangId, int HoodId, Weapon Weapon) : Order(GangId);

/// <summary>Buy a man a car first thing Monday, trading in the one he has.</summary>
public sealed record CarOrder(int GangId, int HoodId, Car Car) : Order(GangId);

/// <summary>Buy a business the gang protects outright, as a front to wash money through.</summary>
public sealed record FrontOrder(int GangId, int BusinessId) : Order(GangId);

/// <summary>Send a man to lean on the jurors in a case against one of the gang's own.</summary>
public sealed record JuryOrder(int GangId, int HoodId, int DefendantId) : Order(GangId);

/// <summary>Put a lawyer on retainer, change him, or let him go.</summary>
public sealed record LawyerOrder(int GangId, Lawyer Lawyer) : Order(GangId);

/// <summary>Pay off the judge in the case against one of the gang's men.</summary>
public sealed record JudgeOrder(int GangId, int DefendantId) : Order(GangId);

/// <summary>Open or close a soup kitchen in the boss's name.</summary>
public sealed record SoupOrder(int GangId, bool On) : Order(GangId);

/// <summary>Put up a man's bail, so he's out until his trial.</summary>
public sealed record BailOrder(int GangId, int HoodId) : Order(GangId);

/// <summary>Send a man out of town: jumping his bail if he's out on it, else lying low.</summary>
public sealed record LamOrder(int GangId, int HoodId) : Order(GangId);

/// <summary>Bring a man home from out of town.</summary>
public sealed record HomeOrder(int GangId, int HoodId) : Order(GangId);

/// <summary>Send a man to ask around the outfit for whoever is talking to the police, or working for someone else.</summary>
public sealed record HuntOrder(int GangId, int HoodId) : Order(GangId);

/// <summary>Have a rat taken care of: a man in a cell who is talking to the DA, or one found out on the street.</summary>
public sealed record SeenToOrder(int GangId, int HoodId, int TargetHoodId) : Order(GangId);

/// <summary>Send a man to sign on with a rival outfit and report back what it's planning.</summary>
public sealed record PlantOrder(int GangId, int HoodId, int TargetGangId) : Order(GangId);

/// <summary>Bring a planted man home.</summary>
public sealed record RecallOrder(int GangId, int HoodId) : Order(GangId);

/// <summary>Look after the family of a man in jail, so he has less reason to take the DA's deal.</summary>
public sealed record FamilyOrder(int GangId, int HoodId) : Order(GangId);

/// <summary>Stop the shipment on a rival's supply line, on the road or at the landing, and sell the load.</summary>
public sealed record WaylayOrder(int GangId, int HoodId, int LineId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}

/// <summary>Strike a deal with the suppliers on a line nobody holds.</summary>
public sealed record LineOrder(int GangId, int LineId) : Order(GangId);

/// <summary>Change how far the gang stretches its liquor.</summary>
public sealed record CutOrder(int GangId, Cut Cut) : Order(GangId);

/// <summary>Go down to a union hall and take over the local, from its members or from the outfit that runs it.</summary>
public sealed record UnionOrder(int GangId, int HoodId, int LocalId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}

/// <summary>Pull a local the gang runs out of a shop until its owner pays.</summary>
public sealed record StrikeOrder(int GangId, int LocalId, int BusinessId) : Order(GangId);

/// <summary>Buy a fast boat to run liquor in from outside.</summary>
public sealed record BoatOrder(int GangId) : Order(GangId);

/// <summary>Send one of the gang's boats up to St. Pierre or down to Nassau for a load.</summary>
public sealed record SailOrder(int GangId, int BoatId, Run Run) : Order(GangId);

/// <summary>Snatch a rival's man and hold him for ransom.</summary>
public sealed record KidnapOrder(int GangId, int HoodId, int TargetHoodId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}

/// <summary>Send a man to buy the fighter or the jockey on this week's card.</summary>
public sealed record FixOrder(int GangId, int HoodId) : Order(GangId);

/// <summary>Offer one of a small gang's men more than his gang pays, to walk out on it and work for you.</summary>
public sealed record PoachOrder(int GangId, int TargetHoodId) : Order(GangId);

/// <summary>Send a man round to a small gang's corner to buy the whole gang in: its men, its shops and its money.</summary>
public sealed record AbsorbOrder(int GangId, int HoodId, int TargetGangId) : Order(GangId);

/// <summary>Send men round to a small gang's corner to finish it for good.</summary>
public sealed record WipeOutOrder(int GangId, int HoodId, int TargetGangId, int[]? Backup = null) : Order(GangId)
{
    [JsonIgnore] public IEnumerable<int> Team => (Backup ?? Array.Empty<int>()).Prepend(HoodId);
}
