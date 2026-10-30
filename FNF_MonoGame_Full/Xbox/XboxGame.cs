using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Windows.Graphics.Display;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame;

/// <summary>
/// Xbox/UWP version of FNFGame.
/// Uses SwapChainPanel for rendering and XboxAudioManager for audio.
/// Initialized via XamlGame&lt;XboxGame&gt;.Create() in GamePage.
/// </summary>
public class XboxGame : Game
{
    public static XboxGame Instance { get; private set; }

    public GraphicsDeviceManager Graphics { get; private set; }
    public SpriteBatch SpriteBatch { get; private set; }
    public AssetManager Assets { get; private set; }
    public InputManager Input { get; private set; }
    public XboxAudioManager Audio { get; private set; }
    public SceneManager Scenes { get; private set; }
    public Engine.DiscordRPCManager Discord { get; private set; }

    public const int SCREEN_WIDTH = 1280;
    public const int SCREEN_HEIGHT = 720;

    private int _frameCount;
    private float _fpsTimer;
    public int FPS { get; private set; }
    public int CurrentFPS => FPS;

    public XboxGame()
    {
        Instance = this;
        UwpLog.Start();      // LocalState\game_log.txt always (rotated, 5 MB cap) + crash\ reports
        XboxDevHooks.Load(); // LocalState\devhooks.txt (test automation only)
        InstallLifecycleTraces();
        Graphics = new GraphicsDeviceManager(this);
        Graphics.IsFullScreen = true;
        Graphics.PreferredBackBufferWidth = 1920;
        Graphics.PreferredBackBufferHeight = 1080;
        Graphics.SynchronizeWithVerticalRetrace = true;
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
    }

    // Render target for resolution-independent rendering
    // All scenes draw at SCREEN_WIDTH x SCREEN_HEIGHT, then scaled to back buffer
    private RenderTarget2D _renderTarget;

    protected override void Initialize()
    {
        // Ensure the mouse cursor stays hidden (belt and suspenders with GamePage)
        try { Windows.UI.Core.CoreWindow.GetForCurrentThread().PointerCursor = null; }
        catch { }

        // Auto-adjust back buffer to the current display resolution.
        try
        {
            var display = DisplayInformation.GetForCurrentView();
            int width = Convert.ToInt32(display.ScreenWidthInRawPixels);
            int height = Convert.ToInt32(display.ScreenHeightInRawPixels);
            // App type (~1 GB): a 4K swap chain alone is ~100 MB; the game draws 1280x720 anyway -> cap at 1080p
            if (LowMemoryLimit && width > 1920) { height = height * 1920 / width; width = 1920; Console.WriteLine($"[XBOX] low memory limit: back buffer capped at {width}x{height}"); }
            if (width > 0 && height > 0)
            {
                Graphics.PreferredBackBufferWidth = width;
                Graphics.PreferredBackBufferHeight = height;
            }
        }
        catch { }

        // Apply back buffer settings once after the SwapChainPanel is ready.
        try { Graphics.ApplyChanges(); } catch { }
        base.Initialize();
    }

    /// <summary>
    /// Get the destination rectangle for scaling the render target to the back buffer
    /// with correct aspect ratio (letterbox/pillarbox).
    /// </summary>
    private RenderTarget2D _nativeTarget;
    private RenderTarget2D PickSceneTarget(Rectangle dest)
    {
        if (Scenes?.CurrentScene == null || !Scenes.CurrentScene.NativeResolution || Engine.StickerTransition.Active) return _renderTarget;
        int w = Math.Clamp(dest.Width, SCREEN_WIDTH, 7680);
        if (w == SCREEN_WIDTH) return _renderTarget;
        int h = (int)Math.Round(w * (double)SCREEN_HEIGHT / SCREEN_WIDTH);
        if (_nativeTarget == null || _nativeTarget.Width != w || _nativeTarget.Height != h)
        {
            _nativeTarget?.Dispose();
            _nativeTarget = new RenderTarget2D(GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        }
        return _nativeTarget;
    }

    public Rectangle GetScaledDestination()
    {
        var viewport = GraphicsDevice.Viewport;
        var safeArea = viewport.TitleSafeArea;

        int viewW = Math.Max(1, viewport.Width);
        int viewH = Math.Max(1, viewport.Height);

        float scale = Math.Min(viewW / (float)SCREEN_WIDTH, viewH / (float)SCREEN_HEIGHT);
        int destW = (int)(SCREEN_WIDTH * scale);
        int destH = (int)(SCREEN_HEIGHT * scale);
        int destX = (viewW - destW) / 2;
        int destY = (viewH - destH) / 2;
        var dest = new Rectangle(destX, destY, destW, destH);

        if (!safeArea.Contains(dest))
        {
            int safeW = Math.Max(1, safeArea.Width);
            int safeH = Math.Max(1, safeArea.Height);
            float safeScale = Math.Min(safeW / (float)SCREEN_WIDTH, safeH / (float)SCREEN_HEIGHT);
            int safeDestW = (int)(SCREEN_WIDTH * safeScale);
            int safeDestH = (int)(SCREEN_HEIGHT * safeScale);
            int safeDestX = safeArea.X + (safeW - safeDestW) / 2;
            int safeDestY = safeArea.Y + (safeH - safeDestH) / 2;
            return new Rectangle(safeDestX, safeDestY, safeDestW, safeDestH);
        }

        return dest;
    }

    /// <summary>
    /// Error message to display if something goes wrong during startup.
    /// LoadingScene reads this to show errors on screen.
    /// </summary>
    public string StartupError { get; set; }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        _renderTarget = new RenderTarget2D(GraphicsDevice, SCREEN_WIDTH, SCREEN_HEIGHT, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents); // scenes may switch to a temp RT (camHUD.alpha layer) and back: DiscardContents would clear the frame
        Scenes = new SceneManager();
        Scenes.ChangeScene(new LoadingScene());
    }

    public string InitializeManagers()
    {
        try
        {
            AssetManager.ResetContentRoot(); // content may have been downloaded/uploaded since the last lookup
            if (Assets == null) Assets = new AssetManager(this);
            if (Input == null) Input = new InputManager();
            if (Audio == null) Audio = new XboxAudioManager(Assets);
            if (Discord == null) Discord = new Engine.DiscordRPCManager();

            // --- Psych Engine initialization (Xbox) -------------------------------
            // The MSIX install directory (Package.Current.InstalledLocation) is read-only
            // on Xbox retail devices, so mods and preferences must live under
            // ApplicationData.Current.LocalFolder (per-user, writable, persistent).
            //
            // Content resolution priority (matches AssetManager):
            //   1. LocalFolder\Content  (side-loaded / downloaded via ContentDownloader)
            //   2. InstalledLocation\Content  (MSIX-bundled fallback)
            string localState = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            string modsDir = System.IO.Path.Combine(localState, "mods");
            try { System.IO.Directory.CreateDirectory(modsDir); } catch { }

            string localContent = System.IO.Path.Combine(localState, "Content");
            string installContent = System.IO.Path.Combine(
                Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Content");
            string contentDir = FNF_MonoGame.Engine.AssetManager.ContentRoot;

            // Preferences + achievements + highscores (all save to LocalFolder on Xbox).
            Engine.PsychEngine.ClientPrefs.Init();
            Engine.PsychEngine.Achievements.Load();
            Engine.PsychEngine.PsychHighscore.Load();

            // Mod discovery + Psych path resolution.
            Engine.PsychEngine.PsychMods.Init(modsDir); Engine.VSliceMods.Refresh(); Engine.PsychEngine.ClientPrefs.EnsureLoaded();   // Psych / Psych Online settings (Options)
            Engine.PsychEngine.PsychPaths.Init(contentDir, modsDir);
            Engine.PsychEngine.Achievements.LoadModAchievements();

            // Legacy ModManager (parallel to PsychMods; used by ModsScene / PlayScene).
            // Initialize expects the game-root path and appends "mods" itself, so we pass
            // LocalFolder so ModsRoot resolves to LocalFolder\mods (matches modsDir above).
            try { Engine.ModManager.Instance.Initialize(localState); } catch { }

            // Load persisted key bindings and audio volumes (previously done in LoadingScene
            // frame 5 — done here now so both paths converge and Psych systems are ready first).
            try { Input.LoadBindings(); } catch { }
            try
            {
                var saveData = Engine.HighscoreManager.Data;
                Audio.MusicVolume = saveData.MusicVolume;
                Audio.SfxVolume = saveData.SfxVolume;
                Audio.PreloadCommonSfx();
            }
            catch { }

            MemStats.Log("startup");
            try
            {
                Console.WriteLine($"[XBOX] device={Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily} memLimit={MemStats.AppLimit / 1048576} MB lowMemoryLimit={LowMemoryLimit}");
                if (LowMemoryLimit)
                    Console.WriteLine("[XBOX] WARNING: running as App type with a low memory limit - set App type = Game in Dev Home (View details) to give the game more memory");
            }
            catch { }
            return null;
        }
        catch (Exception ex)
        {
            return $"InitManagers: {ex.GetType().Name}\n{ex.Message}\n{ex.StackTrace}";
        }
    }


    // ---- crash / exit / memory diagnostics (UWP) ---------------------------------------------------------------
    /// <summary>Appends a line to LocalState\exit_trace.txt synchronously (game_log is async and may lose the last lines).</summary>
    public static void Trace(string what)
    {
        try
        {
            string p = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "exit_trace.txt");
            System.IO.File.AppendAllText(p, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {what}\r\n");
        }
        catch { }
        try { Console.WriteLine("[TRACE] " + what); } catch { }
    }

    private void InstallLifecycleTraces()
    {
        try
        {
            Exiting += (s, e) => Trace($"Game.Exiting scene={Scenes?.CurrentScene?.GetType().Name}\n{Environment.StackTrace}");
            Activated += (s, e) => OnFocusGained();
            Deactivated += (s, e) => OnFocusLost(); // e.g. Xbox guide / another window on PC
            Windows.ApplicationModel.Core.CoreApplication.Exiting += (s, e) => Trace("CoreApplication.Exiting");
            Windows.ApplicationModel.Core.CoreApplication.Suspending += (s, e) => { Trace($"Suspending scene={Scenes?.CurrentScene?.GetType().Name} {MemStats.Line("suspend")}"); try { Audio?.OnSuspend(); } catch { } UwpLog.Flush(); };
            Windows.ApplicationModel.Core.CoreApplication.Resuming += (s, e) => { Trace("Resuming"); try { Audio?.OnResume(); } catch { } };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Trace($"UnhandledException {e.ExceptionObject}");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Trace($"UnobservedTaskException {e.Exception}"); e.SetObserved(); };
            Windows.System.MemoryManager.AppMemoryUsageIncreased += (s, e) =>
            {
                var lvl = Windows.System.MemoryManager.AppMemoryUsageLevel;
                Trace($"AppMemoryUsageIncreased level={lvl} {MemStats.Line("increased")}");
                if (lvl == Windows.System.AppMemoryUsageLevel.High || lvl == Windows.System.AppMemoryUsageLevel.OverLimit) _trimRequested = true;
            };
            Windows.System.MemoryManager.AppMemoryUsageLimitChanging += (s, e) =>
            {
                Trace($"AppMemoryUsageLimitChanging {e.OldLimit / 1048576} -> {e.NewLimit / 1048576} MB {MemStats.Line("limit")}");
                if (e.NewLimit < Windows.System.MemoryManager.AppMemoryUsage + 64UL * 1024 * 1024) _trimRequested = true;
            };
        }
        catch (Exception ex) { Trace("InstallLifecycleTraces failed: " + ex.Message); }
    }

    private volatile bool _trimRequested;
    /// <summary>Memory pressure (Xbox app limit): drop idle cached textures and collect (main thread).</summary>
    private void TrimMemoryIfRequested()
    {
        if (!_trimRequested) return;
        _trimRequested = false;
        SpriteSheetTextureCache.Purge();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Trace("trimmed: " + MemStats.Line("after trim"));
    }

    /// <summary>Xbox Dev Mode runs sideloaded packages as "App" (about 1 GB) unless Dev Home sets them to "Game".</summary>
    public static bool LowMemoryLimit => MemStats.AppLimit > 0 && MemStats.AppLimit < 3L * 1024 * 1024 * 1024
        && Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox";


    private float _noticeT; private bool? _noticeOn;
    /// <summary>Startup notice (first 14 s) when the console runs the game as an "App" with a ~1 GB memory limit.</summary>
    private void DrawMemoryNotice(RenderTarget2D target, float dt)
    {
        try
        {
            if (Assets == null) return;
            _noticeOn ??= LowMemoryLimit || Environment.GetEnvironmentVariable("FNF_DEV_MEMNOTICE") == "1";
            if (_noticeOn != true) return;
            _noticeT += dt;
            if (_noticeT > 14f) { _noticeOn = false; return; }
            var font = Assets.GetFont(18);
            if (font == null) return;
            float a = Math.Clamp(Math.Min(_noticeT * 3f, (14f - _noticeT) * 2f), 0f, 1f);
            string l1 = $"LOW MEMORY: Xbox runs this game as an APP (limit {MemStats.AppLimit / 1048576} MB).";
            const string l2 = "Big songs can close the game. Fix: Dev Home > select the game > View details > App type: GAME.";
            SpriteBatch.Begin(blendState: BlendState.AlphaBlend, transformMatrix: Matrix.CreateScale(target.Width / 1280f));
            SpriteBatch.Draw(Assets.Pixel, new Rectangle(40, 610, 1200, 70), Color.Black * (0.8f * a));
            font.DrawText(SpriteBatch, l1, new Vector2(56, 618), Color.Yellow * a);
            font.DrawText(SpriteBatch, l2, new Vector2(56, 646), Color.White * a);
            SpriteBatch.End();
        }
        catch { }
    }

    // ---- Focus loss (Preferences.autoPause), same as FNFGame ----
    // Songs: PlayState.onFocusLost -> pause(true). Elsewhere: FlxG.autoPause -> nothing updates, sounds pause.
    private bool _focusFrozen, _focusAutoPaused;
    private void OnFocusLost(bool dev = false)
    {
        try
        {
            if (Audio == null || Scenes == null) return;
            if (!Engine.HighscoreManager.Data.AutoPause || (!dev && Engine.SfxPool.IsDevRun)) return;
            if (Scenes.CurrentScene is FNF_MonoGame.Scenes.PlayScene ps && ps.RequestFocusPause()) return;
            _focusFrozen = true;
            if (Audio.MusicPlaying) { _focusAutoPaused = true; Audio.PauseMusic(); }
            Audio.SfxPoolStats.PauseAll();
            Trace("Focus lost -> frozen");
        }
        catch { }
    }
    private void OnFocusGained()
    {
        try
        {
            if (Audio == null) return;
            if (_focusFrozen) { _focusFrozen = false; ResetElapsedTime(); Audio.SfxPoolStats.ResumeAll(); }
            if (_focusAutoPaused) { _focusAutoPaused = false; Audio.ResumeMusic(); }
        }
        catch { }
    }

    // dev: FNF_FOCUSTEST="lose@t,gain@t,suspend@t,resume@t" (s since launch) - same hook as FNFGame + UWP suspend/resume
    // read lazily: static initialisers run before XboxDevHooks.Load() copied devhooks.txt into the environment
    private static string _devFocusTestV; private static bool _devFocusTestRead;
    private static string _devFocusTest { get { if (!_devFocusTestRead) { _devFocusTestRead = true; _devFocusTestV = Environment.GetEnvironmentVariable("FNF_FOCUSTEST"); } return _devFocusTestV; } }
    private static readonly System.Diagnostics.Stopwatch _devFocusClock = System.Diagnostics.Stopwatch.StartNew();
    private int _devFocusStep;
    private void DevFocusTestTick()
    {
        if (string.IsNullOrEmpty(_devFocusTest)) return;
        var steps = _devFocusTest.Split(',');
        if (_devFocusStep >= steps.Length) return;
        var st = steps[_devFocusStep].Split('@');
        if (st.Length != 2 || !double.TryParse(st[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double at)) { _devFocusStep++; return; }
        if (_devFocusClock.Elapsed.TotalSeconds < at) return;
        _devFocusStep++;
        Console.WriteLine($"[FOCUSTEST] {st[0]} at {at:F1}s");
        switch (st[0])
        {
            case "lose": OnFocusLost(dev: true); break;
            case "gain": OnFocusGained(); break;
            case "suspend": Trace("Suspending (FNF_FOCUSTEST)"); Audio?.OnSuspend(); break;
            case "resume": Trace("Resuming (FNF_FOCUSTEST)"); Audio?.OnResume(); break;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        DevFocusTestTick();
        if (_focusFrozen) { base.Update(gameTime); return; } // FlxG.autoPause
        TrimMemoryIfRequested();
        Input?.Update();

        _frameCount++;
        _fpsTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_fpsTimer >= 1f)
        {
            FPS = _frameCount;
            _frameCount = 0;
            _fpsTimer -= 1f;
        }

        Audio?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        Scenes?.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_renderTarget == null || SpriteBatch == null)
        {
            GraphicsDevice.Clear(Color.Black);
            base.Draw(gameTime);
            return;
        }

        // Scenes with NativeResolution (modern main menu / multiplayer) render at the TV's resolution (crisp text/icons)
        var sceneTarget = PickSceneTarget(GetScaledDestination());
        GraphicsDevice.SetRenderTarget(sceneTarget);
        GraphicsDevice.Clear(Color.Black);
        Scenes?.Draw(SpriteBatch);
        Engine.FpsCounter.Draw(SpriteBatch, Matrix.CreateScale(sceneTarget.Width / 1280f));
        DrawMemoryNotice(sceneTarget, (float)gameTime.ElapsedGameTime.TotalSeconds);   // Options > FPS Counter (Xbox: always 60 Hz VSync)

        GraphicsDevice.SetRenderTarget(null);
        XboxDevHooks.AfterDraw(this, sceneTarget);
        GraphicsDevice.Clear(Color.Black);

        var dest = GetScaledDestination();
        SpriteBatch.Begin(samplerState: sceneTarget.Width == dest.Width && sceneTarget.Height == dest.Height ? SamplerState.PointClamp : SamplerState.LinearClamp);
        SpriteBatch.Draw(sceneTarget, dest, Color.White);
        SpriteBatch.End();

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        Scenes?.CurrentScene?.Unload();
        AlphabetFont.DisposeAll();
        Assets?.Dispose();
        Audio?.Dispose();
        base.UnloadContent();
    }
}
