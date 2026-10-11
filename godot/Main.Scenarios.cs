using DryTown.Core;
using Godot;

/// <summary>Scenarios: the briefing when one starts, the goal at the top of the report, and the verdict.</summary>
public partial class Main
{
    /// <summary>The scenario a new game starts, or None for the open city.</summary>
    private ScenarioKind _scenario = ScenarioKind.None;

    private const int ScenarioMenuId = 40;

    /// <summary>The briefing card while it's up.</summary>
    private PopupPanel? _briefing;

    private void FillScenarioMenu(PopupMenu menu)
    {
        for (int i = 0; i < Scenarios.All.Length; i++)
        {
            var info = Scenarios.All[i];
            menu.AddItem($"Scenario: {info.Title} ({info.Size.ToString().ToLowerInvariant()} city, {info.StartYear} to {info.DeadlineYear})", ScenarioMenuId + i);
            menu.SetItemTooltip(menu.ItemCount - 1, info.Goal);
        }
    }

    private void StartScenario(int index)
    {
        _scenario = Scenarios.All[index].Kind;
        NewGame(GD.Randi());
    }

    /// <summary>The settings for a new game: the scenario's own, or an open city of the chosen size.</summary>
    private WorldSettings NewSettings(ulong seed) =>
        _scenario != ScenarioKind.None ? Scenarios.Settings(_scenario, seed) : new WorldSettings { Seed = seed, Size = _citySize };

    /// <summary>The scenario's card: where you stand at the start, and what you have to do.</summary>
    private void ShowBriefing()
    {
        if (W.Settings.Scenario == ScenarioKind.None) return;
        var info = Scenarios.Info(W.Settings.Scenario);
        var page = new PopupPanel { Exclusive = true };
        page.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Background, BorderColor = Palette.Player, BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        col.AddThemeConstantOverride("separation", 10);
        page.AddChild(col);
        AddLabel(col, $"{info.Title.ToUpperInvariant()}  ·  {Reports.Date(W).ToUpperInvariant()}", Palette.InkQuiet, 12);
        AddLabel(col, $"You run {W.Player.Name}.", Palette.Player, 20, wrap: true);
        AddLabel(col, info.Briefing, Palette.Ink, 15, wrap: true);
        AddLabel(col, "THE JOB", Palette.InkQuiet, 12);
        AddLabel(col, info.Goal, Palette.Player, 15, wrap: true);
        AddLabel(col, Scenarios.Progress(W), Palette.InkQuiet, 13, wrap: true);
        // Wrapped text needs a width to wrap to, or the card grows to the height of the screen.
        foreach (var child in col.GetChildren())
            if (child is Label label) label.CustomMinimumSize = new Vector2(460, 0);
        var go = new Button { Text = "Get to work", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        go.Pressed += page.Hide;
        col.AddChild(go);
        page.PopupHide += page.QueueFree;
        _briefing = page;
        AddChild(page);
        page.PopupCentered();
    }

    /// <summary>The goal and how it stands, at the top of the Sunday report. BBCode.</summary>
    private string GoalSection()
    {
        if (W.Settings.Scenario == ScenarioKind.None) return "";
        var info = Scenarios.Info(W.Settings.Scenario);
        string C(Color c) => "#" + Palette.Hex(c);
        string state = W.Goal.Outcome switch
        {
            ScenarioOutcome.Won => $"[color={C(Palette.Good)}]Won in {Reports.Date(W, W.Goal.Week)}.[/color]",
            ScenarioOutcome.Lost => $"[color={C(Palette.Bad)}]Lost in {Reports.Date(W, W.Goal.Week)}.[/color]",
            _ => $"{Scenarios.Deadline(info) - W.Week} weeks left.",
        };
        return $"[color={C(Palette.InkQuiet)}]{info.Title.ToUpperInvariant()}[/color]\n[color={C(Palette.Player)}]{Escape(info.Goal)}[/color]\n" +
               $"[color={C(Palette.InkQuiet)}]{Escape(Scenarios.Progress(W))}[/color] {state}\n\n";
    }

    /// <summary>When the week just played won or lost the scenario, say so.</summary>
    private void AnnounceVerdict()
    {
        if (W.Settings.Scenario == ScenarioKind.None || W.Goal.Outcome == ScenarioOutcome.Playing || W.Goal.Week != _replayWeek) return;
        var info = Scenarios.Info(W.Settings.Scenario);
        bool won = W.Goal.Outcome == ScenarioOutcome.Won;
        string text = won
            ? $"{info.Goal}\n\nDone, in {Reports.Date(W, W.Goal.Week)}. The city is yours to carry on in for as long as you like."
            : (W.Player.Alive ? $"{info.Goal}\n\nTime ran out. You can carry on in the city anyway, or try again from the Game menu." : $"{W.Player.Name} is finished. Try again from the Game menu.");
        var dialog = new AcceptDialog { Title = won ? $"{info.Title}: won" : $"{info.Title}: lost", DialogText = text, OkButtonText = "Carry on" };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(440, 0));
        Audio.Instance?.Play(won ? "cash" : "whistle", 0.7f);
    }
}
