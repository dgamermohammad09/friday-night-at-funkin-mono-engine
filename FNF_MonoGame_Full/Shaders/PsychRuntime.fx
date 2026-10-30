// Template effect for Psych Engine runtime .frag shaders (Engine/PsychEngine/RuntimeShader.cs).
// Only its parameter / sampler / constant-buffer layout is used: at runtime the pixel shader bytecode is replaced by the
// D3DCompile output of the GLSL->HLSL translation, which declares exactly the same resources:
//   t0/s0 = sprite texture (bitmap), t1..t3/s1..s3 = extra sampler2D uniforms, $Globals b0 = float4 _U[64] (uniforms).
Texture2D bitmapTex : register(t0);
sampler bitmapSamp : register(s0) = sampler_state { Texture = <bitmapTex>; };
Texture2D T1 : register(t1);
sampler S1 : register(s1) = sampler_state { Texture = <T1>; };
Texture2D T2 : register(t2);
sampler S2 : register(s2) = sampler_state { Texture = <T2>; };
Texture2D T3 : register(t3);
sampler S3 : register(s3) = sampler_state { Texture = <T3>; };
float4 _U[64];

float4 PSMain(float4 pos : SV_Position, float4 col : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 c = bitmapTex.Sample(bitmapSamp, uv) * col;
    c += T1.Sample(S1, uv) * _U[1] + T2.Sample(S2, uv) * _U[2] + T3.Sample(S3, uv) * _U[3];
    return c * _U[0] + _U[63];
}

technique Main
{
    pass P0
    {
        PixelShader = compile ps_4_0 PSMain();
    }
}
