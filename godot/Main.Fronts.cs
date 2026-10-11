using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>Fronts and laundering in the side panel: what a front does, buying one, and how clean the money is.</summary>
public partial class Main
{
    /// <summary>A line or two under a business's details when someone owns it outright.</summary>
    private void FrontLines(Business b)
    {
        if (!b.IsFront) return;
        if (b.OwnerGangId != Me)
        {
            AddLabel(_businessPanel, $"A front for {W.GangById(b.OwnerGangId).Name}.", Palette.Gang(W, b.OwnerGangId), wrap: true);
            return;
        }
        AddLabel(_businessPanel, $"Your front. It makes ${b.Takings * Content.FrontProfitPercent / 100} a week clean, and can wash ${Content.LaunderCapacity(b)} of dirty money a week.", Palette.Player, wrap: true);
        if (!World.Working(b))
            AddLabel(_businessPanel, b.IsOpen ? "It's no use while another outfit's men watch the place. Take it back." : "It washes nothing while it's shut.", Palette.Bad, wrap: true);
    }

    /// <summary>The offer to buy one of your businesses outright.</summary>
    private void FrontOffer(Business b)
    {
        if (b.IsFront || !b.IsOpen) return;
        int price = Content.FrontPrice(b);
        _businessPanel.AddChild(new HSeparator());
        AddLabel(_businessPanel, "Buy it outright", Palette.Ink);
        AddLabel(_businessPanel, $"The owner will sell for ${price:N0}. As a front it makes ${b.Takings * Content.FrontProfitPercent / 100} a week clean and washes up to ${Content.LaunderCapacity(b)} a week of dirty money, less the bookkeeper's {Content.LaunderFeePercent}%. You stop collecting protection from it.", Palette.InkQuiet, 13, wrap: true);
        var problem = W.CannotBuyFront(W.Player, b);
        var button = AddButton(_businessPanel, problem == null ? $"Buy it, ${price:N0}" : problem, () =>
        {
            if (W.BuyFront(W.Player, b) < 0) return;
            Audio.Instance?.Play("cash", 0.6f);
            RefreshAll();
        });
        button.Disabled = problem != null;
    }

    /// <summary>The cash in the top bar, with the clean and dirty split as its tooltip. Red once the tax men could take an interest.</summary>
    private void CashLabel(Gang p)
    {
        _cash.Text = $"${p.Cash:N0}";
        long capacity = W.LaunderCapacity(p.Id);
        _cash.TooltipText = $"${p.Clean:N0} clean, ${p.Dirty:N0} dirty.\n" +
            (capacity > 0 ? $"Your fronts can wash ${capacity:N0} a week.\n" : "Buy a business you protect to wash money through it.\n") +
            $"Above ${Content.TaxCaseDirty:N0} of dirty money, the Treasury may bring a tax case.";
        _cash.MouseFilter = Control.MouseFilterEnum.Stop;
        _cash.AddThemeColorOverride("font_color", p.Dirty >= Content.TaxCaseDirty ? Palette.Bad : Palette.Ink);
    }
}
