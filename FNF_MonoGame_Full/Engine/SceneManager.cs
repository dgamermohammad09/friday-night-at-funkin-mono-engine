using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Base class for all game scenes
/// </summary>
public abstract class Scene
{
#if XBOX_UWP
    protected XboxGame Game => XboxGame.Instance;
    protected AssetManager Assets => Game.Assets;
    protected InputManager Input => Game.Input;
    protected XboxAudioManager Audio => Game.Audio;
#else
    protected FNFGame Game => FNFGame.Instance;
    protected AssetManager Assets => Game.Assets;
    protected InputManager Input => Game.Input;
    protected AudioManager Audio => Game.Audio;
#endif
    /// <summary>Mouse position in virtual 1280x720 coordinates (-1,-1 when outside the game area).</summary>
    protected Vector2 MouseVirtual
    {
        get
        {
            var d = Game.GetScaledDestination(); var m = Input.RawMousePosition;
            if (d.Width <= 0 || !d.Contains(m)) return new Vector2(-1, -1);
            return new Vector2((m.X - d.X) * 1280f / d.Width, (m.Y - d.Y) * 720f / d.Height);
        }
    }
    
    public virtual void Load() {} // ();
    public virtual void Unload() {} // 
        public virtual void OnBeatHit() {}
    public virtual void Update(GameTime gameTime) {}
    public virtual void Draw(SpriteBatch spriteBatch) {}
    /// <summary>True: the game renders this scene into a back-buffer sized target (crisp on big screens). The scene still
    /// works in 1280x720 logical units but must scale its own batches (ModernUI.Begin does it).</summary>
    public virtual bool NativeResolution => false;
}

/// <summary>
/// Manages scene transitions and the current active scene.
/// Supports fade-to-black transitions between scenes.
/// </summary>
public class SceneManager
{
    /// <summary>Dev clock: game time (sum of update deltas) since the first scene was loaded (FNF_SHOTS_FROMSCENE=1 time base for FNF_SHOTS and FNF_DEVKEYS).</summary>
    public static double DevSceneGameTime = -1;

    public Scene CurrentScene { get; private set; }
    private Scene _nextScene;
    private bool _transitioning;
    
    // Fade transition
    private float _fadeAlpha;
    private float _fadeDuration = 0.3f;
    private bool _fadingOut;
    private bool _fadingIn;
    
    public void ChangeScene(Scene newScene)
    {
        // If called during Unload (transitioning phase), override the pending scene
        if (_transitioning)
        {
            _nextScene = newScene;
            return;
        }
        
        // Guard: ignore if already fading out to a scene
        if (_fadingOut || _nextScene != null)
            return;
            
        _nextScene = newScene;
        if (CurrentScene != null)
        {
            _fadingOut = true;
            _fadeAlpha = 0f;
        }
        else
        {
            _transitioning = true;
        }
    }
    
    /// <summary>
    /// Swap scenes immediately, without the fade (FlxSubState close/open: Freeplay over MainMenu).
    /// <paramref name="load"/>=false re-activates an already loaded scene; <paramref name="unloadOld"/>=false keeps
    /// the old scene alive (e.g. MainMenu kept as Freeplay's parent).
    /// </summary>
    public void SwitchSceneInstant(Scene newScene, bool load = true, bool unloadOld = true)
    {
        if (unloadOld) CurrentScene?.Unload();
        CurrentScene = newScene;
        if (load) LoadTimed(CurrentScene);
        _nextScene = null;
        _fadingOut = _fadingIn = _transitioning = false;
        _fadeAlpha = 0f;
    }

    public void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (DevSceneGameTime >= 0) DevSceneGameTime += dt;
        
        // Fade out current scene
        if (_fadingOut)
        {
            _fadeAlpha += dt / _fadeDuration;
            if (_fadeAlpha >= 1f)
            {
                _fadeAlpha = 1f;
                _fadingOut = false;
                _transitioning = true;
            }
        }
        
        // Handle scene swap at peak of fade
        if (_transitioning && _nextScene != null)
        {
            CurrentScene?.Unload();
            CurrentScene = _nextScene;
            LoadTimed(CurrentScene);
            if (DevSceneGameTime < 0) DevSceneGameTime = 0;
            _nextScene = null;
            _transitioning = false;
            _fadingIn = true;
        }
        
        // Fade in new scene
        if (_fadingIn)
        {
            _fadeAlpha -= dt / _fadeDuration;
            if (_fadeAlpha <= 0f)
            {
                _fadeAlpha = 0f;
                _fadingIn = false;
            }
        }
        
        if (_crashToMenu) { _crashToMenu = false; _drawErrors = 0; GoToMenuAfterCrash(); }
        try
        {
            var __pu = PerfStats.Begin();
            CurrentScene?.Update(gameTime);
            PerfStats.End(0, __pu);
            StickerTransition.Update(dt);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Scene Update error: {ex.Message}\n{ex.StackTrace}");
            CrashHandler.Report(ex, "Update", CurrentScene?.GetType().Name);
            // Fall back to main menu on unhandled scene error
            try
            {
                CurrentScene?.Unload();
                CurrentScene = new FNF_MonoGame.Scenes.MainMenuScene();
                LoadTimed(CurrentScene);
                _nextScene = null;
                _fadingOut = false;
                _fadingIn = false;
                _transitioning = false;
                _fadeAlpha = 0;
            }
            catch { } // Last resort: if main menu also fails, let it crash
        }
    }
    
    private int _drawErrors; private bool _crashToMenu;
    private void GoToMenuAfterCrash()
    {
        try
        {
            CurrentScene?.Unload();
            CurrentScene = new FNF_MonoGame.Scenes.MainMenuScene();
            LoadTimed(CurrentScene);
            _nextScene = null; _fadingOut = false; _fadingIn = false; _transitioning = false; _fadeAlpha = 0;
        }
        catch { }
    }

    // Load-time measurement (Options > engine: faster loading). "[LOADTIME] scene load=ms firstFrame=ms" in game_log.txt.
    private System.Diagnostics.Stopwatch _loadSw; private string _loadName; private double _loadMs;
    public static double LastLoadMs, LastFirstFrameMs;
    private void LoadTimed(Scene s)
    {
        _loadSw = System.Diagnostics.Stopwatch.StartNew(); _loadName = s.GetType().Name; _readySw = _loadSw;
        CrashHandler.Note($"{System.DateTime.Now:HH:mm:ss} scene {_loadName}");
        s.Load();
        _loadMs = _loadSw.Elapsed.TotalMilliseconds; LastLoadMs = _loadMs;
        MemStats.Log($"load {_loadName}");
    }
    private static System.Diagnostics.Stopwatch _readySw;
    /// <summary>Scene finished its deferred loading (PlayScene: stage, characters, notes, song audio ready).</summary>
    public static void MarkReady(string what)
    {
        if (_readySw == null) return;
        System.Console.WriteLine($"[LOADTIME] ready ({what}) total={_readySw.Elapsed.TotalMilliseconds:F0} ms texCache={SpriteSheetTextureCache.Stats}");
        _readySw = null;
        SpriteSheetTextureCache.TrimToBudget();
        MemStats.Log($"ready {what}");
    }
    private void EndLoadTiming()
    {
        if (_loadSw == null) return;
        LastFirstFrameMs = _loadSw.Elapsed.TotalMilliseconds;
        System.Console.WriteLine($"[LOADTIME] {_loadName} load={_loadMs:F0} ms firstFrame={LastFirstFrameMs:F0} ms texCache={SpriteSheetTextureCache.Stats}");
        _loadSw = null;
        if (_loadName != "PlayScene") SpriteSheetTextureCache.TrimToBudget();   // PlayScene loads its atlases after the first frame (MarkReady trims)
        MemStats.Log($"firstFrame {_loadName}");
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        try
        {
            var __pd = PerfStats.Begin();
            CurrentScene?.Draw(spriteBatch);
            PerfStats.End(1, __pd); PerfStats.FrameDone(CurrentScene?.GetType().Name);
            _drawErrors = 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Scene Draw error: {ex.Message}");
            if (++_drawErrors == 1) CrashHandler.Report(ex, "Draw", CurrentScene?.GetType().Name);
            if (_drawErrors >= 3) _crashToMenu = true; // a scene that keeps failing to draw -> main menu (next Update)
            // If Draw threw mid-SpriteBatch.Begin/End, reset the batch state
            try { spriteBatch.End(); } catch { }
        }
        
        // Draw fade overlay
        if (_fadeAlpha > 0.001f)
        {
            var gd = FNFGame.Instance.GraphicsDevice;
            var pixel = FNFGame.Instance.Assets?.Pixel;
            if (pixel != null)
            {
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
                spriteBatch.Draw(pixel,
                    new Rectangle(0, 0, gd.Viewport.Width, gd.Viewport.Height),   // viewport = current (1280x720 or native) target
                    Color.Black * _fadeAlpha);
                spriteBatch.End();
            }
        }
        StickerTransition.Draw(spriteBatch);
        EndLoadTiming();
        { var vp = FNFGame.Instance.GraphicsDevice.Viewport; CrashHandler.DrawNotice(spriteBatch, FNFGame.Instance.Assets, vp.Width, vp.Height);
          if (CurrentScene is FNF_MonoGame.Scenes.MainMenuScene) { UpdateCheck.StartOnce(); UpdateCheck.DrawNotice(spriteBatch, FNFGame.Instance.Assets, vp.Width, vp.Height); } }
    }
}
