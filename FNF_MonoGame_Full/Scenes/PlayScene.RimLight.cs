using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Gameplay;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Original DropShadowShader rim lighting (funkin/graphics/shaders/DropShadowShader.hx), applied per character the
/// way the stage scripts do it (tankmanBattlefieldErect.hxc addCharacter, schoolErect.hxc). Values verbatim from the
/// original scripts. setAdjustColor(b, h, c, s) = (brightness, hue, contrast, saturation).
/// </summary>
public partial class PlayScene
{
    private sealed class RimParams
    {
        public float Hue, Saturation, Brightness, Contrast;
        public Vector3 Color;
        public float AngleDeg = 0, Distance = 15, Strength = 1, Threshold = 0.1f, AA = 2, MaskThreshold = 0;
        public bool UseMask;
        public string MaskPath;   // optional; content-relative
        public Texture2D Mask;
    }

    private Effect _rimEffect;
    private bool _rimLoadTried;
    private RimParams[] _rimByChar;   // 0 = BF, 1 = Dad, 2 = GF
    private bool _rimBuilt;
    private bool _rimDadMaskEnabled;  // tankmanBattlefieldErect "EnableMask" song event

    private static Vector3 Rgb(int hex) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

    private Effect RimEffect
    {
        get
        {
            if (_rimEffect != null || _rimLoadTried) return _rimEffect;
            _rimLoadTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/RimLight.mgfx")
                              ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "RimLight.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _rimEffect = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                else Console.WriteLine("[RIM] RimLight.mgfx not found");
            }
            catch (Exception ex) { Console.WriteLine($"[RIM] load failed: {ex.Message}"); }
            return _rimEffect;
        }
    }

    private void BuildRimParams()
    {
        _rimBuilt = true;
        _rimByChar = new RimParams[3];
        switch (_chart?.Stage)
        {
            case "tankmanBattlefieldErect":   // tankmanBattlefieldErect.hxc:36-70
            {
                RimParams Make(float angle, float thr) => new RimParams
                {
                    Brightness = -46, Hue = -38, Contrast = -25, Saturation = -20,
                    Color = Rgb(0xDFEF3C), AngleDeg = angle, Threshold = thr,
                };
                _rimByChar[0] = Make(90, 0.1f);   // BF
                _rimByChar[2] = Make(90, 0.1f);   // GF (gf-tankmen / nene-tankmen masks handled below when available)
                _rimByChar[1] = Make(25, 0.3f);   // DAD: angle 25, threshold 0.3
                break;
            }
            case "schoolErect":               // schoolErect.hxc addCharacter (Senpai/Roses erect + pico mix)
            {
                Texture2D Mask(string n) { var t = Assets.LoadTexture($"shaders/masks/weeb/erect/{n}.png"); return t == Assets.Pixel ? null : t; }
                RimParams Make() => new RimParams
                {
                    Brightness = -66, Hue = -10, Contrast = 24, Saturation = -23,   // setAdjustColor(-66, -10, 24, -23)
                    Color = Rgb(0x52351D), AngleDeg = 90, Distance = 5, AA = 0, Threshold = 0.1f,
                    UseMask = true, MaskThreshold = 1,
                };
                var bf = Make();
                bf.Mask = Mask(string.Equals(_chart?.PlayerCharacter, "pico-pixel", StringComparison.OrdinalIgnoreCase) ? "picoPixel_mask" : "bfPixel_mask");
                var gf = Make();
                gf.Brightness = -42; gf.Hue = -10; gf.Contrast = 5; gf.Saturation = -25; // setAdjustColor(-42, -10, 5, -25)
                gf.Distance = 3; gf.Threshold = 0.3f;
                gf.Mask = Mask(string.Equals(_chart?.GirlfriendCharacter, "nene-pixel", StringComparison.OrdinalIgnoreCase) ? "nenePixel_mask" : "gfPixel_mask");
                var dad = Make();
                dad.Mask = Mask("senpai_mask");
                _rimByChar[0] = bf; _rimByChar[1] = dad; _rimByChar[2] = gf;
                break;
            }
        }
    }

    /// <summary>Start a rim-lit draw for a character. Returns true if EndRim must be called.</summary>
    private bool BeginRim(SpriteBatch sb, int charType, Character c)
    {
        if (!_rimBuilt) BuildRimParams();
        var rp = _rimByChar?[charType];
        if (rp == null || c?._sprite == null || _gameOver) return false;   // onGameOver removes the shader
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
        bool useMask = rp.UseMask && rp.Mask != null && (charType != 1 || _rimDadMaskEnabled || rp.MaskPath == null);
        p["useMask"]?.SetValue(useMask ? 1f : 0f);
        p["uInputPremultiplied"]?.SetValue(0f);
        p["uOutputPremultiplied"]?.SetValue(1f);
        p["uApplyVertexColor"]?.SetValue(0f);
        c._sprite.FrameEffect = fx;
        c._sprite.FrameEffectMask = useMask ? rp.Mask : null;
        sb.End();
        sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, _worldSampler, null, _worldClipRaster, fx, _worldZoomMatrix);
        return true;
    }

    /// <summary>TankmanSprite.addRimlight() (erect stress tankmen): DropShadowShader baseBrightness -46, baseHue -38,
    /// baseContrast -25, baseSaturation -20, threshold 0.4, color 0xFFDFEF3C, angle 25 (not mirrored for flipX).</summary>
    private static readonly RimParams TankmenRim = new RimParams
    {
        Brightness = -46, Hue = -38, Contrast = -25, Saturation = -20, Color = Rgb(0xDFEF3C), AngleDeg = 25, Threshold = 0.4f,
    };

    private bool BeginSpriteRim(SpriteBatch sb, RimParams rp, AnimatedSprite spr)
    {
        if (rp == null || spr == null || _gameOver) return false;
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
        p["useMask"]?.SetValue(0f);
        p["uInputPremultiplied"]?.SetValue(0f);
        p["uOutputPremultiplied"]?.SetValue(1f);
        p["uApplyVertexColor"]?.SetValue(0f);
        spr.FrameEffect = fx;
        spr.FrameEffectMask = null;
        sb.End();
        sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, _worldSampler, null, _worldClipRaster, fx, _worldZoomMatrix);
        return true;
    }

    private void EndSpriteRim(SpriteBatch sb, AnimatedSprite spr, bool begun)
    {
        if (!begun) return;
        spr.FrameEffect = null; spr.FrameEffectMask = null;
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix,
                 rasterizerState: _worldClipRaster);
    }

    private void EndRim(SpriteBatch sb, Character c, bool begun)
    {
        if (!begun) return;
        if (c?._sprite != null) { c._sprite.FrameEffect = null; c._sprite.FrameEffectMask = null; }
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix,
                 rasterizerState: _worldClipRaster);
    }

    /// <summary>Draw a stage character with the stage's shader: rim light if the stage uses it, else AdjustColor.</summary>
    private void DrawStageCharacter(SpriteBatch sb, int charType, Character c, Vector2 screen)
    {
        if (c == null || !c.Visible) return;
        if (c.HasTrail) c.DrawTrail(sb, Assets, screen.X, screen.Y, _defaultCamZoom * _cameraBopMultiplier); // FunkTrail (zIndex 190, behind spirit)
        // Spaghetti SserafimLipSyncSprite (PlayScene.SserafimLipSync.cs) is a FlxSpriteElement inside the girl's
        // timeline (mouth layer): draw the parts below it, the lip sprite, then the parts above it.
        bool split = LipSplitActive(c) || VsElementSplit(c);
        if (split) c._sprite.CompositeLayer = 1;
        DrawStageCharacterPass(sb, charType, c, screen);
        LipDraw(sb, c, charType); VsElementDraw(sb, c);
        if (split) { c._sprite.CompositeLayer = 2; DrawStageCharacterPass(sb, charType, c, screen); c._sprite.CompositeLayer = 0; }
    }

    private void DrawStageCharacterPass(SpriteBatch sb, int charType, Character c, Vector2 screen)
    {
        bool rim = BeginRim(sb, charType, c);
        bool adj = !rim && (SsfBeginChar(sb, c) || BeginCharAdjust(sb, charType));
        c.Draw(sb, Assets, screen.X, screen.Y, _defaultCamZoom * _cameraBopMultiplier);
        EndAdjust(sb, adj);
        EndRim(sb, c, rim);
    }
}
