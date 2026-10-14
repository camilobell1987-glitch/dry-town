using Godot;

/// <summary>A link that, when the pointer rests on it, shows a little card of help in the rolodex's paper and ink.</summary>
public partial class HelpLink : LinkButton
{
    /// <summary>A heading, then lines of help, one paragraph each.</summary>
    public string Heading { get; set; } = "";
    public string[] Lines { get; set; } = System.Array.Empty<string>();

    public override GodotObject _MakeCustomTooltip(string forText)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("efe6cf"), BorderColor = new Color("cfc3a5"), BorderWidthBottom = 2, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 10,
        });
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 5);
        card.AddChild(lines);
        Label Add(string text, Color colour, int size)
        {
            var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) };
            label.AddThemeColorOverride("font_color", colour);
            label.AddThemeFontSizeOverride("font_size", size);
            lines.AddChild(label);
            return label;
        }
        if (Heading != "") Add(Heading, new Color("7a3a2a"), 15);
        foreach (var line in Lines) Add(line, new Color("2a2622"), 13);
        return card;
    }
}
