using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// The crew as a desk rolodex: one index card at a time with a mugshot and his numbers, the tops of
/// the next cards standing up behind it. Roll the wheel or press the arrows to flip; click a card
/// top to jump to that man; click the front card to pick him out on the map.
/// </summary>
public partial class Rolodex : Control
{
    public event Action<int>? HoodPicked;

    public World? World { get; set; }
    public int GangId { get; set; } = -1;

    /// <summary>What each man is doing this week, if anything.</summary>
    public Func<int, string?> JobOf { get; set; } = _ => null;

    private readonly List<Hood> _cards = new();
    private int _index;
    private int _shownId = -1;

    // The flip: +1 while rolling forward, -1 back, easing to zero. The old card falls away in the first half.
    private float _flip;
    private Hood? _leaving;

    private readonly List<(Rect2 Rect, int Index)> _tabs = new();
    private Rect2 _front, _up, _down, _turn;

    /// <summary>The cards are turned over to show the men's rap sheets.</summary>
    private bool _back;

    /// <summary>Show the front of the cards, or the rap sheets on the back.</summary>
    public bool ShowSheets { get => _back; set { _back = value; QueueRedraw(); } }

    private static readonly Color Paper = new("efe6cf");
    private static readonly Color PaperEdge = new("cfc3a5");
    private static readonly Color Rule = new("9fb4c8");
    private static readonly Color RedRule = new("c46a5c");
    private static readonly Color Type = new("2a2622");
    private static readonly Color TypeQuiet = new("6d6457");
    private static readonly Color Metal = new("6e6a66");
    private static readonly Color MetalDark = new("3b3836");

    private const float CardHeight = 368;
    private const float TabHeight = 22;

    public Rolodex()
    {
        CustomMinimumSize = new Vector2(208, 0);
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>Re-reads the gang, keeping the same man in front if he is still with it.</summary>
    public void Refresh()
    {
        _cards.Clear();
        if (World is { } w && GangId >= 0)
        {
            var gang = w.GangById(GangId);
            var men = w.HoodsOf(GangId).ToList();
            // Boss and heir first, then each crew behind its lieutenant, then the loose men.
            int Rank(Hood h)
            {
                if (h.Id == gang.BossHoodId) return 0;
                if (h.Id == gang.HeirHoodId) return 1;
                var crew = w.CrewOfHood(h.Id);
                return crew == null ? 100000 + h.Id : 10 + crew.Id * 10 + (crew.LieutenantHoodId == h.Id ? 0 : 1 + crew.MemberIds.IndexOf(h.Id));
            }
            _cards.AddRange(men.OrderBy(Rank));
        }
        int at = _cards.FindIndex(h => h.Id == _shownId);
        _index = at >= 0 ? at : Math.Clamp(_index, 0, Math.Max(0, _cards.Count - 1));
        _shownId = _cards.Count > 0 ? _cards[_index].Id : -1;
        QueueRedraw();
    }

    /// <summary>Flip round to this man's card.</summary>
    public void Show(int hoodId)
    {
        int at = _cards.FindIndex(h => h.Id == hoodId);
        if (at >= 0) FlipTo(at);
    }

    private void FlipTo(int index)
    {
        if (_cards.Count == 0) return;
        index = ((index % _cards.Count) + _cards.Count) % _cards.Count;
        if (index == _index) return;
        _leaving = _cards[_index];
        _flip = index > _index ? 1 : -1;
        _index = index;
        _shownId = _cards[index].Id;
        Audio.Instance?.Play("click", 0.25f);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_flip == 0) return;
        float step = (float)delta * 5.5f;
        _flip = _flip > 0 ? Mathf.Max(0, _flip - step) : Mathf.Min(0, _flip + step);
        if (_flip == 0) _leaving = null;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelDown) { FlipTo(_index + 1); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.WheelUp) { FlipTo(_index - 1); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.Right && _front.HasPoint(mb.Position)) { ShowSheets = !_back; AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                var p = mb.Position;
                if (_turn.HasPoint(p)) ShowSheets = !_back;
                else if (_up.HasPoint(p)) FlipTo(_index - 1);
                else if (_down.HasPoint(p)) FlipTo(_index + 1);
                else if (_front.HasPoint(p) && _shownId >= 0) HoodPicked?.Invoke(_shownId);
                else foreach (var (rect, i) in _tabs) if (rect.HasPoint(p)) { FlipTo(i); break; }
                AcceptEvent();
            }
        }
    }

    public override string _GetTooltip(Vector2 at)
    {
        foreach (var (rect, i) in _tabs) if (rect.HasPoint(at)) return _cards[i].Name;
        if (_turn.HasPoint(at)) return _back ? "Turn the cards back over." : "Turn the cards over to read the men's rap sheets.";
        if (_front.HasPoint(at)) return "Click to find him on the map. Roll the wheel to flip cards. Right-click to turn the cards over.";
        return "";
    }

    // ---- Drawing --------------------------------------------------------------

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        float w = Size.X;
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Panel);
        DrawString(font, new Vector2(10, 20), "THE CREW", HorizontalAlignment.Left, -1, 13, Palette.InkQuiet);
        if (_cards.Count > 0)
            DrawString(font, new Vector2(10, 20), $"{_index + 1} of {_cards.Count}", HorizontalAlignment.Right, w - 20, 13, Palette.InkQuiet);
        _tabs.Clear();
        if (World == null || _cards.Count == 0)
        {
            DrawString(font, new Vector2(10, 46), "No men on the books.", HorizontalAlignment.Left, w - 20, 14, Palette.InkQuiet);
            return;
        }

        // Card tops stand behind the front card, as many as fit; the base sits under the front card.
        float cardW = w - 20;
        float cardH = Mathf.Min(CardHeight, Size.Y - 34 - 56);
        int behind = Math.Clamp(Math.Min(_cards.Count - 1, (int)((Size.Y - 34 - 56 - cardH) / TabHeight)), 0, 40);
        float cardTop = 34 + behind * TabHeight + 6;
        float baseTop = cardTop + cardH + 10;
        var cardRect = new Rect2(10, cardTop, cardW, cardH);

        // Nearest lowest and widest.
        for (int k = behind; k >= 1; k--)
        {
            int i = (_index + k) % _cards.Count;
            float inset = Mathf.Min(k, 6) * 3;
            var r = new Rect2(10 + inset, cardTop - k * TabHeight, cardW - inset * 2, TabHeight + 6);
            float shade = 1 - Mathf.Min(k, 8) * 0.045f;
            DrawCardTop(font, r, _cards[i], shade, k);
            _tabs.Add((new Rect2(r.Position, new Vector2(r.Size.X, TabHeight)), i));
        }

        // The spindle the cards turn on.
        DrawRect(new Rect2(18, baseTop - 8, w - 36, 6), MetalDark);

        // The front card, folding over the rail as it flips.
        float t = Mathf.Abs(_flip);
        if (_leaving != null && t > 0.5f) DrawFolded(font, cardRect, _leaving, (t - 0.5f) * 2, _flip > 0);
        else DrawFolded(font, cardRect, _cards[_index], 1 - t * 2, _flip < 0);
        _front = cardRect;
        _turn = new Rect2(cardRect.End.X - 92, cardRect.End.Y - 26, 86, 16);

        DrawBase(font, new Rect2(6, baseTop, w - 12, 40));
    }

    private void DrawFolded(Font font, Rect2 r, Hood h, float scale, bool fromTop)
    {
        scale = Mathf.Clamp(scale, 0, 1);
        if (scale <= 0.02f) return;
        // Squash towards the rail at the bottom, the way a card falls over on a rolodex.
        float pivot = r.End.Y;
        DrawSetTransformMatrix(new Transform2D(new Vector2(1, 0), new Vector2(0, scale), new Vector2(0, pivot * (1 - scale))));
        if (_back) DrawBack(font, r, h);
        else DrawCard(font, r, h);
        TurnLink(font, r);
        if (scale < 1) DrawRect(r, new Color(0, 0, 0, (1 - scale) * (fromTop ? 0.35f : 0.5f)));
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    private void DrawCardTop(Font font, Rect2 r, Hood h, float shade, int depth)
    {
        DrawRect(r, (PaperEdge * shade) with { A = 1 });
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 2)), (Paper * shade) with { A = 1 });
        // A little index tab on the card, staggered so the names don't all line up.
        float tabW = Mathf.Min(r.Size.X * 0.62f, 120);
        float tabX = r.Position.X + 6 + (h.Id * 37 % 5) * (r.Size.X - tabW - 12) / 4;
        var tab = new Rect2(tabX, r.Position.Y - 4, tabW, 14);
        DrawRect(tab, (Paper * shade) with { A = 1 });
        DrawRect(tab, PaperEdge.Darkened(0.2f), false, 1);
        DrawString(font, tab.Position + new Vector2(5, 11), $"{Surname(h.Name)}, {h.Name[0]}.", HorizontalAlignment.Left, tabW - 10, 11, (depth == 1 ? Type : TypeQuiet) with { A = 1 });
        DrawLine(new Vector2(r.Position.X, r.End.Y - 6), new Vector2(r.End.X, r.End.Y - 6), new Color(0, 0, 0, 0.25f), 1);
    }

    private void DrawCard(Font font, Rect2 r, Hood h)
    {
        var w = World!;
        var gang = w.GangById(GangId);
        var x = r.Position.X;
        var y = r.Position.Y;
        float cw = r.Size.X;

        DrawRect(new Rect2(r.Position + new Vector2(2, 3), r.Size), new Color(0, 0, 0, 0.35f));
        DrawRect(r, Paper);
        // Ruled like an index card: one red line under the header, blue lines below.
        DrawLine(new Vector2(x, y + 26), new Vector2(x + cw, y + 26), RedRule, 1.5f);
        for (float ly = y + 186; ly < r.End.Y - 6; ly += 17) DrawLine(new Vector2(x + 4, ly), new Vector2(x + cw - 4, ly), Rule with { A = 0.5f }, 1);
        // The two slots that hold the card on the rail.
        foreach (float sx in new[] { x + cw * 0.3f, x + cw * 0.7f })
            DrawRect(new Rect2(sx - 7, r.End.Y - 7, 14, 7), MetalDark);

        DrawString(font, new Vector2(x + 8, y + 19), h.Name, HorizontalAlignment.Left, cw - 16, 15, Type);

        // The mugshot, with a height chart behind it.
        var shot = new Rect2(x + 10, y + 34, 86, 104);
        Portrait(shot, h);
        DrawRect(shot, MetalDark, false, 1.5f);

        // Particulars beside the photo, typed.
        float tx = shot.End.X + 8, tw = r.End.X - tx - 6;
        float ty = y + 46;
        void Line(string text, Color c) { DrawString(font, new Vector2(tx, ty), text, HorizontalAlignment.Left, tw, 12, c); ty += 15; }
        Line($"Age {h.Age(w.Week)}", h.Age(w.Week) >= 65 ? Palette.Bad.Darkened(0.2f) : Type);
        Line(Content.Label(h.Origin), Type);
        Line($"${h.Wage} a week", Type);
        var crew = w.CrewOfHood(h.Id);
        string role = h.Id == gang.BossHoodId ? "The boss"
            : h.Id == gang.HeirHoodId ? w.RunsFromInside(gang) ? "Street boss" : "Heir"
            : crew == null ? "No crew"
            : crew.LieutenantHoodId == h.Id ? "Lieutenant"
            : $"{Surname(w.HoodById(crew.LieutenantHoodId).Name)} crew";
        Line(role, Type);
        if (h.Family) Line(w.MarriageOf(h) is { } wed ? $"Wed to {wed.Bride.Split(' ')[0]}" : "Family", Palette.Player.Darkened(0.35f));
        Line($"Since {1920 + h.JoinedWeek / Content.WeeksPerYear}", TypeQuiet);

        if (h.Id == gang.BossHoodId) Stamp(font, new Vector2(shot.End.X + 6, shot.End.Y - 20), "BOSS", RedRule.Darkened(0.1f));
        else if (h.State == HoodState.Jailed) Stamp(font, new Vector2(shot.End.X + 6, shot.End.Y - 20), "JAILED", Palette.Police.Darkened(0.25f));
        else if (World.IsWanted(h)) Stamp(font, new Vector2(shot.End.X + 6, shot.End.Y - 20), "WANTED", Palette.Police.Darkened(0.25f));
        else if (h.State == HoodState.Lam) Stamp(font, new Vector2(shot.End.X + 6, shot.End.Y - 20), "AWAY", TypeQuiet);

        // Skills: ten pips each.
        float sy = y + 150;
        foreach (var (label, value) in new[] { ("Intim.", h.Intimidation), ("Muscle", h.Muscle), ("Brains", h.Brains), ("Stealth", h.Stealth) })
        {
            DrawString(font, new Vector2(x + 10, sy + 10), label, HorizontalAlignment.Left, 56, 12, TypeQuiet);
            float px = x + 64, pw = (cw - 64 - 28) / 10f;
            for (int i = 0; i < 10; i++)
            {
                var pip = new Rect2(px + i * pw, sy + 1, pw - 2, 10);
                if (i < value) DrawRect(pip, value >= 8 ? RedRule : Type);
                else DrawRect(pip, Type with { A = 0.3f }, false, 1);
            }
            DrawString(font, new Vector2(x + cw - 24, sy + 10), $"{value}", HorizontalAlignment.Right, 16, 12, Type);
            sy += 17;
        }

        // Loyalty and ambition as gauges out of a hundred.
        sy += 4;
        Gauge(font, new Vector2(x + 10, sy), cw - 20, "Loyalty", h.Loyalty, h.Loyalty < 30 ? Palette.Bad.Darkened(0.15f) : Palette.Good.Darkened(0.35f));
        sy += 19;
        Gauge(font, new Vector2(x + 10, sy), cw - 20, "Ambition", h.Ambition, h.Ambition > 70 ? Palette.Player.Darkened(0.3f) : TypeQuiet);
        sy += 22;

        // What he carries and drives, each with a little stamp of it.
        GunGlyph(new Vector2(x + 18, sy + 7), h.Weapon);
        DrawString(font, new Vector2(x + 34, sy + 11), h.Weapon == Weapon.None ? "Unarmed" : Capital(Content.Guns[h.Weapon].Label), HorizontalAlignment.Left, cw - 44, 12, h.Weapon == Weapon.None ? TypeQuiet : Type);
        sy += 16;
        CarGlyph(new Vector2(x + 18, sy + 7), h.Car);
        DrawString(font, new Vector2(x + 34, sy + 11), h.Car == Car.None ? "On foot" : Capital(Content.Cars[h.Car].Label), HorizontalAlignment.Left, cw - 44, 12, h.Car == Car.None ? TypeQuiet : Type);
        sy += 20;

        // What he's up to, and a note when he is trouble.
        string now = h is { State: HoodState.Jailed, Case: { } c } ? $"Held for trial in {h.JailWeeks} week{(h.JailWeeks == 1 ? "" : "s")}, charged {c.Charge}."
            : h.State == HoodState.Jailed ? $"In jail, {h.JailWeeks} more week{(h.JailWeeks == 1 ? "" : "s")}."
            : h is { State: HoodState.Lam, Case: { } wanted } ? $"On the lam out of town, wanted, charged {wanted.Charge}."
            : h.State == HoodState.Lam ? "Lying low out of town."
            : h is { Bail: > 0, Case: { } bailed } ? $"Out on ${h.Bail:N0} bail. Trial in {h.JailWeeks} week{(h.JailWeeks == 1 ? "" : "s")}, charged {bailed.Charge}."
            : World.IsWanted(h) ? "Home, with a warrant out for him. The police may pick him up any week."
            : JobOf(h.Id) is { } job ? job
            : h.Id == gang.BossHoodId ? "Running the outfit."
            : "Free for work.";
        sy = Paragraph(font, new Vector2(x + 10, sy), cw - 20, now, Type, r.End.Y - 30);
        if (h.Loyalty < 30 && h.Ambition > 60 && h.Id != gang.BossHoodId)
            Paragraph(font, new Vector2(x + 10, sy + 2), cw - 20, "Restless. Might break away.", Palette.Bad.Darkened(0.2f), r.End.Y - 30);
    }

    /// <summary>The back of the card: the police record, typed up, newest first.</summary>
    private void DrawBack(Font font, Rect2 r, Hood h)
    {
        var w = World!;
        var x = r.Position.X;
        var y = r.Position.Y;
        float cw = r.Size.X;
        DrawRect(new Rect2(r.Position + new Vector2(2, 3), r.Size), new Color(0, 0, 0, 0.35f));
        DrawRect(r, Paper.Darkened(0.04f));
        DrawLine(new Vector2(x, y + 26), new Vector2(x + cw, y + 26), RedRule, 1.5f);
        for (float ly = y + 44; ly < r.End.Y - 6; ly += 14) DrawLine(new Vector2(x + 4, ly), new Vector2(x + cw - 4, ly), Rule with { A = 0.35f }, 1);
        foreach (float sx in new[] { x + cw * 0.3f, x + cw * 0.7f })
            DrawRect(new Rect2(sx - 7, r.End.Y - 7, 14, 7), MetalDark);

        DrawString(font, new Vector2(x + 8, y + 18), "RAP SHEET", HorizontalAlignment.Left, -1, 13, RedRule.Darkened(0.15f));
        DrawString(font, new Vector2(x + 8, y + 18), $"{Surname(h.Name)}, {h.Name.Split(' ')[0]}", HorizontalAlignment.Right, cw - 16, 12, Type);

        float bottom = r.End.Y - 30, tw = cw - 20;
        float sy = Paragraph(font, new Vector2(x + 10, y + 32), tw, Reports.Known(h), Type, bottom);
        sy = Paragraph(font, new Vector2(x + 10, sy + 2), tw, Reports.Priors(h), h.Convictions >= Content.HabitualOffender ? Palette.Bad.Darkened(0.2f) : TypeQuiet, bottom);
        int enemies = w.HeldAgainst(Party.Hood, h.Id).Count(g => g.Known);
        if (enemies > 0)
            sy = Paragraph(font, new Vector2(x + 10, sy + 2), tw, $"Bad blood: {(enemies == 1 ? "one person on the street has" : $"{enemies} people on the street have")} it in for him.", RedRule.Darkened(0.2f), bottom);
        if (h.Convictions >= Content.HabitualOffender)
            Stamp(font, new Vector2(x + cw - 100, y + 30), "3 STRIKES", Palette.Bad.Darkened(0.25f));
        int shown = 0;
        foreach (var e in Enumerable.Reverse(h.Record))
        {
            if (sy > bottom - 14) break;
            var colour = e.Kind switch { RapKind.Convicted => RedRule.Darkened(0.25f), RapKind.Acquitted => Palette.Good.Darkened(0.4f), RapKind.Turned => Palette.Bad, _ => Type };
            DrawString(font, new Vector2(x + 10, sy + 13), $"{Content.StartYear + e.Week / Content.WeeksPerYear}", HorizontalAlignment.Left, 34, 11, TypeQuiet);
            sy = Paragraph(font, new Vector2(x + 44, sy + 2), tw - 34, e.Text, colour, bottom);
            shown++;
        }
        if (shown < h.Record.Count && sy <= bottom + 4)
            DrawString(font, new Vector2(x + 10, sy + 11), $"…and {h.Record.Count - shown} older", HorizontalAlignment.Left, tw, 11, TypeQuiet);
    }

    /// <summary>The little link in the corner that turns the cards over.</summary>
    private void TurnLink(Font font, Rect2 r)
    {
        string text = _back ? "‹ the card" : "rap sheet ›";
        DrawString(font, new Vector2(r.End.X - 92, r.End.Y - 13), text, HorizontalAlignment.Right, 84, 12, RedRule.Darkened(0.15f));
    }

    private static string Capital(string s) => char.ToUpper(s[0]) + s[1..];

    /// <summary>A pistol, a shotgun or a Thompson, drawn small; an empty outline when he has none.</summary>
    private void GunGlyph(Vector2 c, Weapon gun)
    {
        var ink = gun == Weapon.None ? Type with { A = 0.3f } : Type;
        float barrel = gun switch { Weapon.Shotgun => 12, Weapon.TommyGun => 11, _ => 7 };
        DrawRect(new Rect2(c.X - 6, c.Y - 3, barrel + 2, 3), ink);
        DrawColoredPolygon(new[] { c + new Vector2(-6, 0), c + new Vector2(-1, 0), c + new Vector2(-3, 6), c + new Vector2(-7, 6) }, ink);
        if (gun == Weapon.TommyGun)
        {
            DrawCircle(c + new Vector2(2, 3), 2.6f, ink);
            DrawRect(new Rect2(c.X - 10, c.Y - 2, 4, 3), ink);
        }
        if (gun == Weapon.Shotgun) DrawRect(new Rect2(c.X - 11, c.Y - 2, 5, 4), RedRule.Darkened(0.4f));
    }

    private void CarGlyph(Vector2 c, Car car)
    {
        var ink = car == Car.None ? Type with { A = 0.3f } : car == Car.Cadillac ? new Color("3a3632") : Type;
        float len = car == Car.ModelT ? 7 : 9;
        DrawRect(new Rect2(c.X - len, c.Y - 1, len * 2, 4), ink);
        DrawColoredPolygon(new[] { c + new Vector2(-len * 0.55f, -1), c + new Vector2(-len * 0.35f, -5), c + new Vector2(len * 0.35f, -5), c + new Vector2(len * 0.55f, -1) }, ink);
        DrawCircle(c + new Vector2(-len * 0.55f, 4), 2.2f, ink);
        DrawCircle(c + new Vector2(len * 0.55f, 4), 2.2f, ink);
    }

    private void Gauge(Font font, Vector2 at, float width, string label, int value, Color fill)
    {
        DrawString(font, at + new Vector2(0, 11), label, HorizontalAlignment.Left, 56, 12, TypeQuiet);
        var bar = new Rect2(at.X + 54, at.Y + 2, width - 54 - 30, 10);
        DrawRect(bar, Type with { A = 0.12f });
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(value, 0, 100) / 100f, bar.Size.Y)), fill);
        DrawRect(bar, Type with { A = 0.45f }, false, 1);
        DrawString(font, new Vector2(at.X + width - 26, at.Y + 11), $"{value}", HorizontalAlignment.Right, 26, 12, Type);
    }

    /// <summary>Word-wrapped text; returns where the next line would go.</summary>
    private float Paragraph(Font font, Vector2 at, float width, string text, Color c, float bottom)
    {
        float y = at.Y + 11;
        string line = "";
        foreach (var word in text.Split(' '))
        {
            string tryLine = line.Length == 0 ? word : line + " " + word;
            if (font.GetStringSize(tryLine, HorizontalAlignment.Left, -1, 12).X > width && line.Length > 0)
            {
                if (y > bottom) return y;
                DrawString(font, new Vector2(at.X, y), line, HorizontalAlignment.Left, width, 12, c);
                y += 14;
                line = word;
            }
            else line = tryLine;
        }
        if (line.Length > 0 && y <= bottom) DrawString(font, new Vector2(at.X, y), line, HorizontalAlignment.Left, width, 12, c);
        return y + 3;
    }

    private void Stamp(Font font, Vector2 at, string text, Color c)
    {
        float tw = font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X + 10;
        DrawSetTransformMatrix(new Transform2D(-0.12f, at));
        DrawRect(new Rect2(0, 0, tw, 18), c with { A = 0.9f }, false, 2);
        DrawString(font, new Vector2(5, 14), text, HorizontalAlignment.Left, -1, 13, c with { A = 0.9f });
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    private void DrawBase(Font font, Rect2 r)
    {
        // A pressed-steel base with a knob at each end, like the real thing.
        DrawRect(new Rect2(r.Position + new Vector2(0, 6), new Vector2(r.Size.X, r.Size.Y - 6)), MetalDark);
        DrawRect(new Rect2(r.Position + new Vector2(2, 6), new Vector2(r.Size.X - 4, 4)), Metal);
        float knobY = r.Position.Y + 22;
        DrawCircle(new Vector2(r.Position.X + 12, knobY), 10, Metal);
        DrawCircle(new Vector2(r.End.X - 12, knobY), 10, Metal);
        DrawCircle(new Vector2(r.Position.X + 12, knobY), 4, MetalDark);
        DrawCircle(new Vector2(r.End.X - 12, knobY), 4, MetalDark);
        float mid = r.Position.X + r.Size.X / 2;
        _up = new Rect2(mid - 52, r.Position.Y + 12, 48, 22);
        _down = new Rect2(mid + 4, r.Position.Y + 12, 48, 22);
        foreach (var (rect, up) in new[] { (_up, true), (_down, false) })
        {
            DrawRect(rect, Metal);
            var c = rect.GetCenter();
            var tri = up
                ? new[] { c + new Vector2(0, -5), c + new Vector2(6, 4), c + new Vector2(-6, 4) }
                : new[] { c + new Vector2(0, 5), c + new Vector2(6, -4), c + new Vector2(-6, -4) };
            DrawColoredPolygon(tri, Palette.Ink);
        }
    }

    // ---- The mugshot ------------------------------------------------------------

    private static readonly Color[] Skins = { new("e7c1a0"), new("d9a982"), new("c48d66"), new("a8714e"), new("f0cfb3"), new("8a5a3c") };
    private static readonly Color[] Hairs = { new("1d1a18"), new("3b2a1e"), new("6b4a2b"), new("8c6a3f"), new("2b2b2b"), new("a3502c") };
    private static readonly Color[] Felts = { new("3a3430"), new("4a4038"), new("2c3036"), new("5a4a36"), new("6c665c"), new("252221") };
    private static readonly Color[] Suits = { new("3d3a40"), new("4a3f35"), new("2f3a45"), new("55504a"), new("3b4535"), new("5a3e3a") };
    private static readonly Color[] Ties = { new("8c2f2a"), new("2f5a8c"), new("c9a23f"), new("3f7a4f"), new("6a3f7a"), new("222222") };

    /// <summary>
    /// A face made from his name and birthday, so the same man always looks the same, from one launch to the next
    /// and from one city of a campaign to the next. (A string's own hash code changes every time the game starts.)
    /// </summary>
    private void Portrait(Rect2 r, Hood h)
    {
        uint seed = 2166136261u;
        foreach (char ch in h.Name) seed = (seed ^ ch) * 16777619u;
        seed ^= (uint)h.BornWeek * 2654435761u;
        int Pick(int n, int salt) => (int)(((seed >> (salt * 3)) ^ (seed * (uint)(salt + 7))) % (uint)n);
        int age = h.Age(World!.Week);

        // Height chart behind him.
        DrawRect(r, new Color("d8d2c2"));
        for (int i = 1; i < 7; i++)
        {
            float ly = r.Position.Y + i * r.Size.Y / 7;
            DrawLine(new Vector2(r.Position.X, ly), new Vector2(r.Position.X + (i % 2 == 0 ? 14 : 8), ly), new Color("8a8478"), 1);
            DrawLine(new Vector2(r.Position.X + 14, ly), new Vector2(r.End.X, ly), new Color(0.5f, 0.48f, 0.44f, 0.18f), 1);
        }

        var skin = Skins[Pick(Skins.Length, 1)];
        var hair = age >= 58 ? new Color("b8b4ad") : age >= 48 && Pick(2, 9) == 0 ? Hairs[Pick(Hairs.Length, 2)].Lerp(new Color("b8b4ad"), 0.5f) : Hairs[Pick(Hairs.Length, 2)];
        var suit = Suits[Pick(Suits.Length, 3)];
        var tie = Ties[Pick(Ties.Length, 4)];
        int hat = Pick(4, 5);
        var felt = Felts[Pick(Felts.Length, 6)];
        // Big men are broad; brainy men wear glasses; hard men carry a scar.
        float broad = 0.85f + h.Muscle * 0.025f;
        float cx = r.Position.X + r.Size.X / 2;
        float headY = r.Position.Y + r.Size.Y * 0.44f;
        float hw = 17 + h.Muscle * 0.35f, hh = 21;

        // Shoulders and suit.
        float sw = 36 * broad;
        var shoulders = new[]
        {
            new Vector2(cx - sw, r.End.Y), new Vector2(cx - sw + 4, r.End.Y - 22), new Vector2(cx - 12, headY + hh + 4),
            new Vector2(cx + 12, headY + hh + 4), new Vector2(cx + sw - 4, r.End.Y - 22), new Vector2(cx + sw, r.End.Y),
        };
        DrawColoredPolygon(shoulders, suit);
        if (Pick(3, 7) == 0)
            for (float px = cx - sw + 8; px < cx + sw - 6; px += 6) DrawLine(new Vector2(px, r.End.Y - 20), new Vector2(px, r.End.Y), suit.Lightened(0.18f), 1);
        // Shirt, tie, lapels.
        DrawColoredPolygon(new[] { new Vector2(cx - 11, headY + hh + 2), new Vector2(cx + 11, headY + hh + 2), new Vector2(cx, r.End.Y - 2) }, new Color("ece6da"));
        DrawColoredPolygon(new[] { new Vector2(cx - 3, headY + hh + 5), new Vector2(cx + 3, headY + hh + 5), new Vector2(cx + 4, r.End.Y - 4), new Vector2(cx, r.End.Y), new Vector2(cx - 4, r.End.Y - 4) }, tie);
        DrawColoredPolygon(new[] { new Vector2(cx - 12, headY + hh + 3), new Vector2(cx - 2, r.End.Y), new Vector2(cx - 16, r.End.Y) }, suit.Darkened(0.2f));
        DrawColoredPolygon(new[] { new Vector2(cx + 12, headY + hh + 3), new Vector2(cx + 16, r.End.Y), new Vector2(cx + 2, r.End.Y) }, suit.Darkened(0.2f));
        if (h.Family) DrawCircle(new Vector2(cx - sw * 0.55f, r.End.Y - 14), 3.2f, new Color("c8463c"));

        // Neck, ears, head.
        DrawRect(new Rect2(cx - 7, headY + hh - 6, 14, 12), skin.Darkened(0.12f));
        DrawCircle(new Vector2(cx - hw + 1, headY + 2), 4, skin.Darkened(0.08f));
        DrawCircle(new Vector2(cx + hw - 1, headY + 2), 4, skin.Darkened(0.08f));
        DrawEllipse(new Vector2(cx, headY), hw, hh, skin);
        // Jaw shadow.
        DrawEllipse(new Vector2(cx, headY + hh * 0.55f), hw * 0.75f, hh * 0.4f, skin.Darkened(0.06f));

        // Hair under the hat, or the whole head of it.
        if (hat == 3)
        {
            DrawEllipse(new Vector2(cx, headY - hh * 0.62f), hw * 1.02f, hh * 0.5f, hair);
            DrawLine(new Vector2(cx - hw * 0.3f, headY - hh * 0.95f), new Vector2(cx - hw * 0.2f, headY - hh * 0.35f), skin.Lerp(hair, 0.4f), 1.2f);
        }
        else
        {
            DrawRect(new Rect2(cx - hw, headY - hh * 0.35f, 4, hh * 0.5f), hair);
            DrawRect(new Rect2(cx + hw - 4, headY - hh * 0.35f, 4, hh * 0.5f), hair);
        }

        // Brows: heavy on intimidating men.
        float browY = headY - 5;
        float brow = 1.2f + h.Intimidation * 0.18f;
        float slant = h.Intimidation >= 7 ? 2.2f : 0;
        DrawLine(new Vector2(cx - 11, browY - slant * 0.2f), new Vector2(cx - 3, browY + slant * 0.6f), hair.Darkened(0.2f), brow);
        DrawLine(new Vector2(cx + 3, browY + slant * 0.6f), new Vector2(cx + 11, browY - slant * 0.2f), hair.Darkened(0.2f), brow);
        // Eyes, narrowed on sly men.
        float eyeH = h.Stealth >= 7 ? 1.2f : 2.2f;
        DrawEllipse(new Vector2(cx - 7, headY), 2.6f, eyeH, new Color("1e1b19"));
        DrawEllipse(new Vector2(cx + 7, headY), 2.6f, eyeH, new Color("1e1b19"));
        if (h.Brains >= 8)
        {
            DrawArc(new Vector2(cx - 7, headY), 5, 0, Mathf.Tau, 16, new Color("2a2622"), 1.2f);
            DrawArc(new Vector2(cx + 7, headY), 5, 0, Mathf.Tau, 16, new Color("2a2622"), 1.2f);
            DrawLine(new Vector2(cx - 2, headY), new Vector2(cx + 2, headY), new Color("2a2622"), 1.2f);
        }
        // Nose, broken on brawlers.
        var noseTip = new Vector2(cx + (h.Muscle >= 8 ? 2 : 0), headY + 8);
        DrawPolyline(new[] { new Vector2(cx, headY + 1), noseTip, new Vector2(cx - 3, headY + 9) }, skin.Darkened(0.3f), 1.4f);
        // Mouth: a flat line, a smirk on ambitious men; a moustache on some.
        float my = headY + 14;
        if (h.Ambition > 65) DrawPolyline(new[] { new Vector2(cx - 6, my), new Vector2(cx + 2, my), new Vector2(cx + 7, my - 2) }, new Color("5a2e26"), 1.6f);
        else DrawLine(new Vector2(cx - 6, my), new Vector2(cx + 6, my), new Color("5a2e26"), 1.6f);
        if (Pick(3, 8) == 0) DrawColoredPolygon(new[] { new Vector2(cx - 9, my - 2), new Vector2(cx, my - 5), new Vector2(cx + 9, my - 2), new Vector2(cx, my - 3) }, hair.Darkened(0.1f));
        // Lines on an old face; stubble on a young tough.
        if (age >= 50)
        {
            DrawLine(new Vector2(cx - 9, headY - 10), new Vector2(cx + 9, headY - 10), skin.Darkened(0.18f), 1);
            DrawLine(new Vector2(cx - 10, my - 4), new Vector2(cx - 8, my + 2), skin.Darkened(0.2f), 1);
            DrawLine(new Vector2(cx + 10, my - 4), new Vector2(cx + 8, my + 2), skin.Darkened(0.2f), 1);
        }
        if (h.Muscle + h.Intimidation >= 15)
            DrawLine(new Vector2(cx + 9, headY - 6), new Vector2(cx + 13, headY + 7), new Color("9c4a42"), 1.4f);

        // Hats: fedora, flat cap, bowler, or none.
        float top = headY - hh;
        switch (hat)
        {
            case 0: // fedora
                DrawEllipse(new Vector2(cx, top + 9), hw + 10, 4.5f, felt);
                DrawColoredPolygon(new[] { new Vector2(cx - hw + 2, top + 9), new Vector2(cx - hw + 5, top - 9), new Vector2(cx - 3, top - 7), new Vector2(cx, top - 4), new Vector2(cx + 3, top - 7), new Vector2(cx + hw - 5, top - 9), new Vector2(cx + hw - 2, top + 9) }, felt);
                DrawRect(new Rect2(cx - hw + 2, top + 2, (hw - 2) * 2, 5), new Color("1a1716"));
                break;
            case 1: // flat cap
                DrawColoredPolygon(new[] { new Vector2(cx - hw - 1, top + 9), new Vector2(cx - hw + 3, top - 3), new Vector2(cx + hw - 2, top - 4), new Vector2(cx + hw + 9, top + 7), new Vector2(cx + hw + 1, top + 10) }, felt.Lightened(0.15f));
                DrawLine(new Vector2(cx - hw + 3, top + 4), new Vector2(cx + hw + 4, top + 5), felt.Darkened(0.2f), 1);
                break;
            case 2: // bowler
                DrawEllipse(new Vector2(cx, top + 9), hw + 6, 3.5f, felt.Darkened(0.2f));
                DrawEllipse(new Vector2(cx, top + 1), hw - 1, 11, felt.Darkened(0.2f));
                break;
        }

        // Prison bars over the photo when he's inside.
        if (h.State == HoodState.Jailed)
            for (float bx = r.Position.X + 8; bx < r.End.X; bx += 13)
                DrawRect(new Rect2(bx, r.Position.Y, 4, r.Size.Y), new Color(0.15f, 0.15f, 0.17f, 0.85f));

        // A sepia wash over the lot, to look like an old print.
        DrawRect(r, new Color(0.55f, 0.42f, 0.25f, 0.12f));
    }

    private void DrawEllipse(Vector2 c, float rx, float ry, Color colour)
    {
        var pts = new Vector2[20];
        for (int i = 0; i < pts.Length; i++)
        {
            float a = Mathf.Tau * i / pts.Length;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, colour);
    }

    private static string Surname(string name) => DryTown.Core.World.Surname(name);
}
