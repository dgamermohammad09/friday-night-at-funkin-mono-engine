using System;
using System.IO;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame;

/// <summary>
/// UWP/Xbox: the game ALWAYS writes LocalState\game_log.txt (Console output, async writer = no frame stalls), so a
/// player can download it from the Xbox Device Portal (File explorer > LocalAppData > FridayNightFunkin... > LocalState).
/// Rotation: on every launch game_log.txt -> game_log.1.txt -> game_log.2.txt (last 3 runs kept); a run that writes
/// more than ~5 MB rotates again in-session (never more than 3 x 5 MB on disk). Crash reports: LocalState\crash\*.txt
/// (Engine/CrashHandler: scene exceptions + fatal AppDomain/XAML exceptions).
/// </summary>
public static class UwpLog
{
    public const long CapChars = 5L * 1024 * 1024;
    public static string Dir { get; private set; }
    public static string PathNow => Dir == null ? null : Path.Combine(Dir, "game_log.txt");
    private static AsyncLogWriter _writer;

    public static void Start()
    {
        if (_writer != null) return;
        try
        {
            Dir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            Shift();
            _writer = new AsyncLogWriter(Open()) { MaxChars = CapChars, Rotate = old =>
            {
                try { old.WriteLine("[LOG] size cap reached -> continued in a new game_log.txt (this file is now game_log.1.txt)"); old.Flush(); old.Dispose(); } catch { }
                Shift();
                var w = Open();
                w.WriteLine($"[LOG] continued {DateTime.Now:yyyy-MM-dd HH:mm:ss} (previous part: game_log.1.txt)");
                return w;
            } };
            Console.SetOut(_writer);
            Console.WriteLine($"[LOG] FNF MonoGame UWP log {DateTime.Now:yyyy-MM-dd HH:mm:ss} file={PathNow} family={DeviceFamily()} ver={AppVersion()}");
            CrashHandler.InstallFatalHandler();   // AppDomain.UnhandledException -> LocalState\crash\FNF_<date>.txt
        }
        catch (Exception e)
        {
            try { XboxGame.Trace("UwpLog.Start failed: " + e.Message); } catch { }
        }
    }

    public static void Flush() { try { _writer?.Flush(); } catch { } }

    private static StreamWriter Open() =>
        new StreamWriter(new FileStream(Path.Combine(Dir, "game_log.txt"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete));

    /// <summary>game_log.1 -> game_log.2, game_log -> game_log.1 (oldest dropped).</summary>
    private static void Shift()
    {
        string f0 = Path.Combine(Dir, "game_log.txt"), f1 = Path.Combine(Dir, "game_log.1.txt"), f2 = Path.Combine(Dir, "game_log.2.txt");
        try { if (File.Exists(f2)) File.Delete(f2); } catch { }
        try { if (File.Exists(f1)) File.Move(f1, f2); } catch { }
        try { if (File.Exists(f0)) File.Move(f0, f1); } catch { }
    }

    private static string DeviceFamily()
    {
        try { return Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily; } catch { return "?"; }
    }

    private static string AppVersion()
    {
        try { var v = Windows.ApplicationModel.Package.Current.Id.Version; return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"; } catch { return "?"; }
    }
}
