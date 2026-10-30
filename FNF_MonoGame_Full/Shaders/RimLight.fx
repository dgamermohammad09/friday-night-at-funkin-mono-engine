// RimLight.fx - faithful MonoGame/HLSL port of the ORIGINAL Friday Night Funkin' (V-Slice 0.8.1)
//   FNF_Official/source/funkin/graphics/shaders/DropShadowShader.hx  (GLSL lines 275-453)
// Adjust Color (HSBC) + Animate-style drop shadow used as rim light.
//
// Coordinate model
// ----------------
// The original shader runs in "bitmap" UV space of whatever OpenFL texture it is drawn from:
//   * Sparrow/packer sprites: the whole atlas bitmap (openfl_TextureSize = atlas size,
//     uFrameBounds = frame.uv = frameRect / atlasSize).
//   * Animate-atlas sprites (useRenderTexture): a RenderTexture of size ceil(timeline bounds)
//     (openfl_TextureSize = RT size, uFrameBounds = (0,0,boundsW,boundsH) IN PIXELS - an upstream
//     quirk of FlxAnimateController.updateTimelineBounds; reproduce it, do not "fix" it).
// The port may store that logical bitmap as a sub-rectangle ("cell") of a bigger texture
// (composite atlas cells). uLogicalRect maps logical UV [0,1]^2 -> physical UV in the bound texture:
//   physical = uLogicalRect.xy + logical * (uLogicalRect.zw - uLogicalRect.xy)
// For Sparrow sprites drawn straight from the atlas use uLogicalRect = (0,0,1,1) (identity).
// All other uniforms (uFrameBounds, uTextureSize, mask UVs) are in LOGICAL space, exactly like the original.
// Logical samples are clamped to texel centres of the logical bitmap = GL CLAMP_TO_EDGE of the original,
// so neighbouring composite cells never bleed in.

#if OPENGL
    #define PS_SHADERMODEL ps_3_0
#else
    #define PS_SHADERMODEL ps_4_0
#endif

// ---- main texture (SpriteBatch binds it to slot 0; filter/address come from SpriteBatch.Begin's
//      SamplerState, same as AdjustColor.fx: use PointClamp for antialiasing=false (pixel) sprites,
//      LinearClamp otherwise - the original uses the sprite's smoothing flag) ----
sampler2D SpriteTextureSampler : register(s0);

// ---- alternate mask (blue channel). OpenFL ShaderInput defaults: NEAREST + CLAMP ----
Texture2D AltMask;
sampler2D AltMaskSampler : register(s1) = sampler_state
{
    Texture = <AltMask>;
    MinFilter = Point; MagFilter = Point; MipFilter = Point;
    AddressU = Clamp; AddressV = Clamp;
};

// ---- original uniforms ----
float4 uFrameBounds;      // (left, top, right, bottom) logical UV; see header
float  ang;               // radians (angle * PI/180)
float  dist;              // logical texels
float  str;               // strength
float  thr;               // threshold
float  angOffset;         // radians (frame.angle * PI/180): 0, or -PI/2 for rotated Sparrow frames
float  useMask;           // 0/1 (GLSL bool)
float  thr2;              // maskThreshold
float3 dropColor;         // rgb 0..1
float  hue;
float  saturation;
float  brightness;
float  contrast;
float  AA_STAGES;         // antialiasAmt (0 = off, default 2), max 8 like the original MAX_AA

// ---- port-side uniforms ----
float2 uTextureSize;          // openfl_TextureSize: logical bitmap size in px (atlas size / RT size)
float4 uLogicalRect = float4(0, 0, 1, 1); // logical->physical UV mapping (see header)
float4 uValidRect   = float4(0, 0, 1, 1); // physical UV rect holding real texels (composite cell rect); outside = transparent
float  uInputPremultiplied = 0;   // 1 if the texture holds premultiplied colour (OpenFL does); 0 = straight
float  uOutputPremultiplied = 0;  // 1 -> return (rgb*a, a) for BlendState.AlphaBlend (exact OpenFL output)
                                  // 0 -> return (rgb, a) straight for BlendState.NonPremultiplied
float  uApplyVertexColor = 0;     // original IGNORES sprite alpha/colour (writes gl_FragColor directly). 0 = faithful.

static const float3 grayscaleValues = float3(0.3098039215686275, 0.607843137254902, 0.0823529411764706);
static const float e = 2.718281828459045;
static const int MAX_AA = 8;
static const float AA_JITTER = 0.5;

// GLSL fract == x - floor(x) == HLSL frac
float2 hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float2 toPhysical(float2 L)
{
    // clamp-to-edge of the logical bitmap (texel centres)
    float2 halfTexel = 0.5 / uTextureSize;
    L = clamp(L, halfTexel, 1.0 - halfTexel);
    return uLogicalRect.xy + L * (uLogicalRect.zw - uLogicalRect.xy);
}

// texture2D(bitmap, L) of the original: returns PREMULTIPLIED colour like OpenFL
float4 sampleBitmap(float2 L)
{
    float2 P = toPhysical(L);
    // texels of the logical bitmap that do not exist in the bound texture (outside the composite cell)
    // are transparent in the original RenderTexture -> return 0 instead of reading a neighbouring cell
    if (P.x < uValidRect.x || P.y < uValidRect.y || P.x > uValidRect.z || P.y > uValidRect.w)
        return float4(0, 0, 0, 0);
    float4 c = tex2Dlod(SpriteTextureSampler, float4(P, 0, 0));
    if (uInputPremultiplied < 0.5) c.rgb *= c.a;
    return c;
}

float sampleMaskB(float2 L)
{
    return tex2Dlod(AltMaskSampler, float4(L, 0, 0)).b;
}

float3 applyHueRotate(float3 c, float aHue)
{
    float angle = radians(aHue);
    float ca = cos(angle);
    float sa = sin(angle);
    // GLSL mat3 column-major: m * c = col0*c.r + col1*c.g + col2*c.b
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

float3 applyHSBCEffect(float3 c)
{
    c = c + (brightness / 255.0);          // brightness
    c = applyHueRotate(c, hue);            // hue
    c = applyContrast(c, contrast);        // contrast
    c = applySaturation(c, saturation);    // saturation
    return c;
}

// curThreshold is unused in the original as well (it reads the thr/thr2 uniforms)
float intensityPass(float2 L)
{
    float4 col = sampleBitmap(L);

    float maskIntensity = 0.0;
    if (useMask > 0.5)
        maskIntensity = lerp(0.0, 1.0, sampleMaskB(L));

    if (col.a == 0.0)
        return 0.0;

    float intensity = dot(col.rgb, float3(0.3098, 0.6078, 0.0823));
    intensity = maskIntensity > 0.0 ? (intensity > thr2 ? 1.0 : 0.0) : (intensity > thr ? 1.0 : 0.0);
    return intensity;
}

float antialias(float2 L)
{
    float color = intensityPass(L);
    // AA_STAGES == 0: loops below run 0 times and AA_TOTAL_PASSES == 1 -> identical to the original early return
    float AA_TOTAL_PASSES = AA_STAGES * AA_STAGES + 1.0;

    // original: i in [0,64), x = i/8, y = i%8, skip x>=AA || y>=AA  ==  x,y in [0,AA)
    [loop]
    for (int x = 0; x < MAX_AA; x++)
    {
        if ((float)x >= AA_STAGES) break;
        [loop]
        for (int y = 0; y < MAX_AA; y++)
        {
            if ((float)y >= AA_STAGES) break;
            float2 offset = AA_JITTER * (2.0 * hash22(float2((float)x, (float)y)) - 1.0) / uTextureSize;
            color += intensityPass(L + offset);
        }
    }
    return color / AA_TOTAL_PASSES;
}

float3 createDropShadow(float3 col, float2 L)
{
    float intensity = antialias(L);

    float2 imageRatio = float2(1.0 / uTextureSize.x, 1.0 / uTextureSize.y);

    float2 checkedPixel = float2(L.x + (dist * cos(ang + angOffset) * imageRatio.x),
                                 L.y - (dist * sin(ang + angOffset) * imageRatio.y));

    float dropShadowAmount = 0.0;
    if (checkedPixel.x > uFrameBounds.x && checkedPixel.y > uFrameBounds.y &&
        checkedPixel.x < uFrameBounds.z && checkedPixel.y < uFrameBounds.w)
    {
        dropShadowAmount = sampleBitmap(checkedPixel).a;
    }

    col.rgb += dropColor.rgb * ((1.0 - (dropShadowAmount * str)) * intensity);
    return col;
}

float4 MainPS(float4 position : SV_POSITION, float4 vcolor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    // physical uv -> logical uv (inverse of uLogicalRect)
    float2 L = (uv - uLogicalRect.xy) / (uLogicalRect.zw - uLogicalRect.xy);

    float4 col = sampleBitmap(L);   // premultiplied, like OpenFL

    float3 unpremultipliedColor = col.a > 0.0 ? col.rgb / col.a : col.rgb;
    float3 outColor = applyHSBCEffect(unpremultipliedColor);
    outColor = createDropShadow(outColor, L);

    // original: gl_FragColor = vec4(outColor.rgb * col.a, col.a)  (unclamped; RT write clamps)
    float4 premul = float4(outColor * col.a, col.a);
    if (uApplyVertexColor > 0.5)
        premul = float4(premul.rgb * vcolor.rgb * vcolor.a, premul.a * vcolor.a);

    if (uOutputPremultiplied > 0.5)
        return premul;
    // straight alpha for BlendState.NonPremultiplied
    return float4(premul.a > 0.0 ? premul.rgb / premul.a : premul.rgb, premul.a);
}

technique RimLight
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
