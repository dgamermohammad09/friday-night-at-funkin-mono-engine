// Port of the ORIGINAL Friday Night Funkin' rain post-effect:
//   funkin.assets/preload/shaders/rain.frag  +  funkin/graphics/shaders/RuntimePostEffectShader.hx
// Applied to the game camera (world) only, exactly like FlxG.camera.filters = [ShaderFilter(rainShader)].
// Only the parts the original main() actually uses are ported (puddle / light code is commented out
// in the original). GLSL mod() is floor-based, so it is reimplemented (HLSL fmod truncates).

#if OPENGL
    #define PS_SHADERMODEL ps_3_0
#else
    #define PS_SHADERMODEL ps_4_0
#endif

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    MinFilter = Linear; MagFilter = Linear; MipFilter = Point;
    AddressU = Clamp; AddressV = Clamp;
};

float2 uScreenResolution;   // (FlxG.width, FlxG.height)
float4 uCameraBounds;       // (camera.viewLeft, viewTop, viewRight, viewBottom)
float  uScale;              // FlxG.height / 200
float  uIntensity;          // remapped song position: start..end intensity
float  uTime;               // starts at 1, += elapsed
float3 uRainColor;          // 0xFF6680cc

float  gmod(float x, float y)   { return x - y * floor(x / y); }
float2 gmod2(float2 x, float y) { return x - y * floor(x / y); }

float2 screenToWorld(float2 sc)
{
    float2 scale = float2(uCameraBounds.z - uCameraBounds.x, uCameraBounds.w - uCameraBounds.y);
    return sc * scale + uCameraBounds.xy;
}

float2 worldToScreen(float2 wc)
{
    float2 scale = float2(uCameraBounds.z - uCameraBounds.x, uCameraBounds.w - uCameraBounds.y);
    return (wc - uCameraBounds.xy) / scale;
}

float rand(float2 a)
{
    return frac(sin(dot(gmod2(a, 1000.0), float2(12.9898, 78.233))) * 43758.5453);
}

float rainDist(float2 p, float scale, float intensity)
{
    p *= 0.1;                                   // scale everything
    p.x += p.y * 0.1;                           // sheer
    p.y -= uTime * 500.0 / scale;               // scroll
    p.y *= 0.03;                                // expand Y
    float ix = floor(p.x);
    p.y += gmod(ix, 2.0) * 0.5 + (rand(float2(ix, ix)) - 0.5) * 0.3;   // shift Y
    float iy = floor(p.y);
    float2 index = float2(ix, iy);
    p -= index;                                 // mod
    p.x += (rand(index.yx) * 2.0 - 1.0) * 0.35; // shift X
    float2 a = abs(p - 0.5);                    // distance
    float res = max(a.x * 0.8, a.y * 0.5) - 0.1;
    bool empty = rand(index) < lerp(1.0, 0.1, intensity);   // decimate
    return empty ? 1.0 : res;
}

float4 MainPS(float4 pos : SV_POSITION, float4 col : COLOR0, float2 screenCoord : TEXCOORD0) : COLOR
{
    float2 wpos = screenToWorld(screenCoord);
    float intensity = uIntensity;

    float3 add = float3(0, 0, 0);
    float rainSum = 0.0;

    float scales[4] = { 1.0, 1.8, 2.6, 4.8 };
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float scale = scales[i];
        float r = rainDist(wpos * scale / uScale + 500.0 * (float)i, scale, intensity);
        if (r < 0.0)
        {
            float v = (1.0 - exp(r * 5.0)) / scale * 2.0;
            wpos.x += v * 10.0 * uScale;
            wpos.y -= v * 2.0 * uScale;
            add += float3(0.1, 0.15, 0.2) * v;
            rainSum += (1.0 - rainSum) * 0.75;
        }
    }

    float4 smp = tex2D(SpriteTextureSampler, worldToScreen(wpos));   // sampleBitmapWorld(wpos)
    float3 color = smp.xyz;
    color += add;
    color = lerp(color, uRainColor, 0.1 * rainSum);
    return float4(color, smp.w);
}

technique Rain
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
