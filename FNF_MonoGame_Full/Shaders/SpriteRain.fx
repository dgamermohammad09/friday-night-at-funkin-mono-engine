// Original rain.frag in sprite mode (spookyMansionErect.hxc: rainShader on bgTrees, scale = FlxG.height/200*2,
// intensity 0.4, spriteMode = true; uCameraBounds/uScreenResolution stay at their defaults (0,0,1280,720)).
//   wpos = screenToWorld(screenToFrame(uv)); 4 rain layers distort wpos; color = texture(uv + worldToScreen(wpos - orig))
//   color += add; color = mix(color, uRainColor, 0.1 * rainSum); gl_FragColor = vec4(color, alpha)  (premultiplied output)
// Drawn with BlendState.AlphaBlend (premultiplied), the engine textures are straight alpha -> premultiply the sample.

sampler2D TextureSampler : register(s0);

float4 uFrameBounds;   // frame.uv (left, top, right, bottom)
float  uScale;
float  uIntensity;
float  uTime;
float3 uRainColor;

float  gmod(float x, float y)   { return x - y * floor(x / y); }
float2 gmod2(float2 x, float y) { return x - y * floor(x / y); }

float rand(float2 a)
{
    return frac(sin(dot(gmod2(a, 1000.0), float2(12.9898, 78.233))) * 43758.5453);
}

float rainDist(float2 p, float scale, float intensity)
{
    p *= 0.1;
    p.x += p.y * 0.1;
    p.y -= uTime * 500.0 / scale;
    p.y *= 0.03;
    float ix = floor(p.x);
    p.y += gmod(ix, 2.0) * 0.5 + (rand(float2(ix, ix)) - 0.5) * 0.3;
    float iy = floor(p.y);
    float2 index = float2(ix, iy);
    p -= index;
    p.x += (rand(index.yx) * 2.0 - 1.0) * 0.35;
    float2 a = abs(p - 0.5);
    float res = max(a.x * 0.8, a.y * 0.5) - 0.1;
    bool empty = rand(index) < lerp(1.0, 0.1, intensity);
    return empty ? 1.0 : res;
}

float4 MainPS(float4 pos : SV_POSITION, float4 col : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float2 lt = uFrameBounds.xy, rb = uFrameBounds.zw;
    float2 fc = (clamp(uv, lt, rb) - lt) / (rb - lt);   // screenToFrame
    float2 res = float2(1280.0, 720.0);
    float2 wpos = fc * res;                              // screenToWorld with default camera bounds
    float2 orig = wpos;
    float3 add = float3(0, 0, 0);
    float rainSum = 0.0;
    float scales[4] = { 1.0, 1.8, 2.6, 4.8 };
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float scale = scales[i];
        float r = rainDist(wpos * scale / uScale + 500.0 * (float)i, scale, uIntensity);
        if (r < 0.0)
        {
            float v = (1.0 - exp(r * 5.0)) / scale * 2.0;
            wpos.x += v * 10.0 * uScale;
            wpos.y -= v * 2.0 * uScale;
            add += float3(0.1, 0.15, 0.2) * v;
            rainSum += (1.0 - rainSum) * 0.75;
        }
    }
    float4 s = tex2D(TextureSampler, uv + (wpos - orig) / res);
    float3 color = s.rgb * s.a + add;
    color = lerp(color, uRainColor, 0.1 * rainSum);
    return float4(color, s.a) * col.a;
}

technique SpookyRain
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 MainPS();
    }
}
