using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// V-Slice (Polymod) mod layer: mods with a _polymod_meta.json are overlaid on the engine's Content layout the way
/// Polymod overlays them on the original's asset libraries (PolymodHandler.loadModsById):
///  - api_version must satisfy PolymodHandler.API_VERSION_RULE ("&gt;=0.8.0 &lt;0.9.0"), otherwise the mod is not loaded;
///  - a mod file REPLACES the base file with the same path (later mods win);
///  - _append/&lt;path&gt; and _merge/&lt;path&gt; patch the (base or modded) JSON file (Polymod: JSON Patch RFC 6902 ops).
/// Path mapping engine -&gt; mod:  songs/&lt;id&gt;[_&lt;var&gt;]/charts/meta.json|chart.json -&gt; data/songs/&lt;id&gt;/&lt;id&gt;-metadata|chart[-&lt;var&gt;].json,
/// songs/&lt;id&gt;/X.ogg -&gt; songs/&lt;id&gt;/X.ogg, images|sounds|music/X -&gt; [&lt;library&gt;/]images|sounds|music/X, data/X -&gt; data/X.
/// </summary>
public static class VSliceMods
{
    public const string ApiVersionRule = ">=0.8.0 <0.9.0";
    static readonly string[] Libraries = { "", "shared/", "preload/", "week1/", "week2/", "week3/", "week4/", "week5/", "week6/", "week7/", "weekend1/", "tutorial/", "videos/" };

    public sealed class Mod
    {
        public string Id, Root, Title, ApiVersion, ModVersion;
        public JObject Meta;
    }

    static List<Mod> _mods;                      // load order (last = highest priority)
    static readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, (Mod mod, string id, string var)> _songFolders;
    static string _modsRoot;
    public static readonly List<string> Rejected = new();

    public static IReadOnlyList<Mod> Mods { get { EnsureLoaded(); return _mods ?? _none; } }
    public static bool Any { get { EnsureLoaded(); return _mods != null && _mods.Count > 0; } }

    static readonly List<Mod> _none = new();
    static void EnsureLoaded()
    {
        if (_mods != null) return;
        if (string.IsNullOrEmpty(PsychEngine.PsychMods.ModsBasePath)) { _modsTmp(); return; }
        Refresh();
    }
    static void _modsTmp() { /* mods folder not initialised yet: behave as "no mods" without caching */ }

    /// <summary>Rescan enabled mods (call after the Mods screen changes the enabled list).</summary>
    public static void Refresh()
    {
        _mods = new List<Mod>(); _cache.Clear(); _dirCache.Clear(); _songFolders = null; Rejected.Clear();
        try
        {
            _modsRoot = PsychEngine.PsychMods.ModsBasePath;
            foreach (var e in PsychEngine.PsychMods.GetAllMods())
            {
                if (!e.Enabled) continue;
                string dir = PsychEngine.PsychMods.GetModDirectory(e.Name);
                if (dir == null) continue;
                string metaPath = Path.Combine(dir, "_polymod_meta.json");
                if (!global::FNF_MonoGame.Engine.FsCI.FileExists(metaPath)) continue;
                JObject meta = null;
                try { meta = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(metaPath)); } catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] {e.Name}: bad _polymod_meta.json: {ex.Message}"); }
                string api = meta?["api_version"]?.ToString() ?? "";
                if (!SatisfiesRule(api, ApiVersionRule))
                {
                    // Polymod: VERSION_CONFLICT_API -> the mod is skipped.
                    Console.WriteLine($"[VSLICE-MODS] {e.Name}: api_version '{api}' does not match {ApiVersionRule}, not loaded");
                    Rejected.Add(e.Name);
                    continue;
                }
                _mods.Add(new Mod { Id = e.Name, Root = dir, Title = meta?["title"]?.ToString() ?? e.Name, ApiVersion = api, ModVersion = meta?["mod_version"]?.ToString(), Meta = meta });
                Console.WriteLine($"[VSLICE-MODS] loaded {e.Name} (api {api})");
            }
        }
        catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] refresh failed: {ex.Message}"); }
        try { ScanSpriteElements(); } catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] element scan: {ex.Message}"); }
    }

    /// <summary>FlxSpriteElement inserted by a character script (onCreate: frame.add(new FlxSpriteElement(new X(..)))
    /// for getFramesWithKeyword(kw)): character id -> keyword symbol + the element's texture atlas.</summary>
    public sealed class SpriteElementSpec { public string CharId, Keyword, AtlasPath, AnimOnDance; }
    public static readonly Dictionary<string, SpriteElementSpec> SpriteElements = new(StringComparer.OrdinalIgnoreCase);

    static void ScanSpriteElements()
    {
        SpriteElements.Clear();
        var files = new List<string>();
        foreach (var mod in _mods) { string d = Path.Combine(mod.Root, "scripts"); if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(d)) files.AddRange(global::FNF_MonoGame.Engine.FsCI.GetFiles(d, "*.hxc", SearchOption.AllDirectories)); }
        var sources = files.ToDictionary(f => f, f => global::FNF_MonoGame.Engine.FsCI.ReadAllText(f));
        foreach (var (f, src) in sources)
        {
            var kw = System.Text.RegularExpressions.Regex.Match(src, @"getFramesWithKeyword\(\s*[""']([^""']+)[""']");
            var id = System.Text.RegularExpressions.Regex.Match(src, @"super\(\s*[""']([^""']+)[""']");
            var el = System.Text.RegularExpressions.Regex.Match(src, @"new\s+FlxSpriteElement\(\s*(\w+)");
            if (!kw.Success || !id.Success || !el.Success) continue;
            // the element variable's class: "<var> = new <Class>(" in the same file
            var cls = System.Text.RegularExpressions.Regex.Match(src, el.Groups[1].Value + @"\s*=\s*new\s+(\w+)\s*\(");
            if (!cls.Success) continue;
            string atlas = null;
            foreach (var src2 in sources.Values)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(src2, @"class\s+" + cls.Groups[1].Value + @"\b")) continue;
                var la = System.Text.RegularExpressions.Regex.Match(src2, @"loadTextureAtlas\(\s*[""']([^""']+)[""']");
                if (la.Success) atlas = la.Groups[1].Value;
            }
            if (atlas == null) continue;
            var play = System.Text.RegularExpressions.Regex.Match(src, el.Groups[1].Value.Replace("element", "") + @"[\w.]*animation\.play\(\s*[""']([^""']+)[""']");
            var anyPlay = System.Text.RegularExpressions.Regex.Match(src, @"\.animation\.play\(\s*[""']([^""']+)[""']\s*,\s*true");
            string charId = id.Groups[1].Value;
            SpriteElements[charId] = new SpriteElementSpec { CharId = charId, Keyword = kw.Groups[1].Value, AtlasPath = atlas, AnimOnDance = anyPlay.Success ? anyPlay.Groups[1].Value : null };
            // the character's atlas path -> keyword (SpriteSheet records the keyword symbol's matrices while loading)
            string cj = Resolve($"data/characters/{charId}.json");
            if (cj != null)
            {
                var j = ParseLenient(global::FNF_MonoGame.Engine.FsCI.ReadAllText(cj));
                string ap = j["assetPath"]?.ToString();
                if (ap != null)
                {
                    if (ap.Contains(':')) ap = ap.Substring(ap.IndexOf(':') + 1);
                    SpriteSheet.AnchorKeywords[ap] = kw.Groups[1].Value;
                    Console.WriteLine($"[VSLICE-MODS] sprite element: {charId} '{kw.Groups[1].Value}' <- {atlas} ({ap})");
                }
            }
        }
    }

    // ---------------------------------------------------------------- semver (thx.semver rules used by Polymod)
    static int[] ParseVer(string v)
    {
        var m = System.Text.RegularExpressions.Regex.Match(v ?? "", @"(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!m.Success) return null;
        return new[] { int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0, m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0 };
    }
    static int Cmp(int[] a, int[] b) { for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]); return 0; }
    public static bool SatisfiesRule(string version, string rule)
    {
        var v = ParseVer(version); if (v == null) return false;
        foreach (var part in rule.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string op = new string(part.TakeWhile(c => c == '>' || c == '<' || c == '=').ToArray());
            var r = ParseVer(part.Substring(op.Length)); if (r == null) continue;
            int c = Cmp(v, r);
            bool ok = op switch { ">=" => c >= 0, ">" => c > 0, "<=" => c <= 0, "<" => c < 0, _ => c == 0 };
            if (!ok) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- song folder index
    static Dictionary<string, (Mod mod, string id, string var)> SongFolders()
    {
        if (_songFolders != null) return _songFolders;
        var d = new Dictionary<string, (Mod, string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in _mods)   // later mods overwrite earlier ones
            foreach (var root in new[] { Path.Combine(mod.Root, "data", "songs"), Path.Combine(mod.Root, "_merge", "data", "songs") })
            {
                if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(root)) continue;
                foreach (var dir in global::FNF_MonoGame.Engine.FsCI.GetDirectories(root))
                {
                    string id = Path.GetFileName(dir).ToLowerInvariant();
                    foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*-metadata*.json"))
                    {
                        string fn = Path.GetFileNameWithoutExtension(f);
                        int k = fn.IndexOf("-metadata", StringComparison.OrdinalIgnoreCase);
                        if (k < 0) continue;
                        string rest = fn.Substring(k + 9);
                        string v = rest.StartsWith("-") ? rest.Substring(1).ToLowerInvariant() : null;
                        foreach (var key in v == null ? new[] { id, id.Replace('-', '_') } : new[] { id + "_" + v, id.Replace('-', '_') + "_" + v })
                            if (!(root.Contains("_merge") && d.ContainsKey(key))) d[key] = (mod, id, v);
                    }
                }
            }
        return _songFolders = d;
    }

    /// <summary>Song ids (data/songs folder names) provided by V-Slice mods.</summary>
    public static IEnumerable<string> ModSongIds() { EnsureLoaded(); if (_mods == null) return Enumerable.Empty<string>(); return SongFolders().Values.Where(x => x.var == null).Select(x => x.id).Distinct(); }

    /// <summary>The mod that provides song &lt;id&gt; (or null).</summary>
    public static Mod ModForSong(string songFolder)
    {
        EnsureLoaded(); if (_mods == null || _mods.Count == 0 || songFolder == null) return null;
        return SongFolders().TryGetValue(songFolder, out var e) ? e.mod : null;
    }

    // ---------------------------------------------------------------- path resolution
    /// <summary>Resolve an engine-relative path to a V-Slice mod file (patched copy for _merge/_append JSON). Null = not modded.</summary>
    public static string Resolve(string relativePath, Func<string, string> baseResolver = null)
    {
        EnsureLoaded();
        if (_mods == null || _mods.Count == 0 || string.IsNullOrEmpty(relativePath)) return null;
        string norm = relativePath.Replace('\\', '/').TrimStart('/');
        lock (_cache)
        {
            if (_cache.TryGetValue(norm, out var hit)) return hit;
        }
        string res = null;
        try { res = ResolveUncached(norm, baseResolver); } catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] resolve {norm}: {ex.Message}"); }
        lock (_cache) _cache[norm] = res;
        return res;
    }

    /// <summary>Candidate mod-relative paths for an engine path (most specific first).</summary>
    static IEnumerable<string> Candidates(string norm)
    {
        var parts = norm.Split('/');
        if (parts.Length >= 4 && parts[0].Equals("songs", StringComparison.OrdinalIgnoreCase) && parts[2].Equals("charts", StringComparison.OrdinalIgnoreCase))
        {
            if (SongFolders().TryGetValue(parts[1], out var s))
            {
                string kind = parts[3].ToLowerInvariant() switch { "meta.json" => "metadata", "chart.json" => "chart", _ => null };
                if (kind != null) yield return $"data/songs/{s.id}/{s.id}-{kind}{(s.var != null ? "-" + s.var : "")}.json";
            }
            yield break;
        }
        if (parts.Length >= 3 && parts[0].Equals("songs", StringComparison.OrdinalIgnoreCase))
        {
            string id = SongFolders().TryGetValue(parts[1], out var s) ? s.id : parts[1];
            string rest = string.Join("/", parts.Skip(2));
            yield return $"songs/{id}/{rest}";
            if (id != parts[1]) yield return $"songs/{parts[1]}/{rest}";
            yield break;
        }
        if (norm.StartsWith("game/characters/", StringComparison.OrdinalIgnoreCase))
        {
            string rest = norm.Substring(16);
            yield return "shared/images/characters/" + rest;
            yield return "images/characters/" + rest;
            yield break;
        }
        if (norm.StartsWith("menus/", StringComparison.OrdinalIgnoreCase))
        {
            // the engine keeps original images/<menu>/... under Content/menus/<menu>/... (e.g. menus/freeplay/icons)
            foreach (var lib in Libraries) yield return lib + "images/" + norm.Substring(6);
            yield break;
        }
        if (norm.StartsWith("images/") || norm.StartsWith("sounds/") || norm.StartsWith("music/"))
        {
            foreach (var lib in Libraries) yield return lib + norm;
            yield break;
        }
        yield return norm;
    }

    static readonly Dictionary<string, string> _dirCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Directory variant of <see cref="Resolve"/> (Adobe Animate atlas folders).</summary>
    public static string ResolveDirectory(string relativePath)
    {
        EnsureLoaded();
        if (_mods == null || _mods.Count == 0 || string.IsNullOrEmpty(relativePath)) return null;
        string norm = relativePath.Replace('\\', '/').TrimStart('/').TrimEnd('/');
        lock (_dirCache) if (_dirCache.TryGetValue(norm, out var hit)) return hit;
        string res = null;
        foreach (var cand in Candidates(norm))
        {
            for (int i = _mods.Count - 1; i >= 0 && res == null; i--)
            {
                string p = Path.Combine(_mods[i].Root, cand);
                if (DirExistsExactCase(p)) res = p;
            }
            if (res != null) break;
        }
        lock (_dirCache) _dirCache[norm] = res;
        return res;
    }

    /// <summary>OpenFL/Polymod asset ids are case-sensitive: a mod folder "characters/BF" (holding BF/bf-qt/...) must not
    /// replace the base atlas folder "characters/bf". The folder must also hold files of its own (an atlas folder).</summary>
    static bool DirExistsExactCase(string p)
    {
        try
        {
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(p)) return false;
            string parent = Path.GetDirectoryName(p), name = Path.GetFileName(p);
            if (parent == null) return true;
            bool exact = false;
            foreach (var d in global::FNF_MonoGame.Engine.FsCI.GetDirectories(parent)) if (Path.GetFileName(d) == name) { exact = true; break; }
            return exact && global::FNF_MonoGame.Engine.FsCI.GetFiles(p).Length > 0;
        }
        catch { return false; }
    }

    static string FindInMods(string modRel, out int modIndex)
    {
        for (int i = _mods.Count - 1; i >= 0; i--)
        {
            string p = Path.Combine(_mods[i].Root, modRel);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) { modIndex = i; return p; }
        }
        modIndex = -1; return null;
    }

    static string ResolveUncached(string norm, Func<string, string> baseResolver)
    {
        foreach (var cand in Candidates(norm))
        {
            string found = FindInMods(cand, out _);
            bool isJson = cand.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            if (isJson)
            {
                var patches = PatchFiles(cand);
                if (patches.Count > 0)
                {
                    string src = found ?? baseResolver?.Invoke(norm);
                    if (src != null) return WritePatched(cand, src, patches);
                }
            }
            if (found != null) return found;
        }
        return null;
    }

    static List<(string kind, string path)> PatchFiles(string modRel)
    {
        var list = new List<(string, string)>();
        foreach (var mod in _mods)
            foreach (var kind in new[] { "_append", "_merge" })
            {
                string p = Path.Combine(mod.Root, kind, modRel);
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) list.Add((kind, p));
            }
        return list;
    }

    static string WritePatched(string modRel, string srcPath, List<(string kind, string path)> patches)
    {
        JToken doc = ParseLenient(global::FNF_MonoGame.Engine.FsCI.ReadAllText(srcPath));
        foreach (var (kind, path) in patches)
        {
            try
            {
                var patch = ParseLenient(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path));
                doc = kind == "_merge" ? ApplyMerge(doc, patch) : ApplyAppend(doc, patch);
                Console.WriteLine($"[VSLICE-MODS] {kind} {modRel} <- {path}");
            }
            catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] {kind} {path} failed: {ex.Message}"); }
        }
        string dir = Path.Combine(_modsRoot ?? Path.GetTempPath(), ".vslice_cache");
        Directory.CreateDirectory(dir);
        string outPath = Path.Combine(dir, modRel.Replace('/', '~').Replace('\\', '~'));
        File.WriteAllText(outPath, doc.ToString(Formatting.Indented));
        return outPath;
    }

    public static JToken ParseLenient(string text)
    {
        using var r = new JsonTextReader(new StringReader(text));
        return JToken.ReadFrom(r, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore, DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace });
    }

    // Polymod JSON _merge: an array of JSON Patch operations (RFC 6902). Legacy {"merge":[{target,payload}]} also accepted.
    static JToken ApplyMerge(JToken doc, JToken patch)
    {
        if (patch is JObject po && po["merge"] is JArray legacy)
        {
            foreach (var m in legacy) doc = SetPointer(doc, "/" + (m["target"]?.ToString() ?? "").Replace('.', '/'), m["payload"]?.DeepClone(), false);
            return doc;
        }
        if (patch is JObject single && single["op"] != null) patch = new JArray(single);
        if (patch is not JArray ops) { return DeepMerge(doc, patch, false); }
        foreach (var op in ops)
        {
            string kind = op["op"]?.ToString(), path = op["path"]?.ToString() ?? "";
            if (path.StartsWith("$")) path = JsonPathToPointer(path);
            try { // one invalid op (e.g. KB's add to /animations/- on a player JSON) is skipped, the rest still applies
            switch (kind)
            {
                case "add": doc = SetPointer(doc, path, op["value"]?.DeepClone(), true); break;
                case "replace": doc = SetPointer(doc, path, op["value"]?.DeepClone(), false); break;
                case "remove": RemovePointer(doc, path); break;
                case "copy": { var v = GetPointer(doc, op["from"]?.ToString()); if (v != null) doc = SetPointer(doc, path, v.DeepClone(), true); break; }
                case "move": { string from = op["from"]?.ToString(); var v = GetPointer(doc, from); if (v != null) { RemovePointer(doc, from); doc = SetPointer(doc, path, v, true); } break; }
                case "test": break;
            }
            } catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] _merge op '{kind} {path}' skipped (invalid target): {ex.Message}"); }
        }
        return doc;
    }

    static JToken ApplyAppend(JToken doc, JToken patch)
    {
        if (doc is JArray a && patch is JArray b) { foreach (var x in b) a.Add(x.DeepClone()); return a; }
        return DeepMerge(doc, patch, true);
    }

    static JToken DeepMerge(JToken doc, JToken patch, bool appendArrays)
    {
        if (doc is JObject o && patch is JObject p)
        {
            foreach (var prop in p.Properties())
                o[prop.Name] = o[prop.Name] == null ? prop.Value.DeepClone() : DeepMerge(o[prop.Name], prop.Value, appendArrays);
            return o;
        }
        if (appendArrays && doc is JArray a && patch is JArray b) { foreach (var x in b) a.Add(x.DeepClone()); return a; }
        return patch.DeepClone();
    }

    static string JsonPathToPointer(string p)
    {
        p = p.TrimStart('$');
        p = System.Text.RegularExpressions.Regex.Replace(p, @"\[(\d+|-)\]", ".$1");
        return p.Replace('.', '/');
    }

    static List<string> SplitPointer(string ptr) =>
        (ptr ?? "").Split('/').Skip(1).Select(s => s.Replace("~1", "/").Replace("~0", "~")).ToList();

    static JToken GetPointer(JToken doc, string ptr)
    {
        JToken cur = doc;
        foreach (var seg in SplitPointer(ptr))
        {
            if (cur is JObject o) cur = o[seg];
            else if (cur is JArray a && int.TryParse(seg, out int i) && i >= 0 && i < a.Count) cur = a[i];
            else return null;
            if (cur == null) return null;
        }
        return cur;
    }

    static JToken SetPointer(JToken doc, string ptr, JToken value, bool insert)
    {
        var segs = SplitPointer(ptr);
        if (segs.Count == 0) return value;
        var parent = GetPointer(doc, "/" + string.Join("/", segs.Take(segs.Count - 1).Select(s => s.Replace("~", "~0").Replace("/", "~1"))));
        string last = segs[^1];
        if (parent is JObject po) po[last] = value;
        else if (parent is JArray pa)
        {
            if (last == "-") pa.Add(value);
            else if (int.TryParse(last, out int i))
            {
                if (insert) pa.Insert(Math.Min(Math.Max(i, 0), pa.Count), value);
                else if (i >= 0 && i < pa.Count) pa[i] = value;
            }
        }
        else throw new InvalidOperationException($"path {ptr} not found");
        return doc;
    }

    static void RemovePointer(JToken doc, string ptr)
    {
        var t = GetPointer(doc, ptr);
        if (t == null) return;
        if (t.Parent is JProperty jp) jp.Remove(); else t.Remove();
    }

    // ---------------------------------------------------------------- listing helpers
    /// <summary>All JSON entries of a registry folder (e.g. "data/levels") across V-Slice mods: id -&gt; resolved (patched) path.</summary>
    public static Dictionary<string, string> ListEntries(string dataDir, Func<string, string> baseResolver = null)
    {
        EnsureLoaded();
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in _mods ?? _none)
        {
            string dir = Path.Combine(mod.Root, dataDir);
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dir)) continue;
            foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(f);
                d[id] = Resolve($"{dataDir}/{id}.json", baseResolver) ?? f;
            }
        }
        return d;
    }

    /// <summary>Script files (*.hxc) of all loaded V-Slice mods, in load order.</summary>
    public static List<(Mod mod, string path)> ScriptFiles()
    {
        EnsureLoaded();
        var l = new List<(Mod, string)>();
        foreach (var mod in _mods ?? _none)
        {
            string dir = Path.Combine(mod.Root, "scripts");
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dir)) continue;
            foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.hxc", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) l.Add((mod, f));
        }
        return l;
    }
}
