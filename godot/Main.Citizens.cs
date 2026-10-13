using System;
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
        W.Meet(id, W.Map.LotAt(c.HomeLotId).WardId);
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

        if (_map.WhereIs(id) == null) AddLabel(panel, "Indoors just now.", Palette.InkQuiet);
        else AddLabel(panel, $"Seen on {W.Map.StreetOf(seen)} St in {W.Wards.ElementAtOrDefault(seen.WardId)?.Name ?? "the district"}.", Palette.InkQuiet, wrap: true);
        if (_overheard != null) AddLabel(panel, _overheard, Palette.Ink, wrap: true);
        if (known == Fact.All) return;
        if (known == Fact.None)
            AddLabel(panel, "You don't know the first thing about this one yet.", Palette.InkQuiet, wrap: true);
        AddLabel(panel, "Word on the street fills in a line now and then on anyone you've come across, more often where you hold turf.", Palette.InkQuiet, wrap: true);

        bool Concerns(Order o) => o switch
        {
            AskOrder a => a.CitizenId == id,
            TailOrder t => t.CitizenId == id,
            _ => false,
        };
        ShowQueued(panel, Concerns);
        if (!CanGiveOrders(panel)) return;
        string who = WhoIs(c, known, seen);
        if (!AlreadyGiven(o => o is AskOrder a && a.CitizenId == id))
        {
            var ask = AddButton(panel, $"Ask around about them (${Content.AskPrice})", () => Give(new AskOrder(Me, id, c.HomeLotId, who)));
            ask.Disabled = W.Player.Cash < Content.AskPrice;
        }
        if (AlreadyGiven(o => o is TailOrder t && t.CitizenId == id)) return;
        var hoods = FreeHoods().OrderByDescending(h => h.Stealth).ToList();
        if (hoods.Count == 0) { AddLabel(panel, "All your men are busy, so nobody can follow them.", Palette.InkQuiet, wrap: true); return; }
        AddLabel(panel, "Have a man follow them for the week. He'll see where they live and work, and a sharp one picks up more. If he's spotted, he only learns the address.", Palette.Ink, wrap: true);
        var man = HoodPicker(hoods, h => $"{Simulation.SpottedChance(h):P0} spotted");
        AddButton(panel, "Have them tailed", () => Give(new TailOrder(Me, man.GetSelectedId(), id, c.HomeLotId, who)));
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
