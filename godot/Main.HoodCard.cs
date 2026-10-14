using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// A gangster's card, yours or another outfit's, on the same paper as a resident's: his particulars, what sort he is,
/// and who he has it in for.
/// </summary>
public partial class Main
{
    private void HoodCard(VBoxContainer panel, Hood man)
    {
        var gang = W.GangById(man.GangId);
        bool mine = man.GangId == Me;
        var lines = NewCard(panel, man.Name);
        string? stamp = man.State switch
        {
            HoodState.Jailed => man.Case != null ? "In a cell, waiting on his trial" : $"Inside, {man.JailWeeks} week{(man.JailWeeks == 1 ? "" : "s")} to go",
            HoodState.Lam => "On the lam, out of town",
            HoodState.Held => "Held for ransom",
            HoodState.Dead => "Dead",
            _ => mine ? "One of yours" : null,
        };
        if (stamp != null) AddLabel(lines, stamp, man.State == HoodState.Free ? new Color("3c6a34") : new Color("9a2a22"), wrap: true);
        CardTabs(lines);
        switch (_cardTab)
        {
            case CardTab.Character: HoodCharacterTab(lines, man); break;
            case CardTab.Grudges: GrudgesTab(lines, Party.Hood, man.Id); break;
            default: HoodParticulars(lines, man, gang); break;
        }

        // What his grudges mean for you.
        var worst = W.AgainstGang(W.Player, Party.Hood, man.Id).Where(g => g.Known || _revealCitizens).OrderByDescending(g => g.Amount).FirstOrDefault();
        if (!mine && worst != null && worst.Amount >= Content.ScowlingAt)
            AddLabel(panel, $"He's {Content.Label(Content.TierOf(worst.Amount)).ToLowerInvariant()} with {(worst.Target == Party.Gang ? "you" : W.HoodById(worst.TargetId).Name)}. Sent after one of your men, he goes at it harder.", Palette.Bad, wrap: true);
        var bitter = W.HeldBy(Party.Hood, man.Id).Where(g => g.Amount >= Content.ScowlingAt && g.Target is Party.Gang or Party.Hood && !(g.Target == Party.Gang && g.TargetId == Me)).OrderByDescending(g => g.Amount).FirstOrDefault();
        if (mine && bitter != null)
            AddLabel(panel, $"He has it in for {(bitter.Target == Party.Gang ? W.GangById(bitter.TargetId).Name : W.HoodById(bitter.TargetId).Name)}. Send him after them and he goes at it harder.", Palette.InkQuiet, wrap: true);
    }

    private void HoodParticulars(VBoxContainer lines, Hood man, Gang gang)
    {
        bool mine = man.GangId == Me;
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 3);
        lines.AddChild(grid);
        void Line(string label, string value)
        {
            AddLabel(grid, label, CardFaint);
            AddLabel(grid, value, CardInk, wrap: true).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        Line("Age", man.Age(W.Week).ToString());
        Line("People", Content.Label(man.Origin));
        Line("Outfit", mine ? $"{gang.Name} (yours)" : gang.Name);
        var crew = W.CrewOfHood(man.Id);
        AddLabel(grid, "Rank", CardFaint);
        if (crew != null && crew.LieutenantHoodId != man.Id)
        {
            int lt = crew.LieutenantHoodId;
            CardLink(grid, $"In {W.HoodById(lt).Name}'s crew", () => OpenHood(lt));
        }
        else
        {
            string rank = gang.BossHoodId == man.Id ? "The boss"
                : gang.HeirHoodId == man.Id ? "Heir to the boss"
                : crew != null ? $"Lieutenant, {crew.MemberIds.Count} {(crew.MemberIds.Count == 1 ? "man" : "men")} under him"
                : man.Family ? "Family of the boss" : "Soldier";
            AddLabel(grid, rank, CardInk, wrap: true).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        Line("Skills", $"Intim. {man.Intimidation} · Muscle {man.Muscle} · Brains {man.Brains} · Stealth {man.Stealth}");
        if (mine)
        {
            Line("Loyalty", $"{man.Loyalty} of 100");
            Line("Ambition", $"{man.Ambition} of 100");
            Line("Wage", $"${man.Wage} a week");
            Line("Joined", man.JoinedWeek <= 0 ? "From the start" : Reports.Date(W, man.JoinedWeek));
        }
        Line("Carries", man.Weapon == Weapon.None ? "Nothing" : Content.Guns[man.Weapon].Label);
        Line("Drives", man.Car == Car.None ? "Walks" : Content.Cars[man.Car].Label);
        int handles = W.Businesses.Count(b => b.HandlerHoodId == man.Id && b.ProtectorGangId == gang.Id);
        if (handles > 0) Line("Collects", $"From {handles} business{(handles == 1 ? "" : "es")}");
        Line("Record", man.Convictions == 0 ? (man.Record.Count == 0 ? "Clean" : "Arrested, never convicted") : $"{man.Convictions} conviction{(man.Convictions == 1 ? "" : "s")}");
        if (!mine && crew != null && crew.LieutenantHoodId == man.Id)
            AddLabel(lines, "If he goes, the best of his men takes over the crew.", CardFaint, 12, wrap: true);
    }

    /// <summary>Where he came from and what sort he is. You know your own men; the street knows the rest.</summary>
    private void HoodCharacterTab(VBoxContainer lines, Hood man)
    {
        AddLabel(lines, "Background", CardFaint, 13);
        AddLabel(lines, Content.Label(man.Background), CardInk, 15);
        string meaning = Content.Meaning(man.Background);
        if (meaning != "") AddLabel(lines, meaning, CardInk, 13, wrap: true);
        lines.AddChild(new ColorRect { Color = CardRule, CustomMinimumSize = new Vector2(0, 1) });
        AddLabel(lines, "Character", CardFaint, 13);
        foreach (var t in man.Traits)
        {
            AddLabel(lines, Content.Label(t), new Color("7a3a2a"), 15);
            AddLabel(lines, Content.HoodMeaning(t), CardInk, 13, wrap: true);
        }
    }
}
