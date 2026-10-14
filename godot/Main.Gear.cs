using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>The side panel for the gun shops and car dealers, and the gear section on one of your own men.</summary>
public partial class Main
{
    /// <summary>The man the shop panel is buying for. Kept while you move between shops.</summary>
    private int _shopFor = -1;

    private List<Hood> MyMen() => W.HoodsOf(Me).OrderBy(h => h.Id == W.Player.BossHoodId ? 0 : 1).ThenByDescending(h => h.Strength).ThenBy(h => h.Id).ToList();

    private string GearLine(Hood h) =>
        $"Carries {(h.Weapon == Weapon.None ? "nothing" : "a " + Content.Guns[h.Weapon].Label)}; {Driving(h)}.";

    /// <summary>What a man drives, whether the police are after it, or where it's hidden.</summary>
    private string Driving(Hood h)
    {
        if (h.Car != Car.None)
            return $"drives a {Content.Cars[h.Car].Label}" + (h.CarWanted > 0 ? $" the police are looking for" : "");
        return W.LockupWithCarOf(h) is { } l && l.GangId == h.GangId && h.GangId == Me
            ? $"walks while his {Content.Cars[l.Car].Label} waits in a lockup"
            : "walks";
    }

    private void ShopPanel(Lot lot)
    {
        var panel = _businessPanel;
        bool guns = lot.Use == LotUse.GunShop;
        AddLabel(panel, World.ShopName(W.Map, lot), Palette.Ink, 18);
        AddLabel(panel, guns
            ? "Guns make a man harder in a fight and quicker to frighten a shopkeeper. They count for less going in through someone else's door than holding your own, and the heavy ones bring the police."
            : "A car gets your men to a job faster and away when it goes wrong: fewer killed, fewer caught. The best car on a job carries everyone. An armoured Cadillac also makes a man hard to hit.",
            Palette.InkQuiet, wrap: true);
        AddLabel(panel, $"{W.Map.Distance(W.HqOf(W.Player), lot) / (double)CityMap.StrideX:0.#} blocks from your HQ", Palette.InkQuiet);
        panel.AddChild(new HSeparator());
        if (!W.Player.Alive) return;

        var men = MyMen();
        if (men.Count == 0) { AddLabel(panel, "You have nobody to buy for.", Palette.InkQuiet); return; }
        if (men.All(h => h.Id != _shopFor)) _shopFor = men[0].Id;
        AddLabel(panel, "Buying for", Palette.Ink);
        var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in men)
        {
            string has = guns ? (h.Weapon == Weapon.None ? "unarmed" : Content.Guns[h.Weapon].Label) : (h.Car == Car.None ? "on foot" : Content.Cars[h.Car].Label);
            pick.AddItem($"{h.Name} · {has}{(h.State == HoodState.Jailed ? " · in jail" : "")}", h.Id);
        }
        pick.Select(men.FindIndex(h => h.Id == _shopFor));
        pick.ItemSelected += i => { _shopFor = pick.GetItemId((int)i); RefreshBusiness(); };
        panel.AddChild(pick);
        var man = W.HoodById(_shopFor);

        if (guns)
            foreach (var (kind, g) in Content.Guns.Where(x => x.Key != Weapon.None))
            {
                string effect = $"+{g.Firepower} strength, {g.Extort * 100:0}% likelier to scare an owner" + (g.Heat > 0 ? $", +{g.Heat} heat in a gunfight" : "");
                int cost = g.Price - Content.TradeIn(Content.Guns[man.Weapon].Price);
                ShopRow(panel, g.Label, effect, g.Price, cost, W.CannotArm(W.Player, man, kind), () => W.Arm(W.Player, man, kind));
            }
        else
            foreach (var (kind, c) in Content.Cars.Where(x => x.Key != Car.None))
            {
                string effect = $"{c.Speed:0.#}× as fast as walking, {c.Getaway * 100:0}% fewer men lost when a job goes wrong" + (kind == Car.Cadillac ? ", and hard to shoot into" : "");
                int cost = c.Price - Content.TradeIn(Content.Cars[man.Car].Price);
                ShopRow(panel, c.Label, effect, c.Price, cost, W.CannotDrive(W.Player, man, kind), () => W.Drive(W.Player, man, kind));
            }
        AddLabel(panel, "Whatever he has now is traded in for half what it cost. A man who is killed loses what he carried.", Palette.InkQuiet, wrap: true);
    }

    private void ShopRow(VBoxContainer panel, string label, string effect, int price, int cost, string? problem, Func<int> buy)
    {
        panel.AddChild(new HSeparator());
        var row = new HBoxContainer();
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddLabel(text, $"{char.ToUpper(label[0])}{label[1..]}  ·  ${price}", Palette.Ink);
        AddLabel(text, effect, Palette.InkQuiet, 13, wrap: true);
        row.AddChild(text);
        string caption = problem switch
        {
            null => cost < price ? $"Trade in, ${cost}" : $"Buy, ${cost}",
            _ when problem.StartsWith("He already") => "He has one",
            _ when problem.Contains("heavier") || problem.Contains("better") => "Has better",
            _ => $"${cost}",
        };
        var button = AddButton(row, caption, () =>
        {
            if (buy() < 0) return;
            Audio.Instance?.Play("cash", 0.6f);
            RefreshAll();
        });
        button.Disabled = problem != null;
        button.TooltipText = problem ?? "";
        panel.AddChild(row);
    }

    /// <summary>What one of your men carries and drives, with ways to hand it on or buy better.</summary>
    private void OwnManGear(VBoxContainer panel, Hood man)
    {
        panel.AddChild(new HSeparator());
        AddLabel(panel, GearLine(man), Palette.Ink, wrap: true);
        var others = MyMen().Where(h => h.Id != man.Id).ToList();
        if (man.Weapon != Weapon.None) HandRow(panel, man, others.Where(h => h.State != HoodState.Jailed).ToList(), car: false);
        if (man.Car != Car.None) HandRow(panel, man, others, car: true);
        var buttons = new HBoxContainer();
        AddButton(buttons, "Find a gun shop", () => GoShopping(man, LotUse.GunShop));
        AddButton(buttons, "Find a car dealer", () => GoShopping(man, LotUse.CarDealer));
        AddButton(buttons, "Lockups", FindLockup);
        panel.AddChild(buttons);
    }

    private void HandRow(VBoxContainer panel, Hood man, List<Hood> to, bool car)
    {
        if (to.Count == 0) return;
        var row = new HBoxContainer();
        var pick = new OptionButton { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var h in to)
            pick.AddItem($"{h.Name} · {(car ? (h.Car == Car.None ? "on foot" : Content.Cars[h.Car].Label) : (h.Weapon == Weapon.None ? "unarmed" : Content.Guns[h.Weapon].Label))}", h.Id);
        row.AddChild(pick);
        AddButton(row, car ? "Give him the car" : "Give him the gun", () =>
        {
            if (W.HandOver(man, W.HoodById(pick.GetSelectedId()), car)) RefreshAll();
        });
        panel.AddChild(row);
    }

    /// <summary>Open the nearest shop of a kind in the side panel, already buying for this man.</summary>
    private void GoShopping(Hood man, LotUse kind)
    {
        var hq = W.HqOf(W.Player);
        var shop = W.Shops(kind).OrderBy(l => W.Map.Distance(hq, l)).ThenBy(l => l.Id).FirstOrDefault();
        if (shop == null) return;
        _shopFor = man.Id;
        _map.Select(shop: shop.Id);
        _map.ResetView(shop);
        RefreshBusiness();
    }
}
