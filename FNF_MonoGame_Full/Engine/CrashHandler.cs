using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Crash handler (Psych Engine backend/CrashHandler idea): an exception inside a scene writes crash/FNF_&lt;date&gt;.txt
/// (scene, exception, stack, recent log lines, engine/OS info) and SceneManager returns to the main menu instead of
/// closing the game; a taped paper note tells the player where the report is. Fatal (non-scene) exceptions are written
/// too (AppDomain.UnhandledException).
/// </summary>
public static class CrashHandler
{
    public static string Dir
    {
        get
        {
#if XBOX_UWP
            string d = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "crash");
#else
            string d = Path.Combine(AssetManager.GameRoot, "crash");
#endif
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    private static readonly Queue<string> _recent = new();
    /// <summary>Remember a log line for the report (kept small).</summary>
    public static void Note(string line)
    {
        lock (_recent) { _recent.Enqueue(line); while (_recent.Count > 40) _recent.Dequeue(); }
    }

    public static string LastReportPath { get; private set; }
    public static string LastMessage { get; private set; }
    public static double LastReportTime = -1;   // FNFGame total seconds when the note appeared
    public static int Count { get; private set; }

    public static string Report(Exception ex, string where, string scene)
    {
        Count++;
        LastMessage = $"{ex.GetType().Name}: {ex.Message}";
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Friday Night Funkin' - MonoGame Engine crash report");
            sb.AppendLine($"Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Where: {where}   Scene: {scene}");
            sb.AppendLine($"OS: {Environment.OSVersion}   .NET: {Environment.Version}   64-bit: {Environment.Is64BitProcess}");
            sb.AppendLine($"Managed memory: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
            sb.AppendLine();
            sb.AppendLine(ex.ToString());
            lock (_recent)
            {
                if (_recent.Count > 0) { sb.AppendLine(); sb.AppendLine("Recent events:"); foreach (var l in _recent) sb.AppendLine("  " + l); }
            }
            sb.AppendLine();
            sb.AppendLine("The game went back to the main menu. Please send this file with your bug report.");
            string p = Path.Combine(Dir, $"FNF_{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.txt");
            File.WriteAllText(p, sb.ToString());
            LastReportPath = p;
            LastReportTime = (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
            Console.WriteLine($"[CRASH] {where} in {scene}: {LastMessage} -> report {p}");
            try { Console.Out.Flush(); } catch { }
            return p;
        }
        catch (Exception e2) { LastReportTime = (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency); Console.WriteLine($"[CRASH] report failed: {e2.Message}"); return null; }
    }

    public static void InstallFatalHandler()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex) Report(ex, e.IsTerminating ? "fatal" : "unhandled", "-");
        };
    }

    // ---- dev hook: FNF_DEV_CRASH_AT=<song seconds> throws once inside PlayScene.Update ----
    // read lazily: on UWP this class is first used by UwpLog.Start(), before XboxDevHooks copied devhooks.txt into the env
    private static double _devCrashAtV = double.NaN;
    private static double _devCrashAt => !double.IsNaN(_devCrashAtV) ? _devCrashAtV
        : (_devCrashAtV = double.TryParse(Environment.GetEnvironmentVariable("FNF_DEV_CRASH_AT"),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : -1);
    private static bool _devCrashed;
    public static void DevCrashCheck(double songPos)
    {
        if (_devCrashAt < 0 || _devCrashed || songPos < _devCrashAt) return;
        _devCrashed = true;
        throw new InvalidOperationException("FNF_DEV_CRASH_AT test crash");
    }

    /// <summary>Hand-made note (paper, black outline, hard shadow, tape, slight tilt) for 7 s after a crash.</summary>
    public static void DrawNotice(SpriteBatch sb, AssetManager assets, int vw, int vh)
    {
        double now = (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
        if (LastReportTime < 0 || assets?.Pixel == null) return;
        double t = now - LastReportTime;
        if (t > 7.0) return;
        var font = assets.GetFont(18);
        if (font == null) return;
        float s = Math.Min(vw / 1280f, vh / 720f);
        float slide = (float)Math.Min(1.0, t / 0.25), fade = (float)Math.Clamp((7.0 - t) / 0.5, 0, 1);
        string l1 = "WHOOPS! THE GAME TRIPPED OVER AN ERROR.", l2 = "Back at the main menu. Report saved to:", l3 = TrimPath(LastReportPath ?? "(could not save)");
        int w = 660, h = 112;
        var m = Matrix.CreateTranslation(-w / 2f, -h / 2f, 0) * Matrix.CreateRotationZ(0.03f)
              * Matrix.CreateTranslation(1280 - w / 2f - 30, 720 - h / 2f - 30 + (1 - slide) * 200, 0) * Matrix.CreateScale(s);
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, m);
        var px = assets.Pixel;
        sb.Draw(px, new Rectangle(8, 9, w, h), new Color(Color.Black, 0.5f * fade));
        sb.Draw(px, new Rectangle(-4, -4, w + 8, h + 8), new Color(Color.Black, fade));
        sb.Draw(px, new Rectangle(0, 0, w, h), new Color(new Color(0xFF, 0xF6, 0xD8), fade));
        for (int y = 30; y < h; y += 26) sb.Draw(px, new Rectangle(0, y, w, 2), new Color(new Color(0x9E, 0xC9, 0xF0), fade));
        sb.Draw(px, new Rectangle(48, 0, 3, h), new Color(new Color(0xF0, 0x7A, 0x9A), fade));
        sb.Draw(px, new Rectangle(w / 2 - 40, -12, 80, 22), new Color(new Color(0xFF, 0x7A, 0xC8), 0.85f * fade));
        font.DrawText(sb, l1, new Vector2(60, 8), new Color(new Color(0xD0, 0x20, 0x40), fade));
        font.DrawText(sb, l2, new Vector2(60, 36), new Color(Color.Black, fade));
        font.DrawText(sb, l3, new Vector2(60, 62), new Color(new Color(0x30, 0x30, 0x60), fade));
        font.DrawText(sb, Clip(LastMessage, 52), new Vector2(60, 86), new Color(new Color(0x60, 0x60, 0x60), fade));
        sb.End();
    }
    private static string TrimPath(string p) => p.Length > 52 ? "..." + p.Substring(p.Length - 49) : p;
    private static string Clip(string s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length > n ? s.Substring(0, n - 3) + "..." : s;
}
