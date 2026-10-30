using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Mods system — mod directory scanning and management.
/// Direct port of backend/Mods.hx.
/// Xbox compatible: uses abstract file system access.
/// </summary>
public static class PsychMods
{
    public static string CurrentModDirectory { get; set; } = "";

    /// <summary>
    /// Internal Psych Engine folders that are NOT mod folders.
    /// </summary>
    public static readonly HashSet<string> IgnoreModFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "characters", "custom_events", "custom_notetypes", "data", "songs",
        "music", "sounds", "shaders", "videos", "images", "stages", "weeks",
        "fonts", "scripts", "achievements"
    };

    // Mod list data
    public class ModEntry
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public bool Restart { get; set; }
        public string Description { get; set; }
        public int[] Color { get; set; } = { 170, 0, 170 };
        public bool IsGlobal { get; set; }
    }

    private static List<ModEntry> _modsList = new();
    private static string _modsBasePath = "";

    /// <summary>
    /// Initialize the mods system with the path to the mods folder.
    /// </summary>
    public static void Init(string modsBasePath)
    {
        _modsBasePath = modsBasePath ?? "";
        ParseList();
    }

    /// <summary>
    /// Get all enabled global mod directories.
    /// </summary>
    public static List<string> GetGlobalMods()
    {
        return _modsList
            .Where(m => m.Enabled && m.IsGlobal)
            .Select(m => m.Name)
            .ToList();
    }

    /// <summary>
    /// Get all mod directories (enabled + disabled).
    /// </summary>
    public static List<string> GetModDirectories()
    {
        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(_modsBasePath))
            return new List<string>();

        return global::FNF_MonoGame.Engine.FsCI.GetDirectories(_modsBasePath)
            .Select(Path.GetFileName)
            .Where(name => !IgnoreModFolders.Contains(name) && !name.StartsWith("."))
            .ToList();
    }

    /// <summary>
    /// Parse modsList.txt to determine enabled/disabled/global mods.
    /// Format: "modName|1" (enabled) or "modName|0" (disabled)
    /// </summary>
    public static ParsedModList ParseList()
    {
        _modsList.Clear();
        var result = new ParsedModList();

        string listPath = Path.Combine(_modsBasePath, "modsList.txt");
        var allDirs = GetModDirectories();

        // Legacy fallback: the current mod menu uses enabled_mods.txt.
        // If modsList.txt is missing, treat enabled_mods.txt as the source of truth.
        HashSet<string> enabledFromLegacy = null;
        string legacyListPath = Path.Combine(_modsBasePath, "enabled_mods.txt");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(legacyListPath))
        {
            enabledFromLegacy = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(legacyListPath)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (global::FNF_MonoGame.Engine.FsCI.FileExists(listPath))
        {
            string[] lines = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(listPath);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] parts = line.Split('|');
                if (parts.Length < 2) continue;

                string name = parts[0].Trim();
                bool enabled = parts[1].Trim() == "1";

                if (!allDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                var entry = LoadModEntry(name);
                entry.Enabled = enabled;
                _modsList.Add(entry);

                if (enabled) result.Enabled.Add(name);
                else result.Disabled.Add(name);

                allDirs.Remove(name);
            }
        }

        // Add any remaining mod directories not in modsList.txt
        // If a legacy enabled_mods.txt exists, use it; otherwise enable by default.
        foreach (string dir in allDirs)
        {
            var entry = LoadModEntry(dir);
            entry.Enabled = enabledFromLegacy?.Contains(dir) ?? true;
            _modsList.Add(entry);
            if (entry.Enabled) result.Enabled.Add(dir);
            else result.Disabled.Add(dir);
        }

        Console.WriteLine($"[PSYCH-MODS] Found {result.Enabled.Count} enabled, {result.Disabled.Count} disabled mods");
        return result;
    }

    /// <summary>
    /// Psych reads pack.json with tjson, which accepts raw newlines/tabs inside strings (common in
    /// multi-line "description" fields). System.Text.Json rejects them; escape them first.
    /// </summary>
    public static string SanitizeJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        var sb = new System.Text.StringBuilder(json.Length + 16);
        bool inStr = false, esc = false;
        foreach (char ch in json)
        {
            if (inStr)
            {
                if (esc) { esc = false; sb.Append(ch); continue; }
                if (ch == '\\') { esc = true; sb.Append(ch); continue; }
                if (ch == '"') inStr = false;
                else if (ch == '\n') { sb.Append("\\n"); continue; }
                else if (ch == '\r') continue;
                else if (ch == '\t') { sb.Append("\\t"); continue; }
                else if (ch < ' ') continue;
            }
            else if (ch == '"') inStr = true;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Load mod metadata from pack.json.
    /// </summary>
    private static ModEntry LoadModEntry(string modDir)
    {
        var entry = new ModEntry { Name = modDir, Enabled = true };

        string packPath = Path.Combine(_modsBasePath, modDir, "pack.json");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(packPath))
        {
            try
            {
                string json = SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(packPath));
                var docOptions = new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true };
                using var doc = System.Text.Json.JsonDocument.Parse(json, docOptions);
                var root = doc.RootElement;

                if (root.TryGetProperty("name", out var nameEl))
                    entry.Description = nameEl.GetString();
                if (root.TryGetProperty("description", out var descEl))
                    entry.Description = descEl.GetString();
                if (root.TryGetProperty("restart", out var restartEl))
                    entry.Restart = restartEl.GetBoolean();
                if (root.TryGetProperty("runsGlobally", out var globalEl))
                    entry.IsGlobal = globalEl.GetBoolean();
                if (root.TryGetProperty("color", out var colorEl) && colorEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var colors = new List<int>();
                    foreach (var c in colorEl.EnumerateArray())
                        colors.Add(c.GetInt32());
                    if (colors.Count >= 3)
                        entry.Color = colors.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PSYCH-MODS] Error reading pack.json for {modDir}: {ex.Message}");
            }
        }

        return entry;
    }

    /// <summary>
    /// Save the current mods list to modsList.txt.
    /// </summary>
    public static void SaveModsList()
    {
        try
        {
            string listPath = Path.Combine(_modsBasePath, "modsList.txt");
            var lines = _modsList.Select(m => $"{m.Name}|{(m.Enabled ? "1" : "0")}");
            File.WriteAllLines(listPath, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PSYCH-MODS] Save modsList failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all directories that contain a specific file.
    /// Used for finding scripts, stages, etc. across mods.
    /// </summary>
    public static List<string> DirectoriesWithFile(string fileName)
    {
        var result = new List<string>();

        // Check current mod
        if (!string.IsNullOrEmpty(CurrentModDirectory))
        {
            string p = Path.Combine(_modsBasePath, CurrentModDirectory, fileName);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) result.Add(p);
        }

        // Check global mods
        foreach (string globalMod in GetGlobalMods())
        {
            string p = Path.Combine(_modsBasePath, globalMod, fileName);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(p) && !result.Contains(p)) result.Add(p);
        }

        return result;
    }

    /// <summary>
    /// Load the top/first enabled mod as the current directory.
    /// </summary>
    public static void LoadTopMod()
    {
        var enabled = _modsList.Where(m => m.Enabled).ToList();
        CurrentModDirectory = enabled.Count > 0 ? enabled[0].Name : "";
    }

    /// <summary>
    /// Set mod enabled/disabled state.
    /// </summary>
    public static void SetModEnabled(string modName, bool enabled)
    {
        var mod = _modsList.Find(m => m.Name.Equals(modName, StringComparison.OrdinalIgnoreCase));
        if (mod != null) mod.Enabled = enabled;
    }

    public static List<ModEntry> GetAllMods() => new(_modsList);

    public static string ModsBasePath => _modsBasePath;

    /// <summary>Psych Paths.modFolders(key): mods/&lt;current mod&gt;/key, then runsGlobally mods, then mods/key. Null if none exists.</summary>
    public static string ModFolders(string key)
    {
        if (string.IsNullOrEmpty(_modsBasePath) || string.IsNullOrEmpty(key)) return null;
        try
        {
            if (!string.IsNullOrEmpty(CurrentModDirectory))
            {
                string p = Path.Combine(_modsBasePath, CurrentModDirectory, key);
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return p;
            }
            foreach (string g in GetGlobalMods())
            {
                string p = Path.Combine(_modsBasePath, g, key);
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return p;
            }
            string r = Path.Combine(_modsBasePath, key);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(r)) return r;
        }
        catch { }
        return null;
    }

    public static string GetModDirectory(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName) || string.IsNullOrWhiteSpace(_modsBasePath))
            return null;
        string path = Path.Combine(_modsBasePath, modName);
        return global::FNF_MonoGame.Engine.FsCI.DirectoryExists(path) ? path : null;
    }

    /// <summary>
    /// Find an enabled mod that contains chart/audio data for the given song.
    /// Checks data/<song> and songs/<song> in each enabled mod.
    /// </summary>
    public static string FindModPathForSong(string songName)
    {
        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(_modsBasePath))
            return null;

        foreach (var mod in _modsList.Where(m => m.Enabled))
        {
            string modPath = GetModDirectory(mod.Name);
            if (string.IsNullOrEmpty(modPath)) continue;
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(modPath, "_polymod_meta.json"))) continue;   // V-Slice mods: VSliceMods overlay, not the Psych mod path

            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(modPath, "data", songName)) ||
                global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(modPath, "songs", songName)) ||
                global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(modPath, "songs", FormatToSongPath(songName))))
                return modPath;
        }
        return null;
    }

    private static string FormatToSongPath(string path) =>
        path?.ToLowerInvariant().Replace(' ', '-') ?? "";

    /// <summary>
    /// pack.json data exposed for Discord RPC mod overrides.
    /// </summary>
    public class Pack
    {
        public string name { get; set; }
        public string description { get; set; }
        public bool restart { get; set; }
        public bool runsGlobally { get; set; }
        public int[] color { get; set; }
        public string discordRPC { get; set; }
    }

    /// <summary>
    /// Get the current mod's pack.json data, if any.
    /// </summary>
    public static Pack GetPack()
    {
        if (string.IsNullOrWhiteSpace(CurrentModDirectory) || string.IsNullOrWhiteSpace(_modsBasePath))
            return null;

        string packPath = Path.Combine(_modsBasePath, CurrentModDirectory, "pack.json");
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(packPath)) return null;

        try
        {
            string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(packPath);
            return System.Text.Json.JsonSerializer.Deserialize<Pack>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PSYCH-MODS] GetPack failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Merge text files named <paramref name="fileName"/> from base game and all enabled mods.
    /// Used by the localization system.
    /// </summary>
    public static List<string> MergeAllTextsNamed(string fileName)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(fileName)) return result;

        // Base game path
        string basePath = Path.Combine(_modsBasePath, fileName);
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(basePath))
            result.AddRange(global::FNF_MonoGame.Engine.FsCI.ReadAllLines(basePath));

        // Enabled mods in order
        foreach (var mod in _modsList.Where(m => m.Enabled))
        {
            string modPath = Path.Combine(_modsBasePath, mod.Name, fileName);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(modPath))
                result.AddRange(global::FNF_MonoGame.Engine.FsCI.ReadAllLines(modPath));
        }

        return result;
    }
}

public class ParsedModList
{
    public List<string> Enabled { get; } = new();
    public List<string> Disabled { get; } = new();
}
