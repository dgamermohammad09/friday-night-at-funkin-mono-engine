// Port of the original FNF shader assets/preload/shaders/adjustColor.frag
// (Animate/Flash "Adjust Color" filter). Same math, same order:
// brightness -> hue rotate -> contrast -> saturation.
// Textures in this engine are straight (non-premultiplied) alpha, so no un-premultiply step.

sampler2D TextureSampler : register(s0);

float hue;
float saturation;
float brightness;
float contrast;

static const float3 grayscaleValues = float3(0.3098039215686275, 0.607843137254902, 0.0823529411764706);
static const float e = 2.718281828459045;

float3 applyHueRotate(float3 c, float aHue)
{
    float angle = radians(aHue);
    float ca = cos(angle);
    float sa = sin(angle);
    // GLSL mat3 is column-major: m * c = col0 * c.r + col1 * c.g + col2 * c.b
    float3 col0 = float3(0.213, 0.213, 0.213) + ca * float3(0.787, -0.213, -0.213) + sa * float3(-0.213, 0.143, -0.787);
    float3 col1 = float3(0.715, 0.715, 0.715) + ca * float3(-0.715, 0.285, -0.715) + sa * float3(-0.715, 0.140, 0.715);
    float3 col2 = float3(0.072, 0.072, 0.072) + ca * float3(-0.072, -0.072, 0.928) + sa * float3(0.928, -0.283, 0.072);
    return col0 * c.r + col1 * c.g + col2 * c.b;
}

float3 applySaturation(float3 c, float value)
{
    if (value > 0.0) { value = value * 3.0; }
    value = (1.0 + (value / 100.0));
    float3 grayscale = dot(c, grayscaleValues).xxx;
    return saturate(lerp(grayscale, c, value));
}

float3 applyContrast(float3 c, float value)
{
    value = (1.0 + (value / 100.0));
    if (value > 1.0)
    {
        value = (((0.00852259 * pow(e, 4.76454 * (value - 1.0))) * 1.01) - 0.0086078159) * 10.0;
        value += 1.0;
    }
    return saturate((c - 0.25) * value + 0.25);
}

float4 MainPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 tex = tex2D(TextureSampler, uv) * color;
    float3 c = tex.rgb;
    c = c + (brightness / 255.0);
    c = applyHueRotate(c, hue);
    c = applyContrast(c, contrast);
    c = applySaturation(c, saturation);
    return float4(c, tex.a);
}

technique AdjustColor
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 MainPS();
    }
}
