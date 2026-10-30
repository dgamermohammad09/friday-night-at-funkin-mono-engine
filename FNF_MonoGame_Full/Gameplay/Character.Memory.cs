using System;
using System.Linq;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Xbox/UWP memory: per-animation atlases that only hold death animations (firstDeath / deathLoop / deathConfirm /
/// fakeoutDeath: picoAndNene-DEAD = 390 MB of pre-rendered frames, Pico_Death 84 MB) are not loaded with the
/// character; PlayScene loads them when the game over opens (same merge code, just later). Desktop loads them up
/// front as before (FNF_DEFER_DEATH=1 enables the UWP behaviour on desktop for testing).
/// </summary>
public partial class Character
{
    private Action _deferredDeathLoad;

#if XBOX_UWP
    public static bool DeferDeathAtlases = Environment.GetEnvironmentVariable("FNF_DEFER_DEATH") != "0";
#else
    public static bool DeferDeathAtlases = Environment.GetEnvironmentVariable("FNF_DEFER_DEATH") == "1";
#endif

    private static bool IsDeathAnimName(string n) => n != null && n.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>True when every JSON animation using this atlas is a death animation.</summary>
    private bool DeferDeathAtlas(string assetPath)
    {
        if (!DeferDeathAtlases || JsonData?.Animations == null) return false;
        var users = JsonData.Animations.Where(a => string.Equals(a.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase)).ToList();
        return users.Count > 0 && users.All(a => IsDeathAnimName(a.Name));
    }

    public bool HasDeferredDeathAtlases => _deferredDeathLoad != null;

    /// <summary>
    /// UWP low memory (App type, ~1 GB) or FNF_LAZYCHAR=1: atlas timeline animations that no character JSON animation
    /// refers to (e.g. Spaghetti Sakura's 24 symbol duplicates of her labels) are not pre-rendered; they become JIT
    /// composites rendered on first PlayAnimation (none of them is played in practice). Death anims without indices are
    /// left to JIT too (played at game over only). Anims the JSON uses stay pre-rendered (no gameplay hitches).
    /// </summary>
#if XBOX_UWP
    public static bool LazyCharComposites => Environment.GetEnvironmentVariable("FNF_LAZYCHAR") == "1"
        || (Environment.GetEnvironmentVariable("FNF_LAZYCHAR") != "0" && FNF_MonoGame.XboxGame.LowMemoryLimit);
#else
    public static bool LazyCharComposites => Environment.GetEnvironmentVariable("FNF_LAZYCHAR") == "1";
#endif

    private System.Collections.Generic.HashSet<string> LazyCompositeKeepSet()
    {
        if (!LazyCharComposites || JsonData?.Animations == null) return null;
        var keep = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { "idle" };
        foreach (var a in JsonData.Animations)
        {
            bool hasIndices = a.FrameIndices != null && a.FrameIndices.Length > 0;
            if (IsDeathAnimName(a.Name) && !hasIndices) continue;
            if (!string.IsNullOrEmpty(a.Prefix)) keep.Add(a.Prefix.Trim());
            if (!string.IsNullOrEmpty(a.Name)) keep.Add(a.Name.Trim());
        }
        if (!string.IsNullOrEmpty(JsonData.StartingAnimation)) keep.Add(JsonData.StartingAnimation.Trim());
        return keep;
    }

    /// <summary>UWP low memory, game over: the opponent / GF are not drawn any more (GameOverSubState, persistentDraw
    /// false) -> free their composite render targets before the death atlases load. Retry builds a new PlayScene.</summary>
    public static bool FreeOthersAtGameOver =>
#if XBOX_UWP
        Environment.GetEnvironmentVariable("FNF_GO_FREE") == "1" || (Environment.GetEnvironmentVariable("FNF_GO_FREE") != "0" && FNF_MonoGame.XboxGame.LowMemoryLimit);
#else
        Environment.GetEnvironmentVariable("FNF_GO_FREE") == "1";
#endif

    public long ReleaseCompositeTextures() => _sprite?.Sheet?.DisposeCompositeTextures() ?? 0;

    /// <summary>Game over: load + merge the death-only atlases skipped at character load.</summary>
    public void LoadDeferredDeathAtlases()
    {
        var a = _deferredDeathLoad; _deferredDeathLoad = null;
        if (a == null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { a(); } catch (Exception ex) { Console.WriteLine($"[MEM] deferred death atlas load failed for {Name}: {ex.Message}"); }
        Console.WriteLine($"[MEM] {Name}: death atlases loaded in {sw.ElapsedMilliseconds} ms");
        FNF_MonoGame.Engine.MemStats.Log("gameover death atlases");
    }
}
