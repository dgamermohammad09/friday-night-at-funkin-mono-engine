using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// nene-pixel.hxc addSunsetShaders() (schoolErect only): abotSpeaker gets a DropShadowShader
/// setAdjustColor(-66, -10, 24, -23), angle 90, color 0xFF52351d, distance 5, antialiasAmt 0, threshold 1,
/// alt mask weeb/erect/masks/aBotPixelSpeaker_mask with maskThreshold 0.
/// </summary>
public partial class PlayScene
{
    private RimParams _w6SpeakerRim;
    private bool _w6SpeakerRimBuilt;

    private bool _w6PropWiggleActive;

    private bool W6BeginPropRim(SpriteBatch sb, string propName, AnimatedSprite sprite)
    {
        _w6PropWiggleActive = false;
        if (sprite != null && W6WiggleParams(propName ?? "") != null && W6BeginWiggle(sb, propName, sprite.Sheet?.Texture))
        { _w6PropWiggleActive = true; return true; }
        if (sprite == null || !string.Equals(propName, "neneSpeakerPixelSpeaker", StringComparison.OrdinalIgnoreCase)) return false;
        if (!_w6SpeakerRimBuilt)
        {
            _w6SpeakerRimBuilt = true;
            if ((_chart?.Stage ?? "") == "schoolErect" && string.Equals(_chart?.GirlfriendCharacter, "nene-pixel", StringComparison.OrdinalIgnoreCase))
            {
                var t = Assets.LoadTexture("shaders/masks/weeb/erect/aBotPixelSpeaker_mask.png");
                _w6SpeakerRim = new RimParams
                {
                    Brightness = -66, Hue = -10, Contrast = 24, Saturation = -23,
                    Color = Rgb(0x52351D), AngleDeg = 90, Distance = 5, AA = 0, Threshold = 1f,
                    UseMask = true, MaskThreshold = 0, Mask = t == Assets.Pixel ? null : t,
                };
            }
        }
        var rp = _w6SpeakerRim;
        if (rp == null) return false;
        var fx = RimEffect;
        if (fx == null) return false;
        var p = fx.Parameters;
        p["hue"]?.SetValue(rp.Hue); p["saturation"]?.SetValue(rp.Saturation);
        p["brightness"]?.SetValue(rp.Brightness); p["contrast"]?.SetValue(rp.Contrast);
        p["dropColor"]?.SetValue(rp.Color);
        p["ang"]?.SetValue(rp.AngleDeg * MathF.PI / 180f);
        p["dist"]?.SetValue(rp.Distance); p["str"]?.SetValue(rp.Strength);
        p["thr"]?.SetValue(rp.Threshold); p["thr2"]?.SetValue(rp.MaskThreshold);
        p["AA_STAGES"]?.SetValue(rp.AA);
        bool useMask = rp.Mask != null;
        p["useMask"]?.SetValue(useMask ? 1f : 0f);
        p["uInputPremultiplied"]?.SetValue(0f);
        p["uOutputPremultiplied"]?.SetValue(1f);
        p["uApplyVertexColor"]?.SetValue(0f);
        sprite.FrameEffect = fx;
        sprite.FrameEffectMask = useMask ? rp.Mask : null;
        sb.End();
        sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, _worldSampler, null, _worldClipRaster, fx, _worldZoomMatrix);
        return true;
    }

    private void W6EndPropRim(SpriteBatch sb, AnimatedSprite sprite, bool begun)
    {
        if (!begun) return;
        if (_w6PropWiggleActive) { _w6PropWiggleActive = false; W6EndWiggle(sb, true); return; }
        if (sprite != null) { sprite.FrameEffect = null; sprite.FrameEffectMask = null; }
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix,
                 rasterizerState: _worldClipRaster);
    }
}

public partial class PlayScene
{
    /// <summary>
    /// FocusCameraSongEvent.handleEvent: targetX/Y = (x, y) + the focused character's cameraFocusPoint
    /// (char -1 = the static position itself). Called after the character focus point was set.
    /// </summary>
    private void W6FocusCameraOffset(object value, int focusChar)
    {
        if (value is not Newtonsoft.Json.Linq.JObject o) return;
        float x = 0f, y = 0f;
        try { x = o.Value<float?>("x") ?? 0f; y = o.Value<float?>("y") ?? 0f; } catch { }
        if (focusChar == -1) { _camTargetWorldX = x; _camTargetWorldY = y; return; }
        if ((focusChar == 0 && _boyfriend == null) || (focusChar == 2 && _girlfriend == null) || (focusChar == 1 && _opponent == null)) return;
        _camTargetWorldX += x; _camTargetWorldY += y;
    }
}

/// <summary>
/// schoolEvil.hxc / schoolEvilErect.hxc: WiggleEffectRuntime(speed, freq, amplitude, DREAMY) on four props each,
/// time += elapsed in onUpdate (Shaders/Wiggle.fx = wiggle.frag DREAMY). Also spirit.hxc's FunkTrail.
/// </summary>
public partial class PlayScene
{
    private Effect _w6WiggleEffect;
    private bool _w6WiggleLoadTried;
    private float _w6WiggleTime;
    private bool _w6WiggleOffApplied;

    private (float speed, float freq, float amp)? W6WiggleParams(string propName)
    {
        string st = _chart?.Stage ?? "";
        if (st == "schoolEvil")
            return propName switch
            {
                "backTrees" => (2f * 0.8f, 4f * 0.4f, 0.011f),
                "school" => (2f, 4f, 0.017f),
                "street" => (2f, 4f, 0.007f),
                "trees" => (2f, 4f, 0.007f),
                _ => null,
            };
        if (st == "schoolEvilErect")
            return propName switch
            {
                "backspikes" => (2f * 0.8f, 4f * 0.4f, 0.011f),
                "school" => (2f, 4f, 0.017f),
                "backspike" => (2f, 4f, 0.01f),
                "evilstreet" => (2f, 4f, 0.007f),
                _ => null,
            };
        return null;
    }

    private Effect W6WiggleEffect
    {
        get
        {
            if (_w6WiggleEffect != null || _w6WiggleLoadTried) return _w6WiggleEffect;
            _w6WiggleLoadTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/Wiggle.mgfx")
                              ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "Wiggle.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _w6WiggleEffect = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                else Console.WriteLine("[W6] Wiggle.mgfx not found");
            }
            catch (Exception ex) { Console.WriteLine($"[W6] wiggle load failed: {ex.Message}"); }
            return _w6WiggleEffect;
        }
    }

    /// <summary>Begin an Immediate batch with the wiggle shader for this prop (texture = the bitmap it samples).</summary>
    private bool W6BeginWiggle(SpriteBatch sb, string propName, Texture2D tex)
    {
        if (tex == null || propName == null) return false;
        var wp = W6WiggleParams(propName);
        if (wp == null) return false;
        var fx = W6WiggleEffect;
        if (fx == null) return false;
        var p = fx.Parameters;
        p["uTime"]?.SetValue(_w6WiggleTime);
        p["uSpeed"]?.SetValue(wp.Value.speed);
        p["uFrequency"]?.SetValue(wp.Value.freq);
        p["uWaveAmplitude"]?.SetValue(wp.Value.amp);
        p["uTextureSize"]?.SetValue(new Vector2(tex.Width, tex.Height));
        sb.End();
        sb.Begin(SpriteSortMode.Immediate, _worldBlendState, _worldSampler, null, _worldClipRaster, fx, _worldZoomMatrix);
        return true;
    }

    private void W6EndWiggle(SpriteBatch sb, bool begun)
    {
        if (!begun) return;
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix,
                 rasterizerState: _worldClipRaster);
    }

    /// <summary>Per update: wiggle clocks (stage onUpdate) and spirit's FunkTrail (FlxTrail.update).</summary>
    private void W6StageScriptUpdate(float dt)
    {
        string st = _chart?.Stage ?? "";
        if (st == "schoolEvil" || st == "schoolEvilErect")
        {
            _w6WiggleTime += dt;
            if (s_w6AbotLog && _conductor != null && _conductor.SongPosition >= 2.9 && _conductor.SongPosition <= 3.1)
                Console.WriteLine($"[W6WIG] song={_conductor.SongPosition * 1000:F1} time={_w6WiggleTime:F4}");
            var off = Environment.GetEnvironmentVariable("FNF_W6_WIGGLE_OFFSET");
            if (!_w6WiggleOffApplied && !string.IsNullOrEmpty(off)) { _w6WiggleOffApplied = true; _w6WiggleTime += float.Parse(off, System.Globalization.CultureInfo.InvariantCulture); }
        }
        if (_opponent != null && string.Equals(_opponent.Name, "spirit", StringComparison.OrdinalIgnoreCase))
        {
            if (!_opponent.HasTrail) _opponent.EnableTrail(4, 24, 0.3f, 0.069f); // spirit.hxc: new FunkTrail(this, null, 4, 24, 0.3, 0.069)
            _opponent.UpdateTrail();
        }
    }
}
