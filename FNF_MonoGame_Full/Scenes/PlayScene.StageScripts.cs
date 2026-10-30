using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Visual parts of the original stage scripts (funkin.assets/preload/scripts/stages/*.hxc) that are
/// pure data: AdjustColorShader values per character/prop, and prop blend modes / alpha set in
/// addProp()/onCreate(). Values copied verbatim from the original scripts.
/// </summary>
public partial class PlayScene
{
    public readonly struct AdjustColorParams
    {
        public readonly float Hue, Saturation, Brightness, Contrast;
        public AdjustColorParams(float hue, float saturation, float brightness, float contrast)
        { Hue = hue; Saturation = saturation; Brightness = brightness; Contrast = contrast; }
    }

    private sealed class StageScriptVisuals
    {
        public AdjustColorParams? Bf, Gf, Dad;              // character.shader
        public readonly Dictionary<string, AdjustColorParams> PropShaders = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> PropBlend = new(StringComparer.OrdinalIgnoreCase);   // exact names
        public readonly Dictionary<string, float> PropAlpha = new(StringComparer.OrdinalIgnoreCase);
        public string LightmapSuffixBlend;                    // props whose name ends with "_lightmap"
        public float LightmapAlpha = 1f;
    }

    private static StageScriptVisuals BuildStageScriptVisuals(string stage)
    {
        var v = new StageScriptVisuals();
        switch (stage)
        {
            case "mainStageErect": // mainStageErect.hxc
                v.Bf = new AdjustColorParams(hue: 12, saturation: 0, brightness: -23, contrast: 7);
                v.Gf = new AdjustColorParams(hue: -9, saturation: 0, brightness: -30, contrast: -4);
                v.Dad = new AdjustColorParams(hue: -32, saturation: 0, brightness: -33, contrast: -23);
                foreach (var n in new[] { "brightLightSmall", "orangeLight", "lightgreen", "lightred", "lightAbove" })
                    v.PropBlend[n] = "add"; // blend = 0 (openfl BlendMode.ADD)
                break;

            case "limoRideErect": // limoRideErect.hxc
            {
                var p = new AdjustColorParams(hue: -30, saturation: -20, brightness: -30, contrast: 0);
                v.Bf = v.Gf = v.Dad = p;
                foreach (var n in new[] { "limoDancer1", "limoDancer2", "limoDancer3", "limoDancer4", "limoDancer5", "fastCar" })
                    v.PropShaders[n] = p;
                foreach (var n in new[] { "mist1", "mist2", "mist3", "mist4", "mist5", "shootingStar" })
                    v.PropBlend[n] = "add";
                break;
            }

            case "mallXmasErect": // mallXmasErect.hxc
            {
                var p = new AdjustColorParams(hue: 5, saturation: 20, brightness: 0, contrast: 0);
                v.Bf = v.Gf = v.Dad = p;
                v.PropShaders["santa"] = p;
                break;
            }

            case "phillyTrainErect": // phillyTrainErect.hxc
            {
                var p = new AdjustColorParams(hue: -26, saturation: -16, brightness: -5, contrast: 0);
                v.Bf = v.Gf = v.Dad = p;
                v.PropShaders["train"] = p;
                break;
            }

            case "phillyStreets": // phillyStreets.hxc addProp()
                v.LightmapSuffixBlend = "add";
                v.LightmapAlpha = 0.6f;
                break;

            case "phillyStreetsErect": // phillyStreetsErect.hxc addProp() + addCharacter()
            {
                var p = new AdjustColorParams(hue: -5, saturation: -40, brightness: -20, contrast: -25);
                v.Bf = v.Gf = v.Dad = p;
                v.LightmapSuffixBlend = "add";
                v.LightmapAlpha = 0.6f;
                v.PropBlend["grey1"] = "add";
                v.PropBlend["grey2"] = "multiply"; // blend = 9 (openfl BlendMode.MULTIPLY)
                foreach (var n in new[] { "mist0", "mist1", "mist2", "mist3", "mist4", "mist5" })
                    v.PropBlend[n] = "add";
                break;
            }
        }
        return v;
    }

    private StageScriptVisuals _stageVisuals;
    private bool _stageVisualsApplied;
    private Effect _adjustColorEffect;
    private bool _adjustColorLoadTried;

    private StageScriptVisuals StageVisuals => _stageVisuals ??= AddCharacterScriptVisuals(BuildStageScriptVisuals(_chart?.Stage ?? ""));

    /// <summary>
    /// Character scripts that shade the A-Bot rig (otis-speaker.hxc:47-92, nene-tankmen.hxc:36-81):
    /// abot, stereoBG, pupil = AdjustColor(hue -40, sat -20, bright -40, contrast -25); visualizer bars =
    /// AdjustColor(hue -30, sat -10, bright -12, contrast 0). Eye whites are not shaded.
    /// </summary>
    private StageScriptVisuals AddCharacterScriptVisuals(StageScriptVisuals v)
    {
        string gf = _chart?.GirlfriendCharacter ?? "";
        if (gf.Equals("otis-speaker", StringComparison.OrdinalIgnoreCase) || gf.Equals("nene-tankmen", StringComparison.OrdinalIgnoreCase))
        {
            var body = new AdjustColorParams(hue: -40, saturation: -20, brightness: -40, contrast: -25);
            var viz = new AdjustColorParams(hue: -30, saturation: -10, brightness: -12, contrast: 0);
            v.PropShaders["neneSpeakerRig"] = body;
            v.PropShaders["neneSpeakerStereoBG"] = body;
            v.PropShaders["neneSpeakerPupil"] = body;
            for (int i = 1; i <= 7; i++) v.PropShaders["neneSpeakerViz" + i] = viz; // bars are named neneSpeakerViz1..7
        }
        if (gf.Equals("nene-dark", StringComparison.OrdinalIgnoreCase))
        {
            // nene-dark.hxc onCreate: vizAdjustColor (brightness -12, hue -26, contrast 0, saturation -45) on every abotViz member
            var viz = new AdjustColorParams(hue: -26, saturation: -45, brightness: -12, contrast: 0);
            for (int i = 1; i <= 7; i++) v.PropShaders["neneSpeakerViz" + i] = viz;
        }
        if (gf.Equals("nene-pixel", StringComparison.OrdinalIgnoreCase) && (_chart?.Stage ?? "") == "schoolErect")
        {
            // nene-pixel.hxc addSunsetShaders() (called by schoolErect.hxc addCharacter): noRimShader on abot, abotBack,
            // abotHead (and abotViz, a no-op) = AdjustColor(hue -10, sat -23, bright -66, contrast 24). The speaker gets a DropShadow (W6BeginPropRim).
            var noRim = new AdjustColorParams(hue: -10, saturation: -23, brightness: -66, contrast: 24);
            v.PropShaders["neneSpeakerRig"] = noRim;
            v.PropShaders["neneSpeakerPixelBack"] = noRim;
            v.PropShaders["neneSpeakerPixelHead"] = noRim;
            // abotViz.shader = noRimShader has no visible effect: ABotVis is a FlxSpriteGroup and flixel's
            // FlxSpriteGroup does not forward `shader` to its members (verified: original bars stay bright green).
        }
        return v;
    }

    /// <summary>Apply prop blend/alpha the way the original stage script's addProp()/onCreate() does. Runs once.</summary>
    private void EnsureStageScriptVisualsApplied()
    {
        if (_stageVisualsApplied) return;
        _stageVisualsApplied = true;
        var v = StageVisuals;
        for (int i = 0; i < _staticProps.Count; i++)
        {
            var p = _staticProps[i];
            if (string.IsNullOrEmpty(p.Name)) continue;
            bool changed = false;
            if (v.LightmapSuffixBlend != null && p.Name.EndsWith("_lightmap", StringComparison.OrdinalIgnoreCase))
            {
                p.Blend = v.LightmapSuffixBlend;
                p.Alpha = v.LightmapAlpha;
                changed = true;
            }
            if (v.PropBlend.TryGetValue(p.Name, out var blend)) { p.Blend = blend; changed = true; }
            if (v.PropAlpha.TryGetValue(p.Name, out var a)) { p.Alpha = a; changed = true; }
            if (changed) _staticProps[i] = p; // StaticStageProp may be a value-like copy in some paths
        }
    }

    private Effect AdjustColorEffect
    {
        get
        {
            if (_adjustColorEffect != null || _adjustColorLoadTried) return _adjustColorEffect;
            _adjustColorLoadTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/AdjustColor.mgfx")
                              ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "AdjustColor.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path))
                    _adjustColorEffect = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                else
                    Console.WriteLine("[SHADER] AdjustColor.mgfx not found; stage color filters disabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SHADER] AdjustColor load failed: {ex.Message}");
            }
            return _adjustColorEffect;
        }
    }

    /// <summary>charType: 0 = BF, 1 = Dad, 2 = GF.</summary>
    private AdjustColorParams? GetCharacterAdjust(int charType) => VsCharAdjust(charType) ?? charType switch
    {
        0 => StageVisuals.Bf,
        1 => StageVisuals.Dad,
        2 => StageVisuals.Gf,
        _ => null
    };

    private AdjustColorParams? GetPropAdjust(string propName)
        => VsPropAdjust(propName) ?? (!string.IsNullOrEmpty(propName) && StageVisuals.PropShaders.TryGetValue(propName, out var p) ? p : null);

    /// <summary>Restart the world batch with the AdjustColor effect. Returns true if the caller must call EndAdjust.</summary>
    private bool BeginAdjust(SpriteBatch sb, AdjustColorParams? p)
    {
        if (p == null) return false;
        var fx = AdjustColorEffect;
        if (fx == null) return false;
        fx.Parameters["hue"]?.SetValue(p.Value.Hue);
        fx.Parameters["saturation"]?.SetValue(p.Value.Saturation);
        fx.Parameters["brightness"]?.SetValue(p.Value.Brightness);
        fx.Parameters["contrast"]?.SetValue(p.Value.Contrast);
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix,
                 rasterizerState: _worldClipRaster, effect: fx);
        return true;
    }

    private void EndAdjust(SpriteBatch sb, bool begun)
    {
        if (!begun) return;
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix,
                 rasterizerState: _worldClipRaster);
    }

    private bool BeginCharAdjust(SpriteBatch sb, int charType) => BeginAdjust(sb, GetCharacterAdjust(charType));
}
