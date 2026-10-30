using System;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Memory diagnostics: "[MEM] tag app=MB limit=MB gpu=MB gpuShared=MB texCache=..." lines in game_log.txt.
/// app/limit = Windows.System.MemoryManager.AppMemoryUsage / AppMemoryUsageLimit on UWP (Xbox kills the app when it
/// exceeds the limit: App type ~1 GB in Dev Mode), process private bytes on desktop. gpu = DXGI QueryVideoMemoryInfo
/// (this process's local / non-local video memory).
/// </summary>
public static class MemStats
{
    public static GraphicsDevice Device;
    public static long PeakAppBytes, PeakGpuBytes;
    private static readonly bool _dump = Environment.GetEnvironmentVariable("FNF_MEMDUMP") == "1";

    public static long AppUsage
    {
        get
        {
#if WINDOWS_UAP
            try { return (long)Windows.System.MemoryManager.AppMemoryUsage; } catch { return 0; }
#else
            try { return System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64; } catch { return 0; }
#endif
        }
    }

    public static long AppLimit
    {
        get
        {
#if WINDOWS_UAP
            try { return (long)Windows.System.MemoryManager.AppMemoryUsageLimit; } catch { return 0; }
#else
            return 0;
#endif
        }
    }

#if !FNF_CROSSPLATFORM
    private static SharpDX.DXGI.Adapter3 _adapter;
#endif
    private static bool _adapterTried;

    /// <summary>(local, nonLocal) video memory used by this process, bytes; (0,0) if unavailable.</summary>
    public static (long Local, long NonLocal) Gpu
    {
        get
        {
#if FNF_CROSSPLATFORM
            return (0, 0);
#else
            try
            {
#if XBOX_UWP
                Device ??= XboxGame.Instance?.GraphicsDevice;
#else
                Device ??= FNFGame.Instance?.GraphicsDevice;
#endif
                if (!_adapterTried && Device != null)
                {
                    _adapterTried = true;
                    if (Device?.Handle is SharpDX.Direct3D11.Device d3d)
                        using (var dxgi = d3d.QueryInterface<SharpDX.DXGI.Device>())
                        using (var ad = dxgi.Adapter)
                            _adapter = ad.QueryInterfaceOrNull<SharpDX.DXGI.Adapter3>();
                }
                if (_adapter == null) return (0, 0);
                var l = _adapter.QueryVideoMemoryInfo(0, SharpDX.DXGI.MemorySegmentGroup.Local);
                var n = _adapter.QueryVideoMemoryInfo(0, SharpDX.DXGI.MemorySegmentGroup.NonLocal);
                return ((long)l.CurrentUsage, (long)n.CurrentUsage);
            }
            catch { return (0, 0); }
#endif
        }
    }

    public static string Line(string tag)
    {
        long app = AppUsage, lim = AppLimit; var g = Gpu;
        if (app > PeakAppBytes) PeakAppBytes = app;
        if (g.Local + g.NonLocal > PeakGpuBytes) PeakGpuBytes = g.Local + g.NonLocal;
        const long MB = 1024 * 1024;
        return $"[MEM] {tag} app={app / MB}MB limit={(lim > 0 ? (lim / MB).ToString() : "-")}MB gpu={g.Local / MB}MB gpuShared={g.NonLocal / MB}MB peakApp={PeakAppBytes / MB}MB peakGpu={PeakGpuBytes / MB}MB managed={GC.GetTotalMemory(false) / MB}MB texCache={SpriteSheetTextureCache.Stats}";
    }

    public static void Log(string tag)
    {
        try { Console.WriteLine(Line(tag)); if (_dump) Console.Write(SpriteSheetTextureCache.Dump()); } catch { }
    }
}
