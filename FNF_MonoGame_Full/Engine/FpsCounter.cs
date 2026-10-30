using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Options > Preferences > FPS Counter: V-Slice style debug display (FPS + memory, white text at the top-left of the
/// game area, VCR font with a thin outline). Counts PRESENTED frames (Draw calls), not fixed-step updates, so it shows
/// the real framerate for every cap / VSync setting.
/// </summary>
public static class FpsCounter
{
    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static int _frames;
    private static double _windowStart;
    private static string _fpsLine = "FPS: 0", _memLine = "";
    public static int Fps { get; private set; }

    /// <summary>Call once per presented frame, into the scene render target after the scene (uiMatrix = target/1280 scale),
    /// so it scales with the game and shows up in screenshots.</summary>
    public static void Draw(SpriteBatch sb, Matrix uiMatrix)
    {
        _frames++;
        double now = _clock.Elapsed.TotalSeconds;
        if (now - _windowStart >= 0.5)
        {
            Fps = (int)Math.Round(_frames / (now - _windowStart));
            _frames = 0; _windowStart = now;
            _fpsLine = $"FPS: {Fps}";
            _memLine = $"RAM: {GC.GetTotalMemory(false) / (1024 * 1024)} MB";
        }
        if (!HighscoreManager.Data.FPSCounter) return;
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: uiMatrix);
        // low framerate -> red like Psych's FPS counter; otherwise white
        var c = Fps < 30 ? new Color(255, 80, 80) : Color.White;
        MenuUI.Text(sb, 16, _fpsLine, 10, 3, c, 1f, 1);
        MenuUI.Text(sb, 16, _memLine, 10, 21, Color.White, 1f, 1);
        sb.End();
    }
}
