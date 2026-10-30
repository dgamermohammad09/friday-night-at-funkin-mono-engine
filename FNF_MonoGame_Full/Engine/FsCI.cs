using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FNF_MonoGame.Engine;

/// <summary>Case-insensitive file access for Linux/Android (and macOS volumes formatted case-sensitive).
/// FNF, Psych and V-Slice content + mods are made on Windows, where "Images/BF.png" and "images/bf.png" are the
/// same file; on a case-sensitive file system the lookup fails. Every File/Directory call in the engine goes
/// through here: on Windows/Xbox (backslash separator) it is a plain pass-through, so behaviour there is unchanged.
/// Elsewhere a missing path is resolved segment by segment ignoring case (and '\' is treated as a separator).</summary>
public static class FsCI
{
    private static readonly bool Sensitive = Path.DirectorySeparatorChar == '/';
    private static readonly object Lock = new object();
    // dir -> (lower-case name -> real name), refreshed when the directory's write time changes
    private static readonly Dictionary<string, KeyValuePair<DateTime, Dictionary<string, string>>> Listings =
        new Dictionary<string, KeyValuePair<DateTime, Dictionary<string, string>>>(StringComparer.Ordinal);

    /// <summary>The real on-disk spelling of <paramref name="path"/> (or the input when nothing matches).</summary>
    /// <summary>Dev: FNF_FILELOG=&lt;file&gt; appends every distinct path the engine touches (Store build content audit).</summary>
    private static readonly string FileLog = Environment.GetEnvironmentVariable("FNF_FILELOG");
    private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static string Fix(string path)
    {
        if (FileLog != null && !string.IsNullOrEmpty(path))
            lock (Logged) if (Logged.Add(path)) try { File.AppendAllText(FileLog, path + "\n"); } catch { }
        if (!Sensitive || string.IsNullOrEmpty(path)) return path;
        string p = path.IndexOf('\\') >= 0 ? path.Replace('\\', '/') : path;
        if (File.Exists(p) || Directory.Exists(p)) return p;
        string full;
        try { full = Path.GetFullPath(p); } catch { return p; }
        string[] parts = full.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string cur = "/";
        for (int i = 0; i < parts.Length; i++)
        {
            string next = Path.Combine(cur, parts[i]);
            if (!File.Exists(next) && !Directory.Exists(next))
            {
                string real = Lookup(cur, parts[i]);
                if (real == null) return p; // genuinely missing: keep the caller's path (and its error message)
                next = Path.Combine(cur, real);
            }
            cur = next;
        }
        return cur;
    }

    private static string Lookup(string dir, string name)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            DateTime stamp = Directory.GetLastWriteTimeUtc(dir);
            Dictionary<string, string> map;
            lock (Lock)
            {
                if (!Listings.TryGetValue(dir, out var kv) || kv.Key != stamp)
                {
                    map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var e in Directory.EnumerateFileSystemEntries(dir))
                    {
                        string n = Path.GetFileName(e);
                        string k = n.ToLowerInvariant();
                        if (!map.ContainsKey(k)) map[k] = n;
                    }
                    Listings[dir] = new KeyValuePair<DateTime, Dictionary<string, string>>(stamp, map);
                }
                else map = kv.Value;
            }
            return map.TryGetValue(name.ToLowerInvariant(), out var real) ? real : null;
        }
        catch { return null; }
    }

    public static bool FileExists(string path) => File.Exists(Fix(path));
    public static bool DirectoryExists(string path) => Directory.Exists(Fix(path));
    public static string ReadAllText(string path) => File.ReadAllText(Fix(path));
    public static string ReadAllText(string path, Encoding enc) => File.ReadAllText(Fix(path), enc);
    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(Fix(path));
    public static string[] ReadAllLines(string path) => File.ReadAllLines(Fix(path));
    public static string[] ReadAllLines(string path, Encoding enc) => File.ReadAllLines(Fix(path), enc);
    public static IEnumerable<string> ReadLines(string path) => File.ReadLines(Fix(path));
    public static FileStream OpenRead(string path) => File.OpenRead(Fix(path));
    public static string[] GetFiles(string path) => Directory.GetFiles(Fix(path));
    public static string[] GetFiles(string path, string pattern) => Directory.GetFiles(Fix(path), pattern);
    public static string[] GetFiles(string path, string pattern, SearchOption o) => Directory.GetFiles(Fix(path), pattern, o);
    public static string[] GetDirectories(string path) => Directory.GetDirectories(Fix(path));
    public static string[] GetDirectories(string path, string pattern) => Directory.GetDirectories(Fix(path), pattern);
    public static string[] GetDirectories(string path, string pattern, SearchOption o) => Directory.GetDirectories(Fix(path), pattern, o);
    public static IEnumerable<string> EnumerateFiles(string path) => Directory.EnumerateFiles(Fix(path));
    public static IEnumerable<string> EnumerateFiles(string path, string pattern) => Directory.EnumerateFiles(Fix(path), pattern);
    public static IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption o) => Directory.EnumerateFiles(Fix(path), pattern, o);
    public static IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(Fix(path));
    public static IEnumerable<string> EnumerateDirectories(string path, string pattern) => Directory.EnumerateDirectories(Fix(path), pattern);
    public static IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption o) => Directory.EnumerateDirectories(Fix(path), pattern, o);
}
