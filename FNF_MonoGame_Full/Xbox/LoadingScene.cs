using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame;

public class LoadingScene : Scene
{
    private int _frameCount;
    private string _error;
    private Texture2D _pixel;

    // Content download state
    private ContentDownloader _downloader;
    private bool _retryHeld = true;
    private bool _downloadPhase;
    private bool _downloadStarted;
    private bool _downloadError;          // _error came from the download (shown on the preloader display)

    // V-Slice FunkinPreloader look for the first-start download (shared with the PC preview FNF_DEV_DLPREVIEW)
    private PreloaderScreen _screen;
    private float _fontRetry;
    private float _fadeStart = -1f;       // _screen.Elapsed when the download finished (renderDisplayFadeOut)

    // Store first start: BEFORE YOU DOWNLOAD agreement (AGREE AND DOWNLOAD / EXIT) before the download starts.
    // Dev: FNF_DEV_AGREEMENT=1 (devhooks.txt) shows it on any build without writing the marker.
    private bool _agreement, _agreementDev;
    private int _agreeFocus;
    private bool _agPad;
    private bool _agPrevA = true, _agPrevB = true, _agPrevL = true, _agPrevR = true, _agPrevClick = true;

    /// <summary>Crisp text on TVs: drawn at the back-buffer resolution (layout stays 1280x720 logical).</summary>
    public override bool NativeResolution => true;

    public override void Load()
    {
        _frameCount = 0;
        _pixel = new Texture2D(XboxGame.Instance.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        // Check if content needs downloading from GitHub
        _downloader = new ContentDownloader();
        _downloadPhase = _downloader.NeedsDownload();
        if (_downloadPhase)
        {
            _screen = new PreloaderScreen(XboxGame.Instance.GraphicsDevice);
            _screen.LoadFonts(FontDirs());
        }
        _agreementDev = Environment.GetEnvironmentVariable("FNF_DEV_AGREEMENT") == "1";
        _agreement = (_downloadPhase && StoreContentInstaller.IsStoreBuild && !ContentDownloader.AgreementAccepted) || _agreementDev;
        if (_agreement)
        {
            if (_screen == null) { _screen = new PreloaderScreen(XboxGame.Instance.GraphicsDevice); _screen.LoadFonts(FontDirs()); }
            string pkg = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
            _screen.LoadGlyphs(new[] { Path.Combine(pkg, "Content", "images", "ui", "prompts"),
                Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Content", "images", "ui", "prompts") });
            _agPad = InputManager.IsXboxFamily || ButtonPrompts.DevPadForced == true;
            for (int i = 0; i < 4 && !_agPad; i++) _agPad = Microsoft.Xna.Framework.Input.GamePad.GetState((PlayerIndex)i).IsConnected;
            if (ButtonPrompts.DevPadForced == false) _agPad = false;
            Console.WriteLine("[STORE] showing the download agreement" + (_agreementDev ? " (dev)" : ""));
        }
    }

    /// <summary>Agreement input from the raw devices (InputManager is created later, in InitializeManagers):
    /// A / Enter / Space / Start = focused button, B / Esc / Back = EXIT, Left / Right moves the focus, mouse click (PC).</summary>
    private void UpdateAgreement()
    {
        bool a = false, b = false, l = false, r = false, padUsed = false;
        for (int i = 0; i < 4; i++)
        {
            var p = Microsoft.Xna.Framework.Input.GamePad.GetState((PlayerIndex)i);
            if (!p.IsConnected) continue;
            bool pa = p.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.A) || p.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.Start);
            bool pb = p.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.B) || p.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.Back);
            bool pl = p.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.DPadLeft) || p.ThumbSticks.Left.X < -0.5f;
            bool pr = p.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.DPadRight) || p.ThumbSticks.Left.X > 0.5f;
            a |= pa; b |= pb; l |= pl; r |= pr; padUsed |= pa || pb || pl || pr;
        }
        var kb = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        bool ka = kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter) || kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Space);
        bool kbk = kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape);
        bool kl = kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Left), kr = kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Right);
        a |= ka; b |= kbk; l |= kl; r |= kr;
        if (ButtonPrompts.DevPadForced == null && !InputManager.IsXboxFamily)
        {
            if (padUsed) _agPad = true;
            else if (ka || kbk || kl || kr) _agPad = false;
        }
        bool click = false; Point mouse = default;
        if (!InputManager.IsXboxFamily)
        {
            var ms = Microsoft.Xna.Framework.Input.Mouse.GetState();
            click = ms.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed; mouse = ms.Position;
        }
        int pick = -1;
        if (l && !_agPrevL) _agreeFocus = 0;
        if (r && !_agPrevR) _agreeFocus = 1;
        if (b && !_agPrevB) pick = 1;
        else if (a && !_agPrevA) pick = _agreeFocus;
        else if (click && !_agPrevClick)
        {
            var vp = XboxGame.Instance.GraphicsDevice.Viewport;
            for (int i = 0; i < 2; i++) if (_screen.AgreementButtonRect(i, new Rectangle(0, 0, vp.Width, vp.Height)).Contains(mouse)) pick = i;
        }
        _agPrevA = a; _agPrevB = b; _agPrevL = l; _agPrevR = r; _agPrevClick = click;
        if (pick == 0)
        {
            if (!_agreementDev) ContentDownloader.AcceptAgreement();
            else Console.WriteLine("[STORE] (dev) agreement accepted, marker not written");
            _agreement = false;
            _retryHeld = true;
        }
        else if (pick == 1)
        {
            Console.WriteLine("[STORE] agreement declined -> exit");
            try { Windows.UI.Xaml.Application.Current.Exit(); }
            catch { try { Windows.ApplicationModel.Core.CoreApplication.Exit(); } catch { } }
        }
    }

    /// <summary>LocalState\Content\fonts (DS-Digital is fetched there first on a Store install) and the package's fonts.</summary>
    private static string[] FontDirs()
    {
        string local = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Content", "fonts");
        string pkg = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Content", "fonts");
        return new[] { local, pkg };
    }

    public override void Unload()
    {
        _pixel?.Dispose();
        _pixel = null;
        _screen?.Dispose();
        _screen = null;
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_screen != null)
        {
            _screen.Update(dt);
            if (!_screen.HasDigitalFont && (_fontRetry -= dt) <= 0f) { _fontRetry = 0.5f; _screen.LoadFonts(FontDirs()); }
        }
        if (_agreement) { UpdateAgreement(); return; }   // nothing is downloaded before AGREE

        if (_error != null)
        {
            // Store build: A / Enter retries the game data download.
            if (_downloadPhase && StoreContentInstaller.IsStoreBuild)
            {
                var pad = Microsoft.Xna.Framework.Input.GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
                var kb = Microsoft.Xna.Framework.Input.Keyboard.GetState();
                bool a = pad.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.A) || kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter);
                for (int i = 1; i < 4 && !a; i++)
                    a = Microsoft.Xna.Framework.Input.GamePad.GetState((Microsoft.Xna.Framework.PlayerIndex)i).IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.A);
                if (a && !_retryHeld) { _error = null; _downloadError = false; _downloadStarted = false; }
                _retryHeld = a;
            }
            return;
        }

        // Phase 1: Download content from GitHub if needed
        if (_downloadPhase)
        {
            if (!_downloadStarted)
            {
                _downloadStarted = true;
                Task.Run(() => _downloader.DownloadContentAsync());
            }

            if (_downloader.HasError)
            {
                _downloadError = true;
                if (StoreContentInstaller.IsStoreBuild) { _error = _downloader.ErrorMessage; return; }
                _error = _downloader.ErrorMessage + "\n\nNo game content found. Upload the Content folder to LocalState\\Content"
                    + " (Device Portal > File explorer, or upload_content_to_xbox.ps1) - see README_XBOX_PORT.md.";
                return;
            }

            if (_downloader.IsComplete)
            {
                AssetManager.ResetContentRoot();   // LocalState\Content now holds the full game data
                _downloadPhase = false;
                _frameCount = 0; // Reset for init phase
                if (_screen != null) _fadeStart = _screen.Elapsed;   // original: "Finishing up", then the display fades out
            }
            return;
        }

        // Phase 2: Initialize game managers (spread across frames to avoid timeout)
        _frameCount++;
        if (_frameCount <= 2) return;

        try
        {
            switch (_frameCount)
            {
                case 3:
                    var err = XboxGame.Instance.InitializeManagers();
                    if (err != null) { _error = err; return; }
                    break;

                case 4:
                    Assets.LoadCommonAssets(); // vcr + the 5by7 named fonts (Freeplay BF card BGScrollingText rows, capsule text) - only vcr was loaded on UWP
                    break;

                case 5:
                    try
                    {
                        var saveData = HighscoreManager.Data;
                        XboxGame.Instance.Audio.MusicVolume = saveData.MusicVolume;
                        XboxGame.Instance.Audio.SfxVolume = saveData.SfxVolume;
                        XboxGame.Instance.Audio.PreloadCommonSfx();
                        XboxGame.Instance.Input.LoadBindings();
                    }
                    catch { }
                    break;

                default:
                    // wait for the preloader display to fade out (FADE_TIME) after a first-start download
                    if (_screen != null && _fadeStart >= 0f && _screen.Elapsed - _fadeStart < PreloaderScreen.FadeTime) return;
                    Game.Scenes.ChangeScene(XboxDevHooks.StartScene() ?? StartupNoticeScene.Gate(new TitleScene()));
                    break;
            }
        }
        catch (Exception ex)
        {
            _error = $"Frame {_frameCount}: {ex.GetType().Name}\n{ex.Message}\n{ex.StackTrace}";
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var viewport = XboxGame.Instance.GraphicsDevice.Viewport;
        var dest = new Rectangle(0, 0, viewport.Width, viewport.Height);

        if (_agreement && _screen != null)
        {
            _screen.DrawAgreement(spriteBatch, dest, _agreeFocus, _agPad);
            return;
        }
        if (_screen != null && (_downloadPhase || _fadeStart >= 0f) && (_error == null || _downloadError))
        {
            _screen.Draw(spriteBatch, dest, BuildView());
            return;
        }

        var px = Assets?.Pixel ?? _pixel;
        if (px == null) return;
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        spriteBatch.Draw(px, dest, Color.Black);
        if (_error != null) DrawError(spriteBatch);
        spriteBatch.End();
    }

    /// <summary>FunkinPreloader left text / bar / percent for the current download state. Line 1 = step name, line 2 =
    /// "n/6 ..." like the original's "Downloading assets \n1/11 ..."; the port-only detail (MB, files, errors) goes
    /// under the bar.</summary>
    private PreloaderView BuildView()
    {
        var v = new PreloaderView { Alpha = 1f, Progress = _downloader?.Progress ?? 0f };
        if (_error != null)
        {
            v.Title = StoreContentInstaller.IsStoreBuild ? "Download failed" : "No game content";
            v.StepLine = StoreContentInstaller.IsStoreBuild ? "Press A to try again" : null;
            v.Status = _error;
            return v;
        }
        if (!_downloadPhase && _fadeStart >= 0f)
        {
            v.Title = "Finishing up"; v.StepLine = $"{StepCount}/{StepCount} {_screen.Ellipsis}"; v.Progress = 1f;
            v.Status = _downloader?.Status;
            v.Alpha = 1f - PreloaderScreen.EaseInOutCirc((_screen.Elapsed - _fadeStart) / PreloaderScreen.FadeTime);
            return v;
        }
        string status = _downloader?.Status ?? "";
        var (title, step) = Phase(status);
        v.Title = title;
        v.StepLine = $"{step}/{StepCount} {_screen.Ellipsis}";
        v.Status = status;
        return v;
    }

    private const int StepCount = 6;

    /// <summary>Installer status text -> preloader step (StoreContentInstaller / ContentDownloader messages).</summary>
    private static (string, int) Phase(string s)
    {
        if (s.StartsWith("Downloading extra")) return ("Downloading extra files", 4);
        if (s.StartsWith("Downloading") || s.StartsWith("Connecting") || s.StartsWith("Download timed out")) return ("Downloading assets", 2);
        if (s.StartsWith("Installing") || s.StartsWith("Extract") || s.StartsWith("Importing")) return ("Installing assets", 3);
        if (s.StartsWith("Building")) return ("Building images", 5);
        if (s.Contains("ready")) return ("Finishing up", StepCount);
        return ("Preparing assets", 1);
    }

    /// <summary>
    /// Draw text using pixel block characters at a given scale.
    /// Scale 3 = easily readable on TV, Scale 5 = large title text.
    /// </summary>
    private static void DrawBlockText(SpriteBatch spriteBatch, Texture2D px, string text, int x, int y, Color color, int scale = 4)
    {
        int charSpacing = scale * 4; // space between characters
        int spaceWidth = scale * 3;  // width of a space character
        int cx = x;
        foreach (char c in text)
        {
            if (c == ' ') { cx += spaceWidth; continue; }
            var pattern = GetCharPattern(c);
            if (pattern != null)
            {
                for (int row = 0; row < pattern.Length; row++)
                {
                    for (int col = 0; col < pattern[row].Length; col++)
                    {
                        if (pattern[row][col] == '#')
                            spriteBatch.Draw(px, new Rectangle(cx + col * scale, y + row * scale, scale, scale), color);
                    }
                }
            }
            cx += charSpacing;
        }
    }

    /// <summary>
    /// Tiny 5x7 pixel font patterns for basic characters (error screen before any font is loaded).
    /// </summary>
    private static string[] GetCharPattern(char c)
    {
        return char.ToUpper(c) switch
        {
            '0' => new[] { "###", "#.#", "#.#", "#.#", "###" },
            '1' => new[] { ".#.", "##.", ".#.", ".#.", "###" },
            '2' => new[] { "###", "..#", "###", "#..", "###" },
            '3' => new[] { "###", "..#", "###", "..#", "###" },
            '4' => new[] { "#.#", "#.#", "###", "..#", "..#" },
            '5' => new[] { "###", "#..", "###", "..#", "###" },
            '6' => new[] { "###", "#..", "###", "#.#", "###" },
            '7' => new[] { "###", "..#", "..#", "..#", "..#" },
            '8' => new[] { "###", "#.#", "###", "#.#", "###" },
            '9' => new[] { "###", "#.#", "###", "..#", "###" },
            '%' => new[] { "#.#", "..#", ".#.", "#..", "#.#" },
            '/' => new[] { "..#", "..#", ".#.", "#..", "#.." },
            '.' => new[] { "...", "...", "...", "...", ".#." },
            'A' => new[] { ".#.", "#.#", "###", "#.#", "#.#" },
            'B' => new[] { "##.", "#.#", "##.", "#.#", "##." },
            'C' => new[] { ".##", "#..", "#..", "#..", ".##" },
            'D' => new[] { "##.", "#.#", "#.#", "#.#", "##." },
            'E' => new[] { "###", "#..", "##.", "#..", "###" },
            'F' => new[] { "###", "#..", "##.", "#..", "#.." },
            'G' => new[] { ".##", "#..", "#.#", "#.#", ".##" },
            'H' => new[] { "#.#", "#.#", "###", "#.#", "#.#" },
            'I' => new[] { "###", ".#.", ".#.", ".#.", "###" },
            'K' => new[] { "#.#", "#.#", "##.", "#.#", "#.#" },
            'L' => new[] { "#..", "#..", "#..", "#..", "###" },
            'M' => new[] { "#.#", "###", "#.#", "#.#", "#.#" },
            'N' => new[] { "#.#", "##.", "###", "#.#", "#.#" },
            'O' => new[] { ".#.", "#.#", "#.#", "#.#", ".#." },
            'P' => new[] { "##.", "#.#", "##.", "#..", "#.." },
            'R' => new[] { "##.", "#.#", "##.", "#.#", "#.#" },
            'S' => new[] { ".##", "#..", ".#.", "..#", "##." },
            'T' => new[] { "###", ".#.", ".#.", ".#.", ".#." },
            'U' => new[] { "#.#", "#.#", "#.#", "#.#", "###" },
            'V' => new[] { "#.#", "#.#", "#.#", "#.#", ".#." },
            'W' => new[] { "#.#", "#.#", "#.#", "###", "#.#" },
            'X' => new[] { "#.#", "#.#", ".#.", "#.#", "#.#" },
            'Y' => new[] { "#.#", "#.#", ".#.", ".#.", ".#." },
            ':' => new[] { "...", ".#.", "...", ".#.", "..." },
            '-' => new[] { "...", "...", "###", "...", "..." },
            _ => null,
        };
    }

    private void DrawError(SpriteBatch spriteBatch)
    {
        var font = Assets?.GetFont(14);
        if (font != null)
        {
            font.DrawText(spriteBatch, "ERROR:", new Vector2(30, 30), Color.Red);
            int y = 55;
            foreach (var line in _error.Split('\n'))
            {
                font.DrawText(spriteBatch, line, new Vector2(30, y), Color.White);
                y += 18;
            }
        }
        else
        {
            // Fonts not loaded yet - use block text for error display
            var px = _pixel;
            if (px == null) return;
            DrawBlockText(spriteBatch, px, "ERROR", 30, 30, Color.Red, 5);
            int ey = 70;
            foreach (var line in _error.Split('\n'))
            {
                DrawBlockText(spriteBatch, px, line, 30, ey, Color.White, 3);
                ey += 25;
            }
        }
    }
}
