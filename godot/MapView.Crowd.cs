using System;
using System.Linq;
using DryTown.Core;
using Godot;

/// <summary>
/// Everyone who lives in the city, one grey dot each, walking round the pavement of the block they
/// live on. There are far too many to draw one by one, so they're one batch the graphics card
/// stamps out, and a shader walks them round the block from the clock: the processor does nothing
/// for them from frame to frame.
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
uniform vec4 fill : source_color;
uniform vec4 edge : source_color;
const float W = 4.2;
const float H = 2.2;
varying vec2 uv;
void vertex() {
    // x, y: the block's north-west corner; z: how far round the block he starts; w: his pace, signed.
    vec4 c = INSTANCE_CUSTOM;
    float per = 2.0 * (W + H);
    float d = mod(c.z + t * c.w, per);
    float across = COLOR.r;
    vec2 p;
    if (d < W) p = vec2(d, across);
    else if (d < W + H) p = vec2(W - across, d - W);
    else if (d < 2.0 * W + H) p = vec2(W - (d - W - H), H - across);
    else p = vec2(across, H - (d - 2.0 * W - H));
    VERTEX = VERTEX * radius * 2.0 + c.xy + p;
    uv = UV;
}
void fragment() {
    float d = length(uv - vec2(0.5)) * 2.0;
    if (d > 1.0) discard;
    COLOR = d > 0.78 ? edge : fill;
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
            buffer[k + 11] = 1;
            buffer[k + 12] = bx * CityMap.StrideX + 1 - 0.1f;
            buffer[k + 13] = by * CityMap.StrideY + 1 - 0.1f;
            buffer[k + 14] = Hash(salt, 911) * 2 * (4.2f + 2.2f);
            buffer[k + 15] = (0.15f + Hash(salt, 912) * 0.12f) * (Hash(salt, 913) < 0.5f ? 1 : -1);
        }
        RenderingServer.MultimeshSetBuffer(_crowdMesh.GetRid(), buffer);

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

    /// <summary>Move the crowd on to the clock and size the dots to the zoom.</summary>
    private void PlaceCrowd()
    {
        if (_crowdMaterial == null) return;
        _crowdMaterial.SetShaderParameter("t", _anim);
        _crowdMaterial.SetShaderParameter("radius", Mathf.Max(3.5f / Mathf.Max(0.01f, Tile), 0.11f) * 0.75f);
    }
}
