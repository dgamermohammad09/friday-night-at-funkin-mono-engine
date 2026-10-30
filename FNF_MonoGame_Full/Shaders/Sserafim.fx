// Port of funkin.graphics.shaders.SserafimShader (V-Slice 0.8.1, source/funkin/graphics/shaders/SserafimShader.hx).
// Same math and order: Adjust Color (brightness -> hue -> contrast -> saturation), then the darken / truck light /
// pulse light multiply. The original samples `texture2D(bitmap, ...)` directly (not flixel_texture2D), so the sprite's
// alpha and colour transform are ignored: the vertex colour is NOT applied here either.
// Textures in this engine are straight alpha (the original un-premultiplies first), output is straight alpha.

sampler2D TextureSampler : register(s0);

float hue;
float saturation;
float brightness;
float contrast;

float darkAmt;
float3 lightColor;
float pulseStrength;
float truckStrength;
float isChar;

static const float3 grayscaleValues = float3(0.3098039215686275, 0.607843137254902, 0.0823529411764706);
static const float e = 2.718281828459045;

float3 applyHueRotate(float3 c, float aHue)
{
    float angle = radians(aHue);
    float ca = cos(angle);
    float sa = sin(angle);
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

float3 hue2rgb(float h)
{
    h = frac(h);
    return saturate(float3(abs(h * 6. - 3.) - 1., 2. - abs(h * 6. - 2.), 2. - abs(h * 6. - 4.)));
}

float3 rgb2hsl(float3 c)
{
    float cMin = min(min(c.r, c.g), c.b);
    float cMax = max(max(c.r, c.g), c.b);
    float delta = cMax - cMin;
    float3 hsl = float3(0., 0., (cMax + cMin) / 2.);
    if (delta != 0.0)
    {
        if (hsl.z < .5) hsl.y = delta / (cMax + cMin);
        else hsl.y = delta / (2. - cMax - cMin);
        float deltaR = (((cMax - c.r) / 6.) + (delta / 2.)) / delta;
        float deltaG = (((cMax - c.g) / 6.) + (delta / 2.)) / delta;
        float deltaB = (((cMax - c.b) / 6.) + (delta / 2.)) / delta;
        if (c.r == cMax) hsl.x = deltaB - deltaG;
        else if (c.g == cMax) hsl.x = (1. / 3.) + deltaR - deltaB;
        else hsl.x = (2. / 3.) + deltaG - deltaR;
        hsl.x = frac(hsl.x);
    }
    return hsl;
}

float3 hsl2rgb(float3 hsl)
{
    if (hsl.y == 0.) return hsl.zzz;
    float b;
    if (hsl.z < .5) b = hsl.z * (1. + hsl.y);
    else b = hsl.z + hsl.y - hsl.y * hsl.z;
    float a = 2. * hsl.z - b;
    return a + hue2rgb(hsl.x) * (b - a);
}

static const float lightMultiplier = 0.07;

float4 MainPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 col = tex2D(TextureSampler, uv);
    float3 outColor = col.rgb;
    outColor = outColor + (brightness / 255.0);
    outColor = applyHueRotate(outColor, hue);
    outColor = applyContrast(outColor, contrast);
    outColor = applySaturation(outColor, saturation);

    float combinedAlpha = truckStrength + pulseStrength;
    float darkFactor = (combinedAlpha * lightMultiplier) + darkAmt;
    if (darkAmt > 0.65) darkFactor = (combinedAlpha * (-1.0 * lightMultiplier)) + darkAmt;

    float3 bruhColor = rgb2hsl(lightColor);
    bruhColor.z = bruhColor.z * pulseStrength;
    bruhColor = hsl2rgb(bruhColor);

    float3 multiplyColor;
    if (isChar > 0.5)
    {
        multiplyColor = lerp(float3(1.0, 1.0, 1.0), float3(0.0, 0.0, 0.0), darkFactor / 5.0);
        multiplyColor = lerp(multiplyColor, bruhColor, darkFactor / 3.0);
    }
    else
    {
        multiplyColor = lerp(float3(1.0, 1.0, 1.0), float3(0.0, 0.0, 0.0), darkFactor);
        multiplyColor = lerp(multiplyColor, bruhColor, darkFactor / 2.0);
    }
    outColor *= multiplyColor;
    return float4(outColor, col.a);
}

// Straight-alpha texture * vertex colour -> premultiplied output (used with a SCREEN blend state: One, InvSrcColor).
float4 PremulPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(TextureSampler, uv) * color;
    return float4(c.rgb * c.a, c.a);
}

technique Premultiply
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 PremulPS();
    }
}

technique Sserafim
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_3 MainPS();
    }
}
