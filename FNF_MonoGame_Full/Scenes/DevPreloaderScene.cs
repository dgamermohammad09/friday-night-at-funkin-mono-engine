using System;
using System.Globalization;
using System.IO;
using FNF_MonoGame.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>PC dev preview of the Xbox/UWP first-start download screen (shared <see cref="PreloaderScreen"/>).
/// FNF_DEV_DLPREVIEW=&lt;0..1&gt; (download progress) | error | orig (original "Finishing up 11/11" frame at 100 %)
/// | agree (Store first-start AGREEMENT, interactive: Left/Right, A/Enter, B/Esc; FNF_DEV_XBOXFAMILY=1 = Xbox glyphs).
/// Optional overrides: FNF_DEV_DLTITLE, FNF_DEV_DLSTEP, FNF_DEV_DLSTATUS ("" = none), FNF_DEV_DLFADE=&lt;s&gt; (fade-out start).</summary>
public sealed class DevPreloaderScene : Scene
{
    private PreloaderScreen _screen;
    private string _mode;

    public static bool Requested => Environment.GetEnvironmentVariable("FNF_DEV_DLPREVIEW") is string s && s.Length > 0;

    /// <summary>Crisp text on TVs: drawn at the back-buffer resolution (layout stays 1280x720 logical).</summary>
    public override bool NativeResolution => true;

    public override void Load()
    {
        _mode = Environment.GetEnvironmentVariable("FNF_DEV_DLPREVIEW") ?? "0.3";
        _screen = new PreloaderScreen(Game.GraphicsDevice);
        // FNF_DEV_DLNODIGITAL=1: only the package's fonts/ui (Store first start before DS-Digital is fetched)
        string fonts = Path.Combine(AssetManager.ContentRoot, "fonts");
        _screen.LoadFonts(new[] { Environment.GetEnvironmentVariable("FNF_DEV_DLNODIGITAL") == "1" ? Path.Combine(fonts, "ui") : fonts });
        _screen.LoadGlyphs(new[] { Path.Combine(AssetManager.ContentRoot, "images", "ui", "prompts") });
        Console.WriteLine($"[DLPREVIEW] mode={_mode} digitalFont={_screen.HasDigitalFont}");
    }

    private int _agreeFocus;

    private void UpdateAgreement()
    {
        if (Input.LeftPressed) _agreeFocus = 0;
        if (Input.RightPressed) _agreeFocus = 1;
        var vp = Game.GraphicsDevice.Viewport;
        var m = Input.RawMousePosition;
        int hover = -1;
        for (int i = 0; i < 2; i++) if (_screen.AgreementButtonRect(i, new Rectangle(0, 0, vp.Width, vp.Height)).Contains(m)) hover = i;
        if (hover >= 0 && Input.MouseLeftPressed) { Pick(hover); return; }
        if (Input.BackPressed) { Pick(1); return; }
        if (Input.ConfirmPressed) Pick(_agreeFocus);
    }

    private void Pick(int i)
    {
        Console.WriteLine("[AGREEMENT] " + (i == 0 ? "agreed -> download" : "exit"));
        if (i == 0) _mode = "0.02";   // the download screen follows
        else Game.Exit();
    }

    public override void Unload() { _screen?.Dispose(); _screen = null; }

    public override void Update(GameTime gameTime)
    {
        _screen?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        if (_screen != null && _mode == "agree") UpdateAgreement();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (_screen == null) return;
        if (_mode == "agree")
        {
            var vpa = Game.GraphicsDevice.Viewport;
            _screen.DrawAgreement(spriteBatch, new Rectangle(0, 0, vpa.Width, vpa.Height), _agreeFocus, ButtonPrompts.UseController(Input));
            return;
        }
        var v = new PreloaderView { Alpha = 1f };
        const float totalMb = 1253f;
        if (_mode == "orig") { v.Title = "Finishing up"; v.StepLine = "11/11 " + _screen.Ellipsis; v.Progress = 1f; }
        else if (_mode == "error")
        {
            v.Title = "Download failed"; v.StepLine = "Press A to try again"; v.Progress = 0.37f;
            v.Status = "Could not download the game data.\nCheck your internet connection.\n\nHttpRequestException: No such host is known.";
        }
        else
        {
            float p = float.TryParse(_mode, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? Math.Clamp(f, 0f, 1f) : 0.3f;
            v.Progress = p;
            var (title, step, status) = p < 0.02f ? ("Preparing assets", 1, "Preparing...")
                : p < 0.70f ? ("Downloading assets", 2, $"Downloading game data from The Funkin' Crew's GitHub... {(int)((p - 0.02f) / 0.68f * totalMb)} / {(int)totalMb} MB")
                : p < 0.85f ? ("Installing assets", 3, $"Installing... {(int)((p - 0.70f) / 0.15f * 1985)}/1985 files")
                : p < 0.92f ? ("Downloading extra files", 4, $"Downloading extra files... {(int)((p - 0.85f) / 0.07f * 40)}/40")
                : p < 1f ? ("Building images", 5, $"Building images... {(int)((p - 0.92f) / 0.07f * 60)}/60")
                : ("Finishing up", 6, "Game data ready!");
            v.Title = title; v.StepLine = $"{step}/6 {_screen.Ellipsis}"; v.Status = status;
        }
        if (Environment.GetEnvironmentVariable("FNF_DEV_DLTITLE") is string t) v.Title = t;
        if (Environment.GetEnvironmentVariable("FNF_DEV_DLSTEP") is string st) v.StepLine = st;
        if (Environment.GetEnvironmentVariable("FNF_DEV_DLSTATUS") is string ss) v.Status = ss;
        if (Environment.GetEnvironmentVariable("FNF_DEV_DLFADE") is string fs && float.TryParse(fs, NumberStyles.Float, CultureInfo.InvariantCulture, out var f0))
            v.Alpha = 1f - PreloaderScreen.EaseInOutCirc((_screen.Elapsed - f0) / PreloaderScreen.FadeTime);
        var vp = Game.GraphicsDevice.Viewport;
        _screen.Draw(spriteBatch, new Rectangle(0, 0, vp.Width, vp.Height), v);
    }
}
