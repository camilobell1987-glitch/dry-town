using System.Collections.Generic;
using Godot;

/// <summary>
/// Draws into a rendering-server canvas item, with the same calls a <see cref="CanvasItem"/> offers
/// in its draw callback. Shapes are gathered into one coloured triangle mesh and sent on
/// <see cref="Flush"/>, so a whole stretch of city costs the graphics card one draw call rather than
/// hundreds. What a pen draws stays drawn until the item is cleared, so the city can be recorded once
/// and then only moved and scaled as the view pans and zooms.
/// </summary>
public sealed class Pen
{
    public Rid Item { get; }

    private readonly List<Vector2> _points = new();
    private readonly List<Color> _colours = new();
    private readonly List<int> _indices = new();

    public Pen(Rid item) { Item = item; }

    public void Clear()
    {
        _points.Clear();
        _colours.Clear();
        _indices.Clear();
        RenderingServer.CanvasItemClear(Item);
    }

    /// <summary>Send whatever has been drawn since the last flush.</summary>
    public void Flush()
    {
        if (_indices.Count == 0) return;
        RenderingServer.CanvasItemAddTriangleArray(Item, _indices.ToArray(), _points.ToArray(), _colours.ToArray());
        _points.Clear();
        _colours.Clear();
        _indices.Clear();
    }

    private int Vertex(Vector2 p, Color c)
    {
        _points.Add(p);
        _colours.Add(c);
        return _points.Count - 1;
    }

    private void Triangle(int a, int b, int c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color colour)
    {
        if (_points.Count > 60000) Flush();
        int i = Vertex(a, colour);
        Vertex(b, colour);
        Vertex(c, colour);
        Vertex(d, colour);
        Triangle(i, i + 1, i + 2);
        Triangle(i, i + 2, i + 3);
    }

    /// <summary>A four-cornered shape whose colour blends between its corners: soot fading up a wall, shade inside a parapet.</summary>
    public void DrawShadedQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        if (_points.Count > 60000) Flush();
        int i = Vertex(a, ca);
        Vertex(b, cb);
        Vertex(c, cc);
        Vertex(d, cd);
        Triangle(i, i + 1, i + 2);
        Triangle(i, i + 2, i + 3);
    }

    /// <summary>A disc whose colour fades smoothly from its centre to its rim: a pool of lamplight, a glow round a sign.</summary>
    public void DrawRadial(Vector2 centre, float radius, Color inner, Color outer, int points = 28)
    {
        if (radius <= 0) return;
        if (_points.Count > 60000) Flush();
        int mid = Vertex(centre, inner);
        for (int i = 0; i < points; i++)
        {
            float a = Mathf.Tau * i / points;
            Vertex(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, outer);
        }
        for (int i = 0; i < points; i++) Triangle(mid, mid + 1 + i, mid + 1 + (i + 1) % points);
    }

    public void DrawRect(Rect2 r, Color c, bool filled = true, float width = -1)
    {
        var p = r.Position;
        var e = r.End;
        if (filled)
        {
            Quad(p, new Vector2(e.X, p.Y), e, new Vector2(p.X, e.Y), c);
            return;
        }
        DrawPolyline(new[] { p, new Vector2(e.X, p.Y), e, new Vector2(p.X, e.Y), p }, c, width);
    }

    public void DrawCircle(Vector2 centre, float radius, Color c)
    {
        if (radius <= 0) return;
        if (_points.Count > 60000) Flush();
        int n = radius < 6 ? 8 : radius < 24 ? 14 : 24;
        int mid = Vertex(centre, c);
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            Vertex(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, c);
        }
        for (int i = 0; i < n; i++) Triangle(mid, mid + 1 + i, mid + 1 + (i + 1) % n);
    }

    /// <summary>A line as a thin quad. A width of zero or less draws one unit wide.</summary>
    public void DrawLine(Vector2 from, Vector2 to, Color c, float width = -1)
    {
        var along = to - from;
        if (along.LengthSquared() < 1e-8f) return;
        var side = along.Normalized().Orthogonal() * (width <= 0 ? 0.5f : width / 2);
        Quad(from + side, to + side, to - side, from - side, c);
    }

    public void DrawPolyline(Vector2[] points, Color c, float width = -1)
    {
        for (int i = 1; i < points.Length; i++) DrawLine(points[i - 1], points[i], c, width);
    }

    public void DrawArc(Vector2 centre, float radius, float from, float to, int points, Color c, float width = -1)
    {
        var line = new Vector2[points + 1];
        for (int i = 0; i <= points; i++)
        {
            float a = from + (to - from) * i / points;
            line[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        DrawPolyline(line, c, width);
    }

    /// <summary>A filled polygon: a fan when it is convex, cut into triangles when it isn't. Slivers are skipped.</summary>
    public void DrawColoredPolygon(Vector2[] points, Color c)
    {
        int n = points.Length;
        if (n < 3) return;
        float area = 0;
        bool pos = false, neg = false;
        for (int i = 0; i < n; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % n];
            var d = points[(i + 2) % n];
            area += a.X * b.Y - b.X * a.Y;
            float turn = (b - a).Cross(d - b);
            if (turn > 1e-6f) pos = true;
            else if (turn < -1e-6f) neg = true;
        }
        if (Mathf.Abs(area) < 0.01f) return;
        if (_points.Count > 60000) Flush();
        int first = _points.Count;
        foreach (var p in points) Vertex(p, c);
        if (!(pos && neg))
        {
            for (int i = 1; i < n - 1; i++) Triangle(first, first + i, first + i + 1);
            return;
        }
        var tris = Geometry2D.TriangulatePolygon(points);
        for (int i = 0; i + 2 < tris.Length; i += 3) Triangle(first + tris[i], first + tris[i + 1], first + tris[i + 2]);
    }

    public void DrawString(Font font, Vector2 pos, string text, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1, int size = 16, Color? modulate = null)
    {
        Flush();
        font.DrawString(Item, pos, text, align, width, size, modulate ?? Colors.White);
    }

    public void DrawSetTransform(Vector2 position, float rotation)
    {
        Flush();
        RenderingServer.CanvasItemAddSetTransform(Item, new Transform2D(rotation, position));
    }

    public void DrawDashedLine(Vector2 from, Vector2 to, Color c, float width, float dash)
    {
        float length = from.DistanceTo(to);
        if (length <= 0) return;
        var dir = (to - from) / length;
        for (float d = 0; d < length; d += dash * 2)
            DrawLine(from + dir * d, from + dir * Mathf.Min(d + dash, length), c, width);
    }
}
