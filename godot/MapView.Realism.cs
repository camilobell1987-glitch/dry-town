using Godot;

/// <summary>
/// What makes the city read as a place rather than a diagram: weathering worked into every wall, roof and paving
/// stone, a shadow where each building meets the pavement, and over the whole view a period grade, the warm
/// light and cool shade of an old photograph, a vignette and a little film grain.
/// </summary>
public partial class MapView
{
    /// <summary>Whether the view gets the film look: grade, vignette and grain. Weathering is always on.</summary>
    public bool FilmLook
    {
        get => _filmLook;
        set { _filmLook = value; QueueRedraw(); }
    }
    private bool _filmLook = true;

    private ShaderMaterial? _groundMaterial, _wallMaterial, _gradeMaterial;
    private Pen? _grade;

    /// <summary>
    /// Stains and grain in plan position, so they stay put on the ground and the walls however the view moves:
    /// broad blotches of damp and soot a tile or so across, and a fine grit over them.
    /// </summary>
    private const string WeatherShader = @"
shader_type canvas_item;
uniform float unit = 32.0;
uniform float stain = 0.1;
uniform float grit = 0.05;
uniform float grit_scale = 40.0;
varying vec2 plan;

void vertex() { plan = VERTEX / unit; }

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * noise(p); p = p * 2.03 + vec2(17.0, 31.0); a *= 0.5; }
    return v;
}
void fragment() {
    float blotch = fbm(plan * 1.6) - 0.5;
    float fine = noise(plan * grit_scale) - 0.5;
    COLOR.rgb *= 1.0 + blotch * stain * 2.0 + fine * grit;
}
";

    /// <summary>
    /// The look of a period photograph over the whole view: a little colour taken out, shade toward slate and
    /// light toward amber, a gentle S-curve, the corners darkened, a soft glow round bright lights, and a fine grain that moves.
    /// </summary>
    private const string GradeShader = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
uniform float vignette = 0.22;
uniform float grain = 0.022;
uniform float sharpen = 0.6;
uniform float saturation = 0.9;
uniform float bloom = 0.9;

vec3 bright(vec2 uv) { return max(texture(screen, uv).rgb - 0.6, 0.0); }

void fragment() {
    vec3 c = clamp(texture(screen, SCREEN_UV).rgb, 0.0, 1.0);
    // An unsharp mask, so edges, lines and lettering stand out crisply from the grade.
    vec2 px = SCREEN_PIXEL_SIZE;
    vec3 soft = (texture(screen, SCREEN_UV + vec2(px.x, 0.0)).rgb + texture(screen, SCREEN_UV - vec2(px.x, 0.0)).rgb
        + texture(screen, SCREEN_UV + vec2(0.0, px.y)).rgb + texture(screen, SCREEN_UV - vec2(0.0, px.y)).rgb) * 0.25;
    c = clamp(c + (c - soft) * sharpen, 0.0, 1.0);
    // Light spills a little round anything bright: lamps, lit windows, the fire in a burning shop.
    vec3 glow = vec3(0.0);
    for (int ring = 1; ring <= 2; ring++)
        for (int k = 0; k < 8; k++) {
            float a = float(k) * 0.785398 + float(ring) * 0.39;
            glow += bright(SCREEN_UV + vec2(cos(a), sin(a)) * SCREEN_PIXEL_SIZE * 5.0 * float(ring));
        }
    c += glow / 16.0 * bloom;
    float l = dot(c, vec3(0.299, 0.587, 0.114));
    c = mix(vec3(l), c, saturation);
    c += mix(vec3(-0.004, 0.0, 0.01), vec3(0.028, 0.012, -0.02), smoothstep(0.12, 0.7, l));
    c = clamp(c, 0.0, 1.0);
    c = mix(c, c * c * (3.0 - 2.0 * c), 0.15);
    vec2 d = (UV - 0.5) * vec2(1.0, 1.15);
    c *= 1.0 - vignette * smoothstep(0.3, 0.75, length(d));
    float g = fract(sin(dot(FRAGCOORD.xy + floor(TIME * 12.0) * vec2(37.0, 17.0), vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
    COLOR = vec4(c + g * grain, 1.0);
}
";

    /// <summary>Work the weathering into the ground and the buildings, and lay the grade over the view.</summary>
    private void DressForRealism()
    {
        _groundMaterial ??= Weather(stain: 0.16f, grit: 0.08f, gritScale: 36);
        _wallMaterial ??= Weather(stain: 0.1f, grit: 0.05f, gritScale: 60);
        foreach (var pen in _groundFull) RenderingServer.CanvasItemSetMaterial(pen.Item, _groundMaterial.GetRid());
        foreach (var pen in _groundCoarse) RenderingServer.CanvasItemSetMaterial(pen.Item, _groundMaterial.GetRid());
        foreach (var pen in _full) RenderingServer.CanvasItemSetMaterial(pen.Item, _wallMaterial.GetRid());
        foreach (var pen in _coarse) RenderingServer.CanvasItemSetMaterial(pen.Item, _wallMaterial.GetRid());

        _gradeMaterial ??= new ShaderMaterial { Shader = new Shader { Code = GradeShader } };
        _grade = NewPen(GetCanvasItem());
        RenderingServer.CanvasItemSetMaterial(_grade.Item, _gradeMaterial.GetRid());
    }

    private static ShaderMaterial Weather(float stain, float grit, float gritScale)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = WeatherShader } };
        m.SetShaderParameter("unit", U);
        m.SetShaderParameter("stain", stain);
        m.SetShaderParameter("grit", grit);
        m.SetShaderParameter("grit_scale", gritScale);
        return m;
    }

    /// <summary>The grade covers the map, under the street names and signs, and goes when the film look is off.</summary>
    private void DrawGrade()
    {
        if (_grade == null) return;
        _grade.Clear();
        RenderingServer.CanvasItemSetVisible(_grade.Item, _filmLook);
        if (_filmLook) RenderingServer.CanvasItemAddRect(_grade.Item, new Rect2(Vector2.Zero, Size), Colors.White);
    }

    /// <summary>
    /// Where a building meets the ground the light can't get in: a soft dark band along the foot of its front
    /// and west walls, on the pavement and the yard.
    /// </summary>
    private void DrawContactShadow(float x0, float y0, float x1, float y1)
    {
        var dark = new Color(0, 0, 0, 0.42f);
        const float reach = 0.1f;
        C.DrawShadedQuad(P(x0, y1), P(x1, y1), P(x1, y1 + reach), P(x0 - reach * 0.5f, y1 + reach), dark, dark, Clear, Clear);
        C.DrawShadedQuad(P(x0, y0), P(x0, y1), P(x0 - reach, y1), P(x0 - reach, y0), dark, dark, Clear, Clear);
    }
}
