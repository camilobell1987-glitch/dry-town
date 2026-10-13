using System;
using System.Collections.Generic;
using DryTown.Core;
using Godot;

/// <summary>
/// Picking out one resident from the crowd. The crowd walks on the graphics card, so to find who was
/// clicked the map works out where each person on the blocks near the click is right now, the same way
/// the shader does, and takes the nearest one who's out on the street.
/// </summary>
public partial class MapView
{
    public event Action<int>? CitizenClicked;

    /// <summary>Everyone who lives in the city, by the block and number the crowd draws them with.</summary>
    public CityDirectory? Directory => World == null ? null : _directory ??= new CityDirectory(World, PopulationOf, l => FillerOf(l) == Filler.Tenement);

    private CityDirectory? _directory;

    public int SelectedCitizen { get; private set; } = -1;

    /// <summary>Residents stopped to talk this frame, and where they stand.</summary>
    private readonly Dictionary<int, Vector2> _chatSpots = new();
    private readonly Dictionary<int, int> _chatPartners = new();

    /// <summary>Who a resident is stopped talking to this moment, or -1.</summary>
    public int TalkingWith(int citizen) => _chatPartners.GetValueOrDefault(citizen, -1);

    /// <summary>Where resident n of a block is on the pavement now, in tiles, and whether he's out at all.</summary>
    private (Vector2 At, bool Out) CrowdSpot(int bx, int by, int n)
    {
        int salt = by * 7919 + bx * 104729 + n;
        float across = (Hash(salt, 910) - 0.5f) * 0.14f;
        float outRoll = Hash(salt, 914), barRoll = Hash(salt, 915);
        float start = Hash(salt, 911) * CrowdPerimeter;
        float pace = (0.15f + Hash(salt, 912) * 0.12f) * (Hash(salt, 913) < 0.5f ? 1 : -1);
        float door = _crowdDoors.Length > 0 ? _crowdDoors[bx, by] : 0;
        float hour = HourOfDay;
        bool night = hour >= 20 || hour < 2;
        float d = Mathf.PosMod(start + _anim * pace, CrowdPerimeter);
        bool drinking = night && door > 0 && barRoll < 0.6f;
        if (drinking) d = Mathf.PosMod(door * CrowdPerimeter + (barRoll / 0.6f - 0.5f) * 0.7f, CrowdPerimeter);
        bool shown = outRoll < OutsideAt(hour) * (drinking ? 2.5f : 1);
        float w = CrowdW, h = CrowdH;
        var p = d < w ? new Vector2(d, across)
            : d < w + h ? new Vector2(w - across, d - w)
            : d < 2 * w + h ? new Vector2(w - (d - w - h), h - across)
            : new Vector2(across, h - (d - 2 * w - h));
        return (new Vector2(bx * CityMap.StrideX + 1 - 0.1f, by * CityMap.StrideY + 1 - 0.1f) + p, shown);
    }

    /// <summary>The resident out walking nearest a click, or -1.</summary>
    private int CrowdPick(Vector2 screen)
    {
        if (Directory == null || _crowdBlocks.Length == 0 || Tile < 9) return -1;
        var tile = (screen - Origin) / Tile;
        var map = World!.Map;
        float reach = Mathf.Max(ClickRadius(DotRadius), 7 / Mathf.Max(1, Tile));
        int best = -1;
        float bestDistance = reach;
        int cx = (int)(tile.X / CityMap.StrideX), cy = (int)(tile.Y / CityMap.StrideY);
        for (int by = Math.Max(0, cy - 1); by <= Math.Min(map.BlocksY - 1, cy + 1); by++)
        for (int bx = Math.Max(0, cx - 1); bx <= Math.Min(map.BlocksX - 1, cx + 1); bx++)
        for (int n = 0; n < _crowdBlocks[bx, by]; n++)
        {
            var (at, shown) = CrowdSpot(bx, by, n);
            float distance = at.DistanceTo(tile);
            if (!shown || distance >= bestDistance) continue;
            bestDistance = distance;
            best = Directory.IdOf(bx, by, n);
        }
        return best;
    }

    /// <summary>A ring round the resident picked out, following him as he walks.</summary>
    private void DrawCitizenRing()
    {
        if (SelectedCitizen < 0 || Directory == null || !Directory.Exists(SelectedCitizen)) return;
        var at = WhereIs(SelectedCitizen);
        if (at is { } spot) SelectionRing(spot, DotRadius);
    }

    /// <summary>Where a resident can be seen right now, or null when he's indoors.</summary>
    public Vector2? WhereIs(int citizen)
    {
        if (Directory == null) return null;
        if (_chatSpots.TryGetValue(citizen, out var talking)) return talking;
        var (bx, by, n) = Directory.Where(citizen);
        var (at, shown) = CrowdSpot(bx, by, n);
        return shown ? at : null;
    }

    /// <summary>
    /// One of the block's residents who's indoors just now, to stand on the pavement and talk, so he isn't
    /// also seen walking the block at the same time.
    /// </summary>
    private int Talker(int bx, int by, int seed, int side)
    {
        int people = _crowdBlocks[bx, by];
        int n = 0;
        for (int k = 0; k < 6; k++)
        {
            n = (int)(Hash(seed, 950 + side * 10 + k) * people);
            if (!CrowdSpot(bx, by, n).Out) break;
        }
        return Directory!.IdOf(bx, by, n);
    }
}
