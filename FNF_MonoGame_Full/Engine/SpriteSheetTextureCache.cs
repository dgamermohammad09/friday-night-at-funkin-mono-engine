using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Shared, ref-counted cache of atlas textures loaded by <see cref="SpriteSheet.Load"/> (Sparrow PNG and Animate
/// spritemap1.png). Scenes still create and Dispose their own SpriteSheet; Dispose only releases the texture here.
/// Released textures stay resident (LRU, size budget) so the next scene / song that needs the same atlas (notes,
/// receptors, menu art, the same stage/characters on Retry or the next song of a week) skips PNG decode + upload.
/// Disabled with Options > Preferences > Cache Assets = off, or FNF_NOCACHE=1 (load-time measurements).
/// </summary>
public static class SpriteSheetTextureCache
{
    private sealed class Entry
    {
        public Texture2D Tex; public int Refs; public long Bytes;
        public Dictionary<(ulong, ulong), List<(Texture2D Tex, Microsoft.Xna.Framework.Rectangle Rect, Microsoft.Xna.Framework.Vector2 Origin)>> Composites; public long LastUse; public DateTime Mtime; public string Path;
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<string, Entry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Texture2D, Entry> _byTex = new();
    private static readonly Dictionary<Texture2D, Entry> _compOwner = new();
    private static long _clock, _hits, _misses, _compHits;

    private static readonly bool _envOff = Environment.GetEnvironmentVariable("FNF_NOCACHE") == "1";
    public static bool Enabled => !_envOff && HighscoreManager.Data.AssetCache;

    /// <summary>Bytes of UNREFERENCED textures kept for reuse. UWP apps on Xbox have a much smaller memory budget.</summary>
#if XBOX_UWP
    // Xbox "App" type has ~1 GB for everything: keep at most 64 MB of idle atlases, 32 MB while the next scene loads
    public static long BudgetBytes = 64L * 1024 * 1024, ScenesSwitchBudgetBytes = 32L * 1024 * 1024;
#else
    // While a scene switch is in progress (old scene released, new one loading) keep up to ScenesSwitchBudgetBytes so
    // the new scene can pick up what it shares with the old one; TrimToBudget() runs once the new scene is ready.
    public static long BudgetBytes = 768L * 1024 * 1024, ScenesSwitchBudgetBytes = 2560L * 1024 * 1024;
#endif

    /// <summary>Scene finished loading: drop idle textures beyond the normal budget (LRU).</summary>
    public static void TrimToBudget() { lock (_lock) Trim(BudgetBytes); }

    public static string Stats
    {
        get { lock (_lock) { long b = 0; foreach (var e in _byPath.Values) b += e.Bytes; return $"{_hits}h/{_misses}m {_byPath.Count} tex {b / (1024 * 1024)}MB comp {_compHits}h"; } }
    }

    /// <summary>Per-entry sizes (largest first) for memory diagnostics.</summary>
    public static string Dump(int max = 25)
    {
        lock (_lock)
        {
            var l = new List<Entry>(_byPath.Values);
            l.Sort((x, y) => y.Bytes.CompareTo(x.Bytes));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < l.Count && i < max; i++)
            {
                var e = l[i]; long atlas = (long)e.Tex.Width * e.Tex.Height * 4;
                sb.Append($"  [TEX] {e.Bytes / 1048576,5}MB atlas {e.Tex.Width}x{e.Tex.Height} {atlas / 1048576}MB comp {(e.Bytes - atlas) / 1048576}MB refs={e.Refs} {e.Path}\n");
            }
            return sb.ToString();
        }
    }

    /// <summary>Texture for an atlas PNG: cached copy when possible, else decoded from disk.</summary>
    public static Texture2D Acquire(GraphicsDevice gd, string path)
    {
        if (!Enabled || string.IsNullOrEmpty(path)) return AssetManager.TextureFromFile(gd, path);
        DateTime mt;
        try { mt = File.GetLastWriteTimeUtc(path); } catch { mt = default; }
        lock (_lock)
        {
            if (_byPath.TryGetValue(path, out var e))
            {
                if (!e.Tex.IsDisposed && e.Mtime == mt && e.Tex.GraphicsDevice == gd)
                {
                    e.Refs++; e.LastUse = ++_clock; _hits++;
                    return e.Tex;
                }
                if (e.Refs == 0) DisposeEntry(e); else Forget(e);
            }
        }
        var tex = AssetManager.TextureFromFile(gd, path);   // decode outside the lock (preload threads)
        lock (_lock)
        {
            _misses++;
            if (_byPath.ContainsKey(path)) return tex;          // another thread raced us: keep this one private
            var e = new Entry { Tex = tex, Refs = 1, Bytes = (long)tex.Width * tex.Height * 4, LastUse = ++_clock, Mtime = mt, Path = path };
            _byPath[path] = e; _byTex[tex] = e;
        }
        return tex;
    }

    /// <summary>Called by SpriteSheet.Dispose. True = the cache owns the texture (do not Dispose it).</summary>
    public static bool Release(Texture2D tex)
    {
        if (tex == null) return false;
        lock (_lock)
        {
            if (!_byTex.TryGetValue(tex, out var e)) return false;
            if (e.Refs > 0) e.Refs--;
            e.LastUse = ++_clock;
            if (!Enabled) { if (e.Refs == 0) DisposeEntry(e); return true; }
            Trim(ScenesSwitchBudgetBytes);
            return true;
        }
    }


    public static bool IsShared(Texture2D atlas)
    {
        if (atlas == null) return false;
        lock (_lock) return _byTex.ContainsKey(atlas);
    }

    /// <summary>Pre-rendered composite frames already made from this cached atlas with identical parts.</summary>
    public static bool TryGetComposite(Texture2D atlas, (ulong, ulong) key, out List<(Texture2D Tex, Microsoft.Xna.Framework.Rectangle Rect, Microsoft.Xna.Framework.Vector2 Origin)> result)
    {
        result = null;
        lock (_lock)
        {
            if (!_byTex.TryGetValue(atlas, out var e) || e.Composites == null || !e.Composites.TryGetValue(key, out var l)) return false;
            foreach (var c in l) if (c.Tex != null && c.Tex.IsDisposed) { e.Composites.Remove(key); return false; }
            _compHits++;
            result = new List<(Texture2D, Microsoft.Xna.Framework.Rectangle, Microsoft.Xna.Framework.Vector2)>(l);
            return true;
        }
    }

    public static void AddComposite(Texture2D atlas, (ulong, ulong) key, List<(Texture2D Tex, Microsoft.Xna.Framework.Rectangle Rect, Microsoft.Xna.Framework.Vector2 Origin)> made)
    {
        if (made == null) return;
        lock (_lock)
        {
            if (!_byTex.TryGetValue(atlas, out var e)) return;
            e.Composites ??= new();
            if (e.Composites.ContainsKey(key)) return;
            e.Composites[key] = new List<(Texture2D, Microsoft.Xna.Framework.Rectangle, Microsoft.Xna.Framework.Vector2)>(made);
            foreach (var c in made)
                if (c.Tex != null && !_compOwner.ContainsKey(c.Tex)) { _compOwner[c.Tex] = e; e.Bytes += (long)c.Tex.Width * c.Tex.Height * 4; }
        }
    }

    /// <summary>SpriteSheet.Dispose: composite textures owned by the cache live (and die) with their atlas entry.</summary>
    public static bool OwnsComposite(Texture2D tex)
    {
        lock (_lock) return _compOwner.ContainsKey(tex);
    }

    private static void DisposeEntry(Entry e)
    {
        Forget(e);
        e.Tex.Dispose();
        if (e.Composites == null) return;
        foreach (var l in e.Composites.Values) foreach (var c in l) if (c.Tex != null && !c.Tex.IsDisposed) c.Tex.Dispose();
    }

    /// <summary>The caller is about to modify the texture's pixels (e.g. premultiply): stop sharing it.</summary>
    public static void Detach(Texture2D tex)
    {
        if (tex == null) return;
        lock (_lock) { if (_byTex.TryGetValue(tex, out var e)) Forget(e); }
    }

    /// <summary>Drop every unreferenced texture (e.g. when memory is tight).</summary>
    public static void Purge()
    {
        lock (_lock)
        {
            var dead = new List<Entry>();
            foreach (var e in _byPath.Values) if (e.Refs == 0) dead.Add(e);
            foreach (var e in dead) DisposeEntry(e);
        }
    }

    private static void Trim(long budget)
    {
        long idle = 0;
        foreach (var e in _byPath.Values) if (e.Refs == 0) idle += e.Bytes;
        while (idle > budget)
        {
            Entry oldest = null;
            foreach (var e in _byPath.Values) if (e.Refs == 0 && (oldest == null || e.LastUse < oldest.LastUse)) oldest = e;
            if (oldest == null) break;
            DisposeEntry(oldest); idle -= oldest.Bytes;
        }
    }

    private static void Forget(Entry e)
    {
        if (_byPath.TryGetValue(e.Path, out var cur) && cur == e) _byPath.Remove(e.Path);
        _byTex.Remove(e.Tex);
        if (e.Composites != null) foreach (var l in e.Composites.Values) foreach (var c in l) if (c.Tex != null) _compOwner.Remove(c.Tex);
    }
}
