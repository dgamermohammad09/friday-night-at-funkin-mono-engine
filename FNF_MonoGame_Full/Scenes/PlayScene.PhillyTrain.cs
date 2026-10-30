using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// phillyTrain.hxc / phillyTrainErect.hxc: train (8 cars, 1/24 s steps of 400 px after train_passes reaches 4.7 s,
/// beat % 8 == 4 + 30 % + cooldown > 8 beats), window lights (random colour every 4 beats, BuildingEffectShader fade).
/// </summary>
public partial class PlayScene
{
    // phillyTrain.hxc lightColors / phillyTrainErect.hxc lightColors
    private static readonly Color[] PtColorsNormal = { new Color(0x31, 0xA2, 0xFD), new Color(0x31, 0xFD, 0x8C), new Color(0xFB, 0x33, 0xF5), new Color(0xFB, 0xA6, 0x33), new Color(0xFD, 0x45, 0x31) };
    private static readonly Color[] PtColorsErect = { new Color(0xB6, 0x6F, 0x43), new Color(0x32, 0x9A, 0x6D), new Color(0x93, 0x2C, 0x28), new Color(0x26, 0x63, 0xAC), new Color(0x50, 0x2D, 0x64) };
    private Color _ptLightColor = Color.White;   // lightWindow.color (white until the first beat % 4 == 0)
    private float _ptAlphaShit = 1f;             // BuildingEffectShader.alphaShit (initial 1.0)
    private int _ptCooldown;                     // trainCooldown (beats)
    private int _ptCars = 8;
    private bool _ptFinishing, _ptStartedMoving;
    private float _ptFrameTiming;
    private double _ptSoundTime;                 // trainSound.time (s since train_passes started)
    private Effect _buildingFx; private bool _buildingFxTried;

    private bool IsPhillyTrainStage => _chart?.Stage is "phillyTrain" or "phillyTrainErect";

    private void PhillyTrainBeat(int beat)
    {
        if (!IsPhillyTrainStage) return;
        if (_trainStaticIndex >= 0)
        {
            if (!_trainMoving) _ptCooldown += 1;
            if (beat % 8 == 4 && _random.NextDouble() * 100 < 30 && !_trainMoving && _ptCooldown > 8)
            {
                _ptCooldown = _random.Next(-4, 1); // FlxG.random.int(-4, 0)
                _trainMoving = true; _ptSoundTime = 0; _ptFrameTiming = 0;
                Audio.PlaySound("game/stages/philly_train/train_passes");
            }
        }
        if (beat % 4 == 0 && _phillyLightsIndex >= 0)
        {
            _ptAlphaShit = 0f; // lightShader.reset()
            var cols = _chart?.Stage == "phillyTrainErect" ? PtColorsErect : PtColorsNormal;
            _ptLightColor = cols[_random.Next(0, cols.Length)];
        }
    }

    private void PhillyTrainUpdate(float elapsed)
    {
        SpookyRainUpdate(elapsed);
        if (!IsPhillyTrainStage) return;
        // shaderInput = beatLengthMs / 1000 * elapsed * 1.5; BuildingEffectShader.update: alphaShit += effectSpeed(1.0) * input
        _ptAlphaShit += (float)(_conductor.Crochet * elapsed * 1.5);
        if (_trainStaticIndex < 0 || !_trainMoving) return;
        _ptSoundTime += elapsed;
        _ptFrameTiming += elapsed;
        if (_ptFrameTiming >= 1f / 24f) { PtUpdateTrainPos(); _ptFrameTiming = 0; }
    }

    private void PtUpdateTrainPos()
    {
        if (_ptSoundTime >= 4.7)
        {
            _ptStartedMoving = true;
            // playAnimation('hairBlow') (restart = false: restarts only when finished or another anim plays)
            if (_girlfriend != null && (_girlfriend.CurrentAnimation != "hairBlow" || _girlfriend._sprite == null || _girlfriend._sprite.Finished))
                _girlfriend.PlayAnimation("hairBlow", force: true);
        }
        if (!_ptStartedMoving) return;
        var p = _staticProps[_trainStaticIndex];
        p.X -= 400f;
        if (p.X < -2000f && !_ptFinishing)
        {
            p.X = -1150f;
            _ptCars -= 1;
            if (_ptCars <= 0) _ptFinishing = true;
        }
        _staticProps[_trainStaticIndex] = p;
        _trainX = p.X;
        if (p.X < -4000f && _ptFinishing) PtTrainReset();
    }

    private void PtTrainReset()
    {
        _girlfriend?.PlayAnimation("hairFall", force: true);
        var p = _staticProps[_trainStaticIndex];
        p.X = 1280 + 200; // FlxG.width + 200
        _staticProps[_trainStaticIndex] = p;
        _trainX = p.X;
        _trainMoving = false; _ptCars = 8; _ptFinishing = false; _ptStartedMoving = false;
    }

    private Effect BuildingEffect
    {
        get
        {
            if (_buildingFx != null || _buildingFxTried) return _buildingFx;
            _buildingFxTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/BuildingEffect.mgfx")
                              ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "BuildingEffect.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _buildingFx = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                else Console.WriteLine("[SHADER] BuildingEffect.mgfx not found");
            }
            catch (Exception ex) { Console.WriteLine($"[SHADER] BuildingEffect load failed: {ex.Message}"); }
            return _buildingFx;
        }
    }

    /// <summary>lightWindow.shader = BuildingEffectShader (building.frag: color -= alphaShit where alpha > 0).</summary>
    private bool PtBeginLightFx(SpriteBatch sb, StaticStageProp prop)
    {
        if (PsyRuntimeShaderBegin(sb, prop)) return true; // Psych setSpriteShader (runtime .frag)
        if (LimoBeginOverlay(sb, prop)) return true;
        if (_phillyLightsIndex < 0 || prop.Name != "lights" || !IsPhillyTrainStage) return false;
        var fx = BuildingEffect;
        if (fx == null) return false;
        fx.Parameters["alphaShit"]?.SetValue(_ptAlphaShit);
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster, effect: fx);
        return true;
    }

    private void PtEndLightFx(SpriteBatch sb, bool begun)
    {
        if (!begun) return;
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
    }
}
