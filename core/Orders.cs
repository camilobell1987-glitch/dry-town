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
[JsonDerivedType(typeof(ArmOrder), "arm")]
[JsonDerivedType(typeof(CarOrder), "car")]
[JsonDerivedType(typeof(FrontOrder), "front")]
[JsonDerivedType(typeof(JuryOrder), "jury")]
[JsonDerivedType(typeof(LawyerOrder), "lawyer")]
[JsonDerivedType(typeof(JudgeOrder), "judge")]
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
