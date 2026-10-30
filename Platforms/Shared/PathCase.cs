#if FNF_CROSSPLATFORM
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace FNF_MonoGame.Engine;

/// <summary>Case-insensitive path fallback for case-sensitive file systems (Linux, Android, case-sensitive APFS).
/// The game data was authored on Windows; when <c>global::FNF_MonoGame.Engine.FsCI.FileExists(path)</c> fails, every segment below a known root is
/// matched ignoring case (directory listings are cached). Only used by the cross-platform builds.</summary>
public static class PathCase
{
    private static readonly bool CaseSensitiveFs = !OperatingSystem.IsWindows();
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Listings = new();

    /// <summary>Returns the real path of <paramref name="full"/> (file or directory) or null.</summary>
    public static string Fix(string full, bool directory = false)
    {
        if (string.IsNullOrEmpty(full)) return null;
        if (directory ? global::FNF_MonoGame.Engine.FsCI.DirectoryExists(full) : global::FNF_MonoGame.Engine.FsCI.FileExists(full)) return full;
        if (!CaseSensitiveFs) return null;
        try
        {
            full = Path.GetFullPath(full.Replace('\\', '/'));
            string root = Path.GetPathRoot(full) ?? "/";
            string cur = root;
            var parts = full.Substring(root.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string next = Path.Combine(cur, parts[i]);
                bool last = i == parts.Length - 1;
                if (last ? (directory ? global::FNF_MonoGame.Engine.FsCI.DirectoryExists(next) : global::FNF_MonoGame.Engine.FsCI.FileExists(next)) : global::FNF_MonoGame.Engine.FsCI.DirectoryExists(next)) { cur = next; continue; }
                var map = Listings.GetOrAdd(cur, Load);
                if (!map.TryGetValue(parts[i], out var real)) return null;
                cur = Path.Combine(cur, real);
            }
            return (directory ? global::FNF_MonoGame.Engine.FsCI.DirectoryExists(cur) : global::FNF_MonoGame.Engine.FsCI.FileExists(cur)) ? cur : null;
        }
        catch { return null; }
    }

    private static Dictionary<string, string> Load(string dir)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var e in Directory.EnumerateFileSystemEntries(dir)) { var n = Path.GetFileName(e); d.TryAdd(n, n); } } catch { }
        return d;
    }
}
#endif
