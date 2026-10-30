using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>spookyMansionErect.hxc: RuntimeRainShader (spriteMode) on the bgTrees prop.</summary>
public partial class PlayScene
{
    private Effect _spriteRainFx; private bool _spriteRainTried;
    private int _spkStrikeBeat = 0, _spkStrikeOffset = 8; // spookyMansion(Erect).hxc lightningStrikeBeat / lightningStrikeOffset
    private float _spookyRainTime = 1f; // RuntimeRainShader.time starts at 1, update(elapsed): time += elapsed

    private void SpookyRainUpdate(float elapsed)
    {
        if (_chart?.Stage == "spookyMansionErect") _spookyRainTime += elapsed;
    }

    private Effect SpriteRainEffect
    {
        get
        {
            if (_spriteRainFx != null || _spriteRainTried) return _spriteRainFx;
            _spriteRainTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/SpriteRain.mgfx")
                              ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "SpriteRain.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _spriteRainFx = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                else Console.WriteLine("[SHADER] SpriteRain.mgfx not found");
            }
            catch (Exception ex) { Console.WriteLine($"[SHADER] SpriteRain load failed: {ex.Message}"); }
            return _spriteRainFx;
        }
    }

    /// <summary>BeginAdjust, or the bgTrees rain shader on spookyMansionErect. End with EndAdjust.</summary>
    private bool BeginAdjustOrRain(SpriteBatch sb, string propName, AnimatedSprite sprite, AdjustColorParams? adjust)
    {
        if (propName == "bgTrees" && _chart?.Stage == "spookyMansionErect" && sprite != null)
        {
            var fx = SpriteRainEffect;
            var fr = sprite.GetCurrentFrame();
            var tex = fr?.Texture ?? sprite.Sheet?.Texture;
            if (fx != null && fr != null && tex != null)
            {
                var r = fr.SourceRect;
                fx.Parameters["uFrameBounds"]?.SetValue(new Vector4(r.Left / (float)tex.Width, r.Top / (float)tex.Height, r.Right / (float)tex.Width, r.Bottom / (float)tex.Height));
                fx.Parameters["uScale"]?.SetValue(720f / 200f * 2f);
                fx.Parameters["uIntensity"]?.SetValue(0.4f);
                fx.Parameters["uTime"]?.SetValue(_spookyRainTime);
                fx.Parameters["uRainColor"]?.SetValue(new Vector3(0x66 / 255f, 0x80 / 255f, 0xCC / 255f));
                sb.End();
                sb.Begin(samplerState: _worldSampler, blendState: BlendState.AlphaBlend, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster, effect: fx);
                return true;
            }
        }
        return BeginAdjust(sb, adjust);
    }
}
