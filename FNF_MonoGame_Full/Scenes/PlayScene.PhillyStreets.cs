using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Port of the ORIGINAL stage script funkin.assets/preload/scripts/stages/phillyStreets.hxc
/// (Weekend 1: Darnell / Lit Up / 2hot): scrolling sky, traffic lights, the two cars driving
/// their quadratic paths, and the rain post-effect on the game camera.
/// </summary>
public partial class PlayScene
{
    private bool _psInit;
    private bool _psActive;
    private readonly Random _psRng = new();

    // traffic / cars state (phillyStreets.hxc fields)
    private bool _psLightsStop;
    private int _psLastChange;
    private int _psChangeInterval = 8;
    private bool _psCarWaiting;
    private bool _psCarInterruptable = true;
    private bool _psCar2Interruptable = true;

    private StaticStageProp _psSky;
    private float _psSkyScrollX;

    private sealed class CarTween
    {
        public bool Active;
        public float Delay, Duration, Elapsed;
        public Vector2 A, B, C;          // FlxTween.quadPath control points (top-left of sprite)
        public float Angle0, Angle1;
        public Func<float, float> Ease;
        public Vector2 ExtraOffset;      // sprite.offset (subtracted when drawn)
        public Action OnComplete;
    }
    private readonly CarTween _psCar1 = new(), _psCar2 = new();

    // rain (RuntimeRainShader)
    private Effect _rainEffect;
    private bool _rainLoadTried;
    private RenderTarget2D _rainWorldRT;
    private RenderTargetBinding[] _rainPrevTargets;
    private float _rainTime = 1f;
    private float _rainStart, _rainEnd;
    private bool _rainPassActive;

    private static float EaseLinear(float t) => t;
    private static float EaseSineIn(float t) => 1f - MathF.Cos(t * MathF.PI / 2f);
    private static float EaseCubeOut(float t) { t -= 1f; return 1f + t * t * t; }

    private int FindAnimatedPropIndex(string name)
    {
        for (int i = 0; i < _animatedProps.Count; i++)
            if (string.Equals(_animatedProps[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private void PlayAnimatedProp(string propName, string anim)
    {
        int i = FindAnimatedPropIndex(propName);
        if (i < 0) return;
        var p = _animatedProps[i];
        string key = p.AnimNameMap != null && p.AnimNameMap.TryGetValue(anim, out var k) ? k : anim;
        bool loop = p.AnimLoops != null && p.AnimLoops.TryGetValue(key, out var l) && l;
        p.Sprite?.PlayAnimation(key, true, loop);
    }

    private void SetCarPose(string propName, Vector2 topLeft, float angle, Vector2 extraOffset)
    {
        int i = FindAnimatedPropIndex(propName);
        if (i < 0) return;
        var p = _animatedProps[i];
        if (p.Sprite == null) return;
        p.Sprite.Position = topLeft - extraOffset;
        p.Sprite.RotateAroundCenter = true;   // FlxSprite rotates about its (centered) origin
        p.Angle = angle;
        _animatedProps[i] = p;
    }

    private void PhillyStreetsInit()
    {
        _psInit = true;
        _psErect = string.Equals(_chart?.Stage, "phillyStreetsErect", StringComparison.OrdinalIgnoreCase);
        _psActive = _psErect || string.Equals(_chart?.Stage, "phillyStreets", StringComparison.OrdinalIgnoreCase);
        if (!_psActive) return;

        // onCreate(): rain intensity per song (phillyStreetsErect.hxc uses a tenth of the values + rainColor 0xFFa8adb5)
        string song = (_songName ?? "").ToLowerInvariant().Replace('_', '-');
        float k = _psErect ? 0.1f : 1f;
        if (song.StartsWith("darnell")) { _rainStart = 0f; _rainEnd = 0.1f * k; }
        else if (song.StartsWith("lit-up")) { _rainStart = 0.1f * k; _rainEnd = 0.2f * k; }
        else if (song.StartsWith("2hot")) { _rainStart = 0.2f * k; _rainEnd = 0.4f * k; }
        _rainColor = _psErect ? new Vector3(0xa8 / 255f, 0xad / 255f, 0xb5 / 255f) : new Vector3(0x66 / 255f, 0x80 / 255f, 0xCC / 255f);

        // buildStage(): FlxTiledSprite(phillySkybox, 2922, 718, repeatX) at (-650,-375), scroll 0.1, z 10, scale 0.65
        string skyPath = _psErect ? "phillyStreets/erect/phillySkybox" : "phillyStreets/phillySkybox";
        var skyTex = LoadStagePropTexture(skyPath) ?? Assets.LoadTexture(_psErect ? "game/stages/philly_streets/erect/phillySkybox.png" : "game/stages/philly_streets/phillySkybox.png");
        if (skyTex != null)
        {
            _psSky = new StaticStageProp
            {
                Texture = MakeHorizontallyTiled(skyTex, 2922),
                Name = "scrollingSky",
                ScaleX = 0.65f, ScaleY = 0.65f, Alpha = 1f,
                ScrollX = 0.1f, ScrollY = 0.1f, ZIndex = 10,
            };
            // Flixel scales about the sprite's centre: visual top-left = pos + size/2 * (1 - scale)
            _psSky.X = -650f + 2922f / 2f * (1f - 0.65f);
            _psSky.Y = -375f + 718f / 2f * (1f - 0.65f);
            _psSky.SourceRect = new Rectangle(0, 0, 2922, 718);
            _staticProps.Add(_psSky);
            SortStaticProps();
        }

        if (_psErect) PsBuildMist();
        PsResetCar(true, true);
        PsResetStageValues();
    }

    // ------------------------------------------------------------------ phillyStreetsErect.hxc mist (FlxBackdrop x6)
    private bool _psErect;
    private Vector3 _rainColor = new(0x66 / 255f, 0x80 / 255f, 0xCC / 255f);
    private float _psMistTimer;
    private float _psMistLogT = Environment.GetEnvironmentVariable("FNF_MISTLOG") == "1" ? 0f : -1f;

    private sealed class MistLayer
    {
        public List<StaticStageProp> Tiles = new();
        public float X, Y, Vx, Scale, Sf;
        public int TileW, TileH;
        public Func<float, float> YAt;
    }
    private readonly List<MistLayer> _psMist = new();

    /// <summary>buildStage(): mist0..5 = FlxBackdrop(image, X axis) setPosition(-650,-100), blend ADD (0), color 0x5c5c5c.</summary>
    private void PsBuildMist()
    {
        _psMist.Clear();
        _psMistTimer = 0f;
        //            image       sf    z     alpha vx    scale  y(t)
        AddMist("mistMid", 1.2f, 1000, 0.6f, 172f, 1f, t => 660 + MathF.Sin(t * 0.35f) * 70);
        AddMist("mistMid", 1.1f, 1000, 0.6f, 150f, 1f, t => 500 + MathF.Sin(t * 0.3f) * 80);
        AddMist("mistBack", 1.2f, 1001, 0.8f, -80f, 1f, t => 540 + MathF.Sin(t * 0.4f) * 60);
        AddMist("mistMid", 0.95f, 99, 0.5f, -50f, 0.8f, t => 230 + MathF.Sin(t * 0.3f) * 70);
        AddMist("mistBack", 0.8f, 88, 1f, 40f, 0.7f, t => 170 + MathF.Sin(t * 0.35f) * 50);
        AddMist("mistMid", 0.5f, 39, 1f, 20f, 1.1f, t => -80 + MathF.Sin(t * 0.08f) * 100);
        SortStaticProps();
        PsUpdateMist(0f);
        Console.WriteLine($"[PSMIST] layers={_psMist.Count} tiles={_psMist.Sum(m => m.Tiles.Count)} first=({_psMist.FirstOrDefault()?.Tiles[0].X},{_psMist.FirstOrDefault()?.Tiles[0].Y}) static={_staticProps.Count}");
    }

    private void AddMist(string image, float sf, int z, float alpha, float vx, float scale, Func<float, float> yAt)
    {
        var tex = LoadStagePropTexture("phillyStreets/erect/" + image) ?? Assets.LoadTexture("game/stages/philly_streets/erect/" + image + ".png");
        if (tex == null) return;
        var m = new MistLayer { X = -650f, Vx = vx, Scale = scale, Sf = sf, TileW = tex.Width, TileH = tex.Height, YAt = yAt };
        // FlxBackdrop repeats the tile along X over the whole view: enough copies for zoom >= 0.5 plus margins.
        int count = (int)Math.Ceiling((1280f / 0.5f) / (tex.Width * scale)) + 2;
        for (int i = 0; i < count; i++)
        {
            var prop = new StaticStageProp
            {
                Texture = tex, Name = $"mist{_psMist.Count}_{i}", ScaleX = scale, ScaleY = scale, Alpha = alpha,
                ScrollX = sf, ScrollY = sf, ZIndex = z, Blend = "add", OverlayColor = new Color(0x5c, 0x5c, 0x5c),
            };
            _staticProps.Add(prop);
            m.Tiles.Add(prop);
        }
        _psMist.Add(m);
    }

    /// <summary>onUpdate(): mist y = base + sin(timer * f) * a; FlxBackdrop velocity.x; tiles repeat along X.</summary>
    private void PsUpdateMist(float dt)
    {
        if (_psMist.Count == 0) return;
        _psMistTimer += dt;
        float zoom = Math.Max(0.1f, _defaultCamZoom * _cameraBopMultiplier);
        foreach (var m in _psMist)
        {
            m.X += m.Vx * dt;          // FlxObject.updateMotion (constant velocity)
            m.Y = m.YAt(_psMistTimer);
            // FlxSprite scales about the frame centre: visual top-left = pos + size/2 * (1 - scale)
            float visX = m.X + m.TileW / 2f * (1f - m.Scale);
            float visY = m.Y + m.TileH / 2f * (1f - m.Scale);
            float step = m.TileW * m.Scale;
            // world x at the left screen edge for this scroll factor: screen = (x - scroll*sf - W/2)*zoom + W/2
            float viewLeft = (_camWorldX - 640f) * m.Sf + 640f - 640f / zoom;
            float n = MathF.Floor((viewLeft - visX) / step);
            for (int i = 0; i < m.Tiles.Count; i++)
            {
                m.Tiles[i].X = visX + (n + i) * step;
                m.Tiles[i].Y = visY;
            }
            if (_psMistLogT >= 0f && _psMistTimer >= _psMistLogT && m == _psMist[^1]) { _psMistLogT += 2f;
                foreach (var q in _psMist) { var sp = WorldToScreen(q.Tiles[0].X, q.Tiles[0].Y, q.Sf, q.Sf); Console.WriteLine($"[PSMIST] t={_psMistTimer:F1} {q.Tiles[0].Name} world=({q.Tiles[0].X:F0},{q.Tiles[0].Y:F0}) screen=({sp.X:F0},{sp.Y:F0}) z={q.Tiles[0].ZIndex} vis={q.Tiles[0].Visible} a={q.Tiles[0].Alpha} idx={_staticProps.IndexOf(q.Tiles[0])} cam=({_camWorldX:F0},{_camWorldY:F0}) zoom={zoom:F2}"); } }
        }
    }

    /// <summary>Builds a texture of at least <paramref name="width"/> + one tile, repeating the source horizontally.</summary>
    private Texture2D MakeHorizontallyTiled(Texture2D src, int width)
    {
        int w = Math.Min(width + src.Width, 16384), h = src.Height;
        var data = new Color[src.Width * h];
        src.GetData(data);
        var outData = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                outData[y * w + x] = data[y * src.Width + (x % src.Width)];
        var tex = new Texture2D(Game.GraphicsDevice, w, h);
        tex.SetData(outData);
        _psSkyTileWidth = src.Width;
        return tex;
    }
    private int _psSkyTileWidth = 1;

    private void PsResetCar(bool left, bool right)
    {
        if (left)
        {
            _psCarWaiting = false; _psCarInterruptable = true; _psCar1.Active = false;
            SetCarPose("phillyCars", new Vector2(1200, 818), 0f, Vector2.Zero);
        }
        if (right)
        {
            _psCar2Interruptable = true; _psCar2.Active = false;
            SetCarPose("phillyCars2", new Vector2(1200, 818), 0f, Vector2.Zero);
        }
    }

    private void PsResetStageValues()
    {
        _psLastChange = 0; _psChangeInterval = 8;
        PlayAnimatedProp("phillyTraffic", "togreen");
        _psLightsStop = false;
    }

    private void PsChangeLights(int beat)
    {
        _psLastChange = beat;
        _psLightsStop = !_psLightsStop;
        if (_psLightsStop) { PlayAnimatedProp("phillyTraffic", "tored"); _psChangeInterval = 20; }
        else
        {
            PlayAnimatedProp("phillyTraffic", "togreen"); _psChangeInterval = 30;
            if (_psCarWaiting) PsFinishCarLights();
        }
    }

    private float RandF(float a, float b) => a + (float)_psRng.NextDouble() * (b - a);
    private int RandI(int a, int b) => _psRng.Next(a, b + 1);          // FlxG.random.int is inclusive
    private bool RandBool(float chancePercent) => _psRng.NextDouble() * 100.0 < chancePercent;

    private (Vector2 extra, float duration) PsCarVariant(string prop, bool lights)
    {
        int variant = RandI(1, 4);
        PlayAnimatedProp(prop, "car" + variant);
        Vector2 extra = Vector2.Zero; float duration = 2f;
        switch (variant)
        {
            case 1: duration = RandF(1f, 1.7f); break;
            case 2: extra = new Vector2(20, -15); duration = lights ? RandF(0.9f, 1.5f) : RandF(0.6f, 1.2f); break;
            case 3: extra = new Vector2(30, 50); duration = RandF(1.5f, 2.5f); break;
            case 4: extra = new Vector2(10, 60); duration = RandF(1.5f, 2.5f); break;
        }
        return (extra, duration);
    }

    private static readonly Vector2 PsOff = new(306.6f, 168.3f);

    private void PsStart(CarTween t, Vector2 a, Vector2 b, Vector2 c, float ang0, float ang1, float duration,
                         Func<float, float> ease, Vector2 extra, float delay, Action onComplete)
    {
        t.A = a; t.B = b; t.C = c; t.Angle0 = ang0; t.Angle1 = ang1; t.Duration = duration;
        t.Ease = ease; t.ExtraOffset = extra; t.Delay = delay; t.Elapsed = 0f; t.OnComplete = onComplete; t.Active = true;
    }

    private void PsFinishCarLights()   // finishCarLights(phillyCars)
    {
        _psCarWaiting = false;
        float duration = RandF(1.8f, 3f);
        float delay = RandF(0.2f, 1.2f);
        PsStart(_psCar1,
            new Vector2(1950 - PsOff.X - 80, 980 - PsOff.Y + 15),
            new Vector2(2400 - PsOff.X, 980 - PsOff.Y - 50),
            new Vector2(3102 - PsOff.X, 1187 - PsOff.Y + 40),
            -5, 18, duration, EaseSineIn, _psCar1.ExtraOffset, delay, () => _psCarInterruptable = true);
    }

    private void PsDriveCarLights()    // driveCarLights(phillyCars)
    {
        _psCarInterruptable = false;
        var (extra, duration) = PsCarVariant("phillyCars", lights: true);
        PsStart(_psCar1,
            new Vector2(1500 - PsOff.X - 20, 1049 - PsOff.Y - 20),
            new Vector2(1770 - PsOff.X - 80, 994 - PsOff.Y + 10),
            new Vector2(1950 - PsOff.X - 80, 980 - PsOff.Y + 15),
            -7, -5, duration, EaseCubeOut, extra, 0f, () =>
            {
                _psCarWaiting = true;
                if (!_psLightsStop) PsFinishCarLights();
            });
    }

    private void PsDriveCar()          // driveCar(phillyCars)
    {
        _psCarInterruptable = false;
        var (extra, duration) = PsCarVariant("phillyCars", lights: false);
        PsStart(_psCar1,
            new Vector2(1570 - PsOff.X, 1049 - PsOff.Y - 30),
            new Vector2(2400 - PsOff.X, 980 - PsOff.Y - 50),
            new Vector2(3102 - PsOff.X, 1187 - PsOff.Y + 40),
            -8, 18, duration, EaseLinear, extra, 0f, () => _psCarInterruptable = true);
    }

    private void PsDriveCarBack()      // driveCarBack(phillyCars2)
    {
        _psCar2Interruptable = false;
        var (extra, duration) = PsCarVariant("phillyCars2", lights: false);
        PsStart(_psCar2,
            new Vector2(3102 - PsOff.X, 1127 - PsOff.Y + 60),
            new Vector2(2400 - PsOff.X, 980 - PsOff.Y - 30),
            new Vector2(1570 - PsOff.X, 1049 - PsOff.Y - 10),
            18, -8, duration, EaseLinear, extra, 0f, () => _psCar2Interruptable = true);
    }

    private void PsStepTween(CarTween t, string prop, float dt)
    {
        if (!t.Active) return;
        if (t.Delay > 0f) { t.Delay -= dt; if (t.Delay > 0f) return; dt = -t.Delay; t.Delay = 0f; }
        t.Elapsed += dt;
        float raw = t.Duration <= 0f ? 1f : Math.Min(1f, t.Elapsed / t.Duration);
        float e = t.Ease(raw);
        float u = 1f - e;
        Vector2 pos = u * u * t.A + 2f * u * e * t.B + e * e * t.C;   // quadratic Bezier (FlxTween.quadPath)
        float ang = t.Angle0 + (t.Angle1 - t.Angle0) * e;
        SetCarPose(prop, pos, ang, t.ExtraOffset);
        if (raw >= 1f) { t.Active = false; t.OnComplete?.Invoke(); }
    }

    /// <summary>onUpdate(): rain intensity/time, sky scroll, car tweens.</summary>
    private void PhillyStreetsUpdate(float dt)
    {
        if (_playLoading) return; // stage props are (re)built while loading: init afterwards so sky/mist stay in _staticProps
        if (!_psInit) PhillyStreetsInit();
        if (!_psActive) return;

        _rainTime += dt;
        if (_psSky != null)
        {
            _psSkyScrollX -= dt * 22f;
            int tile = Math.Max(1, _psSkyTileWidth);
            int sx = (int)(((-_psSkyScrollX) % tile + tile) % tile);
            _psSky.SourceRect = new Rectangle(sx, 0, 2922, 718);
        }
        PsStepTween(_psCar1, "phillyCars", dt);
        PsStepTween(_psCar2, "phillyCars2", dt);    PsUpdateMist(dt);
    }

    /// <summary>onBeatHit()</summary>
    private void PhillyStreetsBeatHit(int beat)
    {
        if (!_psActive) return;
        if (RandBool(10) && beat != (_psLastChange + _psChangeInterval) && _psCarInterruptable)
        {
            if (!_psLightsStop) PsDriveCar(); else PsDriveCarLights();
        }
        if (RandBool(10) && beat != (_psLastChange + _psChangeInterval) && _psCar2Interruptable && !_psLightsStop)
            PsDriveCarBack();
        if (beat == (_psLastChange + _psChangeInterval)) PsChangeLights(beat);
    }

    // ------------------------------------------------------------------ rain post-effect

    private Effect RainEffect
    {
        get
        {
            if (_rainEffect != null || _rainLoadTried) return _rainEffect;
            _rainLoadTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/Rain.mgfx")
                              ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "Rain.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _rainEffect = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                else Console.WriteLine("[RAIN] Rain.mgfx not found");
            }
            catch (Exception ex) { Console.WriteLine($"[RAIN] {ex.Message}"); }
            return _rainEffect;
        }
    }

    /// <summary>Redirect the world pass into an offscreen target (FlxG.camera.filters = [rain]).</summary>
    private void BeginRainWorldPass()
    {
        _rainPassActive = false;
        if (!(_psActive || _bzActive) || RainEffect == null) return;
        var gd = Game.GraphicsDevice;
        if (_rainWorldRT == null || _rainWorldRT.IsDisposed)
            _rainWorldRT = new RenderTarget2D(gd, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT);
        _rainPrevTargets = gd.GetRenderTargets();
        gd.SetRenderTarget(_rainWorldRT);
        gd.Clear(Color.Black);   // camera bgColor
        _rainPassActive = true;
    }

    /// <summary>Composite the world through the rain shader back onto the game target.</summary>
    private void EndRainWorldPass(SpriteBatch sb, float zoom)
    {
        if (!_rainPassActive) return;
        _rainPassActive = false;
        var gd = Game.GraphicsDevice;
        gd.SetRenderTargets(_rainPrevTargets);

        // FlxMath.remapToRange(songPosition, 0, music.length, start, end). Conductor.SongPosition and
        // MusicLength are both in seconds here (the original uses ms for both; the ratio is the same).
        float songLen = (float)Audio.MusicLength;
        float pos = (float)(_conductor?.SongPosition ?? 0);
        float intensity = _bzActive ? 0.5f   // phillyBlazin.hxc: rainShader.intensity = 0.5
            : songLen > 0 ? _rainStart + (pos / songLen) * (_rainEnd - _rainStart) : _rainStart;

        // camera.viewLeft/Top/Right/Bottom (world units at scroll factor 1)
        float halfW = FNFGame.SCREEN_WIDTH / (2f * zoom), halfH = FNFGame.SCREEN_HEIGHT / (2f * zoom);
        var fx = RainEffect;
        fx.Parameters["uScreenResolution"]?.SetValue(new Vector2(FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT));
        fx.Parameters["uCameraBounds"]?.SetValue(new Vector4(_camWorldX - halfW, _camWorldY - halfH, _camWorldX + halfW, _camWorldY + halfH));
        fx.Parameters["uScale"]?.SetValue(FNFGame.SCREEN_HEIGHT / 200f);
        fx.Parameters["uIntensity"]?.SetValue(intensity);
        fx.Parameters["uTime"]?.SetValue(_rainTime);
        fx.Parameters["uRainColor"]?.SetValue(_bzActive ? new Vector3(0x66 / 255f, 0x80 / 255f, 0xCC / 255f) : _rainColor);

        sb.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null, fx);
        sb.Draw(_rainWorldRT, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.White);
        sb.End();
    }
}
