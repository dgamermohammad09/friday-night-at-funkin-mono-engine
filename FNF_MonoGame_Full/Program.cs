using System;
using System.IO;

namespace FNF_MonoGame;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Redirect console output to a log file so it's always visible
        string logPath = Path.Combine(AppContext.BaseDirectory, "game_log.txt");
        StreamWriter writer;
        try { writer = new StreamWriter(logPath) { AutoFlush = true }; }
        catch (IOException)
        {
            // Log locked (another instance still running/closing): don't crash, use a per-process log.
            logPath = Path.Combine(AppContext.BaseDirectory, $"game_log_{Environment.ProcessId}.txt");
            writer = new StreamWriter(logPath) { AutoFlush = true };
        }
#if FNF_CROSSPLATFORM
        catch (UnauthorizedAccessException)
        {
            // Read-only install dir (macOS .app in /Applications, Linux /opt): log to the user's data folder.
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FNF_MonoGame");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "game_log.txt");
            writer = new StreamWriter(logPath) { AutoFlush = true };
        }
#endif
        var logw = new FNF_MonoGame.Engine.AsyncLogWriter(writer); // disk writes off the game thread
        Console.SetOut(logw);
        Console.SetError(logw);
        Console.WriteLine($"[LOG] Game started at {DateTime.Now}");
        Console.WriteLine($"[LOG] Log file: {logPath}");
        FNF_MonoGame.Engine.CrashHandler.InstallFatalHandler();

        // Headless relay server for online multiplayer:  FNF_MonoGame.exe --relay [port]
        if (args.Length > 0 && args[0] == "--relay")
        {
            int port = args.Length > 1 && int.TryParse(args[1], out var pt) ? pt : FNF_MonoGame.Engine.MultiplayerSession.DefaultPort;
            FNF_MonoGame.Engine.MultiplayerSession.RunHeadlessRelay(port);
            return;
        }

        if (!ContentPresent(out string msg))
        {
            Console.WriteLine("[FATAL] " + msg);
            ShowError(msg);
            return;
        }

        using var game = new FNFGame();
        game.Run();
    }

    /// <summary>The game data folder must sit next to the executable. A missing/partial Content
    /// (zip not fully extracted, run from inside the zip viewer) used to crash on the title font.</summary>
    private static bool ContentPresent(out string msg)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Content");
        string[] need = { Path.Combine("data", "characters"), "fonts", "images", "music" };
        string missing = null;
        foreach (var n in need)
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(root, n))) { missing = n; break; }
        if (missing == null) { msg = null; return true; }
        string zipHint = AppContext.BaseDirectory.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
            ? "\n\nIt looks like the game was started from inside the zip. Extract the whole zip first (right-click > Extract All), then run it from the extracted folder."
            : "";
        msg = "Game files are missing.\n\nThe folder \"Content\" (" + missing + ") was not found next to the game:\n" + root +
              "\n\nExtract the whole download into one folder so that \"Content\" sits next to the game executable." + zipHint;
        return false;
    }

    private static void ShowError(string msg)
    {
        const string title = "Friday Night Funkin' - MonoGame Engine";
#if FNF_CROSSPLATFORM
        try { if (SdlBox.Show(title, msg)) return; } catch { }
        Console.Error.WriteLine(msg);
#else
        try { System.Windows.Forms.MessageBox.Show(msg, title, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error); } catch { }
#endif
    }

#if FNF_CROSSPLATFORM
    private static class SdlBox
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate int ShowSimpleMessageBox(uint flags, IntPtr title, IntPtr message, IntPtr window);

        public static bool Show(string title, string msg)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { "SDL2.dll" }
                : OperatingSystem.IsMacOS() ? new[] { "libSDL2.dylib", "libSDL2-2.0.0.dylib" }
                : new[] { "libSDL2-2.0.so.0", "libSDL2.so" };
            foreach (var n in names)
            {
                string p = Path.Combine(AppContext.BaseDirectory, n);
                if (!System.Runtime.InteropServices.NativeLibrary.TryLoad(global::FNF_MonoGame.Engine.FsCI.FileExists(p) ? p : n, out var lib)) continue;
                if (!System.Runtime.InteropServices.NativeLibrary.TryGetExport(lib, "SDL_ShowSimpleMessageBox", out var fn)) continue;
                var f = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<ShowSimpleMessageBox>(fn);
                IntPtr t = Utf8(title), m = Utf8(msg);
                try { return f(0x10 /* SDL_MESSAGEBOX_ERROR */, t, m, IntPtr.Zero) == 0; }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(t); System.Runtime.InteropServices.Marshal.FreeHGlobal(m); }
            }
            return false;
        }

        private static IntPtr Utf8(string s)
        {
            var b = System.Text.Encoding.UTF8.GetBytes(s + "\0");
            var p = System.Runtime.InteropServices.Marshal.AllocHGlobal(b.Length);
            System.Runtime.InteropServices.Marshal.Copy(b, 0, p, b.Length);
            return p;
        }
    }
#endif
}
