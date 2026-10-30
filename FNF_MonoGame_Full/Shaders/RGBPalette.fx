// Port of Psych Engine 1.0.4 shaders/RGBPalette.hx (RGBPalette shader used by notes, strums and splashes).
// newColor.rgb = min(color.r * r + color.g * g + color.b * b, 1); color = mix(color, newColor, mult)
// Textures in this engine are straight (non-premultiplied) alpha.

sampler2D TextureSampler : register(s0);

float3 r;
float3 g;
float3 b;
float mult;

float4 MainPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 tex = tex2D(TextureSampler, uv);
    if (tex.a == 0.0 || mult == 0.0) return tex * color;
    float4 newColor = tex;
    newColor.rgb = min(tex.r * r + tex.g * g + tex.b * b, float3(1.0, 1.0, 1.0));
    float4 c = lerp(tex, newColor, mult);
    return c * color;
}

technique RGBPalette
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 MainPS();
    }
}
