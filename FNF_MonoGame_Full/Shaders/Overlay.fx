// OpenFL/flixel BlendMode.OVERLAY for sprites drawn with blend 'overlay' (Psych setBlendMode(tag, 'overlay')):
//   result = base < 0.5 ? 2 * base * blend : 1 - 2 * (1 - base) * (1 - blend), then alpha-composited over the base.
// The base (destination) is a copy of the current render target bound as DestTexture.
sampler2D TextureSampler : register(s0);
texture DestTexture;
sampler2D DestSampler = sampler_state { Texture = <DestTexture>; MinFilter = Point; MagFilter = Point; AddressU = Clamp; AddressV = Clamp; };
float2 ScreenSize;

float4 MainPS(float4 pos : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 s = tex2D(TextureSampler, uv) * color;
    float3 b = tex2D(DestSampler, pos.xy / ScreenSize).rgb;
    float3 lo = 2.0 * b * s.rgb;
    float3 hi = 1.0 - 2.0 * (1.0 - b) * (1.0 - s.rgb);
    float3 o = float3(b.r < 0.5 ? lo.r : hi.r, b.g < 0.5 ? lo.g : hi.g, b.b < 0.5 ? lo.b : hi.b);
    return float4(o, s.a);
}

technique Overlay
{
    pass P0 { PixelShader = compile ps_4_0 MainPS(); }
}
