using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>One playable character (V-Slice 0.8.x data/players/&lt;id&gt;.json, PlayerData.hx).</summary>
public sealed class PlayerEntry
{
    public string Id;
    public JObject Data;
    public bool IsMod;           // defined by a V-Slice mod (not in Content/data/players)
    public string Name => (string)Data?["name"] ?? Id;
    public List<string> OwnedChars = new();
    public bool ShowUnownedChars => (bool?)Data?["showUnownedChars"] ?? false;
    public bool Unlocked => (bool?)Data?["unlocked"] ?? true;
    public string FreeplayStyle => (string)Data?["freeplayStyle"] ?? "bf";
    public JObject FreeplayDJ => Data?["freeplayDJ"] as JObject;
    public JObject CharSelect => Data?["charSelect"] as JObject;
    /// <summary>charSelect.position (null = no charSelect data -> not shown in Character Select).</summary>
    public int? CharSelectPosition => CharSelect == null ? null : ((int?)CharSelect["position"] ?? 0);
    public string GfAssetPath => (string)CharSelect?["gf"]?["assetPath"];
    public string DjText(int i) => (string)FreeplayDJ?["text" + i];
    public bool Owns(string charId) => charId != null && OwnedChars.Any(c => string.Equals(c, charId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// PlayerRegistry (funkin.data.freeplay.player.PlayerRegistry, 0.8.4): base players from Content/data/players plus
/// players added by V-Slice mods (mod data/players/*.json, _merge/_append applied by VSliceMods.Resolve; JSON parsed
/// leniently like 0.8.4's json2object/thx parser: comments + trailing commas). Invalid entries are skipped with a log line.
/// </summary>
public static class PlayerRegistry
{
    static List<PlayerEntry> _entries;
    static readonly object _lock = new();

    public static void Invalidate() { lock (_lock) _entries = null; }

    public static IReadOnlyList<PlayerEntry> All { get { EnsureLoaded(); return _entries; } }

    public static PlayerEntry Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        EnsureLoaded();
        return _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsBasePlayer(string id) => Get(id) is PlayerEntry e && !e.IsMod;

    /// <summary>PlayerRegistry.getCharacterOwnerId: the player owning a character id (base players first).</summary>
    public static string GetCharacterOwnerId(string charId)
    {
        EnsureLoaded();
        foreach (var e in _entries) if (e.Owns(charId)) return e.Id;
        return null;
    }

    /// <summary>
    /// The engine's song-variation family for a player ("bf" = default charts, "pico" = *_pico charts...). Base players
    /// are their own family; a mod player maps to the base player owning its first owned character (KB owns 'bf'
    /// -> default variations), else its own id (charts named &lt;song&gt;_&lt;id&gt;).
    /// </summary>
    public static string VariationFamily(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId)) return "bf";
        string id = playerId.Trim().ToLowerInvariant();
        var e = Get(id);
        if (e == null) return id.Contains("pico") ? "pico" : "bf";
        if (!e.IsMod) return e.Id.ToLowerInvariant();
        foreach (var c in e.OwnedChars)
            foreach (var b in _entries)
                if (!b.IsMod && b.Owns(c)) return b.Id.ToLowerInvariant();
        foreach (var c in e.OwnedChars) if (c.IndexOf("pico", StringComparison.OrdinalIgnoreCase) >= 0) return "pico";
        return e.Id.ToLowerInvariant();
    }

    static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_entries != null) return;
            var list = new List<PlayerEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Base players (Content/data/players), with mod _merge/_append patches applied.
            try
            {
                string dir = Path.Combine(AssetManager.ContentRoot, "data", "players");
                if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dir))
                    foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal))
                    {
                        string id = Path.GetFileNameWithoutExtension(f);
                        string path = f;
                        try { if (VSliceMods.Any) path = VSliceMods.Resolve($"data/players/{id}.json", _ => f) ?? f; } catch { }
                        var e = Parse(id, path, false);
                        if (e != null && seen.Add(id)) list.Add(e);
                    }
            }
            catch (Exception ex) { Console.WriteLine($"[PLAYERS] base scan failed: {ex.Message}"); }
            // Mod players
            try
            {
                if (VSliceMods.Any)
                    foreach (var kv in VSliceMods.ListEntries("data/players").OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        if (seen.Contains(kv.Key))
                        {
                            // a mod replacing a base player file: re-read it (Resolve already returned the mod copy)
                            int i = list.FindIndex(x => string.Equals(x.Id, kv.Key, StringComparison.OrdinalIgnoreCase));
                            var r = Parse(kv.Key, kv.Value, false);
                            if (i >= 0 && r != null) list[i] = r;
                            continue;
                        }
                        var e = Parse(kv.Key, kv.Value, true);
                        if (e != null && seen.Add(kv.Key)) list.Add(e);
                    }
            }
            catch (Exception ex) { Console.WriteLine($"[PLAYERS] mod scan failed: {ex.Message}"); }
            _entries = list;
            Console.WriteLine("[PLAYERS] " + string.Join(", ", list.Select(e => $"{e.Id}{(e.IsMod ? "(mod)" : "")} pos={e.CharSelectPosition?.ToString() ?? "-"} owned=[{string.Join(",", e.OwnedChars)}]")));
        }
    }

    static PlayerEntry Parse(string id, string path, bool isMod)
    {
        try
        {
            var tok = VSliceMods.ParseLenient(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path));
            if (tok is not JObject jo) { Console.WriteLine($"[PLAYERS] {id}: not a JSON object ({path})"); return null; }
            var e = new PlayerEntry { Id = id, Data = jo, IsMod = isMod };
            if (jo["ownedChars"] is JArray owned) foreach (var o in owned) { string s = (string)o; if (!string.IsNullOrEmpty(s)) e.OwnedChars.Add(s); }
            return e;
        }
        catch (Exception ex) { Console.WriteLine($"[PLAYERS] Failed to load entry data: {id} ({ex.Message})"); return null; }
    }
}
