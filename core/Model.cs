namespace DryTown.Core;

/// <summary>Inside means he has been planted in another outfit and is working there under false colours.</summary>
/// <summary>Lam is out of town: hiding from a warrant, or lying low.</summary>
public enum HoodState { Free, Jailed, Dead, Gone, Inside, Lam, Held }

/// <summary>A court is the yard in the middle of a tenement block: no shop, but out of sight of the street.</summary>
public enum BusinessKind { Grocer, Diner, Barber, Tailor, Garage, Laundry, Hotel, Pharmacy, PoolHall, Warehouse, Court }

/// <summary>What a man carries. Better guns win fights but bring more heat.</summary>
public enum Weapon { None, Pistol, Shotgun, TommyGun }

/// <summary>What a man drives. A car gets him there faster and away when it goes wrong.</summary>
public enum Car { None, ModelT, Packard, Cadillac }

public enum RacketKind { None, Speakeasy, Numbers, LoanShark, Still, Bookie, GamblingDen, Counterfeit, Slots, Casino, Brewery, Nightclub }

public sealed class Hood
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int GangId { get; set; }

    // Skills, 1 to 10.
    public int Intimidation { get; set; }
    public int Muscle { get; set; }
    public int Brains { get; set; }
    public int Stealth { get; set; }

    // 0 to 100. Low loyalty plus high ambition is how gangs split.
    public int Loyalty { get; set; }
    public int Ambition { get; set; }

    public int Wage { get; set; }
    public HoodState State { get; set; } = HoodState.Free;
    public int JailWeeks { get; set; }
    public int JoinedWeek { get; set; }

    /// <summary>The game week he was born, counted from the start of 1920, so usually negative.</summary>
    public int BornWeek { get; set; }

    /// <summary>Family of the boss who brought him in: they start green and loyal.</summary>
    public bool Family { get; set; }

    /// <summary>Where his people came from.</summary>
    public Ethnicity Origin { get; set; }

    /// <summary>His last arrest was federal: if he's sent away, it's to a federal pen, where no word gets out.</summary>
    public bool FederalTime { get; set; }

    public int Age(int week) => (week - BornWeek) / Content.WeeksPerYear;

    public Weapon Weapon { get; set; }
    public Car Car { get; set; }

    /// <summary>The charge he's held on until his trial, or null. While it's open, JailWeeks counts down to the trial.</summary>
    public Case? Case { get; set; }

    /// <summary>His police record, oldest first: every arrest and how it came out.</summary>
    public List<RapEntry> Record { get; set; } = new();

    /// <summary>What he has done for the outfit on the street.</summary>
    public Deeds Deeds { get; set; } = new();

    /// <summary>Weeks until he signs the district attorney's deal, or 0 if he isn't talking to him.</summary>
    public int Talking { get; set; }

    /// <summary>He tells the police what his outfit is up to. Nobody knows until someone finds him out.</summary>
    public bool Informer { get; set; }

    /// <summary>The week the outfit he's betraying found him out, or -1. He runs if he's left alone.</summary>
    public int ExposedWeek { get; set; } = -1;

    /// <summary>The outfit he has been planted in, while he's <see cref="HoodState.Inside"/>.</summary>
    public int InsideGangId { get; set; } = -1;
    public int InsideSinceWeek { get; set; }

    /// <summary>His family has been looked after while he's inside, so he has less reason to listen to the DA.</summary>
    public bool LookedAfter { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public bool Exposed => ExposedWeek >= 0;

    /// <summary>Times a jury has convicted him. Each one makes the next sentence longer.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int Convictions => Record.Count(r => r.Kind == RapKind.Convicted);

    /// <summary>The bail his outfit put up for him, while he's out waiting on his trial; else 0.</summary>
    public int Bail { get; set; }

    /// <summary>The week he left town, while he's on the lam; else -1.</summary>
    public int AwaySinceWeek { get; set; } = -1;

    public bool IsAvailable => State == HoodState.Free;

    /// <summary>Still one of the outfit's men: free, in a cell, or out of town.</summary>
    public bool IsActive => State is HoodState.Free or HoodState.Jailed or HoodState.Lam or HoodState.Held;

    /// <summary>What he brings to a fight: his own menace and muscle, and what he's carrying.</summary>
    public int Strength => Intimidation + Muscle + Content.Guns[Weapon].Firepower;
}

public sealed class Business
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public BusinessKind Kind { get; init; }

    /// <summary>Weekly takings in dollars before protection.</summary>
    public int Takings { get; init; }

    /// <summary>How hard the owner is to lean on, 1 to 10.</summary>
    public int Toughness { get; init; }

    public int ProtectorGangId { get; set; } = -1;
    public int HandlerHoodId { get; set; } = -1;
    public int ProtectionRate { get; set; }

    /// <summary>0 to 100. Rises with high rates and violence; high resentment makes owners talk to police.</summary>
    public int Resentment { get; set; }

    public RacketKind Racket { get; set; } = RacketKind.None;
    public int ShutWeeks { get; set; }

    /// <summary>The week it was last burned out, for the charred shell the map shows while it's shut.</summary>
    public int BurnedWeek { get; set; } = -1000;

    public int LotId { get; init; } = -1;

    /// <summary>The gang that owns it outright as a front, or -1. Owning it is separate from protecting it.</summary>
    public int OwnerGangId { get; set; } = -1;

    public bool IsProtected => ProtectorGangId >= 0;
    public bool IsFront => OwnerGangId >= 0;
    public bool IsOpen => ShutWeeks == 0;
}

public sealed class Gang
{
    public int Id { get; init; }
    public required string Name { get; set; }
    public bool IsPlayer { get; init; }
    public bool Alive { get; set; } = true;
    public int FoundedWeek { get; init; }
    public int DissolvedWeek { get; set; } = -1;

    public int BossHoodId { get; set; } = -1;
    public long Cash { get; set; }

    /// <summary>The part of the cash that has been through a front's books. Spent last, and the tax men can't touch it.</summary>
    public long CleanCash { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public long Clean => Math.Clamp(CleanCash, 0, Math.Max(0, Cash));
    [System.Text.Json.Serialization.JsonIgnore] public long Dirty => Math.Max(0, Cash) - Clean;

    /// <summary>Police attention, 0 to 100.</summary>
    public int Heat { get; set; }

    /// <summary>AI temperament, 0 to 1. Higher means more willing to muscle into rival turf.</summary>
    public double Aggression { get; init; }

    public int ConsecutiveUnpaidWeeks { get; set; }

    /// <summary>Who takes over when the boss dies or goes away for a long stretch, if he's still around and willing.</summary>
    public int HeirHoodId { get; set; } = -1;

    /// <summary>Whether the boss keeps the chair from his cell if he's sent away for a long stretch. Only the player chooses; rivals decide when it happens.</summary>
    public bool RunFromInside { get; set; } = true;

    /// <summary>The week the boss started running the outfit from his cell, or -1 while he's on the street.</summary>
    public int InsideSinceWeek { get; set; } = -1;

    /// <summary>The week the gang last brought a relative into the business.</summary>
    public int LastFamilyWeek { get; set; } = -1000;

    public int HqLotId { get; set; } = -1;

    /// <summary>The lawyer the gang keeps on retainer for its men's trials.</summary>
    public Lawyer Lawyer { get; set; }

    /// <summary>The week the gang last went back on its word. Nobody signs with it for a year afterwards.</summary>
    public int BrokeWordWeek { get; set; } = -1000;

    /// <summary>The week the gang last had reason to think someone in it was talking.</summary>
    public int TalkWeek { get; set; } = -1000;

    /// <summary>The boss's name on the street and in the papers: how much he is feared and how much he is admired, 0 to 100.</summary>
    public double Fear { get; set; }
    public double Respect { get; set; }

    /// <summary>The boss the name belongs to, and how far into the papers it has got (0 unknown, 1 a name, 2 a household name).</summary>
    public int NameBossId { get; set; } = -1;
    public int NameTier { get; set; }

    /// <summary>Whether the outfit runs a soup kitchen in the boss's name.</summary>
    public bool SoupKitchen { get; set; }

    /// <summary>Cases of liquor the gang's boats have brought in and its speakeasies haven't poured yet.</summary>
    public int Cellar { get; set; }

    /// <summary>How far the gang stretches the liquor it sells: more money, and more chance of a bad batch.</summary>
    public Cut Cut { get; set; }
}

/// <summary>How the liquor is served: straight, watered, or cut with whatever's to hand.</summary>
public enum Cut { Straight, Watered, Hard }

public enum PactKind { Truce, Alliance }

/// <summary>
/// An agreement between two gangs: neither moves on the other. A truce can come with tribute, paid
/// every Sunday by one side to the other. Allies also take up each other's quarrels.
/// </summary>
public sealed class Pact
{
    public PactKind Kind { get; set; }
    public int A { get; set; }
    public int B { get; set; }
    public int SignedWeek { get; set; }
    public int UntilWeek { get; set; }

    /// <summary>The side that pays the tribute, or -1, and what it pays each week.</summary>
    public int PayerId { get; set; } = -1;
    public int Tribute { get; set; }

    public bool Involves(int gangId) => A == gangId || B == gangId;
    public int Other(int gangId) => A == gangId ? B : A;
}

/// <summary>How much one gang hates another, 0 to 100. It builds with every hostile act and fades with time.</summary>
public sealed class Grudge
{
    public int From { get; set; }
    public int To { get; set; }
    public int Amount { get; set; }
}

public enum OfferKind { Truce, Alliance, Demand, Marriage, Ransom }

/// <summary>A proposal a rival has put to the player, waiting on an answer.</summary>
public sealed class Offer
{
    public int Id { get; set; }
    public OfferKind Kind { get; set; }
    public int FromGangId { get; set; }

    /// <summary>For a truce, what they offer to pay each week; for a demand, what they want from you.</summary>
    public int Tribute { get; set; }
    public int ExpiresWeek { get; set; }

    /// <summary>For a ransom, the man they're holding.</summary>
    public int HoodId { get; set; } = -1;
}

public enum Lawyer { None, Storefront, Mouthpiece }

public enum RapKind { Petty, Charged, Convicted, Acquitted, Turned }

/// <summary>One line on a man's police record.</summary>
public sealed class RapEntry
{
    public int Week { get; set; }
    public RapKind Kind { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A man's work for the outfit, counted.</summary>
public sealed class Deeds
{
    /// <summary>Shopkeepers he put under the gang's protection.</summary>
    public int Shakedowns { get; set; }

    /// <summary>Businesses he took from other outfits.</summary>
    public int Takeovers { get; set; }

    /// <summary>Men he killed, in a fight or a hit.</summary>
    public int Kills { get; set; }

    public int Fires { get; set; }

    /// <summary>Banks robbed and beer trucks hijacked.</summary>
    public int Heists { get; set; }

    public bool Any => Shakedowns + Takeovers + Kills + Fires + Heists > 0;
}

/// <summary>A man held for trial: what he's charged with, what he faces, and what has been done to help him.</summary>
public sealed class Case
{
    public string Charge { get; set; } = "";

    /// <summary>Weeks he serves if the jury convicts.</summary>
    public int Sentence { get; set; }

    /// <summary>A federal case: the Treasury's own court, where no judge can be bought.</summary>
    public bool Federal { get; set; }

    /// <summary>How likely a jury is to convict before anyone helps him, 0 to 1.</summary>
    public double Strength { get; set; }

    /// <summary>The gang already had a lawyer when he was arrested, so the defence had time to prepare.</summary>
    public bool Prepared { get; set; }

    public bool JudgePaid { get; set; }
    public bool JuryFixed { get; set; }

    /// <summary>A fourth felony: under the habitual offender law, a conviction means life.</summary>
    public bool Life { get; set; }

    /// <summary>He ran while out on bail: no bail again, and the jury hears about it if he's caught.</summary>
    public bool JumpedBail { get; set; }
}

/// <summary>
/// A lieutenant and the men who answer to him. A crew can be sent as one team: it hits harder
/// and guards better than one man, but every member is tied up for the week. Lieutenants set
/// the mood of their crew, and an ambitious one who breaks away takes his crew with him.
/// </summary>
public sealed class Crew
{
    public const int MaxMembers = 3;

    public int Id { get; init; }
    public int GangId { get; set; }
    public int LieutenantHoodId { get; set; }

    /// <summary>The men under the lieutenant, not counting him.</summary>
    public List<int> MemberIds { get; init; } = new();

    public IEnumerable<int> Everyone => MemberIds.Prepend(LieutenantHoodId);
}

public sealed class WeekLedger
{
    public long Protection { get; set; }
    public long Rackets { get; set; }
    public long Wages { get; set; }
    public long Spending { get; set; }
    public long Fines { get; set; }

    /// <summary>The fronts' own honest profit, which comes in clean.</summary>
    public long Legit { get; set; }

    /// <summary>The bookkeeper's cut for running dirty money through the fronts.</summary>
    public long LaunderFee { get; set; }

    /// <summary>Tribute paid to, and received from, other gangs under a truce.</summary>
    public long TributePaid { get; set; }
    public long TributeIn { get; set; }

    /// <summary>Skimmed from the union locals the gang runs, and the longshoremen's cut of other outfits' boats.</summary>
    public long Dues { get; set; }

    /// <summary>The other outfits' bookies' fees, for the outfit that runs the racing wire.</summary>
    public long Wire { get; set; }

    /// <summary>Dirty money that went through the books this week. Not income: it was already in the safe.</summary>
    public long Laundered { get; set; }

    public long Net => Protection + Rackets + Legit + TributeIn + Dues + Wire - Wages - Spending - Fines - LaunderFee - TributePaid;
}

public enum EventKind
{
    Extorted, ExtortFailed, Takeover, TakeoverRepelled, HoodKilled, HoodJailed, HoodReleased,
    Raid, Squeal, RacketOpened, Recruited, Deserted, Breakaway, NewGang, GangDissolved,
    Succession, Bribe, Era, Lapsed, DiedNaturally, Heir, Family, Politics,
    Arson, Hit, Robbery, Hijack, Street, Gear, Front, Trial, Diplomacy, News, Rat,
    /// <summary>Supply lines bought, lost and changing hands, and bad liquor.</summary>
    Booze,
    /// <summary>Something only the gang itself knows: what its men inside hear, who it suspects, what it paid to keep a man quiet.</summary>
    Whisper,
    /// <summary>Union locals taken over, strikes, and the docks.</summary>
    Union,
    /// <summary>After repeal: the racing wire changing hands, the Bureau's public enemy, and the G-men's arrests.</summary>
    Gmen,
    /// <summary>The Commission: seats, votes, and outfits thrown out for breaking its rules.</summary>
    Commission,
    /// <summary>A boss running his outfit from a cell, and how it ends.</summary>
    Inside,
    /// <summary>Men skipping town, lying low and coming home.</summary>
    Lam,
    /// <summary>A boss's name in the papers, and soup kitchens.</summary>
    Name,
    /// <summary>The Prohibition Bureau's squad: who it's after, its raids, and the grand jury.</summary>
    Untouchables,
    /// <summary>Fixed fights and races.</summary>
    Fix,
    /// <summary>Men snatched, ransoms paid, and what happens to the ones nobody pays for.</summary>
    Kidnap,
}

public sealed record GameEvent(int Week, EventKind Kind, int GangId, string Text, int Tick = 0);

public enum ActionKind
{
    /// <summary>A hood leans on a shopkeeper.</summary>
    Extort,
    /// <summary>A hood tries to take a business from a rival; the defender fights back.</summary>
    Takeover,
    /// <summary>A hood sets up a racket in a back room.</summary>
    Racket,
    /// <summary>A hood stands guard at a business all week.</summary>
    Guard,
    /// <summary>Police come from the precinct house.</summary>
    Raid,
    /// <summary>Collectors make the Sunday rounds.</summary>
    Collect,
    /// <summary>A hood sets fire to a rival's business.</summary>
    Arson,
    /// <summary>A hood goes after a named man in a rival gang.</summary>
    Hit,
    /// <summary>A crew holds up a bank.</summary>
    Robbery,
    /// <summary>A crew stops a rival's beer truck on its way to a speakeasy.</summary>
    Hijack,
    /// <summary>A hood has a quiet word with a witness.</summary>
    Silence,
    /// <summary>Someone on the street is hired, paid off or bought.</summary>
    Hire,
    /// <summary>A hood leans on voters outside a polling place.</summary>
    Polls,
    /// <summary>A hood goes to a union hall to take over the local.</summary>
    Union,
}

public enum ActionResult { None, Success, Failed, Won, Lost, BackedOff, Killed, Arrested }

/// <summary>
/// One thing that happened on the street, for the live view to animate. A hood walks from
/// <see cref="FromLot"/> to <see cref="ToLot"/>, arriving at <see cref="Tick"/>.
/// </summary>
public sealed record ScriptAction(
    ActionKind Kind, int GangId, int HoodId, int FromLot, int ToLot, int BusinessId,
    ActionResult Result, string Text,
    int DefenderGangId = -1, int DefenderHoodId = -1, int CasualtyHoodId = -1, int Tick = 0)
{
    /// <summary>Men who went along as backup, not counting <see cref="HoodId"/>.</summary>
    public IReadOnlyList<int> Backup { get; init; } = Array.Empty<int>();
}

/// <summary>Who someone on the street is. Shopkeepers and gangsters are clicked through their businesses and gangs.</summary>
public enum PersonKind { Drifter, BeatCop, Reporter, Witness }

/// <summary>
/// Someone on the street who isn't in a gang: a drifter looking for work, the beat cop, a
/// reporter working on a story about a gang, or a witness who saw one of its men do something.
/// </summary>
public sealed class StreetPerson
{
    public int Id { get; init; }
    public PersonKind Kind { get; init; }
    public required string Name { get; init; }

    /// <summary>Where he hangs about.</summary>
    public int LotId { get; set; }
    public int WardId { get; init; }

    /// <summary>Where his people came from: usually the neighbourhood he's found in.</summary>
    public Ethnicity Origin { get; set; }

    // A drifter's skills, 1 to 10, which he keeps if he's hired.
    public int Intimidation { get; init; }
    public int Muscle { get; init; }
    public int Brains { get; init; }
    public int Stealth { get; init; }
    public int BornWeek { get; init; }

    /// <summary>A reporter's or a witness's gang, and the man the witness saw.</summary>
    public int GangId { get; init; } = -1;
    public int HoodId { get; init; } = -1;

    /// <summary>What he saw or is writing about.</summary>
    public string About { get; init; } = "";

    /// <summary>How hard he is to scare or buy, 1 to 10.</summary>
    public int Nerve { get; init; }

    /// <summary>Weeks until the drifter moves on, the reporter goes to print or the witness testifies.</summary>
    public int WeeksLeft { get; set; }

    /// <summary>The gang that has paid this beat cop to look away this week, or -1.</summary>
    public int PaidByGangId { get; set; } = -1;

    public int Strength => Intimidation + Muscle;

    /// <summary>What a drifter asks to sign on: more for a better man.</summary>
    public int Fee => 60 + (Intimidation + Muscle + Brains + Stealth) * 8;
}

/// <summary>A bank: a vault that fills up through the week, and guards on the door.</summary>
public sealed class Bank
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int LotId { get; init; }
    public int Capacity { get; init; }
    public int Vault { get; set; }
    public int Guards { get; init; }

    /// <summary>The week it was last held up. A bank stays on its guard for a while afterwards.</summary>
    public int RobbedWeek { get; set; } = -1000;
}

public enum LineKind { Truck, Boat }

/// <summary>
/// A run bringing real liquor into the city from outside, by road or by water. An outfit that buys
/// one serves the good stuff in its speakeasies, and its shipments are worth stopping.
/// </summary>
public sealed class SupplyLine
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public LineKind Kind { get; init; }

    /// <summary>What it carries and where from, as a phrase: "Canadian whisky down from Windsor".</summary>
    public required string Cargo { get; init; }

    /// <summary>What the suppliers ask to deal with an outfit.</summary>
    public int Price { get; init; }

    /// <summary>How many speakeasies it keeps in good liquor.</summary>
    public int Capacity { get; init; }

    /// <summary>The outfit the suppliers deal with, or -1.</summary>
    public int OwnerGangId { get; set; } = -1;

    /// <summary>The week its last shipment was stopped. Nothing gets through that week.</summary>
    public int WaylaidWeek { get; set; } = -1000;

    /// <summary>Weeks its owner has had no speakeasy to take a delivery.</summary>
    public int IdleWeeks { get; set; }
}

/// <summary>
/// Two outfits gone to the mattresses: both sides' men sleep in safehouses and the shooting starts
/// in earnest, until one side calls it off, the two sign a pact, or one of them is finished.
/// </summary>
public sealed class War
{
    /// <summary>The outfit that declared it, and the one it declared on.</summary>
    public int A { get; set; }
    public int B { get; set; }
    public int StartWeek { get; set; }

    public bool Involves(int gangId) => A == gangId || B == gangId;
    public int Other(int gangId) => A == gangId ? B : A;
}

/// <summary>
/// A union local: the men who work one set of trades across the city. An outfit that runs it skims
/// the dues, and can pull the members out of any shop in those trades until the owner pays.
/// </summary>
public sealed class UnionLocal
{
    public int Id { get; init; }
    public required string Name { get; init; }

    /// <summary>The kinds of business whose workers belong to it.</summary>
    public BusinessKind[] Trades { get; init; } = Array.Empty<BusinessKind>();

    /// <summary>How hard its own men are to push around, 1 to 10.</summary>
    public int Toughness { get; init; }

    /// <summary>What the outfit skims from the dues of each member shop, a week.</summary>
    public int DuesPerShop { get; init; }

    /// <summary>Where the hall is: the lot of its oldest member shop.</summary>
    public int LotId { get; init; } = -1;

    /// <summary>The outfit that runs it, or -1.</summary>
    public int OwnerGangId { get; set; } = -1;

    /// <summary>The outfit's man in the hall, or -1.</summary>
    public int PresidentHoodId { get; set; } = -1;

    /// <summary>The shop the members have walked out of, or -1, and the week they walked.</summary>
    public int StrikeBusinessId { get; set; } = -1;
    public int StrikeWeek { get; set; }

    /// <summary>The longshoremen: whoever runs them has a say in what comes off the boats.</summary>
    public bool IsDocks => Trades.Contains(BusinessKind.Warehouse);
    public bool OnStrike => StrikeBusinessId >= 0;
}
