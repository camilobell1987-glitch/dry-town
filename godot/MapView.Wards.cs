using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>Ward lines and names. With <see cref="ShowWards"/> on, each ward is washed in the colour of the outfit that owns its alderman.</summary>
public partial class MapView
{
    /// <summary>Set while the City Hall tab is open.</summary>
    public bool ShowWards { get; set; }

    /// <summary>The ward the pointer is over, while wards are shown.</summary>
    public int HoverWard { get; private set; } = -1;

    /// <summary>A ward's outline in tiles, running down the middle of the streets around it.</summary>
    private Rect2 WardRect(int wardId)
    {
        var m = Map;
        int wx = wardId % m.WardsX, wy = wardId / m.WardsX;
        int bx0 = Enumerable.Range(0, m.BlocksX).First(b => b * m.WardsX / m.BlocksX == wx);
        int bx1 = Enumerable.Range(0, m.BlocksX).Last(b => b * m.WardsX / m.BlocksX == wx) + 1;
        int by0 = Enumerable.Range(0, m.BlocksY).First(b => b * m.WardsY / m.BlocksY == wy);
        int by1 = Enumerable.Range(0, m.BlocksY).Last(b => b * m.WardsY / m.BlocksY == wy) + 1;
        var from = new Vector2(bx0 * CityMap.StrideX + 0.5f, by0 * CityMap.StrideY + 0.5f);
        var to = new Vector2(bx1 * CityMap.StrideX + 0.5f, by1 * CityMap.StrideY + 0.5f);
        return new Rect2(from, to - from);
    }

    private int WardAtTile(Vector2 tile)
    {
        if (World == null) return -1;
        for (int i = 0; i < World.Wards.Count; i++)
            if (WardRect(i).HasPoint(tile)) return i;
        return -1;
    }

    private void DrawWards(Font font)
    {
        var w = World!;
        if (w.Wards.Count < 2 && !ShowWards) return;
        float alpha = ShowWards ? 0.9f : 0.35f;
        float lw = Mathf.Max(1.5f, _t * (ShowWards ? 0.09f : 0.05f));

        foreach (var ward in w.Wards)
        {
            var r = WardRect(ward.Id);
            var colour = ward.OwnerGangId >= 0 ? Palette.Gang(w, ward.OwnerGangId) : ward.Reformer ? Palette.Police : Palette.Neutral;
            if (ShowWards)
            {
                var wash = colour with { A = ward.OwnerGangId >= 0 ? 0.22f : 0.1f };
                if (ward.Id == HoverWard) wash.A += 0.08f;
                DrawRect(new Rect2(P(r.Position.X, r.Position.Y), r.Size * _t), wash);
            }

            // Dashed boundary, skipping the edges of the map.
            var c = new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) };
            for (int i = 0; i < 4; i++)
            {
                var a = c[i];
                var b = c[(i + 1) % 4];
                bool edge = (a.X == b.X && (a.X <= 0.5f || a.X >= Map.Width - 0.5f)) || (a.Y == b.Y && (a.Y <= 0.5f || a.Y >= Map.Height - 0.5f));
                if (edge && !ShowWards) continue;
                DrawDashedLine(P(a.X, a.Y), P(b.X, b.Y), (ShowWards ? colour : Palette.Ink) with { A = alpha }, lw, Mathf.Max(4, _t * 0.4f));
            }

            // The ward's name in the middle, with its alderman underneath when wards are shown.
            int size = Mathf.Max(11, (int)(_t * (ShowWards ? 0.55f : 0.42f)));
            var centre = P(r.GetCenter().X, r.GetCenter().Y);
            string name = ward.Name.ToUpperInvariant();
            var ink = (ShowWards ? Palette.Ink : Palette.Ink with { A = 0.28f });
            var textSize = font.GetStringSize(name, HorizontalAlignment.Left, -1, size);
            if (ShowWards)
                DrawRect(new Rect2(centre - new Vector2(textSize.X / 2 + 8, size + 4), new Vector2(textSize.X + 16, size * 2.6f + 8)), Palette.Panel with { A = 0.85f });
            DrawString(font, centre - new Vector2(textSize.X / 2, ShowWards ? 0 : -size * 0.35f), name, HorizontalAlignment.Left, -1, size, ink);
            if (ShowWards)
            {
                int small = Mathf.Max(9, size * 2 / 3);
                string who = ward.Reformer ? $"{ward.Alderman}, reformer" : ward.OwnerGangId >= 0 ? $"{ward.Alderman}, {w.GangById(ward.OwnerGangId).Name}" : $"{ward.Alderman}, the party";
                var ws = font.GetStringSize(who, HorizontalAlignment.Left, -1, small);
                DrawString(font, centre + new Vector2(-ws.X / 2, size * 1.2f), who, HorizontalAlignment.Left, -1, small, colour.Lightened(0.25f));
            }
        }
    }
}
