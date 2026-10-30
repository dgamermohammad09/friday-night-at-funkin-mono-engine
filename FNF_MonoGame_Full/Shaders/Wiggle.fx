// Port of assets/preload/shaders/wiggle.frag (WiggleEffectRuntime), DREAMY effect only (the only type used by
// schoolEvil.hxc / schoolEvilErect.hxc). openfl_TextureSize = size of the bitmap being sampled (the prop texture).
// The original samples texture2D(bitmap, uv) without the colour transform; the props are opaque, alpha 1.

sampler2D TextureSampler : register(s0);

float uTime;
float uSpeed;
float uFrequency;
float uWaveAmplitude;
float2 uTextureSize;

float4 MainPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float2 pt = uv;
    float w = 1.0 / uTextureSize.y;
    float h = 1.0 / uTextureSize.x;

    pt.x = floor(pt.x / h) * h;
    float offsetX = sin(pt.x * uFrequency + uTime * uSpeed) * uWaveAmplitude;
    pt.y += floor(offsetX / w) * w;
    pt.y = floor(pt.y / w) * w;
    float offsetY = sin(pt.y * (uFrequency / 2.0) + uTime * (uSpeed / 2.0)) * (uWaveAmplitude / 2.0);
    pt.x += floor(offsetY / h) * h;

    // GL nearest sampling of an exact texel corner picks texel floor(u * size); sample its centre explicitly
    // so D3D's point sampler cannot round the other way.
    pt = (floor(pt * uTextureSize + 0.001) + 0.5) / uTextureSize;
    return tex2D(TextureSampler, pt) * color;
}

technique Wiggle
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 MainPS();
    }
}
