using System;
using System.Collections.Generic;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// Now and then two citizens stop on the pavement to talk, and speech bubbles pop up over them. What's
/// in the bubble is what they're talking about, and it comes from what's going on round them: the
/// old country's language in their own neighbourhood, a killing or an arrest in the ward this week,
/// the speakeasy on the block, a shopkeeper who paid up, or the morning paper.
/// </summary>
public partial class MapView
{
    private enum Topic { Language, Murder, Arrest, Booze, Money, Paper, Broke, Grumble, Praise, Crime }

    /// <summary>Seconds one conversation cycle takes, and how much of it the pair spend talking.</summary>
    private const float ChatCycle = 15, ChatTalk = 9, ChatTurn = 1.7f;

    /// <summary>What happened in each ward this week as far as the street has heard: killings, arrests and shakedowns.</summary>
    private (int Murders, int Arrests, int Payoffs, int Crimes)[] WardGossip()
    {
        var w = World!;
        var gossip = new (int, int, int, int)[Math.Max(1, w.Map.WardCount)];
        foreach (var a in w.Script)
        {
            if (Live && a.Tick > Clock) continue;
            int lot = a.ToLot >= 0 ? a.ToLot : a.FromLot;
            if (lot < 0) continue;
            int ward = w.Map.LotAt(lot).WardId;
            if (ward < 0 || ward >= gossip.Length) continue;
            ref var g = ref gossip[ward];
            g.Item4++;
            if (a.Result == ActionResult.Killed || a is { Kind: ActionKind.Hit, Result: ActionResult.Success }) g.Item1++;
            else if (a.Result == ActionResult.Arrested || a.Kind == ActionKind.Raid) g.Item2++;
            else if (a is { Kind: ActionKind.Extort, Result: ActionResult.Success }) g.Item3++;
        }
        return gossip;
    }

    /// <summary>Pick what a pair are talking about, weighted by what's going on round them.</summary>
    private Topic PickTopic(float roll, int ward, (int Murders, int Arrests, int Payoffs, int Crimes) news, bool speakeasy, Gang? boss, float resentment)
    {
        var weights = new (Topic Topic, float Weight)[]
        {
            (Topic.Language, World!.Neighbourhood(ward) == Ethnicity.Unknown ? 0 : 3),
            (Topic.Murder, news.Murders * 4),
            (Topic.Arrest, news.Arrests * 2.5f),
            // After dark, outside a speakeasy, it's mostly the drink.
            (Topic.Booze, speakeasy ? (HourOfDay >= 20 || HourOfDay < 2 ? 6 : 2) : 0.3f),
            (Topic.Money, Mathf.Min(news.Payoffs, 4) * 0.5f),
            (Topic.Paper, 0.8f),
            // Money's tight, and tighter where the shops are paying protection.
            (Topic.Broke, 0.7f + Mathf.Min(news.Payoffs, 4) * 0.3f),
            // The boss whose turf this is: cursed where the shops resent him or he's feared, praised where he's respected.
            (Topic.Grumble, boss == null ? 0 : resentment * 3 + (float)boss.Fear / 100 * 1.5f),
            (Topic.Praise, boss == null ? 0 : (float)boss.Respect / 100 * 3 * (1 - resentment)),
            // A week with a lot going on in the ward has everyone saying the streets aren't safe.
            (Topic.Crime, Mathf.Min(news.Crimes * 0.4f, 3)),
        };
        float total = weights.Sum(x => x.Weight), at = roll * total;
        foreach (var (topic, weight) in weights)
        {
            if (at < weight) return topic;
            at -= weight;
        }
        return Topic.Paper;
    }

    /// <summary>Pairs of citizens stopped to talk on the blocks in view.</summary>
    private void DrawChatter(Rect2 view)
    {
        if (Tile < 12) return;
        var w = World!;
        var map = w.Map;
        float outside = OutsideAt(HourOfDay);
        var gossip = WardGossip();
        var drinking = SpeakeasyDoors();
        var turf = BlockBosses();
        float r = DotRadius;
        int bx0 = Math.Max(0, (int)(view.Position.X / CityMap.StrideX)), bx1 = Math.Min(map.BlocksX - 1, (int)(view.End.X / CityMap.StrideX));
        int by0 = Math.Max(0, (int)(view.Position.Y / CityMap.StrideY)), by1 = Math.Min(map.BlocksY - 1, (int)(view.End.Y / CityMap.StrideY));
        for (int by = by0; by <= by1; by++)
        for (int bx = bx0; bx <= bx1; bx++)
        {
            if (by == map.RiverRow || _crowdBlocks.Length == 0 || _crowdBlocks[bx, by] == 0) continue;
            int ward = map.WardOfBlock(bx, by);
            for (int slot = 0; slot < 3; slot++)
            {
                int key = (by * 131 + bx) * 7 + slot;
                float clock = _anim + Hash(key, 940) * ChatCycle;
                int cycle = (int)(clock / ChatCycle);
                float local = clock - cycle * ChatCycle;
                if (local > ChatTalk) continue;
                // Busier at the rush hours: a pair stops about as often as people are out.
                int seed = key * 9973 + cycle;
                if (Hash(seed, 941) > outside * 4) continue;

                float along = Hash(seed, 942) * CrowdPerimeter;
                var (spot, dir) = AlongPavement(bx, by, along);
                if (!view.HasPoint(spot)) continue;
                var gap = dir * (r * 1.3f / _t);
                var a = spot - gap;
                var b = spot + gap;
                DrawDot(a * _t, r, Palette.Citizen);
                DrawDot(b * _t, r, Palette.Citizen);

                var (boss, resentment) = turf[bx, by];
                var topic = PickTopic(Hash(seed, 943), ward, ward < gossip.Length ? gossip[ward] : default, drinking[bx, by] > 0, boss, resentment);
                // They take turns, with a breath between.
                int turn = (int)(local / ChatTurn);
                float into = local - turn * ChatTurn;
                if (into > ChatTurn * 0.85f) continue;
                var speaker = turn % 2 == 0 ? a : b;
                float pop = Mathf.Min(1, into / 0.18f);
                pop = 1 + (1 - pop) * -0.6f + Mathf.Sin(Mathf.Min(1, into / 0.3f) * Mathf.Pi) * 0.12f;
                DrawBubble(speaker, turn % 2 == 0 ? -1 : 1, topic, ward, pop, boss);
            }
        }
    }

    /// <summary>A point on a block's pavement a distance round from its north-west corner, and the way along it there.</summary>
    private static (Vector2 At, Vector2 Along) AlongPavement(int bx, int by, float d)
    {
        float x0 = bx * CityMap.StrideX + 1 - 0.1f, y0 = by * CityMap.StrideY + 1 - 0.1f;
        float w = CrowdW, h = CrowdH;
        if (d < w) return (new Vector2(x0 + d, y0), Vector2.Right);
        if (d < w + h) return (new Vector2(x0 + w, y0 + d - w), Vector2.Down);
        if (d < 2 * w + h) return (new Vector2(x0 + w - (d - w - h), y0 + h), Vector2.Left);
        return (new Vector2(x0, y0 + h - (d - 2 * w - h)), Vector2.Up);
    }

    /// <summary>A speech bubble over someone's head, its tail pointing down at him, with the topic drawn inside.</summary>
    private void DrawBubble(Vector2 speaker, int lean, Topic topic, int ward, float scale, Gang? boss)
    {
        var keep = C;
        C = _marks;
        float s = Mathf.Max(Px(20), _t * 0.28f) * scale;
        var foot = speaker * _t;
        var centre = foot + new Vector2(lean * s * 0.35f, -s * 1.05f);
        var size = new Vector2(s * 1.5f, s * 1.1f);
        // The boss's name is written in the bubble, which widens to hold it. Letters are drawn on the screen, so they stay sharp.
        if (topic is Topic.Grumble or Topic.Praise && boss != null)
        {
            var font = ThemeDB.FallbackFont;
            string name = World!.HoodById(boss.BossHoodId).Name.Split(' ')[^1];
            int px = Mathf.Max(8, (int)(s / Px(1) * 0.5f));
            float wide = Px(font.GetStringSize(name, HorizontalAlignment.Left, -1, px).X);
            size.X = Mathf.Max(size.X, wide + s * 0.5f);
            centre.X = foot.X + lean * Mathf.Max(s * 0.35f, size.X / 2 - s * 0.4f);
            _chatNames.Add((centre / U * Tile + Origin, name, px, topic == Topic.Praise ? new Color("2f8a4a") : new Color("c8382e")));
        }
        var paper = new Color("f3eddf");
        var ink = new Color("1a1716");
        var box = new Rect2(centre - size / 2, size);
        // An outline, the tail, then the paper over both so the tail joins without a seam.
        RoundBox(box.Grow(Mathf.Max(1, s * 0.07f)), s * 0.3f, ink);
        var tailTop = centre + new Vector2(-lean * s * 0.15f, size.Y / 2 - s * 0.05f);
        var tip = foot + new Vector2(0, -s * 0.28f);
        C.DrawColoredPolygon(new[] { tailTop + new Vector2(-s * 0.2f, 0), tailTop + new Vector2(s * 0.2f, 0), tip }, ink);
        C.DrawColoredPolygon(new[] { tailTop + new Vector2(-s * 0.12f, -s * 0.02f), tailTop + new Vector2(s * 0.12f, -s * 0.02f), tip + new Vector2(0, -s * 0.1f) }, paper);
        RoundBox(box, s * 0.25f, paper);
        if (topic is not (Topic.Grumble or Topic.Praise)) DrawTopic(topic, ward, centre, s * 0.48f, ink);
        C = keep;
    }

    private readonly List<(Vector2 At, string Name, int Size, Color Colour)> _chatNames = new();

    /// <summary>The bosses' names in this frame's bubbles, written over them on the screen.</summary>
    private void DrawChatNames(Font font)
    {
        foreach (var (at, name, size, colour) in _chatNames)
            C.DrawString(font, at + new Vector2(-200, size * 0.36f), name, HorizontalAlignment.Center, 400, size, colour);
        _chatNames.Clear();
    }

    /// <summary>On each block, the gang most of its shops pay, and how much the shops there resent paying (0 to 1).</summary>
    private (Gang? Boss, float Resentment)[,] BlockBosses()
    {
        var w = World!;
        var map = w.Map;
        var counts = new Dictionary<(int, int, int), (int Shops, int Resentment)>();
        foreach (var b in w.Businesses)
        {
            int gang = ShownState(b).Gang;
            if (!b.IsOpen || gang < 0) continue;
            var lot = w.LotOf(b);
            var key = ((lot.X - 1) / CityMap.StrideX, (lot.Y - 1) / CityMap.StrideY, gang);
            counts.TryGetValue(key, out var c);
            counts[key] = (c.Shops + 1, c.Resentment + b.Resentment);
        }
        var bosses = new (Gang?, float)[map.BlocksX, map.BlocksY];
        foreach (var group in counts.GroupBy(kv => (kv.Key.Item1, kv.Key.Item2)))
        {
            var top = group.OrderByDescending(kv => kv.Value.Shops).ThenBy(kv => kv.Key.Item3).First();
            var gang = w.Gangs.FirstOrDefault(g => g.Id == top.Key.Item3);
            if (gang is not { Alive: true } || gang.BossHoodId < 0) continue;
            bosses[group.Key.Item1, group.Key.Item2] = (gang, Mathf.Clamp(top.Value.Resentment / (float)top.Value.Shops / 100f, 0, 1));
        }
        return bosses;
    }

    private void RoundBox(Rect2 box, float radius, Color colour)
    {
        radius = Mathf.Min(radius, Mathf.Min(box.Size.X, box.Size.Y) / 2);
        C.DrawRect(box.Grow(-radius).GrowSide(Side.Left, radius).GrowSide(Side.Right, radius), colour);
        C.DrawRect(box.Grow(-radius).GrowSide(Side.Top, radius).GrowSide(Side.Bottom, radius), colour);
        foreach (var corner in new[] { box.Position, new Vector2(box.End.X, box.Position.Y), box.End, new Vector2(box.Position.X, box.End.Y) })
            C.DrawCircle(corner + new Vector2(corner.X > box.GetCenter().X ? -radius : radius, corner.Y > box.GetCenter().Y ? -radius : radius), radius, colour);
    }

    /// <summary>The picture for what they're talking about, drawn in a box about 2m wide round the centre.</summary>
    private void DrawTopic(Topic topic, int ward, Vector2 c, float m, Color ink)
    {
        float line = Mathf.Max(1, m * 0.16f);
        var paperRim = new Color("f3eddf");
        switch (topic)
        {
            case Topic.Language:
                DrawFlag(World!.Neighbourhood(ward), c, m, ink);
                break;
            case Topic.Murder:
            {
                // A chalk outline on dark pavement: a body sprawled where it fell, arms and legs flung out.
                C.DrawRect(new Rect2(c - new Vector2(m * 1.15f, m * 0.8f), new Vector2(m * 2.3f, m * 1.6f)), new Color("3a3632"));
                var chalk = new Color("f4f1ea");
                float w = Mathf.Max(1, m * 0.17f);
                var neck = c + new Vector2(-m * 0.38f, -m * 0.05f);
                var hip = c + new Vector2(m * 0.3f, m * 0.05f);
                C.DrawArc(c + new Vector2(-m * 0.7f, -m * 0.12f), m * 0.24f, 0, Mathf.Tau, 12, chalk, w);
                C.DrawLine(neck, hip, chalk, w);
                C.DrawLine(neck + new Vector2(m * 0.12f, 0), c + new Vector2(-m * 0.15f, -m * 0.62f), chalk, w);
                C.DrawLine(neck + new Vector2(m * 0.12f, 0), c + new Vector2(m * 0.05f, m * 0.55f), chalk, w);
                C.DrawLine(hip, c + new Vector2(m * 0.95f, -m * 0.45f), chalk, w);
                C.DrawLine(hip, c + new Vector2(m * 0.95f, m * 0.55f), chalk, w);
                break;
            }
            case Topic.Arrest:
            {
                // Handcuffs: two open rings joined by a short chain.
                var steel = new Color("3d444c");
                float ring = m * 0.36f, w = Mathf.Max(1, m * 0.15f);
                var left = c + new Vector2(-m * 0.6f, m * 0.15f);
                var right = c + new Vector2(m * 0.6f, m * 0.15f);
                C.DrawArc(left, ring, 0, Mathf.Tau, 16, steel, w);
                C.DrawArc(right, ring, 0, Mathf.Tau, 16, steel, w);
                C.DrawLine(left + new Vector2(ring * 0.5f, -ring * 0.85f), right + new Vector2(-ring * 0.5f, -ring * 0.85f), steel, w * 0.8f);
                C.DrawCircle(c + new Vector2(0, -m * 0.15f), w * 0.9f, steel);
                break;
            }
            case Topic.Booze:
            {
                // A bottle with a label and cork.
                var glass = new Color("3f6b3a");
                C.DrawRect(new Rect2(c + new Vector2(-m * 0.32f, -m * 0.2f), new Vector2(m * 0.64f, m * 0.95f)), glass);
                C.DrawCircle(c + new Vector2(0, -m * 0.2f), m * 0.32f, glass);
                C.DrawRect(new Rect2(c + new Vector2(-m * 0.12f, -m * 0.85f), new Vector2(m * 0.24f, m * 0.5f)), glass);
                C.DrawRect(new Rect2(c + new Vector2(-m * 0.1f, -m * 0.98f), new Vector2(m * 0.2f, m * 0.16f)), new Color("a07a4a"));
                C.DrawRect(new Rect2(c + new Vector2(-m * 0.32f, m * 0.12f), new Vector2(m * 0.64f, m * 0.3f)), new Color("e8dcc0"));
                break;
            }
            case Topic.Money:
            {
                // A dollar bill: green, a pale border and the round seal in the middle.
                var bill = new Rect2(c - new Vector2(m * 1.05f, m * 0.6f), new Vector2(m * 2.1f, m * 1.2f));
                C.DrawRect(bill, new Color("4f8a4a"));
                C.DrawRect(bill.Grow(-m * 0.14f), new Color("a9cf9a"), false, Mathf.Max(1, m * 0.08f));
                C.DrawCircle(c, m * 0.34f, new Color("2c5a2e"));
                C.DrawCircle(c, m * 0.18f, new Color("a9cf9a"));
                break;
            }
            case Topic.Broke:
            {
                // The dollar bill again, struck through in red: they're short.
                DrawTopic(Topic.Money, ward, c, m, ink);
                var red = new Color("c8382e");
                float w = Mathf.Max(1.5f, m * 0.2f);
                C.DrawLine(c + new Vector2(-m * 1.0f, m * 0.75f), c + new Vector2(m * 1.0f, -m * 0.75f), paperRim, w * 1.6f);
                C.DrawLine(c + new Vector2(-m * 1.0f, m * 0.75f), c + new Vector2(m * 1.0f, -m * 0.75f), red, w);
                break;
            }
            case Topic.Crime:
            {
                // A pistol, with a red arrow climbing beside it: crime's going up.
                var gun = new Color("2a2826");
                var p = c + new Vector2(-m * 0.35f, 0);
                C.DrawRect(new Rect2(p + new Vector2(-m * 0.7f, -m * 0.42f), new Vector2(m * 1.15f, m * 0.3f)), gun);
                C.DrawColoredPolygon(new[] { p + new Vector2(-m * 0.7f, -m * 0.15f), p + new Vector2(-m * 0.25f, -m * 0.15f), p + new Vector2(-m * 0.4f, m * 0.6f), p + new Vector2(-m * 0.82f, m * 0.6f) }, gun);
                C.DrawArc(p + new Vector2(-m * 0.12f, -m * 0.08f), m * 0.16f, 0, Mathf.Pi, 6, gun, Mathf.Max(1, m * 0.08f));
                var red = new Color("c8382e");
                var foot = c + new Vector2(m * 0.75f, m * 0.6f);
                var head = c + new Vector2(m * 0.75f, -m * 0.65f);
                C.DrawLine(foot, head + new Vector2(0, m * 0.3f), red, Mathf.Max(1.5f, m * 0.2f));
                C.DrawColoredPolygon(new[] { head, head + new Vector2(-m * 0.3f, m * 0.38f), head + new Vector2(m * 0.3f, m * 0.38f) }, red);
                break;
            }
            default:
            {
                // A folded newspaper: a headline bar and columns of print.
                var page = new Color("e9e3d3");
                C.DrawRect(new Rect2(c - new Vector2(m * 0.95f, m * 0.7f), new Vector2(m * 1.9f, m * 1.4f)), new Color("8a8478"));
                C.DrawRect(new Rect2(c - new Vector2(m * 0.88f, m * 0.63f), new Vector2(m * 1.76f, m * 1.26f)), page);
                C.DrawRect(new Rect2(c + new Vector2(-m * 0.75f, -m * 0.5f), new Vector2(m * 1.5f, m * 0.28f)), ink);
                for (int i = 0; i < 3; i++)
                {
                    float y = -m * 0.08f + i * m * 0.22f;
                    C.DrawLine(c + new Vector2(-m * 0.75f, y), c + new Vector2(-m * 0.08f, y), ink with { A = 0.6f }, Mathf.Max(1, line * 0.6f));
                    C.DrawLine(c + new Vector2(m * 0.08f, y), c + new Vector2(m * 0.75f, y), ink with { A = 0.6f }, Mathf.Max(1, line * 0.6f));
                }
                break;
            }
        }
    }

    /// <summary>The flag of the old country, for a pair talking in their own language.</summary>
    private void DrawFlag(Ethnicity people, Vector2 c, float m, Color ink)
    {
        var box = new Rect2(c - new Vector2(m * 1.05f, m * 0.7f), new Vector2(m * 2.1f, m * 1.4f));
        void Vertical(params Color[] bands)
        {
            float w = box.Size.X / bands.Length;
            for (int i = 0; i < bands.Length; i++) C.DrawRect(new Rect2(box.Position + new Vector2(i * w, 0), new Vector2(w + 0.5f, box.Size.Y)), bands[i]);
        }
        void Horizontal(params Color[] bands)
        {
            float h = box.Size.Y / bands.Length;
            for (int i = 0; i < bands.Length; i++) C.DrawRect(new Rect2(box.Position + new Vector2(0, i * h), new Vector2(box.Size.X, h + 0.5f)), bands[i]);
        }
        switch (people)
        {
            case Ethnicity.Italian: Vertical(new("2f8a4a"), new("f4f1ea"), new("c8382e")); break;
            case Ethnicity.Irish: Vertical(new("2f8a4a"), new("f4f1ea"), new("e08a3a")); break;
            case Ethnicity.German: Horizontal(new("1a1716"), new("c8382e"), new("e8c440")); break;
            case Ethnicity.Polish: Horizontal(new("f4f1ea"), new("c8382e")); break;
            case Ethnicity.Jewish:
            {
                // White with two blue stripes and a blue star between them.
                var blue = new Color("2f5fae");
                C.DrawRect(box, new Color("f4f1ea"));
                C.DrawRect(new Rect2(box.Position + new Vector2(0, box.Size.Y * 0.1f), new Vector2(box.Size.X, box.Size.Y * 0.13f)), blue);
                C.DrawRect(new Rect2(box.Position + new Vector2(0, box.Size.Y * 0.77f), new Vector2(box.Size.X, box.Size.Y * 0.13f)), blue);
                float r = m * 0.3f, w = Mathf.Max(1, m * 0.09f);
                for (int t = 0; t < 2; t++)
                {
                    var pts = new Vector2[4];
                    for (int i = 0; i < 3; i++)
                    {
                        float ang = -Mathf.Pi / 2 + t * Mathf.Pi + i * Mathf.Tau / 3;
                        pts[i] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    }
                    pts[3] = pts[0];
                    C.DrawPolyline(pts, blue, w);
                }
                break;
            }
            default: C.DrawRect(box, new Color("8a8478")); break;
        }
        C.DrawRect(box, ink, false, Mathf.Max(1, m * 0.06f));
    }
}
