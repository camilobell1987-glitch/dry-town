using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// A resident's card from the city directory, for anyone picked out of the crowd. It's a paper index
/// card with a line for each thing worth knowing about him, and a question mark on every line you
/// haven't found out yet.
/// </summary>
public partial class Main
{
    private static readonly Color CardPaper = new("efe6cf");
    private static readonly Color CardEdge = new("cfc3a5");
    private static readonly Color CardInk = new("2a2622");
    private static readonly Color CardFaint = new("8a8170");
    private static readonly Color CardRule = new("c9a9a0");

    /// <summary>Show every line of every card, for screenshots and testing until there are ways to find things out.</summary>
    private bool _revealCitizens;

    /// <summary>What you know about a resident: what you've found out, plus what's plain from dealing with him.</summary>
    private Fact KnownAbout(int citizen)
    {
        if (_revealCitizens) return Fact.All;
        var known = W.KnownAbout(citizen);
        // A shopkeeper who pays you is someone you know: his name, his shop, and that he lives over it.
        if (_map.Directory is { } d && d.Get(citizen) is { OwnsBusinessId: >= 0 } c && W.BusinessById(c.OwnsBusinessId).ProtectorGangId == Me)
            known |= Fact.Name | Fact.Job | Fact.Work | Fact.Home | Fact.Landlord | Fact.Owns;
        return known;
    }

    /// <summary>What was overheard when a resident was picked out of a conversation, to show on the card.</summary>
    private string? _overheard;

    /// <summary>
    /// Someone has been picked out of the crowd: note that you've come across them, so the street can tell you more. Picked
    /// out of a conversation, you overhear what the pair call each other and what language they speak.
    /// </summary>
    private void MeetCitizen(int id)
    {
        _overheard = null;
        if (_map.Directory is not { } d || !d.Exists(id)) return;
        var c = d.Get(id);
        W.Introduce(id, W.Map.LotAt(c.HomeLotId).WardId, c.Ties, d.SecretsOf(id), d.CharacterOf(id), c.OwnsBusinessId);
        int partner = _map.TalkingWith(id);
        if (partner < 0) return;
        var other = d.Get(partner);
        W.Learn(id, Fact.Name | Fact.Origin, W.Map.LotAt(c.HomeLotId).WardId);
        W.Learn(partner, Fact.Name | Fact.Origin, W.Map.LotAt(other.HomeLotId).WardId);
        string tongue = c.Origin == other.Origin && c.Origin is not (Ethnicity.Irish or Ethnicity.Unknown) ? $"in {Language(c.Origin)}" : "in English";
        _overheard = $"You overheard them talking {tongue}. This one is {c.Name}, and the other is {other.Name}.";
    }

    private static string Language(Ethnicity e) => e switch
    {
        Ethnicity.Italian => "Italian",
        Ethnicity.Jewish => "Yiddish",
        Ethnicity.Polish => "Polish",
        Ethnicity.German => "German",
        _ => "English",
    };

    /// <summary>How the directory refers to someone in orders and news: by name if you know it.</summary>
    private string WhoIs(Citizen c, Fact known, Lot seen) => known.HasFlag(Fact.Name) ? c.Name : $"someone on {W.Map.StreetOf(seen)} St";

    private void CitizenPanel(int id)
    {
        var panel = _businessPanel;
        var directory = _map.Directory;
        if (directory == null || !directory.Exists(id)) return;
        var c = directory.Get(id);
        var known = KnownAbout(id);
        var home = W.Map.LotAt(c.HomeLotId);
        W.Introduce(id, home.WardId, c.Ties, directory.SecretsOf(id), directory.CharacterOf(id), c.OwnsBusinessId);
        var (bx, by, _) = directory.Where(id);
        var seen = _map.WhereIs(id) is { } at
            ? W.Map.LotsOfBlock(bx, by).OrderBy(l => new Vector2(l.X + 0.5f, l.Y + 0.5f).DistanceSquaredTo(at)).First()
            : home;

        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = CardPaper, BorderColor = CardEdge, BorderWidthBottom = 2, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 10,
        });
        panel.AddChild(card);
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 2);
        card.AddChild(lines);

        var head = AddLabel(lines, (known.HasFlag(Fact.Name) ? c.Name : "???"), CardInk, 18);
        head.AddThemeColorOverride("font_color", CardInk);
        var rule = new ColorRect { Color = CardRule, CustomMinimumSize = new Vector2(0, 2) };
        lines.AddChild(rule);
        if (W.Acquaintances.GetValueOrDefault(id) is { Standing: not Standing.None } dealt)
        {
            string stamp = dealt.Standing switch
            {
                Standing.Bought => dealt.GangId == Me ? "On your payroll" : "On someone's payroll",
                Standing.Scared => dealt.GangId == Me ? "Scared of you" : "Scared",
                Standing.Held or Standing.Jailed or Standing.Joined => dealt.Note,
                Standing.Blackmailed => dealt.GangId == Me ? $"Paying you ${World.HushMoney(dealt.Worth)} a week to keep quiet" : "Being blackmailed",
                _ => $"Dead. {dealt.Note}",
            };
            AddLabel(lines, stamp, dealt.Standing is Standing.Dead or Standing.Held or Standing.Jailed ? new Color("9a2a22") : new Color("3c6a34"), wrap: true);
        }

        // Tabs, like the dividers in a card file: the particulars, what sort of person they are, and their grudges.
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 14);
        lines.AddChild(tabs);
        foreach (var (title, which) in new[] { ("Particulars", CardTab.Particulars), ("Character", CardTab.Character), ("Grudges", CardTab.Grudges) })
        {
            bool on = _cardTab == which;
            var tab = new LinkButton { Text = title, Underline = on ? LinkButton.UnderlineMode.Always : LinkButton.UnderlineMode.OnHover, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            tab.AddThemeColorOverride("font_color", on ? CardInk : CardFaint);
            tab.AddThemeColorOverride("font_hover_color", new Color("9a2a22"));
            tab.AddThemeFontSizeOverride("font_size", 13);
            tab.Pressed += () => { _cardTab = which; RefreshBusiness(); };
            tabs.AddChild(tab);
        }
        var grid = new GridContainer { Columns = 2, Visible = _cardTab == CardTab.Particulars };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 3);
        lines.AddChild(grid);
        if (_cardTab == CardTab.Character) CharacterTab(lines, id, known);
        if (_cardTab == CardTab.Grudges) GrudgesTab(lines, id);
        // Each line you know is a link where it leads somewhere: a place moves the map there, a person opens their card.
        void Line(string label, Fact fact, Func<string> value, Action? go = null)
        {
            AddLabel(grid, label, CardFaint);
            bool have = known.HasFlag(fact);
            if (have && go != null) { CardLink(grid, value(), go); return; }
            var text = AddLabel(grid, have ? value() : "???", have ? CardInk : CardFaint, wrap: true);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        Action ToBusiness(int biz) => () => _map.PanTo(W.LotOf(W.BusinessById(biz)));
        Line("Age", Fact.Age, () => c.Age.ToString());
        Line("People", Fact.Origin, () => Content.Label(c.Origin));
        Line("Job", Fact.Job, () => c.Job);
        Line("Works at", Fact.Work, () => c.WorkBusinessId >= 0 ? W.BusinessById(c.WorkBusinessId).Name
            : c.OwnsBusinessId >= 0 ? "Runs the place" : "Nowhere in particular",
            c.WorkBusinessId >= 0 ? ToBusiness(c.WorkBusinessId) : c.OwnsBusinessId >= 0 ? ToBusiness(c.OwnsBusinessId) : null);
        Line("Lives at", Fact.Home, () => Address(home), () => _map.PanTo(home));
        bool rents = c.LandlordId >= 0 && c.LandlordId != c.Id;
        Line("Landlord", Fact.Landlord, () => rents ? directory.Get(c.LandlordId).Name
            : c.LandlordId == c.Id || c.OwnsBusinessId >= 0 && W.LotOf(W.BusinessById(c.OwnsBusinessId)).Id == c.HomeLotId ? "Owns the building" : "None",
            rents ? () => OpenCard(c.LandlordId) : null);
        if (c.Owns.Count > 0 && known.HasFlag(Fact.Owns))
        {
            // A landlord's buildings, one link each.
            AddLabel(grid, "Owns", CardFaint);
            var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 8);
            grid.AddChild(flow);
            foreach (int building in c.Owns)
            {
                var lot = W.Map.LotAt(building);
                CardLink(flow, Address(lot), () => _map.PanTo(lot));
            }
        }
        else Line("Owns", Fact.Owns, () => c.OwnsBusinessId >= 0 ? W.BusinessById(c.OwnsBusinessId).Name : "Nothing to speak of",
            c.OwnsBusinessId >= 0 ? ToBusiness(c.OwnsBusinessId) : null);
        Family(grid, id, c, known);
        Secrets(grid, id, c);

        if (_map.WhereIs(id) == null) AddLabel(panel, "Indoors just now.", Palette.InkQuiet);
        else AddLabel(panel, $"Seen on {W.Map.StreetOf(seen)} St in {W.Wards.ElementAtOrDefault(seen.WardId)?.Name ?? "the district"}.", Palette.InkQuiet, wrap: true);
        if (_overheard != null) AddLabel(panel, _overheard, Palette.Ink, wrap: true);
        var standing = W.Acquaintances.GetValueOrDefault(id);
        if (standing?.Standing == Standing.Dead) return;
        if (known == Fact.None)
            AddLabel(panel, "You don't know the first thing about this one yet.", Palette.InkQuiet, wrap: true);
        if (known != Fact.All)
            AddLabel(panel, "Word on the street fills in a line now and then on anyone you've come across, more often where you hold turf. It also turns up who they are close to: click a name to see that card.", Palette.InkQuiet, wrap: true);

        bool Concerns(Order o) => o switch
        {
            AskOrder a => a.CitizenId == id,
            TailOrder t => t.CitizenId == id,
            LeanOrder l => l.CitizenId == id,
            _ => false,
        };
        ShowQueued(panel, Concerns);
        if (!CanGiveOrders(panel)) return;
        string who = WhoIs(c, known, seen);
        if (known != Fact.All && !AlreadyGiven(o => o is AskOrder a && a.CitizenId == id))
        {
            var ask = AddButton(panel, $"Ask around about them (${Content.AskPrice})", () => Give(new AskOrder(Me, id, c.HomeLotId, who)));
            ask.Disabled = W.Player.Cash < Content.AskPrice;
        }
        var hoods = FreeHoods().ToList();
        if (hoods.Count == 0) { AddLabel(panel, "All your men are busy, so nobody can go to them.", Palette.InkQuiet, wrap: true); return; }
        if (standing?.Standing is Standing.Held or Standing.Jailed or Standing.Joined) return;
        bool tailing = AlreadyGiven(o => o is TailOrder t && t.CitizenId == id), leaning = AlreadyGiven(o => o is LeanOrder l && l.CitizenId == id);
        if (tailing || leaning) return;

        panel.AddChild(new HSeparator());
        AddLabel(panel, "Send a man to them", Palette.Ink, 16);
        // The boss goes himself only when there's hardly anyone else.
        var men = hoods.Where(h => h.Id != W.Player.BossHoodId || W.AvailableHoodsOf(Me).Count() < 3).OrderByDescending(h => h.Strength).ToList();
        if (men.Count == 0) return;
        var man = HoodPicker(men, h => $"Intim. {h.Intimidation} · Muscle {h.Muscle} · Stealth {h.Stealth}");
        if (known != Fact.All)
            AddButton(panel, "Tail them for the week", () => Give(new TailOrder(Me, man.GetSelectedId(), id, c.HomeLotId, who)));
        // What each would cost and the odds with the man picked. Until you know the person you can only guess at his nerve.
        bool reads = W.ReadsNerve(W.Player, id);
        int nerve = reads ? W.NerveOf(id) : 5;
        // A family pays what the richest of them can.
        int family = W.KnownTiesOf(id).Where(t => t.Kin != Kin.Employer && directory.Exists(t.Id)).Select(t => directory.Get(t.Id).Worth).Append(c.Worth).Max();
        double leverage = W.Leverage(W.Player, id);
        var buttons = new List<(Button Button, Lean Lean, Func<string> Label)>();
        void Act(Lean lean, Func<string> label)
        {
            var b = AddButton(panel, "", () => Give(new LeanOrder(Me, man.GetSelectedId(), id, c.HomeLotId, who, lean, lean == Lean.Kidnap ? family : c.Worth, c.OwnsBusinessId, c.Owns.Count)));
            buttons.Add((b, lean, label));
        }
        // What they're good for shows once you know what they do.
        int price = World.BribePrice(c.Worth);
        bool sized = known.HasFlag(Fact.Job) || known.HasFlag(Fact.Owns);
        string Money(int dollars) => sized ? $"${dollars}" : "$???";
        Act(Lean.Bribe, () => $"Put them on the payroll ({Money(price)}, then {Money(World.Retainer(c.Worth))} a week)");
        Act(Lean.Intimidate, () => "Lean on them");
        Act(Lean.Extort, () => $"Shake them down (about {Money(Math.Max(5, c.Worth * Content.ShakedownOfWorth))})");
        Act(Lean.Kidnap, () => $"Snatch them for ransom ({Money(Math.Max(50, family * Content.RansomOfWorth))})");
        Act(Lean.Kill, () => "Have them killed");
        Act(Lean.Blackmail, () => $"Blackmail them ({Money(World.HushMoney(c.Worth))} a week)");
        int bad = W.BadBlood(W.Player, id, knownOnly: !_revealCitizens);
        int squarePrice = Content.SquarePrice(c.Worth, bad);
        if (bad > 0)
        {
            Act(Lean.Square, () => $"Pay them to square things ({Money(squarePrice)})");
            Act(Lean.Favour, () => "Do them a good turn");
            if (c.OwnsBusinessId >= 0) Act(Lean.GoodWord, () => $"Put in a good word for {Short(W.BusinessById(c.OwnsBusinessId).Name)}");
        }
        if (known.HasFlag(Fact.Name) && (_revealCitizens ? directory!.CharacterOf(id).Traits : W.KnownTraitsOf(id)).Contains(Trait.WannabeTorpedo))
        {
            var b = AddButton(panel, "", () => Give(new LeanOrder(Me, man.GetSelectedId(), id, c.HomeLotId, c.Name, Lean.Recruit, c.Worth) { Origin = c.Origin, Age = c.Age }));
            buttons.Add((b, Lean.Recruit, () => "Ask him to join the outfit"));
        }
        if (W.KnownCrime(id) is { } crime) Act(Lean.TurnIn, () => $"Turn them in for {Content.Crime(crime)} ({Content.Penalty(Content.Offence(crime)).Least:0} to {Content.Penalty(Content.Offence(crime)).Most:0} years)");
        void Odds()
        {
            var hood = W.HoodById(man.GetSelectedId());
            foreach (var (b, lean, label) in buttons)
            {
                string why = W.CannotLean(W.Player, id, lean) is { } no ? $" · {no}" : "";
                double odds = Simulation.LeanChance(hood, lean, nerve, known, leverage,
                    W.TraitOdds(id, lean, knownOnly: !_revealCitizens) + W.GrudgeOdds(W.Player, id, lean, knownOnly: !_revealCitizens));
                b.Text = $"{label()} · {(reads ? "" : "maybe ")}{odds:P0}{why}";
                b.Disabled = why != "" || lean == Lean.Bribe && W.Player.Cash < price || lean == Lean.Square && W.Player.Cash < squarePrice;
            }
        }
        man.ItemSelected += _ => Odds();
        Odds();
        if (W.HostageFrom(W.Player, id) is int held and >= 0)
            AddLabel(panel, $"You're holding their {Content.Label(W.KnownTiesOf(id).First(t => t.Id == held).Kin, directory.Get(held).Woman).ToLowerInvariant()}. They'll do as they're told.", Palette.Good, wrap: true);
        else if (leverage > 0) AddLabel(panel, "You know who their family are, which makes a threat or a shakedown likelier to work. Snatch one of them and they'll do as they're told.", Palette.InkQuiet, wrap: true);
        if (W.HasVendetta(W.Player, id, knownOnly: !_revealCitizens))
            AddLabel(panel, "They've sworn a vendetta against you. It will never fade, nothing will square it, and sooner or later they'll act on it.", Palette.Bad, wrap: true);
        else if (bad > 0) AddLabel(panel, $"They're {Content.Label(Content.TierOf(bad)).ToLowerInvariant()} with you, which makes them harder to buy or scare. Paying them off eases it most and calms their family too; a good turn or a good word for their business helps, and so does a soup kitchen in your name. Let it reach a vendetta and there's no coming back.", Palette.InkQuiet, wrap: true);
        if (!reads) AddLabel(panel, $"You can't tell yet how much nerve they have, so the odds are a guess. Learn {Content.NerveFacts} things about them to know for sure.", Palette.InkQuiet, wrap: true);
        AddLabel(panel, "Anything they're hiding makes a threat likelier to work, and is grounds for blackmail. A crime you know of can be handed to the police, which cools them on you. The term fits the crime and how the trial goes: money buys a deal, and reformers want examples made. A shopkeeper taken in leaves his shop shut for a while.", Palette.InkQuiet, wrap: true);
        AddLabel(panel, "Knowing where they live makes a threat or a snatch likelier to work, and knowing where they work helps with everything else. A shopkeeper in your pocket takes your protection gladly and resents it less, and anyone you've bought or scared keeps the block quiet when there are witnesses.", Palette.InkQuiet, wrap: true);
        AddLabel(panel, "A bribed man tells you what he hears, and so does a frightened one for a while. Shaking someone down pays now; a snatch pays if the family comes up with the money. Every one but a bribe brings heat, and anyone can be seen doing it. A killing makes you feared and the neighbourhood talks of murder.", Palette.InkQuiet, wrap: true);
    }

    /// <summary>A line on the card for each of the people someone is close to that you know of, each one a way to their card.</summary>
    private void Family(GridContainer grid, int id, Citizen c, Fact known)
    {
        var d = _map.Directory!;
        var a = W.Acquaintances.GetValueOrDefault(id);
        var ties = _revealCitizens ? c.Ties.ToList() : W.KnownTiesOf(id).ToList();
        // Someone you've bought or frightened has told you who they have; otherwise there may always be more.
        bool told = _revealCitizens || a is { Standing: Standing.Bought or Standing.Scared } && a.GangId == Me && !a.UnknownTies.Any();
        foreach (var tie in ties.Where(t => d.Exists(t.Id)))
        {
            var them = d.Get(tie.Id);
            AddLabel(grid, Content.Label(tie.Kin, them.Woman), CardFaint);
            string surname = them.Name[(them.Name.IndexOf(' ') + 1)..];
            string name = KnownAbout(tie.Id).HasFlag(Fact.Name) ? them.Name
                : tie.Kin != Kin.Employer && known.HasFlag(Fact.Name) && c.Name.EndsWith(surname) ? $"??? {surname}" : "Name unknown";
            int other = tie.Id;
            CardLink(grid, name, () => OpenCard(other));
        }
        if (told && ties.Count == 0)
        {
            AddLabel(grid, "Family", CardFaint);
            AddLabel(grid, "Nobody to speak of", CardInk);
        }
        else if (!told)
        {
            AddLabel(grid, ties.Count == 0 ? "Family" : "Others", CardFaint);
            AddLabel(grid, "???", CardFaint);
        }
    }

    private enum CardTab { Particulars, Character, Grudges }

    /// <summary>Which tab of a resident's card is showing.</summary>
    private CardTab _cardTab;

    /// <summary>The card's second tab: where they came from and what sort they are, as far as you know.</summary>
    private void CharacterTab(VBoxContainer lines, int id, Fact known)
    {
        var d = _map.Directory!;
        var a = W.Acquaintances.GetValueOrDefault(id);
        var character = d.CharacterOf(id);
        void Entry(string label, string meaning, Color colour)
        {
            AddLabel(lines, label, colour, 15);
            if (meaning != "") AddLabel(lines, meaning, CardInk, 13, wrap: true);
        }
        AddLabel(lines, "Background", CardFaint, 13);
        if (_revealCitizens || known.HasFlag(Fact.Past)) Entry(Content.Label(character.Background), Content.Meaning(character.Background), CardInk);
        else AddLabel(lines, "???", CardFaint);
        lines.AddChild(new ColorRect { Color = CardRule, CustomMinimumSize = new Vector2(0, 1) });
        AddLabel(lines, "Character", CardFaint, 13);
        var traits = _revealCitizens ? character.Traits.ToList() : W.KnownTraitsOf(id).ToList();
        bool woman = d.Get(id).Woman;
        foreach (var t in traits) Entry(Content.Label(t, woman), Content.Meaning(t), new Color("7a3a2a"));
        // Someone who has told you everything has nothing more to show; anyone else may.
        bool told = _revealCitizens || a is { Standing: Standing.Bought } && a.GangId == Me && !a.UnknownTraits.Any();
        if (!told) AddLabel(lines, traits.Count == 0 ? "???" : "More, perhaps: ???", CardFaint);
        AddLabel(lines, "Asking around, tailing them, the street's gossip and dealing with them all show what sort they are.", CardFaint, 12, wrap: true);
    }

    /// <summary>The card's third tab: who they have it in for, and who has it in for them, as far as you know.</summary>
    private void GrudgesTab(VBoxContainer lines, int id)
    {
        var d = _map.Directory!;
        bool Shown(Grievance g) => _revealCitizens || g.Known;
        var held = W.HeldBy(Party.Resident, id).Where(Shown).OrderByDescending(g => g.Amount).ToList();
        var against = W.HeldAgainst(Party.Resident, id).Where(Shown).OrderByDescending(g => g.Amount).ToList();
        string Over(Grievance g) => Content.Over(g, g.AboutId >= 0 && d.Exists(g.AboutId) ? d.Get(g.AboutId).Woman : null);
        string Resident(int other) => !d.Exists(other) ? "someone" : KnownAbout(other).HasFlag(Fact.Name) || _revealCitizens ? d.Get(other).Name : "someone you don't know";
        Color Heat(int amount) => amount >= Content.BeefingAt ? new Color("9a2a22") : amount >= Content.ScowlingAt ? new Color("7a3a2a") : CardInk;
        void Reason(string text) => AddLabel(lines, text, CardInk, 13, wrap: true);

        AddLabel(lines, "Holds a grudge against", CardFaint, 13);
        foreach (var g in held)
        {
            string words = Content.GrudgeWords(g.Amount);
            switch (g.Target)
            {
                case Party.Gang:
                    AddLabel(lines, $"{words} {(g.TargetId == Me ? "you" : W.GangById(g.TargetId).Name)}", Heat(g.Amount), 15, wrap: true);
                    break;
                case Party.Hood:
                    var hood = W.Hoods.FirstOrDefault(x => x.Id == g.TargetId);
                    AddLabel(lines, $"{words} {(hood == null ? "a gangster" : hood.Name + (hood.GangId == Me ? ", one of yours" : ""))}", Heat(g.Amount), 15, wrap: true);
                    break;
                default:
                    // Someone you can go and look at.
                    var row = new HBoxContainer();
                    lines.AddChild(row);
                    var label = AddLabel(row, words, Heat(g.Amount), 15);
                    label.AutowrapMode = TextServer.AutowrapMode.Off;
                    label.CustomMinimumSize = Vector2.Zero;
                    int other = g.TargetId;
                    var cop = g.Target == Party.Cop ? W.PersonById(other) : null;
                    if (cop != null) CardLink(row, cop.Name, () => _map.PanTo(W.Map.LotAt(cop.LotId)));
                    else if (g.Target == Party.Cop) AddLabel(row, "a cop", Heat(g.Amount), 15);
                    else CardLink(row, Resident(other), () => OpenCard(other));
                    break;
            }
            string they = g.Target switch { Party.Gang when g.TargetId == Me => "You", Party.Cop or Party.Hood => "He", _ => "They" };
            Reason($"{they} {Over(g)}.");
        }
        if (held.Count == 0) AddLabel(lines, _revealCitizens ? "Nobody" : "Nobody you know of", CardInk);
        lines.AddChild(new ColorRect { Color = CardRule, CustomMinimumSize = new Vector2(0, 1) });
        AddLabel(lines, "Who has it in for them", CardFaint, 13);
        foreach (var g in against)
        {
            var row = new HFlowContainer();
            lines.AddChild(row);
            int holder = g.HolderId;
            CardLink(row, Resident(holder), () => OpenCard(holder));
            AddLabel(row, Content.GrudgeWords(g.Amount).ToLowerInvariant() + " them", Heat(g.Amount), 14);
            Reason($"They {Over(g)}.");
        }
        if (against.Count == 0) AddLabel(lines, _revealCitizens ? "Nobody" : "Nobody you know of", CardInk);
        AddLabel(lines, "Grudges run from prickly to sore, scowling, beefing and, at the worst, a vendetta, which never fades and can't be squared. Someone with it in for you is harder to buy or scare and quicker to come forward as a witness; once they're beefing they go to the police or the papers, or put a brick through a window. Money, a good turn, a good word for their business or a soup kitchen in your name all ease a grudge before it gets that far. Someone who hates a neighbour is glad to talk about them.", CardFaint, 12, wrap: true);
    }

    /// <summary>A link on a card, in the card's ink, turning red under the pointer.</summary>
    private static LinkButton CardLink(Container parent, string text, Action go)
    {
        var link = new LinkButton { Text = text, Underline = LinkButton.UnderlineMode.Always, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        link.AddThemeColorOverride("font_color", CardInk);
        link.AddThemeColorOverride("font_hover_color", new Color("9a2a22"));
        link.AddThemeColorOverride("font_pressed_color", new Color("9a2a22"));
        link.AddThemeFontSizeOverride("font_size", 14);
        link.Pressed += go;
        parent.AddChild(link);
        return link;
    }

    /// <summary>Open someone's card and bring the map to where they are, or to their home if they're indoors.</summary>
    private void OpenCard(int citizen)
    {
        if (_map.Directory is not { } d || !d.Exists(citizen)) return;
        _map.Select(citizen: citizen);
        _overheard = null;
        RefreshBusiness();
        // Out on the street the ring round them marks them; indoors, their front door is marked.
        if (_map.WhereIs(citizen) is { } at)
            _map.PanTo(W.Map.LotsOfBlock(d.Where(citizen).Bx, d.Where(citizen).By).OrderBy(l => new Vector2(l.X + 0.5f, l.Y + 0.5f).DistanceSquaredTo(at)).First(), mark: false);
        else _map.PanTo(W.Map.LotAt(d.Get(citizen).HomeLotId));
    }

    /// <summary>A line on the card for each thing you've found out that they're hiding.</summary>
    private void Secrets(GridContainer grid, int id, Citizen c)
    {
        var d = _map.Directory!;
        var secrets = _revealCitizens ? d.SecretsOf(id).ToList() : W.KnownSecretsOf(id).ToList();
        foreach (var s in secrets)
        {
            AddLabel(grid, Content.Label(s.Vice), new Color("9a2a22"));
            var secret = s;
            Action? go = s.Vice switch
            {
                Vice.Lover when s.Other >= 0 => () => OpenCard(secret.Other),
                Vice.Drink or Vice.Gambling or Vice.Thief when s.Other >= 0 => () => _map.PanTo(W.LotOf(W.BusinessById(secret.Other))),
                _ => null,
            };
            if (go != null) CardLink(grid, SecretText(s, c), go);
            else AddLabel(grid, SecretText(s, c), CardInk, wrap: true).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        if (_revealCitizens && secrets.Count == 0)
        {
            AddLabel(grid, "Secrets", CardFaint);
            AddLabel(grid, "Clean as a whistle", CardInk);
        }
        else if (!_revealCitizens)
        {
            AddLabel(grid, secrets.Count == 0 ? "Secrets" : "Others", CardFaint);
            AddLabel(grid, "???", CardFaint);
        }
    }

    /// <summary>A secret in a few words, naming whoever and wherever you know of.</summary>
    private string SecretText(Secret s, Citizen c)
    {
        var d = _map.Directory!;
        string Place() => s.Other >= 0 ? Short(W.BusinessById(s.Other).Name) : "a place nearby";
        bool married = c.Ties.Any(t => t.Kin == Kin.Spouse);
        return s.Vice switch
        {
            Vice.Lover => $"Seeing {(s.Other >= 0 && KnownAbout(s.Other).HasFlag(Fact.Name) ? d.Get(s.Other).Name : "someone on the block")} on the quiet{(married ? $", behind their {(c.Woman ? "husband's" : "wife's")} back" : "")}",
            Vice.Drink => $"Drinks in the back room at {Place()}",
            Vice.Gambling => $"Loses more than they earn at cards at {Place()}",
            Vice.Debt => "Owes a loan shark more than they can pay",
            Vice.Thief => s.Other >= 0 ? $"Has a hand in the till at {Place()}" : "Fences stolen goods",
            _ => $"Wanted for {Content.Crime(s)}",
        };
    }

    private static string Short(string name) => name.IndexOf(", ", StringComparison.Ordinal) is int comma and > 0 ? name[..comma] : name;

    /// <summary>The people you know something about, so you can find their cards again.</summary>
    private void Acquaintances(VBoxContainer panel)
    {
        if (_map.Directory is not { } d) return;
        var known = W.Acquaintances.Where(kv => kv.Value.Known != Fact.None && d.Exists(kv.Key))
            .OrderByDescending(kv => System.Numerics.BitOperations.PopCount((uint)kv.Value.Known)).ThenBy(kv => kv.Key).Take(12).ToList();
        if (known.Count == 0) return;
        panel.AddChild(new HSeparator());
        AddLabel(panel, "Your directory", Palette.Ink, 16);
        foreach (var (id, a) in known)
        {
            var c = d.Get(id);
            string name = a.Known.HasFlag(Fact.Name) ? c.Name : "Name unknown";
            string job = a.Known.HasFlag(Fact.Job) ? $", {c.Job.ToLowerInvariant()[0]}{c.Job[1..]}" : "";
            AddButton(panel, $"{name}{job}", () => OpenCard(id));
        }
    }

    /// <summary>A street address for a lot, as the directory would print it.</summary>
    private string Address(Lot lot)
    {
        if (lot.BusinessId is int biz and >= 0) return $"Over {W.BusinessById(biz).Name}";
        int number = 100 * (lot.X / CityMap.StrideX + 1) + (lot.X % CityMap.StrideX) * 12 + (lot.FrontY < lot.Y ? 1 : 2);
        return $"{number} {W.Map.StreetOf(lot)} St";
    }
}
