using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;
using FNF_MonoGame.Gameplay;
using Note = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Weekend 1 "Blazin'" — port of the ORIGINAL scripts:
///   stages/phillyBlazin.hxc   (scrolling sky, rain shader + rainTimeScale, lightning, character colours, camera init/fade, popup offsets)
///   songs/blazin.hxc          (opponent strumline hidden, player strumline centred)
///   characters/pico-blazin.hxc + darnell-blazin.hxc (note-kind fight animations, zIndex swaps, camera shakes, danceEvery 0)
/// </summary>
public partial class PlayScene
{
    private bool _bzInit, _bzActive;
    private static readonly bool _bzLog = Environment.GetEnvironmentVariable("FNF_BZLOG") == "1";
    private string _bzLastBa = "", _bzLastDa = "";
    private float _bzDevLightningAt = float.TryParse(Environment.GetEnvironmentVariable("FNF_BZ_LIGHTNING_AT"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : -1f;
    private readonly Random _bzRng = new();
    private string _bzLastRating = "SICK!!";          // event.judgement of the player hit being dispatched
    private Vector2 _popUpOffsets = Vector2.Zero;     // PlayState.comboPopUps.offsets

    // stage state
    private StaticStageProp _bzSky;                   // FlxTiledSprite(skyBlur, 4000, 495, repeatX)
    private float _bzSkyScrollX;
    private int _bzSkyTileW = 1;
    private bool _bzCameraInitialized;
    private float _bzLightningTimer = 3.0f;
    private bool _bzLightningActive = true;
    private float _bzRainTimeScale = 1.0f;
    private Vector2 _bzLightningBasePos;              // sprite position for the JSON x (50)
    private bool _bzLightningBaseSet;

    // FlxTweens of applyLightning()
    private float _bzSkyAddT = -1f, _bzFgMulT = -1f, _bzLightenT = -1f, _bzColorT = -1f;
    private const float LIGHTNING_FULL_DURATION = 1.5f, LIGHTNING_FADE_DURATION = 0.3f;

    // camGame fx
    private float _bzFadeAlpha;                       // camGame.fade(black, 1.5, fadeIn)
    private float _bzShakeIntensity, _bzShakeDuration;
    private Vector2 _bzShakeOffset;

    // characters (pico-blazin / darnell-blazin scripts)
    private readonly int[] _bzOrder = { 0, 1, 2 };    // draw order of gf / dad / bf (stable zIndex sort)
    private bool _bzPicoAlternate, _bzDarnellAlternate, _bzPicoCantUppercut, _bzDarnellCantUppercut;

    private static readonly Color BzCharColor = new(0xDE, 0xDE, 0xDE);
    private static readonly Color BzGfColor = new(0x88, 0x88, 0x88);
    private static readonly Color BzFlashColor = new(0x60, 0x60, 0x60);

    private bool IsChar(Character c, string id) => c != null
        && (string.Equals(c.Name, id, StringComparison.OrdinalIgnoreCase) || string.Equals(c.OriginalName, id, StringComparison.OrdinalIgnoreCase));

    private StaticStageProp BzStatic(string name)
    {
        foreach (var p in _staticProps) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    private void BlazinInit()
    {
        _bzInit = true;
        _bzActive = string.Equals(_chart?.Stage, "phillyBlazin", StringComparison.OrdinalIgnoreCase);
        if (!_bzActive) return;

        // onCreate()
        _bzCameraInitialized = false;
        _bzLightningActive = true;
        _bzLightningTimer = 3.0f;
        _bzRainTimeScale = 1.0f;

        // buildStage(): blends + hidden lightning props
        foreach (var n in new[] { "skyAdditive", "foregroundMultiply", "additionalLighten" })
        {
            var p = BzStatic(n);
            if (p != null) { p.Visible = false; p.Alpha = 1f; }
        }
        BzSetLightningVisible(false);

        // scrollingSky = new FlxTiledSprite(Paths.image('phillyBlazin/skyBlur'), 4000, 495, true, false) at (-700,-120), scroll 0, z 10
        var skyTex = LoadStagePropTexture("phillyBlazin/skyBlur") ?? Assets.LoadTexture("game/stages/philly_blazin/skyBlur.png");
        if (skyTex != null && skyTex != Assets.Pixel)
        {
            int keep = _psSkyTileWidth;
            var tiled = MakeHorizontallyTiled(skyTex, 4000);
            _bzSkyTileW = _psSkyTileWidth; _psSkyTileWidth = keep;
            _bzSky = new StaticStageProp
            {
                Texture = tiled, Name = "scrollingSky", ScaleX = 1f, ScaleY = 1f, Alpha = 1f,
                ScrollX = 0f, ScrollY = 0f, ZIndex = 10, X = -700f, Y = -120f,
                SourceRect = new Rectangle(0, 0, 4000, 495),
            };
            _staticProps.Add(_bzSky);
            SortStaticProps();
        }

        // blazin.hxc onUpdate (first frame): hideOpponentStrumline() + centerPlayerStrumline()
        _middlescroll = true;

    }

    private void BzSetLightningVisible(bool v)
    {
        int i = FindAnimatedPropIndex("lightning");
        if (i < 0) return;
        var p = _animatedProps[i];
        if (!_bzLightningBaseSet && p.Sprite != null) { _bzLightningBasePos = p.Sprite.Position; _bzLightningBaseSet = true; }
        p.Alpha = v ? 1f : 0f;
        _animatedProps[i] = p;
    }

    private void BzSetAbotColor(Color c)
    {
        int i = FindAnimatedPropIndex("neneSpeakerRig");
        if (i < 0) return;
        var p = _animatedProps[i]; p.Color = c; _animatedProps[i] = p;
    }

    private static float BzSmoothLerpPrecision(float b, float target, float dt, float duration, float precision = 1f / 100f)
    {
        if (dt == 0f) return b;
        if (b == target) return target;
        return target + (b - target) * MathF.Pow(precision, dt / duration);   // MathUtil.lerp(target, base, pow(precision, dt/duration))
    }

    /// <summary>phillyBlazin.hxc onUpdate (+ FlxTween / camera fx stepping).</summary>
    private void BlazinUpdate(float dt)
    {
        // buildStage()/onCreate run once the stage and characters exist (the loading phase also dispatches updates)
        if (!_bzInit && _boyfriend != null && _opponent != null && _girlfriend != null && !_playLoading) BlazinInit();
        if (!_bzActive || _gameOver) return;
        dt = Math.Min(dt, 0.1f);   // FlxG.elapsed (maxElapsed 0.1)

        // rainShader.update(elapsed * rainTimeScale); rainTimeScale = smoothLerpPrecision(rainTimeScale, 0.02, elapsed, 1.535)
        _rainTime += dt * _bzRainTimeScale;
        _bzRainTimeScale = BzSmoothLerpPrecision(_bzRainTimeScale, 0.02f, dt, 1.535f);

        // scrollingSky.scrollX -= FlxG.elapsed * 35
        if (_bzSky != null)
        {
            _bzSkyScrollX -= dt * 35f;
            int tile = Math.Max(1, _bzSkyTileW);
            int sx = (int)(((-_bzSkyScrollX) % tile + tile) % tile);
            _bzSky.SourceRect = new Rectangle(sx, 0, 4000, 495);
        }

        if (!_bzCameraInitialized)
        {
            _bzCameraInitialized = true;
            // character scripts (onCreate): danceEvery = 0, own note handling
            if (IsChar(_boyfriend, "pico-blazin")) _boyfriend.ScriptAnimOnly = true;
            if (IsChar(_opponent, "darnell-blazin")) _opponent.ScriptAnimOnly = true;
            Console.WriteLine($"[BLAZIN] scripts pico={_boyfriend.ScriptAnimOnly} darnell={_opponent.ScriptAnimOnly} bfZ={_bfZIndex} dadZ={_dadZIndex} gfZ={_gfZIndex}");
            BzRefresh();
            BzInitializeCamera();
            if (_boyfriend != null) _boyfriend.Tint = BzCharColor;
            if (_opponent != null) _opponent.Tint = BzCharColor;
            _girlfriend.Tint = BzGfColor;
            BzSetAbotColor(BzGfColor);
        }

        if (_bzLightningActive) _bzLightningTimer -= dt; else _bzLightningTimer = 1f;
        // dev hook: FNF_BZ_LIGHTNING_AT=<song s> [FNF_BZ_LIGHTNING_X=<x>] forces applyLightning() (comparison with the original)
        if (_bzDevLightningAt >= 0f && SongPositionSeconds >= _bzDevLightningAt)
        {
            _bzDevLightningAt = -1f;
            BzApplyLightning();
            if (float.TryParse(Environment.GetEnvironmentVariable("FNF_BZ_LIGHTNING_X"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fx))
            {
                int li2 = FindAnimatedPropIndex("lightning");
                if (li2 >= 0 && _animatedProps[li2].Sprite != null) _animatedProps[li2].Sprite.Position = new Vector2(_bzLightningBasePos.X + (fx - 50f), _bzLightningBasePos.Y);
            }
            _bzLightningTimer = 100f;
        }
        if (_bzLightningTimer <= 0f)
        {
            BzApplyLightning();
            _bzLightningTimer = 7f + (float)_bzRng.NextDouble() * 8f;   // FlxG.random.float(7, 15)
        }

        BzStepTweens(dt);
        if (_bzLog && SongPositionSeconds < 12.0)
        {
            string ba = _boyfriend?.CurrentAnimation, da = _opponent?.CurrentAnimation;
            if (ba != _bzLastBa || da != _bzLastDa) Console.WriteLine($"ANIMCHG t={SongPositionSeconds * 1000:F1} bf={ba} dad={da}");
            _bzLastBa = ba; _bzLastDa = da;
        }

        // camGame fade-in / shake
        if (_bzFadeAlpha > 0f) _bzFadeAlpha = Math.Max(0f, _bzFadeAlpha - dt / 1.5f);
        if (_bzShakeDuration > 0f)
        {
            _bzShakeDuration -= dt;
            if (_bzShakeDuration <= 0f) _bzShakeOffset = Vector2.Zero;
            else
            {
                float z = _defaultCamZoom * _cameraBopMultiplier;
                _bzShakeOffset = new Vector2(
                    ((float)_bzRng.NextDouble() * 2f - 1f) * _bzShakeIntensity * FNFGame.SCREEN_WIDTH * z,
                    ((float)_bzRng.NextDouble() * 2f - 1f) * _bzShakeIntensity * FNFGame.SCREEN_HEIGHT * z);
            }
        }
    }

    private void BzInitializeCamera()
    {
        // cameraFocusPoint = originalPosition + size/2 + character cameraOffsets + stage cameraOffsets
        var mid = _girlfriend.GetMidpoint();
        float ox = _girlfriend.CameraOffsets?.Length >= 2 ? _girlfriend.CameraOffsets[0] : 0f;
        float oy = _girlfriend.CameraOffsets?.Length >= 2 ? _girlfriend.CameraOffsets[1] : 0f;
        float xTarget = mid.X + ox + 50f;
        float yTarget = mid.Y + oy - 90f;
        _camWorldX = _camTargetWorldX = xTarget;    // cameraFollowPoint.setPosition + resetCamera()
        _camWorldY = _camTargetWorldY = yTarget;
        _popUpOffsets = new Vector2(480, -50);      // comboPopUps.offsets
        _bzFadeAlpha = 1f;                           // camGame.fade(0xFF000000, 1.5, true)
        Console.WriteLine($"[BLAZIN] camera init gfMid={mid} camOff=({ox},{oy}) target=({xTarget},{yTarget})");
    }

    private void BzCameraShake(float intensity, float duration)
    { if (!FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.CamShakes) return;
        _bzShakeIntensity = intensity; _bzShakeDuration = duration;
    }

    private void BzApplyLightning()
    {
        var sky = BzStatic("skyAdditive"); var fg = BzStatic("foregroundMultiply"); var add = BzStatic("additionalLighten");
        if (sky != null) { sky.Visible = true; sky.Alpha = 0.7f; }
        _bzSkyAddT = 0f;
        if (fg != null) { fg.Visible = true; fg.Alpha = 0.64f; }
        _bzFgMulT = 0f;
        if (add != null) { add.Visible = true; add.Alpha = 0.3f; }
        _bzLightenT = 0f;

        BzSetLightningVisible(true);
        PlayAnimatedProp("lightning", "strike");
        float lx = _bzRng.NextDouble() * 100.0 < 65 ? _bzRng.Next(-250, 281) : _bzRng.Next(780, 901);
        int li = FindAnimatedPropIndex("lightning");
        if (li >= 0 && _animatedProps[li].Sprite != null)
            _animatedProps[li].Sprite.Position = new Vector2(_bzLightningBasePos.X + (lx - 50f), _bzLightningBasePos.Y);

        // FlxTween.color(character, 0.3, 0xFF606060, target)
        _bzColorT = 0f;
        BzApplyColorTween(0f);

        Audio.PlaySound($"sounds/weekend1/Lightning{_bzRng.Next(1, 4)}", 1.0f);
        Console.WriteLine($"[BLAZIN] lightning x={lx} song={SongPositionSeconds:F3}");
    }

    private void BzApplyColorTween(float t)
    {
        Color Lerp(Color a, Color b) => new Color(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        if (_boyfriend != null) _boyfriend.Tint = Lerp(BzFlashColor, BzCharColor);
        if (_opponent != null) _opponent.Tint = Lerp(BzFlashColor, BzCharColor);
        if (_girlfriend != null) _girlfriend.Tint = Lerp(BzFlashColor, BzGfColor);
    }

    private void BzStepTweens(float dt)
    {
        var sky = BzStatic("skyAdditive"); var fg = BzStatic("foregroundMultiply"); var add = BzStatic("additionalLighten");
        if (_bzSkyAddT >= 0f)
        {
            _bzSkyAddT += dt;
            float k = Math.Min(1f, _bzSkyAddT / LIGHTNING_FULL_DURATION);
            if (sky != null) sky.Alpha = 0.7f * (1f - k);
            if (k >= 1f)
            {
                _bzSkyAddT = -1f;
                // cleanupLightning()
                if (sky != null) sky.Visible = false;
                if (fg != null) fg.Visible = false;
                if (add != null) add.Visible = false;
                BzSetLightningVisible(false);
            }
        }
        if (_bzFgMulT >= 0f)
        {
            _bzFgMulT += dt;
            float k = Math.Min(1f, _bzFgMulT / LIGHTNING_FULL_DURATION);
            if (fg != null) fg.Alpha = 0.64f * (1f - k);
            if (k >= 1f) _bzFgMulT = -1f;
        }
        if (_bzLightenT >= 0f)
        {
            _bzLightenT += dt;
            float k = Math.Min(1f, _bzLightenT / LIGHTNING_FADE_DURATION);
            if (add != null) add.Alpha = 0.3f * (1f - k);
            if (k >= 1f) _bzLightenT = -1f;
        }
        if (_bzColorT >= 0f)
        {
            _bzColorT += dt;
            float k = Math.Min(1f, _bzColorT / LIGHTNING_FADE_DURATION);
            BzApplyColorTween(k);
            if (k >= 1f) _bzColorT = -1f;
        }
    }

    // ------------------------------------------------------------------ drawing

    private static readonly BlendState BzMultiplyBlend = new()
    {
        // OpenFL MULTIPLY with alpha: dst * (src*a) + dst * (1 - a)
        ColorSourceBlend = Blend.DestinationColor, ColorDestinationBlend = Blend.InverseSourceAlpha, ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add,
    };
    private static readonly BlendState BzAddBlend = new()
    {
        ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.One, ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add,
    };

    /// <summary>skyAdditive / additionalLighten (blend ADD) and foregroundMultiply (blend MULTIPLY).</summary>
    private void BzDrawBlendProp(SpriteBatch spriteBatch, StaticStageProp prop)
    {
        if (prop?.Texture == null || !prop.Visible) return;
        bool multiply = prop.Name == "foregroundMultiply";
        float a = Math.Clamp(prop.Alpha, 0f, 1f);
        spriteBatch.End();
        spriteBatch.Begin(samplerState: _worldSampler, blendState: multiply ? BzMultiplyBlend : BzAddBlend,
            transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
        var screenPos = WorldToScreen(prop.X, prop.Y, prop.ScrollX, prop.ScrollY);
        // multiply: premultiplied tint (src*a, alpha a); add: straight tint (alpha a, applied once by SourceAlpha)
        Color tint = multiply ? new Color(a, a, a, a) : new Color(1f, 1f, 1f, a);
        spriteBatch.Draw(prop.Texture, screenPos, prop.SourceRect, tint, 0f, Vector2.Zero, new Vector2(prop.ScaleX, prop.ScaleY), SpriteEffects.None, 0f);
        spriteBatch.End();
        spriteBatch.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
    }

    /// <summary>camGame effects drawn into the camera image (fade), before the rain filter.</summary>
    private void BlazinDrawCameraFx(SpriteBatch spriteBatch)
    {
        if (!_bzActive || _bzFadeAlpha <= 0f) return;
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(0, 0, 0, (int)(_bzFadeAlpha * 255f)));
        spriteBatch.End();
    }

    private int[] BzCharDrawOrder(int[] charZ)
    {
        if (!_bzActive) return new[] { 0, 1, 2 }.OrderBy(i => charZ[i]).ToArray();   // Stage.refresh: stable sort by zIndex (a mod stage may put dad below gf)
        return _bzOrder;
    }

    /// <summary>Stage.refresh(): stable sort by zIndex.</summary>
    private void BzRefresh()
    {
        int Z(int ci) => ci == 0 ? _gfZIndex : ci == 1 ? _dadZIndex : _bfZIndex;
        for (int i = 1; i < 3; i++)
        {
            int v = _bzOrder[i], j = i - 1;
            while (j >= 0 && Z(_bzOrder[j]) > Z(v)) { _bzOrder[j + 1] = _bzOrder[j]; j--; }
            _bzOrder[j + 1] = v;
        }
    }

    // ------------------------------------------------------------------ characters

    private static bool BzKind(Note note, out string kind)
    {
        kind = note?.Kind ?? "";
        return kind.StartsWith("weekend-1-", StringComparison.Ordinal);
    }
    private bool BzPoorHit() => _bzLastRating == "BAD" || _bzLastRating == "SHIT";
    private bool BzLowHealth() => _health <= 0.30f * 2.0f;

    /// <summary>onNoteHit dispatched to the stage + characters (opponent first, like the original).</summary>
    private void BlazinNoteHit(Note note, bool isPlayer)
    {
        if (!_bzActive || !_bzCameraInitialized) return;
        if (!isPlayer) _bzLastRating = "perfect";  // opponent hits: HitNoteScriptEvent(note, 0, 0, 'perfect', ...)
        if (_opponent != null && _opponent.ScriptAnimOnly) DarnellNoteHit(note);
        if (_boyfriend != null && _boyfriend.ScriptAnimOnly) PicoNoteHit(note);
        _bzRainTimeScale += 0.7f;   // phillyBlazin.hxc onNoteHit
    }

    private void BlazinNoteMiss(Note note)
    {
        if (!_bzActive || !_bzCameraInitialized) return;
        float healthChange = -(4.0f / 100f * HEALTH_MAX);
        if (_opponent != null && _opponent.ScriptAnimOnly) DarnellNoteMiss(note, healthChange);
        if (_boyfriend != null && _boyfriend.ScriptAnimOnly) PicoNoteMiss(note, healthChange);
    }

    private void BlazinGhostMiss()
    {
        if (!_bzActive || !_bzCameraInitialized) return;
        bool lethal = _health - (4.0f / 100f * HEALTH_MAX) <= 0f;
        if (_opponent != null && _opponent.ScriptAnimOnly)
        {
            if (lethal) DnPunchLow();
            else if (_bzRng.NextDouble() * 100.0 < 50) DnDodge(); else DnBlock();
        }
        if (_boyfriend != null && _boyfriend.ScriptAnimOnly)
        {
            if (lethal) PcHitLow(); else PcPunchHigh();
        }
    }

    // ---- darnell-blazin.hxc
    private void DnPlay(string anim, bool restart = true) => _opponent.PlayAnimation(anim, force: restart || _opponent.CurrentAnimation != anim);
    private void DnBack() { _dadZIndex = 2000; BzRefresh(); }
    private void DnFront() { _dadZIndex = 3000; BzRefresh(); }
    private string DnAlt() { _bzDarnellAlternate = !_bzDarnellAlternate; return _bzDarnellAlternate ? "1" : "2"; }
    private void DnBlock() { DnPlay("block"); BzCameraShake(0.002f, 0.1f); DnBack(); }
    private void DnCringe() { DnPlay("cringe"); DnBack(); }
    private void DnDodge() { DnPlay("dodge"); DnBack(); }
    private void DnIdle() { DnPlay("idle", false); DnBack(); }
    private void DnFakeout() { DnPlay("fakeout"); DnBack(); }
    private void DnPissedConditional() { if (_opponent.CurrentAnimation == "cringe") DnPissed(); else DnIdle(); }
    private void DnPissed() { DnPlay("pissed"); DnBack(); }
    private void DnUppercutPrep() { DnPlay("uppercutPrep"); DnFront(); }
    private void DnUppercut() { DnPlay("uppercut"); DnFront(); }
    private void DnUppercutHit() { DnPlay("uppercutHit"); DnBack(); }
    private void DnHitHigh() { DnPlay("hitHigh"); BzCameraShake(0.0025f, 0.15f); DnBack(); }
    private void DnHitLow() { DnPlay("hitLow"); BzCameraShake(0.0025f, 0.15f); DnBack(); }
    private void DnPunchHigh() { DnPlay("punchHigh" + DnAlt()); DnFront(); }
    private void DnPunchLow() { DnPlay("punchLow" + DnAlt()); DnFront(); }
    private void DnSpin() { DnPlay("hitSpin"); BzCameraShake(0.0025f, 0.15f); DnBack(); }

    private void DarnellNoteHit(Note note)
    {
        if (!BzKind(note, out string kind)) return;
        bool prep = BzPoorHit() && BzLowHealth() && _bzRng.NextDouble() * 100.0 < 30;
        if (prep) { DnUppercutPrep(); return; }
        if (_bzDarnellCantUppercut) { DnPunchHigh(); return; }
        switch (kind)
        {
            case "weekend-1-punchlow": DnHitLow(); break;
            case "weekend-1-punchlowblocked": DnBlock(); break;
            case "weekend-1-punchlowdodged": DnDodge(); break;
            case "weekend-1-punchlowspin": DnSpin(); break;
            case "weekend-1-punchhigh": DnHitHigh(); break;
            case "weekend-1-punchhighblocked": DnBlock(); break;
            case "weekend-1-punchhighdodged": DnDodge(); break;
            case "weekend-1-punchhighspin": DnSpin(); break;
            case "weekend-1-blockhigh": DnPunchHigh(); break;
            case "weekend-1-blocklow": DnPunchLow(); break;
            case "weekend-1-blockspin": DnPunchHigh(); break;
            case "weekend-1-dodgehigh": DnPunchHigh(); break;
            case "weekend-1-dodgelow": DnPunchLow(); break;
            case "weekend-1-dodgespin": DnPunchHigh(); break;
            case "weekend-1-hithigh": DnPunchHigh(); break;
            case "weekend-1-hitlow": DnPunchLow(); break;
            case "weekend-1-hitspin": DnPunchHigh(); break;
            case "weekend-1-picouppercutprep": break; // continue whatever animation was playing
            case "weekend-1-picouppercut": DnUppercutHit(); break;
            case "weekend-1-darnelluppercutprep": DnUppercutPrep(); break;
            case "weekend-1-darnelluppercut": DnUppercut(); break;
            case "weekend-1-idle": DnIdle(); break;
            case "weekend-1-fakeout": DnCringe(); break;
            case "weekend-1-taunt": DnPissedConditional(); break;
            case "weekend-1-tauntforce": DnPissed(); break;
            case "weekend-1-reversefakeout": DnFakeout(); break;
        }
        _bzDarnellCantUppercut = false;
    }

    private void DarnellNoteMiss(Note note, float healthChange)
    {
        if (!BzKind(note, out string kind)) return;
        if (_opponent.CurrentAnimation == "uppercutPrep") { DnUppercut(); return; }
        if (_health + healthChange <= 0f) { DnPunchLow(); return; }
        if (_bzDarnellCantUppercut) { DnPunchHigh(); return; }
        switch (kind)
        {
            case "weekend-1-punchlow":
            case "weekend-1-punchlowblocked":
            case "weekend-1-punchlowdodged":
            case "weekend-1-punchlowspin": DnPunchLow(); break;
            case "weekend-1-punchhigh":
            case "weekend-1-punchhighblocked":
            case "weekend-1-punchhighdodged":
            case "weekend-1-punchhighspin": DnPunchHigh(); break;
            case "weekend-1-blockhigh": DnPunchHigh(); break;
            case "weekend-1-blocklow": DnPunchLow(); break;
            case "weekend-1-blockspin": DnPunchHigh(); break;
            case "weekend-1-dodgehigh": DnPunchHigh(); break;
            case "weekend-1-dodgelow": DnPunchLow(); break;
            case "weekend-1-dodgespin": DnPunchHigh(); break;
            case "weekend-1-hithigh": DnPunchHigh(); break;
            case "weekend-1-hitlow": DnPunchLow(); break;
            case "weekend-1-hitspin": DnPunchHigh(); break;
            case "weekend-1-picouppercutprep": DnHitHigh(); _bzDarnellCantUppercut = true; break;
            case "weekend-1-picouppercut": DnDodge(); break;
            case "weekend-1-darnelluppercutprep": DnUppercutPrep(); break;
            case "weekend-1-darnelluppercut": DnUppercut(); break;
            case "weekend-1-idle": DnIdle(); break;
            case "weekend-1-fakeout": DnCringe(); break;
            case "weekend-1-taunt": DnPissedConditional(); break;
            case "weekend-1-tauntforce": return;  // original calls playPissed() (undefined -> script error): no anim change, cantUppercut kept
            case "weekend-1-reversefakeout": DnFakeout(); break;
        }
        // NOTE: the original sets cantUppercut = true for picouppercutprep and then resets it here.
        _bzDarnellCantUppercut = false;
    }

    // ---- pico-blazin.hxc
    private void PcPlay(string anim, bool restart = true) => _boyfriend.PlayAnimation(anim, force: restart || _boyfriend.CurrentAnimation != anim);
    private void PcBack() { _bfZIndex = 2000; BzRefresh(); }
    private void PcFront() { _bfZIndex = 3000; BzRefresh(); }
    private string PcAlt() { _bzPicoAlternate = !_bzPicoAlternate; return _bzPicoAlternate ? "1" : "2"; }
    private void PcBlock() { PcPlay("block"); BzCameraShake(0.002f, 0.1f); PcBack(); }
    private void PcDodge() { PcPlay("dodge"); PcBack(); }
    private void PcIdle() { PcPlay("idle", false); PcBack(); }
    private void PcFakeout() { PcPlay("fakeout"); PcBack(); }
    private void PcUppercutPrep() { PcPlay("uppercutPrep"); PcFront(); }
    private void PcUppercut(bool hit) { PcPlay("uppercut"); if (hit) BzCameraShake(0.005f, 0.25f); PcFront(); }
    private void PcUppercutHit() { PcPlay("uppercutHit"); BzCameraShake(0.005f, 0.25f); PcBack(); }
    private void PcHitHigh() { PcPlay("hitHigh"); BzCameraShake(0.0025f, 0.15f); PcBack(); }
    private void PcHitLow() { PcPlay("hitLow"); BzCameraShake(0.0025f, 0.15f); PcBack(); }
    private void PcHitSpin() { _boyfriend.PlayAnimationReversed("hitSpin"); BzCameraShake(0.0025f, 0.15f); PcBack(); }
    private void PcPunchHigh() { PcPlay("punchHigh" + PcAlt()); PcFront(); }
    private void PcPunchLow() { PcPlay("punchLow" + PcAlt()); PcFront(); }
    private void PcTauntConditional() { if (_boyfriend.CurrentAnimation == "fakeout") PcTaunt(); else PcIdle(); }
    private void PcTaunt() { PcPlay("taunt"); PcBack(); }
    private bool DarnellPreppingUppercut() => _opponent?.CurrentAnimation == "uppercutPrep";
    private bool DarnellInUppercut() => _opponent?.CurrentAnimation == "uppercut" || _opponent?.CurrentAnimation == "uppercut-hold";

    private void PicoNoteHit(Note note)
    {
        if (!BzKind(note, out string kind)) return;
        if (BzPoorHit() && BzLowHealth() && DarnellPreppingUppercut()) { PcPunchHigh(); return; }
        if (_bzPicoCantUppercut) { PcBlock(); _bzPicoCantUppercut = false; return; }
        switch (kind)
        {
            case "weekend-1-punchlow":
            case "weekend-1-punchlowblocked":
            case "weekend-1-punchlowdodged":
            case "weekend-1-punchlowspin": PcPunchLow(); break;
            case "weekend-1-punchhigh":
            case "weekend-1-punchhighblocked":
            case "weekend-1-punchhighdodged":
            case "weekend-1-punchhighspin": PcPunchHigh(); break;
            case "weekend-1-blockhigh":
            case "weekend-1-blocklow":
            case "weekend-1-blockspin": PcBlock(); break;
            case "weekend-1-dodgehigh":
            case "weekend-1-dodgelow":
            case "weekend-1-dodgespin": PcDodge(); break;
            case "weekend-1-hithigh": PcHitHigh(); break;
            case "weekend-1-hitlow": PcHitLow(); break;
            case "weekend-1-hitspin": PcHitSpin(); break;
            case "weekend-1-picouppercutprep": PcUppercutPrep(); break;
            case "weekend-1-picouppercut": PcUppercut(true); break;
            case "weekend-1-darnelluppercutprep": PcIdle(); break;
            case "weekend-1-darnelluppercut": PcUppercutHit(); break;
            case "weekend-1-idle": PcIdle(); break;
            case "weekend-1-fakeout": PcFakeout(); break;
            case "weekend-1-taunt": PcTauntConditional(); break;
            case "weekend-1-tauntforce": PcTaunt(); break;
            case "weekend-1-reversefakeout": PcIdle(); break;
        }
    }

    private void PicoNoteMiss(Note note, float healthChange)
    {
        if (DarnellInUppercut()) { PcUppercutHit(); return; }
        if (_health + healthChange <= 0f) { PcHitLow(); return; }
        if (_bzPicoCantUppercut) { PcHitHigh(); return; }
        switch (note?.Kind ?? "")
        {
            case "weekend-1-punchlow":
            case "weekend-1-punchlowblocked":
            case "weekend-1-punchlowdodged": PcHitLow(); break;
            case "weekend-1-punchlowspin": PcHitSpin(); break;
            case "weekend-1-punchhigh":
            case "weekend-1-punchhighblocked":
            case "weekend-1-punchhighdodged": PcHitHigh(); break;
            case "weekend-1-punchhighspin": PcHitSpin(); break;
            case "weekend-1-blockhigh": PcHitHigh(); break;
            case "weekend-1-blocklow": PcHitLow(); break;
            case "weekend-1-blockspin": PcHitSpin(); break;
            case "weekend-1-dodgehigh": PcHitHigh(); break;
            case "weekend-1-dodgelow": PcHitLow(); break;
            case "weekend-1-dodgespin": PcHitSpin(); break;
            case "weekend-1-hithigh": PcHitHigh(); break;
            case "weekend-1-hitlow": PcHitLow(); break;
            case "weekend-1-hitspin": PcHitSpin(); break;
            case "weekend-1-picouppercutprep": PcPunchHigh(); _bzPicoCantUppercut = true; break;
            case "weekend-1-picouppercut": PcUppercut(false); break;
            case "weekend-1-darnelluppercutprep": PcIdle(); break;
            case "weekend-1-darnelluppercut": PcUppercutHit(); break;
            case "weekend-1-idle": PcIdle(); break;
            case "weekend-1-fakeout": PcHitHigh(); break;
            case "weekend-1-taunt": PcTauntConditional(); break;
            case "weekend-1-tauntforce": PcTaunt(); break;
            case "weekend-1-reversefakeout": PcIdle(); break;
        }
    }
}
