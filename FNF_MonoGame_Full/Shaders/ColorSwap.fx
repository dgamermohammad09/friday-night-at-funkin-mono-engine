// Port of the original funkin/graphics/shaders/ColorSwap.hx (ColorSwapShader, awesomeOutline = false):
// rgb -> hsv, hue += uTime, hsv -> rgb (hsv2rgb uses fract, so the hue wraps). Used by the Title screen
// (logo, GF, "Press Enter") for the Left/Right hue shift and the cheat-code rainbow.
// Textures in this engine are straight alpha; the hue rotation is independent of premultiplication.

sampler2D TextureSampler : register(s0);

float uTime;

float3 rgb2hsv(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 hsv2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

float4 MainPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 tex = tex2D(TextureSampler, uv) * color;
    float3 hsv = rgb2hsv(tex.rgb);
    hsv.x += uTime;
    return float4(hsv2rgb(hsv), tex.a);
}

technique ColorSwap
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 MainPS();
    }
}
