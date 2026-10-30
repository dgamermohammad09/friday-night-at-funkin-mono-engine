using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// backcard-pico.hxc (PicoCard) idle layers, ported literally:
///  - scrollBack / scrollLower / scrollTop / scrollMiddle are FlxBackdrops (repeat X, spacing 20/20/20/15) whose
///    velocity runs from the moment the card is created (they are only hidden until introDone).
///  - scrollTop plays non-looping 24 fps 'xxx info' animations chosen in update() from
///    scrollProgress = |scrollTop.x % (frameWidth + 20)| once the current animation has finished.
///  - glow (ADD) and glowDark (MULTIPLY) are drawn at alpha 1: PicoCard.beatHit() is never called in 0.8.1
///    (BackingCard.onBeatHit is empty), verified on the running original (glow.alpha = glowDark.alpha = 1 always).
///  - blueBar MULTIPLY alpha 0.4, scrollBack alpha 0.39 flipX.
/// OpenFL blends premultiplied colours: MULTIPLY = (DST_COLOR, ONE_MINUS_SRC_ALPHA) needs premultiplied texels.
/// </summary>
public partial class FreeplayScene
{
    private bool _picoTopFinished;
    private float _picoTopFrameTimer;
    private Texture2D _picoGlowPM, _picoBlueBarPM;
    private bool _picoCardHidden;   // PicoCard.disappear(): layers invisible
    // dev hook FNF_PICO_HIDE_AT="t1,t2,.." (scene s): hides glow, glowDark, blueBar, scrollBack, scrollLower, scrollMiddle, scrollTop in turn
    private static readonly float[] _devPicoHideAt = (Environment.GetEnvironmentVariable("FNF_PICO_HIDE_AT") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    private bool PicoLayerOn(int layer) => !(layer < _devPicoHideAt.Length && DevSceneElapsed >= _devPicoHideAt[layer]);

    /// <summary>OpenFL MULTIPLY: src(premultiplied) * dst + dst * (1 - srcA).</summary>
    private static readonly BlendState OpenFlMultiply = new BlendState
    {
        ColorBlendFunction = BlendFunction.Add,
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha
    };

    internal static Texture2D PremultipliedCopy(Texture2D src)
    {
        if (src == null) return src;
        var data = new Color[src.Width * src.Height];
        src.GetData(data);
        for (int i = 0; i < data.Length; i++)
        {
            var c = data[i];
            data[i] = new Color((byte)((c.R * c.A + 127) / 255), (byte)((c.G * c.A + 127) / 255), (byte)((c.B * c.A + 127) / 255), c.A);
        }
        var t = new Texture2D(src.GraphicsDevice, src.Width, src.Height);
        t.SetData(data);
        return t;
    }

    private static bool InBounds(float v, float min, float max) => v >= min && v <= max;   // FlxMath.inBounds

    private void PicoTopPlay(string anim)
    {
        // FlxAnimation.play(Force=true): _frameTimer = 0, finished = false, curFrame = 0
        _picoTopLoopAnimName = anim;
        _picoTopLoopFrameIndex = 0;
        _picoTopFrameTimer = 0f;
        _picoTopFinished = false;
    }

    /// <summary>PicoCard.update (group update first: velocity motion + scrollTop animation, then the script logic).</summary>
    private void PicoCardUpdate(float dt)
    {
        if (!_isPicoTheme) return;

        // FlxObject.updateMotion (no acceleration/drag): x += velocity * elapsed
        float vm = CsVelMul; // enterCharSel: velocity.x -> 0 over 0.8 s sineIn
        _picoScrollBackX += 110f * vm * dt;
        _picoScrollLowerX += -110f * vm * dt;
        _picoScrollTopX += -220f * vm * dt;
        _picoScrollMiddleX += 220f * vm * dt;

        // FlxAnimation.update (24 fps, not looped)
        var frames = _picoTopLoopSheet?.GetAnimation(_picoTopLoopAnimName);
        int n = frames?.Count ?? 1;
        if (!_picoTopFinished)
        {
            const float dur = 1f / 24f;
            _picoTopFrameTimer += dt;
            while (_picoTopFrameTimer > dur && !_picoTopFinished)
            {
                _picoTopFrameTimer -= dur;
                if (_picoTopLoopFrameIndex + 1 > n - 1) _picoTopFinished = true;   // set_curFrame clamps + finished
                else _picoTopLoopFrameIndex++;
            }
        }

        if (_picoTopLoopFrameWidth <= 0) return;
        float scrollProgress = Math.Abs(_picoScrollTopX % (_picoTopLoopFrameWidth + 20));
        if (_picoTopFinished)
        {
            if (InBounds(scrollProgress, 500, 700) && _picoTopLoopAnimName != "sniper info") PicoTopPlay("sniper info");
            if (InBounds(scrollProgress, 700, 1300) && _picoTopLoopAnimName != "rifle info") PicoTopPlay("rifle info");
            if (InBounds(scrollProgress, 1450, 2000) && _picoTopLoopAnimName != "rocket launcher info") PicoTopPlay("rocket launcher info");
            if (InBounds(scrollProgress, 0, 300) && _picoTopLoopAnimName != "uzi info") PicoTopPlay("uzi info");
        }
    }

    /// <summary>FlxBackdrop.drawComplex, repeatAxes X, camera at 0 with 1280 view.</summary>
    private static void DrawBackdropX(SpriteBatch sb, Texture2D tex, Rectangle? src, float x, float y,
        int frameWidth, float spacing, Color color, bool flipX)
    {
        float period = frameWidth + spacing;
        // left = modMin(bounds.right, period, viewLeft) - bounds.width
        float right = x + frameWidth;
        float left = right - MathF.Floor(right / period) * period - frameWidth;
        for (float tx = left; tx < FNFGame.SCREEN_WIDTH; tx += period)
            sb.Draw(tex, new Vector2(tx, y), src, color, 0f, Vector2.Zero, 1f,
                flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }

    /// <summary>PicoCard layers in add() order: scrollBack, scrollLower, blueBar, scrollTop, scrollMiddle, glowDark, glow.</summary>
    private void DrawPicoCardLayers(SpriteBatch sb)
    {
        if (!_isPicoTheme || !_introDone || _picoCardHidden) return;

        if (_picoLowerLoop != null && _picoLowerLoop != Assets.Pixel)
        {
            if (PicoLayerOn(3)) DrawBackdropX(sb, _picoLowerLoop, null, _picoScrollBackX, 200f, _picoLowerLoop.Width, 20f, new Color(Color.White, 0.39f), true);
            if (PicoLayerOn(4)) DrawBackdropX(sb, _picoLowerLoop, null, _picoScrollLowerX, 406f, _picoLowerLoop.Width, 20f, Color.White, false);
        }

        _picoBlueBarPM ??= PremultipliedCopy(_picoBlueBar);
        _picoGlowPM ??= PremultipliedCopy(_picoGlow);

        if (_picoBlueBarPM != null && _picoBlueBarPM != Assets.Pixel && PicoLayerOn(2))
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: OpenFlMultiply);
            sb.Draw(_picoBlueBarPM, new Vector2(0f, 239f), Color.White * 0.4f);   // premultiplied vertex colour
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        }

        var frame = GetPicoTopLoopFrame();
        if (frame != null && _picoTopLoopSheet?.Texture != null && PicoLayerOn(6))
            DrawBackdropX(sb, _picoTopLoopSheet.Texture, frame.SourceRect, _picoScrollTopX + frame.Offset.X, 80f + frame.Offset.Y,
                _picoTopLoopFrameWidth, 20f, Color.White, false);

        if (_picoMiddleLoop != null && _picoMiddleLoop != Assets.Pixel && PicoLayerOn(5))
            DrawBackdropX(sb, _picoMiddleLoop, null, _picoScrollMiddleX, 346f, _picoMiddleLoop.Width, 15f, Color.White, false);

        float glowX = CutoutWidth * DJ_POS_MULTI - 300f;
        if (_picoGlowPM != null && _picoGlowPM != Assets.Pixel)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: OpenFlMultiply);
            if (PicoLayerOn(1)) sb.Draw(_picoGlowPM, new Vector2(glowX, 330f), Color.White);          // glowDark, MULTIPLY
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Additive);
            if (PicoLayerOn(0)) sb.Draw(_picoGlow, new Vector2(glowX, 330f), Color.White);            // glow, ADD
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        }
    }

    private void DisposePicoCardExtras()
    {
        if (_picoGlowPM != null && _picoGlowPM != _picoGlow) _picoGlowPM.Dispose();
        if (_picoBlueBarPM != null && _picoBlueBarPM != _picoBlueBar) _picoBlueBarPM.Dispose();
        _picoGlowPM = _picoBlueBarPM = null;
    }

    public string DevPicoInfo => _isPicoTheme
        ? $"top={_picoScrollTopX:F2} anim={_picoTopLoopAnimName}@{_picoTopLoopFrameIndex} fin={_picoTopFinished} back={_picoScrollBackX:F2}"
        : "";
}
