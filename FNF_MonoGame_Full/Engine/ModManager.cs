using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Manages mods for the MonoGame FNF engine.
/// Loads Psych Engine-style mods from the mods/ folder.
/// Each mod can contain: characters, stages, songs, scripts, images, sounds.
/// </summary>
public class ModManager
{
    private static ModManager _instance;
    public static ModManager Instance => _instance ??= new ModManager();

    /// <summary>
    /// Root folder where mods are stored.
    /// </summary>
    public string ModsRoot { get; private set; }

    /// <summary>
    /// List of all discovered mods.
    /// </summary>
    public List<ModInfo> AllMods { get; } = new();

    /// <summary>
    /// List of currently enabled mods (in load order).
    /// </summary>
    public List<ModInfo> EnabledMods { get; } = new();

    /// <summary>
    /// Currently active mod (for asset resolution).
    /// </summary>
    public ModInfo ActiveMod { get; set; }

    /// <summary>
    /// Initialize the mod system and scan for mods.
    /// </summary>
    public void Initialize(string gameRootPath)
    {
        ModsRoot = Path.Combine(gameRootPath, "mods");

        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(ModsRoot))
        {
            Directory.CreateDirectory(ModsRoot);
            Console.WriteLine($"[MODS] Created mods folder: {ModsRoot}");
        }

        ScanMods();
        LoadEnabledList();
        Console.WriteLine($"[MODS] Found {AllMods.Count} mods, {EnabledMods.Count} enabled");
    }

    /// <summary>
    /// Scan the mods/ folder for valid mods.
    /// A valid mod has a pack.json or _polymod.meta in its root.
    /// </summary>
    public void ScanMods()
    {
        AllMods.Clear();

        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(ModsRoot)) return;

        var dirs = global::FNF_MonoGame.Engine.FsCI.GetDirectories(ModsRoot);
        Console.WriteLine($"[MODS] scanning {ModsRoot}: {dirs.Length} folder(s)");
        foreach (var dir in dirs)
        {
            ModInfo mod = null;
            try { mod = TryLoadMod(dir); } catch (Exception ex) { Console.WriteLine($"[MODS] {Path.GetFileName(dir)}: {ex.GetType().Name}: {ex.Message}"); }
            if (mod == null) Console.WriteLine($"[MODS] not a mod: {Path.GetFileName(dir)}");
            if (mod != null)
            {
                foreach (var ic in new[] { "pack.png", "_polymod_icon.png", "icon.png" })
                    if (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(dir, ic))) { mod.IconPath = Path.Combine(dir, ic); break; }
                AllMods.Add(mod);
                Console.WriteLine($"[MODS] Discovered: {mod.Name} v{mod.Version} by {mod.Author}");
            }
        }

        // Sort alphabetically by default, then apply the saved load order (mods_order.txt, top = highest priority)
        AllMods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        try
        {
            string orderPath = Path.Combine(ModsRoot, "mods_order.txt");
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(orderPath))
            {
                var order = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(orderPath).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
                int Rank(ModInfo m) { int i = order.FindIndex(o => string.Equals(o, m.Id, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }
                var sorted = AllMods.OrderBy(Rank).ToList();   // stable: unknown mods keep alphabetical order at the end
                AllMods.Clear(); AllMods.AddRange(sorted);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[MODS] mods_order.txt: {ex.Message}"); }
    }

    /// <summary>EnabledMods = enabled mods in reverse display order (ResolveAssetPath: last = highest priority = top of the list).</summary>
    private void RebuildEnabled()
    {
        EnabledMods.Clear();
        for (int i = AllMods.Count - 1; i >= 0; i--) if (AllMods[i].Enabled) EnabledMods.Add(AllMods[i]);
    }

    /// <summary>Move a mod up (delta -1) or down (+1) in the load order. Returns true when moved.</summary>
    public bool MoveMod(ModInfo mod, int delta)
    {
        int i = AllMods.IndexOf(mod), j = i + delta;
        if (i < 0 || j < 0 || j >= AllMods.Count) return false;
        AllMods[i] = AllMods[j]; AllMods[j] = mod;
        RebuildEnabled(); SaveEnabledList();
        return true;
    }

    /// <summary>Enable or disable every mod.</summary>
    public void SetAllEnabled(bool enabled)
    {
        foreach (var m in AllMods) m.Enabled = enabled;
        RebuildEnabled(); SaveEnabledList();
    }

    /// <summary>Delete a mod: removes its folder from the mods directory (read-only flags cleared first), drops it from the
    /// lists and rewrites enabled_mods.txt / mods_order.txt / modsList.txt. Returns null on success, else the error.
    /// Blocking (big mods take a moment): call it from a background task.</summary>
    public string DeleteMod(ModInfo mod)
    {
        if (mod == null || string.IsNullOrEmpty(mod.ModPath)) return "no mod folder";
        string dir = mod.ModPath.TrimEnd('/', '\\');
        try
        {
            // safety: only folders directly inside the mods root
            string root = Path.GetFullPath(ModsRoot).TrimEnd('/', '\\');
            string full = Path.GetFullPath(dir);
            if (!string.Equals(Path.GetDirectoryName(full)?.TrimEnd('/', '\\'), root, StringComparison.OrdinalIgnoreCase))
                return "not inside the mods folder";
            if (Directory.Exists(full))
            {
                foreach (var f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    try { var a = File.GetAttributes(f); if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly); } catch { }
                Directory.Delete(full, true);
            }
            Console.WriteLine($"[MODS] Deleted: {mod.Name} ({full})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MODS] delete {mod.Name} failed: {ex.Message}");
            if (Directory.Exists(dir)) return ex.Message;   // partly deleted: still drop it from the list below if the folder is gone
        }
        lock (AllMods)
        {
            AllMods.Remove(mod);
            RebuildEnabled();
        }
        SaveEnabledList();
        try
        {   // Psych modsList.txt: SaveEnabledList skips writing it when no Psych mod is left
            string ml = Path.Combine(ModsRoot, "modsList.txt");
            string folder = Path.GetFileName(dir);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(ml))
            {
                var lines = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(ml).Where(l => !l.Split('|')[0].Trim().Equals(folder, StringComparison.OrdinalIgnoreCase)).ToList();
                File.WriteAllLines(ml, lines);
            }
            FNF_MonoGame.Engine.PsychEngine.PsychMods.ParseList();
        }
        catch (Exception ex) { Console.WriteLine($"[MODS] modsList.txt after delete: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Try to load mod metadata from a directory.
    /// Supports both Psych Engine pack.json and FNF _polymod.meta formats.
    /// </summary>
    private ModInfo TryLoadMod(string modDir)
    {
        string dirName = Path.GetFileName(modDir);

        // Try Psych Engine format: pack.json
        string packJsonPath = Path.Combine(modDir, "pack.json");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(packJsonPath))
        {
            try
            {
                var json = FNF_MonoGame.Engine.PsychEngine.PsychMods.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(packJsonPath));
                var packData = JsonSerializer.Deserialize<PsychPackJson>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                });

                int[] color = null;
                try { using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
                      if (doc.RootElement.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() >= 3)
                          color = new[] { c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32() }; } catch { }
                return new ModInfo
                {
                    Color = color,
                    Id = dirName,
                    Name = packData?.Name ?? dirName,
                    Description = packData?.Description ?? "",
                    Author = packData?.Author ?? "Unknown",
                    Version = packData?.Version ?? "1.0.0",
                    ModPath = modDir,
                    Format = ModFormat.PsychEngine,
                    Enabled = false
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MODS] Error reading pack.json in {dirName}: {ex.Message}");
            }
        }

        // V-Slice polymod: _polymod_meta.json {title, description, contributors[{name,role}], api_version, mod_version}
        string polyJson = Path.Combine(modDir, "_polymod_meta.json");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(polyJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(polyJson), new JsonDocumentOptions { AllowTrailingCommas = true });
                var r = doc.RootElement;
                string Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                string author = null;
                if (r.TryGetProperty("contributors", out var cs) && cs.ValueKind == JsonValueKind.Array)
                    author = string.Join(", ", cs.EnumerateArray().Select(c => c.TryGetProperty("name", out var n) ? n.GetString() : null).Where(n => !string.IsNullOrEmpty(n)));
                if (string.IsNullOrEmpty(author)) author = Str("author");
                return new ModInfo
                {
                    Id = dirName,
                    Name = Str("title") ?? dirName,
                    Description = Str("description") ?? "",
                    Author = string.IsNullOrEmpty(author) ? "Unknown" : author,
                    Version = Str("mod_version") ?? "1.0.0",
                    ApiVersion = Str("api_version"),
                    ModPath = modDir,
                    Format = ModFormat.FunkinPolymod,
                    Enabled = false
                };
            }
            catch (Exception ex) { Console.WriteLine($"[MODS] Error reading _polymod_meta.json in {dirName}: {ex.Message}"); }
        }

        // Try FNF _polymod.meta format
        string polymodPath = Path.Combine(modDir, "_polymod.meta");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(polymodPath))
        {
            try
            {
                var lines = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(polymodPath);
                var meta = new Dictionary<string, string>();
                foreach (var line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                        meta[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }

                return new ModInfo
                {
                    Id = dirName,
                    Name = meta.GetValueOrDefault("name", dirName),
                    Description = meta.GetValueOrDefault("description", ""),
                    Author = meta.GetValueOrDefault("author", "Unknown"),
                    Version = meta.GetValueOrDefault("version", "1.0.0"),
                    ModPath = modDir,
                    Format = ModFormat.FunkinPolymod,
                    Enabled = false
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MODS] Error reading _polymod.meta in {dirName}: {ex.Message}");
            }
        }

        // If folder has standard mod structure (characters/, stages/, songs/) treat as mod
        bool hasContent = global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(modDir, "characters")) ||
                          global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(modDir, "stages")) ||
                          global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(modDir, "songs")) ||
                          global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(modDir, "scripts")) ||
                          global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(modDir, "images"));

        if (hasContent)
        {
            return new ModInfo
            {
                Id = dirName,
                Name = dirName,
                Description = "No metadata found",
                Author = "Unknown",
                Version = "1.0.0",
                ModPath = modDir,
                Format = ModFormat.Generic,
                Enabled = false
            };
        }

        return null;
    }

    /// <summary>
    /// Enable a mod.
    /// </summary>
    public void EnableMod(ModInfo mod)
    {
        if (!mod.Enabled)
        {
            mod.Enabled = true;
            RebuildEnabled();
            SaveEnabledList();
            Console.WriteLine($"[MODS] Enabled: {mod.Name}");
        }
    }

    /// <summary>
    /// Disable a mod.
    /// </summary>
    public void DisableMod(ModInfo mod)
    {
        if (mod.Enabled)
        {
            mod.Enabled = false;
            RebuildEnabled();
            SaveEnabledList();
            Console.WriteLine($"[MODS] Disabled: {mod.Name}");
        }
    }

    /// <summary>
    /// Toggle a mod on/off.
    /// </summary>
    public void ToggleMod(ModInfo mod)
    {
        if (mod.Enabled)
            DisableMod(mod);
        else
            EnableMod(mod);
    }

    /// <summary>
    /// Resolve an asset path, checking enabled mods first (in reverse order for priority).
    /// Falls back to base game path if no mod overrides it.
    /// </summary>
    public string ResolveAssetPath(string relativePath)
    {
        // Check enabled mods in reverse order (last enabled = highest priority)
        for (int i = EnabledMods.Count - 1; i >= 0; i--)
        {
            string modPath = Path.Combine(EnabledMods[i].ModPath, relativePath);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(modPath))
                return modPath;
        }

        // Fall back to base game
        return null;
    }

    /// <summary>
    /// Get all song folders from enabled mods.
    /// </summary>
    public List<(string SongName, ModInfo Mod)> GetModSongs()
    {
        var songs = new List<(string, ModInfo)>();
        foreach (var mod in EnabledMods)
        {
            string songsDir = Path.Combine(mod.ModPath, "songs");
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(songsDir))
            {
                foreach (var songDir in global::FNF_MonoGame.Engine.FsCI.GetDirectories(songsDir))
                {
                    songs.Add((Path.GetFileName(songDir), mod));
                }
            }
        }
        return songs;
    }

    /// <summary>
    /// Get all character JSONs from enabled mods.
    /// </summary>
    public List<(string CharName, string JsonPath, ModInfo Mod)> GetModCharacters()
    {
        var characters = new List<(string, string, ModInfo)>();
        foreach (var mod in EnabledMods)
        {
            string charsDir = Path.Combine(mod.ModPath, "characters");
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(charsDir))
            {
                foreach (var jsonFile in global::FNF_MonoGame.Engine.FsCI.GetFiles(charsDir, "*.json"))
                {
                    characters.Add((Path.GetFileNameWithoutExtension(jsonFile), jsonFile, mod));
                }
            }
        }
        return characters;
    }

    /// <summary>
    /// Get all stage scripts from enabled mods.
    /// </summary>
    public List<(string StageName, string ScriptPath, ModInfo Mod)> GetModStages()
    {
        var stages = new List<(string, string, ModInfo)>();
        foreach (var mod in EnabledMods)
        {
            string stagesDir = Path.Combine(mod.ModPath, "stages");
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(stagesDir))
            {
                foreach (var file in global::FNF_MonoGame.Engine.FsCI.GetFiles(stagesDir, "*.hx"))
                {
                    stages.Add((Path.GetFileNameWithoutExtension(file), file, mod));
                }
                foreach (var file in global::FNF_MonoGame.Engine.FsCI.GetFiles(stagesDir, "*.hxc"))
                {
                    stages.Add((Path.GetFileNameWithoutExtension(file), file, mod));
                }
                foreach (var file in global::FNF_MonoGame.Engine.FsCI.GetFiles(stagesDir, "*.json"))
                {
                    stages.Add((Path.GetFileNameWithoutExtension(file), file, mod));
                }
            }
        }
        return stages;
    }

    /// <summary>
    /// Get all script files (.hx, .hxc) from enabled mods.
    /// </summary>
    public List<(string ScriptName, string ScriptPath, ModInfo Mod)> GetModScripts()
    {
        var scripts = new List<(string, string, ModInfo)>();
        foreach (var mod in EnabledMods)
        {
            string scriptsDir = Path.Combine(mod.ModPath, "scripts");
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(scriptsDir))
            {
                foreach (var file in global::FNF_MonoGame.Engine.FsCI.EnumerateFiles(scriptsDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".hx") || f.EndsWith(".hxc")))
                {
                    scripts.Add((Path.GetFileNameWithoutExtension(file), file, mod));
                }
            }
        }
        return scripts;
    }

    /// <summary>
    /// Save the list of enabled mods to a file.
    /// </summary>
    private void SaveEnabledList()
    {
        string listPath = Path.Combine(ModsRoot, "enabled_mods.txt");
        var enabledIds = EnabledMods.Select(m => m.Id).ToList();
        try
        {
            File.WriteAllLines(listPath, enabledIds);
            File.WriteAllLines(Path.Combine(ModsRoot, "mods_order.txt"), AllMods.Select(m => m.Id));
        }
        catch (Exception ex) { Console.WriteLine($"[MODS] save list: {ex.Message}"); }
        // Psych Engine reads modsList.txt ("folder|1"/"folder|0"); keep it in sync with the Mods menu so
        // toggling a mod here also enables/disables its scripts, weeks and characters in songs.
        try
        {
            var enabledSet = new HashSet<string>(enabledIds, StringComparer.OrdinalIgnoreCase);
            var lines = AllMods.Where(m => m.Format == ModFormat.PsychEngine && !string.IsNullOrEmpty(m.ModPath))
                .Select(m => $"{Path.GetFileName(m.ModPath.TrimEnd('/', '\\'))}|{(enabledSet.Contains(m.Id) ? "1" : "0")}")
                .ToList();
            if (lines.Count > 0)
            {
                File.WriteAllLines(Path.Combine(ModsRoot, "modsList.txt"), lines);
                FNF_MonoGame.Engine.PsychEngine.PsychMods.ParseList();
            }
        }
        catch (Exception ex) { Console.WriteLine($"[MODS] modsList.txt sync failed: {ex.Message}"); }
    }

    /// <summary>
    /// Load the list of enabled mods from file.
    /// </summary>
    private void LoadEnabledList()
    {
        string listPath = Path.Combine(ModsRoot, "enabled_mods.txt");
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(listPath)) return;

        var enabledIds = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(listPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in AllMods)
            mod.Enabled = enabledIds.Contains(mod.Id);
        RebuildEnabled();
    }
}

/// <summary>
/// Information about a single mod.
/// </summary>
public class ModInfo
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string Version { get; set; }
    public string ModPath { get; set; }
    public ModFormat Format { get; set; }
    public bool Enabled { get; set; }
    /// <summary>pack.png (Psych) / _polymod_icon.png (V-Slice) / icon.png, or null.</summary>
    public string IconPath { get; set; }
    /// <summary>Psych pack.json "color" [r,g,b] (menu background tint), or null.</summary>
    public int[] Color { get; set; }
    public string ApiVersion { get; set; }
    private int[] _counts;
    /// <summary>Number of songs, characters, stages, scripts shipped by the mod (cached).</summary>
    public int[] ContentCounts => _counts ??= new[] { CountDirs("songs") + CountDirs("data/songs", true), CountFiles("characters", "*.json") + CountFiles("data/characters", "*.json"),
        CountFiles("stages", "*.*") + CountFiles("data/stages", "*.json"), CountFiles("scripts", "*.*", true) };
    private int CountDirs(string rel, bool _ = false) { try { var d = System.IO.Path.Combine(ModPath, rel); return global::FNF_MonoGame.Engine.FsCI.DirectoryExists(d) ? global::FNF_MonoGame.Engine.FsCI.GetDirectories(d).Length : 0; } catch { return 0; } }
    private int CountFiles(string rel, string pat, bool deep = false) { try { var d = System.IO.Path.Combine(ModPath, rel); return global::FNF_MonoGame.Engine.FsCI.DirectoryExists(d) ? global::FNF_MonoGame.Engine.FsCI.GetFiles(d, pat, deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Length : 0; } catch { return 0; } }
}

/// <summary>
/// The format/source of the mod.
/// </summary>
public enum ModFormat
{
    PsychEngine,
    FunkinPolymod,
    Generic
}

/// <summary>
/// Psych Engine pack.json structure.
/// </summary>
public class PsychPackJson
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("api_version")]
    public string ApiVersion { get; set; }

    [JsonPropertyName("mod_version")]
    public string ModVersion { get; set; }
}
