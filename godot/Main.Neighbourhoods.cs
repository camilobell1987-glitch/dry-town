using System.Linq;
using DryTown.Core;

/// <summary>The neighbourhoods on the business panel and at City Hall: whose people live where.</summary>
public partial class Main
{
    private static string An(Ethnicity e) => e == Ethnicity.Irish ? "an Irish" : $"a {Content.Label(e)}";

    /// <summary>Whose neighbourhood a business is in, and what that means for your men.</summary>
    private void NeighbourhoodLine(Business b)
    {
        if (W.Wards.Count == 0) return;
        var ward = Politics.WardOf(W, b);
        var people = W.Neighbourhood(ward);
        int home = W.AvailableHoodsOf(Me).Count(h => h.Origin == people);
        string mine = W.PeopleOf(W.Player) == people ? " Your own people: they mind paying you a little less." : "";
        AddLabel(_businessPanel, $"{ward.Name}, {An(people)} neighbourhood.{mine} " +
            (home > 0 ? $"You have {home} {Content.Label(people)} {(home == 1 ? "man" : "men")} free to lean on the owner." : $"None of your free men are {Content.Label(people)}."),
            Palette.InkQuiet, 13, wrap: true);
    }

    /// <summary>The selected ward's people, and what it means at the polls.</summary>
    private void WardPeopleLine(Ward ward)
    {
        var people = W.Neighbourhood(ward);
        int mine = W.HoodsOf(Me).Count(h => h.IsActive && h.Origin == people);
        AddLabel(_hallPanel, $"{An(people)} neighbourhood. A {Content.Label(people)} man on the ballot here is worth {Content.HomeCandidate} more votes, and gets out half as many again at the polls. " +
            $"You have {mine} {Content.Label(people)} {(mine == 1 ? "man" : "men")}.", Palette.InkQuiet, 13, wrap: true);
    }

    /// <summary>A man's people, after his name in a picker, when they live where he's going.</summary>
    private string Among(Hood h, Business b) => W.Wards.Count > 0 && W.AtHome(h, b) ? $" · {Content.Label(h.Origin)}, among his own" : "";
}
