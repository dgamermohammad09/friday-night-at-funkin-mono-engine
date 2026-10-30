using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Audio;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame;

/// <summary>
/// Friday Night Funkin' - MonoGame Port
/// Main game class that handles game loop and scene management
/// </summary>
public class FNFGame : Game
{
    public static FNFGame Instance { get; private set; }

    public GraphicsDeviceManager Graphics { get; private set; }
    public SpriteBatch SpriteBatch { get; private set; }
    public AssetManager Assets { get; private set; }
    public InputManager Input { get; private set; }
    public AudioManager Audio { get; private set; }
    public SceneManager Scenes { get; private set; }
    public Engine.DiscordRPCManager Discord { get; private set; }
    public Engine.VolumeOverlay VolumeOverlay { get; private set; }
    
    // Screen settings
    public const int SCREEN_WIDTH = 1280;
    public const int SCREEN_HEIGHT = 720;
    
    // FPS counter
    private int _frameCount;
    private float _fpsTimer;
    public int CurrentFPS { get; private set; } = 60;
    private string _fpsText = "60 FPS";
    
    // Fullscreen toggle state
    private KeyboardState _prevKbState;
    private bool _isFullscreen;
    private bool _isBorderless;
    
    // Render target for resolution-independent rendering
    // All scenes draw at SCREEN_WIDTH x SCREEN_HEIGHT, then this is scaled to the back buffer
    private RenderTarget2D _renderTarget;
    // Scenes with NativeResolution (modern main menu / multiplayer menus) draw into a back-buffer sized target so text,
    // icons and shapes stay crisp on 1440p / 4K screens and TVs (logical coordinates stay 1280x720; see ModernUI).
    private RenderTarget2D _nativeTarget;
    private static readonly string DevRenderSize = Environment.GetEnvironmentVariable("FNF_RENDER_SIZE");   // dev: "2560x1440"
    private RenderTarget2D PickSceneTarget(Rectangle dest)
    {
        if (Scenes?.CurrentScene == null || !Scenes.CurrentScene.NativeResolution || Engine.StickerTransition.Active) return _renderTarget;
        int w = dest.Width;
        if (!string.IsNullOrEmpty(DevRenderSize) && int.TryParse(DevRenderSize.Split('x')[0], out var dw)) w = dw;
        w = Math.Clamp(w, SCREEN_WIDTH, 7680);
        if (w == SCREEN_WIDTH) return _renderTarget;
        int h = (int)Math.Round(w * (double)SCREEN_HEIGHT / SCREEN_WIDTH);
        if (_nativeTarget == null || _nativeTarget.Width != w || _nativeTarget.Height != h)
        {
            _nativeTarget?.Dispose();
            _nativeTarget = new RenderTarget2D(GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        }
        return _nativeTarget;
    }

    // True only when the window auto-paused audio because it lost focus. Prevents Activated
    // from resuming music that was paused by the user via the in-game pause menu.
    private bool _focusAutoPaused;
    private bool _inResize;


    /// <summary>
    /// Get the destination rectangle for drawing the render target onto the back buffer,
    /// maintaining aspect ratio with letterboxing/pillarboxing.
    /// </summary>
    public Rectangle GetScaledDestination()
    {
        int bufW = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int bufH = GraphicsDevice.PresentationParameters.BackBufferHeight;
        float scaleX = (float)bufW / SCREEN_WIDTH;
        float scaleY = (float)bufH / SCREEN_HEIGHT;
        float scale = Math.Min(scaleX, scaleY);
        int w = (int)(SCREEN_WIDTH * scale);
        int h = (int)(SCREEN_HEIGHT * scale);
        int x = (bufW - w) / 2;
        int y = (bufH - h) / 2;
        return new Rectangle(x, y, w, h);
    }
    
    public FNFGame()
    {
        Instance = this;
        Graphics = new GraphicsDeviceManager(this);
        // HiDef required: FNF spritesheets (e.g. menus/main_menu/buttons.png = 7914x308)
        // exceed the 4096 texture-size limit of the default Reach profile (FL 9.3).
        // HiDef targets FL 10.0+ (>=8192) and typically uses FL 11.0 (16384).
        Graphics.GraphicsProfile = GraphicsProfile.HiDef;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        
        Graphics.PreferredBackBufferWidth = SCREEN_WIDTH;
        Graphics.PreferredBackBufferHeight = SCREEN_HEIGHT;
        Graphics.SynchronizeWithVerticalRetrace = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0); // 60 FPS
#if ANDROID
        Graphics.IsFullScreen = true;
        Graphics.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
#endif
#if TOUCH_TEST
        Microsoft.Xna.Framework.Input.Touch.TouchPanel.EnableMouseTouchPoint = true;   // dev: test the Android touch UI with a mouse
#endif
    }

    protected override void Initialize()
    {
        Window.Title = "Friday Night Funkin' - MonoGame Engine";
        // Resizable window: enables the maximize button; the backbuffer follows the client size.
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (s, e) =>
        {
            if (IsFullscreenAny || _inResize) return;
            var cb = Window.ClientBounds;
            if (cb.Width < 64 || cb.Height < 64) return;
            if (cb.Width == Graphics.PreferredBackBufferWidth && cb.Height == Graphics.PreferredBackBufferHeight) return;
            _inResize = true;
            Graphics.PreferredBackBufferWidth = cb.Width;
            Graphics.PreferredBackBufferHeight = cb.Height;
            Graphics.ApplyChanges();
            _inResize = false;
        };
        
        // Initialize managers
        Input = new InputManager();
        Audio = new AudioManager();
        Scenes = new SceneManager();
        Discord = new Engine.DiscordRPCManager();
        Discord.Initialize();
        VolumeOverlay = new Engine.VolumeOverlay();
        
        // Auto-pause on focus loss. Only resume on Activated if WE were the ones who auto-paused
        // due to focus loss � otherwise clicking back on the window un-pauses music that the user
        // deliberately paused via the in-game pause menu.
        Activated += (s, e) => OnFocusGained();
        Deactivated += (s, e) => OnFocusLost();
        
        base.Initialize();
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        _renderTarget = new RenderTarget2D(GraphicsDevice, SCREEN_WIDTH, SCREEN_HEIGHT, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents); // scenes may switch to a temp RT (camHUD.alpha layer) and back: DiscardContents would clear the frame
        Assets = new AssetManager(this);

        // Note: This project copies raw files from `Content/` and does not use an MGCB pipeline.
        // MonoGame effects require precompiled `.mgfxo` built via the content pipeline.
        // If/when MGCB is introduced, we can load and apply the Freeplay BlueFade effect here.
        
        // Share funkin.assets search roots with AudioManager
        // AssetManager auto-detects the funkin.assets directory on construction
        var funkinPath = AssetManager.FindFunkinAssets();
        if (funkinPath != null)
            Audio.ExtraContentRoots.Add(funkinPath);
        
        // Load common assets
        Assets.LoadCommonAssets();
        
        // Load saved preferences (volume, character selection, etc.)
        var saveData = Engine.HighscoreManager.Data;
        Audio.MusicVolume = saveData.MusicVolume;
        Audio.SfxVolume = saveData.SfxVolume;
        Audio.PreloadCommonSfx(); // decode menu/countdown/miss SFX now: the first press must not wait on (or lose) a decode
        Audio.Muted = saveData.Mute;
        
        // Load volume overlay textures
        VolumeOverlay?.LoadTextures(Assets);
        
        // Load custom key bindings
        Input.LoadBindings();
        ApplyDisplaySettings();
        // Default (new and old saves, once): start in borderless fullscreen-windowed. Dev/test runs stay windowed.
        if (Engine.HighscoreManager.Data.WindowPrefsVersion < 1)
        {
            Engine.HighscoreManager.Data.WindowPrefsVersion = 1;
            Engine.HighscoreManager.Data.LaunchFullscreen = true;
            try { Engine.HighscoreManager.SavePreferences(); } catch { }
        }
        bool devRun = Environment.GetEnvironmentVariable("FNF_SHOTS") != null || Environment.GetEnvironmentVariable("FNF_AUTOPLAY_SONG") != null
                      || Environment.GetEnvironmentVariable("FNF_START_SCENE") != null || Environment.GetEnvironmentVariable("FNF_WINDOWED") == "1";
#if !ANDROID
        if (Engine.HighscoreManager.Data.LaunchFullscreen && !IsFullscreenAny && !devRun) ToggleFullscreen(true);
        if (Environment.GetEnvironmentVariable("FNF_DEV_BORDERLESS") == "1" && !IsFullscreenAny) ToggleFullscreen(true);   // dev: big-screen checks
        // dev: FNF_DEV_BACKBUFFER=1920x1080 -> windowed run with that back-buffer size (native-resolution UI checks)
        if (Environment.GetEnvironmentVariable("FNF_DEV_BACKBUFFER") is string devBb && devBb.Split('x') is { Length: 2 } bbp
            && int.TryParse(bbp[0], out int bbw) && int.TryParse(bbp[1], out int bbh))
        { _inResize = true; Graphics.PreferredBackBufferWidth = bbw; Graphics.PreferredBackBufferHeight = bbh; Graphics.ApplyChanges(); _inResize = false; }
#endif

        // Initialize mod system
        ModManager.Instance.Initialize(AppContext.BaseDirectory);

        // Initialize Psych Engine systems
        Engine.PsychEngine.ClientPrefs.Init();
        Engine.PsychEngine.Achievements.Load();
        Engine.PsychEngine.PsychHighscore.Load();
        string modsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "mods");
        Engine.PsychEngine.PsychMods.Init(modsDir); Engine.VSliceMods.Refresh(); Engine.PsychEngine.ClientPrefs.EnsureLoaded();   // Psych / Psych Online settings (Options)
        Engine.PsychEngine.PsychPaths.Init(
            System.IO.Path.Combine(AppContext.BaseDirectory, "Content"),
            modsDir
        );
        Engine.PsychEngine.Achievements.LoadModAchievements();

        // Original FNF boots into TitleState (same as the Xbox build's LoadingScene -> TitleScene).
        // Dev hook: FNF_AUTOPLAY_SONG=<songId> [FNF_AUTOPLAY_DIFF=normal] boots straight into a song
        // in botplay, used for automated position verification. No effect when unset.
        string autoSong = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_SONG");
        if (!string.IsNullOrEmpty(autoSong) && Environment.GetEnvironmentVariable("FNF_AUTOPLAY_WEEKID") is string wid && wid.Length > 0)
            FNF_MonoGame.Scenes.PlayScene.ResetStoryCampaign(Environment.GetEnvironmentVariable("FNF_AUTOPLAY_WEEKTITLE") ?? wid);
        if (FNF_MonoGame.Scenes.DevPreloaderScene.Requested)   // dev: FNF_DEV_DLPREVIEW=<0..1>|error|orig = Xbox first-start download screen
            Scenes.ChangeScene(new FNF_MonoGame.Scenes.DevPreloaderScene());
        else if (!string.IsNullOrEmpty(autoSong))
            Scenes.ChangeScene(new Scenes.PlayScene(new Scenes.PlayStateParams
            {
                SongName = autoSong,
                TargetDifficulty = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_DIFF") ?? "normal",
                BotPlayMode = true,
                // Dev hook: FNF_AUTOPLAY_WEEKSONGS="ugh,guns,stress" plays the song as part of a story week (index of FNF_AUTOPLAY_SONG)
                WeekSongs = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_WEEKSONGS") is string ws && ws.Length > 0 ? new System.Collections.Generic.List<string>(ws.Split(',')) : null,
                WeekSongIndex = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_WEEKSONGS") is string ws2 && ws2.Length > 0 ? Math.Max(0, System.Array.IndexOf(ws2.Split(','), autoSong)) : 0,
                WeekId = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_WEEKID"), // + FNF_AUTOPLAY_WEEKSONGS = story level in botplay (story Results)
            }));
        else if (Environment.GetEnvironmentVariable("FNF_START_SCENE") == "results")
        {
            // Dev hook: FNF_RESULTS="sick,good,bad,shit,missed,totalHit,total,maxCombo,score[,char[,song[,diff]]]"
            var r = (Environment.GetEnvironmentVariable("FNF_RESULTS") ?? "70,5,3,2,10,80,90,50,123456").Split(',');
            int I(int i) => int.Parse(r[i]);
            Scenes.ChangeScene(new Scenes.ResultsScene(I(8), I(7), I(4), r.Length > 10 ? r[10] : "bopeebo", r.Length > 11 ? r[11] : "normal", true,
                I(0), I(1), I(2), I(3), I(4), I(5), I(6)) { DevCharacterId = r.Length > 9 ? r[9] : null, Title = "Bopeebo by Kawai Sprite", IsBotPlayMode = Environment.GetEnvironmentVariable("FNF_RES_BOTPLAY") == "1" });
        }
        else if (Environment.GetEnvironmentVariable("FNF_START_SCENE") is "mainmenu" or "mods" or "skins" or "achievements" or "options" or "story" or "credits")
        {   // dev hooks for automated menu checks
            string sc = Environment.GetEnvironmentVariable("FNF_START_SCENE");
            Scenes.ChangeScene(sc == "mods" ? new Scenes.ModsScene() : sc == "skins" ? new Scenes.SkinsScene() : sc == "achievements" ? new Scenes.AchievementsScene()
                : sc == "options" ? new Scenes.OptionsScene() : sc == "story" ? new Scenes.StoryModeScene() : sc == "credits" ? new Scenes.CreditsScene() : new Scenes.MainMenuScene());
        }
        else if (Environment.GetEnvironmentVariable("FNF_START_SCENE") == "editors")
            Scenes.ChangeScene(new FNF_MonoGame.Scenes.Editors.EditorsHubScene()); // dev hook: FNF_EDITOR_OPEN=<card index>
        else if (Environment.GetEnvironmentVariable("FNF_START_SCENE") == "multiplayer")
            Scenes.ChangeScene(new Scenes.MultiplayerScene()); // dev hook: FNF_MP_AUTO=host|join (see MultiplayerScene)
        else if (Environment.GetEnvironmentVariable("FNF_START_SCENE") == "charselect")
            Scenes.ChangeScene(new Scenes.CharacterSelectScene()); // dev hook (CharSelectSubState icons)
        else if (Environment.GetEnvironmentVariable("FNF_START_SCENE") == "psychonline")
            Scenes.ChangeScene(new FNF_MonoGame.Scenes.PsychOnlineScene());
        else if (Environment.GetEnvironmentVariable("FNF_START_SCENE") == "modbrowser")
            Scenes.ChangeScene(new FNF_MonoGame.Scenes.ModBrowserScene()); // dev hook: FNF_GB_SEARCH / FNF_GB_OPEN / FNF_GB_INSTALL
        else if (Environment.GetEnvironmentVariable("FNF_START_SCENE") == "freeplay")
            { // dev hook for automated menu checks; FNF_FROMRESULTS="song,diff,oldRank|none,newRank" = return-from-results rank animation
            var fr = Environment.GetEnvironmentVariable("FNF_FROMRESULTS");
            if (!string.IsNullOrEmpty(fr)) { var p = fr.Split(',');
                Scenes.ChangeScene(new Scenes.FreeplayScene(new Scenes.FreeplayResultParams { SongId = p[0], DifficultyId = p[1],
                    OldRank = Enum.TryParse<Scenes.ScoringRank>(p[2], true, out var o) ? o : null, NewRank = Enum.Parse<Scenes.ScoringRank>(p[3], true) })); }
            else Scenes.ChangeScene(new Scenes.FreeplayScene()); }
        else
            Scenes.ChangeScene(FNF_MonoGame.Scenes.StartupNoticeScene.Gate(new Scenes.TitleScene()));   // startup NOTICE first (skipped in dev runs / after DON'T SHOW AGAIN)
    }
    
    // ---- Focus loss (Preferences.autoPause) ----
    // Songs: PlayState.onFocusLost -> pause(true) (pause menu; FlxG.autoPause is off while a song may pause).
    // Everywhere else: FlxG.autoPause -> the whole game stops updating and every sound pauses until focus returns.
    private bool _focusFrozen;
    private static readonly string _devFocusTest = Environment.GetEnvironmentVariable("FNF_FOCUSTEST"); // dev: "lose@t,gain@t" (s since launch)
    private int _devFocusStep;
    private void OnFocusLost(bool dev = false)
    {
        if (Audio == null || Scenes == null) return;
        if (!Engine.HighscoreManager.Data.AutoPause || (!dev && Engine.SfxPool.IsDevRun)) return; // test windows run in the background
        if (Scenes.CurrentScene is Scenes.PlayScene ps && ps.RequestFocusPause()) return;
        _focusFrozen = true;
        if (Audio.MusicPlaying) { _focusAutoPaused = true; Audio.PauseMusic(); }
        Audio.SfxPoolStats.PauseAll();
        Console.WriteLine($"[FOCUS] lost -> game frozen (scene={Scenes.CurrentScene?.GetType().Name}, music paused={_focusAutoPaused})");
    }
    private void OnFocusGained()
    {
        if (Audio == null) return;
        if (_focusFrozen)
        {
            _focusFrozen = false;
            ResetElapsedTime(); // no catch-up burst of updates for the time spent unfocused
            Audio.SfxPoolStats.ResumeAll();
            Console.WriteLine("[FOCUS] gained -> resumed");
        }
        if (_focusAutoPaused) { _focusAutoPaused = false; Audio.ResumeMusic(); }
    }
    private void DevFocusTestTick()
    {
        if (string.IsNullOrEmpty(_devFocusTest)) return;
        var steps = _devFocusTest.Split(',');
        if (_devFocusStep >= steps.Length) return;
        var st = steps[_devFocusStep].Split('@');
        if (st.Length != 2 || !double.TryParse(st[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double at)) { _devFocusStep++; return; }
        if (_devShotClock.Elapsed.TotalSeconds < at) return;
        _devFocusStep++;
        if (st[0] == "lose") OnFocusLost(dev: true); else OnFocusGained();
    }

    protected override void Update(GameTime gameTime)
    {
        var __flu = Engine.FrameLog.Begin(); try { UpdateCore(gameTime); } finally { if (Engine.FrameLog.Enabled) Engine.FrameLog.FullUpdMs += Engine.FrameLog.Since(__flu); }
    }
    private void UpdateCore(GameTime gameTime)
    {
        DevFocusTestTick();
        if (_focusFrozen) { base.Update(gameTime); return; } // FlxG.autoPause: nothing updates while unfocused
        if (Environment.GetEnvironmentVariable("FNF_STALLLOG") == "1")
        {
            double nowMs = _devShotClock.Elapsed.TotalMilliseconds;
            if (_lastUpdMs > 0 && nowMs - _lastUpdMs > 100) Console.WriteLine($"[STALL] gap {nowMs - _lastUpdMs:F0} ms before update (lastDraw {_lastDrawMs:F0} ms, endDraw {_lastEndDrawMs:F0} ms)");
            _lastUpdMs = nowMs;
        }
        // Update input
        Input.Update();
        
        // Process deferred audio callbacks (intro?loop chaining, fade-outs) on main thread
        Audio.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        // Update Discord Rich Presence
        Discord?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        // Fullscreen toggle: F11 = exclusive fullscreen, Alt+Enter = borderless windowed fullscreen
        var kbState = Keyboard.GetState();
        bool f11Pressed = kbState.IsKeyDown(Keys.F11) && !_prevKbState.IsKeyDown(Keys.F11);
        bool altEnterPressed = (kbState.IsKeyDown(Keys.LeftAlt) || kbState.IsKeyDown(Keys.RightAlt))
            && kbState.IsKeyDown(Keys.Enter) && !_prevKbState.IsKeyDown(Keys.Enter);
        
        if (f11Pressed)
            ToggleFullscreen(false); // Exclusive fullscreen
        else if (altEnterPressed)
            ToggleFullscreen(true); // Borderless windowed fullscreen

        // F5 = open Editor Hub from any scene (check BEFORE storing prevKbState)
        if (kbState.IsKeyDown(Keys.F5) && !_prevKbState.IsKeyDown(Keys.F5))
        {
            if (Scenes.CurrentScene is not FNF_MonoGame.Scenes.Editors.EditorsHubScene && Scenes.CurrentScene is not FNF_MonoGame.Scenes.Editors.EditorBase)
                Scenes.ChangeScene(new FNF_MonoGame.Scenes.Editors.EditorsHubScene(Scenes.CurrentScene));
        }

        _prevKbState = kbState;

        // FPS counter
        _frameCount++;
        _fpsTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_fpsTimer >= 1.0f)
        {
            CurrentFPS = _frameCount;
            _fpsText = $"{CurrentFPS} FPS";
            _frameCount = 0;
            _fpsTimer -= 1.0f;
        }

        // Debug overlay (F3 toggle)
        Engine.DebugOverlay.Update();

        // Update current scene (each scene handles its own input)
        { var __sw = System.Diagnostics.Stopwatch.StartNew(); Scenes.Update(gameTime); Engine.FrameLog.UpdMs += __sw.Elapsed.TotalMilliseconds;
          // Flixel caps FlxG.elapsed at maxElapsed (0.1 s): a load hitch must not be replayed as up to 0.5 s of
          // undrawn catch-up updates (MonoGame fixed-step), which skipped intro/confirm animation frames.
          if (__sw.ElapsedMilliseconds > 100) ResetElapsedTime();
          if (__sw.ElapsedMilliseconds > 100 && Environment.GetEnvironmentVariable("FNF_STALLLOG") == "1") Console.WriteLine($"[STALL] update {__sw.ElapsedMilliseconds} ms scene={Scenes.CurrentScene?.GetType().Name}"); }

        // Volume overlay (global +/- hotkeys)
        VolumeOverlay?.Update((float)gameTime.ElapsedGameTime.TotalSeconds, Input, Audio);
        // dev clock keeps running after Freeplay closes (FNF_SHOTS_SCENETIME shots of the menu it returned to)
        if (FNF_MonoGame.Scenes.FreeplayScene.DevClockStarted && !(Scenes.CurrentScene is FNF_MonoGame.Scenes.FreeplayScene)) FNF_MonoGame.Scenes.FreeplayScene.DevSceneElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Achievement popups (Psych parity: they tick globally, not per-scene)
        Engine.PsychEngine.Achievements.UpdatePopups((float)gameTime.ElapsedGameTime.TotalSeconds);

        base.Update(gameTime);
    }

    // Dev hook for automated visual checks (no effect unless set):
    //   FNF_SHOTS="12.5,13,20"  seconds since launch at which to save the game's 1280x720 frame
    //   FNF_SHOT_DIR=<folder>   where shot_<n>.png files go;  FNF_SHOT_EXIT=1 quits after the last shot
    private float[] _devShotTimes;
    private int _devShotIndex;
    private bool _devResultsShotDone, _devExitAfterRequest;
    private readonly System.Diagnostics.Stopwatch _devShotClock = InitDevShotClock();
    private static System.Diagnostics.Stopwatch InitDevShotClock()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Engine.InputManager.DevShotClock = () => Environment.GetEnvironmentVariable("FNF_SHOTS_FROMSCENE") == "1"
            ? Engine.SceneManager.DevSceneGameTime
            : Environment.GetEnvironmentVariable("FNF_SHOTS_PAUSETIME") == "1" ? ((Instance?.Scenes?.CurrentScene as Scenes.PlayScene)?.DevPauseElapsed ?? -1) // s since the pause opened
            : sw.Elapsed.TotalSeconds;   // FNF_DEVKEYS share the FNF_SHOTS time base
        return sw;
    }
    private void SaveDevScreenshotIfDue(RenderTarget2D shotTarget = null)
    {
        shotTarget ??= _renderTarget;
        if (_devShotTimes == null)
        {
            var spec = Environment.GetEnvironmentVariable("FNF_SHOTS");
            _devShotTimes = string.IsNullOrEmpty(spec) ? System.Array.Empty<float>()
                : spec.Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }
        // FNF_SHOTS_SONGTIME=1: times are song position (seconds), not seconds since launch
        double now = Engine.InputManager.DevShotClock();
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_SONGTIME") == "1")
            now = (Scenes.CurrentScene as Scenes.PlayScene)?.SongPositionSeconds ?? -1;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_CDTIME") == "1") // song ms incl. countdown (negative) / 1000
            now = ((Scenes.CurrentScene as Scenes.PlayScene)?.DevCountdownSongMs ?? -1e9) / 1000.0;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_RANKTIME") == "1")   // seconds since FreeplayState.rankAnimStart
            now = (Scenes.CurrentScene as Scenes.FreeplayScene)?.DevRankElapsed ?? -1;
        else
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_CONFIRMTIME") == "1")
            now = (Scenes.CurrentScene as Scenes.FreeplayScene)?.DevConfirmElapsed ?? -1;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_PAUSETIME") == "1")
            now = (Scenes.CurrentScene as Scenes.PlayScene)?.DevPauseElapsed ?? -1;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_STICKERTIME") == "1")
            now = Engine.StickerTransition.DevClock; // seconds since the sticker transition started (-1 before)
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_CSTIME") == "1")
            now = (Scenes.CurrentScene as Scenes.CharacterSelectScene)?.DevElapsed ?? -1;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_RESULTSTIME") == "1")
            now = (Scenes.CurrentScene as Scenes.ResultsScene)?.DevElapsed ?? -1;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_SCENETIME") == "1")
            now = FNF_MonoGame.Scenes.FreeplayScene.DevClockStarted ? FNF_MonoGame.Scenes.FreeplayScene.DevSceneElapsed : -1;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_W6TIME") == "1")
            now = (Scenes.CurrentScene as Scenes.PlayScene)?.DevW6Elapsed ?? -1; // seconds since PlayScene finished loading
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_W1TIME") == "1")
            now = (Scenes.CurrentScene as Scenes.PlayScene)?.DevW1Elapsed ?? -1; // seconds since the weekend 1 cutscene started
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_VIDEOTIME") == "1")
            now = (Scenes.CurrentScene as Scenes.PlayScene)?.DevVideoTime ?? -1; // position of the playing video cutscene (s)
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_DIALOGUETIME") == "1")
            now = (Scenes.CurrentScene as Scenes.PlayScene)?.DevDialogueElapsed ?? -1; // seconds since Psych startDialogue
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_SPKTIME") == "1")
            now = (Scenes.CurrentScene as Scenes.PlayScene)?.DevSpkElapsed ?? -1; // seconds since the spooky Erect lightning strike
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_DEATHTIME") == "1")
            now = (Scenes.CurrentScene as Scenes.PlayScene)?.DevDeathElapsed ?? -1;
        // FNF_RESULTS_SHOT_AT=<s>: one 'results.png' shot <s> seconds into the Results screen; FNF_RESULTS_EXIT=1 quits after it (bug-hunt runs)
        if (!_devResultsShotDone && Scenes.CurrentScene is Scenes.ResultsScene rsDev && float.TryParse(Environment.GetEnvironmentVariable("FNF_RESULTS_SHOT_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rsAt) && rsDev.DevElapsed >= rsAt)
        {
            _devResultsShotDone = true; Engine.InputManager.DevShotRequest = "results";
            if (Environment.GetEnvironmentVariable("FNF_RESULTS_EXIT") == "1") _devExitAfterRequest = true;
        }
        if (Engine.InputManager.DevShotRequest is string req)
        {
            Engine.InputManager.DevShotRequest = null;
            try { string d = Environment.GetEnvironmentVariable("FNF_SHOT_DIR") ?? AppContext.BaseDirectory; System.IO.Directory.CreateDirectory(d);
                  using var rf = System.IO.File.Create(System.IO.Path.Combine(d, req + ".png")); _renderTarget.SaveAsPng(rf, _renderTarget.Width, _renderTarget.Height); Console.WriteLine("[DEVSHOT] " + req); }
            catch (Exception ex) { Console.WriteLine($"[DEVSHOT] {ex.Message}"); }
            if (_devExitAfterRequest) { Exit(); return; }
        }
        if (_devShotIndex >= _devShotTimes.Length || now < _devShotTimes[_devShotIndex]) return;
        try
        {
            string dir = Environment.GetEnvironmentVariable("FNF_SHOT_DIR") ?? AppContext.BaseDirectory;
            System.IO.Directory.CreateDirectory(dir);
            using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, $"shot_{_devShotIndex:D2}.png"));
            shotTarget.SaveAsPng(fs, shotTarget.Width, shotTarget.Height);
            if (Environment.GetEnvironmentVariable("FNF_MEMSHOT") == "1") FNF_MonoGame.Engine.MemStats.Log("shot");
            Console.WriteLine($"[DEVSHOT] #{_devShotIndex} wall={_devShotClock.Elapsed.TotalSeconds:F3} scene={Scenes.CurrentScene?.GetType().Name} {(Scenes.CurrentScene as Scenes.FreeplayScene)?.DevConfirmInfo} {(Scenes.CurrentScene as Scenes.FreeplayScene)?.DevRankInfo} {(Scenes.CurrentScene as Scenes.ResultsScene)?.DevDump}");
            if (Scenes.CurrentScene is Scenes.CharacterSelectScene css) Console.WriteLine($"[CSICON] {css.DevIconInfo}");
            if (Scenes.CurrentScene is Scenes.PlayScene ps)
            { Console.WriteLine($"[DLGINFO] {ps.DevDialogueInfo}"); Console.WriteLine($"[SHOTINFO] t={ps.SongPositionSeconds:F3} cam={ps.DebugCamCenter} zoom={ps.DefaultCamZoom} {ps.DevScoreInfo}"); Console.WriteLine($"[W6INFO] {ps.DevW6Info}"); Console.WriteLine($"[SHOTINFO] zoomNow={ps.DevCamZoomNow}"); Console.WriteLine($"[VSSTRUM] t={ps.SongPositionSeconds:F3}{ps.VsShotInfo()}"); if (ps.QsDevInfo() is string __qs) Console.WriteLine(__qs); Console.Write(ps.DevPropInfo); Console.WriteLine($"[ORDER] {ps.PsychMembersDump()}"); }
            if (Scenes.CurrentScene is Scenes.PlayScene ps2)
                foreach (var c in new[] { ps2.Boyfriend, ps2.Dad, ps2.Girlfriend })
                    if (c != null) Console.WriteLine($"[SHOTINFO] t={ps2.SongPositionSeconds:F3} {c.Name} anim={c.CurrentAnimation} frame={c._sprite?.FrameIndex} pos={c.Position}");
        }
        catch (Exception ex) { Console.WriteLine($"[DEVSHOT] {ex.Message}"); }
        _devShotIndex++;
        ResetElapsedTime(); // the PNG save stalls ~0.3 s: do not let the fixed-step loop catch up (keeps shot times exact)
        if (_devShotIndex >= _devShotTimes.Length && Environment.GetEnvironmentVariable("FNF_SHOT_EXIT") == "1") Exit();
    }

    private double _lastUpdMs, _lastDrawMs, _lastEndDrawMs;
    protected override void EndDraw() { var sw = System.Diagnostics.Stopwatch.StartNew(); base.EndDraw(); _lastEndDrawMs = sw.Elapsed.TotalMilliseconds; if (Engine.FrameLog.Enabled) Engine.FrameLog.FrameDone(_lastEndDrawMs, (Scenes.CurrentScene as FNF_MonoGame.Scenes.PlayScene)?.SongPositionSeconds ?? -1); }
    protected override void Draw(GameTime gameTime)
    {
        // Render all scene content to the fixed-resolution render target (or the native-size one, see PickSceneTarget)
        var sceneTarget = PickSceneTarget(GetScaledDestination());
        var uiMatrix = Matrix.CreateScale(sceneTarget.Width / (float)SCREEN_WIDTH);
        GraphicsDevice.SetRenderTarget(sceneTarget);
        GraphicsDevice.Clear(Color.Black);
        { var __sw = System.Diagnostics.Stopwatch.StartNew(); Scenes.Draw(SpriteBatch); _lastDrawMs = __sw.Elapsed.TotalMilliseconds; Engine.FrameLog.DrawMs = _lastDrawMs; if (__sw.ElapsedMilliseconds > 100 && Environment.GetEnvironmentVariable("FNF_STALLLOG") == "1") Console.WriteLine($"[STALL] draw {__sw.ElapsedMilliseconds} ms scene={Scenes.CurrentScene?.GetType().Name}"); }

        // Volume overlay HUD (drawn within render target so it scales with the game)
        if (VolumeOverlay != null)
        {
            SpriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, transformMatrix: uiMatrix);
            VolumeOverlay.Draw(SpriteBatch);
            SpriteBatch.End();
        }

        // Achievement popups (drawn within render target so they scale with the game,
        // matching how Psych attaches AchievementPopup to FlxG.game).
        if (Engine.PsychEngine.Achievements.ShowingPopups)
        {
            SpriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, transformMatrix: uiMatrix);
            Engine.PsychEngine.Achievements.DrawPopups(SpriteBatch);
            SpriteBatch.End();
        }

        Engine.FpsCounter.Draw(SpriteBatch, uiMatrix);   // Options > FPS Counter (top-left, V-Slice debug display style)
#if ANDROID || TOUCH_TEST
        FNF_MonoGame.Mobile.TouchControls.Draw(SpriteBatch);   // Android-only touch UI (hitbox lanes / menu pad)
#endif

        // Switch back to the real back buffer and draw the render target scaled
        GraphicsDevice.SetRenderTarget(null);
        SaveDevScreenshotIfDue(sceneTarget);
        GraphicsDevice.Clear(Color.Black);

        var dest = GetScaledDestination();

        SpriteBatch.Begin(samplerState: sceneTarget.Width == dest.Width && sceneTarget.Height == dest.Height ? SamplerState.PointClamp : SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        SpriteBatch.Draw(sceneTarget, dest, Color.White);
        SpriteBatch.End();
        
        // Debug overlay (F3) — drawn on top of everything
        if (Engine.DebugOverlay.Visible && Assets != null)
        {
            SpriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            string sceneName = Scenes.CurrentScene?.GetType().Name ?? "(none)";
            Engine.DebugOverlay.Draw(SpriteBatch, Assets, sceneName);
            SpriteBatch.End();
        }
        
        base.Draw(gameTime);
    }

    /// <summary>
    /// Toggle between windowed and fullscreen modes.
    /// F11 = exclusive fullscreen (changes resolution to native).
    /// Alt+Enter = borderless windowed fullscreen (keeps desktop resolution).
    /// </summary>
    public bool IsFullscreenAny => _isFullscreen || _isBorderless;

    /// <summary>Options > VSync / FPS: apply the saved display settings (PC only).</summary>
    public void ApplyDisplaySettings()
    {
        var d = Engine.HighscoreManager.Data;
        // FramerateCap 0 = unlimited (variable time step; with VSync on it runs at the monitor's refresh rate)
        if (d.FramerateCap <= 0) IsFixedTimeStep = false;
        else
        {
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / Math.Clamp(d.FramerateCap, 30, 360));
        }
        if (Graphics.SynchronizeWithVerticalRetrace != d.VSync)
        {
            Graphics.SynchronizeWithVerticalRetrace = d.VSync;
            Graphics.ApplyChanges();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    /// <summary>Real desktop size in physical pixels (app is per-monitor DPI aware via app.manifest).
    /// MonoGame's CurrentDisplayMode can report a smaller/scaled mode, which left the borderless window undersized.</summary>
    private static Point DesktopSize()
    {
        if (OperatingSystem.IsWindows())   // user32 only exists on Windows (DesktopGL builds also run on Linux/macOS)
            try { int w = GetSystemMetrics(0), h = GetSystemMetrics(1); if (w > 0 && h > 0) return new Point(w, h); } catch { }
        var m = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode; return new Point(m.Width, m.Height);
    }

    public void ToggleFullscreen(bool borderless)
    {
        if (borderless)
        {
            // Borderless windowed fullscreen
            if (_isBorderless)
            {
                // Return to windowed
                _isBorderless = false;
                _isFullscreen = false;
                Graphics.IsFullScreen = false;
                Graphics.PreferredBackBufferWidth = SCREEN_WIDTH;
                Graphics.PreferredBackBufferHeight = SCREEN_HEIGHT;
                Window.IsBorderless = false;
                Graphics.ApplyChanges();
            }
            else
            {
                // Go borderless fullscreen at desktop resolution
                _isBorderless = true;
                _isFullscreen = false;
                Graphics.IsFullScreen = false;
                var ds = DesktopSize();
                Window.IsBorderless = true;
                Graphics.PreferredBackBufferWidth = ds.X;
                Graphics.PreferredBackBufferHeight = ds.Y;
                Graphics.ApplyChanges();
#if !ANDROID
                Window.Position = Point.Zero;
#endif
            }
        }
        else
        {
            // Exclusive fullscreen
            if (_isFullscreen)
            {
                // Return to windowed
                _isFullscreen = false;
                _isBorderless = false;
                Graphics.IsFullScreen = false;
                Graphics.PreferredBackBufferWidth = SCREEN_WIDTH;
                Graphics.PreferredBackBufferHeight = SCREEN_HEIGHT;
                Window.IsBorderless = false;
                Graphics.ApplyChanges();
            }
            else
            {
                _isFullscreen = true;
                _isBorderless = false;
                Window.IsBorderless = false;
                Graphics.PreferredBackBufferWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                Graphics.PreferredBackBufferHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
                Graphics.IsFullScreen = true;
                Graphics.ApplyChanges();
            }
        }
        
        Console.WriteLine($"Display mode: {((_isFullscreen) ? "Fullscreen" : (_isBorderless) ? "Borderless Fullscreen" : "Windowed")} " +
            $"({Graphics.PreferredBackBufferWidth}x{Graphics.PreferredBackBufferHeight})");
    }

    protected override void UnloadContent()
    {
        Scenes.CurrentScene?.Unload();
        _renderTarget?.Dispose();
        _renderTarget = null;
        _nativeTarget?.Dispose();
        _nativeTarget = null;
        AlphabetFont.DisposeAll();
        Assets?.Dispose();
        Audio?.Dispose();
        Discord?.Dispose();
        base.UnloadContent();
    }

}
