// Port of the original FNF shader assets/preload/shaders/building.frag (BuildingEffectShader, philly window lights):
//   vec4 color = flixel_texture2D(bitmap, uv); if (color.a > 0.0) color -= alphaShit; gl_FragColor = color;
// OpenFL works in premultiplied alpha; this engine's world batch is straight alpha (NonPremultiplied), so
// premultiply, subtract, clamp (8-bit target), then un-premultiply for the NonPremultiplied blend.

sampler2D TextureSampler : register(s0);

float alphaShit;

float4 MainPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 tex = tex2D(TextureSampler, uv) * color;      // straight rgb * tint, alpha
    float4 p = float4(tex.rgb * tex.a, tex.a);           // premultiplied (flixel_texture2D)
    if (p.a > 0.0) p -= alphaShit;
    p = saturate(p);
    if (p.a <= 0.0) return float4(0, 0, 0, 0);
    return float4(p.rgb / p.a, p.a);
}

technique BuildingEffect
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 MainPS();
    }
}
