using DryTown.Core;
using Godot;

/// <summary>Scenarios and the campaign: the briefing when one starts, the goal at the top of the report, and the verdict.</summary>
public partial class Main
{
    /// <summary>The scenario a new game starts, or None for the open city.</summary>
    private ScenarioKind _scenario = ScenarioKind.None;

    private const int ScenarioMenuId = 40, CampaignMenuId = 37, NextChapterMenuId = 38, RetryChapterMenuId = 39;

    /// <summary>The settings for the next new game when they're set already: a campaign's next city.</summary>
    private WorldSettings? _nextCity;

    /// <summary>The briefing card while it's up.</summary>
    private PopupPanel? _briefing;

    private void FillScenarioMenu(PopupMenu menu)
    {
        menu.AddItem($"Campaign: one boss through all {Campaign.Chapters.Length} scenarios, 1920 to repeal", CampaignMenuId);
        menu.SetItemTooltip(menu.ItemCount - 1, "Each chapter is a new city in the year the last one's deadline fell. Win or lose, an outfit still standing goes on to the next.");
        if (W.Settings.Campaign is { } c)
        {
            menu.AddItem(c.Chapter + 1 < Campaign.Chapters.Length ? $"Campaign: go on to chapter {c.Chapter + 2}, {Scenarios.Info(Campaign.Chapters[c.Chapter + 1].Kind).Title}" : "Campaign: go on", NextChapterMenuId);
            if (Campaign.CannotGoOn(W) is { } why)
            {
                menu.SetItemDisabled(menu.ItemCount - 1, true);
                menu.SetItemTooltip(menu.ItemCount - 1, why);
            }
            menu.AddItem($"Campaign: try chapter {c.Chapter + 1} again in a new city", RetryChapterMenuId);
        }
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

    /// <summary>A new campaign, the next chapter's city, or this chapter again.</summary>
    private void OnCampaignMenu(long id)
    {
        ulong seed = GD.Randi();
        if (id == CampaignMenuId) _nextCity = Campaign.Start(seed);
        else if (id == NextChapterMenuId && Campaign.CannotGoOn(W) == null) _nextCity = Campaign.Next(W, seed);
        else if (id == RetryChapterMenuId && W.Settings.Campaign != null) _nextCity = Campaign.Retry(W, seed);
        else return;
        NewGame(seed);
    }

    /// <summary>The settings for a new game: the campaign's next city, the scenario's own, or an open city of the chosen size.</summary>
    private WorldSettings NewSettings(ulong seed)
    {
        if (_nextCity is { } next) { _nextCity = null; return next; }
        return _scenario != ScenarioKind.None ? Scenarios.Settings(_scenario, seed) : new WorldSettings { Seed = seed, Size = _citySize };
    }

    /// <summary>"Chapter 2 of 5" in a campaign, else nothing.</summary>
    private string ChapterLabel => W.Settings.Campaign is { } c ? $"Chapter {c.Chapter + 1} of {Campaign.Chapters.Length}" : "";

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
        string chapter = ChapterLabel is { Length: > 0 } l ? $"{l.ToUpperInvariant()}  ·  " : "";
        AddLabel(col, $"{chapter}{info.Title.ToUpperInvariant()}  ·  {Reports.Date(W).ToUpperInvariant()}", Palette.InkQuiet, 12);
        AddLabel(col, $"You run {W.Player.Name}.", Palette.Player, 20, wrap: true);
        if (W.Settings.Campaign is { } c && Campaign.Bridge(c) is { Length: > 0 } bridge) AddLabel(col, bridge, Palette.InkQuiet, 14, wrap: true);
        AddLabel(col, info.Briefing, Palette.Ink, 15, wrap: true);
        AddLabel(col, "THE JOB", Palette.InkQuiet, 12);
        AddLabel(col, Scenarios.Goal(W), Palette.Player, 15, wrap: true);
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
            _ => $"{Scenarios.Deadline(W) - W.Week} weeks left.",
        };
        string chapter = ChapterLabel is { Length: > 0 } l ? $"{l.ToUpperInvariant()}  ·  " : "";
        return $"[color={C(Palette.InkQuiet)}]{chapter}{info.Title.ToUpperInvariant()}[/color]\n[color={C(Palette.Player)}]{Escape(Scenarios.Goal(W))}[/color]\n" +
               $"[color={C(Palette.InkQuiet)}]{Escape(Scenarios.Progress(W))}[/color] {state}\n\n";
    }

    /// <summary>When the week just played won or lost the scenario, say so.</summary>
    private void AnnounceVerdict()
    {
        if (W.Settings.Scenario == ScenarioKind.None || W.Goal.Outcome == ScenarioOutcome.Playing || W.Goal.Week != _replayWeek) return;
        var info = Scenarios.Info(W.Settings.Scenario);
        bool won = W.Goal.Outcome == ScenarioOutcome.Won;
        string goal = Scenarios.Goal(W);
        string text = won
            ? $"{goal}\n\nDone, in {Reports.Date(W, W.Goal.Week)}. The city is yours to carry on in for as long as you like."
            : (W.Player.Alive ? $"{goal}\n\nTime ran out. You can carry on in the city anyway, or try again from the Game menu." : $"{W.Player.Name} is finished. Try again from the Game menu.");
        if (W.Settings.Campaign != null)
        {
            text = Campaign.Verdict(W) is { } verdict ? $"{text.Split("\n\n")[0]}\n\n{verdict}"
                : won || W.Player.Alive ? $"{text.Split("\n\n")[0]}\n\n{(won ? "Done" : "Time ran out")}. Go on to the next city when you're ready, now or later from the Game menu."
                : text;
        }
        var dialog = new AcceptDialog { Title = (ChapterLabel is { Length: > 0 } l ? l + ": " : "") + (won ? $"{info.Title}: won" : $"{info.Title}: lost"), DialogText = text, OkButtonText = "Carry on" };
        if (W.Settings.Campaign != null)
        {
            if (Campaign.CannotGoOn(W) == null) dialog.AddButton("Next chapter", right: true, action: "next");
            if (Campaign.Verdict(W) == null) dialog.AddButton("Try again", right: true, action: "again");
            dialog.CustomAction += action =>
            {
                dialog.Hide();
                dialog.QueueFree();
                OnCampaignMenu(action == "next" ? NextChapterMenuId : RetryChapterMenuId);
            };
        }
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(440, 0));
        Audio.Instance?.Play(won ? "cash" : "whistle", 0.7f);
    }
}
