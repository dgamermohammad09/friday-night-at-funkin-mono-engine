using System;
using System.Diagnostics;
using System.Text;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Lightweight frame profiler (dev hook FNF_PERFLOG=1): per named slot wall time and managed bytes allocated on the
/// main thread, summarised every 5 s as "[PERF] ..." in game_log.txt (avg / max ms, avg KB per frame, gen0 GCs).
/// Zero cost when the env var is not set (Begin returns default, End returns at once).
/// </summary>
public static class PerfStats
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("FNF_PERFLOG") == "1";
    public static readonly string[] Names = { "update", "draw", "stage", "props", "chars", "notes", "hud", "splash", "other" };
    private static readonly double[] _ms = new double[Names.Length], _max = new double[Names.Length];
    private static readonly long[] _bytes = new long[Names.Length];
    private static int _frames; private static long _lastReport = Stopwatch.GetTimestamp(); private static int _gc0;
    private static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

    public readonly struct Token { public readonly long T, A; public Token(long t, long a) { T = t; A = a; } }

    private static long Alloc()
    {
#if XBOX_UWP
        return 0;
#else
        return GC.GetAllocatedBytesForCurrentThread();
#endif
    }

    public static Token Begin() => Enabled ? new Token(Stopwatch.GetTimestamp(), Alloc()) : default;

    public static void End(int slot, Token t)
    {
        if (!Enabled) return;
        double ms = (Stopwatch.GetTimestamp() - t.T) * TickMs;
        _ms[slot] += ms; if (ms > _max[slot]) _max[slot] = ms;
        _bytes[slot] += Alloc() - t.A;
    }

    /// <summary>Once per drawn frame (SceneManager.Draw end).</summary>
    public static void FrameDone(string scene)
    {
        if (!Enabled) return;
        _frames++;
        long now = Stopwatch.GetTimestamp();
        if ((now - _lastReport) * TickMs < 5000) return;
        int gc0 = GC.CollectionCount(0);
        var sb = new StringBuilder($"[PERF] {scene} frames={_frames} fps={_frames / ((now - _lastReport) * TickMs / 1000.0):F1} gen0={gc0 - _gc0}");
        for (int i = 0; i < Names.Length; i++)
        {
            if (_ms[i] <= 0 && _bytes[i] == 0) continue;
            sb.Append($" | {Names[i]} {_ms[i] / _frames:F2}ms max {_max[i]:F1} {_bytes[i] / (double)_frames / 1024.0:F1}KB");
            _ms[i] = 0; _max[i] = 0; _bytes[i] = 0;
        }
        Console.WriteLine(sb.ToString());
        _frames = 0; _lastReport = now; _gc0 = gc0;
    }
}

/// <summary>
/// Per-frame hitch log (dev hook FNF_FRAMELOG=1): every drawn frame slower than 30 ms is written as
/// "[FRAME] song=.. dt=.. upd=.. draw=.. gc=g0/g1/g2 pause=.. | events" where events are the expensive one-off
/// operations noted during that frame (JIT composites, texture/sound loads ...). FNF_FRAMELOG_ALL=1 logs every frame.
/// </summary>
public static class FrameLog
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("FNF_FRAMELOG") == "1";
    private static readonly bool _all = Environment.GetEnvironmentVariable("FNF_FRAMELOG_ALL") == "1";
    private static readonly StringBuilder _ev = new StringBuilder();
    private static long _last; private static int _g0, _g1, _g2; private static TimeSpan _pause;
    public static double UpdMs, DrawMs, FullUpdMs;
    private static long _cpu; private static int _resN; private static System.Reflection.FieldInfo _resField;
#if !XBOX_UWP
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool GetThreadTimes(IntPtr h, out long c, out long e, out long k, out long u);
    private static long ThreadCpu() => GetThreadTimes(GetCurrentThread(), out _, out _, out long k, out long u) ? k + u : 0;   // 100 ns units
#else
    private static long ThreadCpu() => 0;
#endif

    public static void Event(string what, double ms)
    {
        if (!Enabled) return;
        lock (_ev) _ev.Append($" [{what} {ms:F1}ms]");
    }

    public static Token Begin() => Enabled ? new Token(Stopwatch.GetTimestamp()) : default;
    public static double Since(Token t) => (Stopwatch.GetTimestamp() - t.T) * 1000.0 / Stopwatch.Frequency;
    public readonly struct Token { public readonly long T; public Token(long t) { T = t; } }

    public static void FrameDone(double endDrawMs, double songSec)
    {
        if (!Enabled) return;
        long now = Stopwatch.GetTimestamp();
        double dt = _last == 0 ? 0 : (now - _last) * 1000.0 / Stopwatch.Frequency;
        _last = now;
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
#if XBOX_UWP
        TimeSpan pause = TimeSpan.Zero;
#else
        TimeSpan pause = GC.GetTotalPauseDuration();
#endif
        long cpu = ThreadCpu(); double cpuMs = (cpu - _cpu) / 10000.0; _cpu = cpu;
#if !XBOX_UWP
        try
        {
            var gd = FNF_MonoGame.FNFGame.Instance?.GraphicsDevice;
            _resField ??= typeof(Microsoft.Xna.Framework.Graphics.GraphicsDevice).GetField("_resources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (gd != null && _resField?.GetValue(gd) is System.Collections.IList rl) { int n = rl.Count; if (_resN != 0 && n > _resN) { var last = (rl[n - 1] as WeakReference)?.Target; Event($"res {_resN}->{n} {last?.GetType().Name} {(last as Microsoft.Xna.Framework.Graphics.Texture2D)?.Width}x{(last as Microsoft.Xna.Framework.Graphics.Texture2D)?.Height}", 0); } _resN = n; }
        }
        catch { }
#endif
        string ev; lock (_ev) { ev = _ev.ToString(); _ev.Clear(); }
        if (dt > 30 || _all || ev.Length > 0)
            Console.WriteLine($"[FRAME] song={songSec:F3} dt={dt:F1} cpu={cpuMs:F1} upd={UpdMs:F1}/{FullUpdMs:F1} draw={DrawMs:F1} present={endDrawMs:F1} gc={g0 - _g0}/{g1 - _g1}/{g2 - _g2} pause={(pause - _pause).TotalMilliseconds:F1}{ev}");
        _g0 = g0; _g1 = g1; _g2 = g2; _pause = pause; UpdMs = 0; FullUpdMs = 0;
    }
}
