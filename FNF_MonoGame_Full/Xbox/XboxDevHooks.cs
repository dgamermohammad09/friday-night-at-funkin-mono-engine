using System.Globalization;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame;

/// <summary>
/// Dev/test hooks for the UWP build (no effect in normal use).
/// UWP apps do not inherit environment variables, so the desktop FNF_* dev hooks are read from
/// LocalState\devhooks.txt (one KEY=VALUE per line, '#' comments). The file is renamed to
/// devhooks.used.txt on start, so it only affects ONE launch.
/// Supported: every FNF_* variable read by shared code (FNF_DEVKEYS, FNF_VIDEO_*, ...) plus
///   FNF_AUTOPLAY_SONG/_DIFF/_WEEKSONGS, FNF_START_SCENE=mainmenu|options|story|freeplay|multiplayer|mods|achievements,
///   FNF_SHOTS="t1,t2" (seconds since launch; FNF_SHOTS_SONGTIME=1 / FNF_SHOTS_VIDEOTIME=1 / FNF_SHOTS_FROMSCENE=1),
///   FNF_SHOT_DIR (default LocalState\shots), FNF_SHOT_EXIT=1, FNF_LOG=1 (Console -> LocalState\game_log.txt).
/// </summary>
public static class XboxDevHooks
{
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static float[] _shotTimes;
    private static int _shotIndex;
    public static bool Active { get; private set; }

    private static string LocalState
    {
        get { try { return Windows.Storage.ApplicationData.Current.LocalFolder.Path; } catch { return null; } }
    }

    public static void Load()
    {
        try
        {
            string ls = LocalState;
            if (ls == null) return;
            string file = Path.Combine(ls, "devhooks.txt");
            if (!File.Exists(file)) return;
            foreach (var raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Environment.SetEnvironmentVariable(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            Active = true;
            string used = Path.Combine(ls, "devhooks.used.txt");
            try { if (File.Exists(used)) File.Delete(used); File.Move(file, used); } catch { }
            // game_log.txt is always written now (Xbox/UwpLog.cs); FNF_LOG=1 is accepted but no longer needed.
            Console.WriteLine($"[XBOXDEV] devhooks loaded {DateTime.Now:O} content={AssetManager.ContentRoot}");
            InputManager.DevShotClock = () => Environment.GetEnvironmentVariable("FNF_SHOTS_FROMSCENE") == "1"
                ? SceneManager.DevSceneGameTime : Clock.Elapsed.TotalSeconds;
        }
        catch { }
    }

    /// <summary>First scene after loading (dev hooks) or null = TitleScene.</summary>
    public static Scene StartScene()
    {
        if (!Active) return null;
        string autoSong = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_SONG");
        if (!string.IsNullOrEmpty(autoSong))
        {
            string ws = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_WEEKSONGS");
            return new Scenes.PlayScene(new Scenes.PlayStateParams
            {
                SongName = autoSong,
                TargetDifficulty = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_DIFF") ?? "normal",
                BotPlayMode = Environment.GetEnvironmentVariable("FNF_AUTOPLAY_BOT") != "0",
                WeekSongs = !string.IsNullOrEmpty(ws) ? new List<string>(ws.Split(',')) : null,
                WeekSongIndex = !string.IsNullOrEmpty(ws) ? Math.Max(0, Array.IndexOf(ws.Split(','), autoSong)) : 0,
            });
        }
        switch (Environment.GetEnvironmentVariable("FNF_START_SCENE"))
        {
            case "mainmenu": return new Scenes.MainMenuScene();
            case "options": return new Scenes.OptionsScene();
            case "story": return new Scenes.StoryModeScene();
            case "mods": return new Scenes.ModsScene();
            case "skins": return new Scenes.SkinsScene();
            case "achievements": return new Scenes.AchievementsScene();
            case "freeplay": return new Scenes.FreeplayScene();
            case "multiplayer": return new Scenes.MultiplayerScene();
        }
        return null;
    }

    /// <summary>Called after the scenes drew into the 1280x720 render target.</summary>
    public static void AfterDraw(XboxGame game, RenderTarget2D rt)
    {
        if (!Active || rt == null) return;
        if (_shotTimes == null)
        {
            var spec = Environment.GetEnvironmentVariable("FNF_SHOTS");
            _shotTimes = string.IsNullOrEmpty(spec) ? Array.Empty<float>()
                : spec.Split(',').Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
        }
        if (_shotIndex >= _shotTimes.Length) return;
        double now = InputManager.DevShotClock?.Invoke() ?? Clock.Elapsed.TotalSeconds;
        var ps = game.Scenes?.CurrentScene as Scenes.PlayScene;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_SONGTIME") == "1") now = ps?.SongPositionSeconds ?? -1;
        if (Environment.GetEnvironmentVariable("FNF_SHOTS_VIDEOTIME") == "1") now = ps?.DevVideoTime ?? -1;
        if (now < _shotTimes[_shotIndex]) return;
        try
        {
            string dir = Environment.GetEnvironmentVariable("FNF_SHOT_DIR") ?? Path.Combine(LocalState, "shots");
            Directory.CreateDirectory(dir);
            using (var fs = File.Create(Path.Combine(dir, $"shot_{_shotIndex:D2}.png")))
                rt.SaveAsPng(fs, rt.Width, rt.Height);
            if (Environment.GetEnvironmentVariable("FNF_MEMSHOT") == "1") FNF_MonoGame.Engine.MemStats.Log("shot");
            Console.WriteLine($"[DEVSHOT] #{_shotIndex} wall={Clock.Elapsed.TotalSeconds:F3} scene={game.Scenes?.CurrentScene?.GetType().Name}"
                + (ps != null ? $" song={ps.SongPositionSeconds:F3} video={ps.DevVideoTime:F3}" : ""));
        }
        catch (Exception ex) { Console.WriteLine($"[DEVSHOT] {ex.GetType().Name}: {ex.Message}"); }
        _shotIndex++;
        game.ResetElapsedTime();
        if (_shotIndex >= _shotTimes.Length && Environment.GetEnvironmentVariable("FNF_SHOT_EXIT") == "1")
            try { Windows.UI.Xaml.Application.Current.Exit(); } catch { }
    }
}
