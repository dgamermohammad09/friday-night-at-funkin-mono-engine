using System;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine Loading State.
/// Original: states/LoadingState.hx
/// Ported to MonoGame as LoadingScene.
/// Manages asynchronous loading of heavy assets (songs, characters) before a PlayScene.
/// </summary>
public class LoadingScene : Scene
{
    private readonly Scene _targetScene;
    private readonly bool _stopMusic;
    private bool _dontUpdate;
    private bool _finishedLoading;
    private bool _transitioning;
    
    // UI Elements
    private float _intendedPercent = 0f;
    private float _curPercent = 0f;
    private float _stateChangeDelay = 0f;
    private float _timePassed = 0f;
    
    private Engine.SpriteSheet _funkay;

    // Loading State
    public static int Loaded { get; private set; }
    public static int LoadMax { get; private set; }
    private static bool _initialThreadCompleted;

    public LoadingScene(Scene targetScene, bool stopMusic = false)
    {
        _targetScene = targetScene;
        _stopMusic = stopMusic;
    }

    public override void Load()
    {
        base.Load();
        
        string funkayPath = Engine.PsychEngine.PsychPaths.Image("funkay");
        if (!string.IsNullOrEmpty(funkayPath) && global::FNF_MonoGame.Engine.FsCI.FileExists(funkayPath))
        {
            _funkay = Engine.SpriteSheet.Load(FNFGame.Instance, System.IO.Path.ChangeExtension(funkayPath, null));
        }

        // Start asynchronous load
        _initialThreadCompleted = false;
        Loaded = 0;
        LoadMax = 100; // Simulated for now unless deeply integrated with AssetManager

        Task.Run(async () =>
        {
            // Simulate deep asset loading (Psych engine threaded loading)
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(10); // Simulated delay
                Loaded = i;
            }
            Loaded = LoadMax;
            _initialThreadCompleted = true;
        });
    }

    public override void Unload()
    {
        if (_stopMusic && FNFGame.Instance.Audio?.Music != null)
        {
            FNFGame.Instance.Audio.Music.Stop();
        }
    }

    public override void Update(GameTime gameTime)
    {
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_dontUpdate) return;

        if (!_transitioning)
        {
            if (!_finishedLoading && CheckLoaded())
            {
                if (_stateChangeDelay <= 0)
                {
                    _transitioning = true;
                    OnLoad();
                    return;
                }
                else
                {
                    _stateChangeDelay = Math.Max(0, _stateChangeDelay - elapsed);
                }
            }
            if (LoadMax > 0)
                _intendedPercent = (float)Loaded / LoadMax;
        }

        if (_curPercent != _intendedPercent)
        {
            if (Math.Abs(_curPercent - _intendedPercent) < 0.001f)
                _curPercent = _intendedPercent;
            else
                _curPercent = MathHelper.Lerp(_curPercent, _intendedPercent, elapsed * 15f);
        }

        _timePassed += elapsed;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Draw Funkay background (Base Game Loading Screen)
        if (_funkay != null && _funkay.Texture != null)
        {
            spriteBatch.Draw(_funkay.Texture, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.White);
        }
        else
        {
            // Fallback lime green background
            Engine.AssetManager assets = FNFGame.Instance.Assets;
            spriteBatch.Draw(assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(0xCA, 0xFF, 0x4D, 0xFF));
        }

        // Draw loading bar
        Engine.AssetManager am = FNFGame.Instance.Assets;
        
        int barBackWidth = FNFGame.SCREEN_WIDTH - 300;
        int barBackHeight = 25;
        int barBackX = (FNFGame.SCREEN_WIDTH - barBackWidth) / 2;
        int barBackY = 660;
        
        spriteBatch.Draw(am.Pixel, new Rectangle(barBackX, barBackY, barBackWidth, barBackHeight), Color.Black);
        
        int barWidth = (int)((barBackWidth - 10) * _curPercent);
        if (barWidth > 0)
        {
            spriteBatch.Draw(am.Pixel, new Rectangle(barBackX + 5, barBackY + 5, barWidth, barBackHeight - 10), Color.White);
        }

        spriteBatch.End();
    }

    private bool CheckLoaded()
    {
        return Loaded >= LoadMax && _initialThreadCompleted;
    }

    private void OnLoad()
    {
        _finishedLoading = true;
        _dontUpdate = true;
        FNFGame.Instance.Scenes.ChangeScene(_targetScene);
    }
}
