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
