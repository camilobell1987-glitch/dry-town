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
        W.Introduce(id, W.Map.LotAt(c.HomeLotId).WardId, c.Ties);
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
        W.Introduce(id, home.WardId, c.Ties);
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
                Standing.Held => dealt.Note,
                _ => $"Dead. {dealt.Note}",
            };
            AddLabel(lines, stamp, dealt.Standing is Standing.Dead or Standing.Held ? new Color("9a2a22") : new Color("3c6a34"), wrap: true);
        }

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 3);
        lines.AddChild(grid);
        void Line(string label, Fact fact, Func<string> value)
        {
            AddLabel(grid, label, CardFaint);
            bool have = known.HasFlag(fact);
            var text = AddLabel(grid, have ? value() : "???", have ? CardInk : CardFaint, wrap: true);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        Line("Age", Fact.Age, () => c.Age.ToString());
        Line("People", Fact.Origin, () => Content.Label(c.Origin));
        Line("Job", Fact.Job, () => c.Job);
        Line("Works at", Fact.Work, () => c.WorkBusinessId >= 0 ? W.BusinessById(c.WorkBusinessId).Name
            : c.OwnsBusinessId >= 0 ? "Runs the place" : "Nowhere in particular");
        Line("Lives at", Fact.Home, () => Address(home));
        Line("Landlord", Fact.Landlord, () => c.LandlordId >= 0 && c.LandlordId != c.Id ? directory.Get(c.LandlordId).Name
            : c.LandlordId == c.Id || c.OwnsBusinessId >= 0 && W.LotOf(W.BusinessById(c.OwnsBusinessId)).Id == c.HomeLotId ? "Owns the building" : "None");
        Line("Owns", Fact.Owns, () => c.OwnsBusinessId >= 0 ? W.BusinessById(c.OwnsBusinessId).Name
            : c.Owns.Count > 0 ? $"{(c.Owns.Count == 1 ? "A tenement" : $"{c.Owns.Count} tenements")}: {string.Join("; ", c.Owns.Select(l => Address(W.Map.LotAt(l))))}"
            : "Nothing to speak of");
        Family(grid, id, c, known);

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
        if (standing?.Standing == Standing.Held) return;
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
        void Odds()
        {
            var hood = W.HoodById(man.GetSelectedId());
            foreach (var (b, lean, label) in buttons)
            {
                string why = W.CannotLean(W.Player, id, lean) is { } no ? $" · {no}" : "";
                double odds = Simulation.LeanChance(hood, lean, nerve, known, leverage);
                b.Text = $"{label()} · {(reads ? "" : "maybe ")}{odds:P0}{why}";
                b.Disabled = why != "" || lean == Lean.Bribe && W.Player.Cash < price;
            }
        }
        man.ItemSelected += _ => Odds();
        Odds();
        if (W.HostageFrom(W.Player, id) is int held and >= 0)
            AddLabel(panel, $"You're holding their {Content.Label(W.KnownTiesOf(id).First(t => t.Id == held).Kin, directory.Get(held).Woman).ToLowerInvariant()}. They'll do as they're told.", Palette.Good, wrap: true);
        else if (leverage > 0) AddLabel(panel, "You know who their family are, which makes a threat or a shakedown likelier to work. Snatch one of them and they'll do as they're told.", Palette.InkQuiet, wrap: true);
        if (!reads) AddLabel(panel, $"You can't tell yet how much nerve they have, so the odds are a guess. Learn {Content.NerveFacts} things about them to know for sure.", Palette.InkQuiet, wrap: true);
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
            var link = new LinkButton { Text = name, Underline = LinkButton.UnderlineMode.OnHover };
            link.AddThemeColorOverride("font_color", CardInk);
            link.AddThemeColorOverride("font_hover_color", new Color("9a2a22"));
            link.AddThemeFontSizeOverride("font_size", 14);
            int other = tie.Id;
            link.Pressed += () =>
            {
                _map.Select(citizen: other);
                _overheard = null;
                RefreshBusiness();
            };
            grid.AddChild(link);
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
            AddButton(panel, $"{name}{job}", () =>
            {
                _map.Select(citizen: id);
                _map.ResetView(W.Map.LotAt(c.HomeLotId));
                _overheard = null;
                RefreshBusiness();
            });
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
