using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// Everyone who lives in the city, one grey dot each. Most are indoors at any hour; the rest walk
/// round the pavement of the block they live on, more of them at the morning and evening rush, and at
/// night a crowd gathers at the door of any speakeasy on the block. There are far too many to draw one
/// by one, so they're one batch the graphics card stamps out, and a shader walks them from the clock:
/// the processor does nothing for them from frame to frame.
/// </summary>
public partial class MapView
{
    private Rid _crowdItem;
    private MultiMesh? _crowdMesh;
    private ShaderMaterial? _crowdMaterial;

    /// <summary>How many people live in the city, as the map counts them.</summary>
    public int Population { get; private set; }

    private const string CrowdShader = @"
shader_type canvas_item;
uniform float t;
uniform float radius;
uniform float outside;
uniform float night;
uniform vec4 fill : source_color;
uniform vec4 edge : source_color;
// The night's darkening. The crowd keeps its data in the instance colour, so it can't be modulated.
uniform vec4 shade : source_color = vec4(1.0);
const float W = 4.2;
const float H = 2.2;
varying vec2 uv;
void vertex() {
    // x, y: the block's north-west corner; z: how far round the block he starts; w: his pace, signed.
    vec4 c = INSTANCE_CUSTOM;
    float per = 2.0 * (W + H);
    float d = mod(c.z + t * c.w, per);
    // r: where across the pavement he walks; g: whether he's out at this hour; b: whether he goes to the
    // speakeasy at night; a: how far round the block its door is, as a share of the way, or 0 if none.
    float across = COLOR.r;
    // At night a speakeasy on the block draws more people out than walk the streets, and they stand at its door.
    bool drinking = night > 0.5 && COLOR.a > 0.0 && COLOR.b < 0.6;
    if (drinking) d = mod(COLOR.a * per + (COLOR.b / 0.6 - 0.5) * 0.7, per);
    // Indoors he shrinks to nothing.
    float size = COLOR.g < outside * (drinking ? 2.5 : 1.0) ? radius * 2.0 : 0.0;
    vec2 p;
    if (d < W) p = vec2(d, across);
    else if (d < W + H) p = vec2(W - across, d - W);
    else if (d < 2.0 * W + H) p = vec2(W - (d - W - H), H - across);
    else p = vec2(across, H - (d - 2.0 * W - H));
    VERTEX = VERTEX * size + c.xy + p;
    uv = UV;
}
void fragment() {
    float d = length(uv - vec2(0.5)) * 2.0;
    if (d > 1.0) discard;
    COLOR = (d > 0.78 ? edge : fill) * shade;
}";

    /// <summary>How many people live in a lot's building. A tenement holds twenty families; a shop has flats over it.</summary>
    private int PopulationOf(Lot lot)
    {
        float h = Hash(lot.Id, 900);
        if (lot.Terrain == Terrain.Church) return 2;
        if (lot.Terrain != Terrain.Land) return 0;
        return lot.Use switch
        {
            LotUse.Business => 20 + (int)(h * 21),
            LotUse.Headquarters => 15 + (int)(h * 11),
            LotUse.Empty => FillerOf(lot) switch
            {
                Filler.Tenement => lot.Court ? 120 + (int)(h * 41) : 80 + (int)(h * 31),
                Filler.Office => 2,
                _ => 0,
            },
            _ => 2,
        };
    }

    /// <summary>Put everyone on the pavement round the block they live on.</summary>
    private void BuildCrowd(Rid parent)
    {
        var map = World!.Map;
        var perBlock = new int[map.BlocksX, map.BlocksY];
        foreach (var lot in map.Lots)
            perBlock[(lot.X - 1) / CityMap.StrideX, (lot.Y - 1) / CityMap.StrideY] += PopulationOf(lot);
        Population = perBlock.Cast<int>().Sum();

        _crowdMaterial ??= new ShaderMaterial { Shader = new Shader { Code = CrowdShader } };
        _crowdMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One },
            CustomAabb = new Aabb(new Vector3(-2, -2, -1), new Vector3(map.Width + 4, map.Height + 4, 2)),
        };
        _crowdMesh.InstanceCount = Population;
        _crowdBlocks = perBlock;
        _directory = null;
        FillCrowd();
        _crowdItem = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_crowdItem, parent);
        RenderingServer.CanvasItemSetDrawIndex(_crowdItem, _items.Count);
        _items.Add(_crowdItem);
        RenderingServer.CanvasItemSetTransform(_crowdItem, Transform2D.Identity.Scaled(new Vector2(U, U)));
        RenderingServer.CanvasItemSetCustomRect(_crowdItem, true, new Rect2(-2, -2, map.Width + 4, map.Height + 4));
        RenderingServer.CanvasItemSetMaterial(_crowdItem, _crowdMaterial.GetRid());
        RenderingServer.CanvasItemAddMultimesh(_crowdItem, _crowdMesh.GetRid());
        _crowdMaterial.SetShaderParameter("fill", Palette.Citizen);
        _crowdMaterial.SetShaderParameter("edge", new Color("d8d0c0", 0.55f));
    }

    private int[,] _crowdBlocks = new int[0, 0];
    private float[,] _crowdDoors = new float[0, 0];
    private int _goneCount;

    /// <summary>Whether resident n of a block is held somewhere or dead.</summary>
    private bool Gone(int bx, int by, int n) =>
        World!.StandingOf(by * World.Map.BlocksX * CityDirectory.PerBlock + bx * CityDirectory.PerBlock + n) is Standing.Held or Standing.Dead or Standing.Jailed;
    private const float CrowdW = 4.2f, CrowdH = 2.2f, CrowdPerimeter = 2 * (CrowdW + CrowdH);

    /// <summary>How far round a block's pavement a lot's front door is, from its north-west corner.</summary>
    private static float DoorAlong(Lot lot, int bx, int by)
    {
        float x0 = bx * CityMap.StrideX + 1 - 0.1f;
        float x = lot.X + 0.5f;
        return lot.FrontY < lot.Y ? x - x0 : CrowdW + CrowdH + (x0 + CrowdW - x);
    }

    /// <summary>A speakeasy's door on each block that has one, as the map shows it now.</summary>
    private float[,] SpeakeasyDoors()
    {
        var w = World!;
        var doors = new float[w.Map.BlocksX, w.Map.BlocksY];
        foreach (var b in w.Businesses)
        {
            if (!b.IsOpen || ShownState(b).Racket is not (RacketKind.Speakeasy or RacketKind.Nightclub or RacketKind.Casino)) continue;
            var lot = w.LotOf(b);
            int bx = (lot.X - 1) / CityMap.StrideX, by = (lot.Y - 1) / CityMap.StrideY;
            if (doors[bx, by] == 0) doors[bx, by] = DoorAlong(lot, bx, by) / CrowdPerimeter;
        }
        return doors;
    }

    /// <summary>Write where everyone lives and how they spend their day into the batch.</summary>
    private void FillCrowd()
    {
        var map = World!.Map;
        var perBlock = _crowdBlocks;
        var doors = SpeakeasyDoors();
        _crowdDoors = doors;

        // Per person: a 2D transform (8 floats), a colour (4) and custom data (4).
        var buffer = new float[Population * 16];
        int i = 0;
        for (int by = 0; by < map.BlocksY; by++)
        for (int bx = 0; bx < map.BlocksX; bx++)
        for (int n = 0; n < perBlock[bx, by]; n++, i++)
        {
            int k = i * 16, salt = by * 7919 + bx * 104729 + n;
            buffer[k + 0] = 1; buffer[k + 5] = 1;
            buffer[k + 8] = (Hash(salt, 910) - 0.5f) * 0.14f;
            // The kidnapped and the dead are never seen out.
            buffer[k + 9] = Gone(bx, by, n) ? 2 : Hash(salt, 914);
            buffer[k + 10] = Hash(salt, 915);
            buffer[k + 11] = doors[bx, by];
            buffer[k + 12] = bx * CityMap.StrideX + 1 - 0.1f;
            buffer[k + 13] = by * CityMap.StrideY + 1 - 0.1f;
            buffer[k + 14] = Hash(salt, 911) * 2 * (4.2f + 2.2f);
            buffer[k + 15] = (0.15f + Hash(salt, 912) * 0.12f) * (Hash(salt, 913) < 0.5f ? 1 : -1);
        }
        RenderingServer.MultimeshSetBuffer(_crowdMesh!.GetRid(), buffer);
    }

    /// <summary>The share of people out on the street at an hour of the day: a morning and evening rush, lunch, and the dead of night.</summary>
    private static float OutsideAt(float hour)
    {
        ReadOnlySpan<(float Hour, float Share)> day = stackalloc (float, float)[]
        {
            (0, 0.04f), (4, 0.01f), (6, 0.04f), (7.5f, 0.15f), (9, 0.08f), (12.5f, 0.12f), (14, 0.08f),
            (17.5f, 0.15f), (19, 0.1f), (21, 0.09f), (23, 0.06f), (24, 0.04f),
        };
        for (int i = 1; i < day.Length; i++)
            if (hour <= day[i].Hour)
                return Mathf.Lerp(day[i - 1].Share, day[i].Share, (hour - day[i - 1].Hour) / (day[i].Hour - day[i - 1].Hour));
        return day[0].Share;
    }

    /// <summary>Move the crowd on to the clock and size the dots to the zoom.</summary>
    private void PlaceCrowd()
    {
        if (_crowdMaterial == null) return;
        // Someone was snatched, killed or let go: take them off the street, or put them back.
        int gone = World!.Acquaintances.Count(kv => kv.Value.Standing is Standing.Held or Standing.Dead or Standing.Jailed);
        if (gone != _goneCount)
        {
            _goneCount = gone;
            FillCrowd();
        }
        float hour = HourOfDay;
        _crowdMaterial.SetShaderParameter("t", _anim);
        _crowdMaterial.SetShaderParameter("outside", OutsideAt(hour));
        _crowdMaterial.SetShaderParameter("night", hour >= 20 || hour < 2 ? 1f : 0f);
        _crowdMaterial.SetShaderParameter("radius", Mathf.Max(2.5f / Mathf.Max(0.01f, Tile), 0.05f) * 0.85f);
    }
}
