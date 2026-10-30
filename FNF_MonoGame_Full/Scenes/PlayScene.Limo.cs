using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// limoRide.hxc / limoRideErect.hxc: fastCar (resetFastCar at build, 10 % per beat, velocity = int(170,220)/elapsed*3,
/// carPass sound, Sequence resets after 2 s), limoSunset OverlayBlend (normal only), erect mist backdrops + shooting star.
/// </summary>
public partial class PlayScene
{
    private bool _limoInit;
    private float _limoLastDt = 1f / 60f;
    private float _limoSeqTimer = -1f;   // carSequence (time since fastCarDrive), -1 = none
    private float _limoTimer;            // limoRideErect _timer
    private int _limoStarBeat, _limoStarOffset = 2;
    private Effect _limoOverlayFx; private bool _overlayTried; private Texture2D _limoOverlayTex;

    private bool IsLimoStage => _chart?.Stage is "limoRide" or "limoRideErect";

    private void LimoResetFastCar()
    {
        if (_fastCarStaticIndex < 0) return;
        var p = _staticProps[_fastCarStaticIndex];
        p.X = -12600f;
        p.Y = _random.Next(140, 251); // FlxG.random.int(140, 250)
        _staticProps[_fastCarStaticIndex] = p;
        _fastCarVelocityX = 0f;
        _fastCarCanDrive = true;
    }

    private void LimoBeat()
    {
        if (!IsLimoStage) return;
        LimoEnsureInit();
        if (_random.NextDouble() * 100 < 10 && _fastCarCanDrive && _fastCarStaticIndex >= 0)
        {
            Audio.PlaySound("game/stages/limo/carPass" + _random.Next(0, 2), 0.7f);
            _fastCarVelocityX = (_random.Next(170, 221) / _limoLastDt) * 3f; // FlxG.elapsed of this frame
            _fastCarCanDrive = false;
            _limoSeqTimer = 0f;
        }
        if (_chart?.Stage == "limoRideErect" && _random.NextDouble() * 100 < 10 && _curBeatForLimo > _limoStarBeat + _limoStarOffset)
            LimoShootingStar(_curBeatForLimo);
    }
    private int _curBeatForLimo => (int)Math.Floor(_conductor.SongPosition / _conductor.Crochet);

    private void LimoShootingStar(int beat)
    {
        for (int i = 0; i < _animatedProps.Count; i++)
        {
            var a = _animatedProps[i];
            if (a.Name != "shootingStar" || a.Sprite == null) continue;
            a.Sprite.Position = new Vector2(_random.Next(50, 901), _random.Next(-10, 21));
            bool flip = _random.NextDouble() * 100 < 50;
            a.Sprite.Effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            string anim = a.AnimNameMap != null && a.AnimNameMap.TryGetValue("shooting star", out var m) ? m : "shooting star";
            a.Sprite.PlayAnimation(anim, force: true, loop: false);
        }
        _limoStarBeat = beat;
        _limoStarOffset = _random.Next(4, 9);
    }

    private void LimoEnsureInit()
    {
        if (_limoInit) return;
        _limoInit = true;
        LimoResetFastCar();
        if (_chart?.Stage == "limoRideErect") LimoBuildMist();
    }

    private void LimoUpdate(float dt)
    {
        if (!IsLimoStage) return;
        LimoEnsureInit();
        if (dt > 0) _limoLastDt = dt;
        if (_fastCarStaticIndex >= 0 && _fastCarVelocityX != 0f)
        {
            var p = _staticProps[_fastCarStaticIndex];
            p.X += _fastCarVelocityX * dt;
            _staticProps[_fastCarStaticIndex] = p;
        }
        if (_limoSeqTimer >= 0f)
        {
            _limoSeqTimer += dt;
            if (_limoSeqTimer >= 2f) { _limoSeqTimer = -1f; LimoResetFastCar(); }
        }
        if (_chart?.Stage == "limoRideErect") { _limoTimer += dt; PsUpdateMist(dt); }
    }

    /// <summary>limoRideErect.hxc buildStage: mist1..5 FlxBackdrop (X axis), ADD blend, colour/alpha/velocity/scale per layer.</summary>
    private void LimoBuildMist()
    {
        _psMist.Clear();
        _psMistTimer = 0f;
        // y(t) uses limoRideErect _timer (same clock as the mist timer: both start at 0 and add elapsed)
        AddMistEx("mistMid", 1.1f, 400, 0.4f, 1700f, 1.3f, new Color(0xc6, 0xbf, 0xde), t => 100 + MathF.Sin(t) * 200);
        AddMistEx("mistBack", 1.2f, 401, 1f, 2100f, 1f, new Color(0x6a, 0x4d, 0xa1), t => 0 + MathF.Sin(t * 0.8f) * 100);
        AddMistEx("mistMid", 0.8f, 99, 0.5f, 900f, 1.5f, new Color(0xa7, 0xd9, 0xbe), t => -20 + MathF.Sin(t * 0.5f) * 200);
        AddMistEx("mistBack", 0.6f, 98, 1f, 700f, 1.5f, new Color(0x9c, 0x77, 0xc7), t => -180 + MathF.Sin(t * 0.4f) * 300);
        AddMistEx("mistMid", 0.2f, 15, 1f, 100f, 1.5f, new Color(0xE7, 0xA4, 0x80), t => -450 + MathF.Sin(t * 0.2f) * 150);
        SortStaticProps();
        PsUpdateMist(0f);
    }

    private void AddMistEx(string image, float sf, int z, float alpha, float vx, float scale, Color color, Func<float, float> yAt)
    {
        var tex = LoadStagePropTexture("limo/erect/" + image) ?? Assets.LoadTexture("game/stages/limo_erect/" + image + ".png");
        if (tex == null) return;
        var m = new MistLayer { X = -650f, Vx = vx, Scale = scale, Sf = sf, TileW = tex.Width, TileH = tex.Height, YAt = yAt };
        int count = (int)Math.Ceiling((1280f / 0.5f) / (tex.Width * scale)) + 2;
        for (int i = 0; i < count; i++)
        {
            var prop = new StaticStageProp
            {
                Texture = tex, Name = $"limoMist{_psMist.Count}_{i}", ScaleX = scale, ScaleY = scale, Alpha = alpha,
                ScrollX = sf, ScrollY = sf, ZIndex = z, Blend = "add", OverlayColor = color,
            };
            _staticProps.Add(prop);
            m.Tiles.Add(prop);
        }
        _psMist.Add(m);
    }

    /// <summary>limoRide.hxc: limoSunset.shader = OverlayBlend (funnyShit = limo/limoOverlay pixels, uv + (0.1, 0.2)).</summary>
    private bool LimoBeginOverlay(SpriteBatch sb, StaticStageProp prop)
    {
        if (prop.Name != "limoSunset" || _chart?.Stage != "limoRide") return false;
        if (!_overlayTried)
        {
            _overlayTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/OverlayBlend.mgfx")
                              ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "OverlayBlend.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _limoOverlayFx = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                _limoOverlayTex = LoadStagePropTexture("limo/limoOverlay") ?? Assets.LoadTexture("game/stages/limo/limoOverlay.png");
            }
            catch (Exception ex) { Console.WriteLine($"[SHADER] OverlayBlend load failed: {ex.Message}"); }
        }
        if (_limoOverlayFx == null || _limoOverlayTex == null) return false;
        _limoOverlayFx.Parameters["OverlayTex"]?.SetValue(_limoOverlayTex);
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster, effect: _limoOverlayFx);
        return true;
    }
}
