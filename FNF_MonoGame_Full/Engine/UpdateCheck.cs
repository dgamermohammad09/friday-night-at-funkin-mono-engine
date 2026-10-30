using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CP = FNF_MonoGame.Engine.PsychEngine.ClientPrefs;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Optional update check (Psych TitleState 'Check for Updates', Options > Gameplay): once per launch, when the option is
/// on and an update URL is configured (env FNF_UPDATE_URL or the first line of Content/data/engine_update.txt), the text
/// at that URL is read as the newest version ("0.1.3"); if it is newer than <see cref="CurrentVersion"/> the main menu
/// shows a taped note. Off = no network request at all. Failures are silent (log only).
/// </summary>
public static class UpdateCheck
{
    public const string CurrentVersion = "0.2.1";
    public static string NewVersion { get; private set; }
    private static bool _started;
    private static double _shownAt = -1;

    public static void StartOnce()
    {
        if (_started) return;
        _started = true;
        CP.EnsureLoaded();
        if (!CP.Data.CheckForUpdates) { Console.WriteLine("[UPDATE] check disabled in options"); return; }
        string url = Environment.GetEnvironmentVariable("FNF_UPDATE_URL");
        try
        {
            string cfg = Path.Combine(AssetManager.ContentRoot, "data", "engine_update.txt");
            if (string.IsNullOrEmpty(url) && global::FNF_MonoGame.Engine.FsCI.FileExists(cfg)) url = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(cfg)[0].Trim();
        }
        catch { }
        if (string.IsNullOrEmpty(url)) { Console.WriteLine("[UPDATE] no update URL configured"); return; }
        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                string v = (await http.GetStringAsync(url)).Trim().Split('\n')[0].Trim().TrimStart('v', 'V');
                Console.WriteLine($"[UPDATE] latest={v} current={CurrentVersion}");
                if (Version.TryParse(v, out var nv) && Version.TryParse(CurrentVersion, out var cv) && nv > cv) NewVersion = v;
            }
            catch (Exception e) { Console.WriteLine("[UPDATE] check failed: " + e.Message); }
        });
    }

    /// <summary>Main menu: small tilted tape note bottom-right for 8 s once an update is known.</summary>
    public static void DrawNotice(SpriteBatch sb, AssetManager assets, int vw, int vh)
    {
        if (NewVersion == null || assets?.Pixel == null) return;
        double now = (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
        if (_shownAt < 0) _shownAt = now;
        double t = now - _shownAt;
        if (t > 8.0) return;
        var font = assets.GetFont(18);
        if (font == null) return;
        float s = Math.Min(vw / 1280f, vh / 720f), a = (float)Math.Clamp(Math.Min(t / 0.2, (8.0 - t) / 0.5), 0, 1);
        string txt = $"NEW VERSION v{NewVersion} IS OUT!";
        int w = (int)font.MeasureString(txt).X + 40, h = 36;
        var m = Matrix.CreateTranslation(-w / 2f, -h / 2f, 0) * Matrix.CreateRotationZ(-0.04f)
              * Matrix.CreateTranslation(1280 - w / 2f - 40, 8 + h / 2f, 0) * Matrix.CreateScale(s);
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, m);
        sb.Draw(assets.Pixel, new Rectangle(6, 6, w, h), new Color(Color.Black, 0.5f * a));
        sb.Draw(assets.Pixel, new Rectangle(-3, -3, w + 6, h + 6), new Color(Color.Black, a));
        sb.Draw(assets.Pixel, new Rectangle(0, 0, w, h), new Color(new Color(0x3E, 0xD8, 0x6A), a));
        font.DrawText(sb, txt, new Vector2(20, 8), new Color(Color.Black, a));
        sb.End();
    }
}
