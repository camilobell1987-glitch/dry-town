using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>City events on screen: a front page when one breaks, and what's in the papers on the report and at City Hall.</summary>
public partial class Main
{
    private static readonly Color Newsprint = new("e9e1cc"), PrintInk = new("1f1c18"), PrintQuiet = new("5d564b");

    /// <summary>Anything that broke on Sunday goes on a front page the player has to put down.</summary>
    private void AnnounceNews()
    {
        var fresh = W.CityEvents.Where(e => e.StartWeek == W.Week).OrderBy(e => e.Kind).ToList();
        if (fresh.Count == 0) return;
        var serif = new SystemFont { FontNames = new[] { "Georgia", "DejaVu Serif", "Liberation Serif", "serif" } };

        var page = new PopupPanel { Exclusive = true };
        page.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Newsprint, BorderColor = PrintInk, BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        col.AddThemeConstantOverride("separation", 8);
        page.AddChild(col);

        Label Print(string text, int size, Color colour, Font? font = null, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = align, CustomMinimumSize = new Vector2(420, 0) };
            l.AddThemeColorOverride("font_color", colour);
            l.AddThemeFontSizeOverride("font_size", size);
            if (font != null) l.AddThemeFontOverride("font", font);
            col.AddChild(l);
            return l;
        }
        Rule(col, 2);
        Print("THE CITY TRIBUNE", 30, PrintInk, serif, HorizontalAlignment.Center);
        Rule(col, 1);
        Print($"EXTRA  ·  {Reports.Date(W).ToUpperInvariant()}  ·  TWO CENTS", 11, PrintQuiet, null, HorizontalAlignment.Center);
        Rule(col, 2);
        foreach (var e in fresh)
        {
            var info = Content.CityEvents[e.Kind];
            Print(W.Describe(e, info.Headline), 21, PrintInk, serif);
            Print(W.Describe(e, info.Effect), 13, PrintQuiet);
        }
        var read = new Button { Text = "Read on", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        read.Pressed += page.Hide;
        col.AddChild(read);
        page.PopupHide += page.QueueFree;
        AddChild(page);
        page.PopupCentered();
        Audio.Instance?.Play("paper");
    }

    private static void Rule(Container parent, int thickness) =>
        parent.AddChild(new ColorRect { Color = PrintInk, CustomMinimumSize = new Vector2(0, thickness) });

    /// <summary>Each event in progress: headline, what it does, and how long it has left. BBCode for the report.</summary>
    private string InThePapers()
    {
        if (W.CityEvents.Count == 0) return "";
        string C(Color c) => "#" + Palette.Hex(c);
        var sb = new System.Text.StringBuilder($"[color={C(Palette.InkQuiet)}]IN THE PAPERS[/color]\n");
        foreach (var e in W.CityEvents.OrderBy(e => e.StartWeek))
        {
            var info = Content.CityEvents[e.Kind];
            int left = e.UntilWeek - W.Week;
            sb.Append($"[b]{Escape(W.Describe(e, info.Headline))}[/b] [color={C(Palette.InkQuiet)}]{Escape(W.Describe(e, info.Effect))} {left} week{(left == 1 ? "" : "s")} left.[/color]\n");
        }
        return sb.Append('\n').ToString();
    }

    /// <summary>The same, at the top of the City Hall tab.</summary>
    private void NewsSection()
    {
        if (W.CityEvents.Count == 0) return;
        AddLabel(_hallPanel, "In the papers", Palette.Ink);
        foreach (var e in W.CityEvents.OrderBy(e => e.StartWeek))
        {
            var info = Content.CityEvents[e.Kind];
            int left = e.UntilWeek - W.Week;
            AddLabel(_hallPanel, W.Describe(e, info.Headline), Palette.Player, 13, wrap: true);
            AddLabel(_hallPanel, $"{W.Describe(e, info.Effect)} {left} week{(left == 1 ? "" : "s")} left.", Palette.InkQuiet, 13, wrap: true);
        }
        _hallPanel.AddChild(new HSeparator());
    }
}
