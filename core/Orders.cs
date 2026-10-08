namespace DryTown.Core;

/// <summary>Orders a gang gives during the planning phase. They resolve when the week runs.</summary>
public abstract record Order(int GangId);

/// <summary>Send a hood to lean on a business. Works on unprotected or rival-protected businesses.</summary>
public sealed record ExtortOrder(int GangId, int HoodId, int BusinessId) : Order(GangId);

/// <summary>Open a racket behind a business the gang protects.</summary>
public sealed record RacketOrder(int GangId, int HoodId, int BusinessId, RacketKind Racket) : Order(GangId);

/// <summary>Hire a new hood.</summary>
public sealed record RecruitOrder(int GangId) : Order(GangId);

/// <summary>Pay off police. Amount is spent in units of Content.BribeUnit.</summary>
public sealed record BribeOrder(int GangId, int Amount) : Order(GangId);

/// <summary>Change the weekly protection rate (percent of takings) on one business.</summary>
public sealed record SetRateOrder(int GangId, int BusinessId, int RatePercent) : Order(GangId);
