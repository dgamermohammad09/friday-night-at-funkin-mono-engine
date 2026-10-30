using System;

namespace FNF_MonoGame.Scenes;

// The original (PlayState.create / LoadingState) has every asset of the song loaded before the countdown starts.
// The port opened the OGG readers (and scanned the Inst length for the HUD) only when the song started, and loaded
// some sprites lazily (speaker muzzle flash) on the first note -> a 0.5-3 s freeze right at song start; in bot play the
// notes due during the freeze were flagged missed (stress_pico: 3 misses in the first 4 s).
public partial class PlayScene
{
    private void PreloadBeforeSongStart()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (string.Equals(_chart?.Stage, "sserafim", StringComparison.OrdinalIgnoreCase)) SsfInit(); // sserafim.hxc buildStage(): load the extra girls + cutscene atlases with the stage
        try
        {
            Audio.BeginPrepare();
            StartSongAudio();          // same path resolution as StartSong, but only pre-opens the readers
        }
        catch (Exception e) { Console.WriteLine($"[PRELOAD] audio: {e.Message}"); }
        finally { Audio.EndPrepare(); }
        // SetHealthIcon events (Spaghetti: one per girl) decoded their icon PNG mid-song (0.2-0.7 s hitch on a busy
        // machine); the original's icons come from the preloaded asset cache -> decode them now.
        try
        {
            if (_chart?.Events != null)
                foreach (var ev in _chart.Events)
                    if (string.Equals(ev.Name, "SetHealthIcon", StringComparison.OrdinalIgnoreCase) && ev.Value is Newtonsoft.Json.Linq.JObject hi && (string)hi["id"] is string hid && hid.Length > 0)
                        Assets.LoadTexture($"images/icons/icon-{hid}.png");
        }
        catch (Exception e) { Console.WriteLine($"[PRELOAD] icons: {e.Message}"); }
        try { UpdateSpeakerShoot(0f, preloadOnly: true); } catch (Exception e) { Console.WriteLine($"[PRELOAD] speaker: {e.Message}"); }
        try { GpuWarmup(); } catch (Exception e) { Console.WriteLine($"[PRELOAD] gpu warmup: {e.Message}"); }
        Console.WriteLine($"[PRELOAD] done in {sw.ElapsedMilliseconds} ms"); FNF_MonoGame.Engine.SceneManager.MarkReady("song assets");
    }

    /// <summary>
    /// Touch every live texture once (1x1 draw into a private 4x4 target, then read it back) so the driver uploads /
    /// makes resident the pre-rendered composite pages now instead of on their first on-screen draw. Spaghetti keeps
    /// ~4 GB of character composites; the first wide shot of all girls (38.75 s: zoom 0.8 + every girl singing) stalled
    /// Present for 0.2-0.45 s per run (FNF_FRAMELOG). Lime/OpenFL only has the small atlases to upload. Nothing is
    /// drawn on screen. FNF_GPUWARM=0 disables.
    /// Xbox (Game type, 5 GB): same first-wide-shot freeze reported on the console, so the warm-up runs there too. It
    /// allocates nothing (every texture already exists; UWP uses tight composites, ~1.8 GB for Spaghetti); skipped only
    /// in the App-type low-memory mode, where unused composites are JIT-rendered anyway.
    /// </summary>
    private void GpuWarmup()
    {
        if (Environment.GetEnvironmentVariable("FNF_GPUWARM") == "0") return;
#if XBOX_UWP
        if (FNF_MonoGame.XboxGame.LowMemoryLimit && Environment.GetEnvironmentVariable("FNF_GPUWARM") != "1") return;
#endif
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var gd = Game.GraphicsDevice;
        var f = typeof(Microsoft.Xna.Framework.Graphics.GraphicsDevice).GetField("_resources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = f?.GetValue(gd) as System.Collections.IList;
        if (list == null) { Console.WriteLine("[PRELOAD] gpu warmup: no resource list"); return; }
        var texs = new System.Collections.Generic.List<Microsoft.Xna.Framework.Graphics.Texture2D>();
        lock (list) foreach (var o in list) if (o is WeakReference w && w.Target is Microsoft.Xna.Framework.Graphics.Texture2D t && !t.IsDisposed) texs.Add(t);
        long bytes = 0;
        var prev = gd.GetRenderTargets();
        using var rt = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(gd, 4, 4);
        using var sb = new Microsoft.Xna.Framework.Graphics.SpriteBatch(gd);
        gd.SetRenderTarget(rt);
        sb.Begin();
        foreach (var t in texs) { if (t == rt) continue; sb.Draw(t, new Microsoft.Xna.Framework.Rectangle(0, 0, 1, 1), Microsoft.Xna.Framework.Color.White); bytes += (long)t.Width * t.Height * 4; }
        sb.End();
        var px = new Microsoft.Xna.Framework.Color[16]; rt.GetData(px);
        gd.SetRenderTargets(prev);
        Console.WriteLine($"[PRELOAD] gpu warmup: {texs.Count} textures {bytes / 1048576} MB in {sw.ElapsedMilliseconds} ms");
    }
}
