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

    /// <summary>What you know about a resident.</summary>
    private Fact KnownAbout(int citizen) => _revealCitizens ? Fact.All : Fact.None;

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
        if (known != Fact.All)
            AddLabel(panel, "You don't know the first thing about this one yet. Asking around, having them followed and dealing with them will fill in the card.", Palette.InkQuiet, wrap: true);
    }

    /// <summary>A street address for a lot, as the directory would print it.</summary>
    private string Address(Lot lot)
    {
        if (lot.BusinessId is int biz and >= 0) return $"Over {W.BusinessById(biz).Name}";
        int number = 100 * (lot.X / CityMap.StrideX + 1) + (lot.X % CityMap.StrideX) * 12 + (lot.FrontY < lot.Y ? 1 : 2);
        return $"{number} {W.Map.StreetOf(lot)} St";
    }
}
