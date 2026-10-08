using DryTown.Core;
using Godot;

/// <summary>
/// Phase 1 screen: the planning phase as text. Buttons send the common commands; the
/// input line takes any order the console runner accepts. Phase 2 replaces this with the
/// planning screen and live city view.
/// </summary>
public partial class Main : Control
{
    private CommandShell _shell = null!;
    private RichTextLabel _output = null!;
    private Label _status = null!;
    private LineEdit _input = null!;

    private static readonly (string Label, string Command)[] Buttons =
    {
        ("Plan for me", "auto"), ("Orders", "orders"), ("End week", "end"),
        ("Gangs", "gangs"), ("My hoods", "hoods"), ("My turf", "turf"),
        ("Targets", "targets"), ("Log", "log"), ("Help", "help"),
    };

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 12);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        _status = new Label();
        column.AddChild(_status);

        var bar = new HFlowContainer();
        foreach (var (label, command) in Buttons)
        {
            var button = new Button { Text = label };
            button.Pressed += () => Run(command);
            bar.AddChild(button);
        }
        var newGame = new Button { Text = "New city" };
        newGame.Pressed += () => NewGame(GD.Randi());
        bar.AddChild(newGame);
        column.AddChild(bar);

        _output = new RichTextLabel
        {
            ScrollFollowing = true,
            SelectionEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _output.AddThemeFontOverride("normal_font", new SystemFont
        {
            FontNames = new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New", "monospace" },
        });
        column.AddChild(_output);

        _input = new LineEdit { PlaceholderText = "Type an order, for example: extort 2 14   (Enter to queue it)" };
        _input.TextSubmitted += text =>
        {
            Run(text);
            _input.Clear();
        };
        column.AddChild(_input);

        NewGame(GD.Randi());
    }

    private void NewGame(ulong seed)
    {
        _shell = new CommandShell(Simulation.New(new WorldSettings { Seed = seed }));
        _output.Clear();
        _output.AddText($"{_shell.Welcome}City seed {seed}.\n\n{CommandShell.Help}");
        Refresh();
    }

    private void Run(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        _output.AddText($"\n> {command}\n");
        var result = _shell.Execute(command);
        if (result == null)
        {
            GetTree().Quit();
            return;
        }
        _output.AddText(result);
        Refresh();
        _input.GrabFocus();
    }

    private void Refresh() => _status.Text = _shell.Prompt;
}
