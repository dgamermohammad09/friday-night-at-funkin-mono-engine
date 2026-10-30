// Port of funkin/graphics/shaders/OverlayBlend.hx (limoRide.hxc limoSunset.shader):
//   color = texture(bitmap, uv); gf = texture(funnyShit, uv + vec2(0.1, 0.2)); gl_FragColor = blendOverlay(color, gf)
//   blendOverlay = mix(1 - 2(1-base)(1-blend), 2*base*blend, step(base, 0.5)) per channel (incl. alpha).
// OpenFL textures are premultiplied; this engine's are straight -> premultiply both, un-premultiply the result.

sampler2D TextureSampler : register(s0);
Texture2D OverlayTex;
sampler2D OverlaySampler = sampler_state
{
    Texture = <OverlayTex>;
    MinFilter = Point; MagFilter = Point; MipFilter = Point;   // OpenFL ShaderInput default filter NEAREST, wrap CLAMP
    AddressU = Clamp; AddressV = Clamp;
};

float4 blendOverlay(float4 base, float4 blend)
{
    float4 screen = 1.0 - 2.0 * (1.0 - base) * (1.0 - blend);
    float4 mult = 2.0 * base * blend;
    return lerp(screen, mult, step(base, float4(0.5, 0.5, 0.5, 0.5)));
}

float4 MainPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 t = tex2D(TextureSampler, uv) * color;
    float4 c = float4(t.rgb * t.a, t.a);
    float4 o = tex2D(OverlaySampler, uv + float2(0.1, 0.2));
    float4 g = float4(o.rgb * o.a, o.a);
    float4 m = saturate(blendOverlay(c, g));
    if (m.a <= 0.0) return float4(0, 0, 0, 0);
    return float4(m.rgb / m.a, m.a);
}

technique OverlayBlend
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 MainPS();
    }
}
