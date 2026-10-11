using DryTown.Core;
using Godot;

/// <summary>Fronts on the map: a business a gang owns outright wears a banknote on its roof.</summary>
public partial class MapView
{
    /// <summary>A folded banknote edged in the owning gang's colour, beside the billboard.</summary>
    private void DrawFrontBadge(Vector2 c, Color gang)
    {
        float w = _t * 0.36f, h = _t * 0.2f;
        c += new Vector2(0, -h * 1.1f);
        var note = new Rect2(c.X - w / 2, c.Y - h / 2, w, h);
        C.DrawRect(new Rect2(note.Position + new Vector2(w * 0.08f, h * 0.25f), note.Size), Shadow);
        C.DrawRect(note, new Color("5f8a52"));
        C.DrawRect(note, gang, false, Mathf.Max(1.5f, _t * 0.03f));
        if (!Fine) return;
        // The seal in the middle, and the corner numbers.
        C.DrawCircle(c, h * 0.3f, new Color("d9d2a8"));
        C.DrawArc(c, h * 0.3f, 0, Mathf.Tau, 12, new Color("2f4a2a"), Mathf.Max(1, _t * 0.012f));
        C.DrawRect(new Rect2(c.X - w * 0.42f, c.Y - h * 0.32f, w * 0.12f, h * 0.18f), new Color("d9d2a8"));
        C.DrawRect(new Rect2(c.X + w * 0.3f, c.Y + h * 0.14f, w * 0.12f, h * 0.18f), new Color("d9d2a8"));
    }
}
